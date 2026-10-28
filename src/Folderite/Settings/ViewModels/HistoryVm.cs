using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Folderite.History;
using Folderite.Interop;
using Folderite.Menu;

namespace Folderite.Settings.ViewModels;

public interface IHistoryBackend
{
    IReadOnlyList<HistoryEntry> Load();
    void Remove(string path);
    void Clear();
}

/// <summary>Talks to the tray app over IPC when it runs (it owns history.json), else edits the file directly.</summary>
public sealed class TrayHistoryBackend(string historyPath) : IHistoryBackend
{
    public IReadOnlyList<HistoryEntry> Load()
    {
        if (Ipc.IsRunning)
            Ipc.Send(Ipc.FlushHistory);

        var store = new FrecencyStore(historyPath);
        store.Load();
        return store.Entries();
    }

    public void Remove(string path)
    {
        if (Ipc.IsRunning)
        {
            Ipc.Send(Ipc.RemoveHistory, path);
            return;
        }

        var store = new FrecencyStore(historyPath);
        store.Load();
        store.Remove(path);
        store.Save();
    }

    public void Clear()
    {
        if (Ipc.IsRunning)
        {
            Ipc.Send(Ipc.ClearHistory);
            return;
        }

        var store = new FrecencyStore(historyPath);
        store.Load();
        store.Clear();
        store.Save();
    }
}

public sealed partial class HistoryRowVm : ObservableObject
{
    private readonly TimeProvider _time;

    public HistoryRowVm(HistoryEntry entry, TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
        Path = entry.Path;
        Score = entry.Score;
        LastAccess = entry.LastAccess;
        Name = MenuComposer.DisplayName(entry.Path);
        Parent = System.IO.Path.GetDirectoryName(entry.Path.TrimEnd('\\')) ?? "";
    }

    public string Path { get; }
    public string Name { get; }
    public string Parent { get; }
    public double Score { get; }
    public DateTimeOffset LastAccess { get; }

    /// <summary>Relative to now; evaluated on every access.</summary>
    public string LastUsedText
    {
        get
        {
            var age = _time.GetUtcNow() - LastAccess;
            if (age < TimeSpan.FromMinutes(1))
                return "just now";
            if (age < TimeSpan.FromHours(1))
                return $"{(int)age.TotalMinutes} min ago";
            if (age < TimeSpan.FromHours(24))
                return $"{(int)age.TotalHours} h ago";
            if (age < TimeSpan.FromHours(48))
                return "yesterday";
            if (age <= TimeSpan.FromDays(30))
                return $"{(int)age.TotalDays} days ago";
            return LastAccess.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }
}

public sealed partial class HistoryVm : ObservableObject
{
    private readonly IHistoryBackend _backend;
    private readonly TimeProvider _time;
    private List<HistoryRowVm> _all = [];

    public HistoryVm(IHistoryBackend backend, TimeProvider? time = null)
    {
        _backend = backend;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The rows matching <see cref="Filter"/>.</summary>
    public ObservableCollection<HistoryRowVm> Rows { get; } = [];

    [ObservableProperty]
    private string filter = "";

    [ObservableProperty]
    private HistoryRowVm? selected;

    [ObservableProperty]
    private int totalCount;

    partial void OnFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    public void Refresh()
    {
        string? selectedPath = Selected?.Path;
        _all = [.. _backend.Load().Select(e => new HistoryRowVm(e, _time))];
        TotalCount = _all.Count;
        ApplyFilter();
        Selected = selectedPath is null ? null : Rows.FirstOrDefault(r => r.Path == selectedPath);
    }

    [RelayCommand]
    public void Remove(HistoryRowVm? row)
    {
        row ??= Selected;
        if (row is null)
            return;
        _backend.Remove(row.Path);
        Refresh();
    }

    [RelayCommand]
    public void ClearAll()
    {
        _backend.Clear();
        Refresh();
    }

    private void ApplyFilter()
    {
        string needle = (Filter ?? "").Trim();
        Rows.Clear();
        foreach (var row in _all)
        {
            if (needle.Length == 0 || row.Path.Contains(needle, StringComparison.OrdinalIgnoreCase))
                Rows.Add(row);
        }
    }
}
