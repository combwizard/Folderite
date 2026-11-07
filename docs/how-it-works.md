# How it works

This page follows one middle-click from the mouse to the folder change, then covers the parts around it: history, the menu, the Settings process and config reload. Paths such as `src/Folderite/App.cs` are relative to the repository root.

## The pieces

| Piece | File | What it does |
| --- | --- | --- |
| Mouse hook | `src/Folderite/Hooks/MouseHook.cs` | Global middle-click hook on its own thread. Swallows the click and its release when the window is supported. |
| Window to adapter | `src/Folderite/Targets/TargetRegistry.cs` | Turns the window under the cursor into an adapter, or nothing. Applies the disabled-apps list. |
| Explorer | `src/Folderite/Targets/ExplorerAdapter.cs` | `Shell.Application` COM. The active Windows 11 tab is found through `ShellTabWindowClass`. |
| File dialogs | `src/Folderite/Targets/FileDialogAdapter.cs` | Reads the breadcrumb caption. Navigates through the file name box and OK in Open dialogs, or through the address bar in Save dialogs and folder pickers. |
| Directory Opus | `src/Folderite/Targets/ThirdParty/DopusAdapter.cs` | Runs `dopusrt.exe`. |
| Total Commander | `src/Folderite/Targets/ThirdParty/TotalCommanderAdapter.cs` | `WM_USER+50` queries to read, `WM_COPYDATA` with a `CD` command to navigate. |
| XYplorer | `src/Folderite/Targets/ThirdParty/XYplorerAdapter.cs` | Reads the title bar, navigates with a `::goto` script sent by `WM_COPYDATA`. |
| Blender | `src/Folderite/Targets/ThirdParty/BlenderAdapter.cs` | Synthesized keys into the File View window, with the clipboard borrowed. Can't read the folder. |
| Desktop | `src/Folderite/Targets/DesktopAdapter.cs` | Recognizes the desktop's icon area. "Navigating" opens a new Explorer window. |
| History | `src/Folderite/History/HistoryTracker.cs`, `FrecencyStore.cs` | WinEvent hooks for name and foreground changes, zoxide-style frecency. |
| Menu | `src/Folderite/Menu/` | A menu tree built from config and context, drawn as an owner-drawn native popup. |
| Settings | `src/Folderite/Settings/` | `Folderite.exe --settings`: a WPF window in a separate process. |
| IPC | `src/Folderite/Interop/Ipc.cs` | Settings to tray commands over `WM_COPYDATA`. |
| Wiring | `src/Folderite/App.cs` | The hidden message window that connects everything. |

## The tray app

`src/Folderite/Program.cs` takes a mutex (`Local\Folderite.SingleInstance`) so only one copy runs, then starts `App` (`src/Folderite/App.cs`). `App` creates a hidden top-level window, loads config and history, builds the adapter list, installs the hooks and runs a normal message loop.

The window is a real hidden window, not a message-only one, because `TrackPopupMenu` needs an owner that can take the foreground. Everything that has to happen on the main thread arrives at this window as a message: the hook's trigger, the tray icon, timers, the keyboard shortcut, IPC and config reloads. Work from other threads goes onto a queue and wakes the window with a posted message.

