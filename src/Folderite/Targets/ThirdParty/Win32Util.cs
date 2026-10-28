using Folderite.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.DataExchange;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Folderite.Targets.ThirdParty;

/// <summary>Small helpers shared by the third-party file manager adapters.</summary>
internal static unsafe class Win32Util
{
    public static string GetClassName(nint hwnd)
    {
        try { return Win32.GetClassName((HWND)hwnd); }
        catch { return ""; }
    }

    /// <summary>Text of a window or control in another process (uses WM_GETTEXT, which works cross-process).</summary>
    public static string GetWindowText(nint hwnd)
    {
        try { return Win32.GetControlText((HWND)hwnd); }
        catch { return ""; }
    }

    public static bool TryGetRect(nint hwnd, out RECT rect)
    {
        rect = default;
        try { return PInvoke.GetWindowRect(new HWND(hwnd), out rect); }
        catch { return false; }
    }

    public static bool IsDescendantOrSelf(nint ancestor, nint window) =>
        ancestor != 0 && window != 0 &&
        (ancestor == window || PInvoke.IsChild(new HWND(ancestor), new HWND(window)));

    /// <summary>SendMessage that cannot hang us if the target is busy or hung (200 ms cap).</summary>
    public static nint SendMessageTimeout(nint hwnd, uint msg, nuint wParam, nint lParam, uint timeoutMs = 200)
    {
        try
        {
            nuint result = 0;
            var r = PInvoke.SendMessageTimeout(new HWND(hwnd), msg, wParam, lParam,
                SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG, timeoutMs, &result);
            return r.Value == 0 ? 0 : (nint)result;
        }
        catch { return 0; }
    }

    /// <summary>Sends a WM_COPYDATA block (not null-terminated unless the caller adds it to <paramref name="data"/>).</summary>
    public static bool SendCopyData(nint hwnd, nuint dwData, byte[] data, uint timeoutMs = 2000)
    {
        try
        {
            fixed (byte* p = data)
            {
                var cds = new COPYDATASTRUCT
                {
                    dwData = (UIntPtr)dwData,
                    cbData = (uint)data.Length,
                    lpData = p,
                };
                nuint result = 0;
                var r = PInvoke.SendMessageTimeout(new HWND(hwnd), PInvoke.WM_COPYDATA, 0, (nint)(&cds),
                    SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG, timeoutMs, &result);
                return r.Value != 0;
            }
        }
        catch { return false; }
    }

    /// <summary>Full path of the executable that owns <paramref name="pid"/>, or null.</summary>
    public static string? GetProcessImagePath(uint pid)
    {
        try { return Win32.GetProcessImagePath(pid); }
        catch { return null; }
    }

    public static uint GetWindowProcessId(nint hwnd)
    {
        try
        {
            uint pid = 0;
            PInvoke.GetWindowThreadProcessId(new HWND(hwnd), &pid);
            return pid;
        }
        catch { return 0; }
    }

    /// <summary>Visible top-level windows with the given class name.</summary>
    public static List<nint> FindTopLevelWindows(string className)
    {
        var found = new List<nint>();
        try
        {
            PInvoke.EnumWindows((hwnd, _) =>
            {
                if (PInvoke.IsWindowVisible(hwnd) && Win32.GetClassName(hwnd) == className)
                    found.Add((nint)hwnd);
                return true;
            }, 0);
        }
        catch { }
        return found;
    }

    /// <summary>A usable local/UNC directory path (no trailing filter, must exist unless UNC), or null.</summary>
    public static string? ValidateDirectory(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            path = path.Trim();
            if (!Path.IsPathRooted(path)) return null;
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return path;
            return Directory.Exists(path) ? path : null;
        }
        catch { return null; }
    }
}
