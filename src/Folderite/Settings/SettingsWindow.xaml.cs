using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Folderite.Config;
using Folderite.Settings.Pages;
using Folderite.Settings.ViewModels;

namespace Folderite.Settings;

public partial class SettingsWindow : Window
{
    private sealed record NavPage(string Key, string Title, string Glyph);

    private static readonly NavPage[] Pages =
    [
        new("favorites", "Favorites", ""),
        new("actions", "Actions", ""),
        new("menu", "Menu", ""),
        new("exclusions", "Exclusions", ""),
        new("history", "History", ""),
        new("general", "General", ""),
    ];

    private readonly ConfigStore _store;
    private readonly SettingsVm _vm;
    private readonly HistoryVm _history;
    private readonly GeneralVm _general;
    private readonly MenuPreview _preview;
    private readonly Dictionary<string, FrameworkElement> _pageCache = new();
    private volatile bool _storeReleased;

    internal SettingsWindow(ConfigStore store)
    {
        InitializeComponent();
        _store = store;
        _vm = new SettingsVm(store, action => Dispatcher.BeginInvoke(action));
        _vm.Load();
        _history = new HistoryVm(new TrayHistoryBackend(store.HistoryPath));
        _general = new GeneralVm(store)
        {
            FlushPending = _vm.SaveNow,
            ReleaseStore = ReleaseStore,
            SetStatus = text => _vm.Status = text,
        };
        _preview = new MenuPreview(this, store, _vm);
        DataContext = _vm;

        store.Changed += _vm.OnExternalChange;
        Nav.ItemsSource = Pages;
        Nav.SelectedIndex = 0;
        Closed += (_, _) =>
        {
            _vm.Dispose();
            _store.Dispose(); // this window owns its store
        };
    }

    /// <summary>Selects a nav page by key ("general", "history", ...).</summary>
    internal void ShowPage(string key)
    {
        int index = Array.FindIndex(Pages, p => p.Key == key);
        if (index >= 0)
            Nav.SelectedIndex = index;
    }

    internal void SetStatus(string text) => _vm.Status = text;

    // Called (from a worker thread) just before the config files move: from here on this window must not write
    // anything, because its store points at the old location.
    private void ReleaseStore()
    {
        _storeReleased = true;
        _store.Changed -= _vm.OnExternalChange;
        _store.Dispose();
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavPage page) return;
        PageTitle.Text = page.Title;
        if (!_pageCache.TryGetValue(page.Key, out var view))
            _pageCache[page.Key] = view = CreatePage(page.Key);
        PageHost.Content = view;

        // These two show state that lives elsewhere, so refresh them every time they're shown.
        if (page.Key == "history") _history.RefreshCommand.Execute(null);
        if (page.Key == "general") _general.RefreshStatusCommand.Execute(null);
    }

    private FrameworkElement CreatePage(string key) => key switch
    {
        "favorites" => new FavoritesPage { DataContext = _vm },
        "actions" => new ActionsPage { DataContext = _vm },
        "menu" => new MenuPage { DataContext = _vm },
        "exclusions" => new ExclusionsPage { DataContext = _vm },
        "history" => new HistoryPage { DataContext = _history },
        _ => new GeneralPage { DataContext = _general },
    };

    private void Preview_Click(object sender, RoutedEventArgs e) => _preview.Show(PreviewButton);

    private void OpenConfigFile_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_store.ConfigPath) { UseShellExecute = true }); }
        catch { Process.Start("notepad.exe", $"\"{_store.ConfigPath}\""); }
    }

    // With the content in the title bar, a maximized window hangs its resize border off-screen; pad it back in.
    protected override void OnStateChanged(EventArgs e)
    {
        var border = SystemParameters.WindowResizeBorderThickness;
        Root.Margin = WindowState == WindowState.Maximized
            ? new Thickness(border.Left + 4, border.Top + 4, border.Right + 4, border.Bottom + 4)
            : default;
        base.OnStateChanged(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _store.Changed -= _vm.OnExternalChange;
        // After a move the store is disposed but its Save would still write to the old location.
        if (!_storeReleased)
            _vm.SaveNow();
        base.OnClosing(e);
    }
}
