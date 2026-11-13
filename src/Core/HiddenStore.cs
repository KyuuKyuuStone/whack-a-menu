using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using ContextMenuCleaner.Models;

namespace ContextMenuCleaner.Core
{
    /// <summary>
    /// Persistent, human-readable ledger of hide operations. Keeping the ledger outside
    /// the registry means "restore" works even after a reboot, a move, or a reinstall.
    /// </summary>
    public class HiddenStore
    {
        public int Version = 1;
        public List<HiddenRecord> Records = new List<HiddenRecord>();

        public static string DirectoryPath
        {
            get
            {
                string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(baseDir, "ContextMenuCleaner");
            }
        }

        public static string FilePath
        {
            get { return Path.Combine(DirectoryPath, "hidden.xml"); }
        }

        public static HiddenStore Load()
        {
            HiddenStore store = new HiddenStore();
            if (!File.Exists(FilePath)) return store;
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(HiddenStore));
                using (FileStream fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    HiddenStore loaded = serializer.Deserialize(fs) as HiddenStore;
                    if (loaded != null && loaded.Records != null) store = loaded;
                }
            }
            catch (Exception)
            {
                // Move the damaged ledger aside once. Copying instead of moving would
                // retry (and overwrite the backup) on every single launch.
                try
                {
                    string broken = FilePath + ".broken";
                    if (File.Exists(broken)) File.Delete(broken);
                    File.Move(FilePath, broken);
                }
                catch (Exception) { /* ignore */ }
            }
            return store;
        }

        public void Save()
        {
            Directory.CreateDirectory(DirectoryPath);
            XmlSerializer serializer = new XmlSerializer(typeof(HiddenStore));
            using (FileStream fs = new FileStream(FilePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                serializer.Serialize(fs, this);
            }
        }

        public HiddenRecord Find(string id)
        {
            for (int i = 0; i < Records.Count; i++)
            {
                if (string.Equals(Records[i].Id, id, StringComparison.OrdinalIgnoreCase)) return Records[i];
            }
            return null;
        }

        public bool Contains(string id) { return Find(id) != null; }

        public void Add(HiddenRecord record)
        {
            Remove(record.Id);
            Records.Add(record);
        }

        public void Remove(string id)
        {
            for (int i = Records.Count - 1; i >= 0; i--)
            {
                if (string.Equals(Records[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    Records.RemoveAt(i);
            }
        }
    }
}
