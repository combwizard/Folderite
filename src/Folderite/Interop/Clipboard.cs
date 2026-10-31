using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Memory;

namespace Folderite.Interop;

internal static unsafe class Clipboard
{
    private const uint CfUnicodeText = 13;

    public static string? GetText(HWND owner)
    {
        if (!PInvoke.IsClipboardFormatAvailable(CfUnicodeText) || !PInvoke.OpenClipboard(owner)) return null;
        try
        {
            HANDLE data = PInvoke.GetClipboardData(CfUnicodeText);
            if (data.IsNull) return null;
            void* p = PInvoke.GlobalLock((HGLOBAL)(nint)data);
            if (p is null) return null;
            try { return new string((char*)p); }
            finally { PInvoke.GlobalUnlock((HGLOBAL)(nint)data); }
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    /// <param name="transient">Mark the content so clipboard history, cloud clipboard and clipboard managers ignore it.</param>
    public static bool SetText(HWND owner, string text, bool transient = false)
    {
        if (!Open(owner)) return false;
        try
        {
            PInvoke.EmptyClipboard();
            var bytes = new byte[(text.Length + 1) * sizeof(char)];
            System.Text.Encoding.Unicode.GetBytes(text, bytes);
            if (!Put(CfUnicodeText, bytes)) return false;
            if (transient) MarkTransient();
            return true;
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    /// <summary>Copy of every memory-backed format on the clipboard (GDI handle formats are skipped; images survive as CF_DIB).</summary>
    public sealed record Snapshot(IReadOnlyList<(uint Format, byte[] Data)> Items);

    public static Snapshot? Save(HWND owner)
    {
        if (!Open(owner)) return null;
        try
        {
            var items = new List<(uint, byte[])>();
            for (uint format = PInvoke.EnumClipboardFormats(0); format != 0; format = PInvoke.EnumClipboardFormats(format))
            {
                if (IsHandleFormat(format)) continue;
                HANDLE data = PInvoke.GetClipboardData(format);
                if (data.IsNull) continue;
                var mem = (HGLOBAL)(nint)data;
                nuint size = PInvoke.GlobalSize(mem);
                if (size == 0 || size > 256u * 1024 * 1024) continue;
                void* p = PInvoke.GlobalLock(mem);
                if (p is null) continue;
                try { items.Add((format, new ReadOnlySpan<byte>(p, (int)size).ToArray())); }
                finally { PInvoke.GlobalUnlock(mem); }
            }
            return new Snapshot(items);
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    /// <summary>Puts a <see cref="Save"/>d clipboard back, marked transient so history doesn't record it a second time.</summary>
    public static bool Restore(HWND owner, Snapshot snapshot)
    {
        if (!Open(owner)) return false;
        try
        {
            PInvoke.EmptyClipboard();
            foreach (var (format, data) in snapshot.Items) Put(format, data);
            if (snapshot.Items.Count > 0) MarkTransient();
            return true;
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

    public static uint SequenceNumber => PInvoke.GetClipboardSequenceNumber();

    // Another app may hold the clipboard open for a moment.
    private static bool Open(HWND owner)
    {
        for (int i = 0; i < 10; i++)
        {
            if (PInvoke.OpenClipboard(owner)) return true;
            Thread.Sleep(10);
        }
        return false;
    }

    private static bool Put(uint format, ReadOnlySpan<byte> data)
    {
        HGLOBAL mem = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, (nuint)Math.Max(data.Length, 1));
        if (mem.IsNull) return false;
        void* dst = PInvoke.GlobalLock(mem);
        if (dst is null) { PInvoke.GlobalFree(mem); return false; }
        data.CopyTo(new Span<byte>(dst, data.Length));
        PInvoke.GlobalUnlock(mem);
        if (!PInvoke.SetClipboardData(format, (HANDLE)(nint)mem).IsNull) return true;
        PInvoke.GlobalFree(mem);
        return false;
    }

    private static void MarkTransient()
    {
        uint exclude = PInvoke.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
        if (exclude != 0) Put(exclude, new byte[4]);
    }

    // Formats whose data is a GDI/metafile handle, not an HGLOBAL.
    private static bool IsHandleFormat(uint format) => format switch
    {
        2 or 3 or 9 or 14 => true,                     // CF_BITMAP, CF_METAFILEPICT, CF_PALETTE, CF_ENHMETAFILE
        0x80 or 0x82 or 0x83 or 0x8E => true,          // CF_OWNERDISPLAY, CF_DSPBITMAP, CF_DSPMETAFILEPICT, CF_DSPENHMETAFILE
        >= 0x200 and <= 0x3FF => true,                 // CF_PRIVATEFIRST..CF_GDIOBJLAST
        _ => false,
    };

    /// <summary>The clipboard text if it is a single existing local folder path (quotes trimmed); UNC paths are not probed.</summary>
    public static string? GetFolderPath(HWND owner)
    {
        var text = GetText(owner)?.Trim().Trim('"');
        if (string.IsNullOrEmpty(text) || text.Length > 1024 || text.Contains('\n') || !Path.IsPathFullyQualified(text)) return null;
        if (text.StartsWith(@"\\", StringComparison.Ordinal)) return text;
        try { return Directory.Exists(text) ? text : null; }
        catch { return null; }
    }
}
