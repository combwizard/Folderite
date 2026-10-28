namespace Folderite;

/// <summary>Append-only error log next to the config (%APPDATA%\Folderite\folderite.log).</summary>
internal static class Log
{
    private static readonly string FilePath = Path.Combine(Config.ConfigStore.DefaultDirectory, "folderite.log");

    public static void Error(Exception ex) => Write($"ERROR {ex}");

    public static void Info(string message) => Write($"INFO  {message}");

    private static void Write(string line)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 1_000_000) File.Delete(FilePath);
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch { }
    }
}
