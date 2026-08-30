using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Windows.Forms;

namespace Gamma_Manager
{
    public partial class Window : Form
    {
        // 업데이트 확인용 재사용 HttpClient (타임아웃 10초)
        private static readonly HttpClient _updateHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        private void buttonBackup_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = LanguageManager.Korean ? "프로필 백업 저장" : "Save Profile Backup";
                dialog.Filter = "Display Profile Manager Backup (*.ini)|*.ini|All files (*.*)|*.*";
                dialog.FileName = $"Display-Profile-Manager-Backup-{DateTime.Now:yyyyMMdd-HHmmss}.ini";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    iniFile.Flush(); // 👈 최신 메모리 캐시를 디스크에 먼저 즉시 저장
                    File.Copy(iniFile.FilePath, dialog.FileName, true);
                    MessageBox.Show(
    LanguageManager.Korean
        ? "저장된 모든 프로필, 핫키 및 프로그램 설정을 백업했습니다."
        : "All saved profiles, hotkeys, and application settings were backed up.",
    LanguageManager.Korean ? "백업 완료" : "Backup Complete",
    MessageBoxButtons.OK,
    MessageBoxIcon.Information);
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
                dialog.Filter = "Display Profile Manager Backup (*.ini)|*.ini|INI files (*.ini)|*.ini|All files (*.*)|*.*";
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

                    iniFile.Reload();

                    MessageBox.Show(
                        LanguageManager.Korean ? "백업을 불러왔습니다. 프로그램을 다시 시작해 적용합니다." :
                        "The backup was restored. The application will restart to apply it.",
                        LanguageManager.Korean ? "복원 완료" : "Restore Complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);

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

        private void buttonOpenFolder_Click(object sender, EventArgs e)
        {
            try
            {
                string path = AppPaths.Root;
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);

                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Logger.Warn("Failed to open application folder: " + ex.Message);
            }
        }

