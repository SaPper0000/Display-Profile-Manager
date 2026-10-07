using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal sealed class ScreenshotSettingsForm : Form
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private readonly IniFile _iniFile;
        private readonly Window _mainWindow;
        private readonly List<Display.DisplayInfo> _displays;
        private readonly Display.DisplayInfo _currDisplay;
        private readonly bool _isKorean;

        private Keys _key = Keys.None;
        private GlobalHotkey.Modifiers _modifiers = GlobalHotkey.Modifiers.None;
        private bool _capturing = false;

        private Button btnHotkey;
        private Button btnClearHotkey;
        private ComboBox comboTarget;
        private CheckBox chkClipboard;
        private CheckBox chkSaveFile;
        private TextBox txtSaveDir;
        private Button btnBrowseDir;
        private Button btnOpenDir;
        private ComboBox comboFormat;
        private CheckBox chkPlaySound;
        private CheckBox chkShowOSD;
        private Button btnTestCapture;
        private Button btnSave;
        private Button btnCancel;

        public ScreenshotSettingsForm(IniFile iniFile, Window mainWindow, List<Display.DisplayInfo> displays, Display.DisplayInfo currDisplay)
        {
            _iniFile = iniFile;
            _mainWindow = mainWindow;
            _displays = displays;
            _currDisplay = currDisplay;
            _isKorean = LanguageManager.Korean;

            Text = _isKorean ? "📸 화면 필터 스크린샷 설정" : "📸 Filtered Screenshot Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            Size = new Size(500, 560);

            BuildUI();
            LoadSettings();
            ThemeManager.Apply(this);
        }

        private void BuildUI()
        {
            int margin = 15;
            int top = margin;

            // Hotkey Group
            GroupBox grpHotkey = new GroupBox
            {
                Text = _isKorean ? "스크린샷 단축키" : "Screenshot Hotkey",
                Location = new Point(margin, top),
                Size = new Size(455, 90)
            };

            btnHotkey = new Button
            {
                Location = new Point(15, 25),
                Size = new Size(310, 30),
                UseVisualStyleBackColor = true
            };
            btnHotkey.Click += BtnHotkey_Click;

            btnClearHotkey = new Button
            {
                Text = _isKorean ? "지우기" : "Clear",
                Location = new Point(335, 25),
                Size = new Size(105, 30),
                UseVisualStyleBackColor = true
            };
            btnClearHotkey.Click += BtnClearHotkey_Click;

            grpHotkey.Controls.Add(btnHotkey);
            grpHotkey.Controls.Add(btnClearHotkey);
            Controls.Add(grpHotkey);
            top += grpHotkey.Height + 10;

            // Capture Target Group
            GroupBox grpTarget = new GroupBox
            {
                Text = _isKorean ? "캡처 대상" : "Capture Target",
                Location = new Point(margin, top),
                Size = new Size(455, 75)
            };

            comboTarget = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(15, 30),
                Size = new Size(425, 25)
            };
            comboTarget.Items.Add(_isKorean ? "마우스 위치 모니터 (권장)" : "Monitor at Mouse Cursor (Recommended)");
            comboTarget.Items.Add(_isKorean ? "주 모니터 (Primary Screen)" : "Primary Monitor Only");
            comboTarget.Items.Add(_isKorean ? "전체 화면 (모든 가상 모니터)" : "All Screens (Virtual Desktop)");

            grpTarget.Controls.Add(comboTarget);
            Controls.Add(grpTarget);
            top += grpTarget.Height + 10;

            // Output & Save Options Group
            GroupBox grpOutput = new GroupBox
            {
                Text = _isKorean ? "출력 및 저장 옵션" : "Output & Save Options",
                Location = new Point(margin, top),
                Size = new Size(455, 155)
            };

            chkClipboard = new CheckBox
            {
                Text = _isKorean ? "클립보드에 복사" : "Copy to Clipboard",
                Location = new Point(15, 25),
                Size = new Size(425, 20),
                UseVisualStyleBackColor = true
            };

            chkSaveFile = new CheckBox
            {
                Text = _isKorean ? "파일로 저장" : "Save to File",
                Location = new Point(15, 50),
                Size = new Size(425, 20),
                UseVisualStyleBackColor = true
            };
            chkSaveFile.CheckedChanged += (s, e) => UpdateSaveDirControlsState();

            txtSaveDir = new TextBox
            {
                Location = new Point(15, 78),
                Size = new Size(260, 25)
            };

            btnBrowseDir = new Button
            {
                Text = _isKorean ? "찾아보기..." : "Browse...",
                Location = new Point(280, 77),
                Size = new Size(75, 27),
                UseVisualStyleBackColor = true
            };
            btnBrowseDir.Click += BtnBrowseDir_Click;

            btnOpenDir = new Button
            {
                Text = _isKorean ? "폴더 열기" : "Open Folder",
                Location = new Point(360, 77),
                Size = new Size(80, 27),
                UseVisualStyleBackColor = true
            };
            btnOpenDir.Click += (s, e) => ScreenshotManager.OpenSaveFolder(_iniFile);

            Label lblFormat = new Label
            {
                Text = _isKorean ? "저장 형식:" : "Format:",
                Location = new Point(15, 115),
                Size = new Size(70, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };

            comboFormat = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(90, 112),
                Size = new Size(100, 25)
            };
            comboFormat.Items.Add("PNG");
            comboFormat.Items.Add("JPG");

            grpOutput.Controls.Add(chkClipboard);
            grpOutput.Controls.Add(chkSaveFile);
            grpOutput.Controls.Add(txtSaveDir);
            grpOutput.Controls.Add(btnBrowseDir);
            grpOutput.Controls.Add(btnOpenDir);
            grpOutput.Controls.Add(lblFormat);
            grpOutput.Controls.Add(comboFormat);

            Controls.Add(grpOutput);
            top += grpOutput.Height + 10;

            // Feedback Group
            GroupBox grpFeedback = new GroupBox
            {
                Text = _isKorean ? "피드백" : "Feedback",
                Location = new Point(margin, top),
                Size = new Size(455, 60)
            };

            chkPlaySound = new CheckBox
            {
                Text = _isKorean ? "셔터음 재생" : "Play Shutter Sound",
                Location = new Point(15, 25),
                Size = new Size(200, 20),
                UseVisualStyleBackColor = true
            };

            chkShowOSD = new CheckBox
            {
                Text = _isKorean ? "OSD 메시지 표시" : "Show OSD Message",
                Location = new Point(230, 25),
                Size = new Size(200, 20),
                UseVisualStyleBackColor = true
            };

            grpFeedback.Controls.Add(chkPlaySound);
            grpFeedback.Controls.Add(chkShowOSD);
            Controls.Add(grpFeedback);
            top += grpFeedback.Height + 15;

            // Bottom Buttons
            btnTestCapture = new Button
            {
                Text = _isKorean ? "📸 테스트 캡처" : "📸 Test Capture",
                Location = new Point(margin, top),
                Size = new Size(130, 30),
                UseVisualStyleBackColor = true
            };
            btnTestCapture.Click += BtnTestCapture_Click;

            btnSave = new Button
            {
                Text = _isKorean ? "저장" : "Save",
                DialogResult = DialogResult.OK,
                Location = new Point(285, top),
                Size = new Size(90, 30),
                UseVisualStyleBackColor = true
            };
            btnSave.Click += BtnSave_Click;

            btnCancel = new Button
            {
                Text = _isKorean ? "취소" : "Cancel",
                DialogResult = DialogResult.Cancel,
                Location = new Point(380, top),
                Size = new Size(90, 30),
                UseVisualStyleBackColor = true
            };
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(btnTestCapture);
            Controls.Add(btnSave);
            Controls.Add(btnCancel);

            AcceptButton = btnSave;
            CancelButton = btnCancel;

            KeyDown += ScreenshotSettingsForm_KeyDown;
        }

        private void LoadSettings()
        {
            string hkStr = ScreenshotManager.GetHotkey(_iniFile);
            TryParseHotkey(hkStr, out _key, out _modifiers);
            UpdateHotkeyButton();

            int targetIdx = ScreenshotManager.GetTarget(_iniFile);
            if (targetIdx >= 0 && targetIdx < comboTarget.Items.Count)
                comboTarget.SelectedIndex = targetIdx;
            else
                comboTarget.SelectedIndex = 0;

            chkClipboard.Checked = ScreenshotManager.GetClipboard(_iniFile);
            chkSaveFile.Checked = ScreenshotManager.GetSaveFile(_iniFile);
            txtSaveDir.Text = ScreenshotManager.GetSaveDir(_iniFile);

            string format = ScreenshotManager.GetFormat(_iniFile);
            if (comboFormat.Items.Contains(format))
                comboFormat.SelectedItem = format;
            else
                comboFormat.SelectedIndex = 0;

            chkPlaySound.Checked = ScreenshotManager.GetPlaySound(_iniFile);
            chkShowOSD.Checked = ScreenshotManager.GetShowOSD(_iniFile);

            UpdateSaveDirControlsState();
        }

        private void UpdateSaveDirControlsState()
        {
            bool saveFile = chkSaveFile.Checked;
            txtSaveDir.Enabled = saveFile;
            btnBrowseDir.Enabled = saveFile;
            btnOpenDir.Enabled = saveFile;
            comboFormat.Enabled = saveFile;
        }

        private void BtnHotkey_Click(object sender, EventArgs e)
        {
            _capturing = true;
            UpdateHotkeyButton();
            ActiveControl = null;
            Focus();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _mainWindow?.SuspendGlobalHotkeys();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _mainWindow?.ResumeGlobalHotkeys();
            base.OnFormClosed(e);
        }

        private void BtnClearHotkey_Click(object sender, EventArgs e)
        {
            _key = Keys.None;
            _modifiers = GlobalHotkey.Modifiers.None;
            _capturing = false;
            UpdateHotkeyButton();
        }

        private void ScreenshotSettingsForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (!_capturing) return;

            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
            {
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                _capturing = false;
                UpdateHotkeyButton();
                e.SuppressKeyPress = true;
                return;
            }

            GlobalHotkey.Modifiers mods = GlobalHotkey.Modifiers.None;
            if (e.Control) mods |= GlobalHotkey.Modifiers.Control;
            if (e.Alt) mods |= GlobalHotkey.Modifiers.Alt;
            if (e.Shift) mods |= GlobalHotkey.Modifiers.Shift;

            bool lWinDown = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0;
            bool rWinDown = (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
            if (lWinDown || rWinDown) mods |= GlobalHotkey.Modifiers.Win;

            // 1. 즉시 볼륨 전환 단축키와 중복 검사
            string volDuckHk = _iniFile.Read("VolumeDuckHotkey", "Settings");
            if (TryParseHotkey(volDuckHk, out Keys vKey, out GlobalHotkey.Modifiers vMods))
            {
                if (vKey == e.KeyCode && vMods == mods)
                {
                    string volDuckName = _isKorean ? "즉시 볼륨 전환" : "Quick Volume Switch";
                    MessageBox.Show(
                        _isKorean
                            ? $"이미 [{volDuckName}]에 등록된 핫키입니다.\r\n다른 키로 등록해주세요."
                            : $"This hotkey is already registered to [{volDuckName}].\r\nPlease choose another key.",
                        _isKorean ? "핫키 중복" : "Duplicate Hotkey",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    e.SuppressKeyPress = true;
                    return;
                }
            }

            // 2. 프로필 및 특수 핫키와의 중복 검사
            string[] hotkeyNames = _iniFile.GetKeys("Hotkeys");
            string conflictName = null;
            if (hotkeyNames != null)
            {
                foreach (string hkName in hotkeyNames)
                {
                    if (string.Equals(hkName, HotkeySettingsForm.SCREENSHOT_PRESET, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string registeredHk = _iniFile.Read(hkName, "Hotkeys");
                    if (TryParseHotkey(registeredHk, out Keys rKey, out GlobalHotkey.Modifiers rMods))
                    {
                        if (rKey == e.KeyCode && rMods == mods)
                        {
                            if (hkName == HotkeySettingsForm.HARD_RESET_ALL_PRESET)
                            {
                                conflictName = _isKorean ? "모든 디스플레이 초기화" : "Reset All Displays";
                            }
                            else if (hkName.StartsWith(HotkeySettingsForm.HARD_RESET_SINGLE_PREFIX, StringComparison.OrdinalIgnoreCase))
                            {
                                conflictName = _isKorean ? "모니터 초기화" : "Reset Monitor";
                            }
                            else if (hkName.StartsWith(HotkeySettingsForm.CYCLE_SINGLE_PREFIX, StringComparison.OrdinalIgnoreCase))
                            {
                                conflictName = _isKorean ? "프로필 순환" : "Cycle Profiles";
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
                    _isKorean
                        ? $"이미 [{conflictName}]에 등록된 핫키입니다.\r\n다른 키로 등록해주세요."
                        : $"This hotkey is already registered to [{conflictName}].\r\nPlease choose another key.",
                    _isKorean ? "핫키 중복" : "Duplicate Hotkey",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                e.SuppressKeyPress = true;
                return;
            }

            _key = e.KeyCode;
            _modifiers = mods;
            _capturing = false;
            UpdateHotkeyButton();
            e.SuppressKeyPress = true;
        }

        private void UpdateHotkeyButton()
        {
            if (_capturing)
            {
                btnHotkey.Text = _isKorean ? "키 입력 대기 중... (ESC = 취소)" : "Press key... (ESC = Cancel)";
                btnHotkey.ForeColor = Color.Red;
            }
            else
            {
                btnHotkey.Text = FormatHotkey(_key, _modifiers);
                btnHotkey.ForeColor = SystemColors.ControlText;
            }
        }

        private string FormatHotkey(Keys key, GlobalHotkey.Modifiers modifiers)
        {
            if (key == Keys.None)
                return _isKorean ? "미지정 (클릭하여 설정)" : "None (Click to set)";

            string t = "";
            if ((modifiers & GlobalHotkey.Modifiers.Control) != 0) t += "Ctrl + ";
            if ((modifiers & GlobalHotkey.Modifiers.Alt) != 0) t += "Alt + ";
            if ((modifiers & GlobalHotkey.Modifiers.Shift) != 0) t += "Shift + ";
            if ((modifiers & GlobalHotkey.Modifiers.Win) != 0) t += "Win + ";

            return t + key.ToString();
        }

        private bool TryParseHotkey(string str, out Keys k, out GlobalHotkey.Modifiers m)
        {
            k = Keys.None;
            m = GlobalHotkey.Modifiers.None;

            if (string.IsNullOrWhiteSpace(str))
                return false;

            string[] parts = str.Split(new char[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (i < parts.Length - 1)
                {
                    if (string.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase) || string.Equals(part, "Control", StringComparison.OrdinalIgnoreCase))
                        m |= GlobalHotkey.Modifiers.Control;
                    else if (string.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase))
                        m |= GlobalHotkey.Modifiers.Alt;
                    else if (string.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase))
                        m |= GlobalHotkey.Modifiers.Shift;
                    else if (string.Equals(part, "Win", StringComparison.OrdinalIgnoreCase))
                        m |= GlobalHotkey.Modifiers.Win;
                }
                else
                {
                    try
                    {
                        k = (Keys)Enum.Parse(typeof(Keys), part, true);
                    }
                    catch
                    {
                        k = Keys.None;
                    }
                }
            }

            return k != Keys.None;
        }

        private void BtnBrowseDir_Click(object sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.SelectedPath = txtSaveDir.Text.Trim();
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    txtSaveDir.Text = fbd.SelectedPath;
                }
            }
        }

        private void BtnTestCapture_Click(object sender, EventArgs e)
        {
            Hide();
            Application.DoEvents();
            System.Threading.Thread.Sleep(200);
            try
            {
                ScreenshotManager.Capture(_mainWindow, _iniFile, _displays, _currDisplay);
            }
            finally
            {
                System.Threading.Tasks.Task.Delay(1000).ContinueWith(t =>
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke((Action)(() =>
                        {
                            Show();
                            WindowState = FormWindowState.Normal;
                        }));
                    }
                });
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            string hkText = _key == Keys.None ? "" : FormatHotkey(_key, _modifiers);
            ScreenshotManager.SetHotkey(_iniFile, hkText);
            if (string.IsNullOrEmpty(hkText))
            {
                _iniFile.DeleteKey(HotkeySettingsForm.SCREENSHOT_PRESET, "Hotkeys");
            }
            else
            {
                _iniFile.Write(HotkeySettingsForm.SCREENSHOT_PRESET, hkText, "Hotkeys");
            }
            ScreenshotManager.SetTarget(_iniFile, comboTarget.SelectedIndex);
            ScreenshotManager.SetClipboard(_iniFile, chkClipboard.Checked);
            ScreenshotManager.SetSaveFile(_iniFile, chkSaveFile.Checked);
            ScreenshotManager.SetSaveDir(_iniFile, txtSaveDir.Text.Trim());
            ScreenshotManager.SetFormat(_iniFile, comboFormat.SelectedItem?.ToString() ?? "PNG");
            ScreenshotManager.SetPlaySound(_iniFile, chkPlaySound.Checked);
            ScreenshotManager.SetShowOSD(_iniFile, chkShowOSD.Checked);

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}