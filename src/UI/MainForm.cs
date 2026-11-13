using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ContextMenuCleaner.Core;
using ContextMenuCleaner.Models;

namespace ContextMenuCleaner.UI
{
    public class MainForm : Form
    {
        private enum Category { All = 0, File = 1, Folder = 2, Background = 3, Drive = 4, Modern = 5, Hidden = 6 }

        private const int TitleHeight = 58;
        private const int SidebarWidth = 208;
        private const int ToolbarHeight = 64;
        private const int FooterHeight = 72;
        private const int NoticeHeight = 36;
        private const int RowPitch = 68;

        private HiddenStore store;
        private ScanResult scan;
        private List<ContextMenuItem> allItems = new List<ContextMenuItem>();
        private readonly List<MenuRow> rows = new List<MenuRow>();
        private Category category = Category.All;
        private bool onlyThirdParty;
        private bool noticeVisible;
        private bool layingOut;

        private Panel titleBar;
        private Panel sidebar;
        private Panel toolbar;
        private Panel footer;
        private Panel noticeBar;
        private Panel listHost;
        private Panel rowsBox;
        private Panel toast;
        private Panel grip;
        private SearchBox search;
        private ToggleSwitch thirdPartyToggle;
        private ModernButton btnQuirk;
        private ModernButton btnAbout;
        private ModernButton btnMin;
        private ModernButton btnClose;
        private ModernButton btnRefresh;
        private ModernButton btnSelectAll;
        private ModernButton btnClear;
        private ModernButton btnExport;
        private ModernButton btnRestart;
        private ModernButton btnApply;
        private ModernButton btnElevate;
        private string statusText = "";
        private Rectangle statusTextRect;
        private Rectangle toggleTextRect;
        private NavItem[] navItems;
        private Timer filterTimer;
        private Timer toastTimer;
        private Icon appIcon;
        private Bitmap appIconBitmap;
        private string toastMessage = "";
        private bool toastError;
        private int logoClicks;
        private DateTime lastLogoClick = DateTime.MinValue;
        private bool updateChecking;
        private readonly Random rnd = new Random();
        private string mascotLine = "";

        private static readonly string[] MascotLines =
        {
            "右键比我的社交圈还长",
            "不删键，只请走。我很温柔的",
            "菜单条：我当时害怕极了",
            "别点我，我在蓄力",
            "这堆快捷项，没一个是主动辞职的",
            "九条命，零耐心",
            "你右键一次，我加班一次",
            "系统内置项惹不起，其他的随便",
            "咻——又飞出去一个",
            "一爪一个，概不赊账"
        };

        private static readonly string[] RescanLines =
        {
            "又数了一遍。它们还在。嘿嘿",
            "扫完了，菜单条一个都没跑",
            "重新点名，全体到齐（除了被拍飞的）"
        };

        private static readonly string[] CleanLines =
        {
            "已请走 {0} 项 · 右键终于能喘口气了",
            "{0} 项已打包送走 · 橘座舔了舔爪子",
            "走你！{0} 项菜单条已被一爪拍飞",
            "已请走 {0} 项 · 点「重启资源管理器」让它们真的走"
        };

        private static readonly string[] RestoreLines =
        {
            "放回来 {0} 项 · 橘座：我就知道你会后悔",
            "{0} 项归位，它们保证下次还敢",
            "请回来 {0} 项 · 右键又热闹了"
        };

        private static string SelectionFile
        {
            get { return Path.Combine(Path.GetTempPath(), "cmc-selection.txt"); }
        }

        public MainForm(bool elevated)
        {
            Text = "右键菜单清理";

            // Windows 11 only: size the window from the real working area and the
            // DPI scale instead of a hard-coded 1180x740 box.
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            int maxW = (int)(work.Width * 0.92);
            int maxH = (int)(work.Height * 0.92);
            ClientSize = new Size(Math.Min(Theme.S(1400), maxW),
                                  Math.Min(Theme.S(880), maxH));
            MinimumSize = new Size(Math.Min(Theme.S(980), (int)(work.Width * 0.8)),
                                   Math.Min(Theme.S(620), (int)(work.Height * 0.8)));
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Background;
            DoubleBuffered = true;
            ShowInTaskbar = true;
            MaximizeBox = false;

            try { appIcon = new Icon(Application.ExecutablePath, new Size(64, 64)); }
            catch (Exception) { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            if (appIcon == null) appIcon = SystemIcons.Application;
            Icon = appIcon;
            try { appIconBitmap = appIcon.ToBitmap(); }
            catch (Exception) { appIconBitmap = null; }

            BuildUi();
            noticeVisible = !MenuManager.IsAdministrator;
            Rescan();
            if (elevated) RestoreSelection();
            mascotLine = Pick(MascotLines);
            DoLayout();
        }

        // WS_THICKFRAME keeps system resizing on a borderless form; CS_DROPSHADOW adds a soft shadow.
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x00040000;
                cp.ClassStyle |= 0x00020000;
                return cp;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyRegion();
            DoLayout();
            // Silent background check: only speaks up when a newer Release exists.
            BeginUpdateCheck(false);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ApplyRegion();
            DoLayout();
        }

