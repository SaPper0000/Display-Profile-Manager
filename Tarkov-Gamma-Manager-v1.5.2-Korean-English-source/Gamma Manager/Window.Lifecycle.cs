using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Gamma_Manager
{
    // Win32 message handling, resize/close lifecycle, tray events, and monitor toolbar events.
    public partial class Window : Form
    {
        // 모니터 변경 이벤트 중복 트리거 방지용 디바운스 토큰
        private int _displayChangeToken = 0;

        public void RegisterSystemEvents()
        {
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
        }

        public void UnregisterSystemEvents()
        {
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
        }

        private async void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            int currentToken = unchecked(++_displayChangeToken);
            Logger.Info("Display configuration or connection status changed. Scheduling handle refresh...");

            // 절전 모드 복귀 시 하드웨어 드라이버 및 DDC/CI 링크가 완전히 올라오도록 1.5초 대기
            await System.Threading.Tasks.Task.Delay(1500);

            if (currentToken != _displayChangeToken || isClosing || IsDisposed) return;

            try
            {
                if (displays != null)
                {
                    foreach (var d in displays)
                    {
                        if (d != null && d.isExternal)
                        {
                            Display.RefreshPhysicalMonitorHandle(d);
                        }
                    }
                    Logger.Info("Physical monitor handles refreshed successfully after display change event.");
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("DisplaySettingsChanged handler exception: " + ex.Message);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312) // WM_HOTKEY 메시지
            {
                int hotkeyId = m.WParam.ToInt32();
                // 루프 없이 ID로 즉시 핫키를 찾아 실행
                if (globalHotkeys != null && globalHotkeys.TryGetValue(hotkeyId, out GlobalHotkey hotkey))
                {
                    hotkey.ProcessMessage(ref m);
                }
            }
            base.WndProc(ref m);
        }

        private void Window_FormClosed(object sender, FormClosedEventArgs e)
        {
            // 0. 시스템 디스플레이 이벤트 리스너 해제 (메모리 누수 방지)
            UnregisterSystemEvents();

            // 1. 대기 중이던 비동기/디바운스 슬라이더 작업 차단
            isClosing = true;
            unchecked { brightnessDebounceToken++; }
            unchecked { contrastDebounceToken++; }

            displayService?.NextGeneration();

            // 2. 트레이 아이콘 잔상 제거 및 리소스 해제
            try
            {
                if (notifyIcon != null)
                {
                    notifyIcon.Visible = false;
                    notifyIcon.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to dispose notifyIcon: " + ex.Message);
            }

            // 3. INI 파일 저장 대기열 즉시 플러시
            IniFile.Shared?.Flush();

            // 4. 등록된 모든 글로벌 단축키 해제
            SuspendGlobalHotkeys();

            try
            {
                // 5. GPU 감마 램프 / 채도 복원 및 해제
                Saturation.Reset();
                Saturation.Shutdown();

                // 6. 프로그램 시작 시점의 모니터 원본 밝기/대비 값으로 최종 복원
                var copyDisplays = displays;
                if (displayService != null)
                {
                    lock (displayService.ApplyLock)
                    {
                        try
                        {
                            if (copyDisplays != null)
                            {
                                foreach (var display in copyDisplays)
                                {
                                    if (display == null) continue;

                                    bool restored = StartupStateManager.RestoreOriginalMonitor(display);

                                    if (!restored && !string.IsNullOrEmpty(display.displayLink))
                                    {
                                        Gamma.SetGammaRamp(display.displayLink, Gamma.CreateGammaRamp(1f, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f));
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Error during monitor state restore on form closed.", ex);
                        }
                    }
                }

                // 7. 열려있던 모든 모니터 물리 핸들을 안전하게 해제 (핸들 누수 완벽 차단)
                if (displays != null)
                {
                    Display.ReleasePhysicalMonitorHandles(displays);
                }

                // 8. 정상 복원 완료 후 불필요한 백업 파일 삭제
                StartupStateManager.ClearBackup();
            }
            catch (Exception ex)
            {
                Logger.Error("Error during form closed cleanup.", ex);
            }
        }

        private void Window_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
            }
        }

        private void RestoreWindowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            Activate();
            BringToFront();
        }

        private void notifyIcon_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            RestoreWindowFromTray();
        }

        private void notifyIcon_DoubleClick(object sender, EventArgs e)
        {
            RestoreWindowFromTray();
        }

        private void toolSettings_Click(object sender, EventArgs e)
        {
            RestoreWindowFromTray();
        }

        private void toolExit_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void comboBoxToolMonitor_IndexChanged(object sender, EventArgs e)
        {
            int selectedIndex = -1;
            if (sender is ToolStripComboBox toolBox)
            {
                selectedIndex = toolBox.SelectedIndex;
            }
            else if (sender is ComboBox cb)
            {
                selectedIndex = cb.SelectedIndex;
            }

            if (selectedIndex >= 0 && comboBoxMonitors != null && comboBoxMonitors.Items.Count > selectedIndex)
            {
                comboBoxMonitors.SelectedIndex = selectedIndex;
            }
        }
    }
}