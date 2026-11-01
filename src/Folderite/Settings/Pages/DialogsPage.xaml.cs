using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Folderite.Settings.ViewModels;
using Microsoft.Win32;

namespace Folderite.Settings.Pages;

public partial class DialogsPage : UserControl
{
    public DialogsPage() => InitializeComponent();

    // The button's command adds the row right after Click; focus its App box once it has been generated.
    private void AddFolder_Click(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, FocusLastApp);

    private void FocusLastApp()
    {
        if (FolderList.Items.Count == 0) return;
        FolderList.UpdateLayout();
        if (FolderList.ItemContainerGenerator.ContainerFromIndex(FolderList.Items.Count - 1) is not FrameworkElement row) return;
        row.BringIntoView();
        if (FindTextBox(row) is { } box)
        {
            box.Focus();
            box.SelectAll();
        }
    }

    private static TextBox? FindTextBox(DependencyObject parent)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is TextBox box) return box;
            if (FindTextBox(child) is { } found) return found;
        }
        return null;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DialogFolderVm row) return;
        var dialog = new OpenFolderDialog { Title = "Choose folder" };
        var current = Environment.ExpandEnvironmentVariables(row.Path.Trim());
        if (current.Length > 0 && Directory.Exists(current)) dialog.InitialDirectory = current;
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) row.Path = dialog.FolderName;
    }
}
