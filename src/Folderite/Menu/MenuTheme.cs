using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.WindowsAndMessaging;

#pragma warning disable CA1416 // Per-monitor DPI APIs need Windows 10 1607+; the app manifest already requires it.

namespace Folderite.Menu;

/// <summary>Colours, fonts and metrics for the owner-drawn menu at one DPI. Owns its GDI fonts and brush.</summary>
internal sealed unsafe class MenuTheme : IDisposable
{
    public const char ChevronRight = '';

    private const string FluentFace = "Segoe Fluent Icons";
    private const string Mdl2Face = "Segoe MDL2 Assets";
    private const uint SPI_GETNONCLIENTMETRICS = 0x29;
    private const byte CLEARTYPE_QUALITY = 5;
    private const uint DefaultAccent = 0x0067C0;

    private bool _disposed;

    private MenuTheme(int dpi)
    {
        Dpi = dpi;
        IsDark = IsAppsDark();

        if (IsDark)
        {
            Background = Rgb(0x2B2B2B);
            Text = Rgb(0xFFFFFF);
            DimText = Rgb(0xA6A6A6);
            DisabledText = Rgb(0x6E6E6E);
            Separator = Rgb(0x3F3F3F);
            Glyph = Rgb(0xC8C8C8);
            HoverFill = Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF);
        }
        else
        {
            Background = Rgb(0xF9F9F9);
            Text = Rgb(0x1A1A1A);
            DimText = Rgb(0x6B6B6B);
            DisabledText = Rgb(0xA0A0A0);
            Separator = Rgb(0xE0E0E0);
            Glyph = Rgb(0x5A5A5A);
            HoverFill = Color.FromArgb(0x0F, 0, 0, 0);
        }
        HeaderText = Rgb(MixAccent(GetAccent(), IsDark));

