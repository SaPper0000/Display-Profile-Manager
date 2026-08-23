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
    // Profile selection, save/delete/reset, monitor-profile state, and preset application.
    public partial class Window : Form
    {
        private void InitializeCurrentPresetState()
        {
            currentPresetByMonitor.Clear();
            string prefix = LanguageManager.Korean ? "기본값 - " : "Default - ";
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

        private void EnsureDefaultProfile()
        {
            // Keep one automatic Default profile for every detected monitor.
            // The profile name contains the monitor name because INI section names
            // must be unique, while the main window filters profiles by monitor.
            // Existing profiles are never overwritten.
            string prefix = LanguageManager.Korean ? "기본값 - " : "Default - ";

            foreach (Display.DisplayInfo display in displays)
            {
                if (display == null || string.IsNullOrEmpty(display.displayName))
                    continue;

                string defaultName = prefix + display.displayName;
                if (!string.IsNullOrEmpty(iniFile.Read("monitor", defaultName)))
                    continue;

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
                iniFile.Write("monitorBrightness", display.monitorBrightness.ToString(customCulture), defaultName);
                iniFile.Write("monitorContrast", display.monitorContrast.ToString(customCulture), defaultName);
            }
        }

        private void SyncMonitorSettingsForProfileSave()
        {
            if (currDisplay == null) return;

            // IMPORTANT: profile saving must capture the values shown in the UI,
            // not re-read the physical monitor. The physical monitor readback can
            // lag/fail (or the display may reject DDC/CI writes), which previously
            // caused a just-entered 30/30 value to be replaced by the old hardware
            // value before the INI was written.
            currDisplay.monitorBrightness = Clamp(trackBarMonitorBrightness.Value,
                trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);

            if (currDisplay.isExternal)
            {
                currDisplay.monitorContrast = Clamp(trackBarMonitorContrast.Value,
                    trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum);
            }

            // Keep the numeric controls synchronized with exactly what is stored.
            disableChangeFunc = true;
            try
            {
                textBoxMonitorBrightness.Value = currDisplay.monitorBrightness;
                if (currDisplay.isExternal)
                    textBoxMonitorContrast.Value = currDisplay.monitorContrast;
            }
            finally
            {
                disableChangeFunc = false;
            }
        }

        private void buttonSave_Click(object sender, EventArgs e)
        {
            // Always capture the values currently shown by the monitor controls at the
            // exact moment Save is pressed. This deliberately does NOT depend on the
            // monitor-enable checkbox. Profiles used by Hotkey/Game Auto must contain
            // the physical monitor values, not a stale hardware readback.
            SyncMonitorSettingsForProfileSave();
            int savedMonitorBrightness = Clamp(trackBarMonitorBrightness.Value,
                trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
            int savedMonitorContrast = currDisplay.isExternal
                ? Clamp(trackBarMonitorContrast.Value, trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum)
                : currDisplay.monitorContrast;

            string tmp = comboBoxPresets.Text;
            iniFile.Write("monitor", currDisplay.displayName, currDisplay.displayName+": "+ comboBoxPresets.Text);
            iniFile.Write("rGamma", currDisplay.rGamma.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("gGamma", currDisplay.gGamma.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("bGamma", currDisplay.bGamma.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("rContrast", currDisplay.rContrast.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("gContrast", currDisplay.gContrast.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("bContrast", currDisplay.bContrast.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("rBright", currDisplay.rBright.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("gBright", currDisplay.gBright.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("bBright", currDisplay.bBright.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("saturation", currDisplay.saturation.ToString(), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("monitorBrightness", savedMonitorBrightness.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("monitorContrast", savedMonitorContrast.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);

            initPresets(currDisplay.displayName + ": " + tmp);
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
            // HARD RESET: restore the exact display state captured when the
            // application started. This is intentionally NOT a 50/50 or neutral reset.
            // It must undo the physical monitor changes made by profiles/hotkeys/game auto.
            physicalMonitorApplyGeneration++; // invalidate all deferred profile writes

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

            if (currDisplay == null) return;

            bool restored = StartupStateManager.RestoreOriginalMonitor(currDisplay);

            // Restore UI/model values without firing hardware-change events.
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

            // The reset updates the sliders while change handlers are disabled,
            // so synchronize the numeric boxes explicitly after the UI state is restored.
            RefreshNumericBoxes();

            if (currDisplay.saturationSupported)
                Saturation.Apply(currDisplay, currDisplay.saturation);
            Gamma.SetGammaRamp(currDisplay.displayLink, Gamma.CreateGammaRamp(1, 1, 1, 1, 1, 1, 0, 0, 0));

            currentPresetByMonitor.Remove(currDisplay.displayName);
            initPresets();
            initTrayMenu();

            Logger.Info("HARD RESET completed. Monitor=" + currDisplay.displayName + ", OriginalMonitorStateRestored=" + restored);
            if (!restored)
            {
                MessageBox.Show(
                    LanguageManager.Korean ? "원래 모니터 설정값을 복원하지 못했습니다. 로그를 확인하세요." :
                    "The original monitor settings could not be restored. Please check the log.",
                    LanguageManager.Korean ? "초기화 실패" : "Reset Failed",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void buttonHide_Click(object sender, EventArgs e)
        {
            // When "Always on Top" is enabled, preserve the existing behavior:
            // hide the window completely and keep it available from the tray.
            // When it is disabled, behave like a normal Windows window and
            // minimize to the taskbar instead.
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
            string num = comboBoxMonitors.SelectedItem.ToString();

            num = num.Substring(0, num.IndexOf(")"));
            numDisplay = Int32.Parse(num)-1;

            currDisplay = displays[numDisplay];
            fillInfo(currDisplay);
            Saturation.Prepare(currDisplay);
            Saturation.Apply(currDisplay, currDisplay.saturation);
            
            initPresets();
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
            trackBarMonitorContrast.Enabled = enabled && currDisplay.isExternal;
            textBoxMonitorBrightness.Enabled = enabled;
            textBoxMonitorContrast.Enabled = enabled && currDisplay.isExternal;
            buttonReset.Enabled = true;
        }

        private void buttonForward_Click(object sender, EventArgs e)
        {
            if (numDisplay + 1 <= displays.Count-1)
            {
                comboBoxMonitors.SelectedIndex = numDisplay + 1;
            } else
            {
                comboBoxMonitors.SelectedIndex = 0;
            }
        }

        private void comboBoxPresets_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Main-window profile selection is intentionally NOT part of Game Auto priority.
            // It changes the profile immediately on the main screen, but while a mapped game
            // is active the priority is strictly: Hotkey > Game Auto.
            if (!disableChangeFunc && !applyingPreset && comboBoxPresets.SelectedIndex >= 0)
            {
                string preset = comboBoxPresets.SelectedItem as string;
                if (!string.IsNullOrEmpty(preset))
                    ApplyPreset(preset, false);
            }
        }

        private void ApplyPreset(string preset)
        {
            ApplyPreset(preset, true, true);
        }

        private void ApplyPreset(string preset, bool switchMonitor)
        {
            ApplyPreset(preset, switchMonitor, true);
        }

        private void ApplyPhysicalMonitorProfileValues(Display.DisplayInfo display, int brightness, int contrast, string profileName)
        {
            if (display == null) return;

            int safeBrightness = Math.Max(0, Math.Min(100, brightness));
            int safeContrast = Math.Max(0, Math.Min(100, contrast));

            if (display.isExternal)
            {
                // IMPORTANT: a PHYSICAL_MONITOR handle is not a permanent device
                // handle. Windows can invalidate it after monitor sleep/wake, a
                // fullscreen mode change, HDR/display-driver reset, or cable/display
                // re-enumeration. Always reacquire the handle immediately before a
                // Hotkey/Game Auto/Toggle physical-monitor write.
                Display.RefreshPhysicalMonitorHandle(display);

                if (display.PhysicalHandle == IntPtr.Zero || display.PhysicalHandle == (IntPtr)(-1))
                {
                    Logger.Warn("No valid DDC/CI handle before profile apply. Profile=" +
                        profileName + ", Target=" + safeBrightness + "/" + safeContrast +
                        ", Monitor=" + display.displayName);
                }
                else
                {
                    bool applied = ExternalMonitor.SetBrightnessAndContrast(
                        display.PhysicalHandle, safeBrightness, safeContrast);

                    // If the first handle/write failed, reacquire once more and retry.
                    // This specifically handles stale DXVA2 handles left behind by
                    // fullscreen/display-driver transitions.
                    if (!applied)
                    {
                        Logger.Warn("Profile physical monitor apply failed; refreshing DDC/CI handle and retrying. " +
                            "Profile=" + profileName + ", Target=" + safeBrightness + "/" + safeContrast +
                            ", Monitor=" + display.displayName);
                        if (Display.RefreshPhysicalMonitorHandle(display) &&
                            display.PhysicalHandle != IntPtr.Zero && display.PhysicalHandle != (IntPtr)(-1))
                        {
                            applied = ExternalMonitor.SetBrightnessAndContrast(
                                display.PhysicalHandle, safeBrightness, safeContrast);
                        }
                    }

                    if (!applied)
                    {
                        Logger.Warn("Profile physical monitor apply verification failed after handle refresh. " +
                            "Profile=" + profileName + ", Target=" + safeBrightness + "/" + safeContrast +
                            ", Monitor=" + display.displayName);
                    }
                }
            }
            else if (!display.isExternal)
            {
                InternalMonitor.SetBrightness((byte)safeBrightness);
            }

            display.monitorBrightness = safeBrightness;
            display.monitorContrast = safeContrast;
        }

        private void ApplyPreset(string preset, bool switchMonitor, bool applyMonitorSettings)
        {
            if (string.IsNullOrEmpty(preset)) return;

            // Invalidate any delayed physical-monitor apply belonging to an older
            // hotkey/toggle action. The newest requested profile always wins.
            int thisApplyGeneration = ++physicalMonitorApplyGeneration;
            applyingPreset = true;
            try
            {
                string monitorName = iniFile.Read("monitor", preset);
                if (string.IsNullOrEmpty(monitorName)) return;

                int displayIndex = -1;
                for (int i = 0; i < displays.Count; i++)
                {
                    if (displays[i].displayName.Equals(monitorName, StringComparison.Ordinal))
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
                    comboBoxMonitors.SelectedIndex = displayIndex;
                    disableChangeFunc = false;
                }
                else
                {
                    currDisplay = displays[displayIndex];
                    numDisplay = displayIndex;
                }

                currDisplay.rGamma = float.Parse(iniFile.Read("rGamma", preset), customCulture);
                currDisplay.gGamma = float.Parse(iniFile.Read("gGamma", preset), customCulture);
                currDisplay.bGamma = float.Parse(iniFile.Read("bGamma", preset), customCulture);
                currDisplay.rContrast = float.Parse(iniFile.Read("rContrast", preset), customCulture);
                currDisplay.gContrast = float.Parse(iniFile.Read("gContrast", preset), customCulture);
                currDisplay.bContrast = float.Parse(iniFile.Read("bContrast", preset), customCulture);
                currDisplay.rBright = float.Parse(iniFile.Read("rBright", preset), customCulture);
                currDisplay.gBright = float.Parse(iniFile.Read("gBright", preset), customCulture);
                currDisplay.bBright = float.Parse(iniFile.Read("bBright", preset), customCulture);
                string saturationText = iniFile.Read("saturation", preset);
                int parsedSaturation;
                if (!int.TryParse(saturationText, out parsedSaturation)) parsedSaturation = currDisplay.saturationDefault;
                currDisplay.saturation = Clamp(parsedSaturation, currDisplay.saturationMin, currDisplay.saturationMax);
                int storedMonitorBrightness;
                int storedMonitorContrast;
                if (!int.TryParse(iniFile.Read("monitorBrightness", preset), out storedMonitorBrightness))
                    storedMonitorBrightness = currDisplay.monitorBrightness;
                if (!int.TryParse(iniFile.Read("monitorContrast", preset), out storedMonitorContrast))
                    storedMonitorContrast = currDisplay.monitorContrast;
                currDisplay.monitorBrightness = Clamp(storedMonitorBrightness, 0, 100);
                currDisplay.monitorContrast = Clamp(storedMonitorContrast, 0, 100);
                fillInfo(currDisplay);
                clearColors();
                buttonAllColors.PerformClick();

                disableChangeFunc = true;
                int presetIndex = comboBoxPresets.Items.IndexOf(preset);
                if (presetIndex >= 0) comboBoxPresets.SelectedIndex = presetIndex;
                else comboBoxPresets.Text = preset;
                disableChangeFunc = false;

                Gamma.SetGammaRamp(currDisplay.displayLink,
                    Gamma.CreateGammaRamp(currDisplay.rGamma, currDisplay.gGamma, currDisplay.bGamma,
                    currDisplay.rContrast, currDisplay.gContrast, currDisplay.bContrast,
                    currDisplay.rBright, currDisplay.gBright, currDisplay.bBright));
                Saturation.Prepare(currDisplay);
            Saturation.Apply(currDisplay, currDisplay.saturation);

                if (applyMonitorSettings)
                {
                    // Physical monitor values are a separate layer from GPU gamma.
                    // Apply them directly from the profile every time a profile is
                    // activated by Hotkey/Game Auto. Do not depend on TrackBar events
                    // or the Enable Monitor checkbox.
                    int safeBrightness = Math.Max(0, Math.Min(100, currDisplay.monitorBrightness));
                    int safeContrast = Math.Max(0, Math.Min(100, currDisplay.monitorContrast));
                    ApplyPhysicalMonitorProfileValues(currDisplay, safeBrightness, safeContrast, preset);

                    // Keep the UI in sync, but do NOT call the slider handlers here.
                    // The profile path already wrote the physical monitor directly above;
                    // invoking both slider handlers again caused a second DDC/CI write
                    // and made rapid Toggle presses race with delayed re-applies.
                    disableChangeFunc = true;
                    trackBarMonitorBrightness.Value = Math.Max(trackBarMonitorBrightness.Minimum, Math.Min(trackBarMonitorBrightness.Maximum, safeBrightness));
                    if (currDisplay.isExternal)
                        trackBarMonitorContrast.Value = Math.Max(trackBarMonitorContrast.Minimum, Math.Min(trackBarMonitorContrast.Maximum, safeContrast));
                    disableChangeFunc = false;

                    // Keep one cancellable delayed verification/re-assert for monitors that need a
                    // short settling period after the profile/GPU change. If another
                    // profile/toggle is requested before this runs, the generation token
                    // makes this callback a no-op instead of resurrecting the old value.
                    string reapplyMonitorName = currDisplay.displayName;
                    int reapplyBrightness = safeBrightness;
                    int reapplyContrast = safeContrast;
                    string reapplyProfile = preset;
                    try
                    {
                        Timer reapplyTimer = null;
                        reapplyTimer = new Timer();
                        reapplyTimer.Interval = 90;
                        reapplyTimer.Tick += delegate
                        {
                            reapplyTimer.Stop();
                            reapplyTimer.Dispose();
                            if (thisApplyGeneration != physicalMonitorApplyGeneration) return;
                            foreach (Display.DisplayInfo d in displays)
                            {
                                if (d != null && string.Equals(d.displayName, reapplyMonitorName, StringComparison.Ordinal))
                                {
                                    string livePreset = GetCurrentPresetForMonitor(reapplyMonitorName);
                                    if (string.Equals(livePreset, reapplyProfile, StringComparison.Ordinal))
                                        ApplyPhysicalMonitorProfileValues(d, reapplyBrightness, reapplyContrast, reapplyProfile + " [deferred]");
                                    break;
                                }
                            }
                        };
                        reapplyTimer.Start();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn("Deferred physical monitor profile apply failed: " + ex.Message);
                    }
                }

                int selectedIndex = comboBoxPresets.Items.IndexOf(preset);
                if (selectedIndex >= 0)
                {
                    disableChangeFunc = true;
                    comboBoxPresets.SelectedIndex = selectedIndex;
                    disableChangeFunc = false;
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
