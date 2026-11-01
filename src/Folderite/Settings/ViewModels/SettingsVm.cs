using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Folderite.Config;

namespace Folderite.Settings.ViewModels;

/// <summary>Editable view of config.json with debounced autosave.</summary>
public sealed partial class SettingsVm : ObservableObject, IDisposable
{
    public const int MaxRecentCount = 50;
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(400);

    private readonly ConfigStore _store;
    private readonly Action<Action> _post;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly object _saveLock = new();
    private readonly PropertyChangedEventHandler _onItemChanged;

    private AppConfig _baseline = new();
    private ITimer? _timer;
    private AppConfig? _snapshot;
    private bool _saving;
    private bool _loading;
    private int _recentCount = 15;

    public SettingsVm(ConfigStore store, Action<Action> post, TimeProvider? time = null)
    {
        _store = store;
        _post = post;
        _time = time ?? TimeProvider.System;
        _onItemChanged = (_, _) => ScheduleSave();

        Favorites.Changed += ScheduleSave;
        Watch(Actions);
        Watch(HistoryExclude);
        Watch(DisabledProcesses);
        Watch(DialogFolders);
        Actions.CollectionChanged += (_, _) => RefreshActionCommands();
    }

    public FavoritesTreeVm Favorites { get; } = new();
    public ObservableCollection<ActionVm> Actions { get; } = [];
    public ObservableCollection<StringItemVm> HistoryExclude { get; } = [];
    public ObservableCollection<StringItemVm> DisabledProcesses { get; } = [];
    public ObservableCollection<DialogFolderVm> DialogFolders { get; } = [];

    [ObservableProperty]
    private ActionVm? selectedAction;

    [ObservableProperty]
    private bool showOpenWindows = true;

    [ObservableProperty]
    private bool showClipboard = true;

    [ObservableProperty]
    private bool enableOnDesktop = true;

    /// <summary>Keyboard shortcut text ("Win+Shift+F"), "" for none.</summary>
    [ObservableProperty]
    private string hotkey = "";

    [ObservableProperty]
    private bool showDrives;

    [ObservableProperty]
    private bool dialogLastFolder;

    [ObservableProperty]
    private string status = "";

    [ObservableProperty]
    private string? loadError;

    [ObservableProperty]
    private bool autosaveEnabled = true;

    /// <summary>Clamped to 0..50.</summary>
    public int RecentCount
    {
        get => _recentCount;
        set
        {
            int clamped = Math.Clamp(value, 0, MaxRecentCount);
            if (SetProperty(ref _recentCount, clamped))
                ScheduleSave();
            else if (clamped != value)
                OnPropertyChanged();
        }
    }

    public bool HasPendingSave
    {
        get { lock (_gate) return _snapshot is not null || _saving; }
    }

    partial void OnShowOpenWindowsChanged(bool value) => ScheduleSave();
    partial void OnShowClipboardChanged(bool value) => ScheduleSave();
    partial void OnEnableOnDesktopChanged(bool value) => ScheduleSave();
    partial void OnHotkeyChanged(string value) => ScheduleSave();
    partial void OnShowDrivesChanged(bool value) => ScheduleSave();
    partial void OnDialogLastFolderChanged(bool value) => ScheduleSave();
    partial void OnSelectedActionChanged(ActionVm? value) => RefreshActionCommands();

    /// <summary>Resets everything from the store's current config without scheduling a save.</summary>
    public void Load()
    {
        CancelPending();
        _loading = true;
        try
        {
            _baseline = ConfigStore.Clone(_store.Current);
            Favorites.Load(_baseline.Favorites);
            Fill(Actions, _baseline.Actions.Select(ActionVm.From));
            Fill(HistoryExclude, _baseline.HistoryExclude.Select(s => new StringItemVm(s)));
            Fill(DisabledProcesses, _baseline.DisabledProcesses.Select(s => new StringItemVm(s)));
            Fill(DialogFolders, _baseline.DialogFolders.Select(DialogFolderVm.From));
            SelectedAction = null;
            RecentCount = _baseline.RecentCount;
            ShowOpenWindows = _baseline.ShowOpenWindows;
            ShowClipboard = _baseline.ShowClipboard;
            EnableOnDesktop = _baseline.EnableOnDesktop;
            Hotkey = _baseline.Hotkey ?? "";
            ShowDrives = _baseline.ShowDrives;
            DialogLastFolder = _baseline.DialogLastFolder;

            LoadError = _store.LastError;
            AutosaveEnabled = LoadError is null;
            Status = LoadError is null ? "" : "Autosave is off: config.json has errors";
        }
        finally
        {
            _loading = false;
        }
        RefreshActionCommands();
    }

