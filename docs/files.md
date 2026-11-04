# Files and logs

Folderite keeps three files, always together in one folder, and writes only two registry values.

| File | What it holds |
| --- | --- |
| `config.json` | Your settings. See [The config file](config.md). |
| `history.json` | The folders you visit, with how often and how recently, for the **Recent** submenu. |
| `folderite.log` | Errors and a few notices, for bug reports. It only exists once something has been logged. |

## Where the files are

Folderite picks the folder in this order and uses the first that applies:

1. **Next to `Folderite.exe`**, if a `config.json` is there. This is [portable mode](#portable-mode).
2. **The folder in the registry**, if the value `ConfigDirectory` under `HKEY_CURRENT_USER\Software\Folderite` is set and not blank. Settings sets it when you use **Change location…**. Environment variables such as `%OneDrive%` in it are expanded.
3. **`%APPDATA%\Folderite`**, which is `C:\Users\<you>\AppData\Roaming\Folderite`. This is the default.

The rule looks for `config.json` only in the first case. A folder named in the registry doesn't need a `config.json` yet: Folderite creates the folder and a new file with default settings.

All three files always sit in the same folder. The log follows the config, including in portable mode, where it is written next to `Folderite.exe`.

To see where you are, open **Settings > General**. The **Config file** card shows the full path, with a caption that says **Default location**, **Custom location** or **Portable**. **Open file** and **Open folder** are on the same card, and the tray menu has **Edit config…** and **Open config folder**.

Folderite finds the folder when it starts. If you change where the files are by hand (create a `config.json` next to the exe, or edit the registry value), quit Folderite from the tray and start it again.

## Moving the files

On **Settings > General**, the **Config file** card has two buttons:

- **Change location…** asks for a folder ("Choose a folder for Folderite's settings") and moves the files there. Folderite remembers the folder in the registry.
- **Use default** moves them back to `%APPDATA%\Folderite`. It shows only when the files are somewhere else.

Moving stops Folderite, moves the files, and starts it again, then reopens Settings with a note such as "Settings moved to D:\Sync\Folderite". The Settings window is disabled meanwhile. Nothing is deleted from the old folder until every file has been copied, so a failure leaves your settings where they were. If Folderite doesn't close within a few seconds, you get "Folderite didn't close, so the settings were not moved." and nothing changes. Any other failure shows its Windows error and reopens Settings on the old location.

`config.json`, `history.json` and `folderite.log` all move.

### When the new folder already has settings

If the folder you pick already holds a `config.json`, Folderite asks:

> *folder* already has Folderite settings.
>
> Yes: use the settings in that folder.
> No: replace them with your current settings (the old file is kept as config.json.bak).

| Answer | Result |
| --- | --- |
| **Yes** | You switch to the settings in that folder. Your current `config.json` is left behind in the old folder. Your history is copied over only if the new folder has none; otherwise it stays behind too. |
| **No** | The folder's `config.json` is renamed to `config.json.bak` and yours is copied in. `history.json` gets the same treatment when you have one to copy. |
| **Cancel** | Nothing happens. |

Whenever a `.bak` file already exists it is replaced. The log is moved in every case and overwrites a `folderite.log` already in the new folder, without a backup.

## Portable mode

A `config.json` next to `Folderite.exe` always wins over the registry and over `%APPDATA%`. With that file in place, Folderite is portable: the settings, history and log travel with the folder, for example on a USB stick, and the registry value and `%APPDATA%` are ignored.

To turn it on, choose Folderite's own folder in **Change location…** and answer **OK** to:

> *folder* is Folderite's own folder. Settings stored there make Folderite portable: it uses them wherever the folder goes, for example on a USB stick.
>
> Make Folderite portable?

The registry value is removed in the process, so the folder alone decides. You can also copy a `config.json` next to the exe yourself and restart Folderite.

While it is portable, the **Config file** card shows a **Portable** badge and the note "Portable mode: the config lives next to Folderite.exe. Change location or Use default moves it out."

To leave portable mode, use **Use default** or **Change location…** to a different folder. If you answered **Yes** to using settings that were already in the other folder, your portable `config.json` is renamed to `config.json.bak`, because it would otherwise keep winning.

Two cautions:

- The folder must be writable. In a protected folder such as `C:\Program Files`, Settings can't save and shows "Not saved:" with the reason, and the log can't be written.
- **Start with Windows** stores the exe's full path (see below). If you move a portable folder to another drive letter or path, turn the option off and on again.

## Backing up

Copy `config.json` for your settings and `history.json` for your recent folders. That's all. Restore them by putting them back in the folder from [Where the files are](#where-the-files-are), or move to a new PC by pointing a new install at a folder that holds them (see above).

- Quit Folderite from the tray first if you want the latest history. It writes `history.json` about every 60 seconds when something changed, when you quit, and when you open the **History** page in Settings. A copy taken while it runs can miss the last minute.
- `history.json` is rebuilt as you work. If it can't be read, Folderite starts with an empty history, and your next save replaces the file. Deleting it while Folderite is quit clears your history (the **History** page in Settings can also remove entries one by one).
- You may see short-lived files named like `config.json.<random>.tmp`. Folderite writes the new file under that name and then renames it over the old one, so a crash can't leave a half-written `config.json`.
- To start over, quit Folderite, delete `config.json` and start it again. The default file is created.

## The log

`folderite.log` is a plain text file. Each entry is one line, or a few for an error with a stack trace, starting with the local date and time:

```
2026-11-04 09:15:02 INFO  Win+Shift+F is already used by another app. Pick another shortcut in Settings › General.
2026-11-04 09:20:41 ERROR System.IO.IOException: ...
```

`INFO` lines are notices. `ERROR` lines are unexpected exceptions with their details. What gets written:

- **Startup failures.** If Folderite can't start (for example the mouse hook can't be installed), the error is logged and it exits.
- **Unexpected errors** while handling a click, a window event, the open-window list, a subfolder submenu or the **Drives** submenu. Ordinary conditions such as an offline folder or a missing drive are not logged.
- **Keyboard shortcut problems.** The two messages listed under [Troubleshooting](troubleshooting.md#the-keyboard-shortcut-does-nothing).
- **Dialogs.** "Couldn't move the *program* file dialog to *folder*." when a dialog never took the start folder you set.
- **The menu.** "Menu owner could not take the foreground; outside clicks may not dismiss the menu."

Not in the log: config errors (they appear in a balloon and in Settings), the folders you visit, and file names.

**Size.** The file is appended to. Before each write, Folderite checks it, and when it is larger than 1,000,000 bytes (about 1 MB) it deletes the whole file and starts a new one. So the log never grows past roughly 1 MB, and after a reset the older entries are gone. Copy the file first if you need them.

If the folder can't be written to, logging stops silently. It can't cause an error of its own.

You can delete the log any time. It comes back when the next entry is written.

## Registry values

Folderite writes two values, both under `HKEY_CURRENT_USER`, so no administrator rights are needed.

| Key | Value | Written when | Content |
| --- | --- | --- | --- |
| `Software\Folderite` | `ConfigDirectory` | You move the files with **Change location…** to a folder that is not the default and not Folderite's own. | The full path of the folder (a string). |
| `Software\Microsoft\Windows\CurrentVersion\Run` | `Folderite` | **Start with Windows** is switched on, in Settings or the tray menu. | The full path of `Folderite.exe` in quotes. |

Both values are removed when you switch the option off, or, for `ConfigDirectory`, when you choose **Use default** or Folderite's own folder. Folderite never writes to `HKEY_LOCAL_MACHINE`.

The `Run` value holds the path as it was when you switched the option on. If you move or rename the Folderite folder, switch **Start with Windows** off and on again from the new location.

To remove every trace, switch **Start with Windows** off, choose **Use default** in Settings, quit Folderite, and delete the folder. Then delete `%APPDATA%\Folderite` if you want your settings gone as well. See [Install and uninstall](install.md).
