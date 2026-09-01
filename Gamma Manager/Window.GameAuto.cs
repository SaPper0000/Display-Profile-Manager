using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Gamma_Manager
{
    public partial class Window : Form
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref uint lpdwSize);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private IntPtr gameAutoWinEventHook = IntPtr.Zero;
        private WinApi.WinEventDelegate gameAutoWinEventDelegate = null;
        private IntPtr lastForegroundHwnd = IntPtr.Zero;

        private bool autoGameFocused = false;
        private string activeAutoPreset = null;
        private string activeAutoMonitor = null;
        private string activeAutoProcess = null;
        private string gameAutoSessionProcess = null;

        private readonly Dictionary<string, string> gameAutoPreviousPresetByMonitor = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MonitorStateSnapshot> gameAutoPreviousStateByMonitor = new Dictionary<string, MonitorStateSnapshot>(StringComparer.OrdinalIgnoreCase);
        private bool gameAutoPreviousStateCaptured = false;
        private bool gameAutoEvaluationPending = false;

        public void SetupGameAutoHook()
        {
            if (gameAutoWinEventHook != IntPtr.Zero) return;
            if (!IsGameAutoEnabled()) return;

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
                Logger.Warn("SetWinEventHook failed for Game Auto.");
            else
                Logger.Info("Game Auto foreground-window hook registered.");

            EvaluateGameAutoState();
        }

        public void CleanupGameAutoHook()
        {
            if (gameAutoWinEventHook != IntPtr.Zero)
            {
                WinApi.UnhookWinEvent(gameAutoWinEventHook);
                gameAutoWinEventHook = IntPtr.Zero;
            }
            gameAutoWinEventDelegate = null;
        }

        public bool IsGameAutoEnabled()
        {
            string value = iniFile?.Read("AutoGameEnabled", "Settings");
            return value == "1" || string.Equals(value, "True", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsGameAutoOSDEnabled()
        {
            try
            {
                string mainVal = iniFile?.Read("OsdEnabled", "Settings");
                bool mainEnabled = string.IsNullOrEmpty(mainVal) ||
                                   mainVal.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                                   mainVal == "1";
                if (!mainEnabled) return false;

                string gaVal = iniFile?.Read("OsdGameAutoEnabled", "Settings");
                return string.IsNullOrEmpty(gaVal) ||
                       gaVal.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                       gaVal == "1";
            }
            catch
            {
                return true;
            }
        }

        internal void SetGameAutoEnabledFromSettings(bool enabled)
        {
            iniFile.Write("AutoGameEnabled", enabled ? "1" : "0", "Settings");

            if (enabled)
            {
                SetupGameAutoHook();
                UpdateGameAutoButtonState();
                return;
            }

            CleanupGameAutoHook();

            if (autoGameFocused && !string.IsNullOrEmpty(activeAutoMonitor))
                RestoreGameAutoPreviousMonitor(activeAutoMonitor);

            autoGameFocused = false;
            activeAutoPreset = null;
            activeAutoMonitor = null;
            activeAutoProcess = null;
            gameAutoSessionProcess = null;
            gameAutoPreviousPresetByMonitor.Clear();
            gameAutoPreviousStateByMonitor.Clear();
            gameAutoPreviousStateCaptured = false;

            UpdateGameAutoButtonState();
        }

        private void GameAutoWinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint idEventThread, uint msEventTime)
        {
            if (eventType != WinApi.EVENT_SYSTEM_FOREGROUND) return;
            if (idObject != WinApi.OBJID_WINDOW || idChild != 0) return;
            if (hwnd == lastForegroundHwnd && hwnd != IntPtr.Zero) return;
            lastForegroundHwnd = hwnd;

            if (gameAutoEvaluationPending || isClosing || IsDisposed || !IsHandleCreated) return;
            gameAutoEvaluationPending = true;

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    gameAutoEvaluationPending = false;
                    if (!isClosing && !IsDisposed)
                    {
                        EvaluateGameAutoState();
                    }
                });
            }
            catch
            {
                gameAutoEvaluationPending = false;
            }
        }

        private string GetForegroundProcessName()
        {
            IntPtr hwnd = WinApi.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;

            WinApi.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return null;

            IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    uint capacity = 1024;
                    StringBuilder sb = new StringBuilder((int)capacity);
                    if (QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
                    {
                        return Path.GetFileName(sb.ToString());
                    }
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }

            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                {
                    return p.ProcessName + ".exe";
                }
            }
            catch
            {
                return null;
            }
        }

        private Display.DisplayInfo GetForegroundDisplay()
        {
            IntPtr hwnd = WinApi.GetForegroundWindow();
            if (hwnd == IntPtr.Zero || displays == null || displays.Count == 0) return null;

            try
            {
                string deviceName = Screen.FromHandle(hwnd).DeviceName;
                if (string.IsNullOrEmpty(deviceName)) return null;

                foreach (var d in displays)
                {
                    if (d != null && string.Equals(d.displayLink, deviceName, StringComparison.OrdinalIgnoreCase))
                        return d;
                }
            }
            catch { }
            return null;
        }

        private bool TryGetAutoMapping(string foregroundExe, Display.DisplayInfo fgDisplay, out string profile)
        {
            profile = null;
            if (string.IsNullOrEmpty(foregroundExe) || !IsGameAutoEnabled()) return false;

            string[] sections = iniFile.GetSections();
            if (sections == null) return false;

            string fgMonitorKey = fgDisplay != null ? DisplayService.GetMonitorKey(fgDisplay) : string.Empty;
            string fgHardwareId = fgDisplay?.hardwareId ?? string.Empty;
            string fgDisplayName = fgDisplay?.displayName ?? string.Empty;
            string fgBaseDisplayName = fgDisplay?.baseDisplayName ?? fgDisplayName;
            bool hasDuplicateModel = displays != null && fgDisplay != null && displays.FindAll(d => d != null &&
                string.Equals(d.baseDisplayName ?? d.displayName, fgBaseDisplayName, StringComparison.OrdinalIgnoreCase)).Count > 1;

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
                    string mappedMonitorKey = iniFile.Read("monitorKey", p);
                    string mappedHwId = iniFile.Read("hardwareId", p);
                    string mappedMon = iniFile.Read("monitor", p);

                    if (fgDisplay != null && (!string.IsNullOrEmpty(mappedMonitorKey) || !string.IsNullOrEmpty(mappedHwId) || !string.IsNullOrEmpty(mappedMon)))
                    {
                        bool matchKey = !string.IsNullOrEmpty(mappedMonitorKey) && !string.IsNullOrEmpty(fgMonitorKey) &&
                                         string.Equals(mappedMonitorKey, fgMonitorKey, StringComparison.OrdinalIgnoreCase);
                        bool matchHwId = !string.IsNullOrEmpty(mappedHwId) && !string.IsNullOrEmpty(fgHardwareId) &&
                                         string.Equals(mappedHwId, fgHardwareId, StringComparison.OrdinalIgnoreCase);

                        bool matchName = false;
                        if (!string.IsNullOrEmpty(mappedMon) && !string.IsNullOrEmpty(fgDisplayName))
                        {
                            if (hasDuplicateModel)
                            {
                                matchName = string.Equals(mappedMon, fgDisplayName, StringComparison.OrdinalIgnoreCase);
                            }
                            else
                            {
                                matchName = string.Equals(mappedMon, fgDisplayName, StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(mappedMon, fgBaseDisplayName, StringComparison.OrdinalIgnoreCase);
                            }
                        }

                        // New profiles require exact stable key. Legacy profiles may use HW/name fallback.
                        if (!string.IsNullOrEmpty(mappedMonitorKey))
                        {
                            if (!matchKey) continue;
                        }
                        else if (!matchHwId && !matchName)
                        {
                            continue;
                        }
                    }

                    profile = p;
                    return true;
                }
            }
            return false;
        }

        private void CaptureGameAutoPreviousState()
        {
            gameAutoPreviousPresetByMonitor.Clear();
            gameAutoPreviousStateByMonitor.Clear();

            if (displays == null) return;
            foreach (var display in displays)
            {
                if (display == null) continue;
                string key = DisplayService.GetMonitorKey(display);
                string preset = GetCurrentPresetForMonitor(key);
                if (!string.IsNullOrEmpty(preset)) gameAutoPreviousPresetByMonitor[key] = preset;
                gameAutoPreviousStateByMonitor[key] = CaptureMonitorState(display);
            }
            gameAutoPreviousStateCaptured = true;
        }

        private void RestoreGameAutoPreviousMonitor(string monitorKey)
        {
            if (string.IsNullOrEmpty(monitorKey)) return;

            Display.DisplayInfo targetDisplay = FindDisplayByKey(monitorKey);
            string restoredPreset = null;

            // 1순위: 핫키 토글 활성 상태 확인 (핫키 우선순위 보호)
            if (toggleStateByMonitor.TryGetValue(monitorKey, out ToggleState toggle) && toggle != null && !string.IsNullOrEmpty(toggle.ActivePreset))
            {
                ApplyPreset(toggle.ActivePreset, false, true);
                currentPresetByMonitor[monitorKey] = toggle.ActivePreset;
                restoredPreset = toggle.ActivePreset;
            }
            else if (gameAutoPreviousStateCaptured)
            {
                if (gameAutoPreviousStateByMonitor.TryGetValue(monitorKey, out MonitorStateSnapshot state) && state != null)
                {
                    applyingPreset = true;
                    try { RestoreMonitorState(monitorKey, state); }
                    finally { applyingPreset = false; }

                    if (gameAutoPreviousPresetByMonitor.TryGetValue(monitorKey, out string rp) && !string.IsNullOrEmpty(rp))
                    {
                        currentPresetByMonitor[monitorKey] = rp;
                        restoredPreset = rp;
                    }
                    else
                    {
                        currentPresetByMonitor.Remove(monitorKey);
                        restoredPreset = GetCurrentPresetForMonitor(monitorKey);
                    }
                }
                else if (gameAutoPreviousPresetByMonitor.TryGetValue(monitorKey, out string preset) && !string.IsNullOrEmpty(preset))
                {
                    applyingPreset = true;
                    try { ApplyPreset(preset, false, true); }
                    finally { applyingPreset = false; }
                    restoredPreset = preset;
                }
            }
            else
            {
                restoredPreset = GetCurrentPresetForMonitor(monitorKey);
            }

            // 복원 시 메인 폼 UI(모니터, 슬라이더, 프로필 콤보박스) 즉시 동기화
            if (targetDisplay != null)
            {
                SyncUIWithTargetMonitorAndPreset(targetDisplay, restoredPreset);
            }
        }

        private bool IsProcessRunning(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return false;
            try
            {
                string name = Path.GetFileNameWithoutExtension(exeName);
                Process[] procs = Process.GetProcessesByName(name);
                bool exists = procs.Length > 0;
                foreach (Process p in procs)
                {
                    p.Dispose();
                }
                return exists;
            }
            catch
            {
                return false;
            }
        }

        public void EvaluateGameAutoState()
        {
            if (isClosing || IsDisposed || Disposing || applyingPreset || displays == null || displays.Count == 0) return;
            if (!IsGameAutoEnabled()) return;

            string foreground = GetForegroundProcessName();
            Display.DisplayInfo fgDisplay = GetForegroundDisplay() ?? currDisplay;
            string fgMonitorKey = fgDisplay != null ? DisplayService.GetMonitorKey(fgDisplay) : string.Empty;

            // 게임 프로세스가 종료되었을 때 세션 정리 전 복원 보장
            if (!string.IsNullOrEmpty(gameAutoSessionProcess) && !IsProcessRunning(gameAutoSessionProcess))
            {
                if (autoGameFocused && !string.IsNullOrEmpty(activeAutoMonitor))
                {
                    RestoreGameAutoPreviousMonitor(activeAutoMonitor);
                    autoGameFocused = false;
                }
                activeAutoPreset = null;
                activeAutoMonitor = null;
                activeAutoProcess = null;
                gameAutoSessionProcess = null;
                gameAutoPreviousPresetByMonitor.Clear();
                gameAutoPreviousStateByMonitor.Clear();
                gameAutoPreviousStateCaptured = false;
            }

            bool mapped = TryGetAutoMapping(foreground, fgDisplay, out string matchedProfile);

            // 핫키/토글 활성 여부 검사 (핫키 우선)
            bool toggleActiveOnFg = !string.IsNullOrEmpty(fgMonitorKey) &&
                                    toggleStateByMonitor.TryGetValue(fgMonitorKey, out ToggleState tState) &&
                                    tState != null && !string.IsNullOrEmpty(tState.ActivePreset);

            if (mapped && toggleActiveOnFg)
            {
                autoGameFocused = true;
                activeAutoMonitor = fgMonitorKey;
                activeAutoProcess = foreground;
                return;
            }

            if (mapped)
            {
                if (!autoGameFocused && !gameAutoPreviousStateCaptured)
                {
                    CaptureGameAutoPreviousState();
                    gameAutoSessionProcess = foreground;
                }
                else if (!string.IsNullOrEmpty(activeAutoMonitor) && !string.Equals(activeAutoMonitor, fgMonitorKey, StringComparison.OrdinalIgnoreCase))
                {
                    RestoreGameAutoPreviousMonitor(activeAutoMonitor);
                    autoGameFocused = false;
                }

                if (!autoGameFocused || !string.Equals(activeAutoPreset, matchedProfile, StringComparison.Ordinal) ||
                      !string.Equals(activeAutoMonitor, fgMonitorKey, StringComparison.OrdinalIgnoreCase))
                {
                    activeAutoPreset = matchedProfile;
                    activeAutoMonitor = fgMonitorKey;
                    activeAutoProcess = foreground;
                    ApplyPreset(matchedProfile, false, true);

                    // 게임 프로필이 적용되었음을 모니터 상태에 동기화
                    if (!string.IsNullOrEmpty(fgMonitorKey))
                        currentPresetByMonitor[fgMonitorKey] = matchedProfile;

                    // 게임 프로필 적용 시 메인 UI 동기화
                    if (fgDisplay != null)
                    {
                        SyncUIWithTargetMonitorAndPreset(fgDisplay, matchedProfile);

                        // 게임 자동 전환 OSD 알림 표시 (개별 설정 반영)
                        if (IsGameAutoOSDEnabled())
                        {
                            string displayGameName = matchedProfile;
                            string prefix = fgDisplay.displayName + ": ";
                            if (displayGameName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                displayGameName = displayGameName.Substring(prefix.Length);

                            OSDForm.ShowMessage(fgDisplay.displayLink, $"🎮 {displayGameName}");
                        }
                    }
                }
                autoGameFocused = true;
            }
            else if (autoGameFocused)
            {
                if (!string.IsNullOrEmpty(activeAutoMonitor))
                    RestoreGameAutoPreviousMonitor(activeAutoMonitor);

                autoGameFocused = false;
                activeAutoPreset = null;
                activeAutoMonitor = null;
                activeAutoProcess = null;
            }
        }
    }
}