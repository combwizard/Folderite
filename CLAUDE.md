# Folderite

A middle-click folder menu for Windows. [README.md](README.md) says what it does, and [docs/building.md](docs/building.md)
how to build and test it.

## Keep the docs in step with the code

The user docs are the Markdown pages in `docs/`. Every push to `main` that touches them publishes them to
<https://combwizard.github.io/Folderite/docs/>, so they describe `main`, not the last release.

- **A change a user can see or do updates its docs page in the same commit.** That covers settings, menu items, config
  keys, defaults, supported apps, file names and locations, log lines, messages, requirements and install steps. A new
  feature is documented where a user would look for it; a removed one is taken out of every page that mentions it.
- **Check `README.md` and the landing page (`site/index.html`)** when the change touches install, requirements, first
  steps or the feature list.
- **Pictures:** `tools\screenshots\capture.ps1` makes everything in `docs/images/`, in a light and a dark version. When a
  change alters something in a picture, retake it in both Windows themes and look it over before committing. Pages
  show both versions, one after the other, with `#only-light` and `#only-dark` on the file names.
- **Build the docs** after editing them: `mkdocs build --strict` (`pip install -r requirements-docs.txt` first). It
  fails on a broken link or anchor. Link between pages with relative `.md` links so they work on GitHub too.
- **Keep `site_dir` in `mkdocs.yml`.** MkDocs' default output folder is `site/`, which it empties first, and `site/` is
  the landing page's source here. Don't pass `-d site` either.

Where each part is documented:

| Code | Page in `docs/` |
|---|---|
| `Menu/` (sections, order, accelerators, subfolders, drives) | `menu.md`, `config.md` |
| `Tray/` (the tray icon and its menu, Start with Windows) | `menu.md`, `install.md` |
| `Hooks/` (middle-click, keyboard shortcut) | `menu.md`, `troubleshooting.md`, `how-it-works.md` |
| `History/` (recent folders, frecency) | `menu.md`, `how-it-works.md` |
| `Settings/` (the Settings window) | `settings.md` |
| `Dialogs/`, `Targets/FileDialogAdapter.cs` (Open and Save dialogs, start folders) | `dialogs.md`, `settings.md` |
| `Targets/` (supported apps) | `apps.md`, `how-it-works.md` |
| `Config/AppConfig.cs` (config keys and defaults) | `config.md`, `settings.md` |
| `Config/ConfigLocation.cs`, `ConfigRelocator.cs`, `Settings/ConfigMove.cs`, `Log.cs` (where files live) | `files.md` |
| `Interop/` (Settings and tray app talking) | `how-it-works.md` |
| A new way for something to fail, or a new log line worth reading | `troubleshooting.md` |
| `tests/`, `tools/`, `.github/workflows/`, `Folderite.csproj` | `building.md`, and `install.md` for requirements |

Write like the existing pages: plain, short, concrete sentences, American spelling (favorites, color), UI labels and
keys in **bold**, no em dashes, no marketing words.

The docs theme is in `docs-theme/` and `docs/stylesheets/extra.css`. It copies the landing page's look and tokens from
`site/styles.css`, so a change to the landing page's look may need the same change there. Pages follow the system's
light or dark mode, with no switch, like the landing page.
