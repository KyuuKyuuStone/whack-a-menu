using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;
using ContextMenuCleaner.Models;

namespace ContextMenuCleaner.Core
{
    public class OperationResult
    {
        public bool Success;
        public string Message = "";

        public static OperationResult Ok(string message)
        {
            return new OperationResult { Success = true, Message = message };
        }

        public static OperationResult Fail(string message)
        {
            return new OperationResult { Success = false, Message = message };
        }
    }

    /// <summary>
    /// Applies reversible hide / restore operations to context-menu registry entries.
    /// Nothing is ever deleted: values are backed up under CMC_* names and the whole
    /// operation is journalled in HiddenStore.
    /// </summary>
    public static class MenuManager
    {
        private const string BackupClsidValue = "CMC_OriginalCLSID";
        private const string BackupHandlerValue = "CMC_ExplorerCommandHandler";
        private const string BackupDelegateValue = "CMC_DelegateExecute";
        private const string ProgrammaticAccessOnly = "ProgrammaticAccessOnly";

        private const string HklmBlocked =
            "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Shell Extensions\\Blocked";
        private const string HkcuBlocked =
            "Software\\Microsoft\\Windows\\CurrentVersion\\Shell Extensions\\Blocked";

        private static bool? administratorCache;

        public static bool IsAdministrator
        {
            get
            {
                if (administratorCache.HasValue) return administratorCache.Value;
                try
                {
                    using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                    {
                        administratorCache = new WindowsPrincipal(identity)
                            .IsInRole(WindowsBuiltInRole.Administrator);
                    }
                }
                catch (Exception) { administratorCache = false; }
                return administratorCache.Value;
            }
        }

        /// <summary>Whether this entry can be changed from the current process.</summary>
        public static bool RequiresAdmin(ContextMenuItem item)
        {
            if (item == null) return false;
            return item.Hive == "HKLM" || item.Kind == EntryKind.PackagedCom;
        }

        public static OperationResult Hide(ContextMenuItem item, HiddenStore store)
        {
            if (item == null) return OperationResult.Fail("无效项目");
            if (store.Contains(item.Id)) return OperationResult.Ok("该项已在隐藏列表中");
            if (RequiresAdmin(item) && !IsAdministrator)
                return OperationResult.Fail("需要管理员权限：请以管理员身份运行本程序后重试");

            HiddenRecord record = NewRecord(item);
            OperationResult op;

            try
            {
                switch (item.Kind)
                {
                    case EntryKind.ComHandler:
                        op = HideComHandler(item, record);
                        break;
                    case EntryKind.PackagedCom:
                        op = HidePackaged(item, record);
                        break;
                    default:
                        op = HideShellVerb(item, record);
                        break;
                }
            }
            catch (UnauthorizedAccessException)
            {
                return OperationResult.Fail("权限不足：请以管理员身份运行本程序");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("写入注册表失败：" + ex.Message);
            }

            if (!op.Success) return op;
            store.Add(record);
            try { store.Save(); }
            catch (Exception ex)
            {
                // Registry change is still reversible through the CMC_* backup values,
                // but the UI ledger is missing -- tell the user instead of crashing.
                return OperationResult.Ok("已隐藏（但记录保存失败：" + ex.Message + "）");
            }
            return op;
        }

        public static OperationResult Restore(ContextMenuItem item, HiddenStore store)
        {
            HiddenRecord record = store.Find(item.Id);
            if (record == null)
                return OperationResult.Fail("找不到隐藏记录，无法恢复（可能由其他工具隐藏）");

            // The software was uninstalled: no registry write is needed, so this
            // must work without elevation (otherwise orphan records get stuck).
            if ((EntryKind)record.Kind != EntryKind.PackagedCom &&
                !KeyExists(record.Hive, record.RegistryPath))
            {
                store.Remove(item.Id);
                try { store.Save(); } catch (Exception) { }
                return OperationResult.Ok("原注册表项已不存在，已移除隐藏记录");
            }

            if (RequiresAdmin(item) && !IsAdministrator)
                return OperationResult.Fail("需要管理员权限：请以管理员身份运行本程序后重试");

            OperationResult op;
            try
            {
                switch ((EntryKind)record.Kind)
                {
                    case EntryKind.ComHandler:
                        op = RestoreComHandler(record);
                        break;
                    case EntryKind.PackagedCom:
                        op = RestorePackaged(record);
                        break;
                    default:
                        op = RestoreShellVerb(record);
                        break;
                }
            }
            catch (UnauthorizedAccessException)
            {
                return OperationResult.Fail("权限不足：请以管理员身份运行本程序");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("恢复失败：" + ex.Message);
            }

            if (!op.Success) return op;
            store.Remove(item.Id);
            try { store.Save(); }
            catch (Exception ex)
            {
                return OperationResult.Ok("已恢复（但记录清理失败：" + ex.Message + "）");
            }
            return op;
        }

