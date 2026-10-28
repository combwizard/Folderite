using System.Diagnostics;
using Folderite.Config;
using Folderite.Interop;

namespace Folderite.Settings;

internal enum LocationKind { Default, Custom, Portable }

/// <summary>What the user answered when the target folder already holds a config.json.</summary>
internal enum ConflictChoice { UseExisting, Replace, Cancel }

internal enum MoveStatus { Moved, TrayWouldNotClose, Failed }

internal sealed record MoveOutcome(MoveStatus Status, string? Error = null);

/// <summary>Decisions and the step-by-step work behind "Change location…" and "Use default" on the General page.</summary>
internal static class ConfigMove
{
    public const string PortableNote =
        "Portable mode: the config lives next to Folderite.exe. Change location or Use default moves it out.";

    public static string PortablePrompt(string folder) =>
        $"{folder} is Folderite's own folder. Settings stored there make Folderite portable: it uses them " +
        "wherever the folder goes, for example on a USB stick.\n\nMake Folderite portable?";

    /// <summary>True when <paramref name="target"/> is the exe's folder, i.e. moving there turns on portable mode.</summary>
    public static bool IsPortableTarget(string target, string portableDir) => ConfigLocation.SameDirectory(target, portableDir);

    /// <summary>Leaving portable mode: the portable config.json must go, or it would keep winning.</summary>
    public static bool MustRetireSource(string from, string target, string portableDir, bool sourceStillHasConfig) =>
        sourceStillHasConfig && ConfigLocation.SameDirectory(from, portableDir) && !ConfigLocation.SameDirectory(target, portableDir);

    private const string TrayMutexName = @"Local\Folderite.SingleInstance";
    private static readonly TimeSpan TrayExitTimeout = TimeSpan.FromSeconds(5);

    public static LocationKind KindOf(bool portable, string currentDir, string defaultDir) =>
        portable ? LocationKind.Portable
        : ConfigLocation.SameDirectory(currentDir, defaultDir) ? LocationKind.Default
        : LocationKind.Custom;

    public static string Caption(LocationKind kind) => kind switch
    {
        LocationKind.Portable => "Portable",
        LocationKind.Custom => "Custom location",
        _ => "Default location",
    };

    public static string ConflictPrompt(string folder) =>
        $"{folder} already has Folderite settings.\n\n" +
        "Yes: use the settings in that folder.\n" +
        "No: replace them with your current settings (the old file is kept as config.json.bak).";

    /// <summary>The relocate mode to use, or null if the user cancelled. Without a config in the target there is no question.</summary>
    public static RelocateMode? ChooseMode(bool targetHasConfig, ConflictChoice answer)
    {
        if (!targetHasConfig)
            return RelocateMode.Move;
        return answer switch
        {
            ConflictChoice.UseExisting => RelocateMode.UseExisting,
            ConflictChoice.Replace => RelocateMode.Replace,
            _ => null,
        };
    }

    /// <summary>
    /// Runs on a worker thread: stop the tray, release this process's config store, move the files, switch the registry
    /// override, restart the tray. <paramref name="releaseStore"/> must dispose the store and stop it saving.
    /// </summary>
    public static MoveOutcome Execute(string from, string target, RelocateMode mode, Action releaseStore)
    {
        bool trayWasRunning = Ipc.IsRunning;
        if (trayWasRunning)
        {
            Ipc.Send(Ipc.Exit);
            if (!Ipc.WaitForExit(TrayExitTimeout))
                return new MoveOutcome(MoveStatus.TrayWouldNotClose);
            WaitForTrayProcess();
        }

        releaseStore();

        try
        {
            ConfigRelocator.Relocate(from, target, mode);
        }
        catch (Exception ex)
        {
            // Relocate copies before deleting, so the old location is still intact and the registry is untouched.
            if (trayWasRunning) StartTray();
            return new MoveOutcome(MoveStatus.Failed, ex.Message);
        }

        try
        {
            // SetOverride clears the value for the default folder and for the exe's folder (portable mode).
            ConfigLocation.SetOverride(target);
            // With UseExisting our portable config.json stays behind (it wasn't copied); keep it, but as a .bak.
            if (MustRetireSource(from, target, ConfigLocation.PortableDirectory, ConfigRelocator.HasConfig(from)))
                ConfigRelocator.Retire(from);
        }
        catch (Exception ex)
        {
            // Files are already over there but the registry still points here: put them back (best effort).
            if (mode != RelocateMode.UseExisting)
            {
                try { ConfigRelocator.Relocate(target, from, RelocateMode.Move); }
                catch { /* leave them where they are */ }
            }
            if (trayWasRunning) StartTray();
            return new MoveOutcome(MoveStatus.Failed, ex.Message);
        }

        if (trayWasRunning) StartTray();
        return new MoveOutcome(MoveStatus.Moved);
    }

    // The tray's message window vanishes before it saves history and releases its single-instance mutex. Wait for the
    // mutex so history.json is final before it is moved and the restarted tray isn't turned away as a second instance.
    private static void WaitForTrayProcess()
    {
        if (!Mutex.TryOpenExisting(TrayMutexName, out var mutex))
            return;
        using (mutex)
        {
            try
            {
                if (mutex.WaitOne(TrayExitTimeout))
                    mutex.ReleaseMutex();
            }
            catch (AbandonedMutexException)
            {
                // The tray process ended without releasing it, which is the normal way out here.
                mutex.ReleaseMutex();
            }
            catch
            {
                // Best effort.
            }
        }
    }

    private static void StartTray()
    {
        try
        {
            if (Environment.ProcessPath is { } exe)
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
        }
        catch
        {
            // The user can start it from the General page.
        }
    }
}
