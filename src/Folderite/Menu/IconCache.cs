using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Folderite.Menu;

/// <summary>Shell icons as HICONs for the owner-drawn menu, cached per path and size.</summary>
internal sealed class IconCache : IDisposable
{
    private const int MaxEntries = 512;

    private readonly Dictionary<(string Path, int Size), nint> _icons = new(IconKeyComparer.Instance);

    /// <summary>Returns an HICON (owned by the cache) to be drawn at <paramref name="size"/> px, or 0 if no icon could be obtained.</summary>
    public nint GetIcon(string path, int size)
    {
        if (string.IsNullOrEmpty(path)) return 0;
        var key = (path, size);
        if (_icons.TryGetValue(key, out var cached)) return cached;
        var icon = FetchIcon(path, size > PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSMICON));
        _icons[key] = icon;
        return icon;
    }

    /// <summary>Clears the cache if it has grown too large. Call only while no menu is showing.</summary>
    public void TrimIfLarge()
    {
        if (_icons.Count > MaxEntries) Clear();
    }

    public void Clear()
    {
        foreach (var icon in _icons.Values)
            if (icon != 0) PInvoke.DestroyIcon(new HICON(icon));
        _icons.Clear();
    }

    public void Dispose() => Clear();

    private static unsafe nint FetchIcon(string path, bool large)
    {
        uint flags = SHGFI_ICON | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON);
        uint attrs = 0;
        if (!IsSafeToTouch(path))
        {
            flags |= SHGFI_USEFILEATTRIBUTES;
            attrs = FILE_ATTRIBUTE_DIRECTORY;
        }

        var info = new ShFileInfo();
        var r = SHGetFileInfoW(path, attrs, ref info, (uint)sizeof(ShFileInfo), flags);
        return r == 0 ? 0 : info.hIcon;
    }

    private sealed class IconKeyComparer : IEqualityComparer<(string Path, int Size)>
    {
        public static readonly IconKeyComparer Instance = new();

        public bool Equals((string Path, int Size) a, (string Path, int Size) b) =>
            a.Size == b.Size && string.Equals(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Path, int Size) k) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(k.Path), k.Size);
    }

    // Declared by hand: CsWin32 only emits SHGetFileInfo for a specific PlatformTarget, and the project builds AnyCPU.
    private const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_LARGEICON = 0x0, SHGFI_USEFILEATTRIBUTES = 0x10;
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
}
