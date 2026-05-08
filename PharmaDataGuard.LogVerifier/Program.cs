using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PharmaDataGuard.LogVerifier
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                string logPath = args.Length >= 1
                    ? args[0]
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                                   "PharmaDataGuard", "pharma-data-guard.log");
                string machine = args.Length >= 2 ? args[1] : Environment.MachineName;

                if (!File.Exists(logPath))
                {
                    Console.Error.WriteLine("Log not found: " + logPath);
                    return 2;
                }

                byte[] key = Encoding.UTF8.GetBytes("PharmaDataGuard-v1-" + machine);
                int total = 0;
                int bad = 0;
                int firstBadLine = -1;
                string firstBadDetail = null;

                string prevHashB64 = string.Empty;
                int lineNo = 0;

                using (var hmac = new HMACSHA256(key))
                using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        lineNo++;
                        if (string.IsNullOrEmpty(line)) continue;
                        total++;

                        int idx = line.LastIndexOf("| h=", StringComparison.Ordinal);
                        if (idx < 0)
                        {
                            bad++;
                            if (firstBadLine < 0) { firstBadLine = lineNo; firstBadDetail = "missing hash suffix"; }
                            continue;
                        }
                        string body = line.Substring(0, idx).TrimEnd();
                        string recordedHashB64 = line.Substring(idx + 4).Trim();

                        string toHash = prevHashB64 + body;
                        byte[] computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(toHash));
                        string computedB64 = Convert.ToBase64String(computed);

                        if (!ConstantTimeEquals(computedB64, recordedHashB64))
                        {
                            bad++;
                            if (firstBadLine < 0)
                            {
                                firstBadLine = lineNo;
                                firstBadDetail = "expected " + computedB64.Substring(0, Math.Min(12, computedB64.Length)) +
                                                 "… got " + recordedHashB64.Substring(0, Math.Min(12, recordedHashB64.Length)) + "…";
                            }
                        }

                        // Use the recorded hash as the chain pointer so a single tampered line does not cascade.
                        prevHashB64 = recordedHashB64;
                    }
                }

                if (bad == 0)
                {
                    Console.WriteLine("OK: " + total + " lines verified.");
                    return 0;
                }
                Console.WriteLine("FAIL: " + bad + " tampered line(s) found.");
                Console.WriteLine("First mismatch: line " + firstBadLine + " — " + firstBadDetail);
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("I/O error: " + ex.Message);
                return 2;
            }
        }

        private static bool ConstantTimeEquals(string a, string b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