        private void ApplyRegion()
        {
            if (Width <= 0 || Height <= 0) return;
            using (GraphicsPath path = Theme.RoundedPath(
                new Rectangle(0, 0, Width, Height), Theme.S(12)))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        // ------------------------------------------------------------ build UI

        private void BuildUi()
        {
            titleBar = NewPanel(Theme.Surface);
            titleBar.Paint += TitleBarPaint;
            titleBar.MouseDown += delegate (object s, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                int logicalX = (int)(e.X / Theme.Scale);
                // Poking the mascot always gets a reaction in quirky mode.
                if (logicalX < 56 && Theme.Quirky)
                {
                    PokeMascot();
                    return;
                }
                if (logicalX < Theme.LW(titleBar) - 282) DragWindow();
            };
            Controls.Add(titleBar);

            sidebar = NewPanel(Theme.Sidebar);
            sidebar.Paint += SidebarPaint;
            Controls.Add(sidebar);

            toolbar = NewPanel(Theme.Surface);
            toolbar.Paint += ToolbarPaint;
            Controls.Add(toolbar);

            noticeBar = NewPanel(Theme.WarningSoft);
            noticeBar.Paint += NoticePaint;
            Controls.Add(noticeBar);

            footer = NewPanel(Theme.Surface);
            footer.Paint += FooterPaint;
            Controls.Add(footer);

            listHost = new Panel();
            listHost.BackColor = Theme.Background;
            listHost.AutoScroll = true;
            listHost.MouseWheel += ForwardWheel;
            Controls.Add(listHost);

            rowsBox = new Panel();
            rowsBox.BackColor = Theme.Background;
            rowsBox.Location = new Point(0, 0);
            Theme.EnableDoubleBuffer(rowsBox);
            rowsBox.Paint += EmptyStatePaint;
            listHost.Controls.Add(rowsBox);

            BuildTitleButtons();
            BuildSidebar();
            BuildToolbar();
            BuildNotice();
            BuildFooter();
            BuildOverlays();

            filterTimer = new Timer();
            filterTimer.Interval = 140;
            filterTimer.Tick += delegate { filterTimer.Stop(); RefreshList(); };

            toastTimer = new Timer();
            toastTimer.Interval = 3600;
            toastTimer.Tick += delegate { toastTimer.Stop(); toast.Visible = false; };
        }

        private static Panel NewPanel(Color color)
        {
            Panel panel = new Panel();
            panel.BackColor = color;
            Theme.EnableDoubleBuffer(panel);
            return panel;
        }

        private void BuildTitleButtons()
        {
            btnQuirk = new ModernButton
            {
                Text = Theme.Quirky ? "搞怪 ON" : "正经",
                Style = ButtonStyle.Ghost, Width = 76, Height = 30
            };
            btnQuirk.Click += delegate
            {
                Theme.Quirky = !Theme.Quirky;
                btnQuirk.Text = Theme.Quirky ? "搞怪 ON" : "正经";
                titleBar.Invalidate();
                sidebar.Invalidate();
                rowsBox.Invalidate();
                ShowToast(Theme.Quirky ? "搞怪模式：橘座回来了" : "正经模式：橘座闭嘴了");
            };
            titleBar.Controls.Add(btnQuirk);

            btnAbout = new ModernButton { Text = "关于", Style = ButtonStyle.Ghost, Width = 60, Height = 30 };
            btnAbout.Click += delegate
            {
                using (AboutDialog dialog = new AboutDialog(appIcon))
                {
                    dialog.OnCheckUpdate = delegate { BeginUpdateCheck(true); };
                    dialog.ShowDialog(this);
                }
            };
            titleBar.Controls.Add(btnAbout);

            btnMin = new ModernButton { Text = "—", Style = ButtonStyle.Ghost, Width = 36, Height = 30 };
            btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            titleBar.Controls.Add(btnMin);

            btnClose = new ModernButton { Text = "×", Style = ButtonStyle.Ghost, Width = 36, Height = 30 };
            btnClose.Click += delegate { Close(); };
            titleBar.Controls.Add(btnClose);
        }

        private void BuildSidebar()
        {
            string[] captions =
            {
                "全部项目", "文件右键", "文件夹右键", "桌面 / 空白处",
                "磁盘 / 快捷", "Win11 组件", "已隐藏"
            };
            navItems = new NavItem[captions.Length];
            for (int i = 0; i < captions.Length; i++)
            {
                NavItem item = new NavItem();
                item.Index = i;
                item.Caption = captions[i];
                item.Active = i == (int)Category.All;
                item.Clicked += NavClicked;
                navItems[i] = item;
                sidebar.Controls.Add(item);
            }
        }

        private void BuildToolbar()
        {
            search = new SearchBox();
            search.QueryChanged += delegate { filterTimer.Stop(); filterTimer.Start(); };
            toolbar.Controls.Add(search);

            thirdPartyToggle = new ToggleSwitch();
            thirdPartyToggle.CheckedChanged += delegate
            {
                onlyThirdParty = thirdPartyToggle.Checked;
                UpdateNavCounts();
                RefreshList();
            };
            toolbar.Controls.Add(thirdPartyToggle);

            btnRefresh = new ModernButton { Text = "刷新", Style = ButtonStyle.Secondary, Width = 76, Height = 32 };
            btnRefresh.Click += delegate
            {
                Rescan();
                if (Theme.Quirky) NextMascotLine();
                ShowToast(Theme.Quirky ? Pick(RescanLines) : "已重新扫描右键菜单");
            };
            toolbar.Controls.Add(btnRefresh);

            btnSelectAll = new ModernButton { Text = "全选", Style = ButtonStyle.Ghost, Width = 66, Height = 32 };
            btnSelectAll.Click += SelectAllVisible;
            toolbar.Controls.Add(btnSelectAll);

            btnClear = new ModernButton { Text = "清空", Style = ButtonStyle.Ghost, Width = 66, Height = 32 };
            btnClear.Click += delegate
            {
                foreach (ContextMenuItem item in allItems) item.Checked = false;
                RefreshList();
            };
            toolbar.Controls.Add(btnClear);
        }

        private void BuildNotice()
        {
            btnElevate = new ModernButton
            {
                Text = "以管理员重启",
                Style = ButtonStyle.Secondary,
                Width = 122,
                Height = 26
            };
            btnElevate.Click += delegate
            {
                SaveSelection();
                MenuManager.RestartElevated();
                Close();
            };
            noticeBar.Controls.Add(btnElevate);
        }

        private void BuildFooter()
        {
            btnExport = new ModernButton { Text = "导出清单", Style = ButtonStyle.Ghost, Width = 92, Height = 34 };
            btnExport.Click += ExportCsv;
            footer.Controls.Add(btnExport);

            btnRestart = new ModernButton { Text = "重启资源管理器", Style = ButtonStyle.Secondary, Width = 136, Height = 34 };
            btnRestart.Click += delegate
            {
                DialogResult answer = MessageBox.Show(this,
                    "重启资源管理器会让任务栏和桌面短暂刷新（不会关闭已打开的程序窗口）。\n\n继续吗？",
                    "刷新右键菜单", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer != DialogResult.Yes) return;
                OperationResult result = MenuManager.RestartExplorer();
                ShowToast(result.Message, !result.Success);
            };
            footer.Controls.Add(btnRestart);

            btnApply = new ModernButton { Text = "清理选中项 (0)", Style = ButtonStyle.Primary, Width = 160, Height = 36 };
            btnApply.Click += delegate { ApplySelection(); };
            footer.Controls.Add(btnApply);
        }

        private void BuildOverlays()
        {
            toast = new Panel();
            toast.Height = Theme.S(40);
            toast.Width = Theme.S(460);
            toast.Visible = false;
            toast.BackColor = Color.Transparent;
            Theme.EnableDoubleBuffer(toast);
            toast.Paint += delegate (object s, PaintEventArgs e)
            {
                Theme.Prepare(e.Graphics);
                int w = Theme.LW(toast), h = Theme.LH(toast);
                Rectangle rect = new Rectangle(0, 0, w - 5, h - 5);
                Theme.FillRound(e.Graphics, new Rectangle(3, 3, w - 5, h - 5), 12,
                                Theme.Shadow, Color.Transparent);
                Theme.FillRound(e.Graphics, rect, 12, Theme.Surface,
                                toastError ? Theme.Danger : Theme.Outline, 1.7f);
                using (SolidBrush brush = new SolidBrush(toastError ? Theme.Danger : Theme.Success))
                {
                    e.Graphics.FillEllipse(brush, 16, 15, 10, 10);
                }
                Theme.DrawText(e.Graphics, toastMessage, Theme.MakeFont(9f), Theme.Ink,
                               new Rectangle(34, 2, w - 50, h - 8));
            };
            Controls.Add(toast);
            toast.BringToFront();

            grip = new Panel();
            grip.Width = Theme.S(16);
            grip.Height = Theme.S(16);
            grip.BackColor = Color.Transparent;
            grip.Cursor = Cursors.SizeNWSE;
            grip.Paint += delegate (object s, PaintEventArgs e)
            {
                Theme.Prepare(e.Graphics);
                using (Pen pen = new Pen(Theme.TextMuted, 1.4f))
                {
                    e.Graphics.DrawLine(pen, 4, 13, 13, 4);
                    e.Graphics.DrawLine(pen, 8, 13, 13, 8);
                }
            };
            grip.MouseDown += delegate
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessagePtr(Handle, NativeMethods.WM_NCLBUTTONDOWN,
                                             (IntPtr)17, IntPtr.Zero); // HTBOTTOMRIGHT
            };
            Controls.Add(grip);
            grip.BringToFront();
        }

