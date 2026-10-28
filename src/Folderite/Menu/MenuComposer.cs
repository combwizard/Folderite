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
                nodes.Add(new MenuItem(name, () => cmd.Navigate(path), path));
            }
        }
    }

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
