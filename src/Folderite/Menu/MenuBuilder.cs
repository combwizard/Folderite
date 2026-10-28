using System.Drawing;
using System.Drawing.Drawing2D;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Controls;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Folderite.Menu;

/// <summary>Renders a <see cref="MenuNode"/> tree as a native Win32 popup menu with owner-drawn, theme-aware items.</summary>
internal sealed unsafe class MenuBuilder
{
    private readonly IconCache _icons;
    private readonly Dictionary<uint, MenuItem> _commands = new();
    private readonly Dictionary<nint, SubMenu> _lazy = new();
    private readonly List<Entry> _entries = new();
    // Per popup, the accelerator texts by position (null = not choosable), for WM_MENUCHAR.
    private readonly Dictionary<nint, List<string?>> _texts = new();
    // No animation: the fade glitches with owner-drawn items (the popup shows for a frame, vanishes, then fades in).
    private const uint TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100, TPM_NOANIMATION = 0x4000;
    private const uint MF_HILITE = 0x80, MNC_EXECUTE = 2, MNC_SELECT = 3;

    private uint _nextId;
    private MenuTheme? _theme;

    private enum Kind { Item, SubMenu, Header, Placeholder, Separator }

    private sealed record Entry(Kind Kind, string Text, string? Detail, string? IconPath, bool Disabled, bool IsDefault);

    public MenuBuilder(IconCache icons) => _icons = icons;

    public static string EscapeAmpersands(string text) => text.Replace("&", "&&");

    /// <summary>Shows the menu modally at screen point (x, y). Runs the chosen item's action after the menu is destroyed.</summary>
    public bool Show(IReadOnlyList<MenuNode> nodes, nint owner, int x, int y)
    {
        // A trigger while a menu is up is dispatched inside its modal loop; nesting would wipe the open menu's
        // state. Treat it as a toggle instead.
        if (_theme is not null)
        {
            PInvoke.EndMenu();
            return false;
        }

        _icons.TrimIfLarge();
        Reset();
        _nextId = 1;

        var root = PInvoke.CreatePopupMenu();
        if (root.IsNull) return false;

        MenuItem? chosen = null;
        _theme = MenuTheme.Create(MenuTheme.DpiAt(x, y));
        try
        {
            Populate(root, nodes);
            SetBackground(root);

            var hwnd = new HWND(owner);
            ForceForeground(hwnd);
            uint cmd;
            using (NoMenuAnimation.Begin())
                cmd = (uint)PInvoke.TrackPopupMenuEx(root,
                    TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NOANIMATION,
                    x, y, hwnd, null).Value;
            PInvoke.PostMessage(hwnd, PInvoke.WM_NULL, 0, 0);

            if (cmd != 0) _commands.TryGetValue(cmd, out chosen);
        }
        finally
        {
            PInvoke.DestroyMenu(root);
            Reset();
            _theme.Dispose();
            _theme = null;
        }

        if (chosen is null) return false;
        chosen.OnInvoke();
        return true;
    }

