using Folderite.Config;
using Folderite.History;
using Folderite.Settings.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace Folderite.Tests;

public sealed class SettingsVmTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ConfigStore _store;

    public SettingsVmTests()
    {
        _store = new ConfigStore(_temp.Root);
    }

    public void Dispose()
    {
        _store.Dispose();
        _temp.Dispose();
    }

    private string ConfigPath => Path.Combine(_temp.Root, "config.json");

    private void WriteStoreConfig(AppConfig config)
    {
        _store.Save(config);
        _store.Load();
    }

    private static AppConfig SampleConfig() => new()
    {
        RecentCount = 9,
        ShowClipboard = false,
        EnableOnDesktop = false,
        HistoryExclude = [@"C:\Windows\*"],
        DisabledProcesses = ["game"],
        Actions = [new() { Name = "&Code", Command = "code.exe", Args = "\"{path}\"" }],
        Favorites =
        [
            new() { Name = "&Home", Path = @"C:\Users\me" },
            new() { Name = "G", Items = [new() { Name = "-" }] },
        ],
    };

    private SettingsVm NewVm(AppConfig? config = null, bool load = true)
    {
        WriteStoreConfig(config ?? SampleConfig());
        var vm = new SettingsVm(_store, a => a(), _time);
        if (load)
            vm.Load();
        return vm;
    }

    private AppConfig ReadFromDisk()
    {
        using var store = new ConfigStore(_temp.Root);
        return store.Load();
    }

    [Fact]
    public void Load_PopulatesFieldsWithoutScheduling()
    {
        var vm = NewVm();

        Assert.Equal(9, vm.RecentCount);
        Assert.True(vm.ShowOpenWindows);
        Assert.False(vm.ShowClipboard);
        Assert.False(vm.EnableOnDesktop);
        Assert.Equal(@"C:\Windows\*", Assert.Single(vm.HistoryExclude).Value);
        Assert.Equal("game", Assert.Single(vm.DisabledProcesses).Value);
        Assert.Equal("Code", Assert.Single(vm.Actions).DisplayName);
        Assert.Equal(2, vm.Favorites.Roots.Count);
        Assert.Null(vm.LoadError);
        Assert.True(vm.AutosaveEnabled);
        Assert.Equal("", vm.Status);
        Assert.False(vm.HasPendingSave);
    }

    [Fact]
    public void BuildConfig_PreservesUntouchedValues()
    {
        var original = SampleConfig();
        var vm = NewVm(original);

        var built = vm.BuildConfig();

        Assert.Equal(9, built.RecentCount);
        Assert.True(built.ShowOpenWindows);
        Assert.False(built.ShowClipboard);
        Assert.False(built.EnableOnDesktop);
        Assert.Equal(original.HistoryExclude, built.HistoryExclude);
        Assert.Equal(original.DisabledProcesses, built.DisabledProcesses);
        Assert.Equal("&Code", built.Actions[0].Name);
        Assert.Equal("code.exe", built.Actions[0].Command);
        Assert.Equal("&Home", built.Favorites[0].Name);
        Assert.Equal("-", built.Favorites[1].Items![0].Name);
        Assert.NotSame(_store.Current, built);
    }

    [Fact]
    public void BuildConfig_TrimsAndSkipsBlankStrings()
    {
        var vm = NewVm();
        vm.HistoryExclude.Add(new StringItemVm("   "));
        vm.HistoryExclude.Add(new StringItemVm("  D:\\tmp\\*  "));
        vm.DisabledProcesses.Add(new StringItemVm(""));

        var built = vm.BuildConfig();

        Assert.Equal([@"C:\Windows\*", @"D:\tmp\*"], built.HistoryExclude);
        Assert.Equal(["game"], built.DisabledProcesses);
    }

    [Fact]
    public void RecentCount_IsClamped()
    {
        var vm = NewVm();

        vm.RecentCount = 500;
        Assert.Equal(50, vm.RecentCount);
        vm.RecentCount = -3;
        Assert.Equal(0, vm.RecentCount);
    }

    [Fact]
    public void TwoQuickEdits_SaveOnceAfter400ms_WithBothChanges()
    {
        var vm = NewVm();

        vm.RecentCount = 3;
        _time.Advance(TimeSpan.FromMilliseconds(300));
        vm.ShowOpenWindows = false;

        Assert.True(vm.HasPendingSave);
        _time.Advance(TimeSpan.FromMilliseconds(399));
        Assert.Equal(9, ReadFromDisk().RecentCount);
        Assert.True(vm.HasPendingSave);

        _time.Advance(TimeSpan.FromMilliseconds(1));

        var saved = ReadFromDisk();
        Assert.Equal(3, saved.RecentCount);
        Assert.False(saved.ShowOpenWindows);
        Assert.False(vm.HasPendingSave);
        Assert.Equal("Saved", vm.Status);
    }

    [Fact]
    public void NoSaveBefore400ms()
    {
        var vm = NewVm();

        vm.RecentCount = 4;
        _time.Advance(TimeSpan.FromMilliseconds(399));

        Assert.Equal(9, ReadFromDisk().RecentCount);
    }

    [Fact]
    public void EditsToCollectionsAndItems_ScheduleSave()
    {
        var vm = NewVm();

        vm.Actions[0].Command = "wt.exe";
        _time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Equal("wt.exe", ReadFromDisk().Actions[0].Command);

        vm.HistoryExclude[0].Value = @"E:\*";
        _time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Equal([@"E:\*"], ReadFromDisk().HistoryExclude);

        vm.Favorites.Roots[0].Name = "Renamed";
        _time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.Equal("Renamed", ReadFromDisk().Favorites[0].Name);
    }

    [Fact]
    public void RemovedItems_NoLongerScheduleSaves()
    {
        var vm = NewVm();
        var item = vm.HistoryExclude[0];
        vm.RemoveExcludeCommand.Execute(item);
        _time.Advance(TimeSpan.FromMilliseconds(400));
        Assert.False(vm.HasPendingSave);

        item.Value = "changed";

        Assert.False(vm.HasPendingSave);
    }

    [Fact]
    public void SaveNow_FlushesPendingSnapshot()
    {
        var vm = NewVm();
        vm.RecentCount = 12;

        vm.SaveNow();

        Assert.Equal(12, ReadFromDisk().RecentCount);
        Assert.False(vm.HasPendingSave);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(12, ReadFromDisk().RecentCount);
    }

    [Fact]
    public void LoadError_DisablesAutosave_AndEditsDoNotWriteTheFile()
    {
        File.WriteAllText(ConfigPath, "{ this is not json");
        _store.Load();
        Assert.NotNull(_store.LastError);
        var vm = new SettingsVm(_store, a => a(), _time);

        vm.Load();
        Assert.NotNull(vm.LoadError);
        Assert.False(vm.AutosaveEnabled);
        Assert.Equal("Autosave is off: config.json has errors", vm.Status);

        vm.RecentCount = 2;
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.False(vm.HasPendingSave);
        Assert.Equal("{ this is not json", File.ReadAllText(ConfigPath));
        Assert.Equal("Not saved: fix config.json or reload", vm.Status);
    }

    [Theory]
    [InlineData(true)]  // broken before the edit
    [InlineData(false)] // broken while the save is pending
    public void FileBrokenAfterOpening_StopsAutosave_AndKeepsTheFile(bool beforeEdit)
    {
        var vm = NewVm();
        Assert.True(vm.AutosaveEnabled);

        // Simulates a hand edit that the store's hot reload failed to parse.
        if (beforeEdit)
        {
            File.WriteAllText(ConfigPath, "{ half typed");
            _store.Load();
        }
        vm.RecentCount = 3;
        if (!beforeEdit)
        {
            File.WriteAllText(ConfigPath, "{ half typed");
            _store.Load();
        }
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal("{ half typed", File.ReadAllText(ConfigPath));
        Assert.False(vm.AutosaveEnabled);
        Assert.NotNull(vm.LoadError);
        Assert.False(vm.HasPendingSave);
    }

    [Fact]
    public void Reload_AfterFixingFile_ReenablesAutosave()
    {
        File.WriteAllText(ConfigPath, "{ nope");
        _store.Load();
        var vm = new SettingsVm(_store, a => a(), _time);
        vm.Load();

        File.WriteAllText(ConfigPath, """{ "recentCount": 4 }""");
        vm.Reload();

        Assert.Null(vm.LoadError);
        Assert.True(vm.AutosaveEnabled);
        Assert.Equal(4, vm.RecentCount);
        Assert.Equal("", vm.Status);
    }

    [Fact]
    public void SaveFailure_PostsNotSavedStatus()
    {
        var vm = NewVm();
        // A directory where config.json should go makes the atomic move fail.
        File.Delete(ConfigPath);
        Directory.CreateDirectory(ConfigPath);

        vm.RecentCount = 2;
        _time.Advance(TimeSpan.FromMilliseconds(400));

        Assert.StartsWith("Not saved: ", vm.Status);
    }

    [Fact]
    public void OnExternalChange_ReloadsWhenNothingPending()
    {
        var vm = NewVm();
        vm.SelectedAction = vm.Actions[0];
        var changed = SampleConfig();
        changed.RecentCount = 21;
        changed.Actions.Add(new() { Name = "B", Command = "b.exe" });
        WriteStoreConfig(changed);

        vm.OnExternalChange();

        Assert.Equal(21, vm.RecentCount);
        Assert.Equal(2, vm.Actions.Count);
        Assert.Same(vm.Actions[0], vm.SelectedAction);
        Assert.False(vm.HasPendingSave);
    }

    [Fact]
    public void OnExternalChange_IgnoredWhileSavePending()
    {
        var vm = NewVm();
        vm.ShowOpenWindows = false;
        var changed = SampleConfig();
        changed.RecentCount = 21;
        WriteStoreConfig(changed);

        vm.OnExternalChange();

        Assert.Equal(9, vm.RecentCount);
        Assert.False(vm.ShowOpenWindows);
        Assert.True(vm.HasPendingSave);
    }

    [Fact]
    public void AddDisabledProcess_NormalisesAndSkipsDuplicates()
    {
        var vm = NewVm();

        vm.AddDisabledProcessCommand.Execute("  Notepad.EXE ");
        vm.AddDisabledProcessCommand.Execute("notepad");
        vm.AddDisabledProcessCommand.Execute("GAME.exe");
        vm.AddDisabledProcessCommand.Execute("   ");
        vm.AddDisabledProcessCommand.Execute(".exe");

        Assert.Equal(["game", "Notepad"], vm.DisabledProcesses.Select(p => p.Value));
    }

    [Fact]
    public void ActionCommands_AddRemoveAndMove()
    {
        var vm = NewVm();

        vm.AddActionCommand.Execute(null);
        var added = vm.Actions[^1];
        Assert.Equal("New action", added.Name);
        Assert.Equal("\"{path}\"", added.Args);
        Assert.Same(added, vm.SelectedAction);

        vm.MoveActionUpCommand.Execute(null);
        Assert.Same(added, vm.Actions[0]);
        Assert.Same(added, vm.SelectedAction);
        Assert.False(vm.MoveActionUpCommand.CanExecute(null));

        vm.MoveActionDownCommand.Execute(null);
        Assert.Same(added, vm.Actions[1]);

        vm.RemoveActionCommand.Execute(null);
        Assert.Single(vm.Actions);
        Assert.Same(vm.Actions[0], vm.SelectedAction);
    }
}

