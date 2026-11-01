using Folderite.Config;
using Folderite.Settings.ViewModels;

namespace Folderite.Tests;

public sealed class FavoritesVmTests
{
    private static List<FavoriteEntry> SampleConfig() =>
    [
        new() { Name = "&Home", Path = @"C:\Users\me" },
        new()
        {
            Name = "Work",
            Items =
            [
                new() { Name = "&Projects", Path = @"%USERPROFILE%\Projects" },
                new() { Name = "-" },
                new()
                {
                    Name = "Deep",
                    Items = [new() { Name = "R&&D", Path = @"D:\rd" }, new() { Name = "Empty group", Items = [] }],
                },
                new() { Name = "No path" },
            ],
        },
        new() { Name = "-" },
        new() { Name = "Tail", Path = @"\\server\share" },
    ];

    private static FavoritesTreeVm LoadedTree(List<FavoriteEntry>? entries = null)
    {
        var tree = new FavoritesTreeVm();
        tree.Load(entries ?? SampleConfig());
        return tree;
    }

    private static string[] Names(IEnumerable<FavoriteNodeVm> nodes) => nodes.Select(n => n.Name).ToArray();

    private static void AssertEqual(FavoriteEntry expected, FavoriteEntry actual)
    {
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Path, actual.Path);
        Assert.Equal(expected.ShowSubfolders, actual.ShowSubfolders);
        Assert.Equal(expected.SortSubfolders, actual.SortSubfolders);
        Assert.Equal(expected.Items is null, actual.Items is null);
        if (expected.Items is null)
            return;
        Assert.Equal(expected.Items.Count, actual.Items!.Count);
        for (int i = 0; i < expected.Items.Count; i++)
            AssertEqual(expected.Items[i], actual.Items[i]);
    }

    [Fact]
    public void FromConfig_ToConfig_RoundTripsExactly()
    {
        var original = SampleConfig();

        var nodes = FavoriteNodeVm.FromConfig(original);
        var back = FavoriteNodeVm.ToConfig(nodes);

        Assert.Equal(original.Count, back.Count);
        for (int i = 0; i < original.Count; i++)
            AssertEqual(original[i], back[i]);
    }

    [Fact]
    public void Subfolders_RoundTripThroughViewModel()
    {
        var original = new List<FavoriteEntry>
        {
            new() { Name = "Plain", Path = @"C:\Plain" },
            new() { Name = "ByName", Path = @"C:\ByName", ShowSubfolders = true },
            new() { Name = "Newest", Path = @"C:\Newest", ShowSubfolders = true, SortSubfolders = FavoriteEntry.SortNewest },
            new() { Name = "Group", Items = [new() { Name = "Inner", Path = @"C:\Inner", ShowSubfolders = true, SortSubfolders = "newest" }] },
        };

        var nodes = FavoriteNodeVm.FromConfig(original);

        Assert.False(nodes[0].ShowSubfolders);
        Assert.True(nodes[1].ShowSubfolders);
        Assert.False(nodes[1].SubfoldersNewestFirst);
        Assert.True(nodes[1].SubfoldersByName);
        Assert.True(nodes[2].SubfoldersNewestFirst);
        Assert.False(nodes[2].SubfoldersByName);
        Assert.True(nodes[3].Children[0].SubfoldersNewestFirst);

        var back = FavoriteNodeVm.ToConfig(nodes);
        for (int i = 0; i < original.Count; i++)
            AssertEqual(original[i], back[i]);
    }

    [Fact]
    public void Subfolders_DefaultsWriteNothingExtra()
    {
        var entry = Assert.Single(FavoriteNodeVm.ToConfig([FavoriteNodeVm.NewFolder(@"C:\x")]));

        Assert.False(entry.ShowSubfolders);
        Assert.Null(entry.SortSubfolders);
    }

    [Fact]
    public void Subfolders_ToConfig_WritesNewestAsNewestAndNameAsNull()
    {
        var node = FavoriteNodeVm.NewFolder(@"C:\x");
        node.ShowSubfolders = true;
        node.SubfoldersNewestFirst = true;
        Assert.Equal("newest", FavoriteNodeVm.ToConfig([node])[0].SortSubfolders);

        node.SubfoldersByName = true;
        Assert.False(node.SubfoldersNewestFirst);
        Assert.Null(FavoriteNodeVm.ToConfig([node])[0].SortSubfolders);
    }

    [Fact]
    public void Subfolders_GroupAndSeparatorIgnoreTheProperties()
    {
        var group = FavoriteNodeVm.NewGroup("G");
        group.ShowSubfolders = true;
        var separator = FavoriteNodeVm.NewSeparator();
        separator.ShowSubfolders = true;

        var back = FavoriteNodeVm.ToConfig([group, separator]);

        Assert.All(back, e => Assert.False(e.ShowSubfolders));
        Assert.All(back, e => Assert.Null(e.SortSubfolders));
    }

    [Fact]
    public void Subfolders_ChangingEitherProperty_RaisesTreeChanged()
    {
        var tree = LoadedTree();
        int changed = 0;
        tree.Changed += () => changed++;
        var home = tree.Roots[0];

        home.ShowSubfolders = true;
        Assert.Equal(1, changed);

        home.SubfoldersNewestFirst = true;
        Assert.Equal(2, changed);

        home.SubfoldersByName = true;
        Assert.Equal(3, changed);
    }

    [Fact]
    public void Subfolders_LoadingDoesNotRaiseTreeChanged()
    {
        var tree = new FavoritesTreeVm();
        int changed = 0;
        tree.Changed += () => changed++;

        tree.Load([new() { Name = "A", Path = @"C:\A", ShowSubfolders = true, SortSubfolders = "newest" }]);

        Assert.Equal(0, changed);
        Assert.True(tree.Roots[0].ShowSubfolders);
        Assert.Equal("newest", tree.ToConfig()[0].SortSubfolders);
    }

    [Fact]
    public void FromConfig_ClassifiesKindsAndSetsParents()
    {
        var nodes = FavoriteNodeVm.FromConfig(SampleConfig());

        var work = nodes[1];
        Assert.Equal(FavoriteKind.Folder, nodes[0].Kind);
        Assert.Equal(FavoriteKind.Group, work.Kind);
        Assert.Equal(FavoriteKind.Separator, work.Children[1].Kind);
        Assert.Equal(FavoriteKind.Separator, nodes[2].Kind);
        Assert.Equal(FavoriteKind.Folder, work.Children[3].Kind);
        Assert.Null(work.Children[3].Path);
        Assert.All(work.Children, c => Assert.Same(work, c.Parent));
        Assert.Same(work.Children[2], work.Children[2].Children[0].Parent);
        Assert.Null(nodes[0].Parent);
    }

    [Fact]
    public void SeparatorToConfig_IsJustDash()
    {
        var entry = Assert.Single(FavoriteNodeVm.ToConfig([FavoriteNodeVm.NewSeparator()]));

        Assert.Equal("-", entry.Name);
        Assert.Null(entry.Path);
        Assert.Null(entry.Items);
    }

    [Fact]
    public void DisplayNameAndAccelerator()
    {
        var node = FavoriteNodeVm.NewFolder(@"C:\a&b");
        Assert.Equal("a&&b", node.Name);
        Assert.Equal("a&b", node.DisplayName);

        node.Name = "Do&wnloads";
        Assert.Equal("Downloads", node.DisplayName);
        Assert.Equal('W', node.Accelerator);

        node.Name = "R&&D";
        Assert.Equal("R&D", node.DisplayName);
        Assert.Null(node.Accelerator);

        Assert.Equal("Separator", FavoriteNodeVm.NewSeparator().DisplayName);
    }

    [Fact]
    public void FolderWithEmptyName_DisplaysFolderNameOfExpandedPath()
    {
        var node = new FavoriteNodeVm(FavoriteKind.Folder, "", @"C:\Stuff\Things");
        Assert.Equal("Things", node.DisplayName);

        Environment.SetEnvironmentVariable("FOLDERITE_TEST_DIR", @"C:\Env\Target");
        node.Path = @"%FOLDERITE_TEST_DIR%";
        Assert.Equal("Target", node.DisplayName);
        Assert.Equal(@"C:\Env\Target", node.ExpandedPath);
    }

    [Fact]
    public void NameAndPath_RaiseComputedNotifications()
    {
        var node = FavoriteNodeVm.NewFolder(@"C:\x");
        var raised = new List<string?>();
        node.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        node.Name = "&X";
        node.Path = @"C:\y";

        Assert.Contains("DisplayName", raised);
        Assert.Contains("Accelerator", raised);
        Assert.Contains("ExpandedPath", raised);
        Assert.Contains("PathMissing", raised);
    }

    [Fact]
    public void PathMissing_OnlyForMissingLocalDriveFolders()
    {
        using var temp = new TempDirectory();

        Assert.False(new FavoriteNodeVm(FavoriteKind.Folder, "t", temp.Root).PathMissing);
        Assert.True(new FavoriteNodeVm(FavoriteKind.Folder, "t", Path.Combine(temp.Root, "nope")).PathMissing);
        Assert.False(new FavoriteNodeVm(FavoriteKind.Folder, "t", @"\\no-such-server\share").PathMissing);
        Assert.False(new FavoriteNodeVm(FavoriteKind.Folder, "t", null).PathMissing);
        Assert.False(new FavoriteNodeVm(FavoriteKind.Group, "g", Path.Combine(temp.Root, "nope")).PathMissing);
    }

    [Fact]
    public void Add_NoSelection_AppendsToRootsAndSelects()
    {
        var tree = LoadedTree();

        tree.AddFolder(@"C:\New");

        Assert.Equal("New", tree.Roots[^1].Name);
        Assert.Same(tree.Roots[^1], tree.Selected);
        Assert.True(tree.Selected!.IsSelected);
    }

    [Fact]
    public void Add_FolderSelected_InsertsAfterAmongSiblings()
    {
        var tree = LoadedTree();
        var projects = tree.Roots[1].Children[0];
        tree.Selected = projects;

        tree.AddFolder(@"C:\New");

        Assert.Equal(["&Projects", "New", "-", "Deep", "No path"], Names(tree.Roots[1].Children));
        Assert.Same(tree.Roots[1], tree.Selected!.Parent);
    }

    [Fact]
    public void Add_GroupSelected_AppendsToItsChildrenAndExpands()
    {
        var tree = LoadedTree();
        var group = tree.Roots[1];
        group.IsExpanded = false;
        tree.Selected = group;

        tree.AddSeparator();

        Assert.Equal("-", group.Children[^1].Name);
        Assert.Same(group, group.Children[^1].Parent);
        Assert.True(group.IsExpanded);
        Assert.Same(group.Children[^1], tree.Selected);
    }

    [Fact]
    public void RemoveSelected_SelectsNextThenPreviousThenParent()
    {
        var tree = LoadedTree();
        var group = tree.Roots[1];
        var kids = group.Children;

        tree.Selected = kids[1];
        tree.RemoveSelectedCommand.Execute(null);
        Assert.Same(kids[1], tree.Selected);
        Assert.Equal("Deep", tree.Selected!.Name);

        tree.Selected = kids[^1];
        tree.RemoveSelectedCommand.Execute(null);
        Assert.Equal("Deep", tree.Selected!.Name);

        tree.Selected = kids[1];
        tree.RemoveSelectedCommand.Execute(null);
        Assert.Equal("&Projects", tree.Selected!.Name);

        tree.RemoveSelectedCommand.Execute(null);
        Assert.Same(group, tree.Selected);
        Assert.Empty(group.Children);
    }

    [Fact]
    public void RemoveSelected_NothingSelected_CannotExecute()
    {
        var tree = LoadedTree();
        Assert.False(tree.RemoveSelectedCommand.CanExecute(null));
        tree.Selected = tree.Roots[0];
        Assert.True(tree.RemoveSelectedCommand.CanExecute(null));
    }

    [Fact]
    public void MoveUpMoveDown_StayWithinSiblingsAndStopAtEdges()
    {
        var tree = LoadedTree();
        var first = tree.Roots[0];
        var last = tree.Roots[^1];

        tree.Selected = first;
        Assert.False(tree.MoveUpCommand.CanExecute(null));
        tree.MoveUp();
        Assert.Same(first, tree.Roots[0]);

        tree.MoveDown();
        Assert.Same(first, tree.Roots[1]);
        Assert.Same(first, tree.Selected);
        Assert.True(first.IsSelected);

        tree.Selected = last;
        Assert.False(tree.MoveDownCommand.CanExecute(null));
        tree.MoveDown();
        Assert.Same(last, tree.Roots[^1]);

        tree.MoveUp();
        Assert.Same(last, tree.Roots[^2]);
    }

    [Fact]
    public void Move_IntoGroup_ReparentsAndExpands()
    {
        var tree = LoadedTree();
        var home = tree.Roots[0];
        var work = tree.Roots[1];
        work.IsExpanded = false;

        Assert.True(tree.Move(home, work, 1));

        Assert.DoesNotContain(home, tree.Roots);
        Assert.Same(home, work.Children[1]);
        Assert.Same(work, home.Parent);
        Assert.True(work.IsExpanded);
    }

    [Fact]
    public void Move_ToRoots_ClearsParent()
    {
        var tree = LoadedTree();
        var projects = tree.Roots[1].Children[0];

        Assert.True(tree.Move(projects, null, 0));

        Assert.Same(projects, tree.Roots[0]);
        Assert.Null(projects.Parent);
        Assert.DoesNotContain(projects, tree.Roots[2].Children);
    }

    [Fact]
    public void Move_IntoOwnDescendantSelfOrNonGroup_IsRefusedAndChangesNothing()
    {
        var tree = LoadedTree();
        var work = tree.Roots[1];
        var deep = work.Children[2];
        var before = Names(tree.Roots).Concat(Names(work.Children)).ToArray();
        int changes = 0;
        tree.Changed += () => changes++;

        Assert.False(tree.Move(work, deep, 0));
        Assert.False(tree.Move(work, work, 0));
        Assert.False(tree.Move(tree.Roots[0], tree.Roots[3], 0));

        Assert.Equal(before, Names(tree.Roots).Concat(Names(work.Children)).ToArray());
        Assert.Equal(0, changes);
        Assert.True(tree.IsAncestor(work, deep.Children[0]));
        Assert.False(tree.IsAncestor(deep, work));
    }

    [Fact]
    public void Move_WithinSameParent_AccountsForIndexShift()
    {
        var tree = LoadedTree();
        var a = tree.Roots[0];
        var b = tree.Roots[1];
        var c = tree.Roots[2];
        var d = tree.Roots[3];

        // Dropping before index 3 while moving from 0 puts it right before d.
        Assert.True(tree.Move(a, null, 3));
        Assert.Equal([b, c, a, d], tree.Roots);

        // Moving upward needs no shift.
        Assert.True(tree.Move(a, null, 0));
        Assert.Equal([a, b, c, d], tree.Roots);

        // Index past the end clamps to the end.
        Assert.True(tree.Move(a, null, 99));
        Assert.Equal([b, c, d, a], tree.Roots);
    }

    [Fact]
    public void Changed_RaisedForNestedRenameAndStructure_NotForExpandOrSelect()
    {
        var tree = LoadedTree();
        int changes = 0;
        tree.Changed += () => changes++;
        var nested = tree.Roots[1].Children[2].Children[0];

        nested.IsExpanded = !nested.IsExpanded;
        nested.IsSelected = true;
        tree.Selected = tree.Roots[0];
        Assert.Equal(0, changes);

        nested.Name = "Renamed";
        Assert.Equal(1, changes);

        nested.Path = @"D:\other";
        Assert.Equal(2, changes);

        tree.Roots[1].Children[2].Children.Add(FavoriteNodeVm.NewSeparator());
        Assert.Equal(3, changes);

        tree.Selected = tree.Roots[0];
        tree.MoveDown();
        Assert.Equal(4, changes);
    }

    [Fact]
    public void Changed_NotRaisedByLoad_AndOldNodesAreDetached()
    {
        var tree = LoadedTree();
        var oldNested = tree.Roots[1].Children[2].Children[0];
        int changes = 0;
        tree.Changed += () => changes++;

        tree.Load(SampleConfig());
        Assert.Equal(0, changes);

        oldNested.Name = "ghost";
        Assert.Equal(0, changes);

        tree.Roots[1].Children[2].Children[0].Name = "live";
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Changed_RaisedOnceForCompoundOperation()
    {
        var tree = LoadedTree();
        int changes = 0;
        tree.Changed += () => changes++;

        tree.Move(tree.Roots[0], tree.Roots[1], 0);

        Assert.Equal(1, changes);
    }

    [Fact]
    public void ToConfig_ReflectsEdits()
    {
        var tree = LoadedTree();
        tree.Selected = tree.Roots[0];
        tree.AddGroup();
        tree.Selected!.Name = "Fresh";

        var config = tree.ToConfig();

        Assert.Equal("Fresh", config[1].Name);
        Assert.NotNull(config[1].Items);
        Assert.Empty(config[1].Items!);
    }
}
