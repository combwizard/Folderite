using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.DataExchange;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Folderite.Interop;

/// <summary>WM_COPYDATA channel from the settings process to the tray app's message window.</summary>
internal static unsafe class Ipc
{
    public const string WindowClass = "FolderiteMessageWindow";

    public const string FlushHistory = "flush-history";
    public const string RemoveHistory = "remove-history";
    public const string ClearHistory = "clear-history";
    public const string GetPaused = "get-paused";
    public const string SetPaused = "set-paused";

    private const nuint Magic = 0x464C4452; // "FLDR"
    private const uint MaxBytes = 64 * 1024;
    private const uint TimeoutMs = 1000;

    public static bool IsRunning => !PInvoke.FindWindow(WindowClass, null).IsNull;

    /// <summary>Sends a command to the tray app. Null if it isn't running or the call timed out.</summary>
    public static nint? Send(string command, string? arg = null)
    {
        var hwnd = PInvoke.FindWindow(WindowClass, null);
        if (hwnd.IsNull)
            return null;

        var payload = Encode(command, arg);
        if ((uint)payload.Length * 2 > MaxBytes)
            return null;

        fixed (char* p = payload)
        {
            var cds = new COPYDATASTRUCT
            {
                dwData = Magic,
                cbData = (uint)payload.Length * 2,
                lpData = p,
            };
            nuint result = 0;
            var r = PInvoke.SendMessageTimeout(hwnd, PInvoke.WM_COPYDATA, 0, (nint)(&cds),
                SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG | SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_BLOCK, TimeoutMs, &result);
            return r.Value == 0 ? null : (nint)result;
        }
    }

    /// <summary>Receiving side: decodes the COPYDATASTRUCT behind a WM_COPYDATA lParam.</summary>
    public static bool TryHandle(nint lParam, out string command, out string? arg)
    {
        command = "";
        arg = null;
        if (lParam == 0)
            return false;

        var cds = (COPYDATASTRUCT*)lParam;
        if (cds->dwData != Magic || cds->cbData % 2 != 0 || cds->cbData > MaxBytes || cds->lpData is null)
            return false;

        var payload = new string((char*)cds->lpData, 0, (int)(cds->cbData / 2));
        return TryDecode(payload, out command, out arg);
    }

    internal static string Encode(string command, string? arg) =>
        arg is null ? command : command + "\n" + arg;

    internal static bool TryDecode(string payload, out string command, out string? arg)
    {
        var split = payload.IndexOf('\n');
        command = split < 0 ? payload : payload[..split];
        arg = split < 0 ? null : payload[(split + 1)..];
        return command.Length > 0;
    }
}
