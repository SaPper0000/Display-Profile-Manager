# Tarkov Gamma Manager v1.4

Windows monitor gamma / display profile manager designed for use with Escape from Tarkov.

> **Important:** This project is a Windows utility for changing monitor/display settings. It does not intentionally modify Tarkov files or access Tarkov process memory.

## Features

- Per-monitor display profiles
- Automatic default profiles for detected monitors
- Game Auto profile assignment
- Global hotkeys
- Hotkey **Apply / Toggle** modes
- Profile backup and restore
- Monitor control can be disabled by default
- Korean and English UI variants
- Portable operation (no installer required)

## Hotkey Toggle

A profile hotkey can be configured as either:

- **Apply** — activates the selected profile.
- **Toggle** — activates the selected profile on the first press, then returns to the profile that was active immediately before the toggle on the next press.

Example:

```text
Game Auto -> Profile A
B profile -> F11 / Toggle

Game starts -> A
F11        -> B
F11 again  -> A
```

## Backup / Restore

The profile manager includes full settings backup and restore. Backups use the application's INI format and can be retained before future upgrades.

Before restoring, the application creates a safety copy of the current settings.

## Build

Requirements:

- Windows
- Visual Studio 2022 (or a compatible Visual Studio version)
- .NET Framework 4.7.2 targeting/development tools

Open `Tarkov-Gamma-Manager-v1.4.sln` and select **Release** configuration, then use **Build > Rebuild Solution**.

See `BUILD-INSTRUCTIONS-KR.md` for Korean build notes.

## Release distribution

For normal users, use the ZIP files attached to the GitHub Release. The repository itself contains the source and Visual Studio solution so that the project can be inspected and rebuilt independently.

## Security / anti-cheat scope

This project is intended as a display utility. The source does not intentionally implement:

- Tarkov process-memory reading/writing
- DLL injection into the game
- kernel drivers
- game file modification
- packet manipulation
- input injection for gameplay automation

This is a technical description, not a guarantee that a third-party anti-cheat will never flag any future version. Users should review the source and use the software at their own discretion.

## Provenance and licensing

The project was developed by modifying and extending the previously published **Gamma Manager** project. The original project was released under the **CC0 1.0 Universal** dedication, which is retained in `LICENSE.txt`.

The current repository contains substantial modifications and additional features. See `CHANGELOG.md` for the v1.4 changes maintained in this fork.

## Development assistance

Parts of the project were developed and refactored with assistance from OpenAI's ChatGPT.

## Disclaimer

Use the software at your own risk. The authors are not responsible for display configuration problems, data loss, game issues, or third-party anti-cheat decisions resulting from use of the software.
