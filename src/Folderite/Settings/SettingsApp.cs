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
        var app = new Application
        {
            ThemeMode = ThemeMode.System,
            ShutdownMode = ShutdownMode.OnMainWindowClose,
        };
        // After ThemeMode, so BasedOn styles pick up the Fluent ones.
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Folderite;component/Settings/SettingsStyles.xaml"),
        });
        DarkMode.Enable(); // the live preview's native menu frame

        var dispatcher = app.Dispatcher;
        using var store = new ConfigStore(ConfigStore.DefaultDirectory, action => dispatcher.BeginInvoke(action));
        store.Load();
        store.StartWatching();

        return app.Run(new SettingsWindow(store));
    }
}
