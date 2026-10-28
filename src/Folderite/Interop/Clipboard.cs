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

    public static bool SetText(HWND owner, string text)
    {
        if (!PInvoke.OpenClipboard(owner)) return false;
        try
        {
            PInvoke.EmptyClipboard();
            nuint bytes = (nuint)((text.Length + 1) * sizeof(char));
            HGLOBAL mem = PInvoke.GlobalAlloc(GLOBAL_ALLOC_FLAGS.GMEM_MOVEABLE, bytes);
            if (mem.IsNull) return false;
            char* dst = (char*)PInvoke.GlobalLock(mem);
            text.AsSpan().CopyTo(new Span<char>(dst, text.Length));
            dst[text.Length] = '\0';
            PInvoke.GlobalUnlock(mem);
            if (PInvoke.SetClipboardData(CfUnicodeText, (HANDLE)(nint)mem).IsNull)
            {
                PInvoke.GlobalFree(mem);
                return false;
            }
            return true;
        }
        finally
        {
            PInvoke.CloseClipboard();
        }
    }

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