The tray app never loads WPF. The Settings window lives in its own process (see [below](#the-settings-process)), and `Program.cs` keeps that code out of `Main` so the tray app doesn't even load its types.

## The mouse hook

`MouseHook` installs a `WH_MOUSE_LL` hook. A low-level hook runs inside the thread that installed it, and Windows gives it a short time to answer. If a callback is slow, input stops for the whole system, and Windows can remove the hook without telling you. So the hook has its own thread with nothing on it except a message loop, and its callback does very little.

On a middle-button press, the callback:

1. Finds the window under the cursor and its top-level window.
2. Asks `ShouldTrigger`, which is `_registry.Find(...)` from `App.Run`, whether that window belongs to an adapter.
3. If yes, remembers the exact window under the cursor, posts a `WM_APP+1` trigger message to the tray app's window with the top-level window and the packed screen point, and returns without passing the click on.

The slow work (COM calls, `dopusrt.exe`, building the menu) happens when the main thread handles that message.

The callback swallows the button press and, later, the matching release. If it ate only the press, the app under the cursor would see a release with no press, which some programs treat as a click or a drag end. When the press is swallowed, a flag is set, and the next middle-button release is swallowed too. Clicks on windows that don't belong to an adapter pass through untouched, as do all clicks while Folderite is paused.

## From window to adapter

`TargetRegistry.Describe` turns a window handle into a `TargetWindow`: the top-level window, the exact child under the cursor, the window class, the lower-case process name without `.exe`, and the click point. Process names are cached per process id, so the hook doesn't look up the process image on every click.

`TargetRegistry.Find` then does two things:

1. If the process name is in `disabledProcesses`, the answer is nothing.
2. Otherwise, it asks each adapter in order whether it `Matches` the window and returns the first that says yes.

The order is set in `App.Run`: Explorer, file dialogs, Directory Opus, Total Commander, XYplorer, Blender, desktop. `Matches` runs on the hook thread, so it has to be cheap: class names, process names, and at most a `FindWindowEx` or two. No COM and no process launches.

Every adapter implements `ITargetAdapter` (`src/Folderite/Targets/ITargetAdapter.cs`): `Matches`, `GetCurrentPath`, `Navigate` and, for the **Open windows** section, `EnumerateOpenPaths`.

## The adapters

### Explorer

`ExplorerAdapter` matches the classes `CabinetWClass` and `ExploreWClass`, except over the tab row. On Windows 11, the tabs share the title bar row with a window of class `TITLE_BAR_SCAFFOLDING_WINDOW_CLASS`. A click above the bottom edge of that window passes through, so a middle-click on a tab still closes it. A window without that child has no tabs, so there is no exemption. The keyboard shortcut has no click point, so it skips this check.

Folderite reads and drives Explorer through the `Shell.Application` COM object, late-bound, and its `Windows()` list. On Windows 11, every tab is its own entry in that list, and they all report the HWND of the frame window. The active tab is the entry whose own window matches the first `ShellTabWindowClass` child of the frame. Folderite gets each entry's tab window through `IServiceProvider` and `SID_STopLevelBrowser`. If the frame has no such child (an older Explorer), the first entry for that frame is used.

- **Read:** the entry's `Document.Folder.Self.Path`. A path that starts with `::` is a virtual folder and counts as no path.
- **Navigate:** `Navigate2(path)` on the entry.
- **Open windows:** the path of every entry.

### File dialogs

`FileDialogAdapter` matches a `#32770` dialog that has a `DUIViewWndClassName` or `SHELLDLL_DefView` child.

- **Read:** the dialog's address bar is a breadcrumb toolbar whose caption reads `Address: C:\...`, with the word in the user's language. Folderite finds the `ToolbarWindow32` whose parent is `Breadcrumb Parent`, cuts the text after the first `: `, and keeps it if it's a rooted path.
- **Navigate in an Open dialog:** put the path into the file name box and send OK, the same as typing a folder into the box and pressing Enter. The dialog treats a folder as "go there". The old text of the box is saved first. The dialog clears the box once it arrives, so Folderite waits (up to about a second) until the breadcrumb shows the new folder and then puts your text back, unless you've typed something else.
- **Navigate in any other dialog:** a dialog without a file-type box among its direct children takes the address bar. That covers folder pickers, where OK would accept the current folder, and Save dialogs, whose controls are nested with no ids. Text set in a Save dialog's nested file name box doesn't count, and OK then saves under the old name in the old folder. Folderite types the path into the address bar's edit control and sends Enter to that control directly. A posted Enter would go through the dialog's own key handling and turn into OK.
- **No edit control:** in a known folder such as Documents, the address bar has no edit control until real input asks for one. Messages don't create it. With the dialog in front, Folderite sends Ctrl+L, waits for the control, and navigates. The bar turns back into path buttons a moment later and takes the focus, so Folderite then moves the focus back to where it was.

`FileNameHasFolder` reports whether the file name box already holds a path, which means the app chose where to save. The dialog start folder feature uses it to leave such dialogs alone.

### Directory Opus

`DopusAdapter` matches the class `dopus.lister` and drives `dopusrt.exe`, found next to the running Opus or in the default install folder.

- **Read:** `dopusrt /info <temp file>,paths` writes an XML file with one `<path>` for each tab: the lister and tab window handles, the side, which tab is active, and whether a tab is the source or destination. Folderite picks the tab that contains the window under the cursor, else the source tab, else the active one.
- **Navigate:** `dopusrt /acmd Go "<path>"`. `Go` acts on the source tab of the most recently active lister. If the tab you clicked is the destination one, Folderite first runs `Set FOCUS=left` or `Set FOCUS=right` to make it the source.
- **Open windows:** the same query, de-duplicated. If no lister exists, `dopusrt.exe` isn't started.

Every call to `dopusrt.exe` is capped at two seconds. Arguments are passed as one raw string, because Opus parses its own quotes.

### Total Commander

`TotalCommanderAdapter` matches the class `TTOTAL_CMD`.

- **Read:** send `WM_USER+50` with `wParam` 9 or 10 to get the window handle of the left or right path label, then read its text. A trailing `*.*` or `>` is dropped, and the result has to be an existing folder. `wParam` 1000 asks which panel is active.
- **Which panel:** the panel whose path label overlaps the clicked control horizontally and sits closest above it. This handles side-by-side and stacked layouts. If two panels fit equally well, it counts as unknown.
- **Navigate:** a `WM_COPYDATA` message with the code `CD`, carrying the left path, a line break, and the right path in UTF-8 with a byte-order mark for each. If the clicked panel is the left one, only the left path is sent. For the right one, the left path is empty. If the panel is unknown, an `S` flag tells Total Commander that the path is for the source panel.

### XYplorer

`XYplorerAdapter` matches the class `ThunderRT6FormDC` in a process named `xyplorer`.

- **Read:** XYplorer's default title is `<path> - XYplorer <version>`. Folderite takes the longest prefix ending at a ` - ` that is an existing folder, so paths that contain ` - ` still work.
- **Navigate:** a `WM_COPYDATA` message with the id `0x00400001` and the UTF-16 script `::goto 'path';`. A single quote in the path is doubled.

### Blender

Blender draws its own interface and reads the keyboard through raw input, so there are no controls to talk to and posted key messages are ignored. `BlenderAdapter` matches a window of class `GHOST_WindowClass` in the process `blender` whose title is `Blender File View`. It can't read the folder, so `GetCurrentPath` is always empty.

To navigate, it needs real input:

1. Copy the path to the clipboard, marked as excluded from clipboard history, after taking a copy of everything that is on it.
2. Wait up to half a second for Blender to become the foreground window.
3. In one batch of `SendInput`, move the pointer to the middle of the window, press Ctrl+F, and move the pointer back. Blender sends keys to the area under the mouse, and Ctrl+F over a bookmark list opens that list's own filter. Ctrl+F starts editing the filter box in the path bar.
4. Press Shift+Tab to move to the directory field.
5. Press Ctrl+A, Ctrl+V and Enter to replace its text with the path.
6. After a short pause so Blender has read the clipboard, restore the old clipboard, unless the clipboard changed in the meantime.

After each batch, Folderite waits until Blender has handled it, then checks that Blender is still in front. If not, it stops. The keys go out only while Blender has the foreground, so they can't land in another app. The scan code is filled in as well as the virtual key, because Blender reads both.

### Desktop

`DesktopAdapter` matches the classes `Progman` and `WorkerW` when the window under the cursor is `SysListView32` or `SHELLDLL_DefView`, and `enableOnDesktop` is on. The current folder is the Desktop folder. "Navigating" starts `explorer.exe` with the path.

## The keyboard shortcut

The shortcut is an ordinary `RegisterHotKey` on the hidden window, with the no-repeat flag, parsed by `src/Folderite/Hooks/Hotkey.cs`. When it fires, `App.OnHotkey` takes the foreground window and its top-level window, and finds the control that has the keyboard focus (`GetGUIThreadInfo`). That control stands in for the window under the cursor that a click would give. Opus and Total Commander use that control to choose the pane or panel. The registry resolves it like any other window, with the same adapters and the same disabled-apps list. With no match, the tray menu opens instead: no current folder, and picking a folder opens a new Explorer window.

## The trigger, step by step

For a click on a supported window:

1. The hook thread sees the middle-button press, finds the adapter, swallows the click and posts a trigger message.
2. The main thread resolves the window again with the click point and gets the adapter. (`App.OnTrigger`.)
3. `App.ShowFolderMenu` asks the adapter for the current folder, and records it in history. It collects the open windows from every adapter, the top recent folders, and the folder on the clipboard.
4. `MenuComposer.Build` makes a menu tree from the config and this context.
5. `MenuBuilder.Show` shows it and waits for you to pick.
6. The chosen item's action runs after the menu is gone. For a folder, `App.Navigate` brings the target window to the front, calls the adapter's `Navigate`, and records the folder in history for that app. If `Navigate` fails, a tray balloon says so.

## History

`HistoryTracker` records folders without polling. It installs two out-of-context WinEvent hooks, skipping Folderite's own process:

- `EVENT_SYSTEM_FOREGROUND`: a window came to the front.
- `EVENT_OBJECT_NAMECHANGE`: a window's name changed. Explorer, Opus and XYplorer put the folder in the title, and Total Commander changes the text of its path label, so folder changes show up this way.

For each event on a top-level window, the tracker asks the registry whether an adapter handles the window. If so, it adds the window to a pending set and starts a 500 ms timer. When the timer fires, the tracker asks each pending window's adapter for its current folder and records it. This is a debounce: a burst of name changes while you click through folders produces one read.

Folderite also records the folder when the menu opens, and the folder you picked afterward. The foreground event is also passed on, immediately, to the dialog start folder code (`src/Folderite/Dialogs/DialogStarter.cs`), so it can move a new Open or Save dialog before history records the folder the dialog opened in.

### Frecency

`FrecencyStore` (`src/Folderite/History/FrecencyStore.cs`) keeps one entry per folder (case-insensitive), with a rank and the time of the last visit. Each visit adds 1 to the rank. It also keeps the same two numbers per app, keyed by the lower-case process name.

A folder's score is its rank times a factor that depends on how long ago you last used it:

| Last used | Factor |
| --- | --- |
| Less than 1 hour ago | 4 |
| Less than 1 day ago | 2 |
| Less than 7 days ago | 0.5 |
| Longer ago | 0.25 |

So that the **Recent** list can favor the app you're in, the sort key is:

```text
score(folder) + 3 * score(folder's use in this app)
```

where each `score` is rank times the factor for that last-used time. The factor `3` is `AppWeight`. A few uses in the current app outweigh a modest lead elsewhere, but a far more popular folder still wins. Ties go to the most recent visit. With no app (the tray icon), the second term is zero.

The rest of the rules:

- A repeat visit within 2 seconds is ignored. The folder and each app have their own 2-second check.
- When the sum of all ranks goes above 9000, every rank is multiplied by 0.9, and entries below 1 are dropped. The folder being recorded is never dropped.
- At most 1000 folders are kept. If there are more, the lowest scores go first.
- At most 10 apps are kept for each folder.
- Folders that match a pattern in `historyExclude` are neither recorded nor returned.
- The menu asks for `recentCount + 1` folders, since one may be the current folder, which is left out.

History is saved in `history.json`, written to a temporary file that then replaces the old one. The tray app saves it every 60 seconds if it changed, when Settings asks for it, and when it exits.

## The menu

The menu has three stages.

1. **Model.** `MenuComposer` (`src/Folderite/Menu/MenuComposer.cs`) turns config and context into a tree of items, submenus, headers and separators (`MenuModel.cs`). It touches neither Win32 nor the disk, which is why unit tests can cover it.
2. **Native popup.** `MenuBuilder` creates a Win32 popup menu from the tree and shows it with `TrackPopupMenuEx`, which returns the chosen command. The items are owner-drawn: the builder answers `WM_MEASUREITEM` and `WM_DRAWITEM` itself, and `MenuTheme` supplies colors, fonts and sizes for the DPI at the click point, in the light or dark app theme, with the accent color for headers. It uses Segoe Fluent Icons (or Segoe MDL2 Assets) for the submenu arrow. `DarkMode` makes the popup frame follow the theme with two undocumented `uxtheme` calls. `IconCache` supplies shell icons.
3. **Lazy submenus.** A favorite's subfolder list and the Drives list aren't read until you open them (`WM_INITMENUPOPUP`), so a network folder that's gone can't stall the menu when it opens.

Other details:

- The menu is modal. The action runs after it is destroyed.
- Because the hook swallowed the click, Windows won't let the hidden window take the foreground, and a popup whose owner isn't in front doesn't close when you click outside it. So `MenuBuilder` attaches to the foreground thread's input for that one call.
- Menu animation is turned off while the menu is open, because owner-drawn submenus glitch when they fade. Only the session is changed, nothing is written.
- Triggering again while a menu is open closes it.
- Accelerator keys (`&`) are handled through `WM_MENUCHAR`, because the items are owner-drawn.

## The Settings process

**Settings** starts `Folderite.exe --settings`, which runs a WPF app (`src/Folderite/Settings/SettingsApp.cs`) in its own process. It has its own mutex, `Local\Folderite.Settings`, and the tray app brings an existing Settings window forward by finding its title, `Folderite Settings`. A separate process keeps WPF's memory and startup cost out of the tray app, which runs all the time.

The two processes share two files and a small command channel:

- **`config.json`.** Settings has its own `ConfigStore`, edits the settings and writes the file. The tray app notices the change (see below).
- **`history.json`.** The tray app owns it while it runs. Settings asks the tray app to save (`flush-history`) before it reads the file, and to remove or clear entries (`remove-history`, `clear-history`). If the tray app isn't running, Settings edits the file itself.
- **Commands.** `src/Folderite/Interop/Ipc.cs` sends text commands to the tray app with `WM_COPYDATA`. The receiver is the hidden window, found by its class `FolderiteMessageWindow`.

The message carries a fixed marker (`FLDR`) in `dwData`, and UTF-16 text of the form `command` or `command\nargument`, up to 64 KB. The sender uses `SendMessageTimeout` with a one-second limit that gives up on a hung receiver. The receiver ignores messages without the marker. The commands:

| Command | Does |
| --- | --- |
| `flush-history` | Saves history if there are unsaved changes. |
| `remove-history` | Removes one folder from history. |
| `clear-history` | Clears history. |
| `get-paused`, `set-paused` | Reads or sets Pause. |
| `get-hotkey-status` | Tells whether the keyboard shortcut is registered. |
| `exit` | Quits the tray app. Used when you move the config folder. |

There is no `ChangeWindowMessageFilterEx` call, since both processes run at the same integrity level.

## Config reload

`ConfigStore` (`src/Folderite/Config/ConfigStore.cs`) loads `config.json` (comments and trailing commas allowed) and watches it with a `FileSystemWatcher`. Both the tray app and the Settings window use it. When the file changes:

1. A 300 ms timer restarts on each event, so an editor's several writes count once.
2. Changes within one second after the app's own save are ignored, so Settings doesn't reload what it wrote itself.
3. The file is read with sharing allowed. If another program has it locked, Folderite tries up to three times, 100 ms apart.
4. If it parses, the new config replaces the old one and the `Changed` event runs on the main thread. If it doesn't, the old config stays in use and the error is kept. The tray menu's **Reload** item reports it with a balloon.

`App.ApplyConfig` then updates the parts that are cached: the disabled-apps list, the history exclusions, the icon cache and the keyboard shortcut. Everything else, such as favorites, actions, recent count and the section switches, is read from the current config each time the menu opens, so it needs no work.

Saves write to a temporary file and move it over `config.json`, so a half-written file is never seen. If the keyboard shortcut text changed, it is unregistered and registered again, and a failure shows a balloon once.