        // --------------------------------------------------------------- layout

        /// <summary>Place a control using logical (96-DPI) coordinates.</summary>
        private static void Place(Control control, int x, int y, int width, int height)
        {
            control.SetBounds(Theme.S(x), Theme.S(y), Theme.S(width), Theme.S(height));
        }

        private void DoLayout()
        {
            // ClientSize is assigned in the constructor before BuildUi() runs, and
            // WinForms raises SizeChanged at that point. Skip until controls exist.
            if (titleBar == null || listHost == null || navItems == null) return;
            if (layingOut) return;
            layingOut = true;
            try
            {
                int w = Theme.LW(this);
                int h = Theme.LH(this);
                int noticeH = noticeVisible ? NoticeHeight : 0;

                Place(titleBar, 0, 0, w, TitleHeight);
                Place(sidebar, 0, TitleHeight, SidebarWidth, h - TitleHeight);
                Place(toolbar, SidebarWidth, TitleHeight, w - SidebarWidth, ToolbarHeight);
                Place(noticeBar, SidebarWidth, TitleHeight + ToolbarHeight,
                      w - SidebarWidth, noticeH);
                noticeBar.Visible = noticeVisible;
                Place(footer, SidebarWidth, h - FooterHeight, w - SidebarWidth, FooterHeight);

                int listTop = TitleHeight + ToolbarHeight + noticeH;
                Place(listHost, SidebarWidth, listTop, w - SidebarWidth,
                      Math.Max(80, h - FooterHeight - listTop));

                Place(btnQuirk, w - 268, 13, 76, 30);
                Place(btnAbout, w - 186, 13, 60, 30);
                Place(btnMin, w - 116, 13, 36, 30);
                Place(btnClose, w - 72, 13, 36, 30);

                for (int i = 0; i < navItems.Length; i++)
                    Place(navItems[i], 8, 44 + i * 44, SidebarWidth - 16, 40);

                int searchWidth = Math.Min(340, Math.Max(220, w - SidebarWidth - 520));
                Place(search, 20, 14, searchWidth, 36);
                int toggleX = 20 + searchWidth + 18;
                Place(thirdPartyToggle, toggleX, 21, 40, 22);
                toggleTextRect = new Rectangle(toggleX + 46, 12, 110, 36);

                int right = w - SidebarWidth - 20;
                Place(btnClear, right - 66, 16, 66, 32);
                Place(btnSelectAll, right - 138, 16, 66, 32);
                Place(btnRefresh, right - 220, 16, 76, 32);

                Place(btnElevate, w - SidebarWidth - 142, 5, 122, 26);

                statusTextRect = new Rectangle(20, 12,
                    Math.Max(240, w - SidebarWidth - 440), 46);
                int footerRight = w - SidebarWidth - 20;
                Place(btnApply, footerRight - 160, 18, 160, 36);
                Place(btnRestart, footerRight - 306, 19, 136, 34);
                Place(btnExport, footerRight - 406, 19, 92, 34);

                Place(toast, (w - 460) / 2, h - FooterHeight - 56, 460, 40);
                Place(grip, w - 18, h - 18, 16, 16);

                rowsBox.Width = listHost.ClientSize.Width;
                int rowWidth = Math.Max(Theme.S(220), rowsBox.Width - Theme.S(32));
                foreach (MenuRow row in rows) row.Width = rowWidth;
            }
            finally
            {
                layingOut = false;
            }
        }

        // -------------------------------------------------------------- drawing