        RowHeight = Scale(32);
        HeaderHeight = Scale(26);
        SeparatorHeight = Scale(9);
        Inset = Scale(4);
        HoverRadius = Scale(4);
        Padding = Scale(10);
        IconGap = Scale(10);
        ColumnGap = Scale(24);
        ChevronWidth = Scale(20);
        MaxDetailWidth = Scale(200);
        int icon = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, (uint)dpi);
        IconSize = icon > 0 ? icon : Scale(16);

        BackgroundBrush = PInvoke.CreateSolidBrush(Background);

        var lf = GetMenuLogFont(dpi);
        int height = lf.lfHeight < 0 ? -lf.lfHeight : lf.lfHeight;
        if (height == 0) height = Scale(12);

        Font = CreateFont(lf, height, 400);
        BoldFont = CreateFont(lf, height, 600);
        int smallHeight = Math.Max(1, (int)Math.Round(height * 0.85));
        SmallFont = CreateFont(lf, smallHeight, 400);
        HeaderFont = CreateFont(lf, smallHeight, 600);
        GlyphFont = CreateGlyphFont(smallHeight);
    }

    public static int DpiAt(int x, int y)
    {
        try
        {
            var mon = PInvoke.MonitorFromPoint(new System.Drawing.Point(x, y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
            if (PInvoke.GetDpiForMonitor(mon, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _).Failed || dpiX == 0)
                return 96;
            return (int)dpiX;
        }
        catch
        {
            return 96;
        }
    }

    public static MenuTheme Create(int dpi) => new(dpi > 0 ? dpi : 96);

    public int Dpi { get; }
    public bool IsDark { get; }

    public int Scale(int px) => (int)Math.Round(px * (double)Dpi / 96, MidpointRounding.AwayFromZero);

    public COLORREF Background { get; }
    public COLORREF Text { get; }
    public COLORREF DimText { get; }
    public COLORREF DisabledText { get; }
    public COLORREF HeaderText { get; }
    public COLORREF Separator { get; }
    public COLORREF Glyph { get; }
    public Color HoverFill { get; }

    public HBRUSH BackgroundBrush { get; }

    public HFONT Font { get; }
    public HFONT BoldFont { get; }
    public HFONT SmallFont { get; }
    public HFONT HeaderFont { get; }
    public HFONT GlyphFont { get; }

    public int RowHeight { get; }
    public int HeaderHeight { get; }
    public int SeparatorHeight { get; }
    public int Inset { get; }
    public int HoverRadius { get; }
    public int Padding { get; }
    public int IconSize { get; }
    public int IconGap { get; }
    public int ColumnGap { get; }
    public int ChevronWidth { get; }
    public int MaxDetailWidth { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var font in new[] { Font, BoldFont, SmallFont, HeaderFont, GlyphFont })
            if (!font.IsNull) PInvoke.DeleteObject(font);
        if (!BackgroundBrush.IsNull) PInvoke.DeleteObject(BackgroundBrush);
    }

    private static COLORREF Rgb(uint rgb) =>
        new(((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF));

    private static bool IsAppsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    // Returns 0xRRGGBB.
    private static uint GetAccent()
    {
        try
        {
            if (PInvoke.DwmGetColorizationColor(out var argb, out _).Succeeded) return argb & 0xFFFFFF;
        }
        catch
        {
            // Fall through to the default.
        }
        return DefaultAccent;
    }

    // Light: 35% toward black. Dark: 45% toward white.
    private static uint MixAccent(uint rgb, bool dark)
    {
        uint Mix(uint c) => dark
            ? (uint)Math.Round(c + (255 - c) * 0.45)
            : (uint)Math.Round(c * 0.65);
        return (Mix((rgb >> 16) & 0xFF) << 16) | (Mix((rgb >> 8) & 0xFF) << 8) | Mix(rgb & 0xFF);
    }

    private static LOGFONTW GetMenuLogFont(int dpi)
    {
        var ncm = new NONCLIENTMETRICSW { cbSize = (uint)sizeof(NONCLIENTMETRICSW) };
        try
        {
            if (PInvoke.SystemParametersInfoForDpi(SPI_GETNONCLIENTMETRICS, ncm.cbSize, &ncm, 0, (uint)dpi))
                return ncm.lfMenuFont;
        }
        catch
        {
            // Fall through to the fallback.
        }

        var lf = new LOGFONTW { lfHeight = -(int)Math.Round(9.0 * dpi / 72), lfWeight = 400 };
        SetFace(ref lf, "Segoe UI");
        return lf;
    }

    private static void SetFace(ref LOGFONTW lf, string face)
    {
        var span = lf.lfFaceName.AsSpan();
        span.Clear();
        face.AsSpan(0, Math.Min(face.Length, span.Length - 1)).CopyTo(span);
    }

    private static HFONT CreateFont(LOGFONTW lf, int height, int weight)
    {
        lf.lfHeight = -height;
        lf.lfWeight = weight;
        lf.lfQuality = (FONT_QUALITY)CLEARTYPE_QUALITY;
        return PInvoke.CreateFontIndirect(&lf);
    }

    private static HFONT CreateGlyphFont(int height)
    {
        var lf = new LOGFONTW
        {
            lfHeight = -height,
            lfWeight = 400,
            lfCharSet = FONT_CHARSET.DEFAULT_CHARSET,
            lfQuality = (FONT_QUALITY)CLEARTYPE_QUALITY,
        };
        SetFace(ref lf, FluentFace);
        var font = PInvoke.CreateFontIndirect(&lf);
        if (font.IsNull || FaceName(font) == FluentFace) return font;

        PInvoke.DeleteObject(font);
        SetFace(ref lf, Mdl2Face);
        return PInvoke.CreateFontIndirect(&lf);
    }

    private static string FaceName(HFONT font)
    {
        var dc = PInvoke.CreateCompatibleDC(default);
        if (dc.IsNull) return "";
        try
        {
            var old = PInvoke.SelectObject(dc, font);
            var buf = stackalloc char[64];
            int n = GetTextFaceW(dc, 64, buf);
            PInvoke.SelectObject(dc, old);
            return n > 0 ? new string(buf) : "";
        }
        finally
        {
            PInvoke.DeleteDC(dc);
        }
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetTextFaceW(HDC hdc, int c, char* name);
}