        // ---------------------------------------------------------------- hide

        private static OperationResult HideShellVerb(ContextMenuItem item, HiddenRecord record)
        {
            using (RegistryKey root = OpenHive(item.Hive))
            using (RegistryKey key = root.OpenSubKey(item.RegistryPath, true))
            {
                if (key == null)
                    return OperationResult.Fail("注册表项已不存在，可能软件已被卸载");

                record.HadProgrammaticAccessOnly = HasValue(key, ProgrammaticAccessOnly);
                key.SetValue(ProgrammaticAccessOnly, "", RegistryValueKind.String);

                string handler = ReadString(key, "ExplorerCommandHandler");
                if (!string.IsNullOrEmpty(handler))
                {
                    record.HadExplorerCommandHandler = true;
                    record.OriginalExplorerCommandHandler = handler;
                    key.SetValue(BackupHandlerValue, handler, RegistryValueKind.String);
                    key.DeleteValue("ExplorerCommandHandler", false);
                }

                // Some verbs reach the modern menu through DelegateExecute instead.
                string delegateValue = ReadString(key, "DelegateExecute");
                if (!string.IsNullOrEmpty(delegateValue))
                {
                    record.HadDelegateExecute = true;
                    record.OriginalDelegateExecute = delegateValue;
                    key.SetValue(BackupDelegateValue, delegateValue, RegistryValueKind.String);
                    key.DeleteValue("DelegateExecute", false);
                }
            }
            return OperationResult.Ok("已隐藏：" + item.Name);
        }

        private static OperationResult HideComHandler(ContextMenuItem item, HiddenRecord record)
        {
            using (RegistryKey root = OpenHive(item.Hive))
            using (RegistryKey key = root.OpenSubKey(item.RegistryPath, true))
            {
                if (key == null)
                    return OperationResult.Fail("注册表项已不存在，可能软件已被卸载");

                string original = ReadString(key, "") ?? "";
                record.OriginalDefaultValue = original;
                key.SetValue(BackupClsidValue, original, RegistryValueKind.String);
                // Explorer enumerates handler subkeys and reads the default CLSID.
                // An empty CLSID makes it skip this extension entirely.
                key.SetValue("", "", RegistryValueKind.String);
            }
            return OperationResult.Ok("已隐藏：" + item.Name);
        }

        private static OperationResult HidePackaged(ContextMenuItem item, HiddenRecord record)
        {
            string clsid = string.IsNullOrEmpty(item.Clsid) ? ClsidFromPath(item.RegistryPath) : item.Clsid;
            if (string.IsNullOrEmpty(clsid))
                return OperationResult.Fail("无法解析组件 CLSID");

            // The ledger MUST carry the resolved CLSID: RestorePackaged uses it to
            // delete the blacklist value. Without this, restoring leaves a stale
            // entry in Shell Extensions\Blocked forever.
            record.Clsid = clsid;

            AddToBlocked("HKLM", HklmBlocked, clsid, item.Name);
            record.BlockedInHklm = true;
            try
            {
                AddToBlocked("HKCU", HkcuBlocked, clsid, item.Name);
                record.BlockedInHkcu = true;
            }
            catch (Exception) { /* HKLM is the authoritative list */ }
            return OperationResult.Ok("已加入 shell 扩展黑名单：" + item.Name);
        }

        // ------------------------------------------------------------- restore

        private static OperationResult RestoreShellVerb(HiddenRecord record)
        {
            using (RegistryKey root = OpenHive(record.Hive))
            using (RegistryKey key = root.OpenSubKey(record.RegistryPath, true))
            {
                if (key == null)
                    return OperationResult.Ok("原注册表项已不存在，已移除隐藏记录");

                if (!record.HadProgrammaticAccessOnly)
                    key.DeleteValue(ProgrammaticAccessOnly, false);
                if (record.HadExplorerCommandHandler)
                {
                    key.SetValue("ExplorerCommandHandler", record.OriginalExplorerCommandHandler ?? "",
                                 RegistryValueKind.String);
                    key.DeleteValue(BackupHandlerValue, false);
                }
                if (record.HadDelegateExecute)
                {
                    key.SetValue("DelegateExecute", record.OriginalDelegateExecute ?? "",
                                 RegistryValueKind.String);
                    key.DeleteValue(BackupDelegateValue, false);
                }
            }
            return OperationResult.Ok("已恢复：" + record.Name);
        }

