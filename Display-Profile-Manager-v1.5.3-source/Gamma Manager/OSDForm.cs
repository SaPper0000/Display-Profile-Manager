using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Gamma_Manager
{
    public class OSDForm : Form
    {
        private System.Windows.Forms.Timer fadeTimer;
        private double currentOpacity = 1.0;
        private static OSDForm _instance = null;
        private static CancellationTokenSource _fadeCts = null;
        private string _message = "";
        private Color _textColor = Color.LimeGreen;
        private int _fontSize = 32;

        // [최적화 1] 폰트를 재사용하기 위한 캐싱 변수 선언
        private Font _cachedFont = null;

        private StringAlignment _textAlign = StringAlignment.Center;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x20 | 0x08000000 | 0x80;
                return cp;
            }
        }

        public OSDForm()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(-10000, -10000);
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.BackColor = Color.Black;
            this.TransparencyKey = Color.Black;
            this.Opacity = 1.0;
            this.Size = new Size(1000, 120);
            this.DoubleBuffered = true;

            fadeTimer = new System.Windows.Forms.Timer();
            fadeTimer.Interval = 30;
            fadeTimer.Tick += FadeTimer_Tick;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(Color.Black);

            if (string.IsNullOrEmpty(_message)) return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.CompositingQuality = CompositingQuality.HighQuality;

            Rectangle rect = new Rectangle(10, 0, this.Width - 20, this.Height);
            using (GraphicsPath path = new GraphicsPath())
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = _textAlign;
                sf.LineAlignment = StringAlignment.Near;

                // [최적화 2] 폰트가 없거나 크기가 달라졌을 때만 새로 생성, 평소에는 기존 폰트 재사용
                if (_cachedFont == null || (int)_cachedFont.Size != _fontSize)
                {
                    _cachedFont?.Dispose();
                    _cachedFont = new Font("Segoe UI", _fontSize, FontStyle.Bold);
                }
                path.AddString(_message, _cachedFont.FontFamily, (int)_cachedFont.Style, _cachedFont.Size, rect, sf);

                using (var pen = new Pen(Color.Black, 4))
                using (var brush = new SolidBrush(_textColor))
                {
                    e.Graphics.DrawPath(pen, path);
                    e.Graphics.FillPath(brush, path);
                }
            }
        }

        public static void ShowMessage(string displayLink, string message)
        {
            try
            {
                if (Application.OpenForms.Count > 0)
                {
                    Form mainForm = Application.OpenForms[0];
                    if (mainForm != null && !mainForm.IsDisposed && mainForm.IsHandleCreated)
                    {
                        if (mainForm.InvokeRequired)
                        {
                            mainForm.BeginInvoke(new Action(() => ShowMessage(displayLink, message)));
                            return;
                        }
                    }
                }

                ShowMessageInternal(displayLink, message);
            }
            catch (Exception ex)
            {
                Logger.Warn("OSD Invoke Error: " + ex.Message);
            }
        }

        private static void ShowMessageInternal(string displayLink, string message)
        {
            try
            {
                string enabledStr = IniFile.Shared.Read("OsdEnabled", "Settings");
                bool isEnabled = string.IsNullOrEmpty(enabledStr) || enabledStr.Equals("True", StringComparison.OrdinalIgnoreCase) || enabledStr == "1";

                if (!isEnabled) return;

                if (_instance == null || _instance.IsDisposed || !_instance.IsHandleCreated)
                {
                    _instance?.Dispose();
                    _instance = new OSDForm();
                }

                _instance.UpdateOSD(displayLink, message);
            }
            catch (Exception ex)
            {
                Logger.Warn("OSD ShowMessageInternal Error: " + ex.Message);
            }
        }

        private void UpdateOSD(string displayLink, string message)
        {
            int colonIndex = message.IndexOf(": ");
            if (colonIndex > 0)
            {
                int spaceIndex = message.IndexOf(' ');
                if (spaceIndex >= 0 && spaceIndex < colonIndex)
                {
                    string iconPart = message.Substring(0, spaceIndex + 1);
                    string profilePart = message.Substring(colonIndex + 2);
                    message = iconPart + profilePart;
                }
            }

            // 이전 타이머 및 지연 대기 취소
            this.fadeTimer.Stop();
            if (_fadeCts != null)
            {
                _fadeCts.Cancel();
                _fadeCts.Dispose();
                _fadeCts = null;
            }

            this.Opacity = 1.0;
            this.currentOpacity = 1.0;
            this._message = message;

            string colorName = IniFile.Shared.Read("OsdColor", "Settings");
            string posName = IniFile.Shared.Read("OsdPosition", "Settings");
            string fontSzStr = IniFile.Shared.Read("OsdFontSize", "Settings");
            string durStr = IniFile.Shared.Read("OsdDuration", "Settings");

            int durationMs = 1500;
            if (int.TryParse(durStr, out int parsedMs)) durationMs = Math.Max(1000, Math.Min(3000, parsedMs));

            if (int.TryParse(fontSzStr, out int parsedSz))
                _fontSize = Math.Max(20, Math.Min(48, parsedSz));
            else
                _fontSize = 32;

            switch (colorName?.ToLowerInvariant())
            {
                case "yellow": _textColor = Color.Yellow; break;
                case "skyblue": _textColor = Color.DeepSkyBlue; break;
                case "white": _textColor = Color.White; break;
                case "orange": _textColor = Color.Orange; break;
                case "red": _textColor = Color.Red; break;
                default: _textColor = Color.LimeGreen; break;
            }

            Screen targetScreen = Screen.PrimaryScreen;
            if (!string.IsNullOrEmpty(displayLink))
            {
                foreach (var s in Screen.AllScreens)
                {
                    if (s.DeviceName.Equals(displayLink, StringComparison.OrdinalIgnoreCase))
                    {
                        targetScreen = s;
                        break;
                    }
                }
            }

            int x = targetScreen.Bounds.X;
            int y = targetScreen.Bounds.Y + 50;

            switch (posName?.ToLowerInvariant())
            {
                case "topleft":
                    x = targetScreen.Bounds.X + 50;
                    _textAlign = StringAlignment.Near;
                    break;
                case "topright":
                    x = targetScreen.Bounds.X + targetScreen.Bounds.Width - this.Width - 50;
                    _textAlign = StringAlignment.Far;
                    break;
                case "topcenter":
                default:
                    x = targetScreen.Bounds.X + (targetScreen.Bounds.Width - this.Width) / 2;
                    _textAlign = StringAlignment.Center;
                    break;
            }

            this.Location = new Point(x, y);

            if (!this.Visible) this.Show();

            this.Invalidate(true);

            // 신규 CancellationToken 생성 및 안전한 페이드 대기
            _fadeCts = new CancellationTokenSource();
            var token = _fadeCts.Token;

            Task.Delay(durationMs, token).ContinueWith(t =>
            {
                if (!token.IsCancellationRequested && !this.IsDisposed && this.IsHandleCreated)
                {
                    this.BeginInvoke((Action)(() =>
                    {
                        if (!token.IsCancellationRequested && !this.IsDisposed)
                            this.fadeTimer.Start();
                    }));
                }
            }, token);
        }

        private void FadeTimer_Tick(object sender, EventArgs e)
        {
            currentOpacity -= 0.05;
            if (currentOpacity <= 0)
            {
                fadeTimer.Stop();
                this.Hide();
            }
            else
            {
                this.Opacity = currentOpacity;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // [최적화 3] 프로그램 종료 시 캐싱된 폰트 메모리 완전 해제
                _cachedFont?.Dispose();
                _cachedFont = null;

                if (fadeTimer != null)
                {
                    fadeTimer.Stop();
                    fadeTimer.Dispose();
                    fadeTimer = null;
                }
                if (_fadeCts != null)
                {
                    _fadeCts.Cancel();
                    _fadeCts.Dispose();
                    _fadeCts = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}