public sealed class HistoryVmTests
{
    private sealed class FakeBackend : IHistoryBackend
    {
        public List<HistoryEntry> Entries { get; } = [];
        public List<string> Removed { get; } = [];
        public int Clears { get; private set; }
        public int Loads { get; private set; }

        public IReadOnlyList<HistoryEntry> Load()
        {
            Loads++;
            return [.. Entries];
        }

        public void Remove(string path)
        {
            Removed.Add(path);
            Entries.RemoveAll(e => e.Path == path);
        }

        public void Clear()
        {
            Clears++;
            Entries.Clear();
        }
    }

    private static readonly DateTimeOffset Now = new(2026, 5, 10, 12, 0, 0, TimeSpan.Zero);

    private static (HistoryVm Vm, FakeBackend Backend) Create()
    {
        var backend = new FakeBackend();
        backend.Entries.Add(new(@"C:\Projects\Alpha", 8, Now.AddMinutes(-5)));
        backend.Entries.Add(new(@"D:\Media\Photos", 4, Now.AddDays(-3)));
        backend.Entries.Add(new(@"C:\projects\Beta", 2, Now.AddDays(-40)));
        var vm = new HistoryVm(backend, new FakeTime(Now));
        vm.Refresh();
        return (vm, backend);
    }

    [Fact]
    public void Refresh_LoadsRowsWithNameAndParent()
    {
        var (vm, _) = Create();

        Assert.Equal(3, vm.TotalCount);
        Assert.Equal(3, vm.Rows.Count);
        Assert.Equal("Alpha", vm.Rows[0].Name);
        Assert.Equal(@"C:\Projects", vm.Rows[0].Parent);
        Assert.Equal(8, vm.Rows[0].Score);
    }

