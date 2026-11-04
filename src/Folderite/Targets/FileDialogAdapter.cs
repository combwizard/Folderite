using Folderite.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Folderite.Targets;

/// <summary>
/// Common Open/Save/folder-picker dialogs (#32770 hosting a shell view). Reads the folder from the breadcrumb
/// toolbar's caption ("Address: C:\..."); navigates by typing the path into the filename box and pressing OK,
/// or, for folder pickers (where OK would accept the folder), through the address bar's edit box.
/// </summary>
internal sealed class FileDialogAdapter : ITargetAdapter
{
    private const int IdOk = 1;
    private const int IdFileNameCombo = 0x47C;   // cmb13
    private const int IdFileNameEdit = 0x480;    // edt1 (older dialogs)
    private const int IdFileTypeCombo = 0x470;   // cmb1; folder pickers don't have one
    private const nuint VkReturn = 0x0D;
    private const uint WmNextDlgCtl = 0x0028;

    public string Name => "File dialog";

    public bool Matches(in TargetWindow window) =>
        window.ClassName == "#32770" &&
        (!PInvoke.FindWindowEx((HWND)window.Root, HWND.Null, "DUIViewWndClassName", null).IsNull ||
         !PInvoke.FindWindowEx((HWND)window.Root, HWND.Null, "SHELLDLL_DefView", null).IsNull);

    public string? GetCurrentPath(in TargetWindow window)
    {
        HWND crumbs = Win32.FindDescendant((HWND)window.Root, "ToolbarWindow32",
            h => Win32.GetClassName(PInvoke.GetParent(h)) == "Breadcrumb Parent");
        if (crumbs.IsNull) return null;

        // Caption is "<localized 'Address'>: <path>"; ':' can't appear in folder names, only after a drive letter.
        string text = Win32.GetWindowText(crumbs);
        int sep = text.IndexOf(": ", StringComparison.Ordinal);
        string path = sep >= 0 ? text[(sep + 2)..] : text;
        return Path.IsPathRooted(path) ? path : null;
    }

    public bool Navigate(in TargetWindow window, string path)
    {
        HWND root = (HWND)window.Root;
        // Save dialogs nest their controls, so they have no direct file type box either and take the address bar too.
        // Keep it that way: text set in their nested file name box doesn't count, and OK then saves the old name in the old folder.
        bool isFolderPicker = PInvoke.GetDlgItem(root, IdFileTypeCombo).IsNull;
        HWND fileEdit = FindFileNameEdit(root);

        if (!isFolderPicker && !fileEdit.IsNull)
            return NavigateViaFileName(root, fileEdit, path);
        return NavigateViaAddressBar(root, path);
    }

    /// <summary>True when the file name box holds a path (the app picked the location itself).</summary>
    public bool FileNameHasFolder(in TargetWindow window)
    {
        // In Vista-style dialogs the file name box sits deep inside the view, and its control id is 0: it's the editable
        // ComboBox under a FloatNotifySink (the file type box there has no Edit). Older dialogs use the classic ids.
        HWND root = (HWND)window.Root;
        HWND edit = default;
        Win32.FindDescendant(root, "ComboBox", h =>
        {
            if (PInvoke.GetDlgCtrlID(h) != IdFileNameCombo && Win32.GetClassName(PInvoke.GetParent(h)) != "FloatNotifySink")
                return false;
            edit = Win32.FindDescendant(h, "Edit");
            return !edit.IsNull;
        });
        if (edit.IsNull)
            edit = Win32.FindDescendant(root, "Edit", h => PInvoke.GetDlgCtrlID(h) == IdFileNameEdit);
        return !edit.IsNull && Win32.GetControlText(edit) is var text && (text.Contains('\\') || text.Contains('/'));
    }

    private static HWND FindFileNameEdit(HWND root)
    {
        HWND combo = PInvoke.GetDlgItem(root, IdFileNameCombo);
        if (!combo.IsNull)
        {
            HWND edit = Win32.FindDescendant(combo, "Edit");
            if (!edit.IsNull) return edit;
        }
        return PInvoke.GetDlgItem(root, IdFileNameEdit);
    }

    private bool NavigateViaFileName(HWND root, HWND edit, string path)
    {
        string previous = Win32.GetControlText(edit);
        Win32.SetControlText(edit, path);
        HWND ok = PInvoke.GetDlgItem(root, IdOk);
        PInvoke.SendMessage(root, PInvoke.WM_COMMAND, IdOk /* BN_CLICKED << 16 */, (nint)ok);

        // The dialog clears the filename box asynchronously once navigation completes; put the user's text back after that.
        if (previous.Length > 0)
        {
            var window = new TargetWindow(root, root, "#32770", "", 0);
            Task.Run(async () =>
            {
                for (int i = 0; i < 20 && !PathEquals(GetCurrentPath(window), path); i++)
                    await Task.Delay(50);
                await Task.Delay(100);
                if (PInvoke.IsWindow(edit) && Win32.GetControlText(edit) is var text && (text.Length == 0 || text == path))
                    Win32.SetControlText(edit, previous);
            });
        }
        return true;
    }

