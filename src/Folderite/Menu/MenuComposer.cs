using Folderite.Config;

namespace Folderite.Menu;

/// <summary>What the menu needs to know about the moment it was opened.</summary>
internal sealed record MenuContext(
    string? CurrentPath,
    IReadOnlyList<string> OpenPaths,
    IReadOnlyList<string> RecentPaths,
    string? ClipboardPath);

/// <summary>Side effects the menu items trigger; implemented by App.</summary>
internal interface IMenuCommands
{
    void Navigate(string path);
    void RunAction(CustomAction action, string path);
    void CopyPath(string path);
    void AddFavorite(string path);
    void OpenSettings();
    void Reload();

    /// <summary>Subfolders for a favorite's submenu; called when that submenu opens.</summary>
    FolderListing ListSubfolders(string path, bool newestFirst);

    /// <summary>Drives for the Drives submenu; called when it opens.</summary>
    IReadOnlyList<DriveEntry> ListDrives();
}

/// <summary>Builds the popup's menu model from config + context. Pure: no Win32, no disk access.</summary>
internal static class MenuComposer
{
    private const int MaxOpenWindows = 10;

    public static IReadOnlyList<MenuNode> Build(AppConfig config, MenuContext ctx, IMenuCommands cmd)
    {
        var nodes = new List<MenuNode>();
        string? current = ctx.CurrentPath;

        if (current is not null)
        {
            var actions = new List<MenuNode>
            {
                new MenuItem("&Copy path", () => cmd.CopyPath(current)),
            };
            foreach (var action in config.Actions)
                actions.Add(new MenuItem(action.Name, () => cmd.RunAction(action, current)));
            if (!ContainsFavorite(config.Favorites, current))
            {
                actions.Add(MenuSeparator.Instance);
                actions.Add(new MenuItem("&Add to favorites", () => cmd.AddFavorite(current)));
            }
            // No parent path here: it's the folder the menu was opened over, and a long path made the whole menu wide.
            nodes.Add(new SubMenu(Escape(DisplayName(current)), actions, current));
            nodes.Add(MenuSeparator.Instance);
        }

        AddFavorites(nodes, config.Favorites, cmd);

        if (config.ShowOpenWindows)
        {
            var open = Distinct(ctx.OpenPaths, current).Take(MaxOpenWindows).ToList();
            if (open.Count > 0)
            {
                nodes.Add(MenuSeparator.Instance);
                nodes.Add(new MenuHeader("Open windows"));
                foreach (var path in open)
                    nodes.Add(FolderItem(path, cmd, showParent: true));
            }
        }

        var recent = Distinct(ctx.RecentPaths, current).Take(Math.Max(0, config.RecentCount)).ToList();
        if (recent.Count > 0)
        {
            nodes.Add(MenuSeparator.Instance);
            nodes.Add(new SubMenu("&Recent", recent.Select(p => (MenuNode)FolderItem(p, cmd, showParent: true)).ToList()));
        }

        if (config.ShowDrives)
        {
            nodes.Add(MenuSeparator.Instance);
            nodes.Add(new SubMenu("Dri&ves", [], LazyChildren: () => DriveItems(cmd)));
        }

        if (config.ShowClipboard && ctx.ClipboardPath is { } clip && !PathEquals(clip, current))
        {
            nodes.Add(MenuSeparator.Instance);
            nodes.Add(new MenuItem("C&lipboard", () => cmd.Navigate(clip), clip, Detail: clip));
        }

        nodes.Add(MenuSeparator.Instance);
        nodes.Add(new MenuItem("&Settings…", cmd.OpenSettings));
        nodes.Add(new MenuItem("Reloa&d", cmd.Reload));
        return nodes;
    }

    private static void AddFavorites(List<MenuNode> nodes, List<FavoriteEntry> entries, IMenuCommands cmd)
    {
        foreach (var entry in entries)
        {
            if (entry.IsGroup)
            {
                var children = new List<MenuNode>();
                AddFavorites(children, entry.Items!, cmd);
                nodes.Add(new SubMenu(entry.Name, children));
            }
            else if (entry.Name == "-")
            {
                nodes.Add(MenuSeparator.Instance);
            }
            else if (!string.IsNullOrWhiteSpace(entry.Path))
            {
                string path = Environment.ExpandEnvironmentVariables(entry.Path);
                string name = string.IsNullOrEmpty(entry.Name) ? Escape(DisplayName(path)) : entry.Name;
                if (entry.ShowSubfolders)
                {
                    bool newestFirst = entry.SubfoldersNewestFirst;
                    nodes.Add(new SubMenu(name, [], path, () => SubfolderItems(path, newestFirst, cmd)));
                }
                else
                {
                    nodes.Add(new MenuItem(name, () => cmd.Navigate(path), path));
                }
            }
        }
    }

    /// <summary>
    /// Children of a favorite's subfolder submenu. A submenu row can't be clicked, so the folder itself is the
    /// bold first item. Runs when the submenu opens, which is the only time the disk is read.
    /// </summary>
    private static IReadOnlyList<MenuNode> SubfolderItems(string path, bool newestFirst, IMenuCommands cmd)
    {
        var listing = cmd.ListSubfolders(path, newestFirst);
        var items = new List<MenuNode>
        {
            new MenuItem(Escape(DisplayName(path)), () => cmd.Navigate(path), path, IsDefault: true),
            MenuSeparator.Instance,
        };
        foreach (var sub in listing.Paths)
            items.Add(FolderItem(sub, cmd, showParent: false));
        if (listing.Truncated)
            items.Add(new MenuItem($"Showing the first {FolderLister.MaxSubfolders} folders", () => { }, Enabled: false));
        return items;
    }

    /// <summary>Children of the Drives submenu, with free space as the dimmed detail. Runs when the submenu opens.</summary>
    private static IReadOnlyList<MenuNode> DriveItems(IMenuCommands cmd) =>
        cmd.ListDrives()
            .Select(d => (MenuNode)new MenuItem(
                Escape(d.Label),
                () => cmd.Navigate(d.Root),
                d.Root,
                Detail: d.FreeBytes is { } free && d.TotalBytes is { } total ? FolderLister.FormatSpace(free, total) : null))
            .ToList();

    /// <summary>Folder entry: the name as text, with the parent folder as the dimmed <see cref="MenuItem.Detail"/>.</summary>
    private static MenuItem FolderItem(string path, IMenuCommands cmd, bool showParent) =>
        new(Escape(DisplayName(path)), () => cmd.Navigate(path), path, Detail: showParent ? ParentOf(path) : null);

    public static string DisplayName(string path)
    {
        string trimmed = path.TrimEnd('\\');
        string name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static string? ParentOf(string path)
    {
        string? parent = Path.GetDirectoryName(path.TrimEnd('\\'));
        return string.IsNullOrEmpty(parent) ? null : parent;
    }

    private static IEnumerable<string> Distinct(IEnumerable<string> paths, string? exclude) =>
        paths.Where(p => !PathEquals(p, exclude))
             .DistinctBy(p => p.TrimEnd('\\'), StringComparer.OrdinalIgnoreCase);

    private static bool ContainsFavorite(List<FavoriteEntry> entries, string path) =>
        entries.Any(e => e.IsGroup
            ? ContainsFavorite(e.Items!, path)
            : e.Path is not null && PathEquals(Environment.ExpandEnvironmentVariables(e.Path), path));

    private static bool PathEquals(string? a, string? b) =>
        a is not null && b is not null &&
        string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static string Escape(string text) => text.Replace("&", "&&");
}
