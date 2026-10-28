using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Folderite.Settings.ViewModels;
using Microsoft.Win32;

namespace Folderite.Settings.Pages;

public partial class FavoritesPage : UserControl
{
    private const string NodeFormat = "Folderite.FavoriteNode";

    private enum Zone { Before, After, Into }

    private FavoritesTreeVm? _tree;
    private Point _dragStart;
    private FavoriteNodeVm? _dragNode;
    private DropAdorner? _adorner;

    public FavoritesPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach();
    }

    private void Attach()
    {
        if (_tree is not null) _tree.PropertyChanged -= Tree_VmPropertyChanged;
        _tree = (DataContext as SettingsVm)?.Favorites;
        if (_tree is not null) _tree.PropertyChanged += Tree_VmPropertyChanged;
        UpdateDetails();
    }

    // ---- Selection: TreeView.SelectedItem is read-only, so sync both ways by hand ----

    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_tree is not null && e.NewValue is FavoriteNodeVm node) _tree.Selected = node;
    }

    private void Tree_VmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FavoritesTreeVm.Selected)) return;
        if (_tree?.Selected is { } node)
        {
            for (var p = node.Parent; p is not null; p = p.Parent) p.IsExpanded = true;
            node.IsSelected = true;
        }
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        if (_tree?.Selected is not { } node) return;
        KindTitle.Text = node.Kind switch
        {
            FavoriteKind.Group => "Group",
            FavoriteKind.Separator => "Separator",
            _ => "Folder",
        };
        NameFields.Visibility = node.Kind == FavoriteKind.Separator ? Visibility.Collapsed : Visibility.Visible;
        PathFields.Visibility = node.Kind == FavoriteKind.Folder ? Visibility.Visible : Visibility.Collapsed;
        SeparatorNote.Visibility = node.Kind == FavoriteKind.Separator ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Tree_KeyDown(object sender, KeyEventArgs e)
    {
        if (_tree is null) return;
        // Alt+arrow arrives as a system key.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var command = (key, Keyboard.Modifiers) switch
        {
            (Key.Delete, ModifierKeys.None) => _tree.RemoveSelectedCommand,
            (Key.Up, ModifierKeys.Alt) => _tree.MoveUpCommand,
            (Key.Down, ModifierKeys.Alt) => _tree.MoveDownCommand,
            _ => null,
        };
        if (command is null) return;
        if (command.CanExecute(null)) command.Execute(null);
        e.Handled = true;
    }

    // ---- Buttons ----

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_tree is null) return;
        var dialog = new OpenFolderDialog { Title = "Add favorite folders", Multiselect = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        foreach (var folder in dialog.FolderNames) _tree.AddFolder(folder);
        Tree.Focus();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (_tree?.Selected is not { Kind: FavoriteKind.Folder } node) return;
        var dialog = new OpenFolderDialog { Title = "Choose folder" };
        if (node.ExpandedPath is { } current && Directory.Exists(current)) dialog.InitialDirectory = current;
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) node.Path = dialog.FolderName;
    }

    // ---- Drag and drop: reorder and nest nodes, or drop folders from Explorer ----

    private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(Tree);
        _dragNode = ItemFrom(e.OriginalSource as DependencyObject)?.DataContext as FavoriteNodeVm;
    }

    private void Tree_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragNode is null || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(Tree) - _dragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var node = _dragNode;
        _dragNode = null;
        DragDrop.DoDragDrop(Tree, new DataObject(NodeFormat, node), DragDropEffects.Move);
        ClearAdorner();
    }

    private void Tree_DragOver(object sender, DragEventArgs e)
    {
        var (header, target, zone) = HitTarget(e);
        e.Effects = EffectFor(e.Data, target, zone);
        if (e.Effects == DragDropEffects.None || header is null) ClearAdorner();
        else ShowAdorner(header, zone);
        e.Handled = true;
    }

    private void Tree_DragLeave(object sender, DragEventArgs e) => ClearAdorner();

    private void Tree_Drop(object sender, DragEventArgs e)
    {
        ClearAdorner();
        if (_tree is null) return;
        var (_, target, zone) = HitTarget(e);
        if (EffectFor(e.Data, target, zone) == DragDropEffects.None) return;
        var (parent, index) = Resolve(target, zone);

        if (e.Data.GetData(NodeFormat) is FavoriteNodeVm node)
        {
            if (_tree.Move(node, parent, index)) _tree.Selected = node;
        }
        else
        {
            foreach (var folder in DroppedFolders(e.Data))
            {
                // Add appends at the root (nothing selected), then Move puts it where it was dropped.
                var added = FavoriteNodeVm.NewFolder(folder);
                _tree.Selected = null;
                _tree.Add(added);
                if (_tree.Move(added, parent, index)) index++;
                _tree.Selected = added;
            }
        }
        e.Handled = true;
    }

    private DragDropEffects EffectFor(IDataObject data, FavoriteNodeVm? target, Zone zone)
    {
        if (_tree is null) return DragDropEffects.None;
        if (data.GetData(NodeFormat) is FavoriteNodeVm node)
        {
            if (target == node) return DragDropEffects.None;
            var (parent, _) = Resolve(target, zone);
            for (var p = parent; p is not null; p = p.Parent)
                if (p == node) return DragDropEffects.None; // into itself or a descendant
            return DragDropEffects.Move;
        }
        return DroppedFolders(data).Any() ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private static IEnumerable<string> DroppedFolders(IDataObject data) =>
        data.GetData(DataFormats.FileDrop) is string[] paths ? paths.Where(Directory.Exists) : [];

    private (FavoriteNodeVm? Parent, int Index) Resolve(FavoriteNodeVm? target, Zone zone)
    {
        if (target is null) return (null, _tree!.Roots.Count);
        if (zone == Zone.Into) return (target, target.Children.Count);
        var siblings = target.Parent?.Children ?? _tree!.Roots;
        int i = siblings.IndexOf(target);
        return (target.Parent, zone == Zone.Before ? i : i + 1);
    }

    private (FrameworkElement? Header, FavoriteNodeVm? Node, Zone Zone) HitTarget(DragEventArgs e)
    {
        var item = ItemFrom(Tree.InputHitTest(e.GetPosition(Tree)) as DependencyObject);
        if (item?.DataContext is not FavoriteNodeVm node) return (null, null, Zone.After);

        // The item's own row; the TreeViewItem itself also spans its expanded children.
        var header = item.Template?.FindName("PART_Header", item) as FrameworkElement ?? item;
        double h = Math.Max(1, Math.Min(header.ActualHeight, 40));
        double y = e.GetPosition(header).Y;
        var zone = node.Kind == FavoriteKind.Group
            ? y < h * 0.3 ? Zone.Before : y > h * 0.7 ? Zone.After : Zone.Into
            : y < h / 2 ? Zone.Before : Zone.After;
        return (header, node, zone);
    }

    private static TreeViewItem? ItemFrom(DependencyObject? d)
    {
        while (d is not null and not TreeViewItem)
            d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as TreeViewItem;
    }

    private void ShowAdorner(FrameworkElement header, Zone zone)
    {
        if (_adorner is { } a && a.AdornedElement == header && a.Zone == zone) return;
        ClearAdorner();
        if (AdornerLayer.GetAdornerLayer(header) is not { } layer) return;
        var brush = TryFindResource("AccentFillColorDefaultBrush") as Brush ?? SystemColors.HighlightBrush;
        _adorner = new DropAdorner(header, zone, brush);
        layer.Add(_adorner);
    }

    private void ClearAdorner()
    {
        if (_adorner is null) return;
        AdornerLayer.GetAdornerLayer(_adorner.AdornedElement)?.Remove(_adorner);
        _adorner = null;
    }

    /// <summary>Insertion line above/below a row, or an outline around a group being dropped into.</summary>
    private sealed class DropAdorner : Adorner
    {
        private readonly Pen _pen;

        public DropAdorner(UIElement element, Zone zone, Brush brush) : base(element)
        {
            Zone = zone;
            _pen = new Pen(brush, 2);
            _pen.Freeze();
            IsHitTestVisible = false;
        }

        public Zone Zone { get; }

        protected override void OnRender(DrawingContext dc)
        {
            var size = ((FrameworkElement)AdornedElement).RenderSize;
            double h = Math.Min(size.Height, 40);
            switch (Zone)
            {
                case Zone.Into:
                    dc.DrawRoundedRectangle(null, _pen, new Rect(1, 1, Math.Max(0, size.Width - 2), Math.Max(0, h - 2)), 4, 4);
                    break;
                default:
                    double y = Zone == Zone.Before ? 1 : h - 1;
                    dc.DrawLine(_pen, new Point(0, y), new Point(size.Width, y));
                    break;
            }
        }
    }
}
