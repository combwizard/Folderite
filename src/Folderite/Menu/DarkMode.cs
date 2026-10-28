using System.Runtime.InteropServices;

namespace Folderite.Menu;

internal static class DarkMode
{
    // Undocumented uxtheme ordinals, stable since Windows 10 1903; they make the native menu frame follow the system theme.
    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    private const int AllowDark = 1;

    public static void Enable()
    {
        try
        {
            if (Environment.OSVersion.Version.Build < 18362) return;
            SetPreferredAppMode(AllowDark);
            FlushMenuThemes();
        }
        catch
        {
            // Best effort only.
        }
    }
}
