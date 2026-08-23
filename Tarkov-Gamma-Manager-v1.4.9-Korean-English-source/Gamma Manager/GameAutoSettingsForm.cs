using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal sealed class GameAutoSettingsForm : Form
    {
        private sealed class Mapping
        {
            public string Profile;
            public string Exe;
            public bool Enabled;
            public override string ToString() { return Profile + "  ←  " + Exe; }
        }

        private readonly IniFile ini;
        private readonly string[] presets;
        private readonly List<Mapping> mappings = new List<Mapping>();
        private CheckedListBox list;
        private CheckBox autoProfileEnabled;
        private Button add;
        private Button remove;
        private Button edit;
        private Button save;
        private Button close;
        private Label help;
        private bool ko;

        public GameAutoSettingsForm(IniFile iniFile, string[] presetNames)
        {
            ini = iniFile;
            // Only real saved profiles belong in the Game Auto profile selector.
            // Internal INI sections such as [Hotkeys] and [AutoGame_1] must never
            // appear as selectable profiles. A real profile has a saved monitor key.
            List<string> realProfiles = new List<string>();
            if (presetNames != null)
            {
                foreach (string preset in presetNames)
                {
                    if (string.IsNullOrEmpty(preset)) continue;
                    if (preset.StartsWith("AutoGame_", StringComparison.OrdinalIgnoreCase)) continue;
                    if (preset.Equals("Hotkeys", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(iniFile.Read("monitor", preset)))
                        realProfiles.Add(preset);
                }
            }
            presets = realProfiles.ToArray();
            ko = LanguageManager.Korean;
            Text = ko ? "게임 자동 적용 관리" : "Game Auto Apply Manager";
            Width = 720; Height = 500; StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            Build();
            LoadMappings();
            ThemeManager.Apply(this);
        }

        private void Build()
        {
            Label title = new Label { Text = ko ? "게임 자동 프로필" : "Game Auto Profiles", Location = new Point(20, 18), AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
            Controls.Add(title);

            help = new Label {
                Text = ko ? "게임 창이 활성화되면 지정한 프로필을 적용합니다.\r\nAlt+Tab으로 게임에서 나오면 이전 디스플레이 설정으로 복구합니다.\r\n프로그램을 추가하면 원하는 게임마다 다른 프로필을 지정할 수 있습니다."
                          : "Applies the assigned profile when the game window is active.\r\nAlt+Tab away restores the previous display settings.\r\nAdd as many game programs as you need and assign a profile to each.",
                Location = new Point(20, 52), Size = new Size(660, 65)
            };
            Controls.Add(help);

            list = new CheckedListBox { Location = new Point(20, 125), Size = new Size(660, 220), IntegralHeight = false, CheckOnClick = true };
                        Controls.Add(list);

            string autoEnabledValue = ini.Read("AutoGameEnabled", "Settings");
            // Game Auto is OFF by default when the setting does not exist yet.
            bool autoEnabled = autoEnabledValue == "1" ||
                string.Equals(autoEnabledValue, "True", StringComparison.OrdinalIgnoreCase);
            autoProfileEnabled = new CheckBox { Text = ko ? "프로필 자동변경 On/Off" : "Auto Profile Switching On/Off", Location = new Point(20, 350), Size = new Size(300, 24), Checked = autoEnabled };
            autoProfileEnabled.CheckedChanged += delegate
            {
                Window owner = Owner as Window;
                if (owner != null)
                {
                    owner.SetGameAutoEnabledFromSettings(autoProfileEnabled.Checked);
                }
                else
                {
                    ini.Write("AutoGameEnabled", autoProfileEnabled.Checked ? "1" : "0", "Settings");
                }
            };
            Controls.Add(autoProfileEnabled);

            add = new Button { Text = ko ? "프로그램 추가" : "Add Program", Location = new Point(20, 402), Size = new Size(125, 32) };
            add.Click += Add_Click; Controls.Add(add);
            remove = new Button { Text = ko ? "선택 삭제" : "Remove Selected", Location = new Point(155, 402), Size = new Size(125, 32) };
            remove.Click += Remove_Click; Controls.Add(remove);
            edit = new Button { Text = ko ? "수정" : "Edit", Location = new Point(290, 402), Size = new Size(90, 32) };
            edit.Click += Edit_Click; Controls.Add(edit);
            save = new Button { Text = ko ? "저장" : "Save", Location = new Point(500, 402), Size = new Size(85, 32) };
            save.Click += delegate { SaveMappings(); DialogResult = DialogResult.OK; Close(); }; Controls.Add(save);
            close = new Button { Text = ko ? "닫기" : "Close", Location = new Point(595, 402), Size = new Size(85, 32), DialogResult = DialogResult.Cancel };
            Controls.Add(close);
        }

        private void LoadMappings()
        {
            mappings.Clear();
            string[] sections = ini.GetSections();
            if (sections != null)
            {
                foreach (string section in sections)
                {
                    if (!section.StartsWith("AutoGame_", StringComparison.OrdinalIgnoreCase)) continue;
                    string profile = ini.Read("Profile", section);
                    string exe = ini.Read("Process", section);
                    if (!string.IsNullOrEmpty(profile) && !string.IsNullOrEmpty(exe))
                        mappings.Add(new Mapping { Profile = profile, Exe = Path.GetFileName(exe), Enabled = ini.Read("Enabled", section) != "0" });
                }
            }

            // Backward compatibility with the previous one-game-per-profile format.
            if (mappings.Count == 0 && presets != null)
            {
                foreach (string p in presets)
                {
                    string exe = ini.Read("AutoProcess", p);
                    string en = ini.Read("AutoEnabled", p);
                    if (!string.IsNullOrEmpty(exe) && (en == "1" || string.Equals(en, "true", StringComparison.OrdinalIgnoreCase)))
                        mappings.Add(new Mapping { Profile = p, Exe = Path.GetFileName(exe), Enabled = true });
                }
            }
            RefreshList();
        }

        private void RefreshList()
        {
            list.Items.Clear();
            foreach (Mapping m in mappings) list.Items.Add(m);
            for (int i = 0; i < mappings.Count; i++) list.SetItemChecked(i, mappings[i].Enabled);
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }

        private void Add_Click(object sender, EventArgs e)
        {
            if (presets.Length == 0)
            {
                MessageBox.Show(ko ? "먼저 프로필을 하나 이상 저장하세요." : "Save at least one profile first.");
                return;
            }
            using (Form f = new Form())
            {
                f.Text = ko ? "게임 프로그램 추가" : "Add Game Program";
                f.StartPosition = FormStartPosition.CenterParent; f.ClientSize = new Size(500, 190);
                f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false;
                Label lp = new Label { Text = ko ? "프로필" : "Profile", Location = new Point(20, 20), AutoSize = true };
                ComboBox cp = new ComboBox { Location = new Point(110, 16), Width = 350, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (string p in presets) cp.Items.Add(p);
                if (cp.Items.Count > 0) cp.SelectedIndex = 0;
                Label le = new Label { Text = ko ? "게임 EXE" : "Game EXE", Location = new Point(20, 65), AutoSize = true };
                TextBox te = new TextBox { Location = new Point(110, 61), Width = 275 };
                Button br = new Button { Text = ko ? "찾기" : "Browse", Location = new Point(392, 59), Width = 68 };
                br.Click += delegate { using (OpenFileDialog d = new OpenFileDialog { Filter = "Executable (*.exe)|*.exe", Title = ko ? "게임 실행 파일 선택" : "Select game executable" }) { if (d.ShowDialog(f) == DialogResult.OK) te.Text = Path.GetFileName(d.FileName); } };
                Button ok = new Button { Text = ko ? "추가" : "Add", Location = new Point(300, 135), Width = 75, DialogResult = DialogResult.OK };
                Button cancel = new Button { Text = ko ? "취소" : "Cancel", Location = new Point(385, 135), Width = 75, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lp, cp, le, te, br, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                if (f.ShowDialog(this) == DialogResult.OK && cp.SelectedItem != null && !string.IsNullOrWhiteSpace(te.Text))
                {
                    mappings.Add(new Mapping { Profile = cp.SelectedItem.ToString(), Exe = Path.GetFileName(te.Text.Trim()), Enabled = true });
                    RefreshList();
                }
            }
        }

        private void Remove_Click(object sender, EventArgs e)
        {
            if (list.SelectedIndex < 0) return;
            mappings.RemoveAt(list.SelectedIndex); RefreshList();
        }

        private void Edit_Click(object sender, EventArgs e)
        {
            int index = list.SelectedIndex;
            if (index < 0 || index >= mappings.Count)
            {
                MessageBox.Show(ko ? "수정할 게임 자동 항목을 선택하세요." : "Select a Game Auto entry to edit.");
                return;
            }

            Mapping current = mappings[index];
            using (Form f = new Form())
            {
                f.Text = ko ? "게임 자동 프로필 수정" : "Edit Game Auto Profile";
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(500, 190);
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false;

                Label lp = new Label { Text = ko ? "프로필" : "Profile", Location = new Point(20, 20), AutoSize = true };
                ComboBox cp = new ComboBox { Location = new Point(110, 16), Width = 350, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (string p in presets) cp.Items.Add(p);
                int profileIndex = cp.Items.IndexOf(current.Profile);
                cp.SelectedIndex = profileIndex >= 0 ? profileIndex : (cp.Items.Count > 0 ? 0 : -1);

                Label le = new Label { Text = ko ? "게임 EXE" : "Game EXE", Location = new Point(20, 65), AutoSize = true };
                TextBox te = new TextBox { Location = new Point(110, 61), Width = 275, Text = current.Exe };
                Button br = new Button { Text = ko ? "찾기" : "Browse", Location = new Point(392, 59), Width = 68 };
                br.Click += delegate { using (OpenFileDialog d = new OpenFileDialog { Filter = "Executable (*.exe)|*.exe", Title = ko ? "게임 실행 파일 선택" : "Select game executable" }) { if (d.ShowDialog(f) == DialogResult.OK) te.Text = Path.GetFileName(d.FileName); } };
                Button ok = new Button { Text = ko ? "확인" : "OK", Location = new Point(300, 135), Width = 75, DialogResult = DialogResult.OK };
                Button cancel = new Button { Text = ko ? "취소" : "Cancel", Location = new Point(385, 135), Width = 75, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lp, cp, le, te, br, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;

                if (f.ShowDialog(this) == DialogResult.OK && cp.SelectedItem != null && !string.IsNullOrWhiteSpace(te.Text))
                {
                    current.Profile = cp.SelectedItem.ToString();
                    current.Exe = Path.GetFileName(te.Text.Trim());
                                        RefreshList();
                    if (index < list.Items.Count) list.SelectedIndex = index;
                }
            }
        }

        private void SaveMappings()
        {
            for (int i = 0; i < mappings.Count && i < list.Items.Count; i++) mappings[i].Enabled = list.GetItemChecked(i);
            string[] sections = ini.GetSections();
            if (sections != null)
                foreach (string s in sections)
                    if (s.StartsWith("AutoGame_", StringComparison.OrdinalIgnoreCase)) ini.DeleteSection(s);

            for (int i = 0; i < mappings.Count; i++)
            {
                string section = "AutoGame_" + (i + 1).ToString();
                Mapping m = mappings[i];
                ini.Write("Profile", m.Profile, section);
                ini.Write("Process", m.Exe, section);
                ini.Write("Enabled", m.Enabled ? "1" : "0", section);
            }

            // Clear the old format so it cannot create duplicate automatic matches.
            if (presets != null)
                foreach (string p in presets) { ini.DeleteKey("AutoEnabled", p); ini.DeleteKey("AutoProcess", p); }
        }
    }
}