    /// <summary>Re-reads config.json from disk, then <see cref="Load"/>.</summary>
    public void Reload()
    {
        _store.Load();
        Load();
    }

    /// <summary>Call when the settings process's own ConfigStore reports a change on disk.</summary>
    public void OnExternalChange()
    {
        if (HasPendingSave)
            return;

        int actionIndex = SelectedAction is { } a ? Actions.IndexOf(a) : -1;
        var favoritePath = Favorites.GetSelectionPath();
        Load();
        if (actionIndex >= 0 && Actions.Count > 0)
            SelectedAction = Actions[Math.Min(actionIndex, Actions.Count - 1)];
        Favorites.SelectPath(favoritePath);
        if (LoadError is null)
            Status = "Reloaded: config.json changed on disk";
    }

    public AppConfig BuildConfig()
    {
        var config = ConfigStore.Clone(_baseline);
        config.Favorites = Favorites.ToConfig();
        config.Actions = [.. Actions.Select(a => a.ToConfig())];
        config.RecentCount = RecentCount;
        config.ShowOpenWindows = ShowOpenWindows;
        config.ShowClipboard = ShowClipboard;
        config.EnableOnDesktop = EnableOnDesktop;
        config.HistoryExclude = Clean(HistoryExclude);
        config.DisabledProcesses = Clean(DisabledProcesses);
        config.Hotkey = Hotkey.Trim();
        config.ShowDrives = ShowDrives;
        config.DialogLastFolder = DialogLastFolder;
        config.DialogFolders = [.. DialogFolders.Select(d => d.ToConfig()).Where(d => d.App.Length > 0 && d.Path.Length > 0)];
        return config;
    }

    /// <summary>Writes a pending snapshot immediately (window close).</summary>
    public void SaveNow()
    {
        AppConfig? snapshot;
        lock (_gate)
        {
            snapshot = _snapshot;
            _snapshot = null;
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            if (snapshot is not null)
                _saving = true;
        }
        if (snapshot is null)
            return;

        if (_store.LastError is { } error)
        {
            lock (_gate) _saving = false;
            StopAutosave(error);
            Status = "Not saved: fix config.json or reload";
            return;
        }

        try
        {
            lock (_saveLock) _store.Save(snapshot);
            Status = "Saved";
        }
        catch (Exception ex)
        {
            Status = "Not saved: " + ex.Message;
        }
        finally
        {
            lock (_gate) _saving = false;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    [RelayCommand]
    private void AddAction()
    {
        var action = new ActionVm { Name = "New action", Command = "", Args = "\"{path}\"" };
        Actions.Add(action);
        SelectedAction = action;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedAction))]
    private void RemoveAction()
    {
        if (SelectedAction is not { } action)
            return;
        int index = Actions.IndexOf(action);
        Actions.RemoveAt(index);
        SelectedAction = Actions.Count == 0 ? null : Actions[Math.Min(index, Actions.Count - 1)];
    }

    [RelayCommand(CanExecute = nameof(CanMoveActionUp))]
    private void MoveActionUp() => MoveAction(-1);

    [RelayCommand(CanExecute = nameof(CanMoveActionDown))]
    private void MoveActionDown() => MoveAction(1);

    [RelayCommand]
    private void AddExclude() => HistoryExclude.Add(new StringItemVm());

    [RelayCommand]
    private void RemoveExclude(StringItemVm? item)
    {
        if (item is not null)
            HistoryExclude.Remove(item);
    }

    [RelayCommand]
    private void AddDialogFolder() => DialogFolders.Add(new DialogFolderVm());

