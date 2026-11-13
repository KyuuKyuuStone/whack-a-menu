using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ContextMenuCleaner.Core;

namespace ContextMenuCleaner.UI
{
    public enum ButtonStyle { Primary, Secondary, Ghost, Danger, Success }

    /// <summary>
    /// Sticker-comic palette matched to the orange-cat mascot: warm cream paper,
    /// cat-orange as the primary, and the logo's deep navy for outlines / hard
    /// offset shadows. All controls are owner-drawn; no blue "enterprise" blues.
    /// </summary>
    public static class Theme
    {
        public static readonly Color Background = Color.FromArgb(0xFF, 0xF7, 0xED);
        public static readonly Color Surface = Color.White;
        public static readonly Color Sidebar = Color.FromArgb(0xFF, 0xEC, 0xD5);
        public static readonly Color Border = Color.FromArgb(0xE8, 0xD5, 0xBE);
        public static readonly Color RowBorder = Color.FromArgb(0xF0, 0xDF, 0xC9);
        public static readonly Color Text = Color.FromArgb(0x23, 0x30, 0x4E);
        public static readonly Color TextMuted = Color.FromArgb(0x7A, 0x6A, 0x5C);
        public static readonly Color TextFaint = Color.FromArgb(0x9C, 0x8A, 0x7A);

        public static readonly Color Primary = Color.FromArgb(0xF7, 0xA2, 0x3B);
        public static readonly Color PrimaryDark = Color.FromArgb(0xA3, 0x4C, 0x0A);
        public static readonly Color PrimaryLight = Color.FromArgb(0xFF, 0xB3, 0x57);
        public static readonly Color PrimarySoft = Color.FromArgb(0xFF, 0xE7, 0xC9);
        public static readonly Color Hover = Color.FromArgb(0xFF, 0xF0, 0xDC);
        public static readonly Color NavActive = Color.FromArgb(0xFF, 0xD8, 0xA6);

        public static readonly Color Success = Color.FromArgb(0x2E, 0x9E, 0x6B);
        public static readonly Color SuccessSoft = Color.FromArgb(0xDD, 0xF3, 0xE6);
        public static readonly Color Warning = Color.FromArgb(0xC9, 0x7A, 0x16);
        public static readonly Color WarningSoft = Color.FromArgb(0xFF, 0xF0, 0xD2);
        public static readonly Color Danger = Color.FromArgb(0xD9, 0x53, 0x4A);
        public static readonly Color DangerSoft = Color.FromArgb(0xFF, 0xE4, 0xDE);
        public static readonly Color NeutralSoft = Color.FromArgb(0xF4, 0xE6, 0xD3);

        // Navy ink from the mascot's sticker outline; also used for hard shadows.
        public static readonly Color Ink = Color.FromArgb(0x1B, 0x2A, 0x4A);
        public static readonly Color Outline = Color.FromArgb(0x1B, 0x2A, 0x4A);
        public static readonly Color Shadow = Color.FromArgb(0x1B, 0x2A, 0x4A);

        // Coral accent picked up from the mascot's sparkles.
        public static readonly Color Accent = Color.FromArgb(0xEE, 0x6C, 0x4D);
        public static readonly Color AccentSoft = Color.FromArgb(0xFF, 0xE1, 0xD5);

        /// <summary>
        /// Master switch for the mascot's antics: speech bubble, rotating one-liners,
        /// easter eggs and playful toast copy. Turn it off for screenshots you have
        /// to send to somebody serious.
        /// </summary>
        public static bool Quirky = true;

        private const string FontFamily = "Microsoft YaHei UI";

        /// <summary>
        /// Global UI scale. All layout constants are written in 96-DPI logical pixels;
        /// control bounds use S(x) and every owner-drawn Paint is transformed by this
        /// factor, so the whole UI grows with the Windows display scaling setting.
        /// </summary>
        public static float Scale = 1f;

        public static void SetScaleFromSystem()
        {
            int dpi = 96;
            try
            {
                uint value = NativeMethods.GetDpiForSystem();
                if (value >= 96 && value <= 480) dpi = (int)value;
            }
            catch (Exception)
            {
                try
                {
                    IntPtr hdc = NativeMethods.GetDC(IntPtr.Zero);
                    int fallback = NativeMethods.GetDeviceCaps(hdc, NativeMethods.LOGPIXELSX);
                    NativeMethods.ReleaseDC(IntPtr.Zero, hdc);
                    if (fallback >= 96) dpi = fallback;
                }
                catch (Exception) { /* keep 96 */ }
            }
            float factor = dpi / 96f;
            // A 100% DPI desktop still felt cramped; keep a comfortable floor.
            if (factor < 1.25f) factor = 1.25f;
            if (factor > 2f) factor = 2f;
            Scale = factor;
        }

        public static void OverrideScale(float value)
        {
            Scale = Math.Max(1f, Math.Min(2.5f, value));
        }

        /// <summary>Logical pixels -> physical pixels for control bounds.</summary>
        public static int S(int value)
        {
            return (int)Math.Round(value * Scale);
        }

        /// <summary>Physical control width -> logical width for owner drawing.</summary>
        public static int LW(Control control)
        {
            return Math.Max(1, (int)Math.Round(control.Width / Scale));
        }

        /// <summary>Physical control height -> logical height for owner drawing.</summary>
        public static int LH(Control control)
        {
            return Math.Max(1, (int)Math.Round(control.Height / Scale));
        }

        private static readonly Dictionary<string, Font> FontCache =
            new Dictionary<string, Font>();

