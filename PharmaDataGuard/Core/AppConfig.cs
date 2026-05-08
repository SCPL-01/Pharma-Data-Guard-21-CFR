using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Xml.Serialization;

namespace PharmaDataGuard.Core
{
    [XmlRoot("PharmaDataGuardConfig")]
    public sealed class AppConfig
    {
        private static readonly object _initLock = new object();
        private static AppConfig _instance;

        public static AppConfig Instance
        {
            get
            {
                if (_instance != null) return _instance;
                lock (_initLock)
                {
                    if (_instance == null) _instance = Load();
                    return _instance;
                }
            }
        }

        public static string ConfigPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PharmaDataGuard");
                return Path.Combine(dir, "config.xml");
            }
        }

        private string _hash = string.Empty;
        private string _salt = string.Empty;
        private List<string> _paths = new List<string>();
        private bool _enableWatchdog = true;
        private bool _enableLegacyContextMenuFallback = true;
        private bool _restartExplorerOnPolicyChange = true;
        private bool _blockFileDragDrop = true;

        public string UnlockPasswordHash { get { return _hash; } set { _hash = value; } }
        public string UnlockPasswordSalt { get { return _salt; } set { _salt = value; } }

        [XmlArray("ProtectedPaths")]
        [XmlArrayItem("Path")]
        public List<string> ProtectedPaths { get { return _paths; } set { _paths = value; } }

        public bool BlockSelectAll { get; set; }
        public bool BlockSave { get; set; }
        public bool BlockPrint { get; set; }
        public bool EnableWatchdog { get { return _enableWatchdog; } set { _enableWatchdog = value; } }
        // Replaces the older EnableDragDropGuard flag (now ignored). Default ON so an existing
        // config.xml without this element automatically gets drag-drop blocking on next launch.
        public bool BlockFileDragDrop
        {
            get { return _blockFileDragDrop; }
            set { _blockFileDragDrop = value; }
        }
        public bool EnableLegacyContextMenuFallback
        {
            get { return _enableLegacyContextMenuFallback; }
            set { _enableLegacyContextMenuFallback = value; }
        }
        public bool RestartExplorerOnPolicyChange
        {
            get { return _restartExplorerOnPolicyChange; }
            set { _restartExplorerOnPolicyChange = value; }
        }

        private static AppConfig Load()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
                if (File.Exists(ConfigPath))
                {
                    var ser = new XmlSerializer(typeof(AppConfig));
                    using (var fs = new FileStream(ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        var cfg = (AppConfig)ser.Deserialize(fs);
                        if (cfg.ProtectedPaths == null) cfg.ProtectedPaths = new List<string>();
                        return cfg;
                    }
                }
            }
            catch { }

            var def = new AppConfig();
            def.SetPassword("PharmaGuard@123");
            try { def.Save(); } catch { }
            return def;
        }

        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            var ser = new XmlSerializer(typeof(AppConfig));
            string tmp = ConfigPath + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                ser.Serialize(fs, this);
            }
            if (File.Exists(ConfigPath)) File.Delete(ConfigPath);
            File.Move(tmp, ConfigPath);
        }

        public void SetPassword(string plain)
        {
            if (plain == null) throw new ArgumentNullException("plain");
            byte[] salt = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
            byte[] hash;
            using (var kdf = new Rfc2898DeriveBytes(plain, salt, 100000))
            {
                hash = kdf.GetBytes(32);
            }
            UnlockPasswordSalt = Convert.ToBase64String(salt);
            UnlockPasswordHash = Convert.ToBase64String(hash);
        }

        public bool VerifyPassword(string plain)
        {
            if (string.IsNullOrEmpty(UnlockPasswordHash) || string.IsNullOrEmpty(UnlockPasswordSalt))
                return false;
            try
            {
                byte[] salt = Convert.FromBase64String(UnlockPasswordSalt);
                byte[] expected = Convert.FromBase64String(UnlockPasswordHash);
                byte[] actual;
                string p = plain == null ? string.Empty : plain;
                using (var kdf = new Rfc2898DeriveBytes(p, salt, 100000))
                {
                    actual = kdf.GetBytes(expected.Length);
                }
                if (expected.Length != actual.Length) return false;
                int diff = 0;
                for (int i = 0; i < expected.Length; i++) diff |= expected[i] ^ actual[i];
                return diff == 0;
            }
            catch { return false; }
        }
    }
}
