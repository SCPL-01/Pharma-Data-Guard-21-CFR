using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using PharmaDataGuard.Core;

namespace PharmaDataGuard.UI
{
    public sealed class ChangePasswordDialog : Form
    {
        private readonly TextBox _old;
        private readonly TextBox _new;
        private readonly TextBox _confirm;
        private readonly Label _status;
        private readonly Button _ok;
        private int _failedOld;
        private const int MaxOldAttempts = 5;
        private const string FactoryDefault = "PharmaGuard@123";

        public ChangePasswordDialog()
        {
            this.Text = "Pharma Data Guard — Change Password";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(480, 330);

            var hdr = new Label
            {
                Text = "Change administrator password",
                Location = new Point(20, 15),
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 11, FontStyle.Bold)
            };

            var rules = new Label
            {
                Text = "New password must be at least 12 characters and include uppercase,\r\n" +
                       "lowercase, digit, and a symbol. Cannot equal the factory default,\r\n" +
                       "and must differ from the current password.",
                Location = new Point(20, 45),
                Size = new Size(440, 50)
            };

            var lblOld = new Label
            {
                Text = "Current password:",
                Location = new Point(20, 105),
                AutoSize = true
            };
            _old = new TextBox
            {
                Location = new Point(190, 102),
                Size = new Size(270, 24),
                UseSystemPasswordChar = true
            };

            var lblNew = new Label
            {
                Text = "New password:",
                Location = new Point(20, 140),
                AutoSize = true
            };
            _new = new TextBox
            {
                Location = new Point(190, 137),
                Size = new Size(270, 24),
                UseSystemPasswordChar = true
            };

            var lblCon = new Label
            {
                Text = "Confirm new password:",
                Location = new Point(20, 175),
                AutoSize = true
            };
            _confirm = new TextBox
            {
                Location = new Point(190, 172),
                Size = new Size(270, 24),
                UseSystemPasswordChar = true
            };

            _status = new Label
            {
                Location = new Point(20, 210),
                Size = new Size(440, 60),
                ForeColor = Color.Firebrick
            };

            _ok = new Button
            {
                Text = "Change password",
                Location = new Point(330, 285),
                Size = new Size(130, 30),
                DialogResult = DialogResult.None
            };
            _ok.Click += OnOk;

            var cancel = new Button
            {
                Text = "Cancel",
                Location = new Point(235, 285),
                Size = new Size(85, 30),
                DialogResult = DialogResult.Cancel
            };

            this.Controls.Add(hdr);
            this.Controls.Add(rules);
            this.Controls.Add(lblOld);
            this.Controls.Add(_old);
            this.Controls.Add(lblNew);
            this.Controls.Add(_new);
            this.Controls.Add(lblCon);
            this.Controls.Add(_confirm);
            this.Controls.Add(_status);
            this.Controls.Add(_ok);
            this.Controls.Add(cancel);

            this.AcceptButton = _ok;
            this.CancelButton = cancel;
            this.ActiveControl = _old;
        }

        private static readonly Regex Upper = new Regex("[A-Z]", RegexOptions.Compiled);
        private static readonly Regex Lower = new Regex("[a-z]", RegexOptions.Compiled);
        private static readonly Regex Digit = new Regex("[0-9]", RegexOptions.Compiled);
        private static readonly Regex Symbol = new Regex("[^A-Za-z0-9]", RegexOptions.Compiled);

        private void OnOk(object sender, EventArgs e)
        {
            string oldPw = _old.Text;
            string p1 = _new.Text;
            string p2 = _confirm.Text;

            if (!AppConfig.Instance.VerifyPassword(oldPw))
            {
                _failedOld++;
                _old.Clear();
                _status.Text = "Current password incorrect. Attempt " + _failedOld + "/" + MaxOldAttempts + ".";
                AuditLogger.Instance.Warn("AUTH", "Change-password: wrong current attempt " + _failedOld + "/" + MaxOldAttempts);
                if (_failedOld >= MaxOldAttempts)
                {
                    AuditLogger.Instance.Warn("AUTH", "Change-password: max attempts reached — closing");
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                }
                return;
            }

            if (p1 != p2) { _status.Text = "New passwords do not match."; return; }
            if (p1.Length < 12) { _status.Text = "New password must be at least 12 characters."; return; }
            if (!Upper.IsMatch(p1)) { _status.Text = "Must contain at least one uppercase letter."; return; }
            if (!Lower.IsMatch(p1)) { _status.Text = "Must contain at least one lowercase letter."; return; }
            if (!Digit.IsMatch(p1)) { _status.Text = "Must contain at least one digit."; return; }
            if (!Symbol.IsMatch(p1)) { _status.Text = "Must contain at least one symbol."; return; }
            if (p1 == FactoryDefault) { _status.Text = "Cannot reuse the factory-default password."; return; }
            if (p1 == oldPw) { _status.Text = "New password must differ from current password."; return; }

            try
            {
                AppConfig.Instance.SetPassword(p1);
                AppConfig.Instance.Save();
                AuditLogger.Instance.Info("AUTH", "Administrator password changed");
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                _status.Text = "Save failed: " + ex.Message;
                AuditLogger.Instance.Error("AUTH", "Change-password save: " + ex.Message);
            }
        }
    }
}