    // TPM_NOANIMATION only covers the root popup; submenus still fade, and the fade glitches with owner-drawn
    // items. So menu animation is switched off for the session while the menu is up. fWinIni = 0: nothing is
    // persisted, so a crash mid-menu can't outlive the logon session.
    private readonly struct NoMenuAnimation : IDisposable
    {
        private const uint SPI_GETMENUANIMATION = 0x1002, SPI_SETMENUANIMATION = 0x1003;
        private readonly bool _restore;

        private NoMenuAnimation(bool restore) => _restore = restore;

        public static NoMenuAnimation Begin()
        {
            int on = 0;
            if (!SystemParametersInfoW(SPI_GETMENUANIMATION, 0, (nint)(&on), 0) || on == 0) return default;
            return new NoMenuAnimation(SystemParametersInfoW(SPI_SETMENUANIMATION, 0, 0, 0));
        }

        public void Dispose()
        {
            if (_restore) SystemParametersInfoW(SPI_SETMENUANIMATION, 0, 1, 0);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", ExactSpelling = true)]
        private static extern bool SystemParametersInfoW(uint action, uint param, nint pv, uint winIni);
    }

    // TrackPopupMenu only dismisses on an outside click when its owner is foreground. The hook swallows the
    // middle click, so Windows doesn't treat us as the last input's receiver and refuses SetForegroundWindow;
    // attaching to the foreground thread's input queue lifts that restriction for the call.
    private static void ForceForeground(HWND hwnd)
    {
        var fg = PInvoke.GetForegroundWindow();
        if (fg == hwnd) return;

        uint self = PInvoke.GetCurrentThreadId();
        uint other = fg.IsNull ? 0 : PInvoke.GetWindowThreadProcessId(fg, null);
        bool attached = other != 0 && other != self && PInvoke.AttachThreadInput(self, other, true);
        try
        {
            if (!PInvoke.SetForegroundWindow(hwnd))
                Log.Info("Menu owner could not take the foreground; outside clicks may not dismiss the menu.");
        }
        finally
        {
            if (attached) PInvoke.AttachThreadInput(self, other, false);
        }
    }

    private void Reset()
    {
        _commands.Clear();
        _lazy.Clear();
        _entries.Clear();
        _texts.Clear();
    }

    /// <summary>Handles the owner messages of a showing menu. Returns true if the message was handled.</summary>
    public bool HandleMessage(nint hwnd, uint msg, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (_theme is null) return false;
        switch (msg)
        {
            case PInvoke.WM_INITMENUPOPUP:
                return FillLazy((nint)wParam);
            case PInvoke.WM_MEASUREITEM:
                var mis = (MEASUREITEMSTRUCT*)lParam;
                if (mis->CtlType != DRAWITEMSTRUCT_CTL_TYPE.ODT_MENU || !TryEntry(mis->itemData, out var me)) return false;
                Measure(me, out mis->itemWidth, out mis->itemHeight);
                result = 1;
                return true;
            case PInvoke.WM_DRAWITEM:
                var dis = (DRAWITEMSTRUCT*)lParam;
                if (dis->CtlType != DRAWITEMSTRUCT_CTL_TYPE.ODT_MENU || !TryEntry(dis->itemData, out var de)) return false;
                Draw(de, dis);
                result = 1;
                return true;
            case PInvoke.WM_MENUCHAR:
                if (!_texts.TryGetValue(lParam, out var texts)) return false;
                result = MenuChar(new HMENU(lParam), texts, (char)(ushort)wParam);
                return true;
        }
        return false;
    }

    private bool TryEntry(nuint data, out Entry entry)
    {
        // Item data is index + 1, so 0 (any foreign owner-drawn item) never matches.
        var i = (int)data - 1;
        entry = i >= 0 && i < _entries.Count ? _entries[i] : null!;
        return entry is not null;
    }

    private bool FillLazy(nint hmenu)
    {
        if (!_lazy.Remove(hmenu, out var sub)) return false;

        var menu = new HMENU(hmenu);
        for (var n = PInvoke.GetMenuItemCount(menu); n > 0; n--)
            PInvoke.DeleteMenu(menu, 0, MENU_ITEM_FLAGS.MF_BYPOSITION);
        _texts.Remove(hmenu);

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
            AddPlaceholder(menu, "(empty)");
        else
            Populate(menu, children);
        return true;
    }

    private static nint MenuChar(HMENU menu, List<string?> texts, char key)
    {
        var current = -1;
        for (var i = 0; i < texts.Count; i++)
            if ((PInvoke.GetMenuState(menu, (uint)i, MENU_ITEM_FLAGS.MF_BYPOSITION) & MF_HILITE) != 0)
            {
                current = i;
                break;
            }

        var (index, unique) = Accelerators.Find(texts, key, current);
        if (index < 0) return 0; // MNC_IGNORE
        return (nint)(((unique ? MNC_EXECUTE : MNC_SELECT) << 16) | (uint)index);
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
                    Add(menu, new Entry(Kind.Header, h.Text, null, h.IconPath, true, false), 0, default);
                    break;
                case MenuItem item:
                    var id = _nextId++;
                    _commands[id] = item;
                    Add(menu, new Entry(Kind.Item, item.Text, item.Detail, item.IconPath, !item.Enabled, item.IsDefault), id, default);
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
            AddPlaceholder(popup, "…");
            _lazy[(nint)popup.Value] = sub;
        }
        else if (sub.Children.Count == 0)
        {
            AddPlaceholder(popup, "(empty)");
        }
        else
        {
            Populate(popup, sub.Children);
        }

        // On success the parent owns the popup and destroys it with the root.
        if (!Add(parent, new Entry(Kind.SubMenu, sub.Text, sub.Detail, sub.IconPath, false, false), 0, popup))
        {
            _lazy.Remove((nint)popup.Value);
            _texts.Remove((nint)popup.Value);
            PInvoke.DestroyMenu(popup);
        }
    }

