using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using ContextMenuCleaner.Models;

namespace ContextMenuCleaner.Core
{
    public class ScanResult
    {
        public List<ContextMenuItem> Items = new List<ContextMenuItem>();
        public List<string> Warnings = new List<string>();
        public int BranchCount;
    }

    /// <summary>
    /// Enumerates every registry location that can contribute an item to the
    /// Windows 11 Explorer context menu. Read-only by design.
    /// </summary>
    public static class RegistryScanner
    {
        private class TargetDef
        {
            public string Key;
            public ScopeKind Scope;
        }

        private static readonly TargetDef[] Targets =
        {
            new TargetDef { Key = "*",                       Scope = ScopeKind.File },
            new TargetDef { Key = "AllFilesystemObjects",   Scope = ScopeKind.AllObjects },
            new TargetDef { Key = "Directory",              Scope = ScopeKind.Directory },
            new TargetDef { Key = "Directory\\Background",  Scope = ScopeKind.Background },
            new TargetDef { Key = "Folder",                 Scope = ScopeKind.Folder },
            new TargetDef { Key = "Drive",                  Scope = ScopeKind.Drive },
            new TargetDef { Key = "lnkfile",                Scope = ScopeKind.Shortcut }
        };

        // Windows registers per-type menu extensions under
        // HKCR\SystemFileAssociations\<type> and under the ProgID a file extension
        // points at (e.g. .jpg -> jpegfile\shell). Scanning only "*" / "Directory"
        // used to hide all of those, which is why the sidebar felt out of sync
        // with what Explorer actually shows for an image, a video or a .zip.
        private static readonly string[] ImageExt =
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".tif", ".tiff", ".svg", ".ico", ".psd", ".raw" };
        private static readonly string[] VideoExt =
        { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts" };
        private static readonly string[] AudioExt =
        { ".mp3", ".flac", ".wav", ".aac", ".ogg", ".m4a", ".wma", ".ape", ".opus" };
        private static readonly string[] DocumentExt =
        { ".txt", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".rtf", ".md", ".csv",
          ".log", ".ini", ".xml", ".json", ".htm", ".html" };
        private static readonly string[] ArchiveExt =
        { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".cab", ".iso" };
        private static readonly string[] KnownExtensions =
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".mp4", ".mkv", ".avi", ".mov",
          ".mp3", ".flac", ".wav", ".txt", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt",
          ".pptx", ".zip", ".rar", ".7z", ".iso" };

