using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;

namespace Folderite.Targets.ThirdParty;

/// <summary>
/// Directory Opus (class dopus.lister), driven through dopusrt.exe.
/// Read: <c>dopusrt /info file,paths</c> writes UTF-8 XML, one &lt;path&gt; per tab with attributes
/// lister (HWND of the lister), tab (HWND of the tab), side (1/2), active_tab (set on the active tab of each side),
/// tab_state (1 = source, 2 = destination, on active tabs only).
/// Write: <c>dopusrt /acmd Go "path"</c> goes to the most recently active lister's source tab;
/// when the clicked tab is the destination one we first run <c>Set FOCUS=left|right</c>.
/// </summary>
internal sealed class DopusAdapter : ITargetAdapter
{
    private const string WindowClass = "dopus.lister";
    private const string FallbackExe = @"C:\Program Files\GPSoftware\Directory Opus\dopusrt.exe";
    private const int ProcessTimeoutMs = 2000;

    private string? _rtPath;

    public string Name => "Directory Opus";

    public bool Matches(in TargetWindow window) => window.ClassName == WindowClass;

    public string? GetCurrentPath(in TargetWindow window)
    {
        try
        {
            var entry = FindEntry(QueryPaths(window.ProcessId), window);
            return entry?.Path;
        }
        catch { return null; }
    }

    public bool Navigate(in TargetWindow window, string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path)) return false;
            string? rt = LocateRt(window.ProcessId);
            if (rt == null) return false;

            // Go acts on the source tab. If the user clicked in the destination pane, make it the source first.
            var entry = FindEntry(QueryPaths(window.ProcessId), window);
            if (entry is { TabState: 2, Side: 1 or 2 })
                Run(rt, "/acmd Set FOCUS=" + (entry.Side == 1 ? "left" : "right"));

            return Run(rt, "/acmd Go " + Quote(path));
        }
        catch { return false; }
    }

    public IEnumerable<string> EnumerateOpenPaths()
    {
        // Don't spawn dopusrt on every menu open unless a lister actually exists.
        if (Windows.Win32.PInvoke.FindWindowEx(default, default, "dopus.lister", null).IsNull) return [];
        try
        {
            return QueryPaths().Select(e => e.Path)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch { return []; }
    }

    private sealed record PathEntry(string Path, nint Lister, nint Tab, int Side, int TabState, bool ActiveTab, bool ActiveLister);

    private List<PathEntry> QueryPaths(uint processId = 0)
    {
        var list = new List<PathEntry>();
        string? rt = LocateRt(processId);
        if (rt == null) return list;

        string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "folderite-dopus-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            if (!Run(rt, "/info " + Quote(tmp) + ",paths") || !File.Exists(tmp)) return list;

            var doc = XDocument.Load(tmp);
            foreach (var p in doc.Descendants("path"))
            {
                string text = p.Value.Trim();
                if (text.Length == 0) continue;
                list.Add(new PathEntry(
                    text,
                    ParseHandle((string?)p.Attribute("lister")),
                    ParseHandle((string?)p.Attribute("tab")),
                    ParseInt((string?)p.Attribute("side")),
                    ParseInt((string?)p.Attribute("tab_state")),
                    p.Attribute("active_tab") != null,
                    p.Attribute("active_lister") != null));
            }
        }
        catch { list.Clear(); }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
        return list;
    }

    /// <summary>The tab under the cursor in this lister; else the source tab; else the active tab.</summary>
    private static PathEntry? FindEntry(List<PathEntry> all, in TargetWindow window)
    {
        nint root = window.Root, hit = window.Hit; // 'in' parameters cannot be captured by lambdas
        var mine = all.Where(e => e.Lister == root).ToList();
        if (mine.Count == 0) return null;

        if (hit != 0)
        {
            var clicked = mine.FirstOrDefault(e => Win32Util.IsDescendantOrSelf(e.Tab, hit));
            if (clicked != null) return clicked;
        }
        return mine.FirstOrDefault(e => e.TabState == 1)
            ?? mine.FirstOrDefault(e => e.ActiveTab)
            ?? mine[0];
    }

    private string? LocateRt(uint processId)
    {
        if (_rtPath != null && File.Exists(_rtPath)) return _rtPath;
        try
        {
            if (processId != 0)
            {
                string? exe = Win32Util.GetProcessImagePath(processId);
                string? dir = exe == null ? null : System.IO.Path.GetDirectoryName(exe);
                if (dir != null)
                {
                    string candidate = System.IO.Path.Combine(dir, "dopusrt.exe");
                    if (File.Exists(candidate)) return _rtPath = candidate;
                }
            }
            if (File.Exists(FallbackExe)) return _rtPath = FallbackExe;
        }
        catch { }
        return null;
    }

    /// <summary>Raw argument string on purpose: Opus parses its own quotes, ArgumentList would escape them.</summary>
    private static bool Run(string exe, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            if (!proc.WaitForExit(ProcessTimeoutMs))
            {
                try { proc.Kill(); } catch { }
                return false;
            }
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    // A backslash right before the closing quote can read as an escape, so drop it unless it is a drive root.
    private static string Quote(string s) => "\"" + (s.Length > 3 && s.EndsWith('\\') ? s.TrimEnd('\\') : s) + "\"";

    private static nint ParseHandle(string? s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return long.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long v) ? (nint)v : 0;
    }

    private static int ParseInt(string? s) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
}
