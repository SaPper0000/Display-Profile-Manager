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
    public partial class Window : Form
    {
        private Button btnOsdSettings;

        private static void ConfigureNumericBox(NumericUpDown box, decimal minimum, decimal maximum, decimal value, decimal increment, int decimals)
        {
            box.Minimum = minimum;
            box.Maximum = maximum;
            box.Increment = increment;
            box.DecimalPlaces = decimals;
            box.Value = Math.Max(minimum, Math.Min(maximum, value));
            box.ReadOnly = false;
            box.TextAlign = HorizontalAlignment.Center;
            box.ThousandsSeparator = false;
            box.InterceptArrowKeys = true;
        }

        private void ApplyLanguage()
        {
            bool ko = LanguageManager.Korean;
            Text = "Display Profile Manager v1.5.3";
            notifyIcon.Text = "Display Profile Manager v1.5.3";

            buttonRed.Text = ko ? "빨강" : "Red";
            buttonGreen.Text = ko ? "초록" : "Green";
            buttonBlue.Text = ko ? "파랑" : "Blue";
            buttonAllColors.Text = ko ? "RGB\n연동" : "RGB\nLink";
            buttonReset.Text = ko ? "초기화" : "Reset";
            buttonSave.Text = ko ? "저장" : "Save";
            buttonDelete.Text = ko ? "목록 관리" : "Manage List";
            buttonHotkeys.Text = ko ? "핫키 설정" : "Hotkeys";
            if (buttonBackup != null) buttonBackup.Text = ko ? "백업" : "Backup";
            if (buttonRestore != null) buttonRestore.Text = ko ? "불러오기" : "Restore";
            if (buttonUpdateCheck != null) buttonUpdateCheck.Text = ko ? "업데이트 확인" : "Check for Updates";
            if (buttonOpenFolder != null) buttonOpenFolder.Text = ko ? "📁 설정 / 로그 폴더 열기" : "📁 Open App Folder";
            if (btnOsdSettings != null) btnOsdSettings.Text = ko ? "OSD 팝업 알림 설정" : "OSD Popup Settings";
            buttonHide.Text = ko ? "숨기기" : "Hide";
            buttonForward.Text = ko ? "다음" : "Next";
            labelGamma.Text = ko ? "감마" : "Gamma";
            labelBrightness.Text = ko ? "밝기" : "Brightness";
            labelContrast.Text = ko ? "대비" : "Contrast";
            labelSaturation.Text = currDisplay != null && currDisplay.saturationSupported
                ? (currDisplay.adapterVendor == WinApi.DisplayAdapterVendor.Nvidia ? (ko ? "디지털\n바이브런스" : "Digital\nVibrance") : (ko ? "채도" : "Saturation"))
                : (ko ? "채도 (미지원 GPU)" : "Saturation (unsupported)");
            labelMonitorBrightnessUp.Text = ko ? "밝기" : "Brightness";
            labelMonitorBrightnessDown.Text = "";
            labelMonitorContrastUp.Text = ko ? "대비" : "Contrast";
            labelMonitorContrastDown.Text = "";
            checkBoxExContrast.Text = ko ? "확장 대비\n(최대 10.00)" : "Extended\nContrast (10.00)";
            sectionDisplay.Text = ko ? "디스플레이 & 프로필" : "DISPLAY & PROFILE";
            sectionGpu.Text = ko ? "GPU 색상" : "GPU COLOR";
            sectionMonitor.Text = ko ? "모니터 설정" : "MONITOR SETTINGS";
            if (sectionMonitorActions != null) sectionMonitorActions.Text = ko ? "작업" : "ACTIONS";
            checkBoxMonitorEnabled.Text = ko ? "모니터 조절 사용" : "Enable Monitor";

            checkBoxTopMost.Text = ko ? "  창을 맨위로" : "Always on Top";
            if (topMostToolTip != null && topMostHelp != null)
            {
                string topMostTip = ko
                    ? "창 맨위로\n창을 항상 다른 창보다 위에 표시합니다.\n\n☑ ON → 창 내리기 시 알림 영역으로 숨김\n☐ OFF → 일반 Windows 프로그램처럼 작업표시줄로 최소화"
                    : "Always on Top\nKeeps the window above other windows.\n\n☑ ON → Minimizing hides the window to the system tray\n☐ OFF → Minimizes normally to the Windows taskbar";
                topMostToolTip.SetToolTip(topMostHelp, topMostTip);
                if (topMostHelpHitArea != null)
                    topMostToolTip.SetToolTip(topMostHelpHitArea, topMostTip);
            }
            if (checkBoxImageOff != null) checkBoxImageOff.Text = ko ? "이미지 끄기" : "Hide Image";
            if (checkBoxLogEnabled != null) checkBoxLogEnabled.Text = ko ? "  로그 저장" : "Save Logs";

            if (comboBoxImageSelect != null)
            {
                bool useCustomImage =
                    iniFile != null &&
                    string.Equals(
                        iniFile.Read("SelectedImage", "Settings"),
                        "Custom",
                        StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(GetCustomImagePath());

                comboBoxImageSelect.SelectedIndexChanged -= comboBoxImageSelect_SelectedIndexChanged;

                comboBoxImageSelect.BeginUpdate();
                try
                {
                    comboBoxImageSelect.Items.Clear();
                    comboBoxImageSelect.Items.Add(ko ? "기본 고양이 이미지" : "Default Cat Image");
                    comboBoxImageSelect.Items.Add(ko ? "내 이미지 선택..." : "Choose My Image...");

                    comboBoxImageSelect.SelectedIndex = useCustomImage ? 1 : 0;
                }
                finally
                {
                    comboBoxImageSelect.EndUpdate();
                    comboBoxImageSelect.SelectedIndexChanged += comboBoxImageSelect_SelectedIndexChanged;
                }
            }

            if (languageLabel != null) languageLabel.Text = ko ? "언어 (Language)" : "Language";
            if (themeLabel != null) themeLabel.Text = ko ? "테마 (Theme)" : "Theme";
            if (buttonKorean != null) buttonKorean.Text = "한국어";
            if (buttonEnglish != null) buttonEnglish.Text = "English";
            appTitle.Text = "DISPLAY PROFILE MANAGER";
            appSubtitle.Text = ko ? "디스플레이 프로필 및 색상 조절" : "Display profile & color control";
            imageCaption.Text = ko ? "화면 설정 프로필" : "DISPLAY SETTINGS";
            imageSubCaption.Text = ko ? "밝기 · 대비 · 감마 · 채도" : "Brightness · Contrast · Gamma · Saturation";
        }

        private void ApplyCurrentTheme()
        {
            ThemeManager.Apply(this);
            if (buttonTheme != null)
                buttonTheme.Text = LanguageManager.Korean
                    ? (ThemeManager.IsDark ? "☀  밝은 테마" : "☾  어두운 테마")
                    : (ThemeManager.IsDark ? "☀  Light Theme" : "☾  Dark Theme");

            Color accent = Color.FromArgb(0, 120, 215);
            Color control = ThemeManager.IsDark ? ThemeManager.DarkControl : ThemeManager.LightControl;
            Color text = ThemeManager.IsDark ? ThemeManager.DarkText : ThemeManager.LightText;
            if (buttonKorean != null)
            {
                buttonKorean.BackColor = LanguageManager.Korean ? accent : control;
                buttonKorean.ForeColor = LanguageManager.Korean ? Color.White : text;
            }
            if (buttonEnglish != null)
            {
                buttonEnglish.BackColor = !LanguageManager.Korean ? accent : control;
                buttonEnglish.ForeColor = !LanguageManager.Korean ? Color.White : text;
            }

            if (appTitle != null)
                appTitle.ForeColor = ThemeManager.IsDark ? Color.White : Color.FromArgb(25, 28, 34);
            if (appSubtitle != null)
                appSubtitle.ForeColor = ThemeManager.IsDark ? ThemeManager.DarkMuted : ThemeManager.LightMuted;
            if (sectionDisplay != null)
                sectionDisplay.ForeColor = ThemeManager.IsDark ? Color.FromArgb(120, 180, 255) : Color.FromArgb(35, 95, 160);
            if (sectionGpu != null)
                sectionGpu.ForeColor = ThemeManager.IsDark ? Color.FromArgb(120, 180, 255) : Color.FromArgb(35, 95, 160);
            if (sectionMonitor != null)
                sectionMonitor.ForeColor = ThemeManager.IsDark ? Color.FromArgb(120, 180, 255) : Color.FromArgb(35, 95, 160);
            if (sectionMonitorActions != null)
                sectionMonitorActions.ForeColor = ThemeManager.IsDark ? Color.FromArgb(120, 180, 255) : Color.FromArgb(35, 95, 160);
            if (imageCaption != null)
                imageCaption.ForeColor = ThemeManager.IsDark ? Color.White : Color.FromArgb(25, 28, 34);
            if (imageSubCaption != null)
                imageSubCaption.ForeColor = ThemeManager.IsDark ? ThemeManager.DarkMuted : ThemeManager.LightMuted;
            if (profilePriorityTitle != null)
                profilePriorityTitle.ForeColor = ThemeManager.IsDark ? Color.FromArgb(120, 180, 255) : Color.FromArgb(35, 95, 160);
            if (profilePriorityHotkey != null)
                profilePriorityHotkey.ForeColor = ThemeManager.IsDark ? Color.White : Color.FromArgb(25, 28, 34);
          

            Color card = ThemeManager.IsDark ? ThemeManager.DarkPanel : ThemeManager.LightPanel;
            Color border = ThemeManager.IsDark ? ThemeManager.DarkBorder : ThemeManager.LightBorder;
            if (displayCard != null) { displayCard.BackColor = card; displayCard.BorderColor = border; displayCard.Invalidate(); }
            if (gpuCard != null) { gpuCard.BackColor = card; gpuCard.BorderColor = border; gpuCard.Invalidate(); }
            if (rgbCard != null) { rgbCard.BackColor = card; rgbCard.BorderColor = border; rgbCard.Invalidate(); }
            if (monitorCard != null) { monitorCard.BackColor = card; monitorCard.BorderColor = border; monitorCard.Invalidate(); }
            if (monitorActionCard != null) { monitorActionCard.BackColor = card; monitorActionCard.BorderColor = border; monitorActionCard.Invalidate(); }
            if (rightCard != null) { rightCard.BackColor = card; rightCard.BorderColor = border; rightCard.Invalidate(); }
        }

        private void SetupModernLayout()
        {
            Controls.Clear();
            this.ClientSize = new Size(1080, 720);

            appTitle = CreateLabel("DISPLAY PROFILE MANAGER", new Font("Segoe UI", 24f, FontStyle.Bold), 28, 12, 660, 44);
            appSubtitle = CreateLabel(LanguageManager.Korean ? "디스플레이 프로필 및 색상 조절" : "Display profile & color control", new Font("Segoe UI", 10.5f), 28, 50, 520, 28);
            Controls.Add(appTitle);
            Controls.Add(appSubtitle);

            languageLabel = CreateLabel(LanguageManager.Korean ? "언어 (Language)" : "Language", new Font("Segoe UI", 9.5f), 28, 82, 118, 32);
            Controls.Add(languageLabel);

            buttonKorean = CreateButton(112, 36);
            buttonKorean.Location = new Point(150, 78);
            buttonKorean.Click += delegate { ChangeLanguage(true); };
            Controls.Add(buttonKorean);

            buttonEnglish = CreateButton(112, 36);
            buttonEnglish.Location = new Point(268, 78);
            buttonEnglish.Click += delegate { ChangeLanguage(false); };
            Controls.Add(buttonEnglish);

            themeLabel = CreateLabel(LanguageManager.Korean ? "테마 (Theme)" : "Theme", new Font("Segoe UI", 9.5f), 430, 82, 105, 32);
            Controls.Add(themeLabel);

            buttonTheme = CreateButton(170, 36);
            buttonTheme.Location = new Point(535, 78);
            buttonTheme.Click += buttonTheme_Click;
            Controls.Add(buttonTheme);

            displayCard = CreateCard(new Point(20, 126), new Size(690, 150));
            gpuCard = CreateCard(new Point(20, 286), new Size(690, 238));
            monitorCard = CreateCard(new Point(20, 534), new Size(480, 165));
            monitorActionCard = CreateCard(new Point(510, 534), new Size(200, 165));
            rightCard = CreateCard(new Point(725, 20), new Size(335, 679));
            Controls.Add(displayCard);
            Controls.Add(gpuCard);
            Controls.Add(monitorCard);
            Controls.Add(monitorActionCard);
            Controls.Add(rightCard);

            sectionDisplay = CreateSectionTitle("DISPLAY & PROFILE", 24, 12, 300, 28);
            sectionGpu = CreateSectionTitle("GPU COLOR", 24, 8, 300, 28);
            sectionMonitor = CreateSectionTitle("MONITOR SETTINGS", 24, 12, 135, 30);
            displayCard.Controls.Add(sectionDisplay);
            gpuCard.Controls.Add(sectionGpu);
            monitorCard.Controls.Add(sectionMonitor);

            comboBoxMonitors.Location = new Point(24, 55);
            comboBoxMonitors.Size = new Size(260, 32);
            comboBoxMonitors.DropDownStyle = ComboBoxStyle.DropDownList;
            displayCard.Controls.Add(comboBoxMonitors);

            buttonForward.Location = new Point(294, 50);
            buttonForward.Size = new Size(90, 32);
            displayCard.Controls.Add(buttonForward);

            buttonHotkeys.Size = new Size(90, 32);
            buttonHotkeys.Location = new Point(390, 50);
            displayCard.Controls.Add(buttonHotkeys);

            comboBoxPresets.Location = new Point(24, 99);
            comboBoxPresets.Size = new Size(260, 32);
            displayCard.Controls.Add(comboBoxPresets);

            buttonSave.Location = new Point(294, 94);
            buttonSave.Size = new Size(90, 32);
            displayCard.Controls.Add(buttonSave);

            buttonDelete.Location = new Point(390, 94);
            buttonDelete.Size = new Size(90, 32);
            displayCard.Controls.Add(buttonDelete);

            AddSliderRow(gpuCard, labelGamma, trackBarGamma, textBoxGamma, "Gamma", 38, 112, 245, 100);
            AddSliderRow(gpuCard, labelBrightness, trackBarBrightness, textBoxBrightness, "Brightness", 86, 112, 245, 100);
            AddSliderRow(gpuCard, labelContrast, trackBarContrast, textBoxContrast, "Contrast", 134, 112, 245, 100);
            AddSliderRow(gpuCard, labelSaturation, trackBarSaturation, textBoxSaturation, "Saturation", 182, 112, 245, 100);

            rgbCard = CreateCard(new Point(500, 18), new Size(184, 198));
            gpuCard.Controls.Add(rgbCard);

            buttonAllColors.Location = new Point(10, 30);
            buttonAllColors.Size = new Size(164, 34);
            rgbCard.Controls.Add(buttonAllColors);

            buttonRed.Location = new Point(10, 72);
            buttonRed.Size = new Size(79, 34);
            rgbCard.Controls.Add(buttonRed);

            buttonGreen.Location = new Point(95, 72);
            buttonGreen.Size = new Size(79, 34);
            rgbCard.Controls.Add(buttonGreen);

            buttonBlue.Location = new Point(10, 114);
            buttonBlue.Size = new Size(79, 34);
            rgbCard.Controls.Add(buttonBlue);

            checkBoxExContrast.AutoSize = true;
            checkBoxExContrast.Location = new Point(10, 154);
            checkBoxExContrast.Size = new Size(164, 38);
            checkBoxExContrast.TextAlign = ContentAlignment.MiddleLeft;
            rgbCard.Controls.Add(checkBoxExContrast);

            checkBoxMonitorEnabled = new CheckBox();
            checkBoxMonitorEnabled.AutoSize = true;
            checkBoxMonitorEnabled.Location = new Point(166, 16);
            checkBoxMonitorEnabled.Size = new Size(290, 32);
            checkBoxMonitorEnabled.Checked = false;
            checkBoxMonitorEnabled.Font = new Font("Segoe UI", 11.5f);
            checkBoxMonitorEnabled.ForeColor = ThemeManager.IsDark ? Color.White : Color.FromArgb(35, 38, 42);
            checkBoxMonitorEnabled.BringToFront();
            checkBoxMonitorEnabled.CheckedChanged += checkBoxMonitorEnabled_CheckedChanged;
            monitorCard.Controls.Add(checkBoxMonitorEnabled);

            AddSliderRow(monitorCard, labelMonitorBrightnessUp, trackBarMonitorBrightness, textBoxMonitorBrightness, "Brightness", 58, 112, 245, 100);
            AddSliderRow(monitorCard, labelMonitorContrastUp, trackBarMonitorContrast, textBoxMonitorContrast, "Contrast", 108, 112, 245, 100);
            checkBoxMonitorEnabled.BringToFront();

            sectionMonitorActions = CreateSectionTitle("ACTIONS", 16, 12, 160, 30);
            monitorActionCard.Controls.Add(sectionMonitorActions);
            buttonReset.Location = new Point(20, 54);
            buttonReset.Size = new Size(160, 36);
            monitorActionCard.Controls.Add(buttonReset);
            buttonHide.Location = new Point(20, 102);
            buttonHide.Size = new Size(160, 36);
            monitorActionCard.Controls.Add(buttonHide);

            labelMonitorBrightnessDown.Visible = false;
            labelMonitorContrastDown.Visible = false;

            pictureBox1.Location = new Point(18, 18);
            pictureBox1.Size = new Size(304, 190);
            pictureBox1.BorderStyle = BorderStyle.FixedSingle;
            pictureBox1.BackgroundImageLayout = ImageLayout.Zoom;
            rightCard.Controls.Add(pictureBox1);

            checkBoxImageOff = new CheckBox();
            checkBoxImageOff.AutoSize = true;
            checkBoxImageOff.Location = new Point(18, 214);

            bool imageOff =
                iniFile != null &&
                string.Equals(
                    iniFile.Read("ImageOff", "Settings"),
                    "True",
                    StringComparison.OrdinalIgnoreCase);

            checkBoxImageOff.Checked = imageOff;
            pictureBox1.Visible = !imageOff;

            checkBoxImageOff.CheckedChanged += checkBoxImageOff_CheckedChanged;
            rightCard.Controls.Add(checkBoxImageOff);

            comboBoxImageSelect = new ComboBox();
            comboBoxImageSelect.Location = new Point(130, 212);
            comboBoxImageSelect.Size = new Size(190, 28);
            comboBoxImageSelect.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxImageSelect.Font = new Font("Segoe UI", 9.5f);
            comboBoxImageSelect.SelectedIndexChanged += comboBoxImageSelect_SelectedIndexChanged;
            rightCard.Controls.Add(comboBoxImageSelect);

            imageCaption = CreateLabel(LanguageManager.Korean ? "화면 설정 프로필" : "DISPLAY SETTINGS",new Font("Segoe UI", 12.5f, FontStyle.Bold),18, 244, 304, 32);
            imageSubCaption = CreateLabel("Display profile manager", new Font("Segoe UI", 9.5f), 18, 280, 304, 26);
            rightCard.Controls.Add(imageCaption);
            rightCard.Controls.Add(imageSubCaption);

            // 1. 백업 / 불러오기 버튼
            buttonBackup = CreateButton(146, 36);
            buttonBackup.Location = new Point(18, 318);
            buttonBackup.Click += buttonBackup_Click;
            rightCard.Controls.Add(buttonBackup);

            buttonRestore = CreateButton(146, 36);
            buttonRestore.Location = new Point(176, 318);
            buttonRestore.Click += buttonRestore_Click;
            rightCard.Controls.Add(buttonRestore);

            // 2. 업데이트 확인 버튼
            buttonUpdateCheck = CreateButton(304, 36);
            buttonUpdateCheck.Location = new Point(18, 364);
            buttonUpdateCheck.Click += buttonUpdateCheck_Click;
            rightCard.Controls.Add(buttonUpdateCheck);

            // 3. 설정 / 로그 폴더 열기 버튼
            buttonOpenFolder = CreateButton(304, 36);
            buttonOpenFolder.Location = new Point(18, 410);
            buttonOpenFolder.Click += buttonOpenFolder_Click;
            rightCard.Controls.Add(buttonOpenFolder);

            // 4. OSD 팝업 알림 설정 버튼
            btnOsdSettings = CreateButton(304, 36);
            btnOsdSettings.Location = new Point(18, 456);
            btnOsdSettings.Click += (s, e) => {
                using (OSDSettingsForm osdForm = new OSDSettingsForm(iniFile))
                {
                    osdForm.ShowDialog(this);
                }
            };
            rightCard.Controls.Add(btnOsdSettings);

            // 5. 오른쪽 카드 맨 아래 체크박스 영역
            checkBoxLogEnabled = new CheckBox();
            checkBoxLogEnabled.AutoSize = true;
            checkBoxLogEnabled.Location = new Point(18, 616);
            checkBoxLogEnabled.Checked = false;
            checkBoxLogEnabled.CheckedChanged += checkBoxLogEnabled_CheckedChanged;
            rightCard.Controls.Add(checkBoxLogEnabled);

            checkBoxTopMost = new CheckBox();
            checkBoxTopMost.AutoSize = true;
            checkBoxTopMost.Location = new Point(18, 644);
            checkBoxTopMost.Checked = true;
            checkBoxTopMost.CheckedChanged += checkBoxTopMost_CheckedChanged;
            rightCard.Controls.Add(checkBoxTopMost);

            topMostHelp = new Label();
            topMostHelp.Text = "?";
            topMostHelp.TextAlign = ContentAlignment.MiddleCenter;
            topMostHelp.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            topMostHelp.Size = new Size(20, 20);
            topMostHelp.Location = new Point(125, 640);
            topMostHelp.Cursor = Cursors.Help;
            topMostHelp.BorderStyle = BorderStyle.FixedSingle;
            topMostHelp.ForeColor = ThemeManager.IsDark ? Color.White : Color.FromArgb(70, 70, 70);
            rightCard.Controls.Add(topMostHelp);

            topMostHelpHitArea = new Label();
            topMostHelpHitArea.Location = new Point(125, 640);
            topMostHelpHitArea.Size = new Size(22, 22);
            topMostHelpHitArea.Cursor = Cursors.Help;
            rightCard.Controls.Add(topMostHelpHitArea);
            topMostHelp.BringToFront();

            topMostToolTip = new ToolTip();
            topMostToolTip.AutoPopDelay = 10000;
            topMostToolTip.InitialDelay = 250;
            topMostToolTip.ReshowDelay = 100;
            topMostToolTip.ShowAlways = true;

            ApplyLanguage();
            ApplyCurrentTheme();

            // 정확한 UI 컨텐츠 크기(1080x720) 고정
            this.ClientSize = new Size(1080, 720);
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimumSize = this.Size;
            this.MaximumSize = this.Size;
        }

        private void ChangeLanguage(bool korean)
        {
            string currentPreset = comboBoxPresets != null ? comboBoxPresets.Text : null;
            string monitorName = currDisplay != null ? currDisplay.displayName : null;

            LanguageManager.SetLanguage(korean);
            if (iniFile != null)
            {
                MigrateDefaultProfileLanguage();
                iniFile.Write("Language", korean ? "Korean" : "English", "Settings");
            }

            if (!string.IsNullOrEmpty(currentPreset) && !string.IsNullOrEmpty(monitorName))
            {
                string oldDefaultPrefix = korean ? "Default - " : "기본값 - ";
                string newDefaultPrefix = korean ? "기본값 - " : "Default - ";
                if (currentPreset.StartsWith(oldDefaultPrefix, StringComparison.Ordinal))
                    currentPreset = newDefaultPrefix + monitorName;
            }

            ApplyLanguage();
            ApplyCurrentTheme();
            initTrayMenu();

            if (currDisplay != null)
            {
                currentPresetByMonitor.Clear();
                initPresets(currentPreset);
                fillInfo(currDisplay);
            }
        }

        private void MigrateDefaultProfileLanguage()
        {
            if (iniFile == null) return;

            string oldPrefix = LanguageManager.Korean ? "Default - " : "기본값 - ";
            string newPrefix = LanguageManager.Korean ? "기본값 - " : "Default - ";
            string[] sections = iniFile.GetSections();
            if (sections == null) return;

            foreach (string section in sections)
            {
                if (string.IsNullOrEmpty(section) || !section.StartsWith(oldPrefix, StringComparison.Ordinal))
                    continue;

                string monitor = iniFile.Read("monitor", section);
                if (string.IsNullOrEmpty(monitor)) continue;

                string newName = newPrefix + monitor;
                if (string.Equals(section, newName, StringComparison.Ordinal)) continue;

                if (string.IsNullOrEmpty(iniFile.Read("monitor", newName)))
                    iniFile.RenameSection(section, newName);
                else
                    iniFile.DeleteSection(section);
            }
        }

        private static Label CreateLabel(string text, Font font, int x, int y, int width, int height)
        {
            return new Label
            {
                Text = text,
                Font = font,
                Location = new Point(x, y),
                Size = new Size(width, height),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false,
                BackColor = Color.Transparent
            };
        }

        private static Label CreateSectionTitle(string text, int x, int y, int width, int height)
        {
            return CreateLabel(text, new Font("Segoe UI", 12f, FontStyle.Bold), x, y, width, height);
        }

        private static Button CreateButton(int width, int height)
        {
            return new Button
            {
                Size = new Size(width, height),
                FlatStyle = FlatStyle.Flat,
                TabStop = true,
                UseVisualStyleBackColor = false
            };
        }

        private static ModernPanel CreateCard(Point location, Size size)
        {
            return new ModernPanel
            {
                Location = location,
                Size = size,
                BorderThickness = 1,
                CornerRadius = 0,
                BackColor = ThemeManager.DarkPanel,
                Padding = new Padding(10)
            };
        }

        private static void AddSliderRow(Panel parent, Label label, TrackBar slider, NumericUpDown valueBox,
            string fallbackText, int top, int sliderX, int sliderWidth, int valueWidth)
        {
            label.AutoSize = false;
            label.Location = new Point(18, top);
            label.Size = new Size(90, 42);
            label.Font = new Font("Segoe UI", 12f);
            label.TextAlign = ContentAlignment.MiddleLeft;
            if (string.IsNullOrWhiteSpace(label.Text))
                label.Text = fallbackText;
            parent.Controls.Add(label);

            slider.Location = new Point(sliderX, top + 9);
            slider.Size = new Size(sliderWidth, 45);
            slider.TickStyle = TickStyle.None;
            parent.Controls.Add(slider);

            valueBox.Location = new Point(sliderX + sliderWidth + 8, top + 8);
            valueBox.Size = new Size(valueWidth, 30);
            valueBox.TextAlign = HorizontalAlignment.Center;
            valueBox.Font = new Font("Segoe UI", 10.5f);
            parent.Controls.Add(valueBox);
        }
    }
}