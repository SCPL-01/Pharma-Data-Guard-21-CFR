using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace PharmaDataGuard.Core
{
    public static class Watchdog
    {
        private static Thread _selfWatcher;
        private static volatile bool _stop;
        private static volatile int _watchdogPid = -1;

        public static void StartWatchingSelf()
        {
            if (_selfWatcher != null && _selfWatcher.IsAlive) return;
            _stop = false;
            _selfWatcher = new Thread(SelfWatchLoop);
            _selfWatcher.IsBackground = true;
            _selfWatcher.Name = "PharmaDataGuard-WatchdogSpawner";
            _selfWatcher.Start();
        }

        public static void StopWatchingSelf()
        {
            _stop = true;
        }

        // Stop spawning new watchdogs and forcibly terminate any sibling watchdog
        // process. Must be called before Application.Exit() on an authorised exit,
        // otherwise the watchdog relaunches Pharma Data Guard the moment we die.
        public static void StopAndKillWatchdog()
        {
            _stop = true;

            int tracked = _watchdogPid;
            if (tracked > 0)
            {
                TryKillByPid(tracked);
                _watchdogPid = -1;
            }

            // Belt-and-suspenders: kill any other PharmaDataGuard.exe still around.
            try
            {
                int ourPid = Process.GetCurrentProcess().Id;
                var procs = Process.GetProcessesByName("PharmaDataGuard");
                foreach (var p in procs)
                {
                    try
                    {
                        if (p.Id != ourPid)
                        {
                            try { if (!p.HasExited) p.Kill(); } catch { }
                        }
                    }
                    finally { p.Dispose(); }
                }
            }
            catch { }
        }

        private static void TryKillByPid(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    if (!p.HasExited) p.Kill();
                }
            }
            catch { }
        }

        private static void SelfWatchLoop()
        {
            int ourPid = Process.GetCurrentProcess().Id;
            string ourExe = Process.GetCurrentProcess().MainModule.FileName;
            string ourName = Path.GetFileNameWithoutExtension(ourExe);

            while (!_stop)
            {
                try
                {
                    if (!SiblingWatchdogExists(ourName, ourPid))
                    {
                        var psi = new ProcessStartInfo(ourExe, "--watchdog " + ourPid);
                        psi.UseShellExecute = false;
                        psi.CreateNoWindow = true;
                        psi.WindowStyle = ProcessWindowStyle.Hidden;
                        var spawned = Process.Start(psi);
                        if (spawned != null) _watchdogPid = spawned.Id;
                        AuditLogger.Instance.Info("WATCHDOG", "Sibling watchdog spawned for pid=" + ourPid +
                            " wd=" + (spawned == null ? -1 : spawned.Id));
                    }
                }
                catch (Exception ex)
                {
                    AuditLogger.Instance.Error("WATCHDOG", "spawn loop: " + ex.Message);
                }
                Thread.Sleep(2000);
            }
        }

        private static bool SiblingWatchdogExists(string ourName, int ourPid)
        {
            try
            {
                var procs = Process.GetProcessesByName(ourName);
                foreach (var p in procs)
                {
                    if (p.Id == ourPid) { p.Dispose(); continue; }
                    p.Dispose();
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static void RunAsWatchdog(int parentPid)
        {
            string globalLog = AuditLogger.GlobalLogPath;
            string parentExe = null;
            try
            {
                using (var parent = Process.GetProcessById(parentPid))
                {
                    var mainModule = parent.MainModule;
                    parentExe = mainModule == null ? null : mainModule.FileName;
                }
            }
            catch
            {
                AppendGlobal(globalLog, "WATCHDOG started but parent pid=" + parentPid + " already gone");
            }

            AppendGlobal(globalLog, "WATCHDOG online — monitoring pid=" + parentPid);

            while (true)
            {
                try
                {
                    if (!ProcessAlive(parentPid))
                    {
                        AppendGlobal(globalLog, "WATCHDOG parent pid=" + parentPid + " died — relaunching");
                        if (!string.IsNullOrEmpty(parentExe) && File.Exists(parentExe))
                        {
                            try
                            {
                                var psi = new ProcessStartInfo(parentExe);
                                psi.UseShellExecute = false;
                                psi.CreateNoWindow = false;
                                Process.Start(psi);
                                AppendGlobal(globalLog, "WATCHDOG relaunch issued: " + parentExe);
                            }
                            catch (Exception ex)
                            {
                                AppendGlobal(globalLog, "WATCHDOG relaunch failed: " + ex.Message);
                            }
                        }
                        return;
                    }
                }
                catch (Exception ex)
                {
                    AppendGlobal(globalLog, "WATCHDOG loop error: " + ex.Message);
                }
                Thread.Sleep(1000);
            }
        }

        private static bool ProcessAlive(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    return !p.HasExited;
                }
            }
            catch { return false; }
        }

        private static void AppendGlobal(string path, string msg)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string line = "[" + DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") + "] WATCHDOG | " + msg + Environment.NewLine;
                File.AppendAllText(path, line);
            }
            catch { }
        }
    }
}
