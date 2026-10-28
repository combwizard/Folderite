using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Folderite.Config;
using Folderite.Menu;

namespace Folderite.Settings.ViewModels;

public enum FavoriteKind { Folder, Group, Separator }

/// <summary>Helpers for menu text with "&amp;" accelerator markers.</summary>
public static class MenuText
{
    /// <summary>"&amp;&amp;" becomes "&amp;", a single "&amp;" is dropped.</summary>
    public static string StripAccelerators(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '&')
            {
                sb.Append(text[i]);
                continue;
            }
            if (i + 1 < text.Length && text[i + 1] == '&')
            {
                sb.Append('&');
                i++;
            }
        }
        return sb.ToString();
    }
}

/// <summary>One favorites tree node: a folder, a group (submenu) or a separator.</summary>
public sealed partial class FavoriteNodeVm : ObservableObject
{
    public const string SeparatorName = "-";

    public FavoriteNodeVm(FavoriteKind kind, string name = "", string? path = null)
    {
        Kind = kind;
        this.name = name;
        this.path = path;
    }

    public FavoriteKind Kind { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName), nameof(Accelerator))]
    private string name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName), nameof(ExpandedPath), nameof(PathMissing))]
    private string? path;

    [ObservableProperty]
    private bool isExpanded = true;

    [ObservableProperty]
    private bool isSelected;

    /// <summary>Only groups use this, but it is never null.</summary>
    public ObservableCollection<FavoriteNodeVm> Children { get; } = [];

    public FavoriteNodeVm? Parent { get; internal set; }

    public bool IsFolder => Kind == FavoriteKind.Folder;
    public bool IsGroup => Kind == FavoriteKind.Group;
    public bool IsSeparator => Kind == FavoriteKind.Separator;

    public string DisplayName
    {
        get
        {
            if (Kind == FavoriteKind.Separator)
                return "Separator";
            if (Kind == FavoriteKind.Folder && string.IsNullOrEmpty(Name))
                return ExpandedPath is { Length: > 0 } p ? MenuComposer.DisplayName(p) : "";
            return MenuText.StripAccelerators(Name);
        }
    }

    public char? Accelerator => Accelerators.Of(Name);

    public string? ExpandedPath => Path is null ? null : Environment.ExpandEnvironmentVariables(Path);

    /// <summary>True for a folder on a local drive letter that doesn't exist. Evaluated on every access.</summary>
    public bool PathMissing
    {
        get
        {
            if (Kind != FavoriteKind.Folder || ExpandedPath is not { Length: >= 3 } p)
                return false;
            if (!char.IsAsciiLetter(p[0]) || p[1] != ':' || (p[2] != '\\' && p[2] != '/'))
                return false;
            try { return !Directory.Exists(p); }
            catch { return false; }
        }
    }

    public static FavoriteNodeVm NewFolder(string path) =>
        new(FavoriteKind.Folder, MenuComposer.DisplayName(path).Replace("&", "&&"), path);

    public static FavoriteNodeVm NewGroup(string name = "New group") => new(FavoriteKind.Group, name);

    public static FavoriteNodeVm NewSeparator() => new(FavoriteKind.Separator, SeparatorName);

    public static ObservableCollection<FavoriteNodeVm> FromConfig(IEnumerable<FavoriteEntry> entries)
    {
        var result = new ObservableCollection<FavoriteNodeVm>();
        foreach (var entry in entries)
            result.Add(FromEntry(entry, null));
        return result;
    }

    public static List<FavoriteEntry> ToConfig(IEnumerable<FavoriteNodeVm> nodes)
    {
        var result = new List<FavoriteEntry>();
        foreach (var node in nodes)
        {
            result.Add(node.Kind switch
            {
                FavoriteKind.Group => new FavoriteEntry { Name = node.Name, Items = ToConfig(node.Children) },
                FavoriteKind.Separator => new FavoriteEntry { Name = SeparatorName },
                _ => new FavoriteEntry { Name = node.Name, Path = node.Path },
            });
        }
        return result;
    }

    private static FavoriteNodeVm FromEntry(FavoriteEntry entry, FavoriteNodeVm? parent)
    {
        FavoriteNodeVm node;
        if (entry.IsGroup)
        {
            node = new FavoriteNodeVm(FavoriteKind.Group, entry.Name);
            foreach (var child in entry.Items!)
                node.Children.Add(FromEntry(child, node));
        }
        else if (entry.Name == SeparatorName && entry.Path is null)
        {
            node = new FavoriteNodeVm(FavoriteKind.Separator, SeparatorName);
        }
        else
        {
            node = new FavoriteNodeVm(FavoriteKind.Folder, entry.Name, entry.Path);
        }
        node.Parent = parent;
        return node;
    }
}

