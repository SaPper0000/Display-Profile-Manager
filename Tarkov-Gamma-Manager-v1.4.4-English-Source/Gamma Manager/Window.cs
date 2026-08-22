using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Drawing;
using System.Windows.Forms;

namespace Gamma_Manager
{
    public partial class Window : Form
    {
        System.Globalization.CultureInfo customCulture;
        IniFile iniFile;

        List<Display.DisplayInfo> displays = new List<Display.DisplayInfo>();
        int numDisplay = 0;
        Display.DisplayInfo currDisplay;

        List<ToolStripComboBox> toolMonitors = new List<ToolStripComboBox>();
        ToolStripComboBox toolMonitor;

        readonly Dictionary<int, GlobalHotkey> globalHotkeys = new Dictionary<int, GlobalHotkey>();
        readonly Dictionary<int, string> globalHotkeyPresets = new Dictionary<int, string>();
        int nextHotkeyId = 1000;
        Button buttonTheme;
        Button buttonGameAuto;
        Button buttonBackup;
        Button buttonRestore;
        // Windows foreground-change event hook replaces the old 350ms polling timer.
        private IntPtr gameAutoWinEventHook = IntPtr.Zero;
        private WinApi.WinEventDelegate gameAutoWinEventDelegate;
        string activeAutoPreset = null;
        bool autoGameFocused = false;
        string manualOverridePreset = null;
        string manualOverrideProcess = null;
        bool manualOverrideActive = false;
        bool manualToggleActive = false;
        string manualTogglePreset = null;
        string manualToggleReturnPreset = null;
        readonly Dictionary<string, string> currentPresetByMonitor = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> manualTogglePresetByMonitor = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> manualToggleReturnPresetByMonitor = new Dictionary<string, string>(StringComparer.Ordinal);
        Label appTitle;
        Label appSubtitle;
        Label sectionDisplay;
        Label sectionGpu;
        Label sectionMonitor;
        Panel gpuCard;
        Panel monitorCard;
        Panel rightCard;
        Label imageCaption;
        Label imageSubCaption;
        CheckBox checkBoxMonitorEnabled;
        CheckBox checkBoxTopMost;

        bool disableChangeFunc = false;
        bool applyingPreset = false;

        bool allColors = true;
        bool redColor = false;
        bool greenColor = false;
        bool blueColor = false;

        private void clearColors()
        {
            buttonAllColors.Font = new Font(buttonAllColors.Font.Name, buttonAllColors.Font.Size, FontStyle.Regular);
            buttonRed.Font = new Font(buttonRed.Font.Name, buttonRed.Font.Size, FontStyle.Regular);
            buttonGreen.Font = new Font(buttonGreen.Font.Name, buttonGreen.Font.Size, FontStyle.Regular);
            buttonBlue.Font = new Font(buttonBlue.Font.Name, buttonBlue.Font.Size, FontStyle.Regular);

            allColors = false;
            redColor = false;
            greenColor = false;
            blueColor = false;
        }

        private void initPresets(string preferredPreset = null)
        {
            string current = preferredPreset;
            if (string.IsNullOrEmpty(current))
                current = comboBoxPresets.SelectedItem as string;

            comboBoxPresets.Items.Clear();

            string[] presets = iniFile.GetSections();
            if (presets != null)
            {
                for (int i = 0; i < presets.Length; i++)
                {
                    string monitor = iniFile.Read("monitor", presets[i]);
                    if (!string.IsNullOrEmpty(monitor) &&
                        monitor.Equals(currDisplay.displayName, StringComparison.Ordinal))
                    {
                        comboBoxPresets.Items.Add(presets[i]);
                    }
                }
            }

            if (!string.IsNullOrEmpty(current))
            {
                int index = comboBoxPresets.Items.IndexOf(current);
                if (index >= 0)
                {
                    disableChangeFunc = true;
                    comboBoxPresets.SelectedIndex = index;
                    disableChangeFunc = false;
                }
                else
                {
                    comboBoxPresets.Text = string.Empty;
                }
            }
            else
            {
                comboBoxPresets.Text = string.Empty;
            }
        }

        private void initTrayMenu()
        {
            contextMenu.Items.Clear();
            toolMonitors.Clear();

            ToolStripMenuItem toolSetting = new ToolStripMenuItem(LanguageManager.Korean ? "설정" : "Settings", null, toolSettings_Click);
            contextMenu.Items.Add(toolSetting);

            ToolStripMenuItem toolProfiles = new ToolStripMenuItem(LanguageManager.Korean ? "프로필..." : "Profiles...", null, toolProfiles_Click);
            contextMenu.Items.Add(toolProfiles);

            ToolStripMenuItem toolHotkeys = new ToolStripMenuItem(LanguageManager.Korean ? "핫키..." : "Hotkeys...", null, toolHotkeys_Click);
            contextMenu.Items.Add(toolHotkeys);

            ToolStripSeparator toolStripSeparator1 = new ToolStripSeparator();
            contextMenu.Items.Add(toolStripSeparator1);

            for (int i = 0; i < displays.Count; i++)
            {
                toolMonitor = new ToolStripComboBox(displays[i].displayName);
                toolMonitor.DropDownStyle = ComboBoxStyle.DropDownList;

                toolMonitor.Items.Add(displays[i].displayName + ":");
                toolMonitor.Text = displays[i].displayName + ":";

                toolMonitor.SelectedIndexChanged += new EventHandler(comboBoxToolMonitor_IndexChanged);

                string[] presets = iniFile.GetSections();
                if (presets != null)
                {
                    for (int j = 0; j < presets.Length; j++)
                    {
                        if (iniFile.Read("monitor", presets[j]).Equals(displays[i].displayName))
                        {
                            //preset.name = presets[j].Substring(presets[j].IndexOf(")") + 1);
                            toolMonitor.Items.Add(presets[j]);
                        }
                    }
                }
                toolMonitors.Add(toolMonitor);
                contextMenu.Items.Add(toolMonitor);
            }
            ToolStripSeparator toolStripSeparator2 = new ToolStripSeparator();
            contextMenu.Items.Add(toolStripSeparator2);
            ToolStripMenuItem toolExit = new ToolStripMenuItem(LanguageManager.Korean ? "종료" : "Exit", null, toolExit_Click);
            contextMenu.Items.Add(toolExit);
        }

