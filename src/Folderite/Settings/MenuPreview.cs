using System.Windows;
using System.Windows.Interop;
using Folderite.Config;
using Folderite.History;
using Folderite.Interop;
using Folderite.Menu;
using Folderite.Settings.ViewModels;
using Folderite.Targets;
using Windows.Win32.Foundation;
using FolderClipboard = Folderite.Interop.Clipboard;

namespace Folderite.Settings;

/// <summary>Pops up the real owner-drawn folder menu, built from the settings being edited. Picks only report what they would do.</summary>
internal sealed class MenuPreview : IMenuCommands
{
    private readonly Window _window;
    private readonly ConfigStore _store;
    private readonly SettingsVm _vm;
    private readonly MenuBuilder _menu = new(new IconCache());
    private HwndSource? _source;

    public MenuPreview(Window window, ConfigStore store, SettingsVm vm)
    {
        _window = window;
        _store = store;
        _vm = vm;
    }

    public void Show(FrameworkElement anchor)
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (_source is null)
        {
            // MenuBuilder paints in the owner's WM_MEASUREITEM / WM_DRAWITEM, so route those to it.
            _source = HwndSource.FromHwnd(hwnd);
            _source.AddHook(Hook);
        }

        var config = _vm.BuildConfig();
        var pt = anchor.PointToScreen(new Point(0, anchor.ActualHeight + 4)); // device pixels
        _menu.Show(MenuComposer.Build(config, Context(config, hwnd), this), hwnd, (int)pt.X, (int)pt.Y);
    }

    private nint Hook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (!_menu.HandleMessage(hwnd, (uint)msg, (nuint)wParam, lParam, out var result)) return 0;
        handled = true;
        return result;
    }

    // Same sources as the tray app's menu, with the user profile standing in for the window it was opened over.
    private MenuContext Context(AppConfig config, nint hwnd)
    {
        var history = new FrecencyStore(_store.HistoryPath);
        try
        {
            Ipc.Send(Ipc.FlushHistory);
            history.Load();
            history.SetExcludes(config.HistoryExclude);
        }
        catch { /* preview without recents */ }

        var open = new List<string>();
        if (config.ShowOpenWindows)
        {
            try { open.AddRange(new ExplorerAdapter().EnumerateOpenPaths()); }
            catch { /* no open windows */ }
        }

        string? clip = null;
        if (config.ShowClipboard)
        {
            try { clip = FolderClipboard.GetFolderPath(new HWND(hwnd)); }
            catch { /* no clipboard */ }
        }

        return new MenuContext(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            open,
            history.Top(config.RecentCount + 1),
            clip);
    }

    private void Report(string text) => _vm.Status = "Preview: " + text;

    public void Navigate(string path) => Report($"would open {path}");
    public void RunAction(CustomAction action, string path) => Report($"would run {action.Command} {action.Args.Replace("{path}", path)}");
    public void CopyPath(string path) => Report($"would copy {path}");
    public void AddFavorite(string path) => Report($"would add {path} to favorites");
    public void OpenSettings() => Report("this is Settings");
    public void Reload() => Report("would reload config.json");
}
