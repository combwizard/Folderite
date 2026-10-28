using System.Text;

namespace Folderite.Targets.ThirdParty;

/// <summary>
/// XYplorer (class ThunderRT6FormDC, process xyplorer).
/// Write: WM_COPYDATA with dwData 0x00400001 and an inline UTF-16 script ("::" prefix), not NUL-terminated.
/// Read: the default title bar template is "&lt;path&gt; - &lt;app&gt; &lt;ver&gt;", so we parse the title.
/// </summary>
internal sealed class XYplorerAdapter : ITargetAdapter
{
    private const string WindowClass = "ThunderRT6FormDC";
    private const string ProcessName = "xyplorer";
    private const nuint RemoteControlId = 0x00400001; // 4194305

    public string Name => "XYplorer";

    public bool Matches(in TargetWindow window) =>
        window.ClassName == WindowClass && window.ProcessName == ProcessName;

    public string? GetCurrentPath(in TargetWindow window)
    {
        try { return ParseTitle(Win32Util.GetWindowText(window.Root)); }
        catch { return null; }
    }

    public bool Navigate(in TargetWindow window, string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path)) return false;
            // Single quotes are literal in XYplorer scripts (no variable/escape resolution); double a quote inside.
            string script = "::goto '" + path.Replace("'", "''") + "';";
            byte[] data = Encoding.Unicode.GetBytes(script);
            return Win32Util.SendCopyData(window.Root, RemoteControlId, data);
        }
        catch { return false; }
    }

    public IEnumerable<string> EnumerateOpenPaths()
    {
        var result = new List<string>();
        try
        {
            foreach (nint hwnd in Win32Util.FindTopLevelWindows(WindowClass))
            {
                string? exe = Win32Util.GetProcessImagePath(Win32Util.GetWindowProcessId(hwnd));
                if (!string.Equals(Path.GetFileNameWithoutExtension(exe), ProcessName, StringComparison.OrdinalIgnoreCase))
                    continue;
                string? p = ParseTitle(Win32Util.GetWindowText(hwnd));
                if (p != null) result.Add(p);
            }
        }
        catch { }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// "C:\Users\me - XYplorer 25.00" -> "C:\Users\me". Paths may themselves contain " - ",
    /// so try the longest prefix first and shorten at each separator until a real directory is found.
    /// </summary>
    internal static string? ParseTitle(string title)
    {
        const string Sep = " - ";
        int end = title.Length;
        while (end > 0)
        {
            int idx = title.LastIndexOf(Sep, end - 1, StringComparison.Ordinal);
            if (idx <= 0) break;
            string candidate = title[..idx].Trim();
            if (candidate.Length > 3) candidate = candidate.TrimEnd('\\');
            string? valid = Win32Util.ValidateDirectory(candidate);
            if (valid != null) return valid;
            end = idx;
        }
        return null;
    }
}
