using System;
using System.Drawing;
using System.Windows.Forms;
using PharmaDataGuard.Core;

namespace PharmaDataGuard.UI
{
    public sealed class UnlockDialog : Form
    {
        private readonly TextBox _password;
        private readonly Button _ok;
        private readonly Button _cancel;
        private readonly Label _status;
        private int _attempts;
        private const int MaxAttempts = 5;

        public bool Unlocked { get; private set; }

        public UnlockDialog()
        {
            this.Text = "Pharma Data Guard — Unlock";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ClientSize = new Size(420, 180);

            var lbl = new Label
            {
                Text = "Enter administrator password to proceed:",
                Location = new Point(20, 20),
                AutoSize = true
            };

            _password = new TextBox
            {
                Location = new Point(20, 55),
                Size = new Size(380, 24),
                UseSystemPasswordChar = true
            };

            _status = new Label
            {
                Location = new Point(20, 90),
                Size = new Size(380, 24),
                ForeColor = Color.Firebrick
            };

            _ok = new Button
            {
                Text = "Unlock",
                Location = new Point(220, 130),
                Size = new Size(85, 28),
                DialogResult = DialogResult.None
            };
            _ok.Click += OnOk;

            _cancel = new Button
            {
                Text = "Cancel",
                Location = new Point(315, 130),
                Size = new Size(85, 28),
                DialogResult = DialogResult.Cancel
            };

            this.Controls.Add(lbl);
            this.Controls.Add(_password);
            this.Controls.Add(_status);
            this.Controls.Add(_ok);
            this.Controls.Add(_cancel);

            this.AcceptButton = _ok;
            this.CancelButton = _cancel;
            this.ActiveControl = _password;
        }

        private void OnOk(object sender, EventArgs e)
        {
            _attempts++;
            string pw = _password.Text;
            if (AppConfig.Instance.VerifyPassword(pw))
            {
                Unlocked = true;
                AuditLogger.Instance.Info("AUTH", "Unlock dialog: success");
                this.DialogResult = DialogResult.OK;
                this.Close();
                return;
            }

            _password.Clear();
            _status.Text = "Incorrect. Attempt " + _attempts + "/" + MaxAttempts + ".";
            AuditLogger.Instance.Warn("AUTH", "Unlock dialog: wrong attempt " + _attempts + "/" + MaxAttempts);

            if (_attempts >= MaxAttempts)
            {
                AuditLogger.Instance.Warn("AUTH", "Unlock dialog: max attempts reached — closing");
                Unlocked = false;
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            }
        }
    }
}
