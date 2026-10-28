using System.Text;
using Windows.Win32.Foundation;

namespace Folderite.Targets.ThirdParty;

/// <summary>
/// Total Commander (class TTOTAL_CMD).
/// Read: WM_USER+50 (1074) with wParam 9/10 = HWND of the left/right path label, 1000 = active panel (1/2).
/// Write: WM_COPYDATA, dwData = 'C'+256*'D', payload = UTF-8 (with BOM) "left CR right", NUL, optional flags, NUL.
/// </summary>
internal sealed class TotalCommanderAdapter : ITargetAdapter
{
    private const string WindowClass = "TTOTAL_CMD";
    private const uint WM_USER_INFO = 0x0400 + 50; // 1074; TC 9+ info requests
    private const nuint InfoLeftPath = 9;
    private const nuint InfoRightPath = 10;
    private const nuint InfoActivePanel = 1000;
    private const nuint CopyDataCd = 'C' + 256 * 'D';

    public string Name => "Total Commander";

    public bool Matches(in TargetWindow window) => window.ClassName == WindowClass;

    public string? GetCurrentPath(in TargetWindow window)
    {
        try
        {
            int side = DetectSide(window);
            return side == 0 ? null : ReadPanelPath(window.Root, side);
        }
        catch { return null; }
    }

    public bool Navigate(in TargetWindow window, string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path)) return false;
            int side = DetectClickedSide(window);

            // Panel selection: left path, CR, right path. Each part is UTF-8 and carries its own BOM.
            // If we could not tell which panel was clicked, the "S" flag means "path 1 = source panel".
            string payload;
            string flags = "";
            switch (side)
            {
                case 2: payload = "\uFEFF\r\uFEFF" + path; break;
                case 1: payload = "\uFEFF" + path; break;
                default: payload = "\uFEFF" + path; flags = "S"; break;
            }

            var bytes = new List<byte>(Encoding.UTF8.GetBytes(payload)) { 0 };
            if (flags.Length > 0)
            {
                bytes.AddRange(Encoding.ASCII.GetBytes(flags));
                bytes.Add(0);
            }
            return Win32Util.SendCopyData(window.Root, CopyDataCd, bytes.ToArray());
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
                for (int side = 1; side <= 2; side++)
                {
                    string? p = ReadPanelPath(hwnd, side);
                    if (p != null) result.Add(p);
                }
            }
        }
        catch { }
        return result;
    }

    /// <summary>1 = left, 2 = right, 0 = unknown.</summary>
    private static int DetectSide(in TargetWindow window)
    {
        int side = DetectClickedSide(window);
        if (side != 0) return side;
        nint active = Win32Util.SendMessageTimeout(window.Root, WM_USER_INFO, InfoActivePanel, 0);
        return active is 1 or 2 ? (int)active : 1;
    }

    private static string? ReadPanelPath(nint root, int side)
    {
        nint label = Win32Util.SendMessageTimeout(root, WM_USER_INFO, side == 1 ? InfoLeftPath : InfoRightPath, 0);
        if (label == 0) return null;
        return ParsePathLabel(Win32Util.GetWindowText(label));
    }

    /// <summary>"C:\folder\*.*" -> "C:\folder"; also tolerates a trailing '&gt;' (command-line style label).</summary>
    internal static string? ParsePathLabel(string text)
    {
        text = text.Trim().TrimEnd('>').Trim();
        if (text.Length == 0) return null;

        int slash = text.LastIndexOf('\\');
        if (slash >= 0 && text.IndexOfAny(['*', '?'], slash) >= 0)
            text = text[..(slash + 1)];

        // Keep the trailing backslash only for roots ("C:\"), where it is required.
        if (text.Length > 3 && text.EndsWith('\\')) text = text.TrimEnd('\\');
        return Win32Util.ValidateDirectory(text);
    }

    /// <summary>
    /// Which panel the click landed in (1 left, 2 right) or 0 if unclear. The panel is the path label
    /// that horizontally overlaps the clicked control and sits closest above it (works for side-by-side and stacked layouts).
    /// </summary>
    private static int DetectClickedSide(in TargetWindow window)
    {
        try
        {
            if (window.Hit == 0 || !Win32Util.TryGetRect(window.Hit, out RECT hit)) return 0;
            int hitW = hit.right - hit.left;
            if (hitW <= 0) return 0;

            int best = 0;
            int bestBottom = int.MinValue;
            for (int side = 1; side <= 2; side++)
            {
                nint label = Win32Util.SendMessageTimeout(window.Root, WM_USER_INFO, side == 1 ? InfoLeftPath : InfoRightPath, 0);
                if (label == 0 || !Win32Util.TryGetRect(label, out RECT r)) continue;

                int overlap = Math.Min(r.right, hit.right) - Math.Max(r.left, hit.left);
                if (overlap * 2 < hitW) continue;     // not above/below this panel
                if (r.top > hit.top + 2) continue;    // label must not start below the clicked control
                if (r.bottom > bestBottom) { bestBottom = r.bottom; best = side; }
                else if (r.bottom == bestBottom) best = 0; // both fit equally: ambiguous
            }
            return best;
        }
        catch { return 0; }
    }
}