    private void AddPlaceholder(HMENU menu, string text) =>
        Add(menu, new Entry(Kind.Placeholder, text, null, null, true, false), 0, default);

    private void InsertSeparator(HMENU menu) =>
        Add(menu, new Entry(Kind.Separator, "", null, null, true, false), 0, default);

    // Owner-drawn, but the text is still stored so accessibility tools can read it.
    private bool Add(HMENU menu, Entry entry, uint id, HMENU sub)
    {
        _entries.Add(entry);
        var data = (nuint)_entries.Count;

        var mask = MENU_ITEM_MASK.MIIM_FTYPE | MENU_ITEM_MASK.MIIM_DATA;
        var type = MENU_ITEM_TYPE.MFT_OWNERDRAW;
        if (entry.Kind == Kind.Separator)
        {
            type |= MENU_ITEM_TYPE.MFT_SEPARATOR;
        }
        else
        {
            mask |= MENU_ITEM_MASK.MIIM_STRING | MENU_ITEM_MASK.MIIM_STATE | MENU_ITEM_MASK.MIIM_ID;
        }
        if (!sub.IsNull) mask |= MENU_ITEM_MASK.MIIM_SUBMENU;

        var state = MENU_ITEM_STATE.MFS_ENABLED;
        if (entry.Disabled) state |= MENU_ITEM_STATE.MFS_DISABLED;
        if (entry.IsDefault) state |= MENU_ITEM_STATE.MFS_DEFAULT;

        bool ok;
        fixed (char* p = entry.Text)
        {
            var mii = new MENUITEMINFOW
            {
                cbSize = (uint)sizeof(MENUITEMINFOW),
                fMask = mask,
                fType = type,
                fState = state,
                wID = id,
                hSubMenu = sub,
                dwItemData = data,
                dwTypeData = p,
            };
            ok = PInvoke.InsertMenuItem(menu, uint.MaxValue, true, &mii);
        }

        if (ok)
        {
            if (!_texts.TryGetValue((nint)menu.Value, out var texts))
                _texts[(nint)menu.Value] = texts = new List<string?>();
            texts.Add(entry.Kind is Kind.Item or Kind.SubMenu && !entry.Disabled ? entry.Text : null);
        }
        return ok;
    }

    private void SetBackground(HMENU root)
    {
        var mi = new MENUINFO
        {
            cbSize = (uint)sizeof(MENUINFO),
            fMask = MENUINFO_MASK.MIM_BACKGROUND | MENUINFO_MASK.MIM_APPLYTOSUBMENUS,
            hbrBack = _theme!.BackgroundBrush,
        };
        PInvoke.SetMenuInfo(root, &mi);
    }

    // ---- Measuring and painting ----

