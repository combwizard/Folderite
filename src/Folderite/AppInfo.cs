using System.Reflection;

namespace Folderite;

/// <summary>Facts about this build, shared by the tray icon and Settings.</summary>
internal static class AppInfo
{
    /// <summary>"0.4.1", or "0.4.1-2-gabc1234" for a build after a tag; the "+commit" build metadata is dropped.</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "";
        int plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }
}