        private static OperationResult RestoreComHandler(HiddenRecord record)
        {
            using (RegistryKey root = OpenHive(record.Hive))
            using (RegistryKey key = root.OpenSubKey(record.RegistryPath, true))
            {
                if (key == null)
                    return OperationResult.Ok("原注册表项已不存在，已移除隐藏记录");

                key.SetValue("", record.OriginalDefaultValue ?? "", RegistryValueKind.String);
                key.DeleteValue(BackupClsidValue, false);
            }
            return OperationResult.Ok("已恢复：" + record.Name);
        }

        private static OperationResult RestorePackaged(HiddenRecord record)
        {
            if (record.BlockedInHklm) RemoveFromBlocked("HKLM", HklmBlocked, record.Clsid);
            if (record.BlockedInHkcu) RemoveFromBlocked("HKCU", HkcuBlocked, record.Clsid);
            return OperationResult.Ok("已移出黑名单：" + record.Name);
        }

        // --------------------------------------------------------------- utils

        private static HiddenRecord NewRecord(ContextMenuItem item)
        {
            HiddenRecord record = new HiddenRecord();
            record.Id = item.Id;
            record.Name = item.Name;
            record.Hive = item.Hive;
            record.RegistryPath = item.RegistryPath;
            record.Clsid = item.Clsid;
            record.Publisher = item.Publisher;
            record.IconSpec = item.IconSpec;
            record.BinaryPath = item.BinaryPath;
            record.Kind = (int)item.Kind;
            record.Scope = (int)item.Scope;
            record.HiddenAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            return record;
        }

        private static void AddToBlocked(string hive, string path, string name, string data)
        {
            using (RegistryKey root = OpenHive(hive))
            using (RegistryKey key = root.CreateSubKey(path))
            {
                key.SetValue(name, string.IsNullOrEmpty(data) ? name : data, RegistryValueKind.String);
            }
        }

        private static void RemoveFromBlocked(string hive, string path, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            using (RegistryKey root = OpenHive(hive))
            using (RegistryKey key = root.OpenSubKey(path, true))
            {
                if (key != null) key.DeleteValue(name, false);
            }
        }

        private static string ClsidFromPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            int cut = path.LastIndexOf('\\');
            return cut >= 0 ? path.Substring(cut + 1) : path;
        }

        private static bool KeyExists(string hive, string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try
            {
                using (RegistryKey root = OpenHive(hive))
                using (RegistryKey key = root.OpenSubKey(path, false))
                {
                    return key != null;
                }
            }
            catch (Exception) { return false; }
        }

        private static RegistryKey OpenHive(string hive)
        {
            if (hive == "HKLM")
                return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        }

        private static string ReadString(RegistryKey key, string valueName)
        {
            try
            {
                object value = key.GetValue(valueName, null);
                return value == null ? null : Convert.ToString(value);
            }
            catch (Exception) { return null; }
        }

        private static bool HasValue(RegistryKey key, string valueName)
        {
            string[] names = key.GetValueNames();
            foreach (string name in names)
            {
                if (string.Equals(name, valueName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>Lightweight association-change notification; a full Explorer restart is stronger.</summary>
        public static void RefreshShell()
        {
            try
            {
                NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED,
                                             NativeMethods.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception) { /* best effort */ }
        }

        public static OperationResult RestartExplorer()
        {
            try
            {
                Process[] processes = Process.GetProcessesByName("explorer");
                foreach (Process process in processes)
                {
                    try { process.Kill(); }
                    catch (Exception) { /* already gone */ }
                    finally { process.Dispose(); }
                }
                Thread.Sleep(900);
                // Windows usually relaunches the shell by itself; only start it if it didn't.
                if (Process.GetProcessesByName("explorer").Length == 0)
                {
                    string exe = Environment.ExpandEnvironmentVariables("%WINDIR%\\explorer.exe");
                    Process.Start(exe);
                }
                return OperationResult.Ok("资源管理器已重启，右键菜单已刷新");
            }
            catch (Exception ex)
            {
                return OperationResult.Fail("重启失败：" + ex.Message);
            }
        }

        public static void RestartElevated()
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                ProcessStartInfo info = new ProcessStartInfo(exe);
                info.Arguments = "--elevated";
                info.Verb = "runas";
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception) { /* user cancelled UAC */ }
        }
    }
}
