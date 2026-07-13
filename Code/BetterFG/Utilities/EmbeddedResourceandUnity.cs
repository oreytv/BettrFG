using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BetterFG.Utilities
{
    internal class EmbeddedResourceandUnity
    {
        static Assembly asm = Assembly.GetExecutingAssembly();

        public static Texture2D LoadTexture(string resourcePath)
        {
            using (Stream stream = asm.GetManifestResourceStream(resourcePath))
            {
                if (stream == null)
                {
                    Plugin.Log.LogWarning($"EmbeddedRes: no resource at '{resourcePath}'");
                    return null;
                }

                byte[] data = new byte[stream.Length];
                stream.Read(data, 0, data.Length);

                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(data);
                tex.filterMode = FilterMode.Bilinear;
                tex.Apply();
                // keep it alive across scene unloads so cached references don't go dead between rounds
                tex.hideFlags = HideFlags.HideAndDontSave;
                return tex;
            }
        }

        public static Sprite LoadSprite(string resourcePath, float pixelsPerUnit = 100f)
        {
            Texture2D tex = LoadTexture(resourcePath);
            if (tex == null) return null;

            var sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                pixelsPerUnit
            );
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        public static Sprite LoadSprite(string resourcePath, Rect rect, Vector2 pivot, float pixelsPerUnit = 100f)
        {
            Texture2D tex = LoadTexture(resourcePath);
            if (tex == null) return null;

            var sprite = Sprite.Create(tex, rect, pivot, pixelsPerUnit);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}