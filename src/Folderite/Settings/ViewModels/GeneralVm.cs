using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Folderite.Config;
using Folderite.Interop;
using Folderite.Tray;

namespace Folderite.Settings.ViewModels;

/// <summary>General page: autostart, tray status/pause, keyboard shortcut, config location and version.</summary>
public sealed partial class GeneralVm : ObservableObject
{
    private bool _refreshing;
    private bool _isPaused;
    private SettingsVm? _settings;
    private int _hotkeyCheck;

    // The tray app reloads config.json about 300 ms after it changes on disk, then re-registers the shortcut.
    private static readonly TimeSpan HotkeySettleTime = TimeSpan.FromMilliseconds(1500);

    public GeneralVm(ConfigStore store)
    {
        ConfigPath = store.ConfigPath;
        ConfigDirectory = store.Directory;
        IsPortable = ConfigLocation.IsPortable;
        LocationKind = ConfigMove.KindOf(IsPortable, ConfigDirectory, ConfigLocation.DefaultDirectory);
        Version = AppInfo.Version;
        RefreshStatus();
    }

    public string ConfigPath { get; }
    public string ConfigDirectory { get; }
    public bool IsPortable { get; }
    public string Version { get; }

    internal LocationKind LocationKind { get; }
    public string LocationCaption => ConfigMove.Caption(LocationKind);
    public string PortableNote => ConfigMove.PortableNote;
    public bool CanChangeLocation => true;
    /// <summary>"Use default" moves a custom or portable config back to %APPDATA%\Folderite.</summary>
    public bool CanUseDefault => LocationKind != LocationKind.Default;

    // Wired by SettingsWindow: it owns the SettingsVm and the config store.
    internal Action? FlushPending { get; set; }
    internal Action? ReleaseStore { get; set; }
    internal Action<string>? SetStatus { get; set; }

    /// <summary>
    /// Moves the config to <paramref name="target"/>: flush edits, then (off the UI thread) stop the tray, release the
    /// store, move the files, switch the registry override and restart the tray. The caller handles dialogs and reopening.
    /// </summary>
    internal async Task<MoveOutcome> MoveConfigAsync(string target, RelocateMode mode)
    {
        FlushPending?.Invoke();
        SetStatus?.Invoke("Moving settings…");

        var from = ConfigDirectory;
        var release = ReleaseStore ?? (() => { });
        var outcome = await Task.Run(() => ConfigMove.Execute(from, target, mode, release));

        if (outcome.Status != MoveStatus.Moved)
            SetStatus?.Invoke("Settings were not moved");
        return outcome;
    }

    [ObservableProperty]
    private bool isTrayRunning;

    /// <summary>True when the tray app says another program already owns the keyboard shortcut.</summary>
    [ObservableProperty]
    private bool hotkeyConflict;

    /// <summary>The keyboard shortcut, as text ("" = none). It lives in <see cref="SettingsVm"/>, which autosaves it.</summary>
    public string Hotkey => _settings?.Hotkey ?? "";
    public bool HasHotkey => Hotkey.Length > 0;

    /// <summary>
    /// The page's DataContext is this view model, but the shortcut is saved by the window's <see cref="SettingsVm"/>,
    /// so the page hands that over once it is in the window. Safe to call again.
    /// </summary>
    internal void AttachSettings(SettingsVm settings)
    {
        if (_settings != settings)
        {
            if (_settings is not null)
                _settings.PropertyChanged -= OnSettingsChanged;
            _settings = settings;
            settings.PropertyChanged += OnSettingsChanged;
            OnPropertyChanged(nameof(Hotkey));
            OnPropertyChanged(nameof(HasHotkey));
        }
        _ = CheckHotkeyAsync(TimeSpan.Zero);
    }

    internal void SetHotkey(string text)
    {
        if (_settings is not null)
            _settings.Hotkey = text;
    }

    [RelayCommand]
    public void ClearHotkey() => SetHotkey("");

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsVm.Hotkey))
            return;
        OnPropertyChanged(nameof(Hotkey));
        OnPropertyChanged(nameof(HasHotkey));
        HotkeyConflict = false; // the old answer is about the old shortcut
        _ = CheckHotkeyAsync(HotkeySettleTime);
    }

    /// <summary>Asks the tray app whether the shortcut registered. A newer check cancels an older one; no answer shows nothing.</summary>
    private async Task CheckHotkeyAsync(TimeSpan delay)
    {
        int ticket = ++_hotkeyCheck;
        if (delay > TimeSpan.Zero)
            await Task.Delay(delay);
        if (ticket != _hotkeyCheck)
            return;

        // No answer, or 0 from an older Folderite that doesn't know the command, shows nothing.
        var answer = await Task.Run(() => Ipc.Send(Ipc.GetHotkeyStatus));
        if (ticket == _hotkeyCheck)
            HotkeyConflict = answer == Ipc.HotkeyFailed;
    }

    public bool StartWithWindows
    {
        get
        {
            try { return Autostart.IsEnabled; }
            catch { return false; }
        }
        set
        {
            if (value == StartWithWindows)
                return;
            try { Autostart.Set(value); }
            catch { /* registry not writable; the getter reports the real state */ }
            OnPropertyChanged();
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (!SetProperty(ref _isPaused, value))
                return;
            if (!_refreshing)
                Ipc.Send(Ipc.SetPaused, value ? "1" : "0");
        }
    }

    [RelayCommand]
    public void RefreshStatus()
    {
        _refreshing = true;
        try
        {
            IsTrayRunning = Ipc.IsRunning;
            IsPaused = Ipc.Send(Ipc.GetPaused) is { } r && r != 0;
            OnPropertyChanged(nameof(StartWithWindows));
        }
        finally
        {
            _refreshing = false;
        }
    }

    [RelayCommand]
    public void OpenConfigFile()
    {
        try { Process.Start(new ProcessStartInfo(ConfigPath) { UseShellExecute = true }); }
        catch
        {
            try { Process.Start("notepad.exe", $"\"{ConfigPath}\""); }
            catch { /* nothing else to try */ }
        }
    }

    [RelayCommand]
    public void OpenConfigFolder()
    {
        try { Process.Start("explorer.exe", $"\"{ConfigDirectory}\""); }
        catch { /* explorer unavailable */ }
    }

    [RelayCommand]
    public async Task StartFolderite()
    {
        try
        {
            if (Environment.ProcessPath is { } exe)
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
        }
        catch
        {
            return;
        }
        await Task.Delay(1000);
        RefreshStatus();
    }
}
