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

        /// <summary>
        /// 1. 순수 UI 렌더링: 메모리에 있는 현재 모니터 상태값을 슬라이더와 텍스트 박스에 즉시 표시 (0ms)
        /// </summary>
        private void UpdateMonitorUI(Display.DisplayInfo display)
        {
            if (display == null) return;

            // GPU 감마/대비/밝기/채도 슬라이더 및 텍스트 갱신
            if (allColors)
            {
                textBoxGamma.Text = ((display.rGamma + display.gGamma + display.bGamma) / 3f).ToString("0.00");
                textBoxContrast.Text = ((display.rContrast + display.gContrast + display.bContrast) / 3f).ToString("0.00");
                textBoxBrightness.Text = ((display.rBright + display.gBright + display.bBright) / 3f).ToString("0.00");

                trackBarGamma.Value = Clamp((int)(((display.rGamma + display.gGamma + display.bGamma) / 3f) * 100f), trackBarGamma.Minimum, trackBarGamma.Maximum);
                trackBarContrast.Value = Clamp((int)(((display.rContrast + display.gContrast + display.bContrast) / 3f) * 100f), trackBarContrast.Minimum, trackBarContrast.Maximum);
                trackBarBrightness.Value = Clamp((int)(((display.rBright + display.gBright + display.bBright) / 3f) * 100f), trackBarBrightness.Minimum, trackBarBrightness.Maximum);
            }
            else if (redColor)
            {
                textBoxGamma.Text = display.rGamma.ToString("0.00");
                textBoxContrast.Text = display.rContrast.ToString("0.00");
                textBoxBrightness.Text = display.rBright.ToString("0.00");

                trackBarGamma.Value = Clamp((int)(display.rGamma * 100f), trackBarGamma.Minimum, trackBarGamma.Maximum);
                trackBarContrast.Value = Clamp((int)(display.rContrast * 100f), trackBarContrast.Minimum, trackBarContrast.Maximum);
                trackBarBrightness.Value = Clamp((int)(display.rBright * 100f), trackBarBrightness.Minimum, trackBarBrightness.Maximum);
            }
            else if (greenColor)
            {
                textBoxGamma.Text = display.gGamma.ToString("0.00");
                textBoxContrast.Text = display.gContrast.ToString("0.00");
                textBoxBrightness.Text = display.gBright.ToString("0.00");

                trackBarGamma.Value = Clamp((int)(display.gGamma * 100f), trackBarGamma.Minimum, trackBarGamma.Maximum);
                trackBarContrast.Value = Clamp((int)(display.gContrast * 100f), trackBarContrast.Minimum, trackBarContrast.Maximum);
                trackBarBrightness.Value = Clamp((int)(display.gBright * 100f), trackBarBrightness.Minimum, trackBarBrightness.Maximum);
            }
            else if (blueColor)
            {
                textBoxGamma.Text = display.bGamma.ToString("0.00");
                textBoxContrast.Text = display.bContrast.ToString("0.00");
                textBoxBrightness.Text = display.bBright.ToString("0.00");

                trackBarGamma.Value = Clamp((int)(display.bGamma * 100f), trackBarGamma.Minimum, trackBarGamma.Maximum);
                trackBarContrast.Value = Clamp((int)(display.bContrast * 100f), trackBarContrast.Minimum, trackBarContrast.Maximum);
                trackBarBrightness.Value = Clamp((int)(display.bBright * 100f), trackBarBrightness.Minimum, trackBarBrightness.Maximum);
            }

            // 채도(Digital Vibrance) UI 설정
            trackBarSaturation.Minimum = display.saturationMin;
            trackBarSaturation.Maximum = Math.Max(display.saturationMin + 1, display.saturationMax);
            trackBarSaturation.SmallChange = Math.Max(1, display.saturationStep);
            trackBarSaturation.Value = Clamp(display.saturation, trackBarSaturation.Minimum, trackBarSaturation.Maximum);
            textBoxSaturation.Text = display.saturation.ToString();
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

                int safeBrightness = Clamp(display.monitorBrightness, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                textBoxMonitorBrightness.Text = safeBrightness.ToString();
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
                                    if (gotBrightness)
                                    {
                                        int liveSafeBrightness = Clamp(targetDisplay.monitorBrightness, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                                        textBoxMonitorBrightness.Text = liveSafeBrightness.ToString();
                                        trackBarMonitorBrightness.Value = liveSafeBrightness;
                                    }
                                    if (gotContrast)
                                    {
                                        int liveSafeContrast = Clamp(targetDisplay.monitorContrast, trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum);
                                        textBoxMonitorContrast.Text = liveSafeContrast.ToString();
                                        trackBarMonitorContrast.Value = liveSafeContrast;
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
                    textBoxMonitorBrightness.Text = safeBrightness.ToString();
                    trackBarMonitorBrightness.Value = safeBrightness;
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