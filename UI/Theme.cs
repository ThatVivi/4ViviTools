// Theme.cs — dark, flat, rounded theme for 4rVivi (WinForms).
// ApplyDark() recolours an entire control tree (incl. embedded feature forms) so text stays
// readable on a dark background. RoundCorners() softens edges. MIT.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace _4rVivi.UI
{
    public static class Theme
    {
        public static readonly Color Bg        = Color.FromArgb(20, 21, 26);
        public static readonly Color Surface   = Color.FromArgb(28, 30, 38);
        public static readonly Color Surface2  = Color.FromArgb(40, 43, 54);
        public static readonly Color Border    = Color.FromArgb(60, 64, 78);
        public static readonly Color Text       = Color.FromArgb(234, 236, 242);
        public static readonly Color TextMuted  = Color.FromArgb(160, 165, 180);
        public static readonly Color Accent      = Color.FromArgb(124, 116, 240);
        public static readonly Color Ok          = Color.FromArgb(58, 196, 142);
        public static readonly Color Danger       = Color.FromArgb(232, 86, 86);
        public static readonly Color Info          = Color.FromArgb(74, 152, 232);

        // Recolour an entire control tree to dark + readable. Call AFTER all controls/forms exist.
        public static void ApplyDark(Control root)
        {
            ColorOne(root);
            foreach (Control c in root.Controls) ApplyDark(c);
        }

        private static void ColorOne(Control c)
        {
            switch (c)
            {
                case Button b:
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.BorderColor = Border;
                    b.FlatAppearance.BorderSize = 1;
                    b.FlatAppearance.MouseOverBackColor = Surface2;
                    b.BackColor = Surface2;
                    b.ForeColor = Text;
                    b.UseVisualStyleBackColor = false;
                    b.Cursor = Cursors.Hand;
                    RoundCorners(b, 8);
                    break;
                case TextBox _:
                case ComboBox _:
                case NumericUpDown _:
                case ListBox _:
                    c.BackColor = Surface2;
                    c.ForeColor = Text;
                    break;
                case ListView lv:
                    lv.BackColor = Surface; lv.ForeColor = Text; lv.BorderStyle = BorderStyle.None;
                    break;
                case DataGridView dgv:
                    dgv.BackgroundColor = Surface;
                    dgv.GridColor = Border;
                    dgv.EnableHeadersVisualStyles = false;
                    dgv.DefaultCellStyle.BackColor = Surface;
                    dgv.DefaultCellStyle.ForeColor = Text;
                    dgv.ColumnHeadersDefaultCellStyle.BackColor = Surface2;
                    dgv.ColumnHeadersDefaultCellStyle.ForeColor = Text;
                    break;
                case Label _:
                case CheckBox _:
                case RadioButton _:
                    c.ForeColor = Text;
                    c.BackColor = Color.Transparent;
                    break;
                case GroupBox _:
                    c.ForeColor = Text; c.BackColor = Bg;
                    break;
                case Panel _:
                case TabControl _:
                case UserControl _:
                case Form _:
                    c.BackColor = c is Form ? Bg : Surface;
                    c.ForeColor = Text;
                    break;
                default:
                    // PictureBox / TrackBar / ProgressBar etc — just darken the backing.
                    if (!(c is PictureBox)) c.BackColor = Surface;
                    c.ForeColor = Text;
                    break;
            }
        }

        // Soft rounded corners for a control (re-applied on resize).
        public static void RoundCorners(Control c, int radius)
        {
            void Apply()
            {
                if (c.Width <= 0 || c.Height <= 0) return;
                int d = radius * 2;
                var p = new GraphicsPath();
                p.AddArc(0, 0, d, d, 180, 90);
                p.AddArc(c.Width - d - 1, 0, d, d, 270, 90);
                p.AddArc(c.Width - d - 1, c.Height - d - 1, d, d, 0, 90);
                p.AddArc(0, c.Height - d - 1, d, d, 90, 90);
                p.CloseFigure();
                c.Region = new Region(p);
            }
            Apply();
            c.Resize -= ResizeHandler;
            c.Resize += ResizeHandler;
            void ResizeHandler(object s, EventArgs e) { Apply(); }
        }


        // Mirror the whole UI to the right (RTL) for an Arabic feel.
        public static void ApplyRtl(Control root)
        {
            root.RightToLeft = RightToLeft.Yes;
            var form = root as Form; if (form != null) form.RightToLeftLayout = true;
            var tc = root as TabControl; if (tc != null) tc.RightToLeftLayout = true;
            var lv = root as ListView; if (lv != null) lv.RightToLeftLayout = true;
            foreach (Control c in root.Controls) ApplyRtl(c);
        }

        // Flat dark HP/SP bar.
        public sealed class StatBar : Control
        {
            public int Value = 100;
            public Color Fill = Danger;
            public string Caption = "HP";
            public StatBar() { Height = 18; DoubleBuffered = true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Surface2);
                int w = (int)(Width * (Value / 100.0));
                using (var br = new SolidBrush(Fill)) g.FillRectangle(br, 0, 0, w, Height);
                using (var br = new SolidBrush(Theme.Text))
                    g.DrawString(Caption + " " + Value + "%", new Font("Segoe UI", 8f), br, 6, 1);
                using (var pen = new Pen(Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    // Rounded sidebar nav button.
    public sealed class NavButton : Button
    {
        private bool _active;
        public NavButton(string text)
        {
            Text = "    " + text;
            TextAlign = ContentAlignment.MiddleLeft;
            Height = 38;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = Theme.Surface2;
            BackColor = Theme.Surface;
            ForeColor = Theme.TextMuted;
            UseVisualStyleBackColor = false;
            Font = new Font("Segoe UI", 10f);
            Cursor = Cursors.Hand;
        }
        public void SetActive(bool on)
        {
            _active = on;
            BackColor = on ? Theme.Surface2 : Theme.Surface;
            ForeColor = on ? Color.White : Theme.TextMuted;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_active)
                using (var b = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(b, 0, 6, 3, Height - 12);
        }
    }
}
