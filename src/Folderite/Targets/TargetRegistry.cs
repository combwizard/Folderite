using Folderite.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Folderite.Targets;

/// <summary>Ordered list of adapters; resolves a window under the cursor to the adapter that handles it.</summary>
internal sealed class TargetRegistry(IReadOnlyList<ITargetAdapter> adapters)
{
    private readonly Dictionary<uint, string> _processNames = [];

    public IReadOnlyList<ITargetAdapter> Adapters { get; } = adapters;

    /// <summary>Process names (lower-case, no extension) where nothing triggers.</summary>
    public HashSet<string> DisabledProcesses { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public TargetWindow Describe(HWND root, HWND hit, (int X, int Y)? cursor = null)
    {
        uint pid;
        unsafe { PInvoke.GetWindowThreadProcessId(root, &pid); }
        return new TargetWindow(root, hit, Win32.GetClassName(root), GetProcessName(pid), pid) { Cursor = cursor };
    }

    public ITargetAdapter? Find(in TargetWindow window)
    {
        if (DisabledProcesses.Contains(window.ProcessName)) return null;
        foreach (var adapter in Adapters)
            if (adapter.Matches(window))
                return adapter;
        return null;
    }

    // Called from both the hook thread and the UI thread.
    private string GetProcessName(uint pid)
    {
        lock (_processNames)
        {
            if (_processNames.TryGetValue(pid, out var name)) return name;
            if (_processNames.Count > 256) _processNames.Clear();   // pids get reused; keep the cache short-lived
            var path = Win32.GetProcessImagePath(pid);
            name = path is null ? "" : Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            _processNames[pid] = name;
            return name;
        }
    }
}
