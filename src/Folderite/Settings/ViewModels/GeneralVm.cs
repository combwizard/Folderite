using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Folderite.Config;
using Folderite.Interop;
using Folderite.Tray;

namespace Folderite.Settings.ViewModels;

/// <summary>General page: autostart, tray status/pause, config location and version.</summary>
public sealed partial class GeneralVm : ObservableObject
{
    private bool _refreshing;
    private bool _isPaused;

    public GeneralVm(ConfigStore store)
    {
        ConfigPath = store.ConfigPath;
        ConfigDirectory = store.Directory;
        IsPortable = ConfigLocation.IsPortable;
        LocationKind = ConfigMove.KindOf(IsPortable, ConfigDirectory, ConfigLocation.DefaultDirectory);
        Version = ReadVersion();
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

    private static string ReadVersion()
    {
        var info = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "";
        int plus = info.IndexOf('+');
        return plus >= 0 ? info[..plus] : info;
    }
}
