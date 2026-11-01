using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Folderite.History;

public readonly record struct HistoryEntry(string Path, double Score, DateTimeOffset LastAccess);

/// <summary>zoxide-style frecency store of visited folders, persisted as JSON.</summary>
public sealed class FrecencyStore
{
    private const int MaxEntries = 1000;
    private const double MaxTotalRank = 9000;
    private const int MaxAppsPerEntry = 10;

    /// <summary>
    /// How much a folder's use in the asked-about app counts relative to its use overall. At 3, a few uses in the app
    /// outweigh a modest lead elsewhere, but a far more popular folder still wins.
    /// </summary>
    private const double AppWeight = 3;

    private static readonly TimeSpan DebounceWindow = TimeSpan.FromSeconds(2);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _filePath;
    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private List<Regex> _excludes = [];
    private bool _dirty;

    public FrecencyStore(string filePath, TimeProvider? time = null)
    {
        _filePath = filePath;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>True after changes since the last Load/Save.</summary>
    public bool IsDirty
    {
        get { lock (_lock) return _dirty; }
    }

    /// <summary>Loads entries from disk (file versions 1 and 2). A missing or corrupt file yields an empty store; never throws.</summary>
    public void Load()
    {
        var loaded = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(_filePath))
            {
                using var stream = File.OpenRead(_filePath);
                var file = JsonSerializer.Deserialize<FileModel>(stream, JsonOptions);
                foreach (var e in file?.Entries ?? [])
                {
                    var path = Normalize(e?.Path);
                    if (path is null || e is null || !double.IsFinite(e.Rank) || e.Rank <= 0)
                        continue;
                    loaded[path] = new Entry { Path = path, Rank = e.Rank, LastAccess = e.LastAccess, Apps = LoadApps(e.Apps) };
                }
            }
        }
        catch
        {
            loaded.Clear();
        }

