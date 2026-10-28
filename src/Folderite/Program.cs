namespace Folderite;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
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
}
