using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal sealed class ProfileManagerForm : Form
    {
        private readonly IContainer components;
        private readonly IniFile iniFile;
        private readonly List<Display.DisplayInfo> displays;
        private readonly ListBox listProfiles;
        private readonly Label info;
        private readonly ContextMenuStrip sortMenu;
        private bool changed;

        public ProfileManagerForm(IniFile iniFile, List<Display.DisplayInfo> displays = null)
        {
            this.components = new Container();
            this.iniFile = iniFile;
            this.displays = displays;

            Text = LanguageManager.Korean ? "목록 관리" : "Manage List";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 440);

            info = new Label
            {
                AutoSize = false,
                Location = new Point(12, 12),
                Size = new Size(536, 42),
                Text = LanguageManager.Korean
                    ? "저장된 프로필을 관리합니다.\r\n다중 선택(Shift/Ctrl)하여 순서를 변경하거나 삭제할 수 있습니다."
                    : "Manage your saved profiles.\r\nYou can select multiple profiles to move or delete them."
            };

            listProfiles = new ListBox
            {
                Location = new Point(12, 58),
                Size = new Size(440, 315),
                IntegralHeight = false,
                SelectionMode = SelectionMode.MultiExtended
            };

            int btnY = 58;
            const int btnGap = 36;

            Button btnTop = new Button { Text = LanguageManager.Korean ? "▲▲ 맨위로" : "▲▲ Top", Size = new Size(86, 30), Location = new Point(460, btnY) };
            btnTop.Click += delegate { MoveProfilesToTop(); };
            btnY += btnGap;

            Button btnUp = new Button { Text = LanguageManager.Korean ? "▲ 위로" : "▲ Up", Size = new Size(86, 30), Location = new Point(460, btnY) };
            btnUp.Click += delegate { MoveProfilesUp(); };
            btnY += btnGap;

            Button btnDown = new Button { Text = LanguageManager.Korean ? "▼ 아래로" : "▼ Down", Size = new Size(86, 30), Location = new Point(460, btnY) };
            btnDown.Click += delegate { MoveProfilesDown(); };
            btnY += btnGap;

            Button btnBottom = new Button { Text = LanguageManager.Korean ? "▼▼ 맨아래" : "▼▼ Bottom", Size = new Size(86, 30), Location = new Point(460, btnY) };
            btnBottom.Click += delegate { MoveProfilesToBottom(); };
            btnY += btnGap;

            Button btnRename = new Button { Text = LanguageManager.Korean ? "이름 수정" : "Rename", Size = new Size(86, 30), Location = new Point(460, btnY) };
            btnRename.Click += delegate { RenameSelected(); };
            btnY += btnGap;

            Button btnSort = new Button { Text = LanguageManager.Korean ? "정렬 ▼" : "Sort ▼", Size = new Size(86, 30), Location = new Point(460, btnY) };

            sortMenu = new ContextMenuStrip(components);
            ToolStripMenuItem sortAscItem = new ToolStripMenuItem(LanguageManager.Korean ? "오름차순 (A-Z)" : "Ascending (A-Z)");
            sortAscItem.Click += delegate { SortProfilesByName(true); };
            ToolStripMenuItem sortDescItem = new ToolStripMenuItem(LanguageManager.Korean ? "내림차순 (Z-A)" : "Descending (Z-A)");
            sortDescItem.Click += delegate { SortProfilesByName(false); };
            sortMenu.Items.AddRange(new ToolStripItem[] { sortAscItem, sortDescItem });
            btnSort.Click += delegate { sortMenu.Show(btnSort, new Point(0, btnSort.Height)); };
            btnY += btnGap;

            Button btnExport = new Button { Text = LanguageManager.Korean ? "공유 복사" : "Export", Size = new Size(86, 30), Location = new Point(460, btnY) };
            btnExport.Click += delegate { ExportProfile(); };
            btnY += btnGap;

            Button btnImport = new Button { Text = LanguageManager.Korean ? "붙여넣기" : "Import", Size = new Size(86, 30), Location = new Point(460, btnY) };
            btnImport.Click += delegate { ImportProfile(); };

            Button delete = new Button { Text = LanguageManager.Korean ? "선택 삭제" : "Delete", Size = new Size(95, 32), Location = new Point(345, 390) };
            delete.Click += delegate { DeleteSelected(); };

            Button close = new Button { Text = LanguageManager.Korean ? "닫기" : "Close", Size = new Size(95, 32), Location = new Point(451, 390) };
            close.Click += delegate { Close(); };

            FormClosing += delegate
            {
                if (changed)
                {
                    SaveProfileOrder();
                    DialogResult = DialogResult.OK;
                    changed = false;
                }
            };

            Controls.AddRange(new Control[] { info, listProfiles, btnTop, btnUp, btnDown, btnBottom, btnRename, btnSort, btnExport, btnImport, delete, close });

            LoadProfiles();
            ThemeManager.Apply(this);
        }

        private void LoadProfiles()
        {
            listProfiles.Items.Clear();
            string[] sections = iniFile.GetSections();
            if (sections == null) return;

            foreach (string section in sections)
            {
                string monitor = iniFile.Read("monitor", section);
                if (!string.IsNullOrEmpty(monitor))
                    listProfiles.Items.Add(section);
            }
        }

        private void MoveProfilesUp()
        {
            if (listProfiles.SelectedItems.Count == 0) return;
            var indices = listProfiles.SelectedIndices.Cast<int>().OrderBy(x => x).ToList();
            if (indices[0] == 0) return;

            listProfiles.BeginUpdate();
            foreach (int index in indices)
            {
                object item = listProfiles.Items[index];
                listProfiles.Items.RemoveAt(index);
                listProfiles.Items.Insert(index - 1, item);
                listProfiles.SetSelected(index - 1, true);
            }
            listProfiles.EndUpdate();
            changed = true;
        }

        private void MoveProfilesDown()
        {
            if (listProfiles.SelectedItems.Count == 0) return;
            var indices = listProfiles.SelectedIndices.Cast<int>().OrderByDescending(x => x).ToList();
            if (indices[0] == listProfiles.Items.Count - 1) return;

            listProfiles.BeginUpdate();
            foreach (int index in indices)
            {
                object item = listProfiles.Items[index];
                listProfiles.Items.RemoveAt(index);
                listProfiles.Items.Insert(index + 1, item);
                listProfiles.SetSelected(index + 1, true);
            }
            listProfiles.EndUpdate();
            changed = true;
        }

        private void MoveProfilesToTop()
        {
            if (listProfiles.SelectedItems.Count == 0) return;

            var selected = listProfiles.SelectedItems.Cast<string>().ToList();
            var unselected = listProfiles.Items.Cast<string>().Where(x => !selected.Contains(x)).ToList();

            listProfiles.BeginUpdate();
            listProfiles.Items.Clear();
            foreach (var item in selected) listProfiles.Items.Add(item);
            foreach (var item in unselected) listProfiles.Items.Add(item);
            for (int i = 0; i < selected.Count; i++) listProfiles.SetSelected(i, true);
            listProfiles.EndUpdate();
            changed = true;
        }

        private void MoveProfilesToBottom()
        {
            if (listProfiles.SelectedItems.Count == 0) return;

            var selected = listProfiles.SelectedItems.Cast<string>().ToList();
            var unselected = listProfiles.Items.Cast<string>().Where(x => !selected.Contains(x)).ToList();

            listProfiles.BeginUpdate();
            listProfiles.Items.Clear();
            foreach (var item in unselected) listProfiles.Items.Add(item);
            foreach (var item in selected) listProfiles.Items.Add(item);
            for (int i = listProfiles.Items.Count - selected.Count; i < listProfiles.Items.Count; i++) listProfiles.SetSelected(i, true);
            listProfiles.EndUpdate();
            changed = true;
        }

        private void SortProfilesByName(bool ascending)
        {
            if (listProfiles.Items.Count <= 1) return;
            var items = listProfiles.Items.Cast<string>();
            items = ascending ? items.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase) : items.OrderByDescending(x => x, StringComparer.CurrentCultureIgnoreCase);

            var sortedItems = items.ToList();
            var selectedItems = listProfiles.SelectedItems.Cast<string>().ToHashSet();

            listProfiles.BeginUpdate();
            listProfiles.Items.Clear();
            foreach (var item in sortedItems) listProfiles.Items.Add(item);
            for (int i = 0; i < listProfiles.Items.Count; i++)
            {
                if (selectedItems.Contains(listProfiles.Items[i].ToString())) listProfiles.SetSelected(i, true);
            }
            listProfiles.EndUpdate();
            changed = true;
        }

        private void RenameSelected()
        {
            if (listProfiles.SelectedItems.Count != 1)
            {
                MessageBox.Show(LanguageManager.Korean ? "이름을 수정할 프로필을 하나만 선택하세요." : "Select only one profile to rename.",
                    LanguageManager.Korean ? "이름 수정" : "Rename Profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string oldName = listProfiles.SelectedItem.ToString();
            string newName = ShowInputDialog(LanguageManager.Korean ? "새로운 프로필 이름을 입력하세요:" : "Enter a new profile name:",
                LanguageManager.Korean ? "프로필 이름 수정" : "Rename Profile", oldName);

            if (string.IsNullOrWhiteSpace(newName) || newName.Equals(oldName, StringComparison.OrdinalIgnoreCase)) return;

            if (newName.Equals("Hotkeys", StringComparison.OrdinalIgnoreCase) || newName.Equals("Settings", StringComparison.OrdinalIgnoreCase) ||
                newName.StartsWith("__HARD_RESET_", StringComparison.OrdinalIgnoreCase) ||
                newName.StartsWith("__CYCLE_", StringComparison.OrdinalIgnoreCase) ||
                newName.StartsWith("AutoGame_", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(LanguageManager.Korean ? "사용할 수 없는 예약된 이름입니다." : "This name is reserved and cannot be used.",
                    LanguageManager.Korean ? "오류" : "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string[] sections = iniFile.GetSections();
            if (sections != null && sections.Contains(newName, StringComparer.OrdinalIgnoreCase))
            {
                MessageBox.Show(LanguageManager.Korean ? "이미 존재하는 프로필 이름입니다." : "A profile with this name already exists.",
                    LanguageManager.Korean ? "이름 중복" : "Name Exists", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            iniFile.RenameSection(oldName, newName);

            // 핫키 키 연동
            string hotkey = iniFile.Read(oldName, "Hotkeys");
            if (!string.IsNullOrEmpty(hotkey))
            {
                iniFile.Write(newName, hotkey, "Hotkeys");
                iniFile.DeleteKey(oldName, "Hotkeys");
            }

            // 게임 자동 매핑(AutoGame_*) 섹션 연동 갱신
            if (sections != null)
            {
                foreach (string sec in sections)
                {
                    if (sec.StartsWith("AutoGame_", StringComparison.OrdinalIgnoreCase))
                    {
                        if (string.Equals(iniFile.Read("Profile", sec), oldName, StringComparison.OrdinalIgnoreCase))
                            iniFile.Write("Profile", newName, sec);
                    }
                }
            }

            int selectedIndex = listProfiles.SelectedIndex;
            listProfiles.Items.RemoveAt(selectedIndex);
            listProfiles.Items.Insert(selectedIndex, newName);
            listProfiles.SelectedIndex = selectedIndex;
            changed = true;
        }

        private static string EscapeField(string text)
        {
            return Uri.EscapeDataString(text ?? string.Empty);
        }

        private static string UnescapeField(string text)
        {
            return Uri.UnescapeDataString(text ?? string.Empty);
        }

        private void ExportProfile()
        {
            if (listProfiles.SelectedItems.Count != 1)
            {
                MessageBox.Show(LanguageManager.Korean ? "공유할 프로필을 하나만 선택하세요." : "Select exactly one profile to share.",
                    LanguageManager.Korean ? "프로필 공유" : "Export Profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string p = listProfiles.SelectedItem.ToString();
            string raw = string.Join("|", "TGM-Profile", "v1.5.3-2", EscapeField(p),
                EscapeField(iniFile.Read("monitor", p)),
                EscapeField(iniFile.Read("hardwareId", p)),
                EscapeField(iniFile.Read("monitorKey", p)),
                EscapeField(iniFile.Read("rGamma", p)),
                EscapeField(iniFile.Read("gGamma", p)),
                EscapeField(iniFile.Read("bGamma", p)),
                EscapeField(iniFile.Read("rContrast", p)),
                EscapeField(iniFile.Read("gContrast", p)),
                EscapeField(iniFile.Read("bContrast", p)),
                EscapeField(iniFile.Read("rBright", p)),
                EscapeField(iniFile.Read("gBright", p)),
                EscapeField(iniFile.Read("bBright", p)),
                EscapeField(iniFile.Read("saturation", p)),
                EscapeField(iniFile.Read("monitorBrightness", p)),
                EscapeField(iniFile.Read("monitorContrast", p))
            );

            string b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(raw));

            try
            {
                Clipboard.SetText(b64, TextDataFormat.Text);
                MessageBox.Show(LanguageManager.Korean ? "클립보드에 프로필 코드가 복사되었습니다!\n디스코드나 메신저에 붙여넣기(Ctrl+V) 하세요." : "Profile code copied to clipboard!",
                    LanguageManager.Korean ? "프로필 복사" : "Profile Copied", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                MessageBox.Show(LanguageManager.Korean ? "클립보드 접근에 실패했습니다. 잠시 후 다시 시도해 주세요." : "Failed to access clipboard. Please try again.",
                    LanguageManager.Korean ? "오류" : "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ImportProfile()
        {
            string b64 = "";
            try
            {
                b64 = Clipboard.GetText().Trim();
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                MessageBox.Show(LanguageManager.Korean ? "클립보드 접근에 실패했습니다. 다시 시도해 주세요." : "Failed to access clipboard. Please try again.",
                    LanguageManager.Korean ? "가져오기 오류" : "Import Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                string raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64));
                string[] parts = raw.Split('|');

                if (parts.Length < 16 || parts[0] != "TGM-Profile")
                    throw new FormatException("Invalid header format");

                // v1.5.3-2: monitor | hardwareId | monitorKey | gamma/contrast/bright... (offset=2)
                // v1.5.3  : monitor | hardwareId | gamma/contrast/bright...              (offset=1)
                // legacy  : gamma/contrast/bright...                                      (offset=0)
                bool isV1532 = string.Equals(parts[1], "v1.5.3-2", StringComparison.OrdinalIgnoreCase);
                bool isV153 = !isV1532 && string.Equals(parts[1], "v1.5.3", StringComparison.OrdinalIgnoreCase);

                if (isV1532 && parts.Length < 18)
                    throw new FormatException("Incomplete v1.5.3-2 profile format");
                if (isV153 && parts.Length < 17)
                    throw new FormatException("Incomplete v1.5.3 profile format");

                int offset = isV1532 ? 2 : (isV153 ? 1 : 0);
                string originalName = UnescapeField(parts[2]).Trim();
                int colonIdx = originalName.IndexOf(':');
                if (colonIdx >= 0)
                {
                    originalName = originalName.Substring(colonIdx + 1).Trim();
                }
                if (string.IsNullOrWhiteSpace(originalName))
                {
                    originalName = "Profile";
                }
                string defaultImportName = originalName + (LanguageManager.Korean ? " (가져옴)" : " (Imported)");

                string pName;
                Display.DisplayInfo targetDisplay;
                if (!ShowImportDialog(defaultImportName, out pName, out targetDisplay))
                    return;

                string fullProfileName = pName;
                if (targetDisplay != null)
                {
                    string prefix = targetDisplay.displayName + ": ";
                    string userChosenName = pName.Trim();
                    int lastColonIdx = userChosenName.LastIndexOf(':');
                    if (lastColonIdx >= 0)
                    {
                        userChosenName = userChosenName.Substring(lastColonIdx + 1).Trim();
                    }
                    fullProfileName = prefix + userChosenName;
                }

                string[] sections = iniFile.GetSections();
                if (sections != null && sections.Contains(fullProfileName, StringComparer.OrdinalIgnoreCase))
                {
                    if (MessageBox.Show(LanguageManager.Korean ? "같은 이름의 프로필이 있습니다. 덮어쓸까요?" : "Profile exists. Overwrite?",
                        LanguageManager.Korean ? "프로필 덮어쓰기" : "Import", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return;
                }

                if (targetDisplay != null)
                {
                    // 사용자가 지정한 현재 PC의 타깃 모니터 정보로 저장
                    iniFile.Write("monitor", targetDisplay.displayName, fullProfileName);
                    if (!string.IsNullOrEmpty(targetDisplay.hardwareId))
                        iniFile.Write("hardwareId", targetDisplay.hardwareId, fullProfileName);

                    string mk = DisplayService.GetMonitorKey(targetDisplay);
                    if (!string.IsNullOrEmpty(mk))
                        iniFile.Write("monitorKey", mk, fullProfileName);
                }
                else
                {
                    // 모니터 목록을 전달받지 못한 경우 공유 코드 원본 모니터 정보로 저장 (Fallback)
                    iniFile.Write("monitor", UnescapeField(parts[3]), fullProfileName);
                    if (isV1532 || isV153)
                    {
                        string hwId = UnescapeField(parts[4]);
                        if (!string.IsNullOrEmpty(hwId))
                            iniFile.Write("hardwareId", hwId, fullProfileName);
                    }
                    if (isV1532)
                    {
                        string mk = UnescapeField(parts[5]);
                        if (!string.IsNullOrEmpty(mk))
                            iniFile.Write("monitorKey", mk, fullProfileName);
                    }
                }

                iniFile.Write("rGamma", UnescapeField(parts[4 + offset]), fullProfileName);
                iniFile.Write("gGamma", UnescapeField(parts[5 + offset]), fullProfileName);
                iniFile.Write("bGamma", UnescapeField(parts[6 + offset]), fullProfileName);
                iniFile.Write("rContrast", UnescapeField(parts[7 + offset]), fullProfileName);
                iniFile.Write("gContrast", UnescapeField(parts[8 + offset]), fullProfileName);
                iniFile.Write("bContrast", UnescapeField(parts[9 + offset]), fullProfileName);
                iniFile.Write("rBright", UnescapeField(parts[10 + offset]), fullProfileName);
                iniFile.Write("gBright", UnescapeField(parts[11 + offset]), fullProfileName);
                iniFile.Write("bBright", UnescapeField(parts[12 + offset]), fullProfileName);
                iniFile.Write("saturation", UnescapeField(parts[13 + offset]), fullProfileName);
                iniFile.Write("monitorBrightness", UnescapeField(parts[14 + offset]), fullProfileName);
                iniFile.Write("monitorContrast", UnescapeField(parts[15 + offset]), fullProfileName);

                changed = true;
                LoadProfiles();
                listProfiles.SelectedItem = fullProfileName;
                MessageBox.Show(LanguageManager.Korean ? "프로필을 성공적으로 가져왔습니다!" : "Profile successfully imported!",
                    LanguageManager.Korean ? "가져오기 완료" : "Import", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch
            {
                MessageBox.Show(LanguageManager.Korean ? "클립보드의 텍스트가 올바른 TGM 프로필 코드가 아닙니다." : "Invalid profile code in clipboard.",
                    LanguageManager.Korean ? "가져오기 오류" : "Import Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private bool ShowImportDialog(string defaultName, out string chosenName, out Display.DisplayInfo chosenDisplay)
        {
            chosenName = string.Empty;
            chosenDisplay = null;

            using (Form prompt = new Form
            {
                Width = 440,
                Height = 230,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = LanguageManager.Korean ? "프로필 붙여넣기 (가져오기)" : "Import Profile",
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false
            })
            {
                Label nameLabel = new Label
                {
                    Left = 20, Top = 16, Width = 380,
                    Text = LanguageManager.Korean ? "가져올 프로필 이름:" : "Imported Profile Name:"
                };
                TextBox textBox = new TextBox
                {
                    Left = 20, Top = 40, Width = 380,
                    Text = defaultName
                };

                Label monitorLabel = new Label
                {
                    Left = 20, Top = 74, Width = 380,
                    Text = LanguageManager.Korean ? "적용할 대상 모니터 선택:" : "Select Target Monitor:"
                };
                ComboBox comboMonitors = new ComboBox
                {
                    Left = 20, Top = 98, Width = 380,
                    DropDownStyle = ComboBoxStyle.DropDownList
                };

                if (displays != null && displays.Count > 0)
                {
                    for (int i = 0; i < displays.Count; i++)
                    {
                        comboMonitors.Items.Add($"{i + 1}) {displays[i].displayName}");
                    }
                    comboMonitors.SelectedIndex = 0;
                }
                else
                {
                    comboMonitors.Items.Add(LanguageManager.Korean ? "(현재 연결된 모니터 기본값)" : "(Current Monitor Default)");
                    comboMonitors.SelectedIndex = 0;
                }

                Button confirmation = new Button
                {
                    Text = LanguageManager.Korean ? "확인" : "OK",
                    Left = 225, Width = 85, Top = 142, Height = 32,
                    DialogResult = DialogResult.OK
                };
                Button cancel = new Button
                {
                    Text = LanguageManager.Korean ? "취소" : "Cancel",
                    Left = 315, Width = 85, Top = 142, Height = 32,
                    DialogResult = DialogResult.Cancel
                };

                prompt.Controls.AddRange(new Control[] { nameLabel, textBox, monitorLabel, comboMonitors, confirmation, cancel });
                prompt.AcceptButton = confirmation;
                prompt.CancelButton = cancel;
                ThemeManager.Apply(prompt);

                if (prompt.ShowDialog(this) == DialogResult.OK)
                {
                    chosenName = textBox.Text.Trim();
                    if (displays != null && comboMonitors.SelectedIndex >= 0 && comboMonitors.SelectedIndex < displays.Count)
                    {
                        chosenDisplay = displays[comboMonitors.SelectedIndex];
                    }
                    return !string.IsNullOrWhiteSpace(chosenName);
                }

                return false;
            }
        }

        private string ShowInputDialog(string text, string caption, string defaultValue)
        {
            using (Form prompt = new Form
            {
                Width = 350,
                Height = 160,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = caption,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false
            })
            {
                Label textLabel = new Label { Left = 20, Top = 20, Width = 300, Text = text };
                TextBox textBox = new TextBox { Left = 20, Top = 50, Width = 290, Text = defaultValue };
                Button confirmation = new Button { Text = LanguageManager.Korean ? "확인" : "OK", Left = 140, Width = 80, Top = 85, DialogResult = DialogResult.OK };
                Button cancel = new Button { Text = LanguageManager.Korean ? "취소" : "Cancel", Left = 230, Width = 80, Top = 85, DialogResult = DialogResult.Cancel };

                prompt.Controls.AddRange(new Control[] { textLabel, textBox, confirmation, cancel });
                prompt.AcceptButton = confirmation;
                prompt.CancelButton = cancel;
                ThemeManager.Apply(prompt);

                return prompt.ShowDialog(this) == DialogResult.OK ? textBox.Text.Trim() : string.Empty;
            }
        }

        private void SaveProfileOrder()
        {
            List<string> orderedProfiles = listProfiles.Items.Cast<string>().ToList();
            iniFile.ReorderSections(orderedProfiles);
        }

        private void DeleteSelected()
        {
            if (listProfiles.SelectedItems.Count == 0)
            {
                MessageBox.Show(LanguageManager.Korean ? "삭제할 프로필을 선택하세요." : "Select a profile to delete.",
                    LanguageManager.Korean ? "프로필 삭제" : "Delete Profile", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            List<string> selectedProfiles = listProfiles.SelectedItems.Cast<string>()
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (selectedProfiles.Count == 0) return;

            string profileNames = string.Join("\r\n", selectedProfiles);
            string message = selectedProfiles.Count == 1
                ? (LanguageManager.Korean ? "다음 프로필을 삭제할까요?\r\n\r\n" + profileNames + "\r\n\r\n이 프로필에 연결된 핫키도 함께 제거됩니다."
                    : "Delete this profile?\r\n\r\n" + profileNames + "\r\n\r\nIts assigned hotkey will also be removed.")
                : (LanguageManager.Korean ? selectedProfiles.Count + "개의 프로필을 삭제할까요?\r\n\r\n" + profileNames + "\r\n\r\n선택한 프로필에 연결된 핫키도 함께 제거됩니다."
                    : "Delete " + selectedProfiles.Count + " profiles?\r\n\r\n" + profileNames + "\r\n\r\nAssigned hotkeys for the selected profiles will also be removed.");

            if (MessageBox.Show(message, LanguageManager.Korean ? "프로필 삭제" : "Delete Profile", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

            string[] sections = iniFile.GetSections();

            foreach (string preset in selectedProfiles)
            {
                iniFile.DeleteSection(preset);
                iniFile.DeleteKey(preset, "Hotkeys");

                // 게임 자동 매핑에서 삭제된 프로필 참조 제거
                if (sections != null)
                {
                    foreach (string sec in sections)
                    {
                        if (sec.StartsWith("AutoGame_", StringComparison.OrdinalIgnoreCase))
                        {
                            if (string.Equals(iniFile.Read("Profile", sec), preset, StringComparison.OrdinalIgnoreCase))
                                iniFile.DeleteSection(sec);
                        }
                    }
                }
            }

            changed = true;
            LoadProfiles();
            if (listProfiles.Items.Count == 0) info.Text = LanguageManager.Korean ? "저장된 프로필이 없습니다." : "No saved profiles.";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}