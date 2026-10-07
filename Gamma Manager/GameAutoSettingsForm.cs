using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
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
            List<string> realProfiles = new List<string>();
            if (presetNames != null)
            {
                foreach (string preset in presetNames)
                {
                    if (string.IsNullOrEmpty(preset)) continue;
                    if (preset.StartsWith("AutoGame_", StringComparison.OrdinalIgnoreCase)) continue;
                    if (preset.Equals("Hotkeys", StringComparison.OrdinalIgnoreCase) || preset.Equals("Settings", StringComparison.OrdinalIgnoreCase)) continue;
                    if (preset.StartsWith("__HARD_RESET_", StringComparison.OrdinalIgnoreCase) || preset.StartsWith("__CYCLE_", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.IsNullOrEmpty(iniFile.Read("monitor", preset)))
                        realProfiles.Add(preset);
                }
            }
            presets = realProfiles.ToArray();
            ko = LanguageManager.Korean;
            Text = ko ? "게임 자동 적용 관리" : "Game Auto Apply Manager";
            Width = 720; Height = 510; StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            Build();
            LoadMappings();
            ThemeManager.Apply(this);
        }

        private void Build()
        {
            Label title = new Label { Text = ko ? "게임 자동 프로필" : "Game Auto Profiles", Location = new Point(20, 12), AutoSize = true, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
            Controls.Add(title);

            help = new Label
            {
                Text = ko ? "게임 창이 활성화되면 지정한 프로필을 자동으로 적용합니다.\r\nAlt+Tab 등으로 게임을 벗어나면 이전 설정으로 복구됩니다.\r\n\r\n[프로필 우선순위]\r\n 1. 핫키 / 토글\r\n 2. 게임 자동"
                          : "Automatically applies the assigned profile when the game window is active.\r\nRestores previous display settings when switching away (Alt+Tab).\r\n\r\n[Profile Priority]\r\n 1. Hotkeys / Toggle\r\n 2. Game Auto",
                Location = new Point(20, 38),
                Size = new Size(660, 105),
                Font = new Font("Segoe UI", 9f)
            };
            Controls.Add(help);

            list = new CheckedListBox { Location = new Point(20, 150), Size = new Size(660, 190), IntegralHeight = false, CheckOnClick = true };
            Controls.Add(list);

            string autoEnabledValue = ini.Read("AutoGameEnabled", "Settings");
            bool autoEnabled = autoEnabledValue == "1" || string.Equals(autoEnabledValue, "True", StringComparison.OrdinalIgnoreCase);
            autoProfileEnabled = new CheckBox { Text = ko ? "게임 자동 프로필 기능 On/Off (끄면 완전히 정지)" : "Game Auto Profile Switching On/Off", Location = new Point(20, 352), Size = new Size(400, 24), Checked = autoEnabled };
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

            add = new Button { Text = ko ? "프로그램 추가" : "Add Program", Location = new Point(20, 410), Size = new Size(125, 34) };
            add.Click += Add_Click; Controls.Add(add);
            remove = new Button { Text = ko ? "선택 삭제" : "Remove Selected", Location = new Point(155, 410), Size = new Size(125, 34) };
            remove.Click += Remove_Click; Controls.Add(remove);
            edit = new Button { Text = ko ? "수정" : "Edit", Location = new Point(290, 410), Size = new Size(90, 34) };
            edit.Click += Edit_Click; Controls.Add(edit);
            save = new Button { Text = ko ? "저장" : "Save", Location = new Point(500, 410), Size = new Size(85, 34) };
            save.Click += delegate { SaveMappings(); DialogResult = DialogResult.OK; Close(); }; Controls.Add(save);
            close = new Button { Text = ko ? "닫기" : "Close", Location = new Point(595, 410), Size = new Size(85, 34), DialogResult = DialogResult.Cancel };
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
            RefreshList();
        }

        private void RefreshList()
        {
            list.Items.Clear();
            foreach (Mapping m in mappings) list.Items.Add(m);
            for (int i = 0; i < mappings.Count; i++) list.SetItemChecked(i, mappings[i].Enabled);
            if (list.Items.Count > 0 && list.SelectedIndex < 0) list.SelectedIndex = 0;
        }

        private void ShowProcessSelector(TextBox targetBox, Form parent)
        {
            using (Form selectForm = new Form())
            {
                selectForm.Text = ko ? "현재 실행 중인 프로그램 선택" : "Select Running Program";
                selectForm.Size = new Size(450, 480);
                selectForm.StartPosition = FormStartPosition.CenterParent;
                selectForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                selectForm.MaximizeBox = false; selectForm.MinimizeBox = false;

                ListBox pList = new ListBox { Location = new Point(15, 15), Size = new Size(405, 370) };

                // Process 핸들 완벽 해제 및 안전한 프로세스 이름 추출
                Process[] procs = null;
                List<string> procNames = new List<string>();
                try
                {
                    int currentPid = Process.GetCurrentProcess().Id;
                    procs = Process.GetProcesses();

                    foreach (var p in procs)
                    {
                        try
                        {
                            if (p.Id == currentPid || p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle))
                                continue;

                            string exeName = string.Empty;
                            try
                            {
                                if (p.MainModule != null)
                                    exeName = Path.GetFileName(p.MainModule.FileName);
                            }
                            catch
                            {
                                exeName = p.ProcessName + ".exe";
                            }

                            if (!string.IsNullOrEmpty(exeName) && !procNames.Contains(exeName, StringComparer.OrdinalIgnoreCase))
                            {
                                procNames.Add(exeName);
                            }
                        }
                        catch { }
                    }
                }
                finally
                {
                    if (procs != null)
                    {
                        foreach (var p in procs)
                        {
                            try { p.Dispose(); } catch { }
                        }
                    }
                }

                procNames.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var item in procNames) pList.Items.Add(item);

                Button btnOk = new Button { Text = ko ? "선택" : "Select", Location = new Point(235, 395), Size = new Size(85, 30), DialogResult = DialogResult.OK };
                Button btnCancel = new Button { Text = ko ? "취소" : "Cancel", Location = new Point(335, 395), Size = new Size(85, 30), DialogResult = DialogResult.Cancel };

                pList.DoubleClick += delegate
                {
                    if (pList.SelectedItem != null)
                    {
                        targetBox.Text = pList.SelectedItem.ToString();
                        selectForm.DialogResult = DialogResult.OK;
                        selectForm.Close();
                    }
                };

                btnOk.Click += delegate
                {
                    if (pList.SelectedItem != null)
                    {
                        targetBox.Text = pList.SelectedItem.ToString();
                        selectForm.DialogResult = DialogResult.OK;
                        selectForm.Close();
                    }
                };

                selectForm.Controls.AddRange(new Control[] { pList, btnOk, btnCancel });
                ThemeManager.Apply(selectForm);
                selectForm.ShowDialog(parent);
            }
        }

        private void Add_Click(object sender, EventArgs e)
        {
            if (presets.Length == 0)
            {
                MessageBox.Show(ko ? "먼저 프로필을 하나 이상 저장하세요." : "Save at least one profile first.",
                    ko ? "알림" : "Notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (Form f = new Form())
            {
                f.Text = ko ? "게임 프로그램 추가" : "Add Game Program";
                f.StartPosition = FormStartPosition.CenterParent; f.ClientSize = new Size(540, 210);
                f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false;

                Label lp = new Label { Text = ko ? "프로필" : "Profile", Location = new Point(20, 20), AutoSize = true };
                ComboBox cp = new ComboBox { Location = new Point(110, 16), Width = 390, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (string p in presets) cp.Items.Add(p);
                if (cp.Items.Count > 0) cp.SelectedIndex = 0;

                Label le = new Label { Text = ko ? "게임 EXE" : "Game EXE", Location = new Point(20, 65), AutoSize = true };
                TextBox te = new TextBox { Location = new Point(110, 61), Width = 230 };
                Button br = new Button { Text = ko ? "찾기" : "Browse", Location = new Point(345, 59), Width = 75 };
                br.Click += delegate
                {
                    using (OpenFileDialog d = new OpenFileDialog { Filter = "Executable (*.exe)|*.exe", Title = ko ? "게임 실행 파일 선택" : "Select game executable" })
                    {
                        if (d.ShowDialog(f) == DialogResult.OK) te.Text = Path.GetFileName(d.FileName);
                    }
                };

                Button btnSelectProc = new Button { Text = ko ? "실행중 선택" : "Running", Location = new Point(425, 59), Width = 85 };
                btnSelectProc.Click += delegate { ShowProcessSelector(te, f); };

                Button ok = new Button { Text = ko ? "추가" : "Add", Location = new Point(340, 150), Width = 75, Height = 30, DialogResult = DialogResult.OK };
                Button cancel = new Button { Text = ko ? "취소" : "Cancel", Location = new Point(425, 150), Width = 75, Height = 30, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lp, cp, le, te, br, btnSelectProc, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                ThemeManager.Apply(f);

                if (f.ShowDialog(this) == DialogResult.OK && cp.SelectedItem != null && !string.IsNullOrWhiteSpace(te.Text))
                {
                    string exeName = Path.GetFileName(te.Text.Trim());

                    // 중복 등록 검사
                    if (mappings.Any(m => string.Equals(m.Exe, exeName, StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.Show(ko ? "이미 목록에 등록되어 있는 실행 파일입니다." : "This executable is already registered in the list.",
                            ko ? "중복 오류" : "Duplicate Entry", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    mappings.Add(new Mapping { Profile = cp.SelectedItem.ToString(), Exe = exeName, Enabled = true });
                    RefreshList();
                    list.SelectedIndex = mappings.Count - 1;
                }
            }
        }

        private void Remove_Click(object sender, EventArgs e)
        {
            if (list.SelectedIndex < 0)
            {
                MessageBox.Show(ko ? "삭제할 항목을 선택하세요." : "Select an item to remove.",
                    ko ? "알림" : "Notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int index = list.SelectedIndex;
            mappings.RemoveAt(index);
            RefreshList();

            if (mappings.Count > 0)
            {
                list.SelectedIndex = Math.Min(index, mappings.Count - 1);
            }
        }

        private void Edit_Click(object sender, EventArgs e)
        {
            int index = list.SelectedIndex;
            if (index < 0 || index >= mappings.Count)
            {
                MessageBox.Show(ko ? "수정할 항목을 선택하세요." : "Select an item to edit.",
                    ko ? "알림" : "Notice", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Mapping current = mappings[index];
            using (Form f = new Form())
            {
                f.Text = ko ? "게임 자동 프로필 수정" : "Edit Game Auto Profile";
                f.StartPosition = FormStartPosition.CenterParent; f.ClientSize = new Size(540, 210);
                f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MaximizeBox = false; f.MinimizeBox = false;

                Label lp = new Label { Text = ko ? "프로필" : "Profile", Location = new Point(20, 20), AutoSize = true };
                ComboBox cp = new ComboBox { Location = new Point(110, 16), Width = 390, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (string p in presets) cp.Items.Add(p);
                int pIdx = cp.Items.IndexOf(current.Profile);
                cp.SelectedIndex = pIdx >= 0 ? pIdx : (cp.Items.Count > 0 ? 0 : -1);

                Label le = new Label { Text = ko ? "게임 EXE" : "Game EXE", Location = new Point(20, 65), AutoSize = true };
                TextBox te = new TextBox { Location = new Point(110, 61), Width = 230, Text = current.Exe };
                Button br = new Button { Text = ko ? "찾기" : "Browse", Location = new Point(345, 59), Width = 75 };
                br.Click += delegate
                {
                    using (OpenFileDialog d = new OpenFileDialog { Filter = "Executable (*.exe)|*.exe", Title = ko ? "게임 실행 파일 선택" : "Select game executable" })
                    {
                        if (d.ShowDialog(f) == DialogResult.OK) te.Text = Path.GetFileName(d.FileName);
                    }
                };

                Button btnSelectProc = new Button { Text = ko ? "실행중 선택" : "Running", Location = new Point(425, 59), Width = 85 };
                btnSelectProc.Click += delegate { ShowProcessSelector(te, f); };

                Button ok = new Button { Text = ko ? "확인" : "OK", Location = new Point(340, 150), Width = 75, Height = 30, DialogResult = DialogResult.OK };
                Button cancel = new Button { Text = ko ? "취소" : "Cancel", Location = new Point(425, 150), Width = 75, Height = 30, DialogResult = DialogResult.Cancel };
                f.Controls.AddRange(new Control[] { lp, cp, le, te, br, btnSelectProc, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
                ThemeManager.Apply(f);

                if (f.ShowDialog(this) == DialogResult.OK && cp.SelectedItem != null && !string.IsNullOrWhiteSpace(te.Text))
                {
                    string newExe = Path.GetFileName(te.Text.Trim());

                    // 다른 항목과의 중복 검사
                    if (mappings.Where((m, idx) => idx != index).Any(m => string.Equals(m.Exe, newExe, StringComparison.OrdinalIgnoreCase)))
                    {
                        MessageBox.Show(ko ? "이미 다른 매핑에 등록되어 있는 실행 파일입니다." : "This executable is already registered in another mapping.",
                            ko ? "중복 오류" : "Duplicate Entry", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }

                    current.Profile = cp.SelectedItem.ToString();
                    current.Exe = newExe;
                    RefreshList();
                    list.SelectedIndex = index;
                }
            }
        }

        private void SaveMappings()
        {
            for (int i = 0; i < mappings.Count && i < list.Items.Count; i++)
            {
                mappings[i].Enabled = list.GetItemChecked(i);
            }

            string[] sections = ini.GetSections();
            if (sections != null)
            {
                foreach (string s in sections)
                {
                    if (s.StartsWith("AutoGame_", StringComparison.OrdinalIgnoreCase))
                        ini.DeleteSection(s);
                }
            }

            for (int i = 0; i < mappings.Count; i++)
            {
                string section = "AutoGame_" + (i + 1).ToString();
                Mapping m = mappings[i];
                ini.Write("Profile", m.Profile, section);
                ini.Write("Process", m.Exe, section);
                ini.Write("Enabled", m.Enabled ? "1" : "0", section);
            }
        }
    }
}