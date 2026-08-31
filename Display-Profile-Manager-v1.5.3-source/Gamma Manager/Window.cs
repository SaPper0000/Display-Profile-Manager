using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Gamma_Manager
{
    public partial class Window : Form
    {
        // 1. 클래스 상단 필드에 캐싱용 변수 선언
        private Font _cachedRegularFont;
        private Font _cachedBoldFont;

        // 👈 하드웨어 비동기 읽기 작업 취소 토큰 (빠른 모니터 전환 시 경합 방지)
        private CancellationTokenSource _hardwareReadCts;

        public Window()
        {
            InitializeComponent();

            // 2. 폰트 객체를 딱 1번만 생성해서 보관
            _cachedRegularFont = new Font(buttonAllColors.Font, FontStyle.Regular);
            _cachedBoldFont = new Font(buttonAllColors.Font, FontStyle.Bold);

            ConfigureNumericBox(textBoxGamma, 0.30m, 4.40m, 1.00m, 0.05m, 2);
            ConfigureNumericBox(textBoxContrast, 0.10m, 3.00m, 1.00m, 0.01m, 2);
            ConfigureNumericBox(textBoxBrightness, -1.00m, 1.00m, 0.00m, 0.05m, 2);
            ConfigureNumericBox(textBoxSaturation, 0m, 10000m, 100m, 5m, 0);
            ConfigureNumericBox(textBoxMonitorBrightness, 0m, 100m, 50m, 1m, 0);
            ConfigureNumericBox(textBoxMonitorContrast, 0m, 100m, 50m, 1m, 0);

            EnableNumericEditing();
            try
            {
                var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (appIcon != null)
                {
                    this.Icon = appIcon;
                    notifyIcon.Icon = appIcon;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not load application icon: " + ex.Message);
            }

            // 싱글톤 공용 IniFile 인스턴스 사용
            iniFile = IniFile.Shared;

            string savedLanguage = iniFile.Read("Language", "Settings");
            LanguageManager.SetLanguage(!string.Equals(savedLanguage, "English", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(savedLanguage))
                iniFile.Write("Language", "Korean", "Settings");

            MigrateDefaultProfileLanguage();

            // 1. 가벼운 메인 UI 레이아웃 즉시 구성
            SetupModernLayout();

            // 로딩 중 UI 오조작 방지를 위해 컨트롤 패널 잠금
            SetControlCardsEnabled(false);

            string savedLogEnabled = iniFile.Read("LogEnabled", "Settings");
            bool logEnabled = string.Equals(savedLogEnabled, "True", StringComparison.OrdinalIgnoreCase) || savedLogEnabled == "1";
            checkBoxLogEnabled.CheckedChanged -= checkBoxLogEnabled_CheckedChanged;
            checkBoxLogEnabled.Checked = logEnabled;
            checkBoxLogEnabled.CheckedChanged += checkBoxLogEnabled_CheckedChanged;
            Logger.SetEnabled(logEnabled);
            if (logEnabled) Logger.Info("Logging enabled from saved Settings.");

            string savedImageOff = iniFile.Read("ImageOff", "Settings");
            bool imageOff = string.Equals(savedImageOff, "True", StringComparison.OrdinalIgnoreCase) || savedImageOff == "1";
            checkBoxImageOff.CheckedChanged -= checkBoxImageOff_CheckedChanged;
            checkBoxImageOff.Checked = imageOff;
            checkBoxImageOff.CheckedChanged += checkBoxImageOff_CheckedChanged;
            pictureBox1.Visible = !imageOff;

            string savedImageMode = iniFile.Read("SelectedImage", "Settings");
            bool useCustomImage =
                string.Equals(savedImageMode, "Custom", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(AppPaths.ImagesDirectory, "custom-banner.png"));

            if (comboBoxImageSelect != null)
            {
                comboBoxImageSelect.SelectedIndexChanged -= comboBoxImageSelect_SelectedIndexChanged;

                try
                {
                    comboBoxImageSelect.SelectedIndex = useCustomImage ? 1 : 0;
                    UpdatePictureBoxImage();
                }
                finally
                {
                    comboBoxImageSelect.SelectedIndexChanged += comboBoxImageSelect_SelectedIndexChanged;
                }
            }

            string savedTopMost = iniFile.Read("TopMost", "Settings");
            bool topMost = string.IsNullOrWhiteSpace(savedTopMost)
                ? true
                : !string.Equals(savedTopMost, "False", StringComparison.OrdinalIgnoreCase)
                    && savedTopMost != "0";
            checkBoxTopMost.Checked = topMost;
            this.TopMost = topMost;
            if (string.IsNullOrWhiteSpace(savedTopMost))
                iniFile.Write("TopMost", "True", "Settings");

            ThemeManager.SetTheme(!string.Equals(iniFile.Read("Theme", "Settings"), "Light",
                StringComparison.OrdinalIgnoreCase));
            ApplyCurrentTheme();

            buttonAllColors.Font = _cachedBoldFont;
            buttonRed.Font = _cachedRegularFont;
            buttonGreen.Font = _cachedRegularFont;
            buttonBlue.Font = _cachedRegularFont;

            // 4자리 버전(1.5.3.0)에서 3자리(v1.5.3)로 깔끔하게 정규화
            Version ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string appVersion = ver != null ? $"v{ver.ToString(3)}" : "v1.5.3";

            this.Text = "Display Profile Manager " + appVersion;
            if (notifyIcon != null) notifyIcon.Text = "Display Profile Manager " + appVersion;

            // 2. 창이 화면에 뜨자마자(Shown) 비동기 백그라운드 로딩 시작
            Shown += Window_ShownAsync;
            FormClosed += Window_FormClosed;
            // 모니터 케이블 탈착/절전 복귀 이벤트 리스너 등록
            RegisterSystemEvents();
        }

        private async void Window_ShownAsync(object sender, EventArgs e)
        {
            Shown -= Window_ShownAsync;

            try
            {
                // 👈 모니터 탐색 시작 전 대기 중이던 이전 비동기 작업 및 하드웨어 읽기 취소
                CancelPendingHardwareRead();

                if (displayService != null)
                {
                    displayService.NextTopologyGeneration();
                    displayService.InvalidateAllGenerations();
                }

                List<Display.DisplayInfo> loadedDisplays = null;

                // 1. 하드웨어 조회 및 DDC/CI 캡처 작업을 백그라운드에서 실행
                await Task.Run(() =>
                {
                    Logger.Info("Enumerating Windows displays and physical monitor handles in background.");
                    loadedDisplays = Display.QueryDisplayDevices();
                    loadedDisplays.Reverse();
                    Logger.Info("Display enumeration complete. Count=" + loadedDisplays.Count);

                    // 동일 모델 모니터가 2대 이상일 경우 hardwareId/displayLink 기준으로 항상 일관된 (#1), (#2) 부여
                    var groups = loadedDisplays
                        .Where(d => !string.IsNullOrEmpty(d.displayName))
                        .GroupBy(d => d.displayName, StringComparer.OrdinalIgnoreCase)
                        .Where(g => g.Count() > 1);

                    foreach (var group in groups)
                    {
                        // 불변 식별자인 hardwareId(우선) 및 displayLink로 정렬하여 순서 고정
                        var ordered = group.OrderBy(d => !string.IsNullOrEmpty(d.hardwareId) ? d.hardwareId : d.displayLink, StringComparer.OrdinalIgnoreCase).ToList();
                        for (int idx = 0; idx < ordered.Count; idx++)
                        {
                            ordered[idx].displayName = $"{ordered[idx].displayName} (#{idx + 1})";
                        }
                    }

                    if (loadedDisplays.Count > 0)
                    {
                        // 백업 파일이 남아있는지 확인 (비정상 종료 여부 체크)
                        if (StartupStateManager.HasPendingBackup())
                        {
                            Logger.Warn("Previous session terminated abnormally. Restoring original display settings...");
                            StartupStateManager.RestorePending(loadedDisplays);

                            // AMD ADL 및 DDC/CI 펌웨어 적용 대기
                            System.Threading.Thread.Sleep(300);
                        }

                        // 복원이 끝난 깨끗한 상태(또는 정상 부팅 상태)를 현재 세션의 원본으로 새로 캡처
                        StartupStateManager.Capture(loadedDisplays);
                    }
                });

                // 2. 폼이 작업 도중 닫혔을 경우 안전하게 중단
                if (IsDisposed || Disposing) return;

                displays = loadedDisplays ?? new List<Display.DisplayInfo>();

                // 3. UI 컨트롤 데이터 바인딩
                comboBoxMonitors.Items.Clear();
                for (int i = 0; i < displays.Count; i++)
                {
                    displays[i].numDisplay = i;
                    comboBoxMonitors.Items.Add(i + 1 + ") " + displays[i].displayName);
                }

                if (displays.Count > 0)
                {
                    currDisplay = displays[0];
                    numDisplay = 0;
                    comboBoxMonitors.SelectedIndex = 0;

                    EnsureDefaultProfile();
                    InitializeCurrentPresetState();
                    initPresets();

                    // 자동 적용 없이 복원된 현재 상태 정보만 UI에 표시
                    fillInfo(currDisplay);
                }
                initTrayMenu();
                notifyIcon.ContextMenuStrip = contextMenu;
                RefreshGlobalHotkeys();
                if (IsGameAutoEnabled())
                    SetupGameAutoHook();
            }
            catch (Exception ex)
            {
                Logger.Error("Window_ShownAsync failed.", ex);
            }
            finally
            {
                // 4. 예외가 발생하더라도 UI 조작 잠금은 반드시 해제
                SetControlCardsEnabled(true);
            }
        }

        // 👈 진행 중인 이전 하드웨어 I/O 읽기 작업을 안전하게 취소
        private void CancelPendingHardwareRead()
        {
            if (_hardwareReadCts != null)
            {
                try
                {
                    _hardwareReadCts.Cancel();
                    _hardwareReadCts.Dispose();
                }
                catch { }
                _hardwareReadCts = null;
            }
        }

        private void SetControlCardsEnabled(bool enabled)
        {
            if (displayCard != null) displayCard.Enabled = enabled;
            if (gpuCard != null) gpuCard.Enabled = enabled;
            if (monitorCard != null) monitorCard.Enabled = enabled;
            if (monitorActionCard != null) monitorActionCard.Enabled = enabled;
            if (rightCard != null) rightCard.Enabled = enabled;
        }
    }
}