# Folderite

[![CI](https://github.com/combwizard/folderite/actions/workflows/ci.yml/badge.svg)](https://github.com/combwizard/folderite/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/combwizard/folderite)](https://github.com/combwizard/folderite/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Middle-click in a file manager or Open/Save dialog to pop up a folder menu: favorites, folders open in other windows, recent folders (frecency-ranked), and the folder on the clipboard. Picking one sends *that* window there.

Supported: Windows Explorer (incl. Win11 tabs), common file dialogs, Directory Opus, Total Commander, XYplorer, Blender's file browser window, and the desktop (opens a new Explorer window).

## Install

1. Install the [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) if you don't have it.
2. Download `Folderite-<version>-win-x64.zip` from the [latest release](https://github.com/combwizard/folderite/releases/latest), unzip it to a folder of your choice, and run `Folderite.exe`.

To upgrade, quit Folderite from the tray and unzip the new release over the old folder. Your settings are kept.

Requires Windows 10 1903 or later; Windows 11 recommended.

## Build from source

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bat
dotnet build
dotnet test
src\Folderite\bin\Debug\net10.0-windows\win-x64\Folderite.exe
```

## Usage

Runs in the tray. Right-click the tray icon for Settings, Pause, Start with Windows, Edit config, and Exit. Left-click opens the folder menu; picking a folder there opens a new Explorer window.

## Settings

**Settings…** (tray menu, or the bottom of the folder menu) opens an editor for everything below: favorites (drag to reorder or nest, drop folders from Explorer), actions, menu sections, exclusions, your folder history, and Start with Windows / Pause. Changes save as you make them and the running app picks them up immediately. **Preview menu** shows the real menu with the current settings.

Settings rewrites `config.json` when it saves, so `//` comments in it are not kept. If the file has an error, Settings stops saving until it is fixed.

## Config

`%APPDATA%\Folderite\config.json` by default. **Settings → General → Change location…** moves it, with `history.json`, to another folder (remembered in `HKCU\Software\Folderite\ConfigDirectory`); **Use default** moves it back. Choosing Folderite's own folder makes it **portable**: a `config.json` next to the exe always wins, so the settings travel with the folder. The file reloads automatically when saved. `//` comments and trailing commas are allowed.

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
| Blender "File View" window: synthesized Ctrl+F, Shift+Tab, Ctrl+A, Ctrl+V, Enter into the path field (clipboard and cursor restored); current folder can't be read | `Targets/ThirdParty/BlenderAdapter.cs` |
| History from WinEvent name/foreground changes, zoxide-style frecency | `History/` |
| Menu model → owner-drawn native popup (dark mode, shell icons) | `Menu/` |
| Settings window (`Folderite.exe --settings`, WPF + Fluent), separate process | `Settings/` |
| Settings ↔ tray app: pause and history over `WM_COPYDATA` | `Interop/Ipc.cs` |

## Manual test checklist

- [ ] Explorer: menu shows; favorite navigates; with several tabs, the *active* tab navigates
- [ ] Explorer (Win11): middle-click on a tab still closes it (tab row is exempt)
- [ ] Open/Save dialog (Notepad, Office, browser): navigates; typed filename is kept
- [ ] Folder picker dialog: navigates instead of accepting the folder
- [ ] Directory Opus dual pane: clicked side navigates
- [ ] Total Commander: left/right panel, non-ASCII path *(untested so far)*
- [ ] XYplorer: `::goto` with spaces *(untested so far)*
- [ ] Blender 5.x File View (Open, Import with options panel, folder picker): navigates from a click on the bookmarks and on the file list; clipboard (text and image) comes back; typed filename is kept
- [ ] Middle-click elsewhere (browser tab, editor) still works normally
- [ ] Elevated Explorer/dialog: fails gracefully unless Folderite runs elevated

## Releasing

Push a version tag; the [Release workflow](.github/workflows/release.yml) tests, publishes, and attaches `Folderite-<version>-win-x64.zip` to a draft release with that tag. Edit the notes and publish it.

```bat
git tag v0.3.0
git push origin v0.3.0
```

The version comes from the tag, not `Folderite.csproj`. A tag with a suffix (`v0.3.0-beta.1`) makes a pre-release. If a release for the tag already exists, the zip is added to it instead.

## License

[MIT](LICENSE)
