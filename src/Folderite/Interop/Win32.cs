using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace Folderite.Interop;

/// <summary>Small string-returning wrappers over CsWin32 calls used across modules.</summary>
internal static unsafe class Win32
{
    public static string GetClassName(HWND hwnd)
    {
        Span<char> buf = stackalloc char[256];
        fixed (char* p = buf)
        {
            int n = PInvoke.GetClassName(hwnd, p, buf.Length);
            return n > 0 ? new string(p, 0, n) : "";
        }
    }

    public static string GetWindowText(HWND hwnd)
    {
        int len = PInvoke.GetWindowTextLength(hwnd);
        if (len <= 0) return "";
        var buf = new char[len + 1];
        fixed (char* p = buf)
        {
            int n = PInvoke.GetWindowText(hwnd, p, buf.Length);
            return new string(p, 0, n);
        }
    }

    /// <summary>Text of a control in another process (WM_GETTEXT is marshalled cross-process, GetWindowText is not).</summary>
    public static string GetControlText(HWND hwnd)
    {
        int len = (int)PInvoke.SendMessage(hwnd, PInvoke.WM_GETTEXTLENGTH, 0, 0).Value;
        if (len <= 0) return "";
        var buf = new char[len + 1];
        fixed (char* p = buf)
        {
            int n = (int)PInvoke.SendMessage(hwnd, PInvoke.WM_GETTEXT, (nuint)buf.Length, (nint)p).Value;
            return new string(p, 0, n);
        }
    }

    public static void SetControlText(HWND hwnd, string text)
    {
        fixed (char* p = text)
            PInvoke.SendMessage(hwnd, PInvoke.WM_SETTEXT, 0, (nint)p);
    }

    public static string? GetProcessImagePath(uint pid)
    {
        HANDLE h = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h.IsNull) return null;
        try
        {
            char* buf = stackalloc char[1024];
            uint size = 1024;
            if (!PInvoke.QueryFullProcessImageName(h, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buf, &size))
                return null;
            return new string(buf, 0, (int)size);
        }
        finally
        {
            PInvoke.CloseHandle(h);
        }
    }

    /// <summary>First descendant window with the given class (depth-first), or null.</summary>
    public static HWND FindDescendant(HWND parent, string className, Func<HWND, bool>? predicate = null)
    {
        HWND found = default;
        PInvoke.EnumChildWindows(parent, (h, _) =>
        {
            if (GetClassName(h) == className && (predicate is null || predicate(h)))
            {
                found = h;
                return false;
            }
            return true;
        }, 0);
        return found;
    }
}
