// Scenario driver for README captures: windows, mouse, keys, menus, ffmpeg recording.
// Usage: Driver.exe scenario.txt   (one command per line, # comments, "quoted args", %ENV% expanded)
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

static unsafe class Program
{
    static readonly Dictionary<string, nint> Wins = new();
    static Process? _rec;
    static string _baseDir = "";

    [STAThread]
    static int Main(string[] args)
    {
        _baseDir = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
        var lines = File.ReadAllLines(args[0]);
        int n = 0;
        try
        {
            foreach (var raw in lines)
            {
                n++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                line = Environment.ExpandEnvironmentVariables(line);
                var t = Tokens(line);
                Console.WriteLine($"> {line}");
                Run(t);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"!! line {n}: {ex.Message}");
            StopRec();
            return 1;
        }
        finally
        {
            ClipRestore();
        }
    }

    // ---- the user's clipboard: saved by "clipsave", put back when the scenario ends (even on error)

    static DataObject? _savedClip;

    static void ClipSave()
    {
        var src = Clipboard.GetDataObject();
        if (src is null) return;
        var copy = new DataObject();
        foreach (var f in src.GetFormats(false))
        {
            try { if (src.GetData(f, false) is { } d) copy.SetData(f, false, d); } catch { /* skip formats that can't be read */ }
        }
        _savedClip = copy;
    }

    static void ClipRestore()
    {
        if (_savedClip is null) return;
        try { Clipboard.SetDataObject(_savedClip, true, 5, 100); Console.WriteLine("  clipboard restored"); }
        catch (Exception ex) { Console.WriteLine($"  clipboard restore failed: {ex.Message}"); }
        _savedClip = null;
    }