        private async void buttonUpdateCheck_Click(object sender, EventArgs e)
        {
            if (buttonUpdateCheck != null)
                buttonUpdateCheck.Enabled = false;

            try
            {
                const string apiUrl = "https://api.github.com/repos/SaPper0000/Display-Profile-Manager/releases/latest";
                const string releaseUrl = "https://github.com/SaPper0000/Display-Profile-Manager/releases/latest";

                using (var request = new HttpRequestMessage(HttpMethod.Get, apiUrl))
                {
                    request.Headers.Add("User-Agent", "Display-Profile-Manager");

                    using (var response = await _updateHttpClient.SendAsync(request))
                    {
                        response.EnsureSuccessStatusCode();
                        string json = await response.Content.ReadAsStringAsync();

                        Match match = Regex.Match(json, @"""tag_name""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
                        if (!match.Success)
                            throw new InvalidOperationException(LanguageManager.Korean ? "GitHub 릴리즈 정보를 읽을 수 없습니다." : "Could not read the GitHub release information.");

                        string latestText = match.Groups[1].Value.Trim();
                        string latestVersionText = latestText.TrimStart('v', 'V');
                        Version latestVersion;
                        Version currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version("1.5.3.0");
                        const int currentRevision = 0;
                        string currentVersionText = "v" + currentVersion.ToString(3);

                        // 👈 'J-' 오타 제거 및 하이픈(-), 점(.), 언더바(_) 뒤의 리비전 번호를 정상 파싱하도록 수정
                        Match versionMatch = Regex.Match(latestVersionText, @"^(\d+\.\d+\.\d+)(?:[-._](\d+))?$", RegexOptions.IgnoreCase);
                        if (!versionMatch.Success || !Version.TryParse(versionMatch.Groups[1].Value, out latestVersion))
                            throw new InvalidOperationException(LanguageManager.Korean ? "최신 버전 정보를 해석할 수 없습니다." : "Could not parse the latest version information.");

                        int latestRevision = 0;
                        if (versionMatch.Groups[2].Success)
                            int.TryParse(versionMatch.Groups[2].Value, out latestRevision);

                        // 3자리(Major.Minor.Build)로 정규화하여 4자리 어셈블리 버전과의 비교 불일치(Revision = -1 vs 0) 방지
                        Version currentVersionRaw = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 5, 3, 0);
                        Version currentVersionNormalized = new Version(currentVersionRaw.Major, currentVersionRaw.Minor, currentVersionRaw.Build);
                        Version latestVersionNormalized = new Version(latestVersion.Major, latestVersion.Minor, latestVersion.Build);

                        bool newerRelease = latestVersionNormalized > currentVersionNormalized ||
                            (latestVersionNormalized == currentVersionNormalized && latestRevision > currentRevision);

                        if (newerRelease)
                        {
                            DialogResult result = MessageBox.Show(
                                LanguageManager.Korean
                                    ? "새 버전이 있습니다.\r\n\r\n현재 버전: " + currentVersionText + "\r\n최신 버전: " + latestText + "\r\n\r\nGitHub 릴리즈 페이지를 열까요?"
                                    : "A new version is available.\r\n\r\nCurrent version: " + currentVersionText + "\r\nLatest version: " + latestText + "\r\n\r\nOpen the GitHub release page?",
                                LanguageManager.Korean ? "업데이트 확인" : "Check for Updates",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Information);

                            if (result == DialogResult.Yes)
                                Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true });
                        }
                        else
                        {
                            MessageBox.Show(
                                LanguageManager.Korean ? "현재 최신 버전입니다.\r\n\r\n현재 버전: " + currentVersionText : "You are using the latest version.\r\n\r\nCurrent version: " + currentVersionText,
                                LanguageManager.Korean ? "업데이트 확인" : "Check for Updates",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
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

      
        private const string CustomImageFileName = "custom-banner.png";

        private string GetCustomImagePath()
        {
            return Path.Combine(AppPaths.ImagesDirectory, CustomImageFileName);
        }

        private void comboBoxImageSelect_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBoxImageSelect == null)
                return;

            // 프로그램 시작/언어 변경 중에는 SelectedIndex가 코드로 바뀔 수 있다.
            // 그때 기존 Custom 설정을 Default로 덮어쓰지 않도록 막는다.
            if (comboBoxImageSelect.Focused == false)
                return;

            // 0번: 기본 고양이 이미지
            if (comboBoxImageSelect.SelectedIndex == 0)
            {
                iniFile?.Write("SelectedImage", "Default", "Settings");
                iniFile?.Flush();

                UpdatePictureBoxImage();
                return;
            }

            // 1번: 사용자가 직접 고른 이미지
            if (comboBoxImageSelect.SelectedIndex == 1)
            {
                ChooseCustomImage();
            }
        }

        private void ChooseCustomImage()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = LanguageManager.Korean ? "내 이미지 선택" : "Choose My Image";
                dialog.Filter =
                    "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|" +
                    "All files (*.*)|*.*";

                dialog.Multiselect = false;
                dialog.CheckFileExists = true;
                dialog.InitialDirectory =
                    Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreImageComboBoxSelection();
                    return;
                }

                try
                {
                    string destinationPath = GetCustomImagePath();

                    // 폴더가 없으면 AppPaths.ImagesDirectory 접근 시 자동 생성된다.
                    File.Copy(dialog.FileName, destinationPath, true);

                    // 다음 실행에도 사용자 이미지를 쓰도록 설정값 저장
                    iniFile?.Write("SelectedImage", "Custom", "Settings");

                    // 앱 종료 직전에만 저장되길 기다리지 않고 즉시 INI에 기록
                    iniFile?.Flush();

                    UpdatePictureBoxImage();
                }
                catch (Exception ex)
                {
                    Logger.Warn("Could not save custom image: " + ex);

                    MessageBox.Show(
                        (LanguageManager.Korean
                            ? "선택한 이미지를 저장하지 못했습니다.\r\n\r\n"
                            : "Could not save the selected image.\r\n\r\n") + ex.Message,
                        LanguageManager.Korean ? "이미지 오류" : "Image Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);

                    RestoreImageComboBoxSelection();
                }
            }
        }

        private void RestoreImageComboBoxSelection()
        {
            if (comboBoxImageSelect == null)
                return;

            bool hasCustomImage =
                string.Equals(iniFile?.Read("SelectedImage", "Settings"), "Custom",
                    StringComparison.OrdinalIgnoreCase) &&
                File.Exists(GetCustomImagePath());

            comboBoxImageSelect.SelectedIndexChanged -= comboBoxImageSelect_SelectedIndexChanged;
            try
            {
                comboBoxImageSelect.SelectedIndex = hasCustomImage ? 1 : 0;
            }
            finally
            {
                comboBoxImageSelect.SelectedIndexChanged += comboBoxImageSelect_SelectedIndexChanged;
            }
        }

        private void UpdatePictureBoxImage()
        {
            if (pictureBox1 == null)
                return;

            string customImagePath = GetCustomImagePath();
            bool useCustomImage =
                string.Equals(iniFile?.Read("SelectedImage", "Settings"), "Custom",
                    StringComparison.OrdinalIgnoreCase) &&
                File.Exists(customImagePath);

            Image nextImage = null;

            try
            {
                if (useCustomImage)
                {
                    // FromFile 뒤 Bitmap으로 복제하면 이미지 파일이 잠기지 않는다.
                    using (Image source = Image.FromFile(customImagePath))
                    {
                        nextImage = new Bitmap(source);
                    }
                }
                else
                {
                    nextImage =
                        global::Gamma_Manager.Properties.Resources.DefaultCatBanner;
                }

                Image oldImage = pictureBox1.BackgroundImage;
                pictureBox1.BackgroundImage = nextImage;
                pictureBox1.BackgroundImageLayout = ImageLayout.Zoom;

                // 기본 리소스 이미지에는 Dispose를 호출하지 않는다.
                if (oldImage != null &&
                    oldImage != global::Gamma_Manager.Properties.Resources.DefaultCatBanner)
                {
                    oldImage.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not load preview image: " + ex);

                Image oldImage = pictureBox1.BackgroundImage;
                pictureBox1.BackgroundImage =
                    global::Gamma_Manager.Properties.Resources.DefaultCatBanner;
                pictureBox1.BackgroundImageLayout = ImageLayout.Zoom;

                if (oldImage != null &&
                    oldImage != global::Gamma_Manager.Properties.Resources.DefaultCatBanner)
                {
                    oldImage.Dispose();
                }
            }
        }

        private void buttonKorean_Click(object sender, EventArgs e)
        {
            SwitchLanguage(true);
        }

        private void buttonEnglish_Click(object sender, EventArgs e)
        {
            SwitchLanguage(false);
        }

        private void SwitchLanguage(bool isKorean)
        {
            LanguageManager.SetLanguage(isKorean);

            if (iniFile != null)
            {
                iniFile.Write("Language", isKorean ? "Korean" : "English", "Settings");
            }

            // 1. 메인 폼의 모든 컨트롤 텍스트 강제 즉시 갱신
            ApplyCurrentTheme();

            // 2. 트레이 메뉴 언어 다시 그리기
            initTrayMenu();

            // 3. 프리셋 목록 다시 로드
            initPresets();

            // 4. 단축키 핸들러에 바인딩된 텍스트 갱신을 위해 재등록
            RefreshGlobalHotkeys();
        }
        private void buttonOSDSettings_Click(object sender, EventArgs e)
        {
            using (OSDSettingsForm form = new OSDSettingsForm(iniFile))
            {
                form.ShowDialog(this);
            }
        }
    }
}