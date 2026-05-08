using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace PharmaDataGuard.Core
{
    public sealed class AuditLogger
    {
        private static readonly Lazy<AuditLogger> _lazy = new Lazy<AuditLogger>(LazyFactory);
        private static AuditLogger LazyFactory() { return new AuditLogger(); }

        public static AuditLogger Instance { get { return _lazy.Value; } }

        public static string GlobalLogPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PharmaDataGuard");
                return Path.Combine(dir, "pharma-data-guard.log");
            }
        }

        private readonly object _lock = new object();
        private readonly string _logPath;
        private readonly byte[] _hmacKey;
        private string _prevHashB64 = string.Empty;

        private AuditLogger()
        {
            _logPath = GlobalLogPath;
            _hmacKey = Encoding.UTF8.GetBytes("PharmaDataGuard-v1-" + Environment.MachineName);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath));
                _prevHashB64 = ReadLastHash();
            }
            catch { }
        }

        private string ReadLastHash()
        {
            try
            {
                if (!File.Exists(_logPath)) return string.Empty;
                string lastLine = string.Empty;
                using (var fs = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (!string.IsNullOrEmpty(line)) lastLine = line;
                    }
                }
                int idx = lastLine.LastIndexOf("| h=", StringComparison.Ordinal);
                if (idx < 0) return string.Empty;
                return lastLine.Substring(idx + 4).Trim();
            }
            catch { return string.Empty; }
        }

        public void Info(string category, string payload) { Write("INFO", category, payload); }
        public void Warn(string category, string payload) { Write("WARN", category, payload); }
        public void Error(string category, string payload) { Write("ERROR", category, payload); }

        public void Blocked(string category, string what, string context)
        {
            string payload = "what=" + Escape(what) + " ctx=" + Escape(context ?? "");
            Write("BLOCK", category, payload);
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
        }

        private string CurrentUser()
        {
            try { return WindowsIdentity.GetCurrent().Name; } catch { return "unknown"; }
        }

        private void Write(string level, string category, string payload)
        {
            try
            {
                lock (_lock)
                {
                    string ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                    string body = "[" + ts + "] " + level + " | " + category +
                                  " | user=" + Escape(CurrentUser()) + " | " + Escape(payload);
                    string toHash = _prevHashB64 + body;
                    string hashB64;
                    using (var hmac = new HMACSHA256(_hmacKey))
                    {
                        byte[] h = hmac.ComputeHash(Encoding.UTF8.GetBytes(toHash));
                        hashB64 = Convert.ToBase64String(h);
                    }
                    string line = body + " | h=" + hashB64;
                    File.AppendAllText(_logPath, line + Environment.NewLine, Encoding.UTF8);
                    _prevHashB64 = hashB64;
                }
            }
            catch { }
        }
    }
}
