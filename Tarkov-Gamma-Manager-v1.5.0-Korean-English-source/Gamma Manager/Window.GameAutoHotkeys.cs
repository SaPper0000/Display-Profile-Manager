using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Windows.Forms;

namespace Gamma_Manager
{
    // Game auto-detection and global hotkey management.
    public partial class Window : Form
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, System.Text.StringBuilder lpExeName, ref uint lpdwSize);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private IntPtr lastForegroundHwnd = IntPtr.Zero;

        private void SetupGameAutoHook()
        {
            // Do not create a second hook if one is already active.
            if (gameAutoWinEventHook != IntPtr.Zero) return;

            // Listen for Windows foreground-window changes instead of polling every 350ms.
            // This only observes which top-level window is active; it does not hook into
            // Tarkov itself, inject a DLL, read game memory, or capture the screen.
            gameAutoWinEventDelegate = GameAutoWinEventProc;
            gameAutoWinEventHook = WinApi.SetWinEventHook(
                WinApi.EVENT_SYSTEM_FOREGROUND,
                WinApi.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                gameAutoWinEventDelegate,
                0,
                0,
                WinApi.WINEVENT_OUTOFCONTEXT);

            if (gameAutoWinEventHook == IntPtr.Zero)
                Logger.Warn("SetWinEventHook failed for Game Auto. Win32Error=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            else
                Logger.Info("Game Auto foreground-window hook registered.");

            // Evaluate once so starting the manager while a mapped game is already focused
            // still applies the correct profile immediately.
            EvaluateGameAutoState();
        }

        private bool IsGameAutoEnabled()
        {
            string value = iniFile.Read("AutoGameEnabled", "Settings");
            // Game Auto is OFF by default. Only an explicit 1/true enables it.
            return value == "1" || string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
        }

        // Called by the Game Auto settings UI. Turning the feature off removes the
        // Windows foreground-event hook and restores the exact profile state that
        // existed immediately before Game Auto first took control.
        internal void SetGameAutoEnabledFromSettings(bool enabled)
        {
            iniFile.Write("AutoGameEnabled", enabled ? "1" : "0", "Settings");

            if (enabled)
            {
                SetupGameAutoHook();
                return;
            }

            if (gameAutoWinEventHook != IntPtr.Zero)
            {
                WinApi.UnhookWinEvent(gameAutoWinEventHook);
                gameAutoWinEventHook = IntPtr.Zero;
            }
            gameAutoWinEventDelegate = null;

            // If Game Auto is currently controlling a game, restore the exact
            // pre-game profile set. If Alt+Tab already restored it, this is a no-op.
            if (autoGameFocused && !string.IsNullOrEmpty(activeAutoMonitor))
                RestoreGameAutoPreviousMonitor(activeAutoMonitor);

            autoGameFocused = false;
            activeAutoPreset = null;
            activeAutoMonitor = null;
            activeAutoProcess = null;
            gameAutoSessionProcess = null;
            gameAutoPreviousPresetByMonitor.Clear();
            gameAutoPreviousStateByMonitor.Clear();
            manualToggleReturnStateByMonitor.Clear();
            manualToggleWasOverGameAutoByMonitor.Clear();
            gameAutoPreviousStateCaptured = false;
        }

        private MonitorStateSnapshot CaptureMonitorState(Display.DisplayInfo display)
        {
            if (display == null) return null;

            // IMPORTANT: never trust the UI/profile cache for physical monitor state.
            // Hotkey/Game Auto must restore what the monitor was actually using at the
            // moment we entered the temporary state. DDC/CI and WMI can change outside
            // this app, so refresh the live hardware values first.
            int liveBrightness;
            int liveContrast;
            if (display.isExternal && display.PhysicalHandle != IntPtr.Zero && display.PhysicalHandle != (IntPtr)(-1))
            {
                if (ExternalMonitor.TryGetBrightness(display.PhysicalHandle, out liveBrightness))
                    display.monitorBrightness = liveBrightness;
                if (ExternalMonitor.TryGetContrast(display.PhysicalHandle, out liveContrast))
                    display.monitorContrast = liveContrast;
            }
            else if (!display.isExternal)
            {
                if (InternalMonitor.TryGetBrightness(out liveBrightness))
                    display.monitorBrightness = liveBrightness;
            }

            return new MonitorStateSnapshot
            {
                rGamma = display.rGamma,
                gGamma = display.gGamma,
                bGamma = display.bGamma,
                rContrast = display.rContrast,
                gContrast = display.gContrast,
                bContrast = display.bContrast,
                rBright = display.rBright,
                gBright = display.gBright,
                bBright = display.bBright,
                saturation = display.saturation,
                monitorBrightness = display.monitorBrightness,
                monitorContrast = display.monitorContrast
            };
        }

        private void RestoreMonitorState(string monitorName, MonitorStateSnapshot state)
        {
            if (string.IsNullOrEmpty(monitorName) || state == null) return;
            Display.DisplayInfo display = null;
            foreach (Display.DisplayInfo d in displays)
            {
                if (d != null && string.Equals(d.displayName, monitorName, StringComparison.Ordinal))
                { display = d; break; }
            }
            if (display == null) return;

            display.rGamma = state.rGamma; display.gGamma = state.gGamma; display.bGamma = state.bGamma;
            display.rContrast = state.rContrast; display.gContrast = state.gContrast; display.bContrast = state.bContrast;
            display.rBright = state.rBright; display.gBright = state.gBright; display.bBright = state.bBright;
            display.saturation = Clamp(state.saturation, display.saturationMin, display.saturationMax);
            display.monitorBrightness = state.monitorBrightness;
            display.monitorContrast = state.monitorContrast;

            Gamma.SetGammaRamp(display.displayLink,
                Gamma.CreateGammaRamp(display.rGamma, display.gGamma, display.bGamma,
                display.rContrast, display.gContrast, display.bContrast,
                display.rBright, display.gBright, display.bBright));
            Saturation.Prepare(display);
            Saturation.Apply(display, display.saturation);

            // Physical-monitor restore is asynchronous and uses the same serialized
            // queue as profile application. This prevents a stale Hotkey/Toggle write
            // from finishing after a restore and putting the old value back on screen.
            int restoreGeneration = ++physicalMonitorApplyGeneration;
            int restoreBrightness = Math.Max(0, Math.Min(100, display.monitorBrightness));
            int restoreContrast = Math.Max(0, Math.Min(100, display.monitorContrast));
            QueuePhysicalMonitorProfileValues(
                monitorName, restoreBrightness, restoreContrast, "Restore", restoreGeneration);

            if (currDisplay != null && string.Equals(currDisplay.displayName, monitorName, StringComparison.Ordinal))
            {
                fillInfo(display);
                disableChangeFunc = true;
                trackBarMonitorBrightness.Value = Math.Max(trackBarMonitorBrightness.Minimum, Math.Min(trackBarMonitorBrightness.Maximum, display.monitorBrightness));
                if (display.isExternal)
                    trackBarMonitorContrast.Value = Math.Max(trackBarMonitorContrast.Minimum, Math.Min(trackBarMonitorContrast.Maximum, display.monitorContrast));
                disableChangeFunc = false;
            }
        }

        private void CaptureGameAutoPreviousState()
        {
            gameAutoPreviousPresetByMonitor.Clear();
            gameAutoPreviousStateByMonitor.Clear();
            foreach (Display.DisplayInfo display in displays)
            {
                if (display == null || string.IsNullOrEmpty(display.displayName)) continue;
                string preset = GetCurrentPresetForMonitor(display.displayName);
                if (!string.IsNullOrEmpty(preset))
                    gameAutoPreviousPresetByMonitor[display.displayName] = preset;
                gameAutoPreviousStateByMonitor[display.displayName] = CaptureMonitorState(display);
            }
            gameAutoPreviousDisplayIndex = numDisplay;
            gameAutoPreviousStateCaptured = true;
        }

        private void RestoreGameAutoPreviousMonitor(string monitorName)
        {
            if (string.IsNullOrEmpty(monitorName)) return;

            // Layer order is: base state -> Game Auto -> manual Toggle.
            // If a manual toggle is active on the Game Auto monitor, removing
            // Game Auto must reveal the toggle layer, NOT the base state.
            string togglePreset;
            if (manualTogglePresetByMonitor.TryGetValue(monitorName, out togglePreset) &&
                !string.IsNullOrEmpty(togglePreset))
            {
                // The toggle is an overlay, not a new Game Auto session. Re-apply its
                // hardware values to the same monitor without changing foreground/UI
                // monitor selection or resurrecting Game Auto state.
                Logger.Info("Game Auto layer removed; preserving active toggle overlay. Monitor=" +
                    monitorName + ", Profile=" + togglePreset);
                applyingPreset = true;
                try { ApplyPreset(togglePreset, false, true); }
                finally { applyingPreset = false; }
                currentPresetByMonitor[monitorName] = togglePreset;
                return;
            }

            if (!gameAutoPreviousStateCaptured) return;

            MonitorStateSnapshot state;
            if (gameAutoPreviousStateByMonitor.TryGetValue(monitorName, out state) && state != null)
            {
                applyingPreset = true;
                try { RestoreMonitorState(monitorName, state); }
                finally { applyingPreset = false; }

                // Keep the logical profile layer synchronized with the restored base
                // state. This is critical after a toggle has already been turned OFF:
                // the old toggle profile must not reappear on a later Alt+Tab.
                string restoredPreset;
                if (gameAutoPreviousPresetByMonitor.TryGetValue(monitorName, out restoredPreset) &&
                    !string.IsNullOrEmpty(restoredPreset))
                    currentPresetByMonitor[monitorName] = restoredPreset;
                else
                    currentPresetByMonitor.Remove(monitorName);
                return;
            }

            string preset;
            if (!gameAutoPreviousPresetByMonitor.TryGetValue(monitorName, out preset) || string.IsNullOrEmpty(preset)) return;
            applyingPreset = true;
            try { ApplyPreset(preset, false, true); }
            finally { applyingPreset = false; }
        }

        private void RestoreGameAutoPreviousState()
        {
            if (!gameAutoPreviousStateCaptured) return;

            applyingPreset = true;
            try
            {
                foreach (Display.DisplayInfo display in displays)
                {
                    if (display == null) continue;
                    // A manual toggle is above Game Auto in the runtime layer.
                    // Never let a Game Auto restore erase an active toggle.
                    string togglePreset;
                    if (manualTogglePresetByMonitor.TryGetValue(display.displayName, out togglePreset) &&
                        !string.IsNullOrEmpty(togglePreset))
                    {
                        ApplyPreset(togglePreset, true, true);
                        continue;
                    }

                    MonitorStateSnapshot state;
                    if (gameAutoPreviousStateByMonitor.TryGetValue(display.displayName, out state) && state != null)
                        RestoreMonitorState(display.displayName, state);
                    else
                    {
                        string preset;
                        if (gameAutoPreviousPresetByMonitor.TryGetValue(display.displayName, out preset) && !string.IsNullOrEmpty(preset))
                            ApplyPreset(preset, false, true);
                    }
                }

                if (gameAutoPreviousDisplayIndex >= 0 && gameAutoPreviousDisplayIndex < displays.Count)
                {
                    disableChangeFunc = true;
                    numDisplay = gameAutoPreviousDisplayIndex;
                    currDisplay = displays[gameAutoPreviousDisplayIndex];
                    comboBoxMonitors.SelectedIndex = gameAutoPreviousDisplayIndex;
                    disableChangeFunc = false;
                    fillInfo(currDisplay);
                }
            }
            finally
            {
                applyingPreset = false;
                disableChangeFunc = false;
            }
        }

        private bool gameAutoEvaluationPending;

        private void GameAutoWinEventProc(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint idEventThread,
            uint msEventTime)
        {
            if (eventType != WinApi.EVENT_SYSTEM_FOREGROUND) return;
            if (idObject != WinApi.OBJID_WINDOW || idChild != 0) return;

            if (hwnd == lastForegroundHwnd && hwnd != IntPtr.Zero) return;
            lastForegroundHwnd = hwnd;

            // Windows can deliver EVENT_SYSTEM_FOREGROUND just before...

            // Windows can deliver EVENT_SYSTEM_FOREGROUND just before
            // GetForegroundWindow() reflects the newly activated window.
            // Defer the evaluation to the UI message queue so a mouse click
            // from a window on monitor B back to a borderless/fullscreen game
            // on monitor A is evaluated against the actual new foreground HWND.
            if (gameAutoEvaluationPending) return;
            gameAutoEvaluationPending = true;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    gameAutoEvaluationPending = false;
                    EvaluateGameAutoState();
                });
            }
            catch (Exception ex)
            {
                gameAutoEvaluationPending = false;
                Logger.Warn("Game Auto deferred evaluation failed: " + ex.Message);
            }
        }

        private string GetForegroundProcessName()
        {
            IntPtr hwnd = WinApi.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            uint pid;
            WinApi.GetWindowThreadProcessId(hwnd, out pid);

            try
            {
                // .NET의 무거운 Process 조회 대신, 이름만 읽는 안전한 Native API(최소 권한) 사용
                IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProcess != IntPtr.Zero)
                {
                    uint capacity = 1024;
                    System.Text.StringBuilder sb = new System.Text.StringBuilder((int)capacity);
                    if (QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
                    {
                        CloseHandle(hProcess);
                        return Path.GetFileName(sb.ToString());
                    }
                    CloseHandle(hProcess);
                }

                // API 호출 실패 시에만 기존 방식(Fallback) 사용
                Process p = Process.GetProcessById((int)pid);
                return p.ProcessName + ".exe";
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not resolve process name for foreground window: " + ex.Message);
                return null;
            }
        }

        private string GetForegroundMonitorName()
        {
            IntPtr hwnd = WinApi.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            try
            {
                string deviceName = Screen.FromHandle(hwnd).DeviceName;
                if (string.IsNullOrEmpty(deviceName) || displays == null) return null;

                foreach (Display.DisplayInfo display in displays)
                {
                    if (display != null && string.Equals(display.displayLink, deviceName, StringComparison.OrdinalIgnoreCase))
                        return display.displayName;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not resolve foreground monitor: " + ex.Message);
            }
            return null;
        }

        private bool TryGetAutoMapping(string foregroundExe, out string profile, out bool applyMonitor)
        {
            profile = null;
            applyMonitor = false;
            if (string.IsNullOrEmpty(foregroundExe)) return false;

            string foregroundMonitor = GetForegroundMonitorName();

            // Global Game Auto switch. Individual mapping checkboxes are evaluated below.
            string autoGameEnabled = iniFile.Read("AutoGameEnabled", "Settings");
            if (autoGameEnabled == "0" || string.Equals(autoGameEnabled, "False", StringComparison.OrdinalIgnoreCase))
                return false;

            string[] sections = iniFile.GetSections();
            if (sections == null) return false;

            foreach (string section in sections)
            {
                if (!section.StartsWith("AutoGame_", StringComparison.OrdinalIgnoreCase)) continue;
                string enabled = iniFile.Read("Enabled", section);
                string exe = iniFile.Read("Process", section);
                string p = iniFile.Read("Profile", section);
                if ((enabled == "1" || string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrEmpty(exe) && !string.IsNullOrEmpty(p) &&
                    string.Equals(Path.GetFileName(exe), Path.GetFileName(foregroundExe), StringComparison.OrdinalIgnoreCase))
                {
                    // If the same EXE is assigned to multiple profiles, select the profile
                    // belonging to the monitor where the foreground game window actually is.
                    // This is what allows Monitor A -> Profile A and Monitor B -> Profile B.
                    string mappedMonitor = iniFile.Read("monitor", p);
                    if (!string.IsNullOrEmpty(foregroundMonitor) &&
                        !string.IsNullOrEmpty(mappedMonitor) &&
                        !string.Equals(mappedMonitor, foregroundMonitor, StringComparison.OrdinalIgnoreCase))
                        continue;

                    profile = p;
                    // The profile itself determines the target monitor.
                    applyMonitor = true;
                    return true;
                }
            }

            // Backward compatibility: old mappings never changed physical monitor controls automatically.
            foreach (string p in sections)
            {
                if (p.StartsWith("AutoGame_", StringComparison.OrdinalIgnoreCase)) continue;
                if (p.Equals("Hotkeys", StringComparison.OrdinalIgnoreCase)) continue;
                string enabled = iniFile.Read("AutoEnabled", p);
                string exe = iniFile.Read("AutoProcess", p);
                if ((enabled == "1" || string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrEmpty(exe) &&
                    string.Equals(Path.GetFileName(exe), Path.GetFileName(foregroundExe), StringComparison.OrdinalIgnoreCase))
                {
                    string mappedMonitor = iniFile.Read("monitor", p);
                    if (!string.IsNullOrEmpty(foregroundMonitor) &&
                        !string.IsNullOrEmpty(mappedMonitor) &&
                        !string.Equals(mappedMonitor, foregroundMonitor, StringComparison.OrdinalIgnoreCase))
                        continue;

                    profile = p;
                    applyMonitor = true;
                    return true;
                }
            }
            return false;
        }

        private bool IsProcessRunning(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return false;
            try
            {
                string name = Path.GetFileNameWithoutExtension(exeName);
                return Process.GetProcessesByName(name).Length > 0;
            }
            catch (Exception ex)
            {
                Logger.Warn("Game Auto process-state check failed: " + ex.Message);
                return false;
            }
        }

        private void EvaluateGameAutoState()
        {
            if (IsDisposed || Disposing || applyingPreset || displays == null || displays.Count == 0) return;
            if (!IsGameAutoEnabled()) return;

            string foreground = GetForegroundProcessName();
            string matchedProfile;
            bool applyMonitor;
            bool mapped = TryGetAutoMapping(foreground, out matchedProfile, out applyMonitor);

            // The Game Auto "previous" state belongs to the lifetime of the mapped
            // GAME PROCESS, not the lifetime of its foreground focus. Alt+Tab must not
            // discard this snapshot: the user can enable/disable a Toggle while the game
            // is unfocused and we still need the original 70/70 base later.
            if (!string.IsNullOrEmpty(gameAutoSessionProcess) &&
                !IsProcessRunning(gameAutoSessionProcess))
            {
                Logger.Info("Game Auto session ended; clearing previous-state snapshot. Process=" +
                    gameAutoSessionProcess);
                gameAutoSessionProcess = null;
                gameAutoPreviousPresetByMonitor.Clear();
                gameAutoPreviousStateByMonitor.Clear();
                gameAutoPreviousStateCaptured = false;
            }

            // Priority is strictly: Hotkey > Game Auto.
            // Main-window profile selection is never used to override Game Auto.
            // Hotkey selection survives Alt+Tab and is reapplied while its game session is alive.
            if (manualOverrideActive && !string.IsNullOrEmpty(manualOverrideProcess) &&
                !IsProcessRunning(manualOverrideProcess))
            {
                // A toggle is independent of the lifetime of the process that happened
                // to be foreground when it was pressed. Keep the toggle override alive
                // until the same toggle hotkey is pressed again.
                if (manualToggleActive)
                {
                    manualOverrideProcess = null;
                }
                else
                {
                    manualOverrideActive = false;
                    manualOverridePreset = null;
                    manualOverrideProcess = null;
                    manualOverrideReturnPreset = null;
                    manualOverrideMonitor = null;
                }
            }

            string foregroundMonitorForHotkey = GetForegroundMonitorName();
            string foregroundTogglePreset = null;
            bool toggleAppliesToForegroundMonitor =
                !string.IsNullOrEmpty(foregroundMonitorForHotkey) &&
                manualTogglePresetByMonitor.TryGetValue(foregroundMonitorForHotkey, out foregroundTogglePreset) &&
                !string.IsNullOrEmpty(foregroundTogglePreset);

            bool hotkeyAppliesToForegroundMonitor = toggleAppliesToForegroundMonitor ||
                (manualOverrideActive &&
                 !string.IsNullOrEmpty(manualOverridePreset) &&
                 !string.IsNullOrEmpty(manualOverrideMonitor) &&
                 string.Equals(manualOverrideMonitor, foregroundMonitorForHotkey, StringComparison.OrdinalIgnoreCase));

            // A Toggle is the top-most runtime layer. Once a Toggle is active on the
            // monitor where the mapped game is running, Game Auto must NEVER write its
            // profile to that monitor, even during the foreground transition caused by
            // Alt+Tab. The old flow could briefly restore Game Auto/base and then apply
            // the 30/30 profile before the toggle check ran, which made an active 40/40
            // Toggle appear to turn off when returning to the game.
            //
            // Handle this before any Game Auto snapshot/restore/apply work. The toggle
            // remains authoritative until its own hotkey is pressed again.
            if (mapped && toggleAppliesToForegroundMonitor)
            {
                Logger.Info("Game Auto blocked by active Toggle. Monitor=" +
                    foregroundMonitorForHotkey + ", ToggleProfile=" + foregroundTogglePreset +
                    ", GameAutoProfile=" + matchedProfile);

                // If Game Auto was controlling a different monitor, finish that layer
                // without touching the protected Toggle monitor. If it was controlling
                // this same monitor, leave it alone: the Toggle is already the visible
                // top layer and must remain in place.
                if (autoGameFocused && !string.IsNullOrEmpty(activeAutoMonitor) &&
                    !string.Equals(activeAutoMonitor, foregroundMonitorForHotkey, StringComparison.OrdinalIgnoreCase))
                {
                    RestoreGameAutoPreviousMonitor(activeAutoMonitor);
                    autoGameFocused = false;
                    activeAutoPreset = null;
                    activeAutoMonitor = null;
                    activeAutoProcess = null;
                }

                activeAutoPreset = foregroundTogglePreset;
                activeAutoMonitor = foregroundMonitorForHotkey;
                activeAutoProcess = foreground;
                lastGameAutoAppliedMonitor = foregroundMonitorForHotkey;
                lastGameAutoAppliedPreset = foregroundTogglePreset;
                autoGameFocused = true;
                currentPresetByMonitor[foregroundMonitorForHotkey] = foregroundTogglePreset;
                return;
            }

            if (mapped)
            {
                string desiredProfile = matchedProfile;
                bool desiredMonitor = applyMonitor;

                // 1) Hotkey override
                // Hotkey overrides are scoped to their configured monitor. A toggle on
                // monitor B must never block Game Auto on monitor A. This is especially
                // important when the user leaves a toggle active on one monitor and then
                // launches/focuses a mapped game on another monitor.
                if (toggleAppliesToForegroundMonitor)
                {
                    desiredProfile = foregroundTogglePreset;
                    desiredMonitor = true;
                }
                else if (hotkeyAppliesToForegroundMonitor)
                {
                    desiredProfile = manualOverridePreset;
                    desiredMonitor = true;
                    if (string.IsNullOrEmpty(manualOverrideProcess))
                        manualOverrideProcess = foreground;
                }

                string foregroundMonitor = GetForegroundMonitorName();

                if (!autoGameFocused && !gameAutoPreviousStateCaptured)
                {
                    // Capture the exact profile state once, before Game Auto changes
                    // anything. Keep this snapshot across Alt+Tab while a manual Toggle
                    // is still layered above Game Auto; otherwise returning to the game
                    // would incorrectly capture the Toggle profile as the new "base".
                    CaptureGameAutoPreviousState();
                    gameAutoSessionProcess = foreground;
                }
                else if (!string.IsNullOrEmpty(activeAutoMonitor) &&
                         !string.Equals(activeAutoMonitor, foregroundMonitor, StringComparison.OrdinalIgnoreCase))
                {
                    // Alt+Tab is a Game Auto exit condition. Restore the exact state that
                    // existed before Game Auto took control on its original monitor.
                    // Any manual toggle on another monitor is independent and is NOT touched.
                    Logger.Info("Game Auto focus lost. Restoring auto monitor before Alt+Tab. AutoMonitor=" +
                        activeAutoMonitor + ", ForegroundMonitor=" + foregroundMonitor);
                    RestoreGameAutoPreviousMonitor(activeAutoMonitor);
                    autoGameFocused = false;
                    activeAutoPreset = null;
                    activeAutoMonitor = null;
                    activeAutoProcess = null;
                }

                if (!autoGameFocused || !string.Equals(activeAutoPreset, desiredProfile, StringComparison.Ordinal) ||
                    !string.Equals(activeAutoMonitor, foregroundMonitor, StringComparison.OrdinalIgnoreCase))
                {
                    // Never let a Game Auto transition overwrite a manual toggle layer
                    // that belongs to a different monitor. The toggle dictionary is the
                    // authoritative top layer and is restored independently on focus loss.
                    string toggleOnTargetMonitor = null;
                    if (manualTogglePresetByMonitor.TryGetValue(foregroundMonitor, out toggleOnTargetMonitor) &&
                        !string.IsNullOrEmpty(toggleOnTargetMonitor))
                    {
                        desiredProfile = toggleOnTargetMonitor;
                        desiredMonitor = true;
                    }

                    activeAutoPreset = desiredProfile;
                    activeAutoMonitor = foregroundMonitor;
                    activeAutoProcess = foreground;
                    lastGameAutoAppliedMonitor = foregroundMonitor;
                    lastGameAutoAppliedPreset = desiredProfile;
                    ApplyPreset(desiredProfile, true, desiredMonitor);
                }
                else
                {
                    activeAutoMonitor = foregroundMonitor;
                }
                autoGameFocused = true;
            }
            else if (autoGameFocused)
            {
                // No mapped Game Auto profile is foreground. This is Alt+Tab or another
                // non-mapped window. Game Auto ends immediately on focus loss. The mapped
                // monitor is restored, while any manual toggle on the foreground monitor
                // remains completely independent.
                Logger.Info("Game Auto focus lost. Restoring auto monitor. AutoMonitor=" +
                    (activeAutoMonitor ?? "<none>") + ", ForegroundMonitor=" +
                    (GetForegroundMonitorName() ?? "<none>"));

                if (!string.IsNullOrEmpty(activeAutoMonitor))
                    RestoreGameAutoPreviousMonitor(activeAutoMonitor);

                string endedAutoMonitor = activeAutoMonitor;
                string endedAutoProcess = activeAutoProcess;
                autoGameFocused = false;
                activeAutoPreset = null;
                activeAutoMonitor = null;
                activeAutoProcess = null;

                // IMPORTANT: losing foreground focus is NOT the end of the Game Auto
                // session. Keep the original pre-Game-Auto snapshot for as long as the
                // mapped game process is alive. This is what lets: 70/70 -> game 30/30
                // -> Alt+Tab 70/70 -> Toggle 40/40 -> Toggle OFF 30/30 -> Alt+Tab 70/70
                // work without ever mistaking the Toggle's 40/40 for the base state.
                if (string.IsNullOrEmpty(gameAutoSessionProcess))
                    gameAutoSessionProcess = endedAutoProcess;

                // Keep the UI selection aligned with the actual foreground monitor.
                // This prevents a restored toggle profile from making the manager appear
                // to have switched to the toggle's monitor during Alt+Tab.
                string fgMonitorAfterExit = GetForegroundMonitorName();
                if (!string.IsNullOrEmpty(fgMonitorAfterExit))
                {
                    for (int i = 0; i < displays.Count; i++)
                    {
                        if (displays[i] != null && string.Equals(displays[i].displayName, fgMonitorAfterExit, StringComparison.OrdinalIgnoreCase))
                        {
                            disableChangeFunc = true;
                            numDisplay = i;
                            currDisplay = displays[i];
                            comboBoxMonitors.SelectedIndex = i;
                            disableChangeFunc = false;
                            fillInfo(currDisplay);
                            break;
                        }
                    }
                }

                // Do not touch manualTogglePresetByMonitor here. A toggle on another
                // monitor must survive Alt+Tab and remain active until its own toggle
                // hotkey is pressed again.
            }

            else if (!mapped && manualOverrideActive && !string.IsNullOrEmpty(manualOverrideProcess) && !IsProcessRunning(manualOverrideProcess))
            {
                // A hotkey pressed before entering the game keeps its priority until the
                // mapped game actually starts. Once that game process ends, clear the override.
                manualOverrideActive = false;
                manualOverridePreset = null;
                manualOverrideProcess = null;
                manualOverrideReturnPreset = null;
                manualOverrideMonitor = null;
            }
        }

        internal void SuspendGlobalHotkeys()
        {
            foreach (GlobalHotkey hotkey in globalHotkeys.Values)
                hotkey.Dispose();
            globalHotkeys.Clear();
            globalHotkeyPresets.Clear();
        }

        internal void ResumeGlobalHotkeys()
        {
            RefreshGlobalHotkeys();
        }

        private void ClearManualHotkeyStateAndRestoreBase()
        {
            // Removing/changing a hotkey must also remove the live manual override.
            // Restore exactly what was underneath the hotkey first (Game Auto profile
            // or the normal/default profile), then clear the override state.
            string returnPreset = manualToggleActive ? null : manualOverrideReturnPreset;
            string returnMonitor = manualOverrideMonitor;

            manualOverrideActive = false;
            manualOverridePreset = null;
            manualOverrideProcess = null;
            manualOverrideReturnPreset = null;
            manualOverrideMonitor = null;
            manualTogglePresetByMonitor.Clear();
            manualToggleReturnPresetByMonitor.Clear();
            manualToggleActiveByMonitor.Clear();
            manualToggleActive = false;
            manualTogglePreset = null;
            manualToggleReturnPreset = null;

            if (!string.IsNullOrEmpty(returnPreset))
            {
                applyingPreset = true;
                try { ApplyPreset(returnPreset, true, true); }
                finally { applyingPreset = false; }
            }
            else
            {
                // No captured profile means the hotkey was applied over the normal
                // baseline. Prefer the monitor default instead of leaving the old
                // hotkey profile stuck on screen.
                if (!string.IsNullOrEmpty(returnMonitor))
                {
                    string prefix = LanguageManager.Korean ? "기본값 - " : "Default - ";
                    string defaultPreset = prefix + returnMonitor;
                    if (!string.IsNullOrEmpty(iniFile.Read("monitor", defaultPreset)))
                    {
                        applyingPreset = true;
                        try { ApplyPreset(defaultPreset, true, true); }
                        finally { applyingPreset = false; }
                    }
                }
            }

            autoGameFocused = false;
            activeAutoPreset = null;
            activeAutoMonitor = null;
            activeAutoProcess = null;
            if (IsGameAutoEnabled())
                EvaluateGameAutoState();
        }

        // 특정 모니터 1개만 강제 초기화하는 메서드
        private void ResetMonitorHard(Display.DisplayInfo targetDisplay)
        {
            if (targetDisplay == null) return;

            Logger.Info($"Hard Reset hotkey pressed for single monitor: {targetDisplay.displayName}");

            physicalMonitorApplyGeneration++;

            // Toggle 상태 등을 해당 모니터에 대해서만 해제
            manualTogglePresetByMonitor.Remove(targetDisplay.displayName);
            manualToggleReturnPresetByMonitor.Remove(targetDisplay.displayName);
            manualToggleReturnStateByMonitor.Remove(targetDisplay.displayName);
            manualToggleWasOverGameAutoByMonitor.Remove(targetDisplay.displayName);
            manualToggleActiveByMonitor.Remove(targetDisplay.displayName);
            manualToggleActive = manualTogglePresetByMonitor.Count > 0;
            if (!manualToggleActive)
            {
                manualTogglePreset = null;
                manualToggleReturnPreset = null;
            }

            if (string.Equals(manualOverrideMonitor, targetDisplay.displayName, StringComparison.OrdinalIgnoreCase))
            {
                manualOverrideActive = false;
                manualOverridePreset = null;
                manualOverrideProcess = null;
                manualOverrideReturnPreset = null;
                manualOverrideMonitor = null;
            }

            if (string.Equals(activeAutoMonitor, targetDisplay.displayName, StringComparison.OrdinalIgnoreCase))
            {
                activeAutoPreset = null;
                autoGameFocused = false;
                activeAutoMonitor = null;
            }

            gameAutoPreviousPresetByMonitor.Remove(targetDisplay.displayName);

            // 캡처해둔 원본 상태로 복원
            StartupStateManager.RestoreOriginalMonitor(targetDisplay);

            // 현재 UI의 모니터가 대상 모니터일 경우 슬라이더 값 갱신 (화면 렉 방지)
            if (currDisplay != null && string.Equals(currDisplay.displayName, targetDisplay.displayName, StringComparison.Ordinal))
            {
                disableChangeFunc = true;
                try
                {
                    currDisplay.rGamma = 1f; currDisplay.gGamma = 1f; currDisplay.bGamma = 1f;
                    currDisplay.rContrast = 1f; currDisplay.gContrast = 1f; currDisplay.bContrast = 1f;
                    currDisplay.rBright = 0f; currDisplay.gBright = 0f; currDisplay.bBright = 0f;

                    trackBarGamma.Value = 100;
                    trackBarContrast.Value = 100;
                    trackBarBrightness.Value = 0;

                    if (currDisplay.isExternal)
                    {
                        int b, c;
                        if (ExternalMonitor.TryGetBrightness(currDisplay.PhysicalHandle, out b)) currDisplay.monitorBrightness = b;
                        if (ExternalMonitor.TryGetContrast(currDisplay.PhysicalHandle, out c)) currDisplay.monitorContrast = c;
                        trackBarMonitorBrightness.Value = Clamp(currDisplay.monitorBrightness, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                        trackBarMonitorContrast.Value = Clamp(currDisplay.monitorContrast, trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum);
                    }
                    else
                    {
                        int b;
                        if (InternalMonitor.TryGetBrightness(out b)) currDisplay.monitorBrightness = b;
                        trackBarMonitorBrightness.Value = Clamp(currDisplay.monitorBrightness, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                    }

                    if (currDisplay.saturationSupported)
                    {
                        currDisplay.saturation = currDisplay.saturationDefault;
                        trackBarSaturation.Value = Clamp(currDisplay.saturationDefault, trackBarSaturation.Minimum, trackBarSaturation.Maximum);
                    }
                    comboBoxPresets.Text = string.Empty;
                }
                finally
                {
                    disableChangeFunc = false;
                }
                RefreshNumericBoxes();
            }
            else
            {
                // UI에 보이지 않는 모니터라도 내부 값은 초기화
                targetDisplay.rGamma = 1f; targetDisplay.gGamma = 1f; targetDisplay.bGamma = 1f;
                targetDisplay.rContrast = 1f; targetDisplay.gContrast = 1f; targetDisplay.bContrast = 1f;
                targetDisplay.rBright = 0f; targetDisplay.gBright = 0f; targetDisplay.bBright = 0f;
                if (targetDisplay.saturationSupported) targetDisplay.saturation = targetDisplay.saturationDefault;
            }

            if (targetDisplay.saturationSupported)
                Saturation.Apply(targetDisplay, targetDisplay.saturation);
            Gamma.SetGammaRamp(targetDisplay.displayLink, Gamma.CreateGammaRamp(1, 1, 1, 1, 1, 1, 0, 0, 0));

            currentPresetByMonitor.Remove(targetDisplay.displayName);
        }

        // 전체 모니터를 강제 초기화하는 메서드
        private void ResetAllMonitorsHard()
        {
            Logger.Info("Hard Reset All hotkey pressed.");

            // 1. 모든 지연 예약 취소
            physicalMonitorApplyGeneration++;

            // 2. 모든 런타임 토글/오버라이드 상태 싹 초기화
            manualOverrideActive = false;
            manualOverridePreset = null;
            manualOverrideProcess = null;
            manualOverrideReturnPreset = null;
            manualOverrideMonitor = null;
            manualTogglePresetByMonitor.Clear();
            manualToggleReturnPresetByMonitor.Clear();
            manualToggleReturnStateByMonitor.Clear();
            manualToggleWasOverGameAutoByMonitor.Clear();
            manualToggleActiveByMonitor.Clear();
            manualToggleActive = false;
            manualTogglePreset = null;
            manualToggleReturnPreset = null;
            activeAutoPreset = null;
            autoGameFocused = false;
            gameAutoPreviousPresetByMonitor.Clear();
            gameAutoPreviousStateCaptured = false;
            activeAutoMonitor = null;
            gameAutoPreviousDisplayIndex = numDisplay;

            // 3. 모든 물리 모니터 순회하며 캡처된 원본 상태 복원
            foreach (Display.DisplayInfo display in displays)
            {
                if (display == null) continue;

                // 물리 모니터 원래 상태 씌우기
                bool restored = StartupStateManager.RestoreOriginalMonitor(display);

                if (!restored)
                {
                    Logger.Warn($"ResetAllMonitorsHard: RestoreOriginalMonitor failed for {display.displayName}");
                }

                // 내부 데이터 초기화
                display.rGamma = 1f; display.gGamma = 1f; display.bGamma = 1f;
                display.rContrast = 1f; display.gContrast = 1f; display.bContrast = 1f;
                display.rBright = 0f; display.gBright = 0f; display.bBright = 0f;

                if (display.saturationSupported)
                {
                    display.saturation = display.saturationDefault;
                    Saturation.Apply(display, display.saturation);
                }

                Gamma.SetGammaRamp(display.displayLink, Gamma.CreateGammaRamp(1, 1, 1, 1, 1, 1, 0, 0, 0));
                currentPresetByMonitor.Remove(display.displayName);

                // 현재 UI에 떠있는 모니터라면 UI값도 동기화
                if (currDisplay != null && string.Equals(currDisplay.displayName, display.displayName, StringComparison.Ordinal))
                {
                    disableChangeFunc = true;
                    try
                    {
                        trackBarGamma.Value = 100;
                        trackBarContrast.Value = 100;
                        trackBarBrightness.Value = 0;

                        if (currDisplay.isExternal)
                        {
                            int b, c;
                            if (ExternalMonitor.TryGetBrightness(currDisplay.PhysicalHandle, out b)) currDisplay.monitorBrightness = b;
                            if (ExternalMonitor.TryGetContrast(currDisplay.PhysicalHandle, out c)) currDisplay.monitorContrast = c;
                            trackBarMonitorBrightness.Value = Clamp(currDisplay.monitorBrightness, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                            trackBarMonitorContrast.Value = Clamp(currDisplay.monitorContrast, trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum);
                        }
                        else
                        {
                            int b;
                            if (InternalMonitor.TryGetBrightness(out b)) currDisplay.monitorBrightness = b;
                            trackBarMonitorBrightness.Value = Clamp(currDisplay.monitorBrightness, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                        }

                        if (currDisplay.saturationSupported)
                        {
                            trackBarSaturation.Value = Clamp(currDisplay.saturationDefault, trackBarSaturation.Minimum, trackBarSaturation.Maximum);
                        }
                        comboBoxPresets.Text = string.Empty;
                    }
                    finally
                    {
                        disableChangeFunc = false;
                    }
                    RefreshNumericBoxes();
                }
            }

            initPresets();
            initTrayMenu();
            Logger.Info("ALL MONITORS HARD RESET completed via hotkey.");
        }

        private void RefreshGlobalHotkeys()
        {
            foreach (GlobalHotkey hotkey in globalHotkeys.Values)
                hotkey.Dispose();
            globalHotkeys.Clear();
            globalHotkeyPresets.Clear();

            // --- 1. 전체 모니터 초기화 핫키 등록 ---
            string resetAllHotkeyText = iniFile.Read(HotkeySettingsForm.HARD_RESET_ALL_PRESET, "Hotkeys");
            Keys resetAllKey;
            GlobalHotkey.Modifiers resetAllModifiers;
            if (TryParseHotkey(resetAllHotkeyText, out resetAllKey, out resetAllModifiers))
            {
                int resetId = nextHotkeyId++;
                GlobalHotkey resetHotkey = new GlobalHotkey(this.Handle, resetId, resetAllKey, resetAllModifiers);
                resetHotkey.Pressed += delegate
                {
                    // 전체 모니터 초기화 전용 로직 호출
                    ResetAllMonitorsHard();
                };

                if (resetHotkey.Register())
                {
                    globalHotkeys.Add(resetId, resetHotkey);
                    globalHotkeyPresets.Add(resetId, HotkeySettingsForm.HARD_RESET_ALL_PRESET);
                }
                else
                {
                    resetHotkey.Dispose();
                }
            }

            // --- 2. 개별 모니터 초기화 핫키 등록 ---
            foreach (Display.DisplayInfo display in displays)
            {
                if (display == null) continue;
                string presetName = HotkeySettingsForm.HARD_RESET_SINGLE_PREFIX + display.displayName;
                string resetSingleHotkeyText = iniFile.Read(presetName, "Hotkeys");
                Keys resetSingleKey;
                GlobalHotkey.Modifiers resetSingleModifiers;

                if (TryParseHotkey(resetSingleHotkeyText, out resetSingleKey, out resetSingleModifiers))
                {
                    int resetId = nextHotkeyId++;
                    GlobalHotkey resetHotkey = new GlobalHotkey(this.Handle, resetId, resetSingleKey, resetSingleModifiers);

                    // 이벤트 핸들러가 참조할 모니터 변수를 지역변수로 캡처 (클로저 이슈 방지)
                    Display.DisplayInfo targetDisplay = display;

                    resetHotkey.Pressed += delegate
                    {
                        ResetMonitorHard(targetDisplay);
                    };

                    if (resetHotkey.Register())
                    {
                        globalHotkeys.Add(resetId, resetHotkey);
                        globalHotkeyPresets.Add(resetId, presetName);
                    }
                    else
                    {
                        resetHotkey.Dispose();
                    }
                }
            }

            // --- 3. 일반 프로필 핫키 등록 ---
            string[] presets = iniFile.GetSections();
            if (presets == null) return;

            foreach (string preset in presets)
            {
                string hotkeyText = iniFile.Read(preset, "Hotkeys");
                Keys key;
                GlobalHotkey.Modifiers modifiers;
                if (!TryParseHotkey(hotkeyText, out key, out modifiers)) continue;

                int id = nextHotkeyId++;
                GlobalHotkey hotkey = new GlobalHotkey(this.Handle, id, key, modifiers);
                string capturedPreset = preset;
                bool isToggle = string.Equals(iniFile.Read("HotkeyMode", capturedPreset), "Toggle", StringComparison.OrdinalIgnoreCase);
                hotkey.Pressed += delegate
                {
                    string visiblePreset = capturedPreset;
                    string fg = GetForegroundProcessName();
                    string mapped;
                    bool mappedMonitor;
                    bool hasGameMapping = TryGetAutoMapping(fg, out mapped, out mappedMonitor);
                    string capturedMonitor = iniFile.Read("monitor", capturedPreset);
                    if (string.IsNullOrEmpty(capturedMonitor)) return;

                    if (isToggle)
                    {
                        // IMPORTANT: A Toggle belongs to the monitor configured in
                        // the selected profile, NOT to whichever monitor happens to be
                        // in the foreground when the hotkey is pressed.
                        //
                        // Example:
                        //   A game is on monitor A -> Alt+Tab to B -> press A Toggle
                        //   -> A becomes 40/40 and stays 40/40 when returning to the game.
                        //
                        // This is required for the priority order:
                        //   Toggle > Game Auto > normal/base
                        //
                        // The configured monitor is therefore the runtime Toggle layer key.
                        string targetProfileMonitor = capturedMonitor;

                        // Find an already-active toggle by PROFILE. This allows the same
                        // Toggle hotkey to turn itself OFF even when it is pressed while
                        // another monitor is in the foreground.
                        string toggleMonitor = null;
                        foreach (KeyValuePair<string, string> kv in manualTogglePresetByMonitor)
                        {
                            if (string.Equals(kv.Value, capturedPreset, StringComparison.Ordinal))
                            {
                                toggleMonitor = kv.Key;
                                break;
                            }
                        }

                        bool toggleAlreadyActive = !string.IsNullOrEmpty(toggleMonitor);
                        string targetToggleMonitor = toggleAlreadyActive ? toggleMonitor : targetProfileMonitor;

                        if (toggleAlreadyActive)
                        {
                            // Invalidate the previous Toggle-ON delayed physical apply before
                            // restoring the OFF state. Without this, a queued 70/70 callback
                            // could run after OFF and put the monitor back at 70/70 even though
                            // the UI correctly shows the new 40/40 profile.
                            ++physicalMonitorApplyGeneration;

                            // IMPORTANT: a toggle belongs to the monitor configured by
                            // the profile. Pressing its hotkey from another foreground
                            // monitor must still restore only this toggle's own state.
                            bool toggleWasOverGameAuto = false;
                            manualToggleWasOverGameAutoByMonitor.TryGetValue(targetToggleMonitor, out toggleWasOverGameAuto);

                            MonitorStateSnapshot returnState = null;
                            string restoredPreset = null;

                            // Layer rule:
                            //   Base 50/50 -> Game Auto A -> Toggle B
                            // If B was enabled while Game Auto A was active, B OFF must reveal
                            // the layer that is actually underneath it. When the game is
                            // unfocused, Game Auto is no longer the visible layer, so reveal
                            // the original pre-Game-Auto base (50/50). When the game is still
                            // focused, reveal Game Auto A instead.
                            if (toggleWasOverGameAuto && !autoGameFocused && gameAutoPreviousStateCaptured &&
                                gameAutoPreviousStateByMonitor.TryGetValue(targetToggleMonitor, out returnState) && returnState != null)
                            {
                                gameAutoPreviousPresetByMonitor.TryGetValue(targetToggleMonitor, out restoredPreset);
                                Logger.Info("Toggle OFF while Game Auto is unfocused; restoring pre-Game-Auto base state. Monitor=" +
                                    targetToggleMonitor + ", BasePreset=" + (restoredPreset ?? "<none>"));
                            }
                            else if (manualToggleReturnStateByMonitor.TryGetValue(targetToggleMonitor, out returnState) && returnState != null)
                            {
                                manualToggleReturnPresetByMonitor.TryGetValue(targetToggleMonitor, out restoredPreset);
                            }

                            if (returnState != null)
                            {
                                applyingPreset = true;
                                try { RestoreMonitorState(targetToggleMonitor, returnState); }
                                finally { applyingPreset = false; }

                                // Keep the logical layer synchronized with the restored
                                // runtime state so later Game Auto/Alt+Tab cannot resurrect B.
                                if (!string.IsNullOrEmpty(restoredPreset))
                                    currentPresetByMonitor[targetToggleMonitor] = restoredPreset;
                                else
                                    currentPresetByMonitor.Remove(targetToggleMonitor);
                            }
                            else
                            {
                                string returnPreset = null;
                                if (!manualToggleReturnPresetByMonitor.TryGetValue(targetToggleMonitor, out returnPreset) || string.IsNullOrEmpty(returnPreset))
                                    returnPreset = GetCurrentPresetForMonitor(targetToggleMonitor);
                                if (!string.IsNullOrEmpty(returnPreset))
                                {
                                    applyingPreset = true;
                                    try { ApplyPreset(returnPreset, false, true); }
                                    finally { applyingPreset = false; }
                                }
                            }

                            manualTogglePresetByMonitor.Remove(targetToggleMonitor);
                            manualToggleReturnPresetByMonitor.Remove(targetToggleMonitor);
                            manualToggleReturnStateByMonitor.Remove(targetToggleMonitor);
                            manualToggleWasOverGameAutoByMonitor.Remove(targetToggleMonitor);
                            manualToggleActiveByMonitor.Remove(targetToggleMonitor);

                            // Recompute the compatibility flag without touching any
                            // other monitor's toggle state.
                            manualToggleActive = manualTogglePresetByMonitor.Count > 0;
                            manualTogglePreset = null;
                            manualToggleReturnPreset = null;

                            // Clear the legacy single override only when it belongs to
                            // this same monitor. Never clear another monitor's override.
                            if (string.Equals(manualOverrideMonitor, targetToggleMonitor, StringComparison.OrdinalIgnoreCase))
                            {
                                manualOverrideActive = false;
                                manualOverridePreset = null;
                                manualOverrideProcess = null;
                                manualOverrideReturnPreset = null;
                                manualOverrideMonitor = null;
                            }

                            // DO NOT blindly restore the pre-toggle state and stop here.
                            // If the mapped game is currently foreground on this monitor,
                            // the state underneath the Toggle is Game Auto (for example
                            // 30/30), while manualToggleReturnState is the state that was
                            // visible when the Toggle was first pressed (for example 70/70).
                            // In that case the Toggle OFF action must immediately reveal
                            // Game Auto instead of leaving the monitor at 70/70 until the
                            // next Alt+Tab. EvaluateGameAutoState() performs that layer
                            // transition synchronously. If the game is not foreground,
                            // it leaves the restored/base state alone.
                            // Only reveal Game Auto when it is actually the active layer
                            // on this monitor. IsGameAutoEnabled() merely means the feature
                            // is configured; it does NOT mean this monitor is currently
                            // under Game Auto. If the game is unfocused, Toggle OFF must
                            // restore the exact state captured before the toggle (e.g. 40/40),
                            // not jump to the default/base profile.
                            bool gameAutoOwnsTarget = autoGameFocused &&
                                !string.IsNullOrEmpty(activeAutoMonitor) &&
                                string.Equals(activeAutoMonitor, targetToggleMonitor, StringComparison.OrdinalIgnoreCase);
                            if (gameAutoOwnsTarget)
                            {
                                try { EvaluateGameAutoState(); }
                                catch (Exception ex)
                                { Logger.Warn("Game Auto re-evaluation after Toggle OFF failed: " + ex.Message); }
                            }

                            visiblePreset = GetCurrentPresetForMonitor(targetToggleMonitor);
                        }
                        else
                        {
                            // Capture the configured target monitor state independently. This snapshot can never
                            // be overwritten by Game Auto while the Toggle layer is active.
                            string toggleReturnPreset = GetCurrentPresetForMonitor(targetToggleMonitor);
                            MonitorStateSnapshot toggleReturnState = null;
                            foreach (Display.DisplayInfo d in displays)
                            {
                                if (d != null && string.Equals(d.displayName, targetToggleMonitor, StringComparison.Ordinal))
                                { toggleReturnState = CaptureMonitorState(d); break; }
                            }

                            // IMPORTANT: the toggle must restore the state that was
                            // actually visible at the moment the toggle was pressed.
                            // If Game Auto is currently active on this same monitor, that
                            // means the Game Auto profile is the correct return state. Do
                            // NOT replace it with gameAutoPreviousStateByMonitor here: that
                            // snapshot represents the state BEFORE Game Auto and would make
                            // B toggle OFF jump all the way back to the desktop/default state
                            // instead of returning to A's currently active Game Auto profile.
                            //
                            // This also keeps the layers independent:
                            //   base/default -> Game Auto A -> Toggle B
                            // Toggle B OFF returns to Game Auto A. When Game Auto later leaves
                            // the monitor, its own snapshot restores the original base state.

                            manualToggleReturnStateByMonitor[targetToggleMonitor] = toggleReturnState;

                            bool wasOverGameAuto = gameAutoPreviousStateCaptured &&
                                !string.IsNullOrEmpty(gameAutoSessionProcess) &&
                                gameAutoPreviousStateByMonitor.ContainsKey(targetToggleMonitor);
                            manualToggleWasOverGameAutoByMonitor[targetToggleMonitor] = wasOverGameAuto;

                            // Do NOT use the Toggle return state as the Game Auto base.
                            // The base snapshot must be captured when Game Auto actually
                            // takes control of the game, before any Toggle overlay exists.
                            // A Toggle pressed during Alt+Tab is only an overlay and must
                            // never redefine the real 70/70 base as 40/40 (or 30/30).

                            manualToggleActiveByMonitor[targetToggleMonitor] = true;
                            manualTogglePresetByMonitor[targetToggleMonitor] = capturedPreset;
                            manualToggleReturnPresetByMonitor[targetToggleMonitor] = toggleReturnPreset;
                            manualToggleActive = true;
                            manualTogglePreset = capturedPreset;
                            manualToggleReturnPreset = toggleReturnPreset;

                            // The toggle layer is now the visible layer for the foreground monitor.
                            // Keep the runtime profile map in sync so a later Game Auto
                            // restore cannot accidentally resurrect this toggle after it
                            // has been turned OFF.
                            currentPresetByMonitor[targetToggleMonitor] = capturedPreset;

                            // Legacy override fields are retained only for compatibility;
                            // Game Auto priority is now decided from the per-monitor toggle
                            // dictionary above.
                            manualOverrideActive = true;
                            manualOverridePreset = capturedPreset;
                            manualOverrideProcess = null;
                            manualOverrideReturnPreset = toggleReturnPreset;
                            manualOverrideMonitor = targetToggleMonitor;

                            ApplyPreset(capturedPreset, true, true);

                            // Re-assert the Toggle after the hotkey event has fully returned to
                            // the Windows message loop. Game Auto/window activation events can be
                            // queued at the same time as the hotkey; if one of those events runs
                            // immediately after ApplyPreset(), it can write the Game Auto profile
                            // back over the newly-enabled Toggle. The Toggle remains the top layer,
                            // so one deferred re-apply closes that race without changing OFF logic.
                            try
                            {
                                BeginInvoke((Action)delegate
                                {
                                    string activeToggle;
                                    if (manualTogglePresetByMonitor.TryGetValue(targetToggleMonitor, out activeToggle) &&
                                        string.Equals(activeToggle, capturedPreset, StringComparison.Ordinal))
                                    {
                                        ApplyPreset(capturedPreset, false, true);
                                        currentPresetByMonitor[targetToggleMonitor] = capturedPreset;
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn("Deferred Toggle re-apply failed: " + ex.Message);
                            }
                        }
                    }
                    else
                    {
                        // A normal Apply hotkey cancels only the toggle belonging to
                        // this profile's monitor. Other monitors keep their toggle state.
                        manualTogglePresetByMonitor.Remove(capturedMonitor);
                        manualToggleReturnPresetByMonitor.Remove(capturedMonitor);
                        manualToggleActiveByMonitor.Remove(capturedMonitor);
                        manualToggleActive = manualTogglePresetByMonitor.Count > 0;
                        manualTogglePreset = null;
                        manualToggleReturnPreset = null;

                        string previousPreset = GetCurrentPresetForMonitor(capturedMonitor);
                        if (string.Equals(previousPreset, capturedPreset, StringComparison.Ordinal))
                            previousPreset = null;

                        lastGameAutoAppliedMonitor = null;
                        lastGameAutoAppliedPreset = null;
                        manualOverrideActive = true;
                        manualOverridePreset = capturedPreset;
                        manualOverrideProcess = hasGameMapping ? fg : null;
                        manualOverrideReturnPreset = previousPreset;
                        manualOverrideMonitor = capturedMonitor;

                        ApplyPreset(capturedPreset, true, true);
                    }

                    int visibleIndex = comboBoxPresets.Items.IndexOf(visiblePreset);
                    if (visibleIndex >= 0)
                    {
                        disableChangeFunc = true;
                        comboBoxPresets.SelectedIndex = visibleIndex;
                        disableChangeFunc = false;
                    }
                };
                if (hotkey.Register())
                {
                    globalHotkeys.Add(id, hotkey);
                    globalHotkeyPresets.Add(id, preset);
                }
                else
                {
                    hotkey.Dispose();
                    // Do not interrupt startup. The user can see the setting again
                    // in the Hotkeys window and choose another combination.
                }
            }
        }

        private static bool TryParseHotkey(string value, out Keys key, out GlobalHotkey.Modifiers modifiers)
        {
            key = Keys.None;
            modifiers = GlobalHotkey.Modifiers.None;
            if (string.IsNullOrWhiteSpace(value)) return false;

            string[] parts = value.Split('+');
            string keyText = parts[parts.Length - 1].Trim();
            if (!Enum.TryParse(keyText, true, out key) || key == Keys.None) return false;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl":
                    case "control": modifiers |= GlobalHotkey.Modifiers.Control; break;
                    case "alt": modifiers |= GlobalHotkey.Modifiers.Alt; break;
                    case "shift": modifiers |= GlobalHotkey.Modifiers.Shift; break;
                    case "win":
                    case "windows": modifiers |= GlobalHotkey.Modifiers.Win; break;
                    default: return false;
                }
            }
            return true;
        }
    }
}