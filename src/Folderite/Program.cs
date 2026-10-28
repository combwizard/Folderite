using System.Runtime.CompilerServices;
using Folderite.Settings;

namespace Folderite;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase)))
            return RunSettingsProcess();

        using var mutex = new Mutex(true, @"Local\Folderite.SingleInstance", out bool isFirst);
        if (!isFirst) return 0;

        try
        {
            using var app = new App();
            return app.Run();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return 1;
        }
    }

    private static int RunSettingsProcess()
    {
        using var mutex = new Mutex(true, @"Local\Folderite.Settings", out bool isFirst);
        if (!isFirst) return 0;

        try
        {
            return RunSettings();
        }
        catch (Exception ex)
        {
            Log.Error(ex);
            return 1;
        }
    }

    // Kept out of Main so tray mode never JIT-loads WPF types.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunSettings() => SettingsApp.Run();
}
