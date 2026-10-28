using System.Text.Json;
using System.Text.Json.Serialization;

namespace Folderite.Config;

/// <summary>Loads, saves and hot-reloads config.json.</summary>
public sealed class ConfigStore : IDisposable
{
    private const string ConfigFileName = "config.json";
    private const string HistoryFileName = "history.json";
    private const int DebounceMs = 300;
    private const int SuppressMs = 1000;
    private const int ReadRetries = 3;
    private const int RetryDelayMs = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IgnoreReadOnlyProperties = true, // keeps FavoriteEntry.IsGroup out of the file
    };

    private readonly Action<Action>? _dispatcher;
    private readonly object _lock = new();
    private AppConfig _current = new();
    private string? _lastError;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;
    private long _suppressUntilTicks; // Environment.TickCount64 based
    private bool _disposed;

    public ConfigStore(string directory, Action<Action>? dispatcher = null)
    {
        Directory = directory;
        _dispatcher = dispatcher;
        ConfigPath = Path.Combine(directory, ConfigFileName);
        HistoryPath = Path.Combine(directory, HistoryFileName);
    }

    /// <summary>Portable mode (config.json next to the exe) if present, else %APPDATA%\Folderite.</summary>
    public static string DefaultDirectory
    {
        get
        {
            if (File.Exists(Path.Combine(AppContext.BaseDirectory, ConfigFileName)))
                return AppContext.BaseDirectory;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Folderite");
        }
    }

    public string Directory { get; }
    public string ConfigPath { get; }
    public string HistoryPath { get; }

    public AppConfig Current
    {
        get { lock (_lock) return _current; }
    }

    /// <summary>Message of the last failed load, or null if the last load succeeded.</summary>
    public string? LastError
    {
        get { lock (_lock) return _lastError; }
    }

    /// <summary>Raised (via the dispatcher) after a watcher-triggered reload succeeded.</summary>
    public event Action? Changed;

    /// <summary>Loads config.json, creating it with defaults if missing. Never throws; failures set <see cref="LastError"/>.</summary>
    public AppConfig Load()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            return Current;
        }

        if (!File.Exists(ConfigPath))
        {
            var defaults = CreateDefault();
            lock (_lock)
            {
                _current = defaults;
                _lastError = null;
            }
            try { Save(); }
            catch (Exception ex) { SetError(ex.Message); }
            return defaults;
        }

        TryRead(retry: false);
        return Current;
    }

    /// <summary>Writes <see cref="Current"/> to config.json without triggering a hot reload.</summary>
    public void Save()
    {
        var config = Current;
        System.IO.Directory.CreateDirectory(Directory);
        var json = JsonSerializer.Serialize(config, JsonOptions);

        Suppress();
        var temp = ConfigPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, ConfigPath, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch { /* best effort */ }
            throw;
        }
        finally
        {
            Suppress();
        }
    }

    /// <summary>Starts watching config.json for external edits.</summary>
    public void StartWatching()
    {
        lock (_lock)
        {
            if (_disposed || _watcher is not null)
                return;

            System.IO.Directory.CreateDirectory(Directory);
            _debounce = new Timer(_ => OnDebounced(), null, Timeout.Infinite, Timeout.Infinite);
            var watcher = new FileSystemWatcher(Directory, ConfigFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
            };
            watcher.Changed += OnFileEvent;
            watcher.Created += OnFileEvent;
            watcher.Renamed += OnFileEvent;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
    }

    public void Dispose()
    {
        FileSystemWatcher? watcher;
        Timer? timer;
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            watcher = _watcher;
            timer = _debounce;
            _watcher = null;
            _debounce = null;
        }

        if (watcher is not null)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        timer?.Dispose();
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _debounce?.Change(DebounceMs, Timeout.Infinite);
        }
    }

    private void OnDebounced()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
        }

        if (Environment.TickCount64 < Interlocked.Read(ref _suppressUntilTicks))
            return;
        if (!File.Exists(ConfigPath))
            return;

        if (!TryRead(retry: true))
            return;

        var handler = Changed;
        if (handler is null)
            return;
        if (_dispatcher is null)
            handler();
        else
            _dispatcher(() => handler());
    }

    private void Suppress() =>
        Interlocked.Exchange(ref _suppressUntilTicks, Environment.TickCount64 + SuppressMs);

    private bool TryRead(bool retry)
    {
        var attempts = retry ? ReadRetries : 1;
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                string text;
                using (var stream = new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream))
                    text = reader.ReadToEnd();

                var config = JsonSerializer.Deserialize<AppConfig>(text, JsonOptions)
                    ?? throw new JsonException("config.json is empty or null.");
                lock (_lock)
                {
                    _current = config;
                    _lastError = null;
                }
                return true;
            }
            catch (IOException) when (i < attempts - 1)
            {
                Thread.Sleep(RetryDelayMs);
            }
            catch (Exception ex)
            {
                SetError(ex.Message);
                return false;
            }
        }
        return false;
    }

    private void SetError(string message)
    {
        lock (_lock) _lastError = message;
    }

    private static AppConfig CreateDefault()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new AppConfig
        {
            Favorites =
            [
                new() { Name = "&Home", Path = profile },
                new() { Name = "&Desktop", Path = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) },
                new() { Name = "Do&wnloads", Path = Path.Combine(profile, "Downloads") },
                new() { Name = "D&ocuments", Path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) },
            ],
        };
    }
}
