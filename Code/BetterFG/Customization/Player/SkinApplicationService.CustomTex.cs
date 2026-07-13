using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;

namespace BetterFG.Customization.Player
{
    public partial class SkinApplicationService
    {
        // keyed by bean instance id — original materials per renderer before we touched them
        private Dictionary<int, List<CustomTexOriginal>> customTexOriginals = new Dictionary<int, List<CustomTexOriginal>>();
        private HashSet<int> customTexPollingBeans = new HashSet<int>();
        // beans we've already made our one real custom-tex attempt on (their GEO was fully built).
        // the game fires BindMeshToFallguy once per mesh, constantly (idle anim / LOD), and every
        // fire re-arms the reapply poll. without this, a bean whose costume DOESN'T contain the
        // target material never lands in customTexOriginals, so it re-polls forever -> the steady
        // background freeze after a texture is applied. cleared on revert so a settings/costume
        // change re-attempts cleanly.
        private HashSet<int> customTexAttemptedBeans = new HashSet<int>();
        private static readonly string[] customTexProps = { "_MainTex", "_BaseMap", "_BaseTexture", "_MainTex2" };

        private struct CustomTexOriginal
        {
            public Renderer renderer;
            public int matIdx;
            public string prop;
            public Texture texture;
            public string textureName;
        }

        // process-wide cache of decoded custom textures. each bean push used to re-read the file
        // and re-decode the PNG/JPG on the main thread, which stacks freezes during state changes
        // (round load, qual, reward — each pushes the local bean and OnBeansFound iterates every
        // saved entry). keyed on path + write timestamp so editing the file invalidates.
        private static readonly Dictionary<string, (long stamp, Texture2D tex)> _customTexCache =
            new Dictionary<string, (long, Texture2D)>(StringComparer.OrdinalIgnoreCase);

        // decode every enabled entry's texture up front (plugin load) so the first per-bean
        // auto-reapply is a cache hit — this cache, not the tab's _texCache, is what it reads
        public static void PrewarmCustomTexCache()
        {
            if (!int.TryParse(SettingsService.Get("skintex.entryCount", "0"), out int count) || count <= 0) return;
            for (int i = 0; i < count; i++)
            {
                if (SettingsService.Get($"skintex.entry.{i}.enabled", "1") != "1") continue;
                string texPath = SettingsService.Get($"skintex.entry.{i}.texPath", "");
                if (!string.IsNullOrEmpty(texPath)) GetCachedCustomTex(texPath);
            }
        }

        private static Texture2D GetCachedCustomTex(string path)
        {
            long stamp;
            try { stamp = System.IO.File.GetLastWriteTimeUtc(path).Ticks; } catch { return null; }
            if (_customTexCache.TryGetValue(path, out var hit) && hit.stamp == stamp && hit.tex != null)
                return hit.tex;
            byte[] data;
            try { data = System.IO.File.ReadAllBytes(path); }
            catch (Exception ex) { Plugin.Log.LogWarning($"read {path}: {ex.Message}"); return null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(data);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            _customTexCache[path] = (stamp, tex);
            return tex;
        }

        public int TryAutoReapplyCustomTextureForBean(GameObject bean)
        {
            if (bean == null) return 0;
            int beanId = bean.GetInstanceID();
            if (customTexOriginals.ContainsKey(beanId)) return 0;

            if (!int.TryParse(SettingsService.Get("skintex.entryCount", "0"), out int count) || count <= 0) return 0;

            int total = 0;
            for (int i = 0; i < count; i++)
            {
                if (SettingsService.Get($"skintex.entry.{i}.enabled", "1") != "1") continue;

                string texPath = SettingsService.Get($"skintex.entry.{i}.texPath", "");
                if (string.IsNullOrEmpty(texPath) || !System.IO.File.Exists(texPath)) continue;

                if (!int.TryParse(SettingsService.Get($"skintex.entry.{i}.matIdx", "0"), out int matIdx)) matIdx = 0;

                // rebuild match names from saved matNames so we only touch the right costume material
                string matNamesRaw = SettingsService.Get($"skintex.entry.{i}.matNames", "");
                var matchNames = new HashSet<string>();
                if (!string.IsNullOrEmpty(matNamesRaw))
                {
                    var parts = matNamesRaw.Split('|');
                    if (matIdx < parts.Length && !string.IsNullOrEmpty(parts[matIdx]))
                        matchNames.Add(parts[matIdx]);
                }

                // no match name = skip, don't blast everything
                if (matchNames.Count == 0) continue;

                try
                {
                    var tex = GetCachedCustomTex(texPath);
                    if (tex != null) total += ApplyCustomTexture(bean, matIdx, tex, matchNames);
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"auto-reapply entry {i}: {ex.Message}"); }
            }
            return total;
        }

