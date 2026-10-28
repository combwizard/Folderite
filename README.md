# Folderite

Middle-click in a file manager or Open/Save dialog to pop up a folder menu: favorites, folders open in other windows, recent folders (frecency-ranked), and the folder on the clipboard. Picking one sends *that* window there.

Supported: Windows Explorer (incl. Win11 tabs), common file dialogs, Directory Opus, Total Commander, XYplorer, and the desktop (opens a new Explorer window).

## Build & run

```bat
dotnet build
src\Folderite\bin\Debug\net10.0-windows\win-x64\Folderite.exe
```

Runs in the tray. Right-click the tray icon for Pause, Start with Windows, Edit config, and Exit. Left-click opens the folder menu; picking a folder there opens a new Explorer window.

## Config

`%APPDATA%\Folderite\config.json`. If `config.json` sits next to the exe, the app runs in portable mode and uses that one instead. The file reloads automatically when saved. `//` comments and trailing commas are allowed.

```jsonc
{
  "favorites": [
    { "name": "&Projects", "path": "D:\\Projects" },
    { "name": "-" },                                   // separator
    { "name": "Work", "items": [                       // submenu
      { "name": "Specs", "path": "%USERPROFILE%\\Work\\Specs" }
    ]}
  ],
  "recentCount": 15,
  "historyExclude": ["C:\\Windows\\*", "*\\node_modules\\*"],
  "actions": [
    { "name": "Open &terminal here", "command": "wt.exe", "args": "-d \"{path}\"" },
    { "name": "Open in &VS Code",    "command": "code",   "args": "\"{path}\"" }
  ],
  "disabledProcesses": ["someapp"],   // exe names without .exe
  "showOpenWindows": true,
  "showClipboard": true,
  "enableOnDesktop": true
}
```

`&` marks the accelerator key in names. History lives in `history.json`, and errors are logged to `folderite.log`, both in the same folder.

## How it works

| Piece | File |
| --- | --- |
| Global middle-click hook (own thread, swallows down+up) | `Hooks/MouseHook.cs` |
| Window → adapter resolution | `Targets/TargetRegistry.cs` |
| Explorer: Shell.Application COM, active Win11 tab via `ShellTabWindowClass` | `Targets/ExplorerAdapter.cs` |
| File dialogs: breadcrumb caption to read; filename box + OK to navigate (address bar for folder pickers) | `Targets/FileDialogAdapter.cs` |
| Opus (`dopusrt`), TC (`WM_COPYDATA "CD"`), XYplorer (`WM_COPYDATA ::goto`) | `Targets/ThirdParty/` |
| History from WinEvent name/foreground changes, zoxide-style frecency | `History/` |
| Menu model → native popup with shell icons | `Menu/` |

## Manual test checklist

- [ ] Explorer: menu shows; favorite navigates; with several tabs, the *active* tab navigates
- [ ] Explorer (Win11): middle-click on a tab still closes it (tab row is exempt)
- [ ] Open/Save dialog (Notepad, Office, browser): navigates; typed filename is kept
- [ ] Folder picker dialog: navigates instead of accepting the folder
- [ ] Directory Opus dual pane: clicked side navigates
- [ ] Total Commander: left/right panel, non-ASCII path *(untested so far)*
- [ ] XYplorer: `::goto` with spaces *(untested so far)*
- [ ] Middle-click elsewhere (browser tab, editor) still works normally
- [ ] Elevated Explorer/dialog: fails gracefully unless Folderite runs elevated