        private void fillInfo(Display.DisplayInfo currDisplay)
        {
            disableChangeFunc = true;

            textBoxGamma.Text = ((currDisplay.rGamma + currDisplay.gGamma + currDisplay.bGamma) / 3f).ToString("0.00");
            textBoxContrast.Text = ((currDisplay.rContrast + currDisplay.gContrast + currDisplay.bContrast) / 3f).ToString("0.00");
            textBoxBrightness.Text = ((currDisplay.rBright + currDisplay.gBright + currDisplay.bBright) / 3f).ToString("0.00");
            textBoxSaturation.Text = currDisplay.saturation.ToString();

            trackBarGamma.Value = (int)(((currDisplay.rGamma + currDisplay.gGamma + currDisplay.bGamma) / 3f) * 100f);
            trackBarContrast.Value = (int)(((currDisplay.rContrast + currDisplay.gContrast + currDisplay.bContrast) / 3f) * 100f);
            trackBarBrightness.Value = (int)(((currDisplay.rBright + currDisplay.gBright + currDisplay.bBright) / 3f) * 100f);
            trackBarSaturation.Minimum = currDisplay.saturationMin;
            trackBarSaturation.Maximum = Math.Max(currDisplay.saturationMin + 1, currDisplay.saturationMax);
            trackBarSaturation.SmallChange = Math.Max(1, currDisplay.saturationStep);
            trackBarSaturation.Value = Clamp(currDisplay.saturation, trackBarSaturation.Minimum, trackBarSaturation.Maximum);
            trackBarSaturation.Enabled = currDisplay.saturationSupported;
            textBoxSaturation.ReadOnly = !currDisplay.saturationSupported;
            labelSaturation.Text = currDisplay.saturationSupported
                ? (currDisplay.adapterVendor == WinApi.DisplayAdapterVendor.Nvidia ? (LanguageManager.Korean ? "디지털\n바이브런스" : "Digital\nVibrance") : (LanguageManager.Korean ? "채도" : "Saturation"))
                : (LanguageManager.Korean ? "채도 (미지원 GPU)" : "Saturation (unsupported)");

            if (currDisplay.isExternal)
            {
                labelMonitorContrastUp.Visible = true;
                labelMonitorContrastDown.Visible = true;
                trackBarMonitorContrast.Visible = true;
                textBoxMonitorContrast.Visible = true;

                int liveBrightness;
                if (ExternalMonitor.TryGetBrightness(currDisplay.PhysicalHandle, out liveBrightness))
                    currDisplay.monitorBrightness = liveBrightness;
                int liveContrast;
                if (ExternalMonitor.TryGetContrast(currDisplay.PhysicalHandle, out liveContrast))
                    currDisplay.monitorContrast = liveContrast;

                int safeBrightness = Clamp(currDisplay.monitorBrightness, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                int safeContrast = Clamp(currDisplay.monitorContrast, trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum);
                textBoxMonitorBrightness.Text = safeBrightness.ToString();
                trackBarMonitorBrightness.Value = safeBrightness;
                textBoxMonitorContrast.Text = safeContrast.ToString();
                trackBarMonitorContrast.Value = safeContrast;
            }
            else
            {
                labelMonitorContrastUp.Visible = false;
                labelMonitorContrastDown.Visible = false;
                trackBarMonitorContrast.Visible = false;
                textBoxMonitorContrast.Visible = false;

                int liveBrightness;
                if (InternalMonitor.TryGetBrightness(out liveBrightness))
                    currDisplay.monitorBrightness = liveBrightness;

                int safeBrightness = Clamp(currDisplay.monitorBrightness, trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                textBoxMonitorBrightness.Text = safeBrightness.ToString();
                trackBarMonitorBrightness.Value = safeBrightness;
            }
            disableChangeFunc = false;

            // When monitor control is disabled, prevent the monitor adjustment controls
            // from being edited. Reapply this after every monitor/profile refresh so the
            // UI always reflects the master checkbox state.
            bool monitorEnabled = checkBoxMonitorEnabled.Checked;
            trackBarMonitorBrightness.Enabled = monitorEnabled;
            trackBarMonitorContrast.Enabled = monitorEnabled && currDisplay.isExternal;
            textBoxMonitorBrightness.Enabled = monitorEnabled;
            textBoxMonitorContrast.Enabled = monitorEnabled && currDisplay.isExternal;
        }
        private void Window_Load(object sender, EventArgs e)
        {
            int screenWidth = Screen.PrimaryScreen.Bounds.Size.Width;
            int windowWidth = Width;
            int screenHeight = Screen.PrimaryScreen.Bounds.Size.Height;
            int windowHeight = Height;
            int tmp = Screen.PrimaryScreen.Bounds.Height;
            int TaskBarHeight = tmp - Screen.PrimaryScreen.WorkingArea.Height;

            //dpi
            /*int PSH = SystemParameters.PrimaryScreenHeight;
            int PSBH = Screen.PrimaryScreen.Bounds.Height;
            double ratio = PSH / PSBH;
            int TaskBarHeight = PSBH - Screen.PrimaryScreen.WorkingArea.Height;
            TaskBarHeight *= ratio;*/

            Location = new Point(screenWidth - windowWidth, screenHeight - (windowHeight + TaskBarHeight));
        }

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
            catch { }
            SetupModernLayout();
            customCulture = (System.Globalization.CultureInfo)System.Threading.Thread.CurrentThread.CurrentCulture.Clone();
            customCulture.NumberFormat.NumberDecimalSeparator = ",";

            iniFile = new IniFile("GammaManager.ini");

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


            displays = Display.QueryDisplayDevices();
            displays.Reverse();
            for (int i = 0; i < displays.Count; i++)
            {
                displays[i].numDisplay = i;
                comboBoxMonitors.Items.Add(i+1+") "+ displays[i].displayName);
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
            this.Text = "Tarkov Gamma Manager v1.4.4 " + (LanguageManager.Korean ? "Korean" : "English");

            initTrayMenu();
            notifyIcon.ContextMenuStrip = contextMenu;
            RefreshGlobalHotkeys();
            SetupGameAutoHook();
            FormClosed += Window_FormClosed;
        }

        private void EnableNumericEditing()
        {
            textBoxGamma.ReadOnly = false;
            textBoxContrast.ReadOnly = false;
            textBoxBrightness.ReadOnly = false;
            textBoxSaturation.ReadOnly = false;
            textBoxMonitorBrightness.ReadOnly = false;
            textBoxMonitorContrast.ReadOnly = false;

            textBoxGamma.KeyDown += NumericBox_KeyDown;
            textBoxContrast.KeyDown += NumericBox_KeyDown;
            textBoxBrightness.KeyDown += NumericBox_KeyDown;
            textBoxSaturation.KeyDown += NumericBox_KeyDown;
            textBoxMonitorBrightness.KeyDown += NumericBox_KeyDown;
            textBoxMonitorContrast.KeyDown += NumericBox_KeyDown;

            textBoxGamma.Leave += NumericBox_Leave;
            textBoxContrast.Leave += NumericBox_Leave;
            textBoxBrightness.Leave += NumericBox_Leave;
            textBoxSaturation.Leave += NumericBox_Leave;
            textBoxMonitorBrightness.Leave += NumericBox_Leave;
            textBoxMonitorContrast.Leave += NumericBox_Leave;
        }

        private void NumericBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplyNumericBox((TextBox)sender);
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                disableChangeFunc = true;
                RefreshNumericBoxes();
                disableChangeFunc = false;
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        }

        private void NumericBox_Leave(object sender, EventArgs e)
        {
            ApplyNumericBox((TextBox)sender);
        }

        private void ApplyNumericBox(TextBox box)
        {
            if (disableChangeFunc || currDisplay == null) return;

            string text = box.Text.Trim().Replace(',', '.');
            double value;
            if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                disableChangeFunc = true;
                RefreshNumericBoxes();
                disableChangeFunc = false;
                return;
            }

            int trackValue;
            if (box == textBoxGamma)
            {
                trackValue = Clamp((int)Math.Round(value * 100.0), trackBarGamma.Minimum, trackBarGamma.Maximum);
                disableChangeFunc = true;
                trackBarGamma.Value = trackValue;
                disableChangeFunc = false;
                trackBarGamma_ValueChanged(trackBarGamma, EventArgs.Empty);
            }
            else if (box == textBoxContrast)
            {
                trackValue = Clamp((int)Math.Round(value * 100.0), trackBarContrast.Minimum, trackBarContrast.Maximum);
                disableChangeFunc = true;
                trackBarContrast.Value = trackValue;
                disableChangeFunc = false;
                trackBarContrast_ValueChanged(trackBarContrast, EventArgs.Empty);
            }
            else if (box == textBoxBrightness)
            {
                trackValue = Clamp((int)Math.Round(value * 100.0), trackBarBrightness.Minimum, trackBarBrightness.Maximum);
                disableChangeFunc = true;
                trackBarBrightness.Value = trackValue;
                disableChangeFunc = false;
                trackBarBrightness_ValueChanged(trackBarBrightness, EventArgs.Empty);
            }
            else if (box == textBoxSaturation)
            {
                trackValue = Clamp((int)Math.Round(value), trackBarSaturation.Minimum, trackBarSaturation.Maximum);
                disableChangeFunc = true;
                trackBarSaturation.Value = trackValue;
                disableChangeFunc = false;
                trackBarSaturation_ValueChanged(trackBarSaturation, EventArgs.Empty);
            }
            else if (box == textBoxMonitorBrightness)
            {
                trackValue = Clamp((int)Math.Round(value), trackBarMonitorBrightness.Minimum, trackBarMonitorBrightness.Maximum);
                if (!checkBoxMonitorEnabled.Checked) { RefreshNumericBoxes(); return; }
                disableChangeFunc = true;
                trackBarMonitorBrightness.Value = trackValue;
                disableChangeFunc = false;
                trackBarMonitorBrightness_ValueChanged(trackBarMonitorBrightness, EventArgs.Empty);
            }
            else if (box == textBoxMonitorContrast)
            {
                trackValue = Clamp((int)Math.Round(value), trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum);
                if (!checkBoxMonitorEnabled.Checked) { RefreshNumericBoxes(); return; }
                disableChangeFunc = true;
                trackBarMonitorContrast.Value = trackValue;
                disableChangeFunc = false;
                trackBarMonitorContrast_ValueChanged(trackBarMonitorContrast, EventArgs.Empty);
            }
        }

        private int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private void RefreshNumericBoxes()
        {
            textBoxGamma.Text = (trackBarGamma.Value / 100f).ToString("0.00");
            textBoxContrast.Text = (trackBarContrast.Value / 100f).ToString("0.00");
            textBoxBrightness.Text = (trackBarBrightness.Value / 100f).ToString("0.00");
            textBoxSaturation.Text = trackBarSaturation.Value.ToString();
            textBoxMonitorBrightness.Text = trackBarMonitorBrightness.Value.ToString();
            textBoxMonitorContrast.Text = trackBarMonitorContrast.Value.ToString();
        }

        private void trackBarGamma_ValueChanged(object sender, EventArgs e)
        {
            if (!disableChangeFunc)
            {
                comboBoxPresets.Text = string.Empty;
                textBoxGamma.Text = ((float)trackBarGamma.Value / 100f).ToString("0.00");

                if (allColors)
                {
                    currDisplay.rGamma = (float)trackBarGamma.Value / 100f;
                    currDisplay.gGamma = (float)trackBarGamma.Value / 100f;
                    currDisplay.bGamma = (float)trackBarGamma.Value / 100f;
                    Gamma.SetGammaRamp(currDisplay.displayLink,
                        Gamma.CreateGammaRamp(currDisplay.rGamma, currDisplay.gGamma, currDisplay.bGamma, currDisplay.rContrast,
                        currDisplay.gContrast, currDisplay.bContrast, currDisplay.rBright, currDisplay.gBright, currDisplay.bBright));
                    goto EndColors;
                }

                if (redColor)
                {
                    currDisplay.rGamma = (float)trackBarGamma.Value / 100f;
                    Gamma.SetGammaRamp(currDisplay.displayLink,
                        Gamma.CreateGammaRamp(currDisplay.rGamma, currDisplay.gGamma, currDisplay.bGamma, currDisplay.rContrast,
                        currDisplay.gContrast, currDisplay.bContrast, currDisplay.rBright, currDisplay.gBright, currDisplay.bBright));
                    goto EndColors;
                }

                if (greenColor)
                {
                    currDisplay.gGamma = (float)trackBarGamma.Value / 100f;
                    Gamma.SetGammaRamp(currDisplay.displayLink,
                        Gamma.CreateGammaRamp(currDisplay.rGamma, currDisplay.gGamma, currDisplay.bGamma, currDisplay.rContrast,
                        currDisplay.gContrast, currDisplay.bContrast, currDisplay.rBright, currDisplay.gBright, currDisplay.bBright));
                    goto EndColors;
                }

                if (blueColor)
                {
                    currDisplay.bGamma = (float)trackBarGamma.Value / 100f;
                    Gamma.SetGammaRamp(currDisplay.displayLink,
                        Gamma.CreateGammaRamp(currDisplay.rGamma, currDisplay.gGamma, currDisplay.bGamma, currDisplay.rContrast,
                        currDisplay.gContrast, currDisplay.bContrast, currDisplay.rBright, currDisplay.gBright, currDisplay.bBright));
                }

            EndColors:
                return;

            }
        }
            

