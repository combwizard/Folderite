using System.Runtime.InteropServices;

namespace Folderite.Menu;

/// <summary>Subfolders of a folder, as listed in a favorite's submenu.</summary>
/// <param name="Truncated">More folders exist than <see cref="FolderLister.MaxSubfolders"/>.</param>
public sealed record FolderListing(IReadOnlyList<string> Paths, bool Truncated)
{
    public static readonly FolderListing Empty = new([], false);
}

/// <summary>A drive for the Drives submenu. Sizes are null when the drive isn't ready.</summary>
/// <param name="Root">"C:\".</param>
/// <param name="Label">"Local Disk (C:)" or "Data (D:)".</param>
public sealed record DriveEntry(string Root, string Label, long? FreeBytes, long? TotalBytes);

/// <summary>
/// Disk reads for the menu's lazy submenus. They run while a submenu is opening (on the UI thread, inside the
/// menu's modal loop), so each read happens on a worker thread with a time limit: a sleeping network share or
/// a disconnected drive gives an empty or partial list instead of a frozen menu.
/// </summary>
internal static class FolderLister
{
    public const int MaxSubfolders = 100;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(1500);

    public static FolderListing ListSubfolders(string path, bool newestFirst) =>
        Run(() => ReadSubfolders(path, newestFirst)) ?? FolderListing.Empty;

    /// <summary>All drives, read in parallel under one time limit; a drive that doesn't answer in time is listed without sizes.</summary>
    public static IReadOnlyList<DriveEntry> ListDrives()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }   // GetLogicalDrives: no disk access
        catch (Exception ex) { Log.Error(ex); return []; }

        var tasks = drives.Select(d => Task.Run(() => ReadDrive(d))).ToArray();
        try { Task.WaitAll(tasks, Timeout); }
        catch (AggregateException) { /* failed drives fall back below */ }

        var list = new List<DriveEntry>(drives.Length);
        for (int i = 0; i < drives.Length; i++)
        {
            list.Add(tasks[i].IsCompletedSuccessfully
                ? tasks[i].Result
                : new DriveEntry(drives[i].Name, Label(drives[i].Name, null, SafeType(drives[i])), null, null));
        }
        return list;
    }

    private static T? Run<T>(Func<T> read) where T : class
    {
        var task = Task.Run(read);
        try
        {
            return task.Wait(Timeout) ? task.Result : null;
        }
        catch (AggregateException ex) when (ex.InnerException is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;   // missing, offline or locked folder: an ordinary state, not worth a log entry
        }
        catch (AggregateException ex)
        {
            Log.Error(ex.InnerException ?? ex);
            return null;
        }
    }

    private static FolderListing ReadSubfolders(string path, bool newestFirst)
    {
        var dir = new DirectoryInfo(path);
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            RecurseSubdirectories = false,
        };
        var folders = dir.EnumerateDirectories("*", options).ToList();

        IEnumerable<DirectoryInfo> sorted = newestFirst
            ? folders.OrderByDescending(SafeLastWrite)
            : folders.OrderBy(d => d.Name, LogicalComparer.Instance);
        var paths = sorted.Take(MaxSubfolders).Select(d => d.FullName).ToList();
        return new FolderListing(paths, folders.Count > MaxSubfolders);
    }

    private static DateTime SafeLastWrite(DirectoryInfo d)
    {
        try { return d.LastWriteTimeUtc; }
        catch { return DateTime.MinValue; }
    }

    private static DriveType SafeType(DriveInfo d)
    {
        try { return d.DriveType; }
        catch { return DriveType.Unknown; }
    }

    private static DriveEntry ReadDrive(DriveInfo drive)
    {
        if (!drive.IsReady)
            return new DriveEntry(drive.Name, Label(drive.Name, null, drive.DriveType), null, null);
        string? volume = null;
        try { volume = drive.VolumeLabel; } catch { /* no label */ }
        return new DriveEntry(drive.Name, Label(drive.Name, volume, drive.DriveType), drive.AvailableFreeSpace, drive.TotalSize);
    }

    internal static string Label(string root, string? volume, DriveType type)
    {
        string letter = root.TrimEnd('\\');
        string name = !string.IsNullOrWhiteSpace(volume) ? volume : type switch
        {
            DriveType.Removable => "USB Drive",
            DriveType.Network => "Network Drive",
            DriveType.CDRom => "CD Drive",
            DriveType.Ram => "RAM Disk",
            _ => "Local Disk",
        };
        return $"{name} ({letter})";
    }

    /// <summary>"12.3 GB free of 476 GB".</summary>
    public static string FormatSpace(long free, long total) => $"{FormatSize(free)} free of {FormatSize(total)}";

    internal static string FormatSize(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB", "PB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        if (unit == 0) return $"{bytes} bytes";
        // Three significant digits, like Explorer: 9.87 GB, 98.7 GB, 987 GB.
        string format = value < 10 ? "0.##" : value < 100 ? "0.#" : "0";
        return value.ToString(format, System.Globalization.CultureInfo.CurrentCulture) + " " + units[unit];
    }

    /// <summary>Explorer's name order ("file2" before "file10").</summary>
    private sealed class LogicalComparer : IComparer<string>
    {
        public static readonly LogicalComparer Instance = new();
        public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? "", y ?? "");

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int StrCmpLogicalW(string psz1, string psz2);
    }
}