        lock (_lock)
        {
            _entries.Clear();
            foreach (var (k, v) in loaded)
                _entries[k] = v;
            _dirty = false;
        }
    }

    /// <summary>Atomically writes the store (temp file + move). IO failures leave <see cref="IsDirty"/> set.</summary>
    public void Save()
    {
        string json;
        lock (_lock)
        {
            var model = new FileModel
            {
                Version = 2,
                Entries = _entries.Values
                    .Select(e => (EntryModel?)new EntryModel
                    {
                        Path = e.Path,
                        Rank = e.Rank,
                        LastAccess = e.LastAccess,
                        // Null rather than an empty object, so entries without app data look exactly like version 1.
                        Apps = e.Apps is { Count: > 0 }
                            ? e.Apps.ToDictionary(kv => kv.Key, kv => (AppUseModel?)new AppUseModel { Rank = kv.Value.Rank, LastAccess = kv.Value.LastAccess })
                            : null,
                    })
                    .ToList(),
            };
            json = JsonSerializer.Serialize(model, JsonOptions);
            _dirty = false;
        }

        string? temp = null;
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(_filePath));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            temp = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _filePath, overwrite: true);
            temp = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            lock (_lock) _dirty = true;
        }
        finally
        {
            if (temp is not null)
            {
                try { File.Delete(temp); } catch { /* best effort */ }
            }
        }
    }

    /// <summary>
    /// Records a visit to a folder. Repeats within 2 seconds are ignored. When <paramref name="app"/> names the
    /// process the folder was used in (e.g. "notepad"), that app's use of the folder is counted too. The app use has
    /// its own 2-second debounce, so a repeat in one signal never hides a fresh one in the other.
    /// </summary>
    public void Record(string path, string? app = null)
    {
        var key = Normalize(path);
        if (key is null)
            return;
        var appKey = NormalizeApp(app);

        lock (_lock)
        {
            if (IsExcluded(key))
                return;

            var now = _time.GetUtcNow();
            _entries.TryGetValue(key, out var entry);

            AppUse? existingUse = null;
            if (appKey is not null)
                entry?.Apps?.TryGetValue(appKey, out existingUse);

            var bumpGlobal = entry is null || !IsRepeat(entry.LastAccess, now);
            var bumpApp = appKey is not null && (existingUse is null || !IsRepeat(existingUse.LastAccess, now));
            if (!bumpGlobal && !bumpApp)
                return;

            if (entry is null)
            {
                entry = new Entry { Path = key };
                _entries[key] = entry;
            }

            if (bumpGlobal)
            {
                entry.Path = key;
                entry.Rank += 1;
                entry.LastAccess = now;
            }

            if (bumpApp)
            {
                if (existingUse is not null)
                {
                    existingUse.Rank += 1;
                    existingUse.LastAccess = now;
                }
                else
                {
                    entry.Apps ??= new Dictionary<string, AppUse>(StringComparer.OrdinalIgnoreCase);
                    entry.Apps[appKey!] = new AppUse { Rank = 1, LastAccess = now };
                    TrimApps(entry, now, keepApp: appKey!);
                }
            }

            Age(now, keep: key, keepApp: bumpApp ? appKey : null);
            _dirty = true;
        }
    }

    /// <summary>Removes a folder from the store.</summary>
    public void Remove(string path)
    {
        var key = Normalize(path);
        if (key is null)
            return;
        lock (_lock)
        {
            if (_entries.Remove(key))
                _dirty = true;
        }
    }

    /// <summary>Sets wildcard (* and ?) patterns of paths to exclude from recording and results.</summary>
    public void SetExcludes(IEnumerable<string> patterns)
    {
        var list = new List<Regex>();
        foreach (var p in patterns ?? [])
        {
            if (string.IsNullOrWhiteSpace(p))
                continue;
            var regex = "^" + Regex.Escape(p.Trim()).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            list.Add(new Regex(regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline));
        }

        lock (_lock) _excludes = list;
    }

    /// <summary>
    /// Returns up to <paramref name="count"/> folders ordered by frecency. Does not touch the disk.
    /// With <paramref name="app"/>, a folder's use in that app adds to its score, so the app's own folders float up.
    /// </summary>
    public IReadOnlyList<string> Top(int count, string? app = null)
    {
        if (count <= 0)
            return [];
        var appKey = NormalizeApp(app);

        lock (_lock)
        {
            var now = _time.GetUtcNow();
            return _entries.Values
                .Where(e => !IsExcluded(e.Path))
                .OrderByDescending(e => Score(e, now) + (appKey is null ? 0 : AppWeight * AppScore(e, appKey, now)))
                .ThenByDescending(e => e.LastAccess)
                .Take(count)
                .Select(e => e.Path)
                .ToList();
        }
    }

    /// <summary>The non-excluded folder most recently used in <paramref name="app"/>, or null if there is none.</summary>
    public string? LastInApp(string app)
    {
        var appKey = NormalizeApp(app);
        if (appKey is null)
            return null;

        lock (_lock)
        {
            string? best = null;
            var bestAccess = DateTimeOffset.MinValue;
            foreach (var e in _entries.Values)
            {
                if (e.Apps is null || !e.Apps.TryGetValue(appKey, out var use) || IsExcluded(e.Path))
                    continue;
                if (best is null || use.LastAccess > bestAccess)
                {
                    best = e.Path;
                    bestAccess = use.LastAccess;
                }
            }
            return best;
        }
    }

    /// <summary>All non-excluded folders ordered like <see cref="Top"/>, with the score it sorts by.</summary>
    public IReadOnlyList<HistoryEntry> Entries()
    {
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            return _entries.Values
                .Where(e => !IsExcluded(e.Path))
                .Select(e => new HistoryEntry(e.Path, Score(e, now), e.LastAccess))
                .OrderByDescending(e => e.Score)
                .ThenByDescending(e => e.LastAccess)
                .ToList();
        }
    }

    /// <summary>Removes every folder from the store.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _dirty = true;
        }
    }

    private bool IsExcluded(string path)
    {
        foreach (var rx in _excludes)
        {
            if (rx.IsMatch(path))
                return true;
        }
        return false;
    }

    private static bool IsKeep(string key, string keep) => string.Equals(key, keep, StringComparison.OrdinalIgnoreCase);

    private static bool IsRepeat(DateTimeOffset last, DateTimeOffset now) => now - last < DebounceWindow && now >= last;

    private static double Score(Entry e, DateTimeOffset now) => Score(e.Rank, e.LastAccess, now);

    private static double AppScore(Entry e, string app, DateTimeOffset now) =>
        e.Apps is not null && e.Apps.TryGetValue(app, out var use) ? Score(use.Rank, use.LastAccess, now) : 0;

    private static double Score(double rank, DateTimeOffset lastAccess, DateTimeOffset now)
    {
        var age = now - lastAccess;
        if (age < TimeSpan.FromHours(1)) return rank * 4;
        if (age < TimeSpan.FromDays(1)) return rank * 2;
        if (age < TimeSpan.FromDays(7)) return rank * 0.5;
        return rank * 0.25;
    }

    /// <summary>Apps are keyed by lower-case process name without ".exe", so "Notepad.EXE" and "notepad" are one app.</summary>
    private static string? NormalizeApp(string? app)
    {
        if (string.IsNullOrWhiteSpace(app))
            return null;
        var name = app.Trim();
        if (name.Length > 4 && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name.ToLowerInvariant();
    }

    private static Dictionary<string, AppUse>? LoadApps(Dictionary<string, AppUseModel?>? apps)
    {
        if (apps is null)
            return null;

        var result = new Dictionary<string, AppUse>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, use) in apps)
        {
            var key = NormalizeApp(name);
            if (key is null || use is null || !double.IsFinite(use.Rank) || use.Rank <= 0)
                continue;
            result[key] = new AppUse { Rank = use.Rank, LastAccess = use.LastAccess };
        }
        return result.Count > 0 ? result : null;
    }

    /// <summary>Caps an entry's per-app data. The app just recorded is never the one dropped, or its visit would be lost.</summary>
    private static void TrimApps(Entry entry, DateTimeOffset now, string keepApp)
    {
        if (entry.Apps is null)
            return;
        while (entry.Apps.Count > MaxAppsPerEntry)
        {
            var drop = entry.Apps
                .Where(kv => !string.Equals(kv.Key, keepApp, StringComparison.OrdinalIgnoreCase))
                .OrderBy(kv => Score(kv.Value.Rank, kv.Value.LastAccess, now))
                .ThenBy(kv => kv.Value.LastAccess)
                .First().Key;
            entry.Apps.Remove(drop);
        }
    }

    /// <param name="keep">The entry just recorded; never dropped by aging, or the visit that triggered it would be lost.</param>
    /// <param name="keepApp">The app use just recorded on <paramref name="keep"/>, if any; spared for the same reason.</param>
    private void Age(DateTimeOffset now, string keep, string? keepApp)
    {
        if (_entries.Values.Sum(e => e.Rank) > MaxTotalRank)
        {
            foreach (var e in _entries.Values)
            {
                e.Rank *= 0.9;
                if (e.Apps is null)
                    continue;
                foreach (var use in e.Apps.Values)
                    use.Rank *= 0.9;
            }
            foreach (var key in _entries.Where(kv => kv.Value.Rank < 1 && !IsKeep(kv.Key, keep)).Select(kv => kv.Key).ToList())
                _entries.Remove(key);

            // Entries dropped above took their app data with them; this prunes the survivors' faded app uses.
            foreach (var (key, e) in _entries)
            {
                if (e.Apps is null)
                    continue;
                var spare = IsKeep(key, keep) ? keepApp : null;
                foreach (var name in e.Apps.Where(kv => kv.Value.Rank < 1 && !string.Equals(kv.Key, spare, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList())
                    e.Apps.Remove(name);
                if (e.Apps.Count == 0)
                    e.Apps = null;
            }
        }

        if (_entries.Count > MaxEntries)
        {
            var drop = _entries
                .Where(kv => !IsKeep(kv.Key, keep))
                .OrderBy(kv => Score(kv.Value, now))
                .ThenBy(kv => kv.Value.LastAccess)
                .Take(_entries.Count - MaxEntries)
                .Select(kv => kv.Key)
                .ToList();
            foreach (var key in drop)
                _entries.Remove(key);
        }
    }

    private static string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("::", StringComparison.Ordinal))
            return null;
        if (!Path.IsPathFullyQualified(path))
            return null;

        try
        {
            var full = Path.GetFullPath(path);
            var rootLength = Path.GetPathRoot(full)?.Length ?? 0;
            while (full.Length > rootLength && full[^1] == Path.DirectorySeparatorChar)
                full = full[..^1];
            return full;
        }
        catch
        {
            return null;
        }
    }

    private sealed class Entry
    {
        public string Path { get; set; } = "";
        public double Rank { get; set; }
        public DateTimeOffset LastAccess { get; set; }

        /// <summary>Per-app use keyed by lower-case process name; null when the entry has no app data.</summary>
        public Dictionary<string, AppUse>? Apps { get; set; }
    }

    private sealed class AppUse
    {
        public double Rank { get; set; }
        public DateTimeOffset LastAccess { get; set; }
    }

    private sealed class FileModel
    {
        public int Version { get; set; } = 1;
        public List<EntryModel?> Entries { get; set; } = [];
    }

    private sealed class EntryModel
    {
        public string? Path { get; set; }
        public double Rank { get; set; }
        public DateTimeOffset LastAccess { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, AppUseModel?>? Apps { get; set; }
    }

    private sealed class AppUseModel
    {
        public double Rank { get; set; }
        public DateTimeOffset LastAccess { get; set; }
    }
}
