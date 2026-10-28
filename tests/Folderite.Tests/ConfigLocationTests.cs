using Folderite.Config;

namespace Folderite.Tests;

public sealed class ConfigLocationTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Dir(string name)
    {
        var path = Path.Combine(_temp.Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Write(string dir, string file, string text) =>
        File.WriteAllText(Path.Combine(dir, file), text);

    private static string Read(string dir, string file) =>
        File.ReadAllText(Path.Combine(dir, file));

    private static bool Exists(string dir, string file) => File.Exists(Path.Combine(dir, file));

    // Resolve

    [Fact]
    public void Resolve_PortableBeatsOverrideAndDefault() =>
        Assert.Equal("P", ConfigLocation.Resolve(true, "P", "O", "D"));

    [Fact]
    public void Resolve_OverrideBeatsDefault() =>
        Assert.Equal("O", ConfigLocation.Resolve(false, "P", "O", "D"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankOverrideCountsAsNone(string? overrideDir) =>
        Assert.Equal("D", ConfigLocation.Resolve(false, "P", overrideDir, "D"));

    // SameDirectory

    [Fact]
    public void SameDirectory_IgnoresTrailingSeparatorAndCase()
    {
        Assert.True(ConfigLocation.SameDirectory(@"C:\Foo\Bar", @"C:\Foo\Bar\"));
        Assert.True(ConfigLocation.SameDirectory(@"C:\Foo\Bar", @"c:\FOO\bar"));
        Assert.False(ConfigLocation.SameDirectory(@"C:\Foo\Bar", @"C:\Foo\Baz"));
    }

    [Fact]
    public void SameDirectory_RelativeMatchesFullPath()
    {
        Assert.True(ConfigLocation.SameDirectory(".", Directory.GetCurrentDirectory()));
        Assert.True(ConfigLocation.SameDirectory(@"x\..\y", Path.Combine(Directory.GetCurrentDirectory(), "y")));
    }

    // Relocate

    [Fact]
    public void Move_IntoEmptyDirectory_MovesFilesAndRemovesSources()
    {
        var from = Dir("from");
        var to = Path.Combine(_temp.Root, "to", "nested");
        Write(from, "config.json", "cfg");
        Write(from, "history.json", "hist");
        Write(from, "folderite.log", "log");

        ConfigRelocator.Relocate(from, to, RelocateMode.Move);

        Assert.Equal("cfg", Read(to, "config.json"));
        Assert.Equal("hist", Read(to, "history.json"));
        Assert.Equal("log", Read(to, "folderite.log"));
        Assert.False(Exists(from, "config.json"));
        Assert.False(Exists(from, "history.json"));
        Assert.False(Exists(from, "folderite.log"));
    }

    [Fact]
    public void Move_TargetHasHistory_MovesOursAndBacksUpTheirs()
    {
        var from = Dir("from");
        var to = Dir("to");
        Write(from, "config.json", "cfg");
        Write(from, "history.json", "our-hist");
        Write(to, "history.json", "their-hist");

        ConfigRelocator.Relocate(from, to, RelocateMode.Move);

        Assert.Equal("cfg", Read(to, "config.json"));
        Assert.Equal("our-hist", Read(to, "history.json"));
        Assert.Equal("their-hist", Read(to, "history.json.bak"));
        Assert.False(Exists(from, "config.json"));
        Assert.False(Exists(from, "history.json"));
    }

    [Fact]
    public void UseExisting_KeepsTargetConfigCopiesHistoryOnlyWhenMissing()
    {
        var from = Dir("from");
        var to = Dir("to");
        Write(from, "config.json", "our-cfg");
        Write(from, "history.json", "our-hist");
        Write(to, "config.json", "their-cfg");

        ConfigRelocator.Relocate(from, to, RelocateMode.UseExisting);

        Assert.Equal("their-cfg", Read(to, "config.json"));
        Assert.Equal("our-hist", Read(to, "history.json"));
        Assert.Equal("our-cfg", Read(from, "config.json")); // not copied, so not deleted
        Assert.False(Exists(from, "history.json"));
    }

    [Fact]
    public void UseExisting_TargetHistoryIsKept()
    {
        var from = Dir("from");
        var to = Dir("to");
        Write(from, "config.json", "our-cfg");
        Write(from, "history.json", "our-hist");
        Write(to, "config.json", "their-cfg");
        Write(to, "history.json", "their-hist");

        ConfigRelocator.Relocate(from, to, RelocateMode.UseExisting);

        Assert.Equal("their-hist", Read(to, "history.json"));
        Assert.Equal("our-hist", Read(from, "history.json")); // ours stays behind rather than being lost
        Assert.Equal("our-cfg", Read(from, "config.json"));
    }

    [Fact]
    public void Replace_BacksUpTargetFilesAndCopiesOurs()
    {
        var from = Dir("from");
        var to = Dir("to");
        Write(from, "config.json", "our-cfg");
        Write(from, "history.json", "our-hist");
        Write(to, "config.json", "their-cfg");
        Write(to, "history.json", "their-hist");
        Write(to, "config.json.bak", "stale");

        ConfigRelocator.Relocate(from, to, RelocateMode.Replace);

        Assert.Equal("our-cfg", Read(to, "config.json"));
        Assert.Equal("our-hist", Read(to, "history.json"));
        Assert.Equal("their-cfg", Read(to, "config.json.bak"));
        Assert.Equal("their-hist", Read(to, "history.json.bak"));
        Assert.False(Exists(from, "config.json"));
        Assert.False(Exists(from, "history.json"));
    }

    [Fact]
    public void SameDirectory_RelocateIsNoOp()
    {
        var dir = Dir("same");
        Write(dir, "config.json", "cfg");
        Write(dir, "history.json", "hist");

        ConfigRelocator.Relocate(dir, dir + Path.DirectorySeparatorChar, RelocateMode.Move);

        Assert.Equal("cfg", Read(dir, "config.json"));
        Assert.Equal("hist", Read(dir, "history.json"));
    }

    [Fact]
    public void MissingSourceFiles_DoNotThrow()
    {
        var from = Dir("from");
        var to = Path.Combine(_temp.Root, "to");

        ConfigRelocator.Relocate(from, to, RelocateMode.Move);
        ConfigRelocator.Relocate(from, to, RelocateMode.UseExisting);
        ConfigRelocator.Relocate(from, to, RelocateMode.Replace);

        Assert.True(Directory.Exists(to));
    }

    [Fact]
    public void FailedCopy_ThrowsAndKeepsSource()
    {
        var from = Dir("from");
        var to = Dir("to");
        Write(from, "config.json", "cfg");
        Write(from, "history.json", "hist");
        Directory.CreateDirectory(Path.Combine(to, "config.json"));

        Assert.ThrowsAny<Exception>(() => ConfigRelocator.Relocate(from, to, RelocateMode.Move));

        Assert.Equal("cfg", Read(from, "config.json"));
        Assert.Equal("hist", Read(from, "history.json"));
    }

    [Fact]
    public void HasConfig_ReflectsConfigJson()
    {
        var dir = Dir("d");
        Assert.False(ConfigRelocator.HasConfig(dir));
        Write(dir, "config.json", "{}");
        Assert.True(ConfigRelocator.HasConfig(dir));
    }

    [Fact]
    public void Retire_RenamesConfigToBak_ReplacingAnOlderBak()
    {
        var dir = Dir("portable");
        Write(dir, "config.json", "current");
        Write(dir, "config.json.bak", "older");

        ConfigRelocator.Retire(dir);

        Assert.False(ConfigRelocator.HasConfig(dir));
        Assert.Equal("current", Read(dir, "config.json.bak"));
    }
}
