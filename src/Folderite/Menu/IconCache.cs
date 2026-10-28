using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Folderite.Menu;

/// <summary>Shell small icons as 32bpp premultiplied-alpha DIB sections, suitable for MENUITEMINFO.hbmpItem.</summary>
internal sealed class IconCache : IDisposable
{
    private const int MaxEntries = 512;

    private readonly Dictionary<string, nint> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns an HBITMAP (owned by the cache), or 0 if no icon could be obtained.</summary>
    public nint GetBitmap(string path)
    {
        if (string.IsNullOrEmpty(path)) return 0;
        if (_cache.TryGetValue(path, out var cached)) return cached;
        var bmp = Create(path);
        _cache[path] = bmp;
        return bmp;
    }

    /// <summary>Clears the cache if it has grown too large. Call only while no menu is showing.</summary>
    public void TrimIfLarge()
    {
        if (_cache.Count > MaxEntries) Clear();
    }

    public void Clear()
    {
        foreach (var bmp in _cache.Values)
            if (bmp != 0) PInvoke.DeleteObject(new HGDIOBJ(bmp));
        _cache.Clear();
    }

    public void Dispose() => Clear();

    private static unsafe nint Create(string path)
    {
        uint flags = SHGFI_ICON | SHGFI_SMALLICON;
        uint attrs = 0;
        if (!IsSafeToTouch(path))
        {
            flags |= SHGFI_USEFILEATTRIBUTES;
            attrs = FILE_ATTRIBUTE_DIRECTORY;
        }

        var info = new ShFileInfo();
        var r = SHGetFileInfoW(path, attrs, ref info, (uint)sizeof(ShFileInfo), flags);
        if (r == 0 || info.hIcon == 0) return 0;

        var icon = new HICON(info.hIcon);
        try
        {
            return IconToBitmap(icon);
        }
        finally
        {
            PInvoke.DestroyIcon(icon);
        }
    }

    // Declared by hand: CsWin32 only emits SHGetFileInfo for a specific PlatformTarget, and the project builds AnyCPU.
    private const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct ShFileInfo
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        public fixed char szDisplayName[260];
        public fixed char szTypeName[80];
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint SHGetFileInfoW(string path, uint attrs, ref ShFileInfo info, uint cbInfo, uint flags);

    // Only local fixed/RAM drives are queried for real; UNC, network, removable and missing drives use attributes only.
    private static bool IsSafeToTouch(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        if (path.Length < 2 || path[1] != ':') return false;
        var type = PInvoke.GetDriveType(path[..2] + @"\");
        return type is 3 or 6; // DRIVE_FIXED, DRIVE_RAMDISK
    }

    private static unsafe nint IconToBitmap(HICON icon)
    {
        int size = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON);
        if (size <= 0) size = 16;

        var bmi = new BITMAPINFO();
        bmi.bmiHeader.biSize = (uint)sizeof(BITMAPINFOHEADER);
        bmi.bmiHeader.biWidth = size;
        bmi.bmiHeader.biHeight = -size; // top-down
        bmi.bmiHeader.biPlanes = 1;
        bmi.bmiHeader.biBitCount = 32;
        bmi.bmiHeader.biCompression = 0; // BI_RGB

        void* bits;
        var hbmp = PInvoke.CreateDIBSection(default, &bmi, DIB_USAGE.DIB_RGB_COLORS, &bits, default, 0);
        if (hbmp.IsNull) return 0;

        var dc = PInvoke.CreateCompatibleDC(default);
        if (dc.IsNull)
        {
            PInvoke.DeleteObject(hbmp);
            return 0;
        }

        var old = PInvoke.SelectObject(dc, hbmp);
        var ok = PInvoke.DrawIconEx(dc, 0, 0, icon, size, size, 0, default, DI_FLAGS.DI_NORMAL);
        PInvoke.SelectObject(dc, old);
        PInvoke.DeleteDC(dc);

        if (!ok)
        {
            PInvoke.DeleteObject(hbmp);
            return 0;
        }
        return (nint)hbmp.Value;
    }
}
