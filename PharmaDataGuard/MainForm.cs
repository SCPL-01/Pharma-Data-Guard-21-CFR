using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PharmaDataGuard.Core;
using PharmaDataGuard.UI;

namespace PharmaDataGuard
{
    public sealed class MainForm : Form
    {
        private NotifyIcon _tray;
        private ContextMenuStrip _trayMenu;

        private KeyboardHook _keyboard;
        private ClipboardGuard _clipboard;
        private ContextMenuGuard _menuGuard;
        private FileGuard _files;
        private PolicyManager _policy;
        private MouseGuard _mouse;
        private ModernMenuPolicy _modernMenu;

        private const string DefaultPassword = "PharmaGuard@123";

        public MainForm()
        {
            this.Text = "Pharma Data Guard";
            this.ShowInTaskbar = false;
            this.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            this.StartPosition = FormStartPosition.Manual;
            this.Location = new Point(-32000, -32000);
            this.Size = new Size(1, 1);
            this.Opacity = 0;
            this.Load += OnLoad;
            this.Shown += OnShownHide;
            this.FormClosing += OnFormClosingHandler;
        }

        private void OnShownHide(object sender, EventArgs e)
        {
            // Form must be made visible briefly so OnLoad fires; immediately hide it.
            this.Hide();
        }

