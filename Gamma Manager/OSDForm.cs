using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        private static readonly object _syncLock = new object();

        private string _message = "";
        private Color _textColor = Color.LimeGreen;
        private int _fontSize = 32;
        private StringAlignment _textAlign = StringAlignment.Center;

        // GDI+ 렌더링 최적화 캐시
        private Font _cachedFont = null;
        private GraphicsPath _cachedPath = null;
        private bool _isPathDirty = true;

        // TransparencyKey(0,0,0)에 의해 잘려나가지 않는 외곽선 색상
        private static readonly Color OutlineColor = Color.FromArgb(16, 16, 16);

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                // WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
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
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // 메시지/설정 변경 시에만 GraphicsPath를 재구성하고, 페이드 틱 중에는 캐시 재사용
            if (_isPathDirty || _cachedPath == null)
            {
                _cachedPath?.Dispose();
                _cachedPath = new GraphicsPath();

                if (_cachedFont == null || Math.Abs(_cachedFont.Size - _fontSize) > 0.001f)
                {
                    _cachedFont?.Dispose();
                    _cachedFont = new Font("Segoe UI", _fontSize, FontStyle.Bold);
                }

                Rectangle rect = new Rectangle(10, 0, this.Width - 20, this.Height);
                using (StringFormat sf = new StringFormat())
                {
                    sf.Alignment = _textAlign;
                    sf.LineAlignment = StringAlignment.Near;
                    _cachedPath.AddString(_message, _cachedFont.FontFamily, (int)_cachedFont.Style, _cachedFont.Size, rect, sf);
                }
                _isPathDirty = false;
            }

            using (var pen = new Pen(OutlineColor, 4) { LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(_textColor))
            {
                e.Graphics.DrawPath(pen, _cachedPath);
                e.Graphics.FillPath(brush, _cachedPath);
            }
        }

        public static void ShowMessage(string displayLink, string message, bool persistent = false)
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
                            mainForm.BeginInvoke(new Action(() => ShowMessage(displayLink, message, persistent)));
                            return;
                        }
                    }
                }

                ShowMessageInternal(displayLink, message, persistent);
            }
            catch (Exception ex)
            {
                Logger.Warn("OSD ShowMessage Error: " + ex.Message);
            }
        }

        public static void HideOSD()
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
                            mainForm.BeginInvoke(new Action(HideOSD));
                            return;
                        }
                    }
                }

                lock (_syncLock)
                {
                    if (_instance != null && !_instance.IsDisposed && _instance.Visible)
                    {
                        _instance.fadeTimer.Stop();
                        if (_fadeCts != null)
                        {
                            _fadeCts.Cancel();
                            _fadeCts.Dispose();
                            _fadeCts = null;
                        }
                        _instance.Hide();
                    }
                }
            }
            catch { }
        }

        private static void ShowMessageInternal(string displayLink, string message, bool persistent = false)
        {
            lock (_syncLock)
            {
                try
                {
                    bool isVolumeMsg = message != null && message.StartsWith("🔊");
                    bool isEnabled;

                    if (isVolumeMsg)
                    {
                        string duckOsdStr = IniFile.Shared.Read("VolumeDuckOSD", "Settings");
                        isEnabled = string.IsNullOrEmpty(duckOsdStr) ||
                                    duckOsdStr.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                                    duckOsdStr == "1";
                    }
                    else
                    {
                        string enabledStr = IniFile.Shared.Read("OsdEnabled", "Settings");
                        isEnabled = string.IsNullOrEmpty(enabledStr) ||
                                    enabledStr.Equals("True", StringComparison.OrdinalIgnoreCase) ||
                                    enabledStr == "1";
                    }

                    if (!isEnabled) return;

                    if (_instance == null || _instance.IsDisposed)
                    {
                        _instance = new OSDForm();
                    }

                    _instance.UpdateOSD(displayLink, message, persistent);
                }
                catch (Exception ex)
                {
                    Logger.Warn("OSD ShowMessageInternal Error: " + ex.Message);
                }
            }
        }

        private void UpdateOSD(string displayLink, string message, bool persistent = false)
        {
            if (!message.StartsWith("🔊"))
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
            }

            fadeTimer.Stop();
            if (_fadeCts != null)
            {
                _fadeCts.Cancel();
                _fadeCts.Dispose();
                _fadeCts = null;
            }

            currentOpacity = 1.0;
            this.Opacity = 1.0;

            if (this._message != message)
            {
                this._message = message;
                this._isPathDirty = true;
            }

            bool isVolumeMsg = message != null && message.StartsWith("🔊");

            string colorName;
            string posName;
            string fontSzStr;
            string durStr;

            if (isVolumeMsg)
            {
                colorName = IniFile.Shared.Read("VolumeDuckOsdColor", "Settings");
                if (string.IsNullOrEmpty(colorName)) colorName = IniFile.Shared.Read("OsdColor", "Settings");

                posName = IniFile.Shared.Read("VolumeDuckOsdPosition", "Settings");
                if (string.IsNullOrEmpty(posName)) posName = IniFile.Shared.Read("OsdPosition", "Settings");

                fontSzStr = IniFile.Shared.Read("VolumeDuckOsdFontSize", "Settings");
                if (string.IsNullOrEmpty(fontSzStr)) fontSzStr = IniFile.Shared.Read("OsdFontSize", "Settings");

                durStr = IniFile.Shared.Read("VolumeDuckOsdDuration", "Settings");
                if (string.IsNullOrEmpty(durStr)) durStr = IniFile.Shared.Read("OsdDuration", "Settings");
            }
            else
            {
                colorName = IniFile.Shared.Read("OsdColor", "Settings");
                posName = IniFile.Shared.Read("OsdPosition", "Settings");
                fontSzStr = IniFile.Shared.Read("OsdFontSize", "Settings");
                durStr = IniFile.Shared.Read("OsdDuration", "Settings");
            }

            int durationMs = 1500;
            if (int.TryParse(durStr, out int parsedMs))
                durationMs = Math.Max(500, Math.Min(10000, parsedMs));

            int newFontSize = 32;
            if (int.TryParse(fontSzStr, out int parsedSz))
                newFontSize = Math.Max(20, Math.Min(48, parsedSz));

            if (_fontSize != newFontSize)
            {
                _fontSize = newFontSize;
                _isPathDirty = true;
            }

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
            else
            {
                // displayLink가 null인 경우: 현재 포그라운드 활성 창이 위치한 화면을 기본 타깃으로 선택
                IntPtr fgHwnd = WinApi.GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero)
                {
                    try
                    {
                        targetScreen = Screen.FromHandle(fgHwnd) ?? Screen.PrimaryScreen;
                    }
                    catch
                    {
                        targetScreen = Screen.PrimaryScreen;
                    }
                }
            }

            StringAlignment newAlign;
            int x;
            int y = targetScreen.Bounds.Y + 50;

            switch (posName?.ToLowerInvariant())
            {
                case "topleft":
                    x = targetScreen.Bounds.X + 50;
                    newAlign = StringAlignment.Near;
                    break;
                case "topright":
                    x = targetScreen.Bounds.X + targetScreen.Bounds.Width - this.Width - 50;
                    newAlign = StringAlignment.Far;
                    break;
                case "topcenter":
                default:
                    x = targetScreen.Bounds.X + (targetScreen.Bounds.Width - this.Width) / 2;
                    newAlign = StringAlignment.Center;
                    break;
            }

            if (_textAlign != newAlign)
            {
                _textAlign = newAlign;
                _isPathDirty = true;
            }

            this.Location = new Point(x, y);

            if (!this.Visible) this.Show();

            this.Invalidate();

            if (!persistent)
            {
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
                fadeTimer?.Dispose();
                _cachedFont?.Dispose();
                _cachedPath?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}