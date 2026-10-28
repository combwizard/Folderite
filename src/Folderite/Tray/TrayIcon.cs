using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Folderite.Tray;

// Notification-area icon using NOTIFYICON_VERSION_4 semantics.
internal sealed class TrayIcon : IDisposable
{
    private const uint IconId = 1;
    private const uint WmContextMenu = 0x007B;
    private const uint NotifyIconVersion4 = 4;
    private const uint NinSelect = 0x0400;     // WM_USER + 0
    private const uint NinKeySelect = 0x0401;  // WM_USER + 1

    private readonly HWND _hwnd;
    private readonly uint _callbackMessage;
    private readonly HICON _icon;
    private string _tooltip;
    private bool _disposed;

    public TrayIcon(nint hwnd, uint callbackMessage, string tooltip)
    {
        _hwnd = new HWND(hwnd);
        _callbackMessage = callbackMessage;
        _tooltip = tooltip;
        _icon = LoadFolderIcon();
        Add();
    }

    // Registered once; Explorer broadcasts this when the taskbar is recreated.
    public static uint TaskbarCreatedMessage { get; } = PInvoke.RegisterWindowMessage("TaskbarCreated");

    public event Action<int, int>? ContextMenuRequested;
    public event Action? Activated;

    // Re-adds the icon after Explorer restarts.
    public void Readd() => Add();

    public void HandleCallback(nuint wParam, nint lParam)
    {
        // Event code is in LOWORD(lParam) with NOTIFYICON_VERSION_4.
        uint notifyEvent = (ushort)lParam;
        switch (notifyEvent)
        {
            case WmContextMenu:
                // Anchor point is packed into wParam as signed shorts.
                int x = (short)(ushort)wParam;
                int y = (short)(ushort)(wParam >> 16);
                ContextMenuRequested?.Invoke(x, y);
                break;
            case NinSelect:
            case NinKeySelect:
                Activated?.Invoke();
                break;
        }
    }

    public void ShowBalloon(string title, string text)
    {
        unsafe
        {
            var data = CreateData(NOTIFY_ICON_DATA_FLAGS.NIF_INFO);
            CopyTruncated(text, data.szInfo.AsSpan());
            CopyTruncated(title, data.szInfoTitle.AsSpan());
            data.dwInfoFlags = NOTIFY_ICON_INFOTIP_FLAGS.NIIF_INFO;
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &data);
        }
    }

    public void SetTooltip(string text)
    {
        _tooltip = text;
        unsafe
        {
            var data = CreateData(NOTIFY_ICON_DATA_FLAGS.NIF_TIP | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
            CopyTruncated(text, data.szTip.AsSpan());
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, &data);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        unsafe
        {
            var data = CreateData(0);
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, &data);
        }
        PInvoke.DestroyIcon(_icon);
    }

    private void Add()
    {
        unsafe
        {
            var data = CreateData(
                NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE |
                NOTIFY_ICON_DATA_FLAGS.NIF_ICON |
                NOTIFY_ICON_DATA_FLAGS.NIF_TIP |
                NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP);
            CopyTruncated(_tooltip, data.szTip.AsSpan());
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, &data);

            data.Anonymous.uVersion = NotifyIconVersion4;
            PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, &data);
        }
    }

    private NOTIFYICONDATAW CreateData(NOTIFY_ICON_DATA_FLAGS flags)
    {
        unsafe
        {
            return new NOTIFYICONDATAW
            {
                cbSize = (uint)sizeof(NOTIFYICONDATAW),
                hWnd = _hwnd,
                uID = IconId,
                uFlags = flags,
                uCallbackMessage = _callbackMessage,
                hIcon = _icon,
            };
        }
    }

    private static HICON LoadFolderIcon()
    {
        unsafe
        {
            var info = new SHSTOCKICONINFO { cbSize = (uint)sizeof(SHSTOCKICONINFO) };
            HRESULT hr = PInvoke.SHGetStockIconInfo(
                SHSTOCKICONID.SIID_FOLDER,
                SHGSI_FLAGS.SHGSI_ICON | SHGSI_FLAGS.SHGSI_SMALLICON,
                &info);
            Marshal.ThrowExceptionForHR(hr.Value);
            return info.hIcon;
        }
    }

    // Copies text into a fixed char buffer, truncating and keeping a null terminator.
    private static void CopyTruncated(string text, Span<char> buffer)
    {
        int n = Math.Min(text.Length, buffer.Length - 1);
        text.AsSpan(0, n).CopyTo(buffer);
        buffer[n] = '\0';
    }
}
