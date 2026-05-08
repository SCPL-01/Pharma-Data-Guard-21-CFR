using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Win32;

namespace PharmaDataGuard.Core
{
    // Disables the Windows 11 "modern" XAML context menu by blanking the
    // shell host CLSID. Explorer falls back to the legacy #32768 popup,
    // which ContextMenuGuard greys out item-by-item.
    //
    //   HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32 = ""
    //
    // Per-user (HKCU), no admin required at the registry layer; explorer.exe
    // restart needs the elevation we already have. Restored on authorised exit.
    public sealed class ModernMenuPolicy
    {
        private const string ClsidPath =
            @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
        private const string InprocSubPath = ClsidPath + @"\InprocServer32";

        private bool _applied;
        private bool _hadOriginal;
        private string _originalValue;
        private bool _createdParent;

        public void Apply(bool restartExplorer)
        {
            if (_applied) return;
            try
            {
                using (var parent = Registry.CurrentUser.OpenSubKey(ClsidPath))
                {
                    _createdParent = (parent == null);
                }

                using (var sub = Registry.CurrentUser.CreateSubKey(InprocSubPath))
                {
                    if (sub == null)
                    {
                        AuditLogger.Instance.Error("MODERNMENU", "Cannot create " + InprocSubPath);
                        return;
                    }
                    object cur = sub.GetValue(null, null);
                    _hadOriginal = (cur != null);
                    _originalValue = cur as string;
                    sub.SetValue(null, "", RegistryValueKind.String);
                }
                _applied = true;
                AuditLogger.Instance.Info("MODERNMENU", "Modern context menu disabled (legacy fallback)");

                if (restartExplorer) RestartExplorer();
            }
            catch (Exception ex)
            {
                AuditLogger.Instance.Error("MODERNMENU", "Apply: " + ex.Message);
            }
        }

        public void Restore(bool restartExplorer)
        {
            if (!_applied) return;
            try
            {
                if (_hadOriginal)
                {
                    using (var sub = Registry.CurrentUser.CreateSubKey(InprocSubPath))
                    {
                        if (sub != null)
                        {
                            sub.SetValue(null, _originalValue == null ? "" : _originalValue,
                                RegistryValueKind.String);
                        }
                    }
                }
                else
                {
                    // We created the key — remove it cleanly.
                    try
                    {
                        if (_createdParent)
                            Registry.CurrentUser.DeleteSubKeyTree(ClsidPath, false);
                        else
                            Registry.CurrentUser.DeleteSubKeyTree(InprocSubPath, false);
                    }
                    catch { }
                }
                _applied = false;
                AuditLogger.Instance.Info("MODERNMENU", "Modern context menu restored");

                if (restartExplorer) RestartExplorer();
            }
            catch (Exception ex)
            {
                AuditLogger.Instance.Error("MODERNMENU", "Restore: " + ex.Message);
            }
        }

        private static void RestartExplorer()
        {
            try
            {
                var procs = Process.GetProcessesByName("explorer");
                foreach (var p in procs)
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                    finally { p.Dispose(); }
                }
                // Explorer normally auto-restarts; kick it if it didn't.
                Thread.Sleep(1200);
                if (Process.GetProcessesByName("explorer").Length == 0)
                {
                    var psi = new ProcessStartInfo("explorer.exe");
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                AuditLogger.Instance.Info("MODERNMENU", "Explorer restarted to apply menu policy");
            }
            catch (Exception ex)
            {
                AuditLogger.Instance.Warn("MODERNMENU", "Explorer restart failed: " + ex.Message);
            }
        }
    }
}
