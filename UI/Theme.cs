// Theme.cs — flat dark theme + sidebar-shell helper for 4rVivi (WinForms).
// Apply Theme.Apply(form) in each form's constructor after InitializeComponent().
// .NET Framework 4.x. MIT.

using System.Drawing;
using System.Windows.Forms;

namespace _4rVivi.UI
{
    public static class Theme
    {
        public static readonly Color Bg        = Color.FromArgb(18, 18, 22);
        public static readonly Color Surface    = Color.FromArgb(28, 28, 34);
        public static readonly Color Surface2   = Color.FromArgb(38, 38, 46);
        public static readonly Color Border     = Color.FromArgb(54, 54, 64);
        public static readonly Color Text       = Color.FromArgb(232, 232, 238);
        public static readonly Color TextMuted  = Color.FromArgb(150, 150, 162);
        public static readonly Color Accent     = Color.FromArgb(120, 110, 230);
        public static readonly Color Ok         = Color.FromArgb(60, 190, 140);
        public static readonly Color Danger     = Color.FromArgb(226, 75, 74);
        public static readonly Color Info       = Color.FromArgb(55, 138, 221);

        public static void Apply(Control root)
        {
            root.BackColor = Bg;
            root.ForeColor = Text;
            if (root.Font.Size < 8.5f) root.Font = new Font("Segoe UI", 9f);
            StyleChildren(root);
        }

        private static void StyleChildren(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                switch (c)
                {
                    case Button b:
                        b.FlatStyle = FlatStyle.Flat;
                        b.FlatAppearance.BorderColor = Border;
                        b.FlatAppearance.BorderSize = 1;
                        b.FlatAppearance.MouseOverBackColor = Surface2;
                        b.BackColor = Surface;
                        b.ForeColor = Text;
                        b.Cursor = Cursors.Hand;
                        break;
                    case TextBox _:
                    case ComboBox _:
                    case NumericUpDown _:
                    case ListBox _:
                        c.BackColor = Surface2;
                        c.ForeColor = Text;
                        break;
                    case ListView lv:
                        lv.BackColor = Surface;
                        lv.ForeColor = Text;
                        lv.OwnerDraw = false;
                        lv.BorderStyle = BorderStyle.None;
                        break;
                    case Label l:
                        l.ForeColor = l.Font.Bold ? Text : TextMuted;
                        l.BackColor = Color.Transparent;
                        break;
                    case Panel _:        // also covers FlowLayoutPanel / TableLayoutPanel (derive from Panel)
                    case GroupBox _:
                    case TabControl _:
                        c.BackColor = c is GroupBox ? Bg : Surface;
                        c.ForeColor = Text;
                        break;
                    case CheckBox cb:
                        cb.ForeColor = Text;
                        cb.FlatStyle = FlatStyle.Flat;
                        break;
                }
                if (c.HasChildren) StyleChildren(c);
            }
        }

        public sealed class StatBar : Control
        {
            public int Value = 100;
            public Color Fill = Danger;
            public string Caption = "HP";
            public StatBar() { Height = 18; DoubleBuffered = true; }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Surface2);
                int w = (int)(Width * (Value / 100.0));
                using (var br = new SolidBrush(Fill)) g.FillRectangle(br, 0, 0, w, Height);
                using (var br = new SolidBrush(Theme.Text))
                    g.DrawString($"{Caption} {Value}%", new Font("Segoe UI", 8f), br, 6, 1);
                using (var pen = new Pen(Border)) g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        }
    }

    public sealed class NavButton : Button
    {
        public NavButton(string text)
        {
            Text = "   " + text;
            TextAlign = ContentAlignment.MiddleLeft;
            Dock = DockStyle.Top;
            Height = 40;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.Surface;
            ForeColor = Theme.TextMuted;
            Font = new Font("Segoe UI", 10f);
            Cursor = Cursors.Hand;
        }
        public void SetActive(bool on)
        {
            BackColor = on ? Theme.Accent : Theme.Surface;
            ForeColor = on ? Color.White : Theme.TextMuted;
        }
    }
}
