using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterFG.Features.UnityRound.Editor
{
    // Custom textures for the placeable obstacles we keep around a custom round (they can look off
    // against a custom map). Scans scene 0's placeables for the unique textures in use, lets you
    // swap any of them for a PNG, and persists the mapping to texture.json next to info.json.
    //
    // Keyed by texture name: one row per unique texture, and applying swaps it on EVERY renderer in
    // scene 0 that uses it. The unity packager never writes this file.

    public static class ObstacleTextureLoader
    {
        // texture property names we look at on a material (same set the skin texture system uses)
        private static readonly string[] TexProps = { "_MainTex", "_BaseMap", "_BaseTexture", "_MainTex2" };

        // texName -> local png path
        private static readonly Dictionary<string, string> _overrides = new Dictionary<string, string>();
        // texName -> original texture, so we can revert
        private static readonly Dictionary<string, Texture> _originals = new Dictionary<string, Texture>();

        private static string _jsonPath;

        public static IReadOnlyDictionary<string, string> Overrides => _overrides;

        // ── discovery ───────────────────────────────────────────────────────────

        // every unique main-texture name found on placeables in scene 0
        public static List<string> DiscoverTextureNames()
        {
            var found = new List<string>();
            var seen = new HashSet<string>();

            for (int si = 0; si < SceneManager.sceneCount; si++)
            {
                var s = SceneManager.GetSceneAt(si);
                if (!s.isLoaded) continue;

                foreach (var root in s.GetRootGameObjects())
                {
                    string n = root.name;
                    if (!(n.StartsWith("Placeable_") || n.StartsWith("PB_"))) continue;

                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null) continue;
                        var mats = r.sharedMaterials;
                        if (mats == null) continue;
                        foreach (var m in mats)
                        {
                            string texName = MainTexName(m);
                            if (string.IsNullOrEmpty(texName)) continue;
                            if (seen.Add(texName)) found.Add(texName);
                        }
                    }
                }
            }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        private static string MainTexName(Material m)
        {
            if (m == null) return null;
            foreach (var prop in TexProps)
            {
                try
                {
                    if (!m.HasProperty(prop)) continue;
                    var t = m.GetTexture(prop);
                    if (t != null && !string.IsNullOrEmpty(t.name)) return t.name;
                }
                catch { }
            }
            return null;
        }

        // ── apply / revert ──────────────────────────────────────────────────────

        // create/replace a row: load the png and swap it onto every placeable material using texName
        public static bool SetOverride(string texName, string pngPath, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(texName)) { error = "no texture"; return false; }
            if (string.IsNullOrEmpty(pngPath) || !File.Exists(pngPath)) { error = "png not found"; return false; }

            Texture2D tex;
            try
            {
                byte[] data = File.ReadAllBytes(pngPath);
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(data);
                tex.Apply();
                tex.name = texName; // keep the name so re-scans still recognise it
            }
            catch (Exception ex) { error = "png load failed: " + ex.Message; return false; }

            int touched = ApplyTextureToScene(texName, tex);
            if (touched == 0) { error = "no placeable material used that texture"; return false; }

            _overrides[texName] = pngPath;
            Save();
            return true;
        }

        public static bool SetOverrideBytes(string texName, byte[] data, string label, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(texName)) { error = "no texture"; return false; }
            if (data == null || data.Length == 0) { error = "png empty"; return false; }

            Texture2D tex;
            try
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(data);
                tex.Apply();
                tex.name = texName;
            }
            catch (Exception ex) { error = "png load failed: " + ex.Message; return false; }

            int touched = ApplyTextureToScene(texName, tex);
            if (touched == 0) { error = "no placeable material used that texture"; return false; }

            _overrides[texName] = label ?? "";
            return true;
        }

        public static void RemoveOverride(string texName)
        {
            if (string.IsNullOrEmpty(texName)) return;
            RevertOne(texName);
            _overrides.Remove(texName);
            Save();
        }

        public static bool SaveTexturePng(string texName, out string pngPath, out string error)
        {
            pngPath = null;
            error = null;

            if (string.IsNullOrEmpty(texName)) { error = "no texture"; return false; }
            if (string.IsNullOrEmpty(_jsonPath)) { error = "load a round first"; return false; }

            Texture src = FindTextureInScene(texName);
            if (src == null) { error = "texture not found"; return false; }

            string folder = Path.GetDirectoryName(_jsonPath);
            string texFolder = Path.Combine(folder, "textures");
            string file = CleanFileName(texName) + ".png";
            pngPath = Path.Combine(texFolder, file);

            Texture2D readable = null;
            RenderTexture oldRt = RenderTexture.active;
            RenderTexture rt = null;
            try
            {
                Directory.CreateDirectory(texFolder);

                int w = Math.Max(1, src.width);
                int h = Math.Max(1, src.height);
                rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;

                readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
                readable.Apply();

                File.WriteAllBytes(pngPath, readable.EncodeToPNG());
                return true;
            }
            catch (Exception ex) { error = "png save failed: " + ex.Message; return false; }
            finally
            {
                RenderTexture.active = oldRt;
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                if (readable != null) UnityEngine.Object.Destroy(readable);
            }
        }

        // the live texture currently on the placeables for this name (the override if one is
        // applied, otherwise the original). used by the window to show a thumbnail per row.
        public static Texture GetCurrentTexture(string texName) => FindTextureInScene(texName);

        private static Texture FindTextureInScene(string texName)
        {
            for (int si = 0; si < SceneManager.sceneCount; si++)
            {
                var s = SceneManager.GetSceneAt(si);
                if (!s.isLoaded) continue;

                foreach (var root in s.GetRootGameObjects())
                {
                    string n = root.name;
                    if (!(n.StartsWith("Placeable_") || n.StartsWith("PB_"))) continue;

                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null) continue;
                        var mats = r.sharedMaterials;
                        if (mats == null) continue;
                        foreach (var m in mats)
                        {
                            if (m == null) continue;
                            foreach (var prop in TexProps)
                            {
                                try
                                {
                                    if (!m.HasProperty(prop)) continue;
                                    var t = m.GetTexture(prop);
                                    if (t != null && t.name == texName) return t;
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            return null;
        }

        private static string CleanFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return string.IsNullOrEmpty(s) ? "texture" : s;
        }

        private static int ApplyTextureToScene(string texName, Texture2D tex)
        {
            int count = 0;
            for (int si = 0; si < SceneManager.sceneCount; si++)
            {
                var s = SceneManager.GetSceneAt(si);
                if (!s.isLoaded) continue;

                foreach (var root in s.GetRootGameObjects())
                {
                    string n = root.name;
                    if (!(n.StartsWith("Placeable_") || n.StartsWith("PB_"))) continue;

                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null) continue;
                        var mats = r.materials; // instance mats so we don't stomp the shared asset
                        if (mats == null) continue;
                        bool touched = false;

                        foreach (var m in mats)
                        {
                            if (m == null) continue;
                            foreach (var prop in TexProps)
                            {
                                try
                                {
                                    if (!m.HasProperty(prop)) continue;
                                    var cur = m.GetTexture(prop);
                                    if (cur == null || cur.name != texName) continue;

                                    if (!_originals.ContainsKey(texName)) _originals[texName] = cur;
                                    m.SetTexture(prop, tex);
                                    touched = true;
                                    count++;
                                }
                                catch { }
                            }
                        }
                        if (touched) r.materials = mats;
                    }
                }
            }
            return count;
        }

        private static void RevertOne(string texName)
        {
            if (!_originals.TryGetValue(texName, out var original) || original == null) return;
            for (int si = 0; si < SceneManager.sceneCount; si++)
            {
                var s = SceneManager.GetSceneAt(si);
                if (!s.isLoaded) continue;

                foreach (var root in s.GetRootGameObjects())
                {
                    string n = root.name;
                    if (!(n.StartsWith("Placeable_") || n.StartsWith("PB_"))) continue;

                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r == null) continue;
                        var mats = r.materials;
                        if (mats == null) continue;
                        bool touched = false;

                        foreach (var m in mats)
                        {
                            if (m == null) continue;
                            foreach (var prop in TexProps)
                            {
                                try
                                {
                                    if (!m.HasProperty(prop)) continue;
                                    var cur = m.GetTexture(prop);
                                    if (cur == null || cur.name != texName) continue;
                                    m.SetTexture(prop, original);
                                    touched = true;
                                }
                                catch { }
                            }
                        }
                        if (touched) r.materials = mats;
                    }
                }
            }
            _originals.Remove(texName);
        }

        // revert every override (e.g. on unload) but keep the saved mapping
        public static void RevertAll()
        {
            foreach (var key in new List<string>(_originals.Keys))
                RevertOne(key);
            _originals.Clear();
        }

        public static void ClearRuntime()
        {
            _overrides.Clear();
            _originals.Clear();
            _jsonPath = null;
        }

        // ── persistence (texture.json next to info.json) ──────────────────────────

        // called by the round loader after a round loads: read texture.json + apply each override
        public static void LoadAndApplyForRound(string infoJsonPath)
        {
            _overrides.Clear();
            _originals.Clear();
            _jsonPath = null;

            if (string.IsNullOrEmpty(infoJsonPath)) return;
            string folder = Path.GetDirectoryName(infoJsonPath);
            _jsonPath = Path.Combine(folder, "texture.json");

            if (!File.Exists(_jsonPath)) return;

            try
            {
                foreach (var pair in ParseJson(File.ReadAllText(_jsonPath)))
                {
                    // paths in texture.json are relative to the round folder
                    string png = Path.IsPathRooted(pair.Value) ? pair.Value : Path.Combine(folder, pair.Value);
                    if (SetOverride(pair.Key, png, out _))
                        _overrides[pair.Key] = png; // SetOverride already saved, but keep map consistent
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[ObstacleTextureLoader] load failed: {ex.Message}"); }
        }

        // current round's texture.json path; null until a round is loaded
        public static string JsonPath => _jsonPath;

        private static void Save()
        {
            if (string.IsNullOrEmpty(_jsonPath)) return;
            try
            {
                string folder = Path.GetDirectoryName(_jsonPath);
                var sb = new StringBuilder();
                sb.AppendLine("{");
                int i = 0;
                foreach (var pair in _overrides)
                {
                    // store relative to the round folder when the png lives inside it, else absolute
                    string val = pair.Value;
                    try
                    {
                        if (val.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                            val = val.Substring(folder.Length).TrimStart('\\', '/');
                    }
                    catch { }

                    string comma = (++i < _overrides.Count) ? "," : "";
                    sb.AppendLine($"  \"{Esc(pair.Key)}\": \"{Esc(val)}\"{comma}");
                }
                sb.AppendLine("}");
                File.WriteAllText(_jsonPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex) { Debug.LogWarning($"[ObstacleTextureLoader] save failed: {ex.Message}"); }
        }

        // tiny flat string:string json parser (texture.json is only ever a flat map)
        public static IEnumerable<KeyValuePair<string, string>> ParseTextureJson(string json) => ParseJson(json);

        private static IEnumerable<KeyValuePair<string, string>> ParseJson(string json)
        {
            var list = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(json)) return list;

            int i = 0;
            while (i < json.Length)
            {
                int k1 = json.IndexOf('"', i);
                if (k1 < 0) break;
                int k2 = json.IndexOf('"', k1 + 1);
                if (k2 < 0) break;
                string key = json.Substring(k1 + 1, k2 - k1 - 1);

                int colon = json.IndexOf(':', k2 + 1);
                if (colon < 0) break;
                int v1 = json.IndexOf('"', colon + 1);
                if (v1 < 0) break;
                int v2 = json.IndexOf('"', v1 + 1);
                if (v2 < 0) break;
                string val = json.Substring(v1 + 1, v2 - v1 - 1);

                list.Add(new KeyValuePair<string, string>(Unesc(key), Unesc(val)));
                i = v2 + 1;
            }
            return list;
        }

        private static string Esc(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";
        private static string Unesc(string s) => s?.Replace("\\\\", "\\").Replace("\\\"", "\"") ?? "";
    }
}
