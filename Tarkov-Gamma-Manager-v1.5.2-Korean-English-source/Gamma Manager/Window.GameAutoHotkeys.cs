using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Gamma_Manager
{
    // Global hotkey and display reset management.
    public partial class Window : Form
    {
        // 4개로 쪼개져 있던 토글 상태를 하나로 묶는 클래스 정의
        private sealed class ToggleState
        {
            public string ActivePreset;
            public string ReturnPreset;
            public MonitorStateSnapshot ReturnState;
        }

        // 모니터 이름별 토글 상태를 단 하나의 딕셔너리로 관리
        private readonly Dictionary<string, ToggleState> toggleStateByMonitor = new Dictionary<string, ToggleState>(StringComparer.Ordinal);

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

        private string GetDisplayLink(string displayName)
        {
            if (string.IsNullOrEmpty(displayName) || displays == null) return null;
            foreach (var d in displays)
            {
                if (d != null && string.Equals(d.displayName, displayName, StringComparison.OrdinalIgnoreCase))
                    return d.displayLink;
            }
            return null;
        }

        private MonitorStateSnapshot CaptureMonitorState(Display.DisplayInfo display)
        {
            if (display == null) return null;

            int currentBrightness = display.monitorBrightness;
            int currentContrast = display.monitorContrast;

            // 현재 선택된 모니터라면 슬라이더(UI)의 최신 값을 우선적으로 스냅샷에 반영하여 비동기 지연 충돌 방지
            if (currDisplay != null && string.Equals(currDisplay.displayName, display.displayName, StringComparison.Ordinal))
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

        private void RestoreMonitorState(string monitorName, MonitorStateSnapshot state)
        {
            if (string.IsNullOrEmpty(monitorName) || state == null || displays == null) return;
            Display.DisplayInfo display = null;
            foreach (Display.DisplayInfo d in displays)
            {
                if (d != null && string.Equals(d.displayName, monitorName, StringComparison.Ordinal))
                {
                    display = d;
                    break;
                }
            }
            if (display == null) return;

            display.rGamma = state.rGamma; display.gGamma = state.gGamma; display.bGamma = state.bGamma;
            display.rContrast = state.rContrast; display.gContrast = state.gContrast; display.bContrast = state.bContrast;
            display.rBright = state.rBright; display.gBright = state.gBright; display.bBright = state.bBright;
            display.saturation = Clamp(state.saturation, display.saturationMin, display.saturationMax);
            display.monitorBrightness = state.monitorBrightness; display.monitorContrast = state.monitorContrast;

            if (!string.IsNullOrEmpty(display.displayLink))
            {
                Gamma.SetGammaRamp(display.displayLink, Gamma.CreateGammaRamp(display.rGamma, display.gGamma, display.bGamma, display.rContrast, display.gContrast, display.bContrast, display.rBright, display.gBright, display.bBright));
            }
            Saturation.Prepare(display); Saturation.Apply(display, display.saturation);

            int restoreGeneration = displayService != null ? displayService.NextGeneration() : 0;
            int restoreBrightness = Math.Max(0, Math.Min(100, display.monitorBrightness));
            int restoreContrast = Math.Max(0, Math.Min(100, display.monitorContrast));
            QueuePhysicalMonitorProfileValues(monitorName, restoreBrightness, restoreContrast, "Restore", restoreGeneration);

            if (currDisplay != null && string.Equals(currDisplay.displayName, monitorName, StringComparison.Ordinal))
            {
                fillInfo(display);
                disableChangeFunc = true;
                if (trackBarMonitorBrightness != null) trackBarMonitorBrightness.Value = Math.Max(trackBarMonitorBrightness.Minimum, Math.Min(trackBarMonitorBrightness.Maximum, display.monitorBrightness));
                if (display.isExternal && trackBarMonitorContrast != null) trackBarMonitorContrast.Value = Math.Max(trackBarMonitorContrast.Minimum, Math.Min(trackBarMonitorContrast.Maximum, display.monitorContrast));
                disableChangeFunc = false;
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
        }

        internal void ResumeGlobalHotkeys() { RefreshGlobalHotkeys(); }

        private void ClearManualHotkeyStateAndRestoreBase()
        {
            toggleStateByMonitor.Clear();
            manualToggleActive = false;
            manualTogglePreset = null;
            manualToggleReturnPreset = null;

            if (currDisplay != null && !string.IsNullOrEmpty(currDisplay.displayName))
            {
                ResetMonitorHard(currDisplay);
            }
        }

        private void ResetMonitorHard(Display.DisplayInfo targetDisplay)
        {
            if (targetDisplay == null) return;

            int currentGen = displayService != null ? displayService.NextGeneration() : 0;
            toggleStateByMonitor.Remove(targetDisplay.displayName);
            manualToggleActive = toggleStateByMonitor.Count > 0;
            if (!manualToggleActive) { manualTogglePreset = null; manualToggleReturnPreset = null; }

            int origBrightness = targetDisplay.monitorBrightness;
            int origContrast = targetDisplay.monitorContrast;
            if (StartupStateManager.TryGetOriginalValues(targetDisplay.displayLink, out int bVal, out int cVal))
            {
                origBrightness = bVal;
                origContrast = cVal;
            }

            targetDisplay.monitorBrightness = origBrightness;
            targetDisplay.monitorContrast = origContrast;

            System.Threading.Tasks.Task.Run(() =>
            {
                if (displayService == null) return;
                lock (displayService.ApplyLock)
                {
                    if (currentGen != displayService.CurrentGeneration) return;
                    StartupStateManager.RestoreOriginalMonitor(targetDisplay);
                }
            });

            Action updateUI = () =>
            {
                if (currDisplay != null && string.Equals(currDisplay.displayName, targetDisplay.displayName, StringComparison.Ordinal))
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

            if (targetDisplay.saturationSupported) Saturation.Apply(targetDisplay, targetDisplay.saturation);
            if (!string.IsNullOrEmpty(targetDisplay.displayLink))
            {
                Gamma.SetGammaRamp(targetDisplay.displayLink, Gamma.CreateGammaRamp(1, 1, 1, 1, 1, 1, 0, 0, 0));
            }
            currentPresetByMonitor?.Remove(targetDisplay.displayName);

            if (IsOSDEnabled())
                OSDForm.ShowMessage(targetDisplay.displayLink, "🔄 초기화됨");
        }

        private void ResetAllMonitorsHard()
        {
            if (displays == null) return;
            int currentGen = displayService != null ? displayService.NextGeneration() : 0;
            toggleStateByMonitor.Clear();
            manualToggleActive = false;
            manualTogglePreset = null;
            manualToggleReturnPreset = null;

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

                currentPresetByMonitor?.Remove(display.displayName);
            }

            List<Display.DisplayInfo> targetDisplays = new List<Display.DisplayInfo>(displays);
            System.Threading.Tasks.Task.Run(() =>
            {
                if (displayService == null) return;
                lock (displayService.ApplyLock)
                {
                    if (currentGen != displayService.CurrentGeneration) return;
                    StartupStateManager.RestoreOriginalMonitors(targetDisplays);
                }
            });

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

            foreach (Display.DisplayInfo display in displays)
            {
                if (display == null) continue;
                if (display.saturationSupported) Saturation.Apply(display, display.saturation);
                if (!string.IsNullOrEmpty(display.displayLink))
                {
                    Gamma.SetGammaRamp(display.displayLink, Gamma.CreateGammaRamp(1, 1, 1, 1, 1, 1, 0, 0, 0));
                }
            }

            if (IsOSDEnabled())
                OSDForm.ShowMessage(null, "🔄 전체 초기화됨");
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

                    string cyclePresetName = HotkeySettingsForm.CYCLE_SINGLE_PREFIX + display.displayName;
                    string cycleText = iniFile.Read(cyclePresetName, "Hotkeys");
                    if (TryParseHotkey(cycleText, out Keys cKey, out GlobalHotkey.Modifiers cMod))
                    {
                        int id = nextHotkeyId++;
                        GlobalHotkey hk = new GlobalHotkey(this.Handle, id, cKey, cMod);
                        hk.Pressed += delegate {
                            List<string> cycleList = new List<string>();
                            string[] sections = iniFile.GetSections();
                            if (sections != null)
                            {
                                foreach (string sec in sections)
                                {
                                    if (iniFile.Read("monitor", sec) == targetDisplay.displayName && iniFile.Read("CycleInclude", sec) == "1")
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

                            cycleList.Sort(StringComparer.CurrentCultureIgnoreCase);
                            string current = GetCurrentPresetForMonitor(targetDisplay.displayName);

                            int idx = cycleList.IndexOf(current);
                            int nextIdx = (idx + 1) % cycleList.Count;
                            string nextPreset = cycleList[nextIdx];

                            ApplyPreset(nextPreset, false, true);

                            if (IsOSDEnabled())
                                OSDForm.ShowMessage(targetDisplay.displayLink, $"🔁 {nextPreset}");
                        };
                        if (hk.Register())
                        {
                            globalHotkeys[id] = hk;
                            globalHotkeyPresets[id] = cyclePresetName;
                        }
                        else { hk.Dispose(); }
                    }
                }
            }

            // 3. 일반 프로필 핫키 (단일 ToggleState 구조 적용)
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
                    string capturedMonitor = iniFile.Read("monitor", capturedPreset);
                    if (string.IsNullOrEmpty(capturedMonitor)) return;

                    if (isToggle)
                    {
                        // 해당 모니터에 이미 토글이 활성화되어 있는지 확인
                        if (toggleStateByMonitor.TryGetValue(capturedMonitor, out ToggleState activeState) &&
                            string.Equals(activeState.ActivePreset, capturedPreset, StringComparison.Ordinal))
                        {
                            // [토글 끄기: 이전 원본 상태로 복원]
                            displayService?.NextGeneration();

                            if (activeState.ReturnState != null)
                            {
                                applyingPreset = true;
                                try { RestoreMonitorState(capturedMonitor, activeState.ReturnState); }
                                finally { applyingPreset = false; }

                                if (!string.IsNullOrEmpty(activeState.ReturnPreset))
                                    currentPresetByMonitor[capturedMonitor] = activeState.ReturnPreset;
                                else
                                    currentPresetByMonitor.Remove(capturedMonitor);
                            }
                            else if (!string.IsNullOrEmpty(activeState.ReturnPreset))
                            {
                                applyingPreset = true;
                                try { ApplyPreset(activeState.ReturnPreset, false, true); }
                                finally { applyingPreset = false; }
                            }

                            toggleStateByMonitor.Remove(capturedMonitor);
                            manualToggleActive = toggleStateByMonitor.Count > 0;
                            manualTogglePreset = null;
                            manualToggleReturnPreset = null;

                            visiblePreset = GetCurrentPresetForMonitor(capturedMonitor);

                            if (IsOSDEnabled())
                                OSDForm.ShowMessage(GetDisplayLink(capturedMonitor), $"📴 {capturedPreset}");
                        }
                        else
                        {
                            // [토글 켜기: 현재 상태 스냅샷 저장 후 프로필 적용]
                            string returnPreset = null;
                            MonitorStateSnapshot returnState = null;

                            if (toggleStateByMonitor.TryGetValue(capturedMonitor, out ToggleState existingToggle) && existingToggle != null)
                            {
                                returnPreset = existingToggle.ReturnPreset;
                                returnState = existingToggle.ReturnState;
                            }
                            else
                            {
                                returnPreset = GetCurrentPresetForMonitor(capturedMonitor);
                                if (displays != null)
                                {
                                    foreach (Display.DisplayInfo d in displays)
                                    {
                                        if (d != null && string.Equals(d.displayName, capturedMonitor, StringComparison.Ordinal))
                                        {
                                            returnState = CaptureMonitorState(d);
                                            break;
                                        }
                                    }
                                }
                            }

                            toggleStateByMonitor[capturedMonitor] = new ToggleState
                            {
                                ActivePreset = capturedPreset,
                                ReturnPreset = returnPreset,
                                ReturnState = returnState
                            };

                            manualToggleActive = true;
                            manualTogglePreset = capturedPreset;
                            manualToggleReturnPreset = returnPreset;
                            currentPresetByMonitor[capturedMonitor] = capturedPreset;

                            ApplyPreset(capturedPreset, true, true);

                            if (IsOSDEnabled())
                                OSDForm.ShowMessage(GetDisplayLink(capturedMonitor), $"🔛 {capturedPreset}");
                        }
                    }
                    else
                    {
                        // 일반 적용 모드
                        toggleStateByMonitor.Remove(capturedMonitor);
                        manualToggleActive = toggleStateByMonitor.Count > 0;
                        manualTogglePreset = null;
                        manualToggleReturnPreset = null;

                        ApplyPreset(capturedPreset, true, true);

                        if (IsOSDEnabled())
                            OSDForm.ShowMessage(GetDisplayLink(capturedMonitor), $"⌨️ {capturedPreset}");
                    }

                    if (comboBoxPresets != null)
                    {
                        int visibleIndex = comboBoxPresets.Items.IndexOf(visiblePreset);
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