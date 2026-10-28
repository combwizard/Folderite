namespace Folderite.Targets;

/// <summary>A window under the cursor when the trigger fired.</summary>
/// <param name="Root">Top-level window (GetAncestor GA_ROOT).</param>
/// <param name="Hit">The exact child window under the cursor.</param>
/// <param name="ClassName">Window class of <paramref name="Root"/>.</param>
/// <param name="ProcessName">Executable file name without extension, lower-case (e.g. "explorer").</param>
/// <param name="ProcessId">Owning process id.</param>
public readonly record struct TargetWindow(nint Root, nint Hit, string ClassName, string ProcessName, uint ProcessId)
{
    /// <summary>Screen point (physical pixels) of the click, when the window was resolved from a click.</summary>
    public (int X, int Y)? Cursor { get; init; }
}

/// <summary>Knows how to read and change the current folder of one kind of file-manager window.</summary>
public interface ITargetAdapter
{
    string Name { get; }

    /// <summary>
    /// Called from inside the low-level mouse hook, so it must be cheap: class names,
    /// process names, at most a FindWindowEx or two. No COM, no process launches.
    /// </summary>
    bool Matches(in TargetWindow window);

    /// <summary>Current folder of the window, or null if it cannot be determined (virtual folder, error).</summary>
    string? GetCurrentPath(in TargetWindow window);

    /// <summary>Navigate the window to <paramref name="path"/>. Returns false on failure.</summary>
    bool Navigate(in TargetWindow window, string path);

    /// <summary>Folders currently shown in all windows of this kind (for the "Open windows" section).</summary>
    IEnumerable<string> EnumerateOpenPaths() => [];
}
