using System;
using System.Threading;
using System.Windows.Forms;
using PharmaDataGuard.Core;

namespace PharmaDataGuard
{
    internal static class Program
    {
        private const string MutexName = @"Global\PharmaDataGuard_Singleton";

        [STAThread]
        private static int Main(string[] args)
        {
            try { AuditLogger.Instance.Info("BOOT", "Process started pid=" + System.Diagnostics.Process.GetCurrentProcess().Id); } catch { }

            if (args != null && args.Length >= 1 && string.Equals(args[0], "--watchdog", StringComparison.OrdinalIgnoreCase))
            {
                int parentPid;
                if (args.Length < 2 || !int.TryParse(args[1], out parentPid))
                {
                    return 1;
                }
                Watchdog.RunAsWatchdog(parentPid);
                return 0;
            }

            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    return 0;
                }

                try
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new MainForm());
                    return 0;
                }
                catch (Exception ex)
                {
                    try { AuditLogger.Instance.Error("FATAL", ex.ToString()); } catch { }
                    try
                    {
                        MessageBox.Show("Pharma Data Guard encountered a fatal error and will exit.\n\n" + ex.Message,
                            "Pharma Data Guard", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    catch { }
                    return 2;
                }
            }
        }
    }
}
