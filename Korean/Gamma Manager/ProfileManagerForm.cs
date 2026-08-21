using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal sealed class ProfileManagerForm : Form
    {
        private readonly IniFile iniFile;
        private readonly ListBox listProfiles;
        private readonly Label info;
        private bool changed;

        public ProfileManagerForm(IniFile iniFile)
        {
            this.iniFile = iniFile;

            Text = LanguageManager.Korean ? "프로필 관리" : "Profile Manager";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(560, 430);

            info = new Label();
            info.AutoSize = false;
            info.Location = new Point(12, 12);
            info.Size = new Size(536, 42);
            info.Text = LanguageManager.Korean
                ? "저장된 프로필을 관리합니다.\r\n프로필을 선택한 뒤 삭제할 수 있습니다."
                : "Manage your saved profiles.\r\nSelect a profile and delete it if needed.";

            listProfiles = new ListBox();
            listProfiles.Location = new Point(12, 62);
            listProfiles.Size = new Size(536, 285);
            listProfiles.IntegralHeight = false;

            Button delete = new Button();
            delete.Text = LanguageManager.Korean ? "삭제" : "Delete";
            delete.Size = new Size(100, 32);
            delete.Location = new Point(328, 365);
            delete.Click += delegate { DeleteSelected(); };

            Button close = new Button();
            close.Text = LanguageManager.Korean ? "닫기" : "Close";
            close.Size = new Size(100, 32);
            close.Location = new Point(448, 365);
            close.Click += delegate { DialogResult = changed ? DialogResult.OK : DialogResult.Cancel; Close(); };

            Controls.Add(info);
            Controls.Add(listProfiles);
            Controls.Add(delete);
            Controls.Add(close);

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
                // A real profile always has a monitor key. This excludes [Hotkeys]
                // and any unrelated INI sections.
                string monitor = iniFile.Read("monitor", section);
                if (!string.IsNullOrEmpty(monitor))
                    listProfiles.Items.Add(section);
            }
        }

        private void DeleteSelected()
        {
            if (listProfiles.SelectedIndex < 0)
            {
                MessageBox.Show(LanguageManager.Korean ? "삭제할 프로필을 선택하세요." : "Select a profile to delete.",
                    LanguageManager.Korean ? "프로필 삭제" : "Delete Profile",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string preset = listProfiles.SelectedItem as string;
            if (string.IsNullOrEmpty(preset)) return;

            if (MessageBox.Show(
                (LanguageManager.Korean
                    ? "다음 프로필을 삭제할까요?\r\n\r\n" + preset + "\r\n\r\n이 프로필에 연결된 핫키도 함께 제거됩니다."
                    : "Delete this profile?\r\n\r\n" + preset + "\r\n\r\nIts assigned hotkey will also be removed."),
                LanguageManager.Korean ? "프로필 삭제" : "Delete Profile", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            // Delete the actual profile section.
            iniFile.DeleteSection(preset);
            // Hotkeys are stored as [Hotkeys] profileName=value.
            iniFile.DeleteKey(preset, "Hotkeys");

            changed = true;
            LoadProfiles();
            ThemeManager.Apply(this);
            if (listProfiles.Items.Count == 0)
                info.Text = LanguageManager.Korean ? "저장된 프로필이 없습니다." : "No saved profiles.";
        }
    }
}
