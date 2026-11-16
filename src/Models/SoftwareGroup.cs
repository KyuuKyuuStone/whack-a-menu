using System;
using System.Collections.Generic;
using System.Drawing;

namespace ContextMenuCleaner.Models
{
    /// <summary>
    /// One installed product often registers the very same shell extension under
    /// several scopes (File, Directory, Drive, Background) and in both hives.
    /// Registry-wise these ARE independent entries and each one has to be hidden
    /// and restored on its own, so the data layer keeps them all. This class only
    /// bundles them for display: six identical AABdzCtx rows become one card that
    /// says "6 处注册" and can be expanded into the individual registration points.
    /// </summary>
    public class SoftwareGroup
    {
        public string Key = "";
        public string Name = "";
        public string Publisher = "";
        public string BinaryPath = "";
        public string Clsid = "";
        public Bitmap IconImage;
        public bool IsSystem;
        public bool NeedsAdmin;
        public readonly List<ContextMenuItem> Entries = new List<ContextMenuItem>();

        public int Count { get { return Entries.Count; } }

        /// <summary>True only when every registration point in this group is checked.</summary>
        public bool Checked
        {
            get
            {
                if (Entries.Count == 0) return false;
                foreach (ContextMenuItem entry in Entries)
                    if (!entry.Checked) return false;
                return true;
            }
            set
            {
                foreach (ContextMenuItem entry in Entries) entry.Checked = value;
            }
        }

        public int CheckedCount
        {
            get
            {
                int n = 0;
                foreach (ContextMenuItem entry in Entries) if (entry.Checked) n++;
                return n;
            }
        }

        public bool AnyHidden
        {
            get
            {
                foreach (ContextMenuItem entry in Entries) if (entry.IsHidden) return true;
                return false;
            }
        }

        /// <summary>Short scope list, e.g. "文件 · 文件夹 · 磁盘".</summary>
        public string ScopeSummary
        {
            get
            {
                List<string> seen = new List<string>();
                foreach (ContextMenuItem entry in Entries)
                {
                    string scope = entry.ScopeText;
                    if (!seen.Contains(scope)) seen.Add(scope);
                }
                if (seen.Count > 3)
                    return seen[0] + " · " + seen[1] + " · " + seen[2] + " 等 " + seen.Count + " 类";
                return string.Join(" · ", seen.ToArray());
            }
        }

        /// <summary>
        /// Grouping key, best evidence first: the real binary on disk, then the COM
        /// CLSID, then publisher+name, then a single entry. Never group two entries
        /// that we cannot prove belong to the same product.
        /// </summary>
        public static string MakeKey(ContextMenuItem item)
        {
            string bin = (item.BinaryPath ?? "").Trim();
            if (bin.Length > 0) return "bin|" + bin.ToLowerInvariant();
            string clsid = (item.Clsid ?? "").Trim();
            if (clsid.Length > 0) return "clsid|" + clsid.ToLowerInvariant();
            string pub = (item.Publisher ?? "").Trim();
            string name = (item.Name ?? "").Trim();
            if (pub.Length > 0) return "pub|" + pub.ToLowerInvariant() + "|" + name.ToLowerInvariant();
            return "one|" + (item.Id ?? "");
        }

        public static List<SoftwareGroup> Build(IEnumerable<ContextMenuItem> items)
        {
            Dictionary<string, SoftwareGroup> map =
                new Dictionary<string, SoftwareGroup>(StringComparer.OrdinalIgnoreCase);
            List<SoftwareGroup> groups = new List<SoftwareGroup>();

            foreach (ContextMenuItem item in items)
            {
                string key = MakeKey(item);
                SoftwareGroup group;
                if (!map.TryGetValue(key, out group))
                {
                    group = new SoftwareGroup();
                    group.Key = key;
                    group.Name = item.Name;
                    group.Publisher = item.Publisher;
                    group.BinaryPath = item.BinaryPath;
                    group.Clsid = item.Clsid;
                    group.IconImage = item.IconImage;
                    group.IsSystem = item.IsSystem;
                    map.Add(key, group);
                    groups.Add(group);
                }
                if (group.IconImage == null && item.IconImage != null)
                    group.IconImage = item.IconImage;
                // Prefer a readable product name and a known publisher if the
                // first entry happened to be a bare registry key name.
                if (string.IsNullOrEmpty(group.Publisher) && !string.IsNullOrEmpty(item.Publisher))
                    group.Publisher = item.Publisher;
                if (group.IsSystem && !item.IsSystem) group.IsSystem = false;
                if (item.NeedsAdmin) group.NeedsAdmin = true;
                group.Entries.Add(item);
            }

            groups.Sort(delegate (SoftwareGroup a, SoftwareGroup b)
            {
                int c = a.Count.CompareTo(b.Count) * -1;   // noisiest products first
                if (c != 0) return c;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return groups;
        }
    }
}
