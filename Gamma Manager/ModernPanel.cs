using System.Drawing;
using System.Windows.Forms;

namespace Gamma_Manager
{
    /// <summary>
    /// Lightweight card container used by the main window. It owns its border
    /// rendering instead of relying on the old designer Panel/BorderStyle setup.
    /// </summary>
    internal sealed class ModernPanel : Panel
    {
        public Color BorderColor { get; set; } = ThemeManager.DarkBorder;
        public int BorderThickness { get; set; } = 1;
        public int CornerRadius { get; set; } = 0;

        public ModernPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (BorderThickness <= 0) return;

            using (Pen pen = new Pen(BorderColor, BorderThickness))
            {
                Rectangle rect = new Rectangle(
                    BorderThickness / 2,
                    BorderThickness / 2,
                    Width - BorderThickness,
                    Height - BorderThickness);

                if (CornerRadius > 0)
                {
                    using (var path = RoundedRectangle(rect, CornerRadius))
                        e.Graphics.DrawPath(pen, path);
                }
                else
                {
                    e.Graphics.DrawRectangle(pen, rect);
                }
            }
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRectangle(Rectangle rect, int radius)
        {
            int d = radius * 2;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
