using Folderite.Config;

namespace Folderite.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    private string ConfigPath => Path.Combine(_temp.Root, "config.json");

    public void Dispose() => _temp.Dispose();

    private void WriteConfig(string json) => File.WriteAllText(ConfigPath, json);

    [Fact]
    public void Load_EmptyDirectory_CreatesConfigWithFourDefaultFavorites()
    {
        using var store = new ConfigStore(_temp.Root);

        var config = store.Load();

        Assert.True(File.Exists(ConfigPath));
        Assert.Null(store.LastError);
        Assert.Equal(new[] { "&Home", "&Desktop", "Do&wnloads", "D&ocuments" },
            config.Favorites.Select(f => f.Name));
    }

    [Fact]
    public void Load_JsonWithCommentsAndTrailingCommas_Parses()
    {
        WriteConfig("""
            // user config
            {
              /* how many recents to show */
              "recentCount": 7,
              "favorites": [
                { "name": "&Projects", "path": "C:\\Projects", },
              ],
            }
            """);
        using var store = new ConfigStore(_temp.Root);

        var config = store.Load();

        Assert.Null(store.LastError);
        Assert.Equal(7, config.RecentCount);
        Assert.Equal(@"C:\Projects", Assert.Single(config.Favorites).Path);
    }

    [Fact]
    public void Load_InvalidJson_KeepsPreviousConfigAndSetsLastError()
    {
        WriteConfig("""{ "recentCount": 7 }""");
        using var store = new ConfigStore(_temp.Root);
        store.Load();

        WriteConfig("{ \"recentCount\": ");
        store.Load();

        Assert.Equal(7, store.Current.RecentCount);
        Assert.NotNull(store.LastError);
    }

    [Fact]
    public void Load_ValidFileAfterInvalid_ClearsLastError()
    {
        WriteConfig("{ broken");
        using var store = new ConfigStore(_temp.Root);
        store.Load();
        Assert.NotNull(store.LastError);

        WriteConfig("""{ "recentCount": 9 }""");
        store.Load();

        Assert.Null(store.LastError);
        Assert.Equal(9, store.Current.RecentCount);
    }

    [Fact]
    public void Save_WritesCamelCaseWithoutNullItemsOrIsGroup()
    {
        using var store = new ConfigStore(_temp.Root);
        store.Load();

        store.Save();

        var text = File.ReadAllText(ConfigPath);
        Assert.Contains("\"recentCount\"", text);
        Assert.Contains("\"favorites\"", text);
        Assert.DoesNotContain("RecentCount", text);
        Assert.DoesNotContain("\"items\"", text);
        Assert.DoesNotContain("isGroup", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsFavoriteGroups()
    {
        WriteConfig("""
            {
              "favorites": [
                { "name": "Work", "items": [ { "name": "&Repo", "path": "C:\\Repo" } ] },
                { "name": "&Top", "path": "C:\\Top" }
              ]
            }
            """);
        using (var first = new ConfigStore(_temp.Root))
        {
            first.Load();
            first.Save();
        }

        using var second = new ConfigStore(_temp.Root);
        var config = second.Load();

        Assert.Equal(2, config.Favorites.Count);
        Assert.True(config.Favorites[0].IsGroup);
        Assert.Equal("Work", config.Favorites[0].Name);
        var child = Assert.Single(config.Favorites[0].Items!);
        Assert.Equal(@"C:\Repo", child.Path);
        Assert.False(config.Favorites[1].IsGroup);
        Assert.Equal(@"C:\Top", config.Favorites[1].Path);
    }
}
