using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Gamma_Manager
{
    public partial class Window : Form
    {
        private readonly IniFile iniFile;
        private readonly DisplayService displayService = new DisplayService();

        private List<Display.DisplayInfo> displays = new List<Display.DisplayInfo>();
        private Display.DisplayInfo currDisplay;
        private int numDisplay = 0;

        private bool disableChangeFunc = false;
        private bool applyingPreset = false;
        private bool applyingNumericBox = false;
        private bool isClosing = false;

        private bool allColors = true;
        private bool redColor = false;
        private bool greenColor = false;
        private bool blueColor = false;

        private readonly Dictionary<int, GlobalHotkey> globalHotkeys = new Dictionary<int, GlobalHotkey>();
        private readonly Dictionary<int, string> globalHotkeyPresets = new Dictionary<int, string>();
        private int nextHotkeyId = 1;

        private readonly Dictionary<string, string> currentPresetByMonitor = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // 핫키 토글 관리 변수


        // UI 컨트롤 및 폼 요소 선언부
        private Label appTitle;
        private Label appSubtitle;
        private ModernPanel rgbCard;
        private ModernPanel gpuCard;
        private ModernPanel monitorCard;
        private ModernPanel monitorActionCard;
        private ModernPanel displayCard;
        private ModernPanel rightCard;

        private Label sectionGpu;
        private Label sectionMonitor;
        private Label sectionMonitorActions;
        private Label sectionDisplay;

        private Button buttonTheme;
        private Label themeLabel;
        private Button buttonEnglish;
        private Button buttonKorean;
        private Label languageLabel;

        private CheckBox checkBoxTopMost;
        private Label topMostHelp;
        private Label topMostHelpHitArea;
        private ToolTip topMostToolTip;

        private CheckBox checkBoxImageOff;
        private ComboBox comboBoxImageSelect;
        private Label imageCaption;
        private Label imageSubCaption;

        private CheckBox checkBoxLogEnabled;
        private CheckBox checkBoxMonitorEnabled;

        private Button buttonRestore;
        private Button buttonBackup;
        private Button buttonOpenFolder;
        private Button buttonUpdateCheck;

        private ToolStripComboBox toolMonitor = new ToolStripComboBox();
        private List<ToolStripItem> toolMonitors = new List<ToolStripItem>();

#pragma warning disable CS0649
        private Label profilePriorityTitle;
        private Label profilePriorityHotkey;
#pragma warning restore CS0649

        private class MonitorStateSnapshot
        {
            public float rGamma;
            public float gGamma;
            public float bGamma;
            public float rContrast;
            public float gContrast;
            public float bContrast;
            public float rBright;
            public float gBright;
            public float bBright;
            public int saturation;
            public int monitorBrightness;
            public int monitorContrast;
        }
    }
}