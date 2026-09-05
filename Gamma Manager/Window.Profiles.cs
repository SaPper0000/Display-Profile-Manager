using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Gamma_Manager
{
    public partial class Window : Form
    {
        private void InitializeCurrentPresetState()
        {
            currentPresetByMonitor.Clear();
            string prefix = LanguageManager.Korean ? "기본값 - " : "Default - ";
            if (displays == null) return;

            foreach (Display.DisplayInfo display in displays)
            {
                if (display == null) continue;
                string monitorKey = DisplayService.GetMonitorKey(display);
                if (string.IsNullOrEmpty(monitorKey)) continue;

                string defaultPreset = prefix + display.displayName;
                if (!string.IsNullOrEmpty(iniFile.Read("monitor", defaultPreset)))
                {
                    currentPresetByMonitor[monitorKey] = defaultPreset;

                    // INI에 저장되어 있는 [기본값] 프로필 수치를 모니터 객체에 즉시 동기화
                    display.rGamma = ReadProfileFloat("rGamma", defaultPreset, display.rGamma);
                    display.gGamma = ReadProfileFloat("gGamma", defaultPreset, display.gGamma);
                    display.bGamma = ReadProfileFloat("bGamma", defaultPreset, display.bGamma);
                    display.rContrast = ReadProfileFloat("rContrast", defaultPreset, display.rContrast);
                    display.gContrast = ReadProfileFloat("gContrast", defaultPreset, display.gContrast);
                    display.bContrast = ReadProfileFloat("bContrast", defaultPreset, display.bContrast);
                    display.rBright = ReadProfileFloat("rBright", defaultPreset, display.rBright);
                    display.gBright = ReadProfileFloat("gBright", defaultPreset, display.gBright);
                    display.bBright = ReadProfileFloat("bBright", defaultPreset, display.bBright);

                    if (int.TryParse(iniFile.Read("saturation", defaultPreset), out int sat))
                        display.saturation = Clamp(sat, display.saturationMin, display.saturationMax);

                    if (int.TryParse(iniFile.Read("monitorBrightness", defaultPreset), out int mb))
                        display.monitorBrightness = Clamp(mb, 0, 100);

                    if (int.TryParse(iniFile.Read("monitorContrast", defaultPreset), out int mc))
                        display.monitorContrast = Clamp(mc, 0, 100);

                    if (int.TryParse(iniFile.Read("shadowBoost", defaultPreset), out int sb))
                        display.shadowBoost = Clamp(sb, 0, 100);

                    if (int.TryParse(iniFile.Read("shadowBoostMode", defaultPreset), out int sbm))
                        display.shadowBoostMode = Clamp(sbm, 0, 2);
                }
            }
        }

        private string GetCurrentPresetForMonitor(string monitorKey)
        {
            if (string.IsNullOrEmpty(monitorKey)) return null;

            if (currentPresetByMonitor.TryGetValue(monitorKey, out string preset) && !string.IsNullOrEmpty(preset))
                return preset;

            Display.DisplayInfo d = FindDisplayByKey(monitorKey);
            string monitorName = d != null ? d.displayName : monitorKey;

            string prefix = LanguageManager.Korean ? "기본값 - " : "Default - ";
            string defaultPreset = prefix + monitorName;
            if (!string.IsNullOrEmpty(iniFile.Read("monitor", defaultPreset)))
            {
                currentPresetByMonitor[monitorKey] = defaultPreset;
                return defaultPreset;
            }
            return null;
        }

        private void MarkCurrentMonitorAsCustom()
        {
            if (disableChangeFunc || applyingPreset || currDisplay == null) return;
            string key = DisplayService.GetMonitorKey(currDisplay);
            if (!string.IsNullOrEmpty(key))
            {
                currentPresetByMonitor.Remove(key);
            }

            if (comboBoxPresets != null && (comboBoxPresets.SelectedIndex != -1 || !string.IsNullOrEmpty(comboBoxPresets.Text)))
            {
                disableChangeFunc = true;
                try
                {
                    comboBoxPresets.SelectedIndex = -1;
                    comboBoxPresets.Text = string.Empty;
                }
                finally
                {
                    disableChangeFunc = false;
                }
            }
        }

        /// <summary>
        /// v1.5.3-2 이전 프로필에 monitorKey가 없으면 현재 연결된 모니터와 안전하게 매칭하여 보강한다.
        /// 동일 hardwareId가 여러 개면 임의의 모니터를 선택하지 않고 monitor(displayName)가 유일하게 맞을 때만 저장한다.
        /// </summary>
        private void MigrateLegacyProfileMonitorKeys()
        {
            if (displays == null || displays.Count == 0) return;

            string[] sections = iniFile.GetSections();
            if (sections == null) return;

            foreach (string section in sections)
            {
                if (string.Equals(section, "Settings", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(section, "Hotkeys", StringComparison.OrdinalIgnoreCase) ||
                    section.StartsWith("AutoGame_", StringComparison.OrdinalIgnoreCase) ||
                    section.StartsWith("__", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!string.IsNullOrEmpty(iniFile.Read("monitorKey", section)))
                    continue;

                string monitorName = iniFile.Read("monitor", section);
                string hardwareId = iniFile.Read("hardwareId", section);
                Display.DisplayInfo target = null;

                // 1순위: hardwareId. 동일 모델 2대에서도 displayName보다 안전하다.
                if (!string.IsNullOrEmpty(hardwareId))
                {
                    List<Display.DisplayInfo> hwMatches = displays
                        .Where(d => d != null && string.Equals(d.hardwareId, hardwareId, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (hwMatches.Count == 1)
                        target = hwMatches[0];
                }

                // 2순위: 기존 profile에 저장된 표시명. 현재 displayName 및 원본 모델명 모두 지원한다.
                if (target == null && !string.IsNullOrEmpty(monitorName))
                {
                    List<Display.DisplayInfo> nameMatches = displays
                        .Where(d => d != null &&
                                    (string.Equals(d.displayName, monitorName, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(d.baseDisplayName, monitorName, StringComparison.OrdinalIgnoreCase)))
                        .ToList();
                    if (nameMatches.Count == 1)
                        target = nameMatches[0];
                }

                if (target != null)
                {
                    string key = DisplayService.GetMonitorKey(target);
                    if (!string.IsNullOrEmpty(key))
                        iniFile.Write("monitorKey", key, section);
                }
            }

            iniFile.Flush();
        }

        private float ReadProfileFloat(string key, string preset, float fallback)
        {
            string raw = iniFile.Read(key, preset);

            if (string.IsNullOrWhiteSpace(raw))
                return fallback;

            // 쉼표 → 점 변환 (한국 Windows 대비)
            string normalized = raw.Trim().Replace(',', '.');

            if (float.TryParse(
                normalized,
                NumberStyles.Float | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out float value))
            {
                // 감마 값이 너무 이상하면 경고
                if (key.EndsWith("Gamma", StringComparison.OrdinalIgnoreCase))
                {
                    if (value < 0.1f || value > 10.0f)
                    {
                        Logger.Warn(
                            "Profile gamma value out of expected range. " +
                            "Profile=" + preset + ", Key=" + key + ", Value=" + value);
                    }
                }

                return value;
            }

            Logger.Warn("Invalid profile float. Profile=" + preset + ", Key=" + key + ", Value=" + raw);

            return fallback;
        }

        private void EnsureDefaultProfile()
        {
            if (displays == null) return;
            string prefix = LanguageManager.Korean ? "기본값 - " : "Default - ";

            foreach (Display.DisplayInfo display in displays)
            {
                if (display == null || string.IsNullOrEmpty(display.displayName))
                    continue;

                string defaultName = prefix + display.displayName;
                if (!string.IsNullOrEmpty(iniFile.Read("monitor", defaultName)))
                    continue;

                int bVal = display.monitorBrightness;
                int cVal = display.monitorContrast;
                if (StartupStateManager.TryGetOriginalValues(display.displayLink, out int origB, out int origC))
                {
                    bVal = origB;
                    cVal = origC;
                }

                iniFile.Write("monitor", display.displayName, defaultName);
                if (!string.IsNullOrEmpty(display.hardwareId))
                {
                    iniFile.Write("hardwareId", display.hardwareId, defaultName);
                }
                if (!string.IsNullOrEmpty(display.monitorKey))
                {
                    iniFile.Write("monitorKey", DisplayService.GetMonitorKey(display), defaultName);
                }
                iniFile.Write("rGamma", display.rGamma.ToString("0.00", CultureInfo.InvariantCulture), defaultName);
                iniFile.Write("gGamma", display.gGamma.ToString("0.00", CultureInfo.InvariantCulture), defaultName);
                iniFile.Write("bGamma", display.bGamma.ToString("0.00", CultureInfo.InvariantCulture), defaultName);
                iniFile.Write("rContrast", display.rContrast.ToString("0.00", CultureInfo.InvariantCulture), defaultName);
                iniFile.Write("gContrast", display.gContrast.ToString("0.00", CultureInfo.InvariantCulture), defaultName);
                iniFile.Write("bContrast", display.bContrast.ToString("0.00", CultureInfo.InvariantCulture), defaultName);
                iniFile.Write("rBright", display.rBright.ToString("0.00", CultureInfo.InvariantCulture), defaultName);
                iniFile.Write("gBright", display.gBright.ToString("0.00", CultureInfo.InvariantCulture), defaultName);
                iniFile.Write("bBright", display.bBright.ToString("0.00", CultureInfo.InvariantCulture), defaultName);
                iniFile.Write("saturation", display.saturation.ToString(), defaultName);
                iniFile.Write("monitorBrightness", bVal.ToString(CultureInfo.InvariantCulture), defaultName);
                iniFile.Write("monitorContrast", cVal.ToString(CultureInfo.InvariantCulture), defaultName);
            }
        }

        private void SyncMonitorSettingsForProfileSave()
        {
            if (currDisplay == null) return;

            currDisplay.monitorBrightness = Clamp(trackBarMonitorBrightness.Value,
                trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);

            if (currDisplay.isExternal)
            {
                currDisplay.monitorContrast = Clamp(trackBarMonitorContrast.Value,
                    trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum);
            }

            disableChangeFunc = true;
            try
            {
                textBoxMonitorBrightness.Value = Math.Max(textBoxMonitorBrightness.Minimum, Math.Min(textBoxMonitorBrightness.Maximum, (decimal)currDisplay.monitorBrightness));
                if (currDisplay.isExternal)
                    textBoxMonitorContrast.Value = Math.Max(textBoxMonitorContrast.Minimum, Math.Min(textBoxMonitorContrast.Maximum, (decimal)currDisplay.monitorContrast));
            }
            finally
            {
                disableChangeFunc = false;
            }
        }

        private void buttonSave_Click(object sender, EventArgs e)
        {
            if (currDisplay == null) return;

            SyncMonitorSettingsForProfileSave();
            int savedMonitorBrightness = Clamp(trackBarMonitorBrightness.Value,
                trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
            int savedMonitorContrast = currDisplay.isExternal
                ? Clamp(trackBarMonitorContrast.Value, trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum)
                : currDisplay.monitorContrast;

            string targetName = comboBoxPresets.Text.Trim();
            if (string.IsNullOrEmpty(targetName)) return;

            string prefix = currDisplay.displayName + ": ";
            if (targetName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                targetName = targetName.Substring(prefix.Length).Trim();
            }

            string fullProfileName = prefix + targetName;

            string[] existingSections = iniFile.GetSections();
            if (existingSections != null && Array.Exists(existingSections, s => s.Equals(fullProfileName, StringComparison.OrdinalIgnoreCase)))
            {
                DialogResult result = MessageBox.Show(
                    LanguageManager.Korean
                        ? $"'{targetName}' 프로필이 이미 존재합니다.\r\n덮어쓰시겠습니까?"
                        : $"Profile '{targetName}' already exists.\r\nDo you want to overwrite it?",
                    LanguageManager.Korean ? "프로필 덮어쓰기" : "Overwrite Profile",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (result != DialogResult.Yes)
                {
                    return;
                }
            }

            iniFile.Write("monitor", currDisplay.displayName, fullProfileName);
            if (!string.IsNullOrEmpty(currDisplay.hardwareId))
            {
                iniFile.Write("hardwareId", currDisplay.hardwareId, fullProfileName);
            }
            if (!string.IsNullOrEmpty(currDisplay.monitorKey))
            {
                iniFile.Write("monitorKey", DisplayService.GetMonitorKey(currDisplay), fullProfileName);
            }
            iniFile.Write("rGamma", currDisplay.rGamma.ToString("0.00", CultureInfo.InvariantCulture), fullProfileName);
            iniFile.Write("gGamma", currDisplay.gGamma.ToString("0.00", CultureInfo.InvariantCulture), fullProfileName);
            iniFile.Write("bGamma", currDisplay.bGamma.ToString("0.00", CultureInfo.InvariantCulture), fullProfileName);
            iniFile.Write("rContrast", currDisplay.rContrast.ToString("0.00", CultureInfo.InvariantCulture), fullProfileName);
            iniFile.Write("gContrast", currDisplay.gContrast.ToString("0.00", CultureInfo.InvariantCulture), fullProfileName);
            iniFile.Write("bContrast", currDisplay.bContrast.ToString("0.00", CultureInfo.InvariantCulture), fullProfileName);
            iniFile.Write("rBright", currDisplay.rBright.ToString("0.00", CultureInfo.InvariantCulture), fullProfileName);
            iniFile.Write("gBright", currDisplay.gBright.ToString("0.00", CultureInfo.InvariantCulture), fullProfileName);
            iniFile.Write("bBright", currDisplay.bBright.ToString("0.00", CultureInfo.InvariantCulture), fullProfileName);
            iniFile.Write("saturation", currDisplay.saturation.ToString(), fullProfileName);
            iniFile.Write("shadowBoost", currDisplay.shadowBoost.ToString(), fullProfileName);
            iniFile.Write("shadowBoostMode", currDisplay.shadowBoostMode.ToString(), fullProfileName);
            iniFile.Write("monitorBrightness", savedMonitorBrightness.ToString(CultureInfo.InvariantCulture), fullProfileName);
            iniFile.Write("monitorContrast", savedMonitorContrast.ToString(CultureInfo.InvariantCulture), fullProfileName);

            iniFile.Write("LastProfile_" + currDisplay.displayName, fullProfileName, "Settings");
            iniFile.Flush();

            // 저장한 프로필을 현재 모니터의 활성 프로필 상태로 즉시 등록
            string monitorKey = DisplayService.GetMonitorKey(currDisplay);
            if (!string.IsNullOrEmpty(monitorKey))
            {
                currentPresetByMonitor[monitorKey] = fullProfileName;
            }

            initPresets(fullProfileName);
            initTrayMenu();
        }

        private void buttonDelete_Click(object sender, EventArgs e)
        {
            using (ProfileManagerForm form = new ProfileManagerForm(iniFile, displays))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    ClearManualHotkeyStateAndRestoreBase();
                    RefreshGlobalHotkeys();
                    initPresets();
                    initTrayMenu();
                }
            }
        }

        private void buttonReset_Click(object sender, EventArgs e)
        {
            if (currDisplay == null) return;

            string key = DisplayService.GetMonitorKey(currDisplay);
            if (!string.IsNullOrEmpty(key))
            {
                currentPresetByMonitor.Remove(key);
                toggleStateByMonitor.Remove(key);
            }

            ResetMonitorHard(currDisplay);
            initPresets(string.Empty);
            initTrayMenu();
        }

        private void buttonHide_Click(object sender, EventArgs e)
        {
            if (checkBoxTopMost != null && checkBoxTopMost.Checked)
            {
                Hide();
            }
            else
            {
                WindowState = FormWindowState.Minimized;
            }
        }

        private void comboBoxMonitors_SelectedIndexChanged(object sender, EventArgs e)
        {
            // 👈 프로필 적용 중 코드에 의해 인덱스가 변경될 때 재진입 방지
            if (disableChangeFunc) return;

            if (comboBoxMonitors.SelectedIndex < 0 || displays == null || displays.Count == 0) return;

            int targetIndex = comboBoxMonitors.SelectedIndex;
            if (targetIndex >= 0 && targetIndex < displays.Count)
            {
                numDisplay = targetIndex;
                currDisplay = displays[numDisplay];
                fillInfo(currDisplay);
                Saturation.Apply(currDisplay, currDisplay.saturation);

                initPresets();
                UpdateShadowBoostButtonState();
            }
        }

        private void checkBoxTopMost_CheckedChanged(object sender, EventArgs e)
        {
            bool enabled = checkBoxTopMost != null && checkBoxTopMost.Checked;
            TopMost = enabled;
            if (buttonHide != null)
            {
                buttonHide.Text = enabled
                    ? (LanguageManager.Korean ? "숨기기" : "Hide")
                    : (LanguageManager.Korean ? "내리기" : "Minimize");
            }
            if (iniFile != null)
                iniFile.Write("TopMost", enabled ? "True" : "False", "Settings");
        }

        private void checkBoxMonitorEnabled_CheckedChanged(object sender, EventArgs e)
        {
            bool enabled = checkBoxMonitorEnabled.Checked;
            trackBarMonitorBrightness.Enabled = enabled;
            trackBarMonitorContrast.Enabled = enabled && currDisplay != null && currDisplay.isExternal;

            textBoxMonitorBrightness.Enabled = true;
            textBoxMonitorContrast.Enabled = currDisplay != null && currDisplay.isExternal;
            buttonReset.Enabled = true;

            if (enabled && currDisplay != null)
            {
                int targetBrightness = Clamp(trackBarMonitorBrightness.Value, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                int targetContrast = currDisplay.isExternal
                    ? Clamp(trackBarMonitorContrast.Value, trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum)
                    : currDisplay.monitorContrast;

                // 👈 DisplayService.GetMonitorKey로 통일하여 세대 발급 및 큐잉 전달
                string monitorKey = DisplayService.GetMonitorKey(currDisplay);
                int thisApplyGeneration = displayService != null ? displayService.NextGeneration(monitorKey) : 0;
                QueuePhysicalMonitorProfileValues(monitorKey, targetBrightness, targetContrast, "MonitorEnabledSync", thisApplyGeneration);
            }
        }

        private void buttonForward_Click(object sender, EventArgs e)
        {
            if (displays == null || displays.Count == 0) return;

            if (numDisplay + 1 < displays.Count)
            {
                comboBoxMonitors.SelectedIndex = numDisplay + 1;
            }
            else
            {
                comboBoxMonitors.SelectedIndex = 0;
            }
        }

        private void comboBoxPresets_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!disableChangeFunc && !applyingPreset && comboBoxPresets.SelectedIndex >= 0)
            {
                string preset = comboBoxPresets.SelectedItem as string;
                if (!string.IsNullOrEmpty(preset))
                {
                    string defaultLabelKo = "기본값";
                    string defaultLabelEn = "Default";
                    if (string.Equals(preset, defaultLabelKo, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(preset, defaultLabelEn, StringComparison.OrdinalIgnoreCase))
                    {
                        if (currDisplay != null)
                        {
                            string key = DisplayService.GetMonitorKey(currDisplay);
                            if (!string.IsNullOrEmpty(key))
                            {
                                currentPresetByMonitor.Remove(key);
                                toggleStateByMonitor.Remove(key);
                            }
                            ResetMonitorHard(currDisplay);
                            disableChangeFunc = true;
                            try
                            {
                                comboBoxPresets.SelectedIndex = 0;
                            }
                            finally
                            {
                                disableChangeFunc = false;
                            }
                        }
                    }
                    else
                    {
                        ApplyPreset(preset, false);
                    }
                }
            }
        }

        private void ApplyPhysicalMonitorProfileValues(Display.DisplayInfo display, int brightness, int contrast, string profileName)
        {
            displayService?.ApplyPhysicalDirect(display, brightness, contrast, profileName);
        }

        private void QueuePhysicalMonitorProfileValues(string monitorKey, int brightness, int contrast, string profileName, int generation)
        {
            displayService?.QueuePhysicalSettings(displays, monitorKey, brightness, contrast, profileName, generation);
        }

        internal void ApplyPreset(string preset)
        {
            ApplyPreset(preset, true, true);
        }

        internal void ApplyPreset(string preset, Display.DisplayInfo targetDisplay)
        {
            if (targetDisplay != null && displays != null)
            {
                int idx = displays.IndexOf(targetDisplay);
                if (idx >= 0)
                {
                    numDisplay = idx;
                    currDisplay = targetDisplay;
                }
            }
            ApplyPreset(preset, true, true);
        }

        private void ApplyPreset(string preset, bool switchMonitor = true, bool applyMonitorSettings = true)
        {
            if (string.IsNullOrEmpty(preset) || displays == null) return;

            applyingPreset = true;
            disableChangeFunc = true;
            try
            {
                string actualSection = preset;
                string targetMonitorKey = iniFile.Read("monitorKey", actualSection);
                string targetHardwareId = iniFile.Read("hardwareId", actualSection);
                string monitorName = iniFile.Read("monitor", actualSection);

                if (string.IsNullOrEmpty(targetMonitorKey) && string.IsNullOrEmpty(monitorName) && string.IsNullOrEmpty(targetHardwareId) && currDisplay != null)
                {
                    string candidate = currDisplay.displayName + ": " + preset;
                    string baseCandidate = (currDisplay.baseDisplayName ?? currDisplay.displayName) + ": " + preset;
                    string candidateSection = candidate;
                    if (string.IsNullOrEmpty(iniFile.Read("monitor", candidate)) &&
                        string.IsNullOrEmpty(iniFile.Read("hardwareId", candidate)) &&
                        string.IsNullOrEmpty(iniFile.Read("monitorKey", candidate)) &&
                        !string.Equals(baseCandidate, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        candidateSection = baseCandidate;
                    }
                    if (!string.IsNullOrEmpty(iniFile.Read("monitor", candidateSection)) || !string.IsNullOrEmpty(iniFile.Read("hardwareId", candidateSection)) || !string.IsNullOrEmpty(iniFile.Read("monitorKey", candidateSection)))
                    {
                        actualSection = candidateSection;
                        targetMonitorKey = iniFile.Read("monitorKey", actualSection);
                        targetHardwareId = iniFile.Read("hardwareId", actualSection);
                        monitorName = iniFile.Read("monitor", actualSection);
                    }
                }

                if (string.IsNullOrEmpty(targetMonitorKey) && string.IsNullOrEmpty(monitorName) && string.IsNullOrEmpty(targetHardwareId)) return;

                int displayIndex = -1;

                // 1순위: EDID/PNP 기반 monitorKey
                if (!string.IsNullOrEmpty(targetMonitorKey))
                {
                    for (int i = 0; i < displays.Count; i++)
                    {
                        if (displays[i] != null && string.Equals(DisplayService.GetMonitorKey(displays[i]), targetMonitorKey, StringComparison.OrdinalIgnoreCase))
                        {
                            displayIndex = i;
                            break;
                        }
                    }
                }

                // 2순위: 구버전 hardwareId는 현재 목록에서 유일할 때만 사용
                if (displayIndex < 0 && !string.IsNullOrEmpty(targetHardwareId))
                {
                    int candidateIndex = -1;
                    int matches = 0;
                    for (int i = 0; i < displays.Count; i++)
                    {
                        if (displays[i] != null && string.Equals(displays[i].hardwareId, targetHardwareId, StringComparison.OrdinalIgnoreCase))
                        {
                            candidateIndex = i;
                            matches++;
                        }
                    }
                    if (matches == 1) displayIndex = candidateIndex;
                }

                // 3순위 (하위 호환 Fallback): displayName 매칭
                if (displayIndex < 0 && !string.IsNullOrEmpty(monitorName))
                {
                    for (int i = 0; i < displays.Count; i++)
                    {
                        if (displays[i] != null && displays[i].displayName.Equals(monitorName, StringComparison.OrdinalIgnoreCase))
                        {
                            displayIndex = i;
                            break;
                        }
                    }
                }

                if (displayIndex < 0) return;

                Display.DisplayInfo targetDisplay = displays[displayIndex];
                string monitorKey = DisplayService.GetMonitorKey(targetDisplay);

                int thisApplyGeneration = displayService != null ? displayService.NextGeneration(monitorKey) : 0;

                currentPresetByMonitor[monitorKey] = actualSection;
                iniFile.Write("LastProfile_" + monitorName, actualSection, "Settings");

                if (switchMonitor)
                {
                    numDisplay = displayIndex;
                    currDisplay = displays[displayIndex];
                    if (comboBoxMonitors != null && comboBoxMonitors.Items.Count > displayIndex)
                        comboBoxMonitors.SelectedIndex = displayIndex;
                }
                else
                {
                    currDisplay = displays[displayIndex];
                    numDisplay = displayIndex;
                }

                if (currDisplay == null) return;

                currDisplay.rGamma = ReadProfileFloat("rGamma", actualSection, currDisplay.rGamma);
                currDisplay.gGamma = ReadProfileFloat("gGamma", actualSection, currDisplay.gGamma);
                currDisplay.bGamma = ReadProfileFloat("bGamma", actualSection, currDisplay.bGamma);
                currDisplay.rContrast = ReadProfileFloat("rContrast", actualSection, currDisplay.rContrast);
                currDisplay.gContrast = ReadProfileFloat("gContrast", actualSection, currDisplay.gContrast);
                currDisplay.bContrast = ReadProfileFloat("bContrast", actualSection, currDisplay.bContrast);
                currDisplay.rBright = ReadProfileFloat("rBright", actualSection, currDisplay.rBright);
                currDisplay.gBright = ReadProfileFloat("gBright", actualSection, currDisplay.gBright);
                currDisplay.bBright = ReadProfileFloat("bBright", actualSection, currDisplay.bBright);

                string saturationText = iniFile.Read("saturation", actualSection);
                if (!int.TryParse(saturationText, out int parsedSaturation))
                    parsedSaturation = currDisplay.saturationDefault;
                currDisplay.saturation = Clamp(parsedSaturation, currDisplay.saturationMin, currDisplay.saturationMax);

                string shadowBoostText = iniFile.Read("shadowBoost", actualSection);
                if (!int.TryParse(shadowBoostText, out int parsedShadowBoost))
                    parsedShadowBoost = 0;
                currDisplay.shadowBoost = Clamp(parsedShadowBoost, 0, 100);

                string shadowBoostModeText = iniFile.Read("shadowBoostMode", actualSection);
                if (!int.TryParse(shadowBoostModeText, out int parsedShadowBoostMode))
                    parsedShadowBoostMode = 0;
                currDisplay.shadowBoostMode = Clamp(parsedShadowBoostMode, 0, 2);

                if (!int.TryParse(iniFile.Read("monitorBrightness", actualSection), out int storedMonitorBrightness))
                    storedMonitorBrightness = currDisplay.monitorBrightness;
                if (!int.TryParse(iniFile.Read("monitorContrast", actualSection), out int storedMonitorContrast))
                    storedMonitorContrast = currDisplay.monitorContrast;

                currDisplay.monitorBrightness = Clamp(storedMonitorBrightness, 0, 100);
                currDisplay.monitorContrast = Clamp(storedMonitorContrast, 0, 100);

                fillInfo(currDisplay);
                clearColors();
                allColors = true;
                UpdateShadowBoostButtonState();

                if (comboBoxPresets != null)
                {
                    int presetIndex = comboBoxPresets.Items.IndexOf(preset);
                    if (presetIndex < 0)
                    {
                        string cleanName = actualSection;
                        string prefix = currDisplay.displayName + ": ";
                        if (cleanName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            cleanName = cleanName.Substring(prefix.Length);
                        presetIndex = comboBoxPresets.Items.IndexOf(cleanName);
                    }

                    if (presetIndex >= 0) comboBoxPresets.SelectedIndex = presetIndex;
                    else comboBoxPresets.Text = preset;
                }

                // 프로필 적용 시작 시 세대 번호를 올려 이전 리셋/복원 비동기 작업을 즉시 무효화
                if (displayService != null)
                {
                    thisApplyGeneration = displayService.NextGeneration(monitorKey);
                }

                int targetTopologyGen = displayService != null ? displayService.CurrentTopologyGeneration : 0;

                // 1. 소프트웨어 락 안에서 감마 즉시 적용 (리셋 비동기 작업과의 경합 차단)
                bool softOk = false;
                if (displayService != null)
                {
                    lock (displayService.SoftwareLock)
                    {
                        softOk = displayService.ApplyGammaOnly(targetDisplay);
                    }
                }

                // 2. GPU 드라이버 API(채도)는 세대 검증 후 백그라운드 태스크로 분리 (UI 끊김 원천 차단)
                if (targetDisplay.saturationSupported)
                {
                    int satValue = targetDisplay.saturation;
                    Display.DisplayInfo satDisplay = targetDisplay;
                    string satKey = monitorKey;
                    int satGen = thisApplyGeneration;

                    System.Threading.Tasks.Task.Run(() =>
                    {
                        if (displayService != null)
                        {
                            if (targetTopologyGen != displayService.CurrentTopologyGeneration) return;
                            if (satGen != displayService.GetCurrentGeneration(satKey)) return;
                        }

                        Saturation.Apply(satDisplay, satValue);
                    });
                }

                if (applyMonitorSettings)
                {
                    int safeBrightness = Math.Max(0, Math.Min(100, targetDisplay.monitorBrightness));
                    int safeContrast = Math.Max(0, Math.Min(100, targetDisplay.monitorContrast));

                    if (currDisplay == targetDisplay)
                    {
                        if (trackBarMonitorBrightness != null)
                            trackBarMonitorBrightness.Value = Math.Max(trackBarMonitorBrightness.Minimum, Math.Min(trackBarMonitorBrightness.Maximum, safeBrightness));
                        if (targetDisplay.isExternal && trackBarMonitorContrast != null)
                            trackBarMonitorContrast.Value = Math.Max(trackBarMonitorContrast.Minimum, Math.Min(trackBarMonitorContrast.Maximum, safeContrast));
                    }

                    string physicalProfileName = actualSection;
                    QueuePhysicalMonitorProfileValues(monitorKey, safeBrightness, safeContrast, physicalProfileName, thisApplyGeneration);
                }

                // 👈 프로필 적용 시점에 단발성 INFO 로그 기록
                Logger.Info($"Profile applied. Monitor={targetDisplay.displayName}, Profile={preset}");
            }
            finally
            {
                applyingPreset = false;
                disableChangeFunc = false;
            }
        }
    }
}