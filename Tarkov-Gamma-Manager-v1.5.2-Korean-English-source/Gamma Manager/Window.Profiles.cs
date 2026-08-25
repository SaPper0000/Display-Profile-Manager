using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace Gamma_Manager
{
    // Profile selection, save/delete/reset, monitor-profile state, and preset application.
    public partial class Window : Form
    {
        private void InitializeCurrentPresetState()
        {
            currentPresetByMonitor.Clear();
            string prefix = LanguageManager.Korean ? "기본값 - " : "Default - ";
            if (displays == null) return;

            foreach (Display.DisplayInfo display in displays)
            {
                if (display == null || string.IsNullOrEmpty(display.displayName)) continue;
                string defaultPreset = prefix + display.displayName;
                if (!string.IsNullOrEmpty(iniFile.Read("monitor", defaultPreset)))
                    currentPresetByMonitor[display.displayName] = defaultPreset;
            }
        }

        private string GetCurrentPresetForMonitor(string monitorName)
        {
            if (string.IsNullOrEmpty(monitorName)) return null;

            string preset;
            if (currentPresetByMonitor.TryGetValue(monitorName, out preset) && !string.IsNullOrEmpty(preset))
                return preset;

            string prefix = LanguageManager.Korean ? "기본값 - " : "Default - ";
            string defaultPreset = prefix + monitorName;
            if (!string.IsNullOrEmpty(iniFile.Read("monitor", defaultPreset)))
            {
                currentPresetByMonitor[monitorName] = defaultPreset;
                return defaultPreset;
            }
            return null;
        }

        private void MarkCurrentMonitorAsCustom()
        {
            if (disableChangeFunc || applyingPreset || currDisplay == null || string.IsNullOrEmpty(currDisplay.displayName)) return;
            currentPresetByMonitor.Remove(currDisplay.displayName);
        }

        private float ReadProfileFloat(string key, string preset, float fallback)
        {
            string raw = iniFile.Read(key, preset);
            if (string.IsNullOrWhiteSpace(raw)) return fallback;

            // 콤마(,)를 점(.)으로 치환한 후 표준 InvariantCulture로만 파싱
            string normalized = raw.Trim().Replace(',', '.');
            if (float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return value;

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
                iniFile.Write("rGamma", display.rGamma.ToString(customCulture), defaultName);
                iniFile.Write("gGamma", display.gGamma.ToString(customCulture), defaultName);
                iniFile.Write("bGamma", display.bGamma.ToString(customCulture), defaultName);
                iniFile.Write("rContrast", display.rContrast.ToString(customCulture), defaultName);
                iniFile.Write("gContrast", display.gContrast.ToString(customCulture), defaultName);
                iniFile.Write("bContrast", display.bContrast.ToString(customCulture), defaultName);
                iniFile.Write("rBright", display.rBright.ToString(customCulture), defaultName);
                iniFile.Write("gBright", display.gBright.ToString(customCulture), defaultName);
                iniFile.Write("bBright", display.bBright.ToString(customCulture), defaultName);
                iniFile.Write("saturation", display.saturation.ToString(), defaultName);
                iniFile.Write("monitorBrightness", bVal.ToString(customCulture), defaultName);
                iniFile.Write("monitorContrast", cVal.ToString(customCulture), defaultName);
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
            iniFile.Write("rGamma", currDisplay.rGamma.ToString(customCulture), fullProfileName);
            iniFile.Write("gGamma", currDisplay.gGamma.ToString(customCulture), fullProfileName);
            iniFile.Write("bGamma", currDisplay.bGamma.ToString(customCulture), fullProfileName);
            iniFile.Write("rContrast", currDisplay.rContrast.ToString(customCulture), fullProfileName);
            iniFile.Write("gContrast", currDisplay.gContrast.ToString(customCulture), fullProfileName);
            iniFile.Write("bContrast", currDisplay.bContrast.ToString(customCulture), fullProfileName);
            iniFile.Write("rBright", currDisplay.rBright.ToString(customCulture), fullProfileName);
            iniFile.Write("gBright", currDisplay.gBright.ToString(customCulture), fullProfileName);
            iniFile.Write("bBright", currDisplay.bBright.ToString(customCulture), fullProfileName);
            iniFile.Write("saturation", currDisplay.saturation.ToString(), fullProfileName);
            iniFile.Write("monitorBrightness", savedMonitorBrightness.ToString(customCulture), fullProfileName);
            iniFile.Write("monitorContrast", savedMonitorContrast.ToString(customCulture), fullProfileName);

            initPresets(fullProfileName);
            initTrayMenu();
        }

        private void buttonDelete_Click(object sender, EventArgs e)
        {
            using (ProfileManagerForm form = new ProfileManagerForm(iniFile))
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
            unchecked { brightnessDebounceToken++; }
            unchecked { contrastDebounceToken++; }

            if (currDisplay == null) return;

            // 프로그램 시작 시 저장된 원본 하드웨어 모니터 값(감마, 밝기, 대비)으로 복원
            ResetMonitorHard(currDisplay);

            initPresets();
            initTrayMenu();
        }

        private void buttonHide_Click(object sender, EventArgs e)
        {
            if (checkBoxTopMost.Checked)
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
            if (comboBoxMonitors.SelectedIndex < 0 || displays == null || displays.Count == 0) return;

            int targetIndex = comboBoxMonitors.SelectedIndex;
            if (targetIndex >= 0 && targetIndex < displays.Count)
            {
                numDisplay = targetIndex;
                currDisplay = displays[numDisplay];
                fillInfo(currDisplay);
                Saturation.Prepare(currDisplay);
                Saturation.Apply(currDisplay, currDisplay.saturation);

                initPresets();
            }
        }

        private void checkBoxTopMost_CheckedChanged(object sender, EventArgs e)
        {
            bool enabled = checkBoxTopMost.Checked;
            TopMost = enabled;
            if (iniFile != null)
                iniFile.Write("TopMost", enabled ? "True" : "False", "Settings");
        }

        private void checkBoxMonitorEnabled_CheckedChanged(object sender, EventArgs e)
        {
            bool enabled = checkBoxMonitorEnabled.Checked;
            trackBarMonitorBrightness.Enabled = enabled;
            trackBarMonitorContrast.Enabled = enabled && currDisplay != null && currDisplay.isExternal;

            // 숫자 상자는 잠그지 않고 항상 입력 가능하게 유지
            textBoxMonitorBrightness.Enabled = true;
            textBoxMonitorContrast.Enabled = currDisplay != null && currDisplay.isExternal;
            buttonReset.Enabled = true;

            // 체크를 켜는 순간, 현재 UI에 설정된 밝기/대비 값을 모니터 하드웨어에 즉시 동기화
            if (enabled && currDisplay != null)
            {
                int targetBrightness = Clamp(trackBarMonitorBrightness.Value, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                int targetContrast = currDisplay.isExternal
                    ? Clamp(trackBarMonitorContrast.Value, trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum)
                    : currDisplay.monitorContrast;

                int thisApplyGeneration = displayService != null ? displayService.NextGeneration() : 0;
                QueuePhysicalMonitorProfileValues(currDisplay.displayName, targetBrightness, targetContrast, "MonitorEnabledSync", thisApplyGeneration);
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
                // 콤보박스 텍스트와 실제 선택된 아이템이 일치할 때만(목록 클릭 시에만) 프로필 적용
                string preset = comboBoxPresets.SelectedItem as string;
                if (!string.IsNullOrEmpty(preset) && string.Equals(comboBoxPresets.Text, preset, StringComparison.Ordinal))
                    ApplyPreset(preset, false);
            }
        }

        private void ApplyPhysicalMonitorProfileValues(Display.DisplayInfo display, int brightness, int contrast, string profileName)
        {
            displayService?.ApplyPhysicalDirect(display, brightness, contrast, profileName);
        }

        private void QueuePhysicalMonitorProfileValues(string monitorName, int brightness, int contrast, string profileName, int generation)
        {
            displayService?.QueuePhysicalSettings(displays, monitorName, brightness, contrast, profileName, generation);
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

            int thisApplyGeneration = displayService != null ? displayService.NextGeneration() : 0;
            applyingPreset = true;
            try
            {
                string monitorName = iniFile.Read("monitor", preset);
                if (string.IsNullOrEmpty(monitorName)) return;

                int displayIndex = -1;
                for (int i = 0; i < displays.Count; i++)
                {
                    if (displays[i] != null && displays[i].displayName.Equals(monitorName, StringComparison.OrdinalIgnoreCase))
                    {
                        displayIndex = i;
                        break;
                    }
                }
                if (displayIndex < 0) return;

                currentPresetByMonitor[monitorName] = preset;

                if (switchMonitor)
                {
                    disableChangeFunc = true;
                    numDisplay = displayIndex;
                    currDisplay = displays[displayIndex];
                    if (comboBoxMonitors != null && comboBoxMonitors.Items.Count > displayIndex)
                        comboBoxMonitors.SelectedIndex = displayIndex;
                    disableChangeFunc = false;
                }
                else
                {
                    currDisplay = displays[displayIndex];
                    numDisplay = displayIndex;
                }

                if (currDisplay == null) return;

                currDisplay.rGamma = ReadProfileFloat("rGamma", preset, currDisplay.rGamma);
                currDisplay.gGamma = ReadProfileFloat("gGamma", preset, currDisplay.gGamma);
                currDisplay.bGamma = ReadProfileFloat("bGamma", preset, currDisplay.bGamma);
                currDisplay.rContrast = ReadProfileFloat("rContrast", preset, currDisplay.rContrast);
                currDisplay.gContrast = ReadProfileFloat("gContrast", preset, currDisplay.gContrast);
                currDisplay.bContrast = ReadProfileFloat("bContrast", preset, currDisplay.bContrast);
                currDisplay.rBright = ReadProfileFloat("rBright", preset, currDisplay.rBright);
                currDisplay.gBright = ReadProfileFloat("gBright", preset, currDisplay.gBright);
                currDisplay.bBright = ReadProfileFloat("bBright", preset, currDisplay.bBright);

                string saturationText = iniFile.Read("saturation", preset);
                if (!int.TryParse(saturationText, out int parsedSaturation))
                    parsedSaturation = currDisplay.saturationDefault;
                currDisplay.saturation = Clamp(parsedSaturation, currDisplay.saturationMin, currDisplay.saturationMax);

                if (!int.TryParse(iniFile.Read("monitorBrightness", preset), out int storedMonitorBrightness))
                    storedMonitorBrightness = currDisplay.monitorBrightness;
                if (!int.TryParse(iniFile.Read("monitorContrast", preset), out int storedMonitorContrast))
                    storedMonitorContrast = currDisplay.monitorContrast;

                currDisplay.monitorBrightness = Clamp(storedMonitorBrightness, 0, 100);
                currDisplay.monitorContrast = Clamp(storedMonitorContrast, 0, 100);

                fillInfo(currDisplay);
                clearColors();
                buttonAllColors?.PerformClick();

                disableChangeFunc = true;
                if (comboBoxPresets != null)
                {
                    int presetIndex = comboBoxPresets.Items.IndexOf(preset);
                    if (presetIndex >= 0) comboBoxPresets.SelectedIndex = presetIndex;
                    else comboBoxPresets.Text = preset;
                }
                disableChangeFunc = false;

                displayService?.ApplySoftwareDisplaySettings(currDisplay);

                if (applyMonitorSettings)
                {
                    int safeBrightness = Math.Max(0, Math.Min(100, currDisplay.monitorBrightness));
                    int safeContrast = Math.Max(0, Math.Min(100, currDisplay.monitorContrast));

                    disableChangeFunc = true;
                    if (trackBarMonitorBrightness != null)
                        trackBarMonitorBrightness.Value = Math.Max(trackBarMonitorBrightness.Minimum, Math.Min(trackBarMonitorBrightness.Maximum, safeBrightness));
                    if (currDisplay.isExternal && trackBarMonitorContrast != null)
                        trackBarMonitorContrast.Value = Math.Max(trackBarMonitorContrast.Minimum, Math.Min(trackBarMonitorContrast.Maximum, safeContrast));
                    disableChangeFunc = false;

                    string physicalMonitorName = currDisplay.displayName;
                    string physicalProfileName = preset;
                    QueuePhysicalMonitorProfileValues(physicalMonitorName, safeBrightness, safeContrast, physicalProfileName, thisApplyGeneration);
                }
                if (IsOSDEnabled())
                {
                    OSDForm.ShowMessage(currDisplay?.displayLink, $"🎮 {preset}");
                }
            }
            finally
            {
                applyingPreset = false;
                disableChangeFunc = false;
            }
        }
    }
}