    static List<string> Tokens(string line) =>
        Regex.Matches(line, "\"([^\"]*)\"|(\\S+)").Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).ToList();

    static int I(string s) => int.Parse(s);
    static string P(string s) => Path.IsPathRooted(s) ? s : Path.Combine(_baseDir, s);

    static void Run(List<string> t)
    {
        switch (t[0])
        {
            case "wait": Thread.Sleep(I(t[1])); break;
            case "run":
                Process.Start(new ProcessStartInfo(t[1], string.Join(' ', t.Skip(2).Select(a => a.Contains(' ') ? $"\"{a}\"" : a))) { UseShellExecute = true });
                break;
            case "waitwin": Wins[t[1]] = WaitWin(t[2], t[3], t.Count > 4 ? I(t[4]) : 8000); break;
            case "place":
            {
                var h = Wins[t[1]];
                ShowWindow(h, 9 /*SW_RESTORE*/);
                SetWindowPos(h, 0, I(t[2]), I(t[3]), I(t[4]), I(t[5]), 0x0040 /*SHOWWINDOW*/);
                break;
            }
            case "fg": Foreground(Wins[t[1]]); break;
            case "close": PostMessage(Wins[t[1]], 0x0010, 0, 0); break;
            case "rect": { GetWindowRect(Wins[t[1]], out var r); Console.WriteLine($"  {t[1]}: {r.L},{r.T} {r.R - r.L}x{r.B - r.T}"); break; }
            case "move": Glide(I(t[1]), I(t[2]), t.Count > 3 ? I(t[3]) : 400); break;
            case "movein":
            {
                GetWindowRect(Wins[t[1]], out var r);
                Glide(r.L + I(t[2]), r.T + I(t[3]), t.Count > 4 ? I(t[4]) : 400);
                break;
            }
            case "item": MoveToItem(t[1], t.Count > 2 ? I(t[2]) : 350, t.Count > 3 ? double.Parse(t[3], System.Globalization.CultureInfo.InvariantCulture) : 0.3); break;
            case "menus": DumpMenus(); break;
            case "mclick": Button(0x0020, 0x0040); break;
            case "lclick": Button(0x0002, 0x0004); break;
            case "rclick": Button(0x0008, 0x0010); break;
            case "type": TypeText(t[1], t.Count > 2 ? I(t[2]) : 60); break;
            case "key": Key(t[1]); break;
            case "ipc": Console.WriteLine($"  ipc -> {Ipc(t[1], t.Count > 2 ? t[2] : null)}"); break;
            case "clip": Clipboard.SetText(t[1]); break;
            case "clipsave": ClipSave(); break;
            case "savedialog": SaveDialog(t[1], t[2]); break;
            case "backdrop": Backdrop(I(t[1]), I(t[2]), I(t[3]), I(t[4]), t[5]); break;
            case "rec": StartRec(P(t[1]), I(t[2]), I(t[3]), I(t[4]), I(t[5]), t.Count > 6 ? I(t[6]) : 20); break;
            case "stop": StopRec(); break;
            case "mark": File.WriteAllText(P(t[1]), _recWatch.Elapsed.TotalSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)); break;
            case "snap": Snap(P(t[1]), I(t[2]), I(t[3]), I(t[4]), I(t[5]), !t.Contains("nomouse")); break;
            default: throw new InvalidOperationException($"unknown command {t[0]}");
        }
    }

    // ---- windows

    static nint WaitWin(string cls, string titlePrefix, int timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeout)
        {
            nint found = 0;
            EnumWindows((h, _) =>
            {
                if (!IsWindowVisible(h)) return true;
                var c = new StringBuilder(256); GetClassName(h, c, 256);
                var s = new StringBuilder(512); GetWindowText(h, s, 512);
                if (c.ToString().StartsWith(cls, StringComparison.Ordinal) &&s.ToString().StartsWith(titlePrefix, StringComparison.OrdinalIgnoreCase)) { found = h; return false; }
                return true;
            }, 0);
            if (found != 0) { Thread.Sleep(300); return found; }
            Thread.Sleep(100);
        }
        throw new TimeoutException($"no window {cls} '{titlePrefix}'");
    }

    static void Foreground(nint h)
    {
        keybd_event(0x12, 0, 0, 0); keybd_event(0x12, 0, 2, 0); // Alt tap lifts the foreground lock
        SetForegroundWindow(h);
    }

    // ---- mouse / keyboard

    static void Glide(int x, int y, int ms)
    {
        GetCursorPos(out var p);
        int steps = Math.Max(1, ms / 8);
        for (int i = 1; i <= steps; i++)
        {
            double f = (double)i / steps;
            f = f < 0.5 ? 4 * f * f * f : 1 - Math.Pow(-2 * f + 2, 3) / 2;
            MoveAbs((int)Math.Round(p.X + (x - p.X) * f), (int)Math.Round(p.Y + (y - p.Y) * f));
            Thread.Sleep(8);
        }
        MoveAbs(x, y);
    }

    static void MoveAbs(int x, int y)
    {
        int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
        var inp = new INPUT { type = 0 };
        inp.u.mi.dx = (int)Math.Round((x - vx) * 65535.0 / (vw - 1));
        inp.u.mi.dy = (int)Math.Round((y - vy) * 65535.0 / (vh - 1));
        inp.u.mi.dwFlags = 0x0001 | 0x8000 | 0x4000; // MOVE | ABSOLUTE | VIRTUALDESK
        SendInput(1, &inp, sizeof(INPUT));
    }

    static void Button(uint down, uint up)
    {
        var inp = new INPUT { type = 0 };
        inp.u.mi.dwFlags = down; SendInput(1, &inp, sizeof(INPUT));
        Thread.Sleep(60);
        inp.u.mi.dwFlags = up; SendInput(1, &inp, sizeof(INPUT));
    }

    static void TypeText(string text, int delay)
    {
        foreach (var ch in text)
        {
            var inp = new INPUT { type = 1 };
            inp.u.ki.wScan = ch; inp.u.ki.dwFlags = 0x0004; // UNICODE
            SendInput(1, &inp, sizeof(INPUT));
            inp.u.ki.dwFlags = 0x0004 | 0x0002;
            SendInput(1, &inp, sizeof(INPUT));
            Thread.Sleep(delay);
        }
    }

    static readonly Dictionary<string, byte> Vk = new()
    {
        ["ctrl"] = 0x11, ["shift"] = 0x10, ["alt"] = 0x12, ["enter"] = 0x0D, ["esc"] = 0x1B, ["tab"] = 0x09,
        ["back"] = 0x08, ["delete"] = 0x2E, ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
        ["home"] = 0x24, ["end"] = 0x23, ["f5"] = 0x74,
    };

    static void Key(string combo)
    {
        var keys = combo.ToLowerInvariant().Split('+').Select(k => Vk.TryGetValue(k, out var v) ? v : (byte)char.ToUpperInvariant(k[0])).ToList();
        foreach (var k in keys) keybd_event(k, 0, 0, 0);
        Thread.Sleep(40);
        foreach (var k in Enumerable.Reverse(keys)) keybd_event(k, 0, 2, 0);
    }

    // ---- popup menus

    record MenuItemPos(nint Menu, int Index, string Text, RECT Rect);

    static List<List<MenuItemPos>> OpenMenus()
    {
        var menus = new List<List<MenuItemPos>>();
        EnumWindows((h, _) =>
        {
            var c = new StringBuilder(64); GetClassName(h, c, 64);
            if (c.ToString() != "#32768" || !IsWindowVisible(h)) return true;
            var hmenu = SendMessage(h, 0x01E1 /*MN_GETHMENU*/, 0, 0);
            if (hmenu == 0) return true;
            var items = new List<MenuItemPos>();
            int count = GetMenuItemCount(hmenu);
            for (int i = 0; i < count; i++)
            {
                var sb = new StringBuilder(512);
                GetMenuString(hmenu, (uint)i, sb, 512, 0x400 /*MF_BYPOSITION*/);
                GetMenuItemRect(0, hmenu, (uint)i, out var r);
                items.Add(new MenuItemPos(hmenu, i, sb.ToString().Replace("&", ""), r));
            }
            menus.Add(items);
            return true;
        }, 0);
        return menus; // topmost (most recently opened) first
    }

    static void DumpMenus()
    {
        foreach (var m in OpenMenus())
        {
            Console.WriteLine("  menu:");
            foreach (var it in m) Console.WriteLine($"    [{it.Index}] {it.Rect.L},{it.Rect.T} {it.Rect.R - it.Rect.L}x{it.Rect.B - it.Rect.T} '{it.Text}'");
        }
    }

    static void MoveToItem(string text, int ms, double fx)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 3000)
        {
            foreach (var m in OpenMenus())
                foreach (var it in m)
                    if (it.Text.Split('\t')[0].Equals(text, StringComparison.OrdinalIgnoreCase))
                    {
                        var r = it.Rect;
                        Glide(r.L + (int)((r.R - r.L) * fx), (r.T + r.B) / 2, ms);
                        return;
                    }
            Thread.Sleep(100);
        }
        DumpMenus();
        throw new InvalidOperationException($"menu item '{text}' not found");
    }

    // ---- folderite ipc

    static nint Ipc(string cmd, string? arg)
    {
        var h = FindWindow("FolderiteMessageWindow", null);
        if (h == 0) return -1;
        var payload = arg is null ? cmd : cmd + "\n" + arg;
        fixed (char* p = payload)
        {
            var cds = new COPYDATASTRUCT { dwData = 0x464C4452, cbData = (uint)payload.Length * 2, lpData = (nint)p };
            SendMessageTimeout(h, 0x004A, 0, (nint)(&cds), 0x0002 | 0x0001, 2000, out var result);
            return result;
        }
    }

    // ---- dialogs

    static void SaveDialog(string dir, string fileName)
    {
        var th = new Thread(() =>
        {
            using var d = new SaveFileDialog { InitialDirectory = dir, FileName = fileName, Title = "Save As", Filter = "Word document (*.docx)|*.docx|All files (*.*)|*.*", OverwritePrompt = false };
            d.ShowDialog();
        });
        th.SetApartmentState(ApartmentState.STA);
        th.IsBackground = true;
        th.Start();
    }

    // Plain borderless window that hides whatever is on screen behind the demo windows.
    static void Backdrop(int x, int y, int w, int h, string hex)
    {
        var ready = new ManualResetEventSlim();
        var th = new Thread(() =>
        {
            var f = new Form
            {
                FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
                Bounds = new System.Drawing.Rectangle(x, y, w, h), ShowInTaskbar = false, Text = "Backdrop",
                BackColor = System.Drawing.ColorTranslator.FromHtml(hex),
            };
            f.Shown += (_, _) => ready.Set();
            Application.Run(f);
        });
        th.SetApartmentState(ApartmentState.STA);
        th.IsBackground = true;
        th.Start();
        ready.Wait(5000);
        Thread.Sleep(200);
    }

    // ---- capture (ffmpeg gdigrab; offsets are screen coordinates)

    // "mark" writes the time since the recording started, so capture.ps1 can pull a still from the video
    // without pausing the animation. ffmpeg starts grabbing slightly after the watch starts, so the frame
    // at a mark is a moment *later* than the mark: put marks where the screen stays the same for ~1 s.
    static readonly Stopwatch _recWatch = new();

    static void StartRec(string file, int x, int y, int w, int h, int fps)
    {
        var args = $"-hide_banner -loglevel error -y -f gdigrab -framerate {fps} -offset_x {x} -offset_y {y} -video_size {w}x{h} -draw_mouse 1 -i desktop -c:v libx264rgb -crf 0 -preset ultrafast \"{file}\"";
        _recWatch.Restart();
        _rec = Process.Start(new ProcessStartInfo("ffmpeg", args) { RedirectStandardInput = true, UseShellExecute = false });
        Thread.Sleep(1200);
    }

    static void StopRec()
    {
        if (_rec is null) return;
        _rec.StandardInput.Write("q");
        _rec.StandardInput.Flush();
        if (!_rec.WaitForExit(10000)) _rec.Kill();
        _rec = null;
    }

    static void Snap(string file, int x, int y, int w, int h, bool mouse)
    {
        var args = $"-hide_banner -loglevel error -y -f gdigrab -framerate 10 -offset_x {x} -offset_y {y} -video_size {w}x{h} -draw_mouse {(mouse ? 1 : 0)} -i desktop -frames:v 1 \"{file}\"";
        using var p = Process.Start(new ProcessStartInfo("ffmpeg", args) { UseShellExecute = false })!;
        p.WaitForExit();
    }

    // ---- interop

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct COPYDATASTRUCT { public nuint dwData; public uint cbData; public nint lpData; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public nint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nint dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
    delegate bool EnumProc(nint h, nint l);

    [DllImport("user32")] static extern bool EnumWindows(EnumProc cb, nint l);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetClassName(nint h, StringBuilder s, int n);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetWindowText(nint h, StringBuilder s, int n);
    [DllImport("user32")] static extern bool IsWindowVisible(nint h);
    [DllImport("user32")] static extern bool ShowWindow(nint h, int cmd);
    [DllImport("user32")] static extern bool SetWindowPos(nint h, nint after, int x, int y, int w, int hh, uint f);
    [DllImport("user32")] static extern bool SetForegroundWindow(nint h);
    [DllImport("user32")] static extern bool PostMessage(nint h, uint m, nint w, nint l);
    [DllImport("user32")] static extern nint SendMessage(nint h, uint m, nint w, nint l);
    [DllImport("user32")] static extern nint SendMessageTimeout(nint h, uint m, nint w, nint l, uint f, uint t, out nint r);
    [DllImport("user32")] static extern bool GetWindowRect(nint h, out RECT r);
    [DllImport("user32")] static extern bool GetCursorPos(out POINT p);
    [DllImport("user32")] static extern int GetSystemMetrics(int i);
    [DllImport("user32")] static extern uint SendInput(uint n, INPUT* i, int size);
    [DllImport("user32")] static extern void keybd_event(byte vk, byte scan, uint flags, nint extra);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern nint FindWindow(string? cls, string? title);
    [DllImport("user32")] static extern int GetMenuItemCount(nint m);
    [DllImport("user32", CharSet = CharSet.Unicode)] static extern int GetMenuString(nint m, uint id, StringBuilder s, int n, uint f);
    [DllImport("user32")] static extern bool GetMenuItemRect(nint h, nint m, uint i, out RECT r);
}
