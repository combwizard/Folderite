# The config file

Everything you set in [Settings](settings.md) is stored in one file, `config.json`. You can edit it by hand too. This page lists every key, the shape of the nested items, and what happens when something in the file is wrong. To find the file, see [Files and logs](files.md#where-the-files-are).

## Format

`config.json` is JSON with a few allowances:

- `//` and `/* */` comments are allowed.
- Trailing commas are allowed.
- Key names are not case sensitive (`recentCount` and `RecentCount` are the same key). Settings writes them in camelCase, as shown on this page.
- Keys Folderite doesn't know are ignored.
- A key you leave out gets its default, listed below.
- Backslashes in paths must be doubled: `"C:\\Projects"`.

Folderite reloads the file by itself. About 300 ms after a change on disk it reads the file again, and the new values apply to the next menu you open. The keyboard shortcut, the list of disabled apps and the history exclusions are applied at the same moment. You don't have to restart. The tray menu has **Reload** (and the folder menu has **Reload** at the bottom) to read the file again on demand.

**Anything that saves the file rewrites it from scratch.** That happens when you change something in Settings, when you pick **Add to favorites** in the folder menu, and when Folderite creates the file on first run. The rewrite:

- drops your `//` and `/* */` comments,
- drops keys Folderite doesn't know,
- writes the keys in a fixed order with two-space indentation,
- leaves out `items`, `showSubfolders` and `sortSubfolders` where they have no effect.

If you keep notes in comments, keep them in a copy of the file, or edit by hand and don't touch Settings. Settings only saves when you change something, so opening it does not rewrite the file.

## Keys

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `favorites` | list of [favorite entries](#favorites) | `[]` (see below) | The favorites section of the menu. |
| `recentCount` | number | `15` | How many folders the **Recent** submenu lists. `0` hides the submenu. A negative number counts as `0`. Settings limits it to 0 to 50 (**Menu > Recent folders**). |
| `showAppRecent` | true or false | `false` | Add a **Recent in** submenu with the folders used most in the app the menu is opened over. It lists up to `recentCount` folders. |
| `historyExclude` | list of text | `[]` | [Wildcard patterns](#history-exclusions) for folders that are never remembered. |
| `actions` | list of [actions](#actions) | one action, **Open &terminal here** | Commands in the current folder's submenu. |
| `disabledProcesses` | list of text | `[]` | Program names, without `.exe`, where Folderite does nothing. |
| `showOpenWindows` | true or false | `true` | Show the **Open windows** section. |
| `showClipboard` | true or false | `true` | Show the **Clipboard** item when the clipboard holds a folder path. |
| `enableOnDesktop` | true or false | `true` | Also open the menu on the desktop. Picking a folder there opens a new Explorer window. |
| `hotkey` | text | `""` | The [keyboard shortcut](#keyboard-shortcut) that opens the menu. Empty means none. |
| `showDrives` | true or false | `false` | Add a **Drives** submenu with the free space of each drive. |
| `dialogLastFolder` | true or false | `false` | Open new Open and Save dialogs in the folder last used in the same app. |
| `dialogFolders` | list of [dialog folders](#dialog-folders) | `[]` | A fixed start folder for the dialogs of one app. Wins over `dialogLastFolder`. |

Some defaults need a note:

- **`favorites`.** A key that is missing means no favorites. On first run, Folderite creates the file with four: `&Home` (your user folder), `&Desktop`, `Do&wnloads` and `D&ocuments`, each written as a full path.
- **`actions`.** A key that is missing gives the one default action, `Open &terminal here`, which runs `wt.exe` with `-d "{path}"`. To have no actions at all, write `"actions": []`.

Where each key appears in Settings: `favorites` on **Favorites**, `actions` on **Actions**, `recentCount`, `showAppRecent`, `showOpenWindows`, `showClipboard`, `showDrives` and `enableOnDesktop` on **Menu**, `dialogLastFolder` and `dialogFolders` on **Dialogs**, `historyExclude` and `disabledProcesses` on **Exclusions**, and `hotkey` on **General**. See [Settings](settings.md).

## Favorites

Each entry in `favorites` is one of three things.

**A folder** has a `path`:

```json
{ "name": "&Projects", "path": "D:\\Projects" }
```

**A separator** is an entry whose name is a single hyphen. It draws a line in the menu:

```json
{ "name": "-" }
```

**A group** has `items`, a list of more entries of any kind. It becomes a submenu, and groups can hold groups:

```json
{ "name": "Work", "items": [
  { "name": "Specs", "path": "%USERPROFILE%\\Work\\Specs" }
]}
```

| Key | Type | Meaning |
| --- | --- | --- |
| `name` | text | The text in the menu. Put `&` before a letter to make it the [accelerator key](#accelerators). A folder with an empty name shows the last part of its path. |
| `path` | text | The folder. [Environment variables](#environment-variables) are expanded. Folder entries only. |
| `items` | list of entries | Makes the entry a group. An entry with `items` is a group even when the list is empty, and its `path` is ignored. |
| `showSubfolders` | true or false | Folder entries only. Shows the folder as a submenu of its subfolders. Default `false`. |
| `sortSubfolders` | text | With `showSubfolders`: `"newest"` lists the most recently modified folders first. Anything else, or leaving it out, sorts by name. |

How each kind is treated:

- A folder entry with no `path`, or a blank one, is skipped. It does not show up in the menu.
- The `-` separator works anywhere in the tree, including inside groups.
- Folderite does not check that a folder exists when it builds the menu. Picking one that is gone fails (see [Troubleshooting](troubleshooting.md#a-folder-or-drive-is-not-available)). The Favorites page in Settings marks a missing local folder with "This folder doesn't exist."
- A group's `name` is shown as written, so a plain `&` in it becomes an accelerator. A name taken from a folder's path (when `name` is empty) has no accelerator, and any `&` in the folder name is shown as it is.

### Subfolder submenus

With `showSubfolders` on, the favorite is a submenu instead of an item. The first item in it is the folder itself, in bold, so you can still open it. Below a separator come its subfolders:

- Hidden and system folders are left out.
- By name uses Explorer's order (`file2` comes before `file10`). `"sortSubfolders": "newest"` orders by last modified time, newest first.
- At most 100 subfolders are listed. If there are more, the last item is a grayed-out "Showing the first 100 folders".
- The folders are read when you open the submenu, not before. If the folder doesn't answer within 1.5 seconds (a sleeping network share, for example), the submenu shows the folder itself and no subfolders, and the menu does not freeze.

`showSubfolders` and `sortSubfolders` on a group or a separator do nothing.

## Actions

Each action adds an item to the submenu that opens from the top item of the menu (the current folder). Actions are not shown when the menu has no current folder, for example when you open it from the tray icon.

```json
{ "name": "Open in &VS Code", "command": "code", "args": "\"{path}\"" }
```

| Key | Type | Meaning |
| --- | --- | --- |
| `name` | text | The menu text. `&` marks the accelerator key. |
| `command` | text | The program or file to run. [Environment variables](#environment-variables) are expanded. |
| `args` | text | The arguments. Every `{path}` is replaced with the current folder's path, without quotes added, so write `\"{path}\"` in JSON when the path may contain spaces. |

Folderite starts the command with the Windows shell, with the current folder as the working directory. That means a program name that is on your `PATH`, a full path, or a document all work. If it can't start, a balloon from the tray icon says "*name* failed:" followed by Windows' reason, with the `&` removed from the name.

The submenu also holds two items of its own: **Copy path**, which is always there, and **Add to favorites**, which is hidden when the folder is already a favorite. They come from Folderite, not from `actions`.

## Dialog folders

`dialogFolders` sets the folder a new Open or Save dialog starts in, per app. See [Open and Save dialogs](dialogs.md).

```json
"dialogFolders": [
  { "app": "notepad", "path": "D:\\Notes" }
]
```

| Key | Type | Meaning |
| --- | --- | --- |
| `app` | text | The program name of the app that opens the dialog, without `.exe`. A trailing `.exe` is tolerated here. Not case sensitive. |
| `path` | text | The start folder. [Environment variables](#environment-variables) are expanded. |

An entry with an empty `app` or `path` is ignored (Settings drops it when it saves). The first entry whose `app` matches is used. If the folder doesn't exist, the dialog is left alone.

## History exclusions

Each pattern in `historyExclude` is matched against the whole folder path, with these rules:

| Character | Matches |
| --- | --- |
| `*` | Any run of characters, including none and including `\`. |
| `?` | Exactly one character. |
| anything else | Itself. Case doesn't matter. |

```json
"historyExclude": ["C:\\Windows\\*", "*\\node_modules\\*", "X:\\*"]
```

- The pattern must match the whole path. `C:\Windows\*` matches everything below `C:\Windows`, but not `C:\Windows` itself. Add a second pattern for that.
- Paths are compared without a trailing `\`, except for a drive root (`X:\`).
- Environment variables are not expanded in patterns.
- A blank pattern is ignored.
- A folder that matches is not recorded, and it is hidden from **Recent**. Folders recorded before you added the pattern stay in `history.json`, and show in **Recent** again if you remove the pattern.

## Keyboard shortcut

`hotkey` is text such as `"Win+Shift+F"` or `"Ctrl+Alt+F5"`. In Settings you record it by pressing the keys, so you rarely write it by hand.

- Modifiers: `Ctrl` (or `Control`), `Alt`, `Shift`, `Win` (or `Windows`), joined with `+`, in any order.
- Keys: a letter, a digit, `F1` to `F24`, `Space`, `Insert`, `Delete` (or `Del`), `Home`, `End`, `PageUp`, `PageDown`, `Up`, `Down`, `Left`, `Right`, a punctuation key that has its own key on your keyboard layout, or a hex key code such as `0xC0`.
- Case doesn't matter, and spaces around `+` are fine.
- Exactly one key, and at least one modifier. Function keys are the only exception and can stand alone.

If the text can't be read, or another program already owns the shortcut, Folderite shows a balloon when it starts or when the value changes, and Settings shows a warning. The exact messages are in [Troubleshooting](troubleshooting.md#the-keyboard-shortcut-does-nothing).

## Environment variables

Windows environment variables written as `%NAME%` are expanded in:

- the `path` of a favorite,
- the `command` of an action (but not its `args`),
- the `path` of a dialog folder,
- the registry value that moves the config folder (see [Files and logs](files.md#registry-values)).

A variable that doesn't exist is left as written, so the path won't resolve. Environment variables are not expanded in `historyExclude`.

## Accelerators

An `&` in a name marks the letter after it as the accelerator. With the menu open, press that letter to pick the item. Write `&&` for a literal `&`.

- `"&Projects"` shows as Projects with the P underlined. Press **P**.
- `"Pro&jects && Notes"` shows as Projects & Notes, with the J as the accelerator.
- If only one item in a menu has that letter, pressing it picks the item. If several share it, each press moves to the next one, and **Enter** picks the highlighted one.
- An item with no accelerator can still be reached with the first letter of its text, when no item claims that letter.

The folder menu's own items use accelerators too (**Copy path**, **Add to favorites**, **Recent**, **Drives**, **Clipboard**, **Settings…**, **Reload**), so pick your letters with those in mind where they share a menu.

## Disabled apps

`disabledProcesses` takes the program's file name without `.exe`, for example `"notepad"`. Folderite compares it with the name of the program that owns the window, ignoring case. A name written with `.exe` in the file does not match, so leave it off. (Settings removes the `.exe` when you add a name there.)

An app on this list gets no menu from the mouse or the keyboard shortcut, and its folders are not recorded in your history. For Explorer windows and the desktop the name is `explorer`. See [Supported apps](apps.md#turn-folderite-off-for-one-app).

## A full example

Every key appears once.

```jsonc
{
  "favorites": [
    // Its subfolders in a submenu, newest first.
    { "name": "&Projects", "path": "D:\\Projects", "showSubfolders": true, "sortSubfolders": "newest" },
    { "name": "-" },                                   // separator
    { "name": "Work", "items": [                       // group: a submenu
      { "name": "Specs", "path": "%USERPROFILE%\\Work\\Specs" }
    ]}
  ],
  "recentCount": 15,
  "showAppRecent": false,             // a second submenu: the folders used most in this app
  "historyExclude": ["C:\\Windows\\*", "*\\node_modules\\*"],
  "actions": [
    { "name": "Open &terminal here", "command": "wt.exe", "args": "-d \"{path}\"" },
    { "name": "Open in &VS Code",    "command": "code",   "args": "\"{path}\"" }
  ],
  "disabledProcesses": ["someapp"],   // program names without .exe
  "showOpenWindows": true,
  "showClipboard": true,
  "enableOnDesktop": true,
  "showDrives": false,                // a Drives submenu with free space
  "hotkey": "Win+Shift+F",            // keyboard shortcut for the menu; "" = none
  "dialogLastFolder": false,          // open file dialogs in the folder last used in that app
  "dialogFolders": [                  // fixed start folders for one app's file dialogs
    { "app": "notepad", "path": "D:\\Notes" }
  ]
}
```

## When the file has a mistake

How Folderite treats a bad file depends on what is wrong.

| What is wrong | What happens |
| --- | --- |
| Not valid JSON: a missing comma or brace, an unquoted key, single quotes, a single backslash in a path | The whole file is rejected. Nothing from it is used. |
| A value of the wrong type, such as `"recentCount": "ten"` or `"showDrives": "yes"` | The same: the whole file is rejected. |
| An empty file | The same. |
| A key Folderite doesn't know, or a different capitalization of a known key | The unknown key is ignored. A known key in another capitalization is read normally. |
| A number out of range, such as a negative `recentCount` | The value is accepted and treated as the nearest sensible one (`0`). |
| A favorite with no `path` and no `items`, or a blank `path` | That entry is skipped. The rest of the file works. |
| A path that doesn't exist | Accepted. The menu shows the item, and picking it fails when the window can't go there. |
| An unreadable `hotkey` | The rest of the file works. Only the shortcut is not set, and a balloon says so. |

When the file is rejected:

- **Folderite keeps using the last good settings.** If this happens at startup, there are no earlier settings, so Folderite runs with built-in defaults: no favorites, and the one default action.
- **The error message comes from the JSON reader.** It names the position, for example the line number and the path of the key. A balloon with the title "Folderite: config error" shows it at startup and when you pick **Reload**. A change you save from your editor while Folderite runs is checked silently, and the balloon is not shown. The message is not written to `folderite.log`.
- **Settings stops saving.** The Settings window shows "config.json has an error, so changes here are not saved." with the message under it and the buttons **Open file** and **Reload**. This protects the file you are in the middle of fixing, because Settings would otherwise overwrite it with its own copy. Fix the file and save it, and Settings picks the change up on its own, or press **Reload**.

See [Troubleshooting](troubleshooting.md#settings-says-configjson-has-an-error) for the steps.

If you delete `config.json` while Folderite runs, it keeps working with what it has in memory. The next time it reads the file (**Reload** from the tray menu, or a restart), it creates the file again with the four default favorites.
