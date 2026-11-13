using System;
using System.Xml.Serialization;

namespace ContextMenuCleaner.Models
{
    /// <summary>How the menu entry is registered.</summary>
    public enum EntryKind
    {
        ShellVerb = 0,     // ...\shell\<verb>
        ComHandler = 1,    // ...\shellex\ContextMenuHandlers\<handler>
        PackagedCom = 2    // Win11 modern menu: Classes\PackagedCom\Package\...\Class\<CLSID>
    }

    /// <summary>Which right-click target the entry belongs to.</summary>
    public enum ScopeKind
    {
        File = 0,
        AllObjects = 1,
        Directory = 2,
        Background = 3,
        Folder = 4,
        Drive = 5,
        Shortcut = 6,
        Modern = 7,
        Other = 8
    }

    public class ContextMenuItem
    {
        public string Id = "";
        public string Name = "";
        public string Detail = "";
        public string Publisher = "";
        public string Hive = "HKCU";
        public string RegistryPath = "";
        public string IconSpec = "";
        public string BinaryPath = "";
        public string Clsid = "";
        public string KeyName = "";

        public EntryKind Kind = EntryKind.ShellVerb;
        public ScopeKind Scope = ScopeKind.File;
        public bool IsSystem;
        public bool IsHidden;
        public bool NeedsAdmin;

        [XmlIgnore] public System.Drawing.Bitmap IconImage;
        [XmlIgnore] public bool Checked;

        public string KindText
        {
            get
            {
                if (Kind == EntryKind.ComHandler) return "COM 扩展";
                if (Kind == EntryKind.PackagedCom) return "Win11 现代菜单";
                return "菜单命令";
            }
        }

        public string ScopeText
        {
            get
            {
                switch (Scope)
                {
                    case ScopeKind.File: return "文件";
                    case ScopeKind.AllObjects: return "所有对象";
                    case ScopeKind.Directory: return "文件夹";
                    case ScopeKind.Background: return "桌面/空白处";
                    case ScopeKind.Folder: return "文件夹";
                    case ScopeKind.Drive: return "磁盘";
                    case ScopeKind.Shortcut: return "快捷方式";
                    case ScopeKind.Modern: return "现代菜单";
                    default: return "其他";
                }
            }
        }

        /// <summary>Short registry location shown as the second line of a row.</summary>
        public string SourceText
        {
            get
            {
                string path = RegistryPath ?? "";
                int cut = path.IndexOf("\\Classes\\", StringComparison.OrdinalIgnoreCase);
                if (cut >= 0) path = path.Substring(cut + 9);
                if (path.Length > 78) path = path.Substring(0, 75) + "...";
                return Hive + "\\...\\" + path;
            }
        }

        public bool Matches(string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            string q = query.Trim();
            if (q.Length == 0) return true;
            return Contains(Name, q) || Contains(Publisher, q) ||
                   Contains(Detail, q) || Contains(RegistryPath, q) ||
                   Contains(Clsid, q) || Contains(KeyName, q);
        }

        private static bool Contains(string source, string value)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>
    /// Everything needed to undo one hide operation. Stored as XML under %LOCALAPPDATA%,
    /// so recovery survives moving or reinstalling the tool.
    /// </summary>
    public class HiddenRecord
    {
        public string Id = "";
        public string Name = "";
        public string Hive = "";
        public string RegistryPath = "";
        public string Clsid = "";
        public string Publisher = "";
        public string IconSpec = "";
        public string BinaryPath = "";
        public string HiddenAt = "";
        public int Kind;
        public int Scope;

        public bool HadProgrammaticAccessOnly;
        public bool HadExplorerCommandHandler;
        public string OriginalExplorerCommandHandler = "";
        public bool HadDelegateExecute;
        public string OriginalDelegateExecute = "";
        public string OriginalDefaultValue = "";

        public bool BlockedInHkcu;
        public bool BlockedInHklm;
    }
}
