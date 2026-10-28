using Folderite.Interop;

namespace Folderite.Targets;

/// <summary>The desktop: middle-click on the icon area; "navigating" opens a new Explorer window.</summary>
internal sealed class DesktopAdapter(Func<bool> isEnabled) : ITargetAdapter
{
    public string Name => "Desktop";

    public bool Matches(in TargetWindow window) =>
        window.ClassName is "Progman" or "WorkerW" &&
        Win32.GetClassName((Windows.Win32.Foundation.HWND)window.Hit) is "SysListView32" or "SHELLDLL_DefView" &&
        isEnabled();

    public string? GetCurrentPath(in TargetWindow window) =>
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    public bool Navigate(in TargetWindow window, string path)
    {
        try { ExplorerAdapter.OpenNewWindow(path); return true; }
        catch { return false; }
    }
}
