using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace PharmaDataGuard.Core
{
    public sealed class KeyboardHook
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        private const int VK_CONTROL = 0x11;
        private const int VK_SHIFT = 0x10;
        private const int VK_MENU = 0x12;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        // CRITICAL: keep delegate alive — native code holds a function pointer to it.
        private LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public void Start()
        {
            if (_hookId != IntPtr.Zero) return;
            _proc = HookCallback;
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
            }
            if (_hookId == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                AuditLogger.Instance.Error("KEYBOARD", "SetWindowsHookEx failed err=" + err);
            }
            else
            {
                AuditLogger.Instance.Info("KEYBOARD", "Low-level keyboard hook installed");
            }
        }

        public void Stop()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
                AuditLogger.Instance.Info("KEYBOARD", "Hook removed");
            }
            _proc = null;
        }

        private static bool IsDown(int vk)
        {
            return (GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
                {
                    var data = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                    Keys vk = (Keys)data.vkCode;

                    bool ctrl = IsDown(VK_CONTROL);
                    bool shift = IsDown(VK_SHIFT);
                    bool alt = IsDown(VK_MENU);
                    bool win = IsDown(VK_LWIN) || IsDown(VK_RWIN);

                    var cfg = AppConfig.Instance;
                    string blockedCombo = null;

                    if (vk == Keys.PrintScreen) blockedCombo = ComboName(ctrl, shift, alt, win, "PrintScreen");
                    else if (win && shift && vk == Keys.S) blockedCombo = "Win+Shift+S";
                    else if (win && vk == Keys.PrintScreen) blockedCombo = "Win+PrintScreen";
                    else if (alt && vk == Keys.PrintScreen) blockedCombo = "Alt+PrintScreen";
                    else if (ctrl && vk == Keys.C) blockedCombo = "Ctrl+C";
                    else if (ctrl && vk == Keys.X) blockedCombo = "Ctrl+X";
                    else if (ctrl && vk == Keys.V) blockedCombo = "Ctrl+V";
                    else if (ctrl && vk == Keys.Insert) blockedCombo = "Ctrl+Insert";
                    else if (shift && vk == Keys.Insert) blockedCombo = "Shift+Insert";
                    else if (shift && vk == Keys.Delete) blockedCombo = "Shift+Delete";
                    else if (vk == Keys.Delete && !ctrl && !alt && !win) blockedCombo = "Delete";
                    else if (cfg.BlockSelectAll && ctrl && vk == Keys.A) blockedCombo = "Ctrl+A";
                    else if (cfg.BlockSave && ctrl && vk == Keys.S) blockedCombo = "Ctrl+S";
                    else if (cfg.BlockPrint && ctrl && vk == Keys.P) blockedCombo = "Ctrl+P";

                    if (blockedCombo != null)
                    {
                        AuditLogger.Instance.Blocked("KEYBOARD", blockedCombo, GetForegroundProcessName());
                        return (IntPtr)1;
                    }
                }
            }
            catch (Exception ex)
            {
                AuditLogger.Instance.Error("KEYBOARD", "callback: " + ex.Message);
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private static string ComboName(bool ctrl, bool shift, bool alt, bool win, string key)
        {
            var sb = new StringBuilder();
            if (ctrl) sb.Append("Ctrl+");
            if (shift) sb.Append("Shift+");
            if (alt) sb.Append("Alt+");
            if (win) sb.Append("Win+");
            sb.Append(key);
            return sb.ToString();
        }

        private static string GetForegroundProcessName()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero) return "unknown";
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                using (var p = Process.GetProcessById((int)pid))
                {
                    return p.ProcessName + " (pid=" + pid + ")";
                }
            }
            catch { return "unknown"; }
        }
    }
}
