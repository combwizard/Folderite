using Folderite.Config;
using Folderite.Dialogs;

namespace Folderite.Tests;

public sealed class DialogStarterTests
{
    private static readonly Func<string, string?> NoHistory = _ => null;

    [Fact]
    public void NothingSet_LeavesDialogAlone()
    {
        Assert.Null(DialogStarter.Resolve(new AppConfig(), "notepad", _ => @"C:\Last"));
    }

    [Fact]
    public void LastFolderOption_UsesTheAppsLastFolder()
    {
        var config = new AppConfig { DialogLastFolder = true };
        string? asked = null;
        var result = DialogStarter.Resolve(config, "notepad", app => { asked = app; return @"C:\Notes"; });
        Assert.Equal(@"C:\Notes", result);
        Assert.Equal("notepad", asked);
    }

    [Fact]
    public void LastFolderOption_NoHistory_LeavesDialogAlone()
    {
        Assert.Null(DialogStarter.Resolve(new AppConfig { DialogLastFolder = true }, "notepad", NoHistory));
    }

    [Fact]
    public void AppFolder_WinsOverLastFolder_EvenWhenOptionIsOff()
    {
        var config = new AppConfig
        {
            DialogLastFolder = true,
            DialogFolders = [new DialogFolder { App = "Notepad.EXE", Path = @"D:\Fixed" }],
        };
        Assert.Equal(@"D:\Fixed", DialogStarter.Resolve(config, "notepad", _ => @"C:\Last"));

        config.DialogLastFolder = false;
        Assert.Equal(@"D:\Fixed", DialogStarter.Resolve(config, "notepad", NoHistory));
    }

    [Fact]
    public void AppFolder_OnlyForThatApp()
    {
        var config = new AppConfig { DialogFolders = [new DialogFolder { App = "notepad", Path = @"D:\Fixed" }] };
        Assert.Null(DialogStarter.Resolve(config, "winword", NoHistory));
    }

    [Fact]
    public void AppFolder_ExpandsEnvironmentVariables()
    {
        var config = new AppConfig { DialogFolders = [new DialogFolder { App = "notepad", Path = @"%USERPROFILE%\Notes" }] };
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Notes");
        Assert.Equal(expected, DialogStarter.Resolve(config, "notepad", NoHistory), ignoreCase: true);
    }

    [Fact]
    public void AppFolder_BlankPathIsIgnored()
    {
        var config = new AppConfig
        {
            DialogLastFolder = true,
            DialogFolders = [new DialogFolder { App = "notepad", Path = "  " }],
        };
        Assert.Equal(@"C:\Last", DialogStarter.Resolve(config, "notepad", _ => @"C:\Last"));
    }

    [Fact]
    public void UnknownProcess_LeavesDialogAlone()
    {
        Assert.Null(DialogStarter.Resolve(new AppConfig { DialogLastFolder = true }, "", _ => @"C:\Last"));
    }
}
