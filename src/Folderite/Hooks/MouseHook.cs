using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Folderite.Hooks;

/// <summary>
/// Global WH_MOUSE_LL hook for the middle button, running on its own thread so that slow work on the UI
/// thread (COM calls, dopusrt) can never stall system-wide mouse input or get the hook silently removed.
/// The callback only asks <see cref="ShouldTrigger"/> and posts <see cref="TriggerMessage"/> to the owner.
/// </summary>
internal sealed class MouseHook : IDisposable
{
    /// <summary>wParam = root HWND, lParam = packed screen point (see <see cref="UnpackPoint"/>).</summary>
    public const uint TriggerMessage = PInvoke.WM_APP + 1;

    private readonly HWND _owner;
    private readonly HOOKPROC _proc;   // kept alive for the native side
    private Thread? _thread;
    private uint _threadId;
    private bool _swallowUp;

    /// <summary>Given (root window, hit window, screen point), decide whether to swallow the click. Called on the hook thread; must be fast.</summary>
    public Func<HWND, HWND, (int X, int Y), bool>? ShouldTrigger { get; set; }

    public volatile bool Paused;

    public MouseHook(HWND owner)
    {
        _owner = owner;
        _proc = HookProc;
    }

    public void Install()
    {
        using var ready = new ManualResetEventSlim();
        Exception? error = null;
        _thread = new Thread(() =>
        {
            _threadId = PInvoke.GetCurrentThreadId();
            using var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _proc, PInvoke.GetModuleHandle((string?)null), 0);
            if (hook.IsInvalid) error = new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
            ready.Set();
            if (error is not null) return;
            while (PInvoke.GetMessage(out MSG msg, HWND.Null, 0, 0) > 0) { }
        })
        { IsBackground = true, Name = "Folderite mouse hook" };
        _thread.Start();
        ready.Wait();
        if (error is not null) throw error;
    }

    private LRESULT HookProc(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0 && !Paused)
        {
            uint msg = (uint)wParam.Value;
            if (msg == PInvoke.WM_MBUTTONDOWN)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                HWND hit = PInvoke.WindowFromPoint(info.pt);
                HWND root = PInvoke.GetAncestor(hit, GET_ANCESTOR_FLAGS.GA_ROOT);
                bool trigger;
                try { trigger = !root.IsNull && ShouldTrigger?.Invoke(root, hit, (info.pt.X, info.pt.Y)) == true; }
                catch { trigger = false; }

                if (trigger)
                {
                    _swallowUp = true;
                    LastHit = hit;
                    PInvoke.PostMessage(_owner, TriggerMessage, (nuint)(nint)root, PackPoint(info.pt.X, info.pt.Y));
                    return (LRESULT)1;
                }
            }
            else if (msg == PInvoke.WM_MBUTTONUP && _swallowUp)
            {
                _swallowUp = false;
                return (LRESULT)1;
            }
        }
        return PInvoke.CallNextHookEx(null, code, wParam, lParam);
    }

    /// <summary>Exact window under the cursor at the last trigger.</summary>
    public HWND LastHit { get; private set; }

    public static nint PackPoint(int x, int y) => (nint)(((long)(uint)y << 32) | (uint)x);
    public static (int X, int Y) UnpackPoint(nint packed) => ((int)(uint)((long)packed & 0xFFFFFFFF), (int)(uint)((long)packed >> 32));

    public void Dispose()
    {
        if (_thread is null) return;
        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, 0, 0);
        _thread.Join(1000);
        _thread = null;
    }
}
