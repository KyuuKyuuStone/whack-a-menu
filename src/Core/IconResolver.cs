using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using ContextMenuCleaner.Models;

namespace ContextMenuCleaner.Core
{
    /// <summary>
    /// Turns a registry icon spec ("app.exe,0", "imageres.dll,-5302", "@dll,-id")
    /// into a 32x32 ARGB bitmap, with a deterministic letter tile as fallback.
    /// </summary>
    public static class IconResolver
    {
        private static readonly Dictionary<string, Bitmap> Cache = new Dictionary<string, Bitmap>();

        private static readonly Color[] Palette =
        {
            Color.FromArgb(0x4F, 0x6B, 0xED),
            Color.FromArgb(0x0E, 0x9F, 0x6E),
            Color.FromArgb(0xE1, 0x6A, 0x3C),
            Color.FromArgb(0x8B, 0x5C, 0xF6),
            Color.FromArgb(0x0E, 0xA5, 0xC9),
            Color.FromArgb(0xD9, 0x4A, 0x6A),
            Color.FromArgb(0x3A, 0x7B, 0xD5),
            Color.FromArgb(0x7A, 0x8B, 0x3F)
        };

        public static Bitmap Get(ContextMenuItem item)
        {
            string key = (item.IconSpec ?? "") + "|" + (item.BinaryPath ?? "") + "|" +
                         (item.Name ?? "") + "|" + item.IsSystem;
            Bitmap cached;
            if (Cache.TryGetValue(key, out cached)) return cached;

            Bitmap result = Try(item.IconSpec);
            if (result == null) result = Try(item.BinaryPath);
            if (result == null) result = MakeLetterIcon(item.Name, item.IsSystem);
            Cache[key] = result;
            return result;
        }

        private static Bitmap Try(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return null;
            string s = spec.Trim();
            if (s.Length == 0) return null;

            try { s = Environment.ExpandEnvironmentVariables(s); }
            catch (Exception) { return null; }

            if (s[0] == '@') s = s.Substring(1);   // indirect icon string
            s = s.Trim().Trim('"');

            int index = 0;
            string path = s;
            int comma = s.LastIndexOf(',');
            if (comma > 0)
            {
                string tail = s.Substring(comma + 1).Trim();
                int parsed;
                if (int.TryParse(tail, out parsed))
                {
                    index = parsed;
                    path = s.Substring(0, comma).Trim().Trim('"');
                }
            }
            path = path.Trim().Trim('"');
            if (path.Length == 0) return null;

            if (!File.Exists(path))
            {
                // Specs often contain a bare DLL name resolvable from System32.
                try
                {
                    string sysDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                    string candidate = Path.Combine(sysDir, Path.GetFileName(path));
                    if (File.Exists(candidate)) path = candidate;
                    else return null;
                }
                catch (Exception) { return null; }
            }

            Icon icon = null;
            try { icon = NativeMethods.ExtractIcon(path, index, 64); }
            catch (Exception) { icon = null; }
            if (icon == null)
            {
                try { icon = Icon.ExtractAssociatedIcon(path); }
                catch (Exception) { icon = null; }
            }
            if (icon == null) return null;

            using (icon)
            {
                return ToBitmap(icon);
            }
        }

        private static Bitmap ToBitmap(Icon icon)
        {
            // Render at 64px so the 32 logical-px row icon stays crisp on 150-200% DPI.
            Bitmap bmp = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                try
                {
                    using (Bitmap src = icon.ToBitmap())
                    {
                        g.DrawImage(src, new Rectangle(0, 0, 64, 64));
                    }
                }
                catch (Exception)
                {
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(150, 160, 180)))
                    {
                        g.FillRectangle(brush, 8, 8, 48, 48);
                    }
                }
            }
            return bmp;
        }

        private static Bitmap MakeLetterIcon(string name, bool system)
        {
            Bitmap bmp = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                int hash = 0;
                string s = name ?? "";
                for (int i = 0; i < s.Length; i++) hash = hash * 31 + s[i];
                Color color = system
                    ? Color.FromArgb(0x93, 0x9E, 0xB4)
                    : Palette[Math.Abs(hash) % Palette.Length];

                using (GraphicsPath gp = new GraphicsPath())
                {
                    AddRoundedRect(gp, new Rectangle(0, 0, 64, 64), 16);
                    using (SolidBrush brush = new SolidBrush(color))
                    {
                        g.FillPath(brush, gp);
                    }
                }

                string letter = FirstLetter(s);
                using (Font font = new Font("Microsoft YaHei UI", 30f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (StringFormat sf = new StringFormat())
                using (SolidBrush white = new SolidBrush(Color.White))
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.DrawString(letter, font, white, new RectangleF(0, 0, 64, 64), sf);
                }
            }
            return bmp;
        }

        private static string FirstLetter(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '&' || char.IsWhiteSpace(c) || c == '.' || c == '-' || c == '_') continue;
                if (char.IsLetter(c)) return char.ToUpperInvariant(c).ToString();
                return c.ToString();
            }
            return "?";
        }

        private static void AddRoundedRect(GraphicsPath path, Rectangle rect, int radius)
        {
            int d = radius * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
        }
    }
}
