# Tarkov Gamma Manager

Windows display profile manager for **Escape from Tarkov**.

Tarkov Gamma Manager lets you save and quickly switch monitor display settings such as gamma, brightness, contrast, **saturation (AMD)** and **Digital Vibrance (NVIDIA)**. It also supports per-monitor profiles, hotkeys, game auto profiles, and backup/restore.

> ⚠️ This is an independent community project and is **not affiliated with or endorsed by Battlestate Games or Escape from Tarkov**.

## ✨ What's New in v1.4.3

### 🎨 Saturation / Digital Vibrance

- **AMD:** Saturation control
- **NVIDIA:** Digital Vibrance control
- Per-monitor display settings can be saved and restored with profiles.
- The value box next to the slider can be edited directly with the keyboard.
- Press **Enter** after entering a value to apply it.

### 🖥️ Per-Monitor Profiles

Each detected monitor can have its own display settings and profile values, making the program suitable for multi-monitor setups.

### 🎮 Game Auto Profile

Automatically applies a selected profile when the configured game process starts.

Example:

```text
Escape from Tarkov starts
        ↓
Tarkov profile is applied
```

### ⌨️ Profile Hotkeys

Assign hotkeys to profiles for quick switching.

- **Apply:** activates the selected profile.
- **Toggle:** switches to the selected profile and pressing the same hotkey again returns to the previous profile.

### 💾 Backup / Restore

Back up saved profiles and program settings before updating. Restore them if necessary.

### 🌐 Korean / English

Two source versions are included:

- `Korean-Source`
- `English-Source`

The English source is based on the same feature set as the Korean source.

## 📦 Download / Run

The application is distributed as a portable Windows program.

1. Download the desired language ZIP from **GitHub Releases**.
2. Extract the ZIP.
3. Run the included `Tarkov Gamma Manager v1.4.exe`.

No installer is required.

## 🛠️ Build From Source

Open the solution with Visual Studio:

```text
Tarkov-Gamma-Manager-v1.4.sln
```

Recommended configuration:

```text
Configuration: Release
Platform: Any CPU
```

Build instructions are included in each source directory.

## 📁 Repository Structure

```text
Tarkov-Gamma-Manager-v1.4.3-Korean-English/
├─ Tarkov-Gamma-Manager-v1.4.3-Korean-Source/
│  ├─ Tarkov-Gamma-Manager-v1.4.sln
│  ├─ README.md
│  └─ BUILD-INSTRUCTIONS-KR.md
│
├─ Tarkov-Gamma-Manager-v1.4.3-English-Source/
│  ├─ Tarkov-Gamma-Manager-v1.4.sln
│  ├─ README.md
│  └─ BUILD-INSTRUCTIONS-EN.md
│
└─ README.md
```

## 🔒 Anti-Cheat / Game Interaction

This project is intended to manage Windows display settings. It does **not** intentionally include:

- Game memory read/write
- DLL injection
- Game DLL hooking
- Kernel drivers
- Game file modification
- Packet manipulation
- Game memory scanning
- In-game overlays

The game-auto feature only checks whether the configured game process is running and then applies the selected display profile.

> ⚠️ This describes the intended technical scope of the application. It is **not a guarantee** that BattlEye or Escape from Tarkov will always allow or ignore third-party software. Anti-cheat behavior and policies can change at any time. Use the software at your own discretion.

## ⚠️ Notes

- Monitor brightness/contrast controls depend on monitor and DDC/CI support.
- Some hardware or driver configurations may limit which display values can be read or changed.
- NVIDIA Digital Vibrance requires a supported NVIDIA graphics environment.
- AMD Saturation requires a supported AMD graphics environment.
- Back up your profiles before replacing an older version.
- This project is not an official Escape from Tarkov product.

## 📜 Original Project

This project is based on and extends the open-source **Gamma Manager** project by KrasnovM:

https://github.com/KrasnovM/Gamma-Manager

The original project's license and notices are retained in `LICENSE.txt`.

### Main additions / changes in this project

- Display profile management
- Game auto profiles
- Per-monitor settings
- Profile hotkeys
- Apply / Toggle hotkey modes
- Profile backup / restore
- Korean / English UI
- Monitor control improvements
- Display value read stability improvements
- **AMD Saturation control**
- **NVIDIA Digital Vibrance control**
- Direct keyboard value input for display controls
- UI and usability improvements
- Versioned release / source structure

## 🤖 Development

OpenAI ChatGPT was used as a development assistance tool during parts of the project's implementation, debugging, refactoring, and documentation process. Final code, testing, and release decisions remain the responsibility of the project maintainer.

## 📄 License

See `LICENSE.txt` for the original project's license and applicable notices.

## 📌 Current Version

**v1.4.3**