        private void TitleBarPaint(object sender, PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            int tw = Theme.LW(titleBar), th = Theme.LH(titleBar);
            if (appIconBitmap != null)
                e.Graphics.DrawImage(appIconBitmap, new Rectangle(16, 9, 40, 40));
            else if (appIcon != null)
                e.Graphics.DrawIcon(appIcon, new Rectangle(18, 11, 36, 36));
            Theme.DrawText(e.Graphics, "右键菜单清理", Theme.MakeFont(11.5f, true), Theme.Text,
                           new Rectangle(62, 4, 260, 30));
            if (Theme.Quirky)
            {
                Theme.DrawPill(e.Graphics, 184, 12, "嘿嘿", Theme.MakeFont(8f),
                               Theme.Ink, Theme.PrimarySoft);
                Theme.DrawSparkle(e.Graphics, 404, 14, 12, Theme.Accent);
                Theme.DrawSparkle(e.Graphics, 420, 34, 7, Theme.PrimaryDark);

                // "Mascot says" bubble: rotating one-liners, refreshed on every
                // rescan, category switch and poke. Ellipsized if the window is narrow.
                Font bubbleFont = Theme.MakeFont(8.5f, true);
                int bx = 432;
                int maxBubble = tw - 282 - bx;
                if (maxBubble >= 170 && !string.IsNullOrEmpty(mascotLine))
                {
                    int bw = Math.Min(maxBubble,
                        Math.Max(230, Theme.Measure(mascotLine, bubbleFont).Width + 32));
                    Rectangle bubble = new Rectangle(bx, 12, bw, 30);
                    // Sticker speech bubble: solid navy drop plus a bold outline.
                    Theme.FillRound(e.Graphics, new Rectangle(bx + 3, 15, bw, 30), 15,
                                    Theme.Shadow, Color.Transparent);
                    Theme.FillRound(e.Graphics, bubble, 15, Theme.PrimarySoft,
                                    Theme.Outline, 1.6f);
                    using (SolidBrush brush = new SolidBrush(Theme.Outline))
                    {
                        e.Graphics.FillPolygon(brush, new Point[]
                        {
                            new Point(bx + 4, 21), new Point(bx - 9, 28), new Point(bx + 4, 35)
                        });
                    }
                    Theme.DrawText(e.Graphics, mascotLine, bubbleFont, Theme.Ink,
                                   new Rectangle(bx + 14, 11, bw - 28, 32));
                }
            }
            Theme.DrawText(e.Graphics,
                Theme.Quirky ? "whack-a-menu  ·  右键不该比开始菜单还长"
                             : "whack-a-menu  ·  ContextMenu Cleaner for Windows 11",
                Theme.MakeFont(8f), Theme.TextMuted, new Rectangle(62, 30, 360, 24));
            using (Pen pen = new Pen(Theme.Outline, 1.1f))
            {
                e.Graphics.DrawLine(pen, 0, th - 1, tw, th - 1);
            }
        }

        private void SidebarPaint(object sender, PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            int sw = Theme.LW(sidebar), sh = Theme.LH(sidebar);
            using (Pen pen = new Pen(Theme.Outline, 1.1f))
            {
                e.Graphics.DrawLine(pen, sw - 1, 0, sw - 1, sh);
            }
            Theme.DrawText(e.Graphics, Theme.Quirky ? "按地盘分" : "菜单分类",
                           Theme.MakeFont(8.5f, true), Theme.TextFaint,
                           new Rectangle(20, 12, 160, 28));
            if (Theme.Quirky)
                Theme.DrawSparkle(e.Graphics, sw - 40, 16, 11, Theme.Accent);

            int bottom = Math.Max(sh - 122, 44 + navItems.Length * 44 + 16);
            if (Theme.Quirky)
            {
                Theme.DrawSparkle(e.Graphics, sw - 46, bottom + 2, 12, Theme.Accent);
                Theme.DrawSparkle(e.Graphics, sw - 30, bottom + 26, 7, Theme.PrimaryDark);
            }
            Theme.DrawText(e.Graphics,
                Theme.Quirky ? "不删键，只请走" : "可逆清理 · 不做删除",
                Theme.MakeFont(9f, true), Theme.Text,
                new Rectangle(20, bottom, sw - 32, 26));
            Theme.DrawText(e.Graphics,
                Theme.Quirky ? "全部留档，随时能请回来" : "所有操作留档，随时一键恢复",
                Theme.MakeFont(8f), Theme.TextMuted,
                new Rectangle(20, bottom + 26, sw - 32, 24));
            Theme.DrawText(e.Graphics,
                Theme.Quirky ? "v1.5.0 · 橘座：" + CountQuip() : "PolyForm NC · v1.5.0",
                Theme.MakeFont(8f), Theme.TextFaint,
                new Rectangle(20, bottom + 52, sw - 32, 24));
        }

        private void ToolbarPaint(object sender, PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            using (Pen pen = new Pen(Theme.Outline, 1.1f))
            {
                e.Graphics.DrawLine(pen, 0, Theme.LH(toolbar) - 1,
                                    Theme.LW(toolbar), Theme.LH(toolbar) - 1);
            }
            Theme.DrawText(e.Graphics, "仅看第三方", Theme.MakeFont(9f), Theme.Text, toggleTextRect);
        }

        private void NoticePaint(object sender, PaintEventArgs e)
        {
            if (!noticeVisible) return;
            Theme.Prepare(e.Graphics);
            using (SolidBrush brush = new SolidBrush(Theme.Primary))
            {
                e.Graphics.FillEllipse(brush, 18, 11, 14, 14);
            }
            Theme.DrawCentered(e.Graphics, "i", Theme.MakeFont(8f, true), Theme.Ink,
                               new Rectangle(16, 8, 18, 20));
            Theme.DrawText(e.Graphics,
                Theme.Quirky ? "普通权限：HKLM 和 Win11 组件请不动，得叫管理员来"
                             : "当前为普通权限：HKLM 中的菜单项与 Win11 现代组件需要管理员权限才能清理",
                Theme.MakeFont(9f), Theme.Ink,
                new Rectangle(40, 2, Theme.LW(noticeBar) - 200, 32));
        }

