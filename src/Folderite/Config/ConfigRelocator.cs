namespace Folderite.Config;

public enum RelocateMode
{
    /// <summary>The target has no config.json: bring ours along.</summary>
    Move,
    /// <summary>Keep the target's config.json; it replaces ours.</summary>
    UseExisting,
    /// <summary>Back up the target's files as .bak, then copy ours in.</summary>
    Replace,
}

/// <summary>Moves config.json, history.json and the log to a new directory.</summary>
public static class ConfigRelocator
{
    public const string ConfigFile = "config.json";
    public const string HistoryFile = "history.json";
    public const string LogFile = "folderite.log";

    public static bool HasConfig(string directory) => File.Exists(Path.Combine(directory, ConfigFile));

    /// <summary>Copies first and deletes the sources only once every copy succeeded, so a failure leaves the source intact.</summary>
    public static void Relocate(string fromDir, string toDir, RelocateMode mode)
    {
        if (ConfigLocation.SameDirectory(fromDir, toDir))
            return;

        Directory.CreateDirectory(toDir);

        var srcConfig = Path.Combine(fromDir, ConfigFile);
        var srcHistory = Path.Combine(fromDir, HistoryFile);
        var dstConfig = Path.Combine(toDir, ConfigFile);
        var dstHistory = Path.Combine(toDir, HistoryFile);

        // A file is removed from the old folder only after it was copied, so nothing is lost: with UseExisting our
        // config (and our history, if the target has one) simply stays behind. Whatever ours overwrites in the
        // target is kept as .bak.
        var copyConfig = mode != RelocateMode.UseExisting && File.Exists(srcConfig);
        var copyHistory = File.Exists(srcHistory) && (mode != RelocateMode.UseExisting || !File.Exists(dstHistory));

        if (copyConfig)
            Backup(dstConfig);
        if (copyHistory)
            Backup(dstHistory);
        if (copyConfig)
            Retry(() => File.Copy(srcConfig, dstConfig, overwrite: true));
        if (copyHistory)
            Retry(() => File.Copy(srcHistory, dstHistory, overwrite: true));

        if (copyConfig)
            Retry(() => File.Delete(srcConfig));
        if (copyHistory)
            Retry(() => File.Delete(srcHistory));

        try
        {
            var srcLog = Path.Combine(fromDir, LogFile);
            if (File.Exists(srcLog))
                File.Move(srcLog, Path.Combine(toDir, LogFile), overwrite: true);
        }
        catch
        {
            // Best effort.
        }
    }

    /// <summary>Renames <paramref name="directory"/>'s config.json to config.json.bak, e.g. to end portable mode without losing it.</summary>
    public static void Retire(string directory) => Backup(Path.Combine(directory, ConfigFile));

    private static void Backup(string path)
    {
        if (File.Exists(path))
            Retry(() => File.Move(path, path + ".bak", overwrite: true));
    }

    // Antivirus, the indexer or OneDrive can hold a just-written file for a moment.
    private static void Retry(Action action)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(100 * attempt);
            }
        }
    }
}