    private static bool PathEquals(string? a, string b) =>
        a is not null && string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static bool NavigateViaAddressBar(HWND root, string path)
    {
        HWND band = Win32.FindDescendant(root, "Address Band Root");
        if (band.IsNull) return false;
        HWND edit = Win32.FindDescendant(band, "Edit");
        HWND focus = HWND.Null;
        if (edit.IsNull)
        {
            if (!WaitForFront(root, out focus)) return false;
            edit = OpenAddressEdit(band);
        }
        if (edit.IsNull) return false;
        Win32.SetControlText(edit, path);
        // SendMessage, not PostMessage: a posted Enter goes through the dialog's IsDialogMessage, which turns it
        // into IDOK and accepts the *old* folder. Sent directly, the address band's edit handles it and navigates.
        PInvoke.SendMessage(edit, PInvoke.WM_KEYDOWN, VkReturn, 0x001C0001);
        PInvoke.SendMessage(edit, PInvoke.WM_KEYUP, VkReturn, unchecked((nint)0xC01C0001));
        if (!focus.IsNull) RestoreFocusAfterAddressBar(root, focus);
        return true;
    }

    /// <summary>
    /// Ctrl+L leaves the focus in the address bar, where Enter would not save or select. The bar turns back into path
    /// buttons a moment after navigating and takes the focus then, so wait for that. Left alone if the user moved on.
    /// </summary>
    private static void RestoreFocusAfterAddressBar(HWND root, HWND focus)
    {
        Task.Run(async () =>
        {
            for (int i = 0; i < 40; i++)
            {
                await Task.Delay(25);
                if (!PInvoke.IsWindow(root)) return;
                HWND now = FocusedControl(root);
                if (!now.IsNull && Win32.GetClassName(PInvoke.GetParent(now)) == "Breadcrumb Parent")
                {
                    PInvoke.SendMessage(root, WmNextDlgCtl, (nuint)(nint)focus, 1);
                    return;
                }
            }
        });
    }

    /// <summary>
    /// Waits for the dialog to be in front, so keys can't land in another app, and for it to have its focus back, which
    /// comes a moment later when the menu has just closed. <paramref name="focus"/> stays null if it doesn't come back.
    /// </summary>
    private static bool WaitForFront(HWND root, out HWND focus)
    {
        focus = HWND.Null;
        for (int i = 0; i < 15 && (PInvoke.GetForegroundWindow() != root || focus.IsNull); i++)
        {
            if (PInvoke.GetForegroundWindow() == root) focus = FocusedControl(root);
            if (focus.IsNull) Thread.Sleep(20);
        }
        return PInvoke.GetForegroundWindow() == root;
    }

    /// <summary>
    /// In a known folder such as Documents, the address bar has no edit box until someone asks for one, and only real
    /// input does that. Ctrl+L asks for it. The dialog has to be in front.
    /// </summary>
    private static HWND OpenAddressEdit(HWND band)
    {
        INPUT[] keys = [KeyEvent(VIRTUAL_KEY.VK_CONTROL, false), KeyEvent(VIRTUAL_KEY.VK_L, false), KeyEvent(VIRTUAL_KEY.VK_L, true), KeyEvent(VIRTUAL_KEY.VK_CONTROL, true)];
        if (PInvoke.SendInput(keys, System.Runtime.InteropServices.Marshal.SizeOf<INPUT>()) != keys.Length) return HWND.Null;

        HWND edit = HWND.Null;
        for (int i = 0; i < 25 && edit.IsNull; i++)
        {
            Thread.Sleep(20);
            edit = Win32.FindDescendant(band, "Edit");
        }
        return edit;
    }

    private static INPUT KeyEvent(VIRTUAL_KEY vk, bool up) => new()
    {
        type = INPUT_TYPE.INPUT_KEYBOARD,
        Anonymous = new() { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP : 0 } },
    };

    private static unsafe HWND FocusedControl(HWND root)
    {
        uint thread = PInvoke.GetWindowThreadProcessId(root, null);
        var info = new GUITHREADINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
        return thread != 0 && PInvoke.GetGUIThreadInfo(thread, ref info) ? info.hwndFocus : HWND.Null;
    }
}
