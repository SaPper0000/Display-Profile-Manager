namespace Gamma_Manager
{
    partial class Window
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null)
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            var resources = new System.ComponentModel.ComponentResourceManager(typeof(Window));

            trackBarGamma = new System.Windows.Forms.TrackBar();
            buttonRed = new System.Windows.Forms.Button();
            buttonGreen = new System.Windows.Forms.Button();
            buttonBlue = new System.Windows.Forms.Button();
            buttonAllColors = new System.Windows.Forms.Button();
            comboBoxPresets = new System.Windows.Forms.ComboBox();
            buttonReset = new System.Windows.Forms.Button();
            buttonSave = new System.Windows.Forms.Button();
            comboBoxMonitors = new System.Windows.Forms.ComboBox();
            trackBarContrast = new System.Windows.Forms.TrackBar();
            trackBarBrightness = new System.Windows.Forms.TrackBar();
            trackBarSaturation = new System.Windows.Forms.TrackBar();
            textBoxSaturation = new System.Windows.Forms.NumericUpDown();
            labelSaturation = new System.Windows.Forms.Label();
            textBoxGamma = new System.Windows.Forms.NumericUpDown();
            textBoxContrast = new System.Windows.Forms.NumericUpDown();
            textBoxBrightness = new System.Windows.Forms.NumericUpDown();
            labelGamma = new System.Windows.Forms.Label();
            labelContrast = new System.Windows.Forms.Label();
            labelBrightness = new System.Windows.Forms.Label();
            buttonDelete = new System.Windows.Forms.Button();
            labelMonitorBrightnessUp = new System.Windows.Forms.Label();
            textBoxMonitorBrightness = new System.Windows.Forms.NumericUpDown();
            trackBarMonitorBrightness = new System.Windows.Forms.TrackBar();
            labelMonitorBrightnessDown = new System.Windows.Forms.Label();
            buttonHotkeys = new System.Windows.Forms.Button();
            buttonHide = new System.Windows.Forms.Button();
            labelMonitorContrastUp = new System.Windows.Forms.Label();
            labelMonitorContrastDown = new System.Windows.Forms.Label();
            trackBarMonitorContrast = new System.Windows.Forms.TrackBar();
            textBoxMonitorContrast = new System.Windows.Forms.NumericUpDown();
            buttonForward = new System.Windows.Forms.Button();
            checkBoxExContrast = new System.Windows.Forms.CheckBox();
            notifyIcon = new System.Windows.Forms.NotifyIcon(components);
            pictureBox1 = new System.Windows.Forms.PictureBox();
            contextMenu = new System.Windows.Forms.ContextMenuStrip(components);

            trackBarGamma.LargeChange = 1; trackBarGamma.Minimum = 30; trackBarGamma.Maximum = 440; trackBarGamma.SmallChange = 5; trackBarGamma.TickStyle = System.Windows.Forms.TickStyle.None; trackBarGamma.Value = 100; trackBarGamma.ValueChanged += trackBarGamma_ValueChanged;
            trackBarContrast.LargeChange = 1; trackBarContrast.Minimum = 10; trackBarContrast.Maximum = 300; trackBarContrast.SmallChange = 1; trackBarContrast.TickStyle = System.Windows.Forms.TickStyle.None; trackBarContrast.Value = 10; trackBarContrast.ValueChanged += trackBarContrast_ValueChanged;
            trackBarBrightness.LargeChange = 1; trackBarBrightness.Minimum = -100; trackBarBrightness.Maximum = 100; trackBarBrightness.SmallChange = 5; trackBarBrightness.TickStyle = System.Windows.Forms.TickStyle.None; trackBarBrightness.Value = 0; trackBarBrightness.ValueChanged += trackBarBrightness_ValueChanged;
            trackBarSaturation.LargeChange = 1; trackBarSaturation.Minimum = 0; trackBarSaturation.Maximum = 200; trackBarSaturation.SmallChange = 5; trackBarSaturation.TickStyle = System.Windows.Forms.TickStyle.None; trackBarSaturation.Value = 100; trackBarSaturation.ValueChanged += trackBarSaturation_ValueChanged;
            trackBarMonitorBrightness.LargeChange = 1; trackBarMonitorBrightness.Minimum = 0; trackBarMonitorBrightness.Maximum = 100; trackBarMonitorBrightness.SmallChange = 1; trackBarMonitorBrightness.TickStyle = System.Windows.Forms.TickStyle.None; trackBarMonitorBrightness.Value = 100; trackBarMonitorBrightness.ValueChanged += trackBarMonitorBrightness_ValueChanged;
            trackBarMonitorContrast.LargeChange = 1; trackBarMonitorContrast.Minimum = 0; trackBarMonitorContrast.Maximum = 100; trackBarMonitorContrast.SmallChange = 1; trackBarMonitorContrast.TickStyle = System.Windows.Forms.TickStyle.None; trackBarMonitorContrast.Value = 100; trackBarMonitorContrast.ValueChanged += trackBarMonitorContrast_ValueChanged;

            buttonRed.Text = "Red"; buttonRed.UseVisualStyleBackColor = true; buttonRed.Click += buttonRed_Click;
            buttonGreen.Text = "Green"; buttonGreen.UseVisualStyleBackColor = true; buttonGreen.Click += buttonGreen_Click;
            buttonBlue.Text = "Blue"; buttonBlue.UseVisualStyleBackColor = true; buttonBlue.Click += buttonBlue_Click;
            buttonAllColors.Text = "All Colors"; buttonAllColors.UseVisualStyleBackColor = true; buttonAllColors.Click += buttonAllColors_Click;
            buttonReset.Text = "Reset"; buttonReset.UseVisualStyleBackColor = true; buttonReset.Click += buttonReset_Click;
            buttonSave.Text = "Save"; buttonSave.UseVisualStyleBackColor = true; buttonSave.Click += buttonSave_Click;
            buttonDelete.Text = "Profiles"; buttonDelete.UseVisualStyleBackColor = true; buttonDelete.Click += buttonDelete_Click;
            buttonHotkeys.Text = "Hotkeys"; buttonHotkeys.UseVisualStyleBackColor = true; buttonHotkeys.Click += buttonHotkeys_Click;
            buttonHide.Text = "Hide"; buttonHide.UseVisualStyleBackColor = true; buttonHide.Click += buttonHide_Click;
            buttonForward.Text = "Forward"; buttonForward.UseVisualStyleBackColor = true; buttonForward.Click += buttonForward_Click;

            comboBoxMonitors.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            comboBoxMonitors.FormattingEnabled = true;
            comboBoxMonitors.SelectedIndexChanged += comboBoxMonitors_SelectedIndexChanged;

            comboBoxPresets.FormattingEnabled = true;
            comboBoxPresets.SelectedIndexChanged += comboBoxPresets_SelectedIndexChanged;

            ConfigureNumericBox(textBoxGamma, 0.30m, 4.40m, 1.00m, 0.05m, 2);
            ConfigureNumericBox(textBoxContrast, 0.10m, 3.00m, 1.00m, 0.01m, 2);
            ConfigureNumericBox(textBoxBrightness, -1.00m, 1.00m, 0.00m, 0.05m, 2);
            ConfigureNumericBox(textBoxSaturation, 0m, 10000m, 100m, 5m, 0);
            ConfigureNumericBox(textBoxMonitorBrightness, 0m, 100m, 50m, 1m, 0);
            ConfigureNumericBox(textBoxMonitorContrast, 0m, 100m, 50m, 1m, 0);

            labelGamma.AutoSize = true;
            labelContrast.AutoSize = true;
            labelBrightness.AutoSize = true;
            labelSaturation.AutoSize = true;
            labelMonitorBrightnessUp.AutoSize = true;
            labelMonitorBrightnessDown.AutoSize = true;
            labelMonitorContrastUp.AutoSize = true;
            labelMonitorContrastDown.AutoSize = true;

            checkBoxExContrast.AutoSize = true;
            checkBoxExContrast.Text = "RGB Link";
            checkBoxExContrast.CheckedChanged += checkBoxExContrast_CheckedChanged;

            pictureBox1.BackgroundImage = global::Gamma_Manager.Properties.Resources.TestMonitor;
            pictureBox1.BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            pictureBox1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            pictureBox1.TabStop = false;

            notifyIcon.Icon = (System.Drawing.Icon)resources.GetObject("notifyIcon.Icon");
            notifyIcon.Text = "Tarkov Gamma Manager v1.4.7";
            notifyIcon.Visible = true;
            notifyIcon.DoubleClick += notifyIcon_DoubleClick;

            contextMenu.ImageScalingSize = new System.Drawing.Size(20, 20);
            contextMenu.Name = "contextMenu";

            AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            ShowIcon = true;
            Name = "Window";
            StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            Text = "Tarkov Gamma Manager v1.4.7";
            Load += Window_Load;
            Resize += Window_Resize;
        }

        #endregion

        private System.Windows.Forms.TrackBar trackBarGamma;
        private System.Windows.Forms.Button buttonRed;
        private System.Windows.Forms.Button buttonGreen;
        private System.Windows.Forms.Button buttonBlue;
        private System.Windows.Forms.Button buttonAllColors;
        private System.Windows.Forms.ComboBox comboBoxPresets;
        private System.Windows.Forms.Button buttonReset;
        private System.Windows.Forms.Button buttonSave;
        private System.Windows.Forms.ComboBox comboBoxMonitors;
        private System.Windows.Forms.TrackBar trackBarContrast;
        private System.Windows.Forms.TrackBar trackBarBrightness;
        private System.Windows.Forms.TrackBar trackBarSaturation;
        private System.Windows.Forms.NumericUpDown textBoxSaturation;
        private System.Windows.Forms.Label labelSaturation;
        private System.Windows.Forms.NumericUpDown textBoxGamma;
        private System.Windows.Forms.NumericUpDown textBoxContrast;
        private System.Windows.Forms.NumericUpDown textBoxBrightness;
        private System.Windows.Forms.Label labelGamma;
        private System.Windows.Forms.Label labelContrast;
        private System.Windows.Forms.Label labelBrightness;
        private System.Windows.Forms.Button buttonDelete;
        private System.Windows.Forms.Label labelMonitorBrightnessUp;
        private System.Windows.Forms.NumericUpDown textBoxMonitorBrightness;
        private System.Windows.Forms.TrackBar trackBarMonitorBrightness;
        private System.Windows.Forms.Label labelMonitorBrightnessDown;
        private System.Windows.Forms.Button buttonHotkeys;
        private System.Windows.Forms.Button buttonHide;
        private System.Windows.Forms.Label labelMonitorContrastUp;
        private System.Windows.Forms.Label labelMonitorContrastDown;
        private System.Windows.Forms.TrackBar trackBarMonitorContrast;
        private System.Windows.Forms.NumericUpDown textBoxMonitorContrast;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Button buttonForward;
        private System.Windows.Forms.CheckBox checkBoxExContrast;
        private System.Windows.Forms.NotifyIcon notifyIcon;
        private System.Windows.Forms.ContextMenuStrip contextMenu;
    }
}
