using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PharmaDataGuard.Core
{
    public sealed class ClipboardGuard : NativeWindow, IDisposable
    {
        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenClipboard(IntPtr hWndNewOwner);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern int CountClipboardFormats();

        private bool _listenerOk;
        private System.Windows.Forms.Timer _pollTimer;
        private bool _started;
        private long _lastLogTickMs;

        public void Start()
        {
            if (_started) return;
            _started = true;

            var cp = new CreateParams();
            cp.Caption = "PharmaDataGuard_ClipMsgWnd";
            cp.Parent = HWND_MESSAGE;
            CreateHandle(cp);

            _listenerOk = AddClipboardFormatListener(this.Handle);
            if (_listenerOk)
            {
                AuditLogger.Instance.Info("CLIPBOARD", "AddClipboardFormatListener registered");
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                AuditLogger.Instance.Warn("CLIPBOARD", "Listener failed err=" + err + " — using polling fallback");
                _pollTimer = new System.Windows.Forms.Timer();
                _pollTimer.Interval = 100;
                _pollTimer.Tick += OnPollTick;
                _pollTimer.Start();
            }

            WipeClipboard("init");
        }

        private void OnPollTick(object sender, EventArgs e)
        {
            WipeClipboard("poll");
        }

        public void Stop()
        {
            if (!_started) return;
            try
            {
                if (_listenerOk) RemoveClipboardFormatListener(this.Handle);
                if (_pollTimer != null) { _pollTimer.Stop(); _pollTimer.Dispose(); _pollTimer = null; }
                if (this.Handle != IntPtr.Zero) DestroyHandle();
            }
            catch (Exception ex)
            {
                AuditLogger.Instance.Error("CLIPBOARD", "stop: " + ex.Message);
            }
            _started = false;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                WipeClipboard("update");
                return;
            }
            base.WndProc(ref m);
        }

        private static readonly object _wipeLock = new object();

        private void WipeClipboard(string trigger)
        {
            lock (_wipeLock)
            {
                // Break the EmptyClipboard → WM_CLIPBOARDUPDATE → EmptyClipboard feedback loop:
                // if the clipboard is already empty, the notification was caused by our own wipe.
                try
                {
                    if (CountClipboardFormats() == 0 && trigger != "init") return;
                }
                catch { }

                bool wiped = false;
                for (int i = 0; i < 5; i++)
                {
                    if (OpenClipboard(IntPtr.Zero))
                    {
                        try
                        {
                            EmptyClipboard();
                            wiped = true;
                        }
                        finally
                        {
                            CloseClipboard();
                        }
                        break;
                    }
                    Thread.Sleep(10);
                }

                if (wiped && trigger == "update")
                {
                    long now = Environment.TickCount;
                    if (now - _lastLogTickMs > 1000 || _lastLogTickMs == 0)
                    {
                        _lastLogTickMs = now;
                        AuditLogger.Instance.Blocked("CLIPBOARD", "wiped", "trigger=" + trigger);
                    }
                }
                else if (!wiped)
                {
                    AuditLogger.Instance.Warn("CLIPBOARD", "Open failed after 5 retries trigger=" + trigger);
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
