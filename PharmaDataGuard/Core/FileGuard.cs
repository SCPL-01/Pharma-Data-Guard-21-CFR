using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PharmaDataGuard.Core
{
    public sealed class FileGuard
    {
        private static string BackupDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "PharmaDataGuard", "AclBackup");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private const FileSystemRights DenyRights =
            FileSystemRights.Delete |
            FileSystemRights.DeleteSubdirectoriesAndFiles |
            FileSystemRights.Write |
            FileSystemRights.ChangePermissions |
            FileSystemRights.TakeOwnership;

        private static SecurityIdentifier WorldSid
        {
            get { return new SecurityIdentifier(WellKnownSidType.WorldSid, null); }
        }

        public void Lock(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                if (Directory.Exists(path))
                {
                    BackupSddlIfMissing(path, true);
                    var di = new DirectoryInfo(path);
                    var sec = di.GetAccessControl();
                    var rule = new FileSystemAccessRule(
                        WorldSid,
                        DenyRights,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Deny);
                    sec.AddAccessRule(rule);
                    di.SetAccessControl(sec);
                    AuditLogger.Instance.Info("FILEGUARD", "DENY ACE applied (dir): " + path);
                }
                else if (File.Exists(path))
                {
                    BackupSddlIfMissing(path, false);
                    var fi = new FileInfo(path);
                    var sec = fi.GetAccessControl();
                    var rule = new FileSystemAccessRule(
                        WorldSid,
                        DenyRights,
                        AccessControlType.Deny);
                    sec.AddAccessRule(rule);
                    fi.SetAccessControl(sec);
                    File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
                    AuditLogger.Instance.Info("FILEGUARD", "DENY ACE applied (file): " + path);
                }
                else
                {
                    AuditLogger.Instance.Warn("FILEGUARD", "Path missing — skipped: " + path);
                }
            }
            catch (Exception ex)
            {
                AuditLogger.Instance.Error("FILEGUARD", "Lock(" + path + "): " + ex.Message);
            }
        }

        public void Unlock(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                if (Directory.Exists(path))
                {
                    var di = new DirectoryInfo(path);
                    var sec = di.GetAccessControl();
                    var deny = new FileSystemAccessRule(
                        WorldSid,
                        DenyRights,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None,
                        AccessControlType.Deny);
                    sec.RemoveAccessRuleAll(deny);
                    di.SetAccessControl(sec);
                    AuditLogger.Instance.Info("FILEGUARD", "DENY ACE removed (dir): " + path);
                }
                else if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    var sec = fi.GetAccessControl();
                    var deny = new FileSystemAccessRule(WorldSid, DenyRights, AccessControlType.Deny);
                    sec.RemoveAccessRuleAll(deny);
                    fi.SetAccessControl(sec);
                    var attrs = File.GetAttributes(path);
                    if ((attrs & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
                        File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
                    AuditLogger.Instance.Info("FILEGUARD", "DENY ACE removed (file): " + path);
                }
            }
            catch (Exception ex)
            {
                AuditLogger.Instance.Error("FILEGUARD", "Unlock(" + path + "): " + ex.Message);
            }
        }

        private static void BackupSddlIfMissing(string path, bool isDir)
        {
            try
            {
                string key = SafeKey(path);
                string outFile = Path.Combine(BackupDir, key + ".sddl");
                if (File.Exists(outFile)) return;

                string sddl;
                if (isDir)
                {
                    var di = new DirectoryInfo(path);
                    sddl = di.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
                }
                else
                {
                    var fi = new FileInfo(path);
                    sddl = fi.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
                }
                File.WriteAllText(outFile, path + Environment.NewLine + sddl);
            }
            catch (Exception ex)
            {
                AuditLogger.Instance.Warn("FILEGUARD", "SDDL backup failed for " + path + ": " + ex.Message);
            }
        }

        private static string SafeKey(string path)
        {
            var sb = new System.Text.StringBuilder(path.Length);
            foreach (char c in path)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else sb.Append('_');
            }
            string s = sb.ToString();
            if (s.Length > 120) s = s.Substring(0, 120) + "_" + path.GetHashCode().ToString("x");
            return s;
        }
    }
}
