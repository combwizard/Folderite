using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Folderite.Settings.ViewModels;

namespace Folderite.Settings.Pages;

public partial class ExclusionsPage : UserControl
{
    public ExclusionsPage() => InitializeComponent();

    private SettingsVm? Vm => DataContext as SettingsVm;

    // The button's command adds the row right after Click; focus its TextBox once it has been generated.
    private void AddExclude_Click(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, FocusLastPattern);

    private void FocusLastPattern()
    {
        if (ExcludeList.Items.Count == 0) return;
        ExcludeList.UpdateLayout();
        if (ExcludeList.ItemContainerGenerator.ContainerFromIndex(ExcludeList.Items.Count - 1) is not FrameworkElement row) return;
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

    private void AddTyped_Click(object sender, RoutedEventArgs e) => AddTyped();

    private void ProcessBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        AddTyped();
        e.Handled = true;
    }

    private void AddTyped()
    {
        if (Vm is not { } vm) return;
        var name = ProcessBox.Text.Trim();
        if (name.Length == 0) return;
        if (vm.AddDisabledProcessCommand.CanExecute(name)) vm.AddDisabledProcessCommand.Execute(name);
        ProcessBox.Clear();
        ProcessBox.Focus();
    }

    // ---- Running apps ----

    private void AddApp_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;

        var existing = vm.DisabledProcesses.Select(p => p.Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var self = Process.GetCurrentProcess().ProcessName;
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowTitle.Length > 0 &&
                    !string.Equals(process.ProcessName, self, StringComparison.OrdinalIgnoreCase) &&
                    !existing.Contains(process.ProcessName))
                    names.Add(process.ProcessName);
            }
            catch { /* the process exited or is off limits */ }
            finally { process.Dispose(); }
        }

        var menu = new ContextMenu
        {
            PlacementTarget = AddAppButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            MaxHeight = 360,
        };
        if (names.Count == 0)
        {
            menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "No other apps with a window", IsEnabled = false });
        }
        foreach (var name in names)
        {
            var item = new System.Windows.Controls.MenuItem { Header = name.Replace("_", "__") };
            item.Click += (_, _) =>
            {
                if (vm.AddDisabledProcessCommand.CanExecute(name)) vm.AddDisabledProcessCommand.Execute(name);
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
}
