namespace Folderite;

/// <summary>Append-only error log next to the config (folderite.log in the resolved config directory).</summary>
internal static class Log
{
    private static string FilePath => Path.Combine(Config.ConfigLocation.Resolve(), Config.ConfigRelocator.LogFile);

    public static void Error(Exception ex) => Write($"ERROR {ex}");

    public static void Info(string message) => Write($"INFO  {message}");

    private static void Write(string line)
    {
        try
        {
            var path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Delete(path);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch { }
    }
}
