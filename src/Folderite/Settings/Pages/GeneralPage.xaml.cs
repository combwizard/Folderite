using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Folderite.Config;
using Folderite.Settings.ViewModels;

namespace Folderite.Settings.Pages;

public partial class GeneralPage : UserControl
{
    private const string AppName = "Folderite";

    public GeneralPage() => InitializeComponent();

    private void ChangeLocation_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not GeneralVm vm)
            return;

        var dialog = new OpenFolderDialog
        {
            Title = "Choose a folder for Folderite's settings",
            InitialDirectory = vm.ConfigDirectory,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            _ = MoveAsync(vm, dialog.FolderName);
    }

    private void UseDefault_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is GeneralVm vm)
            _ = MoveAsync(vm, ConfigLocation.DefaultDirectory);
    }

    private async Task MoveAsync(GeneralVm vm, string target)
    {
        if (ConfigLocation.SameDirectory(target, vm.ConfigDirectory) || Window.GetWindow(this) is not { } window)
            return;

        bool toPortable = ConfigMove.IsPortableTarget(target, ConfigLocation.PortableDirectory);
        if (toPortable && MessageBox.Show(window, ConfigMove.PortablePrompt(target), AppName,
                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
            return;

        bool hasConfig = ConfigRelocator.HasConfig(target);
        var answer = hasConfig ? AskConflict(window, target) : ConflictChoice.Cancel;
        if (ConfigMove.ChooseMode(hasConfig, answer) is not { } mode)
            return;

        window.IsEnabled = false;
        MoveOutcome outcome;
        try
        {
            outcome = await vm.MoveConfigAsync(target, mode);
        }
        finally
        {
            window.IsEnabled = true;
        }

        switch (outcome.Status)
        {
            case MoveStatus.Moved:
                SettingsApp.OpenWindow("general", toPortable ? "Folderite is now portable" : $"Settings moved to {target}");
                break;
            case MoveStatus.TrayWouldNotClose:
                Error(window, "Folderite didn't close, so the settings were not moved.");
                break;
            default:
                // The store was released before the move, so this window can't be used any more: reopen on the old location.
                Error(window, outcome.Error ?? "The settings could not be moved.");
                SettingsApp.OpenWindow("general", "Settings were not moved");
                break;
        }
    }

    private static ConflictChoice AskConflict(Window owner, string folder) =>
        MessageBox.Show(owner, ConfigMove.ConflictPrompt(folder), AppName,
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => ConflictChoice.UseExisting,
            MessageBoxResult.No => ConflictChoice.Replace,
            _ => ConflictChoice.Cancel,
        };

    private static void Error(Window owner, string message) =>
        MessageBox.Show(owner, message, AppName, MessageBoxButton.OK, MessageBoxImage.Error);
}
