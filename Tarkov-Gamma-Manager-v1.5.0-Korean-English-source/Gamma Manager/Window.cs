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
    public partial class Window : Form
    {
        public Window()
        {
            InitializeComponent();
            EnableNumericEditing();
            try
            {
                // Use the same application icon for the main window and notification icon.
                var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (appIcon != null)
                {
                    this.Icon = appIcon;
                    notifyIcon.Icon = appIcon;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not load application icon: " + ex.Message);
            }
            customCulture = (System.Globalization.CultureInfo)System.Threading.Thread.CurrentThread.CurrentCulture.Clone();
            customCulture.NumberFormat.NumberDecimalSeparator = ",";

            iniFile = new IniFile("GammaManager.ini");

            // Restore the saved UI language before building the layout.
            string savedLanguage = iniFile.Read("Language", "Settings");
            LanguageManager.SetLanguage(!string.Equals(savedLanguage, "English", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(savedLanguage))
                iniFile.Write("Language", "Korean", "Settings");

            // Migrate the automatically-created Default profile name to match the UI language.
            // This keeps an existing Korean "기본값 - Monitor" profile from appearing as a
            // duplicate when the user switches to English, and vice versa.
            MigrateDefaultProfileLanguage();

            SetupModernLayout();

            // Logging is opt-in. When disabled, Logger performs no file I/O.
            string savedLogEnabled = iniFile.Read("LogEnabled", "Settings");
            bool logEnabled = string.Equals(savedLogEnabled, "True", StringComparison.OrdinalIgnoreCase) || savedLogEnabled == "1";
            checkBoxLogEnabled.CheckedChanged -= checkBoxLogEnabled_CheckedChanged;
            checkBoxLogEnabled.Checked = logEnabled;
            checkBoxLogEnabled.CheckedChanged += checkBoxLogEnabled_CheckedChanged;
            Logger.SetEnabled(logEnabled);
            if (logEnabled) Logger.Info("Logging enabled from saved Settings.");

            // Restore the saved right-side image visibility state.
            string savedImageOff = iniFile.Read("ImageOff", "Settings");
            bool imageOff = string.Equals(savedImageOff, "True", StringComparison.OrdinalIgnoreCase) || savedImageOff == "1";
            checkBoxImageOff.CheckedChanged -= checkBoxImageOff_CheckedChanged;
            checkBoxImageOff.Checked = imageOff;
            checkBoxImageOff.CheckedChanged += checkBoxImageOff_CheckedChanged;
            pictureBox1.Visible = !imageOff;

            // --- 콤보박스 이미지 변경 상태 복구 ---
            string savedImageIndexStr = iniFile.Read("SelectedImage", "Settings");
            int savedImageIndex = 0;
            int.TryParse(savedImageIndexStr, out savedImageIndex);
            if (savedImageIndex < 0 || savedImageIndex > 1) savedImageIndex = 0;

            if (comboBoxImageSelect != null && comboBoxImageSelect.Items.Count > savedImageIndex)
            {
                comboBoxImageSelect.SelectedIndexChanged -= comboBoxImageSelect_SelectedIndexChanged;
                comboBoxImageSelect.SelectedIndex = savedImageIndex;
                UpdatePictureBoxImage(savedImageIndex);
                comboBoxImageSelect.SelectedIndexChanged += comboBoxImageSelect_SelectedIndexChanged;
            }
            // -----------------------------

            // Keep the current behavior for existing installations: TopMost is enabled by default.
            // If the setting is missing or invalid, treat it as enabled and persist the default.
            string savedTopMost = iniFile.Read("TopMost", "Settings");
            bool topMost = string.IsNullOrWhiteSpace(savedTopMost)
                ? true
                : !string.Equals(savedTopMost, "False", StringComparison.OrdinalIgnoreCase)
                    && savedTopMost != "0";
            checkBoxTopMost.Checked = topMost;
            this.TopMost = topMost;
            if (string.IsNullOrWhiteSpace(savedTopMost))
                iniFile.Write("TopMost", "True", "Settings");

            ThemeManager.SetTheme(!string.Equals(iniFile.Read("Theme", "Settings"), "Light",
                StringComparison.OrdinalIgnoreCase));
            ApplyCurrentTheme();

            buttonAllColors.Font = new Font(buttonAllColors.Font.Name, buttonAllColors.Font.Size, FontStyle.Bold);


            Logger.Info("Enumerating Windows displays and physical monitor handles.");
            displays = Display.QueryDisplayDevices();
            displays.Reverse();
            Logger.Info("Display enumeration complete. Count=" + displays.Count);
            for (int i = 0; i < displays.Count; i++)
            {
                displays[i].numDisplay = i;
                comboBoxMonitors.Items.Add(i + 1 + ") " + displays[i].displayName);
            }
            // If the previous run ended unexpectedly, restore its saved display state first.
            StartupStateManager.RestorePending(displays);
            // Save the exact pre-program state (including the real gamma ramp) so a normal
            // exit can restore it and a later launch can recover from an unexpected exit.
            StartupStateManager.Capture(displays);
            currDisplay = displays[numDisplay];
            comboBoxMonitors.SelectedIndex = numDisplay;

            fillInfo(currDisplay);

            // Create a Default profile for every detected monitor from the settings present
            // when the application starts. Never overwrite an existing Default profile.
            EnsureDefaultProfile();
            InitializeCurrentPresetState();
            initPresets();
            this.Text = "Tarkov Gamma Manager v1.5.0";

            initTrayMenu();
            notifyIcon.ContextMenuStrip = contextMenu;
            RefreshGlobalHotkeys();
            if (IsGameAutoEnabled())
                SetupGameAutoHook();
            FormClosed += Window_FormClosed;
        }
    }
}