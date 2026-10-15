using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal sealed class HotkeyInputDialog : Form
    {
        private const int VK_HANJA = 0x19;

        public sealed class KeyItem
        {
            public Keys Key { get; }
            public string DisplayName { get; }

            public KeyItem(Keys key, string displayName)
            {
                Key = key;
                DisplayName = displayName;
            }

            public override string ToString() => DisplayName;
        }

        private readonly string targetTitle;
        private readonly bool isKorean;

        private Label lblPreview;
        private CheckBox chkCtrl;
        private CheckBox chkAlt;
        private CheckBox chkShift;
        private CheckBox chkWin;
        private ComboBox cboKey;
        private Button btnOk;
        private Button btnClear;
        private Button btnCancel;
        private bool suppressEvents = false;
        private bool _winKeyHeld = false;

        public Keys SelectedKey { get; private set; }
        public GlobalHotkey.Modifiers SelectedModifiers { get; private set; }

        public HotkeyInputDialog(string targetTitle, Keys initialKey, GlobalHotkey.Modifiers initialModifiers)
        {
            this.targetTitle = targetTitle;
            this.SelectedKey = initialKey;
            this.SelectedModifiers = initialModifiers;
            this.isKorean = LanguageManager.Korean;

            Text = isKorean ? "핫키(단축키) 설정" : "Hotkey Configuration";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Size = new Size(480, 390);
            KeyPreview = true;

            Deactivate += (s, e) => { _winKeyHeld = false; };

            BuildUI();
            InitializeValues(initialKey, initialModifiers);
            ThemeManager.Apply(this);
        }

        private void BuildUI()
        {
            // 상단 대상 타이틀
            Label lblTarget = new Label
            {
                Text = (isKorean ? "설정 대상: " : "Target: ") + targetTitle,
                Location = new Point(20, 15),
                Size = new Size(424, 22),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                AutoEllipsis = true
            };
            Controls.Add(lblTarget);

            // 미리보기 패널 (강조 박스)
            Panel pnlPreview = new Panel
            {
                Location = new Point(20, 42),
                Size = new Size(424, 60),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = ThemeManager.IsDark ? Color.FromArgb(32, 36, 44) : Color.FromArgb(245, 247, 250)
            };

            lblPreview = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = ThemeManager.IsDark ? Color.FromArgb(255, 210, 70) : Color.FromArgb(0, 110, 220)
            };
            pnlPreview.Controls.Add(lblPreview);
            Controls.Add(pnlPreview);

            // 조합키 그룹
            GroupBox grpModifiers = new GroupBox
            {
                Text = isKorean ? "조합키 선택 (Ctrl / Alt / Shift / Win)" : "Modifiers (Ctrl / Alt / Shift / Win)",
                Location = new Point(20, 112),
                Size = new Size(424, 65),
                Font = new Font("Segoe UI", 9f)
            };

            chkCtrl = new CheckBox { Text = "Ctrl", Location = new Point(25, 26), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            chkAlt = new CheckBox { Text = "Alt", Location = new Point(125, 26), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            chkShift = new CheckBox { Text = "Shift", Location = new Point(220, 26), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
            chkWin = new CheckBox { Text = "Win", Location = new Point(325, 26), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };

            chkCtrl.CheckedChanged += (s, e) => { if (!suppressEvents) UpdateFromControls(); };
            chkAlt.CheckedChanged += (s, e) => { if (!suppressEvents) UpdateFromControls(); };
            chkShift.CheckedChanged += (s, e) => { if (!suppressEvents) UpdateFromControls(); };
            chkWin.CheckedChanged += (s, e) => { if (!suppressEvents) UpdateFromControls(); };

            grpModifiers.Controls.AddRange(new Control[] { chkCtrl, chkAlt, chkShift, chkWin });
            Controls.Add(grpModifiers);

            // 일반 키 선택 영역
            Label lblKeyLabel = new Label
            {
                Text = isKorean ? "일반 키 선택:" : "Key:",
                Location = new Point(20, 192),
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
            };
            Controls.Add(lblKeyLabel);

            cboKey = new ComboBox
            {
                Location = new Point(125, 188),
                Size = new Size(319, 28),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Segoe UI", 9.5f)
            };
            PopulateKeyItems();
            cboKey.SelectedIndexChanged += (s, e) => { if (!suppressEvents) UpdateFromControls(); };
            Controls.Add(cboKey);

            // 안내 문구 (팁)
            Label lblHint = new Label
            {
                Text = isKorean
                    ? "💡 키보드로 조합키(예: Ctrl + F5)를 누르면 자동으로 감지됩니다.\r\n   직접 마우스로 조합키 체크박스를 클릭하여 설정할 수도 있습니다."
                    : "💡 Press any key combination (e.g. Ctrl + F5) on your keyboard,\r\n   or choose checkboxes and keys directly with mouse.",
                Location = new Point(20, 230),
                Size = new Size(424, 42),
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = ThemeManager.IsDark ? Color.FromArgb(160, 170, 185) : Color.FromArgb(100, 110, 125)
            };
            Controls.Add(lblHint);

            // 하단 버튼들
            btnOk = new Button
            {
                Text = isKorean ? "확인" : "OK",
                Size = new Size(95, 34),
                Location = new Point(140, 295),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            btnOk.Click += (s, e) => OnConfirm();

            btnClear = new Button
            {
                Text = isKorean ? "단축키 해제" : "Clear Hotkey",
                Size = new Size(100, 34),
                Location = new Point(245, 295),
                Font = new Font("Segoe UI", 9f)
            };
            btnClear.Click += (s, e) => OnClear();

            btnCancel = new Button
            {
                Text = isKorean ? "취소" : "Cancel",
                Size = new Size(85, 34),
                Location = new Point(355, 295),
                Font = new Font("Segoe UI", 9f)
            };
            btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[] { btnOk, btnClear, btnCancel });
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private void PopulateKeyItems()
        {
            List<KeyItem> items = new List<KeyItem>
            {
                new KeyItem(Keys.None, isKorean ? "(없음 / 미지정)" : "(None)")
            };

            // F1 ~ F24 (GlobalHotkey.CandidateVirtualKeys와 일치)
            for (int i = 1; i <= 24; i++)
            {
                Keys k = (Keys)Enum.Parse(typeof(Keys), "F" + i);
                items.Add(new KeyItem(k, "F" + i));
            }

            // A ~ Z
            for (char c = 'A'; c <= 'Z'; c++)
            {
                Keys k = (Keys)Enum.Parse(typeof(Keys), c.ToString());
                items.Add(new KeyItem(k, c.ToString()));
            }

            // 숫자 0 ~ 9 (상단 숫자)
            for (int i = 0; i <= 9; i++)
            {
                Keys k = (Keys)Enum.Parse(typeof(Keys), "D" + i);
                items.Add(new KeyItem(k, i.ToString() + (isKorean ? " (숫자)" : "")));
            }

            // 특수키 (Tab, Enter 제외 — GlobalHotkey 후보 키 정책과 일치)
            items.Add(new KeyItem(Keys.Space, "Space"));
            items.Add(new KeyItem(Keys.Back, "Backspace"));
            items.Add(new KeyItem(Keys.PrintScreen, "PrintScreen"));
            items.Add(new KeyItem(Keys.Insert, "Insert"));
            items.Add(new KeyItem(Keys.Delete, "Delete"));
            items.Add(new KeyItem(Keys.Home, "Home"));
            items.Add(new KeyItem(Keys.End, "End"));
            items.Add(new KeyItem(Keys.PageUp, "PageUp"));
            items.Add(new KeyItem(Keys.PageDown, "PageDown"));
            items.Add(new KeyItem(Keys.Pause, "Pause"));
            items.Add(new KeyItem(Keys.Scroll, "ScrollLock"));

            // 방향키
            items.Add(new KeyItem(Keys.Up, isKorean ? "위 방향키 (Up)" : "Up"));
            items.Add(new KeyItem(Keys.Down, isKorean ? "아래 방향키 (Down)" : "Down"));
            items.Add(new KeyItem(Keys.Left, isKorean ? "왼쪽 방향키 (Left)" : "Left"));
            items.Add(new KeyItem(Keys.Right, isKorean ? "오른쪽 방향키 (Right)" : "Right"));

            // 넘패드
            for (int i = 0; i <= 9; i++)
            {
                Keys k = (Keys)Enum.Parse(typeof(Keys), "NumPad" + i);
                items.Add(new KeyItem(k, "NumPad " + i));
            }
            items.Add(new KeyItem(Keys.Add, "NumPad +"));
            items.Add(new KeyItem(Keys.Subtract, "NumPad -"));
            items.Add(new KeyItem(Keys.Multiply, "NumPad *"));
            items.Add(new KeyItem(Keys.Divide, "NumPad /"));
            items.Add(new KeyItem(Keys.Decimal, "NumPad ."));

            // 기호키
            items.Add(new KeyItem(Keys.Oemtilde, "` / ~"));
            items.Add(new KeyItem(Keys.OemMinus, "-"));
            items.Add(new KeyItem(Keys.Oemplus, "= / +"));
            items.Add(new KeyItem(Keys.OemOpenBrackets, "["));
            items.Add(new KeyItem(Keys.OemCloseBrackets, "]"));
            items.Add(new KeyItem(Keys.Oem1, "; / :"));
            items.Add(new KeyItem(Keys.Oem7, "' / \""));
            items.Add(new KeyItem(Keys.Oemcomma, ","));
            items.Add(new KeyItem(Keys.OemPeriod, "."));
            items.Add(new KeyItem(Keys.OemQuestion, "/"));
            items.Add(new KeyItem(Keys.Oem5, "\\"));

            cboKey.Items.AddRange(items.ToArray());
        }

        private void InitializeValues(Keys key, GlobalHotkey.Modifiers modifiers)
        {
            suppressEvents = true;
            try
            {
                chkCtrl.Checked = (modifiers & GlobalHotkey.Modifiers.Control) != 0;
                chkAlt.Checked = (modifiers & GlobalHotkey.Modifiers.Alt) != 0;
                chkShift.Checked = (modifiers & GlobalHotkey.Modifiers.Shift) != 0;
                chkWin.Checked = (modifiers & GlobalHotkey.Modifiers.Win) != 0;

                SelectKeyInCombo(key);
            }
            finally
            {
                suppressEvents = false;
            }
            UpdatePreview();
        }

        private void SelectKeyInCombo(Keys key)
        {
            for (int i = 0; i < cboKey.Items.Count; i++)
            {
                if (cboKey.Items[i] is KeyItem item && item.Key == key)
                {
                    cboKey.SelectedIndex = i;
                    return;
                }
            }
            cboKey.SelectedIndex = 0; // None
        }

        private void UpdateFromControls()
        {
            GlobalHotkey.Modifiers mods = GlobalHotkey.Modifiers.None;
            if (chkCtrl.Checked) mods |= GlobalHotkey.Modifiers.Control;
            if (chkAlt.Checked) mods |= GlobalHotkey.Modifiers.Alt;
            if (chkShift.Checked) mods |= GlobalHotkey.Modifiers.Shift;
            if (chkWin.Checked) mods |= GlobalHotkey.Modifiers.Win;

            Keys key = Keys.None;
            if (cboKey.SelectedItem is KeyItem item)
            {
                key = item.Key;
            }

            SelectedKey = key;
            SelectedModifiers = mods;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (SelectedKey == Keys.None)
            {
                if (SelectedModifiers != GlobalHotkey.Modifiers.None)
                {
                    string modStr = FormatModifiers(SelectedModifiers);
                    lblPreview.Text = modStr + (isKorean ? " [키를 누르세요]" : " [Press a key]");
                    lblPreview.ForeColor = Color.Orange;
                }
                else
                {
                    lblPreview.Text = isKorean ? "(미지정 / 없음)" : "(None)";
                    lblPreview.ForeColor = ThemeManager.IsDark ? Color.Gray : Color.DarkGray;
                }
                return;
            }

            string text = FormatModifiers(SelectedModifiers) + SelectedKey.ToString();
            lblPreview.Text = text;
            lblPreview.ForeColor = ThemeManager.IsDark ? Color.FromArgb(255, 210, 70) : Color.FromArgb(0, 110, 220);
        }

        private static string FormatModifiers(GlobalHotkey.Modifiers modifiers)
        {
            string t = "";
            if ((modifiers & GlobalHotkey.Modifiers.Control) != 0) t += "Ctrl + ";
            if ((modifiers & GlobalHotkey.Modifiers.Alt) != 0) t += "Alt + ";
            if ((modifiers & GlobalHotkey.Modifiers.Shift) != 0) t += "Shift + ";
            if ((modifiers & GlobalHotkey.Modifiers.Win) != 0) t += "Win + ";
            return t;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys keyCode = GlobalHotkey.ResolveKeyCode(ref msg, keyData);

            // Esc = 취소
            if (keyCode == Keys.Escape)
            {
                _winKeyHeld = false;
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            if (keyCode == Keys.LWin || keyCode == Keys.RWin)
            {
                _winKeyHeld = true;
            }

            GlobalHotkey.Modifiers mods = GlobalHotkey.GetModifiersFromKeyData(keyData, _winKeyHeld);
            bool ctrl = (mods & GlobalHotkey.Modifiers.Control) != 0;
            bool alt = (mods & GlobalHotkey.Modifiers.Alt) != 0;
            bool shift = (mods & GlobalHotkey.Modifiers.Shift) != 0;
            bool win = (mods & GlobalHotkey.Modifiers.Win) != 0;

            // 조합키 자체만 눌린 경우 (Ctrl, Alt, Shift, Win, 한자키 등)
            bool isModifierOnly = keyCode == Keys.ControlKey || keyCode == Keys.LControlKey || keyCode == Keys.RControlKey ||
                                 keyCode == Keys.Menu || keyCode == Keys.LMenu || keyCode == Keys.RMenu ||
                                 keyCode == Keys.ShiftKey || keyCode == Keys.LShiftKey || keyCode == Keys.RShiftKey ||
                                 keyCode == Keys.LWin || keyCode == Keys.RWin || keyCode == (Keys)VK_HANJA;

            // 조합키 체크박스 상태 자동 업데이트
            suppressEvents = true;
            try
            {
                if (ctrl) chkCtrl.Checked = true;
                if (alt) chkAlt.Checked = true;
                if (shift) chkShift.Checked = true;
                if (win) chkWin.Checked = true;
            }
            finally
            {
                suppressEvents = false;
            }

            // 일반 키가 함께 눌린 경우
            if (!isModifierOnly && keyCode != Keys.None && GlobalHotkey.IsCandidateKey(keyCode))
            {
                suppressEvents = true;
                try
                {
                    SelectKeyInCombo(keyCode);
                }
                finally
                {
                    suppressEvents = false;
                }

                UpdateFromControls();
                return true;
            }

            UpdateFromControls();
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
            {
                _winKeyHeld = false;
            }
            base.OnKeyUp(e);
        }

        private void OnConfirm()
        {
            if (SelectedKey == Keys.None && SelectedModifiers != GlobalHotkey.Modifiers.None)
            {
                MessageBox.Show(
                    isKorean
                        ? "조합키(Ctrl/Alt/Shift/Win)만으로는 단축키를 지정할 수 없습니다.\r\n함께 누를 일반 키(예: F5, S 등)를 선택하거나 눌러주세요."
                        : "Cannot assign hotkey with modifier keys only.\r\nPlease select a regular key as well (e.g. F5, S).",
                    isKorean ? "키 선택 필요" : "Key Required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (SelectedKey == Keys.Space && SelectedModifiers == GlobalHotkey.Modifiers.None)
            {
                MessageBox.Show(
                    isKorean
                        ? "Space 키는 단독으로 지정할 수 없습니다. Ctrl/Alt/Shift/Win과 조합해 주세요."
                        : "Space key requires at least one modifier key (Ctrl/Alt/Shift/Win).",
                    isKorean ? "조합키 필요" : "Modifier Required",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (SelectedKey != Keys.None)
            {
                // 다른 프로그램(지포스 익스피리언스, 스팀, 디스코드 등)에서 이미 등록된 키인지 사전 검사
                if (!GlobalHotkey.IsHotkeyAvailableInOS(this.Handle, SelectedKey, SelectedModifiers, out int win32Error))
                {
                    string hkText = FormatModifiers(SelectedModifiers) + SelectedKey.ToString();
                    string warnMsg = isKorean
                        ? $"[안내] 단축키 [{hkText}]는 다른 백그라운드 프로그램(지포스 섀도우플레이, 디스코드, 스팀 등)에서 이미 전역 단축키로 선점 중인 것으로 감지되었습니다.\r\n\r\n이대로 등록하면 해당 프로그램과 겹치거나 핫키가 정상 동작하지 않을 수 있습니다.\r\n그래도 이 단축키로 진행하시겠습니까?"
                        : $"[Notice] Hotkey [{hkText}] appears to be already in use by another application (GeForce ShadowPlay, Discord, Steam, etc.).\r\n\r\nDo you still wish to proceed with this hotkey?";

                    DialogResult dr = MessageBox.Show(
                        warnMsg,
                        isKorean ? "단축키 선점 감지" : "Hotkey Conflict Detected",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (dr != DialogResult.Yes) return;
                }
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnClear()
        {
            SelectedKey = Keys.None;
            SelectedModifiers = GlobalHotkey.Modifiers.None;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