    [Fact]
    public void Filter_IsCaseInsensitiveSubstringOnPath()
    {
        var (vm, _) = Create();

        vm.Filter = "PROJECTS";
        Assert.Equal(["Alpha", "Beta"], vm.Rows.Select(r => r.Name));
        Assert.Equal(3, vm.TotalCount);

        vm.Filter = @"d:\media";
        Assert.Equal("Photos", Assert.Single(vm.Rows).Name);

        vm.Filter = "";
        Assert.Equal(3, vm.Rows.Count);
    }

    [Fact]
    public void Remove_CallsBackendAndRefreshes_DefaultingToSelected()
    {
        var (vm, backend) = Create();
        int loads = backend.Loads;
        vm.Selected = vm.Rows[1];

        vm.RemoveCommand.Execute(null);

        Assert.Equal([@"D:\Media\Photos"], backend.Removed);
        Assert.Equal(loads + 1, backend.Loads);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal(2, vm.TotalCount);
    }

    [Fact]
    public void Remove_WithExplicitRow_UsesThatRow()
    {
        var (vm, backend) = Create();

        vm.RemoveCommand.Execute(vm.Rows[0]);

        Assert.Equal([@"C:\Projects\Alpha"], backend.Removed);
    }

    [Fact]
    public void Remove_NothingSelected_DoesNothing()
    {
        var (vm, backend) = Create();

        vm.RemoveCommand.Execute(null);

        Assert.Empty(backend.Removed);
    }

