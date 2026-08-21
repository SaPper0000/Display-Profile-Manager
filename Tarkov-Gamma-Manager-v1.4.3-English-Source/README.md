# Tarkov Gamma Manager v1.4.3

A portable Windows display gamma and profile manager for Escape from Tarkov.

## v1.4.3 Changes

- Added **GPU saturation** control for supported AMD displays.
- Added **NVIDIA Digital Vibrance** control.
- Added direct numeric editing for the saturation / Digital Vibrance value box.
- Press **Enter** after typing a value, or leave the value box, to apply the new value.
- Keeps per-monitor profile handling and existing game-auto / hotkey features.
- English and Korean source packages are provided separately.
- Version updated to **1.4.3**.

## Usage

- Select a monitor from the monitor list.
- Select or save a profile for that monitor.
- Adjust **Gamma, Brightness, Contrast, Saturation / Digital Vibrance** as supported by the GPU.
- Use **Game Auto** to assign profiles to game executables.
- Use **Hotkeys** to assign global profile shortcuts.
- Use **Backup / Restore** to protect and recover your settings.

Settings are stored in `GammaManager.ini` next to the executable.

## GPU Color Control

- **NVIDIA:** the GPU color control is shown as **Digital Vibrance**.
- **AMD:** the GPU color control is shown as **Saturation** when supported.
- Unsupported adapters show the control as unsupported.

## Build

- Target framework: **.NET Framework 4.7.2**
- Configuration: **Release | Any CPU**
- `Prefer32Bit=false`
- Use **Rebuild Solution** in Visual Studio.
- Do not use Visual Studio Publish / ClickOnce.
- The project is intended to be built as a normal portable WinForms executable.

## v1.4.2 Stability Notes

- The executable name was shortened to avoid CLR startup failures associated with long executable/path names.
- Legacy ClickOnce/bootstrapper settings were removed.
- The normal `Properties\\app.manifest` is used directly by the application.
