using Folderite.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Folderite.Targets.ThirdParty;

/// <summary>
/// Blender's file browser window ("Blender File View", class GHOST_WindowClass, process blender), as opened by
/// File ▸ Open, Import, Export and folder pickers. Blender draws its own UI and reads the keyboard through raw input,
/// so there are no controls to talk to and posted key messages are ignored; the only way in is real input:
/// <list type="number">
/// <item>Ctrl+F (file.start_filter) starts editing the filter box in the path bar;</item>
/// <item>Shift+Tab moves the text cursor to the previous text field in that bar, the directory field;</item>
/// <item>Ctrl+A, Ctrl+V, Enter replace the directory with the path, which the clipboard holds for the moment.</item>
/// </list>
/// The cursor is parked over the file list first, because Blender sends keys to whatever is under the mouse and
/// Ctrl+F over a bookmark list opens that list's own filter; it goes out and back within one input batch, so the
/// pointer doesn't visibly move. The clipboard is put back afterwards.
/// The current folder can't be read, so the menu has no "current folder" for Blender.
/// </summary>
internal sealed class BlenderAdapter(nint clipboardOwner) : ITargetAdapter
{
    private const string WindowClass = "GHOST_WindowClass";
    private const string ProcessName = "blender";
    private const string FileViewTitle = "Blender File View";

    private const ushort VkReturn = 0x0D, VkTab = 0x09, VkShift = 0x10, VkControl = 0x11;

    public string Name => "Blender";

    public bool Matches(in TargetWindow window) =>
        window.ClassName == WindowClass && window.ProcessName == ProcessName &&
        Win32.GetWindowText((HWND)window.Root) == FileViewTitle;   // top-level window of another process: no message sent

    public string? GetCurrentPath(in TargetWindow window) => null;

    public bool Navigate(in TargetWindow window, string path)
    {
        var root = (HWND)window.Root;
        if (string.IsNullOrEmpty(path) || !WaitForForeground(root)) return false;

        var owner = (HWND)clipboardOwner;
        var saved = Clipboard.Save(owner);
        if (!Clipboard.SetText(owner, path, transient: true)) return false;
        uint ours = Clipboard.SequenceNumber;
        PInvoke.GetCursorPos(out var cursor);
        try
        {
            if (!PInvoke.GetClientRect(root, out RECT client)) return false;
            var center = new System.Drawing.Point(client.Width / 2, client.Height / 2);
            PInvoke.ClientToScreen(root, ref center);

            // Out and back in one batch: Blender gets a mouse move to the center, Ctrl+F, then a move back, in that
            // order, while the real pointer is back before the screen can draw it at the center.
            if (!Step(root, [MoveTo(center.X, center.Y), .. Chord(VkControl, 'F'), MoveTo(cursor.X, cursor.Y)])) return false;
            if (!Step(root, Chord(VkShift, VkTab))) return false;
            if (!Step(root, [.. Chord(VkControl, 'A'), .. Chord(VkControl, 'V'), .. Key(VkReturn)])) return false;
            return true;
        }
        finally
        {
            // Only if a batch was cut short; normally the pointer is already back.
            if (PInvoke.GetCursorPos(out var now) && (now.X != cursor.X || now.Y != cursor.Y))
                PInvoke.SetCursorPos(cursor.X, cursor.Y);
            Thread.Sleep(150);   // let Blender finish reading the clipboard before it changes back
            if (saved is not null && Clipboard.SequenceNumber == ours)   // unless the user copied something meanwhile
                Clipboard.Restore(owner, saved);
        }
    }

    private static bool WaitForForeground(HWND root)
    {
        for (int i = 0; i < 25 && PInvoke.GetForegroundWindow() != root; i++)
            Thread.Sleep(20);
        if (PInvoke.GetForegroundWindow() != root) return false;
        Thread.Sleep(50);   // let Blender handle the activation (it resyncs modifier state then)
        return true;
    }

    /// <summary>
    /// Sends one batch of input, then waits until Blender has handled it. Refuses (and stops the sequence) once
    /// Blender is no longer foreground, so the keys can never land in another app.
    /// </summary>
    private static bool Step(HWND root, INPUT[] inputs)
    {
        if (PInvoke.GetForegroundWindow() != root) return false;
        if (!Send(inputs)) return false;
        // Blender dispatches queued input and sent messages in the same pump, then runs its handlers before the next
        // one; two round trips spaced apart put us after the handlers for these keys.
        for (int i = 0; i < 2; i++)
        {
            Thread.Sleep(40);
            Win32Util.SendMessageTimeout(root, PInvoke.WM_NULL, 0, 0, 1000);
        }
        return true;
    }

    private static bool Send(INPUT[] inputs) =>
        PInvoke.SendInput(inputs, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>()) == inputs.Length;

    private static INPUT[] Chord(ushort modifier, ushort vk) => [KeyEvent(modifier, false), .. Key(vk), KeyEvent(modifier, true)];

    private static INPUT[] Key(ushort vk) => [KeyEvent(vk, false), KeyEvent(vk, true)];

    // Blender maps keys from the virtual key and scan code in the raw input record, so fill in both.
    private static INPUT KeyEvent(ushort vk, bool up) => new()
    {
        type = INPUT_TYPE.INPUT_KEYBOARD,
        Anonymous = new()
        {
            ki = new KEYBDINPUT
            {
                wVk = (VIRTUAL_KEY)vk,
                wScan = (ushort)PInvoke.MapVirtualKey(vk, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC),
                dwFlags = up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0,
            },
        },
    };

    // A move through SendInput, so it is queued in order with the keys (SetCursorPos isn't).
    private static INPUT MoveTo(int x, int y)
    {
        int left = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_XVIRTUALSCREEN);
        int top = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_YVIRTUALSCREEN);
        int width = Math.Max(PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXVIRTUALSCREEN), 2);
        int height = Math.Max(PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYVIRTUALSCREEN), 2);
        return new INPUT
        {
            type = INPUT_TYPE.INPUT_MOUSE,
            Anonymous = new()
            {
                mi = new MOUSEINPUT
                {
                    dx = (int)Math.Round((x - left) * 65535.0 / (width - 1)),
                    dy = (int)Math.Round((y - top) * 65535.0 / (height - 1)),
                    dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE | MOUSE_EVENT_FLAGS.MOUSEEVENTF_ABSOLUTE |
                              MOUSE_EVENT_FLAGS.MOUSEEVENTF_VIRTUALDESK,
                },
            },
        };
    }
}
