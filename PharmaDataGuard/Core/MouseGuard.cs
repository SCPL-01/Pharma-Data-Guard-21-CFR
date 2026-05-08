using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PharmaDataGuard.Core
{
    // Drag-and-drop blocker. Two complementary signals:
    //
    //   1. SetWinEventHook on EVENT_OBJECT_DRAGSTART (0x8021) and
    //      EVENT_SYSTEM_DRAGDROPSTART (0x000E). Modern apps including Explorer
    //      fire these as soon as a drag begins, before the visual feedback.
    //   2. WH_MOUSE_LL low-level mouse hook as a fallback for apps that don't
    //      raise either accessibility event. Tracks LBUTTONDOWN -> drag distance
    //      from a shell-file window and cancels the drag.
    //
    // Both paths only act when the drag SOURCE belongs to a shell-file window
    // (Explorer, Desktop, Common File Dialog, file-list controls). That way
    // legitimate drags — text selection, window resize, scrollbars — are
    // untouched.
    //
    // Cancellation: SendInput(VK_ESCAPE) + WM_CANCELMODE to the source's root.
    // ESC is what cancels drag-drop in the standard OLE pipeline; WM_CANCELMODE
    // is the explicit "abort modal interaction" message and helps for the few
    // apps that don't honor ESC.
    public sealed class MouseGuard
    {
        // ---- low-level mouse hook ----
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_MOUSEMOVE = 0x0200;

        // ---- WinEvent hook ----
        private const uint EVENT_SYSTEM_DRAGDROPSTART = 0x000E;
        private const uint EVENT_OBJECT_DRAGSTART = 0x8021;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        // ---- system metrics / window walking ----
        private const int SM_CXDRAG = 68;
        private const int SM_CYDRAG = 69;
        private const uint GA_ROOT = 2;

        // ---- input injection ----
        private const ushort VK_ESCAPE = 0x1B;
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint WM_CANCELMODE = 0x001F;

        // Window classes we treat as "shell file source" — drags FROM these are blocked.
        private static readonly string[] ShellFileClasses = new[]
        {
            "CabinetWClass",        // Explorer window root
            "ExploreWClass",        // legacy Explorer
            "Progman",              // Desktop
            "WorkerW",              // Desktop wallpaper layer
            "#32770",               // Common dialog (file open / save)
            "SHELLDLL_DefView",     // file list view inside Explorer
            "DirectUIHWND",         // DirectUI inside Explorer
            "SysListView32",        // listview inside file open/save dialogs
            "SysTreeView32"         // tree inside file open/save dialogs
        };

        // ---- mouse-hook delegates / state ----
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelMouseProc _mouseProc;
        private IntPtr _mouseHook = IntPtr.Zero;

        private bool _down;
        private int _downX, _downY;
        private IntPtr _downRootHwnd = IntPtr.Zero;
        private bool _downFromShell;
        private bool _escSent;

        // ---- WinEvent delegate / state ----
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);
        private WinEventDelegate _winEventProc;
        private IntPtr _winEventHookA = IntPtr.Zero;
        private IntPtr _winEventHookB = IntPtr.Zero;
        private long _lastCancelTickMs;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT
        {
            [FieldOffset(0)] public uint type;
            [FieldOffset(8)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
            WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        public void Start()
        {
            if (_mouseHook == IntPtr.Zero)
            {
                _mouseProc = MouseHookCallback;
                using (Process p = Process.GetCurrentProcess())
                using (ProcessModule m = p.MainModule)
                {
                    _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(m.ModuleName), 0);
                }
                if (_mouseHook == IntPtr.Zero)
                {
                    AuditLogger.Instance.Error("DRAGDROP", "SetWindowsHookEx mouse failed err=" + Marshal.GetLastWin32Error());
                }
            }

            if (_winEventHookA == IntPtr.Zero)
            {
                _winEventProc = OnWinEvent;
                _winEventHookA = SetWinEventHook(EVENT_SYSTEM_DRAGDROPSTART, EVENT_SYSTEM_DRAGDROPSTART,
                    IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
                _winEventHookB = SetWinEventHook(EVENT_OBJECT_DRAGSTART, EVENT_OBJECT_DRAGSTART,
                    IntPtr.Zero, _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            }

            AuditLogger.Instance.Info("DRAGDROP", "Drag-drop guard installed (mouse hook + WinEvent)");
        }

        public void Stop()
        {
            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            _mouseProc = null;

            if (_winEventHookA != IntPtr.Zero) { UnhookWinEvent(_winEventHookA); _winEventHookA = IntPtr.Zero; }
            if (_winEventHookB != IntPtr.Zero) { UnhookWinEvent(_winEventHookB); _winEventHookB = IntPtr.Zero; }
            _winEventProc = null;

            AuditLogger.Instance.Info("DRAGDROP", "Drag-drop guard removed");
        }

        // ---- WinEvent path: fires when Explorer / OLE source begins a drag ----
        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return;
                if (IsOwnProcessWindow(hwnd)) return;

                IntPtr root = GetAncestor(hwnd, GA_ROOT);
                string srcClass = GetClassNameSafe(hwnd);
                string rootClass = GetClassNameSafe(root);

                if (!IsShellFileWindow(srcClass) && !IsShellFileWindow(rootClass)) return;

                CancelDrag(root, "winEvent",
                    "evt=0x" + eventType.ToString("x") + " src=" + srcClass + " root=" + rootClass);
            }
            catch (Exception ex)
            {
                AuditLogger.Instance.Error("DRAGDROP", "winEvent: " + ex.Message);
            }
        }

        // ---- Mouse-hook path: fallback for apps that don't fire drag events ----
        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0)
                {
                    int msg = wParam.ToInt32();
                    var data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));

                    if (msg == WM_LBUTTONDOWN)
                    {
                        _down = true;
                        _escSent = false;
                        _downX = data.pt.x;
                        _downY = data.pt.y;
                        IntPtr under = WindowFromPoint(data.pt);
                        _downRootHwnd = under == IntPtr.Zero ? IntPtr.Zero : GetAncestor(under, GA_ROOT);

                        string underClass = GetClassNameSafe(under);
                        string rootClass = GetClassNameSafe(_downRootHwnd);
                        _downFromShell = !IsOwnProcessWindow(_downRootHwnd) &&
                                         (IsShellFileWindow(underClass) || IsShellFileWindow(rootClass));
                    }
                    else if (msg == WM_LBUTTONUP)
                    {
                        _down = false;
                        _escSent = false;
                        _downRootHwnd = IntPtr.Zero;
                        _downFromShell = false;
                    }
                    else if (msg == WM_MOUSEMOVE && _down && _downFromShell)
                    {
                        int dx = Math.Abs(data.pt.x - _downX);
                        int dy = Math.Abs(data.pt.y - _downY);
                        if (dx > GetSystemMetrics(SM_CXDRAG) || dy > GetSystemMetrics(SM_CYDRAG))
                        {
                            if (!_escSent)
                            {
                                CancelDrag(_downRootHwnd, "mouseMove",
                                    "dx=" + dx + " dy=" + dy);
                                _escSent = true;
                            }
                            // Swallow motion for the rest of this gesture: returning 1 stops
                            // the event from reaching the input queue, so OLE drag-drop in
                            // Explorer never sees enough movement to enter DoDragDrop.
                            // Resets on WM_LBUTTONUP.
                            return (IntPtr)1;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AuditLogger.Instance.Error("DRAGDROP", "mouseHook: " + ex.Message);
            }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        // ---- cancellation primitive ----
        private void CancelDrag(IntPtr sourceRoot, string trigger, string ctx)
        {
            // Throttle: Windows can fire EVENT_OBJECT_DRAGSTART repeatedly for the
            // same drag operation. One injection per ~250 ms is plenty.
            long now = Environment.TickCount;
            if (now - _lastCancelTickMs < 250) return;
            _lastCancelTickMs = now;

            try { InjectEscape(); } catch { }
            try
            {
                if (sourceRoot != IntPtr.Zero)
                {
                    PostMessage(sourceRoot, WM_CANCELMODE, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch { }

            AuditLogger.Instance.Blocked("DRAGDROP", "drag cancelled (" + trigger + ")", ctx);
        }

        private static void InjectEscape()
        {
            var inputs = new INPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].ki.wVk = VK_ESCAPE;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].ki.wVk = VK_ESCAPE;
            inputs[1].ki.dwFlags = KEYEVENTF_KEYUP;
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private static bool IsOwnProcessWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            try
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                return pid == (uint)Process.GetCurrentProcess().Id;
            }
            catch { return false; }
        }

        private static string GetClassNameSafe(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return string.Empty;
            try
            {
                var sb = new StringBuilder(128);
                int n = GetClassName(hwnd, sb, sb.Capacity);
                return n > 0 ? sb.ToString() : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static bool IsShellFileWindow(string className)
        {
            if (string.IsNullOrEmpty(className)) return false;
            for (int i = 0; i < ShellFileClasses.Length; i++)
            {
                if (string.Equals(ShellFileClasses[i], className, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
