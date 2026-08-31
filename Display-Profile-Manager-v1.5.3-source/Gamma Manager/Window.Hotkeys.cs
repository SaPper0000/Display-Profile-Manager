using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Gamma_Manager
{
    // Global hotkey and display reset management.
    public partial class Window : Form
    {
        // 모니터별 토글 상태를 보관하는 독립 구조체
        private sealed class ToggleState
        {
            public string ActivePreset;
            public string ReturnPreset;
            public MonitorStateSnapshot ReturnState;
        }

        // 모니터 고유 키(hardwareId 우선) 기반 Single Source of Truth
        private readonly Dictionary<string, ToggleState> toggleStateByMonitor = new Dictionary<string, ToggleState>(StringComparer.OrdinalIgnoreCase);

        // 전체 OSD 활성 여부 (Window.Actions.cs의 기본값 갱신 알림 등에서 사용)
        private bool IsOSDEnabled()
        {
            try
            {
                string val = iniFile?.Read("OsdEnabled", "Settings");
                return string.IsNullOrEmpty(val) ||
                       val.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                       val == "1";
            }
            catch
            {
                return true;
            }
        }

        // 핫키 / 토글 전용 OSD 활성 여부
        private bool IsHotkeyOSDEnabled()
        {
            try
            {
                if (!IsOSDEnabled()) return false;

                string hkVal = iniFile?.Read("OsdHotkeyEnabled", "Settings");
                return string.IsNullOrEmpty(hkVal) ||
                       hkVal.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                       hkVal == "1";
            }
            catch
            {
                return true;
            }
        }

        private Display.DisplayInfo FindDisplayByKey(string monitorKey)
        {
            if (string.IsNullOrEmpty(monitorKey) || displays == null) return null;
            foreach (var d in displays)
            {
                if (d != null && (string.Equals(d.hardwareId, monitorKey, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(d.displayName, monitorKey, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(d.displayLink, monitorKey, StringComparison.OrdinalIgnoreCase)))
                    return d;
            }
            return null;
        }

        // 핫키 입력 시 메인 UI의 활성 모니터 및 프로필 콤보박스를 해당 모니터로 자동 전환
        private void SyncUIWithTargetMonitorAndPreset(Display.DisplayInfo targetDisplay, string presetName)
        {
            if (targetDisplay == null || isClosing || IsDisposed) return;

            Action action = () =>
            {
                if (isClosing || IsDisposed || displays == null) return;

                int targetIndex = displays.FindIndex(d =>
                    string.Equals(DisplayService.GetMonitorKey(d), DisplayService.GetMonitorKey(targetDisplay), StringComparison.OrdinalIgnoreCase));

                // 1. 모니터 콤보박스를 핫키 대상 모니터로 변경 (슬라이더 및 프로필 목록 자동 갱신)
                if (targetIndex >= 0)
                {
                    if (comboBoxMonitors != null && comboBoxMonitors.SelectedIndex != targetIndex)
                    {
                        comboBoxMonitors.SelectedIndex = targetIndex;
                    }
                    else
                    {
                        fillInfo(targetDisplay);
                    }
                }

                // 2. 프로필 콤보박스 선택값 동기화
                if (comboBoxPresets != null)
                {
                    string visiblePreset = presetName ?? string.Empty;
                    int visibleIndex = comboBoxPresets.Items.IndexOf(visiblePreset);

                    if (visibleIndex < 0 && !string.IsNullOrEmpty(visiblePreset))
                    {
                        string prefix = targetDisplay.displayName + ": ";
                        if (visiblePreset.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            visibleIndex = comboBoxPresets.Items.IndexOf(visiblePreset.Substring(prefix.Length));
                        }
                        else
                        {
                            visibleIndex = comboBoxPresets.Items.IndexOf(prefix + visiblePreset);
                        }
                    }

                    disableChangeFunc = true;
                    if (visibleIndex >= 0)
                    {
                        comboBoxPresets.SelectedIndex = visibleIndex;
                    }
                    else
                    {
                        comboBoxPresets.Text = visiblePreset;
                    }
                    disableChangeFunc = false;
                }
            };

            if (InvokeRequired) BeginInvoke(action);
            else action();
        }

        private MonitorStateSnapshot CaptureMonitorState(Display.DisplayInfo display)
        {
            if (display == null) return null;

            int currentBrightness = display.monitorBrightness;
            int currentContrast = display.monitorContrast;

            string targetKey = DisplayService.GetMonitorKey(display);
            string currKey = DisplayService.GetMonitorKey(currDisplay);

            if (!string.IsNullOrEmpty(targetKey) && string.Equals(targetKey, currKey, StringComparison.OrdinalIgnoreCase))
            {
                if (trackBarMonitorBrightness != null)
                    currentBrightness = trackBarMonitorBrightness.Value;

                if (display.isExternal && trackBarMonitorContrast != null)
                    currentContrast = trackBarMonitorContrast.Value;
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
                monitorBrightness = currentBrightness,
                monitorContrast = currentContrast
            };
        }

        private void RestoreMonitorState(string monitorKey, MonitorStateSnapshot state)
        {
            if (string.IsNullOrEmpty(monitorKey) || state == null) return;
            Display.DisplayInfo display = FindDisplayByKey(monitorKey);
            if (display == null) return;

            string key = DisplayService.GetMonitorKey(display);

            CancelPendingHardwareRead();

            display.rGamma = state.rGamma; display.gGamma = state.gGamma; display.bGamma = state.bGamma;
            display.rContrast = state.rContrast; display.gContrast = state.gContrast; display.bContrast = state.bContrast;
            display.rBright = state.rBright; display.gBright = state.gBright; display.bBright = state.bBright;
            display.saturation = Clamp(state.saturation, display.saturationMin, display.saturationMax);
            display.monitorBrightness = Math.Max(0, Math.Min(100, state.monitorBrightness));
            display.monitorContrast = Math.Max(0, Math.Min(100, state.monitorContrast));
            int restoreGeneration = displayService != null ? displayService.NextGeneration(key) : 0;
            int targetTopologyGen = displayService != null ? displayService.CurrentTopologyGeneration : 0;

            System.Threading.Tasks.Task.Run(() =>
            {
                if (displayService == null) return;

                lock (displayService.SoftwareLock)
                {
                    if (targetTopologyGen != displayService.CurrentTopologyGeneration) return;
                    if (restoreGeneration != displayService.GetCurrentGeneration(key)) return;

                    if (display.saturationSupported)
                    {
                        Saturation.Apply(display, display.saturation);
                    }

                    if (targetTopologyGen != displayService.CurrentTopologyGeneration) return;
                    if (restoreGeneration != displayService.GetCurrentGeneration(key)) return;

                    if (!string.IsNullOrEmpty(display.displayLink))
                    {
                        Gamma.SetGammaRamp(display.displayLink, Gamma.CreateGammaRamp(display.rGamma, display.gGamma, display.bGamma, display.rContrast, display.gContrast, display.bContrast, display.rBright, display.gBright, display.bBright));
                    }
                }
            });

            QueuePhysicalMonitorProfileValues(key, display.monitorBrightness, display.monitorContrast, "Restore", restoreGeneration);
        }

        internal void SuspendGlobalHotkeys()
        {
            if (globalHotkeys != null)
            {
                foreach (GlobalHotkey hotkey in globalHotkeys.Values)
                {
                    try { hotkey.Dispose(); } catch { }
                }
                globalHotkeys.Clear();
            }
            globalHotkeyPresets?.Clear();
            nextHotkeyId = 1;
        }

        internal void ResumeGlobalHotkeys() { RefreshGlobalHotkeys(); }

        private void ClearManualHotkeyStateAndRestoreBase()
        {
            foreach (var kvp in toggleStateByMonitor)
            {
                string monitorKey = kvp.Key;
                ToggleState state = kvp.Value;
                if (state == null) continue;

                if (state.ReturnState != null)
                {
                    RestoreMonitorState(monitorKey, state.ReturnState);
                    if (!string.IsNullOrEmpty(state.ReturnPreset))
                        currentPresetByMonitor[monitorKey] = state.ReturnPreset;
                    else
                        currentPresetByMonitor.Remove(monitorKey);
                }
                else if (!string.IsNullOrEmpty(state.ReturnPreset))
                {
                    ApplyPreset(state.ReturnPreset, false, true);
                }
            }

            toggleStateByMonitor.Clear();

            if (currDisplay != null)
            {
                fillInfo(currDisplay);
            }
        }

        private void ResetMonitorHard(Display.DisplayInfo targetDisplay)
        {
            if (targetDisplay == null) return;

            string monitorKey = DisplayService.GetMonitorKey(targetDisplay);
            int currentGen = displayService != null ? displayService.NextGeneration(monitorKey) : 0;
            int targetTopologyGen = displayService != null ? displayService.CurrentTopologyGeneration : 0;
            toggleStateByMonitor.Remove(monitorKey);

            int origBrightness = targetDisplay.monitorBrightness;
            int origContrast = targetDisplay.monitorContrast;
            if (StartupStateManager.TryGetOriginalValues(targetDisplay.displayLink, out int bVal, out int cVal))
            {
                origBrightness = bVal;
                origContrast = cVal;
            }

            targetDisplay.monitorBrightness = origBrightness;
            targetDisplay.monitorContrast = origContrast;
            targetDisplay.rGamma = 1f; targetDisplay.gGamma = 1f; targetDisplay.bGamma = 1f;
            targetDisplay.rContrast = 1f; targetDisplay.gContrast = 1f; targetDisplay.bContrast = 1f;
            targetDisplay.rBright = 0f; targetDisplay.gBright = 0f; targetDisplay.bBright = 0f;
            if (targetDisplay.saturationSupported) targetDisplay.saturation = targetDisplay.saturationDefault;

            displayService?.QueuePhysicalSettings(displays, monitorKey, origBrightness, origContrast, "HardReset", currentGen);
            SyncUIWithTargetMonitorAndPreset(targetDisplay, string.Empty);

            System.Threading.Tasks.Task.Run(() =>
            {
                if (displayService == null) return;

                lock (displayService.SoftwareLock)
                {
                    if (targetTopologyGen != displayService.CurrentTopologyGeneration) return;
                    if (currentGen != displayService.GetCurrentGeneration(monitorKey)) return;

                    if (targetDisplay.saturationSupported)
                        Saturation.Apply(targetDisplay, targetDisplay.saturationDefault);

                    if (targetTopologyGen != displayService.CurrentTopologyGeneration) return;
                    if (currentGen != displayService.GetCurrentGeneration(monitorKey)) return;

                    if (!string.IsNullOrEmpty(targetDisplay.displayLink))
                    {
                        Gamma.SetGammaRamp(targetDisplay.displayLink, Gamma.CreateGammaRamp(1, 1, 1, 1, 1, 1, 0, 0, 0));
                    }
                }
            });

            currentPresetByMonitor?.Remove(monitorKey);

            if (IsHotkeyOSDEnabled())
                OSDForm.ShowMessage(targetDisplay.displayLink, LanguageManager.Korean ? "🔄 초기화됨" : "🔄 Reset");
        }

        private void ResetAllMonitorsHard()
        {
            if (displays == null) return;
            displayService?.InvalidateAllGenerations();
            toggleStateByMonitor.Clear();

            List<Display.DisplayInfo> targetDisplays = new List<Display.DisplayInfo>(displays);
            var expectedGenerations = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            if (displayService != null)
            {
                foreach (var d in targetDisplays)
                {
                    if (d != null)
                    {
                        string key = DisplayService.GetMonitorKey(d);
                        expectedGenerations[key] = displayService.GetCurrentGeneration(key);
                    }
                }
            }
            int resetAllTopologyGen = displayService != null ? displayService.CurrentTopologyGeneration : 0;

            foreach (Display.DisplayInfo display in targetDisplays)
            {
                if (display == null) continue;

                int origB = display.monitorBrightness;
                int origC = display.monitorContrast;
                if (StartupStateManager.TryGetOriginalValues(display.displayLink, out int bVal, out int cVal))
                {
                    origB = bVal;
                    origC = cVal;
                }
                display.monitorBrightness = origB;
                display.monitorContrast = origC;

                display.rGamma = 1f; display.gGamma = 1f; display.bGamma = 1f;
                display.rContrast = 1f; display.gContrast = 1f; display.bContrast = 1f;
                display.rBright = 0f; display.gBright = 0f; display.bBright = 0f;
                if (display.saturationSupported) display.saturation = display.saturationDefault;

                string key = DisplayService.GetMonitorKey(display);
                currentPresetByMonitor?.Remove(key);

                int expGen = expectedGenerations.TryGetValue(key, out int g) ? g : 0;
                displayService?.QueuePhysicalSettings(displays, key, origB, origC, "HardResetAll", expGen);
            }

            if (currDisplay != null)
            {
                SyncUIWithTargetMonitorAndPreset(currDisplay, string.Empty);
            }

            System.Threading.Tasks.Task.Run(() =>
            {
                if (displayService == null) return;

                lock (displayService.SoftwareLock)
                {
                    if (resetAllTopologyGen != displayService.CurrentTopologyGeneration) return;

                    foreach (Display.DisplayInfo display in targetDisplays)
                    {
                        if (display == null || isClosing) continue;

                        string key = DisplayService.GetMonitorKey(display);
                        if (expectedGenerations.TryGetValue(key, out int expectedGen))
                        {
                            if (expectedGen != displayService.GetCurrentGeneration(key)) continue;
                        }

                        if (display.saturationSupported)
                            Saturation.Apply(display, display.saturationDefault);

                        if (expectedGenerations.TryGetValue(key, out int expGen2))
                        {
                            if (expGen2 != displayService.GetCurrentGeneration(key)) continue;
                        }

                        if (!string.IsNullOrEmpty(display.displayLink))
                        {
                            Gamma.SetGammaRamp(display.displayLink, Gamma.CreateGammaRamp(1, 1, 1, 1, 1, 1, 0, 0, 0));
                        }
                    }
                }
            });

            if (IsHotkeyOSDEnabled())
                OSDForm.ShowMessage(null, LanguageManager.Korean ? "🔄 전체 초기화됨" : "🔄 All Reset");
        }

        private void RefreshGlobalHotkeys()
        {
            SuspendGlobalHotkeys();

            // 1. 전체 초기화 핫키
            string resetAllText = iniFile.Read(HotkeySettingsForm.HARD_RESET_ALL_PRESET, "Hotkeys");
            if (TryParseHotkey(resetAllText, out Keys rKey, out GlobalHotkey.Modifiers rMod))
            {
                int id = nextHotkeyId++;
                GlobalHotkey hk = new GlobalHotkey(this.Handle, id, rKey, rMod);
                hk.Pressed += delegate { ResetAllMonitorsHard(); };
                if (hk.Register())
                {
                    globalHotkeys[id] = hk;
                    globalHotkeyPresets[id] = HotkeySettingsForm.HARD_RESET_ALL_PRESET;
                }
                else { hk.Dispose(); }
            }

            // 2. 모니터별 초기화 & 순환 핫키
            if (displays != null)
            {
                foreach (Display.DisplayInfo display in displays)
                {
                    if (display == null) continue;
                    Display.DisplayInfo targetDisplay = display;
                    string monitorKey = DisplayService.GetMonitorKey(targetDisplay);

                    // 단일 모니터 초기화 핫키
                    string rSingleText = iniFile.Read(HotkeySettingsForm.HARD_RESET_SINGLE_PREFIX + display.displayName, "Hotkeys");
                    if (TryParseHotkey(rSingleText, out Keys rsKey, out GlobalHotkey.Modifiers rsMod))
                    {
                        int id = nextHotkeyId++;
                        GlobalHotkey hk = new GlobalHotkey(this.Handle, id, rsKey, rsMod);
                        hk.Pressed += delegate { ResetMonitorHard(targetDisplay); };
                        if (hk.Register())
                        {
                            globalHotkeys[id] = hk;
                            globalHotkeyPresets[id] = HotkeySettingsForm.HARD_RESET_SINGLE_PREFIX + display.displayName;
                        }
                        else { hk.Dispose(); }
                    }

                    // 순환 핫키
                    string cyclePresetName = HotkeySettingsForm.CYCLE_SINGLE_PREFIX + display.displayName;
                    string cycleText = iniFile.Read(cyclePresetName, "Hotkeys");
                    if (TryParseHotkey(cycleText, out Keys cKey, out GlobalHotkey.Modifiers cMod))
                    {
                        int id = nextHotkeyId++;
                        GlobalHotkey hk = new GlobalHotkey(this.Handle, id, cKey, cMod);
                        hk.Pressed += delegate
                        {
                            List<string> cycleList = new List<string>();
                            string[] sections = iniFile.GetSections();
                            if (sections != null)
                            {
                                foreach (string sec in sections)
                                {
                                    if (string.Equals(sec, "Settings", StringComparison.OrdinalIgnoreCase) ||
                                        string.Equals(sec, "Hotkeys", StringComparison.OrdinalIgnoreCase) ||
                                        sec.StartsWith("AutoGame_", StringComparison.OrdinalIgnoreCase) ||
                                        sec.StartsWith("__", StringComparison.OrdinalIgnoreCase))
                                        continue;

                                    string secHardwareId = iniFile.Read("hardwareId", sec);
                                    string secMonitor = iniFile.Read("monitor", sec);

                                    bool isMatch = (!string.IsNullOrEmpty(secHardwareId) && !string.IsNullOrEmpty(targetDisplay.hardwareId) && string.Equals(secHardwareId, targetDisplay.hardwareId, StringComparison.OrdinalIgnoreCase))
                                                || (!string.IsNullOrEmpty(secMonitor) && string.Equals(secMonitor, targetDisplay.displayName, StringComparison.OrdinalIgnoreCase))
                                                || sec.StartsWith(targetDisplay.displayName + ": ", StringComparison.OrdinalIgnoreCase);

                                    if (isMatch && iniFile.Read("CycleInclude", sec) == "1")
                                    {
                                        cycleList.Add(sec);
                                    }
                                }
                            }

                            if (cycleList.Count == 0)
                            {
                                if (IsHotkeyOSDEnabled())
                                    OSDForm.ShowMessage(targetDisplay.displayLink, LanguageManager.Korean ? "❌ 순환할 프로필 없음" : "❌ No cycle profiles");
                                return;
                            }

                            string current = GetCurrentPresetForMonitor(monitorKey) ?? GetCurrentPresetForMonitor(targetDisplay.displayName);

                            int idx = -1;
                            if (!string.IsNullOrEmpty(current))
                            {
                                idx = cycleList.FindIndex(p => string.Equals(p, current, StringComparison.OrdinalIgnoreCase) ||
                                                               p.EndsWith(": " + current, StringComparison.OrdinalIgnoreCase) ||
                                                               current.EndsWith(": " + p, StringComparison.OrdinalIgnoreCase));
                            }

                            int nextIdx = (idx + 1) % cycleList.Count;
                            string nextPreset = cycleList[nextIdx];

                            ApplyPreset(nextPreset, false, true);
                            currentPresetByMonitor[monitorKey] = nextPreset;

                            string displayPresetName = nextPreset;
                            string prefix = targetDisplay.displayName + ": ";
                            if (displayPresetName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                displayPresetName = displayPresetName.Substring(prefix.Length);

                            SyncUIWithTargetMonitorAndPreset(targetDisplay, displayPresetName);

                            if (IsHotkeyOSDEnabled())
                                OSDForm.ShowMessage(targetDisplay.displayLink, $"🔁 {displayPresetName}");
                        };

                        if (hk.Register())
                        {
                            globalHotkeys[id] = hk;
                            globalHotkeyPresets[id] = cyclePresetName;
                        }
                        else
                        {
                            hk.Dispose();
                        }
                    }
                }
            }

            // 3. 일반 프로필 핫키
            string[] presets = iniFile.GetSections();
            if (presets == null) return;

            foreach (string preset in presets)
            {
                if (string.Equals(preset, "Settings", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(preset, "Hotkeys", StringComparison.OrdinalIgnoreCase) ||
                    preset.StartsWith("AutoGame_", StringComparison.OrdinalIgnoreCase) ||
                    preset.StartsWith("__", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!TryParseHotkey(iniFile.Read(preset, "Hotkeys"), out Keys key, out GlobalHotkey.Modifiers modifiers)) continue;

                int id = nextHotkeyId++;
                GlobalHotkey hotkey = new GlobalHotkey(this.Handle, id, key, modifiers);
                string capturedPreset = preset;
                bool isToggle = string.Equals(iniFile.Read("HotkeyMode", capturedPreset), "Toggle", StringComparison.OrdinalIgnoreCase);

                hotkey.Pressed += delegate
                {
                    string visiblePreset = capturedPreset;
                    string capturedHardwareId = iniFile.Read("hardwareId", capturedPreset);
                    string capturedMonitor = iniFile.Read("monitor", capturedPreset);

                    Display.DisplayInfo targetDisplay = null;
                    if (!string.IsNullOrEmpty(capturedHardwareId))
                        targetDisplay = FindDisplayByKey(capturedHardwareId);
                    if (targetDisplay == null && !string.IsNullOrEmpty(capturedMonitor))
                        targetDisplay = FindDisplayByKey(capturedMonitor);

                    if (targetDisplay == null) return;
                    string monitorKey = DisplayService.GetMonitorKey(targetDisplay);

                    if (isToggle)
                    {
                        if (toggleStateByMonitor.TryGetValue(monitorKey, out ToggleState activeState) &&
                            string.Equals(activeState.ActivePreset, capturedPreset, StringComparison.Ordinal))
                        {
                            // [토글 끄기]
                            displayService?.NextGeneration(monitorKey);
                            toggleStateByMonitor.Remove(monitorKey);

                            bool handledByGameAuto = false;

                            if (IsGameAutoEnabled())
                            {
                                string foregroundExe = GetForegroundProcessName();
                                Display.DisplayInfo fgDisp = GetForegroundDisplay() ?? targetDisplay;
                                bool isGameMapped = TryGetAutoMapping(foregroundExe, fgDisp, out string matchedGameProfile);

                                if (isGameMapped && !string.IsNullOrEmpty(matchedGameProfile))
                                {
                                    // 1. 현재 창이 게임인 경우 -> 게임 프로필로 복귀
                                    ApplyPreset(matchedGameProfile, false, true);
                                    currentPresetByMonitor[monitorKey] = matchedGameProfile;
                                    visiblePreset = matchedGameProfile;
                                    autoGameFocused = true;
                                    activeAutoPreset = matchedGameProfile;
                                    activeAutoMonitor = monitorKey;
                                    activeAutoProcess = foregroundExe;
                                    handledByGameAuto = true;
                                }
                                else if (autoGameFocused || gameAutoPreviousStateCaptured)
                                {
                                    // 2. 게임이 켜진 상태에서 다른 창을 보고 있을 때 토글을 끈 경우 -> 게임 전 기본값으로 복귀
                                    RestoreGameAutoPreviousMonitor(monitorKey);
                                    autoGameFocused = false;
                                    activeAutoPreset = null;
                                    activeAutoMonitor = null;
                                    activeAutoProcess = null;
                                    handledByGameAuto = true;
                                    visiblePreset = GetCurrentPresetForMonitor(monitorKey);
                                }
                            }

                            // 3. 게임과 무관한 일반 상태(1번 상황) -> 토글 켜기 전 프로필(A 프로필)로 복귀
                            if (!handledByGameAuto)
                            {
                                string returnPreset = activeState.ReturnPreset;
                                if (!string.IsNullOrEmpty(returnPreset))
                                {
                                    ApplyPreset(returnPreset, false, true);
                                    currentPresetByMonitor[monitorKey] = returnPreset;
                                    visiblePreset = returnPreset;
                                }
                                else if (activeState.ReturnState != null)
                                {
                                    applyingPreset = true;
                                    try { RestoreMonitorState(monitorKey, activeState.ReturnState); }
                                    finally { applyingPreset = false; }

                                    currentPresetByMonitor.Remove(monitorKey);
                                    visiblePreset = GetCurrentPresetForMonitor(monitorKey);
                                }
                                else
                                {
                                    visiblePreset = GetCurrentPresetForMonitor(monitorKey);
                                }

                                autoGameFocused = false;
                                activeAutoPreset = null;
                                activeAutoMonitor = null;
                                activeAutoProcess = null;
                            }

                            // UI 모니터 및 프로필 자동 전환 (단일 실행)
                            SyncUIWithTargetMonitorAndPreset(targetDisplay, visiblePreset);

                            if (IsHotkeyOSDEnabled())
                            {
                                string osdText;
                                if (handledByGameAuto)
                                {
                                    string gamePresetName = visiblePreset ?? string.Empty;
                                    string prefix = targetDisplay.displayName + ": ";
                                    if (gamePresetName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                        gamePresetName = gamePresetName.Substring(prefix.Length);

                                    osdText = $"🎮 {gamePresetName}";
                                }
                                else
                                {
                                    string cleanName = visiblePreset ?? string.Empty;
                                    string defaultPrefix = LanguageManager.Korean ? "기본값" : "Default";

                                    if (string.IsNullOrWhiteSpace(cleanName) || cleanName.StartsWith(defaultPrefix, StringComparison.OrdinalIgnoreCase))
                                    {
                                        cleanName = LanguageManager.Korean ? "기본값" : "Default";
                                    }
                                    else
                                    {
                                        string prefix = targetDisplay.displayName + ": ";
                                        if (cleanName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                            cleanName = cleanName.Substring(prefix.Length);
                                    }

                                    osdText = $"🔄 {cleanName}";
                                }

                                OSDForm.ShowMessage(targetDisplay.displayLink, osdText);
                            }
                        }
                        else
                        {
                            // [토글 켜기: 현재 상태 스냅샷 저장 후 프로필 적용]
                            string returnPreset = null;
                            MonitorStateSnapshot returnState = null;

                            if (toggleStateByMonitor.TryGetValue(monitorKey, out ToggleState existingToggle) && existingToggle != null)
                            {
                                returnPreset = existingToggle.ReturnPreset;
                                returnState = existingToggle.ReturnState;
                            }
                            else
                            {
                                returnPreset = GetCurrentPresetForMonitor(monitorKey);
                                returnState = CaptureMonitorState(targetDisplay);
                            }

                            toggleStateByMonitor[monitorKey] = new ToggleState
                            {
                                ActivePreset = capturedPreset,
                                ReturnPreset = returnPreset,
                                ReturnState = returnState
                            };

                            currentPresetByMonitor[monitorKey] = capturedPreset;

                            ApplyPreset(capturedPreset, true, true);

                            // UI 모니터 및 프로필 자동 전환
                            SyncUIWithTargetMonitorAndPreset(targetDisplay, capturedPreset);

                            if (IsHotkeyOSDEnabled())
                                OSDForm.ShowMessage(targetDisplay.displayLink, $"🔛 {capturedPreset}");
                        }
                    }
                    else
                    {
                        // 일반 적용 모드
                        toggleStateByMonitor.Remove(monitorKey);

                        ApplyPreset(capturedPreset, true, true);
                        currentPresetByMonitor[monitorKey] = capturedPreset;

                        // UI 모니터 및 프로필 자동 전환
                        SyncUIWithTargetMonitorAndPreset(targetDisplay, capturedPreset);

                        if (IsHotkeyOSDEnabled())
                            OSDForm.ShowMessage(targetDisplay.displayLink, $"⌨️ {capturedPreset}");
                    }
                };

                if (hotkey.Register())
                {
                    globalHotkeys[id] = hotkey;
                    globalHotkeyPresets[id] = preset;
                }
                else
                {
                    hotkey.Dispose();
                }
            }
        }

        private static bool TryParseHotkey(string value, out Keys key, out GlobalHotkey.Modifiers modifiers)
        {
            key = Keys.None; modifiers = GlobalHotkey.Modifiers.None;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string[] parts = value.Split('+');
            string keyText = parts[parts.Length - 1].Trim();
            if (!Enum.TryParse(keyText, true, out key) || key == Keys.None) return false;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl": case "control": modifiers |= GlobalHotkey.Modifiers.Control; break;
                    case "alt": modifiers |= GlobalHotkey.Modifiers.Alt; break;
                    case "shift": modifiers |= GlobalHotkey.Modifiers.Shift; break;
                    case "win": case "windows": modifiers |= GlobalHotkey.Modifiers.Win; break;
                    default: return false;
                }
            }
            return true;
        }
    }
}