        private void trackBarContrast_ValueChanged(object sender, EventArgs e)
        {
            if (!disableChangeFunc)
            {
                comboBoxPresets.Text = string.Empty;
                textBoxContrast.Text = ((float)trackBarContrast.Value / 100f).ToString("0.00");

                if (allColors)
                {
                    currDisplay.rContrast = (float)trackBarContrast.Value / 100f;
                    currDisplay.gContrast = (float)trackBarContrast.Value / 100f;
                    currDisplay.bContrast = (float)trackBarContrast.Value / 100f;
                    Gamma.SetGammaRamp(currDisplay.displayLink,
                        Gamma.CreateGammaRamp(currDisplay.rGamma, currDisplay.gGamma, currDisplay.bGamma, currDisplay.rContrast,
                        currDisplay.gContrast, currDisplay.bContrast, currDisplay.rBright, currDisplay.gBright, currDisplay.bBright));
                    goto EndColors;
                }

                if (redColor)
                {
                    currDisplay.rContrast = (float)trackBarContrast.Value / 100f;
                    Gamma.SetGammaRamp(currDisplay.displayLink,
                        Gamma.CreateGammaRamp(currDisplay.rGamma, currDisplay.gGamma, currDisplay.bGamma, currDisplay.rContrast,
                        currDisplay.gContrast, currDisplay.bContrast, currDisplay.rBright, currDisplay.gBright, currDisplay.bBright));
                    goto EndColors;
                }

                if (greenColor)
                {
                    currDisplay.gContrast = (float)trackBarContrast.Value / 100f;
                    Gamma.SetGammaRamp(currDisplay.displayLink,
                        Gamma.CreateGammaRamp(currDisplay.rGamma, currDisplay.gGamma, currDisplay.bGamma, currDisplay.rContrast,
                        currDisplay.gContrast, currDisplay.bContrast, currDisplay.rBright, currDisplay.gBright, currDisplay.bBright));
                    goto EndColors;
                }

                if (blueColor)
                {
                    currDisplay.bContrast = (float)trackBarContrast.Value / 100f;
                    Gamma.SetGammaRamp(currDisplay.displayLink,
                        Gamma.CreateGammaRamp(currDisplay.rGamma, currDisplay.gGamma, currDisplay.bGamma, currDisplay.rContrast,
                        currDisplay.gContrast, currDisplay.bContrast, currDisplay.rBright, currDisplay.gBright, currDisplay.bBright));
                }

            EndColors:
                return;
            }
        }

        private void trackBarBrightness_ValueChanged(object sender, EventArgs e)
        {
            if (!disableChangeFunc)
            {
                comboBoxPresets.Text = string.Empty;
                textBoxBrightness.Text = ((float)trackBarBrightness.Value / 100f).ToString("0.00");

                if (allColors)
                {
                    currDisplay.rBright = (float)trackBarBrightness.Value / 100f;
                    currDisplay.gBright = (float)trackBarBrightness.Value / 100f;
                    currDisplay.bBright = (float)trackBarBrightness.Value / 100f;
                    Gamma.SetGammaRamp(currDisplay.displayLink,
                        Gamma.CreateGammaRamp(currDisplay.rGamma, currDisplay.gGamma, currDisplay.bGamma, currDisplay.rContrast,
                        currDisplay.gContrast, currDisplay.bContrast, currDisplay.rBright, currDisplay.gBright, currDisplay.bBright));
                    goto EndColors;
                }

                if (redColor)
                {
                    currDisplay.rBright = (float)trackBarBrightness.Value / 100f;
                    Gamma.SetGammaRamp(currDisplay.displayLink,
                        Gamma.CreateGammaRamp(currDisplay.rGamma, currDisplay.gGamma, currDisplay.bGamma, currDisplay.rContrast,
                        currDisplay.gContrast, currDisplay.bContrast, currDisplay.rBright, currDisplay.gBright, currDisplay.bBright));
                    goto EndColors;
                }

                if (greenColor)
                {
                    currDisplay.gBright = (float)trackBarBrightness.Value / 100f;
                    Gamma.SetGammaRamp(currDisplay.displayLink,
                        Gamma.CreateGammaRamp(currDisplay.rGamma, currDisplay.gGamma, currDisplay.bGamma, currDisplay.rContrast,
                        currDisplay.gContrast, currDisplay.bContrast, currDisplay.rBright, currDisplay.gBright, currDisplay.bBright));
                    goto EndColors;
                }

                if (blueColor)
                {
                    currDisplay.bBright = (float)trackBarBrightness.Value / 100f;
                    Gamma.SetGammaRamp(currDisplay.displayLink,
                        Gamma.CreateGammaRamp(currDisplay.rGamma, currDisplay.gGamma, currDisplay.bGamma, currDisplay.rContrast,
                        currDisplay.gContrast, currDisplay.bContrast, currDisplay.rBright, currDisplay.gBright, currDisplay.bBright));
                }

            EndColors:
                return;
            }
        }

        private void trackBarSaturation_ValueChanged(object sender, EventArgs e)
        {
            if (disableChangeFunc || currDisplay == null || !currDisplay.saturationSupported) return;
            int saturation = trackBarSaturation.Value;
            currDisplay.saturation = saturation;
            textBoxSaturation.Text = saturation.ToString();
            comboBoxPresets.Text = string.Empty;
            Saturation.Apply(currDisplay, saturation);
        }

        private void trackBarMonitorBrightness_ValueChanged(object sender, EventArgs e)
        {
            if (!checkBoxMonitorEnabled.Checked) return;
            if (!disableChangeFunc)
            {
                comboBoxPresets.Text = string.Empty;
                textBoxMonitorBrightness.Text = trackBarMonitorBrightness.Value.ToString();

                currDisplay.monitorBrightness = trackBarMonitorBrightness.Value;

                if (currDisplay.isExternal)
                {
                    ExternalMonitor.SetBrightness(currDisplay.PhysicalHandle, (uint)trackBarMonitorBrightness.Value);
                }
                else
                {
                    InternalMonitor.SetBrightness((byte)trackBarMonitorBrightness.Value);
                }
            }
        }

        private void trackBarMonitorContrast_ValueChanged(object sender, EventArgs e)
        {
            if (!checkBoxMonitorEnabled.Checked) return;
            if (!disableChangeFunc)
            {
                comboBoxPresets.Text = string.Empty;
                textBoxMonitorContrast.Text = trackBarMonitorContrast.Value.ToString();

                currDisplay.monitorContrast = trackBarMonitorContrast.Value;

                ExternalMonitor.SetContrast(currDisplay.PhysicalHandle, (uint)trackBarMonitorContrast.Value);
            }
        }

        private void buttonAllColors_Click(object sender, EventArgs e)
        {
            disableChangeFunc = true;
            clearColors();
            allColors = true;

            textBoxGamma.Text = ((currDisplay.rGamma + currDisplay.gGamma + currDisplay.bGamma) / 3f).ToString("0.00");
            textBoxContrast.Text = ((currDisplay.rContrast + currDisplay.gContrast + currDisplay.bContrast) / 3f).ToString("0.00");
            textBoxBrightness.Text = ((currDisplay.rBright + currDisplay.gBright + currDisplay.bBright) / 3f).ToString("0.00");
            textBoxSaturation.Text = currDisplay.saturation.ToString();

            trackBarGamma.Value = (int)(((currDisplay.rGamma + currDisplay.gGamma + currDisplay.bGamma) / 3f) * 100f);
            trackBarContrast.Value = (int)(((currDisplay.rContrast + currDisplay.gContrast + currDisplay.bContrast) / 3f) * 100f);
            trackBarBrightness.Value = (int)(((currDisplay.rBright + currDisplay.gBright + currDisplay.bBright) / 3f) * 100f);
            trackBarSaturation.Minimum = currDisplay.saturationMin;
            trackBarSaturation.Maximum = Math.Max(currDisplay.saturationMin + 1, currDisplay.saturationMax);
            trackBarSaturation.SmallChange = Math.Max(1, currDisplay.saturationStep);
            trackBarSaturation.Value = Clamp(currDisplay.saturation, trackBarSaturation.Minimum, trackBarSaturation.Maximum);
            trackBarSaturation.Enabled = currDisplay.saturationSupported;
            textBoxSaturation.ReadOnly = !currDisplay.saturationSupported;
            labelSaturation.Text = currDisplay.saturationSupported
                ? (currDisplay.adapterVendor == WinApi.DisplayAdapterVendor.Nvidia ? (LanguageManager.Korean ? "디지털\n바이브런스" : "Digital\nVibrance") : (LanguageManager.Korean ? "채도" : "Saturation"))
                : (LanguageManager.Korean ? "채도 (미지원 GPU)" : "Saturation (unsupported)");

            buttonAllColors.Font = new Font(buttonAllColors.Font.Name, buttonAllColors.Font.Size, FontStyle.Bold);
            disableChangeFunc = false;
        }

        private void buttonRed_Click(object sender, EventArgs e)
        {
            disableChangeFunc = true;
            clearColors();
            redColor = true;

            textBoxGamma.Text = currDisplay.rGamma.ToString("0.00");
            textBoxContrast.Text = currDisplay.rContrast.ToString("0.00");
            textBoxBrightness.Text = currDisplay.rBright.ToString("0.00");

            trackBarGamma.Value = (int)(currDisplay.rGamma * 100f);
            trackBarContrast.Value = (int)(currDisplay.rContrast * 100f);
            trackBarBrightness.Value = (int)(currDisplay.rBright * 100f);

            buttonRed.Font = new Font(buttonRed.Font.Name, buttonRed.Font.Size, FontStyle.Bold);
            disableChangeFunc = false;
        }

        private void buttonGreen_Click(object sender, EventArgs e)
        {
            disableChangeFunc = true;
            clearColors();
            greenColor = true;

            textBoxGamma.Text = currDisplay.gGamma.ToString("0.00");
            textBoxContrast.Text = currDisplay.gContrast.ToString("0.00");
            textBoxBrightness.Text = currDisplay.gBright.ToString("0.00");

            trackBarGamma.Value = (int)(currDisplay.gGamma * 100f);
            trackBarContrast.Value = (int)(currDisplay.gContrast * 100f);
            trackBarBrightness.Value = (int)(currDisplay.gBright * 100f);

            buttonGreen.Font = new Font(buttonGreen.Font.Name, buttonGreen.Font.Size, FontStyle.Bold);
            disableChangeFunc = false;
        }

