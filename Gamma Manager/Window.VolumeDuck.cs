using System;
using System.Drawing;
using System.Windows.Forms;

namespace Gamma_Manager
{
    public partial class Window : Form
    {
        private Button btnVolumeDuck;
        private bool isVolumeDucked = false;
        private int savedOriginalVolume = -1;
        private bool savedMuteState = false;
        private int savedTargetProcessId = -1;
        private string savedTargetProcessName = string.Empty;
        private string savedDuckTargetType = "Master";
        private readonly object _volumeDuckLock = new object();

        public bool IsVolumeDuckEnabled()
        {
            string val = iniFile?.Read("VolumeDuckEnabled", "Settings");
            return val == "1" || string.Equals(val, "True", StringComparison.OrdinalIgnoreCase);
        }

        public string GetVolumeDuckTarget()
        {
            string val = iniFile?.Read("VolumeDuckTarget", "Settings");
            if (string.IsNullOrEmpty(val)) return "Master";
            return val;
        }

        public string GetVolumeDuckProcessName()
        {
            return iniFile?.Read("VolumeDuckProcessName", "Settings") ?? string.Empty;
        }

        public int GetVolumeDuckLevel()
        {
            string val = iniFile?.Read("VolumeDuckLevel", "Settings");
            if (int.TryParse(val, out int level))
            {
                return Math.Max(0, Math.Min(100, level));
            }
            return 10;
        }

        public bool IsVolumeDuckOSDEnabled()
        {
            try
            {
                string val = iniFile?.Read("VolumeDuckOSD", "Settings");
                return string.IsNullOrEmpty(val) ||
                       val.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                       val == "1";
            }
            catch
            {
                return true;
            }
        }

