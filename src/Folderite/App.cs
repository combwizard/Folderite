using System.Collections.Concurrent;
using System.Diagnostics;
using Folderite.Config;
using Folderite.History;
using Folderite.Hooks;
using Folderite.Interop;
using Folderite.Menu;
using Folderite.Targets;
using Folderite.Targets.ThirdParty;
using Folderite.Tray;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Folderite;

/// <summary>Owns the hidden message window and wires hook → adapter → menu → navigation.</summary>
internal sealed unsafe class App : IMenuCommands, IDisposable
{
    private const uint TrayMessage = PInvoke.WM_APP + 2;
    private const uint DispatchMessage = PInvoke.WM_APP + 3;
    private const nuint SaveTimerId = 0x4655;
    private const string WindowClass = "FolderiteMessageWindow";

    private readonly WNDPROC _wndProc;
    private readonly ConcurrentQueue<Action> _dispatchQueue = new();
    private HWND _hwnd;

    private ConfigStore _config = null!;
    private FrecencyStore _history = null!;
    private TargetRegistry _registry = null!;
    private IconCache _icons = null!;
    private MenuBuilder _menu = null!;
    private TrayIcon? _tray;
    private HistoryTracker? _tracker;
    private MouseHook? _hook;

    // The window the open menu was invoked from; null adapter = tray (navigation opens a new Explorer window).
    private TargetWindow _target;
    private ITargetAdapter? _targetAdapter;

    public App() => _wndProc = WndProc;

    private AppConfig Cfg => _config.Current;

    public int Run()
    {
        CreateMessageWindow();

        _config = new ConfigStore(ConfigStore.DefaultDirectory, PostToUi);
        _config.Load();
        _config.Changed += ApplyConfig;
        _config.StartWatching();

        _history = new FrecencyStore(_config.HistoryPath);
        _history.Load();

        _registry = new TargetRegistry(
        [
            new ExplorerAdapter(),
            new FileDialogAdapter(),
            new DopusAdapter(),
            new TotalCommanderAdapter(),
            new XYplorerAdapter(),
            new DesktopAdapter(() => Cfg.EnableOnDesktop),
        ]);
        ApplyConfig();

        _icons = new IconCache();
        _menu = new MenuBuilder(_icons);

        _tray = new TrayIcon(_hwnd, TrayMessage, "Folderite");
        _tray.ContextMenuRequested += ShowTrayMenu;
        _tray.Activated += () =>
        {
            PInvoke.GetCursorPos(out var pt);
            ShowFolderMenu(null, default, pt.X, pt.Y);
        };

        _tracker = new HistoryTracker(_hwnd, _registry, _history);
        _tracker.Start();

        _hook = new MouseHook(_hwnd)
        {
            ShouldTrigger = (root, hit, pt) => _registry.Find(_registry.Describe(root, hit, pt)) is not null,
        };
        _hook.Install();

        PInvoke.SetTimer(_hwnd, SaveTimerId, 60_000, null);

        if (_config.LastError is { } error)
            _tray.ShowBalloon("Folderite: config error", error);

        while (PInvoke.GetMessage(out MSG msg, HWND.Null, 0, 0) > 0)
        {
            PInvoke.TranslateMessage(msg);
            PInvoke.DispatchMessage(msg);
        }
        return 0;
    }

