namespace Folderite.Config;

/// <summary>Root of config.json. Property names serialize camelCase.</summary>
public sealed class AppConfig
{
    public List<FavoriteEntry> Favorites { get; set; } = [];

    /// <summary>Number of recent folders shown in the Recent submenu.</summary>
    public int RecentCount { get; set; } = 15;

    /// <summary>Wildcard patterns (* and ?) of paths never recorded in history, e.g. "C:\\Windows\\*".</summary>
    public List<string> HistoryExclude { get; set; } = [];

    /// <summary>Commands run against the current folder, shown under the current-folder header.</summary>
    public List<CustomAction> Actions { get; set; } =
    [
        new() { Name = "Open &terminal here", Command = "wt.exe", Args = "-d \"{path}\"" },
    ];

    /// <summary>Process names (no extension, case-insensitive) where the trigger is ignored.</summary>
    public List<string> DisabledProcesses { get; set; } = [];

    public bool ShowOpenWindows { get; set; } = true;
    public bool ShowClipboard { get; set; } = true;

    /// <summary>Also trigger on the desktop (navigating opens a new Explorer window).</summary>
    public bool EnableOnDesktop { get; set; } = true;
}

/// <summary>Either a folder (Path set) or a group (Items set) rendered as a submenu.</summary>
public sealed class FavoriteEntry
{
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public List<FavoriteEntry>? Items { get; set; }

    public bool IsGroup => Items is not null;
}

/// <summary>Args may contain {path}, replaced by the current folder.</summary>
public sealed class CustomAction
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Args { get; set; } = "";
}
