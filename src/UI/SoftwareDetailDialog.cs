using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using ContextMenuCleaner.Core;
using ContextMenuCleaner.Models;

namespace ContextMenuCleaner.UI
{
    /// <summary>
    /// "这个到底是什么软件" view. Shows the real product behind a menu entry:
    /// executable path, CLSID, publisher, and every registry place it registered.
    /// Also explains why one product may appear several times in the raw list.
    /// </summary>
    public class SoftwareDetailDialog : Form
    {
        private readonly SoftwareGroup group;
        private readonly Action onGroupToggled;
        private ModernButton selectButton;
        private ModernButton copyButton;
        private ModernButton folderButton;

        public SoftwareDetailDialog(SoftwareGroup software, Action groupToggled)
        {
            group = software;
            onGroupToggled = groupToggled;
            Text = "软件详情";
            ClientSize = new Size(Theme.S(540), Theme.S(466));
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Surface;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            MinimizeBox = false;
            MaximizeBox = false;

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.BackColor = Theme.Surface;
            Theme.EnableDoubleBuffer(body);
            body.Paint += BodyPaint;
            body.MouseDown += delegate
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessagePtr(Handle, NativeMethods.WM_NCLBUTTONDOWN,
                                             (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
            };
            Controls.Add(body);

            copyButton = NewButton("复制程序路径", 28, 410, 118);
            copyButton.Enabled = !string.IsNullOrEmpty(group.BinaryPath);
            copyButton.Click += delegate
            {
                try
                {
                    Clipboard.SetText(group.BinaryPath ?? "");
                    ShowHint("路径已复制");
                }
                catch (Exception) { ShowHint("复制失败，可手动选中下方路径"); }
            };
            body.Controls.Add(copyButton);

            folderButton = NewButton("打开所在文件夹", 154, 410, 128);
            folderButton.Enabled = !string.IsNullOrEmpty(group.BinaryPath) &&
                                   File.Exists(group.BinaryPath);
            folderButton.Click += delegate
            {
                try { Process.Start("explorer.exe", "/select,\"" + group.BinaryPath + "\""); }
                catch (Exception) { ShowHint("打不开文件夹"); }
            };
            body.Controls.Add(folderButton);

            selectButton = NewButton(SelectCaption(), 290, 410, 108);
            selectButton.Click += delegate
            {
                bool next = !group.Checked;
                group.Checked = next;
                selectButton.Text = SelectCaption();
                selectButton.Invalidate();
                if (onGroupToggled != null) onGroupToggled();
                body.Invalidate();
            };
            body.Controls.Add(selectButton);

            ModernButton close = NewButton("关闭", 406, 410, 106);
            close.Style = ButtonStyle.Primary;
            close.Click += delegate { Close(); };
            body.Controls.Add(close);

            Shown += delegate
            {
                using (GraphicsPath path = Theme.RoundedPath(
                    new Rectangle(0, 0, Width, Height), Theme.S(12)))
                {
                    Region = new Region(path);
                }
            };
        }

        private string SelectCaption()
        {
            return group.Checked ? "取消本组勾选" : "勾选本组全部";
        }

        private ModernButton NewButton(string text, int x, int y, int width)
        {
            ModernButton button = new ModernButton
            {
                Text = text,
                Style = ButtonStyle.Secondary,
                Width = Theme.S(width),
                Height = Theme.S(34)
            };
            button.SetBounds(Theme.S(x), Theme.S(y), Theme.S(width), Theme.S(34));
            return button;
        }

        private Label hint;

        private void ShowHint(string text)
        {
            if (hint == null)
            {
                hint = new Label();
                hint.AutoSize = false;
                hint.TextAlign = ContentAlignment.MiddleLeft;
                hint.ForeColor = Theme.TextMuted;
                hint.Font = Theme.MakeFont(8f);
                hint.SetBounds(Theme.S(28), Theme.S(386), Theme.S(484), Theme.S(20));
                Controls[0].Controls.Add(hint);
            }
            hint.Text = text;
            hint.Invalidate();
        }

        private void BodyPaint(object sender, PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            Graphics g = e.Graphics;
            int w = Theme.LW((Control)sender), h = Theme.LH((Control)sender);

            Theme.FillRound(g, new Rectangle(3, 3, w - 5, h - 5), 14,
                            Theme.Shadow, Color.Transparent);
            Theme.FillRound(g, new Rectangle(0, 0, w - 5, h - 5), 14,
                            Theme.Surface, Theme.Outline, 1.8f);

            if (group.IconImage != null)
                g.DrawImage(group.IconImage, new Rectangle(28, 22, 48, 48));
            Theme.DrawText(g, group.Name, Theme.MakeFont(13f, true), Theme.Text,
                           new Rectangle(90, 20, 330, 30));
            Theme.DrawText(g, string.IsNullOrEmpty(group.Publisher) ? "厂商未知" : group.Publisher,
                           Theme.MakeFont(9f), Theme.TextMuted, new Rectangle(90, 49, 330, 22));
            Theme.DrawPill(g, 424, 30, group.Count + " 处注册", Theme.MakeFont(8f, true),
                           Theme.PrimaryDark, Theme.PrimarySoft);

            using (Pen pen = new Pen(Theme.Border, 1.1f))
            {
                g.DrawLine(pen, 28, 84, w - 28, 84);
            }

            string bin = string.IsNullOrEmpty(group.BinaryPath)
                             ? "未找到独立程序文件（可能是系统组件或打包应用）"
                             : group.BinaryPath;
            Field(g, 28, 96, "程序文件", bin);
            Field(g, 28, 140, "CLSID", string.IsNullOrEmpty(group.Clsid) ? "无（静态菜单命令）" : group.Clsid);

            string flags = (group.IsSystem ? "系统内置" : "第三方") +
                           "   ·   " + (group.NeedsAdmin ? "部分位置需要管理员" : "普通权限可处理") +
                           "   ·   " + (group.AnyHidden ? "含已隐藏项" : "全部正常显示");
            Theme.DrawText(g, flags, Theme.MakeFont(8.5f), Theme.TextMuted,
                           new Rectangle(28, 184, w - 56, 22));

            Theme.FillRound(g, new Rectangle(28, 212, w - 56, 38), 10,
                            Theme.PrimarySoft, Color.Transparent);
            Theme.DrawText(g,
                "同一个软件会在多个位置各注册一次，所以原始清单里会出现好几行。\n" +
                "这里按软件合并显示；勾选或清理时，下面这些注册点会一起处理。",
                Theme.MakeFont(8f), Theme.Text, new Rectangle(40, 214, w - 80, 36));

            Theme.DrawText(g, "注册位置（" + group.Count + "）", Theme.MakeFont(9f, true),
                           Theme.Text, new Rectangle(28, 258, 300, 22));

            int maxRows = 6;
            int shown = Math.Min(maxRows, group.Entries.Count);
            for (int i = 0; i < shown; i++)
            {
                ContextMenuItem item = group.Entries[i];
                int y = 284 + i * 17;
                string line = "·  " + item.ScopeText + "  ·  " + item.Hive + "  ·  " +
                              ShortPath(item.RegistryPath, 62);
                Theme.DrawText(g, line, Theme.MakeFont(8f), Theme.TextMuted,
                               new Rectangle(32, y, w - 64, 18));
            }
            if (group.Entries.Count > shown)
                Theme.DrawText(g, "…还有 " + (group.Entries.Count - shown) +
                                  " 处，点列表里的展开箭头可逐条查看与勾选",
                               Theme.MakeFont(8f), Theme.TextFaint,
                               new Rectangle(32, 284 + shown * 17, w - 64, 18));
        }

        private static void Field(Graphics g, int x, int y, string label, string value)
        {
            Theme.DrawText(g, label, Theme.MakeFont(8f, true), Theme.TextFaint,
                           new Rectangle(x, y, 80, 18));
            Theme.DrawText(g, value, Theme.MakeFont(8.5f), Theme.Text,
                           new Rectangle(x + 80, y - 1, 400, 38));
        }

        private static string ShortPath(string value, int max)
        {
            string s = value ?? "";
            if (s.Length <= max) return s;
            return s.Substring(0, max / 2) + "…" + s.Substring(s.Length - max / 3);
        }
    }
}
