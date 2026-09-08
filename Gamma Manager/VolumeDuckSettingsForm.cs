using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal sealed class VolumeDuckSettingsForm : Form
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private readonly IniFile ini;
        private readonly bool ko;

        private CheckBox chkEnabled;
        private Label lblDesc;

        // 적용 대상 라디오 버튼 & 프로세스 선택
        private RadioButton rbTargetMaster;
        private RadioButton rbTargetActiveWindow;
        private RadioButton rbTargetSpecificProcess;
        private ComboBox comboProcess;
        private Button btnRefreshProcess;

        private Button btnHotkey;
        private Button btnClearHotkey;
        private Label lblStatus;
        private TrackBar trackVolume;
        private NumericUpDown numVolume;
        private CheckBox chkOsd;

        private ComboBox comboOsdColor;
        private ComboBox comboOsdPosition;
        private NumericUpDown numOsdDuration;
        private NumericUpDown numOsdFontSize;
        private CheckBox chkPersistentOsd;

        private Button btnSave;
        private Button btnClose;

        private Keys currentKey = Keys.None;
        private GlobalHotkey.Modifiers currentModifiers = GlobalHotkey.Modifiers.None;
        private bool isCapturing = false;

        private sealed class Item
        {
            public string Text { get; }
            public string Value { get; }
            public Item(string text, string value) { Text = text; Value = value; }
            public override string ToString() { return Text; }
        }

        public VolumeDuckSettingsForm(IniFile iniFile)
        {
            ini = iniFile;
            ko = LanguageManager.Korean;
            Text = ko ? "즉시 볼륨 전환 설정" : "Quick Volume Switch Settings";
            Width = 530;
            Height = 635;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            KeyDown += VolumeDuckSettingsForm_KeyDown;

            BuildUI();
            LoadSettings();
            ThemeManager.Apply(this);

            if (lblDesc != null)
            {
                lblDesc.ForeColor = ThemeManager.IsDark
                    ? Color.FromArgb(190, 196, 206)
                    : Color.FromArgb(80, 85, 95);
            }
        }

        private void BuildUI()
        {
            Label lblTitle = new Label
            {
                Text = ko ? "즉시 볼륨 전환 환경설정" : "Quick Volume Switch Preferences",
                Location = new Point(20, 16),
                AutoSize = true,
                Font = new Font("Segoe UI", 11, FontStyle.Bold)
            };
            Controls.Add(lblTitle);

            lblDesc = new Label
            {
                Text = ko
                    ? "토글 단축키를 누르면 지정한 볼륨으로 즉시 전환되며,\r\n다시 누르면 원래 볼륨으로 복원됩니다."
                    : "Press the toggle hotkey to switch to the target volume,\r\nand press again to restore the original volume.",
                Location = new Point(20, 42),
                Size = new Size(475, 36),
                Font = new Font("Segoe UI", 9f)
            };
            Controls.Add(lblDesc);

            // 전체 활성화 체크박스
            chkEnabled = new CheckBox
            {
                Text = ko ? " 즉시 볼륨 전환 기능 사용" : " Enable Quick Volume Switch",
                Location = new Point(20, 82),
                Size = new Size(470, 24),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            Controls.Add(chkEnabled);

            // 적용 대상 그룹 라벨
            Label lblTargetGroup = new Label
            {
                Text = ko ? "적용 대상:" : "Target:",
                Location = new Point(20, 114),
                Size = new Size(110, 24),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            Controls.Add(lblTargetGroup);

            rbTargetMaster = new RadioButton
            {
                Text = ko ? "전체 시스템 마스터 볼륨 (기본)" : "Master Volume (All PC)",
                Location = new Point(135, 114),
                Size = new Size(340, 22),
                Font = new Font("Segoe UI", 9f),
                Checked = true
            };
            Controls.Add(rbTargetMaster);

            rbTargetActiveWindow = new RadioButton
            {
                Text = ko ? "현재 활성화된 창/게임 (자동 감지)" : "Active Window/Game (Auto)",
                Location = new Point(135, 138),
                Size = new Size(340, 22),
                Font = new Font("Segoe UI", 9f)
            };
            Controls.Add(rbTargetActiveWindow);

            rbTargetSpecificProcess = new RadioButton
            {
                Text = ko ? "지정한 프로그램:" : "Specific Program:",
                Location = new Point(135, 162),
                Size = new Size(130, 24),
                Font = new Font("Segoe UI", 9f)
            };
            Controls.Add(rbTargetSpecificProcess);

            comboProcess = new ComboBox
            {
                Location = new Point(265, 163),
                Size = new Size(150, 26),
                Font = new Font("Segoe UI", 9f)
            };
            Controls.Add(comboProcess);

            btnRefreshProcess = new Button
            {
                Text = ko ? "새로고침" : "Refresh",
                Location = new Point(420, 162),
                Size = new Size(70, 27),
                Font = new Font("Segoe UI", 8.5f)
            };
            btnRefreshProcess.Click += (s, e) => PopulateAudioProcesses();
            Controls.Add(btnRefreshProcess);

            rbTargetSpecificProcess.CheckedChanged += (s, e) =>
            {
                comboProcess.Enabled = rbTargetSpecificProcess.Checked;
                btnRefreshProcess.Enabled = rbTargetSpecificProcess.Checked;
            };

            // 단축키 행
            Label lblHkTitle = new Label
            {
                Text = ko ? "토글 단축키:" : "Toggle Hotkey:",
                Location = new Point(20, 198),
                Size = new Size(110, 28),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(lblHkTitle);

            btnHotkey = new Button
            {
                Location = new Point(135, 196),
                Size = new Size(245, 32),
                Font = new Font("Segoe UI", 9.5f)
            };
            btnHotkey.Click += (s, e) => BeginCapture();
            Controls.Add(btnHotkey);

            btnClearHotkey = new Button
            {
                Text = ko ? "해제" : "Clear",
                Location = new Point(390, 196),
                Size = new Size(85, 32),
                Font = new Font("Segoe UI", 9.5f)
            };
            btnClearHotkey.Click += (s, e) => ClearHotkey();
            Controls.Add(btnClearHotkey);

            lblStatus = new Label
            {
                Location = new Point(135, 230),
                Size = new Size(340, 20),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.DarkOrange
            };
            Controls.Add(lblStatus);

            // 전환할 볼륨 슬라이더 행
            Label lblVolTitle = new Label
            {
                Text = ko ? "전환할 볼륨:" : "Target Volume:",
                Location = new Point(20, 256),
                Size = new Size(110, 28),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(lblVolTitle);

            trackVolume = new TrackBar
            {
                Location = new Point(130, 253),
                Size = new Size(230, 45),
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                Value = 10
            };
            Controls.Add(trackVolume);

            numVolume = new NumericUpDown
            {
                Location = new Point(365, 256),
                Size = new Size(65, 28),
                Minimum = 0,
                Maximum = 100,
                Value = 10,
                TextAlign = HorizontalAlignment.Center,
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(numVolume);

            Label lblPercent = new Label
            {
                Text = "%",
                Location = new Point(434, 256),
                Size = new Size(25, 28),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9.5f)
            };
            Controls.Add(lblPercent);

            trackVolume.ValueChanged += (s, e) =>
            {
                if (numVolume.Value != trackVolume.Value)
                    numVolume.Value = trackVolume.Value;
            };

            numVolume.ValueChanged += (s, e) =>
            {
                if (trackVolume.Value != (int)numVolume.Value)
                    trackVolume.Value = (int)numVolume.Value;
            };

            // OSD 구분 패널 / 체크박스
            chkOsd = new CheckBox
            {
                Text = ko ? " 볼륨 변경 시 OSD 팝업 알림 표시" : " Show OSD popup on volume toggle",
                Location = new Point(20, 302),
                Size = new Size(470, 24),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            Controls.Add(chkOsd);

            // OSD 색상
            Label lblOsdColor = new Label
            {
                Text = ko ? "OSD 색상:" : "OSD Color:",
                Location = new Point(40, 336),
                Size = new Size(95, 26),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f)
            };
            comboOsdColor = new ComboBox
            {
                Location = new Point(140, 335),
                Size = new Size(230, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9f)
            };
            comboOsdColor.Items.Add(new Item(ko ? "초록 (LimeGreen)" : "LimeGreen", "LimeGreen"));
            comboOsdColor.Items.Add(new Item(ko ? "노랑 (Yellow)" : "Yellow", "Yellow"));
            comboOsdColor.Items.Add(new Item(ko ? "하늘색 (SkyBlue)" : "SkyBlue", "SkyBlue"));
            comboOsdColor.Items.Add(new Item(ko ? "흰색 (White)" : "White", "White"));
            comboOsdColor.Items.Add(new Item(ko ? "오렌지 (Orange)" : "Orange", "Orange"));
            comboOsdColor.Items.Add(new Item(ko ? "빨강 (Red)" : "Red", "Red"));
            Controls.Add(lblOsdColor);
            Controls.Add(comboOsdColor);

            // OSD 위치
            Label lblOsdPos = new Label
            {
                Text = ko ? "OSD 위치:" : "OSD Position:",
                Location = new Point(40, 374),
                Size = new Size(95, 26),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f)
            };
            comboOsdPosition = new ComboBox
            {
                Location = new Point(140, 373),
                Size = new Size(230, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9f)
            };
            comboOsdPosition.Items.Add(new Item(ko ? "상단 중앙 (Top Center)" : "Top Center", "TopCenter"));
            comboOsdPosition.Items.Add(new Item(ko ? "좌측 상단 (Top Left)" : "Top Left", "TopLeft"));
            comboOsdPosition.Items.Add(new Item(ko ? "우측 상단 (Top Right)" : "Top Right", "TopRight"));
            Controls.Add(lblOsdPos);
            Controls.Add(comboOsdPosition);

            // OSD 표시 시간
            Label lblOsdDur = new Label
            {
                Text = ko ? "표시 시간:" : "Duration:",
                Location = new Point(40, 412),
                Size = new Size(70, 26),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f)
            };
            numOsdDuration = new NumericUpDown
            {
                Location = new Point(115, 411),
                Size = new Size(60, 28),
                Minimum = 0.5m,
                Maximum = 10.0m,
                Increment = 0.5m,
                DecimalPlaces = 1,
                Value = 1.5m,
                TextAlign = HorizontalAlignment.Center,
                Font = new Font("Segoe UI", 9f)
            };
            Label lblSec = new Label
            {
                Text = ko ? "초" : "sec",
                Location = new Point(180, 412),
                Size = new Size(30, 26),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f)
            };
            Controls.Add(lblOsdDur);
            Controls.Add(numOsdDuration);
            Controls.Add(lblSec);

            // OSD 폰트 크기
            Label lblOsdSize = new Label
            {
                Text = ko ? "폰트 크기:" : "Font Size:",
                Location = new Point(225, 412),
                Size = new Size(75, 26),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f)
            };
            numOsdFontSize = new NumericUpDown
            {
                Location = new Point(305, 411),
                Size = new Size(60, 28),
                Minimum = 20m,
                Maximum = 48m,
                Increment = 2m,
                Value = 32m,
                TextAlign = HorizontalAlignment.Center,
                Font = new Font("Segoe UI", 9f)
            };
            Label lblPt = new Label
            {
                Text = "pt",
                Location = new Point(370, 412),
                Size = new Size(30, 26),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9f)
            };
            Controls.Add(lblOsdSize);
            Controls.Add(numOsdFontSize);
            Controls.Add(lblPt);

            // OSD 상시 유지 체크박스
            chkPersistentOsd = new CheckBox
            {
                Text = ko ? " 볼륨 전환 상태 동안 OSD 계속 표시 (상시 유지)" : " Keep OSD visible while volume is switched (Persistent)",
                Location = new Point(40, 448),
                Size = new Size(450, 24),
                Font = new Font("Segoe UI", 9f)
            };
            Controls.Add(chkPersistentOsd);

            // OSD 체크박스 켜고 끌 때 하위 설정 컨트롤 활성/비활성 연동
            chkOsd.CheckedChanged += (s, e) =>
            {
                bool en = chkOsd.Checked;
                comboOsdColor.Enabled = en;
                comboOsdPosition.Enabled = en;
                numOsdDuration.Enabled = en;
                numOsdFontSize.Enabled = en;
                chkPersistentOsd.Enabled = en;
            };

            // 하단 버튼
            btnSave = new Button
            {
                Text = ko ? "저장" : "Save",
                Location = new Point(290, 520),
                Size = new Size(100, 36),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                DialogResult = DialogResult.OK
            };
            btnSave.Click += (s, e) => SaveSettings();
            Controls.Add(btnSave);

            btnClose = new Button
            {
                Text = ko ? "닫기" : "Close",
                Location = new Point(400, 520),
                Size = new Size(90, 36),
                Font = new Font("Segoe UI", 9.5f),
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(btnClose);
            AcceptButton = btnSave;
            CancelButton = btnClose;

            PopulateAudioProcesses();
        }

        private void PopulateAudioProcesses()
        {
            string currentText = comboProcess.Text;
            comboProcess.Items.Clear();

            List<string> procs = AudioManager.GetActiveAudioProcessNames();
            foreach (string p in procs)
            {
                comboProcess.Items.Add(p);
            }

            if (!string.IsNullOrEmpty(currentText))
            {
                comboProcess.Text = currentText;
            }
            else if (comboProcess.Items.Count > 0)
            {
                comboProcess.SelectedIndex = 0;
            }
        }

        private void LoadSettings()
        {
            string enabledVal = ini.Read("VolumeDuckEnabled", "Settings");
            bool enabled = enabledVal == "1" || string.Equals(enabledVal, "True", StringComparison.OrdinalIgnoreCase);
            chkEnabled.Checked = enabled;

            // 적용 대상 로드
            string targetVal = ini.Read("VolumeDuckTarget", "Settings");
            if (string.Equals(targetVal, "ActiveWindow", StringComparison.OrdinalIgnoreCase))
            {
                rbTargetActiveWindow.Checked = true;
            }
            else if (string.Equals(targetVal, "SpecificProcess", StringComparison.OrdinalIgnoreCase))
            {
                rbTargetSpecificProcess.Checked = true;
            }
            else
            {
                rbTargetMaster.Checked = true;
            }

            string procName = ini.Read("VolumeDuckProcessName", "Settings");
            if (!string.IsNullOrEmpty(procName))
            {
                comboProcess.Text = procName;
            }

            comboProcess.Enabled = rbTargetSpecificProcess.Checked;
            btnRefreshProcess.Enabled = rbTargetSpecificProcess.Checked;

            string hotkeyStr = ini.Read("VolumeDuckHotkey", "Settings");
            if (TryParseHotkey(hotkeyStr, out Keys key, out GlobalHotkey.Modifiers mods))
            {
                currentKey = key;
                currentModifiers = mods;
            }
            else
            {
                currentKey = Keys.None;
                currentModifiers = GlobalHotkey.Modifiers.None;
            }
            UpdateButtonText();

            string levelStr = ini.Read("VolumeDuckLevel", "Settings");
            if (int.TryParse(levelStr, out int level))
            {
                level = Math.Max(0, Math.Min(100, level));
            }
            else
            {
                level = 10; // 기본값 10%
            }
            numVolume.Value = level;
            trackVolume.Value = level;

            string osdVal = ini.Read("VolumeDuckOSD", "Settings");
            bool osdEnabled = string.IsNullOrEmpty(osdVal) || osdVal == "1" || string.Equals(osdVal, "True", StringComparison.OrdinalIgnoreCase);
            chkOsd.Checked = osdEnabled;

            // OSD 색상 로드 (VolumeDuck 전용 -> 없으면 디스플레이 OSD 색상 폴백)
            string savedColor = ini.Read("VolumeDuckOsdColor", "Settings");
            if (string.IsNullOrEmpty(savedColor)) savedColor = ini.Read("OsdColor", "Settings");
            if (string.IsNullOrEmpty(savedColor)) savedColor = "LimeGreen";
            foreach (Item item in comboOsdColor.Items)
            {
                if (item.Value.Equals(savedColor, StringComparison.OrdinalIgnoreCase))
                {
                    comboOsdColor.SelectedItem = item;
                    break;
                }
            }
            if (comboOsdColor.SelectedIndex < 0 && comboOsdColor.Items.Count > 0) comboOsdColor.SelectedIndex = 0;

            // OSD 위치 로드 (VolumeDuck 전용 -> 없으면 디스플레이 OSD 위치 폴백)
            string savedPos = ini.Read("VolumeDuckOsdPosition", "Settings");
            if (string.IsNullOrEmpty(savedPos)) savedPos = ini.Read("OsdPosition", "Settings");
            if (string.IsNullOrEmpty(savedPos)) savedPos = "TopCenter";
            foreach (Item item in comboOsdPosition.Items)
            {
                if (item.Value.Equals(savedPos, StringComparison.OrdinalIgnoreCase))
                {
                    comboOsdPosition.SelectedItem = item;
                    break;
                }
            }
            if (comboOsdPosition.SelectedIndex < 0 && comboOsdPosition.Items.Count > 0) comboOsdPosition.SelectedIndex = 0;

            // OSD 표시 시간 로드 (VolumeDuck 전용 -> 없으면 디스플레이 OSD 시간 폴백)
            string savedDur = ini.Read("VolumeDuckOsdDuration", "Settings");
            if (string.IsNullOrEmpty(savedDur)) savedDur = ini.Read("OsdDuration", "Settings");
            if (int.TryParse(savedDur, out int ms))
            {
                decimal sec = ms / 1000m;
                if (sec >= 0.5m && sec <= 10.0m) numOsdDuration.Value = sec;
            }
            else
            {
                numOsdDuration.Value = 1.5m;
            }

            // OSD 폰트 크기 로드 (VolumeDuck 전용 -> 없으면 디스플레이 OSD 폰트 크기 폴백)
            string savedSize = ini.Read("VolumeDuckOsdFontSize", "Settings");
            if (string.IsNullOrEmpty(savedSize)) savedSize = ini.Read("OsdFontSize", "Settings");
            if (int.TryParse(savedSize, out int sz))
            {
                if (sz >= 20 && sz <= 48) numOsdFontSize.Value = sz;
            }
            else
            {
                numOsdFontSize.Value = 32m;
            }

            // OSD 상시 유지 로드
            string persistentVal = ini.Read("VolumeDuckPersistentOSD", "Settings");
            chkPersistentOsd.Checked = persistentVal == "1" || string.Equals(persistentVal, "True", StringComparison.OrdinalIgnoreCase);

            // 초기 활성화 상태에 맞춰 하위 컨트롤 상태 적용
            comboOsdColor.Enabled = osdEnabled;
            comboOsdPosition.Enabled = osdEnabled;
            numOsdDuration.Enabled = osdEnabled;
            numOsdFontSize.Enabled = osdEnabled;
            chkPersistentOsd.Enabled = osdEnabled;
        }

        private void BeginCapture()
        {
            isCapturing = true;
            btnHotkey.Text = ko ? "키 입력 대기..." : "Waiting for key...";
            lblStatus.Text = ko ? "원하는 키 조합을 누르세요. ESC = 취소" : "Press a key combination. ESC = cancel";
            KeyPreview = true;
            ActiveControl = null;
            Focus();
        }

        private void ClearHotkey()
        {
            isCapturing = false;
            currentKey = Keys.None;
            currentModifiers = GlobalHotkey.Modifiers.None;
            UpdateButtonText();
            lblStatus.Text = ko ? "단축키가 해제되었습니다." : "Hotkey cleared.";
        }

        private void VolumeDuckSettingsForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (!isCapturing) return;

            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
            {
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                isCapturing = false;
                UpdateButtonText();
                lblStatus.Text = "";
                e.SuppressKeyPress = true;
                return;
            }

            GlobalHotkey.Modifiers mods = GlobalHotkey.Modifiers.None;
            if (e.Control) mods |= GlobalHotkey.Modifiers.Control;
            if (e.Alt) mods |= GlobalHotkey.Modifiers.Alt;
            if (e.Shift) mods |= GlobalHotkey.Modifiers.Shift;

            bool lWin = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0;
            bool rWin = (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
            if (lWin || rWin) mods |= GlobalHotkey.Modifiers.Win;

            // 프로필 및 특수 핫키와의 중복 검사
            string[] hotkeyNames = ini.GetKeys("Hotkeys");
            string conflictName = null;
            if (hotkeyNames != null)
            {
                foreach (string hkName in hotkeyNames)
                {
                    string registeredHk = ini.Read(hkName, "Hotkeys");
                    if (TryParseHotkey(registeredHk, out Keys rKey, out GlobalHotkey.Modifiers rMods))
                    {
                        if (rKey == e.KeyCode && rMods == mods)
                        {
                            if (hkName == HotkeySettingsForm.HARD_RESET_ALL_PRESET)
                            {
                                conflictName = ko ? "전체 초기화" : "Reset All";
                            }
                            else if (hkName.StartsWith(HotkeySettingsForm.HARD_RESET_SINGLE_PREFIX, StringComparison.OrdinalIgnoreCase))
                            {
                                conflictName = ko ? "모니터 초기화" : "Reset Monitor";
                            }
                            else if (hkName.StartsWith(HotkeySettingsForm.CYCLE_SINGLE_PREFIX, StringComparison.OrdinalIgnoreCase))
                            {
                                conflictName = ko ? "프로필 순환" : "Cycle Profiles";
                            }
                            else
                            {
                                conflictName = hkName;
                            }
                            break;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(conflictName))
            {
                MessageBox.Show(
                    ko
                        ? $"이미 [{conflictName}]에 등록된 핫키입니다.\r\n다른 키로 등록해주세요."
                        : $"This hotkey is already registered to [{conflictName}].\r\nPlease choose another key.",
                    ko ? "핫키 중복" : "Duplicate Hotkey",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                e.SuppressKeyPress = true;
                return;
            }

            currentKey = e.KeyCode;
            currentModifiers = mods;
            isCapturing = false;

            UpdateButtonText();
            lblStatus.Text = "";
            e.SuppressKeyPress = true;
        }

        private void UpdateButtonText()
        {
            btnHotkey.Text = FormatHotkey(currentKey, currentModifiers);
        }

        private static string FormatHotkey(Keys key, GlobalHotkey.Modifiers modifiers)
        {
            if (key == Keys.None) return LanguageManager.Korean ? "없음 (지정)" : "None (Set)";
            string text = "";
            if ((modifiers & GlobalHotkey.Modifiers.Control) != 0) text += "Ctrl + ";
            if ((modifiers & GlobalHotkey.Modifiers.Alt) != 0) text += "Alt + ";
            if ((modifiers & GlobalHotkey.Modifiers.Shift) != 0) text += "Shift + ";
            if ((modifiers & GlobalHotkey.Modifiers.Win) != 0) text += "Win + ";
            return text + key.ToString();
        }

        private static bool TryParseHotkey(string value, out Keys key, out GlobalHotkey.Modifiers modifiers)
        {
            key = Keys.None;
            modifiers = GlobalHotkey.Modifiers.None;
            if (string.IsNullOrWhiteSpace(value)) return false;

            string[] parts = value.Split('+');
            string keyText = parts[parts.Length - 1].Trim();
            if (!Enum.TryParse(keyText, true, out key) || key == Keys.None) return false;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl": case "control": modifiers |= GlobalHotkey.Modifiers.Control; break;
                    case "alt": modifiers |= GlobalHotkey.Modifiers.Alt; break;
                    case "shift": modifiers |= GlobalHotkey.Modifiers.Shift; break;
                    case "win": case "windows": modifiers |= GlobalHotkey.Modifiers.Win; break;
                    default: return false;
                }
            }
            return true;
        }

        private void SaveSettings()
        {
            ini.Write("VolumeDuckEnabled", chkEnabled.Checked ? "1" : "0", "Settings");

            // 적용 대상 저장
            string targetVal = "Master";
            if (rbTargetActiveWindow.Checked) targetVal = "ActiveWindow";
            else if (rbTargetSpecificProcess.Checked) targetVal = "SpecificProcess";
            ini.Write("VolumeDuckTarget", targetVal, "Settings");

            ini.Write("VolumeDuckProcessName", comboProcess.Text.Trim(), "Settings");

            string hkStr = (currentKey == Keys.None) ? "" : FormatHotkey(currentKey, currentModifiers);
            ini.Write("VolumeDuckHotkey", hkStr, "Settings");
            ini.Write("VolumeDuckLevel", ((int)numVolume.Value).ToString(), "Settings");
            ini.Write("VolumeDuckOSD", chkOsd.Checked ? "1" : "0", "Settings");

            // OSD 색상/위치/지속시간/폰트크기 저장 (볼륨 전환 전용 키로 독립 저장)
            if (comboOsdColor.SelectedItem is Item colItem)
                ini.Write("VolumeDuckOsdColor", colItem.Value, "Settings");
            if (comboOsdPosition.SelectedItem is Item posItem)
                ini.Write("VolumeDuckOsdPosition", posItem.Value, "Settings");

            int ms = (int)(numOsdDuration.Value * 1000m);
            ini.Write("VolumeDuckOsdDuration", ms.ToString(), "Settings");
            ini.Write("VolumeDuckOsdFontSize", ((int)numOsdFontSize.Value).ToString(), "Settings");

            // OSD 상시 유지 저장
            ini.Write("VolumeDuckPersistentOSD", chkPersistentOsd.Checked ? "1" : "0", "Settings");

            Close();
        }
    }
}
