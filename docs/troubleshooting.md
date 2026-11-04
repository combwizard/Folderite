# Troubleshooting

Find what you see below. Messages are quoted as Folderite shows them. Most of them come up as a balloon from the tray icon. Windows can hide balloons (Focus assist, or notifications turned off), so if you expect one and don't see it, check your notification settings.

## Folderite won't start

### A dialog says .NET must be installed

Folderite needs the **.NET 10 Desktop Runtime (x64)**. Without it, Windows shows a message from .NET (not from Folderite) saying that you must install or update .NET to run the application. It usually names the missing framework, `Microsoft.WindowsDesktop.App` version 10, and links to the download.

Install the [.NET 10 Desktop Runtime for x64](https://dotnet.microsoft.com/download/dotnet/10.0), then start Folderite again. The wording of the dialog depends on your .NET version. See [Install and uninstall](install.md).

Check that you installed the **Desktop Runtime**, not only the ASP.NET Core Runtime, and the x64 one even if you also have x86.

### Windows protected your PC (SmartScreen)

Windows may show a blue "Windows protected your PC" window the first time you run `Folderite.exe`. The release build isn't code signed, and SmartScreen warns about programs it hasn't seen often. Click **More info**, check that the name is `Folderite.exe`, and click **Run anyway**. You only do this once for the file.

If you'd rather not see it, right-click the downloaded zip, choose **Properties**, tick **Unblock** and click **OK** before you unzip it.

### I started it and nothing happened

- **It is already running.** Folderite allows one copy. Starting a second one does nothing, with no message. Look for the folder icon in the notification area, and in the hidden icons behind the **^** arrow. Hover over it: the tooltip reads "Folderite" and its version.
- **It started and quit.** If Folderite can't set up the mouse hook or its message window, it writes the error to `folderite.log` and exits without a window. Open the log (see [What to send in a bug report](#what-to-send-in-a-bug-report)) and send the last lines.
- **The icon vanished after Explorer restarted.** Folderite puts it back by itself when the taskbar is recreated. If it doesn't come back, start Folderite again.

### It doesn't start with Windows

Check **Settings > General > Start with Windows**, or the tray menu item **Start with Windows: On**. Windows keeps the exe's path from the moment you switched it on, so if you moved or renamed the Folderite folder, switch it off and on again. See [Registry values](files.md#registry-values).

## Middle-click does nothing

Work down this list. The first four are the common ones.

1. **Folderite is paused.** Right-click the tray icon. If it says **Resume**, click it. The **Pause Folderite** switch in **Settings > General** does the same. A pause also silences the keyboard shortcut.
2. **The app is on the disabled list.** Open **Settings > Exclusions** and look under **Don't open the menu in these apps**, or `disabledProcesses` in [config.json](config.md#disabled-apps). Explorer windows and the desktop are both `explorer`.
3. **Folderite doesn't support that window.** It works in Explorer, standard Open and Save dialogs, Directory Opus, Total Commander, XYplorer, Blender's file browser and the desktop. A program that draws its own file dialog isn't recognized, and a middle-click in a browser tab or an editor keeps its normal meaning on purpose. See [Supported apps](apps.md).
4. **You clicked the tab row.** In an Explorer window with tabs, the title bar row beside the tabs is left alone so that middle-click still closes a tab. Click in the file list instead, or use the [keyboard shortcut](menu.md), which has no such exemption.
5. **The desktop is switched off, or you clicked outside its icon area.** On the desktop, the menu opens from the icon area and needs **Settings > Menu > Work on the desktop** (`enableOnDesktop`) to be on.
6. **The window is elevated.** See [below](#the-window-runs-as-administrator).
7. **Folderite is not running.** Look for its tray icon. **Settings > General** also says "Folderite isn't running." and offers a **Start Folderite** button.

If the menu opens but the folder doesn't change, see [Picking a folder doesn't work](#picking-a-folder-doesnt-work).

### The window runs as administrator

Windows doesn't let a program that isn't elevated watch or control an elevated one. Folderite starts without administrator rights, so in an elevated Explorer window or file dialog the menu may not open, or a picked folder may not be used. To use Folderite there, start it as administrator (right-click `Folderite.exe` and choose **Run as administrator**). A copy started with **Start with Windows** is not elevated. See [Supported apps](apps.md#windows-you-run-as-administrator).

### The keyboard shortcut does nothing

The shortcut is set in **Settings > General > Keyboard shortcut**. If it isn't working:

- **Another program owns it.** When the shortcut can't be registered, a balloon titled "Folderite" appears, and the same text goes to `folderite.log`:

  > *Win+Shift+F* is already used by another app. Pick another shortcut in Settings › General.

  The **General** page shows a warning under the box: "This shortcut is already used by another app. Pick another one." Press a different combination.
- **Folderite can't read the text.** If you typed the value in `config.json` yourself and it isn't a valid shortcut, the balloon says:

  > "*text*" isn't a keyboard shortcut Folderite understands. Set one in Settings › General.

  Settings shows the same warning as for a taken shortcut. See [the format](config.md#keyboard-shortcut).
- **It needs a modifier.** The recording box says "Add Ctrl, Alt, Shift or Win" when you press a plain key. Only function keys (`F1` to `F24`) can be used alone.
- **Folderite is paused.** The shortcut is off while paused.

Messages for the shortcut appear once, when Folderite starts or when you change the value.

With a window in front that Folderite doesn't support, the shortcut still opens a menu, without a current folder. Picking a folder then opens a new Explorer window, which is how the tray icon behaves too.

## The menu is wrong

### A favorite is missing

- A folder favorite with no `path`, or a blank one, is skipped.
- A favorite inside a group is in that group's submenu.
- If `config.json` has an error, Folderite uses the last good settings. At startup that means built-in defaults and no favorites. See [Settings says config.json has an error](#settings-says-configjson-has-an-error).
- The **Favorites** page in Settings marks a missing local folder with "This folder doesn't exist."

### There is no current folder item

The top item is missing when Folderite can't read the folder: **This PC**, **Home** and search results in Explorer, a window that isn't supported, a dialog whose address bar can't be read, and Blender. It is also absent when you open the menu from the tray icon. See [Supported apps](apps.md).

### My actions are missing

Actions are in the submenu that opens from the current folder item, so you need a current folder. From the tray icon they don't appear. Check the **Actions** page too: with no `actions` key in the file you get the one default action, `Open &terminal here`.

### An action doesn't run

A balloon titled "Folderite" says:

> *Open in VS Code* failed: *reason*

The reason is Windows' own text, for example that the file can't be found. Check `command` on the **Actions** page: it must be a program on your `PATH` or a full path, and `%NAME%` variables are expanded. See [Actions](config.md#actions).

### The Recent submenu is empty or short

- **Menu > Recent folders** may be `0` (`recentCount`).
- The folder you are in is left out of its own list.
- Folders matching a pattern in **Exclusions** are never recorded, and hidden if they were recorded earlier. See [History exclusions](config.md#history-exclusions).
- Folderite records folders from windows it supports only, and not in an app on the disabled list.
- You cleared the history on the **History** page.

### The Drives submenu is missing

It is off by default. Turn on **Settings > Menu > Show drives**.

## Picking a folder doesn't work

- **A balloon says "Couldn't open *folder* in *Explorer*."** The last word names the window type: Explorer, File dialog, Directory Opus, Total Commander, XYplorer, Blender or Desktop. Folderite asked the window to go there and the window refused or couldn't be reached. The window may have closed or be elevated, or the folder may not exist. Try again, and check [Supported apps](apps.md) for that program.
- **A new Explorer window opens instead.** That is the design when the menu was opened from the tray icon, from the keyboard shortcut over an unsupported window, or on the desktop.
- **A dialog doesn't start in my folder.** Check [Open and Save dialogs](dialogs.md). A start folder is used only if the folder exists. When a dialog never took the folder, Folderite writes "Couldn't move the *program* file dialog to *folder*." to `folderite.log`.

## A folder or drive is not available

A network share that is asleep, a mapped drive that is disconnected or a USB drive that has been removed can't be read. Folderite is built so the menu does not freeze:

- **A favorite with subfolders** reads the folder when you open its submenu and waits at most 1.5 seconds. If the folder doesn't answer, the submenu shows the folder itself and no subfolders. Open it again when the share is back.
- **The Drives submenu** reads all drives together under the same 1.5 second limit. A drive that doesn't answer is listed without its free space.
- **A plain favorite** is not checked when the menu is built. Picking one that is gone fails in the window you were in, or shows the "Couldn't open ... in ..." balloon above.
- **Settings** marks a favorite on a missing drive letter with "This folder doesn't exist." Network (`\\server\share`) paths are not checked. A disconnected mapped drive counts as missing.
- **Recent** keeps showing a folder after it has gone. Remove it on the **History** page, or leave it, as folders you don't use fade down the list.
- **A dialog start folder** that doesn't exist is ignored, and the dialog opens where it normally would.

## Settings says config.json has an error

You see a red banner at the top of the Settings window:

> config.json has an error, so changes here are not saved.

Below it is the message from the JSON reader, which names the line and position, with the buttons **Open file** and **Reload**. The status line at the bottom reads "Autosave is off: config.json has errors", and a change you make shows "Not saved: fix config.json or reload". At startup, and when you choose **Reload** in the tray menu, Folderite shows the same message in a balloon titled "Folderite: config error".

If you break the file while Settings is open, the banner appears the next time you change something there.

Settings stops saving on purpose. If it kept going, it would overwrite the file you are in the middle of fixing with its own copy of the last good settings. In the meantime Folderite keeps using those last good settings.

To fix it:

1. Click **Open file** (or **Edit config…** in the tray menu), and go to the line in the message.
2. Look for the usual causes: a missing comma between two entries, a missing closing `}` or `]`, a single backslash in a path (it must be `\\`), quotes around a number or around `true` or `false`, or an unquoted key. Comments (`//`) and trailing commas are fine.
3. Save the file. Folderite and Settings pick it up on their own after a moment ("Reloaded: config.json changed on disk"). **Reload** in Settings reads it again at once.

If you can't find the mistake, rename `config.json` to something else and restart Folderite. It creates a fresh one with four favorites, and you can copy the parts you want from the old file. The full rules are in [When the file has a mistake](config.md#when-the-file-has-a-mistake).

A different message, "Not saved:" followed by a reason, means the file couldn't be written: the folder is read-only, the disk is full, or another program holds the file. Move the files ([Files and logs](files.md#moving-the-files)) or fix the permissions.

## Moving the settings failed

**Change location…** and **Use default** show one of these in a dialog titled Folderite:

- "Folderite didn't close, so the settings were not moved." Folderite has to stop while the files move. It didn't quit within a few seconds. Quit it from the tray icon and try again.
- A Windows error text, such as access being denied or a path not found. Your files are still in the old place; Settings reopens there with the note "Settings were not moved".

See [Files and logs](files.md#moving-the-files) for what happens when the new folder already holds settings.

## Settings won't open

Choosing **Settings…** starts a second copy of `Folderite.exe` with `--settings`. If Windows refuses, a balloon says "Could not open settings:" with the reason. If the window never appears, look in `folderite.log` for an `ERROR` line from that moment. If Settings is already open, the command brings it to the front instead.

## A problem with one app

Each program has its own limits and its own way to tell it is working: see [Supported apps](apps.md). The usual ones are Explorer's tab row, a Directory Opus panel that isn't in front, and Blender, where the current folder can't be read.

## What to send in a bug report

Report problems at [github.com/combwizard/Folderite/issues](https://github.com/combwizard/Folderite/issues). It helps to include:

- **The version.** Hover over the tray icon: the tooltip reads "Folderite 0.3.0", or similar. **Settings > General** shows the same at the bottom.
- **Your Windows version**, and the program you were in (and its version).
- **What you did and what you saw**, step by step, and what you expected. Say whether you used the mouse or the keyboard shortcut, and quote any balloon text.
- **`folderite.log`**, or its last lines. It sits next to `config.json`. Click **Open config folder** in the tray menu, or **Open folder** under **Settings > General**. The file exists only after something was logged, and Folderite deletes it and starts a new one when it passes about 1 MB. See [The log](files.md#the-log).
- **`config.json`**, if it may matter. Remove any folder names you don't want to share.

Config errors are not written to the log, so for those send the message from the balloon or the Settings banner. If Folderite stopped on its own, the log's last `ERROR` entry is the most useful thing you can send.
