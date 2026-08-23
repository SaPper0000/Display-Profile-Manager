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
    // Win32 message handling, resize/close lifecycle, tray events, and monitor toolbar events.
    public partial class Window : Form
    {
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                foreach (GlobalHotkey hotkey in globalHotkeys.Values)
                {
                    if (hotkey.ProcessMessage(ref m))
                        break;
                }
            }
            base.WndProc(ref m);
        }

        private void Window_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (gameAutoWinEventHook != IntPtr.Zero)
            {
                if (!WinApi.UnhookWinEvent(gameAutoWinEventHook))
                    Logger.Warn("UnhookWinEvent failed during shutdown. Win32Error=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                gameAutoWinEventHook = IntPtr.Zero;
            }
            gameAutoWinEventDelegate = null;

            foreach (GlobalHotkey hotkey in globalHotkeys.Values)
                hotkey.Dispose();
            globalHotkeys.Clear();
            globalHotkeyPresets.Clear();
            manualOverrideActive = false;
            manualOverridePreset = null;
            manualOverrideProcess = null;
            manualTogglePresetByMonitor.Clear();
            manualToggleReturnPresetByMonitor.Clear();
            manualToggleReturnStateByMonitor.Clear();
            manualToggleWasOverGameAutoByMonitor.Clear();
            manualToggleActiveByMonitor.Clear();
            manualToggleActive = false;
            manualTogglePreset = null;
            manualToggleReturnPreset = null;

            // Restore exactly what was present before Tarkov Gamma manager started.
            try
            {
                Saturation.Reset();
                StartupStateManager.RestoreAndClear(displays);
            }
            catch (Exception ex)
            {
                Logger.Error("Exception while restoring startup display state during shutdown.", ex);
            }
            finally
            {
                Display.ReleasePhysicalMonitorHandles(displays);
                InternalMonitor.Cleanup(); // 앱 종료 시 WMI 자원 해제 추가 (메모리 릭 방지)
                Logger.Info("Physical monitor handles released; application shutdown complete.");
            }
        }

        // --- 백그라운드 전환 시 램(RAM) 압축 ---
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, int dwMinimumWorkingSetSize, int dwMaximumWorkingSetSize);

        private void TrimMemory()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
                }
            }
            catch { }
        }
        // --------------------------------------------------

        private void Window_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized && checkBoxTopMost.Checked)
            {
                Hide();
                TrimMemory(); // 트레이로 숨을 때 램 반환
            }
            else if (WindowState == FormWindowState.Minimized)
            {
                TrimMemory(); // 작업표시줄로 내려갈 때 램 반환
            }
        }

        private void notifyIcon_DoubleClick(object sender, EventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
        }

        private void toolSettings_Click(object sender, EventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
        }

        private void toolExit_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void comboBoxToolMonitor_IndexChanged(object sender, EventArgs e)
        {
            if (!disableChangeFunc)
            {
                string monitor = sender.ToString().Substring(0, sender.ToString().IndexOf(":"));
                /*string comName = toolMonitor.Items[i].ToString().Substring
                        (0, toolMonitor.Items[i].ToString().IndexOf(":"));*/

                int tmp = 0;

                disableChangeFunc = true;

                for (int i = 0; i < displays.Count; i++)
                {
                    if (monitor.Equals(displays[i].displayName))
                    {
                        tmp = i;
                    }
                    else
                    {
                        toolMonitor = toolMonitors[i];
                        toolMonitor.SelectedIndex = 0;
                    }
                }
                disableChangeFunc = false;

                toolMonitor = toolMonitors[tmp];

                if (toolMonitor.SelectedIndex != 0)
                {

                    for (int i = 0; i < displays.Count; i++)
                    {
                        if (displays[i].displayName.Equals(toolMonitor.Items[0].ToString().Substring(0, toolMonitor.Items[0].ToString().IndexOf(":"))))
                        {
                            comboBoxMonitors.Text = i + 1 + ") " + displays[i].displayName;
                            
                            numDisplay = i;
                            currDisplay.numDisplay = numDisplay;
                            currDisplay.displayLink = displays[i].displayLink;
                            currDisplay.isExternal = displays[i].isExternal;
                            break;
                        }
                    }

                    currDisplay.displayName = toolMonitor.Items[0].ToString().Substring(0, toolMonitor.Items[0].ToString().IndexOf(":"));

                    currDisplay.rGamma = float.Parse(iniFile.Read("rGamma", toolMonitor.Text), customCulture);
                    currDisplay.gGamma = float.Parse(iniFile.Read("gGamma", toolMonitor.Text), customCulture);
                    currDisplay.bGamma = float.Parse(iniFile.Read("bGamma", toolMonitor.Text), customCulture);
                    currDisplay.rContrast = float.Parse(iniFile.Read("rContrast", toolMonitor.Text), customCulture);
                    currDisplay.gContrast = float.Parse(iniFile.Read("gContrast", toolMonitor.Text), customCulture);
                    currDisplay.bContrast = float.Parse(iniFile.Read("bContrast", toolMonitor.Text), customCulture);
                    currDisplay.rBright = float.Parse(iniFile.Read("rBright", toolMonitor.Text), customCulture);
                    currDisplay.gBright = float.Parse(iniFile.Read("gBright", toolMonitor.Text), customCulture);
                    currDisplay.bBright = float.Parse(iniFile.Read("bBright", toolMonitor.Text), customCulture);
                    string saturationText = iniFile.Read("saturation", toolMonitor.Text);
                    int parsedSaturation;
                    if (!int.TryParse(saturationText, out parsedSaturation)) parsedSaturation = currDisplay.saturationDefault;
                    currDisplay.saturation = Clamp(parsedSaturation, currDisplay.saturationMin, currDisplay.saturationMax);
                    currDisplay.monitorBrightness = int.Parse(iniFile.Read("monitorBrightness", toolMonitor.Text));
                    currDisplay.monitorContrast = int.Parse(iniFile.Read("monitorContrast", toolMonitor.Text));

                    fillInfo(currDisplay);
                    initPresets();
                    buttonAllColors.PerformClick();

                    Gamma.SetGammaRamp(currDisplay.displayLink,
                        Gamma.CreateGammaRamp(currDisplay.rGamma, currDisplay.gGamma, currDisplay.bGamma,
                        currDisplay.rContrast, currDisplay.gContrast, currDisplay.bContrast, currDisplay.rBright, currDisplay.gBright,
                        currDisplay.bBright));
                    Saturation.Prepare(currDisplay);
            Saturation.Apply(currDisplay, currDisplay.saturation);

                    if (currDisplay.isExternal)
                    {
                        trackBarMonitorBrightness.Value = currDisplay.monitorBrightness;
                        trackBarMonitorContrast.Value = currDisplay.monitorContrast;
                    }
                    else
                    {
                        trackBarMonitorBrightness.Value = currDisplay.monitorBrightness;
                    }
                }
            }
        }
    }
}