using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.System.Ole;
using IServiceProvider = Windows.Win32.System.Com.IServiceProvider;

namespace Folderite.Targets;

/// <summary>
/// Windows Explorer via Shell.Application's ShellWindows (late-bound IDispatch). On Windows 11 every tab is its
/// own ShellWindows entry sharing the frame's HWND; the active tab is the first ShellTabWindowClass child.
/// </summary>
internal sealed class ExplorerAdapter : ITargetAdapter
{
    private dynamic? _shell;

    public string Name => "Explorer";

    public bool Matches(in TargetWindow window) =>
        window.ClassName is "CabinetWClass" or "ExploreWClass" && !IsOverTabStrip(window);

    /// <summary>
    /// Win11 tabs share the title-bar row with TITLE_BAR_SCAFFOLDING_WINDOW_CLASS (the caption area right of the tabs);
    /// clicks above its bottom edge keep their native meaning (middle-click closes a tab). No scaffolding = no tabs.
    /// </summary>
    private static bool IsOverTabStrip(in TargetWindow window)
    {
        if (window.Cursor is not { } pt) return false;
        HWND scaffold = PInvoke.FindWindowEx((HWND)window.Root, HWND.Null, "TITLE_BAR_SCAFFOLDING_WINDOW_CLASS", null);
        return !scaffold.IsNull && PInvoke.GetWindowRect(scaffold, out var r) && pt.Y < r.bottom;
    }

    public string? GetCurrentPath(in TargetWindow window)
    {
        try { return FindBrowser((HWND)window.Root) is { } browser ? PathOf(browser) : null; }
        catch { return null; }
    }

    public bool Navigate(in TargetWindow window, string path)
    {
        try
        {
            if (FindBrowser((HWND)window.Root) is not { } browser) return false;
            browser.Navigate2(path);
            return true;
        }
        catch { return false; }
    }

    public IEnumerable<string> EnumerateOpenPaths()
    {
        var paths = new List<string>();
        try
        {
            foreach (var browser in Browsers())
                if (PathOf(browser) is { } p) paths.Add(p);
        }
        catch { }
        return paths;
    }

    public static void OpenNewWindow(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });

    private dynamic? FindBrowser(HWND root)
    {
        HWND activeTab = PInvoke.FindWindowEx(root, HWND.Null, "ShellTabWindowClass", null);
        dynamic? first = null;
        foreach (var browser in Browsers())
        {
            if (HwndOf(browser) != (nint)root) continue;
            if (activeTab.IsNull) return browser;
            first ??= browser;
            if (TabWindowOf(browser) == activeTab) return browser;
        }
        return first;
    }

    private IEnumerable<dynamic> Browsers()
    {
        _shell ??= Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!);
        dynamic windows = _shell!.Windows();
        int count = windows.Count;
        for (int i = 0; i < count; i++)
        {
            dynamic? w = null;
            try { w = windows.Item(i); } catch { }
            if (w is not null) yield return w;
        }
    }

    private static nint HwndOf(dynamic browser)
    {
        try { return (nint)Convert.ToInt64(browser.HWND); }
        catch { return 0; }
    }

    private static HWND TabWindowOf(object browser)
    {
        try
        {
            var sp = (IServiceProvider)browser;
            sp.QueryService(PInvoke.SID_STopLevelBrowser, out IShellBrowser shellBrowser);
            shellBrowser.GetWindow(out HWND hwnd);
            return hwnd;
        }
        catch { return HWND.Null; }
    }

    /// <summary>File-system path of the browser's folder; null for virtual folders (This PC, Home, search results).</summary>
    private static string? PathOf(dynamic browser)
    {
        try
        {
            string? path = browser.Document?.Folder?.Self?.Path;
            return path is not null && !path.StartsWith("::", StringComparison.Ordinal) && Path.IsPathRooted(path) ? path : null;
        }
        catch { return null; }
    }
}