/// <summary>The favorites tree with selection, add/remove/move operations and change tracking.</summary>
public sealed partial class FavoritesTreeVm : ObservableObject
{
    private readonly PropertyChangedEventHandler _onNodeChanged;
    private readonly NotifyCollectionChangedEventHandler _onCollectionChanged;
    private int _batch;
    private bool _pending;
    private bool _loading;

    public FavoritesTreeVm()
    {
        _onNodeChanged = OnNodeChanged;
        _onCollectionChanged = OnCollectionChanged;
        Roots.CollectionChanged += _onCollectionChanged;
    }

    public ObservableCollection<FavoriteNodeVm> Roots { get; } = [];

    [ObservableProperty]
    private FavoriteNodeVm? selected;

    /// <summary>Raised for structural changes and Name/Path edits anywhere in the tree (not for expand/select).</summary>
    public event Action? Changed;

    public void Load(IEnumerable<FavoriteEntry> entries)
    {
        _loading = true;
        try
        {
            Selected = null;
            foreach (var root in Roots)
                Detach(root);
            Roots.Clear();
            foreach (var node in FavoriteNodeVm.FromConfig(entries))
                Roots.Add(node);
        }
        finally
        {
            _loading = false;
        }
        RefreshCommands();
    }

    public List<FavoriteEntry> ToConfig() => FavoriteNodeVm.ToConfig(Roots);

    public void Add(FavoriteNodeVm node)
    {
        Batch(() =>
        {
            if (Selected is { Kind: FavoriteKind.Group } group)
            {
                node.Parent = group;
                group.Children.Add(node);
                group.IsExpanded = true;
            }
            else if (Selected is { } sibling)
            {
                var list = Siblings(sibling);
                node.Parent = sibling.Parent;
                list.Insert(list.IndexOf(sibling) + 1, node);
            }
            else
            {
                node.Parent = null;
                Roots.Add(node);
            }
        });
        Select(node);
    }

    public void AddFolder(string path) => Add(FavoriteNodeVm.NewFolder(path));

    [RelayCommand]
    public void AddGroup() => Add(FavoriteNodeVm.NewGroup());

    [RelayCommand]
    public void AddSeparator() => Add(FavoriteNodeVm.NewSeparator());

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void RemoveSelected()
    {
        if (Selected is not { } node)
            return;

        var list = Siblings(node);
        int index = list.IndexOf(node);
        var neighbour = index + 1 < list.Count ? list[index + 1]
            : index > 0 ? list[index - 1]
            : node.Parent;

        Batch(() =>
        {
            Select(neighbour);
            list.RemoveAt(index);
            node.Parent = null;
        });
    }

    [RelayCommand(CanExecute = nameof(CanMoveUp))]
    public void MoveUp() => MoveBy(-1);

    [RelayCommand(CanExecute = nameof(CanMoveDown))]
    public void MoveDown() => MoveBy(1);

    /// <summary>Drag-drop move. A null parent means the root. Returns false (and changes nothing) for an invalid target.</summary>
    public bool Move(FavoriteNodeVm node, FavoriteNodeVm? newParent, int index)
    {
        if (newParent is not null && (newParent == node || newParent.Kind != FavoriteKind.Group || IsAncestor(node, newParent)))
            return false;

        var source = Siblings(node);
        var target = newParent?.Children ?? Roots;
        int oldIndex = source.IndexOf(node);
        if (oldIndex < 0)
            return false;

        index = Math.Clamp(index, 0, target.Count);
        if (ReferenceEquals(source, target) && oldIndex < index)
            index--;

        var selected = Selected;
        Batch(() =>
        {
            source.RemoveAt(oldIndex);
            node.Parent = newParent;
            target.Insert(index, node);
            if (newParent is not null)
                newParent.IsExpanded = true;
        });
        if (selected is not null)
            Select(selected);
        return true;
    }

