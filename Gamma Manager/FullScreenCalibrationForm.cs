using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Gamma_Manager
{
    /// <summary>
    /// 모니터 전체 화면에서 0% ~ 20% 극암부 계조 및 적군 실루엣 분리도를 실측하는 전문가용 캘리브레이션 뷰어
    /// </summary>
    internal sealed class FullScreenCalibrationForm : Form
    {
        private readonly Display.DisplayInfo _display;
        private readonly bool _ko;

        public FullScreenCalibrationForm(Display.DisplayInfo display)
        {
            _display = display;
            _ko = LanguageManager.Korean;

            DoubleBuffered = true;
            SetStyle(ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint, true);

            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            KeyPreview = true;

            // 대상 모니터 스크린 영역으로 폼 배치
            Screen targetScreen = null;
            if (display != null && !string.IsNullOrEmpty(display.displayLink))
            {
                foreach (var scr in Screen.AllScreens)
                {
                    if (scr.DeviceName.Equals(display.displayLink, StringComparison.OrdinalIgnoreCase))
                    {
                        targetScreen = scr;
                        break;
                    }
                }
            }

            if (targetScreen == null && display != null && display.numDisplay >= 0 && display.numDisplay < Screen.AllScreens.Length)
            {
                targetScreen = Screen.AllScreens[display.numDisplay];
            }

            if (targetScreen == null) targetScreen = Screen.PrimaryScreen;

            StartPosition = FormStartPosition.Manual;
            Bounds = targetScreen.Bounds;

            KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
                {
                    Close();
                }
            };

            Click += (s, e) => Close();
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = ClientSize.Width;
            int h = ClientSize.Height;

            // 1. 상단 안내 헤더
            using (var titleFont = new Font("Segoe UI", 20f, FontStyle.Bold))
            using (var subFont = new Font("Segoe UI", 11f))
            using (var tipFont = new Font("Segoe UI", 10f))
            using (var brushTitle = new SolidBrush(Color.FromArgb(240, 240, 240)))
            using (var brushSub = new SolidBrush(Color.FromArgb(170, 175, 185)))
            using (var brushTip = new SolidBrush(Color.FromArgb(255, 200, 60)))
            {
                string title = _ko
                    ? "🌑 블랙 이퀄라이저 전체화면 캘리브레이션"
                    : "🌑 Black Equalizer Full-Screen Calibration";
                string sub = (_ko ? "대상 모니터: " : "Target Display: ") + (_display?.displayName ?? "Unknown");
                string tip = _ko
                    ? "※ 1% ~ 3% 패치가 배경(0%)과 명확히 구분되는지 확인하세요.  [ESC 키 또는 마우스 클릭 시 닫기]"
                    : "※ Ensure 1% - 3% steps are clearly distinguished from pure black (0%).  [Press ESC or Click to Close]";

                var titleSize = g.MeasureString(title, titleFont);
                var subSize = g.MeasureString(sub, subFont);
                var tipSize = g.MeasureString(tip, tipFont);

                g.DrawString(title, titleFont, brushTitle, (w - titleSize.Width) / 2, 40);
                g.DrawString(sub, subFont, brushSub, (w - subSize.Width) / 2, 85);
                g.DrawString(tip, tipFont, brushTip, (w - tipSize.Width) / 2, 115);
            }

            // 2. 계조 단계 패치 (0% ~ 20%)
            int[] percentages = { 0, 1, 2, 3, 4, 5, 7, 10, 15, 20 };
            int patchCount = percentages.Length;
            int patchWidth = Math.Min(120, (w - 100) / patchCount);
            int patchHeight = 160;
            int startX = (w - (patchWidth * patchCount + (patchCount - 1) * 10)) / 2;
            int startY = h / 2 - 120;

            using (var patchFont = new Font("Segoe UI", 10.5f, FontStyle.Bold))
            using (var rgbFont = new Font("Segoe UI", 8f))
            using (var borderPen = new Pen(Color.FromArgb(30, 32, 40), 1f))
            {
                for (int i = 0; i < patchCount; i++)
                {
                    int pct = percentages[i];
                    int rgb = (int)Math.Round(pct * 255.0 / 100.0);
                    rgb = Math.Max(0, Math.Min(255, rgb));
                    Color patchColor = Color.FromArgb(rgb, rgb, rgb);

                    int px = startX + i * (patchWidth + 10);
                    var rect = new Rectangle(px, startY, patchWidth, patchHeight);

                    using (var b = new SolidBrush(patchColor))
                    {
                        g.FillRectangle(b, rect);
                    }
                    g.DrawRectangle(borderPen, rect);

                    // 패치 내부 텍스트 (텍스트 색상은 배경보다 조금 더 밝게)
                    int textRgb = Math.Min(255, Math.Max(rgb + 60, 90));
                    using (var tb = new SolidBrush(Color.FromArgb(textRgb, textRgb, textRgb)))
                    {
                        string pctStr = $"{pct}%";
                        string rgbStr = $"RGB {rgb}";

                        var pSize = g.MeasureString(pctStr, patchFont);
                        var rSize = g.MeasureString(rgbStr, rgbFont);

                        g.DrawString(pctStr, patchFont, tb, px + (patchWidth - pSize.Width) / 2, startY + 45);
                        g.DrawString(rgbStr, rgbFont, tb, px + (patchWidth - rSize.Width) / 2, startY + 75);

                        if (pct == 0)
                        {
                            string pureStr = "Pure Black";
                            var pureSize = g.MeasureString(pureStr, rgbFont);
                            g.DrawString(pureStr, rgbFont, tb, px + (patchWidth - pureSize.Width) / 2, startY + 105);
                        }
                    }
                }
            }

            // 3. 인게임 실전 적군 실루엣 식별 테스트 패널
            int simWidth = Math.Min(600, w - 80);
            int simHeight = 120;
            int simX = (w - simWidth) / 2;
            int simY = startY + patchHeight + 40;

            var simRect = new Rectangle(simX, simY, simWidth, simHeight);
            using (var simBg = new SolidBrush(Color.FromArgb(6, 6, 8))) // 어두운 건물 내부 / 그늘 (2.3%)
            using (var simPen = new Pen(Color.FromArgb(40, 44, 55), 1f))
            {
                g.FillRectangle(simBg, simRect);
                g.DrawRectangle(simPen, simRect);

                using (var labelFont = new Font("Segoe UI", 9.5f, FontStyle.Bold))
                using (var descFont = new Font("Segoe UI", 8.5f))
                using (var lb = new SolidBrush(Color.FromArgb(180, 185, 195)))
                {
                    string label = _ko
                        ? "🎯 인게임 실전 암부 윤곽 분리 테스트 (In-Game Silhouette Test)"
                        : "🎯 In-Game Shadow Silhouette Separation Test";
                    string desc = _ko
                        ? "배경(RGB 6) 속에 숨겨진 실루엣 3개(RGB 11, 16, 22)가 선명하게 분리되어 보이는지 확인하세요."
                        : "Verify that all 3 silhouette shapes (RGB 11, 16, 22) are clearly distinguishable from the dark background (RGB 6).";

                    g.DrawString(label, labelFont, lb, simX + 15, simY + 12);
                    g.DrawString(desc, descFont, Brushes.Gray, simX + 15, simY + 34);
                }

                // 3개의 식별 타깃 박스
                int[] targets = { 11, 16, 22 };
                string[] tags = { "Deep (RGB 11)", "Mid (RGB 16)", "Light (RGB 22)" };
                int tWidth = 110;
                int tHeight = 44;
                int tSpacing = (simWidth - 30 - tWidth * 3) / 2;

                for (int t = 0; t < 3; t++)
                {
                    int tx = simX + 15 + t * (tWidth + tSpacing);
                    int ty = simY + 62;
                    var tRect = new Rectangle(tx, ty, tWidth, tHeight);

                    using (var tb = new SolidBrush(Color.FromArgb(targets[t], targets[t], targets[t])))
                    using (var tPen = new Pen(Color.FromArgb(targets[t] + 15, targets[t] + 15, targets[t] + 15)))
                    {
                        g.FillRectangle(tb, tRect);
                        g.DrawRectangle(tPen, tRect);
                    }

                    using (var textFont = new Font("Segoe UI", 8.2f))
                    using (var textB = new SolidBrush(Color.FromArgb(150, 155, 165)))
                    {
                        var tSize = g.MeasureString(tags[t], textFont);
                        g.DrawString(tags[t], textFont, textB, tx + (tWidth - tSize.Width) / 2, ty + (tHeight - tSize.Height) / 2);
                    }
                }
            }
        }
    }
}