        private void FooterPaint(object sender, PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            using (Pen pen = new Pen(Theme.Outline, 1.1f))
            {
                e.Graphics.DrawLine(pen, 0, 0, Theme.LW(footer), 0);
            }
            Theme.DrawText(e.Graphics, statusText, Theme.MakeFont(9f),
                           Theme.TextMuted, statusTextRect);
        }

        private void EmptyStatePaint(object sender, PaintEventArgs e)
        {
            if (rows.Count > 0) return;
            Theme.Prepare(e.Graphics);
            int w = Theme.LW(rowsBox), h = Theme.LH(rowsBox);
            int top = Math.Max(20, h / 2 - 60);
            Theme.DrawCentered(e.Graphics,
                Theme.Quirky ? "咻—— 一只菜单条都没有" : "没有匹配的项目",
                Theme.MakeFont(13f, true), Theme.Text, new Rectangle(0, top, w, 32));
            Theme.DrawCentered(e.Graphics, "换个关键词，或者把「仅看第三方」关掉试试",
                               Theme.MakeFont(9f), Theme.TextMuted,
                               new Rectangle(0, top + 36, w, 26));
            if (Theme.Quirky)
                Theme.DrawCentered(e.Graphics, "橘座摊爪：这回真没活儿",
                                   Theme.MakeFont(8f), Theme.TextFaint,
                                   new Rectangle(0, top + 64, w, 22));
        }

        // --------------------------------------------------------------- events

        private void NavClicked(object sender, EventArgs e)
        {
            NavItem clicked = (NavItem)sender;
            category = (Category)clicked.Index;
            foreach (NavItem item in navItems) { item.Active = item == clicked; item.Invalidate(); }
            if (Theme.Quirky) NextMascotLine();
            RefreshList();
        }

        private void SelectAllVisible(object sender, EventArgs e)
        {
            List<ContextMenuItem> visible = VisibleItems();
            int skipped = 0;
            foreach (ContextMenuItem item in visible)
            {
                // In clean mode, system items and already-hidden items cannot be
                // processed by the action button -- don't let "全选" select them.
                if (category != Category.Hidden && (item.IsSystem || item.IsHidden))
                {
                    skipped++;
                    continue;
                }
                item.Checked = true;
            }
            RefreshList();
            ShowToast(Theme.Quirky
                ? (skipped > 0
                    ? "勾了 " + (visible.Count - skipped) + " 项，跳过 " + skipped + " 个惹不起的"
                    : "勾了 " + visible.Count + " 项，剩下的交给橘座")
                : (skipped > 0
                    ? "已勾选 " + (visible.Count - skipped) + " 项，跳过 " + skipped + " 个不可清理项"
                    : "已勾选当前列表中的 " + visible.Count + " 项"));
        }

        private void ForwardWheel(object sender, MouseEventArgs e)
        {
            int delta = (int)(-e.Delta / 2f * Theme.Scale);
            int y = -listHost.AutoScrollPosition.Y + delta;
            listHost.AutoScrollPosition = new Point(0, Math.Max(0, y));
        }

