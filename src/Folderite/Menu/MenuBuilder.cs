using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Folderite.Menu;

/// <summary>Renders a <see cref="MenuNode"/> tree as a native Win32 popup menu.</summary>
internal sealed unsafe class MenuBuilder
{
    private readonly IconCache _icons;
    private readonly Dictionary<uint, MenuItem> _commands = new();
    private readonly Dictionary<nint, SubMenu> _lazy = new();
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100;

    private uint _nextId;

    public MenuBuilder(IconCache icons) => _icons = icons;

    public static string EscapeAmpersands(string text) => text.Replace("&", "&&");

    /// <summary>Shows the menu modally at screen point (x, y). Runs the chosen item's action after the menu is destroyed.</summary>
    public bool Show(IReadOnlyList<MenuNode> nodes, nint owner, int x, int y)
    {
        _icons.TrimIfLarge();
        _commands.Clear();
        _lazy.Clear();
        _nextId = 1;

        var root = PInvoke.CreatePopupMenu();
        if (root.IsNull) return false;

        MenuItem? chosen = null;
        try
        {
            Populate(root, nodes);

            var hwnd = new HWND(owner);
            PInvoke.SetForegroundWindow(hwnd);
            var cmd = (uint)PInvoke.TrackPopupMenuEx(root,
                TPM_RETURNCMD | TPM_RIGHTBUTTON,
                x, y, hwnd, null).Value;
            PInvoke.PostMessage(hwnd, PInvoke.WM_NULL, 0, 0);

            if (cmd != 0) _commands.TryGetValue(cmd, out chosen);
        }
        finally
        {
            PInvoke.DestroyMenu(root);
            _commands.Clear();
            _lazy.Clear();
        }

        if (chosen is null) return false;
        chosen.OnInvoke();
        return true;
    }

    /// <summary>Handles WM_INITMENUPOPUP to fill lazy submenus. Returns true if the message was handled.</summary>
    public bool HandleMessage(nint hwnd, uint msg, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (msg != PInvoke.WM_INITMENUPOPUP) return false;

        var hmenu = (nint)wParam;
        if (!_lazy.Remove(hmenu, out var sub)) return false;

        var menu = new HMENU(hmenu);
        for (var n = PInvoke.GetMenuItemCount(menu); n > 0; n--)
            PInvoke.DeleteMenu(menu, 0, MENU_ITEM_FLAGS.MF_BYPOSITION);

        IReadOnlyList<MenuNode> children;
        try
        {
            children = sub.LazyChildren!();
        }
        catch
        {
            children = [];
        }

        if (children.Count == 0)
            AddString(menu, "(empty)", 0, disabled: true, isDefault: false, 0, default);
        else
            Populate(menu, children);
        return true;
    }

    private void Populate(HMENU menu, IReadOnlyList<MenuNode> nodes)
    {
        foreach (var node in Normalize(nodes))
        {
            switch (node)
            {
                case MenuSeparator:
                    InsertSeparator(menu);
                    break;
                case MenuHeader h:
                    AddString(menu, h.Text, 0, true, false, Icon(h.IconPath), default);
                    break;
                case MenuItem item:
                    var id = _nextId++;
                    _commands[id] = item;
                    AddString(menu, item.Text, id, !item.Enabled, item.IsDefault, Icon(item.IconPath), default);
                    break;
                case SubMenu sub:
                    AddSubMenu(menu, sub);
                    break;
            }
        }
    }

    private void AddSubMenu(HMENU parent, SubMenu sub)
    {
        var popup = PInvoke.CreatePopupMenu();
        if (popup.IsNull) return;

        if (sub.LazyChildren != null)
        {
            AddString(popup, "…", 0, disabled: true, isDefault: false, 0, default);
            _lazy[(nint)popup.Value] = sub;
        }
        else if (sub.Children.Count == 0)
        {
            AddString(popup, "(empty)", 0, disabled: true, isDefault: false, 0, default);
        }
        else
        {
            Populate(popup, sub.Children);
        }

        // On success the parent owns the popup and destroys it with the root.
        if (!AddString(parent, sub.Text, 0, false, false, Icon(sub.IconPath), popup))
        {
            _lazy.Remove((nint)popup.Value);
            PInvoke.DestroyMenu(popup);
        }
    }

    private nint Icon(string? path) => path is null ? 0 : _icons.GetBitmap(path);

    private static bool AddString(HMENU menu, string text, uint id, bool disabled, bool isDefault, nint bitmap, HMENU sub)
    {
        var mask = MENU_ITEM_MASK.MIIM_STRING | MENU_ITEM_MASK.MIIM_FTYPE | MENU_ITEM_MASK.MIIM_STATE | MENU_ITEM_MASK.MIIM_ID;
        var state = MENU_ITEM_STATE.MFS_ENABLED;
        if (disabled) state |= MENU_ITEM_STATE.MFS_DISABLED;
        if (isDefault) state |= MENU_ITEM_STATE.MFS_DEFAULT;
        if (bitmap != 0) mask |= MENU_ITEM_MASK.MIIM_BITMAP;
        if (!sub.IsNull) mask |= MENU_ITEM_MASK.MIIM_SUBMENU;

        fixed (char* p = text)
        {
            var mii = new MENUITEMINFOW
            {
                cbSize = (uint)sizeof(MENUITEMINFOW),
                fMask = mask,
                fType = MENU_ITEM_TYPE.MFT_STRING,
                fState = state,
                wID = id,
                hSubMenu = sub,
                hbmpItem = new Windows.Win32.Graphics.Gdi.HBITMAP(bitmap),
                dwTypeData = p,
            };
            return PInvoke.InsertMenuItem(menu, uint.MaxValue, true, &mii);
        }
    }

    private static void InsertSeparator(HMENU menu)
    {
        var mii = new MENUITEMINFOW
        {
            cbSize = (uint)sizeof(MENUITEMINFOW),
            fMask = MENU_ITEM_MASK.MIIM_FTYPE,
            fType = MENU_ITEM_TYPE.MFT_SEPARATOR,
        };
        PInvoke.InsertMenuItem(menu, uint.MaxValue, true, &mii);
    }

    // Drops leading, trailing and consecutive separators.
    private static List<MenuNode> Normalize(IReadOnlyList<MenuNode> nodes)
    {
        var result = new List<MenuNode>(nodes.Count);
        foreach (var n in nodes)
        {
            if (n is MenuSeparator && (result.Count == 0 || result[^1] is MenuSeparator)) continue;
            result.Add(n);
        }
        if (result.Count > 0 && result[^1] is MenuSeparator) result.RemoveAt(result.Count - 1);
        return result;
    }
}
