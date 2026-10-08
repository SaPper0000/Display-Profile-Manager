using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal sealed class VideoFilterExportForm : Form
    {
        private sealed class ProfileData
        {
            public string Name;
            public float rGamma, gGamma, bGamma;
            public float rContrast, gContrast, bContrast;
            public float rBright, gBright, bBright;
            public int Saturation;
            public int ShadowBoost;
            public int ShadowBoostMode;
            public int ShadowBoostTint;
            public int HighlightGuard;
            public int CustomPeak;
            public int CustomWidth;

            public float AvgGamma => (rGamma + gGamma + bGamma) / 3.0f;
            public float AvgContrast => (rContrast + gContrast + bContrast) / 3.0f;
            public float AvgBright => (rBright + gBright + bBright) / 3.0f;
        }

        private readonly IniFile iniFile;
        private readonly List<Display.DisplayInfo> displaysList;
        private Display.DisplayInfo currentSelectedDisplay;
        private readonly string initialProfile;
        private readonly bool ko;

        private Button btnHelp;
        private ComboBox comboMonitorSelect;
        private ComboBox comboProfileSelect;
        private Label lblInfo;
        private TextBox textOutputDir;
        private Button btnBrowseDir;
        private TextBox textFileName;
        private ComboBox comboEncoder;
        private TextBox textSuffix;
        private CheckBox checkGenerateLut;
        private GroupBox grpFfmpeg;
        private Label lblFfmpegStatus;
        private Button btnDownloadFfmpeg;
        private ProgressBar progressBarFfmpeg;
        private Button btnGenerate;
        private Button btnClose;

        private bool isDownloadingFfmpeg = false;
        private ProfileData currentProfileData;

        public VideoFilterExportForm(IniFile ini, List<Display.DisplayInfo> allDisplays, Display.DisplayInfo activeDisplay, string activeProfile)
        {
            iniFile = ini;
            displaysList = (allDisplays != null && allDisplays.Count > 0)
                ? allDisplays
                : (activeDisplay != null ? new List<Display.DisplayInfo> { activeDisplay } : new List<Display.DisplayInfo>());
            currentSelectedDisplay = activeDisplay ?? (displaysList.Count > 0 ? displaysList[0] : null);
            initialProfile = string.IsNullOrEmpty(activeProfile) ? "Default" : activeProfile;
            ko = LanguageManager.Korean;

            Text = ko ? "동영상/이미지 프로필 필터 적용 (.bat)" : "Apply Video/Image Profile Filter (.bat)";
            ClientSize = new Size(590, 610);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            BuildUI();
            ThemeManager.Apply(this);
        }

        public VideoFilterExportForm(IniFile ini, Display.DisplayInfo disp, string activeProfile, string[] presets)
            : this(ini, disp != null ? new List<Display.DisplayInfo> { disp } : null, disp, activeProfile)
        {
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            CheckFfmpegOnOpen();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (isDownloadingFfmpeg)
            {
                string msg = ko
                    ? "FFmpeg 엔진 다운로드가 진행 중입니다.\r\n정말 창을 닫으시겠습니까?"
                    : "FFmpeg download is in progress.\r\nAre you sure you want to close?";

                if (MessageBox.Show(this, msg, ko ? "다운로드 진행 중" : "Download in Progress", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            base.OnFormClosing(e);
        }

        private async void CheckFfmpegOnOpen()
        {
            if (IsFfmpegInstalled() || isDownloadingFfmpeg) return;

            string prompt = ko
                ? "동영상 및 이미지에 프로필 필터를 입히려면 미디어 처리 엔진(FFmpeg)이 필요합니다.\r\n현재 컴퓨터에 FFmpeg 엔진이 설치되어 있지 않습니다.\r\n\r\n지금 공식 사이트에서 자동으로 다운로드하여 설치하시겠습니까? (최초 1회만 필요)"
                : "The media processing engine (FFmpeg) is required to apply profile filters.\r\nFFmpeg is not currently installed.\r\n\r\nWould you like to download and install it now from the official source? (Required once)";

            DialogResult dr = MessageBox.Show(
                this,
                prompt,
                ko ? "FFmpeg 엔진 다운로드 안내" : "FFmpeg Engine Required",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (dr == DialogResult.Yes)
            {
                await StartFfmpegDownloadAsync();
            }
        }

        private void BuildUI()
        {
            int margin = 20;
            int clientW = ClientSize.Width;
            int contentW = clientW - margin * 2; // 550

            // 1. 헤더 타이틀
            Label lblTitle = new Label
            {
                Text = ko ? "동영상/이미지 프로필 색감 필터 적용 (.bat)" : "Apply Display Profile Filter to Video/Image (.bat)",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                Location = new Point(margin, 16),
                AutoSize = true
            };
            Controls.Add(lblTitle);

            btnHelp = new Button
            {
                Text = ko ? "📖 사용법 안내" : "📖 How to Use",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(clientW - margin - 120, 13),
                Size = new Size(120, 28),
                FlatStyle = FlatStyle.Flat
            };
            btnHelp.Click += (s, e) => ShowUsageHelp();
            Controls.Add(btnHelp);

            Label lblDesc = new Label
            {
                Text = ko
                    ? "선택한 모니터/프로필의 색감(감마/대비/밝기/채도/블랙 EQ)을 동영상 및 이미지(스크린샷)에 그대로 입혀주는\r\n'마우스 드래그 앤 드롭' 전용 필터 적용 배치 파일(.bat)을 생성합니다."
                    : "Generates a drag-and-drop batch file (.bat) that applies your selected\r\nmonitor & profile (Gamma, Contrast, Brightness, Saturation, Black EQ) to videos and images.",
                Font = new Font("Segoe UI", 9f),
                Location = new Point(margin, 46),
                Size = new Size(contentW, 36),
                ForeColor = ThemeManager.IsDark ? Color.FromArgb(170, 175, 185) : Color.FromArgb(100, 100, 100)
            };
            Controls.Add(lblDesc);

            // 2. 적용할 모니터 및 프로필 선택 카드
            GroupBox grpProfile = new GroupBox
            {
                Text = ko ? "적용할 모니터 및 화면 프로필 선택" : "Select Monitor & Profile",
                Location = new Point(margin, 88),
                Size = new Size(contentW, 178),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            // 2-1. 모니터 선택
            Label lblMonitor = new Label
            {
                Text = ko ? "모니터 선택:" : "Monitor:",
                Location = new Point(15, 26),
                Size = new Size(85, 23),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            grpProfile.Controls.Add(lblMonitor);

            comboMonitorSelect = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(105, 23),
                Size = new Size(430, 25),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            grpProfile.Controls.Add(comboMonitorSelect);

            // 2-2. 프로필 선택
            Label lblProfile = new Label
            {
                Text = ko ? "프로필 선택:" : "Profile:",
                Location = new Point(15, 60),
                Size = new Size(85, 23),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            grpProfile.Controls.Add(lblProfile);

            comboProfileSelect = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(105, 57),
                Size = new Size(430, 25),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            grpProfile.Controls.Add(comboProfileSelect);

            // 2-3. 프로필 수치 상세 표시 라벨
            lblInfo = new Label
            {
                Location = new Point(15, 93),
                Size = new Size(520, 75),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = ThemeManager.IsDark ? Color.FromArgb(220, 225, 235) : Color.FromArgb(40, 40, 40)
            };
            grpProfile.Controls.Add(lblInfo);

            Controls.Add(grpProfile);

            // 모니터 목록 구성 및 이벤트 연결
            int initialMonIdx = 0;
            for (int i = 0; i < displaysList.Count; i++)
            {
                Display.DisplayInfo d = displaysList[i];
                string itemText = (i + 1) + ") " + (d != null ? d.displayName : ("Display " + (i + 1)));
                comboMonitorSelect.Items.Add(itemText);
                if (currentSelectedDisplay != null && d != null &&
                    string.Equals(DisplayService.GetMonitorKey(d), DisplayService.GetMonitorKey(currentSelectedDisplay), StringComparison.OrdinalIgnoreCase))
                {
                    initialMonIdx = i;
                }
            }

            comboMonitorSelect.SelectedIndexChanged += (s, e) =>
            {
                int sel = comboMonitorSelect.SelectedIndex;
                if (sel >= 0 && sel < displaysList.Count)
                {
                    currentSelectedDisplay = displaysList[sel];
                    PopulateProfilesForDisplay(currentSelectedDisplay, null);
                }
            };

            comboProfileSelect.SelectedIndexChanged += (s, e) =>
            {
                UpdateSelectedProfileData();
            };

            // 3. 필터 배치 파일 생성 설정 그룹
            GroupBox grpSettings = new GroupBox
            {
                Text = ko ? "필터 배치 파일(.bat) 생성 옵션" : "Filter Batch File (.bat) Options",
                Location = new Point(margin, 276),
                Size = new Size(contentW, 170),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            // 3-1. 저장 폴더
            Label lblDir = new Label
            {
                Text = ko ? "저장 폴더:" : "Save Folder:",
                Location = new Point(15, 26),
                Size = new Size(85, 23),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            grpSettings.Controls.Add(lblDir);

            string defaultDir = iniFile?.Read("VideoFilterExportDir", "Settings");
            if (string.IsNullOrEmpty(defaultDir) || !Directory.Exists(defaultDir))
            {
                defaultDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }

            textOutputDir = new TextBox
            {
                Text = defaultDir,
                Location = new Point(105, 24),
                Size = new Size(335, 23),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            grpSettings.Controls.Add(textOutputDir);

            btnBrowseDir = new Button
            {
                Text = ko ? "찾아보기..." : "Browse...",
                Location = new Point(445, 23),
                Size = new Size(90, 26),
                Font = new Font("Segoe UI", 8.5f),
                FlatStyle = FlatStyle.Flat
            };
            btnBrowseDir.Click += (s, e) =>
            {
                using (FolderBrowserDialog fbd = new FolderBrowserDialog())
                {
                    fbd.SelectedPath = textOutputDir.Text;
                    if (fbd.ShowDialog(this) == DialogResult.OK)
                    {
                        textOutputDir.Text = fbd.SelectedPath;
                    }
                }
            };
            grpSettings.Controls.Add(btnBrowseDir);

            // 3-2. 파일명
            Label lblFile = new Label
            {
                Text = ko ? "파일 이름:" : "File Name:",
                Location = new Point(15, 60),
                Size = new Size(85, 23),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            grpSettings.Controls.Add(lblFile);

            textFileName = new TextBox
            {
                Location = new Point(105, 58),
                Size = new Size(430, 23),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            grpSettings.Controls.Add(textFileName);

            // 3-3. GPU 인코더 선택
            Label lblEncoder = new Label
            {
                Text = ko ? "하드웨어 가속:" : "GPU Encoder:",
                Location = new Point(15, 94),
                Size = new Size(85, 23),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            grpSettings.Controls.Add(lblEncoder);

            comboEncoder = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(105, 92),
                Size = new Size(430, 23),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            comboEncoder.Items.Add(ko ? "자동 감지 (NVIDIA -> AMD -> Intel -> CPU 순서) [권장]" : "Auto Detect (NVIDIA -> AMD -> Intel -> CPU) [Recommended]");
            comboEncoder.Items.Add(ko ? "NVIDIA NVENC (지포스 그래픽카드 초고속 하드웨어 가속)" : "NVIDIA NVENC (GeForce GPU Acceleration)");
            comboEncoder.Items.Add(ko ? "AMD AMF (라데온 그래픽카드 초고속 하드웨어 가속)" : "AMD AMF (Radeon GPU Acceleration)");
            comboEncoder.Items.Add(ko ? "Intel QSV (인텔 내장/외장 그래픽 초고속 하드웨어 가속)" : "Intel QuickSync QSV (Intel GPU Acceleration)");
            comboEncoder.Items.Add(ko ? "CPU 소프트웨어 인코더 (libx264 범용 호환 모드)" : "CPU Software Encoder (libx264)");
            comboEncoder.SelectedIndex = 0;
            grpSettings.Controls.Add(comboEncoder);

            // 3-4. 변환 후 접미사 및 3D LUT 옵션
            Label lblSuffix = new Label
            {
                Text = ko ? "결과물 접미사:" : "Output Suffix:",
                Location = new Point(15, 128),
                Size = new Size(85, 23),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            grpSettings.Controls.Add(lblSuffix);

            textSuffix = new TextBox
            {
                Text = ko ? "_밝게" : "_bright",
                Location = new Point(105, 125),
                Size = new Size(95, 23),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            grpSettings.Controls.Add(textSuffix);

            checkGenerateLut = new CheckBox
            {
                Text = ko ? "3D LUT 색감 보정 사용 (블랙 EQ & 정밀 RGB 100% 반영) [권장]" : "Use 3D LUT Color Engine (Full Black EQ & RGB) [Recommended]",
                Location = new Point(210, 126),
                Size = new Size(335, 23),
                Checked = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular)
            };
            grpSettings.Controls.Add(checkGenerateLut);

            Controls.Add(grpSettings);

            // 4. 미디어 처리 엔진 (FFmpeg) 상태 및 원클릭 다운로드 그룹
            grpFfmpeg = new GroupBox
            {
                Text = ko ? "미디어 처리 엔진 (FFmpeg) 상태" : "Media Engine (FFmpeg) Status",
                Location = new Point(margin, 456),
                Size = new Size(contentW, 78),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };

            lblFfmpegStatus = new Label
            {
                Location = new Point(15, 23),
                Size = new Size(395, 22),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular)
            };
            grpFfmpeg.Controls.Add(lblFfmpegStatus);

            btnDownloadFfmpeg = new Button
            {
                Text = ko ? "📥 엔진 다운로드" : "📥 Download Engine",
                Location = new Point(415, 19),
                Size = new Size(120, 28),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnDownloadFfmpeg.Click += async (s, e) => await StartFfmpegDownloadAsync();
            grpFfmpeg.Controls.Add(btnDownloadFfmpeg);

            progressBarFfmpeg = new ProgressBar
            {
                Location = new Point(15, 48),
                Size = new Size(520, 16),
                Minimum = 0,
                Maximum = 100,
                Visible = false
            };
            grpFfmpeg.Controls.Add(progressBarFfmpeg);

            Controls.Add(grpFfmpeg);

            // 5. 하단 버튼
            btnGenerate = new Button
            {
                Text = ko ? "🎬 필터 적용 (.bat) 생성" : "🎬 Generate Filter (.bat)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Location = new Point(margin, 548),
                Size = new Size(385, 44),
                FlatStyle = FlatStyle.Flat
            };
            btnGenerate.Click += BtnGenerate_Click;
            Controls.Add(btnGenerate);

            btnClose = new Button
            {
                Text = ko ? "닫기" : "Close",
                Font = new Font("Segoe UI", 9f),
                Location = new Point(margin + 395, 548),
                Size = new Size(contentW - 395, 44),
                FlatStyle = FlatStyle.Flat
            };
            btnClose.Click += (s, e) => Close();
            Controls.Add(btnClose);

            // 초기 모니터 선택 및 프로필 로드
            if (comboMonitorSelect.Items.Count > 0)
            {
                comboMonitorSelect.SelectedIndex = initialMonIdx;
            }
            else
            {
                PopulateProfilesForDisplay(currentSelectedDisplay, initialProfile);
            }

            // FFmpeg 상태 새로고침
            RefreshFfmpegStatus();
        }

        private void PopulateProfilesForDisplay(Display.DisplayInfo disp, string preferredProfile)
        {
            comboProfileSelect.Items.Clear();

            string monName = disp != null ? disp.displayName : (ko ? "모니터" : "Monitor");
            string liveItemName = ko ? $"★ [{monName} 현재 실시간 설정]" : $"★ [{monName} Current Live Settings]";
            comboProfileSelect.Items.Add(liveItemName);

            string defaultLabel = ko ? "기본값" : "Default";
            comboProfileSelect.Items.Add(defaultLabel);

            int selectIdx = 0;
            string targetProfile = preferredProfile ?? initialProfile;

            string[] presets = iniFile?.GetSections();
            if (presets != null && disp != null)
            {
                string currentKey = DisplayService.GetMonitorKey(disp);
                string targetBaseName = disp.baseDisplayName ?? disp.displayName;
                bool hasDuplicateModel = displaysList != null && displaysList.FindAll(d => d != null &&
                    string.Equals(d.baseDisplayName ?? d.displayName, targetBaseName, StringComparison.OrdinalIgnoreCase)).Count > 1;

                string defaultPrefixKo = "기본값 - ";
                string defaultPrefixEn = "Default - ";

                for (int i = 0; i < presets.Length; i++)
                {
                    string section = presets[i];
                    if (string.IsNullOrEmpty(section)) continue;

                    if (string.Equals(section, "Settings", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(section, "Hotkeys", StringComparison.OrdinalIgnoreCase) ||
                        section.StartsWith("AutoGame_", StringComparison.OrdinalIgnoreCase) ||
                        section.StartsWith("__", StringComparison.OrdinalIgnoreCase))
                        continue;

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
                            legacyNameMatch = string.Equals(monitor, disp.displayName, StringComparison.OrdinalIgnoreCase);
                        }
                        else
                        {
                            legacyNameMatch = string.Equals(monitor, disp.displayName, StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(monitor, disp.baseDisplayName, StringComparison.OrdinalIgnoreCase);
                        }
                    }

                    if (keyMatch || legacyNameMatch)
                    {
                        if (!comboProfileSelect.Items.Contains(section))
                        {
                            comboProfileSelect.Items.Add(section);
                        }
                    }
                }
            }

            // 선택 항목 결정
            if (!string.IsNullOrEmpty(targetProfile))
            {
                for (int i = 0; i < comboProfileSelect.Items.Count; i++)
                {
                    string itemStr = comboProfileSelect.Items[i].ToString();
                    if (string.Equals(itemStr, targetProfile, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(GetFriendlyProfileName(itemStr), GetFriendlyProfileName(targetProfile), StringComparison.OrdinalIgnoreCase))
                    {
                        selectIdx = i;
                        break;
                    }
                }
            }

            if (comboProfileSelect.Items.Count > 0)
            {
                if (selectIdx >= 0 && selectIdx < comboProfileSelect.Items.Count)
                    comboProfileSelect.SelectedIndex = selectIdx;
                else
                    comboProfileSelect.SelectedIndex = 0;
            }

            UpdateSelectedProfileData();
        }

        private void UpdateSelectedProfileData()
        {
            int selectedIdx = comboProfileSelect.SelectedIndex;
            if (selectedIdx < 0) return;

            string selectedItemText = comboProfileSelect.SelectedItem?.ToString() ?? string.Empty;
            bool isLive = selectedIdx == 0;

            if (isLive)
            {
                currentProfileData = new ProfileData
                {
                    Name = ko ? "현재_화면_설정" : "Current_Settings",
                    rGamma = currentSelectedDisplay != null ? currentSelectedDisplay.rGamma : 1.0f,
                    gGamma = currentSelectedDisplay != null ? currentSelectedDisplay.gGamma : 1.0f,
                    bGamma = currentSelectedDisplay != null ? currentSelectedDisplay.bGamma : 1.0f,
                    rContrast = currentSelectedDisplay != null ? currentSelectedDisplay.rContrast : 1.0f,
                    gContrast = currentSelectedDisplay != null ? currentSelectedDisplay.gContrast : 1.0f,
                    bContrast = currentSelectedDisplay != null ? currentSelectedDisplay.bContrast : 1.0f,
                    rBright = currentSelectedDisplay != null ? currentSelectedDisplay.rBright : 0.0f,
                    gBright = currentSelectedDisplay != null ? currentSelectedDisplay.gBright : 0.0f,
                    bBright = currentSelectedDisplay != null ? currentSelectedDisplay.bBright : 0.0f,
                    Saturation = currentSelectedDisplay != null && currentSelectedDisplay.saturationSupported ? currentSelectedDisplay.saturation : 100,
                    ShadowBoost = currentSelectedDisplay != null ? currentSelectedDisplay.shadowBoost : 0,
                    ShadowBoostMode = currentSelectedDisplay != null ? currentSelectedDisplay.shadowBoostMode : 0,
                    ShadowBoostTint = currentSelectedDisplay != null ? currentSelectedDisplay.shadowBoostTint : 0,
                    HighlightGuard = currentSelectedDisplay != null ? currentSelectedDisplay.highlightGuard : 0,
                    CustomPeak = currentSelectedDisplay != null ? currentSelectedDisplay.shadowBoostCustomPeak : 25,
                    CustomWidth = currentSelectedDisplay != null ? currentSelectedDisplay.shadowBoostCustomWidth : 3
                };
            }
            else
            {
                currentProfileData = ReadProfileFromIni(selectedItemText);
            }

            // 라벨 정보 갱신
            float g = currentProfileData.AvgGamma;
            float c = currentProfileData.AvgContrast;
            float b = currentProfileData.AvgBright;
            int sat = currentProfileData.Saturation;
            int sb = currentProfileData.ShadowBoost;
            int sbm = currentProfileData.ShadowBoostMode;
            string sbmText = sbm == 1 ? (ko ? "야간전" : "Night") : (sbm == 2 ? (ko ? "정밀분리" : "Precision") : (ko ? "감마커브" : "Curve"));

            string friendlyName = GetFriendlyProfileName(currentProfileData.Name);

            string infoText1 = ko
                ? $"• 대상 프로필: {friendlyName}\r\n• 감마: {g:F2}  |  대비: {c:F2}  |  밝기: {b:+0.00;-0.00;0.00}"
                : $"• Profile: {friendlyName}\r\n• Gamma: {g:F2}  |  Contrast: {c:F2}  |  Brightness: {b:+0.00;-0.00;0.00}";

            string infoText2 = ko
                ? $"• 채도: {sat}%  |  블랙 EQ: {(sb > 0 ? $"{sb}% [{sbmText}]" : "OFF")}"
                : $"• Saturation: {sat}%  |  Black EQ: {(sb > 0 ? $"{sb}% [{sbmText}]" : "OFF")}";

            lblInfo.Text = infoText1 + "\r\n" + infoText2;

            // 파일명 제안 업데이트 (FFmpeg 필터그래프 호환을 위해 대괄호 제외)
            string safeName = MakeSafeFileName(friendlyName);
            textFileName.Text = ko ? $"필터_{safeName}.bat" : $"Filter_{safeName}.bat";
        }

        private static string GetFriendlyProfileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Default";
            if (name.Contains(": "))
            {
                return name.Substring(name.IndexOf(": ") + 2).Trim();
            }
            if (name.Contains(":"))
            {
                return name.Substring(name.IndexOf(':') + 1).Trim();
            }
            return name.Trim();
        }

        private ProfileData ReadProfileFromIni(string preset)
        {
            ProfileData data = new ProfileData
            {
                Name = preset,
                rGamma = 1.0f, gGamma = 1.0f, bGamma = 1.0f,
                rContrast = 1.0f, gContrast = 1.0f, bContrast = 1.0f,
                rBright = 0.0f, gBright = 0.0f, bBright = 0.0f,
                Saturation = 100,
                ShadowBoost = 0,
                ShadowBoostMode = 0,
                ShadowBoostTint = 0,
                HighlightGuard = 0,
                CustomPeak = 25,
                CustomWidth = 3
            };

            if (iniFile == null) return data;

            // 실제 INI 섹션명 찾기
            string actualSection = preset;
            string targetKey = iniFile.Read("monitorKey", actualSection);
            string monName = iniFile.Read("monitor", actualSection);

            if (string.IsNullOrEmpty(targetKey) && string.IsNullOrEmpty(monName) && currentSelectedDisplay != null)
            {
                if (preset.Equals("기본값", StringComparison.OrdinalIgnoreCase) || preset.Equals("Default", StringComparison.OrdinalIgnoreCase))
                {
                    string defSection = (ko ? "기본값 - " : "Default - ") + currentSelectedDisplay.displayName;
                    if (!string.IsNullOrEmpty(iniFile.Read("monitor", defSection)) || !string.IsNullOrEmpty(iniFile.Read("monitorKey", defSection)))
                    {
                        actualSection = defSection;
                    }
                }
                else
                {
                    string candidate = currentSelectedDisplay.displayName + ": " + preset;
                    if (!string.IsNullOrEmpty(iniFile.Read("monitor", candidate)) || !string.IsNullOrEmpty(iniFile.Read("monitorKey", candidate)))
                    {
                        actualSection = candidate;
                    }
                }
            }

            data.rGamma = ReadIniFloat("rGamma", actualSection, 1.0f);
            data.gGamma = ReadIniFloat("gGamma", actualSection, 1.0f);
            data.bGamma = ReadIniFloat("bGamma", actualSection, 1.0f);

            data.rContrast = ReadIniFloat("rContrast", actualSection, 1.0f);
            data.gContrast = ReadIniFloat("gContrast", actualSection, 1.0f);
            data.bContrast = ReadIniFloat("bContrast", actualSection, 1.0f);

            data.rBright = ReadIniFloat("rBright", actualSection, 0.0f);
            data.gBright = ReadIniFloat("gBright", actualSection, 0.0f);
            data.bBright = ReadIniFloat("bBright", actualSection, 0.0f);

            if (int.TryParse(iniFile.Read("saturation", actualSection), out int sat))
                data.Saturation = sat;

            if (int.TryParse(iniFile.Read("shadowBoost", actualSection), out int sb))
                data.ShadowBoost = Math.Max(0, Math.Min(100, sb));

            if (int.TryParse(iniFile.Read("shadowBoostMode", actualSection), out int sbm))
                data.ShadowBoostMode = Math.Max(0, Math.Min(4, sbm));

            if (int.TryParse(iniFile.Read("shadowBoostTint", actualSection), out int sbt))
                data.ShadowBoostTint = Math.Max(0, Math.Min(2, sbt));

            if (int.TryParse(iniFile.Read("highlightGuard", actualSection), out int hg))
                data.HighlightGuard = Math.Max(0, Math.Min(100, hg));

            if (int.TryParse(iniFile.Read("shadowBoostCustomPeak", actualSection), out int peak))
                data.CustomPeak = Math.Max(10, Math.Min(40, peak));

            if (int.TryParse(iniFile.Read("shadowBoostCustomWidth", actualSection), out int width))
                data.CustomWidth = Math.Max(1, Math.Min(5, width));

            return data;
        }

        private float ReadIniFloat(string key, string section, float fallback)
        {
            string raw = iniFile.Read(key, section);
            if (string.IsNullOrWhiteSpace(raw)) return fallback;

            string normalized = raw.Trim().Replace(',', '.');
            if (float.TryParse(normalized, NumberStyles.Float | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float val))
            {
                return val;
            }
            return fallback;
        }

        private static string MakeSafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Default";
            string s = name.Replace('[', '_').Replace(']', '_')
                           .Replace('\'', '_').Replace('\"', '_')
                           .Replace(';', '_').Replace(',', '_')
                           .Replace('!', '_').Replace('%', '_')
                           .Replace('&', '_').Replace('^', '_');
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                s = s.Replace(c, '_');
            }
            return s.Trim();
        }

        private void ShowUsageHelp()
        {
            string title = ko ? "📖 동영상/이미지 프로필 필터 사용법 안내" : "📖 Video/Image Profile Filter Usage Guide";
            string helpText = ko
                ? "【 초간단 동영상 및 이미지 프로필 필터 적용 사용법 】\r\n\r\n" +
                  "1. 모니터 및 프로필 선택\r\n" +
                  "   - 상단에서 적용할 모니터를 고르고, 파일에 입히고 싶은 색감 프로필을 선택합니다.\r\n" +
                  "   - 실시간 화면 설정 또는 저장된 프리셋(감마, 대비, 밝기, 채도, 블랙 EQ)이 그대로 적용됩니다.\r\n\r\n" +
                  "2. 필터 파일(.bat) 생성\r\n" +
                  "   - 저장 폴더(기본값: 바탕화면)를 확인하고 [🎬 필터 적용 (.bat) 생성] 버튼을 누릅니다.\r\n" +
                  "   - 바탕화면에는 깔끔하게 [필터_프로필명.bat] 단일 실행 파일만 생성됩니다!\r\n" +
                  "     (3D 색상표 데이터는 전용 시스템 폴더에 안전하게 자동 보관되어 바탕화면을 어지럽히지 않습니다)\r\n\r\n" +
                  "3. 동영상, 이미지 또는 움짤 파일 마우스 드래그 & 드롭 (핵심)\r\n" +
                  "   - 동영상(.mp4, .mkv, .mov 등), 이미지(.jpg, .png, .bmp 등) 또는 움짤/애니메이션(.gif, .webp)을\r\n" +
                  "     마우스로 끌어서 생성된 .bat 파일 아이콘 위에 놓아주세요.\r\n" +
                  "   - 여러 개의 파일(영상+이미지+움짤 혼합 가능)을 한꺼번에 선택하여 던져도 순서대로 자동 적용됩니다!\r\n\r\n" +
                  "4. 결과물 확인 (원본 포맷 및 초고화질 유지)\r\n" +
                  "   - 일반 이미지는 0.1초 만에 무손실/초고화질로 즉시 필터가 입혀져 저장됩니다.\r\n" +
                  "   - GIF 및 WebP 움짤은 모든 프레임과 무한 루프를 보존하며 고품질 팔레트로 필터링됩니다.\r\n" +
                  "   - 동영상 파일은 원본 확장자(mp4, mkv, mov 등)를 유지하며 지포스/라데온/인텔 GPU 가속으로 초고속 인코딩됩니다."
                : "【 How to Use Video/Image Profile Filter 】\r\n\r\n" +
                  "1. Select Monitor & Profile\r\n" +
                  "   - Select the target monitor, then pick the display profile to apply.\r\n" +
                  "   - Live settings or saved presets (Gamma, Contrast, Brightness, Saturation, Black EQ) are applied.\r\n\r\n" +
                  "2. Generate Filter File\r\n" +
                  "   - Click [🎬 Generate Filter (.bat)] to create your clean standalone filter .bat file.\r\n" +
                  "     (3D LUT color tables are stored cleanly in the app data folder without cluttering your desktop)\r\n\r\n" +
                  "3. Drag & Drop Videos, Images or Animations\r\n" +
                  "   - Simply drag video files (.mp4, .mkv, etc.), images (.jpg, .png, etc.), or GIFs / WebP animations\r\n" +
                  "     and drop them onto the generated .bat file icon.\r\n" +
                  "   - Drag multiple files at once for sequential batch processing.\r\n\r\n" +
                  "4. Output Media (Preserves Quality & Original Format)\r\n" +
                  "   - Static images are processed in 0.1s with maximum quality.\r\n" +
                  "   - Animated GIF and WebP files preserve all frames and looping with high fidelity.\r\n" +
                  "   - Videos are hardware accelerated via NVENC, AMF, or QSV with zero quality loss!";

            MessageBox.Show(this, helpText, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string GetGlobalLutDir()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string lutDir = Path.Combine(localAppData, "DisplayProfileManager", "luts");
            if (!Directory.Exists(lutDir))
            {
                try { Directory.CreateDirectory(lutDir); } catch { }
            }
            return lutDir;
        }

        private static string GetGlobalFfmpegDir()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string binDir = Path.Combine(localAppData, "DisplayProfileManager", "bin");
            if (!Directory.Exists(binDir))
            {
                try { Directory.CreateDirectory(binDir); } catch { }
            }
            return binDir;
        }

        private static string GetGlobalFfmpegPath()
        {
            return Path.Combine(GetGlobalFfmpegDir(), "ffmpeg.exe");
        }

        private bool IsFfmpegInstalled()
        {
            if (File.Exists(GetGlobalFfmpegPath())) return true;
            if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"))) return true;
            if (IsFfmpegInPath()) return true;
            return false;
        }

        private static bool IsFfmpegInPath()
        {
            try
            {
                using (var p = new System.Diagnostics.Process())
                {
                    p.StartInfo.FileName = "where";
                    p.StartInfo.Arguments = "ffmpeg";
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.UseShellExecute = false;
                    p.Start();
                    p.WaitForExit(1000);
                    return p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        private void RefreshFfmpegStatus()
        {
            if (lblFfmpegStatus == null) return;

            bool installed = IsFfmpegInstalled();
            if (installed)
            {
                lblFfmpegStatus.Text = ko
                    ? "● FFmpeg 엔진: 준비 완료 (초고속 필터 적용 가능)"
                    : "● FFmpeg Engine: Ready (GPU Acceleration Active)";
                lblFfmpegStatus.ForeColor = Color.FromArgb(46, 204, 113);
                if (btnDownloadFfmpeg != null) btnDownloadFfmpeg.Visible = false;
                if (progressBarFfmpeg != null) progressBarFfmpeg.Visible = false;
            }
            else
            {
                lblFfmpegStatus.Text = ko
                    ? "● FFmpeg 미설치 (필터 적용을 위해 엔진 다운로드 필요)"
                    : "● FFmpeg Not Installed (Download required for video filter)";
                lblFfmpegStatus.ForeColor = Color.FromArgb(231, 76, 60);
                if (btnDownloadFfmpeg != null)
                {
                    btnDownloadFfmpeg.Visible = true;
                    btnDownloadFfmpeg.Enabled = !isDownloadingFfmpeg;
                }
            }
        }

        private async Task<bool> StartFfmpegDownloadAsync()
        {
            if (isDownloadingFfmpeg) return false;
            isDownloadingFfmpeg = true;

            btnDownloadFfmpeg.Enabled = false;
            btnGenerate.Enabled = false;
            progressBarFfmpeg.Visible = true;
            progressBarFfmpeg.Value = 0;

            string targetExe = GetGlobalFfmpegPath();
            string tempZip = Path.Combine(Path.GetTempPath(), $"ffmpeg_{Guid.NewGuid():N}.zip");

            // 초고속 CDN 미러 목록 (1차: 한국 Incheon Edge CDN 지원 GitHub 공식 빌드, 2차: gyan.dev)
            string[] downloadUrls = new[]
            {
                "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip",
                "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip"
            };

            lblFfmpegStatus.ForeColor = ThemeManager.IsDark ? Color.FromArgb(220, 225, 235) : Color.FromArgb(40, 40, 40);
            lblFfmpegStatus.Text = ko ? "FFmpeg 초고속 CDN 연결 중..." : "Connecting to FFmpeg CDN...";

            bool downloadSuccess = false;

            try
            {
                // TLS 1.2 / 1.3 활성화
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)3072;

                foreach (string url in downloadUrls)
                {
                    try
                    {
                        using (var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate })
                        using (var client = new HttpClient(handler))
                        {
                            client.Timeout = TimeSpan.FromMinutes(5);
                            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) DisplayProfileManager");

                            using (var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                            {
                                response.EnsureSuccessStatusCode();

                                long totalBytes = response.Content.Headers.ContentLength ?? -1L;

                                using (var contentStream = await response.Content.ReadAsStreamAsync())
                                using (var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                                {
                                    byte[] buffer = new byte[81920];
                                    long totalRead = 0L;
                                    int bytesRead;
                                    var sw = System.Diagnostics.Stopwatch.StartNew();
                                    long lastReport = 0;

                                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                    {
                                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                                        totalRead += bytesRead;

                                        // UI 갱신을 100ms마다 한 번씩만 호출하여 메시지 큐 병목 및 UI 프리징 완벽 방지
                                        if (sw.ElapsedMilliseconds - lastReport > 100)
                                        {
                                            lastReport = sw.ElapsedMilliseconds;
                                            int pct = totalBytes > 0 ? (int)((totalRead * 100) / totalBytes) : 0;
                                            pct = Math.Max(0, Math.Min(100, pct));
                                            long readMb = totalRead / (1024 * 1024);
                                            long totalMb = totalBytes > 0 ? totalBytes / (1024 * 1024) : 0;

                                            if (!IsDisposed)
                                            {
                                                BeginInvoke(new Action(() =>
                                                {
                                                    progressBarFfmpeg.Value = pct;
                                                    lblFfmpegStatus.Text = totalBytes > 0
                                                        ? (ko ? $"FFmpeg 다운로드 중... {pct}% ({readMb}MB / {totalMb}MB)"
                                                              : $"Downloading FFmpeg... {pct}% ({readMb}MB / {totalMb}MB)")
                                                        : (ko ? $"FFmpeg 다운로드 중... ({readMb}MB)"
                                                              : $"Downloading FFmpeg... ({readMb}MB)");
                                                }));
                                            }
                                        }
                                    }
                                }
                            }
                        }

                        downloadSuccess = true;
                        break;
                    }
                    catch
                    {
                        if (File.Exists(tempZip))
                        {
                            try { File.Delete(tempZip); } catch { }
                        }
                    }
                }

                if (!downloadSuccess)
                {
                    throw new Exception(ko ? "다운로드 서버 연결에 실패했습니다. 네트워크를 확인해 주세요." : "Failed to connect to download servers.");
                }

                lblFfmpegStatus.Text = ko ? "엔진 압축 해제 및 설치 중..." : "Extracting and installing...";

                await Task.Run(() =>
                {
                    string targetDir = Path.GetDirectoryName(targetExe);
                    if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
                    {
                        Directory.CreateDirectory(targetDir);
                    }

                    using (ZipArchive archive = ZipFile.OpenRead(tempZip))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            if (entry.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                            {
                                entry.ExtractToFile(targetExe, true);
                                break;
                            }
                        }
                    }
                });

                if (!IsDisposed)
                {
                    MessageBox.Show(
                        this,
                        ko ? "FFmpeg 엔진 다운로드 및 설치가 성공적으로 완료되었습니다!\r\n이제 동영상에 자유롭게 프로필 필터를 입힐 수 있습니다."
                           : "FFmpeg engine has been successfully downloaded and installed!\r\nYou can now apply profile filters to videos.",
                        ko ? "설치 완료" : "Installation Complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return true;
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    MessageBox.Show(
                        this,
                        (ko ? "다운로드 중 오류가 발생했습니다:\r\n" : "Download error:\r\n") + ex.Message,
                        ko ? "다운로드 실패" : "Download Failed",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempZip)) File.Delete(tempZip);
                }
                catch { }

                isDownloadingFfmpeg = false;
                if (!IsDisposed)
                {
                    btnGenerate.Enabled = true;
                    RefreshFfmpegStatus();
                }
            }
        }

        private async void BtnGenerate_Click(object sender, EventArgs e)
        {
            if (currentProfileData == null) return;

            // FFmpeg 미설치 시 안내 및 다운로드 유도
            if (!IsFfmpegInstalled())
            {
                string prompt = ko
                    ? "동영상에 프로필 필터를 입히려면 FFmpeg 엔진이 필요합니다.\r\n\r\n지금 공식 사이트에서 자동으로 다운로드하시겠습니까? (최초 1회만 필요)"
                    : "The FFmpeg engine is required to apply video profile filters.\r\n\r\nWould you like to download it now from the official source? (Required once)";

                if (MessageBox.Show(this, prompt, ko ? "FFmpeg 엔진 다운로드 안내" : "FFmpeg Engine Required", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    bool downloaded = await StartFfmpegDownloadAsync();
                    if (!downloaded || !IsFfmpegInstalled())
                    {
                        return;
                    }
                }
                else
                {
                    return;
                }
            }

            string outDir = textOutputDir.Text.Trim();
            if (string.IsNullOrEmpty(outDir) || !Directory.Exists(outDir))
            {
                MessageBox.Show(
                    ko ? "올바른 저장 폴더를 선택해 주세요." : "Please choose a valid destination folder.",
                    ko ? "폴더 오류" : "Folder Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string fileName = textFileName.Text.Trim();
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = $"필터_{MakeSafeFileName(currentProfileData.Name)}.bat";
            }
            if (!fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                fileName += ".bat";
            }

            string batPath = Path.Combine(outDir, fileName);
            // FFmpeg filtergraph 파서 호환을 위해 .cube 파일명에서는 대괄호 및 따옴표 완전 제거
            string baseFileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName).Replace("[", "_").Replace("]", "_").Replace("'", "_").Replace("\"", "_");
            string lutFileName = baseFileNameWithoutExt + ".cube";
            // .cube 파일은 사용자의 작업 폴더(바탕화면)를 어지럽히지 않도록 %LOCALAPPDATA%\DisplayProfileManager\luts 에 전용 보관
            string globalLutDir = GetGlobalLutDir();
            string lutPath = Path.Combine(globalLutDir, lutFileName);

            float g = currentProfileData.AvgGamma;
            float c = currentProfileData.AvgContrast;
            float b = currentProfileData.AvgBright;
            float satFactor = currentProfileData.Saturation / 100.0f;
            int sb = currentProfileData.ShadowBoost;
            int sbm = currentProfileData.ShadowBoostMode;
            string suffix = textSuffix.Text.Trim();
            if (string.IsNullOrEmpty(suffix)) suffix = "_밝게";

            try
            {
                // 1. 3D LUT 파일 생성 (요청 시)
                bool lutGenerated = false;
                if (checkGenerateLut.Checked)
                {
                    GenerateCubeLutFile(lutPath, currentProfileData);
                    lutGenerated = true;
                }

                // 2. 배치 파일 생성
                string batContent = BuildBatchScriptContent(
                    currentProfileData.Name,
                    g, c, b, satFactor, sb, sbm,
                    suffix,
                    comboEncoder.SelectedIndex,
                    lutGenerated ? lutFileName : null);

                File.WriteAllText(batPath, batContent, Encoding.GetEncoding(949)); // ANSI/CP949로 저장하여 윈도우 cmd에서 한글 100% 깨짐 방지

                // 저장 폴더 기억
                iniFile?.Write("VideoFilterExportDir", outDir, "Settings");

                string displayProfileName = GetFriendlyProfileName(currentProfileData.Name);
                string successMsg = ko
                    ? $"동영상/이미지 프로필 필터 배치가 성공적으로 생성되었습니다!\r\n\r\n• 대상 프로필: {displayProfileName}\r\n• 파일 위치: {batPath}\r\n\r\n[사용법]\r\n동영상 또는 이미지(스크린샷) 파일을 이 파일 위에 마우스로 끌어다 놓으면(드래그&드롭) 프로필 색감이 그대로 입혀져 저장됩니다.\r\n\r\n저장된 폴더를 지금 여시겠습니까?"
                    : $"Video/image profile filter batch generated successfully!\r\n\r\n• Profile: {displayProfileName}\r\n• Path: {batPath}\r\n\r\n[Usage]\r\nDrag and drop video or image files onto this .bat file to apply your display profile filter.\r\n\r\nOpen the folder now?";

                if (MessageBox.Show(this, successMsg, ko ? "생성 완료" : "Generated Successfully", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{batPath}\"");
                    }
                    catch { }
                }

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    (ko ? "파일 생성 중 오류가 발생했습니다:\r\n" : "Error creating converter file:\r\n") + ex.Message,
                    ko ? "오류" : "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static void GenerateCubeLutFile(string filePath, ProfileData p)
        {
            const int size = 33;
            float rG = p.rGamma;
            float gG = p.gGamma;
            float bG = p.bGamma;

            float rC = p.rContrast;
            float gC = p.gContrast;
            float bC = p.bContrast;

            float rB = p.rBright;
            float gB = p.gBright;
            float bB = p.bBright;

            int sb = p.ShadowBoost;
            int sbm = p.ShadowBoostMode;
            int hg = p.HighlightGuard;
            int peak = p.CustomPeak > 0 ? p.CustomPeak : 25;
            int width = p.CustomWidth > 0 ? p.CustomWidth : 3;

            float satFactor = p.Saturation / 100.0f;

            using (StreamWriter sw = new StreamWriter(filePath, false, Encoding.ASCII))
            {
                sw.WriteLine("# Created by Display Profile Manager v1.5.7");
                sw.WriteLine($"TITLE \"DisplayProfileManager_{GetFriendlyProfileName(p.Name)}\"");
                sw.WriteLine($"LUT_3D_SIZE {size}");
                sw.WriteLine("DOMAIN_MIN 0.0 0.0 0.0");
                sw.WriteLine("DOMAIN_MAX 1.0 1.0 1.0");

                for (int b = 0; b < size; b++)
                {
                    double inB = (double)b / (size - 1);
                    double curB = Gamma.ApplyChannelCurve(inB, bG, bC, bB, sb, sbm, 1.0, hg, peak, width);

                    for (int g = 0; g < size; g++)
                    {
                        double inG = (double)g / (size - 1);
                        double curG = Gamma.ApplyChannelCurve(inG, gG, gC, gB, sb, sbm, 1.0, hg, peak, width);

                        for (int r = 0; r < size; r++)
                        {
                            double inR = (double)r / (size - 1);
                            double curR = Gamma.ApplyChannelCurve(inR, rG, rC, rB, sb, sbm, 1.0, hg, peak, width);

                            double outR = curR;
                            double outG = curG;
                            double outB = curB;

                            if (Math.Abs(satFactor - 1.0f) > 0.001f)
                            {
                                // Rec.709 / sRGB 표준 휘도 가중치로 채도 변환
                                double gray = 0.2126 * curR + 0.7152 * curG + 0.0722 * curB;
                                outR = gray + (curR - gray) * satFactor;
                                outG = gray + (curG - gray) * satFactor;
                                outB = gray + (curB - gray) * satFactor;
                            }

                            outR = Math.Max(0.0, Math.Min(1.0, outR));
                            outG = Math.Max(0.0, Math.Min(1.0, outG));
                            outB = Math.Max(0.0, Math.Min(1.0, outB));

                            sw.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F6} {1:F6} {2:F6}", outR, outG, outB));
                        }
                    }
                }
            }
        }

        private static string BuildBatchScriptContent(
            string profile,
            float gamma, float contrast, float brightness, float satFactor,
            int shadowBoost, int shadowBoostMode,
            string suffix,
            int encoderChoice,
            string lutFileName)
        {
            string gStr = gamma.ToString("0.00", CultureInfo.InvariantCulture);
            string cStr = contrast.ToString("0.00", CultureInfo.InvariantCulture);
            string bStr = brightness.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
            string sStr = satFactor.ToString("0.00", CultureInfo.InvariantCulture);
            string cleanTitle = GetFriendlyProfileName(profile).Replace("\"", "");

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 949 >nul");
            sb.AppendLine("setlocal enabledelayedexpansion");
            sb.AppendLine("pushd \"%~dp0\"");
            sb.AppendLine("");
            sb.AppendLine($"title 동영상/이미지 프로필 필터 적용기 - {cleanTitle}");
            sb.AppendLine("");
            sb.AppendLine(":: ==============================================================================");
            sb.AppendLine(":: [Display Profile Manager v1.5.7] 초고속 드래그 앤 드롭 동영상/이미지 프로필 필터");
            sb.AppendLine($":: 대상 프로필: {cleanTitle}");
            sb.AppendLine($":: 적용 수치  : 감마={gStr}, 대비={cStr}, 밝기={bStr}, 채도={sStr}, 블랙EQ={shadowBoost}%");
            sb.AppendLine("::");
            sb.AppendLine(":: [사용법] 동영상(.mp4, .mkv, .mov 등), 이미지(.jpg, .png 등), 움짤(.gif, .webp) 파일을");
            sb.AppendLine(":: 이 배치 파일 아이콘 위에 마우스로 끌어다 놓으세요(드래그 앤 드롭).");
            sb.AppendLine(":: 여러 개를 동시에 던져도 순서대로 자동 적용됩니다!");
            sb.AppendLine(":: ==============================================================================");
            sb.AppendLine("");
            sb.AppendLine(":: ==================== 수치 직접 수정 (언제든 변경 가능) ====================");
            sb.AppendLine($"set \"GAMMA={gStr}\"");
            sb.AppendLine($"set \"CONTRAST={cStr}\"");
            sb.AppendLine($"set \"BRIGHTNESS={bStr}\"");
            sb.AppendLine($"set \"SATURATION={sStr}\"");
            sb.AppendLine($"set \"SUFFIX={suffix}\"");
            sb.AppendLine(":: ==============================================================================");
            sb.AppendLine("");
            sb.AppendLine("if \"%~1\"==\"\" goto :show_usage");
            sb.AppendLine("goto :check_ffmpeg");
            sb.AppendLine("");
            sb.AppendLine(":show_usage");
            sb.AppendLine("cls");
            sb.AppendLine("echo ==============================================================================");
            sb.AppendLine("echo  [안내] 필터를 입힐 미디어(동영상, 이미지, GIF/WebP 움짤) 파일을 마우스로 끌어서 놓아주세요.");
            sb.AppendLine("echo ==============================================================================");
            sb.AppendLine("echo.");
            sb.AppendLine("echo  현재 적용된 필터 수치:");
            sb.AppendLine($"echo   - 대상 프로필        : {cleanTitle}");
            sb.AppendLine("echo   - 감마 [Gamma]       : !GAMMA!");
            sb.AppendLine("echo   - 대비 [Contrast]    : !CONTRAST!");
            sb.AppendLine("echo   - 밝기 [Brightness]  : !BRIGHTNESS!");
            sb.AppendLine("echo   - 채도 [Saturation]  : !SATURATION!");
            sb.AppendLine($"echo   - 블랙 EQ [ShadowBoost]: {shadowBoost}%%");
            sb.AppendLine("echo.");
            sb.AppendLine("echo  * 메모장으로 이 파일을 열면 상단에서 숫자를 직접 수정할 수 있습니다.");
            sb.AppendLine("echo.");
            sb.AppendLine("echo 아무 키나 누르면 창이 닫힙니다...");
            sb.AppendLine("pause >nul");
            sb.AppendLine("popd");
            sb.AppendLine("exit /b 0");
            sb.AppendLine("");
            sb.AppendLine(":check_ffmpeg");
            sb.AppendLine(":: 1. FFmpeg 존재 확인 (중앙 공용 폴더 -> 배치 파일 폴더 -> 시스템 PATH 순서)");
            sb.AppendLine("set \"FFMPEG_CMD=\"");
            sb.AppendLine("if exist \"%LOCALAPPDATA%\\DisplayProfileManager\\bin\\ffmpeg.exe\" (");
            sb.AppendLine("    set \"FFMPEG_CMD=%LOCALAPPDATA%\\DisplayProfileManager\\bin\\ffmpeg.exe\"");
            sb.AppendLine(") else if exist \"%~dp0ffmpeg.exe\" (");
            sb.AppendLine("    set \"FFMPEG_CMD=%~dp0ffmpeg.exe\"");
            sb.AppendLine(") else (");
            sb.AppendLine("    where ffmpeg >nul 2>&1");
            sb.AppendLine("    if not errorlevel 1 (");
            sb.AppendLine("        set \"FFMPEG_CMD=ffmpeg\"");
            sb.AppendLine("    )");
            sb.AppendLine(")");
            sb.AppendLine("");
            sb.AppendLine("if \"%FFMPEG_CMD%\"==\"\" (");
            sb.AppendLine("    cls");
            sb.AppendLine("    echo ==============================================================================");
            sb.AppendLine("    echo [경고] FFmpeg를 찾을 수 없습니다!");
            sb.AppendLine("    echo 초고속 영상 변환을 위해 ffmpeg.exe 가 필요합니다.");
            sb.AppendLine("    echo.");
            sb.AppendLine("    echo 1. 같은 폴더에 ffmpeg.exe 를 넣어주시거나");
            sb.AppendLine("    echo 2. 지금 엔터를 누르시면 winget 또는 공식 다운로드를 진행합니다.");
            sb.AppendLine("    echo ==============================================================================");
            sb.AppendLine("    echo.");
            sb.AppendLine("    set /p \"DL_OPT=엔터를 누르면 다운로드를 진행합니다 (종료는 N 입력): \"");
            sb.AppendLine("    if /i \"!DL_OPT!\"==\"n\" (");
            sb.AppendLine("        popd");
            sb.AppendLine("        exit /b 1");
            sb.AppendLine("    )");
            sb.AppendLine("    echo.");
            sb.AppendLine("    echo [다운로드 중...] 잠시만 기다려주세요...");
            sb.AppendLine("    winget install Gyan.FFmpeg --accept-source-agreements --accept-package-agreements >nul 2>&1");
            sb.AppendLine("    where ffmpeg >nul 2>&1");
            sb.AppendLine("    if not errorlevel 1 (");
            sb.AppendLine("        set \"FFMPEG_CMD=ffmpeg\"");
            sb.AppendLine("        echo [완료] FFmpeg 설치가 완료되었습니다!");
            sb.AppendLine("    ) else (");
            sb.AppendLine("        echo [안내] winget 자동 설치에 실패했습니다. 공식 경량 빌드를 직접 다운로드합니다...");
            sb.AppendLine("        powershell -NoProfile -Command \"Invoke-WebRequest -Uri 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip' -OutFile '%temp%\\ff.zip'; Expand-Archive -Path '%temp%\\ff.zip' -DestinationPath '%temp%\\ff_ext' -Force; Get-ChildItem -Path '%temp%\\ff_ext' -Filter 'ffmpeg.exe' -Recurse | Select-Object -First 1 | Copy-Item -Destination '%~dp0ffmpeg.exe' -Force; Remove-Item '%temp%\\ff.zip' -Force -ErrorAction SilentlyContinue; Remove-Item '%temp%\\ff_ext' -Recurse -Force -ErrorAction SilentlyContinue\"");
            sb.AppendLine("        if exist \"%~dp0ffmpeg.exe\" (");
            sb.AppendLine("            set \"FFMPEG_CMD=%~dp0ffmpeg.exe\"");
            sb.AppendLine("            echo [완료] ffmpeg.exe 준비가 완료되었습니다!");
            sb.AppendLine("        ) else (");
            sb.AppendLine("            echo [오류] 다운로드에 실패했습니다. 수동으로 ffmpeg.exe를 이 폴더에 넣어주세요.");
            sb.AppendLine("            pause");
            sb.AppendLine("            popd");
            sb.AppendLine("            exit /b 1");
            sb.AppendLine("        )");
            sb.AppendLine("    )");
            sb.AppendLine(")");
            sb.AppendLine("");
            sb.AppendLine(":: 2. 하드웨어 가속 인코더 선택");

            switch (encoderChoice)
            {
                case 1: // NVIDIA
                    sb.AppendLine("set \"VCODEC=h264_nvenc\"");
                    sb.AppendLine("set \"EXTRA_OPTS=-preset p4 -rc vbr -cq 17 -b:v 0 -spatial-aq 1 -temporal-aq 1\"");
                    break;
                case 2: // AMD
                    sb.AppendLine("set \"VCODEC=h264_amf\"");
                    sb.AppendLine("set \"EXTRA_OPTS=-quality balanced -rc cqp -qp_p 17 -qp_i 17\"");
                    break;
                case 3: // Intel QSV
                    sb.AppendLine("set \"VCODEC=h264_qsv\"");
                    sb.AppendLine("set \"EXTRA_OPTS=-preset medium -global_quality 17\"");
                    break;
                case 4: // CPU
                    sb.AppendLine("set \"VCODEC=libx264\"");
                    sb.AppendLine("set \"EXTRA_OPTS=-preset faster -crf 17 -aq-mode 2\"");
                    break;
                default: // Auto
                    sb.AppendLine(":: 자동 감지 (지포스 -> 라데온 -> 인텔 -> CPU)");
                    sb.AppendLine("set \"VCODEC=libx264\"");
                    sb.AppendLine("set \"EXTRA_OPTS=-preset faster -crf 17 -aq-mode 2\"");
                    sb.AppendLine("\"%FFMPEG_CMD%\" -hide_banner -encoders 2>&1 | findstr /i \"h264_nvenc\" >nul");
                    sb.AppendLine("if not errorlevel 1 (");
                    sb.AppendLine("    set \"VCODEC=h264_nvenc\"");
                    sb.AppendLine("    set \"EXTRA_OPTS=-preset p4 -rc vbr -cq 17 -b:v 0 -spatial-aq 1 -temporal-aq 1\"");
                    sb.AppendLine(") else (");
                    sb.AppendLine("    \"%FFMPEG_CMD%\" -hide_banner -encoders 2>&1 | findstr /i \"h264_amf\" >nul");
                    sb.AppendLine("    if not errorlevel 1 (");
                    sb.AppendLine("        set \"VCODEC=h264_amf\"");
                    sb.AppendLine("        set \"EXTRA_OPTS=-quality balanced -rc cqp -qp_p 17 -qp_i 17\"");
                    sb.AppendLine("    ) else (");
                    sb.AppendLine("        \"%FFMPEG_CMD%\" -hide_banner -encoders 2>&1 | findstr /i \"h264_qsv\" >nul");
                    sb.AppendLine("        if not errorlevel 1 (");
                    sb.AppendLine("            set \"VCODEC=h264_qsv\"");
                    sb.AppendLine("            set \"EXTRA_OPTS=-preset medium -global_quality 17\"");
                    sb.AppendLine("        )");
                    sb.AppendLine("    )");
                    sb.AppendLine(")");
                    break;
            }

            sb.AppendLine(":: 3. 색감 필터 설정 (전용 시스템 폴더 및 로컬 폴더 2중 탐색)");
            if (!string.IsNullOrEmpty(lutFileName))
            {
                sb.AppendLine($"set \"LUT_NAME={lutFileName}\"");
                sb.AppendLine("set \"SAFE_LUT_PATH=\"");
                sb.AppendLine("if exist \"%~dp0!LUT_NAME!\" (");
                sb.AppendLine("    set \"SAFE_LUT_PATH=%~dp0!LUT_NAME!\"");
                sb.AppendLine(") else if exist \"%LOCALAPPDATA%\\DisplayProfileManager\\luts\\!LUT_NAME!\" (");
                sb.AppendLine("    set \"SAFE_LUT_PATH=%LOCALAPPDATA%\\DisplayProfileManager\\luts\\!LUT_NAME!\"");
                sb.AppendLine(")");
                sb.AppendLine("");
                sb.AppendLine("if not \"!SAFE_LUT_PATH!\"==\"\" (");
                sb.AppendLine("    set \"FF_LUT=!SAFE_LUT_PATH:\\=/!\"");
                sb.AppendLine("    set \"FF_LUT=!FF_LUT::=\\:!\"");
                sb.AppendLine("    set \"VF_FILTER=lut3d='!FF_LUT!'\"");
                sb.AppendLine(") else (");
                sb.AppendLine("    set \"VF_FILTER=eq=gamma=!GAMMA!:contrast=!CONTRAST!:brightness=!BRIGHTNESS!:saturation=!SATURATION!\"");
                sb.AppendLine(")");
            }
            else
            {
                sb.AppendLine("set \"VF_FILTER=eq=gamma=!GAMMA!:contrast=!CONTRAST!:brightness=!BRIGHTNESS!:saturation=!SATURATION!\"");
            }

            sb.AppendLine("");
            sb.AppendLine("cls");
            sb.AppendLine("echo ==============================================================================");
            sb.AppendLine("echo  동영상/이미지 색감 변환 작업을 시작합니다. (비디오 인코더: !VCODEC!)");
            sb.AppendLine("echo ==============================================================================");
            sb.AppendLine("echo.");
            sb.AppendLine("set \"PROCESSED_COUNT=0\"");
            sb.AppendLine("set \"FAIL_COUNT=0\"");
            sb.AppendLine("");
            sb.AppendLine(":process_loop");
            sb.AppendLine("if \"%~1\"==\"\" goto :all_done");
            sb.AppendLine("");
            sb.AppendLine("if exist \"%~1\\\" (");
            sb.AppendLine("    echo [건너뜀] 폴더는 변환할 수 없습니다: \"%~nx1\"");
            sb.AppendLine("    shift");
            sb.AppendLine("    goto :process_loop");
            sb.AppendLine(")");
            sb.AppendLine("");
            sb.AppendLine("set \"IN_FILE=%~1\"");
            sb.AppendLine("set \"OUT_BASE=%~dp1%~n1!SUFFIX!\"");
            sb.AppendLine("set \"OUT_EXT=%~x1\"");
            sb.AppendLine("set \"OUT_FILE=!OUT_BASE!!OUT_EXT!\"");
            sb.AppendLine("");
            sb.AppendLine("if /i \"!IN_FILE!\"==\"!OUT_FILE!\" (");
            sb.AppendLine("    set \"OUT_BASE=%~dp1%~n1_filtered\"");
            sb.AppendLine("    set \"OUT_FILE=!OUT_BASE!!OUT_EXT!\"");
            sb.AppendLine(")");
            sb.AppendLine("");
            sb.AppendLine(":: 중복 파일 방지 (2), (3)... 자동 순번 부여");
            sb.AppendLine("if exist \"!OUT_FILE!\" (");
            sb.AppendLine("    set \"DUP_IDX=2\"");
            sb.AppendLine("    set \"FOUND_DUP=\"");
            sb.AppendLine("    for /l %%N in (2,1,999) do (");
            sb.AppendLine("        if not defined FOUND_DUP (");
            sb.AppendLine("            if not exist \"!OUT_BASE! (%%N)!OUT_EXT!\" (");
            sb.AppendLine("                set \"DUP_IDX=%%N\"");
            sb.AppendLine("                set \"FOUND_DUP=1\"");
            sb.AppendLine("            )");
            sb.AppendLine("        )");
            sb.AppendLine("    )");
            sb.AppendLine("    set \"OUT_FILE=!OUT_BASE! (!DUP_IDX!)!OUT_EXT!\"");
            sb.AppendLine(")");
            sb.AppendLine("for %%F in (\"!OUT_FILE!\") do set \"OUT_NAME=%%~nxF\"");
            sb.AppendLine("");
            sb.AppendLine(":: 파일 확장자 판별 (GIF 움짤 vs WebP vs 일반 이미지 vs 동영상)");
            sb.AppendLine("set \"EXT=%~x1\"");
            sb.AppendLine("if /i \"!EXT!\"==\".gif\" goto :process_gif");
            sb.AppendLine("if /i \"!EXT!\"==\".webp\" goto :process_webp");
            sb.AppendLine("for %%E in (.jpg .jpeg .png .bmp .tiff .tif) do (");
            sb.AppendLine("    if /i \"!EXT!\"==\"%%E\" goto :process_image");
            sb.AppendLine(")");
            sb.AppendLine("goto :process_video");
            sb.AppendLine("");
            sb.AppendLine(":process_gif");
            sb.AppendLine("echo ------------------------------------------------------------------------------");
            sb.AppendLine("echo [GIF(움짤) 필터 적용 중] \"%~nx1\" --^> \"!OUT_NAME!\"");
            sb.AppendLine("echo ------------------------------------------------------------------------------");
            sb.AppendLine("\"%FFMPEG_CMD%\" -y -hide_banner -loglevel warning -i \"!IN_FILE!\" -vf \"!VF_FILTER!,split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse\" -loop 0 \"!OUT_FILE!\"");
            sb.AppendLine("if errorlevel 1 (");
            sb.AppendLine("        echo.");
            sb.AppendLine("        echo [오류] \"%~nx1\" GIF 변환 실패!");
            sb.AppendLine("        set /a \"FAIL_COUNT+=1\"");
            sb.AppendLine(") else (");
            sb.AppendLine("        echo.");
            sb.AppendLine("        echo [성공] GIF 필터 적용 완료: \"!OUT_NAME!\"");
            sb.AppendLine("        set /a \"PROCESSED_COUNT+=1\"");
            sb.AppendLine(")");
            sb.AppendLine("goto :finish_current_item");
            sb.AppendLine("");
            sb.AppendLine(":process_webp");
            sb.AppendLine("echo ------------------------------------------------------------------------------");
            sb.AppendLine("echo [WebP(이미지/움짤) 필터 적용 중] \"%~nx1\" --^> \"!OUT_NAME!\"");
            sb.AppendLine("echo ------------------------------------------------------------------------------");
            sb.AppendLine("\"%FFMPEG_CMD%\" -y -hide_banner -loglevel warning -i \"!IN_FILE!\" -vf \"!VF_FILTER!\" -c:v libwebp -quality 95 -loop 0 \"!OUT_FILE!\"");
            sb.AppendLine("if errorlevel 1 (");
            sb.AppendLine("        echo.");
            sb.AppendLine("        echo [오류] \"%~nx1\" WebP 변환 실패!");
            sb.AppendLine("        set /a \"FAIL_COUNT+=1\"");
            sb.AppendLine(") else (");
            sb.AppendLine("        echo.");
            sb.AppendLine("        echo [성공] WebP 필터 적용 완료: \"!OUT_NAME!\"");
            sb.AppendLine("        set /a \"PROCESSED_COUNT+=1\"");
            sb.AppendLine(")");
            sb.AppendLine("goto :finish_current_item");
            sb.AppendLine("");
            sb.AppendLine(":process_image");
            sb.AppendLine("echo ------------------------------------------------------------------------------");
            sb.AppendLine("echo [이미지 필터 적용 중] \"%~nx1\" --^> \"!OUT_NAME!\"");
            sb.AppendLine("echo ------------------------------------------------------------------------------");
            sb.AppendLine("set \"IMG_OPTS=\"");
            sb.AppendLine("if /i \"!EXT!\"==\".jpg\" set \"IMG_OPTS=-q:v 2\"");
            sb.AppendLine("if /i \"!EXT!\"==\".jpeg\" set \"IMG_OPTS=-q:v 2\"");
            sb.AppendLine("\"%FFMPEG_CMD%\" -y -hide_banner -loglevel warning -i \"!IN_FILE!\" -vf \"!VF_FILTER!\" -frames:v 1 -update 1 !IMG_OPTS! \"!OUT_FILE!\"");
            sb.AppendLine("if errorlevel 1 (");
            sb.AppendLine("        echo.");
            sb.AppendLine("        echo [오류] \"%~nx1\" 이미지 변환 실패!");
            sb.AppendLine("        set /a \"FAIL_COUNT+=1\"");
            sb.AppendLine(") else (");
            sb.AppendLine("        echo.");
            sb.AppendLine("        echo [성공] 이미지 필터 적용 완료: \"!OUT_NAME!\"");
            sb.AppendLine("        set /a \"PROCESSED_COUNT+=1\"");
            sb.AppendLine(")");
            sb.AppendLine("goto :finish_current_item");
            sb.AppendLine("");
            sb.AppendLine(":process_video");
            sb.AppendLine("echo ------------------------------------------------------------------------------");
            sb.AppendLine("echo [동영상 변환 진행 중] \"%~nx1\" --^> \"!OUT_NAME!\"");
            sb.AppendLine("echo ------------------------------------------------------------------------------");
            sb.AppendLine("\"%FFMPEG_CMD%\" -y -hide_banner -loglevel warning -stats -i \"!IN_FILE!\" -vf \"!VF_FILTER!\" -c:v !VCODEC! !EXTRA_OPTS! -pix_fmt yuv420p -c:a copy \"!OUT_FILE!\"");
            sb.AppendLine("if errorlevel 1 (");
            sb.AppendLine("        echo [안내] 오디오 스트림 호환 AAC 모드로 재시도합니다...");
            sb.AppendLine("        \"%FFMPEG_CMD%\" -y -hide_banner -loglevel warning -stats -i \"!IN_FILE!\" -vf \"!VF_FILTER!\" -c:v !VCODEC! !EXTRA_OPTS! -pix_fmt yuv420p -c:a aac -b:a 192k \"!OUT_FILE!\"");
            sb.AppendLine(")");
            sb.AppendLine("if errorlevel 1 (");
            sb.AppendLine("        echo.");
            sb.AppendLine("        echo [오류] \"%~nx1\" 동영상 변환 실패!");
            sb.AppendLine("        set /a \"FAIL_COUNT+=1\"");
            sb.AppendLine(") else (");
            sb.AppendLine("        echo.");
            sb.AppendLine("        echo [성공] 동영상 필터 적용 완료: \"!OUT_NAME!\"");
            sb.AppendLine("        set /a \"PROCESSED_COUNT+=1\"");
            sb.AppendLine(")");
            sb.AppendLine("goto :finish_current_item");
            sb.AppendLine("");
            sb.AppendLine(":finish_current_item");
            sb.AppendLine("shift");
            sb.AppendLine("goto :process_loop");
            sb.AppendLine("");
            sb.AppendLine(":all_done");
            sb.AppendLine("echo.");
            sb.AppendLine("echo ==============================================================================");
            sb.AppendLine("echo  모든 미디어(동영상/이미지) 변환 작업이 완료되었습니다!");
            sb.AppendLine("echo  - 성공: !PROCESSED_COUNT!건");
            sb.AppendLine("echo  - 실패: !FAIL_COUNT!건");
            sb.AppendLine("echo ==============================================================================");
            sb.AppendLine("echo.");
            sb.AppendLine("echo 아무 키나 누르면 창이 닫힙니다...");
            sb.AppendLine("pause >nul");
            sb.AppendLine("popd");
            sb.AppendLine("exit /b 0");

            return sb.ToString();
        }
    }
}