        public bool IsVolumeDuckPersistentOSD()
        {
            try
            {
                string val = iniFile?.Read("VolumeDuckPersistentOSD", "Settings");
                return val == "1" || string.Equals(val, "True", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public void UpdateVolumeDuckButtonState()
        {
            if (btnVolumeDuck == null) return;

            bool isEnabled = IsVolumeDuckEnabled();
            bool ko = LanguageManager.Korean;

            if (isEnabled)
            {
                btnVolumeDuck.Text = ko ? "🔊 즉시 볼륨 전환  [ON]" : "🔊 Quick Volume Switch  [ON]";
                btnVolumeDuck.ForeColor = Color.FromArgb(255, 75, 75); // 빨간색 ON
            }
            else
            {
                btnVolumeDuck.Text = ko ? "🔊 즉시 볼륨 전환  [OFF]" : "🔊 Quick Volume Switch  [OFF]";
                btnVolumeDuck.ForeColor = ThemeManager.IsDark
                    ? Color.FromArgb(180, 186, 198) // 다크 테마에서도 선명하게 읽히는 차분한 회색
                    : Color.FromArgb(110, 115, 125); // 회색 OFF
            }
        }

        public void ToggleVolumeDuck()
        {
            if (!IsVolumeDuckEnabled()) return;

            lock (_volumeDuckLock)
            {
                bool ko = LanguageManager.Korean;
                string targetType = GetVolumeDuckTarget();
                string displayLink = GetForegroundDisplay()?.displayLink ?? currDisplay?.displayLink;

                if (!isVolumeDucked)
                {
                    savedDuckTargetType = targetType;
                    int targetLevel = GetVolumeDuckLevel();
                    bool persistent = IsVolumeDuckPersistentOSD();
                    bool osdEnabled = IsVolumeDuckOSDEnabled();

                    if (string.Equals(targetType, "ActiveWindow", StringComparison.OrdinalIgnoreCase))
                    {
                        // 1. 현재 활성 창(게임 등) 프로세스 볼륨 조절
                        int fgPid = AudioManager.GetForegroundProcessId();
                        string rawFgName = AudioManager.GetProcessNameById(fgPid);
                        string fgName = !string.IsNullOrEmpty(rawFgName) ? rawFgName : (ko ? "현재 창" : "Active App");

                        savedTargetProcessId = fgPid;
                        savedTargetProcessName = rawFgName;

                        int currentVol = -1;
                        if (!string.IsNullOrEmpty(rawFgName))
                        {
                            currentVol = AudioManager.GetProcessVolumeByName(rawFgName);
                        }
                        if (currentVol < 0 && fgPid > 0)
                        {
                            currentVol = AudioManager.GetProcessVolume(fgPid);
                        }

                        savedOriginalVolume = (currentVol >= 0) ? currentVol : 100;

                        // 프로세스 이름 및 PID 양쪽으로 볼륨 전환 적용 (멀티프로세스 오디오 렌더러 완벽 지원)
                        if (!string.IsNullOrEmpty(rawFgName))
                        {
                            AudioManager.SetProcessVolumeByName(rawFgName, targetLevel);
                        }
                        if (fgPid > 0)
                        {
                            AudioManager.SetProcessVolume(fgPid, targetLevel);
                        }

                        isVolumeDucked = true;

                        if (osdEnabled)
                        {
                            OSDForm.ShowMessage(displayLink, ko ? $"🔊 [{fgName}] 볼륨 전환: {targetLevel}%" : $"🔊 [{fgName}] Volume: {targetLevel}%", persistent);
                        }
                    }
                    else if (string.Equals(targetType, "SpecificProcess", StringComparison.OrdinalIgnoreCase))
                    {
                        // 2. 지정한 특정 프로그램 볼륨 조절
                        string procName = GetVolumeDuckProcessName();
                        if (string.IsNullOrWhiteSpace(procName)) procName = "App";

                        savedTargetProcessName = procName;
                        int currentVol = AudioManager.GetProcessVolumeByName(procName);
                        savedOriginalVolume = (currentVol >= 0) ? currentVol : 100;

                        AudioManager.SetProcessVolumeByName(procName, targetLevel);
                        isVolumeDucked = true;

                        if (osdEnabled)
                        {
                            OSDForm.ShowMessage(displayLink, ko ? $"🔊 [{procName}] 볼륨 전환: {targetLevel}%" : $"🔊 [{procName}] Volume: {targetLevel}%", persistent);
                        }
                    }
                    else
                    {
                        // 3. 전체 시스템 마스터 볼륨 조절 (기본값)
                        int currentVol = AudioManager.GetMasterVolume();
                        if (currentVol >= 0)
                        {
                            savedOriginalVolume = currentVol;
                        }
                        savedMuteState = AudioManager.IsMuted();

                        AudioManager.SetMasterVolume(targetLevel);
                        isVolumeDucked = true;

                        if (osdEnabled)
                        {
                            OSDForm.ShowMessage(displayLink, ko ? $"🔊 볼륨 전환: {targetLevel}%" : $"🔊 Volume Switch: {targetLevel}%", persistent);
                        }
                    }
                }
                else
                {
                    // 원래 볼륨 복원
                    bool osdEnabled = IsVolumeDuckOSDEnabled();
                    int restoreLevel = savedOriginalVolume >= 0 ? savedOriginalVolume : 100;

                    if (string.Equals(savedDuckTargetType, "ActiveWindow", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(savedTargetProcessName))
                        {
                            AudioManager.SetProcessVolumeByName(savedTargetProcessName, restoreLevel);
                        }
                        if (savedTargetProcessId > 0)
                        {
                            AudioManager.SetProcessVolume(savedTargetProcessId, restoreLevel);
                        }

                        isVolumeDucked = false;

                        if (osdEnabled)
                        {
                            string displayName = !string.IsNullOrEmpty(savedTargetProcessName) ? savedTargetProcessName : (ko ? "현재 창" : "Active App");
                            OSDForm.ShowMessage(displayLink, ko ? $"🔊 [{displayName}] 볼륨 복원: {restoreLevel}%" : $"🔊 [{displayName}] Restored: {restoreLevel}%", false);
                        }
                        else
                        {
                            OSDForm.HideOSD();
                        }
                    }
                    else if (string.Equals(savedDuckTargetType, "SpecificProcess", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(savedTargetProcessName))
                        {
                            AudioManager.SetProcessVolumeByName(savedTargetProcessName, restoreLevel);
                        }

                        isVolumeDucked = false;

                        if (osdEnabled)
                        {
                            OSDForm.ShowMessage(displayLink, ko ? $"🔊 [{savedTargetProcessName}] 볼륨 복원: {restoreLevel}%" : $"🔊 [{savedTargetProcessName}] Restored: {restoreLevel}%", false);
                        }
                        else
                        {
                            OSDForm.HideOSD();
                        }
                    }
                    else
                    {
                        restoreLevel = savedOriginalVolume >= 0 ? savedOriginalVolume : 50;
                        AudioManager.SetMasterVolume(restoreLevel);
                        if (savedMuteState)
                        {
                            AudioManager.SetMute(true);
                        }
                        isVolumeDucked = false;

                        if (osdEnabled)
                        {
                            OSDForm.ShowMessage(displayLink, ko ? $"🔊 볼륨 복원: {restoreLevel}%" : $"🔊 Volume Restored: {restoreLevel}%", false);
                        }
                        else
                        {
                            OSDForm.HideOSD();
                        }
                    }
                }
            }
        }

        public void RestoreVolumeIfDucked()
        {
            lock (_volumeDuckLock)
            {
                if (isVolumeDucked && savedOriginalVolume >= 0)
                {
                    if (string.Equals(savedDuckTargetType, "ActiveWindow", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(savedTargetProcessName))
                        {
                            AudioManager.SetProcessVolumeByName(savedTargetProcessName, savedOriginalVolume);
                        }
                        if (savedTargetProcessId > 0)
                        {
                            AudioManager.SetProcessVolume(savedTargetProcessId, savedOriginalVolume);
                        }
                    }
                    else if (string.Equals(savedDuckTargetType, "SpecificProcess", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(savedTargetProcessName))
                        {
                            AudioManager.SetProcessVolumeByName(savedTargetProcessName, savedOriginalVolume);
                        }
                    }
                    else
                    {
                        AudioManager.SetMasterVolume(savedOriginalVolume);
                        if (savedMuteState) AudioManager.SetMute(true);
                    }

                    isVolumeDucked = false;
                    OSDForm.HideOSD();
                }
            }
        }

        private void btnVolumeDuck_Click(object sender, EventArgs e)
        {
            OpenVolumeDuckSettings();
        }

        public void OpenVolumeDuckSettings()
        {
            SuspendGlobalHotkeys();
            try
            {
                using (VolumeDuckSettingsForm form = new VolumeDuckSettingsForm(iniFile))
                {
                    form.ShowDialog(this);
                }
            }
            finally
            {
                if (!IsVolumeDuckEnabled())
                {
                    RestoreVolumeIfDucked();
                }

                ResumeGlobalHotkeys();
                UpdateVolumeDuckButtonState();
            }
        }
    }
}