        public void PollAndReapplyCustomTextureForBean(GameObject bean)
        {
            if (bean == null) return;
            // nothing saved to reapply -> don't spin a poll coroutine that scans the bean's renderers
            // every 0.5s for nothing. the game rebinds costume meshes constantly (animation/LOD) and
            // every rebind re-arms this via the BindMeshToFallguy postfix, so without this gate a
            // game-cosmetics-only loadout still pays a steady background poll.
            if (!int.TryParse(SettingsService.Get("skintex.entryCount", "0"), out int texCount) || texCount <= 0) return;
            int beanId = bean.GetInstanceID();
            if (customTexOriginals.ContainsKey(beanId) || customTexPollingBeans.Contains(beanId) || customTexAttemptedBeans.Contains(beanId)) return;
            customTexPollingBeans.Add(beanId);
            StartCoroutine(PollReapplyCoroutine(bean).WrapToIl2Cpp());
        }

        private IEnumerator PollReapplyCoroutine(GameObject bean)
        {
            float elapsed = 0f;
            int beanId = bean != null ? bean.GetInstanceID() : 0;
            while (elapsed < 10f)
            {
                if (bean == null) break;
                if (customTexOriginals.ContainsKey(beanId)) break;

                var geo = FindBeanGEO(bean);
                if (geo != null && geo.GetComponentsInChildren<Renderer>(true).Length > 0)
                {
                    // bean is fully built — this is our one real attempt. whether or not a texture
                    // matched, a material that isn't on this bean won't appear by polling longer.
                    // mark attempted so the constant BindMeshToFallguy postfix doesn't re-arm us.
                    customTexAttemptedBeans.Add(beanId);
                    TryAutoReapplyCustomTextureForBean(bean);
                    break;
                }
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }
            if (bean != null && !customTexOriginals.ContainsKey(beanId))
                Plugin.Log.LogWarning($"PollReapply no texture matched for {bean.name}");
            if (beanId != 0)
                customTexPollingBeans.Remove(beanId);
        }

        private void RevertLocalCustomTextures()
        {
            var local = BeanMonitorService.LocalPlayerBean;
            if (local != null) RevertCustomTexture(local);

            foreach (var bean in BeanMonitorService.GetTrackedBeans())
            {
                if (bean == null || bean == local) continue;
                if (IsRemoteInRoundBean(bean)) continue;
                RevertCustomTexture(bean);
            }
        }

        // scans bean GEO so normal costumes, custom skins, and additive cosmetics all count
        public int ApplyCustomTexture(GameObject bean, int matSlotIdx, Texture2D tex, HashSet<string> matchTexNames)
        {
            if (bean == null || tex == null) return 0;

            var geo = FindBeanGEO(bean);
            if (geo == null) return 0;

            int texSlot = 0;
            return ApplyTextureToGameObject(geo.gameObject, matSlotIdx, tex, matchTexNames, bean.GetInstanceID(), ref texSlot);
        }

