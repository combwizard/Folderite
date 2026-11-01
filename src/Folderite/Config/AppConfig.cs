using System.Text.Json.Serialization;

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

    /// <summary>Keyboard shortcut that opens the menu, e.g. "Win+Shift+F". Empty = none.</summary>
    public string Hotkey { get; set; } = "";

    /// <summary>Show a Drives submenu (with free space).</summary>
    public bool ShowDrives { get; set; }

    /// <summary>Open file dialogs in the last folder used in the same app.</summary>
    public bool DialogLastFolder { get; set; }

    /// <summary>Per-app start folders for file dialogs; win over <see cref="DialogLastFolder"/>.</summary>
    public List<DialogFolder> DialogFolders { get; set; } = [];
}

/// <summary>Either a folder (Path set) or a group (Items set) rendered as a submenu.</summary>
public sealed class FavoriteEntry
{
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public List<FavoriteEntry>? Items { get; set; }

    /// <summary>Folder only: show its subfolders in a submenu.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ShowSubfolders { get; set; }

    /// <summary>Subfolder order: <see cref="SortByName"/> (or null) or <see cref="SortNewest"/>.</summary>
    public string? SortSubfolders { get; set; }

    public const string SortByName = "name";
    public const string SortNewest = "newest";

    public bool IsGroup => Items is not null;

    public bool SubfoldersNewestFirst => string.Equals(SortSubfolders, SortNewest, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Start folder for the file dialogs of one app.</summary>
public sealed class DialogFolder
{
    /// <summary>Process name without .exe, case-insensitive (e.g. "notepad").</summary>
    public string App { get; set; } = "";
    public string Path { get; set; } = "";
}

/// <summary>Args may contain {path}, replaced by the current folder.</summary>
public sealed class CustomAction
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Args { get; set; } = "";
}
