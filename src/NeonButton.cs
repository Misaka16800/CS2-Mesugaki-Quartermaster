using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Cs2Roulette
{
    /// <summary>霓虹风格按钮：圆角、悬停发光、按下反馈。</summary>
    public sealed class NeonButton : Control
    {
        private bool _hover;
        private bool _down;
        public Color Accent = Color.FromArgb(236, 122, 40);
        public int CornerRadius = 10;
        public bool Ghost;
        public ContentAlignment TextAlign = ContentAlignment.MiddleCenter;

        public NeonButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);

            Color baseCol = Ghost ? Color.FromArgb(24, 29, 39) : Accent;
            if (!Enabled)
                baseCol = ColorUtil.Blend(baseCol, Color.FromArgb(44, 48, 56), 0.6);
            else if (_down)
                baseCol = ColorUtil.Darken(baseCol, 0.22);
            else if (_hover)
                baseCol = ColorUtil.Lighten(baseCol, 0.12);

            if (Enabled && (_hover || !Ghost))
            {
                int glow = _hover ? 5 : 2;
                for (int i = glow; i >= 1; i--)
                {
                    int a = _hover ? 30 - i * 4 : 14 - i * 3;
                    if (a <= 0) continue;
                    var gr = r; gr.Inflate(i, i);
                    using (var gp = Round.Path(gr, CornerRadius + i))
                    using (var b = new SolidBrush(Color.FromArgb(a, Accent)))
                        g.FillPath(b, gp);
                }
            }

            using (var gp = Round.Path(r, CornerRadius))
            {
                if (Ghost)
                {
                    using (var b = new SolidBrush(baseCol)) g.FillPath(b, gp);
                    using (var p = new Pen(Color.FromArgb(_hover ? 220 : 120, Accent), _hover ? 2f : 1.4f))
                        g.DrawPath(p, gp);
                }
                else
                {
                    using (var lg = new LinearGradientBrush(r,
                        ColorUtil.Lighten(baseCol, 0.20), ColorUtil.Darken(baseCol, 0.18), 90f))
                        g.FillPath(lg, gp);
                }
            }

            Color fg = Enabled
                ? (Ghost ? ColorUtil.Lighten(Accent, 0.45) : ColorUtil.ReadableOn(baseCol))
                : Color.FromArgb(140, 148, 160);

            TextFormatFlags flags = TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
            if (TextAlign == ContentAlignment.MiddleLeft)
            { flags |= TextFormatFlags.Left; r = new Rectangle(r.X + 12, r.Y, r.Width - 16, r.Height); }
            else
                flags |= TextFormatFlags.HorizontalCenter;

            TextRenderer.DrawText(g, Text, Font, r, fg, flags);
        }
    }
}