    [Fact]
    public void ClearAll_ClearsBackendAndRows()
    {
        var (vm, backend) = Create();

        vm.ClearAllCommand.Execute(null);

        Assert.Equal(1, backend.Clears);
        Assert.Empty(vm.Rows);
        Assert.Equal(0, vm.TotalCount);
    }

    [Theory]
    [InlineData(0, "just now")]
    [InlineData(30, "just now")]
    [InlineData(5 * 60, "5 min ago")]
    [InlineData(59 * 60, "59 min ago")]
    [InlineData(3 * 3600, "3 h ago")]
    [InlineData(30 * 3600, "yesterday")]
    [InlineData(4 * 86400, "4 days ago")]
    [InlineData(30 * 86400, "30 days ago")]
    public void LastUsedText_Buckets(int secondsAgo, string expected)
    {
        var row = new HistoryRowVm(new(@"C:\x", 1, Now.AddSeconds(-secondsAgo)), new FakeTime(Now));

        Assert.Equal(expected, row.LastUsedText);
    }

    [Fact]
    public void LastUsedText_BeyondThirtyDays_IsADate()
    {
        var access = Now.AddDays(-45);
        var row = new HistoryRowVm(new(@"C:\x", 1, access), new FakeTime(Now));

        Assert.Equal(access.ToLocalTime().ToString("yyyy-MM-dd"), row.LastUsedText);
    }
}
