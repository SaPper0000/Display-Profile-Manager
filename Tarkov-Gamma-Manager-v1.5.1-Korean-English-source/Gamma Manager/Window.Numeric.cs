using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace Gamma_Manager
{
    // Numeric controls, sliders, color-channel selection, and value synchronization.
    public partial class Window : Form
    {
        private int brightnessDebounceToken = 0;
        private int contrastDebounceToken = 0;

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

            textBoxGamma.ValueChanged += NumericBox_ValueChanged;
            textBoxContrast.ValueChanged += NumericBox_ValueChanged;
            textBoxBrightness.ValueChanged += NumericBox_ValueChanged;
            textBoxSaturation.ValueChanged += NumericBox_ValueChanged;
            textBoxMonitorBrightness.ValueChanged += NumericBox_ValueChanged;
            textBoxMonitorContrast.ValueChanged += NumericBox_ValueChanged;
        }

        private void NumericBox_ValueChanged(object sender, EventArgs e)
        {
            if (disableChangeFunc || currDisplay == null) return;
            ApplyNumericBox((NumericUpDown)sender);
        }

        private void NumericBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                ApplyNumericBox((NumericUpDown)sender);
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
            ApplyNumericBox((NumericUpDown)sender);
        }

        private void ApplyNumericBox(NumericUpDown box)
        {
            if (disableChangeFunc || currDisplay == null || applyingNumericBox) return;
            MarkCurrentMonitorAsCustom();
            applyingNumericBox = true;
            try
            {
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
                    if (!checkBoxMonitorEnabled.Checked)
                    {
                        currDisplay.monitorBrightness = trackValue;
                        textBoxMonitorBrightness.Text = trackValue.ToString();
                        return;
                    }
                    disableChangeFunc = true;
                    trackBarMonitorBrightness.Value = trackValue;
                    disableChangeFunc = false;
                    trackBarMonitorBrightness_ValueChanged(trackBarMonitorBrightness, EventArgs.Empty);
                }
                else if (box == textBoxMonitorContrast)
                {
                    trackValue = Clamp((int)Math.Round(value), trackBarMonitorContrast.Minimum, trackBarMonitorContrast.Maximum);
                    if (!checkBoxMonitorEnabled.Checked)
                    {
                        currDisplay.monitorContrast = trackValue;
                        textBoxMonitorContrast.Text = trackValue.ToString();
                        return;
                    }
                    disableChangeFunc = true;
                    trackBarMonitorContrast.Value = trackValue;
                    disableChangeFunc = false;
                    trackBarMonitorContrast_ValueChanged(trackBarMonitorContrast, EventArgs.Empty);
                }
            }
            finally
            {
                applyingNumericBox = false;
            }
        }

        private int Clamp(int value, int min, int max)
        {
            return Math.Max(min, Math.Min(max, value));
        }

        private void RefreshNumericBoxes()
        {
            disableChangeFunc = true;
            try
            {
                textBoxGamma.Value = Math.Max(textBoxGamma.Minimum, Math.Min(textBoxGamma.Maximum, (decimal)(trackBarGamma.Value / 100f)));
                textBoxContrast.Value = Math.Max(textBoxContrast.Minimum, Math.Min(textBoxContrast.Maximum, (decimal)(trackBarContrast.Value / 100f)));
                textBoxBrightness.Value = Math.Max(textBoxBrightness.Minimum, Math.Min(textBoxBrightness.Maximum, (decimal)(trackBarBrightness.Value / 100f)));
                textBoxSaturation.Value = Math.Max(textBoxSaturation.Minimum, Math.Min(textBoxSaturation.Maximum, (decimal)trackBarSaturation.Value));
                textBoxMonitorBrightness.Value = Math.Max(textBoxMonitorBrightness.Minimum, Math.Min(textBoxMonitorBrightness.Maximum, (decimal)trackBarMonitorBrightness.Value));
                textBoxMonitorContrast.Value = Math.Max(textBoxMonitorContrast.Minimum, Math.Min(textBoxMonitorContrast.Maximum, (decimal)trackBarMonitorContrast.Value));
            }
            finally
            {
                disableChangeFunc = false;
            }
        }

        private void trackBarGamma_ValueChanged(object sender, EventArgs e)
        {
            if (!disableChangeFunc)
            {
                MarkCurrentMonitorAsCustom();
                comboBoxPresets.Text = string.Empty;
                textBoxGamma.Text = ((float)trackBarGamma.Value / 100f).ToString("0.00");

                if (allColors)
                {
                    currDisplay.rGamma = (float)trackBarGamma.Value / 100f;
                    currDisplay.gGamma = (float)trackBarGamma.Value / 100f;
                    currDisplay.bGamma = (float)trackBarGamma.Value / 100f;
                }
                else if (redColor)
                {
                    currDisplay.rGamma = (float)trackBarGamma.Value / 100f;
                }
                else if (greenColor)
                {
                    currDisplay.gGamma = (float)trackBarGamma.Value / 100f;
                }
                else if (blueColor)
                {
                    currDisplay.bGamma = (float)trackBarGamma.Value / 100f;
                }

                displayService.ApplySoftwareDisplaySettings(currDisplay);
            }
        }

        private void trackBarContrast_ValueChanged(object sender, EventArgs e)
        {
            if (!disableChangeFunc)
            {
                MarkCurrentMonitorAsCustom();
                comboBoxPresets.Text = string.Empty;
                textBoxContrast.Text = ((float)trackBarContrast.Value / 100f).ToString("0.00");

                if (allColors)
                {
                    currDisplay.rContrast = (float)trackBarContrast.Value / 100f;
                    currDisplay.gContrast = (float)trackBarContrast.Value / 100f;
                    currDisplay.bContrast = (float)trackBarContrast.Value / 100f;
                }
                else if (redColor)
                {
                    currDisplay.rContrast = (float)trackBarContrast.Value / 100f;
                }
                else if (greenColor)
                {
                    currDisplay.gContrast = (float)trackBarContrast.Value / 100f;
                }
                else if (blueColor)
                {
                    currDisplay.bContrast = (float)trackBarContrast.Value / 100f;
                }

                displayService.ApplySoftwareDisplaySettings(currDisplay);
            }
        }

        private void trackBarBrightness_ValueChanged(object sender, EventArgs e)
        {
            if (!disableChangeFunc)
            {
                MarkCurrentMonitorAsCustom();
                comboBoxPresets.Text = string.Empty;
                textBoxBrightness.Text = ((float)trackBarBrightness.Value / 100f).ToString("0.00");

                if (allColors)
                {
                    currDisplay.rBright = (float)trackBarBrightness.Value / 100f;
                    currDisplay.gBright = (float)trackBarBrightness.Value / 100f;
                    currDisplay.bBright = (float)trackBarBrightness.Value / 100f;
                }
                else if (redColor)
                {
                    currDisplay.rBright = (float)trackBarBrightness.Value / 100f;
                }
                else if (greenColor)
                {
                    currDisplay.gBright = (float)trackBarBrightness.Value / 100f;
                }
                else if (blueColor)
                {
                    currDisplay.bBright = (float)trackBarBrightness.Value / 100f;
                }

                displayService.ApplySoftwareDisplaySettings(currDisplay);
            }
        }

        private void trackBarSaturation_ValueChanged(object sender, EventArgs e)
        {
            if (disableChangeFunc || currDisplay == null || !currDisplay.saturationSupported) return;
            int saturation = trackBarSaturation.Value;
            MarkCurrentMonitorAsCustom();
            currDisplay.saturation = saturation;
            textBoxSaturation.Text = saturation.ToString();
            comboBoxPresets.Text = string.Empty;
            Saturation.Apply(currDisplay, saturation);
        }

        private async void trackBarMonitorBrightness_ValueChanged(object sender, EventArgs e)
        {
            if (disableChangeFunc || currDisplay == null) return;

            int val = trackBarMonitorBrightness.Value;
            currDisplay.monitorBrightness = val;

            disableChangeFunc = true;
            textBoxMonitorBrightness.Text = val.ToString();
            disableChangeFunc = false;

            MarkCurrentMonitorAsCustom();

            // 디바운스 토큰 증가 (연타/빠른 드래그 시 이전 전송 캔슬)
            int currentToken;
            lock (displayService.ApplyLock)
            {
                currentToken = ++brightnessDebounceToken;
            }

            int currentGen = displayService.NextGeneration();
            string monitorName = currDisplay.displayName;

            await System.Threading.Tasks.Task.Delay(80);

            lock (displayService.ApplyLock)
            {
                if (isClosing) return;
                if (currentToken != brightnessDebounceToken) return; // 최신 명령이 아니면 폐기
                if (currentGen != displayService.CurrentGeneration) return;
            }

            Display.DisplayInfo targetDisplay = null;
            foreach (var d in displays)
            {
                if (d != null && string.Equals(d.displayName, monitorName, StringComparison.Ordinal))
                {
                    targetDisplay = d;
                    break;
                }
            }

            if (targetDisplay != null)
            {
                displayService.ApplyBrightnessDirect(targetDisplay, val);
            }
        }

        private async void trackBarMonitorContrast_ValueChanged(object sender, EventArgs e)
        {
            if (disableChangeFunc || currDisplay == null || !currDisplay.isExternal) return;

            int val = trackBarMonitorContrast.Value;
            currDisplay.monitorContrast = val;

            disableChangeFunc = true;
            textBoxMonitorContrast.Text = val.ToString();
            disableChangeFunc = false;

            MarkCurrentMonitorAsCustom();

            if (!checkBoxMonitorEnabled.Checked) return;

            // 디바운스 토큰 증가 (연타/빠른 드래그 시 이전 전송 캔슬)
            int currentToken;
            lock (displayService.ApplyLock)
            {
                currentToken = ++contrastDebounceToken;
            }

            int currentGen = displayService.NextGeneration();
            string monitorName = currDisplay.displayName;

            await System.Threading.Tasks.Task.Delay(80);

            lock (displayService.ApplyLock)
            {
                if (isClosing || IsDisposed || Disposing) return;
                if (currentToken != contrastDebounceToken) return; // 최신 명령이 아니면 폐기
                if (currentGen != displayService.CurrentGeneration) return;
            }

            Display.DisplayInfo targetDisplay = null;
            foreach (var d in displays)
            {
                if (d != null && string.Equals(d.displayName, monitorName, StringComparison.Ordinal))
                {
                    targetDisplay = d;
                    break;
                }
            }

            if (targetDisplay != null)
            {
                displayService.ApplyContrastDirect(targetDisplay, val);
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
                textBoxContrast.Maximum = 10000m;
            }
            else
            {
                trackBarContrast.Maximum = 300;
                textBoxContrast.Maximum = 3.00m;
                if (textBoxContrast.Value > textBoxContrast.Maximum) textBoxContrast.Value = textBoxContrast.Maximum;
            }
        }
    }
}