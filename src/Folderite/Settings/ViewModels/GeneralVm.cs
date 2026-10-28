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
        IsPortable = string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(store.Directory)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)),
            StringComparison.OrdinalIgnoreCase);
        Version = ReadVersion();
        RefreshStatus();
    }

    public string ConfigPath { get; }
    public string ConfigDirectory { get; }
    public bool IsPortable { get; }
    public string Version { get; }

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
