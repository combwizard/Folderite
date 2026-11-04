# Install and uninstall

Folderite has no installer. You unzip a folder, run `Folderite.exe`, and it sits in the notification area. Uninstalling means deleting that folder, plus a few leftovers listed [below](#uninstall).

## Requirements

- Windows 10 version 1903 or later, or Windows 11.
- A 64-bit PC. The release is built for `win-x64` only.
- The [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0). Folderite is built as a framework-dependent app, so the release does not carry its own copy of .NET. Other .NET 10 apps share the same runtime.

You do not need administrator rights. Folderite runs with the same rights as the program that starts it. By default it writes only to your own profile and the current-user part of the registry.

## Install

1. Install the .NET 10 Desktop Runtime (x64) if you do not have it.
2. Download `Folderite-<version>-win-x64.zip` from the [latest release](https://github.com/combwizard/Folderite/releases/latest).
3. Unzip it into a folder you will keep, for example `C:\Tools\Folderite`. The zip has no folder inside it, so extract it into a folder of its own.
4. Run `Folderite.exe`.

Folderite opens no window. Look for its icon in the notification area at the right end of the taskbar. If you do not see it, open the overflow with the small arrow, and drag the icon out to keep it visible. Hover the icon to see the version, for example "Folderite 0.3.0".

Keep the folder where it is. Folderite runs from there, and [Start with Windows](#start-with-windows) remembers that location.

Running `Folderite.exe` a second time does nothing, because only one copy runs at a time.

### Windows says "Windows protected your PC"

The exe is not code-signed, so SmartScreen may warn about it the first time it runs. Click **More info**, then **Run anyway**.

The release zip is built from the public source by GitHub Actions, and you can always [build it yourself](building.md).

## First run

The first start creates the config folder `%APPDATA%\Folderite` and a `config.json` in it with a starter setup:

- four favorites: **Home**, **Desktop**, **Downloads** and **Documents**,
- one action, **Open terminal here**, which runs `wt.exe` (Windows Terminal),
- the Open windows and Clipboard sections on, and the Drives section off.

Folderite also keeps your folder history in `history.json` there, and writes `folderite.log` next to them when something goes wrong. See [Files and logs](files.md) for where these live and how to move them.

Go on to [Getting started](getting-started.md) for your first middle-click.

## Start with Windows

To have Folderite start when you sign in, do either of these:

- Right-click the tray icon and choose **Start with Windows: Off**. The entry then reads **Start with Windows: On**.
- Open [Settings](settings.md) and turn on **Start with Windows** under **General**.

Folderite writes a value named `Folderite` under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`. Its data is the full, quoted path of `Folderite.exe`. It is per user, so it needs no administrator rights. Turning the option off deletes the value.

If you move or rename the Folderite folder later, the value still points at the old path, and the tray menu still shows **On** because the value exists. Choose it to turn it **Off**, then run `Folderite.exe` from its new place and turn it **On** again.

## Upgrade

1. Right-click the tray icon and choose **Exit**. Close the Settings window too if it is open. Settings runs from the same `Folderite.exe`, and Windows will not let you overwrite a running file.
2. Unzip the new release over the old folder and replace the files.
3. Run `Folderite.exe` again.

Your settings are kept. They live in `%APPDATA%\Folderite`, outside the program folder. If you made Folderite [portable](files.md), `config.json` sits next to the exe, and the zip does not contain that file, so unzipping does not overwrite it.

Folderite does not check for updates. Watch the [releases page](https://github.com/combwizard/Folderite/releases) if you want to hear about new versions. The current version is shown in the tray icon tooltip and under **Settings**, **General**.

If **Start with Windows** was on and the folder stayed the same, it keeps working after the upgrade.

## Uninstall

1. If **Start with Windows** is on, turn it off first: right-click the tray icon and choose **Start with Windows: On**. This removes the registry value.
2. Right-click the tray icon and choose **Exit**. Close the Settings window if it is open.
3. Delete the Folderite folder.

That removes the program. Folderite leaves these behind, and each is safe to delete:

| What | Where | When it exists |
| --- | --- | --- |
| Settings, history and log | `%APPDATA%\Folderite` (`config.json`, `history.json`, `folderite.log`), or the folder you moved them to | Always, unless Folderite was portable, in which case they were in the program folder and are already gone |
| Remembered config location | `HKEY_CURRENT_USER\Software\Folderite`, value `ConfigDirectory` | Only if you used **Change location…** to pick another folder |
| Start with Windows entry | `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`, value `Folderite` | Only if you left **Start with Windows** on |

Delete the registry values with Registry Editor if you want no trace. Folderite only reads the Windows theme setting under `Software\Microsoft\Windows\CurrentVersion\Themes\Personalize`. It never changes it.

Folderite adds no shortcuts, services, scheduled tasks or machine-wide settings.
