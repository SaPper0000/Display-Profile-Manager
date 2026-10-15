using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal sealed class ScreenshotSettingsForm : Form
    {
        private readonly IniFile _iniFile;
        private readonly Window _mainWindow;
        private readonly List<Display.DisplayInfo> _displays;
        private readonly Display.DisplayInfo _currDisplay;
        private readonly bool _isKorean;

        private Keys _key = Keys.None;
        private GlobalHotkey.Modifiers _modifiers = GlobalHotkey.Modifiers.None;
        private bool _capturing = false;
        private bool _winKeyHeld = false;

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
        private CheckBox chkHdrBypass;
        private ToolTip _tipHdr;
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

            Deactivate += (s, e) =>
            {
                _winKeyHeld = false;
                if (_capturing)
                {
                    _capturing = false;
                    UpdateHotkeyButton();
                }
            };

            Text = _isKorean ? "📸 화면 필터 스크린샷 설정" : "📸 Filtered Screenshot Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            Size = new Size(500, 595);

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
                Location = new Point(15, 23),
                Size = new Size(310, 32),
                UseVisualStyleBackColor = true
            };
            btnHotkey.Click += BtnHotkey_Click;

            btnClearHotkey = new Button
            {
                Text = _isKorean ? "지우기" : "Clear",
                Location = new Point(335, 23),
                Size = new Size(105, 32),
                UseVisualStyleBackColor = true
            };
            btnClearHotkey.Click += BtnClearHotkey_Click;

            Label lblHotkeyTip = new Label
            {
                Text = _isKorean
                    ? "💡 Ctrl, Alt, Shift, Win 키와 조합 가능 (ESC: 취소)"
                    : "💡 Combinable with Ctrl, Alt, Shift, Win (ESC: Cancel)",
                Location = new Point(15, 62),
                Size = new Size(425, 20),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Tag = "Highlight",
                ForeColor = ThemeManager.AccentText
            };

            grpHotkey.Controls.Add(btnHotkey);
            grpHotkey.Controls.Add(btnClearHotkey);
            grpHotkey.Controls.Add(lblHotkeyTip);
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

            // Feedback & Processing Group
            GroupBox grpFeedback = new GroupBox
            {
                Text = _isKorean ? "화면 처리 및 피드백" : "Processing & Feedback",
                Location = new Point(margin, top),
                Size = new Size(455, 90)
            };

            chkHdrBypass = new CheckBox
            {
                Text = _isKorean ? "HDR 모니터 화이트아웃 방지 (감마 중복 차단)" : "Prevent HDR Whiteout (Bypass duplicate gamma)",
                Location = new Point(15, 25),
                Size = new Size(425, 20),
                UseVisualStyleBackColor = true
            };
            _tipHdr = new ToolTip();
            _tipHdr.SetToolTip(chkHdrBypass, _isKorean
                ? "Windows HDR 모니터 캡처 시 감마가 이중 적용되어 화면이 하얗게 날아가는 현상을 방지합니다."
                : "Prevents screenshots from blowing out to pure white caused by duplicate gamma ramp application in Windows HDR.");

            chkPlaySound = new CheckBox
            {
                Text = _isKorean ? "셔터음 재생" : "Play Shutter Sound",
                Location = new Point(15, 55),
                Size = new Size(200, 20),
                UseVisualStyleBackColor = true
            };

            chkShowOSD = new CheckBox
            {
                Text = _isKorean ? "OSD 메시지 표시" : "Show OSD Message",
                Location = new Point(230, 55),
                Size = new Size(200, 20),
                UseVisualStyleBackColor = true
            };

            grpFeedback.Controls.Add(chkHdrBypass);
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
        }

        private void LoadSettings()
        {
            string hkStr = ScreenshotManager.GetHotkey(_iniFile);
            if (string.IsNullOrEmpty(hkStr))
            {
                hkStr = _iniFile.Read(HotkeySettingsForm.SCREENSHOT_PRESET, "Hotkeys");
            }
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
            chkHdrBypass.Checked = ScreenshotManager.GetHdrBypass(_iniFile);

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

        private void BtnClearHotkey_Click(object sender, EventArgs e)
        {
            _key = Keys.None;
            _modifiers = GlobalHotkey.Modifiers.None;
            _capturing = false;
            _winKeyHeld = false;
            UpdateHotkeyButton();
        }

        private void BtnHotkey_Click(object sender, EventArgs e)
        {
            _capturing = true;
            _winKeyHeld = false;
            UpdateHotkeyButton();
            KeyPreview = true;
            ActiveControl = null;
            Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_capturing)
            {
                if (GlobalHotkey.TryExtractHotkeyFromCmdKey(ref msg, keyData, ref _winKeyHeld, out Keys keyCode, out GlobalHotkey.Modifiers mods))
                {
                    ApplyCapturedHotkey(keyCode, mods);
                }
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
            {
                _winKeyHeld = false;
            }
            else if (_capturing && (e.KeyCode == Keys.PrintScreen || e.KeyCode == Keys.Pause))
            {
                GlobalHotkey.Modifiers mods = GlobalHotkey.GetModifiersFromKeyData(e.KeyData, _winKeyHeld);
                _winKeyHeld = false;
                ApplyCapturedHotkey(e.KeyCode, mods);
                e.Handled = true;
                return;
            }

            base.OnKeyUp(e);
        }

        private void ApplyCapturedHotkey(Keys keyCode, GlobalHotkey.Modifiers mods)
        {
            if (!_capturing) return;
            _capturing = false;
            _winKeyHeld = false;

            if (keyCode == Keys.Escape)
            {
                UpdateHotkeyButton();
                return;
            }

            // 1. 즉시 볼륨 전환 단축키와 중복 검사
            string volDuckHk = _iniFile.Read("VolumeDuckHotkey", "Settings");
            if (TryParseHotkey(volDuckHk, out Keys vKey, out GlobalHotkey.Modifiers vMods))
            {
                if (vKey == keyCode && vMods == mods)
                {
                    string volDuckName = _isKorean ? "즉시 볼륨 전환" : "Quick Volume Switch";
                    MessageBox.Show(
                        _isKorean
                            ? $"이미 [{volDuckName}]에 등록된 핫키입니다.\r\n다른 키로 등록해주세요."
                            : $"This hotkey is already registered to [{volDuckName}].\r\nPlease choose another key.",
                        _isKorean ? "핫키 중복" : "Duplicate Hotkey",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    UpdateHotkeyButton();
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
                        if (rKey == keyCode && rMods == mods)
                        {
                            if (hkName == HotkeySettingsForm.HARD_RESET_ALL_PRESET)
                                conflictName = _isKorean ? "모든 디스플레이 초기화" : "Reset All Displays";
                            else if (hkName.StartsWith(HotkeySettingsForm.HARD_RESET_SINGLE_PREFIX, StringComparison.OrdinalIgnoreCase))
                                conflictName = _isKorean ? "모니터 초기화" : "Reset Monitor";
                            else if (hkName.StartsWith(HotkeySettingsForm.CYCLE_SINGLE_PREFIX, StringComparison.OrdinalIgnoreCase))
                                conflictName = _isKorean ? "프로필 순환" : "Cycle Profiles";
                            else
                                conflictName = hkName;
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
                UpdateHotkeyButton();
                return;
            }

            // 3. 외부 백그라운드 프로그램(AMD Software, 지포스 익스피리언스, 스팀, 디스코드 등) 선점 여부 사전 검사
            if (!GlobalHotkey.IsHotkeyAvailableInOS(this.Handle, keyCode, mods, out int win32Error))
            {
                string hkText = FormatHotkey(keyCode, mods);
                string warnMsg = _isKorean
                    ? $"[안내] 단축키 [{hkText}]는 다른 백그라운드 프로그램(AMD Software, 지포스 섀도우플레이, 디스코드, 스팀 등)에서 이미 전역 단축키로 선점 중인 것으로 감지되었습니다.\r\n\r\n이대로 등록하면 해당 프로그램과 겹치거나 핫키가 정상 동작하지 않을 수 있습니다.\r\n그래도 이 단축키로 진행하시겠습니까?"
                    : $"[Notice] Hotkey [{hkText}] appears to be already in use by another application (AMD Software, GeForce ShadowPlay, Discord, Steam, etc.).\r\n\r\nDo you still wish to proceed with this hotkey?";

                DialogResult dr = MessageBox.Show(
                    warnMsg,
                    _isKorean ? "단축키 선점 감지" : "Hotkey Conflict Detected",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (dr != DialogResult.Yes)
                {
                    UpdateHotkeyButton();
                    return;
                }
            }

            _key = keyCode;
            _modifiers = mods;
            UpdateHotkeyButton();
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
                btnHotkey.ForeColor = ThemeManager.IsDark ? ThemeManager.DarkText : ThemeManager.LightText;
            }
        }

        private string FormatHotkey(Keys key, GlobalHotkey.Modifiers modifiers)
        {
            string noneLabel = _isKorean ? "미지정 (클릭하여 설정)" : "None (Click to set)";
            return GlobalHotkey.FormatHotkey(key, modifiers, noneLabel);
        }

        private bool TryParseHotkey(string str, out Keys k, out GlobalHotkey.Modifiers m)
        {
            return GlobalHotkey.TryParseHotkey(str, out k, out m);
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
            // ShowDialog() 모달 루프가 종료되지 않도록 Hide() 대신 Opacity를 0으로 전환하여 캡처 수행
            double prevOpacity = this.Opacity;
            try
            {
                this.Opacity = 0.0;
                this.Refresh();
                Application.DoEvents();
                System.Threading.Thread.Sleep(180);
                ScreenshotManager.Capture(_mainWindow, _iniFile, _displays, _currDisplay);
            }
            finally
            {
                if (!this.IsDisposed)
                {
                    this.Opacity = prevOpacity;
                    this.Activate();
                }
            }
        }

        private void BtnSave_Click(object sender, EventArgs e)
        {
            string hkText = _key == Keys.None ? "" : GlobalHotkey.FormatHotkey(_key, _modifiers);
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
            ScreenshotManager.SetHdrBypass(_iniFile, chkHdrBypass.Checked);

            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tipHdr?.Dispose();
                _tipHdr = null;
            }
            base.Dispose(disposing);
        }
    }
}