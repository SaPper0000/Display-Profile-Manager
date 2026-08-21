# Tarkov Gamma Manager v1.4.0

A Windows display gamma and profile manager for Escape from Tarkov.

## Features

- Automatically creates a **Default** profile for every detected monitor at startup.
- Shows each monitor's Default profile in the main profile list.
- Makes monitor Default profiles available to **Game Auto** and **Hotkeys**.
- Preserves existing saved profiles without overwriting them.
- Global hotkeys with **Apply** and **Toggle** modes.
- Toggle mode returns to the profile that was active immediately before the toggle.
- Game Auto profiles for individual game executables.
- Profile backup and restore, including profiles, hotkeys, Game Auto settings, and program settings.
- Korean and English builds.
- Startup display-state backup/restore remains enabled so the display state from before launch can be restored when the program exits.

## Hotkey Toggle

Each profile hotkey can use either **Apply** or **Toggle** mode.

- **Apply:** pressing the hotkey applies the selected profile.
- **Toggle:** pressing the hotkey applies the selected profile; pressing the same hotkey again returns to the profile that was active before the toggle.

Example:

```text
Game Auto applies Profile A
        ↓
F11 → Profile B
        ↓
F11 → Profile A
```

Existing hotkeys default to **Apply** for compatibility.

## Backup / Restore

Use **Backup** to save all stored profiles, hotkeys, Game Auto settings, and program settings to a backup file.

Use **Restore** to replace the current configuration with a selected backup. The current configuration is automatically preserved as a `.pre-restore` backup before restoration.

It is recommended to make a backup before updating the program.

## Configuration

Settings are stored in `GammaManager.ini` in the same folder as the executable.

## Building

Open `Tarkov-Gamma-Manager-v1.4.sln` in Visual Studio 2022.

Recommended configuration:

- Configuration: `Release`
- Platform: `Any CPU`

Keep the solution and project folder structure intact when moving or extracting the source.

## Language

This source package builds the **English** version.

The language is selected at compile time in:

`Gamma Manager/LanguageManager.cs`

```csharp
public const bool Korean = false;
```

## Notes

This project is not affiliated with Battlestate Games or Escape from Tarkov.

The program manages Windows display settings and monitor settings. It does not intentionally modify game memory, inject DLLs, modify game files, or use a kernel driver.

Monitor brightness/contrast control depends on monitor and DDC/CI support.

See the repository documentation for additional information.
