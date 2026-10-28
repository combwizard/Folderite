using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Folderite.Settings.ViewModels;

namespace Folderite.Settings.Pages;

public partial class HistoryPage : UserControl
{
    public HistoryPage() => InitializeComponent();

    private HistoryVm? Vm => DataContext as HistoryVm;

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        var answer = MessageBox.Show(Window.GetWindow(this),
            "Forget every folder in the history? This can't be undone.",
            "Clear history", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes) vm.ClearAllCommand.Execute(null);
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only a double-click on a row, not on the scrollbar or empty space.
        var d = e.OriginalSource as DependencyObject;
        while (d is not null and not ListBoxItem and not ListBox)
            d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        if (d is not ListBoxItem { DataContext: HistoryRowVm row }) return;

        try
        {
            var start = new ProcessStartInfo("explorer.exe");
            start.ArgumentList.Add(row.Path); // quoted for us, trailing backslash included
            Process.Start(start);
        }
        catch { /* explorer unavailable */ }
        e.Handled = true;
    }

    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete || Vm is not { Selected: not null } vm) return;
        vm.RemoveCommand.Execute(null);
        e.Handled = true;
    }
}