    [RelayCommand]
    private void RemoveDialogFolder(DialogFolderVm? item)
    {
        if (item is not null)
            DialogFolders.Remove(item);
    }

    [RelayCommand]
    private void AddDisabledProcess(string? name)
    {
        var clean = (name ?? "").Trim();
        if (clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            clean = clean[..^4].TrimEnd();
        if (clean.Length == 0 || DisabledProcesses.Any(p => string.Equals(p.Value.Trim(), clean, StringComparison.OrdinalIgnoreCase)))
            return;
        DisabledProcesses.Add(new StringItemVm(clean));
    }

    [RelayCommand]
    private void RemoveDisabledProcess(StringItemVm? item)
    {
        if (item is not null)
            DisabledProcesses.Remove(item);
    }

    private bool HasSelectedAction() => SelectedAction is not null;
    private bool CanMoveActionUp() => SelectedAction is { } a && Actions.IndexOf(a) > 0;
    private bool CanMoveActionDown() => SelectedAction is { } a && Actions.IndexOf(a) is var i && i >= 0 && i < Actions.Count - 1;

    private void MoveAction(int delta)
    {
        if (SelectedAction is not { } action)
            return;
        int index = Actions.IndexOf(action);
        int target = index + delta;
        if (index < 0 || target < 0 || target >= Actions.Count)
            return;
        Actions.Move(index, target);
        SelectedAction = action;
        RefreshActionCommands();
    }

    private void RefreshActionCommands()
    {
        RemoveActionCommand.NotifyCanExecuteChanged();
        MoveActionUpCommand.NotifyCanExecuteChanged();
        MoveActionDownCommand.NotifyCanExecuteChanged();
    }

    private static List<string> Clean(IEnumerable<StringItemVm> items) =>
        [.. items.Select(i => i.Value.Trim()).Where(s => s.Length > 0)];

    private void Watch<T>(ObservableCollection<T> collection) where T : ObservableObject
    {
        collection.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
            {
                foreach (T item in e.OldItems)
                    item.PropertyChanged -= _onItemChanged;
            }
            if (e.NewItems is not null)
            {
                foreach (T item in e.NewItems)
                    item.PropertyChanged += _onItemChanged;
            }
            ScheduleSave();
        };
    }

    private void Fill<T>(ObservableCollection<T> collection, IEnumerable<T> items) where T : ObservableObject
    {
        foreach (var item in collection)
            item.PropertyChanged -= _onItemChanged;
        collection.Clear();
        foreach (var item in items)
            collection.Add(item);
    }

    private void CancelPending()
    {
        lock (_gate)
        {
            _snapshot = null;
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    private void ScheduleSave()
    {
        if (_loading)
            return;
        if (_store.LastError is { } error)
            StopAutosave(error);
        if (!AutosaveEnabled)
        {
            Status = "Not saved: fix config.json or reload";
            return;
        }

        var snapshot = BuildConfig();
        lock (_gate)
        {
            _snapshot = snapshot;
            if (_timer is null)
                _timer = _time.CreateTimer(_ => OnTimer(), null, SaveDelay, Timeout.InfiniteTimeSpan);
            else
                _timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    // A failed hot reload doesn't raise ConfigStore.Changed, only sets LastError. Stop rather than overwrite a
    // config.json the user is in the middle of fixing by hand.
    private void StopAutosave(string error)
    {
        LoadError = error;
        AutosaveEnabled = false;
    }

    private void OnTimer()
    {
        AppConfig? snapshot;
        lock (_gate)
        {
            snapshot = _snapshot;
            _snapshot = null;
            if (snapshot is not null)
                _saving = true;
        }
        if (snapshot is null)
            return;

        if (_store.LastError is { } error)
        {
            lock (_gate) _saving = false;
            _post(() =>
            {
                StopAutosave(error);
                Status = "Not saved: fix config.json or reload";
            });
            return;
        }

        try
        {
            lock (_saveLock) _store.Save(snapshot);
            _post(() => Status = "Saved");
        }
        catch (Exception ex)
        {
            var message = "Not saved: " + ex.Message;
            _post(() => Status = message);
        }
        finally
        {
            lock (_gate) _saving = false;
        }
    }
}
