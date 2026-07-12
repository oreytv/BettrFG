using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BetterFG.Services;
using BetterFG.UI;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class CustomCursorTweak : BfgTweak
    {
        public CustomCursorTweak(System.IntPtr ptr) : base(ptr) { }

        public override string TweakId => "custom_cursor";
        public override string TweakLabel => "Custom Cursor";
        public override bool DefaultEnabled => true;

        private const string PathKey = "tweak.custom_cursor.path";

        private Texture2D _cursorTex;

        public override List<TweakButton> GetCustomButtons() => new List<TweakButton>
        {
            new TweakButton { Label = "Browse", Width = 46f, OnClick = PickAndReload }
        };

        private void PickAndReload()
        {
            WinDialogs.PickFile("Select Cursor Image", path =>
            {
                if (path == null) return;
                // only real picture files stick, anything else falls back to the embedded cursor.png
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".webp")
                    SettingsService.Set(PathKey, path);
                else
                    SettingsService.Set(PathKey, "");
                _cursorTex = null;
                if (IsEnabled) EnableTweak();
            });
        }

        public override void EnableTweak()
        {
            if (_cursorTex == null)
            {
                var bytes = LoadBytes();
                if (bytes == null) return;
                _cursorTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _cursorTex.hideFlags = HideFlags.HideAndDontSave;
                if (!ImageConversion.LoadImage(_cursorTex, bytes))
                {
                    // couldn't decode the picked file, fall back to embedded cursor.png
                    var fallback = GetEmbeddedBytes();
                    if (fallback == null) return;
                    ImageConversion.LoadImage(_cursorTex, fallback);
                }
                _cursorTex.Apply(false, false);
            }
            Cursor.SetCursor(_cursorTex, Vector2.zero, CursorMode.Auto);
        }

        public override void DisableTweak()
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        private byte[] LoadBytes()
        {
            var saved = SettingsService.Get(PathKey, "");
            if (!string.IsNullOrEmpty(saved) && File.Exists(saved))
                return File.ReadAllBytes(saved);
            return GetEmbeddedBytes();
        }

        private static byte[] GetEmbeddedBytes()
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("BetterFG.assets.ui.cursor.png");
            if (stream == null)
            {
                Plugin.Log.LogWarning("CustomCursorTweak: embedded cursor not found");
                return null;
            }
            var bytes = new byte[stream.Length];
            stream.Read(bytes, 0, bytes.Length);
            return bytes;
        }
    }
}