        private int ApplyTextureToGameObject(GameObject costumeObj, int matSlotIdx, Texture2D tex, HashSet<string> matchTexNames, int beanId, ref int texSlot)
        {
            int count = 0;
            var renderers = costumeObj.GetComponentsInChildren<Renderer>(true);
            bool alreadyHadOriginals = customTexOriginals.TryGetValue(beanId, out var originalList);
            if (originalList == null)
                originalList = new List<CustomTexOriginal>();

            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mats = r.materials;
                if (mats == null) continue;
                bool touched = false;

                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;

                    bool hadTextureSlot = false;

                    foreach (var prop in customTexProps)
                    {
                        try
                        {
                            if (!m.HasProperty(prop)) continue;

                            var originalTex = FindCustomTexOriginal(originalList, r, i, prop, out string savedName);
                            if (originalTex == null)
                                originalTex = m.GetTexture(prop);
                            if (originalTex == null) continue;

                            hadTextureSlot = true;
                            string originalTexName = savedName ?? originalTex.name ?? "";
                            string matName = CleanMatName(m.name);
                            bool hasNameFilter = matchTexNames != null && matchTexNames.Count > 0;
                            bool nameMatch = hasNameFilter && (matchTexNames.Contains(originalTexName) || matchTexNames.Contains(matName));
                            if (hasNameFilter ? !nameMatch : texSlot != matSlotIdx) continue;

                            RememberCustomTexOriginal(originalList, r, i, prop, originalTex, originalTexName);

                            if (!string.IsNullOrEmpty(originalTexName))
                                tex.name = originalTexName;
                            m.SetTexture(prop, tex);
                            touched = true;
                            break;
                        }
                        catch { }
                    }

                    if (hadTextureSlot) texSlot++;
                }

                if (touched)
                {
                    r.materials = mats;
                    count++;
                }
            }
            if (count > 0 && !alreadyHadOriginals)
                customTexOriginals[beanId] = originalList;
            return count;
        }

        private static string CleanMatName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.EndsWith(" (Instance)") ? name.Substring(0, name.Length - 11) : name;
        }

        private static Texture FindCustomTexOriginal(List<CustomTexOriginal> originals, Renderer renderer, int matIdx, string prop, out string textureName)
        {
            textureName = null;
            if (originals == null) return null;

            foreach (var o in originals)
                if (o.renderer == renderer && o.matIdx == matIdx && o.prop == prop)
                {
                    textureName = o.textureName;
                    return o.texture;
                }

            return null;
        }

        private static void RememberCustomTexOriginal(List<CustomTexOriginal> originals, Renderer renderer, int matIdx, string prop, Texture texture, string textureName)
        {
            if (originals == null || renderer == null || texture == null) return;

            foreach (var o in originals)
                if (o.renderer == renderer && o.matIdx == matIdx && o.prop == prop) return;

            originals.Add(new CustomTexOriginal
            {
                renderer = renderer,
                matIdx = matIdx,
                prop = prop,
                texture = texture,
                textureName = textureName
            });
        }

        public void RevertCustomTexture(GameObject bean)
        {
            if (bean == null) return;
            int beanId = bean.GetInstanceID();
            // always re-enable a fresh attempt next time the bean rebinds — even for beans that
            // never matched (those aren't in customTexOriginals but DID get marked attempted)
            customTexAttemptedBeans.Remove(beanId);
            if (!customTexOriginals.TryGetValue(beanId, out var originals)) return;

            foreach (var o in originals)
            {
                var r = o.renderer;
                if (r == null) continue;
                try
                {
                    var mats = r.materials;
                    if (mats == null || o.matIdx < 0 || o.matIdx >= mats.Length) continue;
                    var m = mats[o.matIdx];
                    if (m == null || !m.HasProperty(o.prop)) continue;
                    m.SetTexture(o.prop, o.texture);
                    r.materials = mats;
                }
                catch { }
            }

            customTexOriginals.Remove(beanId);
        }
    }
}
