using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace BetterFG.UI
{
    /// <summary>
    /// Win32 file/folder dialogs. All calls are dispatched on a background STA thread
    /// so the game loop never freezes while the picker is open.
    /// Callbacks fire on the calling thread (just call from Unity main thread and handle in callback).
    /// </summary>
    public static class WinDialogs
    {
        // ── GetOpenFileName (file picker) ──────────────────────────────────────

        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetOpenFileName(ref OPENFILENAME ofn);

        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool GetSaveFileName(ref OPENFILENAME ofn);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct OPENFILENAME
        {
            public int lStructSize; public IntPtr hwndOwner, hInstance;
            public string lpstrFilter, lpstrCustomFilter;
            public int nMaxCustFilter, nFilterIndex;
            public string lpstrFile; public int nMaxFile;
            public string lpstrFileTitle; public int nMaxFileTitle;
            public string lpstrInitialDir, lpstrTitle;
            public int Flags; public short nFileOffset, nFileExtension;
            public string lpstrDefExt; public IntPtr lCustData, lpfnHook, lpTemplateName;
            public IntPtr pvReserved; public int dwReserved, FlagsEx;
        }

        // ── IFileDialog COM (folder picker) ───────────────────────────────────

        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder([MarshalAs(UnmanagedType.Interface)] object psi);
            void SetFolder([MarshalAs(UnmanagedType.Interface)] object psi);
            void GetFolder([MarshalAs(UnmanagedType.Interface)] out object ppsi);
            void GetCurrentSelection([MarshalAs(UnmanagedType.Interface)] out object ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult([MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);
            void AddPlace([MarshalAs(UnmanagedType.Interface)] object psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter([MarshalAs(UnmanagedType.Interface)] object pFilter);
            void GetResults([MarshalAs(UnmanagedType.Interface)] out object ppenum);
            void GetSelectedItems([MarshalAs(UnmanagedType.Interface)] out object ppsai);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(ref Guid rclsid, IntPtr pUnkOuter,
            uint dwClsContext, ref Guid riid, out IFileOpenDialog ppv);

        private static readonly Guid CLSID_FileOpenDialog = new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7");
        private static readonly Guid IID_IFileOpenDialog = new Guid("D57C7288-D4AD-4768-BE02-9D969532D960");

        // ── Main-thread dispatch ──────────────────────────────────────────────

        private static readonly System.Collections.Concurrent.ConcurrentQueue<Action> _mainQueue
            = new System.Collections.Concurrent.ConcurrentQueue<Action>();

        public static void Tick()
        {
            while (_mainQueue.TryDequeue(out var a)) a();
        }

        /// <summary>Run an action on the Unity main thread (next Tick). Used by off-thread callers.</summary>
        public static void Enqueue(Action action)
        {
            if (action != null) _mainQueue.Enqueue(action);
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Opens a PNG file picker off the main thread. callback(path) on result, path is null on cancel.</summary>
        public static void PickPng(string title, Action<string> callback)
            => RunOnSta(() =>
            {
                string result = null;
                try
                {
                    var buf = new string('\0', 512);
                    var ofn = new OPENFILENAME
                    {
                        lStructSize = Marshal.SizeOf(typeof(OPENFILENAME)),
                        lpstrFilter = "PNG Images\0*.png\0All Files\0*.*\0",
                        nFilterIndex = 1,
                        lpstrFile = buf,
                        nMaxFile = buf.Length,
                        lpstrTitle = title,
                        lpstrDefExt = "png",
                        Flags = 0x00080000 | 0x00001000 | 0x00000800,
                    };
                    if (GetOpenFileName(ref ofn))
                        result = ofn.lpstrFile.Split('\0')[0];
                }
                catch (Exception ex) { Plugin.Log.LogError("WinDialogs: PickPng: " + ex.Message); }
                _mainQueue.Enqueue(() => callback(result));
            });

        /// <summary>Opens an image file picker (PNG + animated GIF). callback(path) on result, path is null on cancel.</summary>
        public static void PickImage(string title, Action<string> callback)
            => RunOnSta(() =>
            {
                string result = null;
                try
                {
                    var buf = new string('\0', 512);
                    var ofn = new OPENFILENAME
                    {
                        lStructSize = Marshal.SizeOf(typeof(OPENFILENAME)),
                        lpstrFilter = "Images\0*.png;*.gif\0PNG Images\0*.png\0GIF Images\0*.gif\0All Files\0*.*\0",
                        nFilterIndex = 1,
                        lpstrFile = buf,
                        nMaxFile = buf.Length,
                        lpstrTitle = title,
                        lpstrDefExt = "png",
                        Flags = 0x00080000 | 0x00001000 | 0x00000800,
                    };
                    if (GetOpenFileName(ref ofn))
                        result = ofn.lpstrFile.Split('\0')[0];
                }
                catch (Exception ex) { Plugin.Log.LogError("WinDialogs: PickImage: " + ex.Message); }
                _mainQueue.Enqueue(() => callback(result));
            });

        /// <summary>Opens an audio file picker (mp3/wav/ogg). callback(path) on result, path is null on cancel.</summary>
        public static void PickAudio(string title, Action<string> callback)
            => RunOnSta(() =>
            {
                string result = null;
                try
                {
                    var buf = new string('\0', 512);
                    var ofn = new OPENFILENAME
                    {
                        lStructSize = Marshal.SizeOf(typeof(OPENFILENAME)),
                        lpstrFilter = "Audio Files\0*.mp3;*.wav;*.ogg\0All Files\0*.*\0",
                        nFilterIndex = 1,
                        lpstrFile = buf,
                        nMaxFile = buf.Length,
                        lpstrTitle = title,
                        Flags = 0x00080000 | 0x00001000 | 0x00000800,
                    };
                    if (GetOpenFileName(ref ofn))
                        result = ofn.lpstrFile.Split('\0')[0];
                }
                catch (Exception ex) { Plugin.Log.LogError("WinDialogs: PickAudio: " + ex.Message); }
                _mainQueue.Enqueue(() => callback(result));
            });

        /// <summary>Opens a generic file picker and returns the picked file path. callback(path) on result, path is null on cancel.</summary>
        public static void PickFile(string title, Action<string> callback)
            => RunOnSta(() =>
            {
                string result = null;
                try
                {
                    var buf = new string('\0', 512);
                    var ofn = new OPENFILENAME
                    {
                        lStructSize = Marshal.SizeOf(typeof(OPENFILENAME)),
                        lpstrFilter = "All Files\0*.*\0",
                        nFilterIndex = 1,
                        lpstrFile = buf,
                        nMaxFile = buf.Length,
                        lpstrTitle = title,
                        Flags = 0x00080000 | 0x00001000 | 0x00000800,
                    };
                    if (GetOpenFileName(ref ofn))
                        result = ofn.lpstrFile.Split('\0')[0];
                }
                catch (Exception ex) { Plugin.Log.LogError("WinDialogs: PickFile: " + ex.Message); }
                _mainQueue.Enqueue(() => callback(result));
            });

        /// <summary>Opens a save-as dialog. callback(path) with the chosen path (ext appended), null on cancel.</summary>
        public static void SaveFile(string title, string defExt, string defName, Action<string> callback)
            => RunOnSta(() =>
            {
                string result = null;
                try
                {
                    var buf = new string('\0', 512);
                    if (!string.IsNullOrEmpty(defName)) buf = defName + new string('\0', 512 - defName.Length);
                    var ofn = new OPENFILENAME
                    {
                        lStructSize = Marshal.SizeOf(typeof(OPENFILENAME)),
                        lpstrFilter = $"BettrFG Profile\0*.{defExt}\0All Files\0*.*\0",
                        nFilterIndex = 1,
                        lpstrFile = buf,
                        nMaxFile = buf.Length,
                        lpstrTitle = title,
                        lpstrDefExt = defExt,
                        Flags = 0x00080000 | 0x00000800 | 0x00000002, // EXPLORER | PATHMUSTEXIST | OVERWRITEPROMPT
                    };
                    if (GetSaveFileName(ref ofn))
                        result = ofn.lpstrFile.Split('\0')[0];
                }
                catch (Exception ex) { Plugin.Log.LogError("WinDialogs: SaveFile: " + ex.Message); }
                _mainQueue.Enqueue(() => callback(result));
            });

        /// <summary>Opens a file picker and returns its containing folder. callback(path) on result, path is null on cancel.</summary>
        public static void PickFolder(string title, Action<string> callback)
            => RunOnSta(() =>
            {
                string result = null;
                try
                {
                    var buf = new string('\0', 512);
                    var ofn = new OPENFILENAME
                    {
                        lStructSize = Marshal.SizeOf(typeof(OPENFILENAME)),
                        lpstrFilter = "All Files\0*.*\0",
                        nFilterIndex = 1,
                        lpstrFile = buf,
                        nMaxFile = buf.Length,
                        lpstrTitle = title + " (select any file inside the folder)",
                        Flags = 0x00080000 | 0x00001000 | 0x00000800,
                    };
                    if (GetOpenFileName(ref ofn))
                    {
                        string picked = ofn.lpstrFile.Split('\0')[0];
                        result = System.IO.Path.GetDirectoryName(picked);
                    }
                }
                catch (Exception ex) { Plugin.Log.LogError("WinDialogs: PickFolder: " + ex.Message); }
                _mainQueue.Enqueue(() => callback(result));
            });

        // ── STA thread runner ─────────────────────────────────────────────────

        private static void RunOnSta(Action action)
        {
            var t = new Thread(() =>
            {
                try { action(); }
                catch (Exception ex) { Plugin.Log.LogError("WinDialogs: Thread error: " + ex.Message); }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }
    }
}
