# Build from source

This page is for building Folderite from a checkout. You need Windows and the .NET 10 SDK. The projects target `net10.0-windows`.

## Requirements

- Windows.
- The [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). CI uses `10.0.x`.
- The [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0) to run the build. The app is not self-contained.
- For the screenshots: PowerShell 7 (`pwsh`) and `ffmpeg` on the PATH.
- For the docs: Python with `pip`.

## Build, test and run

Run these from the root of the checkout. `Folderite.slnx` holds both projects.

```bat
dotnet build
dotnet test
src\Folderite\bin\Debug\net10.0-windows\win-x64\Folderite.exe
```

For a Release build, add `-c Release`. The output then goes to `src\Folderite\bin\Release\net10.0-windows\win-x64\`. CI (`.github/workflows/ci.yml`) runs `dotnet build -c Release` and `dotnet test -c Release --no-build` on `windows-latest`.

Quit a running Folderite from the tray before you test a build.

## Tests

`dotnet test` runs the xUnit project in `tests\Folderite.Tests`. It covers the logic that doesn't need a desktop:

- config storage, config location and moving the config
- favorites and the Settings view model
- frecency ranking of recent folders
- menu composition
- hotkeys and accelerators (`&` keys)
- IPC between the tray app and Settings
- dialog start folders

There are no tests for Explorer, the file dialogs or the third-party apps. Those are checked by hand with the checklist below.

### Manual test checklist

- Explorer: menu shows; favorite navigates; with several tabs, the *active* tab navigates
- Explorer (Win11): middle-click on a tab still closes it (tab row is exempt)
- Open/Save dialog (Notepad, Office, browser): navigates; typed filename is kept; pressing Enter afterward saves or opens
- Folder picker dialog: navigates instead of accepting the folder
- All three dialogs, starting in Documents or Downloads as well as in a plain folder
- Directory Opus dual pane: clicked side navigates
- Total Commander: left/right panel, non-ASCII path *(untested so far)*
- XYplorer: `::goto` with spaces *(untested so far)*
- Blender 5.x File View (Open, Import with options panel, folder picker): navigates from a click on the bookmarks and on the file list; clipboard (text and image) comes back; typed filename is kept
- Middle-click elsewhere (browser tab, editor) still works normally
- Elevated Explorer/dialog: fails gracefully unless Folderite runs elevated
- Keyboard shortcut: opens the menu for Explorer (active tab), a file dialog, Directory Opus (focused side) and the desktop; elsewhere picking a folder opens a new Explorer window; does nothing while paused; a shortcut that's taken shows a warning in Settings
- Favorite with subfolders: submenu lists subfolders (by name / newest first); the folder itself is the bold top item; a disconnected network folder doesn't freeze the menu
- Recent folders: folders used in the current app rank higher there
- Dialog start folder: off by default; when on, a new Open/Save dialog moves to the app's last folder and keeps the typed file name; a per-app folder wins; folder pickers work
- Drives submenu (when enabled): lists drives with free space; a disconnected network drive doesn't hang the menu

## Screenshots

The pictures in `docs/images/` come from `tools\screenshots\capture.ps1`. Each one exists in a `-light` and a `-dark` version, and the README and the website show the one that matches the viewer's theme.

The script captures in whatever theme Windows is using, so run it twice: once in light mode and once in dark (**Settings > Personalization > Colors**).

```bat
pwsh tools\screenshots\capture.ps1
```

You need the .NET 10 SDK, `ffmpeg` on the PATH, a free `X:` drive letter and a primary monitor of at least 1600x1080. The script takes over the mouse and keyboard for about a minute, so leave them alone until it prints `done`. Before it starts, it quits a running Folderite, and it restarts that copy afterwards. Directory Opus should not be replacing Explorer during the run.

The script hides the navigation pane in Explorer and file dialogs while it runs and restores the previous setting after. It also saves and puts back your clipboard.

It writes `menu` and `settings` as PNG, `explorer` and `save-dialog` as GIF, and the `explorer-still` and `save-dialog-still` PNG frames.

## Docs site

The docs use MkDocs Material. The config is `mkdocs.yml`, the theme overrides are in `docs-theme/`, and the styles are in `docs/stylesheets/extra.css`.

```bat
pip install -r requirements-docs.txt
mkdocs serve
mkdocs build --strict
```

`mkdocs serve` previews the docs with live reload. `mkdocs build --strict` fails on a broken link, so run it before you commit docs changes.

`mkdocs build` writes to `site/.preview/docs`, which `mkdocs.yml` sets on purpose. MkDocs' default output folder is `site/`, which it empties first, and `site/` is the landing page's source here. Keep that `site_dir` line.

The landing page source is in `site/`. To preview it together with the docs, run:

```bat
pwsh site/preview.ps1
```

It serves the landing page on http://127.0.0.1:8000/ and the docs under `/docs/`. Pass `-Port` to use another port.

`.github/workflows/pages.yml` publishes both on a push to `main` that touches `site/`, the docs, the docs theme, `mkdocs.yml` or `requirements-docs.txt` (or when you run it by hand). A pull request that touches the docs only runs the strict build.

## Releasing

Push a version tag, and the [Release workflow](https://github.com/combwizard/Folderite/blob/main/.github/workflows/release.yml) does the rest:

```bat
git tag v0.3.0
git push origin v0.3.0
```

The workflow runs the tests, publishes the app with the version from the tag, zips it as `Folderite-<version>-win-x64.zip` and attaches the zip to a draft release for that tag. Edit the notes, then publish the release.

The version comes from the tag, not from `Folderite.csproj`. A tag with a suffix, such as `v0.3.0-beta.1`, makes a pre-release. If a release for the tag already exists, the zip is added to it.
