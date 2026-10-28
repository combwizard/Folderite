using System.Windows;
using Folderite.Config;
using Folderite.Menu;

namespace Folderite.Settings;

/// <summary>Entry point of <c>Folderite.exe --settings</c>: a WPF window that edits config.json; the tray app hot-reloads it.</summary>
internal static class SettingsApp
{
    public const string WindowTitle = "Folderite Settings";

    public static int Run()
    {
        // A remembered location that is the exe's own folder is just portable mode; drop it so it can't
        // resurrect portable mode later (e.g. by recreating config.json there after the user removes it).
        try
        {
            if (ConfigLocation.Override is { } dir && ConfigLocation.SameDirectory(dir, ConfigLocation.PortableDirectory))
                ConfigLocation.SetOverride(null);
        }
        catch { /* best effort */ }

        var app = new Application
        {
            ThemeMode = ThemeMode.System,
            ShutdownMode = ShutdownMode.OnLastWindowClose,
        };
        // After ThemeMode, so BasedOn styles pick up the Fluent ones.
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Folderite;component/Settings/SettingsStyles.xaml"),
        });
        DarkMode.Enable(); // the live preview's native menu frame

        OpenWindow();
        return app.Run();
    }

    /// <summary>
    /// Opens a settings window on the config location in effect now. If one is already open it is replaced: the new
    /// window is shown first, in the same place and size, and then the old one closes.
    /// </summary>
    internal static void OpenWindow(string? selectPage = null, string? status = null)
    {
        var app = Application.Current;
        var old = app.Windows.OfType<SettingsWindow>().FirstOrDefault();

        var store = new ConfigStore(ConfigStore.DefaultDirectory, action => app.Dispatcher.BeginInvoke(action));
        store.Load();
        store.StartWatching();
        var window = new SettingsWindow(store); // owns the store, disposes it on Closed

        if (selectPage is not null)
            window.ShowPage(selectPage);
        if (status is not null)
            window.SetStatus(status);

        if (old is not null)
        {
            var bounds = old.RestoreBounds;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = bounds.Left;
            window.Top = bounds.Top;
            window.Width = bounds.Width;
            window.Height = bounds.Height;
            window.WindowState = old.WindowState == WindowState.Minimized ? WindowState.Normal : old.WindowState;
        }

        app.MainWindow = window;
        window.Show();
        old?.Close();
    }
}