        /// <summary>
        /// Fonts are created in PIXEL units, not points: a GDI+ in-memory surface may
        /// report 192 DPI even on a 96 DPI screen, which would double every point-sized
        /// font and clip all our fixed-pixel layout. Cached to avoid leaking GDI handles.
        /// </summary>
        public static Font MakeFont(float sizePt, bool bold = false)
        {
            string key = sizePt.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) +
                         (bold ? "B" : "N");
            Font cached;
            if (FontCache.TryGetValue(key, out cached)) return cached;
            float pixels = sizePt * 96f / 72f;
            Font created = new Font(FontFamily, pixels,
                                    bold ? FontStyle.Bold : FontStyle.Regular,
                                    GraphicsUnit.Pixel);
            FontCache[key] = created;
            return created;
        }

        public static GraphicsPath RoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            if (d <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRound(Graphics g, Rectangle rect, int radius,
                                     Color fill, Color border, float borderWidth = 1f)
        {
            using (GraphicsPath path = RoundedPath(rect, radius))
            using (SolidBrush brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
                if (border.A > 0 && borderWidth > 0)
                {
                    using (Pen pen = new Pen(border, borderWidth))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }
        }

        /// <summary>
        /// Sticker-style button/card: a solid offset shadow plus a bold navy outline.
        /// The caller passes a rect with a few pixels of slack so the shadow is not
        /// clipped by the control bounds.
        /// </summary>
        public static void Sticker(Graphics g, Rectangle rect, int radius,
                                   Color fill, Color shadow, int offset = 3,
                                   float outlineWidth = 1.6f)
        {
            if (shadow.A > 0 && offset > 0)
            {
                Rectangle behind = new Rectangle(rect.X + offset, rect.Y + offset,
                                                 rect.Width, rect.Height);
                FillRound(g, behind, radius, shadow, Color.Transparent);
            }
            FillRound(g, rect, radius, fill, Outline, outlineWidth);
        }

        /// <summary>Four-point comic sparkle, matching the stars in the mascot art.</summary>
        public static void DrawSparkle(Graphics g, int x, int y, int size, Color color)
        {
            float c = size / 2f;
            PointF[] points =
            {
                new PointF(x + c, y),
                new PointF(x + c * 1.3f, y + c * 0.7f),
                new PointF(x + size, y + c),
                new PointF(x + c * 1.3f, y + c * 1.3f),
                new PointF(x + c, y + size),
                new PointF(x + c * 0.7f, y + c * 1.3f),
                new PointF(x, y + c),
                new PointF(x + c * 0.7f, y + c * 0.7f)
            };
            using (GraphicsPath path = new GraphicsPath())
            using (SolidBrush brush = new SolidBrush(color))
            {
                path.AddPolygon(points);
                g.FillPath(brush, path);
            }
        }

        private static readonly System.Reflection.MethodInfo SetStyleMethod =
            typeof(Control).GetMethod("SetStyle",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        /// <summary>SetStyle is protected on Control; invoke it once per owner-drawn control.</summary>
        public static void EnableDoubleBuffer(Control control)
        {
            if (SetStyleMethod == null) return;
            ControlStyles styles = ControlStyles.UserPaint |
                                   ControlStyles.AllPaintingInWmPaint |
                                   ControlStyles.OptimizedDoubleBuffer |
                                   ControlStyles.ResizeRedraw |
                                   ControlStyles.SupportsTransparentBackColor;
            SetStyleMethod.Invoke(control, new object[] { styles, true });
        }

        public static void Prepare(Graphics g)
        {
            // One transform per paint pass turns every logical coordinate, font and
            // icon into its DPI-scaled physical size without touching each drawing call.
            g.ScaleTransform(Scale, Scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Grayscale anti-aliasing: ClearType subpixel rendering requires an opaque
            // background and drops scanlines on buffered / transparent surfaces.
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        public static Size Measure(string text, Font font)
        {
            Size size = TextRenderer.MeasureText(text, font, new Size(4096, 4096),
                                                 TextFormatFlags.NoPadding);
            // GDI+ text carries a little more padding than TextRenderer.
            return new Size(size.Width + 8, size.Height + 4);
        }

        /// <summary>
        /// All text is drawn with GDI+ (not TextRenderer/GDI): GDI subpixel rendering
        /// clips glyphs on transparent or buffered surfaces and varies across DPIs.
        /// </summary>
        public static void DrawText(Graphics g, string text, Font font, Color color,
                                    Rectangle bounds, bool right = false)
        {
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = right ? StringAlignment.Far : StringAlignment.Near;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(text ?? "", font, brush, bounds, format);
            }
        }

        public static void DrawCentered(Graphics g, string text, Font font,
                                        Color color, Rectangle bounds)
        {
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                // Buttons and badges contain short labels -- never ellipsize them.
                format.Trimming = StringTrimming.None;
                format.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(text ?? "", font, brush, bounds, format);
            }
        }

        /// <summary>Draws a small rounded badge and returns its width.</summary>
        public static int DrawPill(Graphics g, int x, int y, string text,
                                   Font font, Color foreground, Color background)
        {
            Size size = Measure(text, font);
            int width = size.Width + 14;
            Rectangle rect = new Rectangle(x, y, width, 20);
            FillRound(g, rect, 10, background, Border, 1f);
            Rectangle inner = new Rectangle(x + 2, y, width - 4, 20);
            DrawCentered(g, text, font, foreground, inner);
            return width;
        }
    }
}
