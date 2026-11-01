# Security policy

## Supported versions

Only the [latest release](https://github.com/combwizard/folderite/releases/latest) gets security fixes. If you're on an older version, upgrade first and check whether the problem is still there.

## Reporting a vulnerability

Please don't open a public issue for security problems. Report them privately through GitHub instead:

1. Go to the [Security tab](https://github.com/combwizard/folderite/security) of this repository.
2. Click **Report a vulnerability**.

Include the Folderite version, your Windows version, the steps to reproduce, and what an attacker could do with it.

You should get a reply within a week. Folderite is maintained by one person in their spare time, so a fix may take longer. Once it's out, the advisory is published and you're credited unless you'd rather not be.

## Scope

Folderite runs as your user, installs a global mouse hook, reads other windows' folders, sends keystrokes and clipboard text to some apps (Blender), and accepts messages from the Settings process over `WM_COPYDATA`. Problems worth reporting include:

- Another process or user getting Folderite to run commands, navigate somewhere, or read data it shouldn't
- Folderite exposing your folder history or clipboard contents to other users or processes
- A crafted folder name, path, or window title that makes Folderite crash, hang the menu, or run code
- Folderite leaving the clipboard or another app's input in a changed state that exposes data

Not in scope:

- **Actions** in `config.json` run whatever command you put there. That's the feature. Someone who can already write to your config folder or your `HKCU` registry can run code as you anyway.
- Folderite can't navigate elevated windows unless it runs elevated itself. That's Windows' integrity levels working as intended.
- Bugs in .NET, Windows, or the file managers Folderite talks to. Report those upstream.
