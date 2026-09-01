using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal sealed class HotkeySettingsForm : Form
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private sealed class Row
        {
            public string Preset;
            public string LegacyPreset;
            public Button SetButton;
            public Button ClearButton;
            public ComboBox ModeCombo;
            public CheckBox CycleCheck;
            public Keys Key;
            public GlobalHotkey.Modifiers Modifiers;
        }

        private readonly IniFile iniFile;
        private readonly List<Row> rows = new List<Row>();
        private readonly List<Font> createdFonts = new List<Font>();
        private readonly FlowLayoutPanel panel;
        private readonly Label status;
        private string capturePreset;
        internal bool ChangesSaved { get; private set; }

        public const string HARD_RESET_ALL_PRESET = "__HARD_RESET_ALL__";
        public const string HARD_RESET_SINGLE_PREFIX = "__HARD_RESET_";
        public const string CYCLE_SINGLE_PREFIX = "__CYCLE_";

        public HotkeySettingsForm(IniFile iniFile, string[] presets, List<Display.DisplayInfo> displays)
        {
            this.iniFile = iniFile;
            Text = LanguageManager.Korean ? "핫키 설정" : "Hotkey Settings";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(720, 520);
            Size = new Size(720, 520);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            KeyPreview = true;
            KeyDown += HotkeySettingsForm_KeyDown;
            MaximizeBox = false; MinimizeBox = false;

            Label info = new Label
            {
                Text = LanguageManager.Korean
                    ? "프로필별 글로벌 핫키를 지정하세요.\r\n'순환'에 체크한 프로필들은 [모니터 프로필 순환] 핫키를 누를 때마다 차례대로 적용됩니다."
                    : "Assign global hotkeys.\r\nProfiles with 'Cycle' checked will cycle sequentially when the Cycle hotkey is pressed.",
                AutoSize = true,
                Location = new Point(12, 12)
            };

            // 목록 패널 높이 및 위치
            panel = new FlowLayoutPanel
            {
                Location = new Point(12, 60),
                Size = new Size(680, 325),
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BorderStyle = BorderStyle.FixedSingle
            };

            status = new Label { AutoSize = true, Location = new Point(12, 392) };

            // 좌측 하단 2줄 줄바꿈 및 강조 색상(골드/노랑) 패널
            Panel noticeBox = new Panel
            {
                Location = new Point(14, 420),
                Size = new Size(465, 48),
                BackColor = Color.Transparent
            };

            Font noticeRegularFont = new Font("Segoe UI", 9f, FontStyle.Regular);
            Font noticeBoldFont = new Font("Segoe UI", 9f, FontStyle.Bold);
            createdFonts.Add(noticeRegularFont);
            createdFonts.Add(noticeBoldFont);

            noticeBox.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                using (SolidBrush normalBrush = new SolidBrush(ThemeManager.IsDark ? Color.FromArgb(175, 180, 190) : Color.FromArgb(95, 100, 110)))
                using (SolidBrush highlightBrush = new SolidBrush(Color.FromArgb(255, 195, 0))) // 👈 선명한 골드/노란색 강조
                {
                    if (LanguageManager.Korean)
                    {
                        // 1줄
                        g.DrawString("※ 다른 프로그램(게임 등) 실행 중 핫키를 사용하려면", noticeRegularFont, normalBrush, new PointF(0, 2));

                        // 2줄 (강조 텍스트 분할 렌더링)
                        string prefix = "    프로그램을 ";
                        string boldText = "[관리자 권한으로 실행]";
                        string suffix = "해야 정상 동작합니다.";

                        SizeF szPrefix = g.MeasureString(prefix, noticeRegularFont);
                        SizeF szBold = g.MeasureString(boldText, noticeBoldFont);

                        g.DrawString(prefix, noticeRegularFont, normalBrush, new PointF(0, 22));
                        g.DrawString(boldText, noticeBoldFont, highlightBrush, new PointF(szPrefix.Width - 5, 22));
                        g.DrawString(suffix, noticeRegularFont, normalBrush, new PointF(szPrefix.Width + szBold.Width - 10, 22));
                    }
                    else
                    {
                        g.DrawString("※ To use hotkeys while other apps (or games) are focused,", noticeRegularFont, normalBrush, new PointF(0, 2));

                        string prefix = "    Running with ";
                        string boldText = "[Administrator Privileges]";
                        string suffix = " is required.";

                        SizeF szPrefix = g.MeasureString(prefix, noticeRegularFont);
                        SizeF szBold = g.MeasureString(boldText, noticeBoldFont);

                        g.DrawString(prefix, noticeRegularFont, normalBrush, new PointF(0, 22));
                        g.DrawString(boldText, noticeBoldFont, highlightBrush, new PointF(szPrefix.Width - 5, 22));
                        g.DrawString(suffix, noticeRegularFont, normalBrush, new PointF(szPrefix.Width + szBold.Width - 10, 22));
                    }
                }
            };

            // 저장 / 취소 버튼
            Button save = new Button { Text = LanguageManager.Korean ? "저장" : "Save", Size = new Size(90, 34), Location = new Point(492, 426) };
            save.Click += delegate { SaveAndClose(); };

            Button cancel = new Button { Text = LanguageManager.Korean ? "취소" : "Cancel", Size = new Size(90, 34), Location = new Point(592, 426) };
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[] { info, panel, status, noticeBox, save, cancel });

            // 1. 전체 초기화 핫키
            AddSpecialRow(HARD_RESET_ALL_PRESET, LanguageManager.Korean ? "🔄 모든 디스플레이 초기화" : "🔄 Reset All Displays", false);

            if (displays != null)
            {
                // 2. 모니터별 초기화 & 순환 핫키
                for (int i = 0; i < displays.Count; i++)
                {
                    Display.DisplayInfo display = displays[i];
                    string monitorName = display.displayName;
                    string monitorKey = DisplayService.GetMonitorKey(display);
                    string resetKey = MonitorIdentity.GetStableSpecialPresetName(HARD_RESET_SINGLE_PREFIX, monitorKey);
                    string cycleKey = MonitorIdentity.GetStableSpecialPresetName(CYCLE_SINGLE_PREFIX, monitorKey);
                    if (string.IsNullOrEmpty(resetKey)) resetKey = HARD_RESET_SINGLE_PREFIX + monitorName;
                    if (string.IsNullOrEmpty(cycleKey)) cycleKey = CYCLE_SINGLE_PREFIX + monitorName;
                    string legacyResetKey = HARD_RESET_SINGLE_PREFIX + monitorName;
                    string legacyCycleKey = CYCLE_SINGLE_PREFIX + monitorName;
                    AddSpecialRow(resetKey, LanguageManager.Korean ? $"🔄 {i + 1}) {monitorName} 초기화" : $"🔄 Reset {i + 1}) {monitorName}", false, legacyResetKey);
                    AddSpecialRow(cycleKey, LanguageManager.Korean ? $"🔁 {i + 1}) {monitorName} 프로필 순환" : $"🔁 Cycle {i + 1}) {monitorName}", true, legacyCycleKey);
                }
            }

            // 3. 일반 프로필 핫키
            if (presets != null)
            {
                foreach (string preset in presets)
                {
                    if (string.IsNullOrEmpty(iniFile.Read("monitor", preset))) continue;
                    AddProfileRow(preset);
                }
            }

            ThemeManager.Apply(this);
        }

        private void AddSpecialRow(string presetName, string displayName, bool isCycle, string legacyPresetName = null)
        {
            Panel rowPanel = new Panel { Width = 655, Height = 42, BackColor = ThemeManager.IsDark ? Color.FromArgb(45, 50, 60) : Color.FromArgb(235, 240, 245) };

            Font boldFont = new Font(this.Font, FontStyle.Bold);
            createdFonts.Add(boldFont);

            Label label = new Label { Text = displayName, AutoEllipsis = true, Location = new Point(5, 10), Size = new Size(275, 22), Font = boldFont };
            Button setButton = new Button { Location = new Point(285, 5), Size = new Size(165, 30) };
            Label modeLabel = new Label { Text = LanguageManager.Korean ? (isCycle ? "[순환 엔진]" : "[즉시 실행]") : (isCycle ? "[Cycle]" : "[Immediate]"), Location = new Point(455, 10), Size = new Size(105, 30), TextAlign = ContentAlignment.MiddleCenter, ForeColor = ThemeManager.IsDark ? Color.Gray : Color.DarkGray };
            Button clearButton = new Button { Location = new Point(565, 5), Size = new Size(80, 30), Text = LanguageManager.Korean ? "해제" : "Clear" };

            string storedHotkey = iniFile.Read(presetName, "Hotkeys");
            if (string.IsNullOrEmpty(storedHotkey) && !string.IsNullOrEmpty(legacyPresetName))
                storedHotkey = iniFile.Read(legacyPresetName, "Hotkeys");
            TryParseHotkey(storedHotkey, out Keys key, out GlobalHotkey.Modifiers modifiers);
            Row row = new Row { Preset = presetName, LegacyPreset = legacyPresetName, SetButton = setButton, ClearButton = clearButton, ModeCombo = null, CycleCheck = null, Key = key, Modifiers = modifiers };
            rows.Add(row); UpdateButton(row);

            setButton.Click += delegate { BeginCapture(row, displayName); };
            clearButton.Click += delegate { ClearHotkey(row, displayName); };
            rowPanel.Controls.AddRange(new Control[] { label, setButton, modeLabel, clearButton });
            panel.Controls.Add(rowPanel);
        }

        private void AddProfileRow(string preset)
        {
            Panel rowPanel = new Panel { Width = 655, Height = 42 };
            Label label = new Label { Text = preset, AutoEllipsis = true, Location = new Point(5, 10), Size = new Size(220, 22) };
            Button setButton = new Button { Location = new Point(230, 5), Size = new Size(150, 30) };

            ComboBox modeCombo = new ComboBox { Location = new Point(385, 10), Size = new Size(75, 30), DropDownStyle = ComboBoxStyle.DropDownList };
            modeCombo.Items.AddRange(new object[] { LanguageManager.Korean ? "적용" : "Apply", LanguageManager.Korean ? "토글" : "Toggle" });
            modeCombo.SelectedIndex = string.Equals(iniFile.Read("HotkeyMode", preset), "Toggle", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

            CheckBox cycleCheck = new CheckBox { Text = LanguageManager.Korean ? "순환" : "Cycle", Location = new Point(470, 10), Size = new Size(85, 30) };
            cycleCheck.Checked = iniFile.Read("CycleInclude", preset) == "1";

            Button clearButton = new Button { Location = new Point(565, 5), Size = new Size(80, 30), Text = LanguageManager.Korean ? "해제" : "Clear" };

            TryParseHotkey(iniFile.Read(preset, "Hotkeys"), out Keys key, out GlobalHotkey.Modifiers modifiers);
            Row row = new Row { Preset = preset, SetButton = setButton, ClearButton = clearButton, ModeCombo = modeCombo, CycleCheck = cycleCheck, Key = key, Modifiers = modifiers };
            rows.Add(row); UpdateButton(row);

            setButton.Click += delegate { BeginCapture(row, row.Preset); };
            clearButton.Click += delegate { ClearHotkey(row, row.Preset); };
            rowPanel.Controls.AddRange(new Control[] { label, setButton, modeCombo, cycleCheck, clearButton });
            panel.Controls.Add(rowPanel);
        }

        private void BeginCapture(Row row, string displayName)
        {
            capturePreset = row.Preset;
            status.Text = LanguageManager.Korean ? $"[{displayName}] 원하는 키를 누르세요. ESC = 핫키 해제" : $"[{displayName}] Press a key combination. ESC = clear hotkey";
            row.SetButton.Text = LanguageManager.Korean ? "키 입력 대기..." : "Waiting for key...";
            KeyPreview = true; ActiveControl = null; Focus();
        }

        private void ClearHotkey(Row row, string displayName)
        {
            if (capturePreset == row.Preset) capturePreset = null;
            row.Key = Keys.None; row.Modifiers = GlobalHotkey.Modifiers.None;
            UpdateButton(row);
            status.Text = LanguageManager.Korean ? $"[{displayName}] 핫키가 해제되었습니다. 저장을 눌러 적용하세요." : $"[{displayName}] Hotkey cleared. Click Save to apply.";
        }

        private void HotkeySettingsForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (string.IsNullOrEmpty(capturePreset)) return;
            Row row = rows.Find(r => r.Preset == capturePreset);
            if (row == null) return;

            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.Menu || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
            { e.SuppressKeyPress = true; return; }

            if (e.KeyCode == Keys.Escape)
            {
                row.Key = Keys.None; row.Modifiers = GlobalHotkey.Modifiers.None;
            }
            else
            {
                GlobalHotkey.Modifiers modifiers = GlobalHotkey.Modifiers.None;
                if (e.Control) modifiers |= GlobalHotkey.Modifiers.Control;
                if (e.Alt) modifiers |= GlobalHotkey.Modifiers.Alt;
                if (e.Shift) modifiers |= GlobalHotkey.Modifiers.Shift;

                // Win API를 통해 LWin / RWin 상태 직접 검사
                bool lWinDown = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0;
                bool rWinDown = (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
                if (lWinDown || rWinDown) modifiers |= GlobalHotkey.Modifiers.Win;

                Row conflict = rows.Find(r => r != row && r.Key == e.KeyCode && r.Modifiers == modifiers);
                if (conflict != null)
                {
                    string conflictName = conflict.Preset;
                    DialogResult result = MessageBox.Show(
                        LanguageManager.Korean ? $"이 핫키는 [{conflictName}]에 지정되어 있습니다.\r\n변경하시겠습니까?" : $"Hotkey in use by [{conflictName}]. Change it?",
                        LanguageManager.Korean ? "핫키 중복" : "Duplicate", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result != DialogResult.Yes) { e.SuppressKeyPress = true; return; }
                    conflict.Key = Keys.None; conflict.Modifiers = GlobalHotkey.Modifiers.None; UpdateButton(conflict);
                }
                row.Key = e.KeyCode; row.Modifiers = modifiers;
            }
            UpdateButton(row); capturePreset = null; status.Text = ""; e.SuppressKeyPress = true;
        }

        private void UpdateButton(Row row) { row.SetButton.Text = FormatHotkey(row.Key, row.Modifiers); }

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
            key = Keys.None; modifiers = GlobalHotkey.Modifiers.None;
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

        private void SaveAndClose()
        {
            foreach (Row row in rows)
            {
                string value = (row.Key == Keys.None) ? "" : FormatHotkey(row.Key, row.Modifiers);
                if (string.IsNullOrEmpty(value))
                {
                    iniFile.DeleteKey(row.Preset, "Hotkeys");
                    if (!string.IsNullOrEmpty(row.LegacyPreset))
                        iniFile.DeleteKey(row.LegacyPreset, "Hotkeys");
                    if (row.ModeCombo != null) iniFile.DeleteKey("HotkeyMode", row.Preset);
                }
                else
                {
                    iniFile.Write(row.Preset, value, "Hotkeys");
                    if (!string.IsNullOrEmpty(row.LegacyPreset))
                        iniFile.DeleteKey(row.LegacyPreset, "Hotkeys");
                    if (row.ModeCombo != null) iniFile.Write("HotkeyMode", row.ModeCombo.SelectedIndex == 1 ? "Toggle" : "Apply", row.Preset);
                }

                if (row.CycleCheck != null)
                {
                    iniFile.Write("CycleInclude", row.CycleCheck.Checked ? "1" : "0", row.Preset);
                }
            }
            ChangesSaved = true; DialogResult = DialogResult.OK; Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Font font in createdFonts)
                {
                    font?.Dispose();
                }
                createdFonts.Clear();
            }
            base.Dispose(disposing);
        }
    }
}