        private void DragWindow()
        {
            NativeMethods.ReleaseCapture();
            NativeMethods.SendMessagePtr(Handle, NativeMethods.WM_NCLBUTTONDOWN,
                                         (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
        }

        private string Pick(string[] pool)
        {
            return pool[rnd.Next(pool.Length)];
        }

        private void NextMascotLine()
        {
            if (!Theme.Quirky) return;
            string next;
            int guard = 0;
            do { next = Pick(MascotLines); guard++; }
            while (next == mascotLine && guard < 8);
            mascotLine = next;
            titleBar.Invalidate();
        }

        /// <summary>Every click talks back; three unlocks a wink, seven an achievement.</summary>
        private void PokeMascot()
        {
            DateTime now = DateTime.Now;
            if ((now - lastLogoClick).TotalSeconds > 1.5) logoClicks = 0;
            lastLogoClick = now;
            logoClicks++;
            if (logoClicks >= 7)
            {
                logoClicks = 0;
                mascotLine = "成就解锁：橘座已经被你点烦了";
                ShowToast("成就解锁：你把橘座点烦了，它宣布罢工三秒");
            }
            else
            {
                NextMascotLine();
                ShowToast(logoClicks == 3 ? "嘿嘿，被你发现了 · 橘座好感度 -1" : mascotLine);
            }
            titleBar.Invalidate();
        }

        private string CountQuip()
        {
            if (allItems.Count > 200) return "菜单比简历长";
            if (allItems.Count > 100) return "有点挤，还能忍";
            return "清爽，我很满意";
        }

        // ------------------------------------------------------------- data flow

        private void Rescan()
        {
            store = HiddenStore.Load();
            scan = RegistryScanner.Scan(store);
            allItems = scan.Items;
            UpdateNavCounts();
            RefreshList();
        }

        private void UpdateNavCounts()
        {
            string query = search.Query;
            for (int i = 0; i < navItems.Length; i++)
            {
                int count = 0;
                foreach (ContextMenuItem item in allItems)
                {
                    if (!MatchesCategory(item, (Category)i)) continue;
                    if (onlyThirdParty && item.IsSystem) continue;
                    if (!item.Matches(query)) continue;
                    count++;
                }
                navItems[i].Count = count;
                navItems[i].Invalidate();
            }
        }

        private static bool MatchesCategory(ContextMenuItem item, Category cat)
        {
            switch (cat)
            {
                case Category.All: return item.Scope != ScopeKind.Modern;
                case Category.File: return item.Scope == ScopeKind.File || item.Scope == ScopeKind.AllObjects;
                case Category.Folder: return item.Scope == ScopeKind.Directory || item.Scope == ScopeKind.Folder;
                case Category.Background: return item.Scope == ScopeKind.Background;
                case Category.Drive: return item.Scope == ScopeKind.Drive || item.Scope == ScopeKind.Shortcut ||
                                            item.Scope == ScopeKind.Other;
                case Category.Modern: return item.Scope == ScopeKind.Modern;
                case Category.Hidden: return item.IsHidden;
                default: return true;
            }
        }

        private List<ContextMenuItem> VisibleItems()
        {
            List<ContextMenuItem> result = new List<ContextMenuItem>();
            string query = search.Query;
            foreach (ContextMenuItem item in allItems)
            {
                if (!MatchesCategory(item, category)) continue;
                if (onlyThirdParty && item.IsSystem) continue;
                if (!item.Matches(query)) continue;
                result.Add(item);
            }
            result.Sort(delegate (ContextMenuItem a, ContextMenuItem b)
            {
                int c = a.IsHidden.CompareTo(b.IsHidden);
                if (c != 0) return c;
                c = a.IsSystem.CompareTo(b.IsSystem);
                if (c != 0) return c;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return result;
        }

        private void RefreshList()
        {
            List<ContextMenuItem> visible = VisibleItems();
            rowsBox.SuspendLayout();
            rowsBox.Controls.Clear();
            rows.Clear();

            int rowWidth = Math.Max(Theme.S(220), rowsBox.Width - Theme.S(32));
            for (int i = 0; i < visible.Count; i++)
            {
                MenuRow row = new MenuRow(visible[i]);
                row.SetBounds(Theme.S(16), Theme.S(8 + i * RowPitch), rowWidth, Theme.S(62));
                row.Toggled += RowToggled;
                row.DoubleClick += RowDoubleClick;
                row.MouseWheel += ForwardWheel;
                rowsBox.Controls.Add(row);
                rows.Add(row);
            }
            rowsBox.Height = Math.Max(listHost.ClientSize.Height,
                                      Theme.S(16 + visible.Count * RowPitch + 12));
            rowsBox.ResumeLayout(false);
            listHost.AutoScrollPosition = new Point(0, 0);

            // A vertical scrollbar narrows the client area; re-fit rows to avoid a horizontal one.
            rowsBox.Width = listHost.ClientSize.Width;
            int finalWidth = Math.Max(Theme.S(220), rowsBox.Width - Theme.S(32));
            foreach (MenuRow row in rows) row.Width = finalWidth;

            UpdateNavCounts();
            UpdateFooter();
        }

        private void RowToggled(object sender, EventArgs e)
        {
            MenuRow row = (MenuRow)sender;
            if (row.Item.Checked && row.Item.IsSystem && category != Category.Hidden)
            {
                DialogResult answer = MessageBox.Show(this,
                    "「" + row.Item.Name + "」被识别为 Windows 内置组件。\n" +
                    "这玩意惹不起：隐藏它可能影响系统功能，确定要勾吗？",
                    "这个惹不起", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes)
                {
                    row.Item.Checked = false;
                    row.Invalidate();
                }
            }
            UpdateFooter();
        }

        private void RowDoubleClick(object sender, EventArgs e)
        {
            MenuRow row = sender as MenuRow;
            if (row == null) return;
            ContextMenuItem item = row.Item;
            MessageBox.Show(this,
                "名称：" + item.Name + "\n" +
                "厂商：" + (string.IsNullOrEmpty(item.Publisher) ? "未知" : item.Publisher) + "\n" +
                "类型：" + item.KindText + "\n" +
                "作用域：" + item.ScopeText + "\n" +
                "注册表：" + item.Hive + "\\" + item.RegistryPath + "\n" +
                "CLSID：" + item.Clsid + "\n" +
                "程序文件：" + item.BinaryPath + "\n" +
                "状态：" + (item.IsHidden ? "已隐藏（可恢复）" : "正常显示"),
                "菜单项详情", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void UpdateFooter()
        {
            int selected = 0;
            int cleanableSelected = 0;
            int hiddenSelected = 0;
            int hidden = 0;
            foreach (ContextMenuItem item in allItems)
            {
                if (item.Checked) selected++;
                if (item.Checked && !item.IsHidden) cleanableSelected++;
                if (item.IsHidden) hidden++;
                if (item.IsHidden && item.Checked) hiddenSelected++;
            }
            statusText = "共 " + allItems.Count + " 项  ·  当前显示 " + rows.Count +
                         " 项  ·  已选 " + selected + " 项  ·  已隐藏 " + hidden + " 项" +
                         (scan != null && scan.Warnings.Count > 0
                             ? "  ·  扫描提示 " + scan.Warnings.Count + " 条" : "");
            footer.Invalidate();
            bool restoring = category == Category.Hidden;
            // Only count items the current action can actually process: hidden items
            // checked in another category must not light up the "clean" button.
            btnApply.Text = restoring
                ? (Theme.Quirky ? "放回来 (" + hiddenSelected + ")"
                                : "恢复选中项 (" + hiddenSelected + ")")
                : (Theme.Quirky ? "请走选中项 (" + cleanableSelected + ")"
                                : "清理选中项 (" + cleanableSelected + ")");
            btnApply.Style = restoring ? ButtonStyle.Success : ButtonStyle.Primary;
            btnApply.Enabled = restoring ? hiddenSelected > 0 : cleanableSelected > 0;
            btnApply.Invalidate();
        }

        // ------------------------------------------------------------- actions

        private void ApplySelection()
        {
            bool restoring = category == Category.Hidden;
            List<ContextMenuItem> selected = allItems.Where(i => i.Checked).ToList();
            List<ContextMenuItem> targets = restoring
                ? selected.Where(i => i.IsHidden).ToList()
                : selected.Where(i => !i.IsHidden).ToList();

            if (targets.Count == 0)
            {
                ShowToast(Theme.Quirky
                    ? (restoring ? "先在「已隐藏」里挑几个要放回来的" : "一个都没勾 · 橘座不会替你动手")
                    : (restoring ? "请先在「已隐藏」中勾选要恢复的项目" : "请先勾选要清理的项目"), true);
                return;
            }

            string preview = string.Join("、", targets.Take(4).Select(i => i.Name).ToArray());
            if (targets.Count > 4) preview += " 等 " + targets.Count + " 项";
            string verb = restoring ? "恢复" : "隐藏";
            DialogResult confirm = MessageBox.Show(this,
                "即将" + verb + "以下 " + targets.Count + " 项右键菜单：\n\n" + preview + "\n\n" +
                (restoring
                    ? "放回来之后，它们会重新出现在右键菜单里。"
                    : "不会真的删掉，只是先请到一边：不删注册表键，随时可以在「已隐藏」里请回来。") +
                "\n\n继续吗？",
                Theme.Quirky ? (restoring ? "放它们回来？" : "请它们出去？")
                             : (restoring ? "确认恢复" : "确认清理"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            if (!MenuManager.IsAdministrator && targets.Any(MenuManager.RequiresAdmin))
            {
                DialogResult elevate = MessageBox.Show(this,
                    "其中有项目位于 HKLM（系统级注册表），需要管理员权限。\n\n" +
                    "是否以管理员身份重启本程序？已勾选的项目会被保留。",
                    "需要管理员权限", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (elevate == DialogResult.Yes)
                {
                    SaveSelection();
                    MenuManager.RestartElevated();
                    Close();
                }
                return;
            }

            int succeeded = 0;
            List<string> failures = new List<string>();
            foreach (ContextMenuItem item in targets)
            {
                try
                {
                    OperationResult result = restoring
                        ? MenuManager.Restore(item, store)
                        : MenuManager.Hide(item, store);
                    if (result.Success) { succeeded++; item.Checked = false; }
                    else failures.Add(item.Name + "：" + result.Message);
                }
                catch (Exception ex)
                {
                    // One unexpected failure must not abort the whole batch.
                    failures.Add(item.Name + "：" + ex.Message);
                }
            }
            MenuManager.RefreshShell();
            Rescan();

            if (failures.Count > 0)
            {
                MessageBox.Show(this,
                    "成功 " + succeeded + " 项，失败 " + failures.Count + " 项：\n\n" +
                    string.Join("\n", failures.Take(8).ToArray()) +
                    (failures.Count > 8 ? "\n…" : ""),
                    "操作结果", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                ShowToast(Theme.Quirky
                    ? string.Format(restoring ? Pick(RestoreLines) : Pick(CleanLines), succeeded)
                    : "已" + verb + " " + succeeded + " 项 · 建议点击「重启资源管理器」立即生效");
                if (Theme.Quirky) NextMascotLine();
            }
        }

        private void ExportCsv(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV 文件 (*.csv)|*.csv";
                dialog.FileName = "右键菜单清单_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("名称,厂商,类型,作用域,来源,注册表路径,CLSID,程序文件,状态");
                foreach (ContextMenuItem item in VisibleItems())
                {
                    sb.Append(Csv(item.Name)).Append(',');
                    sb.Append(Csv(item.Publisher)).Append(',');
                    sb.Append(Csv(item.KindText)).Append(',');
                    sb.Append(Csv(item.ScopeText)).Append(',');
                    sb.Append(Csv(item.Hive)).Append(',');
                    sb.Append(Csv(item.RegistryPath)).Append(',');
                    sb.Append(Csv(item.Clsid)).Append(',');
                    sb.Append(Csv(item.BinaryPath)).Append(',');
                    sb.AppendLine(Csv(item.IsHidden ? "已隐藏" : "正常"));
                }
                try
                {
                    File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
                    ShowToast("清单已导出：" + Path.GetFileName(dialog.FileName));
                }
                catch (Exception ex)
                {
                    ShowToast("导出失败：" + ex.Message, true);
                }
            }
        }

        private static string Csv(string value)
        {
            string s = value ?? "";
            if (s.Contains("\"") || s.Contains(",") || s.Contains("\n"))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private void SaveSelection()
        {
            try
            {
                string[] ids = allItems.Where(i => i.Checked).Select(i => i.Id).ToArray();
                File.WriteAllLines(SelectionFile, ids);
            }
            catch (Exception) { /* best effort */ }
        }

        private void RestoreSelection()
        {
            try
            {
                if (!File.Exists(SelectionFile)) return;
                HashSet<string> ids = new HashSet<string>(File.ReadAllLines(SelectionFile),
                                                          StringComparer.OrdinalIgnoreCase);
                foreach (ContextMenuItem item in allItems)
                    if (ids.Contains(item.Id)) item.Checked = true;
                File.Delete(SelectionFile);
                RefreshList();
                ShowToast("勾选还在，接着请走吧");
            }
            catch (Exception) { /* best effort */ }
        }

        // ------------------------------------------------------------ updates

        private void BeginUpdateCheck(bool manual)
        {
            if (updateChecking) return;
            updateChecking = true;
            if (manual) ShowToast(Theme.Quirky ? "橘座去 GitHub 瞄了一眼…" : "正在检查更新…");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo info = UpdateChecker.Check(Program.Version);
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        updateChecking = false;
                        OnUpdateChecked(info, manual);
                    }));
                }
                catch (Exception) { updateChecking = false; } //窗口已关闭
            });
        }

        private void OnUpdateChecked(UpdateInfo info, bool manual)
        {
            if (info.Available)
            {
                if (!manual && info.LatestVersion == ReadDismissedVersion()) return;
                string notes = string.IsNullOrEmpty(info.Notes) ? "" : info.Notes + "\n\n";
                DialogResult answer = MessageBox.Show(this,
                    "发现新版本 " + info.LatestVersion + "（当前 " + Program.Version + "）\n\n" +
                    notes + "去下载页看看吗？\n\n选「否」后这个版本就不再提醒。",
                    Theme.Quirky ? "橘座：有新版本啦" : "发现新版本",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (answer == DialogResult.Yes)
                {
                    try { Process.Start(info.ReleaseUrl); }
                    catch (Exception)
                    {
                        ShowToast("打不开浏览器，请手动访问 " + UpdateChecker.ReleasesUrl, true);
                    }
                }
                else SaveDismissedVersion(info.LatestVersion);
                return;
            }
            if (manual)
            {
                ShowToast(string.IsNullOrEmpty(info.Error)
                    ? (Theme.Quirky ? "已经是最新版了，橘座很满意" : "当前已是最新版本")
                    : "检查更新失败：" + info.Error,
                    !string.IsNullOrEmpty(info.Error));
            }
        }

        private static string DismissFile
        {
            get { return Path.Combine(HiddenStore.DirectoryPath, "update-dismiss.txt"); }
        }

        private static string ReadDismissedVersion()
        {
            try { return File.Exists(DismissFile) ? File.ReadAllText(DismissFile).Trim() : ""; }
            catch (Exception) { return ""; }
        }

        private static void SaveDismissedVersion(string version)
        {
            try
            {
                Directory.CreateDirectory(HiddenStore.DirectoryPath);
                File.WriteAllText(DismissFile, version ?? "");
            }
            catch (Exception) { /* non-critical preference */ }
        }

        private void ShowToast(string message, bool error = false)
        {
            toastMessage = message;
            toastError = error;
            toast.Visible = true;
            toast.Invalidate();
            toast.BringToFront();
            toastTimer.Stop();
            toastTimer.Start();
        }
    }

    // ------------------------------------------------------------- about dialog

    public class AboutDialog : Form
    {
        private Image mascot;
        public Action OnCheckUpdate;

        /// <summary>The full-colour mascot is embedded as a Win32 resource by build.ps1.</summary>
        private static Image LoadMascot()
        {
            try
            {
                System.IO.Stream stream = System.Reflection.Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("ContextMenuCleaner.Assets.Mascot.png");
                if (stream == null) return null;
                using (stream)
                using (Image source = Image.FromStream(stream))
                {
                    return new Bitmap(source);
                }
            }
            catch (Exception) { return null; }
        }

        public AboutDialog(Icon icon)
        {
            Text = "关于";
            ClientSize = new Size(Theme.S(420), Theme.S(306));
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Surface;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            if (icon != null) Icon = icon;
            mascot = LoadMascot();

            Panel body = new Panel();
            body.Dock = DockStyle.Fill;
            body.BackColor = Theme.Surface;
            Theme.EnableDoubleBuffer(body);
            body.Paint += delegate (object s, PaintEventArgs e)
            {
                Theme.Prepare(e.Graphics);
                int w = Theme.LW(body), h = Theme.LH(body);
                Theme.FillRound(e.Graphics, new Rectangle(3, 3, w - 5, h - 5), 14,
                                Theme.Shadow, Color.Transparent);
                Theme.FillRound(e.Graphics, new Rectangle(0, 0, w - 5, h - 5), 14,
                                Theme.Surface, Theme.Outline, 1.8f);
                if (mascot != null)
                {
                    e.Graphics.DrawImage(mascot, new Rectangle(28, 24, 88, 88));
                }
                else if (icon != null)
                {
                    try
                    {
                        using (Bitmap logo = icon.ToBitmap())
                        {
                            e.Graphics.DrawImage(logo, new Rectangle(30, 30, 40, 40));
                        }
                    }
                    catch (Exception) { e.Graphics.DrawIcon(icon, new Rectangle(30, 30, 40, 40)); }
                }
                Theme.DrawText(e.Graphics, "右键菜单清理", Theme.MakeFont(13f, true), Theme.Text,
                               new Rectangle(128, 30, 200, 28));
                if (Theme.Quirky)
                {
                    Theme.DrawPill(e.Graphics, 306, 34, "嘿嘿", Theme.MakeFont(8f),
                                   Theme.Ink, Theme.PrimarySoft);
                    Theme.DrawSparkle(e.Graphics, 386, 40, 10, Theme.Accent);
                }
                Theme.DrawText(e.Graphics, "whack-a-menu  ·  v1.5.0  ·  PolyForm NC",
                               Theme.MakeFont(8.5f), Theme.TextMuted, new Rectangle(128, 60, 260, 20));
                if (Theme.Quirky)
                    Theme.DrawText(e.Graphics, "Nine lives, zero patience.",
                                   Theme.MakeFont(8f), Theme.TextFaint,
                                   new Rectangle(128, 84, 260, 20));
                string text =
                    "右键一下，蹦出二十几个「用 XX 打开」。\n" +
                    "只干一件事：列出来，勾一勾，请走。\n\n" +
                    "不删注册表键，操作留档，随时能请回来。\n" +
                    "更新检查只访问 GitHub，不上传任何数据。\n\n" +
                    (Theme.Quirky ? "九条命，零耐心。\n" : "") +
                    "PolyForm NC 1.0.0 · 记录：" +
                    Path.GetFileName(HiddenStore.DirectoryPath);
                using (SolidBrush brush = new SolidBrush(Theme.Text))
                using (StringFormat format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Near;
                    format.LineAlignment = StringAlignment.Near;
                    e.Graphics.DrawString(text, Theme.MakeFont(8.5f), brush,
                                          new RectangleF(32, 124, 360, 118), format);
                }
            };
            body.MouseDown += delegate
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessagePtr(Handle, NativeMethods.WM_NCLBUTTONDOWN,
                                             (IntPtr)NativeMethods.HTCAPTION, IntPtr.Zero);
            };
            Controls.Add(body);

            ModernButton openDir = new ModernButton
            {
                Text = "打开记录目录",
                Style = ButtonStyle.Secondary,
                Width = Theme.S(122),
                Height = Theme.S(34)
            };
            openDir.SetBounds(Theme.S(32), Theme.S(246), Theme.S(122), Theme.S(34));
            openDir.Click += delegate
            {
                try { Process.Start("explorer.exe", HiddenStore.DirectoryPath); }
                catch (Exception) { /* directory may not exist yet */ }
            };
            body.Controls.Add(openDir);

            ModernButton check = new ModernButton
            {
                Text = "检查更新",
                Style = ButtonStyle.Secondary,
                Width = Theme.S(110),
                Height = Theme.S(34)
            };
            check.SetBounds(Theme.S(166), Theme.S(246), Theme.S(110), Theme.S(34));
            check.Click += delegate
            {
                Close();
                if (OnCheckUpdate != null) OnCheckUpdate();
            };
            body.Controls.Add(check);

            ModernButton close = new ModernButton
            {
                Text = "关闭",
                Style = ButtonStyle.Primary,
                Width = Theme.S(90),
                Height = Theme.S(34)
            };
            close.SetBounds(Theme.S(296), Theme.S(246), Theme.S(90), Theme.S(34));
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

        protected override void Dispose(bool disposing)
        {
            if (disposing && mascot != null) { mascot.Dispose(); mascot = null; }
            base.Dispose(disposing);
        }
    }
}
