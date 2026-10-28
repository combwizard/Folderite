namespace Folderite.Tests;

/// <summary>A unique directory under the system temp folder, deleted on Dispose.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), "folderite-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; the OS temp folder is cleaned eventually.
        }
    }
}

/// <summary>TimeProvider whose clock only moves when a test says so.</summary>
internal sealed class FakeTime : TimeProvider
{
    public FakeTime(DateTimeOffset now) => Now = now;

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now;
}
