using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BetterFG.Nametag
{
    internal static class FlagAssets
    {
        private const string prefix = "BetterFG.assets.flags.";
        private static string[] _codes;

        public static string[] GetAvailableCodes()
        {
            if (_codes != null) return _codes;

            var asm = Assembly.GetExecutingAssembly();
            var names = asm.GetManifestResourceNames();
            var result = new List<string>();

            foreach (string name in names)
            {
                if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (!name.EndsWith(".png", StringComparison.Ordinal)) continue;
                // BetterFG.assets.flags.US.png -> "US"
                string code = name.Substring(prefix.Length, name.Length - prefix.Length - 4);
                if (code.Length == 2) result.Add(code);
            }

            result.Sort(StringComparer.Ordinal);
            _codes = result.ToArray();
            return _codes;
        }

        public static Sprite LoadFlag(string code)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using Stream stream = asm.GetManifestResourceStream(prefix + code.ToUpper() + ".png");
                if (stream == null) return null;
                byte[] data = new byte[stream.Length];
                stream.Read(data, 0, data.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(data);
                tex.Apply();
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            catch { return null; }
        }
    }
}