        private void OnLoad(object sender, EventArgs e)
        {
            try
            {
                AuditLogger.Instance.Info("LIFECYCLE", "Pharma Data Guard starting");

                if (AppConfig.Instance.VerifyPassword(DefaultPassword))
                {
                    using (var first = new FirstRunDialog())
                    {
                        if (first.ShowDialog() != DialogResult.OK)
                        {
                            AuditLogger.Instance.Warn("LIFECYCLE", "FirstRunDialog cancelled — exiting");
                            Application.Exit();
                            return;
                        }
                    }
                }

                BuildTray();
                StartGuards();

                _tray.ShowBalloonTip(4000, "Pharma Data Guard", "Copy / Paste / Delete are locked for audit.", ToolTipIcon.Info);
                AuditLogger.Instance.Info("LIFECYCLE", "Pharma Data Guard active");
            }
            catch (Exception ex)
            {
                AuditLogger.Instance.Error("LIFECYCLE", ex.ToString());
                MessageBox.Show("Pharma Data Guard failed to start: " + ex.Message, "Pharma Data Guard",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
        }

        private void BuildTray()
        {
            _trayMenu = new ContextMenuStrip();
            var hdr = new ToolStripMenuItem("Pharma Data Guard — ACTIVE");
            hdr.Enabled = false;
            _trayMenu.Items.Add(hdr);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Add protected path…", null, OnAddPath);
            _trayMenu.Items.Add("Open audit log", null, OnOpenLog);
            _trayMenu.Items.Add("Change password…", null, OnChangePassword);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Exit (requires password)", null, OnExit);

            _tray = new NotifyIcon();
            _tray.Icon = SystemIcons.Shield;
            _tray.Text = "Pharma Data Guard — audit lockdown active";
            _tray.Visible = true;
            _tray.ContextMenuStrip = _trayMenu;
        }

        private void StartGuards()
        {
            var cfg = AppConfig.Instance;

            _keyboard = new KeyboardHook();
            _keyboard.Start();

            _clipboard = new ClipboardGuard();
            _clipboard.Start();

            _menuGuard = new ContextMenuGuard();
            _menuGuard.Start();

            _files = new FileGuard();
            foreach (var p in cfg.ProtectedPaths)
            {
                _files.Lock(p);
            }

            _policy = new PolicyManager();
            _policy.Apply();

            if (cfg.EnableLegacyContextMenuFallback)
            {
                _modernMenu = new ModernMenuPolicy();
                _modernMenu.Apply(cfg.RestartExplorerOnPolicyChange);
            }

            if (cfg.BlockFileDragDrop)
            {
                _mouse = new MouseGuard();
                _mouse.Start();
            }

            if (cfg.EnableWatchdog)
            {
                Watchdog.StartWatchingSelf();
            }
        }

        private void OnAddPath(object sender, EventArgs e)
        {
            using (var dlg = new UnlockDialog())
            {
                if (dlg.ShowDialog() != DialogResult.OK || !dlg.Unlocked) return;
            }

            using (var fb = new FolderBrowserDialog())
            {
                fb.Description = "Select folder to protect (NTFS DENY ACE)";
                if (fb.ShowDialog() != DialogResult.OK) return;
                string path = fb.SelectedPath;
                try
                {
                    _files.Lock(path);
                    var cfg = AppConfig.Instance;
                    if (!cfg.ProtectedPaths.Contains(path))
                    {
                        cfg.ProtectedPaths.Add(path);
                        cfg.Save();
                    }
                    AuditLogger.Instance.Info("FILEGUARD", "Protected path added: " + path);
                    _tray.ShowBalloonTip(3000, "Pharma Data Guard", "Protected: " + path, ToolTipIcon.Info);
                }
                catch (Exception ex)
                {
                    AuditLogger.Instance.Error("FILEGUARD", "Add path failed: " + ex.Message);
                    MessageBox.Show("Failed to protect path: " + ex.Message, "Pharma Data Guard",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void OnOpenLog(object sender, EventArgs e)
        {
            try
            {
                string path = AuditLogger.GlobalLogPath;
                if (File.Exists(path))
                {
                    var psi = new ProcessStartInfo("notepad.exe", "\"" + path + "\"");
                    psi.UseShellExecute = true;
                    Process.Start(psi);
                }
                else
                {
                    MessageBox.Show("Audit log not yet created.", "Pharma Data Guard");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to open log: " + ex.Message, "Pharma Data Guard");
            }
        }

        private void OnChangePassword(object sender, EventArgs e)
        {
            using (var dlg = new ChangePasswordDialog())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _tray.ShowBalloonTip(3000, "Pharma Data Guard", "Administrator password changed.", ToolTipIcon.Info);
                }
            }
        }

        private void OnExit(object sender, EventArgs e)
        {
            using (var dlg = new UnlockDialog())
            {
                if (dlg.ShowDialog() != DialogResult.OK || !dlg.Unlocked)
                {
                    AuditLogger.Instance.Warn("LIFECYCLE", "Exit refused — wrong/cancelled password");
                    return;
                }
            }

            AuditLogger.Instance.Info("LIFECYCLE", "Authorised exit — tearing down guards");

            // Kill the sibling watchdog FIRST. Otherwise, the moment Application.Exit()
            // takes us down, the watchdog detects "parent died" and relaunches a fresh
            // Pharma Data Guard — to the user it looks like Exit silently failed.
            try { Watchdog.StopAndKillWatchdog(); } catch { }

            try { if (_keyboard != null) _keyboard.Stop(); } catch { }
            try { if (_clipboard != null) _clipboard.Stop(); } catch { }
            try { if (_menuGuard != null) _menuGuard.Stop(); } catch { }
            try { if (_mouse != null) _mouse.Stop(); } catch { }
            try { if (_policy != null) _policy.Restore(); } catch { }
            try
            {
                if (_modernMenu != null)
                    _modernMenu.Restore(AppConfig.Instance.RestartExplorerOnPolicyChange);
            } catch { }

            try
            {
                if (_files != null)
                {
                    foreach (var p in AppConfig.Instance.ProtectedPaths)
                    {
                        try { _files.Unlock(p); } catch { }
                    }
                }
            }
            catch { }

            try
            {
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                }
            }
            catch { }

            AuditLogger.Instance.Info("LIFECYCLE", "Pharma Data Guard stopped");
            Application.Exit();

            // Defensive: if anything keeps the message loop alive, force-terminate
            // after a short grace period so the tray actually disappears.
            var bail = new System.Windows.Forms.Timer();
            bail.Interval = 1500;
            bail.Tick += delegate
            {
                try { Environment.Exit(0); } catch { }
            };
            bail.Start();
        }

        private void OnFormClosingHandler(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                AuditLogger.Instance.Warn("LIFECYCLE", "UserClosing close-attempt blocked");
            }
        }
    }
}
