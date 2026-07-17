using System.Collections.Generic;
using System;
using System.Reflection;
using FallGuysLib.Utils;
using UnityEngine;
using BetterFG.Services;

namespace BetterFG.Core
{
    public class AssetManager : MonoBehaviour
    {
        public static AssetManager Instance { get; private set; }
        private static Material defaultNameMaterial;
        public static Material DefaultNameMaterial
        {
            get
            {
                if (defaultNameMaterial != null) return defaultNameMaterial;
                var mats = Resources.FindObjectsOfTypeAll<Material>();
                for (int i = 0; mats != null && i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat != null && mat.name == "asap-bold sdf_2.0_Shadow")
                        return defaultNameMaterial = mat;
                }
                return null;
            }
        }

        private static Material goldNameMaterial;
        public static Material GoldNameMaterial
        {
            get
            {
                if (goldNameMaterial != null) return goldNameMaterial;
                var mats = Resources.FindObjectsOfTypeAll<Material>();
                for (int i = 0; mats != null && i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat != null && mat.name == "asap-bold sdf_EndFamePass")
                        return goldNameMaterial = mat;
                }
                return null;
            }
        }

        private static TMPro.TMP_FontAsset nameFontAsset;
        public static TMPro.TMP_FontAsset NameFontAsset
        {
            get
            {
                if (nameFontAsset != null) return nameFontAsset;
                var fonts = Resources.FindObjectsOfTypeAll<TMPro.TMP_FontAsset>();
                // the in-game nametag font asset is named "Asap-Bold SDF (Body)".
                for (int i = 0; fonts != null && i < fonts.Length; i++)
                {
                    var f = fonts[i];
                    if (f != null && f.name != null && f.name.IndexOf("Asap-Bold", StringComparison.OrdinalIgnoreCase) >= 0)
                        return nameFontAsset = f;
                }
                return null;
            }
        }

        private static Material ghostMaterial;
        public static Material GhostMaterial
        {
            get
            {
                if (ghostMaterial != null) return ghostMaterial;
                var go = SpawnPersistent("bettrfg_mat_ghost");
                if (go == null) return null;
                var mr = go.GetComponent<MeshRenderer>() ?? go.GetComponentInChildren<MeshRenderer>();
                if (mr != null) ghostMaterial = mr.sharedMaterial;
                Destroy(go);
                return ghostMaterial;
            }
        }

        private static Texture2D goldGreyscaleTex;
        public static Texture2D GoldGreyscaleTex
        {
            get
            {
                if (goldGreyscaleTex != null) return goldGreyscaleTex;
                var src = GoldNameMaterial;
                if (src == null) return null;
                var faceTex = src.GetTexture("_FaceTex") as Texture2D;
                if (faceTex == null) return null;
                return goldGreyscaleTex = ToGreyscaleNormalized(faceTex);
            }
        }

        private static Texture2D ToGreyscaleNormalized(Texture2D src)
        {
            Texture2D readable;
            if (src.isReadable)
            {
                readable = src;
            }
            else
            {
                var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                readable = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
                readable.Apply();
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
            }

            var pixels = readable.GetPixels();
            float maxL = 0f;
            for (int i = 0; i < pixels.Length; i++)
            {
                float l = pixels[i].r * 0.299f + pixels[i].g * 0.587f + pixels[i].b * 0.114f;
                if (l > maxL) maxL = l;
            }
            float scale = maxL > 0f ? 1f / maxL : 1f;
            for (int i = 0; i < pixels.Length; i++)
            {
                float l = (pixels[i].r * 0.299f + pixels[i].g * 0.587f + pixels[i].b * 0.114f) * scale;
                pixels[i] = new Color(l, l, l, pixels[i].a);
            }
            var dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            dst.SetPixels(pixels);
            dst.Apply();
            return dst;
        }

        public readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        public readonly Dictionary<string, GameObject> runtimePrefabs = new Dictionary<string, GameObject>();
        public readonly Dictionary<string, AnimationClip> animClips = new Dictionary<string, AnimationClip>();
        public readonly Dictionary<string, Shader> shaders = new Dictionary<string, Shader>();

        // shader bundled in bettrfg_ui_shader, used by the pink/grey HUD recolour. key is the bundle
        // asset filename without extension, lowercased.
        public static Shader GetShader(string key)
        {
            if (Instance == null) return null;
            Instance.shaders.TryGetValue(key.ToLowerInvariant(), out var s);
            return s;
        }

        public AssetManager(System.IntPtr ptr) : base(ptr)
        {
            Instance = this;
            LoadAllBundles();
        }

        void Awake() { }

        private void LoadAllBundles()
        {
            var asm = Assembly.GetExecutingAssembly();
            foreach (string res in asm.GetManifestResourceNames())
            {
                if (!res.StartsWith("BetterFG.assets.bundles.")) continue;
                var bundle = AssetBundleUtils.LoadEmbeddedBundle(asm, res);
                if (bundle == null) continue;
                foreach (string name in bundle.GetAllAssetNames())
                {
                    var asset = bundle.LoadAsset(name);
                    if (asset == null) continue;
                    string key = System.IO.Path.GetFileNameWithoutExtension(name).ToLowerInvariant();

                    var clip = asset.TryCast<AnimationClip>();
                    if (clip != null)
                    {
                        clip.hideFlags = HideFlags.HideAndDontSave;
                        animClips[key] = clip;
                        Plugin.Log.LogInfo($"AssetManager: loaded clip '{key}' from {res}");
                        continue;
                    }

                    var shader = asset.TryCast<Shader>();
                    if (shader != null)
                    {
                        shader.hideFlags = HideFlags.HideAndDontSave;
                        DontDestroyOnLoad(shader);
                        shaders[key] = shader;
                        Plugin.Log.LogInfo($"AssetManager: loaded shader '{key}' ({shader.name}) from {res}");
                        continue;
                    }

                    GameObject go = null;
                    try { go = asset.Cast<GameObject>(); } catch { continue; }
                    if (go == null) continue;
                    go.hideFlags = HideFlags.HideAndDontSave;
                    go.transform.position = new(go.transform.position.x, go.transform.position.y - 99999, go.transform.position.z);
                    prefabs[key] = go;
                    Plugin.Log.LogInfo($"AssetManager: loaded '{key}' from {res}");
                }
                bundle.Unload(false);
            }
        }

        public static void Spawn(string name, Vector3 pos, float lifetime = 2f)
        {
            if (Instance == null) return;
            string key = name.ToLowerInvariant();
            if (!Instance.prefabs.TryGetValue(key, out var prefab) || prefab == null) return;
            var go = GameObject.Instantiate(prefab, pos, Quaternion.identity);
            GameObject.Destroy(go, lifetime);
        }

        public static void SpawnPoof(Vector3 pos)
        {
            if (Instance != null && Instance.prefabs.TryGetValue("vfx_poof", out var prefab) && prefab != null)
            {
                var go = GameObject.Instantiate(prefab, pos, Quaternion.identity);
                // play once then stop emitting so it doesn't loop forever (some poof prefabs ship
                // with loop on). the Destroy below still bounds the lifetime as a backstop.
                var systems = go.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < systems.Length; i++)
                {
                    if (systems[i] == null) continue;
                    systems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    systems[i].Play(false);
                }
                GameObject.Destroy(go, 1.6f);
            }
            AudioService.PlayPoof();
        }

        /// <summary>
        /// Instantiates a prefab without a lifetime — caller owns the GO.
        /// Returns null if the prefab isn't loaded.
        /// </summary>
        public static GameObject SpawnPersistent(string name)
        {
            if (Instance == null) return null;
            string key = name.ToLowerInvariant();
            if (!Instance.prefabs.TryGetValue(key, out var prefab) || prefab == null) return null;
            var toreturn = GameObject.Instantiate(prefab);
            toreturn.hideFlags = HideFlags.None;
            return toreturn;
        }

        public static GameObject RuntimePrefab(string name, GameObject source, Action<GameObject> prep = null)
        {
            if (Instance == null || source == null) return source;
            string key = name.ToLowerInvariant();
            if (Instance.runtimePrefabs.TryGetValue(key, out var cached) && cached != null) return cached;

            cached = GameObject.Instantiate(source, Instance.transform);
            cached.name = "bfg_runtime_" + key;
            cached.hideFlags = HideFlags.HideAndDontSave;
            cached.SetActive(false);
            prep?.Invoke(cached);
            Instance.runtimePrefabs[key] = cached;
            return cached;
        }
    }
}