    private void Measure(Entry e, out uint width, out uint height)
    {
        var t = _theme!;
        switch (e.Kind)
        {
            case Kind.Separator:
                width = 0;
                height = (uint)t.SeparatorHeight;
                return;
            case Kind.Header:
                height = (uint)t.HeaderHeight;
                break;
            default:
                height = (uint)t.RowHeight;
                break;
        }

        var dc = PInvoke.CreateCompatibleDC(default);
        try
        {
            int w = t.Inset + t.Padding;
            if (e.Kind == Kind.Header)
            {
                w += TextWidth(dc, t.HeaderFont, e.Text, DRAW_TEXT_FORMAT.DT_HIDEPREFIX);
            }
            else
            {
                w += t.IconSize + t.IconGap;
                w += TextWidth(dc, e.IsDefault ? t.BoldFont : t.Font, e.Text, 0);
                if (!string.IsNullOrEmpty(e.Detail))
                    w += t.ColumnGap + Math.Min(t.MaxDetailWidth, TextWidth(dc, t.SmallFont, e.Detail, DRAW_TEXT_FORMAT.DT_NOPREFIX));
                if (e.Kind == Kind.SubMenu)
                    w += t.ChevronWidth;
            }
            w += t.Padding + t.Inset;
            width = (uint)w;
        }
        finally
        {
            PInvoke.DeleteDC(dc);
        }
    }

    // Paints into an off-screen bitmap and blits it once, so hover changes don't flicker.
    private void Draw(Entry e, DRAWITEMSTRUCT* dis)
    {
        var target = dis->hDC;
        var rc = dis->rcItem;
        int w = rc.right - rc.left, h = rc.bottom - rc.top;
        bool selected = (dis->itemState & ODS_FLAGS.ODS_SELECTED) != 0;
        bool noAccel = (dis->itemState & ODS_FLAGS.ODS_NOACCEL) != 0;

        var mem = PInvoke.CreateCompatibleDC(target);
        var bmp = PInvoke.CreateCompatibleBitmap(target, w, h);
        if (mem.IsNull || bmp.IsNull)
        {
            if (!bmp.IsNull) PInvoke.DeleteObject(bmp);
            if (!mem.IsNull) PInvoke.DeleteDC(mem);
            Paint(e, target, rc, selected, noAccel);
        }
        else
        {
            var old = PInvoke.SelectObject(mem, bmp);
            Paint(e, mem, new RECT { right = w, bottom = h }, selected, noAccel);
            PInvoke.BitBlt(target, rc.left, rc.top, w, h, mem, 0, 0, ROP_CODE.SRCCOPY);
            PInvoke.SelectObject(mem, old);
            PInvoke.DeleteObject(bmp);
            PInvoke.DeleteDC(mem);
        }

        // The system paints its own (light-theme) submenu arrow after WM_DRAWITEM; clip it away.
        if (e.Kind == Kind.SubMenu)
            PInvoke.ExcludeClipRect(target, rc.left, rc.top, rc.right, rc.bottom);
    }