        private static readonly HashSet<string> BuiltInVerbs = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "open", "opennew", "edit", "print", "printto", "openas", "openwith",
            "properties", "new", "find", "runas", "runasuser", "cmd", "powershell",
            "pintostartscreen", "pintoquickaccess", "removeproperties", "copyaspath",
            "restorepreviousversions", "giveaccessto", "share", "extractall",
            "takeownership", "scanwithwindowsdefender", "windowsdefender", "search",
            "setdesktopbackground", "rotate", "preview", "casttodevice", "format",
            "eject", "manage", "bitlocker", "optimize", "encrypt", "decrypt",
            "sensitivitylabel", "sensitivity", "opencontainingfolder", "sendto",
            "cut", "copy", "delete", "rename", "addtoproject", "openinnewwindow",
            "windowssecurity", "troubleshoot", "mapnetworkdrive", "disconnect"
        };

        private const int MaxPackagedComponents = 200;

        public static ScanResult Scan(HiddenStore store)
        {
            ScanResult result = new ScanResult();
            Dictionary<string, ContextMenuItem> byId =
                new Dictionary<string, ContextMenuItem>(StringComparer.OrdinalIgnoreCase);

            foreach (string hive in new string[] { "HKCU", "HKLM" })
            {
                try
                {
                    using (RegistryKey root = OpenHive(hive))
                    {
                        string classes = hive == "HKCU" ? "Software\\Classes" : "SOFTWARE\\Classes";
                        foreach (TargetDef target in Targets)
                        {
                            ScanBranch(result, byId, root, hive, classes, target,
                                       "shell", EntryKind.ShellVerb);
                            ScanBranch(result, byId, root, hive, classes, target,
                                       "shellex\\ContextMenuHandlers", EntryKind.ComHandler);
                        }
                        ScanPackaged(result, byId, root, hive, classes);
                        ScanFileTypes(result, byId, root, hive, classes);
                    }
                }
                catch (Exception ex)
                {
                    result.Warnings.Add("无法读取 " + hive + "：" + ex.Message);
                }
            }

            foreach (ContextMenuItem item in result.Items)
            {
                item.IsHidden = store.Contains(item.Id);
                item.NeedsAdmin = string.Equals(item.Hive, "HKLM", StringComparison.OrdinalIgnoreCase) ||
                                  item.Kind == EntryKind.PackagedCom;
                item.IconImage = IconResolver.Get(item);
            }

            // Records whose registry key disappeared (the app was uninstalled) still
            // need to be visible so the user can clear or retry the restore.
            foreach (HiddenRecord record in store.Records)
            {
                if (byId.ContainsKey(record.Id)) continue;
                ContextMenuItem orphan = new ContextMenuItem();
                orphan.Id = record.Id;
                orphan.Name = string.IsNullOrEmpty(record.Name) ? "(已移除的菜单项)" : record.Name;
                orphan.Hive = record.Hive;
                orphan.RegistryPath = record.RegistryPath;
                orphan.Clsid = record.Clsid;
                orphan.IconSpec = record.IconSpec;
                orphan.BinaryPath = record.BinaryPath;
                orphan.Publisher = record.Publisher;
                orphan.Kind = (EntryKind)record.Kind;
                orphan.Scope = (ScopeKind)record.Scope;
                orphan.IsHidden = true;
                orphan.NeedsAdmin = string.Equals(record.Hive, "HKLM", StringComparison.OrdinalIgnoreCase) ||
                                    orphan.Kind == EntryKind.PackagedCom;
                orphan.Detail = "原注册表位置已不存在 · 可移除记录";
                orphan.IconImage = IconResolver.Get(orphan);
                Add(result, byId, orphan);
            }

            return result;
        }

        private static RegistryKey OpenHive(string hive)
        {
            if (hive == "HKLM")
                return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            return RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        }

        private static void ScanBranch(ScanResult result, Dictionary<string, ContextMenuItem> byId,
                                       RegistryKey root, string hive, string classes,
                                       TargetDef target, string subPath, EntryKind kind,
                                       bool skipBuiltInVerbs = false)
        {
            string parentPath = classes + "\\" + target.Key + "\\" + subPath;
            RegistryKey parent = null;
            try { parent = root.OpenSubKey(parentPath, false); }
            catch (Exception) { return; }
            if (parent == null) return;

            using (parent)
            {
                result.BranchCount++;
                string[] names;
                try { names = parent.GetSubKeyNames(); }
                catch (Exception) { return; }

                foreach (string name in names)
                {
                    // A ProgID's shell contains its default verbs (open/edit/new/
                    // print). Those are not third-party menu clutter, so skip them
                    // when scanning per-file-type branches.
                    if (skipBuiltInVerbs && BuiltInVerbs.Contains(name)) continue;
                    try
                    {
                        using (RegistryKey key = parent.OpenSubKey(name, false))
                        {
                            if (key == null) continue;
                            string keyPath = parentPath + "\\" + name;
                            ContextMenuItem item = BuildItem(hive, keyPath, name, key, kind, target.Scope);
                            Add(result, byId, item);
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add(name + "：" + ex.Message);
                    }
                }
            }
        }

        private static void ScanPackaged(ScanResult result, Dictionary<string, ContextMenuItem> byId,
                                         RegistryKey root, string hive, string classes)
        {
            string basePath = classes + "\\PackagedCom\\Package";
            RegistryKey packageRoot = null;
            try { packageRoot = root.OpenSubKey(basePath, false); }
            catch (Exception) { return; }
            if (packageRoot == null) return;

            using (packageRoot)
            {
                string[] packages = packageRoot.GetSubKeyNames();
                int added = 0;
                foreach (string package in packages)
                {
                    if (added >= MaxPackagedComponents) break;
                    string classPath = basePath + "\\" + package + "\\Class";
                    RegistryKey classKey = null;
                    try { classKey = packageRoot.OpenSubKey(package + "\\Class", false); }
                    catch (Exception) { continue; }
                    if (classKey == null) continue;

                    using (classKey)
                    {
                        string[] clsids = classKey.GetSubKeyNames();
                        foreach (string rawClsid in clsids)
                        {
                            if (added >= MaxPackagedComponents) break;
                            string clsid = NormalizeClsid(rawClsid);
                            ClsidInfo info = LookupClsid(clsid);
                            string name = info == null ? "" : info.Name;
                            if (string.IsNullOrEmpty(name) || LooksLikeClsid(name))
                                name = PrettyPackageName(package);
                            if (string.IsNullOrEmpty(name)) continue;

                            ContextMenuItem item = new ContextMenuItem();
                            item.Id = hive + "|" + classPath + "\\" + rawClsid;
                            item.Hive = hive;
                            item.RegistryPath = classPath + "\\" + rawClsid;
                            item.KeyName = rawClsid;
                            item.Name = name;
                            item.Kind = EntryKind.PackagedCom;
                            item.Scope = ScopeKind.Modern;
                            item.Clsid = clsid;
                            item.IconSpec = info == null ? "" : info.IconSpec;
                            item.BinaryPath = info == null ? "" : info.BinaryPath;
                            item.Publisher = GetPublisher(item.BinaryPath);
                            item.IsSystem = package.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
                                            item.Publisher.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase);
                            item.Detail = "Win11 现代菜单组件 · " + PrettyPackageName(package);
                            Add(result, byId, item);
                            added++;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Per-file-type context menus. Explorer picks these branches when you
        /// right-click a photo, a video, a document or an archive, so they need
        /// their own scopes instead of being lumped together with "all files".
        /// </summary>
        private static void ScanFileTypes(ScanResult result, Dictionary<string, ContextMenuItem> byId,
                                          RegistryKey root, string hive, string classes)
        {
            string sfaPath = classes + "\\SystemFileAssociations";
            try
            {
                using (RegistryKey sfa = root.OpenSubKey(sfaPath, false))
                {
                    if (sfa != null)
                    {
                        foreach (string typeKey in sfa.GetSubKeyNames())
                        {
                            ScopeKind scope = ClassifyFileType(typeKey);
                            // Ignore .exe/.bat/.cmd and other unrelated associations:
                            // they add hundreds of rows nobody came here to clean.
                            if (scope == ScopeKind.OtherType) continue;
                            TargetDef target = new TargetDef
                            {
                                Key = "SystemFileAssociations\\" + typeKey,
                                Scope = scope
                            };
                            ScanBranch(result, byId, root, hive, classes, target,
                                       "shell", EntryKind.ShellVerb, true);
                            ScanBranch(result, byId, root, hive, classes, target,
                                       "shellex\\ContextMenuHandlers", EntryKind.ComHandler, true);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add("文件类型关联：" + ex.Message);
            }

            // Many apps register their verb under the ProgID (.jpg -> jpegfile\shell)
            // rather than under the extension key itself.
            foreach (string ext in KnownExtensions)
            {
                try
                {
                    string progId = "";
                    using (RegistryKey extKey = root.OpenSubKey(classes + "\\" + ext, false))
                    {
                        if (extKey != null) progId = extKey.GetValue("") as string ?? "";
                    }
                    if (progId.Length == 0 || progId.IndexOf('\\') >= 0) continue;
                    ScopeKind scope = ClassifyFileType(ext + " " + progId);
                    if (scope == ScopeKind.OtherType) continue;
                    TargetDef target = new TargetDef { Key = progId, Scope = scope };
                    ScanBranch(result, byId, root, hive, classes, target,
                               "shell", EntryKind.ShellVerb, true);
                    ScanBranch(result, byId, root, hive, classes, target,
                               "shellex\\ContextMenuHandlers", EntryKind.ComHandler, true);
                }
                catch (Exception) { /* one broken ProgID must not stop the scan */ }
            }
        }

        private static ScopeKind ClassifyFileType(string token)
        {
            string s = (token ?? "").ToLowerInvariant();
            if (s.Contains("image") || HasAny(s, ImageExt)) return ScopeKind.Image;
            if (s.Contains("video") || s.Contains("movie") || HasAny(s, VideoExt)) return ScopeKind.Video;
            if (s.Contains("audio") || s.Contains("sound") || HasAny(s, AudioExt)) return ScopeKind.Audio;
            if (HasAny(s, ArchiveExt)) return ScopeKind.Archive;
            if (s.Contains("text") || s.Contains("document") || HasAny(s, DocumentExt))
                return ScopeKind.Document;
            return ScopeKind.OtherType;
        }

        private static bool HasAny(string s, string[] parts)
        {
            foreach (string part in parts)
                if (s.IndexOf(part, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        private static ContextMenuItem BuildItem(string hive, string registryPath, string keyName,
                                                  RegistryKey key, EntryKind kind, ScopeKind scope)
        {
            ContextMenuItem item = new ContextMenuItem();
            item.Id = hive + "|" + registryPath;
            item.Hive = hive;
            item.RegistryPath = registryPath;
            item.KeyName = keyName;
            item.Kind = kind;
            item.Scope = scope;

            string name = "";
            string iconSpec = "";
            string binaryPath = "";
            string clsid = "";

            if (kind == EntryKind.ComHandler)
            {
                clsid = NormalizeClsid(ReadString(key, ""));
                name = CleanDisplay(keyName);
                ClsidInfo info = LookupClsid(clsid);
                if (info != null)
                {
                    if (string.IsNullOrEmpty(name) || LooksLikeClsid(name)) name = info.Name;
                    iconSpec = info.IconSpec;
                    binaryPath = info.BinaryPath;
                }
            }
            else
            {
                name = CleanFirst(ReadString(key, ""),
                                  ReadString(key, "MUIVerb"),
                                  ReadString(key, "LocalizedString"));
                if (string.IsNullOrEmpty(name) || LooksLikePath(name) || LooksLikeClsid(name))
                    name = CleanDisplay(keyName);

                iconSpec = ReadString(key, "Icon");
                string handler = ReadString(key, "ExplorerCommandHandler");
                if (string.IsNullOrEmpty(handler)) handler = ReadString(key, "DelegateExecute");
                clsid = NormalizeClsid(handler);

                using (RegistryKey commandKey = key.OpenSubKey("command", false))
                {
                    if (commandKey != null) binaryPath = ExtractBinary(ReadString(commandKey, ""));
                }
                ClsidInfo info = LookupClsid(clsid);
                if (info != null)
                {
                    if (string.IsNullOrEmpty(iconSpec)) iconSpec = info.IconSpec;
                    if (string.IsNullOrEmpty(binaryPath)) binaryPath = info.BinaryPath;
                    if (string.IsNullOrEmpty(name) || LooksLikeClsid(name)) name = info.Name;
                }
            }

            if (string.IsNullOrEmpty(name)) name = keyName;
            if (LooksLikeClsid(name))
            {
                // Some handlers use the CLSID itself as the subkey name and have no
                // readable CLSID registration; show a short, still recognizable label.
                ClsidInfo retry = LookupClsid(name);
                if (retry != null)
                {
                    if (!string.IsNullOrEmpty(retry.Name) && !LooksLikeClsid(retry.Name)) name = retry.Name;
                    if (string.IsNullOrEmpty(iconSpec)) iconSpec = retry.IconSpec;
                    if (string.IsNullOrEmpty(binaryPath)) binaryPath = retry.BinaryPath;
                }
                if (LooksLikeClsid(name))
                {
                    string guid = name.Trim('{', '}');
                    name = "未命名扩展 " + guid.Substring(0, Math.Min(8, guid.Length));
                }
            }
            item.Name = TrimLength(name, 60);
            item.IconSpec = iconSpec ?? "";
            item.BinaryPath = binaryPath ?? "";
            item.Clsid = clsid ?? "";
            item.Publisher = TrimLength(GetPublisher(item.BinaryPath), 40);
            item.IsSystem = DetermineIsSystem(hive, item.BinaryPath, item.Name, keyName, item.Publisher);
            item.Detail = (string.IsNullOrEmpty(item.Publisher) ? "" : item.Publisher + " · ") +
                          item.KindText + " · " + item.ScopeText;
            return item;
        }

        private class ClsidInfo
        {
            public string Name = "";
            public string IconSpec = "";
            public string BinaryPath = "";
        }

        private static ClsidInfo LookupClsid(string clsid)
        {
            if (string.IsNullOrEmpty(clsid)) return null;
            string id = clsid.Trim();
            if (!id.StartsWith("{")) id = "{" + id + "}";

            RegistryKey key = null;
            try { key = Registry.ClassesRoot.OpenSubKey("CLSID\\" + id, false); }
            catch (Exception) { return null; }
            if (key == null) return null;

            using (key)
            {
                ClsidInfo info = new ClsidInfo();
                info.Name = CleanFirst(ReadString(key, "LocalizedString"),
                                       ReadString(key, ""),
                                       ReadString(key, "DisplayName"));
                if (LooksLikeClsid(info.Name)) info.Name = "";

                using (RegistryKey iconKey = key.OpenSubKey("DefaultIcon", false))
                {
                    if (iconKey != null) info.IconSpec = ReadString(iconKey, "") ?? "";
                }
                using (RegistryKey server = key.OpenSubKey("InprocServer32", false))
                {
                    if (server != null) info.BinaryPath = NormalizeBinary(ReadString(server, ""));
                }
                if (string.IsNullOrEmpty(info.BinaryPath))
                {
                    using (RegistryKey server = key.OpenSubKey("LocalServer32", false))
                    {
                        if (server != null) info.BinaryPath = NormalizeBinary(ReadString(server, ""));
                    }
                }
                return info;
            }
        }

        private static bool DetermineIsSystem(string hive, string binaryPath,
                                              string name, string keyName, string publisher)
        {
            string p = (binaryPath ?? "").ToLowerInvariant();
            bool inSystemDir = p.Contains("\\windows\\system32\\") ||
                               p.Contains("\\windows\\syswow64\\") ||
                               p.Contains("\\windowsapps\\");
            bool shellBinary = p.EndsWith("\\explorer.exe") || p.EndsWith("\\shell32.dll") ||
                               p.EndsWith("\\explorerframe.dll") ||
                               p.EndsWith("\\windows.storage.dll") ||
                               p.Contains("\\windows defender\\");
            if (!string.IsNullOrEmpty(publisher) &&
                publisher.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) return true;
            if (shellBinary) return true;
            // An identified non-Microsoft vendor wins over the file location:
            // plenty of third-party shell DLLs are installed into System32.
            if (!string.IsNullOrEmpty(publisher)) return false;
            if (inSystemDir) return true;

            string n = (name ?? "").Trim().ToLowerInvariant();
            if (BuiltInVerbs.Contains(n)) return true;
            if (BuiltInVerbs.Contains((keyName ?? "").Trim().ToLowerInvariant())) return true;
            if (n.StartsWith("windows defender") || n.StartsWith("windows security") ||
                n.StartsWith("microsoft defender") || n.StartsWith("scan with microsoft")) return true;
            return false;
        }

        private static string GetPublisher(string binaryPath)
        {
            if (string.IsNullOrEmpty(binaryPath) || !File.Exists(binaryPath)) return "";
            try
            {
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(binaryPath);
                if (!string.IsNullOrEmpty(version.CompanyName))
                    return version.CompanyName.Trim();
                if (!string.IsNullOrEmpty(version.FileDescription))
                    return version.FileDescription.Trim();
            }
            catch (Exception) { /* unreadable or not a PE file */ }
            return "";
        }

        private static string ExtractBinary(string command)
        {
            if (string.IsNullOrEmpty(command)) return "";
            string s = command.Trim();
            string exe = "";
            int consumed = 0;
            if (s[0] == '"')
            {
                int end = s.IndexOf('"', 1);
                if (end > 1) { exe = s.Substring(1, end - 1); consumed = end + 1; }
            }
            else
            {
                int space = s.IndexOf(' ');
                exe = space > 0 ? s.Substring(0, space) : s;
                consumed = exe.Length;
            }
            if (exe.Length == 0) return "";

            string lower = exe.ToLowerInvariant();
            if (lower.EndsWith("rundll32.exe") || lower.EndsWith("cmd.exe") ||
                lower.EndsWith("powershell.exe") || lower.EndsWith("wscript.exe"))
            {
                // The real provider is the DLL later in the command line.
                string rest = consumed < s.Length ? s.Substring(consumed).Trim() : "";
                if (rest.Length == 0) return "";
                if (rest[0] == '"')
                {
                    int end = rest.IndexOf('"', 1);
                    if (end > 1) return NormalizeBinary(rest.Substring(1, end - 1));
                }
                int comma = rest.IndexOf(',');
                string dll = comma > 0 ? rest.Substring(0, comma) : rest;
                return NormalizeBinary(dll);
            }
            return NormalizeBinary(exe);
        }

        private static string NormalizeBinary(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string s = path.Trim().Trim('"');
            try { s = Environment.ExpandEnvironmentVariables(s); }
            catch (Exception) { /* keep as-is */ }
            return s.Trim().Trim('"');
        }

        private static string NormalizeClsid(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string s = value.Trim().Trim('"').Trim();
            if (s.Length == 0) return "";
            if (!s.StartsWith("{")) s = "{" + s.Trim('}', '{') + "}";
            return s;
        }

        private static bool LooksLikeClsid(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            string s = value.Trim().Trim('{', '}');
            return s.Length == 36 && s.IndexOf('-') == 8;
        }

        private static bool LooksLikePath(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            return value.IndexOf('\\') >= 0 || value.IndexOf(":\\", StringComparison.Ordinal) >= 0;
        }

        private static string PrettyPackageName(string package)
        {
            string s = package ?? "";
            int underscore = s.IndexOf('_');
            if (underscore > 0) s = s.Substring(0, underscore);
            int dot = s.LastIndexOf('.');
            if (dot >= 0 && dot < s.Length - 1) s = s.Substring(dot + 1);
            return string.IsNullOrEmpty(s) ? "打包组件" : s;
        }

        /// <summary>Resolves @dll,-resource indirect strings and strips '&' mnemonics.</summary>
        private static string CleanDisplay(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            string s = value.Trim();
            if (s.StartsWith("@"))
            {
                string resolved = NativeMethods.LoadIndirectString(s);
                if (!string.Equals(resolved, s, StringComparison.Ordinal)) s = resolved;
                else return "";
            }
            s = s.Replace("&", "");
            s = s.Trim().Trim('"');
            if (s.StartsWith("@") || LooksLikePath(s)) return "";
            return s;
        }

        /// <summary>Tries each candidate until one resolves into readable text.</summary>
        private static string CleanFirst(params string[] values)
        {
            foreach (string v in values)
            {
                string cleaned = CleanDisplay(v);
                if (!string.IsNullOrEmpty(cleaned)) return cleaned;
            }
            return "";
        }

        private static string ReadString(RegistryKey key, string valueName)
        {
            if (key == null) return null;
            try
            {
                object value = key.GetValue(valueName, null);
                return value == null ? null : Convert.ToString(value);
            }
            catch (Exception) { return null; }
        }

        private static string TrimLength(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max) return value ?? "";
            return value.Substring(0, max - 1) + "…";
        }

        private static void Add(ScanResult result, Dictionary<string, ContextMenuItem> byId,
                                ContextMenuItem item)
        {
            if (byId.ContainsKey(item.Id)) return;
            byId.Add(item.Id, item);
            result.Items.Add(item);
        }
    }
}
