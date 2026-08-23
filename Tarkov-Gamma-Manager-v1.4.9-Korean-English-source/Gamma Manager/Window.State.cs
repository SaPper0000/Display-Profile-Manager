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
    // Window state, shared fields, and runtime flags.
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
        Button buttonKorean;
        Button buttonEnglish;
        Label languageLabel;
        Label themeLabel;
        Button buttonGameAuto;
        Button buttonBackup;
        Button buttonRestore;
        Button buttonUpdateCheck;
        // Windows foreground-change event hook replaces the old 350ms polling timer.
        private IntPtr gameAutoWinEventHook = IntPtr.Zero;
        private WinApi.WinEventDelegate gameAutoWinEventDelegate;
        string activeAutoPreset = null;
        bool autoGameFocused = false;
        string manualOverridePreset = null;
        string manualOverrideProcess = null;
        string manualOverrideReturnPreset = null;
        string manualOverrideMonitor = null;
        bool manualOverrideActive = false;
        // Toggle state is truly per monitor. Each monitor can have its own toggle
        // active at the same time without changing or clearing another monitor's state.
        readonly Dictionary<string, string> manualTogglePresetByMonitor = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, string> manualToggleReturnPresetByMonitor = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, bool> manualToggleActiveByMonitor = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool manualToggleActive = false; // compatibility flag: true when any monitor has a toggle active
        string manualTogglePreset = null;
        string manualToggleReturnPreset = null;
        readonly Dictionary<string, string> currentPresetByMonitor = new Dictionary<string, string>(StringComparer.Ordinal);
        // Exact per-monitor runtime snapshot used by toggles/Game Auto.
        // A profile name alone is not enough because the user may have changed
        // gamma/brightness/contrast/saturation/physical monitor settings without saving.
        private sealed class MonitorStateSnapshot
        {
            public float rGamma, gGamma, bGamma;
            public float rContrast, gContrast, bContrast;
            public float rBright, gBright, bBright;
            public int saturation;
            public int monitorBrightness, monitorContrast;
        }
        readonly Dictionary<string, MonitorStateSnapshot> manualToggleReturnStateByMonitor = new Dictionary<string, MonitorStateSnapshot>(StringComparer.Ordinal);
        // True when the toggle was enabled on top of an active Game Auto session.
        // In that case, turning the toggle OFF while the game is unfocused must restore
        // the real pre-Game-Auto base state, not the Game Auto profile that happened to
        // be visible when the toggle was enabled.
        readonly Dictionary<string, bool> manualToggleWasOverGameAutoByMonitor = new Dictionary<string, bool>(StringComparer.Ordinal);
        readonly Dictionary<string, MonitorStateSnapshot> gameAutoPreviousStateByMonitor = new Dictionary<string, MonitorStateSnapshot>(StringComparer.Ordinal);
        readonly Dictionary<string, string> gameAutoPreviousPresetByMonitor = new Dictionary<string, string>(StringComparer.Ordinal);
        bool gameAutoPreviousStateCaptured = false;
        int gameAutoPreviousDisplayIndex = 0;
        string activeAutoMonitor = null;
        // Process that currently owns the Game Auto layer. The layer must survive Alt+Tab
        // and only be removed when the mapped game process actually ends.
        string activeAutoProcess = null;
        // Game Auto session owner. This is kept across Alt+Tab so the true pre-Game-Auto
        // base state is not lost while the game process is still running.
        string gameAutoSessionProcess = null;
        // Last Game Auto target retained after Alt+Tab so a toggle pressed from
        // another monitor can still restore the pre-Game-Auto state.
        string lastGameAutoAppliedMonitor = null;
        string lastGameAutoAppliedPreset = null;
        Label appTitle;
        Label appSubtitle;
        Label sectionDisplay;
        Label sectionGpu;
        Label sectionMonitor;
        Label sectionMonitorActions;
        ModernPanel displayCard;
        ModernPanel gpuCard;
        ModernPanel rgbCard;
        ModernPanel monitorCard;
        ModernPanel monitorActionCard;
        ModernPanel rightCard;
        Label imageCaption;
        Label imageSubCaption;
        Label profilePriorityTitle;
        Label profilePriorityHotkey;
        Label profilePriorityGameAuto;
        Label topMostHelp;
        Label topMostHelpHitArea;
        ToolTip topMostToolTip;
        CheckBox checkBoxMonitorEnabled;
        CheckBox checkBoxTopMost;
        CheckBox checkBoxImageOff;
        CheckBox checkBoxLogEnabled;

        bool disableChangeFunc = false;
        bool applyingPreset = false;
        bool applyingNumericBox = false;

        // Generation token for delayed physical-monitor profile applies. Every new
        // profile/toggle application invalidates older callbacks so rapid hotkey
        // presses can never re-apply a stale monitor value after a newer state wins.
        private int physicalMonitorApplyGeneration = 0;

        bool allColors = true;
        bool redColor = false;
        bool greenColor = false;
        bool blueColor = false;

    }
}