    private void Paint(Entry e, HDC dc, RECT rc, bool selected, bool noAccel)
    {
        var t = _theme!;
        PInvoke.FillRect(dc, &rc, t.BackgroundBrush);

        int left = rc.left + t.Inset + t.Padding;
        int right = rc.right - t.Inset - t.Padding;

        if (e.Kind == Kind.Separator)
        {
            int mid = (rc.top + rc.bottom) / 2;
            var line = new RECT { left = rc.left + t.Inset, right = rc.right - t.Inset, top = mid, bottom = mid + Math.Max(1, t.Scale(1)) };
            var brush = PInvoke.CreateSolidBrush(t.Separator);
            PInvoke.FillRect(dc, &line, brush);
            PInvoke.DeleteObject(brush);
            return;
        }

        PInvoke.SetBkMode(dc, BACKGROUND_MODE.TRANSPARENT);

        if (e.Kind == Kind.Header)
        {
            var hr = rc with { left = left, right = right };
            DrawString(dc, t.HeaderFont, t.HeaderText, e.Text, hr, DRAW_TEXT_FORMAT.DT_HIDEPREFIX | DRAW_TEXT_FORMAT.DT_END_ELLIPSIS);
            return;
        }

        if (selected && !e.Disabled) FillPill(dc, rc, t);

        if (e.IconPath is not null && _icons.GetIcon(e.IconPath, t.IconSize) is var icon and not 0)
        {
            int iy = rc.top + (rc.bottom - rc.top - t.IconSize) / 2;
            PInvoke.DrawIconEx(dc, left, iy, new HICON(icon), t.IconSize, t.IconSize, 0, default, DI_FLAGS.DI_NORMAL);
        }
        int textLeft = left + t.IconSize + t.IconGap;

        if (e.Kind == Kind.SubMenu)
        {
            var cr = rc with { left = right - t.ChevronWidth, right = right };
            DrawString(dc, t.GlyphFont, t.Glyph, MenuTheme.ChevronRight.ToString(), cr,
                DRAW_TEXT_FORMAT.DT_CENTER | DRAW_TEXT_FORMAT.DT_NOPREFIX);
            right -= t.ChevronWidth;
        }

        var text = e.Disabled ? t.DisabledText : t.Text;
        if (!string.IsNullOrEmpty(e.Detail))
        {
            int dw = Math.Min(t.MaxDetailWidth, TextWidth(dc, t.SmallFont, e.Detail, DRAW_TEXT_FORMAT.DT_NOPREFIX));
            var dr = rc with { left = right - dw, right = right };
            DrawString(dc, t.SmallFont, e.Disabled ? t.DisabledText : t.DimText, e.Detail, dr,
                DRAW_TEXT_FORMAT.DT_RIGHT | DRAW_TEXT_FORMAT.DT_NOPREFIX | DRAW_TEXT_FORMAT.DT_PATH_ELLIPSIS);
            right -= dw + t.ColumnGap;
        }

        var nr = rc with { left = textLeft, right = right };
        var flags = DRAW_TEXT_FORMAT.DT_END_ELLIPSIS;
        if (noAccel) flags |= DRAW_TEXT_FORMAT.DT_HIDEPREFIX;
        DrawString(dc, e.IsDefault ? t.BoldFont : t.Font, text, e.Text, nr, flags);
    }

    private static void FillPill(HDC dc, RECT rc, MenuTheme t)
    {
        int vgap = t.Scale(2);
        var r = new RectangleF(rc.left + t.Inset, rc.top + vgap, rc.right - rc.left - 2 * t.Inset, rc.bottom - rc.top - 2 * vgap);
        float d = 2 * t.HoverRadius;
        using var g = Graphics.FromHdc((nint)dc.Value);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        using var path = new GraphicsPath();
        path.AddRoundedRectangle(r, new SizeF(d, d));
        using var brush = new SolidBrush(t.HoverFill);
        g.FillPath(brush, path);
    }

    private static void DrawString(HDC dc, HFONT font, COLORREF color, string s, RECT rc, DRAW_TEXT_FORMAT flags)
    {
        var old = PInvoke.SelectObject(dc, font);
        PInvoke.SetTextColor(dc, color);
        fixed (char* p = s)
            PInvoke.DrawText(dc, p, s.Length, &rc, flags | DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_VCENTER);
        PInvoke.SelectObject(dc, old);
    }

    private static int TextWidth(HDC dc, HFONT font, string s, DRAW_TEXT_FORMAT flags)
    {
        var old = PInvoke.SelectObject(dc, font);
        var rc = new RECT();
        fixed (char* p = s)
            PInvoke.DrawText(dc, p, s.Length, &rc, flags | DRAW_TEXT_FORMAT.DT_SINGLELINE | DRAW_TEXT_FORMAT.DT_CALCRECT);
        PInvoke.SelectObject(dc, old);
        return rc.right - rc.left;
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
