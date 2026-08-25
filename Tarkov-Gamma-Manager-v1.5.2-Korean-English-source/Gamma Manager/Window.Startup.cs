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
    // Startup initialization, preset discovery, tray setup, and initial display state.
    public partial class Window : Form
    {
        private void clearColors()
        {
            buttonAllColors.Font = new Font(buttonAllColors.Font.Name, buttonAllColors.Font.Size, FontStyle.Regular);
            buttonRed.Font = new Font(buttonRed.Font.Name, buttonRed.Font.Size, FontStyle.Regular);
            buttonGreen.Font = new Font(buttonGreen.Font.Name, buttonGreen.Font.Size, FontStyle.Regular);
            buttonBlue.Font = new Font(buttonBlue.Font.Name, buttonBlue.Font.Size, FontStyle.Regular);

            allColors = false;
            redColor = false;
            greenColor = false;
            blueColor = false;
        }

        private void initPresets(string preferredPreset = null)
        {
            string current = preferredPreset;
            if (string.IsNullOrEmpty(current))
                current = comboBoxPresets.SelectedItem as string;

            comboBoxPresets.Items.Clear();

            string[] presets = iniFile.GetSections();
            if (presets != null)
            {
                for (int i = 0; i < presets.Length; i++)
                {
                    string monitor = iniFile.Read("monitor", presets[i]);
                    if (!string.IsNullOrEmpty(monitor) &&
                        monitor.Equals(currDisplay.displayName, StringComparison.Ordinal))
                    {
                        comboBoxPresets.Items.Add(presets[i]);
                    }
                }
            }

            if (!string.IsNullOrEmpty(current))
            {
                int index = comboBoxPresets.Items.IndexOf(current);
                if (index >= 0)
                {
                    disableChangeFunc = true;
                    comboBoxPresets.SelectedIndex = index;
                    disableChangeFunc = false;
                }
                else
                {
                    comboBoxPresets.Text = string.Empty;
                }
            }
            else
            {
                comboBoxPresets.Text = string.Empty;
            }
        }

        private void initTrayMenu()
        {
            contextMenu.Items.Clear();
            toolMonitors.Clear();

            ToolStripMenuItem toolSetting = new ToolStripMenuItem(LanguageManager.Korean ? "설정" : "Settings", null, toolSettings_Click);
            contextMenu.Items.Add(toolSetting);

            ToolStripMenuItem toolProfiles = new ToolStripMenuItem(LanguageManager.Korean ? "프로필..." : "Profiles...", null, toolProfiles_Click);
            contextMenu.Items.Add(toolProfiles);

            ToolStripMenuItem toolHotkeys = new ToolStripMenuItem(LanguageManager.Korean ? "핫키..." : "Hotkeys...", null, toolHotkeys_Click);
            contextMenu.Items.Add(toolHotkeys);

            // [추가] OSD 팝업 알림 설정 메뉴
            ToolStripMenuItem toolOsd = new ToolStripMenuItem(LanguageManager.Korean ? "OSD 설정..." : "OSD Settings...", null, buttonOSDSettings_Click);
            contextMenu.Items.Add(toolOsd);

            ToolStripSeparator toolStripSeparator1 = new ToolStripSeparator();
            contextMenu.Items.Add(toolStripSeparator1);

            for (int i = 0; i < displays.Count; i++)
            {
                var targetDisplay = displays[i];
                toolMonitor = new ToolStripComboBox(targetDisplay.displayName);
                toolMonitor.DropDownStyle = ComboBoxStyle.DropDownList;

                toolMonitor.Items.Add(targetDisplay.displayName + ":");
                toolMonitor.Text = targetDisplay.displayName + ":";

                string[] presets = iniFile.GetSections();
                if (presets != null)
                {
                    for (int j = 0; j < presets.Length; j++)
                    {
                        if (iniFile.Read("monitor", presets[j]).Equals(targetDisplay.displayName, StringComparison.OrdinalIgnoreCase))
                        {
                            toolMonitor.Items.Add(presets[j]);
                        }
                    }
                }

                toolMonitor.SelectedIndexChanged += (s, e) =>
                {
                    if (s is ToolStripComboBox cb && cb.SelectedIndex > 0)
                    {
                        string selectedPreset = cb.SelectedItem as string;
                        if (!string.IsNullOrEmpty(selectedPreset))
                        {
                            currDisplay = targetDisplay;
                            ApplyPreset(selectedPreset);
                        }
                    }
                };

                toolMonitors.Add(toolMonitor);
                contextMenu.Items.Add(toolMonitor);
            }

            ToolStripSeparator toolStripSeparator2 = new ToolStripSeparator();
            contextMenu.Items.Add(toolStripSeparator2);

            ToolStripMenuItem toolExit = new ToolStripMenuItem(LanguageManager.Korean ? "종료" : "Exit", null, toolExit_Click);
            contextMenu.Items.Add(toolExit);
        }

        private void fillInfo(Display.DisplayInfo currDisplay)
        {
            disableChangeFunc = true;

            textBoxGamma.Text = ((currDisplay.rGamma + currDisplay.gGamma + currDisplay.bGamma) / 3f).ToString("0.00");
            textBoxContrast.Text = ((currDisplay.rContrast + currDisplay.gContrast + currDisplay.bContrast) / 3f).ToString("0.00");
            textBoxBrightness.Text = ((currDisplay.rBright + currDisplay.gBright + currDisplay.bBright) / 3f).ToString("0.00");
            textBoxSaturation.Text = currDisplay.saturation.ToString();

            trackBarGamma.Value = (int)(((currDisplay.rGamma + currDisplay.gGamma + currDisplay.bGamma) / 3f) * 100f);
            trackBarContrast.Value = (int)(((currDisplay.rContrast + currDisplay.gContrast + currDisplay.bContrast) / 3f) * 100f);
            trackBarBrightness.Value = (int)(((currDisplay.rBright + currDisplay.gBright + currDisplay.bBright) / 3f) * 100f);
            trackBarSaturation.Minimum = currDisplay.saturationMin;
            trackBarSaturation.Maximum = Math.Max(currDisplay.saturationMin + 1, currDisplay.saturationMax);
            trackBarSaturation.SmallChange = Math.Max(1, currDisplay.saturationStep);
            trackBarSaturation.Value = Clamp(currDisplay.saturation, trackBarSaturation.Minimum, trackBarSaturation.Maximum);
            trackBarSaturation.Enabled = currDisplay.saturationSupported;
            textBoxSaturation.ReadOnly = !currDisplay.saturationSupported;
            labelSaturation.Text = currDisplay.saturationSupported
                ? (currDisplay.adapterVendor == WinApi.DisplayAdapterVendor.Nvidia ? (LanguageManager.Korean ? "디지털\n바이브런스" : "Digital\nVibrance") : (LanguageManager.Korean ? "채도" : "Saturation"))
                : (LanguageManager.Korean ? "채도 (미지원 GPU)" : "Saturation (unsupported)");

            if (currDisplay.isExternal)
            {
                labelMonitorContrastUp.Visible = true;
                labelMonitorContrastDown.Visible = true;
                trackBarMonitorContrast.Visible = true;
                textBoxMonitorContrast.Visible = true;

                if (!applyingPreset)
                {
                    int liveBrightness;
                    if (ExternalMonitor.TryGetBrightness(currDisplay.PhysicalHandle, out liveBrightness))
                        currDisplay.monitorBrightness = liveBrightness;
                    int liveContrast;
                    if (ExternalMonitor.TryGetContrast(currDisplay.PhysicalHandle, out liveContrast))
                        currDisplay.monitorContrast = liveContrast;
                }

                int safeBrightness = Clamp(currDisplay.monitorBrightness, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                int safeContrast = Clamp(currDisplay.monitorContrast, trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum);
                textBoxMonitorBrightness.Text = safeBrightness.ToString();
                trackBarMonitorBrightness.Value = safeBrightness;
                textBoxMonitorContrast.Text = safeContrast.ToString();
                trackBarMonitorContrast.Value = safeContrast;
            }
            else
            {
                labelMonitorContrastUp.Visible = false;
                labelMonitorContrastDown.Visible = false;
                trackBarMonitorContrast.Visible = false;
                textBoxMonitorContrast.Visible = false;

                if (!applyingPreset)
                {
                    int liveBrightness;
                    if (InternalMonitor.TryGetBrightness(out liveBrightness))
                        currDisplay.monitorBrightness = liveBrightness;
                }

                int safeBrightness = Clamp(currDisplay.monitorBrightness, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                textBoxMonitorBrightness.Text = safeBrightness.ToString();
                trackBarMonitorBrightness.Value = safeBrightness;
            }
            disableChangeFunc = false;

            // When monitor control is disabled, prevent the monitor adjustment controls
            // from being edited. Reapply this after every monitor/profile refresh so the
            // UI always reflects the master checkbox state.
            bool monitorEnabled = checkBoxMonitorEnabled.Checked;
            trackBarMonitorBrightness.Enabled = monitorEnabled;
            trackBarMonitorContrast.Enabled = monitorEnabled && currDisplay.isExternal;
            textBoxMonitorBrightness.Enabled = monitorEnabled;
            textBoxMonitorContrast.Enabled = monitorEnabled && currDisplay.isExternal;
        }

        private void Window_Load(object sender, EventArgs e)
        {
            int screenWidth = Screen.PrimaryScreen.Bounds.Size.Width;
            int windowWidth = Width;
            int screenHeight = Screen.PrimaryScreen.Bounds.Size.Height;
            int windowHeight = Height;
            int tmp = Screen.PrimaryScreen.Bounds.Height;
            int TaskBarHeight = tmp - Screen.PrimaryScreen.WorkingArea.Height;

            //dpi
            /*int PSH = SystemParameters.PrimaryScreenHeight;
            int PSBH = Screen.PrimaryScreen.Bounds.Height;
            double ratio = PSH / PSBH;
            int TaskBarHeight = PSBH - Screen.PrimaryScreen.WorkingArea.Height;
            TaskBarHeight *= ratio;*/

            Location = new Point(screenWidth - windowWidth, screenHeight - (windowHeight + TaskBarHeight));
        }
    }
}