        private void buttonBlue_Click(object sender, EventArgs e)
        {
            disableChangeFunc = true;
            clearColors();
            blueColor = true;

            textBoxGamma.Text = currDisplay.bGamma.ToString("0.00");
            textBoxContrast.Text = currDisplay.bContrast.ToString("0.00");
            textBoxBrightness.Text = currDisplay.bBright.ToString("0.00");

            trackBarGamma.Value = (int)(currDisplay.bGamma * 100f);
            trackBarContrast.Value = (int)(currDisplay.bContrast * 100f);
            trackBarBrightness.Value = (int)(currDisplay.bBright * 100f);

            buttonBlue.Font = new Font(buttonBlue.Font.Name, buttonBlue.Font.Size, FontStyle.Bold);
            disableChangeFunc = false;
        }

        private void checkBoxExContrast_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxExContrast.Checked)
            {
                trackBarContrast.Maximum = 10000;
            } else
            {
                trackBarContrast.Maximum = 300;
            }
        }

        private void InitializeCurrentPresetState()
        {
            currentPresetByMonitor.Clear();
            string prefix = LanguageManager.Korean ? "기본값 - " : "Default - ";
            foreach (Display.DisplayInfo display in displays)
            {
                if (display == null || string.IsNullOrEmpty(display.displayName)) continue;
                string defaultPreset = prefix + display.displayName;
                if (!string.IsNullOrEmpty(iniFile.Read("monitor", defaultPreset)))
                    currentPresetByMonitor[display.displayName] = defaultPreset;
            }
        }

        private string GetCurrentPresetForMonitor(string monitorName)
        {
            if (string.IsNullOrEmpty(monitorName)) return null;

            string preset;
            if (currentPresetByMonitor.TryGetValue(monitorName, out preset) && !string.IsNullOrEmpty(preset))
                return preset;

            string prefix = LanguageManager.Korean ? "기본값 - " : "Default - ";
            string defaultPreset = prefix + monitorName;
            if (!string.IsNullOrEmpty(iniFile.Read("monitor", defaultPreset)))
            {
                currentPresetByMonitor[monitorName] = defaultPreset;
                return defaultPreset;
            }
            return null;
        }

        private void EnsureDefaultProfile()
        {
            // Keep one automatic Default profile for every detected monitor.
            // The profile name contains the monitor name because INI section names
            // must be unique, while the main window filters profiles by monitor.
            // Existing profiles are never overwritten.
            string prefix = LanguageManager.Korean ? "기본값 - " : "Default - ";

            foreach (Display.DisplayInfo display in displays)
            {
                if (display == null || string.IsNullOrEmpty(display.displayName))
                    continue;

                string defaultName = prefix + display.displayName;
                if (!string.IsNullOrEmpty(iniFile.Read("monitor", defaultName)))
                    continue;

                iniFile.Write("monitor", display.displayName, defaultName);
                iniFile.Write("rGamma", display.rGamma.ToString(customCulture), defaultName);
                iniFile.Write("gGamma", display.gGamma.ToString(customCulture), defaultName);
                iniFile.Write("bGamma", display.bGamma.ToString(customCulture), defaultName);
                iniFile.Write("rContrast", display.rContrast.ToString(customCulture), defaultName);
                iniFile.Write("gContrast", display.gContrast.ToString(customCulture), defaultName);
                iniFile.Write("bContrast", display.bContrast.ToString(customCulture), defaultName);
                iniFile.Write("rBright", display.rBright.ToString(customCulture), defaultName);
                iniFile.Write("gBright", display.gBright.ToString(customCulture), defaultName);
                iniFile.Write("bBright", display.bBright.ToString(customCulture), defaultName);
                iniFile.Write("saturation", display.saturation.ToString(), defaultName);
                iniFile.Write("monitorBrightness", display.monitorBrightness.ToString(customCulture), defaultName);
                iniFile.Write("monitorContrast", display.monitorContrast.ToString(customCulture), defaultName);
            }
        }

        private void buttonSave_Click(object sender, EventArgs e)
        {
            string tmp = comboBoxPresets.Text;
            iniFile.Write("monitor", currDisplay.displayName, currDisplay.displayName+": "+ comboBoxPresets.Text);
            iniFile.Write("rGamma", currDisplay.rGamma.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("gGamma", currDisplay.gGamma.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("bGamma", currDisplay.bGamma.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("rContrast", currDisplay.rContrast.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("gContrast", currDisplay.gContrast.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("bContrast", currDisplay.bContrast.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("rBright", currDisplay.rBright.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("gBright", currDisplay.gBright.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("bBright", currDisplay.bBright.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("saturation", currDisplay.saturation.ToString(), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("monitorBrightness", currDisplay.monitorBrightness.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);
            iniFile.Write("monitorContrast", currDisplay.monitorContrast.ToString(customCulture), currDisplay.displayName + ": " + comboBoxPresets.Text);

            initPresets(currDisplay.displayName + ": " + tmp);
            initTrayMenu();
        }

        private void buttonDelete_Click(object sender, EventArgs e)
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

        private void buttonReset_Click(object sender, EventArgs e)
        {
            // Reset GPU gamma/brightness/contrast to their neutral values.
            // Keep the physical monitor controls at a sane neutral/default level
            // instead of forcing monitor brightness to 100.
            const int defaultMonitorBrightness = 50;
            const int defaultMonitorContrast = 50;

            // Reset means "start from the normal profile-selection state" as well as
            // resetting the display values. Clear any hotkey override/toggle state so
            // the next Game Auto check can select the mapped profile again.
            manualOverrideActive = false;
            manualOverridePreset = null;
            manualOverrideProcess = null;
            manualToggleActive = false;
            manualTogglePreset = null;
            manualToggleReturnPreset = null;
            manualTogglePresetByMonitor.Clear();
            manualToggleReturnPresetByMonitor.Clear();
            activeAutoPreset = null;
            autoGameFocused = false;

            comboBoxPresets.Text = string.Empty;

            buttonAllColors.PerformClick();

            trackBarGamma.Value = 100;
            trackBarContrast.Value = 100;
            trackBarBrightness.Value = 0;
            if (currDisplay.saturationSupported)
                trackBarSaturation.Value = Clamp(currDisplay.saturationDefault, trackBarSaturation.Minimum, trackBarSaturation.Maximum);

            currDisplay.rGamma = 1;
            currDisplay.gGamma = 1;
            currDisplay.bGamma = 1;
            currDisplay.rContrast = 1;
            currDisplay.gContrast = 1;
            currDisplay.bContrast = 1;
            currDisplay.rBright = 0;
            currDisplay.gBright = 0;
            currDisplay.bBright = 0;
            if (currDisplay.saturationSupported)
            {
                currDisplay.saturation = currDisplay.saturationDefault;
                trackBarSaturation.Value = Clamp(currDisplay.saturationDefault, trackBarSaturation.Minimum, trackBarSaturation.Maximum);
                Saturation.Apply(currDisplay, currDisplay.saturation);
            }

            if (checkBoxMonitorEnabled.Checked)
            {
                currDisplay.monitorBrightness = defaultMonitorBrightness;
                trackBarMonitorBrightness.Value = defaultMonitorBrightness;
            }

            if (checkBoxMonitorEnabled.Checked && currDisplay.isExternal)
            {
                currDisplay.monitorContrast = defaultMonitorContrast;
                trackBarMonitorContrast.Value = defaultMonitorContrast;
            }

            Gamma.SetGammaRamp(displays[numDisplay].displayLink, Gamma.CreateGammaRamp(1, 1, 1, 1, 1, 1, 0, 0, 0));

            initPresets();
            initTrayMenu();
        }

        private void buttonHide_Click(object sender, EventArgs e)
        {
            Hide();
        }

        private void comboBoxMonitors_SelectedIndexChanged(object sender, EventArgs e)
        {
            string num = comboBoxMonitors.SelectedItem.ToString();

            num = num.Substring(0, num.IndexOf(")"));
            numDisplay = Int32.Parse(num)-1;

            currDisplay = displays[numDisplay];
            fillInfo(currDisplay);
            Saturation.Prepare(currDisplay);
            Saturation.Apply(currDisplay, currDisplay.saturation);
            
            initPresets();
        }

        private void checkBoxTopMost_CheckedChanged(object sender, EventArgs e)
        {
            bool enabled = checkBoxTopMost.Checked;
            TopMost = enabled;
            if (iniFile != null)
                iniFile.Write("TopMost", enabled ? "True" : "False", "Settings");
        }

        private void checkBoxMonitorEnabled_CheckedChanged(object sender, EventArgs e)
        {
            bool enabled = checkBoxMonitorEnabled.Checked;
            trackBarMonitorBrightness.Enabled = enabled;
            trackBarMonitorContrast.Enabled = enabled && currDisplay.isExternal;
            textBoxMonitorBrightness.Enabled = enabled;
            textBoxMonitorContrast.Enabled = enabled && currDisplay.isExternal;
            buttonReset.Enabled = true;
        }

        private void buttonForward_Click(object sender, EventArgs e)
        {
            if (numDisplay + 1 <= displays.Count-1)
            {
                comboBoxMonitors.SelectedIndex = numDisplay + 1;
            } else
            {
                comboBoxMonitors.SelectedIndex = 0;
            }
        }

        private void comboBoxPresets_SelectedIndexChanged(object sender, EventArgs e)
        {
            // Main-window profile selection is intentionally NOT part of Game Auto priority.
            // It changes the profile immediately on the main screen, but while a mapped game
            // is active the priority is strictly: Hotkey > Game Auto.
            if (!disableChangeFunc && !applyingPreset && comboBoxPresets.SelectedIndex >= 0)
            {
                string preset = comboBoxPresets.SelectedItem as string;
                if (!string.IsNullOrEmpty(preset))
                    ApplyPreset(preset, false);
            }
        }

        private void ApplyPreset(string preset)
        {
            ApplyPreset(preset, true, true);
        }

        private void ApplyPreset(string preset, bool switchMonitor)
        {
            ApplyPreset(preset, switchMonitor, true);
        }

        private void ApplyPreset(string preset, bool switchMonitor, bool applyMonitorSettings)
        {
            if (string.IsNullOrEmpty(preset)) return;
            applyingPreset = true;
            try
            {
                string monitorName = iniFile.Read("monitor", preset);
                if (string.IsNullOrEmpty(monitorName)) return;

                int displayIndex = -1;
                for (int i = 0; i < displays.Count; i++)
                {
                    if (displays[i].displayName.Equals(monitorName, StringComparison.Ordinal))
                    {
                        displayIndex = i;
                        break;
                    }
                }
                if (displayIndex < 0) return;

                currentPresetByMonitor[monitorName] = preset;

                if (switchMonitor)
                {
                    disableChangeFunc = true;
                    numDisplay = displayIndex;
                    currDisplay = displays[displayIndex];
                    comboBoxMonitors.SelectedIndex = displayIndex;
                    disableChangeFunc = false;
                }
                else
                {
                    currDisplay = displays[displayIndex];
                    numDisplay = displayIndex;
                }

                currDisplay.rGamma = float.Parse(iniFile.Read("rGamma", preset), customCulture);
                currDisplay.gGamma = float.Parse(iniFile.Read("gGamma", preset), customCulture);
                currDisplay.bGamma = float.Parse(iniFile.Read("bGamma", preset), customCulture);
                currDisplay.rContrast = float.Parse(iniFile.Read("rContrast", preset), customCulture);
                currDisplay.gContrast = float.Parse(iniFile.Read("gContrast", preset), customCulture);
                currDisplay.bContrast = float.Parse(iniFile.Read("bContrast", preset), customCulture);
                currDisplay.rBright = float.Parse(iniFile.Read("rBright", preset), customCulture);
                currDisplay.gBright = float.Parse(iniFile.Read("gBright", preset), customCulture);
                currDisplay.bBright = float.Parse(iniFile.Read("bBright", preset), customCulture);
                string saturationText = iniFile.Read("saturation", preset);
                int parsedSaturation;
                if (!int.TryParse(saturationText, out parsedSaturation)) parsedSaturation = currDisplay.saturationDefault;
                currDisplay.saturation = Clamp(parsedSaturation, currDisplay.saturationMin, currDisplay.saturationMax);
                currDisplay.monitorBrightness = int.Parse(iniFile.Read("monitorBrightness", preset));
                currDisplay.monitorContrast = int.Parse(iniFile.Read("monitorContrast", preset));
                fillInfo(currDisplay);
                clearColors();
                buttonAllColors.PerformClick();

                disableChangeFunc = true;
                int presetIndex = comboBoxPresets.Items.IndexOf(preset);
                if (presetIndex >= 0) comboBoxPresets.SelectedIndex = presetIndex;
                else comboBoxPresets.Text = preset;
                disableChangeFunc = false;

                Gamma.SetGammaRamp(currDisplay.displayLink,
                    Gamma.CreateGammaRamp(currDisplay.rGamma, currDisplay.gGamma, currDisplay.bGamma,
                    currDisplay.rContrast, currDisplay.gContrast, currDisplay.bContrast,
                    currDisplay.rBright, currDisplay.gBright, currDisplay.bBright));
                Saturation.Prepare(currDisplay);
            Saturation.Apply(currDisplay, currDisplay.saturation);

                if (applyMonitorSettings && checkBoxMonitorEnabled.Checked)
                {
                    disableChangeFunc = false;
                    if (currDisplay.isExternal)
                    {
                        trackBarMonitorBrightness.Value = Math.Max(trackBarMonitorBrightness.Minimum, Math.Min(trackBarMonitorBrightness.Maximum, currDisplay.monitorBrightness));
                        trackBarMonitorContrast.Value = Math.Max(trackBarMonitorContrast.Minimum, Math.Min(trackBarMonitorContrast.Maximum, currDisplay.monitorContrast));
                    }
                    else
                    {
                        trackBarMonitorBrightness.Value = Math.Max(trackBarMonitorBrightness.Minimum, Math.Min(trackBarMonitorBrightness.Maximum, currDisplay.monitorBrightness));
                    }
                }

                int selectedIndex = comboBoxPresets.Items.IndexOf(preset);
                if (selectedIndex >= 0)
                {
                    disableChangeFunc = true;
                    comboBoxPresets.SelectedIndex = selectedIndex;
                    disableChangeFunc = false;
                }
            }
            finally
            {
                applyingPreset = false;
                disableChangeFunc = false;
            }
        }

        private void SetupGameAutoHook()
        {
            // Listen for Windows foreground-window changes instead of polling every 350ms.
            // This only observes which top-level window is active; it does not hook into
            // Tarkov itself, inject a DLL, read game memory, or capture the screen.
            gameAutoWinEventDelegate = GameAutoWinEventProc;
            gameAutoWinEventHook = WinApi.SetWinEventHook(
                WinApi.EVENT_SYSTEM_FOREGROUND,
                WinApi.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                gameAutoWinEventDelegate,
                0,
                0,
                WinApi.WINEVENT_OUTOFCONTEXT);

            // Evaluate once so starting the manager while a mapped game is already focused
            // still applies the correct profile immediately.
            EvaluateGameAutoState();
        }

        private void GameAutoWinEventProc(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint idEventThread,
            uint msEventTime)
        {
            if (eventType != WinApi.EVENT_SYSTEM_FOREGROUND) return;
            if (idObject != WinApi.OBJID_WINDOW || idChild != 0) return;
            EvaluateGameAutoState();
        }

        private string GetForegroundProcessName()
        {
            IntPtr hwnd = WinApi.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            uint pid;
            WinApi.GetWindowThreadProcessId(hwnd, out pid);
            try
            {
                Process p = Process.GetProcessById((int)pid);
                return p.ProcessName + ".exe";
            }
            catch { return null; }
        }

        private bool TryGetAutoMapping(string foregroundExe, out string profile, out bool applyMonitor)
        {
            profile = null;
            applyMonitor = false;
            if (string.IsNullOrEmpty(foregroundExe)) return false;

            string[] sections = iniFile.GetSections();
            if (sections == null) return false;

            foreach (string section in sections)
            {
                if (!section.StartsWith("AutoGame_", StringComparison.OrdinalIgnoreCase)) continue;
                string enabled = iniFile.Read("Enabled", section);
                string exe = iniFile.Read("Process", section);
                string p = iniFile.Read("Profile", section);
                if ((enabled == "1" || string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrEmpty(exe) && !string.IsNullOrEmpty(p) &&
                    string.Equals(Path.GetFileName(exe), Path.GetFileName(foregroundExe), StringComparison.OrdinalIgnoreCase))
                {
                    profile = p;
                    string monitor = iniFile.Read("ApplyMonitor", section);
                    applyMonitor = monitor == "1" || string.Equals(monitor, "true", StringComparison.OrdinalIgnoreCase);
                    return true;
                }
            }

            // Backward compatibility: old mappings never changed physical monitor controls automatically.
            foreach (string p in sections)
            {
                if (p.StartsWith("AutoGame_", StringComparison.OrdinalIgnoreCase)) continue;
                if (p.Equals("Hotkeys", StringComparison.OrdinalIgnoreCase)) continue;
                string enabled = iniFile.Read("AutoEnabled", p);
                string exe = iniFile.Read("AutoProcess", p);
                if ((enabled == "1" || string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase)) &&
                    !string.IsNullOrEmpty(exe) &&
                    string.Equals(Path.GetFileName(exe), Path.GetFileName(foregroundExe), StringComparison.OrdinalIgnoreCase))
                {
                    profile = p;
                    applyMonitor = false;
                    return true;
                }
            }
            return false;
        }

        private bool IsProcessRunning(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return false;
            try
            {
                string name = Path.GetFileNameWithoutExtension(exeName);
                return Process.GetProcessesByName(name).Length > 0;
            }
            catch { return false; }
        }

        private void EvaluateGameAutoState()
        {
            if (IsDisposed || Disposing || applyingPreset || displays == null || displays.Count == 0) return;

            string foreground = GetForegroundProcessName();
            string matchedProfile;
            bool applyMonitor;
            bool mapped = TryGetAutoMapping(foreground, out matchedProfile, out applyMonitor);

            // Priority is strictly: Hotkey > Game Auto.
            // Main-window profile selection is never used to override Game Auto.
            // Hotkey selection survives Alt+Tab and is reapplied while its game session is alive.
            if (manualOverrideActive && !string.IsNullOrEmpty(manualOverrideProcess) &&
                !IsProcessRunning(manualOverrideProcess))
            {
                manualOverrideActive = false;
                manualOverridePreset = null;
                manualOverrideProcess = null;
                manualToggleActive = false;
                manualTogglePreset = null;
                manualToggleReturnPreset = null;
                manualTogglePresetByMonitor.Clear();
                manualToggleReturnPresetByMonitor.Clear();
            }

            if (mapped)
            {
                string desiredProfile = matchedProfile;
                bool desiredMonitor = applyMonitor;

                // 1) Hotkey override
                if (manualOverrideActive && !string.IsNullOrEmpty(manualOverridePreset))
                {
                    desiredProfile = manualOverridePreset;
                    desiredMonitor = true;
                    if (string.IsNullOrEmpty(manualOverrideProcess))
                        manualOverrideProcess = foreground;
                }

                if (!autoGameFocused || !string.Equals(activeAutoPreset, desiredProfile, StringComparison.Ordinal))
                {
                    if (!autoGameFocused)
                    {
                        // Restore the exact pre-game state before applying a new game profile.
                        StartupStateManager.RestoreSaved(displays);
                    }
                    activeAutoPreset = desiredProfile;
                    ApplyPreset(desiredProfile, true, desiredMonitor);
                }
                autoGameFocused = true;
            }
            else if (autoGameFocused)
            {
                autoGameFocused = false;
                activeAutoPreset = null;
                StartupStateManager.RestoreSaved(displays);
                fillInfo(currDisplay);
            }
            else if (!mapped && manualOverrideActive && !string.IsNullOrEmpty(manualOverrideProcess) && !IsProcessRunning(manualOverrideProcess))
            {
                // A hotkey pressed before entering the game keeps its priority until the
                // mapped game actually starts. Once that game process ends, clear the override.
                manualOverrideActive = false;
                manualOverridePreset = null;
                manualOverrideProcess = null;
                manualToggleActive = false;
                manualTogglePreset = null;
                manualToggleReturnPreset = null;
                manualTogglePresetByMonitor.Clear();
                manualToggleReturnPresetByMonitor.Clear();
            }
        }

        private void RefreshGlobalHotkeys()
        {
            manualToggleActive = false;
            manualTogglePreset = null;
            manualToggleReturnPreset = null;
            manualTogglePresetByMonitor.Clear();
            manualToggleReturnPresetByMonitor.Clear();

            foreach (GlobalHotkey hotkey in globalHotkeys.Values)
                hotkey.Dispose();
            globalHotkeys.Clear();
            globalHotkeyPresets.Clear();

            string[] presets = iniFile.GetSections();
            if (presets == null) return;

            foreach (string preset in presets)
            {
                string hotkeyText = iniFile.Read("Hotkeys", preset);
                Keys key;
                GlobalHotkey.Modifiers modifiers;
                if (!TryParseHotkey(hotkeyText, out key, out modifiers)) continue;

                int id = nextHotkeyId++;
                GlobalHotkey hotkey = new GlobalHotkey(this.Handle, id, key, modifiers);
                string capturedPreset = preset;
                bool isToggle = string.Equals(iniFile.Read("HotkeyMode", capturedPreset), "Toggle", StringComparison.OrdinalIgnoreCase);
                hotkey.Pressed += delegate
                {
                    string visiblePreset = capturedPreset;
                    string fg = GetForegroundProcessName();
                    string mapped;
                    bool mappedMonitor;
                    bool hasGameMapping = TryGetAutoMapping(fg, out mapped, out mappedMonitor);

                    string capturedMonitor = iniFile.Read("monitor", capturedPreset);
                    if (string.IsNullOrEmpty(capturedMonitor)) return;

                    string toggleKey = capturedMonitor;
                    bool toggleIsActive = isToggle && manualTogglePresetByMonitor.ContainsKey(toggleKey) &&
                        string.Equals(manualTogglePresetByMonitor[toggleKey], capturedPreset, StringComparison.Ordinal);

                    if (toggleIsActive)
                    {
                        // Toggle off: restore the profile that was active on THIS monitor
                        // immediately before this toggle was enabled. Never use the currently
                        // selected profile of another monitor as the return target.
                        string returnPreset = null;
                        manualToggleReturnPresetByMonitor.TryGetValue(toggleKey, out returnPreset);

                        manualTogglePresetByMonitor.Remove(toggleKey);
                        manualToggleReturnPresetByMonitor.Remove(toggleKey);

                        manualToggleActive = manualTogglePresetByMonitor.Count > 0;
                        manualTogglePreset = manualToggleActive ? capturedPreset : null;
                        manualToggleReturnPreset = null;
                        manualOverrideActive = false;
                        manualOverridePreset = null;
                        manualOverrideProcess = null;

                        if (!string.IsNullOrEmpty(returnPreset))
                        {
                            ApplyPreset(returnPreset, true, true);
                            visiblePreset = returnPreset;
                        }
                    }
                    else
                    {
                        if (isToggle)
                        {
                            // Capture the profile currently assigned to the SAME monitor.
                            // The main-window combo box belongs to whichever monitor is visible,
                            // so it must never be used as the return state for another monitor.
                            string previousPreset = GetCurrentPresetForMonitor(capturedMonitor);

                            if (string.Equals(previousPreset, capturedPreset, StringComparison.Ordinal))
                                previousPreset = null;

                            manualTogglePresetByMonitor[toggleKey] = capturedPreset;
                            manualToggleReturnPresetByMonitor[toggleKey] = previousPreset;
                            manualToggleActive = true;
                            manualTogglePreset = capturedPreset;
                            manualToggleReturnPreset = previousPreset;
                        }
                        else
                        {
                            // A normal Apply hotkey cancels the toggle state for its monitor only.
                            if (!string.IsNullOrEmpty(capturedMonitor))
                            {
                                manualTogglePresetByMonitor.Remove(toggleKey);
                                manualToggleReturnPresetByMonitor.Remove(toggleKey);
                            }
                            manualToggleActive = manualTogglePresetByMonitor.Count > 0;
                            manualTogglePreset = null;
                            manualToggleReturnPreset = null;
                        }

                        manualOverrideActive = true;
                        manualOverridePreset = capturedPreset;
                        manualOverrideProcess = hasGameMapping ? fg : null;

                        ApplyPreset(capturedPreset, true, true);
                    }

                    // ApplyPreset normally updates the combo box, but explicitly refresh
                    // the visible selection as well so the main window always reflects
                    // the profile selected by the hotkey.
                    int visibleIndex = comboBoxPresets.Items.IndexOf(visiblePreset);
                    if (visibleIndex >= 0)
                    {
                        disableChangeFunc = true;
                        comboBoxPresets.SelectedIndex = visibleIndex;
                        disableChangeFunc = false;
                    }
                };

                if (hotkey.Register())
                {
                    globalHotkeys.Add(id, hotkey);
                    globalHotkeyPresets.Add(id, preset);
                }
                else
                {
                    hotkey.Dispose();
                    // Do not interrupt startup. The user can see the setting again
                    // in the Hotkeys window and choose another combination.
                }
            }
        }

        private static bool TryParseHotkey(string value, out Keys key, out GlobalHotkey.Modifiers modifiers)
        {
            key = Keys.None;
            modifiers = GlobalHotkey.Modifiers.None;
            if (string.IsNullOrWhiteSpace(value)) return false;

            string[] parts = value.Split('+');
            string keyText = parts[parts.Length - 1].Trim();
            if (!Enum.TryParse(keyText, true, out key) || key == Keys.None) return false;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl":
                    case "control": modifiers |= GlobalHotkey.Modifiers.Control; break;
                    case "alt": modifiers |= GlobalHotkey.Modifiers.Alt; break;
                    case "shift": modifiers |= GlobalHotkey.Modifiers.Shift; break;
                    case "win":
                    case "windows": modifiers |= GlobalHotkey.Modifiers.Win; break;
                    default: return false;
                }
            }
            return true;
        }

        private void buttonBackup_Click(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = LanguageManager.Korean ? "프로필 백업 저장" : "Save Profile Backup";
                dialog.Filter = "Gamma Manager Backup (*.ini)|*.ini|All files (*.*)|*.*";
                dialog.FileName = "Tarkov-Gamma-Manager-v1.4-Backup.ini";
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

                    MessageBox.Show(
                        LanguageManager.Korean ? "백업을 불러왔습니다. 프로그램을 다시 시작해 적용합니다." :
                        "The backup was restored. The application will restart to apply it.",
                        LanguageManager.Korean ? "복원 완료" : "Restore Complete",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Application.Restart();
                }
                catch (Exception ex)
                {
                    MessageBox.Show((LanguageManager.Korean ? "복원에 실패했습니다.\r\n\r\n" : "Restore failed.\r\n\r\n") + ex.Message,
                        LanguageManager.Korean ? "복원 오류" : "Restore Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void buttonGameAuto_Click(object sender, EventArgs e)
        {
            string[] presets = iniFile.GetSections();
            using (GameAutoSettingsForm form = new GameAutoSettingsForm(iniFile, presets))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    activeAutoPreset = null;
                    manualOverrideActive = false;
                    manualOverridePreset = null;
                    manualOverrideProcess = null;
                    manualToggleActive = false;
                    manualTogglePreset = null;
                    manualToggleReturnPreset = null;
                    manualTogglePresetByMonitor.Clear();
                    manualToggleReturnPresetByMonitor.Clear();
                    autoGameFocused = false;
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
            using (HotkeySettingsForm form = new HotkeySettingsForm(iniFile, presets))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                    RefreshGlobalHotkeys();
            }
        }


        private void buttonTheme_Click(object sender, EventArgs e)
        {
            bool dark = !ThemeManager.IsDark;
            ThemeManager.SetTheme(dark);
            iniFile.Write("Theme", dark ? "Dark" : "Light", "Settings");
            ApplyCurrentTheme();
        }

        private void ApplyLanguage()
        {
            bool ko = LanguageManager.Korean;
            Text = "Tarkov Gamma Manager v1.4.4 " + (LanguageManager.Korean ? "Korean" : "English");
            notifyIcon.Text = "Tarkov Gamma Manager v1.4.4 " + (LanguageManager.Korean ? "Korean" : "English");

            buttonRed.Text = ko ? "빨강" : "Red";
            buttonGreen.Text = ko ? "초록" : "Green";
            buttonBlue.Text = ko ? "파랑" : "Blue";
            buttonAllColors.Text = ko ? "RGB\n연동" : "RGB\nLink";
            buttonReset.Text = ko ? "초기화" : "Reset";
            buttonSave.Text = ko ? "저장" : "Save";
            buttonDelete.Text = ko ? "프로필" : "Profiles";
            buttonHotkeys.Text = ko ? "핫키" : "Hotkeys";
            if (buttonGameAuto != null) buttonGameAuto.Text = ko ? "게임 자동" : "Game Auto";
            if (buttonBackup != null) buttonBackup.Text = ko ? "백업" : "Backup";
            if (buttonRestore != null) buttonRestore.Text = ko ? "불러오기" : "Restore";
            buttonHide.Text = ko ? "숨기기" : "Hide";
            buttonForward.Text = ko ? "다음" : "Next";
            labelGamma.Text = ko ? "감마" : "Gamma";
            labelBrightness.Text = ko ? "밝기" : "Brightness";
            labelContrast.Text = ko ? "대비" : "Contrast";
            labelSaturation.Text = currDisplay != null && currDisplay.saturationSupported
                ? (currDisplay.adapterVendor == WinApi.DisplayAdapterVendor.Nvidia ? (ko ? "디지털\n바이브런스" : "Digital\nVibrance") : (ko ? "채도" : "Saturation"))
                : (ko ? "채도 (미지원 GPU)" : "Saturation (unsupported)");
            labelMonitorBrightnessUp.Text = ko ? "밝기" : "Brightness";
            labelMonitorBrightnessDown.Text = "";
            labelMonitorContrastUp.Text = ko ? "대비" : "Contrast";
            labelMonitorContrastDown.Text = "";
            checkBoxExContrast.Text = ko ? "RGB\n연동" : "RGB\nLink";
            sectionDisplay.Text = ko ? "디스플레이 & 프로필" : "DISPLAY & PROFILE";
            sectionGpu.Text = ko ? "GPU 색상" : "GPU COLOR";
            sectionMonitor.Text = ko ? "모니터 설정" : "MONITOR SETTINGS";
            checkBoxMonitorEnabled.Text = ko ? "모니터 조절 사용" : "Enable Monitor";
            checkBoxTopMost.Text = ko ? "창 맨위로" : "Always on Top";
            appTitle.Text = "TARKOV GAMMA";
            appSubtitle.Text = ko ? "디스플레이 프로필 및 색상 조절" : "Display profile & color control";
            imageCaption.Text = "ESCAPE FROM TARKOV";
            imageSubCaption.Text = ko ? "디스플레이 프로필 관리자" : "Display profile manager";
        }

        private void ApplyCurrentTheme()
        {
            ThemeManager.Apply(this);
            if (buttonTheme != null)
                buttonTheme.Text = LanguageManager.Korean
                    ? (ThemeManager.IsDark ? "밝은 테마" : "어두운 테마")
                    : (ThemeManager.IsDark ? "Light Theme" : "Dark Theme");

            if (appTitle != null)
                appTitle.ForeColor = ThemeManager.IsDark ? Color.White : Color.FromArgb(25, 28, 34);
            if (appSubtitle != null)
                appSubtitle.ForeColor = ThemeManager.IsDark ? ThemeManager.DarkMuted : ThemeManager.LightMuted;
            if (sectionDisplay != null)
                sectionDisplay.ForeColor = ThemeManager.IsDark ? Color.FromArgb(120, 180, 255) : Color.FromArgb(35, 95, 160);
            if (sectionGpu != null)
                sectionGpu.ForeColor = ThemeManager.IsDark ? Color.FromArgb(120, 180, 255) : Color.FromArgb(35, 95, 160);
            if (sectionMonitor != null)
                sectionMonitor.ForeColor = ThemeManager.IsDark ? Color.FromArgb(120, 180, 255) : Color.FromArgb(35, 95, 160);
            if (imageCaption != null)
                imageCaption.ForeColor = ThemeManager.IsDark ? Color.White : Color.FromArgb(25, 28, 34);
            if (imageSubCaption != null)
                imageSubCaption.ForeColor = ThemeManager.IsDark ? ThemeManager.DarkMuted : ThemeManager.LightMuted;

            if (gpuCard != null)
                gpuCard.BackColor = ThemeManager.IsDark ? ThemeManager.DarkPanel : ThemeManager.LightPanel;
            if (monitorCard != null)
                monitorCard.BackColor = ThemeManager.IsDark ? ThemeManager.DarkPanel : ThemeManager.LightPanel;
            if (rightCard != null)
                rightCard.BackColor = ThemeManager.IsDark ? ThemeManager.DarkPanel : ThemeManager.LightPanel;
        }

        private void SetupModernLayout()
        {
            ClientSize = new Size(1040, 630);
            MinimumSize = ClientSize;
            MaximumSize = ClientSize;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Text = "Tarkov Gamma Manager v1.4.4 " + (LanguageManager.Korean ? "Korean" : "English");

            // Header
            appTitle = new Label();
            appTitle.Text = "TARKOV GAMMA";
            appTitle.Font = new Font("Segoe UI", 19f, FontStyle.Bold);
            appTitle.Location = new Point(22, 15);
            appTitle.Size = new Size(330, 36);

            appSubtitle = new Label();
            appSubtitle.Text = LanguageManager.Korean ? "디스플레이 프로필 및 색상 조절" : "Display profile & color control";
            appSubtitle.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            appSubtitle.Location = new Point(24, 51);
            appSubtitle.Size = new Size(420, 30);

            buttonTheme = new Button();
            buttonTheme.Size = new Size(110, 34);
            buttonTheme.Location = new Point(525, 18);
            buttonTheme.Click += new EventHandler(buttonTheme_Click);

            sectionDisplay = new Label();
            sectionDisplay.Text = "DISPLAY & PROFILE";
            sectionDisplay.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            sectionDisplay.Location = new Point(22, 82);
            sectionDisplay.Size = new Size(300, 28);

            sectionGpu = new Label();
            sectionGpu.Text = "GPU COLOR";
            sectionGpu.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            sectionGpu.Location = new Point(22, 172);
            sectionGpu.Size = new Size(220, 26);

            sectionMonitor = new Label();
            sectionMonitor.Text = "MONITOR";
            sectionMonitor.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            sectionMonitor.Location = new Point(22, 426);
            sectionMonitor.Size = new Size(120, 26);

            checkBoxMonitorEnabled = new CheckBox();
            checkBoxMonitorEnabled.AutoSize = true;
            checkBoxMonitorEnabled.Location = new Point(135, 422);
            checkBoxMonitorEnabled.Size = new Size(150, 22);
            checkBoxMonitorEnabled.Checked = false;
            checkBoxMonitorEnabled.CheckedChanged += new EventHandler(checkBoxMonitorEnabled_CheckedChanged);

            checkBoxTopMost = new CheckBox();
            checkBoxTopMost.AutoSize = true;
            checkBoxTopMost.Location = new Point(680, 555);
            checkBoxTopMost.Size = new Size(250, 22);
            checkBoxTopMost.Checked = true;
            checkBoxTopMost.CheckedChanged += new EventHandler(checkBoxTopMost_CheckedChanged);

            gpuCard = new Panel();
            gpuCard.Location = new Point(18, 190);
            gpuCard.Size = new Size(620, 230);
            gpuCard.BorderStyle = BorderStyle.FixedSingle;
            gpuCard.SendToBack();

            monitorCard = new Panel();
            monitorCard.Location = new Point(18, 457);
            monitorCard.Size = new Size(620, 112);
            monitorCard.BorderStyle = BorderStyle.FixedSingle;
            monitorCard.SendToBack();

            rightCard = new Panel();
            rightCard.Location = new Point(662, 18);
            rightCard.Size = new Size(360, 590);
            rightCard.BorderStyle = BorderStyle.FixedSingle;
            rightCard.SendToBack();

            imageCaption = new Label();
            imageCaption.Text = "ESCAPE FROM TARKOV";
            imageCaption.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            imageCaption.Location = new Point(680, 315);
            imageCaption.Size = new Size(320, 28);

            imageSubCaption = new Label();
            imageSubCaption.Text = "Tarkov Gamma manager • Display preset manager";
            imageSubCaption.Font = new Font("Segoe UI", 9f);
            imageSubCaption.Location = new Point(680, 345);
            imageSubCaption.Size = new Size(320, 24);

            // Move existing controls into a cleaner two-column layout.
            comboBoxMonitors.Location = new Point(115, 104);
            comboBoxMonitors.Size = new Size(250, 26);
            buttonForward.Location = new Point(375, 103);
            buttonForward.Size = new Size(90, 28);

            comboBoxPresets.Location = new Point(115, 137);
            comboBoxPresets.Size = new Size(250, 26);
            buttonSave.Location = new Point(375, 136);
            buttonSave.Size = new Size(78, 28);
            buttonDelete.Location = new Point(459, 136);
            buttonDelete.Size = new Size(82, 28);
            buttonHotkeys.Location = new Point(547, 136);
            buttonHotkeys.Size = new Size(82, 28);
            buttonGameAuto = new Button();
            buttonGameAuto.Location = new Point(471, 103);
            buttonGameAuto.Size = new Size(109, 28);
            buttonGameAuto.Click += new EventHandler(buttonGameAuto_Click);
            Controls.Add(buttonGameAuto);

            buttonBackup = new Button();
            buttonBackup.Location = new Point(680, 375);
            buttonBackup.Size = new Size(154, 32);
            buttonBackup.Click += new EventHandler(buttonBackup_Click);
            Controls.Add(buttonBackup);

            buttonRestore = new Button();
            buttonRestore.Location = new Point(844, 375);
            buttonRestore.Size = new Size(160, 32);
            buttonRestore.Click += new EventHandler(buttonRestore_Click);
            Controls.Add(buttonRestore);

            // GPU controls: one consistent row height and vertical center alignment.
            labelGamma.AutoSize = false;
            labelGamma.Location = new Point(35, 198);
            labelGamma.Size = new Size(80, 45);
            labelGamma.TextAlign = ContentAlignment.MiddleLeft;
            labelBrightness.AutoSize = false;
            labelBrightness.Location = new Point(35, 248);
            labelBrightness.Size = new Size(80, 45);
            labelBrightness.TextAlign = ContentAlignment.MiddleLeft;
            labelContrast.AutoSize = false;
            labelContrast.Location = new Point(35, 298);
            labelContrast.Size = new Size(80, 45);
            labelContrast.TextAlign = ContentAlignment.MiddleLeft;

            trackBarGamma.Location = new Point(125, 198);
            trackBarGamma.Size = new Size(315, 45);
            trackBarBrightness.Location = new Point(125, 248);
            trackBarBrightness.Size = new Size(315, 45);
            trackBarContrast.Location = new Point(125, 298);
            trackBarContrast.Size = new Size(315, 45);

            textBoxGamma.Location = new Point(450, 209);
            textBoxBrightness.Location = new Point(450, 259);
            textBoxContrast.Location = new Point(450, 309);
            textBoxGamma.Size = new Size(52, 23);
            textBoxBrightness.Size = new Size(52, 23);
            textBoxContrast.Size = new Size(52, 23);
            textBoxGamma.TextAlign = HorizontalAlignment.Center;
            textBoxBrightness.TextAlign = HorizontalAlignment.Center;
            textBoxContrast.TextAlign = HorizontalAlignment.Center;

            labelSaturation.AutoSize = false;
            labelSaturation.Location = new Point(35, 348);
            labelSaturation.Size = new Size(80, 45);
            labelSaturation.TextAlign = ContentAlignment.MiddleLeft;
            trackBarSaturation.Location = new Point(125, 348);
            trackBarSaturation.Size = new Size(315, 45);
            textBoxSaturation.Location = new Point(450, 359);
            textBoxSaturation.Size = new Size(52, 23);
            textBoxSaturation.TextAlign = HorizontalAlignment.Center;



            buttonAllColors.Location = new Point(520, 196);
            buttonAllColors.Size = new Size(102, 34);
            buttonRed.Location = new Point(520, 236);
            buttonRed.Size = new Size(48, 30);
            buttonGreen.Location = new Point(574, 236);
            buttonGreen.Size = new Size(48, 30);
            buttonBlue.Location = new Point(520, 271);
            buttonBlue.Size = new Size(48, 30);
            checkBoxExContrast.Location = new Point(570, 262);
            checkBoxExContrast.Size = new Size(68, 42);
            checkBoxExContrast.AutoSize = false;
            checkBoxExContrast.TextAlign = ContentAlignment.MiddleLeft;

            // Monitor controls: the section title already says "모니터 설정",
            // so each row only needs its own control name. Keep label, slider and
            // value box on the same vertical center line.
            labelMonitorBrightnessUp.AutoSize = false;
            labelMonitorBrightnessUp.Location = new Point(35, 457);
            labelMonitorBrightnessUp.Size = new Size(80, 45);
            labelMonitorBrightnessUp.TextAlign = ContentAlignment.MiddleLeft;
            labelMonitorBrightnessUp.BackColor = Color.Transparent;
            labelMonitorBrightnessUp.ForeColor = ThemeManager.IsDark ? ThemeManager.DarkText : ThemeManager.LightText;
            labelMonitorBrightnessDown.Visible = false;
            labelMonitorBrightnessUp.Text = LanguageManager.Korean ? "밝기" : "Brightness";

            trackBarMonitorBrightness.Location = new Point(125, 457);
            trackBarMonitorBrightness.Size = new Size(315, 45);
            textBoxMonitorBrightness.Location = new Point(450, 468);
            textBoxMonitorBrightness.Size = new Size(52, 23);
            textBoxMonitorBrightness.TextAlign = HorizontalAlignment.Center;

            labelMonitorContrastUp.AutoSize = false;
            labelMonitorContrastUp.Location = new Point(35, 507);
            labelMonitorContrastUp.Size = new Size(80, 45);
            labelMonitorContrastUp.TextAlign = ContentAlignment.MiddleLeft;
            labelMonitorContrastUp.BackColor = Color.Transparent;
            labelMonitorContrastUp.ForeColor = ThemeManager.IsDark ? ThemeManager.DarkText : ThemeManager.LightText;
            labelMonitorContrastDown.Visible = false;
            labelMonitorContrastUp.Text = LanguageManager.Korean ? "대비" : "Contrast";

            trackBarMonitorContrast.Location = new Point(125, 507);
            trackBarMonitorContrast.Size = new Size(315, 45);
            textBoxMonitorContrast.Location = new Point(450, 518);
            textBoxMonitorContrast.Size = new Size(52, 23);
            textBoxMonitorContrast.TextAlign = HorizontalAlignment.Center;

            buttonReset.Location = new Point(520, 466);
            buttonReset.Size = new Size(109, 30);
            buttonHide.Location = new Point(520, 503);
            buttonHide.Size = new Size(109, 30);

            pictureBox1.Location = new Point(680, 78);
            pictureBox1.Size = new Size(324, 182);
            pictureBox1.BorderStyle = BorderStyle.FixedSingle;
            pictureBox1.BackgroundImageLayout = ImageLayout.Stretch;
            pictureBox1.Parent = this;

            Controls.Add(appTitle);
            Controls.Add(appSubtitle);
            Controls.Add(buttonTheme);
            Controls.Add(sectionDisplay);
            Controls.Add(sectionGpu);
            Controls.Add(sectionMonitor);
            Controls.Add(checkBoxMonitorEnabled);
            Controls.Add(gpuCard);
            Controls.Add(monitorCard);
            Controls.Add(rightCard);
            Controls.Add(checkBoxTopMost);
            Controls.Add(imageCaption);
            Controls.Add(imageSubCaption);
            // Re-add cards first, then send them behind the interactive controls.
            gpuCard.SendToBack();
            monitorCard.SendToBack();
            rightCard.SendToBack();
            labelMonitorBrightnessUp.BringToFront();
            labelMonitorContrastUp.BringToFront();
            checkBoxMonitorEnabled.BringToFront();
            checkBoxTopMost.BringToFront();

            ApplyLanguage();
            ApplyCurrentTheme();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312)
            {
                foreach (GlobalHotkey hotkey in globalHotkeys.Values)
                {
                    if (hotkey.ProcessMessage(ref m))
                        break;
                }
            }
            base.WndProc(ref m);
        }

        private void Window_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (gameAutoWinEventHook != IntPtr.Zero)
            {
                WinApi.UnhookWinEvent(gameAutoWinEventHook);
                gameAutoWinEventHook = IntPtr.Zero;
            }
            gameAutoWinEventDelegate = null;

            foreach (GlobalHotkey hotkey in globalHotkeys.Values)
                hotkey.Dispose();
            globalHotkeys.Clear();
            globalHotkeyPresets.Clear();
            manualOverrideActive = false;
            manualOverridePreset = null;
            manualOverrideProcess = null;
            manualToggleActive = false;
            manualTogglePreset = null;
            manualToggleReturnPreset = null;
            manualTogglePresetByMonitor.Clear();
            manualToggleReturnPresetByMonitor.Clear();

            // Restore exactly what was present before Tarkov Gamma manager started.
            Saturation.Reset();
            StartupStateManager.RestoreAndClear(displays);
        }

        //tray
        private void Window_Resize(object sender, EventArgs e)
        {
            if (WindowState == FormWindowState.Minimized)
            {
                Hide();
            }
        }

        private void notifyIcon_DoubleClick(object sender, EventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
        }

        private void toolSettings_Click(object sender, EventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
        }

        private void toolExit_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void comboBoxToolMonitor_IndexChanged(object sender, EventArgs e)
        {
            if (!disableChangeFunc)
            {
                string monitor = sender.ToString().Substring(0, sender.ToString().IndexOf(":"));
                /*string comName = toolMonitor.Items[i].ToString().Substring
                        (0, toolMonitor.Items[i].ToString().IndexOf(":"));*/

                int tmp = 0;

                disableChangeFunc = true;

                for (int i = 0; i < displays.Count; i++)
                {
                    if (monitor.Equals(displays[i].displayName))
                    {
                        tmp = i;
                    }
                    else
                    {
                        toolMonitor = toolMonitors[i];
                        toolMonitor.SelectedIndex = 0;
                    }
                }
                disableChangeFunc = false;

                toolMonitor = toolMonitors[tmp];

                if (toolMonitor.SelectedIndex != 0)
                {

                    for (int i = 0; i < displays.Count; i++)
                    {
                        if (displays[i].displayName.Equals(toolMonitor.Items[0].ToString().Substring(0, toolMonitor.Items[0].ToString().IndexOf(":"))))
                        {
                            comboBoxMonitors.Text = i + 1 + ") " + displays[i].displayName;
                            
                            numDisplay = i;
                            currDisplay.numDisplay = numDisplay;
                            currDisplay.displayLink = displays[i].displayLink;
                            currDisplay.isExternal = displays[i].isExternal;
                            break;
                        }
                    }

                    currDisplay.displayName = toolMonitor.Items[0].ToString().Substring(0, toolMonitor.Items[0].ToString().IndexOf(":"));

                    currDisplay.rGamma = float.Parse(iniFile.Read("rGamma", toolMonitor.Text), customCulture);
                    currDisplay.gGamma = float.Parse(iniFile.Read("gGamma", toolMonitor.Text), customCulture);
                    currDisplay.bGamma = float.Parse(iniFile.Read("bGamma", toolMonitor.Text), customCulture);
                    currDisplay.rContrast = float.Parse(iniFile.Read("rContrast", toolMonitor.Text), customCulture);
                    currDisplay.gContrast = float.Parse(iniFile.Read("gContrast", toolMonitor.Text), customCulture);
                    currDisplay.bContrast = float.Parse(iniFile.Read("bContrast", toolMonitor.Text), customCulture);
                    currDisplay.rBright = float.Parse(iniFile.Read("rBright", toolMonitor.Text), customCulture);
                    currDisplay.gBright = float.Parse(iniFile.Read("gBright", toolMonitor.Text), customCulture);
                    currDisplay.bBright = float.Parse(iniFile.Read("bBright", toolMonitor.Text), customCulture);
                    string saturationText = iniFile.Read("saturation", toolMonitor.Text);
                    int parsedSaturation;
                    if (!int.TryParse(saturationText, out parsedSaturation)) parsedSaturation = currDisplay.saturationDefault;
                    currDisplay.saturation = Clamp(parsedSaturation, currDisplay.saturationMin, currDisplay.saturationMax);
                    currDisplay.monitorBrightness = int.Parse(iniFile.Read("monitorBrightness", toolMonitor.Text));
                    currDisplay.monitorContrast = int.Parse(iniFile.Read("monitorContrast", toolMonitor.Text));

                    fillInfo(currDisplay);
                    initPresets();
                    buttonAllColors.PerformClick();

                    Gamma.SetGammaRamp(currDisplay.displayLink,
                        Gamma.CreateGammaRamp(currDisplay.rGamma, currDisplay.gGamma, currDisplay.bGamma,
                        currDisplay.rContrast, currDisplay.gContrast, currDisplay.bContrast, currDisplay.rBright, currDisplay.gBright,
                        currDisplay.bBright));
                    Saturation.Prepare(currDisplay);
            Saturation.Apply(currDisplay, currDisplay.saturation);

                    if (currDisplay.isExternal)
                    {
                        trackBarMonitorBrightness.Value = currDisplay.monitorBrightness;
                        trackBarMonitorContrast.Value = currDisplay.monitorContrast;
                    }
                    else
                    {
                        trackBarMonitorBrightness.Value = currDisplay.monitorBrightness;
                    }
                }
            }
        }

        //destroy focuses on buttons, trackbars, comboboxes, text, checkbox
    }
}
