#pragma warning disable CS8981
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace BetterFG.Installer;

public sealed partial class installerform
{
    private sealed class fadeform : Form
    {
        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }
    }

    private sealed class bfgpanel : Panel
    {
        public Image? BackTexture { get; set; }

        public bfgpanel()
        {
            DoubleBuffered = true;
            BackColor = Color.Black;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.Black);
            if (BackTexture != null)
                DrawSliced(e.Graphics, ClientRectangle, BackTexture, 6);
        }
    }

    private enum bfgstyle
    {
        Pink,
        Blue,
        Yellow
    }

    private enum bfgop
    {
        Install,
        Modify,
        Uninstall
    }

    private sealed class bfgbutton : Button
    {
        private bool hovering;
        private bool pressing;
        public bfgstyle Style { get; set; } = bfgstyle.Pink;
        public Image? LeftIcon { get; set; }

        public bfgbutton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Black;
            ForeColor = Color.White;
            Font = new Font("Arial", 7.5f, FontStyle.Bold);
            Padding = new Padding(0);
            TabStop = false;
            MouseEnter += (_, _) => { hovering = true; Invalidate(); };
            MouseLeave += (_, _) => { hovering = false; pressing = false; Invalidate(); };
            MouseDown += (_, _) => { pressing = true; Invalidate(); };
            MouseUp += (_, _) => { pressing = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.Black);

            var shineTex = Style switch
            {
                bfgstyle.Blue => blueButtonShineTex,
                bfgstyle.Yellow => yellowButtonShineTex,
                _ => buttonShineTex
            };
            if (shineTex != null)
            {
                var oldInterpolation = g.InterpolationMode;
                var oldPixelOffset = g.PixelOffsetMode;
                var oldCompositing = g.CompositingQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                var dest = new Rectangle(0, 0, Width, Height);
                var src = new Rectangle(0, 0, shineTex.Width, shineTex.Height);
                g.DrawImage(shineTex, dest, src, GraphicsUnit.Pixel);

                if (!hovering)
                {
                    using var darkBrush = new SolidBrush(Color.FromArgb(26, 0, 0, 0));
                    g.FillRectangle(darkBrush, dest);
                }

                g.InterpolationMode = oldInterpolation;
                g.PixelOffsetMode = oldPixelOffset;
                g.CompositingQuality = oldCompositing;
            }

            var leftAligned = TextAlign == ContentAlignment.MiddleLeft;
            var leftPad = 0;
            if (leftAligned)
            {
                const int iconBox = 28;
                var iconX = 16;
                if (LeftIcon != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    g.DrawImage(LeftIcon, new Rectangle(iconX, (Height - iconBox) / 2, iconBox, iconBox));
                }
                leftPad = iconX + iconBox + 10;
            }

            using var textBrush = new SolidBrush(Enabled ? Color.White : Color.FromArgb(120, 120, 120));
            using var sf = new StringFormat
            {
                Alignment = leftAligned ? StringAlignment.Near : StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            var textRect = new RectangleF(leftPad, pressing ? 1 : 0, Width - 1 - leftPad, Height - 1);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            g.DrawString(Text, Font, textBrush, textRect, sf);
        }
    }
}
#pragma warning restore CS8981
