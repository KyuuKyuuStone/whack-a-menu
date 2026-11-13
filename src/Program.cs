using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using ContextMenuCleaner.Core;
using ContextMenuCleaner.Models;
using ContextMenuCleaner.UI;

namespace ContextMenuCleaner
{
    internal static class Program
    {
        private const string MutexName = "Local\\ContextMenuCleaner_SingleInstance_v1";
        public const string Version = "1.6.0";

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Windows 11 display scaling drives every UI dimension.
            Theme.SetScaleFromSystem();
            string scaleArg = GetArgValue(args, "--scale");
            if (!string.IsNullOrEmpty(scaleArg))
            {
                float parsed;
                if (float.TryParse(scaleArg, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out parsed))
                    Theme.OverrideScale(parsed);
            }

            string renderPath = GetArgValue(args, "--render");
            if (!string.IsNullOrEmpty(renderPath))
                return RenderPreview(renderPath, HasArg(args, "--screen"));
            string aboutPath = GetArgValue(args, "--about");
            if (!string.IsNullOrEmpty(aboutPath)) return RenderAbout(aboutPath);
            if (HasArg(args, "--roundtrip")) return RunRoundTrip();
            if (HasArg(args, "--selftest")) return RunSelfTest();
            if (HasArg(args, "--check-update")) return RunUpdateCheck();
            if (HasArg(args, "--help"))
            {
                NativeMethods.AttachOutputConsole();
                Console.WriteLine("whack-a-menu " + Version);
                Console.WriteLine("  --selftest   扫描真实注册表并输出报告（不启动界面、不修改注册表）");
                Console.WriteLine("  --render <png路径>  离屏渲染主界面到 PNG（用于文档截图 / UI 自检）");
                Console.WriteLine("  --screen         配合 --render 额外保存一份真实屏幕截图 *-screen.png");
                Console.WriteLine("  --about <png路径>  离屏渲染「关于」框（验证嵌入的吉祥物大图与排版）");
                Console.WriteLine("  --roundtrip  用临时 HKCU 测试键验证隐藏/恢复可逆（结束自动清理）");
                Console.WriteLine("  --scale <倍数>  覆盖自动 DPI 缩放（调试/文档截图用，如 1.5）");
                Console.WriteLine("  --check-update  查询 GitHub Releases 最新版本（只读，不修改任何东西）");
                Console.WriteLine("  --elevated   内部使用：管理员重启后恢复勾选状态");
                return 0;
            }

