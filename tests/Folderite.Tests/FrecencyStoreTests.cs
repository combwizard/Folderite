using System.Text.Json;
using Folderite.History;

namespace Folderite.Tests;

public sealed class FrecencyStoreTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new();
    private readonly FakeTime _time = new(Start);

    private string FilePath => Path.Combine(_temp.Root, "history.json");

    public void Dispose() => _temp.Dispose();

    private FrecencyStore NewStore() => new(FilePath, _time);

    private void Advance(TimeSpan by) => _time.Now += by;

    /// <summary>Writes a history.json with exact ranks and timestamps, for ordering tests that need precise scores.</summary>
    private void Seed(params (string Path, double Rank, DateTimeOffset LastAccess)[] entries)
    {
        var model = new
        {
            version = 1,
            entries = entries.Select(e => new { path = e.Path, rank = e.Rank, lastAccess = e.LastAccess }).ToArray(),
        };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(model));
    }

    private List<(string Path, double Rank)> ReadFile()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
        return doc.RootElement.GetProperty("entries").EnumerateArray()
            .Select(e => (e.GetProperty("path").GetString()!, e.GetProperty("rank").GetDouble()))
            .ToList();
    }

    /// <summary>Like <see cref="Seed"/> but writes a version 2 file with per-app use.</summary>
    private void SeedApps(params (string Path, double Rank, DateTimeOffset LastAccess, (string App, double Rank, DateTimeOffset LastAccess)[] Apps)[] entries)
    {
        var model = new
        {
            version = 2,
            entries = entries.Select(e => new
            {
                path = e.Path,
                rank = e.Rank,
                lastAccess = e.LastAccess,
                apps = e.Apps.Length == 0
                    ? null
                    : e.Apps.ToDictionary(a => a.App, a => new { rank = a.Rank, lastAccess = a.LastAccess }),
            }).ToArray(),
        };
        File.WriteAllText(FilePath, JsonSerializer.Serialize(model));
    }

    /// <summary>Path to (app to rank) as stored in the file; entries without app data map to an empty dictionary.</summary>
    private Dictionary<string, Dictionary<string, double>> ReadApps()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
        return doc.RootElement.GetProperty("entries").EnumerateArray().ToDictionary(
            e => e.GetProperty("path").GetString()!,
            e => e.TryGetProperty("apps", out var apps)
                ? apps.EnumerateObject().ToDictionary(a => a.Name, a => a.Value.GetProperty("rank").GetDouble())
                : new Dictionary<string, double>());
    }

    /// <summary>Records <paramref name="times"/> visits, 3 s apart so none is debounced.</summary>
    private void RecordTimes(FrecencyStore store, string path, int times, string? app = null)
    {
        for (var i = 0; i < times; i++)
        {
            store.Record(path, app);
            Advance(TimeSpan.FromSeconds(3));
        }
    }

    [Fact]
    public void Top_MoreVisitsRankHigher()
    {
        var store = NewStore();
        store.Record(@"C:\Projects\Alpha");
        Advance(TimeSpan.FromSeconds(3));
        store.Record(@"C:\Projects\Beta");
        Advance(TimeSpan.FromSeconds(3));
        store.Record(@"C:\Projects\Beta");
        Advance(TimeSpan.FromSeconds(3));
        store.Record(@"C:\Projects\Beta");

        Assert.Equal(new[] { @"C:\Projects\Beta", @"C:\Projects\Alpha" }, store.Top(2));
    }

    [Fact]
    public void Top_TakesOnlyRequestedCount()
    {
        var store = NewStore();
        store.Record(@"C:\A");
        Advance(TimeSpan.FromSeconds(3));
        store.Record(@"C:\B");

        Assert.Single(store.Top(1));
    }

    [Fact]
    public void Top_ZeroCount_ReturnsEmpty()
    {
        var store = NewStore();
        store.Record(@"C:\A");

        Assert.Empty(store.Top(0));
    }

    // Probe: rank 1 at the age under test scores exactly `multiplier`. Rivals at age 0 (x4) score 0.9x and 1.1x of it.
    [Theory]
    [InlineData(30, 4.0)]            // under 1 hour
    [InlineData(5 * 60, 2.0)]        // under 1 day
    [InlineData(3 * 24 * 60, 0.5)]   // under 1 week
    [InlineData(10 * 24 * 60, 0.25)] // older
    public void Top_AppliesRecencyMultiplier(int ageMinutes, double multiplier)
    {
        Seed(
            (@"C:\Target", 1.0, Start - TimeSpan.FromMinutes(ageMinutes)),
            (@"C:\Under", 0.9 * multiplier / 4, Start),
            (@"C:\Over", 1.1 * multiplier / 4, Start));
        var store = NewStore();
        store.Load();

        Assert.Equal(new[] { @"C:\Over", @"C:\Target", @"C:\Under" }, store.Top(3));
    }

    [Fact]
    public void Record_WithinTwoSeconds_IsIgnored()
    {
        var store = NewStore();
        store.Record(@"C:\A");
        Advance(TimeSpan.FromSeconds(1));
        store.Record(@"C:\A");
        store.Save();

        Assert.Equal(1.0, Assert.Single(ReadFile()).Rank);
    }

    [Fact]
    public void Record_AfterTwoSeconds_CountsAgain()
    {
        var store = NewStore();
        store.Record(@"C:\A");
        Advance(TimeSpan.FromSeconds(3));
        store.Record(@"C:\A");
        store.Save();

        Assert.Equal(2.0, Assert.Single(ReadFile()).Rank);
    }

    [Fact]
    public void Record_CaseInsensitiveDuplicate_KeepsLatestCasing()
    {
        var store = NewStore();
        store.Record(@"C:\Projects\Foo");
        Advance(TimeSpan.FromMinutes(1));
        store.Record(@"c:\projects\FOO");

        Assert.Equal(@"c:\projects\FOO", Assert.Single(store.Top(10)));
    }

    [Fact]
    public void Record_TrailingBackslash_IsNormalized()
    {
        var store = NewStore();
        store.Record(@"C:\Projects\Foo\");

        Assert.Equal(@"C:\Projects\Foo", Assert.Single(store.Top(10)));
    }

    [Fact]
    public void Record_DriveRoot_KeepsTrailingBackslash()
    {
        var store = NewStore();
        store.Record(@"C:\");

        Assert.Equal(@"C:\", Assert.Single(store.Top(10)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(@"relative\folder")]
    [InlineData(@"C:relative")]
    [InlineData("::{20D04FE0-3AEA-1069-A2D8-08002B30309D}")]
    public void Record_NonFilesystemOrRelativePath_IsIgnored(string path)
    {
        var store = NewStore();
        store.Record(path);

        Assert.Empty(store.Top(10));
        Assert.False(store.IsDirty);
    }

    [Fact]
    public void Remove_DropsEntry()
    {
        var store = NewStore();
        store.Record(@"C:\A");
        store.Remove(@"C:\A");

        Assert.Empty(store.Top(10));
    }

    [Fact]
    public void Remove_AcceptsTrailingBackslash()
    {
        var store = NewStore();
        store.Record(@"C:\A");
        store.Remove(@"C:\A\");

        Assert.Empty(store.Top(10));
    }

    [Fact]
    public void SetExcludes_StarWildcard_ExcludesSubfoldersFromRecord()
    {
        var store = NewStore();
        store.SetExcludes([@"C:\Windows\*"]);
        store.Record(@"C:\Windows\System32");
        store.Record(@"C:\Work");

        Assert.Equal(new[] { @"C:\Work" }, store.Top(10));
    }

    [Fact]
    public void SetExcludes_QuestionMarkWildcard_MatchesExactlyOneCharacter()
    {
        var store = NewStore();
        store.SetExcludes([@"C:\Temp\?"]);
        store.Record(@"C:\Temp\A");
        Advance(TimeSpan.FromSeconds(3));
        store.Record(@"C:\Temp\AB");

        Assert.Equal(new[] { @"C:\Temp\AB" }, store.Top(10));
    }

    [Fact]
    public void SetExcludes_IsCaseInsensitive()
    {
        var store = NewStore();
        store.SetExcludes([@"c:\windows\*"]);
        store.Record(@"C:\Windows\System32");

        Assert.Empty(store.Top(10));
    }

    [Fact]
    public void Top_FiltersExcludedEntriesLoadedFromDisk()
    {
        Seed(
            (@"C:\Windows\System32", 5, Start),
            (@"C:\Work", 1, Start));
        var store = NewStore();
        store.Load();
        store.SetExcludes([@"C:\Windows\*"]);

        Assert.Equal(new[] { @"C:\Work" }, store.Top(10));
    }

    [Fact]
    public void SaveThenLoad_InNewInstance_RoundTrips()
    {
        var store = NewStore();
        store.Record(@"C:\Alpha");
        Advance(TimeSpan.FromSeconds(3));
        store.Record(@"C:\Alpha");
        Advance(TimeSpan.FromSeconds(3));
        store.Record(@"C:\Beta");
        store.Save();

        var reloaded = NewStore();
        reloaded.Load();

        Assert.Equal(store.Top(10), reloaded.Top(10));
        Assert.Equal(2, reloaded.Top(10).Count);
    }

    [Fact]
    public void Load_CorruptFile_YieldsEmptyStoreWithoutThrowing()
    {
        File.WriteAllText(FilePath, "{ this is not json");
        var store = NewStore();

        var ex = Record.Exception(() => store.Load());

        Assert.Null(ex);
        Assert.Empty(store.Top(10));
    }

    [Fact]
    public void IsDirty_FreshStore_IsFalse()
    {
        Assert.False(NewStore().IsDirty);
    }

    [Fact]
    public void IsDirty_AfterRecord_IsTrue()
    {
        var store = NewStore();
        store.Record(@"C:\A");

        Assert.True(store.IsDirty);
    }

    [Fact]
    public void IsDirty_AfterSave_IsFalse()
    {
        var store = NewStore();
        store.Record(@"C:\A");
        store.Save();

        Assert.False(store.IsDirty);
    }

    [Fact]
    public void IsDirty_AfterLoad_IsFalse()
    {
        var store = NewStore();
        store.Record(@"C:\A");
        store.Load();

        Assert.False(store.IsDirty);
    }

    [Fact]
    public void IsDirty_IgnoredRecordWithinDebounce_StaysFalse()
    {
        var store = NewStore();
        store.Record(@"C:\A");
        store.Save();
        Advance(TimeSpan.FromSeconds(1));
        store.Record(@"C:\A");

        Assert.False(store.IsDirty);
    }

    [Fact]
    public void IsDirty_AfterRemoveOfKnownEntry_IsTrue()
    {
        var store = NewStore();
        store.Record(@"C:\A");
        store.Save();
        store.Remove(@"C:\A");

        Assert.True(store.IsDirty);
    }

    [Fact]
    public void Record_WhenTotalRankExceedsCap_StillKeepsNewlyRecordedFolder()
    {
        // 1000 entries at rank 10 = total 10000, above the 9000 cap, so the next Record triggers aging.
        Seed(Enumerable.Range(0, 1000)
            .Select(i => ($@"C:\Old\{i}", 10.0, Start - TimeSpan.FromDays(1)))
            .ToArray());
        var store = NewStore();
        store.Load();

        store.Record(@"C:\Fresh");

        Assert.Contains(@"C:\Fresh", store.Top(1000));
    }

    [Fact]
    public void Entries_OrderMatchesTop_AndCarriesScore()
    {
        Seed((@"C:\Old", 10, Start - TimeSpan.FromDays(30)),
            (@"C:\Recent", 2, Start - TimeSpan.FromMinutes(5)),
            (@"C:\Today", 3, Start - TimeSpan.FromHours(5)));
        var store = NewStore();
        store.Load();

        var entries = store.Entries();

        Assert.Equal(store.Top(10), entries.Select(e => e.Path));
        Assert.Equal(new[] { 8.0, 6.0, 2.5 }, entries.Select(e => e.Score));
        Assert.Equal(Start - TimeSpan.FromMinutes(5), entries[0].LastAccess);
    }

    [Fact]
    public void Entries_SkipsExcludedFolders()
    {
        var store = NewStore();
        store.Record(@"C:\Keep");
        store.Record(@"C:\Secret\Stuff");
        store.SetExcludes([@"C:\Secret\*"]);

        Assert.Equal(new[] { @"C:\Keep" }, store.Entries().Select(e => e.Path));
    }

    [Fact]
    public void Clear_RemovesEverythingAndMarksDirty()
    {
        var store = NewStore();
        store.Record(@"C:\A");
        store.Save();
        Assert.False(store.IsDirty);

        store.Clear();

        Assert.True(store.IsDirty);
        Assert.Empty(store.Entries());
        Assert.Empty(store.Top(10));
    }

    [Fact]
    public void Top_IgnoresAppUse()
    {
        var store = NewStore();
        RecordTimes(store, @"C:\Elsewhere", 4);
        RecordTimes(store, @"C:\InBlender", 3, "blender");

        Assert.Equal(new[] { @"C:\Elsewhere", @"C:\InBlender" }, store.Top(2));
    }

    [Fact]
    public void TopInApp_ListsOnlyFoldersUsedInThatApp()
    {
        var store = NewStore();
        RecordTimes(store, @"C:\Elsewhere", 20);
        RecordTimes(store, @"C:\InBlender", 3, "blender");
        store.Record(@"C:\InNotepad", "notepad");

        Assert.Equal(new[] { @"C:\InBlender" }, store.TopInApp(10, "blender"));
        Assert.Equal(new[] { @"C:\InNotepad" }, store.TopInApp(10, "notepad"));
        Assert.Empty(store.TopInApp(10, "zbrush"));
    }

    [Fact]
    public void TopInApp_RanksByUseInThatAppNotOverall()
    {
        var store = NewStore();
        RecordTimes(store, @"C:\PopularElsewhere", 10);
        Advance(TimeSpan.FromSeconds(3));
        store.Record(@"C:\PopularElsewhere", "blender");
        Advance(TimeSpan.FromSeconds(3));
        RecordTimes(store, @"C:\BlenderOften", 3, "blender");

        Assert.Equal(new[] { @"C:\PopularElsewhere", @"C:\BlenderOften" }, store.Top(2));
        Assert.Equal(new[] { @"C:\BlenderOften", @"C:\PopularElsewhere" }, store.TopInApp(2, "blender"));
    }

    [Fact]
    public void TopInApp_AppNameIsCaseAndExeInsensitive()
    {
        var store = NewStore();
        RecordTimes(store, @"C:\InBlender", 3, "Blender.EXE");

        Assert.Equal(new[] { @"C:\InBlender" }, store.TopInApp(1, "blender"));
        Assert.Equal(new[] { @"C:\InBlender" }, store.TopInApp(1, "BLENDER.exe"));
    }

    [Fact]
    public void TopInApp_BlankAppOrZeroCount_IsEmpty()
    {
        var store = NewStore();
        store.Record(@"C:\A", "blender");

        Assert.Empty(store.TopInApp(10, ""));
        Assert.Empty(store.TopInApp(10, "  "));
        Assert.Empty(store.TopInApp(0, "blender"));
    }

    [Fact]
    public void TopInApp_SkipsExcludedPaths()
    {
        var store = NewStore();
        store.Record(@"C:\Keep", "blender");
        store.Record(@"C:\Temp\Drop", "blender");
        store.SetExcludes([@"C:\Temp\*"]);

        Assert.Equal(new[] { @"C:\Keep" }, store.TopInApp(10, "blender"));
    }

    [Fact]
    public void TopInApp_TiesGoToMostRecentUseInThatApp()
    {
        // Same app rank and time bucket; only the app's LastAccess differs, and it runs the other way to the global one.
        SeedApps(
            (@"C:\Older", 2, Start - TimeSpan.FromMinutes(5), [("blender", 1.0, Start - TimeSpan.FromMinutes(20))]),
            (@"C:\Newer", 2, Start - TimeSpan.FromMinutes(10), [("blender", 1.0, Start - TimeSpan.FromMinutes(15))]));
        var store = NewStore();
        store.Load();

        Assert.Equal(new[] { @"C:\Newer", @"C:\Older" }, store.TopInApp(2, "blender"));
    }

    [Fact]
    public void Record_PlaceInsideArchive_IsIgnored()
    {
        var zip = Path.Combine(_temp.Root, "tools.zip");
        File.WriteAllBytes(zip, [0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        var store = NewStore();
        store.Record(zip);
        store.Record(Path.Combine(zip, "bin"), "dopus");
        store.Record(Path.Combine(zip, "bin", "x64"));

        Assert.Empty(store.Top(10));
        Assert.Empty(store.TopInApp(10, "dopus"));
        Assert.False(store.IsDirty);
    }

    [Fact]
    public void Record_FolderNamedLikeAnArchive_IsRecorded()
    {
        var folder = Path.Combine(_temp.Root, "backup.zip");
        Directory.CreateDirectory(Path.Combine(folder, "inner"));
        var store = NewStore();
        store.Record(folder);
        Advance(TimeSpan.FromSeconds(3));
        store.Record(Path.Combine(folder, "inner"));

        Assert.Equal(2, store.Top(10).Count);
    }

    [Fact]
    public void Load_DropsPlacesInsideArchives()
    {
        var zip = Path.Combine(_temp.Root, "tools.7z");
        File.WriteAllText(zip, "not really an archive");
        var deleted = Path.Combine(_temp.Root, "gone.zip");   // the archive was deleted after the visit
        Seed((Path.Combine(zip, "bin"), 3, Start), (Path.Combine(deleted, "src"), 2, Start), (@"C:\Real", 1, Start));
        var store = NewStore();
        store.Load();

        Assert.Equal(new[] { @"C:\Real" }, store.Top(10));
    }

    [Fact]
    public void Record_AppDebounceIsIndependentOfGlobalDebounce()
    {
        var store = NewStore();
        store.Record(@"C:\A", "a");
        Advance(TimeSpan.FromSeconds(1));
        store.Record(@"C:\A", "b");   // global is a repeat, but app b is new
        store.Save();

        Assert.Equal(1.0, Assert.Single(ReadFile()).Rank);
        Assert.Equal(new Dictionary<string, double> { ["a"] = 1, ["b"] = 1 }, ReadApps()[@"C:\A"]);
    }

    [Fact]
    public void Record_GlobalCountsWhileAppIsStillDebounced()
    {
        var store = NewStore();
        store.Record(@"C:\A", "a");                 // t0
        Advance(TimeSpan.FromSeconds(1));
        store.Record(@"C:\A", "b");                 // t1: b counted, global debounced
        Advance(TimeSpan.FromSeconds(1.5));
        store.Record(@"C:\A", "b");                 // t2.5: global is 2.5 s old (counts), b is 1.5 s old (debounced)
        store.Save();

        Assert.Equal(2.0, Assert.Single(ReadFile()).Rank);
        Assert.Equal(new Dictionary<string, double> { ["a"] = 1, ["b"] = 1 }, ReadApps()[@"C:\A"]);
    }

    [Fact]
    public void Record_BothDebounced_ChangesNothingAndStaysClean()
    {
        var store = NewStore();
        store.Record(@"C:\A", "a");
        store.Save();
        Advance(TimeSpan.FromSeconds(1));
        store.Record(@"C:\A", "a");

        Assert.False(store.IsDirty);
    }

    [Fact]
    public void Record_AfterTwoSeconds_CountsAppAgain()
    {
        var store = NewStore();
        store.Record(@"C:\A", "a");
        Advance(TimeSpan.FromSeconds(3));
        store.Record(@"C:\A", "a");
        store.Save();

        Assert.Equal(2.0, ReadApps()[@"C:\A"]["a"]);
    }

    [Fact]
    public void Record_AppUseOnlyChange_MarksDirty()
    {
        var store = NewStore();
        store.Record(@"C:\A");
        store.Save();
        Advance(TimeSpan.FromSeconds(1));
        store.Record(@"C:\A", "a");

        Assert.True(store.IsDirty);
    }

    [Fact]
    public void Record_ExcludedPathWithApp_IsIgnored()
    {
        var store = NewStore();
        store.SetExcludes([@"C:\Secret\*"]);
        store.Record(@"C:\Secret\Stuff", "a");

        Assert.False(store.IsDirty);
        Assert.Null(store.LastInApp("a"));
    }

    [Fact]
    public void Record_WithoutApp_WritesNoAppsProperty()
    {
        var store = NewStore();
        store.Record(@"C:\A");
        store.Save();

        Assert.DoesNotContain("apps", File.ReadAllText(FilePath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Record_MoreThanTenApps_DropsLowestScoreButNeverTheOneJustRecorded()
    {
        // Ten apps with ranks 2..11, all a day old. The new app (rank 1, now) scores lowest of all but must survive;
        // the weakest of the others ("app2") goes instead.
        var old = Start - TimeSpan.FromDays(1);
        SeedApps((@"C:\A", 20, old, Enumerable.Range(2, 10).Select(i => ($"app{i}", (double)i, old)).ToArray()));
        var store = NewStore();
        store.Load();

        store.Record(@"C:\A", "newcomer");
        store.Save();

        var apps = ReadApps()[@"C:\A"];
        Assert.Equal(10, apps.Count);
        Assert.Contains("newcomer", apps.Keys);
        Assert.DoesNotContain("app2", apps.Keys);
        Assert.Contains("app3", apps.Keys);
    }

    [Fact]
    public void Record_WhenTotalRankExceedsCap_ScalesAppRanksAndDropsTinyOnes()
    {
        // 999 entries at rank 10 = 9990, above the 9000 cap, so the next Record ages everything by 0.9.
        // Entry 0 has app ranks 5 (-> 4.5, kept) and 1 (-> 0.9, dropped).
        var old = Start - TimeSpan.FromDays(1);
        SeedApps(Enumerable.Range(0, 999)
            .Select(i => ($@"C:\Old\{i}", 10.0, old,
                i == 0 ? new[] { ("big", 5.0, old), ("tiny", 1.0, old) } : Array.Empty<(string, double, DateTimeOffset)>()))
            .ToArray());
        var store = NewStore();
        store.Load();

        store.Record(@"C:\Fresh", "fresh");
        store.Save();

        var apps = ReadApps();
        Assert.Equal(new[] { "big" }, apps[@"C:\Old\0"].Keys);
        Assert.Equal(4.5, apps[@"C:\Old\0"]["big"], 10);
        Assert.Empty(apps[@"C:\Old\1"]);
        // The visit that triggered aging is spared even though 1 x 0.9 < 1, for the entry and its app use.
        Assert.Equal(0.9, apps[@"C:\Fresh"]["fresh"], 10);
    }

    [Fact]
    public void Record_WhenAgingDropsAnEntry_ItsAppDataGoesToo()
    {
        // 900 x 10 + 1 = 9001 is over the cap; "Weak" falls from 1 to 0.9 and is dropped along with its app use.
        var old = Start - TimeSpan.FromDays(1);
        var seeded = Enumerable.Range(0, 900)
            .Select(i => ($@"C:\Old\{i}", 10.0, old, Array.Empty<(string, double, DateTimeOffset)>()))
            .Append((@"C:\Weak", 1.0, old, new[] { ("a", 1.0, old) }))
            .ToArray();
        SeedApps(seeded);
        var store = NewStore();
        store.Load();

        store.Record(@"C:\Fresh");
        store.Save();

        Assert.False(ReadApps().ContainsKey(@"C:\Weak"));
        Assert.Null(store.LastInApp("a"));
    }

    [Fact]
    public void LastInApp_ReturnsFolderWithLatestUseInThatApp()
    {
        var store = NewStore();
        store.Record(@"C:\Early", "blender");
        Advance(TimeSpan.FromSeconds(3));
        store.Record(@"C:\Late", "blender");
        Advance(TimeSpan.FromSeconds(3));
        store.Record(@"C:\Other", "notepad");

        Assert.Equal(@"C:\Late", store.LastInApp("blender"));
        Assert.Equal(@"C:\Other", store.LastInApp("Notepad.exe"));
    }

    [Fact]
    public void LastInApp_UsesTheAppsOwnLastAccessNotTheGlobalOne()
    {
        var store = NewStore();
        store.Record(@"C:\B", "blender");            // blender use at t0
        Advance(TimeSpan.FromSeconds(3));
        store.Record(@"C:\A", "blender");            // blender use at t3
        Advance(TimeSpan.FromSeconds(3));
        store.Record(@"C:\B", "notepad");            // B's global access is now the latest, its blender use is not

        Assert.Equal(@"C:\A", store.LastInApp("blender"));
    }

    [Fact]
    public void LastInApp_SkipsExcludedPaths()
    {
        var store = NewStore();
        store.Record(@"C:\Keep", "blender");
        Advance(TimeSpan.FromSeconds(3));
        store.Record(@"C:\Secret\Stuff", "blender");
        store.SetExcludes([@"C:\Secret\*"]);

        Assert.Equal(@"C:\Keep", store.LastInApp("blender"));
    }

    [Fact]
    public void LastInApp_UnknownOrBlankApp_ReturnsNull()
    {
        var store = NewStore();
        store.Record(@"C:\A", "blender");

        Assert.Null(store.LastInApp("notepad"));
        Assert.Null(store.LastInApp(""));
        Assert.Null(store.LastInApp("   "));
    }

    [Fact]
    public void LastInApp_OnlyExcludedPathsUsedInApp_ReturnsNull()
    {
        var store = NewStore();
        store.Record(@"C:\Secret\Stuff", "blender");
        store.SetExcludes([@"C:\Secret\*"]);

        Assert.Null(store.LastInApp("blender"));
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAppData_AsVersion2()
    {
        var store = NewStore();
        RecordTimes(store, @"C:\Elsewhere", 4);
        RecordTimes(store, @"C:\InBlender", 3, "blender");
        store.Record(@"C:\InBlender", "notepad");
        store.Save();

        using (var doc = JsonDocument.Parse(File.ReadAllText(FilePath)))
            Assert.Equal(2, doc.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(new Dictionary<string, double> { ["blender"] = 3, ["notepad"] = 1 }, ReadApps()[@"C:\InBlender"]);
        Assert.Empty(ReadApps()[@"C:\Elsewhere"]);

        var reloaded = NewStore();
        reloaded.Load();

        Assert.Equal(store.Top(10), reloaded.Top(10));
        Assert.Equal(new[] { @"C:\InBlender" }, reloaded.TopInApp(10, "blender"));
        Assert.Equal(@"C:\InBlender", reloaded.LastInApp("notepad"));
        Assert.False(reloaded.IsDirty);
    }

    [Fact]
    public void Load_Version1FileWithoutApps_Loads()
    {
        Seed((@"C:\A", 3, Start), (@"C:\B", 1, Start));
        var store = NewStore();
        store.Load();

        Assert.Equal(new[] { @"C:\A", @"C:\B" }, store.Top(10));
        Assert.Empty(store.TopInApp(10, "blender"));
        Assert.Null(store.LastInApp("blender"));
    }

    [Fact]
    public void Load_SkipsInvalidAppUses()
    {
        var stamp = Start.ToString("O");
        File.WriteAllText(FilePath, $$"""
            { "version": 2, "entries": [ { "path": "C:\\A", "rank": 1, "lastAccess": "{{stamp}}", "apps": {
                "good": { "rank": 2, "lastAccess": "{{stamp}}" },
                "zero": { "rank": 0, "lastAccess": "{{stamp}}" },
                "negative": { "rank": -1, "lastAccess": "{{stamp}}" },
                "": { "rank": 4, "lastAccess": "{{stamp}}" },
                " ": { "rank": 4, "lastAccess": "{{stamp}}" },
                "missing": null
            } } ] }
            """);
        var store = NewStore();
        store.Load();
        store.Save();

        Assert.Equal(new Dictionary<string, double> { ["good"] = 2 }, ReadApps()[@"C:\A"]);
        Assert.Null(store.LastInApp("zero"));
        Assert.Null(store.LastInApp("negative"));
        Assert.Equal(@"C:\A", store.LastInApp("good"));
    }

    [Fact]
    public void Remove_DropsAppData()
    {
        var store = NewStore();
        store.Record(@"C:\A", "blender");
        store.Remove(@"C:\A");

        Assert.Null(store.LastInApp("blender"));
        Assert.Empty(store.TopInApp(10, "blender"));

        // Recording it afresh starts from zero: no app use is left over from before.
        Advance(TimeSpan.FromSeconds(3));
        store.Record(@"C:\A");
        Assert.Null(store.LastInApp("blender"));
    }

    [Fact]
    public void Clear_DropsAppData()
    {
        var store = NewStore();
        store.Record(@"C:\A", "blender");
        store.Clear();

        Assert.Null(store.LastInApp("blender"));
        Assert.Empty(store.TopInApp(10, "blender"));
    }
}
