using System;
using System.Runtime.InteropServices;
using System.Text;
#if PROFILES
using BetterFG.Customization.Profiles;
using BetterFG.UI;
using BetterFG.UI.Windows;
#endif

namespace BetterFG.Utilities
{
    public static class Shell32Util
    {
        private const int GWLP_WNDPROC = -4;
        private const int GCLP_HICON = -14;
        private const int IDI_APPLICATION = 32512;
        private const int SW_RESTORE = 9;
        private const uint WM_DROPFILES = 0x0233;

        private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
        private const uint NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04, NIF_INFO = 0x10;
        private const uint NOTIFYICON_VERSION_4 = 4;
        private const uint NIN_BALLOONHIDE = 0x0403, NIN_BALLOONTIMEOUT = 0x0404, NIN_BALLOONUSERCLICK = 0x0405;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // W entry points on purpose, the A variants would flip the game window to ANSI and mangle unicode input
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProcDelegate dwNewLong);

        [DllImport("user32.dll", EntryPoint = "CallWindowProcW")]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
        private static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", EntryPoint = "LoadIconW")]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint dwThreadId, EnumWindowsProc lpfn, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

        [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("shell32.dll")] private static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);
        [DllImport("shell32.dll", CharSet = CharSet.Auto)] private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder lpszFile, uint cch);
        [DllImport("shell32.dll")] private static extern void DragFinish(IntPtr hDrop);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
            public uint uVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        private static IntPtr _hwnd;
        private static IntPtr _icon;
        private static IntPtr _origWndProc;
        private static WndProcDelegate _hook;
        private static uint _callbackMsg;
        private static bool _installed;
        private static bool _failed;
        private static bool _iconAdded;
#if PROFILES
        private static bool _dragReady;
#endif

        /// <summary>Turns on .bfgprofile drag-and-drop onto the game window. One-shot, self-guarding.</summary>
        public static void Init()
        {
#if PROFILES
            if (_dragReady || !Install()) return;
            DragAcceptFiles(_hwnd, true);
            _dragReady = true;
            Plugin.Log.LogInfo("drag-drop hook installed");
#endif
        }

        /// <summary>Windows-only toast. Clicking it brings the game window back to the front.</summary>
        public static void Toast(string title, string message)
        {
            if (!Install()) return;

            var d = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1,
            };

            if (!_iconAdded)
            {
                d.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
                d.uCallbackMessage = _callbackMsg;
                d.hIcon = _icon;
                d.szTip = "Fall Guys";
                if (!Shell_NotifyIcon(NIM_ADD, ref d))
                {
                    Plugin.Log.LogWarning($"shell wouldn't take the tray icon, dropping toast: {message}");
                    return;
                }
                _iconAdded = true;

                // version 4 or the shell never tells us the balloon got clicked
                d.uVersion = NOTIFYICON_VERSION_4;
                Shell_NotifyIcon(NIM_SETVERSION, ref d);
                d.uVersion = 0;
            }

            // dwInfoFlags stays NIIF_NONE, NIIF_USER wants a balloon icon of a size we don't have and
            // fails the whole call with "incorrect size argument". win10+ shows szTip's icon anyway
            d.uFlags = NIF_INFO;
            d.szInfoTitle = title;
            d.szInfo = message;
            if (!Shell_NotifyIcon(NIM_MODIFY, ref d))
                Plugin.Log.LogWarning($"shell turned down the balloon (err {Marshal.GetLastWin32Error()}), no toast for: {message}");
        }

        private static bool Install()
        {
            if (_installed) return true;
            if (_failed) return false;

            try
            {
                // we toast precisely when the game isn't active, and an inactive thread has no active
                // window, so GetActiveWindow is no use here
                EnumThreadWindows(GetCurrentThreadId(), PickWindow, IntPtr.Zero);
                if (_hwnd == IntPtr.Zero)
                {
                    Plugin.Log.LogWarning("no visible window on the main thread yet, nothing to hook");
                    return false;
                }

                _icon = GetClassLongPtr(_hwnd, GCLP_HICON);
                if (_icon == IntPtr.Zero) _icon = LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
                _callbackMsg = RegisterWindowMessage("BettrFGToast");
                _hook = WndProc;
                _origWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _hook);
                _installed = true;
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("couldn't hook the game window, no toasts or drag-drop: " + ex.Message);
                _failed = true;
                return false;
            }
        }

        private static bool PickWindow(IntPtr hWnd, IntPtr lParam)
        {
            if (!IsWindowVisible(hWnd)) return true;
            _hwnd = hWnd;
            return false;
        }

        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == _callbackMsg)
            {
                uint ev = (uint)(lParam.ToInt64() & 0xFFFF);
                if (ev == NIN_BALLOONUSERCLICK)
                {
                    if (IsIconic(_hwnd)) ShowWindow(_hwnd, SW_RESTORE);
                    SetForegroundWindow(_hwnd);
                    RemoveIcon();
                }
                else if (ev == NIN_BALLOONTIMEOUT || ev == NIN_BALLOONHIDE) RemoveIcon();
            }
#if PROFILES
            else if (msg == WM_DROPFILES)
            {
                try { HandleDrop(wParam); }
                catch (Exception ex) { Plugin.Log.LogError("drop handling blew up: " + ex.Message); }
            }
#endif
            return CallWindowProc(_origWndProc, hWnd, msg, wParam, lParam);
        }

        private static void RemoveIcon()
        {
            if (!_iconAdded) return;
            var d = new NOTIFYICONDATA { cbSize = Marshal.SizeOf<NOTIFYICONDATA>(), hWnd = _hwnd, uID = 1 };
            Shell_NotifyIcon(NIM_DELETE, ref d);
            _iconAdded = false;
        }

#if PROFILES
        private static void HandleDrop(IntPtr hDrop)
        {
            uint count = DragQueryFile(hDrop, 0xFFFFFFFF, null, 0);
            for (uint i = 0; i < count; i++)
            {
                var sb = new StringBuilder(512);
                DragQueryFile(hDrop, i, sb, (uint)sb.Capacity);
                string path = sb.ToString();
                if (string.IsNullOrEmpty(path)) continue;
                if (!path.EndsWith(".bfgprofile", StringComparison.OrdinalIgnoreCase)) continue;

                // import on the Unity main thread, never touch managed game state from the wndproc
                string captured = path;
                WinDialogs.Enqueue(() =>
                {
                    string name = ProfileService.ImportOverwriteByPlayerName(captured);
                    Plugin.Log.LogInfo($"imported profile '{name}' from drop");
                    ProfilesWindow.RefreshOpen();
                });
            }
            DragFinish(hDrop);
        }
#endif
    }
}
