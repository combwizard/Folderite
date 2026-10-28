using Folderite.Config;
using Folderite.Settings;

namespace Folderite.Tests;

public sealed class ConfigMoveTests
{
    private const string Default = @"C:\Users\x\AppData\Roaming\Folderite";

    [Fact]
    public void KindOf_PortableWinsEvenInOtherFolder() =>
        Assert.Equal(LocationKind.Portable, ConfigMove.KindOf(true, @"D:\Apps\Folderite", Default));

    [Fact]
    public void KindOf_DefaultDirectory_IgnoresCaseAndTrailingSlash() =>
        Assert.Equal(LocationKind.Default, ConfigMove.KindOf(false, Default.ToUpperInvariant() + @"\", Default));

    [Fact]
    public void KindOf_OtherDirectory_IsCustom() =>
        Assert.Equal(LocationKind.Custom, ConfigMove.KindOf(false, @"D:\Sync\Folderite", Default));

    [Fact]
    public void Caption_MatchesKind()
    {
        Assert.Equal("Default location", ConfigMove.Caption(LocationKind.Default));
        Assert.Equal("Custom location", ConfigMove.Caption(LocationKind.Custom));
        Assert.Equal("Portable", ConfigMove.Caption(LocationKind.Portable));
    }

    [Fact]
    public void ChooseMode_NoConfigInTarget_AlwaysMoves()
    {
        foreach (var answer in Enum.GetValues<ConflictChoice>())
            Assert.Equal(RelocateMode.Move, ConfigMove.ChooseMode(false, answer));
    }

    [Fact]
    public void ChooseMode_ExistingConfig_FollowsAnswer()
    {
        Assert.Equal(RelocateMode.UseExisting, ConfigMove.ChooseMode(true, ConflictChoice.UseExisting));
        Assert.Equal(RelocateMode.Replace, ConfigMove.ChooseMode(true, ConflictChoice.Replace));
        Assert.Null(ConfigMove.ChooseMode(true, ConflictChoice.Cancel));
    }

    [Fact]
    public void ConflictPrompt_NamesFolderAndBothChoices()
    {
        var text = ConfigMove.ConflictPrompt(@"D:\Sync");
        Assert.StartsWith(@"D:\Sync already has Folderite settings.", text);
        Assert.Contains("Yes: use the settings in that folder.", text);
        Assert.Contains("No: replace them with your current settings (the old file is kept as config.json.bak).", text);
    }

    private const string Exe = @"D:\Programs\Folderite";

    [Theory]
    [InlineData(@"D:\Programs\Folderite", true)]
    [InlineData(@"d:\programs\folderite\", true)]
    [InlineData(@"D:\Sync\Folderite", false)]
    public void IsPortableTarget_IsTheExeFolder(string target, bool expected) =>
        Assert.Equal(expected, ConfigMove.IsPortableTarget(target, Exe));

    [Fact]
    public void MustRetireSource_OnlyWhenLeavingPortableWithConfigLeftBehind()
    {
        Assert.True(ConfigMove.MustRetireSource(Exe, Default, Exe, sourceStillHasConfig: true));
        Assert.False(ConfigMove.MustRetireSource(Exe, Default, Exe, sourceStillHasConfig: false)); // Move took it along
        Assert.False(ConfigMove.MustRetireSource(Default, Exe, Exe, sourceStillHasConfig: true));  // going portable
        Assert.False(ConfigMove.MustRetireSource(Default, @"D:\Sync", Exe, sourceStillHasConfig: true)); // not portable
    }
}
