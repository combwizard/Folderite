using Folderite.Targets;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Folderite.History;

/// <summary>
/// Records folders visited in any supported file manager without polling. Folder changes show up as
/// name changes (Explorer/Dopus titles, Total Commander's path label) and window activations; both are
/// caught with WinEvent hooks, debounced, and resolved to a path by the matching adapter.
/// </summary>
internal sealed class HistoryTracker : IDisposable
{
    public const nuint TimerId = 0x4654;   // forwarded from the owner's WM_TIMER
    private const uint DebounceMs = 500;

    private readonly HWND _owner;
    private readonly TargetRegistry _registry;
    private readonly FrecencyStore _store;
    private readonly WINEVENTPROC _proc;   // kept alive for the native side
    private readonly HashSet<nint> _pending = [];
    private UnhookWinEventSafeHandle? _foregroundHook;
    private UnhookWinEventSafeHandle? _nameHook;

    public HistoryTracker(HWND owner, TargetRegistry registry, FrecencyStore store)
    {
        _owner = owner;
        _registry = registry;
        _store = store;
        _proc = OnWinEvent;
    }

    public void Start()
    {
        const uint flags = PInvoke.WINEVENT_OUTOFCONTEXT | PInvoke.WINEVENT_SKIPOWNPROCESS;
        _foregroundHook = PInvoke.SetWinEventHook(PInvoke.EVENT_SYSTEM_FOREGROUND, PInvoke.EVENT_SYSTEM_FOREGROUND, null, _proc, 0, 0, flags);
        _nameHook = PInvoke.SetWinEventHook(PInvoke.EVENT_OBJECT_NAMECHANGE, PInvoke.EVENT_OBJECT_NAMECHANGE, null, _proc, 0, 0, flags);
    }

    private void OnWinEvent(HWINEVENTHOOK hook, uint evt, HWND hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd.IsNull || idObject != 0 /* OBJID_WINDOW */ || idChild != 0) return;
        HWND root = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOT);
        if (root.IsNull) return;
        var window = _registry.Describe(root, root);
        if (_registry.Find(window) is null) return;

        _pending.Add(root);
        PInvoke.SetTimer(_owner, TimerId, DebounceMs, null);
    }

    /// <summary>Call when the owner receives WM_TIMER with <see cref="TimerId"/>.</summary>
    public void Flush()
    {
        PInvoke.KillTimer(_owner, TimerId);
        foreach (nint h in _pending)
        {
            var root = (HWND)h;
            if (!PInvoke.IsWindow(root)) continue;
            var window = _registry.Describe(root, root);
            if (_registry.Find(window)?.GetCurrentPath(window) is { } path)
                _store.Record(path);
        }
        _pending.Clear();
    }

    public void Dispose()
    {
        _foregroundHook?.Dispose();
        _nameHook?.Dispose();
    }
}
