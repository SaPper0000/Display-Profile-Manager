using System;
using System.Drawing;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal sealed class OSDSettingsForm : Form
    {
        private readonly IniFile ini;
        private CheckBox chkEnabled;
        private ComboBox comboColor;
        private ComboBox comboPosition;
        private NumericUpDown numFontSize;
        private NumericUpDown numDuration;
        private Button btnSave;
        private Button btnClose;
        private bool ko;

        public OSDSettingsForm(IniFile iniFile)
        {
            ini = iniFile;
            ko = LanguageManager.Korean;
            Text = ko ? "OSD 팝업 알림 설정" : "OSD Popup Settings";
            Width = 420;
            Height = 345;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BuildUI();
            LoadSettings();
            ThemeManager.Apply(this);
        }

        private void BuildUI()
        {
            Label lblTitle = new Label { Text = ko ? "OSD 팝업 알림 환경설정" : "OSD Popup Notification Preferences", Location = new Point(20, 20), AutoSize = true, Font = new Font("Segoe UI", 11, FontStyle.Bold) };
            Controls.Add(lblTitle);

            // Enabled Checkbox
            chkEnabled = new CheckBox { Text = ko ? " OSD 팝업 알림 기능 사용" : " Enable OSD Popup Notifications", Location = new Point(20, 56), Size = new Size(350, 25), Font = new Font("Segoe UI", 9.5f) };
            Controls.Add(chkEnabled);

            // Color
            Label lblColor = new Label { Text = ko ? "폰트 색상:" : "Font Color:", Location = new Point(20, 95), Size = new Size(110, 25), TextAlign = ContentAlignment.MiddleLeft };
            comboColor = new ComboBox { Location = new Point(140, 94), Size = new Size(230, 28), DropDownStyle = ComboBoxStyle.DropDownList };
            comboColor.Items.Add(new Item(ko ? "초록 (LimeGreen)" : "LimeGreen", "LimeGreen"));
            comboColor.Items.Add(new Item(ko ? "노랑 (Yellow)" : "Yellow", "Yellow"));
            comboColor.Items.Add(new Item(ko ? "하늘색 (SkyBlue)" : "SkyBlue", "SkyBlue"));
            comboColor.Items.Add(new Item(ko ? "흰색 (White)" : "White", "White"));
            comboColor.Items.Add(new Item(ko ? "오렌지 (Orange)" : "Orange", "Orange"));
            comboColor.Items.Add(new Item(ko ? "빨강 (Red)" : "Red", "Red"));
            Controls.Add(lblColor);
            Controls.Add(comboColor);

            // Position
            Label lblPos = new Label { Text = ko ? "표시 위치:" : "Position:", Location = new Point(20, 140), Size = new Size(110, 25), TextAlign = ContentAlignment.MiddleLeft };
            comboPosition = new ComboBox { Location = new Point(140, 139), Size = new Size(230, 28), DropDownStyle = ComboBoxStyle.DropDownList };
            comboPosition.Items.Add(new Item(ko ? "상단 중앙 (Top Center)" : "Top Center", "TopCenter"));
            comboPosition.Items.Add(new Item(ko ? "좌측 상단 (Top Left)" : "Top Left", "TopLeft"));
            comboPosition.Items.Add(new Item(ko ? "우측 상단 (Top Right)" : "Top Right", "TopRight"));
            Controls.Add(lblPos);
            Controls.Add(comboPosition);

            // Font Size
            Label lblSize = new Label { Text = ko ? "폰트 크기:" : "Font Size:", Location = new Point(20, 185), Size = new Size(110, 25), TextAlign = ContentAlignment.MiddleLeft };
            numFontSize = new NumericUpDown { Location = new Point(140, 184), Size = new Size(100, 28), Minimum = 20m, Maximum = 48m, Increment = 2m, Value = 32m };
            Controls.Add(lblSize);
            Controls.Add(numFontSize);

            // Duration
            Label lblDur = new Label { Text = ko ? "표시 시간 (초):" : "Duration (sec):", Location = new Point(20, 230), Size = new Size(110, 25), TextAlign = ContentAlignment.MiddleLeft };
            numDuration = new NumericUpDown { Location = new Point(140, 229), Size = new Size(100, 28), Minimum = 1m, Maximum = 3m, Increment = 0.5m, DecimalPlaces = 1, Value = 1.5m };
            Controls.Add(lblDur);
            Controls.Add(numDuration);

            // Buttons
            btnSave = new Button { Text = ko ? "저장" : "Save", Location = new Point(200, 275), Size = new Size(85, 32), DialogResult = DialogResult.OK };
            btnSave.Click += (s, e) => SaveSettings();
            btnClose = new Button { Text = ko ? "닫기" : "Close", Location = new Point(295, 275), Size = new Size(75, 32), DialogResult = DialogResult.Cancel };
            Controls.Add(btnSave);
            Controls.Add(btnClose);
            AcceptButton = btnSave;
            CancelButton = btnClose;
        }

        private class Item
        {
            public string Text { get; }
            public string Value { get; }
            public Item(string text, string value) { Text = text; Value = value; }
            public override string ToString() { return Text; }
        }

        private void LoadSettings()
        {
            string savedEnabled = ini.Read("OsdEnabled", "Settings");
            chkEnabled.Checked = string.IsNullOrEmpty(savedEnabled) || savedEnabled.Equals("True", StringComparison.OrdinalIgnoreCase) || savedEnabled == "1";

            string savedColor = ini.Read("OsdColor", "Settings");
            if (string.IsNullOrEmpty(savedColor)) savedColor = "LimeGreen";
            foreach (Item item in comboColor.Items)
            {
                if (item.Value.Equals(savedColor, StringComparison.OrdinalIgnoreCase))
                {
                    comboColor.SelectedItem = item;
                    break;
                }
            }
            if (comboColor.SelectedIndex < 0 && comboColor.Items.Count > 0) comboColor.SelectedIndex = 0;

            string savedPos = ini.Read("OsdPosition", "Settings");
            if (string.IsNullOrEmpty(savedPos)) savedPos = "TopCenter";
            foreach (Item item in comboPosition.Items)
            {
                if (item.Value.Equals(savedPos, StringComparison.OrdinalIgnoreCase))
                {
                    comboPosition.SelectedItem = item;
                    break;
                }
            }
            if (comboPosition.SelectedIndex < 0 && comboPosition.Items.Count > 0) comboPosition.SelectedIndex = 0;

            string savedSize = ini.Read("OsdFontSize", "Settings");
            if (int.TryParse(savedSize, out int sz))
            {
                if (sz >= 20 && sz <= 48) numFontSize.Value = sz;
            }

            string savedDur = ini.Read("OsdDuration", "Settings");
            if (int.TryParse(savedDur, out int ms))
            {
                decimal sec = ms / 1000m;
                if (sec >= 1m && sec <= 3m) numDuration.Value = sec;
            }
        }

        private void SaveSettings()
        {
            ini.Write("OsdEnabled", chkEnabled.Checked ? "True" : "False", "Settings");
            if (comboColor.SelectedItem is Item colItem)
                ini.Write("OsdColor", colItem.Value, "Settings");
            if (comboPosition.SelectedItem is Item posItem)
                ini.Write("OsdPosition", posItem.Value, "Settings");
            ini.Write("OsdFontSize", ((int)numFontSize.Value).ToString(), "Settings");
            int ms = (int)(numDuration.Value * 1000m);
            ini.Write("OsdDuration", ms.ToString(), "Settings");
        }
    }
}