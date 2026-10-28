using Folderite.Config;
using Folderite.Menu;

namespace Folderite.Tests;

public sealed class MenuComposerTests
{
    private const string Current = @"C:\Users\me\Projects\Folderite";

    /// <summary>Config with no actions, no open windows and no clipboard, so each test opts into one section.</summary>
    private static AppConfig Config(params FavoriteEntry[] favorites) => new()
    {
        Favorites = favorites.ToList(),
        Actions = [],
        ShowOpenWindows = false,
        ShowClipboard = false,
    };

    private static MenuContext Ctx(
        string? current = null,
        IEnumerable<string>? open = null,
        IEnumerable<string>? recent = null,
        string? clipboard = null) =>
        new(current, (open ?? []).ToList(), (recent ?? []).ToList(), clipboard);

    private static IReadOnlyList<MenuNode> Build(AppConfig config, MenuContext ctx, IMenuCommands? cmd = null) =>
        MenuComposer.Build(config, ctx, cmd ?? new FakeCommands());

    /// <summary>Items of the section that follows a header, up to the next separator.</summary>
    private static List<MenuItem> SectionAfter(IReadOnlyList<MenuNode> nodes, string headerText)
    {
        int header = -1;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is MenuHeader h && h.Text == headerText)
                header = i;
        }
        Assert.True(header >= 0, $"No header '{headerText}'.");

        var items = new List<MenuItem>();
        for (int j = header + 1; j < nodes.Count && nodes[j] is not MenuSeparator; j++)
        {
            if (nodes[j] is MenuItem item)
                items.Add(item);
        }
        return items;
    }

    private static MenuItem FirstItemWithText(IEnumerable<MenuNode> nodes, string text) =>
        nodes.OfType<MenuItem>().First(i => i.Text == text);

    private sealed class FakeCommands : IMenuCommands
    {
        public List<string> Navigated { get; } = [];
        public List<string> Copied { get; } = [];
        public List<string> Added { get; } = [];
        public List<(string Name, string Path)> Ran { get; } = [];
        public int OpenSettingsCalls { get; private set; }
        public int ReloadCalls { get; private set; }

        public void Navigate(string path) => Navigated.Add(path);
        public void RunAction(CustomAction action, string path) => Ran.Add((action.Name, path));
        public void CopyPath(string path) => Copied.Add(path);
        public void AddFavorite(string path) => Added.Add(path);
        public void OpenSettings() => OpenSettingsCalls++;
        public void Reload() => ReloadCalls++;
    }

    [Fact]
    public void CurrentPath_FirstNodeIsSubMenuNamedAfterFolder()
    {
        var nodes = Build(Config(), Ctx(Current));

        var sub = Assert.IsType<SubMenu>(nodes[0]);
        Assert.Equal("Folderite", sub.Text);
    }

    [Fact]
    public void NoCurrentPath_HasNoCurrentFolderMenu()
    {
        var nodes = Build(Config(), Ctx());

        Assert.DoesNotContain(nodes, n => n is SubMenu);
    }

    [Fact]
    public void CurrentFolderMenu_CopyPath_InvokesCopyPath()
    {
        var cmd = new FakeCommands();
        var nodes = Build(Config(), Ctx(Current), cmd);

        var copy = Assert.IsType<MenuItem>(((SubMenu)nodes[0]).Children[0]);
        copy.OnInvoke();

        Assert.Equal("&Copy path", copy.Text);
        Assert.Equal(new[] { Current }, cmd.Copied);
    }

    [Fact]
    public void CurrentFolderMenu_ConfiguredAction_InvokingPassesCurrentPath()
    {
        var cmd = new FakeCommands();
        var config = Config();
        config.Actions = [new CustomAction { Name = "Open &terminal here", Command = "wt.exe", Args = "-d \"{path}\"" }];
        var nodes = Build(config, Ctx(Current), cmd);

        var action = FirstItemWithText(((SubMenu)nodes[0]).Children, "Open &terminal here");
        action.OnInvoke();

        Assert.Equal(new[] { ("Open &terminal here", Current) }, cmd.Ran);
    }

    [Fact]
    public void CurrentFolderMenu_AddToFavorites_InvokesAddFavorite()
    {
        var cmd = new FakeCommands();
        var nodes = Build(Config(), Ctx(Current), cmd);

        var add = FirstItemWithText(((SubMenu)nodes[0]).Children, "&Add to favorites");
        add.OnInvoke();

        Assert.Equal(new[] { Current }, cmd.Added);
    }

    [Fact]
    public void AddToFavorites_HiddenWhenPathIsFavorite()
    {
        var config = Config(new FavoriteEntry { Name = "&Folderite", Path = Current });

        var nodes = Build(config, Ctx(Current));

        Assert.DoesNotContain(((SubMenu)nodes[0]).Children.OfType<MenuItem>(), i => i.Text == "&Add to favorites");
    }

    [Fact]
    public void AddToFavorites_HiddenWhenPathIsFavoriteInsideGroup()
    {
        var config = Config(new FavoriteEntry
        {
            Name = "Work",
            Items = [new FavoriteEntry { Name = "&Folderite", Path = Current }],
        });

        var nodes = Build(config, Ctx(Current));

        Assert.DoesNotContain(((SubMenu)nodes[0]).Children.OfType<MenuItem>(), i => i.Text == "&Add to favorites");
    }

    [Fact]
    public void AddToFavorites_HiddenWhenFavoriteDiffersOnlyByTrailingBackslash()
    {
        var config = Config(new FavoriteEntry { Name = "&Folderite", Path = Current + "\\" });

        var nodes = Build(config, Ctx(Current));

        Assert.DoesNotContain(((SubMenu)nodes[0]).Children.OfType<MenuItem>(), i => i.Text == "&Add to favorites");
    }

    [Fact]
    public void FavoriteGroups_BecomeSubMenus()
    {
        var cmd = new FakeCommands();
        var config = Config(new FavoriteEntry
        {
            Name = "Work",
            Items = [new FavoriteEntry { Name = "&Repo", Path = @"C:\Repo" }],
        });

        var nodes = Build(config, Ctx(), cmd);

        var group = Assert.IsType<SubMenu>(nodes[0]);
        Assert.Equal("Work", group.Text);
        var repo = Assert.IsType<MenuItem>(Assert.Single(group.Children));
        Assert.Equal("&Repo", repo.Text);
        repo.OnInvoke();
        Assert.Equal(new[] { @"C:\Repo" }, cmd.Navigated);
    }

    [Fact]
    public void FavoriteNamedDash_BecomesSeparator()
    {
        var config = Config(
            new FavoriteEntry { Name = "&A", Path = @"C:\A" },
            new FavoriteEntry { Name = "-" },
            new FavoriteEntry { Name = "&B", Path = @"C:\B" });

        var nodes = Build(config, Ctx());

        Assert.Equal("&A", Assert.IsType<MenuItem>(nodes[0]).Text);
        Assert.IsType<MenuSeparator>(nodes[1]);
        Assert.Equal("&B", Assert.IsType<MenuItem>(nodes[2]).Text);
    }

    [Fact]
    public void Favorite_Invoke_NavigatesToPath()
    {
        var cmd = new FakeCommands();
        var config = Config(new FavoriteEntry { Name = "&Top", Path = @"C:\Top" });

        var nodes = Build(config, Ctx(), cmd);

        Assert.IsType<MenuItem>(nodes[0]).OnInvoke();
        Assert.Equal(new[] { @"C:\Top" }, cmd.Navigated);
    }

    [Fact]
    public void OpenWindows_ExcludesCurrentPath()
    {
        var config = Config();
        config.ShowOpenWindows = true;

        var nodes = Build(config, Ctx(Current, open: [Current, @"C:\Work\Reports"]));

        var items = SectionAfter(nodes, "Open windows");
        Assert.Equal(new[] { @"C:\Work\Reports" }, items.Select(i => i.IconPath));
    }

    [Fact]
    public void OpenWindows_DedupesIgnoringCaseAndTrailingBackslash()
    {
        var config = Config();
        config.ShowOpenWindows = true;

        var nodes = Build(config, Ctx(open: [@"C:\Work", @"c:\work\", @"C:\Other"]));

        var items = SectionAfter(nodes, "Open windows");
        Assert.Equal(new[] { @"C:\Work", @"C:\Other" }, items.Select(i => i.IconPath));
    }

    [Fact]
    public void OpenWindows_HiddenWhenDisabled()
    {
        var config = Config();
        config.ShowOpenWindows = false;

        var nodes = Build(config, Ctx(open: [@"C:\Work"]));

        Assert.DoesNotContain(nodes, n => n is MenuHeader);
    }

    [Fact]
    public void OpenWindows_CappedAtTen()
    {
        var config = Config();
        config.ShowOpenWindows = true;
        var open = Enumerable.Range(0, 12).Select(i => $@"C:\Folder{i}").ToList();

        var nodes = Build(config, Ctx(open: open));

        Assert.Equal(10, SectionAfter(nodes, "Open windows").Count);
    }

    [Fact]
    public void CurrentFolderSubmenu_HasNoDetail()
    {
        var nodes = Build(Config(), Ctx(current: @"C:\Work\Reports"));

        var sub = Assert.IsType<SubMenu>(nodes[0]);
        Assert.Equal("Reports", sub.Text);
        Assert.Null(sub.Detail);
    }

    [Fact]
    public void OpenWindows_ItemTextIsNameAndDetailIsParent()
    {
        var config = Config();
        config.ShowOpenWindows = true;

        var nodes = Build(config, Ctx(open: [@"C:\Work\Reports"]));

        var item = Assert.Single(SectionAfter(nodes, "Open windows"));
        Assert.Equal("Reports", item.Text);
        Assert.Equal(@"C:\Work", item.Detail);
    }

    [Fact]
    public void OpenWindows_TrailingBackslashPath_ParentIsNotTheFolderItself()
    {
        var config = Config();
        config.ShowOpenWindows = true;

        var nodes = Build(config, Ctx(open: [@"C:\Users\me\Docs\"]));

        var item = Assert.Single(SectionAfter(nodes, "Open windows"));
        Assert.Equal("Docs", item.Text);
        Assert.Equal(@"C:\Users\me", item.Detail);
    }

    [Fact]
    public void OpenWindows_AmpersandInName_IsEscaped()
    {
        var config = Config();
        config.ShowOpenWindows = true;

        var nodes = Build(config, Ctx(open: [@"C:\R&D"]));

        var item = Assert.Single(SectionAfter(nodes, "Open windows"));
        Assert.Equal("R&&D", item.Text);
        Assert.Equal(@"C:\", item.Detail);
    }

    [Fact]
    public void OpenWindows_Invoke_NavigatesToPath()
    {
        var cmd = new FakeCommands();
        var config = Config();
        config.ShowOpenWindows = true;

        var nodes = Build(config, Ctx(open: [@"C:\Work\Reports"]), cmd);
        Assert.Single(SectionAfter(nodes, "Open windows"));
        nodes.OfType<MenuItem>().First(i => i.IconPath == @"C:\Work\Reports").OnInvoke();

        Assert.Equal(new[] { @"C:\Work\Reports" }, cmd.Navigated);
    }

    [Fact]
    public void Recent_RespectsRecentCount()
    {
        var config = Config();
        config.RecentCount = 2;

        var nodes = Build(config, Ctx(recent: [@"C:\A", @"C:\B", @"C:\C", @"C:\D"]));

        var recent = nodes.OfType<SubMenu>().Single(s => s.Text == "&Recent");
        Assert.Equal(2, recent.Children.Count);
    }

    [Fact]
    public void Recent_ExcludesCurrentPath()
    {
        var nodes = Build(Config(), Ctx(Current, recent: [Current, @"C:\A"]));

        var recent = nodes.OfType<SubMenu>().Single(s => s.Text == "&Recent");
        Assert.Equal(@"C:\A", Assert.IsType<MenuItem>(Assert.Single(recent.Children)).IconPath);
    }

    [Fact]
    public void Recent_ItemTextIsNameAndDetailIsParent()
    {
        var nodes = Build(Config(), Ctx(recent: [@"C:\Work\Reports"]));

        var recent = nodes.OfType<SubMenu>().Single(s => s.Text == "&Recent");
        var item = Assert.IsType<MenuItem>(Assert.Single(recent.Children));
        Assert.Equal("Reports", item.Text);
        Assert.Equal(@"C:\Work", item.Detail);
    }

    [Fact]
    public void Clipboard_ShownWhenDifferentFromCurrent_InvokingNavigates()
    {
        var cmd = new FakeCommands();
        var config = Config();
        config.ShowClipboard = true;

        var nodes = Build(config, Ctx(Current, clipboard: @"C:\Clip"), cmd);

        var clip = nodes.OfType<MenuItem>().Single(i => i.Text == "C&lipboard");
        Assert.Equal(@"C:\Clip", clip.Detail);
        clip.OnInvoke();
        Assert.Equal(new[] { @"C:\Clip" }, cmd.Navigated);
    }

    [Fact]
    public void Clipboard_HiddenWhenSameAsCurrentIgnoringTrailingBackslash()
    {
        var config = Config();
        config.ShowClipboard = true;

        var nodes = Build(config, Ctx(Current, clipboard: Current + "\\"));

        Assert.DoesNotContain(nodes.OfType<MenuItem>(), i => i.Text.Contains("lipboard"));
    }

    [Fact]
    public void Clipboard_HiddenWhenDisabled()
    {
        var config = Config();
        config.ShowClipboard = false;

        var nodes = Build(config, Ctx(Current, clipboard: @"C:\Clip"));

        Assert.DoesNotContain(nodes.OfType<MenuItem>(), i => i.Text.Contains("lipboard"));
    }

    [Fact]
    public void CurrentFolderMenu_AmpersandInName_IsEscaped()
    {
        var nodes = Build(Config(), Ctx(@"C:\Tom & Jerry"));

        Assert.Equal("Tom && Jerry", ((SubMenu)nodes[0]).Text);
    }

    [Fact]
    public void Build_EndsWithSettingsAndReload_InvokingCallsCommands()
    {
        var cmd = new FakeCommands();
        var nodes = Build(Config(), Ctx(Current), cmd);

        var settings = Assert.IsType<MenuItem>(nodes[^2]);
        var reload = Assert.IsType<MenuItem>(nodes[^1]);
        Assert.Equal("&Settings…", settings.Text);
        Assert.Equal("Reloa&d", reload.Text);

        settings.OnInvoke();
        reload.OnInvoke();

        Assert.Equal(1, cmd.OpenSettingsCalls);
        Assert.Equal(1, cmd.ReloadCalls);
    }
}
