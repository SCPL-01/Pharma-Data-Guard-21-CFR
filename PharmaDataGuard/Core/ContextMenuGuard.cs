using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PharmaDataGuard.Core
{
    public sealed class ContextMenuGuard
    {
        private const uint MN_GETHMENU = 0x01E1;
        private const uint MF_BYPOSITION = 0x00000400;
        private const uint MF_GRAYED = 0x00000001;
        private const uint MF_DISABLED = 0x00000002;
        private const uint MF_SEPARATOR = 0x00000800;
        private const int MaxDepth = 4;

        private static readonly string[] ForbiddenPrefixes = new[]
        {
            // English
            "copy", "cut", "paste", "delete", "move to", "send to", "share",
            "upload", "open with", "export", "save as", "save a copy", "print", "rename",
            // Hindi
            "कॉपी", "काटें", "चिपकाएं", "हटाएं", "मिटाएं",
            // Gujarati
            "કૉપિ", "પેસ્ટ", "કાઢી"
        };

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetMenuItemCount(IntPtr hMenu);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetMenuString(IntPtr hMenu, uint uIDItem, StringBuilder lpString, int nMaxCount, uint uFlag);

        [DllImport("user32.dll")]
        private static extern uint GetMenuState(IntPtr hMenu, uint uId, uint uFlags);

        [DllImport("user32.dll")]
        private static extern int EnableMenuItem(IntPtr hMenu, uint uIDEnableItem, uint uEnable);

        [DllImport("user32.dll")]
        private static extern IntPtr GetSubMenu(IntPtr hMenu, int nPos);

        private System.Windows.Forms.Timer _timer;
        private long _lastLogTicks;
        private IntPtr _lastSeenHMenu = IntPtr.Zero;

        public void Start()
        {
            if (_timer != null) return;
            _timer = new System.Windows.Forms.Timer { Interval = 50 };
            _timer.Tick += OnTick;
            _timer.Start();
            AuditLogger.Instance.Info("MENU", "Context menu scanner started");
        }

        public void Stop()
        {
            if (_timer == null) return;
            _timer.Stop();
            _timer.Dispose();
            _timer = null;
            AuditLogger.Instance.Info("MENU", "Context menu scanner stopped");
        }

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                IntPtr menuWnd = FindWindow("#32768", null);
                if (menuWnd == IntPtr.Zero || !IsWindowVisible(menuWnd))
                {
                    _lastSeenHMenu = IntPtr.Zero;
                    return;
                }

                IntPtr hMenu = SendMessage(menuWnd, MN_GETHMENU, IntPtr.Zero, IntPtr.Zero);
                if (hMenu == IntPtr.Zero) return;

                // Each popup menu instance only needs to be processed once.
                if (hMenu == _lastSeenHMenu) return;
                _lastSeenHMenu = hMenu;

                ProcessMenu(hMenu);
            }
            catch (Exception ex)
            {
                AuditLogger.Instance.Error("MENU", "tick: " + ex.Message);
            }
        }

        private void ProcessMenu(IntPtr hMenu)
        {
            // Submenus are not walked here — when the user hovers over a submenu, Windows
            // creates a new #32768 popup window for it, which the next tick will catch.
            // Walking unrendered submenus interferes with shell context-menu construction.
            int count = GetMenuItemCount(hMenu);
            if (count <= 0) return;

            for (uint i = 0; i < (uint)count; i++)
            {
                uint state = GetMenuState(hMenu, i, MF_BYPOSITION);
                if (state == 0xFFFFFFFF) continue;
                if ((state & MF_SEPARATOR) != 0) continue;

                var sb = new StringBuilder(256);
                int len = GetMenuString(hMenu, i, sb, sb.Capacity, MF_BYPOSITION);
                if (len > 0)
                {
                    string text = NormalizeMenuText(sb.ToString());
                    if (IsForbidden(text))
                    {
                        EnableMenuItem(hMenu, i, MF_BYPOSITION | MF_GRAYED | MF_DISABLED);
                        ThrottledLog(text);
                    }
                }
            }
        }

        private static string NormalizeMenuText(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            // Strip ampersand accelerator markers ("&Copy" -> "Copy"), but keep "&&" -> "&"
            var sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (c == '&')
                {
                    if (i + 1 < raw.Length && raw[i + 1] == '&') { sb.Append('&'); i++; }
                    continue;
                }
                sb.Append(c);
            }
            string s = sb.ToString();
            int tab = s.IndexOf('\t');
            if (tab >= 0) s = s.Substring(0, tab);
            return s.Trim();
        }

        private static bool IsForbidden(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string lower = text.ToLowerInvariant();
            foreach (var pref in ForbiddenPrefixes)
            {
                if (lower.StartsWith(pref, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void ThrottledLog(string text)
        {
            long now = Stopwatch.GetTimestamp();
            long elapsedMs = (now - _lastLogTicks) * 1000 / Stopwatch.Frequency;
            if (elapsedMs < 1000) return;
            _lastLogTicks = now;
            AuditLogger.Instance.Blocked("MENU", "grayed", "item=" + text);
        }
    }
}