    /// <summary>True if <paramref name="a"/> is a strict ancestor of <paramref name="b"/>.</summary>
    public bool IsAncestor(FavoriteNodeVm a, FavoriteNodeVm b)
    {
        for (var p = b.Parent; p is not null; p = p.Parent)
        {
            if (p == a)
                return true;
        }
        return false;
    }

    internal int[]? GetSelectionPath()
    {
        if (Selected is not { } node)
            return null;
        var path = new List<int>();
        for (var n = node; n is not null; n = n.Parent)
            path.Insert(0, Siblings(n).IndexOf(n));
        return [.. path];
    }

    internal void SelectPath(int[]? path)
    {
        if (path is null)
            return;
        var list = Roots;
        FavoriteNodeVm? node = null;
        foreach (int i in path)
        {
            if (list.Count == 0)
                break;
            node = list[Math.Min(i, list.Count - 1)];
            list = node.Children;
        }
        Select(node);
    }

    private bool HasSelection() => Selected is not null;

    private bool CanMoveUp() => Selected is { } n && Siblings(n).IndexOf(n) > 0;

    private bool CanMoveDown() => Selected is { } n && Siblings(n).IndexOf(n) is var i && i >= 0 && i < Siblings(n).Count - 1;

    private ObservableCollection<FavoriteNodeVm> Siblings(FavoriteNodeVm node) => node.Parent?.Children ?? Roots;

    private void MoveBy(int delta)
    {
        if (Selected is not { } node)
            return;
        var list = Siblings(node);
        int index = list.IndexOf(node);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= list.Count)
            return;

        Batch(() =>
        {
            list.RemoveAt(index);
            list.Insert(target, node);
        });
        Select(node);
    }

    // Re-asserts selection: removing a selected container can make the view clear IsSelected.
    private void Select(FavoriteNodeVm? node)
    {
        Selected = node;
        if (node is not null && !node.IsSelected)
            node.IsSelected = true;
    }

    partial void OnSelectedChanged(FavoriteNodeVm? oldValue, FavoriteNodeVm? newValue)
    {
        if (oldValue is not null)
            oldValue.IsSelected = false;
        if (newValue is not null)
            newValue.IsSelected = true;
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    private void Attach(FavoriteNodeVm node)
    {
        Detach(node);
        node.PropertyChanged += _onNodeChanged;
        node.Children.CollectionChanged += _onCollectionChanged;
        foreach (var child in node.Children)
            Attach(child);
    }

    private void Detach(FavoriteNodeVm node)
    {
        node.PropertyChanged -= _onNodeChanged;
        node.Children.CollectionChanged -= _onCollectionChanged;
        foreach (var child in node.Children)
            Detach(child);
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (FavoriteNodeVm node in e.OldItems)
                Detach(node);
        }
        if (e.NewItems is not null)
        {
            foreach (FavoriteNodeVm node in e.NewItems)
                Attach(node);
        }
        RefreshCommands();
        RaiseChanged();
    }

    private void OnNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(FavoriteNodeVm.Name):
            case nameof(FavoriteNodeVm.Path):
                RaiseChanged();
                break;
            case nameof(FavoriteNodeVm.IsSelected) when sender is FavoriteNodeVm node:
                if (node.IsSelected)
                    Selected = node;
                else if (Selected == node)
                    Selected = null;
                break;
        }
    }

    private void Batch(Action action)
    {
        _batch++;
        try
        {
            action();
        }
        finally
        {
            if (--_batch == 0 && _pending)
            {
                _pending = false;
                Changed?.Invoke();
            }
        }
    }

    private void RaiseChanged()
    {
        if (_loading)
            return;
        if (_batch > 0)
            _pending = true;
        else
            Changed?.Invoke();
    }
}
