using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
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

        // 정규식 캐싱 (성능 최적화)
        private static readonly Regex ReleaseTagRegex = new Regex(@"""tag_name""\s*:\s*""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex VersionFormatRegex = new Regex(@"^(\d+\.\d+\.\d+)(?:[-._](\d+))?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private const string CustomImageFileName = "custom-banner.png";

        private void buttonBackup_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = LanguageManager.Korean ? "프로필 백업 저장" : "Save Profile Backup";
                dialog.Filter = "Display Profile Manager Backup (*.ini)|*.ini|All files (*.*)|*.*";
                dialog.DefaultExt = "ini";
                dialog.AddExtension = true;
                dialog.FileName = $"Display-Profile-Manager-Backup-{DateTime.Now:yyyyMMdd-HHmmss}.ini";

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    iniFile.Flush(); // 최신 메모리 캐시를 디스크에 즉시 저장
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
                    MessageBox.Show(
                        (LanguageManager.Korean ? "백업에 실패했습니다.\r\n\r\n" : "Backup failed.\r\n\r\n") + ex.Message,
                        LanguageManager.Korean ? "백업 오류" : "Backup Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
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
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (confirm != DialogResult.Yes) return;

                try
                {
                    iniFile.Flush();

                    string restoreBackup = iniFile.FilePath + ".pre-restore-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".ini";
                    if (File.Exists(iniFile.FilePath))
                        File.Copy(iniFile.FilePath, restoreBackup, true);

                    File.Copy(dialog.FileName, iniFile.FilePath, true);

                    iniFile.Reload();

                    MessageBox.Show(
                        LanguageManager.Korean ? "백업을 불러왔습니다. 프로그램을 다시 시작해 적용합니다." :
                        "The backup was restored. The application will restart to apply it.",
                        LanguageManager.Korean ? "복원 완료" : "Restore Complete",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

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
                    MessageBox.Show(
                        (LanguageManager.Korean ? "복원에 실패했습니다.\r\n\r\n" : "Restore failed.\r\n\r\n") + ex.Message,
                        LanguageManager.Korean ? "복원 오류" : "Restore Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
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

                        if (isClosing || IsDisposed) return;

                        Match match = ReleaseTagRegex.Match(json);
                        if (!match.Success)
                            throw new InvalidOperationException(LanguageManager.Korean ? "GitHub 릴리즈 정보를 읽을 수 없습니다." : "Could not read the GitHub release information.");

                        string latestText = match.Groups[1].Value.Trim();
                        string latestVersionText = latestText.TrimStart('v', 'V');
                        Version latestVersion;
                        Version currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 5, 4, 0);
                        int currentRevision = currentVersion.Revision > 0 ? currentVersion.Revision : 0;
                        string currentVersionText = "v" + currentVersion.ToString(3) + (currentRevision > 0 ? ("-" + currentRevision) : "");

                        Match versionMatch = VersionFormatRegex.Match(latestVersionText);
                        if (!versionMatch.Success || !Version.TryParse(versionMatch.Groups[1].Value, out latestVersion))
                            throw new InvalidOperationException(LanguageManager.Korean ? "최신 버전 정보를 해석할 수 없습니다." : "Could not parse the latest version information.");

                        int latestRevision = 0;
                        if (versionMatch.Groups[2].Success)
                            int.TryParse(versionMatch.Groups[2].Value, out latestRevision);

                        Version currentVersionNormalized = new Version(currentVersion.Major, currentVersion.Minor, currentVersion.Build);
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
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Information);

                            if (result == DialogResult.Yes)
                                Process.Start(new ProcessStartInfo(releaseUrl) { UseShellExecute = true });
                        }
                        else
                        {
                            MessageBox.Show(
                                LanguageManager.Korean ? "현재 최신 버전입니다.\r\n\r\n현재 버전: " + currentVersionText : "You are using the latest version.\r\n\r\nCurrent version: " + currentVersionText,
                                LanguageManager.Korean ? "업데이트 확인" : "Check for Updates",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!isClosing && !IsDisposed)
                {
                    MessageBox.Show(
                        (LanguageManager.Korean ? "업데이트를 확인하지 못했습니다.\r\n\r\n" : "Could not check for updates.\r\n\r\n") + ex.Message,
                        LanguageManager.Korean ? "업데이트 확인 오류" : "Update Check Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            finally
            {
                if (buttonUpdateCheck != null && !buttonUpdateCheck.IsDisposed)
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
            UpdateGameAutoButtonState();
        }

        private void buttonHotkeys_Click(object sender, EventArgs e)
        {
            toolHotkeys_Click(sender, e);
        }

        private void toolProfiles_Click(object sender, EventArgs e)
        {
            using (ProfileManagerForm form = new ProfileManagerForm(iniFile, displays))
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

        private string GetCustomImagePath()
        {
            return Path.Combine(AppPaths.ImagesDirectory, CustomImageFileName);
        }

        private void comboBoxImageSelect_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBoxImageSelect == null || !comboBoxImageSelect.Focused)
                return;

            if (comboBoxImageSelect.SelectedIndex == 0)
            {
                iniFile?.Write("SelectedImage", "Default", "Settings");
                iniFile?.Flush();
                UpdatePictureBoxImage();
            }
            else if (comboBoxImageSelect.SelectedIndex == 1)
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
                dialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    RestoreImageComboBoxSelection();
                    return;
                }

                try
                {
                    string destinationPath = GetCustomImagePath();

                    // 동일한 파일 경로인 경우 복사 생략 (File.Copy 점유 충돌 방지)
                    if (!string.Equals(Path.GetFullPath(dialog.FileName), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(dialog.FileName, destinationPath, true);
                    }

                    iniFile?.Write("SelectedImage", "Custom", "Settings");
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
                string.Equals(iniFile?.Read("SelectedImage", "Settings"), "Custom", StringComparison.OrdinalIgnoreCase) &&
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
                string.Equals(iniFile?.Read("SelectedImage", "Settings"), "Custom", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(customImagePath);

            Image nextImage = null;
            bool isCustomLoaded = false;

            try
            {
                if (useCustomImage)
                {
                    using (Image source = Image.FromFile(customImagePath))
                    {
                        nextImage = new Bitmap(source);
                        isCustomLoaded = true;
                    }
                }
                else
                {
                    nextImage = global::Gamma_Manager.Properties.Resources.DefaultCatBanner;
                }

                Image oldImage = pictureBox1.BackgroundImage;
                object oldTag = pictureBox1.Tag;

                pictureBox1.BackgroundImage = nextImage;
                pictureBox1.BackgroundImageLayout = ImageLayout.Zoom;
                pictureBox1.Tag = isCustomLoaded ? "Custom" : null;

                // 이전에 로드된 이미지가 동적 생성된 사용자 커스텀 이미지인 경우에만 Dispose (GDI 핸들 누수 완벽 방지)
                if (oldImage != null && string.Equals(oldTag as string, "Custom", StringComparison.Ordinal))
                {
                    oldImage.Dispose();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not load preview image: " + ex);

                Image oldImage = pictureBox1.BackgroundImage;
                object oldTag = pictureBox1.Tag;

                pictureBox1.BackgroundImage = global::Gamma_Manager.Properties.Resources.DefaultCatBanner;
                pictureBox1.BackgroundImageLayout = ImageLayout.Zoom;
                pictureBox1.Tag = null;

                if (oldImage != null && string.Equals(oldTag as string, "Custom", StringComparison.Ordinal))
                {
                    oldImage.Dispose();
                }
            }
        }

        private void buttonKorean_Click(object sender, EventArgs e)
        {
            ChangeLanguage(true);
        }

        private void buttonEnglish_Click(object sender, EventArgs e)
        {
            ChangeLanguage(false);
        }

        private void buttonOSDSettings_Click(object sender, EventArgs e)
        {
            using (OSDSettingsForm form = new OSDSettingsForm(iniFile))
            {
                form.ShowDialog(this);
            }
            UpdateOSDButtonState();
        }

        private void buttonUpdateDefault_Click(object sender, EventArgs e)
        {
            if (currDisplay == null) return;

            // 실수 방지 확인 창
            DialogResult confirm = MessageBox.Show(
                LanguageManager.Korean
                    ? $"현재 [{currDisplay.displayName}] 모니터의 설정을 새로운 '기본값'으로 저장하시겠습니까?\r\n\r\n(이후 핫키 토글 해제 및 프로그램 종료 시 이 설정이 적용됩니다.)"
                    : $"Save current settings as new Default for [{currDisplay.displayName}]?\r\n\r\n(Applied when toggles are off and upon program exit.)",
                LanguageManager.Korean ? "기본값 갱신 확인" : "Update Default",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes) return;

            string prefix = LanguageManager.Korean ? "기본값 - " : "Default - ";
            string defaultProfileName = prefix + currDisplay.displayName;

            // 1. 현재 화면 슬라이더 값 확정
            SyncMonitorSettingsForProfileSave();
            int savedMonitorBrightness = Clamp(trackBarMonitorBrightness.Value, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
            int savedMonitorContrast = currDisplay.isExternal
                ? Clamp(trackBarMonitorContrast.Value, trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum)
                : currDisplay.monitorContrast;

            // 2. INI 파일의 [기본값 - 모니터명] 섹션 갱신 (Config)
            iniFile.Write("monitor", currDisplay.displayName, defaultProfileName);
            if (!string.IsNullOrEmpty(currDisplay.hardwareId))
                iniFile.Write("hardwareId", currDisplay.hardwareId, defaultProfileName);
            if (!string.IsNullOrEmpty(currDisplay.monitorKey))
                iniFile.Write("monitorKey", DisplayService.GetMonitorKey(currDisplay), defaultProfileName);

            iniFile.Write("rGamma", currDisplay.rGamma.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), defaultProfileName);
            iniFile.Write("gGamma", currDisplay.gGamma.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), defaultProfileName);
            iniFile.Write("bGamma", currDisplay.bGamma.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), defaultProfileName);
            iniFile.Write("rContrast", currDisplay.rContrast.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), defaultProfileName);
            iniFile.Write("gContrast", currDisplay.gContrast.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), defaultProfileName);
            iniFile.Write("bContrast", currDisplay.bContrast.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), defaultProfileName);
            iniFile.Write("rBright", currDisplay.rBright.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), defaultProfileName);
            iniFile.Write("gBright", currDisplay.gBright.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), defaultProfileName);
            iniFile.Write("bBright", currDisplay.bBright.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), defaultProfileName);
            iniFile.Write("saturation", currDisplay.saturation.ToString(), defaultProfileName);
            iniFile.Write("shadowBoost", currDisplay.shadowBoost.ToString(), defaultProfileName);
            iniFile.Write("shadowBoostMode", currDisplay.shadowBoostMode.ToString(), defaultProfileName);
            iniFile.Write("monitorBrightness", savedMonitorBrightness.ToString(System.Globalization.CultureInfo.InvariantCulture), defaultProfileName);
            iniFile.Write("monitorContrast", savedMonitorContrast.ToString(System.Globalization.CultureInfo.InvariantCulture), defaultProfileName);

            iniFile.Flush();

            // 3. 현재 모니터의 활성 프로필 상태를 '기본값'으로 인식
            string monitorKey = DisplayService.GetMonitorKey(currDisplay);
            if (!string.IsNullOrEmpty(monitorKey))
                currentPresetByMonitor[monitorKey] = defaultProfileName;

            // 4. 종료 시 복원되는 State 백업도 현재 세팅으로 새로 캡처 (State 동기화)
            if (displays != null)
            {
                StartupStateManager.Capture(displays);
            }

            // 5. OSD 알림 및 UI 목록 갱신
            if (IsOSDEnabled())
                OSDForm.ShowMessage(currDisplay.displayLink, LanguageManager.Korean ? "💾 기본값 갱신됨" : "💾 Default Updated");

            initPresets();

            MessageBox.Show(
                LanguageManager.Korean ? "현재 모니터 설정이 새로운 기본값으로 등록되었습니다." : "Current settings saved as default.",
                LanguageManager.Korean ? "기본값 갱신 완료" : "Default Updated",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void btnShadowBoost_Click(object sender, EventArgs e)
        {
            if (currDisplay == null) return;

            using (ShadowBoostSettingsForm form = new ShadowBoostSettingsForm(currDisplay, displayService, () =>
            {
                MarkCurrentMonitorAsCustom();
                UpdateShadowBoostButtonState();
            }))
            {
                form.ShowDialog(this);
            }
            UpdateShadowBoostButtonState();
        }
    }
}