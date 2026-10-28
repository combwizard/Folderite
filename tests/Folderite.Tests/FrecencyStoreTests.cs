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
}
