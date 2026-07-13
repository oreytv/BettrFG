using System;
using System.Runtime.InteropServices;
using System.Text;
#if PROFILES
using BetterFG.Customization.Profiles;
#endif
using BetterFG.UI;
using BetterFG.UI.Windows;
using UnityEngine;

namespace BetterFG.Services
{
    // Native drag-and-drop of .bfgprofile files onto the game window. We subclass the game's HWND,
    // turn on DragAcceptFiles, and on WM_DROPFILES pull out any dropped .bfgprofile and import it
    // (overwriting any existing profile that shares the same player name). The actual import is
    // bounced to the Unity main thread via WinDialogs' queue so we never touch managed state from
    // the window-proc thread.
    public static class FileDropService
    {
        private const uint WM_DROPFILES = 0x0233;
        private const int GWLP_WNDPROC = -4;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProcDelegate dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("shell32.dll")]
        private static extern void DragAcceptFiles(IntPtr hWnd, bool fAccept);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder lpszFile, uint cch);

        [DllImport("shell32.dll")]
        private static extern void DragFinish(IntPtr hDrop);
            
        [DllImport("user32.dll")]
        private static extern IntPtr GetActiveWindow();

        private static IntPtr _origWndProc;
        private static WndProcDelegate _hook; // kept alive so the GC doesn't collect the delegate
        private static IntPtr _hwnd;
        private static bool _installed;

        public static void Init()
        {
            // .bfgprofile drag-drop only feeds the profiles feature — when it's off, don't bother
            // subclassing the game window. the whole hook body is compiled out.
#if PROFILES
            if (_installed) return;

            _hwnd = GetActiveWindow();
            if (_hwnd == IntPtr.Zero)
            {
                Plugin.Log.LogWarning("FileDrop: no active window yet, skipping");
                return;
            }

            try
            {
                _hook = WndProc;
                _origWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _hook);
                DragAcceptFiles(_hwnd, true);
                _installed = true;
                Plugin.Log.LogInfo("FileDrop: drag-drop hook installed");
            }
            catch (Exception ex) { Plugin.Log.LogError("drag-drop install failed: " + ex.Message); }
#endif
        }

        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_DROPFILES)
            {
                try { HandleDrop(wParam); }
                catch (Exception ex) { Plugin.Log.LogError("FileDrop: handle: " + ex.Message); }
            }
            return CallWindowProc(_origWndProc, hWnd, msg, wParam, lParam);
        }

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

                // import on the Unity main thread — never touch managed game state from the wndproc
                string captured = path;
                WinDialogs.Enqueue(() =>
                {
#if PROFILES
                    string name = ProfileService.ImportOverwriteByPlayerName(captured);
                    Plugin.Log.LogInfo($"FileDrop: imported profile '{name}' from drop");
                    ProfilesWindow.RefreshOpen();
#endif
                });
            }
            DragFinish(hDrop);
        }
    }
}
