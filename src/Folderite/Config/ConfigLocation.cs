using Microsoft.Win32;

namespace Folderite.Config;

/// <summary>Where config.json lives: portable (next to the exe) > HKCU override > %APPDATA%\Folderite.</summary>
public static class ConfigLocation
{
    public const string RegistryKeyPath = @"Software\Folderite";
    public const string ValueName = "ConfigDirectory";

    public static string DefaultDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Folderite");

    public static string PortableDirectory { get; } = AppContext.BaseDirectory;

    /// <summary>True if config.json sits next to the exe. Evaluated on each access.</summary>
    public static bool IsPortable => File.Exists(Path.Combine(PortableDirectory, ConfigRelocator.ConfigFile));

    /// <summary>The user-chosen directory from HKCU, or null if unset, blank or unreadable. Never throws.</summary>
    public static string? Override
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: false);
                if (key?.GetValue(ValueName) is not string raw)
                    return null;
                var value = Environment.ExpandEnvironmentVariables(raw).Trim();
                return value.Length == 0 ? null : value;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>Stores <paramref name="directory"/> as the override; null or the default directory clears it.</summary>
    public static void SetOverride(string? directory)
    {
        // The exe's folder is portable mode (config.json next to the exe), never a remembered location.
        if (string.IsNullOrWhiteSpace(directory) || SameDirectory(directory, DefaultDirectory) ||
            SameDirectory(directory, PortableDirectory))
        {
            using var existing = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
            existing?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath, writable: true);
        key.SetValue(ValueName, Path.GetFullPath(directory.Trim()), RegistryValueKind.String);
    }

    public static string Resolve() => Resolve(IsPortable, PortableDirectory, Override, DefaultDirectory);

    internal static string Resolve(bool portable, string portableDir, string? overrideDir, string defaultDir)
    {
        if (portable)
            return portableDir;
        return string.IsNullOrWhiteSpace(overrideDir) ? defaultDir : overrideDir;
    }

    internal static bool SameDirectory(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
