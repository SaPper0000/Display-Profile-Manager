# Changelog

## v1.4.0

### Added
- Automatic default profiles for detected monitors.
- Default profiles available to Game Auto and Hotkey profile selection.
- Full profile/application settings backup and restore.
- Automatic pre-restore safety backup.
- Hotkey Apply / Toggle modes.
- Toggle hotkeys return to the profile that was active before the toggle.

### Changed
- Monitor control is disabled by default.
- Monitor controls are disabled while the master monitor-control option is off.
- Backup / Restore controls are placed below the Display Profile Manager on the right side of the main window.
- Version metadata updated to 1.4.0.
- Korean and English UI variants are maintained separately.

### Fixed
- Hotkey mode persistence.
- Monitor DDC/CI read failures no longer get treated as a real value of zero.
- Solution/project relative paths cleaned up for Visual Studio builds.
- Legacy project metadata and development-machine paths removed from the source distribution.
