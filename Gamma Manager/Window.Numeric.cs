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
            ApplyNumericValue((NumericUpDown)sender);
        }

        private void NumericBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                CommitNumericText((NumericUpDown)sender);
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
            CommitNumericText((NumericUpDown)sender);
        }

        private void CommitNumericText(NumericUpDown box)
        {
            if (disableChangeFunc || currDisplay == null || applyingNumericBox) return;
            try
            {
                string text = box.Text.Trim().Replace(',', '.');
                if (double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double parsed))
                {
                    decimal clamped = Math.Max(box.Minimum, Math.Min(box.Maximum, (decimal)parsed));
                    if (box.Value != clamped)
                    {
                        box.Value = clamped;
                        return; // box.Value 변경이 ValueChanged 이벤트를 발생시켜 ApplyNumericValue를 호출함
                    }
                }
            }
            catch { }

            ApplyNumericValue(box);
        }

        private void ApplyNumericValue(NumericUpDown box)
        {
            if (disableChangeFunc || currDisplay == null || applyingNumericBox) return;
            MarkCurrentMonitorAsCustom();
            applyingNumericBox = true;
            try
            {
                double value = (double)box.Value;

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

                float gammaVal = (float)trackBarGamma.Value / 100f;

                disableChangeFunc = true;
                textBoxGamma.Value = Math.Max(textBoxGamma.Minimum, Math.Min(textBoxGamma.Maximum, (decimal)gammaVal));
                disableChangeFunc = false;

                if (allColors)
                {
                    currDisplay.rGamma = gammaVal;
                    currDisplay.gGamma = gammaVal;
                    currDisplay.bGamma = gammaVal;
                }
                else if (redColor)
                {
                    currDisplay.rGamma = gammaVal;
                }
                else if (greenColor)
                {
                    currDisplay.gGamma = gammaVal;
                }
                else if (blueColor)
                {
                    currDisplay.bGamma = gammaVal;
                }

                displayService.ApplyGammaOnly(currDisplay);
            }
        }

        private void trackBarContrast_ValueChanged(object sender, EventArgs e)
        {
            if (!disableChangeFunc)
            {
                MarkCurrentMonitorAsCustom();

                float contrastVal = (float)trackBarContrast.Value / 100f;

                disableChangeFunc = true;
                textBoxContrast.Value = Math.Max(textBoxContrast.Minimum, Math.Min(textBoxContrast.Maximum, (decimal)contrastVal));
                disableChangeFunc = false;

                if (allColors)
                {
                    currDisplay.rContrast = contrastVal;
                    currDisplay.gContrast = contrastVal;
                    currDisplay.bContrast = contrastVal;
                }
                else if (redColor)
                {
                    currDisplay.rContrast = contrastVal;
                }
                else if (greenColor)
                {
                    currDisplay.gContrast = contrastVal;
                }
                else if (blueColor)
                {
                    currDisplay.bContrast = contrastVal;
                }

                displayService.ApplyGammaOnly(currDisplay);
            }
        }

        private void trackBarBrightness_ValueChanged(object sender, EventArgs e)
        {
            if (!disableChangeFunc)
            {
                MarkCurrentMonitorAsCustom();

                float brightVal = (float)trackBarBrightness.Value / 100f;

                disableChangeFunc = true;
                textBoxBrightness.Value = Math.Max(textBoxBrightness.Minimum, Math.Min(textBoxBrightness.Maximum, (decimal)brightVal));
                disableChangeFunc = false;

                if (allColors)
                {
                    currDisplay.rBright = brightVal;
                    currDisplay.gBright = brightVal;
                    currDisplay.bBright = brightVal;
                }
                else if (redColor)
                {
                    currDisplay.rBright = brightVal;
                }
                else if (greenColor)
                {
                    currDisplay.gBright = brightVal;
                }
                else if (blueColor)
                {
                    currDisplay.bBright = brightVal;
                }

                displayService.ApplyGammaOnly(currDisplay);
            }
        }

        private void trackBarSaturation_ValueChanged(object sender, EventArgs e)
        {
            if (disableChangeFunc || currDisplay == null || !currDisplay.saturationSupported) return;
            int saturation = trackBarSaturation.Value;
            MarkCurrentMonitorAsCustom();
            currDisplay.saturation = saturation;

            disableChangeFunc = true;
            textBoxSaturation.Value = Math.Max(textBoxSaturation.Minimum, Math.Min(textBoxSaturation.Maximum, (decimal)saturation));
            disableChangeFunc = false;

            Saturation.Apply(currDisplay, saturation);
        }

        private async void trackBarMonitorBrightness_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                if (disableChangeFunc || currDisplay == null) return;

                int val = trackBarMonitorBrightness.Value;
                currDisplay.monitorBrightness = val;

                disableChangeFunc = true;
                textBoxMonitorBrightness.Value = Math.Max(textBoxMonitorBrightness.Minimum, Math.Min(textBoxMonitorBrightness.Maximum, (decimal)val));
                disableChangeFunc = false;

                MarkCurrentMonitorAsCustom();

                if (!checkBoxMonitorEnabled.Checked) return;

                string monitorKey = DisplayService.GetMonitorKey(currDisplay);
                int targetGen = displayService != null ? displayService.NextGeneration(monitorKey) : 0;
                int targetTopologyGen = displayService != null ? displayService.CurrentTopologyGeneration : 0;
                Display.DisplayInfo targetDisplay = currDisplay;

                await System.Threading.Tasks.Task.Delay(130);

                if (isClosing || IsDisposed || Disposing || displayService == null) return;
                if (targetTopologyGen != displayService.CurrentTopologyGeneration) return;
                if (targetGen != displayService.GetCurrentGeneration(monitorKey)) return;

                displayService.ApplyBrightnessDirect(targetDisplay, val, monitorKey, targetGen, targetTopologyGen);
            }
            catch (Exception ex)
            {
                Logger.Warn($"trackBarMonitorBrightness_ValueChanged exception: {ex.Message}");
            }
        }

        private async void trackBarMonitorContrast_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                if (disableChangeFunc || currDisplay == null || !currDisplay.isExternal) return;

                int val = trackBarMonitorContrast.Value;
                currDisplay.monitorContrast = val;

                disableChangeFunc = true;
                textBoxMonitorContrast.Value = Math.Max(textBoxMonitorContrast.Minimum, Math.Min(textBoxMonitorContrast.Maximum, (decimal)val));
                disableChangeFunc = false;

                MarkCurrentMonitorAsCustom();

                if (!checkBoxMonitorEnabled.Checked) return;

                string monitorKey = DisplayService.GetMonitorKey(currDisplay);
                int targetGen = displayService != null ? displayService.NextGeneration(monitorKey) : 0;
                int targetTopologyGen = displayService != null ? displayService.CurrentTopologyGeneration : 0;
                Display.DisplayInfo targetDisplay = currDisplay;

                await System.Threading.Tasks.Task.Delay(130);

                if (isClosing || IsDisposed || Disposing || displayService == null) return;
                if (targetTopologyGen != displayService.CurrentTopologyGeneration) return;
                if (targetGen != displayService.GetCurrentGeneration(monitorKey)) return;

                displayService.ApplyContrastDirect(targetDisplay, val, monitorKey, targetGen, targetTopologyGen);
            }
            catch (Exception ex)
            {
                Logger.Warn($"trackBarMonitorContrast_ValueChanged exception: {ex.Message}");
            }
        }

        // 모든 채널 플래그를 확실하게 해제하는 헬퍼 메서드
        private void ClearAllColorFlags()
        {
            allColors = false;
            redColor = false;
            greenColor = false;
            blueColor = false;
        }

        // RGB 연동 및 각 색상 채널 버튼의 배경/글자 색상 및 굵기 업데이트 함수
        public void UpdateColorChannelButtonStyles()
        {
            if (buttonAllColors == null || buttonRed == null || buttonGreen == null || buttonBlue == null) return;

            Color defaultBack = ThemeManager.IsDark ? ThemeManager.DarkControl : ThemeManager.LightControl;
            Color defaultText = ThemeManager.IsDark ? ThemeManager.DarkText : ThemeManager.LightText;

            // 1. 모든 버튼을 기본 테마 색상으로 초기화
            buttonAllColors.BackColor = defaultBack;
            buttonAllColors.ForeColor = defaultText;
            buttonAllColors.Font = _cachedRegularFont;

            buttonRed.BackColor = defaultBack;
            buttonRed.ForeColor = defaultText;
            buttonRed.Font = _cachedRegularFont;

            buttonGreen.BackColor = defaultBack;
            buttonGreen.ForeColor = defaultText;
            buttonGreen.Font = _cachedRegularFont;

            buttonBlue.BackColor = defaultBack;
            buttonBlue.ForeColor = defaultText;
            buttonBlue.Font = _cachedRegularFont;

            // 2. 현재 선택된 모드에만 고유 색상 및 굵은 글꼴 적용
            if (allColors)
            {
                buttonAllColors.BackColor = Color.FromArgb(0, 120, 215); // 연동 블루
                buttonAllColors.ForeColor = Color.White;
                buttonAllColors.Font = _cachedBoldFont;
            }
            else if (redColor)
            {
                buttonRed.BackColor = Color.FromArgb(215, 45, 45); // 빨강
                buttonRed.ForeColor = Color.White;
                buttonRed.Font = _cachedBoldFont;
            }
            else if (greenColor)
            {
                buttonGreen.BackColor = Color.FromArgb(40, 160, 60); // 초록
                buttonGreen.ForeColor = Color.White;
                buttonGreen.Font = _cachedBoldFont;
            }
            else if (blueColor)
            {
                buttonBlue.BackColor = Color.FromArgb(30, 110, 220); // 파랑
                buttonBlue.ForeColor = Color.White;
                buttonBlue.Font = _cachedBoldFont;
            }
        }

        private void buttonAllColors_Click(object sender, EventArgs e)
        {
            if (currDisplay == null) return;
            disableChangeFunc = true;
            ClearAllColorFlags();
            allColors = true;

            decimal avgGamma = Math.Max(textBoxGamma.Minimum, Math.Min(textBoxGamma.Maximum, (decimal)((currDisplay.rGamma + currDisplay.gGamma + currDisplay.bGamma) / 3f)));
            decimal avgContrast = Math.Max(textBoxContrast.Minimum, Math.Min(textBoxContrast.Maximum, (decimal)((currDisplay.rContrast + currDisplay.gContrast + currDisplay.bContrast) / 3f)));
            decimal avgBright = Math.Max(textBoxBrightness.Minimum, Math.Min(textBoxBrightness.Maximum, (decimal)((currDisplay.rBright + currDisplay.gBright + currDisplay.bBright) / 3f)));

            textBoxGamma.Value = avgGamma;
            textBoxContrast.Value = avgContrast;
            textBoxBrightness.Value = avgBright;
            textBoxSaturation.Value = Math.Max(textBoxSaturation.Minimum, Math.Min(textBoxSaturation.Maximum, (decimal)currDisplay.saturation));

            trackBarGamma.Value = Clamp((int)(avgGamma * 100m), trackBarGamma.Minimum, trackBarGamma.Maximum);
            trackBarContrast.Value = Clamp((int)(avgContrast * 100m), trackBarContrast.Minimum, trackBarContrast.Maximum);
            trackBarBrightness.Value = Clamp((int)(avgBright * 100m), trackBarBrightness.Minimum, trackBarBrightness.Maximum);
            trackBarSaturation.Minimum = currDisplay.saturationMin;
            trackBarSaturation.Maximum = Math.Max(currDisplay.saturationMin + 1, currDisplay.saturationMax);
            trackBarSaturation.SmallChange = Math.Max(1, currDisplay.saturationStep);
            trackBarSaturation.Value = Clamp(currDisplay.saturation, trackBarSaturation.Minimum, trackBarSaturation.Maximum);
            trackBarSaturation.Enabled = currDisplay.saturationSupported;
            textBoxSaturation.ReadOnly = !currDisplay.saturationSupported;
            labelSaturation.Text = currDisplay.saturationSupported
                ? (currDisplay.adapterVendor == WinApi.DisplayAdapterVendor.Nvidia ? (LanguageManager.Korean ? "디지털\n바이브런스" : "Digital\nVibrance") : (LanguageManager.Korean ? "채도" : "Saturation"))
                : (LanguageManager.Korean ? "채도 (미지원 GPU)" : "Saturation (unsupported)");

            UpdateColorChannelButtonStyles();
            disableChangeFunc = false;
        }

        private void buttonRed_Click(object sender, EventArgs e)
        {
            if (currDisplay == null) return;
            disableChangeFunc = true;
            ClearAllColorFlags();
            redColor = true;

            decimal valGamma = Math.Max(textBoxGamma.Minimum, Math.Min(textBoxGamma.Maximum, (decimal)currDisplay.rGamma));
            decimal valContrast = Math.Max(textBoxContrast.Minimum, Math.Min(textBoxContrast.Maximum, (decimal)currDisplay.rContrast));
            decimal valBright = Math.Max(textBoxBrightness.Minimum, Math.Min(textBoxBrightness.Maximum, (decimal)currDisplay.rBright));

            textBoxGamma.Value = valGamma;
            textBoxContrast.Value = valContrast;
            textBoxBrightness.Value = valBright;

            trackBarGamma.Value = Clamp((int)(valGamma * 100m), trackBarGamma.Minimum, trackBarGamma.Maximum);
            trackBarContrast.Value = Clamp((int)(valContrast * 100m), trackBarContrast.Minimum, trackBarContrast.Maximum);
            trackBarBrightness.Value = Clamp((int)(valBright * 100m), trackBarBrightness.Minimum, trackBarBrightness.Maximum);

            UpdateColorChannelButtonStyles();
            disableChangeFunc = false;
        }

        private void buttonGreen_Click(object sender, EventArgs e)
        {
            if (currDisplay == null) return;
            disableChangeFunc = true;
            ClearAllColorFlags();
            greenColor = true;

            decimal valGamma = Math.Max(textBoxGamma.Minimum, Math.Min(textBoxGamma.Maximum, (decimal)currDisplay.gGamma));
            decimal valContrast = Math.Max(textBoxContrast.Minimum, Math.Min(textBoxContrast.Maximum, (decimal)currDisplay.gContrast));
            decimal valBright = Math.Max(textBoxBrightness.Minimum, Math.Min(textBoxBrightness.Maximum, (decimal)currDisplay.gBright));

            textBoxGamma.Value = valGamma;
            textBoxContrast.Value = valContrast;
            textBoxBrightness.Value = valBright;

            trackBarGamma.Value = Clamp((int)(valGamma * 100m), trackBarGamma.Minimum, trackBarGamma.Maximum);
            trackBarContrast.Value = Clamp((int)(valContrast * 100m), trackBarContrast.Minimum, trackBarContrast.Maximum);
            trackBarBrightness.Value = Clamp((int)(valBright * 100m), trackBarBrightness.Minimum, trackBarBrightness.Maximum);

            UpdateColorChannelButtonStyles();
            disableChangeFunc = false;
        }

        private void buttonBlue_Click(object sender, EventArgs e)
        {
            if (currDisplay == null) return;
            disableChangeFunc = true;
            ClearAllColorFlags();
            blueColor = true;

            decimal valGamma = Math.Max(textBoxGamma.Minimum, Math.Min(textBoxGamma.Maximum, (decimal)currDisplay.bGamma));
            decimal valContrast = Math.Max(textBoxContrast.Minimum, Math.Min(textBoxContrast.Maximum, (decimal)currDisplay.bContrast));
            decimal valBright = Math.Max(textBoxBrightness.Minimum, Math.Min(textBoxBrightness.Maximum, (decimal)currDisplay.bBright));

            textBoxGamma.Value = valGamma;
            textBoxContrast.Value = valContrast;
            textBoxBrightness.Value = valBright;

            trackBarGamma.Value = Clamp((int)(valGamma * 100m), trackBarGamma.Minimum, trackBarGamma.Maximum);
            trackBarContrast.Value = Clamp((int)(valContrast * 100m), trackBarContrast.Minimum, trackBarContrast.Maximum);
            trackBarBrightness.Value = Clamp((int)(valBright * 100m), trackBarBrightness.Minimum, trackBarBrightness.Maximum);

            UpdateColorChannelButtonStyles();
            disableChangeFunc = false;
        }

        private void checkBoxExContrast_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxExContrast.Checked)
            {
                trackBarContrast.Maximum = 1000;
                textBoxContrast.Maximum = 10.00m;
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