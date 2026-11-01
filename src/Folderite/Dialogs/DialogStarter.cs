using Folderite.Config;
using Folderite.Targets;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Folderite.Dialogs;

/// <summary>
/// Moves a newly shown Open/Save dialog to its start folder: the folder set for that app in
/// <see cref="AppConfig.DialogFolders"/>, else (with <see cref="AppConfig.DialogLastFolder"/>) the folder last used
/// in that app. Each dialog is handled once, on its first activation; after that it is the user's.
/// </summary>
internal sealed class DialogStarter
{
    public const nuint TimerId = 0x4656;   // forwarded from the owner's WM_TIMER
    private const uint PollMs = 50;
    private const int MaxPolls = 60;       // 3 s for the dialog to show its folder view and take input
    private const int SeenPruneAt = 64;

    private readonly HWND _owner;
    private readonly TargetRegistry _registry;
    private readonly FileDialogAdapter _dialogs;
    private readonly Func<AppConfig> _config;
    private readonly Func<string, string?> _lastInApp;
    private readonly HashSet<nint> _seen = [];
    private readonly List<Pending> _pending = [];

    private sealed class Pending(TargetWindow window, string path)
    {
        public TargetWindow Window { get; } = window;
        public string Path { get; } = path;
        public int Polls { get; set; }
    }

    /// <param name="lastInApp">Process name → the folder last used in that app, or null.</param>
    public DialogStarter(HWND owner, TargetRegistry registry, FileDialogAdapter dialogs, Func<AppConfig> config, Func<string, string?> lastInApp)
    {
        _owner = owner;
        _registry = registry;
        _dialogs = dialogs;
        _config = config;
        _lastInApp = lastInApp;
    }

    /// <summary>Call when any window comes to the foreground, before history records its folder.</summary>
    public void OnActivated(HWND root)
    {
        // Only the class here: a dialog can come to the foreground before its folder view exists, when it doesn't
        // look like a file dialog yet. OnTimer waits for that.
        var window = _registry.Describe(root, root);
        if (window.ClassName != "#32770") return;
        if (_seen.Contains(root)) return;
        if (_seen.Count >= SeenPruneAt) _seen.RemoveWhere(h => !PInvoke.IsWindow((HWND)h));
        _seen.Add(root);

        // Decided now: in 500 ms the history tracker records the folder the dialog opened in, and that would become
        // "the last folder used in this app".
        if (Resolve(_config(), window.ProcessName, _lastInApp) is not { } target) return;
        try { if (!Directory.Exists(target)) return; }
        catch { return; }

        _pending.Add(new Pending(window, target));
        PInvoke.SetTimer(_owner, TimerId, PollMs, null);
    }

    /// <summary>Call when the owner receives WM_TIMER with <see cref="TimerId"/>.</summary>
    public void OnTimer()
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            if (!PInvoke.IsWindow((HWND)p.Window.Root))
            {
                _pending.RemoveAt(i);
                continue;
            }

            // Find, not just Matches: it also honours the disabled-apps list. Message boxes never match and time out.
            // The breadcrumb gets its text once the folder view has loaded; navigating before that can be lost.
            string? current = _registry.Find(p.Window) is FileDialogAdapter ? _dialogs.GetCurrentPath(p.Window) : null;
            if (current is null)
            {
                if (++p.Polls >= MaxPolls) _pending.RemoveAt(i);
                continue;
            }

            // Already there, or a file name with a folder in it (the app chose where to save): leave it.
            if (PathEquals(current, p.Path) || _dialogs.FileNameHasFolder(p.Window))
            {
                _pending.RemoveAt(i);
                continue;
            }
            // Right after the folder view loads, the address bar may not take input yet: try again on the next tick.
            if (_dialogs.Navigate(p.Window, p.Path))
            {
                _pending.RemoveAt(i);
            }
            else if (++p.Polls >= MaxPolls)
            {
                Log.Info($"Couldn't move the {p.Window.ProcessName} file dialog to {p.Path}.");
                _pending.RemoveAt(i);
            }
        }
        if (_pending.Count == 0) PInvoke.KillTimer(_owner, TimerId);
    }

    /// <summary>The start folder for a dialog of <paramref name="processName"/>, or null to leave it alone.</summary>
    internal static string? Resolve(AppConfig config, string processName, Func<string, string?> lastInApp)
    {
        if (string.IsNullOrEmpty(processName)) return null;
        foreach (var folder in config.DialogFolders)
        {
            string app = folder.App.Trim();
            if (app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) app = app[..^4];
            if (string.Equals(app, processName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(folder.Path))
                return Environment.ExpandEnvironmentVariables(folder.Path.Trim());
        }
        return config.DialogLastFolder ? lastInApp(processName) : null;
    }

    private static bool PathEquals(string a, string b) =>
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
