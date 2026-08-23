using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Windows.Forms;

namespace Gamma_Manager
{
    // Toolbar/button actions: backup, restore, update, dialogs, theme, and game-auto controls.
    public partial class Window : Form
    {
        private void buttonBackup_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = LanguageManager.Korean ? "프로필 백업 저장" : "Save Profile Backup";
                dialog.Filter = "Gamma Manager Backup (*.ini)|*.ini|All files (*.*)|*.*";
                dialog.FileName = "Tarkov-Gamma-Manager-v1.5-Backup.ini";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    File.Copy(iniFile.FilePath, dialog.FileName, true);
                    MessageBox.Show(
                        LanguageManager.Korean ? "저장된 모든 프로필, 핫키, 게임 자동 설정 및 프로그램 설정을 백업했습니다." :
                        "All saved profiles, hotkeys, Game Auto mappings, and application settings were backed up.",
                        LanguageManager.Korean ? "백업 완료" : "Backup Complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show((LanguageManager.Korean ? "백업에 실패했습니다.\r\n\r\n" : "Backup failed.\r\n\r\n") + ex.Message,
                        LanguageManager.Korean ? "백업 오류" : "Backup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void buttonRestore_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = LanguageManager.Korean ? "프로필 백업 불러오기" : "Load Profile Backup";
                dialog.Filter = "Gamma Manager Backup (*.ini)|*.ini|INI files (*.ini)|*.ini|All files (*.*)|*.*";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                DialogResult confirm = MessageBox.Show(
                    LanguageManager.Korean ?
                    "선택한 백업으로 현재 설정을 교체합니다.\r\n\r\n현재 설정은 자동으로 .pre-restore 백업으로 보관됩니다.\r\n\r\n계속하시겠습니까?" :
                    "The selected backup will replace your current settings.\r\n\r\nYour current settings will first be saved as a .pre-restore backup.\r\n\r\nContinue?",
                    LanguageManager.Korean ? "백업 불러오기" : "Restore Backup",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;

                try
                {
                    string restoreBackup = iniFile.FilePath + ".pre-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".ini";
                    if (File.Exists(iniFile.FilePath))
                        File.Copy(iniFile.FilePath, restoreBackup, true);

                    File.Copy(dialog.FileName, iniFile.FilePath, true);

                    // 메모리에 새 파일 내용을 강제로 즉시 덮어씌움 (새로 변경된 IniFile 캐시 구조에 맞춤)
                    iniFile.Reload();

                    MessageBox.Show(
                        LanguageManager.Korean ? "백업을 불러왔습니다. 프로그램을 다시 시작해 적용합니다." :
                        "The backup was restored. The application will restart to apply it.",
                        LanguageManager.Korean ? "복원 완료" : "Restore Complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);

                    // Start a restart-aware child first. The child waits for this
                    // instance to release the single-instance mutex, so restoring
                    // a backup no longer gets blocked by the old instance.
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = Application.ExecutablePath,
                        Arguments = "--restart",
                        UseShellExecute = true
                    });
                    Application.Exit();
                }
                catch (Exception ex)
                {
                    MessageBox.Show((LanguageManager.Korean ? "복원에 실패했습니다.\r\n\r\n" : "Restore failed.\r\n\r\n") + ex.Message,
                        LanguageManager.Korean ? "복원 오류" : "Restore Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private async void buttonUpdateCheck_Click(object sender, EventArgs e)
        {
            if (buttonUpdateCheck != null)
                buttonUpdateCheck.Enabled = false;

            try
            {
                const string apiUrl = "https://api.github.com/repos/SaPper0000/Tarkov-Gamma-Manager/releases/latest";
                const string releaseUrl = "https://github.com/SaPper0000/Tarkov-Gamma-Manager/releases/latest";

                using (WebClient client = new WebClient())
                {
                    client.Headers[HttpRequestHeader.UserAgent] = "Tarkov-Gamma-Manager";
                    string json = await client.DownloadStringTaskAsync(apiUrl);
                    Match match = Regex.Match(json, @"""tag_name""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
                    if (!match.Success)
                        throw new InvalidOperationException(LanguageManager.Korean ? "GitHub 릴리즈 정보를 읽을 수 없습니다." : "Could not read the GitHub release information.");

                    string latestText = match.Groups[1].Value.Trim();
                    string latestVersionText = latestText.TrimStart('v', 'V');
                    Version latestVersion;
                    Version currentVersion = new Version("1.5.0");
                    const int currentRevision = 0;

                    Match versionMatch = Regex.Match(latestVersionText, @"^(\d+\.\d+\.\d+)(?:J-(\d+))?$", RegexOptions.IgnoreCase);
                    if (!versionMatch.Success || !Version.TryParse(versionMatch.Groups[1].Value, out latestVersion))
                        throw new InvalidOperationException(LanguageManager.Korean ? "최신 버전 정보를 해석할 수 없습니다." : "Could not parse the latest version information.");

                    int latestRevision = 0;
                    if (versionMatch.Groups[2].Success)
                        int.TryParse(versionMatch.Groups[2].Value, out latestRevision);

                    bool newerRelease = latestVersion > currentVersion ||
                        (latestVersion == currentVersion && latestRevision > currentRevision);

                    if (newerRelease)
                    {
                        DialogResult result = MessageBox.Show(
                            LanguageManager.Korean
                                ? "새 버전이 있습니다.\r\n\r\n현재 버전: v1.5.0\r\n최신 버전: " + latestText + "\r\n\r\nGitHub 릴리즈 페이지를 열까요?"
                                : "A new version is available.\r\n\r\nCurrent version: v1.5.0\r\nLatest version: " + latestText + "\r\n\r\nOpen the GitHub release page?",
                            LanguageManager.Korean ? "업데이트 확인" : "Check for Updates",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                        if (result == DialogResult.Yes)
                            Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true });
                    }
                    else
                    {
                        MessageBox.Show(
                            LanguageManager.Korean ? "현재 최신 버전입니다.\r\n\r\n현재 버전: v1.5.0" : "You are using the latest version.\r\n\r\nCurrent version: v1.5.0",
                            LanguageManager.Korean ? "업데이트 확인" : "Check for Updates",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    (LanguageManager.Korean ? "업데이트를 확인하지 못했습니다.\r\n\r\n" : "Could not check for updates.\r\n\r\n") + ex.Message,
                    LanguageManager.Korean ? "업데이트 확인 오류" : "Update Check Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                if (buttonUpdateCheck != null)
                    buttonUpdateCheck.Enabled = true;
            }
        }

        private void buttonGameAuto_Click(object sender, EventArgs e)
        {
            string[] presets = iniFile.GetSections();
            using (GameAutoSettingsForm form = new GameAutoSettingsForm(iniFile, presets))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    EvaluateGameAutoState();
                }
            }
        }

        private void buttonHotkeys_Click(object sender, EventArgs e)
        {
            toolHotkeys_Click(sender, e);
        }

        private void toolProfiles_Click(object sender, EventArgs e)
        {
            using (ProfileManagerForm form = new ProfileManagerForm(iniFile))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    RefreshGlobalHotkeys();
                    initPresets();
                    initTrayMenu();
                }
            }
        }

        private void toolHotkeys_Click(object sender, EventArgs e)
        {
            string[] presets = iniFile.GetSections();
            SuspendGlobalHotkeys();
            try
            {
                using (HotkeySettingsForm form = new HotkeySettingsForm(iniFile, presets, displays))
                {
                    form.ShowDialog(this);
                    if (form.ChangesSaved)
                        ClearManualHotkeyStateAndRestoreBase();
                }
            }
            finally
            {
                ResumeGlobalHotkeys();
            }
        }

        private void buttonTheme_Click(object sender, EventArgs e)
        {
            bool dark = !ThemeManager.IsDark;
            ThemeManager.SetTheme(dark);
            iniFile.Write("Theme", dark ? "Dark" : "Light", "Settings");
            ApplyCurrentTheme();
        }

        private void checkBoxLogEnabled_CheckedChanged(object sender, EventArgs e)
        {
            bool enabled = checkBoxLogEnabled.Checked;
            if (iniFile != null)
                iniFile.Write("LogEnabled", enabled ? "True" : "False", "Settings");

            Logger.SetEnabled(enabled);
            if (enabled)
            {
                Logger.Info("Logging enabled by user.");
                Logger.Info("LogDirectory=" + Logger.DirectoryPath);
            }
        }

        private void checkBoxImageOff_CheckedChanged(object sender, EventArgs e)
        {
            if (pictureBox1 != null)
                pictureBox1.Visible = !checkBoxImageOff.Checked;

            if (iniFile != null)
                iniFile.Write("ImageOff", checkBoxImageOff.Checked ? "True" : "False", "Settings");
        }

        // --- 이미지 선택 콤보박스 이벤트 핸들러 ---
        private void comboBoxImageSelect_SelectedIndexChanged(object sender, EventArgs e)
        {
            int selectedIndex = comboBoxImageSelect.SelectedIndex;
            if (iniFile != null)
            {
                iniFile.Write("SelectedImage", selectedIndex.ToString(), "Settings");
            }
            UpdatePictureBoxImage(selectedIndex);
        }

        private void UpdatePictureBoxImage(int imageIndex)
        {
            if (pictureBox1 != null)
            {
                try
                {
                    if (imageIndex == 1)
                    {
                        // KillaTagilla 이미지 (Index 1)
                        pictureBox1.BackgroundImage = global::Gamma_Manager.Properties.Resources.KillaTagilla;
                    }
                    else
                    {
                        // 기본 이미지 (Index 0)
                        pictureBox1.BackgroundImage = global::Gamma_Manager.Properties.Resources.TestMonitor;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warn("Could not load alternate image: " + ex.Message);
                }
            }
        }
        // ------------------------------------------------
    }
}