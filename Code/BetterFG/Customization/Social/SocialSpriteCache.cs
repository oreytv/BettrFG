using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BetterFG.Customization.Social
{
    // the wheel re-applies on every open, and decoding a PNG into a Texture2D + Sprite each time is
    // what made the wheel hitch. cache the EXPENSIVE bits (the decoded Texture2D + Sprite) keyed by
    // path, rebuilt only when the file on disk changes (last-write-time).
    //
    // we do NOT cache the CacheableAtlasSprite wrapper across calls: it builds an internal atlas that
    // UnloadUnusedAssets reclaims on a scene/round change, so a wrapper made last round renders blank
    // on the new round's first wheel open. it's cheap to rebuild from the cached sprite, so we make a
    // fresh one every call — that's the wrapper the game's first render actually reads.
    internal static class SocialSpriteCache
    {
        private struct Entry { public long ticks; public Sprite sprite; }
        private static readonly Dictionary<string, Entry> _cache = new Dictionary<string, Entry>();

        // returns false if the path is empty/missing or decode failed. on success fills sprite+cached.
        internal static bool TryGet(string path, out Sprite sprite, out ItemDefinitionSO.CacheableAtlasSprite cached)
        {
            sprite = null; cached = null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;

            long ticks = File.GetLastWriteTimeUtc(path).Ticks;
            if (_cache.TryGetValue(path, out var hit) && hit.ticks == ticks && hit.sprite != null)
            {
                sprite = hit.sprite;
            }
            else
            {
                try
                {
                    byte[] imgData = File.ReadAllBytes(path);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.LoadImage(imgData);
                    tex.Apply();
                    // keep these alive across scene loads / UnloadUnusedAssets — nothing else roots them
                    // as real assets, so without this the wheel icon goes blank after a round change.
                    tex.hideFlags = HideFlags.HideAndDontSave;
                    sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                    sprite.hideFlags = HideFlags.HideAndDontSave;
                    _cache[path] = new Entry { ticks = ticks, sprite = sprite };
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SocialSpriteCache] failed to load {path}: {ex.Message}");
                    return false;
                }
            }

            // fresh wrapper every call — see class note
            cached = new ItemDefinitionSO.CacheableAtlasSprite();
            cached.Cache(sprite, true);
            return true;
        }
    }
}
