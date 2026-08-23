using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal sealed class HotkeySettingsForm : Form
    {
        private sealed class Row
        {
            public string Preset;
            public Button SetButton;
            public Button ClearButton;
            public ComboBox ModeCombo; // 초기화 핫키는 ModeCombo를 null로 사용
            public Keys Key;
            public GlobalHotkey.Modifiers Modifiers;
        }

        private readonly IniFile iniFile;
        private readonly List<Row> rows = new List<Row>();
        private readonly FlowLayoutPanel panel;
        private readonly Label status;
        private string capturePreset;
        internal bool ChangesSaved { get; private set; }

        // 초기화 핫키 저장을 위한 특수 프로필 이름 상수
        public const string HARD_RESET_ALL_PRESET = "__HARD_RESET_ALL__";
        public const string HARD_RESET_SINGLE_PREFIX = "__HARD_RESET_";

        public HotkeySettingsForm(IniFile iniFile, string[] presets, List<Display.DisplayInfo> displays)
        {
            this.iniFile = iniFile;
            Text = LanguageManager.Korean ? "핫키 설정" : "Hotkey Settings";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(620, 420);
            Size = new Size(720, 520);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            KeyPreview = true;
            KeyDown += HotkeySettingsForm_KeyDown;
            MaximizeBox = false;
            MinimizeBox = false;

            Label info = new Label();
            info.Text = LanguageManager.Korean
                ? "프로필별 글로벌 핫키를 지정하세요.\r\n'지정' 버튼을 누른 뒤 원하는 키 조합을 누르세요.\r\n방식에서 '토글'을 선택하면 같은 핫키를 다시 눌러 이전 프로필로 돌아갑니다.\r\n'삭제'를 누르면 해당 프로필의 핫키가 제거됩니다."
                : "Assign a global hotkey for each profile.\r\nClick 'Set' and press the desired key combination.\r\nChoose 'Toggle' to press the same hotkey again and return to the previous profile.\r\nClick 'Clear' to remove the profile hotkey.";
            info.AutoSize = true;
            info.Location = new Point(12, 12);

            panel = new FlowLayoutPanel();
            panel.Location = new Point(12, 78);
            panel.Size = new Size(680, 315);
            panel.AutoScroll = true;
            panel.FlowDirection = FlowDirection.TopDown;
            panel.WrapContents = false;
            panel.BorderStyle = BorderStyle.FixedSingle;

            status = new Label();
            status.AutoSize = true;
            status.Location = new Point(12, 382);

            Button save = new Button();
            save.Text = LanguageManager.Korean ? "저장" : "Save";
            save.Size = new Size(90, 30);
            save.Location = new Point(492, 452);
            save.Click += delegate { SaveAndClose(); };

            Button cancel = new Button();
            cancel.Text = LanguageManager.Korean ? "취소" : "Cancel";
            cancel.Size = new Size(90, 30);
            cancel.Location = new Point(592, 452);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(info);
            Controls.Add(panel);
            Controls.Add(status);
            Controls.Add(save);
            Controls.Add(cancel);

            ThemeManager.Apply(this);

            // --- 1. 초기화(Hard Reset) 전용 핫키 행 추가 (전체 및 개별) ---
            AddHardResetRow(HARD_RESET_ALL_PRESET, LanguageManager.Korean ? "🔄 모든 디스플레이 초기화" : "🔄 Reset All Displays");

            if (displays != null)
            {
                for (int i = 0; i < displays.Count; i++)
                {
                    string monitorName = displays[i].displayName;
                    string presetName = HARD_RESET_SINGLE_PREFIX + monitorName;
                    string displayName = LanguageManager.Korean ? $"🔄 {i + 1}) {monitorName} 초기화" : $"🔄 Reset {i + 1}) {monitorName}";
                    AddHardResetRow(presetName, displayName);
                }
            }

            // --- 2. 일반 프로필 핫키 행 추가 ---
            if (presets != null)
            {
                foreach (string preset in presets)
                {
                    string monitor = iniFile.Read("monitor", preset);
                    if (string.IsNullOrEmpty(monitor)) continue;
                    AddRow(preset);
                }
            }
            ThemeManager.Apply(this);

            // 게임 실행 상태에서 글로벌 핫키를 사용하려면 관리자 권한이 필요함을 안내합니다.
            Label adminNotice = new Label();
            adminNotice.AutoSize = true;
            adminNotice.Text = LanguageManager.Korean
                ? "⚠ 게임 실행 상태로 핫키를 사용하려면 관리자 권한 실행 필요"
                : "⚠ Run as administrator to use hotkeys while the game is running";
            adminNotice.ForeColor = Color.DarkOrange;
            adminNotice.Font = new Font(this.Font, FontStyle.Bold);
            adminNotice.Location = new Point(12, 405);
            Controls.Add(adminNotice);

            Label hotkeyPauseNotice = new Label();
            hotkeyPauseNotice.AutoSize = true;
            hotkeyPauseNotice.Text = LanguageManager.Korean
                ? "※ 핫키 설정창이 열려 있는 동안에는 핫키가 작동하지 않습니다."
                : "※ Hotkeys are disabled while the Hotkey Settings window is open.";
            hotkeyPauseNotice.ForeColor = Color.Gray;
            hotkeyPauseNotice.Location = new Point(12, 428);
            Controls.Add(hotkeyPauseNotice);

            adminNotice.BringToFront();
            hotkeyPauseNotice.BringToFront();
        }

        private void AddHardResetRow(string presetName, string displayName)
        {
            Panel rowPanel = new Panel();
            rowPanel.Width = 655;
            rowPanel.Height = 42;
            rowPanel.BackColor = ThemeManager.IsDark ? Color.FromArgb(45, 50, 60) : Color.FromArgb(235, 240, 245);

            Label label = new Label();
            label.Text = displayName;
            label.AutoEllipsis = true;
            label.Location = new Point(5, 10);
            label.Size = new Size(275, 22);
            label.Font = new Font(this.Font, FontStyle.Bold);

            Button setButton = new Button();
            setButton.Location = new Point(285, 5);
            setButton.Size = new Size(165, 30);

            Label modeLabel = new Label();
            modeLabel.Text = LanguageManager.Korean ? "[즉시 실행]" : "[Immediate]";
            modeLabel.Location = new Point(455, 10);
            modeLabel.Size = new Size(105, 30);
            modeLabel.TextAlign = ContentAlignment.MiddleCenter;
            modeLabel.ForeColor = ThemeManager.IsDark ? Color.Gray : Color.DarkGray;

            Button clearButton = new Button();
            clearButton.Location = new Point(565, 5);
            clearButton.Size = new Size(80, 30);
            clearButton.Text = LanguageManager.Korean ? "삭제" : "Clear";

            Keys key;
            GlobalHotkey.Modifiers modifiers;
            if (!TryParseHotkey(iniFile.Read(presetName, "Hotkeys"), out key, out modifiers))
            {
                key = Keys.None;
                modifiers = GlobalHotkey.Modifiers.None;
            }

            Row row = new Row
            {
                Preset = presetName,
                SetButton = setButton,
                ClearButton = clearButton,
                ModeCombo = null,
                Key = key,
                Modifiers = modifiers
            };
            rows.Add(row);
            UpdateButton(row);

            setButton.Click += delegate { BeginCapture(row, displayName); };
            clearButton.Click += delegate { ClearHotkey(row, displayName); };

            rowPanel.Controls.Add(label);
            rowPanel.Controls.Add(setButton);
            rowPanel.Controls.Add(modeLabel);
            rowPanel.Controls.Add(clearButton);
            panel.Controls.Add(rowPanel);
        }

        private void AddRow(string preset)
        {
            Panel rowPanel = new Panel();
            rowPanel.Width = 655;
            rowPanel.Height = 42;

            Label label = new Label();
            label.Text = preset;
            label.AutoEllipsis = true;
            label.Location = new Point(5, 10);
            label.Size = new Size(275, 22);

            Button setButton = new Button();
            setButton.Location = new Point(285, 5);
            setButton.Size = new Size(165, 30);

            ComboBox modeCombo = new ComboBox();
            modeCombo.Location = new Point(455, 10);
            modeCombo.Size = new Size(105, 30);
            modeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            modeCombo.Items.Add(LanguageManager.Korean ? "적용" : "Apply");
            modeCombo.Items.Add(LanguageManager.Korean ? "토글" : "Toggle");
            modeCombo.SelectedIndex = string.Equals(iniFile.Read("HotkeyMode", preset), "Toggle", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

            Button clearButton = new Button();
            clearButton.Location = new Point(565, 5);
            clearButton.Size = new Size(80, 30);
            clearButton.Text = LanguageManager.Korean ? "삭제" : "Clear";

            Keys key;
            GlobalHotkey.Modifiers modifiers;
            if (!TryParseHotkey(iniFile.Read(preset, "Hotkeys"), out key, out modifiers))
            {
                key = Keys.None;
                modifiers = GlobalHotkey.Modifiers.None;
            }

            Row row = new Row
            {
                Preset = preset,
                SetButton = setButton,
                ClearButton = clearButton,
                ModeCombo = modeCombo,
                Key = key,
                Modifiers = modifiers
            };
            rows.Add(row);
            UpdateButton(row);

            setButton.Click += delegate { BeginCapture(row, row.Preset); };
            clearButton.Click += delegate { ClearHotkey(row, row.Preset); };

            rowPanel.Controls.Add(label);
            rowPanel.Controls.Add(setButton);
            rowPanel.Controls.Add(modeCombo);
            rowPanel.Controls.Add(clearButton);
            panel.Controls.Add(rowPanel);
        }

        private void BeginCapture(Row row, string displayName)
        {
            capturePreset = row.Preset;
            status.Text = LanguageManager.Korean ? $"[{displayName}] 원하는 키를 누르세요. ESC = 핫키 삭제" : $"[{displayName}] Press a key combination. ESC = clear hotkey";
            row.SetButton.Text = LanguageManager.Korean ? "키 입력 대기..." : "Waiting for key...";
            KeyPreview = true;
            ActiveControl = null;
            Focus();
        }

        private void ClearHotkey(Row row, string displayName)
        {
            if (capturePreset == row.Preset)
                capturePreset = null;

            row.Key = Keys.None;
            row.Modifiers = GlobalHotkey.Modifiers.None;
            UpdateButton(row);

            status.Text = LanguageManager.Korean ? $"[{displayName}] 핫키가 삭제되었습니다. 저장을 눌러 적용하세요." : $"[{displayName}] Hotkey cleared. Click Save to apply.";
        }

        private void HotkeySettingsForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (string.IsNullOrEmpty(capturePreset)) return;

            Row row = rows.Find(r => r.Preset == capturePreset);
            if (row == null) return;

            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.ShiftKey ||
                e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
            {
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                row.Key = Keys.None;
                row.Modifiers = GlobalHotkey.Modifiers.None;
            }
            else
            {
                GlobalHotkey.Modifiers modifiers = GlobalHotkey.Modifiers.None;
                if (e.Control) modifiers |= GlobalHotkey.Modifiers.Control;
                if (e.Alt) modifiers |= GlobalHotkey.Modifiers.Alt;
                if (e.Shift) modifiers |= GlobalHotkey.Modifiers.Shift;
                if ((Control.ModifierKeys & Keys.LWin) != 0 || (Control.ModifierKeys & Keys.RWin) != 0)
                    modifiers |= GlobalHotkey.Modifiers.Win;

                Row conflict = rows.Find(r => r != row && r.Key == e.KeyCode && r.Modifiers == modifiers);
                if (conflict != null)
                {
                    string conflictName = conflict.Preset;
                    if (conflictName == HARD_RESET_ALL_PRESET) conflictName = LanguageManager.Korean ? "모든 디스플레이 초기화" : "Reset All Displays";
                    else if (conflictName.StartsWith(HARD_RESET_SINGLE_PREFIX)) conflictName = LanguageManager.Korean ? $"{conflictName.Substring(HARD_RESET_SINGLE_PREFIX.Length)} 초기화" : $"Reset {conflictName.Substring(HARD_RESET_SINGLE_PREFIX.Length)}";

                    DialogResult result = MessageBox.Show(
                        LanguageManager.Korean
                            ? $"이 핫키는 다른 프로필에서 사용 중입니다.\r\n\r\n[{conflictName}]에 지정되어 있습니다.\r\n\r\n현재 항목으로 변경하시겠습니까?"
                            : $"**This hotkey is already in use.**\r\n\r\nIt is assigned to [{conflictName}].\r\n\r\nChange it to the current entry?",
                        LanguageManager.Korean ? "핫키 중복" : "Hotkey Already Assigned",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (result != DialogResult.Yes)
                    {
                        e.SuppressKeyPress = true;
                        return;
                    }

                    conflict.Key = Keys.None;
                    conflict.Modifiers = GlobalHotkey.Modifiers.None;
                    UpdateButton(conflict);
                }

                row.Key = e.KeyCode;
                row.Modifiers = modifiers;
            }

            UpdateButton(row);
            capturePreset = null;
            status.Text = "";
            e.SuppressKeyPress = true;
        }

        private void UpdateButton(Row row)
        {
            row.SetButton.Text = FormatHotkey(row.Key, row.Modifiers);
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
                    case "ctrl":
                    case "control": modifiers |= GlobalHotkey.Modifiers.Control; break;
                    case "alt": modifiers |= GlobalHotkey.Modifiers.Alt; break;
                    case "shift": modifiers |= GlobalHotkey.Modifiers.Shift; break;
                    case "win":
                    case "windows": modifiers |= GlobalHotkey.Modifiers.Win; break;
                    default: return false;
                }
            }
            return true;
        }

        private static string SerializeHotkey(Keys key, GlobalHotkey.Modifiers modifiers)
        {
            if (key == Keys.None) return "";
            return FormatHotkey(key, modifiers);
        }

        private void SaveAndClose()
        {
            HashSet<string> duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Row row in rows)
            {
                string value = SerializeHotkey(row.Key, row.Modifiers);
                if (!string.IsNullOrEmpty(value) && !duplicates.Add(value))
                {
                    MessageBox.Show(
                        LanguageManager.Korean
                            ? "같은 핫키가 두 프로필에 지정되어 있습니다:\r\n" + value
                            : "The same hotkey is assigned to multiple profiles:\r\n" + value,
                        LanguageManager.Korean ? "핫키 중복" : "Duplicate Hotkey",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            foreach (Row row in rows)
            {
                string value = SerializeHotkey(row.Key, row.Modifiers);
                if (string.IsNullOrEmpty(value))
                {
                    iniFile.DeleteKey(row.Preset, "Hotkeys");
                    if (row.ModeCombo != null) iniFile.DeleteKey("HotkeyMode", row.Preset);
                }
                else
                {
                    iniFile.Write(row.Preset, value, "Hotkeys");
                    if (row.ModeCombo != null)
                    {
                        string mode = row.ModeCombo.SelectedIndex == 1 ? "Toggle" : "Apply";
                        iniFile.Write("HotkeyMode", mode, row.Preset);
                    }
                }
            }
            ChangesSaved = true;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}