    private void CreateMessageWindow()
    {
        var hInstance = PInvoke.GetModuleHandle((string?)null);
        fixed (char* className = WindowClass)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _wndProc,
                hInstance = (HINSTANCE)hInstance.DangerousGetHandle(),
                lpszClassName = className,
            };
            if (PInvoke.RegisterClassEx(wc) == 0)
                throw new InvalidOperationException("RegisterClassEx failed");
        }
        // A real (hidden) top-level window rather than HWND_MESSAGE: TrackPopupMenu needs a foreground-capable owner.
        _hwnd = PInvoke.CreateWindowEx(0, WindowClass, "Folderite", WINDOW_STYLE.WS_POPUP, 0, 0, 0, 0,
            HWND.Null, null, hInstance, null);
        if (_hwnd.IsNull) throw new InvalidOperationException("CreateWindowEx failed");
    }

    private LRESULT WndProc(HWND hwnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        try
        {
            if (_menu is not null && _menu.HandleMessage(hwnd, msg, wParam, lParam, out nint handled))
                return (LRESULT)handled;

            switch (msg)
            {
                case MouseHook.TriggerMessage:
                    var (x, y) = MouseHook.UnpackPoint(lParam);
                    OnTrigger((HWND)(nint)wParam.Value, x, y);
                    return (LRESULT)0;
                case TrayMessage:
                    _tray?.HandleCallback(wParam, lParam);
                    return (LRESULT)0;
                case DispatchMessage:
                    while (_dispatchQueue.TryDequeue(out var action)) action();
                    return (LRESULT)0;
                case PInvoke.WM_TIMER when wParam.Value == HistoryTracker.TimerId:
                    _tracker?.Flush();
                    return (LRESULT)0;
                case PInvoke.WM_TIMER when wParam.Value == SaveTimerId:
                    if (_history.IsDirty) _history.Save();
                    return (LRESULT)0;
            }
            if (msg == TrayIcon.TaskbarCreatedMessage && msg != 0)
            {
                _tray?.Readd();
                return (LRESULT)0;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex);
        }
        return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void PostToUi(Action action)
    {
        _dispatchQueue.Enqueue(action);
        PInvoke.PostMessage(_hwnd, DispatchMessage, 0, 0);
    }

    private void ApplyConfig()
    {
        _registry.DisabledProcesses = new HashSet<string>(Cfg.DisabledProcesses, StringComparer.OrdinalIgnoreCase);
        _history.SetExcludes(Cfg.HistoryExclude);
        _icons?.Clear();
    }

    private void OnTrigger(HWND root, int x, int y)
    {
        var window = _registry.Describe(root, _hook!.LastHit, (x, y));
        if (_registry.Find(window) is not { } adapter) return;
        ShowFolderMenu(adapter, window, x, y);
    }

    private void ShowFolderMenu(ITargetAdapter? adapter, TargetWindow window, int x, int y)
    {
        string? current = adapter?.GetCurrentPath(window);
        if (current is not null) _history.Record(current);

        _target = window;
        _targetAdapter = adapter;

        var context = new MenuContext(
            current,
            Cfg.ShowOpenWindows ? OpenPaths() : [],
            _history.Top(Cfg.RecentCount + 1),
            Cfg.ShowClipboard ? Clipboard.GetFolderPath(_hwnd) : null);
        _menu.Show(MenuComposer.Build(Cfg, context, this), _hwnd, x, y);
    }

    private List<string> OpenPaths()
    {
        var paths = new List<string>();
        foreach (var adapter in _registry.Adapters)
        {
            try { paths.AddRange(adapter.EnumerateOpenPaths()); }
            catch (Exception ex) { Log.Error(ex); }
        }
        return paths;
    }

    private void ShowTrayMenu(int x, int y)
    {
        _targetAdapter = null;
        bool paused = _hook?.Paused == true;
        var nodes = new List<MenuNode>
        {
            new MenuItem(paused ? "&Resume" : "&Pause", () => { if (_hook is not null) _hook.Paused = !paused; }),
            new MenuItem(Autostart.IsEnabled ? "Start with Windows: &On" : "Start with Windows: &Off",
                () => Autostart.Set(!Autostart.IsEnabled)),
            MenuSeparator.Instance,
            new MenuItem("&Edit config…", EditConfig),
            new MenuItem("Open config &folder", () => ExplorerAdapter.OpenNewWindow(_config.Directory)),
            new MenuItem("Reloa&d", Reload),
            MenuSeparator.Instance,
            new MenuItem("E&xit", Exit),
        };
        _menu.Show(nodes, _hwnd, x, y);
    }

    // IMenuCommands

    public void Navigate(string path)
    {
        if (_targetAdapter is null)
        {
            ExplorerAdapter.OpenNewWindow(path);
            _history.Record(path);
            return;
        }
        PInvoke.SetForegroundWindow((HWND)_target.Root);
        if (_targetAdapter.Navigate(_target, path))
            _history.Record(path);
        else
            _tray?.ShowBalloon("Folderite", $"Couldn't open {path} in {_targetAdapter.Name}.");
    }

    public void RunAction(CustomAction action, string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(action.Command), action.Args.Replace("{path}", path))
            {
                UseShellExecute = true,
                WorkingDirectory = path,
            });
        }
        catch (Exception ex)
        {
            _tray?.ShowBalloon("Folderite", $"{action.Name.Replace("&", "")} failed: {ex.Message}");
        }
    }

    public void CopyPath(string path) => Clipboard.SetText(_hwnd, path);

    public void AddFavorite(string path)
    {
        Cfg.Favorites.Add(new FavoriteEntry { Name = MenuComposer.DisplayName(path).Replace("&", "&&"), Path = path });
        _config.Save();
    }

    public void EditConfig()
    {
        try { Process.Start(new ProcessStartInfo(_config.ConfigPath) { UseShellExecute = true }); }
        catch { Process.Start("notepad.exe", $"\"{_config.ConfigPath}\""); }
    }

    public void Reload()
    {
        _config.Load();
        ApplyConfig();
        if (_config.LastError is { } error)
            _tray?.ShowBalloon("Folderite: config error", error);
    }

    private void Exit() => PInvoke.PostQuitMessage(0);

    public void Dispose()
    {
        _hook?.Dispose();
        _tracker?.Dispose();
        _tray?.Dispose();
        if (_history?.IsDirty == true) _history.Save();
        _config?.Dispose();
        _icons?.Dispose();
        if (!_hwnd.IsNull) PInvoke.DestroyWindow(_hwnd);
    }
}
