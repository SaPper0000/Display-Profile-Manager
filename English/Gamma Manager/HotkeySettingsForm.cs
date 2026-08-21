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
            public ComboBox ModeCombo;
            public Keys Key;
            public GlobalHotkey.Modifiers Modifiers;
        }

        private readonly IniFile iniFile;
        private readonly List<Row> rows = new List<Row>();
        private readonly FlowLayoutPanel panel;
        private readonly Label status;
        private string capturePreset;

        public HotkeySettingsForm(IniFile iniFile, string[] presets)
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
            panel.Size = new Size(680, 335);
            panel.AutoScroll = true;
            panel.FlowDirection = FlowDirection.TopDown;
            panel.WrapContents = false;
            panel.BorderStyle = BorderStyle.FixedSingle;

            status = new Label();
            status.AutoSize = true;
            status.Location = new Point(12, 420);

            Button save = new Button();
            save.Text = LanguageManager.Korean ? "저장" : "Save";
            save.Size = new Size(90, 30);
            save.Location = new Point(492, 445);
            save.Click += delegate { SaveAndClose(); };

            Button cancel = new Button();
            cancel.Text = LanguageManager.Korean ? "취소" : "Cancel";
            cancel.Size = new Size(90, 30);
            cancel.Location = new Point(592, 445);
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.Add(info);
            Controls.Add(panel);
            Controls.Add(status);
            Controls.Add(save);
            Controls.Add(cancel);

            ThemeManager.Apply(this);

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
            modeCombo.Location = new Point(455, 5);
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
            // Hotkeys are stored as [Hotkeys] / profile-name=...
            if (!TryParseHotkey(iniFile.Read("Hotkeys", preset), out key, out modifiers))
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

            setButton.Click += delegate { BeginCapture(row); };
            clearButton.Click += delegate { ClearHotkey(row); };

            rowPanel.Controls.Add(label);
            rowPanel.Controls.Add(setButton);
            rowPanel.Controls.Add(modeCombo);
            rowPanel.Controls.Add(clearButton);
            panel.Controls.Add(rowPanel);
        }

        private void BeginCapture(Row row)
        {
            capturePreset = row.Preset;
            status.Text = LanguageManager.Korean ? "[" + row.Preset + "] 원하는 키를 누르세요. ESC = 핫키 삭제" : "[" + row.Preset + "] Press a key combination. ESC = clear hotkey";
            row.SetButton.Text = LanguageManager.Korean ? "키 입력 대기..." : "Waiting for key...";
            KeyPreview = true;
            ActiveControl = null;
            Focus();
        }

        private void ClearHotkey(Row row)
        {
            if (capturePreset == row.Preset)
                capturePreset = null;

            row.Key = Keys.None;
            row.Modifiers = GlobalHotkey.Modifiers.None;
            UpdateButton(row);
            status.Text = LanguageManager.Korean ? "[" + row.Preset + "] 핫키가 삭제되었습니다. 저장을 눌러 적용하세요." : "[" + row.Preset + "] Hotkey cleared. Click Save to apply.";
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
                    iniFile.DeleteKey("Hotkeys", row.Preset);
                    iniFile.DeleteKey("HotkeyMode", row.Preset);
                }
                else
                {
                    iniFile.Write("Hotkeys", value, row.Preset);
                    string mode = row.ModeCombo != null && row.ModeCombo.SelectedIndex == 1 ? "Toggle" : "Apply";
                    iniFile.Write("HotkeyMode", mode, row.Preset);
                }
            }
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
