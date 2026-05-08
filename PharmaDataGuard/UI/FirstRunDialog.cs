using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using PharmaDataGuard.Core;

namespace PharmaDataGuard.UI
{
    public sealed class FirstRunDialog : Form
    {
        private readonly TextBox _new;
        private readonly TextBox _confirm;
        private readonly Label _status;
        private readonly Button _ok;
        private bool _completed;

        private const string FactoryDefault = "PharmaGuard@123";

        public FirstRunDialog()
        {
            this.Text = "Pharma Data Guard — First-Run Setup";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(480, 290);

            var hdr = new Label
            {
                Text = "Set the Pharma Data Guard administrator password",
                Location = new Point(20, 15),
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 11, FontStyle.Bold)
            };

            var rules = new Label
            {
                Text = "Password must be at least 12 characters and include uppercase,\r\n" +
                       "lowercase, digit, and a symbol. Cannot equal the factory default.",
                Location = new Point(20, 45),
                Size = new Size(440, 40)
            };

            var lblNew = new Label { Text = "New password:", Location = new Point(20, 95), AutoSize = true };
            _new = new TextBox
            {
                Location = new Point(170, 92),
                Size = new Size(290, 24),
                UseSystemPasswordChar = true
            };

            var lblCon = new Label { Text = "Confirm password:", Location = new Point(20, 130), AutoSize = true };
            _confirm = new TextBox
            {
                Location = new Point(170, 127),
                Size = new Size(290, 24),
                UseSystemPasswordChar = true
            };

            _status = new Label
            {
                Location = new Point(20, 165),
                Size = new Size(440, 60),
                ForeColor = Color.Firebrick
            };

            _ok = new Button
            {
                Text = "Set password",
                Location = new Point(330, 240),
                Size = new Size(130, 30),
                DialogResult = DialogResult.None
            };
            _ok.Click += OnOk;

            this.Controls.Add(hdr);
            this.Controls.Add(rules);
            this.Controls.Add(lblNew);
            this.Controls.Add(_new);
            this.Controls.Add(lblCon);
            this.Controls.Add(_confirm);
            this.Controls.Add(_status);
            this.Controls.Add(_ok);

            this.AcceptButton = _ok;
            this.ActiveControl = _new;
        }

        private static readonly Regex Upper = new Regex("[A-Z]", RegexOptions.Compiled);
        private static readonly Regex Lower = new Regex("[a-z]", RegexOptions.Compiled);
        private static readonly Regex Digit = new Regex("[0-9]", RegexOptions.Compiled);
        private static readonly Regex Symbol = new Regex("[^A-Za-z0-9]", RegexOptions.Compiled);

        private void OnOk(object sender, EventArgs e)
        {
            string p1 = _new.Text;
            string p2 = _confirm.Text;

            if (p1 != p2) { _status.Text = "Passwords do not match."; return; }
            if (p1.Length < 12) { _status.Text = "Password must be at least 12 characters."; return; }
            if (!Upper.IsMatch(p1)) { _status.Text = "Must contain at least one uppercase letter."; return; }
            if (!Lower.IsMatch(p1)) { _status.Text = "Must contain at least one lowercase letter."; return; }
            if (!Digit.IsMatch(p1)) { _status.Text = "Must contain at least one digit."; return; }
            if (!Symbol.IsMatch(p1)) { _status.Text = "Must contain at least one symbol."; return; }
            if (p1 == FactoryDefault) { _status.Text = "Cannot reuse the factory-default password."; return; }

            try
            {
                AppConfig.Instance.SetPassword(p1);
                AppConfig.Instance.Save();
                AuditLogger.Instance.Info("AUTH", "First-run password set");
                _completed = true;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                _status.Text = "Save failed: " + ex.Message;
                AuditLogger.Instance.Error("AUTH", "First-run set: " + ex.Message);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_completed && e.CloseReason == CloseReason.UserClosing)
            {
                var resp = MessageBox.Show(
                    "Pharma Data Guard cannot start until a password is set.\n\nCancel setup and exit Pharma Data Guard?",
                    "Pharma Data Guard", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (resp == DialogResult.No) { e.Cancel = true; return; }
                this.DialogResult = DialogResult.Cancel;
            }
            base.OnFormClosing(e);
        }
    }
}