            bool firstInstance;
            using (Mutex mutex = new Mutex(true, MutexName, out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show(Theme.Quirky ? "已经开着一个了，橘座正在舔毛。"
                                                 : "右键菜单清理已经在运行了。", "提示",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                try
                {
                    Application.Run(new MainForm(HasArg(args, "--elevated")));
                }
                catch (Exception ex)
                {
                    string log = Path.Combine(Path.GetTempPath(), "cmc-crash.log");
                    try { File.WriteAllText(log, ex.ToString()); }
                    catch (Exception) { /* ignore */ }
                    MessageBox.Show("程序遇到错误：\n\n" + ex.Message + "\n\n日志：" + log,
                                    "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
            return 0;
        }

        private static bool HasArg(string[] args, string flag)
        {
            if (args == null) return false;
            foreach (string arg in args)
            {
                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string GetArgValue(string[] args, string flag)
        {
            if (args == null) return null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }

        /// <summary>
        /// Draws the whole form into a PNG without needing a visible desktop session.
        /// Handy for documentation screenshots and for verifying layout in CI.
        /// </summary>
        private static int RenderPreview(string path, bool withScreenShot)
        {
            try
            {
                using (MainForm form = new MainForm(false))
                {
                    form.ShowInTaskbar = false;
                    form.TopMost = withScreenShot;
                    form.Show();
                    if (withScreenShot)
                    {
                        form.Activate();
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(400);

                        // Optional real on-screen capture, used to verify owner-drawn
                        // text on an actual display. Skipped by default so a simple
                        // --render never litters the docs folder with extra files.
                        string screenPath = Path.Combine(
                            Path.GetDirectoryName(Path.GetFullPath(path)),
                            Path.GetFileNameWithoutExtension(path) + "-screen.png");
                        try
                        {
                            using (Bitmap shot = new Bitmap(form.Width, form.Height))
                            {
                                using (Graphics g = Graphics.FromImage(shot))
                                {
                                    g.CopyFromScreen(form.Left, form.Top, 0, 0, form.Size);
                                }
                                shot.Save(screenPath, ImageFormat.Png);
                            }
                            Console.WriteLine("屏幕预览：" + screenPath);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("屏幕抓取不可用：" + ex.Message);
                        }
                    }
                    else
                    {
                        Application.DoEvents();
                    }

                    form.Refresh();
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                        bitmap.Save(path, ImageFormat.Png);
                    }
                    form.Close();
                }
                Console.WriteLine("界面预览已生成：" + Path.GetFullPath(path));
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("渲染失败：" + ex);
                return 1;
            }
        }

        /// <summary>
        /// Renders the About dialog offscreen so the embedded full-colour mascot and
        /// the copy layout can be checked without clicking through the UI.
        /// </summary>
        private static int RenderAbout(string path)
        {
            try
            {
                Icon icon = null;
                try { icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
                catch (Exception) { icon = SystemIcons.Application; }
                using (AboutDialog dialog = new AboutDialog(icon))
                {
                    dialog.ShowInTaskbar = false;
                    dialog.Show();
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(150);
                    dialog.Refresh();
                    using (Bitmap bitmap = new Bitmap(dialog.Width, dialog.Height))
                    {
                        dialog.DrawToBitmap(bitmap, new Rectangle(0, 0, dialog.Width, dialog.Height));
                        bitmap.Save(path, ImageFormat.Png);
                    }
                    dialog.Close();
                }
                Console.WriteLine("关于框预览已生成：" + Path.GetFullPath(path));
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("关于框渲染失败：" + ex);
                return 1;
            }
        }

        /// <summary>Console probe for the update checker: prints what GitHub reports.</summary>
        private static int RunUpdateCheck()
        {
            NativeMethods.AttachOutputConsole();
            try { Console.OutputEncoding = Encoding.UTF8; }
            catch (Exception) { /* console may be unavailable */ }
            UpdateInfo info = UpdateChecker.Check(Version);
            Console.WriteLine("当前版本：" + Version);
            Console.WriteLine("最新版本：" + (string.IsNullOrEmpty(info.LatestVersion)
                                             ? "未知" : info.LatestVersion));
            Console.WriteLine("可用更新：" + (info.Available ? "是" : "否"));
            Console.WriteLine("发布页面：" + info.ReleaseUrl);
            if (!string.IsNullOrEmpty(info.Notes)) Console.WriteLine("更新说明：" + info.Notes);
            if (!string.IsNullOrEmpty(info.Error)) Console.WriteLine("提示：" + info.Error);
            return 0;
        }

        /// <summary>
        /// Writes two throwaway HKCU keys, hides them through the real engine, verifies
        /// the registry state, restores them, then removes the test keys. This is the
        /// only mode that writes to the registry, and only to its own CMC_SelfTest_* keys.
        /// </summary>
        private static int RunRoundTrip()
        {
            NativeMethods.AttachOutputConsole();
            try { Console.OutputEncoding = Encoding.UTF8; }
            catch (Exception) { /* console may be unavailable */ }

            const string VerbPath = "Software\\Classes\\*\\shell\\CMC_SelfTest_Entry";
            const string HandlerPath =
                "Software\\Classes\\*\\shellex\\ContextMenuHandlers\\CMC_SelfTest_Handler";
            const string FakeClsid = "{00000000-0000-0000-0000-000000000001}";

            int failures = 0;
            RegistryKey root = null;
            try
            {
                root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
                using (RegistryKey key = root.CreateSubKey(VerbPath))
                {
                    key.SetValue("", "自检测试项", RegistryValueKind.String);
                    key.SetValue("DelegateExecute", FakeClsid, RegistryValueKind.String);
                }
                using (RegistryKey key = root.CreateSubKey(HandlerPath))
                {
                    key.SetValue("", FakeClsid, RegistryValueKind.String);
                }

                HiddenStore store = HiddenStore.Load();
                ContextMenuItem verb = TestItem(VerbPath, EntryKind.ShellVerb);
                ContextMenuItem handler = TestItem(HandlerPath, EntryKind.ComHandler);

                OperationResult a = MenuManager.Hide(verb, store);
                OperationResult b = MenuManager.Hide(handler, store);
                failures += Check("隐藏静态命令", a.Success, a.Message);
                failures += Check("隐藏 COM 扩展", b.Success, b.Message);
                failures += Check("写入 ProgrammaticAccessOnly",
                                  HasValue(root, VerbPath, "ProgrammaticAccessOnly"), "");
                failures += Check("DelegateExecute 已备份",
                                  HasValue(root, VerbPath, "CMC_DelegateExecute"), "");
                failures += Check("DelegateExecute 已移除",
                                  !HasValue(root, VerbPath, "DelegateExecute"), "");
                failures += Check("备份原 CLSID", HasValue(root, HandlerPath, "CMC_OriginalCLSID"), "");
                failures += Check("默认值已清空", ReadDefault(root, HandlerPath) == "", "");
                failures += Check("隐藏记录已落盘", HiddenStore.Load().Contains(verb.Id), "");

                OperationResult c = MenuManager.Restore(verb, HiddenStore.Load());
                OperationResult d = MenuManager.Restore(handler, HiddenStore.Load());
                failures += Check("恢复静态命令", c.Success, c.Message);
                failures += Check("恢复 COM 扩展", d.Success, d.Message);
                failures += Check("隐藏标记已清除",
                                  !HasValue(root, VerbPath, "ProgrammaticAccessOnly"), "");
                failures += Check("DelegateExecute 已恢复",
                                  ReadValue(root, VerbPath, "DelegateExecute") == FakeClsid, "");
                failures += Check("DelegateExecute 备份已清理",
                                  !HasValue(root, VerbPath, "CMC_DelegateExecute"), "");
                failures += Check("原 CLSID 已写回", ReadDefault(root, HandlerPath) == FakeClsid, "");
                failures += Check("备份值已清理",
                                  !HasValue(root, HandlerPath, "CMC_OriginalCLSID"), "");
                failures += Check("隐藏记录已移除", !HiddenStore.Load().Contains(verb.Id), "");

                // Regression: a record whose registry key was uninstalled must be
                // removable without any registry write (and without elevation).
                const string OrphanPath = "Software\\Classes\\*\\shell\\CMC_Orphan_Test";
                HiddenStore orphanStore = HiddenStore.Load();
                HiddenRecord orphanRecord = new HiddenRecord();
                orphanRecord.Id = "HKLM|" + OrphanPath;
                orphanRecord.Name = "自检孤立记录";
                orphanRecord.Hive = "HKLM";
                orphanRecord.RegistryPath = OrphanPath;
                orphanRecord.Kind = (int)EntryKind.ShellVerb;
                orphanStore.Add(orphanRecord);
                orphanStore.Save();
                try
                {
                    ContextMenuItem orphanItem = TestItem(OrphanPath, EntryKind.ShellVerb);
                    orphanItem.Id = orphanRecord.Id;
                    orphanItem.Hive = "HKLM";
                    OperationResult e = MenuManager.Restore(orphanItem, HiddenStore.Load());
                    failures += Check("孤立记录可清除", e.Success, e.Message);
                    failures += Check("孤立记录已移除",
                                      !HiddenStore.Load().Contains(orphanRecord.Id), "");
                }
                finally
                {
                    HiddenStore cleanup = HiddenStore.Load();
                    cleanup.Remove(orphanRecord.Id);
                    cleanup.Save();
                }

                Console.WriteLine(failures == 0 ? "往返自检：全部通过" :
                                                  "往返自检：失败 " + failures + " 项");
                return failures == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine("往返自检异常：" + ex);
                return 1;
            }
            finally
            {
                if (root != null)
                {
                    try { root.DeleteSubKeyTree(VerbPath, false); } catch (Exception) { }
                    try { root.DeleteSubKeyTree(HandlerPath, false); } catch (Exception) { }
                    root.Dispose();
                }
            }
        }

        private static ContextMenuItem TestItem(string path, EntryKind kind)
        {
            ContextMenuItem item = new ContextMenuItem();
            item.Id = "HKCU|" + path;
            item.Name = "自检测试项";
            item.Hive = "HKCU";
            item.RegistryPath = path;
            item.Kind = kind;
            item.Scope = ScopeKind.File;
            item.Clsid = "{00000000-0000-0000-0000-000000000001}";
            return item;
        }

        private static int Check(string label, bool condition, string detail)
        {
            Console.WriteLine((condition ? "  [PASS] " : "  [FAIL] ") + label +
                              (condition ? "" : " " + detail));
            return condition ? 0 : 1;
        }

        private static bool HasValue(RegistryKey root, string path, string name)
        {
            using (RegistryKey key = root.OpenSubKey(path, false))
            {
                if (key == null) return false;
                foreach (string existing in key.GetValueNames())
                {
                    if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)) return true;
                }
                return false;
            }
        }

        private static string ReadValue(RegistryKey root, string path, string name)
        {
            using (RegistryKey key = root.OpenSubKey(path, false))
            {
                if (key == null) return null;
                object value = key.GetValue(name, null);
                return value == null ? null : Convert.ToString(value);
            }
        }

        private static string ReadDefault(RegistryKey root, string path)
        {
            using (RegistryKey key = root.OpenSubKey(path, false))
            {
                if (key == null) return null;
                object value = key.GetValue("", null);
                return value == null ? null : Convert.ToString(value);
            }
        }

        /// <summary>
        /// Read-only integration check: enumerates the real registry, resolves icons,
        /// and writes a report next to the exe. Safe to run in CI.
        /// </summary>
        private static int RunSelfTest()
        {
            NativeMethods.AttachOutputConsole();
            try { Console.OutputEncoding = Encoding.UTF8; }
            catch (Exception) { /* console may be unavailable */ }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("whack-a-menu v" + Version + " · 自检报告");
            sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("系统：" + Environment.OSVersion.VersionString);
            sb.AppendLine("权限：" + (MenuManager.IsAdministrator ? "管理员" : "普通用户"));
            sb.AppendLine("记录文件：" + HiddenStore.FilePath + (File.Exists(HiddenStore.FilePath) ? "（已存在）" : "（尚未创建）"));
            sb.AppendLine();

            HiddenStore store = HiddenStore.Load();
            ScanResult scan = RegistryScanner.Scan(store);
            List<ContextMenuItem> items = scan.Items;

            sb.AppendLine("扫描分支：" + scan.BranchCount);
            sb.AppendLine("菜单项总数：" + items.Count);
            sb.AppendLine("隐藏记录：" + store.Records.Count);
            sb.AppendLine("第三方：" + items.Count(i => !i.IsSystem) +
                          "   系统内置：" + items.Count(i => i.IsSystem) +
                          "   已隐藏：" + items.Count(i => i.IsHidden));
            sb.AppendLine("带图标来源：" + items.Count(i => !string.IsNullOrEmpty(i.IconSpec) ||
                                                          !string.IsNullOrEmpty(i.BinaryPath)));
            sb.AppendLine("带厂商信息：" + items.Count(i => !string.IsNullOrEmpty(i.Publisher)));
            sb.AppendLine();

            sb.AppendLine("按类型：");
            foreach (EntryKind kind in Enum.GetValues(typeof(EntryKind)))
            {
                sb.AppendLine("  " + kind + "：" + items.Count(i => i.Kind == kind));
            }
            sb.AppendLine();
            sb.AppendLine("按作用域：");
            foreach (ScopeKind scope in Enum.GetValues(typeof(ScopeKind)))
            {
                int count = items.Count(i => i.Scope == scope);
                if (count > 0) sb.AppendLine("  " + scope + "：" + count);
            }
            sb.AppendLine();

            sb.AppendLine("前 40 项：");
            foreach (ContextMenuItem item in items.Take(40))
            {
                sb.AppendLine("  [" + (item.IsSystem ? "系统" : "三方") + "]" +
                              (item.IsHidden ? "[已隐藏]" : "") +
                              " " + item.Name +
                              " | " + item.KindText +
                              " | " + item.ScopeText +
                              " | " + item.Hive +
                              " | " + (string.IsNullOrEmpty(item.Publisher) ? "-" : item.Publisher));
            }
            sb.AppendLine();

            if (scan.Warnings.Count > 0)
            {
                sb.AppendLine("警告（" + scan.Warnings.Count + "）：");
                foreach (string warning in scan.Warnings.Take(20)) sb.AppendLine("  " + warning);
            }
            else
            {
                sb.AppendLine("警告：无");
            }
            sb.AppendLine();
            sb.AppendLine("自检为只读操作，未修改任何注册表项。");

            string report = sb.ToString();
            string reportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest-report.txt");
            try { File.WriteAllText(reportPath, report, new UTF8Encoding(true)); }
            catch (Exception ex) { Console.WriteLine("无法写入报告：" + ex.Message); }

            try { Console.Write(report); }
            catch (Exception) { /* console may be unavailable */ }
            Console.WriteLine("报告：" + reportPath);
            return 0;
        }
    }
}
