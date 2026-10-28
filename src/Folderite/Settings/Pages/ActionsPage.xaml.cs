using System.Windows;
using System.Windows.Controls;
using Folderite.Settings.ViewModels;
using Microsoft.Win32;

namespace Folderite.Settings.Pages;

public partial class ActionsPage : UserControl
{
    public ActionsPage() => InitializeComponent();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if ((DataContext as SettingsVm)?.SelectedAction is not { } action) return;
        var dialog = new OpenFileDialog
        {
            Title = "Choose a program",
            Filter = "Programs and scripts (*.exe;*.cmd;*.bat;*.ps1)|*.exe;*.cmd;*.bat;*.ps1|All files (*.*)|*.*",
        };
        try
        {
            var current = Environment.ExpandEnvironmentVariables(action.Command ?? "");
            if (Path.GetDirectoryName(current) is { Length: > 0 } dir && Directory.Exists(dir))
                dialog.InitialDirectory = dir;
        }
        catch (ArgumentException) { /* not a valid path while typing */ }
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) action.Command = dialog.FileName;
    }
}
