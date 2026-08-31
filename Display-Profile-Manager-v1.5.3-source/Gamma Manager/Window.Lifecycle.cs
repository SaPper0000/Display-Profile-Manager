using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Gamma_Manager
{
    // Win32 message handling, resize/close lifecycle, tray events, and monitor toolbar events.
    public partial class Window : Form
    {
        private int _displayChangeToken = 0;
        private CancellationTokenSource _displayChangeCts = null;
        private bool _systemEventsRegistered = false;

        public void RegisterSystemEvents()
        {
            if (_systemEventsRegistered) return;
            SystemEvents.DisplaySettingsChanged += SystemEvents_DisplaySettingsChanged;
            _systemEventsRegistered = true;
        }

        public void UnregisterSystemEvents()
        {
            if (!_systemEventsRegistered) return;
            SystemEvents.DisplaySettingsChanged -= SystemEvents_DisplaySettingsChanged;
            _systemEventsRegistered = false;
        }

        private async void SystemEvents_DisplaySettingsChanged(object sender, EventArgs e)
        {
            if (isClosing || IsDisposed) return;

            try
            {
                int currentToken = unchecked(++_displayChangeToken);
                Logger.Info("Display configuration or connection status changed. Scheduling display re-enumeration...");

                // 1. 이전 비동기 하드웨어 I/O 및 대기 작업 즉시 취소
                CancelPendingHardwareRead();

                _displayChangeCts?.Cancel();
                _displayChangeCts?.Dispose();
                _displayChangeCts = new CancellationTokenSource();
                var token = _displayChangeCts.Token;

                if (displayService != null)
                {
                    displayService.NextTopologyGeneration();
                    displayService.InvalidateAllGenerations();
                }

                // 2. 드라이버 및 DDC/CI 링크 재구성 안정화 대기 (1.5초)
                await Task.Delay(1500, token);

                if (token.IsCancellationRequested || currentToken != _displayChangeToken || isClosing || IsDisposed) return;

                List<Display.DisplayInfo> freshDisplays = null;

                // 3. 백그라운드 스레드에서 기존 물리 핸들 해제 및 신규 디스플레이 탐색
                await Task.Run(() =>
                {
                    if (token.IsCancellationRequested || isClosing || IsDisposed) return;

                    var oldDisplays = displays;
                    if (oldDisplays != null)
                    {
                        if (displayService != null)
                        {
                            lock (displayService.ApplyLock)
                            {
                                Display.ReleasePhysicalMonitorHandles(oldDisplays);
                            }
                        }
                        else
                        {
                            Display.ReleasePhysicalMonitorHandles(oldDisplays);
                        }
                    }

                    if (token.IsCancellationRequested || isClosing || IsDisposed) return;

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
                }, token);

                if (token.IsCancellationRequested || currentToken != _displayChangeToken || isClosing || IsDisposed)
                {
                    // 취소되거나 폼이 종료된 상태에서 발급된 물리 핸들 즉시 안전 해제
                    if (freshDisplays != null)
                    {
                        Display.ReleasePhysicalMonitorHandles(freshDisplays);
                    }
                    return;
                }

                Action updateUI = () =>
                {
                    if (isClosing || IsDisposed) return;

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
                        if (currDisplay.saturationSupported)
                        {
                            Saturation.Apply(currDisplay, currDisplay.saturation);
                        }
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
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.Warn("DisplaySettingsChanged handler exception: " + ex.Message);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312) // WM_HOTKEY
            {
                int hotkeyId = m.WParam.ToInt32();
                if (globalHotkeys != null && globalHotkeys.TryGetValue(hotkeyId, out GlobalHotkey hotkey))
                {
                    hotkey.ProcessMessage(ref m);
                }
            }
            base.WndProc(ref m);
        }

        private void Window_FormClosed(object sender, FormClosedEventArgs e)
        {
            isClosing = true;

            // 1. 디스플레이 변경 감지 태스크 및 핸들러 취소
            _displayChangeCts?.Cancel();
            _displayChangeCts?.Dispose();
            _displayChangeCts = null;

            CancelPendingHardwareRead();
            CleanupGameAutoHook();
            UnregisterSystemEvents();

            displayService?.InvalidateAllGenerations();

            // 2. 트레이 아이콘 해제
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

            // 3. 설정 플러시 및 핫키 등록 해제
            IniFile.Shared?.Flush();
            SuspendGlobalHotkeys();

            try
            {
                // 4. 모니터 시작 상태 복원
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
                        }
                        catch (Exception ex)
                        {
                            allRestoredSuccessfully = false;
                            Logger.Error("Error during monitor state restore on form closed.", ex);
                        }
                    }
                }

                // 5. GPU 채도 및 네이티브 라이브러리 언로드
                Saturation.Reset();
                Saturation.Shutdown();

                // 6. DDC/CI 물리 핸들 및 WMI 해제
                if (copyDisplays != null)
                {
                    if (displayService != null)
                    {
                        lock (displayService.ApplyLock)
                        {
                            Display.ReleasePhysicalMonitorHandles(copyDisplays);
                        }
                    }
                    else
                    {
                        Display.ReleasePhysicalMonitorHandles(copyDisplays);
                    }
                }

                InternalMonitor.Cleanup();

                // 7. 백업 정리
                if (matchedAny)
                {
                    StartupStateManager.ClearBackup();
                    if (!allRestoredSuccessfully)
                    {
                        Logger.Info("Window_FormClosed: Display restore completed with fallback. Backup cleared.");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Error during form closed cleanup.", ex);
            }

            // 8. 캐시된 GDI 폰트 해제
            _cachedRegularFont?.Dispose();
            _cachedRegularFont = null;
            _cachedBoldFont?.Dispose();
            _cachedBoldFont = null;
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
                if (comboBoxMonitors.SelectedIndex != selectedIndex)
                {
                    comboBoxMonitors.SelectedIndex = selectedIndex;
                }
            }
        }
    }
}