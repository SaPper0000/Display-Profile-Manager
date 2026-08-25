using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Gamma_Manager
{
    public partial class Window : Form
    {
        public Window()
        {
            InitializeComponent();

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

            customCulture = (System.Globalization.CultureInfo)System.Threading.Thread.CurrentThread.CurrentCulture.Clone();
            customCulture.NumberFormat.NumberDecimalSeparator = ",";

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

            string savedImageIndexStr = iniFile.Read("SelectedImage", "Settings");
            int savedImageIndex = 0;
            int.TryParse(savedImageIndexStr, out savedImageIndex);
            if (savedImageIndex < 0 || savedImageIndex > 1) savedImageIndex = 0;

            if (comboBoxImageSelect != null && comboBoxImageSelect.Items.Count > savedImageIndex)
            {
                comboBoxImageSelect.SelectedIndexChanged -= comboBoxImageSelect_SelectedIndexChanged;
                comboBoxImageSelect.SelectedIndex = savedImageIndex;
                UpdatePictureBoxImage(savedImageIndex);
                comboBoxImageSelect.SelectedIndexChanged += comboBoxImageSelect_SelectedIndexChanged;
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

            buttonAllColors.Font = new Font(buttonAllColors.Font.Name, buttonAllColors.Font.Size, FontStyle.Bold);
            this.Text = "Tarkov Gamma Manager v1.5.2";

            // 2. 창이 화면에 뜨자마자(Shown) 비동기 백그라운드 로딩 시작
            Shown += Window_ShownAsync;
            FormClosed += Window_FormClosed;
            // 모니터 케이블 탈착/절전 복귀 이벤트 리스너 등록
            RegisterSystemEvents();
        }

        private async void Window_ShownAsync(object sender, EventArgs e)
        {
            Shown -= Window_ShownAsync;

            List<Display.DisplayInfo> loadedDisplays = null;

            // 1. 하드웨어 조회 및 DDC/CI 캡처 작업을 백그라운드에서 실행
            await Task.Run(() =>
            {
                Logger.Info("Enumerating Windows displays and physical monitor handles in background.");
                loadedDisplays = Display.QueryDisplayDevices();
                loadedDisplays.Reverse();
                Logger.Info("Display enumeration complete. Count=" + loadedDisplays.Count);

                if (loadedDisplays.Count > 0)
                {
                    // 백업 파일이 남아있는지 확인 (비정상 종료 여부 체크)
                    if (StartupStateManager.HasPendingBackup())
                    {
                        Logger.Warn("Previous session terminated abnormally. Restoring original display settings...");
                        StartupStateManager.RestorePending(loadedDisplays);

                        // 하드웨어 복원 안정화를 위해 0.5초 대기
                        System.Threading.Thread.Sleep(500);
                    }

                    // 정상 부팅 시점의 깨끗한 원본 디스플레이 상태를 새로 캡처
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
                fillInfo(currDisplay);

                EnsureDefaultProfile();
                InitializeCurrentPresetState();
                initPresets();
            }

            initTrayMenu();
            notifyIcon.ContextMenuStrip = contextMenu;
            RefreshGlobalHotkeys();

            // 4. 로딩 완료 후 UI 조작 잠금 해제
            SetControlCardsEnabled(true);
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