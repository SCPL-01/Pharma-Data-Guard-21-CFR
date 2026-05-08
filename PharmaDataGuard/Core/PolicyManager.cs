using System;
using Microsoft.Win32;

namespace PharmaDataGuard.Core
{
    public sealed class PolicyManager
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
        private const string ValueName = "DisableTaskMgr";

        private bool _applied;
        private object _originalValue;

        public void Apply()
        {
            if (_applied) return;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
                {
                    if (key == null)
                    {
                        AuditLogger.Instance.Error("POLICY", "Cannot open " + KeyPath);
                        return;
                    }
                    _originalValue = key.GetValue(ValueName, null);
                    key.SetValue(ValueName, 1, RegistryValueKind.DWord);
                }
                _applied = true;
                AuditLogger.Instance.Info("POLICY", "DisableTaskMgr=1 applied");
            }
            catch (Exception ex)
            {
                AuditLogger.Instance.Error("POLICY", "Apply: " + ex.Message);
            }
        }

        public void Restore()
        {
            if (!_applied) return;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true))
                {
                    if (key == null) return;
                    if (_originalValue == null)
                    {
                        key.DeleteValue(ValueName, throwOnMissingValue: false);
                    }
                    else
                    {
                        key.SetValue(ValueName, _originalValue, RegistryValueKind.DWord);
                    }
                }
                _applied = false;
                AuditLogger.Instance.Info("POLICY", "DisableTaskMgr restored");
            }
            catch (Exception ex)
            {
                AuditLogger.Instance.Error("POLICY", "Restore: " + ex.Message);
            }
        }
    }
}
