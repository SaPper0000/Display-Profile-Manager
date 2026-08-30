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
            try
            {
                int currentToken = unchecked(++_displayChangeToken);
                Logger.Info("Display configuration or connection status changed. Invalidating previous tasks and scheduling display re-enumeration...");

                // 👈 1. 핫플러그 감지 즉시 이전 모든 비동기 하드웨어 I/O 작업 및 읽기 즉각 무효화
                CancelPendingHardwareRead();
                if (displayService != null)
                {
                    displayService.NextTopologyGeneration();
                    displayService.InvalidateAllGenerations();
                }

                // 2. 드라이버 및 DDC/CI 링크가 완전히 재구성될 때까지 1.5초 대기
                await System.Threading.Tasks.Task.Delay(1500);

                if (currentToken != _displayChangeToken || isClosing || IsDisposed) return;

                List<Display.DisplayInfo> freshDisplays = null;

                // 3. DDC/CI 및 WMI 조회가 UI를 멈추지 않도록 백그라운드 스레드에서 기존 핸들 안전 정리 후 모니터 재검색
                await System.Threading.Tasks.Task.Run(() =>
                {
                    if (displays != null)
                    {
                        if (displayService != null)
                        {
                            lock (displayService.ApplyLock)
                            {
                                Display.ReleasePhysicalMonitorHandles(displays);
                            }
                        }
                        else
                        {
                            Display.ReleasePhysicalMonitorHandles(displays);
                        }
                    }

                    freshDisplays = Display.QueryDisplayDevices();
                    freshDisplays.Reverse();

                    var groups = freshDisplays
                         .Where(d => !string.IsNullOrEmpty(d.displayName))
                         .GroupBy(d => d.displayName, StringComparer.OrdinalIgnoreCase)
                         .Where(g => g.Count() > 1);

                    foreach (var group in groups)
                    {
                        var ordered = group.OrderBy(d => !string.IsNullOrEmpty(d.hardwareId) ? d.hardwareId : d.displayLink, StringComparer.OrdinalIgnoreCase).ToList();
                        for (int idx = 0; idx < ordered.Count; idx++)
                        {
                            ordered[idx].displayName = $"{ordered[idx].displayName} (#{idx + 1})";
                        }
                    }
                });

                if (currentToken != _displayChangeToken || isClosing || IsDisposed) return;

                Action updateUI = () =>
                {
                    displays = freshDisplays ?? new List<Display.DisplayInfo>();

                    comboBoxMonitors.Items.Clear();
                    for (int i = 0; i < displays.Count; i++)
                    {
                        displays[i].numDisplay = i;
                        comboBoxMonitors.Items.Add(i + 1 + ") " + displays[i].displayName);
                    }

                    if (displays.Count > 0)
                    {
                        int targetIndex = 0;
                        if (currDisplay != null)
                        {
                            string currKey = DisplayService.GetMonitorKey(currDisplay);
                            int found = displays.FindIndex(d => string.Equals(DisplayService.GetMonitorKey(d), currKey, StringComparison.OrdinalIgnoreCase));
                            if (found >= 0) targetIndex = found;
                        }

                        numDisplay = targetIndex;
                        currDisplay = displays[targetIndex];
                        comboBoxMonitors.SelectedIndex = targetIndex;

                        fillInfo(currDisplay);
                        Saturation.Apply(currDisplay, currDisplay.saturation);
                        initPresets();
                    }
                    else
                    {
                        currDisplay = null;
                        numDisplay = -1;
                    }

                    initTrayMenu();
                    RefreshGlobalHotkeys();
                    Logger.Info("Displays re-enumerated and UI updated successfully.");
                };

                if (InvokeRequired) BeginInvoke(updateUI);
                else updateUI();
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
            // 0. 시스템 디스플레이 이벤트 리스너 해제
            UnregisterSystemEvents();

            // 1. 대기 중이던 비동기/디바운스 슬라이더 작업 차단
            isClosing = true;


            // 수정 후 (프로그램 종료 시 전체 모니터 비동기 큐 취소)
            displayService?.InvalidateAllGenerations();

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
                // 5. [복원] 프로그램 시작 시점의 윈도우 원본 밝기/대비/감마 값으로 먼저 복원
                bool allRestoredSuccessfully = true;
                bool matchedAny = false;
                var copyDisplays = displays;

                if (displayService != null)
                {
                    lock (displayService.ApplyLock)
                    {
                        try
                        {
                            if (copyDisplays != null && copyDisplays.Count > 0)
                            {
                                foreach (var display in copyDisplays)
                                {
                                    if (display == null) continue;
                                    matchedAny = true;

                                    bool restored = StartupStateManager.RestoreOriginalMonitor(display);

                                    if (!restored)
                                    {
                                        allRestoredSuccessfully = false;
                                        if (!string.IsNullOrEmpty(display.displayLink))
                                        {
                                            Gamma.SetGammaRamp(display.displayLink, Gamma.CreateGammaRamp(1f, 1f, 1f, 1f, 1f, 1f, 0f, 0f, 0f));
                                        }
                                    }
                                }
                            }
                            else
                            {
                                allRestoredSuccessfully = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            allRestoredSuccessfully = false;
                            Logger.Error("Error during monitor state restore on form closed.", ex);
                        }
                    }
                }

                // 6. GPU 채도 리셋 및 종료
                Saturation.Reset();
                Saturation.Shutdown();

                // 7. 열려있던 모든 모니터 물리 핸들 및 WMI 인스턴스 해제
                if (displays != null)
                {
                    if (displayService != null)
                    {
                        lock (displayService.ApplyLock)
                        {
                            Display.ReleasePhysicalMonitorHandles(displays);
                        }
                    }
                    else
                    {
                        Display.ReleasePhysicalMonitorHandles(displays);
                    }
                }

                InternalMonitor.Cleanup();

                // 8. 정상 종료 시 모니터 감마 복원이 성공했으면 백업 파일 삭제 (DDC 미지원 기기로 인한 무한 복구 루프 방지)
                if (matchedAny && allRestoredSuccessfully)
                {
                    StartupStateManager.ClearBackup();
                }
                else if (matchedAny)
                {
                    // 최소한의 안전장치: 백업 파일 정리하여 다음 부팅 시 반복 루프 차단
                    StartupStateManager.ClearBackup();
                    Logger.Info("Window_FormClosed: Display restore completed with fallback. Backup cleared.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error during form closed cleanup.", ex);
            }
            _cachedRegularFont?.Dispose();
            _cachedBoldFont?.Dispose();
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