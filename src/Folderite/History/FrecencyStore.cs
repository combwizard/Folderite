using System.Text.Json;
using System.Text.RegularExpressions;

namespace Folderite.History;

/// <summary>zoxide-style frecency store of visited folders, persisted as JSON.</summary>
public sealed class FrecencyStore
{
    private const int MaxEntries = 1000;
    private const double MaxTotalRank = 9000;
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

    /// <summary>Loads entries from disk. A missing or corrupt file yields an empty store; never throws.</summary>
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
                    loaded[path] = new Entry { Path = path, Rank = e.Rank, LastAccess = e.LastAccess };
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
                Version = 1,
                Entries = _entries.Values
                    .Select(e => (EntryModel?)new EntryModel { Path = e.Path, Rank = e.Rank, LastAccess = e.LastAccess })
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

    /// <summary>Records a visit to a folder. Repeats within 2 seconds are ignored.</summary>
    public void Record(string path)
    {
        var key = Normalize(path);
        if (key is null)
            return;

        lock (_lock)
        {
            if (IsExcluded(key))
                return;

            var now = _time.GetUtcNow();
            if (_entries.TryGetValue(key, out var entry))
            {
                if (now - entry.LastAccess < DebounceWindow && now >= entry.LastAccess)
                    return;
                entry.Path = key;
                entry.Rank += 1;
                entry.LastAccess = now;
            }
            else
            {
                _entries[key] = new Entry { Path = key, Rank = 1, LastAccess = now };
            }

            Age(now, keep: key);
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

    /// <summary>Returns up to <paramref name="count"/> folders ordered by frecency. Does not touch the disk.</summary>
    public IReadOnlyList<string> Top(int count)
    {
        if (count <= 0)
            return [];

        lock (_lock)
        {
            var now = _time.GetUtcNow();
            return _entries.Values
                .Where(e => !IsExcluded(e.Path))
                .OrderByDescending(e => Score(e, now))
                .ThenByDescending(e => e.LastAccess)
                .Take(count)
                .Select(e => e.Path)
                .ToList();
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

    private static double Score(Entry e, DateTimeOffset now)
    {
        var age = now - e.LastAccess;
        if (age < TimeSpan.FromHours(1)) return e.Rank * 4;
        if (age < TimeSpan.FromDays(1)) return e.Rank * 2;
        if (age < TimeSpan.FromDays(7)) return e.Rank * 0.5;
        return e.Rank * 0.25;
    }

    /// <param name="keep">The entry just recorded; never dropped by aging, or the visit that triggered it would be lost.</param>
    private void Age(DateTimeOffset now, string keep)
    {
        if (_entries.Values.Sum(e => e.Rank) > MaxTotalRank)
        {
            foreach (var e in _entries.Values)
                e.Rank *= 0.9;
            foreach (var key in _entries.Where(kv => kv.Value.Rank < 1 && !IsKeep(kv.Key, keep)).Select(kv => kv.Key).ToList())
                _entries.Remove(key);
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
    }
}
