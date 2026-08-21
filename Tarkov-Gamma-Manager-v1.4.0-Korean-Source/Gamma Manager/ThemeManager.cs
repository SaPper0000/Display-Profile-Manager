using System.Drawing;
using System.Windows.Forms;

namespace Gamma_Manager
{
    internal static class ThemeManager
    {
        public static bool IsDark { get; private set; } = true;

        public static readonly Color DarkBack = Color.FromArgb(18, 20, 24);
        public static readonly Color DarkPanel = Color.FromArgb(27, 30, 36);
        public static readonly Color DarkControl = Color.FromArgb(37, 41, 48);
        public static readonly Color DarkBorder = Color.FromArgb(65, 70, 80);
        public static readonly Color DarkText = Color.FromArgb(235, 238, 242);
        public static readonly Color DarkMuted = Color.FromArgb(160, 166, 176);

        public static readonly Color LightBack = Color.FromArgb(242, 244, 247);
        public static readonly Color LightPanel = Color.White;
        public static readonly Color LightControl = Color.FromArgb(250, 250, 251);
        public static readonly Color LightBorder = Color.FromArgb(210, 214, 220);
        public static readonly Color LightText = Color.FromArgb(32, 35, 40);
        public static readonly Color LightMuted = Color.FromArgb(105, 110, 120);

        public static void SetTheme(bool dark)
        {
            IsDark = dark;
        }

        public static void Apply(Form form)
        {
            Color back = IsDark ? DarkBack : LightBack;
            Color panel = IsDark ? DarkPanel : LightPanel;
            Color control = IsDark ? DarkControl : LightControl;
            Color border = IsDark ? DarkBorder : LightBorder;
            Color text = IsDark ? DarkText : LightText;
            Color muted = IsDark ? DarkMuted : LightMuted;

            form.BackColor = back;
            form.ForeColor = text;

            ApplyControl(form, back, panel, control, border, text, muted);
        }

        private static void ApplyControl(Control root, Color back, Color panel, Color control,
            Color border, Color text, Color muted)
        {
            foreach (Control c in root.Controls)
            {
                if (c is PictureBox)
                {
                    c.BackColor = Color.Black;
                }
                else if (c is Panel || c is GroupBox)
                {
                    c.BackColor = panel;
                    c.ForeColor = text;
                }
                else if (c is Label)
                {
                    c.BackColor = Color.Transparent;
                    c.ForeColor = text;
                }
                else if (c is TextBox)
                {
                    c.BackColor = control;
                    c.ForeColor = text;
                    ((TextBox)c).BorderStyle = BorderStyle.FixedSingle;
                }
                else if (c is ComboBox)
                {
                    c.BackColor = control;
                    c.ForeColor = text;
                    ((ComboBox)c).FlatStyle = FlatStyle.Flat;
                }
                else if (c is Button)
                {
                    c.BackColor = control;
                    c.ForeColor = text;
                    ((Button)c).FlatStyle = FlatStyle.Flat;
                    ((Button)c).FlatAppearance.BorderColor = border;
                    ((Button)c).FlatAppearance.MouseOverBackColor =
                        IsDark ? Color.FromArgb(52, 57, 66) : Color.FromArgb(232, 235, 240);
                }
                else if (c is CheckBox)
                {
                    c.BackColor = panel;
                    c.ForeColor = text;
                }
                else if (c is TrackBar)
                {
                    c.BackColor = panel;
                }
                else
                {
                    c.BackColor = back;
                    c.ForeColor = text;
                }

                if (c.HasChildren)
                    ApplyControl(c, back, panel, control, border, text, muted);
            }
        }
    }
}
