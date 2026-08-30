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

        private bool IsOSDEnabled()
        {
            try
            {
                string val = iniFile?.Read("OsdEnabled", "Settings");
                return val != "False" && val != "0";
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
                                  string.Equals(d.displayName, monitorKey, StringComparison.OrdinalIgnoreCase)))
                    return d;
            }
            return null;
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

            // 1. 복원 전 대기 중이던 비동기 하드웨어 읽기 작업 취소
            CancelPendingHardwareRead();

            display.rGamma = state.rGamma; display.gGamma = state.gGamma; display.bGamma = state.bGamma;
            display.rContrast = state.rContrast; display.gContrast = state.gContrast; display.bContrast = state.bContrast;
            display.rBright = state.rBright; display.gBright = state.gBright; display.bBright = state.bBright;
            display.saturation = Clamp(state.saturation, display.saturationMin, display.saturationMax);
            display.monitorBrightness = Math.Max(0, Math.Min(100, state.monitorBrightness));
            display.monitorContrast = Math.Max(0, Math.Min(100, state.monitorContrast));
            int restoreGeneration = displayService != null ? displayService.NextGeneration(key) : 0;
            int targetTopologyGen = displayService != null ? displayService.CurrentTopologyGeneration : 0;

            // GPU 감마 및 채도 복구 비동기 위임 (소프트웨어 직렬 락으로 보호)
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

            string currKey = DisplayService.GetMonitorKey(currDisplay);
            if (!string.IsNullOrEmpty(key) && string.Equals(currKey, key, StringComparison.OrdinalIgnoreCase))
            {
                // 2. UI 슬라이더와 텍스트 박스를 복원 목표치로 직접 확정 반영 (Read 역류 차단)
                disableChangeFunc = true;
                try
                {
                    if (trackBarGamma != null) trackBarGamma.Value = Clamp((int)(display.rGamma * 100f), trackBarGamma.Minimum, trackBarGamma.Maximum);
                    if (trackBarContrast != null) trackBarContrast.Value = Clamp((int)(display.rContrast * 100f), trackBarContrast.Minimum, trackBarContrast.Maximum);
                    if (trackBarBrightness != null) trackBarBrightness.Value = Clamp((int)(display.rBright * 100f), trackBarBrightness.Minimum, trackBarBrightness.Maximum);
                    if (trackBarSaturation != null && display.saturationSupported)
                        trackBarSaturation.Value = Clamp(display.saturation, trackBarSaturation.Minimum, trackBarSaturation.Maximum);

                    if (trackBarMonitorBrightness != null)
                        trackBarMonitorBrightness.Value = Clamp(display.monitorBrightness, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                    if (display.isExternal && trackBarMonitorContrast != null)
                        trackBarMonitorContrast.Value = Clamp(display.monitorContrast, trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum);

                    RefreshNumericBoxes();
                }
                finally
                {
                    disableChangeFunc = false;
                }
            }
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
            nextHotkeyId = 1; // 👈 핫키 전체 해제 시 ID 카운터를 1로 초기화하여 무한 증가 방지
        }

        internal void ResumeGlobalHotkeys() { RefreshGlobalHotkeys(); }

        private void ClearManualHotkeyStateAndRestoreBase()
        {
            // 활성화되어 있던 모든 모니터의 토글 상태를 각자의 원래 상태로 안전하게 복구
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

            // DDC/CI 하드웨어 복원 백그라운드 작업
            System.Threading.Tasks.Task.Run(() =>
            {
                if (displayService == null) return;
                lock (displayService.ApplyLock)
                {
                    if (currentGen != displayService.GetCurrentGeneration(monitorKey)) return;
                    StartupStateManager.RestoreOriginalMonitor(targetDisplay, () => currentGen == displayService.GetCurrentGeneration(monitorKey));
                }
            });

            // UI 슬라이더 및 텍스트 0ms 즉시 리셋
            Action updateUI = () =>
            {
                string currKey = DisplayService.GetMonitorKey(currDisplay);
                if (string.Equals(currKey, monitorKey, StringComparison.OrdinalIgnoreCase))
                {
                    disableChangeFunc = true;
                    try
                    {
                        currDisplay.rGamma = 1f; currDisplay.gGamma = 1f; currDisplay.bGamma = 1f;
                        currDisplay.rContrast = 1f; currDisplay.gContrast = 1f; currDisplay.bContrast = 1f;
                        currDisplay.rBright = 0f; currDisplay.gBright = 0f; currDisplay.bBright = 0f;
                        if (trackBarGamma != null) trackBarGamma.Value = 100;
                        if (trackBarContrast != null) trackBarContrast.Value = 100;
                        if (trackBarBrightness != null) trackBarBrightness.Value = 0;

                        if (trackBarMonitorBrightness != null) trackBarMonitorBrightness.Value = Clamp(currDisplay.monitorBrightness, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                        if (textBoxMonitorBrightness != null) textBoxMonitorBrightness.Text = currDisplay.monitorBrightness.ToString();

                        if (currDisplay.isExternal)
                        {
                            if (trackBarMonitorContrast != null) trackBarMonitorContrast.Value = Clamp(currDisplay.monitorContrast, trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum);
                            if (textBoxMonitorContrast != null) textBoxMonitorContrast.Text = currDisplay.monitorContrast.ToString();
                        }

                        if (currDisplay.saturationSupported)
                        {
                            currDisplay.saturation = currDisplay.saturationDefault;
                            if (trackBarSaturation != null) trackBarSaturation.Value = Clamp(currDisplay.saturationDefault, trackBarSaturation.Minimum, trackBarSaturation.Maximum);
                            if (textBoxSaturation != null) textBoxSaturation.Text = currDisplay.saturation.ToString();
                        }
                        if (comboBoxPresets != null) comboBoxPresets.Text = string.Empty;
                    }
                    finally { disableChangeFunc = false; }
                    RefreshNumericBoxes();
                }
                else
                {
                    targetDisplay.rGamma = 1f; targetDisplay.gGamma = 1f; targetDisplay.bGamma = 1f;
                    targetDisplay.rContrast = 1f; targetDisplay.gContrast = 1f; targetDisplay.bContrast = 1f;
                    targetDisplay.rBright = 0f; targetDisplay.gBright = 0f; targetDisplay.bBright = 0f;
                    if (targetDisplay.saturationSupported) targetDisplay.saturation = targetDisplay.saturationDefault;
                }
            };

            if (this.InvokeRequired) this.BeginInvoke(updateUI);
            else updateUI();

            int targetTopologyGen = displayService != null ? displayService.CurrentTopologyGeneration : 0;
            // GPU 감마 및 채도 복구를 백그라운드로 실행 (소프트웨어 직렬 락으로 보호)
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

            if (IsOSDEnabled())
                OSDForm.ShowMessage(targetDisplay.displayLink, LanguageManager.Korean ? "🔄 초기화됨" : "🔄 Reset");
        }

        private void ResetAllMonitorsHard()
        {
            if (displays == null) return;
            displayService?.InvalidateAllGenerations();
            toggleStateByMonitor.Clear();

            List<Display.DisplayInfo> targetDisplays = new List<Display.DisplayInfo>(displays);

            // 1. 전체 모니터 세대 번호 스냅샷 사전 캡처 (CS0841 해결)
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

            foreach (Display.DisplayInfo display in displays)
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
            }

            // 2. DDC/CI 하드웨어 복원 백그라운드 작업
            System.Threading.Tasks.Task.Run(() =>
            {
                if (displayService == null) return;
                lock (displayService.ApplyLock)
                {
                    if (resetAllTopologyGen != displayService.CurrentTopologyGeneration) return;
                    StartupStateManager.RestoreOriginalMonitors(targetDisplays, () =>
                    {
                        if (isClosing || resetAllTopologyGen != displayService.CurrentTopologyGeneration) return false;
                        foreach (var d in targetDisplays)
                        {
                            if (d == null) continue;
                            string k = DisplayService.GetMonitorKey(d);
                            if (expectedGenerations.TryGetValue(k, out int expGen) && expGen != displayService.GetCurrentGeneration(k))
                                return false;
                        }
                        return true;
                    });
                }
            });

            // 3. UI 0ms 즉시 리셋
            Action updateUI = () =>
            {
                if (currDisplay != null)
                {
                    disableChangeFunc = true;
                    try
                    {
                        if (trackBarGamma != null) trackBarGamma.Value = 100;
                        if (trackBarContrast != null) trackBarContrast.Value = 100;
                        if (trackBarBrightness != null) trackBarBrightness.Value = 0;

                        if (trackBarMonitorBrightness != null) trackBarMonitorBrightness.Value = Clamp(currDisplay.monitorBrightness, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                        if (textBoxMonitorBrightness != null) textBoxMonitorBrightness.Text = currDisplay.monitorBrightness.ToString();

                        if (currDisplay.isExternal)
                        {
                            if (trackBarMonitorContrast != null) trackBarMonitorContrast.Value = Clamp(currDisplay.monitorContrast, trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum);
                            if (textBoxMonitorContrast != null) textBoxMonitorContrast.Text = currDisplay.monitorContrast.ToString();
                        }
                        else
                        {
                            if (textBoxMonitorBrightness != null) textBoxMonitorBrightness.Text = currDisplay.monitorBrightness.ToString();
                        }

                        if (currDisplay.saturationSupported)
                        {
                            if (trackBarSaturation != null) trackBarSaturation.Value = Clamp(currDisplay.saturationDefault, trackBarSaturation.Minimum, trackBarSaturation.Maximum);
                            if (textBoxSaturation != null) textBoxSaturation.Text = currDisplay.saturation.ToString();
                        }
                        if (comboBoxPresets != null) comboBoxPresets.Text = string.Empty;
                    }
                    finally { disableChangeFunc = false; }
                    RefreshNumericBoxes();
                }

                initPresets();
                initTrayMenu();
            };

            if (this.InvokeRequired) this.BeginInvoke(updateUI);
            else updateUI();

            // 4. 전체 모니터 GPU 감마 및 채도 복구를 백그라운드로 실행 (소프트웨어 직렬 락으로 보호)
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

            if (IsOSDEnabled())
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
                                        string.Equals(sec, "Hotkeys", StringComparison.OrdinalIgnoreCase))
                                        continue;

                                    string secHardwareId = iniFile.Read("hardwareId", sec);
                                    string secMonitor = iniFile.Read("monitor", sec);

                                    // hardwareId 우선 매칭 -> displayName 매칭
                                    bool isMatch = (!string.IsNullOrEmpty(secHardwareId) && !string.IsNullOrEmpty(targetDisplay.hardwareId) && string.Equals(secHardwareId, targetDisplay.hardwareId, StringComparison.OrdinalIgnoreCase))
                                                || (!string.IsNullOrEmpty(secMonitor) && string.Equals(secMonitor, targetDisplay.displayName, StringComparison.OrdinalIgnoreCase))
                                                || sec.StartsWith(targetDisplay.displayName + ": ", StringComparison.OrdinalIgnoreCase);

                                    // 👈 사용자가 체크박스(CycleInclude=1)를 켠 프로필만 정확하게 추가
                                    if (isMatch && iniFile.Read("CycleInclude", sec) == "1")
                                    {
                                        cycleList.Add(sec);
                                    }
                                }
                            }

                            if (cycleList.Count == 0)
                            {
                                if (IsOSDEnabled())
                                    OSDForm.ShowMessage(targetDisplay.displayLink, LanguageManager.Korean ? "❌ 순환할 프로필 없음" : "❌ No cycle profiles");
                                return;
                            }

                            // monitorKey 및 displayName 기반 현재 프로필 양방향 완벽 검색
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

                            // switchMonitor=false로 현재 보고 있는 창 유지한 채 해당 대상 모니터에 적용
                            ApplyPreset(nextPreset, false, true);

                            // 현재 순환된 프로필 상태를 단일 Source of Truth(monitorKey)에 기록
                            currentPresetByMonitor[monitorKey] = nextPreset;

                            string displayPresetName = nextPreset;
                            string prefix = targetDisplay.displayName + ": ";
                            if (displayPresetName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                displayPresetName = displayPresetName.Substring(prefix.Length);

                            if (IsOSDEnabled())
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

            // 3. 일반 프로필 핫키 (단일 Source of Truth: hardwareId 기반 monitorKey)
            string[] presets = iniFile.GetSections();
            if (presets == null) return;

            foreach (string preset in presets)
            {
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
                            // [토글 끄기: 이전 원본 상태로 복원]
                            displayService?.NextGeneration(monitorKey);

                            if (activeState.ReturnState != null)
                            {
                                applyingPreset = true;
                                try { RestoreMonitorState(monitorKey, activeState.ReturnState); }
                                finally { applyingPreset = false; }

                                if (!string.IsNullOrEmpty(activeState.ReturnPreset))
                                    currentPresetByMonitor[monitorKey] = activeState.ReturnPreset;
                                else
                                    currentPresetByMonitor.Remove(monitorKey);
                            }
                            else if (!string.IsNullOrEmpty(activeState.ReturnPreset))
                            {
                                applyingPreset = true;
                                try { ApplyPreset(activeState.ReturnPreset, false, true); }
                                finally { applyingPreset = false; }
                            }

                            toggleStateByMonitor.Remove(monitorKey);
                            visiblePreset = GetCurrentPresetForMonitor(monitorKey);

                            if (IsOSDEnabled())
                                OSDForm.ShowMessage(targetDisplay.displayLink, $"📴 {capturedPreset}");
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

                            if (IsOSDEnabled())
                                OSDForm.ShowMessage(targetDisplay.displayLink, $"🔛 {capturedPreset}");
                        }
                    }
                    else
                    {
                        // 일반 적용 모드
                        toggleStateByMonitor.Remove(monitorKey);

                        ApplyPreset(capturedPreset, true, true);
                        currentPresetByMonitor[monitorKey] = capturedPreset;

                        if (IsOSDEnabled())
                            OSDForm.ShowMessage(targetDisplay.displayLink, $"⌨️ {capturedPreset}");
                    }

                    if (comboBoxPresets != null)
                    {
                        int visibleIndex = comboBoxPresets.Items.IndexOf(visiblePreset);
                        if (visibleIndex < 0 && targetDisplay != null)
                        {
                            string prefix = targetDisplay.displayName + ": ";
                            if (visiblePreset.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            {
                                visibleIndex = comboBoxPresets.Items.IndexOf(visiblePreset.Substring(prefix.Length));
                            }
                        }

                        if (visibleIndex >= 0)
                        {
                            disableChangeFunc = true;
                            comboBoxPresets.SelectedIndex = visibleIndex;
                            disableChangeFunc = false;
                        }
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