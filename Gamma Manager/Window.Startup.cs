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
            buttonAllColors.Font = _cachedRegularFont ?? buttonAllColors.Font;
            buttonRed.Font = _cachedRegularFont ?? buttonRed.Font;
            buttonGreen.Font = _cachedRegularFont ?? buttonGreen.Font;
            buttonBlue.Font = _cachedRegularFont ?? buttonBlue.Font;

            allColors = false;
        }

        private void initPresets(string preferredPreset = null)
        {
            string current = preferredPreset;
            if (current == null)
                current = comboBoxPresets.SelectedItem as string;

            comboBoxPresets.Items.Clear();

            string defaultLabel = LanguageManager.Korean ? "기본값" : "Default";
            comboBoxPresets.Items.Add(defaultLabel);

            string[] presets = iniFile.GetSections();
            if (presets != null && currDisplay != null)
            {
                string currentKey = DisplayService.GetMonitorKey(currDisplay);
                string targetBaseName = currDisplay.baseDisplayName ?? currDisplay.displayName;
                bool hasDuplicateModel = displays != null && displays.FindAll(d => d != null &&
                    string.Equals(d.baseDisplayName ?? d.displayName, targetBaseName, StringComparison.OrdinalIgnoreCase)).Count > 1;

                string defaultPrefixKo = "기본값 - ";
                string defaultPrefixEn = "Default - ";

                for (int i = 0; i < presets.Length; i++)
                {
                    string section = presets[i];
                    if (string.IsNullOrEmpty(section)) continue;

                    // 내부 기본값 섹션(기본값 - 모니터명)은 별도 중복 추가하지 않고 최상단 [기본값]으로 통일
                    if (section.StartsWith(defaultPrefixKo, StringComparison.OrdinalIgnoreCase) ||
                        section.StartsWith(defaultPrefixEn, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string monitorKey = iniFile.Read("monitorKey", section);
                    string monitor = iniFile.Read("monitor", section);
                    bool keyMatch = !string.IsNullOrEmpty(monitorKey) && !string.IsNullOrEmpty(currentKey) &&
                                    string.Equals(monitorKey, currentKey, StringComparison.OrdinalIgnoreCase);

                    bool legacyNameMatch = false;
                    if (string.IsNullOrEmpty(monitorKey) && !string.IsNullOrEmpty(monitor))
                    {
                        if (hasDuplicateModel)
                        {
                            legacyNameMatch = string.Equals(monitor, currDisplay.displayName, StringComparison.OrdinalIgnoreCase);
                        }
                        else
                        {
                            legacyNameMatch = string.Equals(monitor, currDisplay.displayName, StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(monitor, currDisplay.baseDisplayName, StringComparison.OrdinalIgnoreCase);
                        }
                    }

                    if (keyMatch || legacyNameMatch)
                    {
                        comboBoxPresets.Items.Add(presets[i]);
                    }
                }
            }

            disableChangeFunc = true;
            try
            {
                if (string.IsNullOrEmpty(current) ||
                    string.Equals(current, "기본값", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(current, "Default", StringComparison.OrdinalIgnoreCase) ||
                    current.StartsWith("기본값 - ", StringComparison.OrdinalIgnoreCase) ||
                    current.StartsWith("Default - ", StringComparison.OrdinalIgnoreCase))
                {
                    comboBoxPresets.SelectedIndex = 0;
                }
                else
                {
                    int index = comboBoxPresets.Items.IndexOf(current);
                    if (index < 0 && currDisplay != null)
                    {
                        string prefix = currDisplay.displayName + ": ";
                        if (current.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            index = comboBoxPresets.Items.IndexOf(current.Substring(prefix.Length));
                        else
                            index = comboBoxPresets.Items.IndexOf(prefix + current);
                    }

                    if (index >= 0)
                    {
                        comboBoxPresets.SelectedIndex = index;
                    }
                    else
                    {
                        comboBoxPresets.SelectedIndex = -1;
                        comboBoxPresets.Text = string.Empty;
                    }
                }
            }
            finally
            {
                disableChangeFunc = false;
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

            // [추가] 즉시 볼륨 전환 설정 메뉴
            ToolStripMenuItem toolVolumeDuck = new ToolStripMenuItem(LanguageManager.Korean ? "즉시 볼륨 전환 설정..." : "Volume Switch Settings...", null, (s, e) =>
            {
                OpenVolumeDuckSettings();
            });
            contextMenu.Items.Add(toolVolumeDuck);

            ToolStripSeparator toolStripSeparator1 = new ToolStripSeparator();
            contextMenu.Items.Add(toolStripSeparator1);

            for (int i = 0; i < displays.Count; i++)
            {
                var targetDisplay = displays[i];
                toolMonitor = new ToolStripComboBox(targetDisplay.displayName);
                toolMonitor.DropDownStyle = ComboBoxStyle.DropDownList;

                toolMonitor.Items.Add(targetDisplay.displayName + ":");
                toolMonitor.Text = targetDisplay.displayName + ":";

                string targetKey = DisplayService.GetMonitorKey(targetDisplay);
                string targetBaseName = targetDisplay.baseDisplayName ?? targetDisplay.displayName;
                bool hasDuplicateModel = displays != null && displays.FindAll(d => d != null &&
                    string.Equals(d.baseDisplayName ?? d.displayName, targetBaseName, StringComparison.OrdinalIgnoreCase)).Count > 1;

                string[] presets = iniFile.GetSections();
                if (presets != null)
                {
                    for (int j = 0; j < presets.Length; j++)
                    {
                        string section = presets[j];
                        string sectionKey = iniFile.Read("monitorKey", section);
                        string sectionMonitor = iniFile.Read("monitor", section);
                        bool keyMatch = !string.IsNullOrEmpty(sectionKey) && !string.IsNullOrEmpty(targetKey) &&
                                        string.Equals(sectionKey, targetKey, StringComparison.OrdinalIgnoreCase);

                        bool legacyNameMatch = false;
                        if (string.IsNullOrEmpty(sectionKey) && !string.IsNullOrEmpty(sectionMonitor))
                        {
                            if (hasDuplicateModel)
                            {
                                legacyNameMatch = string.Equals(sectionMonitor, targetDisplay.displayName, StringComparison.OrdinalIgnoreCase);
                            }
                            else
                            {
                                legacyNameMatch = string.Equals(sectionMonitor, targetDisplay.displayName, StringComparison.OrdinalIgnoreCase) ||
                                                 string.Equals(sectionMonitor, targetDisplay.baseDisplayName, StringComparison.OrdinalIgnoreCase);
                            }
                        }

                        if (keyMatch || legacyNameMatch)
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

        /// <summary>
        /// 1. 순수 UI 렌더링: 메모리에 있는 현재 모니터 상태값을 슬라이더와 텍스트 박스에 즉시 표시 (0ms)
        /// </summary>
        private void UpdateMonitorUI(Display.DisplayInfo display)
        {
            if (display == null) return;

            // GPU 감마/대비/밝기/채도 슬라이더 및 텍스트 갱신
            if (allColors)
            {
                decimal avgG = Math.Max(textBoxGamma.Minimum, Math.Min(textBoxGamma.Maximum, (decimal)((display.rGamma + display.gGamma + display.bGamma) / 3f)));
                decimal avgC = Math.Max(textBoxContrast.Minimum, Math.Min(textBoxContrast.Maximum, (decimal)((display.rContrast + display.gContrast + display.bContrast) / 3f)));
                decimal avgB = Math.Max(textBoxBrightness.Minimum, Math.Min(textBoxBrightness.Maximum, (decimal)((display.rBright + display.gBright + display.bBright) / 3f)));

                textBoxGamma.Value = avgG;
                textBoxContrast.Value = avgC;
                textBoxBrightness.Value = avgB;

                trackBarGamma.Value = Clamp((int)(avgG * 100m), trackBarGamma.Minimum, trackBarGamma.Maximum);
                trackBarContrast.Value = Clamp((int)(avgC * 100m), trackBarContrast.Minimum, trackBarContrast.Maximum);
                trackBarBrightness.Value = Clamp((int)(avgB * 100m), trackBarBrightness.Minimum, trackBarBrightness.Maximum);
            }
            else if (redColor)
            {
                decimal valG = Math.Max(textBoxGamma.Minimum, Math.Min(textBoxGamma.Maximum, (decimal)display.rGamma));
                decimal valC = Math.Max(textBoxContrast.Minimum, Math.Min(textBoxContrast.Maximum, (decimal)display.rContrast));
                decimal valB = Math.Max(textBoxBrightness.Minimum, Math.Min(textBoxBrightness.Maximum, (decimal)display.rBright));

                textBoxGamma.Value = valG;
                textBoxContrast.Value = valC;
                textBoxBrightness.Value = valB;

                trackBarGamma.Value = Clamp((int)(valG * 100m), trackBarGamma.Minimum, trackBarGamma.Maximum);
                trackBarContrast.Value = Clamp((int)(valC * 100m), trackBarContrast.Minimum, trackBarContrast.Maximum);
                trackBarBrightness.Value = Clamp((int)(valB * 100m), trackBarBrightness.Minimum, trackBarBrightness.Maximum);
            }
            else if (greenColor)
            {
                decimal valG = Math.Max(textBoxGamma.Minimum, Math.Min(textBoxGamma.Maximum, (decimal)display.gGamma));
                decimal valC = Math.Max(textBoxContrast.Minimum, Math.Min(textBoxContrast.Maximum, (decimal)display.gContrast));
                decimal valB = Math.Max(textBoxBrightness.Minimum, Math.Min(textBoxBrightness.Maximum, (decimal)display.gBright));

                textBoxGamma.Value = valG;
                textBoxContrast.Value = valC;
                textBoxBrightness.Value = valB;

                trackBarGamma.Value = Clamp((int)(valG * 100m), trackBarGamma.Minimum, trackBarGamma.Maximum);
                trackBarContrast.Value = Clamp((int)(valC * 100m), trackBarContrast.Minimum, trackBarContrast.Maximum);
                trackBarBrightness.Value = Clamp((int)(valB * 100m), trackBarBrightness.Minimum, trackBarBrightness.Maximum);
            }
            else if (blueColor)
            {
                decimal valG = Math.Max(textBoxGamma.Minimum, Math.Min(textBoxGamma.Maximum, (decimal)display.bGamma));
                decimal valC = Math.Max(textBoxContrast.Minimum, Math.Min(textBoxContrast.Maximum, (decimal)display.bContrast));
                decimal valB = Math.Max(textBoxBrightness.Minimum, Math.Min(textBoxBrightness.Maximum, (decimal)display.bBright));

                textBoxGamma.Value = valG;
                textBoxContrast.Value = valC;
                textBoxBrightness.Value = valB;

                trackBarGamma.Value = Clamp((int)(valG * 100m), trackBarGamma.Minimum, trackBarGamma.Maximum);
                trackBarContrast.Value = Clamp((int)(valC * 100m), trackBarContrast.Minimum, trackBarContrast.Maximum);
                trackBarBrightness.Value = Clamp((int)(valB * 100m), trackBarBrightness.Minimum, trackBarBrightness.Maximum);
            }

            // 채도(Digital Vibrance) UI 설정
            trackBarSaturation.Minimum = display.saturationMin;
            trackBarSaturation.Maximum = Math.Max(display.saturationMin + 1, display.saturationMax);
            trackBarSaturation.SmallChange = Math.Max(1, display.saturationStep);
            trackBarSaturation.Value = Clamp(display.saturation, trackBarSaturation.Minimum, trackBarSaturation.Maximum);
            textBoxSaturation.Value = Math.Max(textBoxSaturation.Minimum, Math.Min(textBoxSaturation.Maximum, (decimal)display.saturation));
            trackBarSaturation.Enabled = display.saturationSupported;
            textBoxSaturation.ReadOnly = !display.saturationSupported;
            labelSaturation.Text = display.saturationSupported
                ? (display.adapterVendor == WinApi.DisplayAdapterVendor.Nvidia ? (LanguageManager.Korean ? "디지털\n바이브런스" : "Digital\nVibrance") : (LanguageManager.Korean ? "채도" : "Saturation"))
                : (LanguageManager.Korean ? "채도 (미지원 GPU)" : "Saturation (unsupported)");

            // 하드웨어 모니터 UI 갱신 (외부 모니터 vs 내장 패널)
            if (display.isExternal)
            {
                labelMonitorContrastUp.Visible = true;
                labelMonitorContrastDown.Visible = true;
                trackBarMonitorContrast.Visible = true;
                textBoxMonitorContrast.Visible = true;

                int safeBrightness = Clamp(display.monitorBrightness, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                int safeContrast = Clamp(display.monitorContrast, trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum);
                textBoxMonitorBrightness.Value = safeBrightness;
                trackBarMonitorBrightness.Value = safeBrightness;
                textBoxMonitorContrast.Value = safeContrast;
                trackBarMonitorContrast.Value = safeContrast;
            }
            else
            {
                labelMonitorContrastUp.Visible = false;
                labelMonitorContrastDown.Visible = false;
                trackBarMonitorContrast.Visible = false;
                textBoxMonitorContrast.Visible = false;

                int safeBrightness = Clamp(display.monitorBrightness, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                textBoxMonitorBrightness.Value = safeBrightness;
                trackBarMonitorBrightness.Value = safeBrightness;
            }

            // 모니터 조절 활성화 체크박스 상태 반영
            bool monitorEnabled = checkBoxMonitorEnabled.Checked;
            trackBarMonitorBrightness.Enabled = monitorEnabled;
            trackBarMonitorContrast.Enabled = monitorEnabled && display.isExternal;
            textBoxMonitorBrightness.Enabled = monitorEnabled;
            textBoxMonitorContrast.Enabled = monitorEnabled && display.isExternal;
        }

        /// <summary>
        /// 2. 백그라운드 하드웨어 I/O: 실제 모니터 물리 DDC/CI 또는 WMI 값을 비동기로 읽어와 UI 보정
        /// </summary>
        private void ReadMonitorHardwareAsync(Display.DisplayInfo targetDisplay)
        {
            if (targetDisplay == null || applyingPreset) return;

            // 1. 이전 모니터 읽기 작업이 진행 중이면 즉시 취소
            CancelPendingHardwareRead();

            if (targetDisplay.isExternal)
            {
                _hardwareReadCts = new System.Threading.CancellationTokenSource();
                var token = _hardwareReadCts.Token;

                System.Threading.Tasks.Task.Run(() =>
                {
                    if (token.IsCancellationRequested) return;

                    Display.RefreshPhysicalMonitorHandle(targetDisplay);
                    if (token.IsCancellationRequested || targetDisplay.PhysicalHandle == IntPtr.Zero || targetDisplay.PhysicalHandle == (IntPtr)(-1))
                        return;

                    int liveBrightness;
                    int liveContrast;
                    bool gotBrightness = ExternalMonitor.TryGetBrightness(targetDisplay.PhysicalHandle, out liveBrightness);
                    if (token.IsCancellationRequested) return;

                    bool gotContrast = ExternalMonitor.TryGetContrast(targetDisplay.PhysicalHandle, out liveContrast);
                    if (token.IsCancellationRequested) return;

                    if (gotBrightness || gotContrast)
                    {
                        if (gotBrightness) targetDisplay.monitorBrightness = liveBrightness;
                        if (gotContrast) targetDisplay.monitorContrast = liveContrast;

                        if (this.IsHandleCreated && !this.IsDisposed && !token.IsCancellationRequested)
                        {
                            this.BeginInvoke((MethodInvoker)delegate
                            {
                                if (token.IsCancellationRequested || currDisplay != targetDisplay) return;

                                if (!trackBarMonitorBrightness.Capture && !trackBarMonitorContrast.Capture)
                                {
                                    disableChangeFunc = true;
                                    try
                                    {
                                        if (gotBrightness)
                                        {
                                            int liveSafeBrightness = Clamp(targetDisplay.monitorBrightness, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                                            textBoxMonitorBrightness.Value = liveSafeBrightness;
                                            trackBarMonitorBrightness.Value = liveSafeBrightness;
                                        }
                                        if (gotContrast)
                                        {
                                            int liveSafeContrast = Clamp(targetDisplay.monitorContrast, trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum);
                                            textBoxMonitorContrast.Value = liveSafeContrast;
                                            trackBarMonitorContrast.Value = liveSafeContrast;
                                        }
                                    }
                                    finally
                                    {
                                        disableChangeFunc = false;
                                    }
                                }
                            });
                        }
                    }
                }, token);
            }
            else
            {
                int liveBrightness;
                if (InternalMonitor.TryGetBrightness(out liveBrightness))
                {
                    targetDisplay.monitorBrightness = liveBrightness;
                    int safeBrightness = Clamp(targetDisplay.monitorBrightness, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                    disableChangeFunc = true;
                    try
                    {
                        textBoxMonitorBrightness.Value = safeBrightness;
                        trackBarMonitorBrightness.Value = safeBrightness;
                    }
                    finally
                    {
                        disableChangeFunc = false;
                    }
                }
            }
        }

        /// <summary>
        /// 기존 호출부 호환용 Facade 메서드
        /// </summary>
        private void fillInfo(Display.DisplayInfo display)
        {
            if (display == null) return;
            disableChangeFunc = true;
            try
            {
                UpdateMonitorUI(display);
                ReadMonitorHardwareAsync(display);
            }
            finally
            {
                disableChangeFunc = false;
            }
        }

        private void Window_Load(object sender, EventArgs e)
        {
            int screenWidth = Screen.PrimaryScreen.Bounds.Size.Width;
            int windowWidth = Width;
            int screenHeight = Screen.PrimaryScreen.Bounds.Size.Height;
            int windowHeight = Height;
            int tmp = Screen.PrimaryScreen.Bounds.Height;
            int TaskBarHeight = tmp - Screen.PrimaryScreen.WorkingArea.Height;

            Location = new Point(screenWidth - windowWidth, screenHeight - (windowHeight + TaskBarHeight));
        }
    }
}