using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ContextMenuCleaner.Core;
using ContextMenuCleaner.Models;

namespace ContextMenuCleaner.UI
{
    // ------------------------------------------------------------------ button

    public class ModernButton : Control
    {
        private bool hovered;
        private bool pressed;

        public ButtonStyle Style = ButtonStyle.Secondary;

        public ModernButton()
        {
            Theme.EnableDoubleBuffer(this);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Width = Theme.S(116);
            Height = Theme.S(34);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            int w = Theme.LW(this), h = Theme.LH(this);
            Color fill = Color.Transparent;
            Color border = Color.Transparent;
            Color foreground = Theme.Text;
            bool bold = false;
            bool sticker = false;

            switch (Style)
            {
                case ButtonStyle.Primary:
                    if (!Enabled) { fill = Theme.NeutralSoft; border = Theme.Border; }
                    else fill = pressed ? Theme.PrimaryDark
                             : (hovered ? Theme.PrimaryLight : Theme.Primary);
                    // Navy ink on cat orange keeps contrast high and matches the logo.
                    foreground = Theme.Ink;
                    bold = true;
                    sticker = true;
                    break;
                case ButtonStyle.Secondary:
                    fill = hovered && Enabled ? Theme.Hover : Theme.Surface;
                    border = Theme.Border;
                    break;
                case ButtonStyle.Ghost:
                    fill = hovered && Enabled ? Theme.PrimarySoft : Color.Transparent;
                    foreground = Theme.TextMuted;
                    break;
                case ButtonStyle.Danger:
                    fill = pressed ? Color.FromArgb(0xBE, 0x3D, 0x36) : Theme.Danger;
                    foreground = Color.White;
                    bold = true;
                    sticker = true;
                    break;
                case ButtonStyle.Success:
                    fill = pressed ? Color.FromArgb(0x22, 0x7E, 0x55) : Theme.Success;
                    foreground = Color.White;
                    bold = true;
                    sticker = true;
                    break;
            }

            if (!Enabled) { foreground = Theme.TextMuted; sticker = false; }

            if (sticker)
            {
                // Pressed buttons sink into their own shadow like a real sticker press.
                Rectangle face = new Rectangle(pressed ? 2 : 0, pressed ? 2 : 0, w - 5, h - 5);
                Theme.Sticker(e.Graphics, face, 10, fill,
                              pressed ? Color.Transparent : Theme.Shadow,
                              pressed ? 0 : 3);
                Theme.DrawCentered(e.Graphics, Text ?? "", Theme.MakeFont(9f, bold), foreground, face);
            }
            else
            {
                Rectangle rect = new Rectangle(0, 0, w - 1, h - 1);
                Theme.FillRound(e.Graphics, rect, 9, fill, border, border.A > 0 ? 1.2f : 0f);
                Theme.DrawCentered(e.Graphics, Text ?? "", Theme.MakeFont(9f, bold), foreground, rect);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    }

    // --------------------------------------------------------------- search box

    public class SearchBox : Panel
    {
        public TextBox Input;
        public event EventHandler QueryChanged;

        private bool focused;
        private bool clearHovered;

        public string Query { get { return Input == null ? "" : Input.Text; } }

        public SearchBox()
        {
            Theme.EnableDoubleBuffer(this);
            BackColor = Theme.Surface;
            Height = Theme.S(36);
            Width = Theme.S(320);

            Input = new TextBox();
            Input.BorderStyle = BorderStyle.None;
            // TextBox is a native control, unaffected by our Paint ScaleTransform.
            Input.Font = Theme.MakeFont(9.5f * Theme.Scale);
            Input.ForeColor = Theme.Text;
            Input.BackColor = Theme.Surface;
            Input.SetBounds(Theme.S(34), Theme.S(8), Theme.S(240), Theme.S(20));
            Controls.Add(Input);

            Input.TextChanged += delegate { Invalidate(); if (QueryChanged != null) QueryChanged(this, EventArgs.Empty); };
            Input.GotFocus += delegate { focused = true; Invalidate(); };
            Input.LostFocus += delegate { focused = false; Invalidate(); };
            Input.HandleCreated += delegate
            {
                try
                {
                    NativeMethods.SendMessageText(Input.Handle, NativeMethods.EM_SETCUEBANNER,
                                                  (IntPtr)1, "搜索软件、厂商或路径…");
                }
                catch (Exception) { /* cue banner is cosmetic */ }
            };
            Cursor = Cursors.IBeam;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (Input != null)
                Input.SetBounds(Theme.S(34), Theme.S(8),
                                Math.Max(Theme.S(40), Width - Theme.S(74)), Theme.S(20));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            int w = Theme.LW(this), h = Theme.LH(this);
            Rectangle rect = new Rectangle(0, 0, w - 1, h - 1);
            Theme.FillRound(e.Graphics, rect, 10, Theme.Surface,
                            focused ? Theme.PrimaryDark : Theme.Border, focused ? 1.9f : 1.2f);

            using (Pen pen = new Pen(Theme.TextMuted, 1.8f))
            {
                e.Graphics.DrawEllipse(pen, 12, 11, 10, 10);
                e.Graphics.DrawLine(pen, 21, 20, 26, 25);
            }

            if (!string.IsNullOrEmpty(Input.Text))
            {
                int cx = w - 30;
                if (clearHovered)
                {
                    using (SolidBrush brush = new SolidBrush(Theme.PrimarySoft))
                    {
                        e.Graphics.FillEllipse(brush, cx - 9, h / 2 - 9, 18, 18);
                    }
                }
                using (Pen pen = new Pen(Theme.Ink, 1.7f))
                {
                    e.Graphics.DrawLine(pen, cx - 4, h / 2 - 4, cx + 4, h / 2 + 4);
                    e.Graphics.DrawLine(pen, cx + 4, h / 2 - 4, cx - 4, h / 2 + 4);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool now = (e.X / Theme.Scale) > (Theme.LW(this) - 42);
            if (now != clearHovered) { clearHovered = now; Invalidate(); }
            Cursor = (now && !string.IsNullOrEmpty(Input.Text)) ? Cursors.Hand : Cursors.IBeam;
            base.OnMouseMove(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if ((e.X / Theme.Scale) > (Theme.LW(this) - 42) &&
                !string.IsNullOrEmpty(Input.Text)) Input.Text = "";
            Input.Focus();
        }
    }

    // ------------------------------------------------------------ toggle switch

    public class ToggleSwitch : Control
    {
        private bool checkedValue;
        private bool hovered;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return checkedValue; }
            set
            {
                if (checkedValue == value) return;
                checkedValue = value;
                Invalidate();
                if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
            }
        }

        public ToggleSwitch()
        {
            Theme.EnableDoubleBuffer(this);
            Width = Theme.S(40);
            Height = Theme.S(22);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            int w = Theme.LW(this), h = Theme.LH(this);
            Color track = checkedValue ? Theme.Primary : Color.FromArgb(0xD9, 0xC7, 0xB2);
            if (hovered && !checkedValue) track = Color.FromArgb(0xC9, 0xB4, 0x9C);
            Theme.FillRound(e.Graphics, new Rectangle(1, 3, w - 3, h - 7), 8,
                            track, Theme.Outline, 1.1f);
            int x = checkedValue ? w - 17 : 4;
            using (SolidBrush brush = new SolidBrush(Color.White))
            {
                e.Graphics.FillEllipse(brush, x, 5, 14, 14);
            }
            using (Pen pen = new Pen(Theme.Outline, 1.2f))
            {
                e.Graphics.DrawEllipse(pen, x, 5, 14, 14);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Checked = !Checked;
            base.OnMouseDown(e);
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    }

    // -------------------------------------------------------------- nav item

    public class NavItem : Control
    {
        public int Index;
        public string Caption = "";
        public int Count;
        public bool Active;
        public event EventHandler Clicked;

        private bool hovered;

        public NavItem()
        {
            Theme.EnableDoubleBuffer(this);
            Height = Theme.S(40);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            int w = Theme.LW(this), h = Theme.LH(this);
            Rectangle rect = new Rectangle(8, 2, w - 16, h - 4);
            if (Active)
            {
                Theme.FillRound(e.Graphics, rect, 10, Theme.NavActive, Theme.Outline, 1.2f);
                using (SolidBrush brush = new SolidBrush(Theme.PrimaryDark))
                {
                    e.Graphics.FillRectangle(brush, 8, 11, 4, h - 22);
                }
            }
            else if (hovered)
            {
                Theme.FillRound(e.Graphics, rect, 10, Theme.PrimarySoft, Color.Transparent);
            }

            Color tint = Active ? Theme.PrimaryDark : Theme.TextMuted;
            DrawGlyph(e.Graphics, Index, new Rectangle(20, (h - 18) / 2, 18, 18), tint);

            Font font = Theme.MakeFont(9.5f, Active);
            Rectangle textRect = new Rectangle(48, 0, w - 96, h);
            Theme.DrawText(e.Graphics, Caption, font, Active ? Theme.PrimaryDark : Theme.Text, textRect);

            if (Count > 0)
            {
                string count = Count.ToString();
                Rectangle badge = new Rectangle(w - 46, (h - 20) / 2, 34, 20);
                Theme.FillRound(e.Graphics, badge, 10,
                                Active ? Theme.Surface : Theme.NeutralSoft, Theme.Border, 1f);
                Theme.DrawCentered(e.Graphics, count, Theme.MakeFont(8.5f),
                                   Active ? Theme.PrimaryDark : Theme.TextMuted, badge);
            }
        }

        private static void DrawGlyph(Graphics g, int index, Rectangle r, Color color)
        {
            using (Pen pen = new Pen(color, 1.4f))
            {
                int x = r.X, y = r.Y;
                switch (index)
                {
                    case 0: // 全部项目: four tiles
                        g.DrawRectangle(pen, x + 1, y + 2, 6, 6);
                        g.DrawRectangle(pen, x + 11, y + 2, 6, 6);
                        g.DrawRectangle(pen, x + 1, y + 11, 6, 6);
                        g.DrawRectangle(pen, x + 11, y + 11, 6, 6);
                        break;
                    case 1: // 所有文件
                        g.DrawRectangle(pen, x + 3, y + 1, 12, 16);
                        g.DrawLine(pen, x + 6, y + 6, x + 12, y + 6);
                        g.DrawLine(pen, x + 6, y + 10, x + 12, y + 10);
                        g.DrawLine(pen, x + 6, y + 14, x + 10, y + 14);
                        break;
                    case 2: // 图片 / 影音: framed picture with a mountain
                        g.DrawRectangle(pen, x + 1, y + 2, 16, 14);
                        g.DrawEllipse(pen, x + 11, y + 5, 3, 3);
                        g.DrawLine(pen, x + 2, y + 14, x + 7, y + 9);
                        g.DrawLine(pen, x + 7, y + 9, x + 10, y + 12);
                        g.DrawLine(pen, x + 10, y + 12, x + 16, y + 7);
                        break;
                    case 3: // 文档 / 压缩包: sheet with folded corner
                        g.DrawRectangle(pen, x + 3, y + 1, 11, 16);
                        g.DrawLine(pen, x + 10, y + 1, x + 14, y + 5);
                        g.DrawLine(pen, x + 14, y + 5, x + 10, y + 5);
                        g.DrawLine(pen, x + 10, y + 5, x + 10, y + 1);
                        g.DrawLine(pen, x + 6, y + 10, x + 11, y + 10);
                        g.DrawLine(pen, x + 6, y + 13, x + 11, y + 13);
                        break;
                    case 4: // 文件夹
                        g.DrawRectangle(pen, x + 1, y + 5, 16, 11);
                        g.DrawLine(pen, x + 1, y + 5, x + 1, y + 3);
                        g.DrawLine(pen, x + 1, y + 3, x + 7, y + 3);
                        g.DrawLine(pen, x + 7, y + 3, x + 9, y + 5);
                        break;
                    case 5: // 桌面 / 空白处
                        g.DrawRectangle(pen, x + 1, y + 2, 16, 12);
                        g.DrawLine(pen, x + 4, y + 16, x + 14, y + 16);
                        g.DrawLine(pen, x + 9, y + 14, x + 9, y + 16);
                        break;
                    case 6: // 磁盘 / 驱动器
                        g.DrawRectangle(pen, x + 1, y + 4, 16, 11);
                        g.DrawEllipse(pen, x + 11, y + 8, 3, 3);
                        g.DrawLine(pen, x + 3, y + 12, x + 9, y + 12);
                        break;
                    case 7: // 快捷方式: box with an arrow
                        g.DrawRectangle(pen, x + 2, y + 4, 12, 12);
                        g.DrawLine(pen, x + 9, y + 2, x + 16, y + 9);
                        g.DrawLine(pen, x + 16, y + 9, x + 12, y + 9);
                        g.DrawLine(pen, x + 16, y + 9, x + 16, y + 5);
                        break;
                    case 8: // Win11 组件
                        g.DrawRectangle(pen, x + 2, y + 2, 6, 6);
                        g.DrawRectangle(pen, x + 10, y + 2, 6, 6);
                        g.DrawRectangle(pen, x + 2, y + 10, 6, 6);
                        g.DrawLine(pen, x + 10, y + 13, x + 16, y + 13);
                        g.DrawLine(pen, x + 13, y + 10, x + 13, y + 16);
                        break;
                    case 9: // 其他 / 未分类
                        g.DrawEllipse(pen, x + 2, y + 3, 14, 12);
                        g.DrawLine(pen, x + 6, y + 8, x + 12, y + 8);
                        g.DrawLine(pen, x + 6, y + 11, x + 10, y + 11);
                        break;
                    default: // 已隐藏 / 恢复
                        g.DrawEllipse(pen, x + 2, y + 3, 14, 12);
                        g.DrawLine(pen, x + 1, y + 17, x + 17, y + 1);
                        break;
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Clicked != null) Clicked(this, EventArgs.Empty);
        }
    }

    // ----------------------------------------------------------------- menu row

    /// <summary>One owner-drawn card: checkbox + real icon + name + badges + source.</summary>
    public class MenuRow : Control
    {
        public ContextMenuItem Item;
        public event EventHandler Toggled;

        private bool hovered;

        public MenuRow(ContextMenuItem item)
        {
            Theme.EnableDoubleBuffer(this);
            Item = item;
            Height = Theme.S(62);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            int w = Theme.LW(this), h = Theme.LH(this);
            Graphics g = e.Graphics;
            Rectangle card = new Rectangle(0, 2, w - 1, h - 4);

            Color background = Item.Checked ? Theme.PrimarySoft
                             : (hovered ? Theme.Hover : Theme.Surface);
            Color border = Item.Checked ? Theme.PrimaryDark : Theme.RowBorder;
            if (Item.IsHidden && !Item.Checked) border = Theme.Warning;
            Theme.FillRound(g, card, 12, background, border, Item.Checked ? 1.9f : 1.1f);

            // checkbox
            Rectangle box = new Rectangle(16, (h - 18) / 2, 18, 18);
            Theme.FillRound(g, box, 5,
                            Item.Checked ? Theme.Primary : Theme.Surface,
                            Item.Checked ? Theme.Outline : Theme.Border, 1.5f);
            if (Item.Checked)
            {
                using (Pen pen = new Pen(Theme.Ink, 2.2f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, box.X + 4, box.Y + 9, box.X + 8, box.Y + 13);
                    g.DrawLine(pen, box.X + 8, box.Y + 13, box.X + 14, box.Y + 5);
                }
            }

            // 32x32 icon extracted from the provider binary
            if (Item.IconImage != null)
            {
                g.DrawImage(Item.IconImage, new Rectangle(46, (h - 32) / 2, 32, 32));
            }

            int textX = 90;
            Font nameFont = Theme.MakeFont(10f, true);
            int pillReserve = Item.IsSystem ? 190 : 160;
            int nameWidth = Math.Max(90, w - textX - pillReserve);
            int realNameWidth = Theme.Measure(Item.Name, nameFont).Width;
            Rectangle nameRect = new Rectangle(textX, 5, nameWidth, 28);
            Theme.DrawText(g, Item.Name, nameFont, Theme.Text, nameRect);

            int x = textX + Math.Min(realNameWidth, nameWidth) + 10;
            int y = 9;
            Font badgeFont = Theme.MakeFont(8f);
            int limit = w - 12;

            x += DrawBadge(g, x, y, badgeFont, limit,
                           Item.IsSystem ? "系统" : "第三方",
                           Item.IsSystem ? Theme.TextMuted : Theme.PrimaryDark,
                           Item.IsSystem ? Theme.NeutralSoft : Theme.PrimarySoft);
            if (Item.IsHidden)
                x += DrawBadge(g, x, y, badgeFont, limit, "已隐藏", Theme.Warning, Theme.WarningSoft);
            if (Item.NeedsAdmin && !Item.IsHidden && !MenuManager.IsAdministrator)
                x += DrawBadge(g, x, y, badgeFont, limit, "需管理员", Theme.Danger, Theme.DangerSoft);

            string second = (string.IsNullOrEmpty(Item.Detail) ? "" : Item.Detail + "   ·   ") + Item.SourceText;
            Rectangle detailRect = new Rectangle(textX, 31, Math.Max(60, w - textX - 14), 24);
            Theme.DrawText(g, second, Theme.MakeFont(8.5f), Theme.TextMuted, detailRect);
        }

        private static int DrawBadge(Graphics g, int x, int y, Font font, int limit,
                                     string text, Color foreground, Color background)
        {
            int width = Theme.Measure(text, font).Width + 14;
            if (x + width + 8 > limit) return 0;
            Theme.DrawPill(g, x, y, text, font, foreground, background);
            return width + 6;
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            // The second click of a double-click would toggle the row a second time.
            // Undo the first toggle instead so opening the detail dialog leaves the
            // selection exactly as it was.
            if (e.Clicks > 1)
            {
                Item.Checked = !Item.Checked;
                Invalidate();
                if (Toggled != null) Toggled(this, EventArgs.Empty);
                return;
            }

            Item.Checked = !Item.Checked;
            Invalidate();
            if (Toggled != null) Toggled(this, EventArgs.Empty);
        }
    }
}
