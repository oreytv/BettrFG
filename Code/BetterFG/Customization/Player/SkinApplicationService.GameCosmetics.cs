using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Utilities;
using BetterFG.Services;
using BetterFG.Core;
using FGClient;
using FG.Common;
using FG.Common.CMS;
using BetterFG.Customization.Menu;
using BetterFG.Network;
using BetterFG.UI.Tab;
using Il2CppInterop.Runtime;

namespace BetterFG.Customization.Player
{
    public partial class SkinApplicationService
    {
        // the in-game (unlocked) cosmetic look: costumes the player has equipped from the game's own
        // wardrobe, plus the single colour / pattern / faceplate. all of it is bean-state, not our
        // instantiated skins — it composites onto the base body via FallguyCustomisationHandler.

        // one equipped game costume slot per id
        private List<GameCosmeticSlot> activeGameCosmetics = new List<GameCosmeticSlot>();
        // colour, pattern and faceplate are each a single selection: the live option + the id we
        // persist + the name the UI shows. same shape three times, so one generic slot.
        private GameLook<ColourOption> activeColour;
        private GameLook<SkinPatternOption> activePattern;
        private GameLook<FaceplateOption> activeFaceplate;
        private bool gameCosmeticsRestoring;

        // masks carve the base body out from under each cosmetic. loading is shared/deduped because
        // the same mask gets requested by every bean, and compositing is a GPU readback we cache.
        private Dictionary<string, Texture> gameCosmeticMaskCache = new Dictionary<string, Texture>();
        private HashSet<string> gameCosmeticMaskLoading = new HashSet<string>();
        private Dictionary<string, Texture2D> compositedMaskCache = new Dictionary<string, Texture2D>();

        // beans currently inside BindMeshToFallguy, so the poller doesn't fight a half-bound mesh
        private HashSet<int> gameCosmeticBindingBeans = new HashSet<int>();

        // per remote-profile bean: the costume options THAT PROFILE applied, so cosmetic masks
        // composite against the bean's own top/bottom costume instead of the local player's
        // (GlobalGameStateClient is always local). keyed by bean instance id.
        private Dictionary<int, List<CostumeOption>> profileBeanCostumes = new Dictionary<int, List<CostumeOption>>();

        private struct GameLook<T> where T : UnityEngine.Object
        {
            public T option;
            public string id;
            public string name;

            public bool On => option != null;
            public void Set(T opt, string newId, string newName) { option = opt; id = newId; name = newName; }
            public void Clear() { option = null; id = null; name = null; }
        }

        private void RemoveGameCosmeticById(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            CostumeOption removedOption = null;
            foreach (var slot in activeGameCosmetics)
            {
                if (slot == null || slot.id != id) continue;
                removedOption = slot.option;
                break;
            }
            activeGameCosmetics.RemoveAll(s => s == null || s.id == id);

            var localBean = BeanMonitorService.LocalPlayerBean;
            var toKill = new List<string>();
            var affectedBeans = new List<GameObject>();
            foreach (var kvp in appliedSkins)
            {
                if (SkinFileFromAppliedKey(kvp.Key) != id) continue;
                var bean = kvp.Value?.bean;
                if (localBean != null && bean != null && bean != localBean && IsRemoteInRoundBean(bean)) continue;
                if (bean != null && !affectedBeans.Contains(bean)) affectedBeans.Add(bean);
                if (kvp.Value?.instance != null)
                {
                    var poller = kvp.Value.instance.GetComponent<CostumePollerComponent>();
                    if (poller != null) poller.enabled = false;
                    DestroyAppliedInstance(kvp.Value.instance, kvp.Value.addressableInstance);
                }
                if (kvp.Value?.boundRenderers != null)
                {
                    foreach (var go in kvp.Value.boundRenderers)
                        if (go != null) GameObject.Destroy(go);
                }
                if (kvp.Value?.beanGEO != null)
                    RestoreBaseBeanVisibility(kvp.Key, kvp.Value);
                toKill.Add(kvp.Key);
            }
            foreach (var k in toKill) { appliedSkins.Remove(k); pendingKeys.Remove(k); }

            foreach (var bean in affectedBeans)
                StartCoroutine(ReapplyGameCosmeticMasksCoroutine(bean, removedOption).WrapToIl2Cpp());
        }

        // Applies the currently active game (unlocked) cosmetics, colour and pattern to one specific bean.
        // Used by callers that spawn their own beans (e.g. the PB ghost) which aren't picked up by the
        // auto-apply-to-all-beans passes.
        public IEnumerator ApplyActiveGameCosmeticsToBeanCoroutine(GameObject bean, ApplyReason reason = ApplyReason.FromMenu)
        {
            if (bean == null) yield break;
            foreach (var slot in new List<GameCosmeticSlot>(activeGameCosmetics))
            {
                if (slot == null) continue;
                string key = MakeKey(bean, slot.id);
                if (appliedSkins.ContainsKey(key) || pendingKeys.Contains(key)) continue;
                pendingKeys.Add(key);
                yield return ApplyGameCosmeticToBeanCoroutine(slot, bean, reason).WrapToIl2Cpp();
            }
            ApplyGameColourPatternToBean(bean);
        }

        private IEnumerator ApplyGameCosmeticToAllBeansCoroutine(GameCosmeticSlot slot, ApplyReason reason)
        {
            var beans = BeanMonitorService.GetTrackedBeans();
            foreach (var bean in beans)
            {
                if (SlotDead(slot)) yield break;
                if (bean == null || SkipMenuSkinAutoApplyForThisBean(bean)) continue;
                string key = MakeKey(bean, slot.id);
                if (appliedSkins.ContainsKey(key) || pendingKeys.Contains(key)) continue;
                pendingKeys.Add(key);
                yield return ApplyGameCosmeticToBeanCoroutine(slot, bean, reason).WrapToIl2Cpp();
            }
        }

        private IEnumerator ApplyGameCosmeticToBeanCoroutine(GameCosmeticSlot slot, GameObject bean, ApplyReason reason)
        {
            string pendingKey = MakeKey(bean, slot.id);
            if (SlotDead(slot) || bean == null) { pendingKeys.Remove(pendingKey); yield break; }
            // full-body UGC costume on the local loadout hides this anyway — don't apply it (profile
            // beans come in via remotePipeline and are exempt; their costumes are their own concern)
            if (!slot.remotePipeline && LoadoutHidesBaseBody()) { pendingKeys.Remove(pendingKey); yield break; }

            Transform beanGEO = FindBeanGEO(bean);
            if (beanGEO == null) { Plugin.Log.LogWarning($"no GEO on {bean.name}"); pendingKeys.Remove(pendingKey); yield break; }

            var disabled = new List<GameObject>();

            // the option comes off CustomisationManager's catalog, which hands back entries the game
            // hasn't loaded yet - their costumePrefabReference isn't ready and InstantiateAsync just
            // stalls until it times out. CostumeOption is an IAddressableLoadableAsset, so ask it to
            // load itself first. (used to be free: the old FindObjectsOfTypeAll lookup could only ever
            // return already-loaded options, so nothing ever hit this)
            if (!slot.option.IsLoaded)
            {
                try { slot.option.LoadBlocking(); }
                catch (Exception ex) { Plugin.Log.LogWarning($"couldn't load {slot.name}: {ex.Message}"); }
            }

            GameObject clone = null;
            string err = null;
            UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject> op;
            try { op = slot.option.costumePrefabReference.InstantiateAsync(); }
            catch (Exception ex) { err = ex.Message; op = default; }

            if (err == null)
            {
                // bounded wait instead of a bare `yield return op`. if the addressable load hangs
                // (round transition thrashing addressables) or the bean/slot dies underneath us, a
                // plain yield would suspend forever and leave pendingKey stuck — every later reapply
                // then sees inPending=true and skips, so the cosmetic silently never comes back.
                float waited = 0f;
                while (!op.IsDone && waited < 8f)
                {
                    if (bean == null || SlotDead(slot)) break;
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (op.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
                    clone = op.Result;
                else if (!op.IsDone)
                    err = $"instantiate timed out after {waited:0.0}s";
                else
                {
                    try { err = op.OperationException != null ? op.OperationException.Message : "instantiate failed"; }
                    catch { err = "instantiate failed"; }
                }
            }

            if (err != null || clone == null)
            {
                Plugin.Log.LogWarning($"instantiate failed: {err}");
                // release the handle so a load that finishes after our timeout doesn't leak an
                // orphaned addressable instance (ReleaseInstance no-ops if it isn't done yet, but the
                // handle release still decrements the ref count once it resolves)
                try { if (op.IsValid()) UnityEngine.AddressableAssets.Addressables.Release(op); } catch { }
                RestoreBaseBeanVisibility(pendingKey, new AppliedSkinInfo { disabledChildren = disabled });
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            if (SlotDead(slot))
            {
                RestoreBaseBeanVisibility(pendingKey, new AppliedSkinInfo { disabledChildren = disabled });
                DestroyAppliedInstance(clone, true);
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.transform.SetParent(beanGEO, true);
            clone.transform.position = beanGEO.position;
            clone.transform.rotation = beanGEO.rotation;
            clone.transform.localScale = Vector3.one;

            if (GameObjectHelper.IsUICharacter(bean) || GameObjectHelper.IsLobbyCharacter(bean))
                GameObjectHelper.SetLayerRecursively(clone, LayerMask.NameToLayer("PlayerUI"));

            var info = new SkinInfo { name = slot.name, file = slot.id, type = "Costume", keepBase = true };

            // Snapshot beanGEO children before bind; BindMeshToFallguy reparents/spawns mesh
            // GameObjects directly under GEO (e.g. CH_Digi_Top(Clone), CH_Digi_Bottom(Clone)) that
            // are NOT children of `clone` — so destroying clone alone leaves them visible.
            var preBindGeoChildren = new HashSet<int>();
            for (int i = 0; i < beanGEO.childCount; i++)
            {
                var c = beanGEO.GetChild(i);
                if (c != null && c.gameObject != null) preBindGeoChildren.Add(c.gameObject.GetInstanceID());
            }

            var boundRenderers = BindGameCosmeticToFallguy(clone, bean);

            for (int i = 0; i < beanGEO.childCount; i++)
            {
                var c = beanGEO.GetChild(i);
                if (c == null || c.gameObject == null) continue;
                var go = c.gameObject;
                if (go == clone) continue;
                if (preBindGeoChildren.Contains(go.GetInstanceID())) continue;
                if (!boundRenderers.Contains(go)) boundRenderers.Add(go);
            }

            yield return ApplyGameCosmeticMaskCoroutine(slot.option, bean).WrapToIl2Cpp();
            ApplyGameColourPatternToBean(bean);

            KillExistingAppliedAtKey(pendingKey);
            appliedSkins[pendingKey] = new AppliedSkinInfo { instance = clone, bean = bean, type = SkinType.Costume, disabledChildren = disabled, boundRenderers = boundRenderers, addressableInstance = true };
            pendingKeys.Remove(pendingKey);
            CustomSkinTextureTab.ReapplyAllEnabledFromSettings();
            OnSkinApplied?.Invoke(new SkinApplyEvent { skinInfo = info, bean = bean, reason = reason });
        }

        private List<GameObject> BindGameCosmeticToFallguy(GameObject clone, GameObject bean)
        {
            var boundGos = new List<GameObject>();
            var fgch = bean.GetComponent<FallguyCustomisationHandler>();
            if (fgch == null)
            {
                Plugin.Log.LogWarning($"no FallguyCustomisationHandler on {bean.name}");
                return boundGos;
            }
            int bound = 0;
            var smrs = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int beanId = bean.GetInstanceID();
            gameCosmeticBindingBeans.Add(beanId);
            try
            {
                if (fgch._topRenderers == null)
                    fgch._topRenderers = new Il2CppSystem.Collections.Generic.List<SkinnedMeshRenderer>();
                if (fgch._bottomRenderers == null)
                    fgch._bottomRenderers = new Il2CppSystem.Collections.Generic.List<SkinnedMeshRenderer>();

                for (int i = 0; i < smrs.Length; i++)
                {
                    var smr = smrs[i];
                    if (smr == null) continue;
                    try
                    {
                        fgch.BindMeshToFallguy(smr);
                        bound++;
                        // BindMeshToFallguy reparents the SMR's GameObject out of `clone`.
                        // Track it so removal can destroy it — otherwise the visible mesh persists
                        // after we Destroy(clone) because the SMR is no longer a child of clone.
                        if (smr != null && smr.gameObject != null && !boundGos.Contains(smr.gameObject))
                            boundGos.Add(smr.gameObject);

                        // copy-colour costumes use the Mediatonic/CH_Costume_ColorCopy shader which
                        // only recolours once the SMR is registered as a top/bottom renderer AND
                        // SetCostumeBaseCopyColor runs after that assignment. so: assign, then call.
                        string n = smr.name != null ? smr.name.ToLowerInvariant() : "";
                        bool isBottom = n.Contains("bottom") || n.Contains("lower") || n.Contains("leg") || n.Contains("feet") || n.Contains("foot");
                        if (isBottom)
                        {
                            if (!fgch._bottomRenderers.Contains(smr)) fgch._bottomRenderers.Add(smr);
                        }
                        else
                        {
                            if (!fgch._topRenderers.Contains(smr)) fgch._topRenderers.Add(smr);
                        }
                        try { fgch.SetCostumeBaseCopyColor(); }
                        catch (Exception ex) { Plugin.Log.LogWarning($"SetCostumeBaseCopyColor failed: {ex.Message}"); }
                    }
                    catch (Exception ex) { Plugin.Log.LogWarning($"bind failed on {smr.name}: {ex.Message}"); }
                }
            }
            finally
            {
                gameCosmeticBindingBeans.Remove(beanId);
            }


            return boundGos;
        }

        private IEnumerator ApplyGameCosmeticMaskCoroutine(CostumeOption option, GameObject bean)
        {
            if (option == null || bean == null) yield break;

            var fgch = bean.GetComponent<FallguyCustomisationHandler>();
            if (fgch == null) yield break;

            // load this cosmetic's mask
            Texture mask = null;
            bool loaded = false;
            yield return LoadCostumeMaskCoroutine(option, "cosm", r => { mask = r; loaded = true; }).WrapToIl2Cpp();
            if (!loaded || mask == null)
            {
                Plugin.Log.LogWarning($"no costume mask for {GetGameCosmeticName(option)}");
                yield break;
            }

            // collect real costume masks (top + bottom) so we can union them with ours —
            // SetMask replaces per slot so we must composite all the masks into one texture.
            // for a remote PROFILE bean use ITS costume options (recorded when the profile applied),
            // not GlobalGameStateClient which is always the local player.
            var realOpts = GetRealCostumeOptionsForBean(bean);

            var maskList = new List<Texture> { mask };
            foreach (var ro in realOpts)
            {
                if (ro == null) continue;
                Texture rm = null;
                bool rl = false;
                yield return LoadCostumeMaskCoroutine(ro, "real", r => { rm = r; rl = true; }).WrapToIl2Cpp();
                if (rl && rm != null) maskList.Add(rm);
            }

            Texture finalMask = maskList.Count == 1 ? mask : CompositeMasks(maskList);

            try
            {
                fgch.SetMask(option, finalMask);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"SetMask failed: {ex.Message}");
            }
        }

        // which top/bottom costume options to union a cosmetic mask against for this bean. remote
        // profile beans use the costumes that profile applied; everyone else falls back to the local
        // player's selections (correct for the local bean).
        private List<CostumeOption> GetRealCostumeOptionsForBean(GameObject bean)
        {
            var result = new List<CostumeOption>();
            if (bean != null && profileBeanCostumes.TryGetValue(bean.GetInstanceID(), out var profileCostumes) && profileCostumes != null)
            {
                foreach (var c in profileCostumes) if (c != null) result.Add(c);
                return result;
            }

            var sel = GlobalGameStateClient.Instance?.PlayerProfile?.CustomisationSelections;
            if (sel != null)
            {
                if (sel.CostumeTopOption != null) result.Add(sel.CostumeTopOption);
                if (sel.CostumeBottomOption != null) result.Add(sel.CostumeBottomOption);
            }
            // ALSO union every active all-cosmetics cosmetic, not just the equipped top/bottom. with
            // multiple cosmetics on, each one's mask must hide the base body under ALL of them — not
            // only under itself + the real costume — otherwise the body pokes through wherever another
            // cosmetic sits. dupes (incl. the cosmetic currently being applied) are harmless: the
            // composite takes a channel-wise max so adding the same mask twice changes nothing.
            foreach (var slot in activeGameCosmetics)
                if (slot?.option != null) result.Add(slot.option);
            return result;
        }

        private IEnumerator LoadCostumeMaskCoroutine(CostumeOption option, string label, Action<Texture> done)
        {
            if (option == null) { done?.Invoke(null); yield break; }

            Texture mask = null;
            try { mask = option.CostumeMask; } catch { }
            string maskKey = GetCostumeMaskKey(option);
            if (mask == null && !string.IsNullOrEmpty(maskKey) && gameCosmeticMaskCache.TryGetValue(maskKey, out var cached) && cached != null)
                mask = cached;

            if (mask == null)
            {
                UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<Texture> op;
                bool started = false;
                bool waitForOtherLoad = false;
                try
                {
                    if (option.costumeMaskReference != null)
                    {
                        var already = option.costumeMaskReference.Asset;
                        if (already != null) mask = already.TryCast<Texture>();

                        if (mask == null && !string.IsNullOrEmpty(maskKey) && gameCosmeticMaskLoading.Contains(maskKey))
                            waitForOtherLoad = true;

                        if (mask == null && !waitForOtherLoad)
                        {
                            if (!string.IsNullOrEmpty(maskKey)) gameCosmeticMaskLoading.Add(maskKey);
                            op = option.costumeMaskReference.LoadAssetAsync<Texture>();
                            started = true;
                        }
                        else op = default;
                    }
                    else op = default;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"{label} mask load start failed: {ex.Message}");
                    op = default;
                }

                if (waitForOtherLoad)
                {
                    float waited = 0f;
                    while (waited < 5f && gameCosmeticMaskLoading.Contains(maskKey))
                    {
                        yield return null;
                        waited += Time.deltaTime;
                    }
                    if (gameCosmeticMaskCache.TryGetValue(maskKey, out cached) && cached != null)
                        mask = cached;
                }

                if (started)
                {
                    yield return op;
                    try
                    {
                        if (op.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
                            mask = op.Result;
                    }
                    catch (Exception ex) { Plugin.Log.LogWarning($"{label} mask result failed: {ex.Message}"); }
                    if (!string.IsNullOrEmpty(maskKey)) gameCosmeticMaskLoading.Remove(maskKey);
                }
            }

            if (mask != null && !string.IsNullOrEmpty(maskKey)) gameCosmeticMaskCache[maskKey] = mask;
            done?.Invoke(mask);
        }

        // composite multiple masks by taking channel-wise max (union of hidden regions).
        // CPU path via RenderTexture readback so we don't depend on a custom shader.
        private Texture CompositeMasks(List<Texture> masks)
        {
            if (masks == null || masks.Count == 0) return null;

            // dedupe by name FIRST. every cosmetic passes its own mask plus the union of all
            // active cosmetics, so its own mask is in the list twice — the duplicate made each
            // cosmetic's cache key unique and all of them re-composited from scratch (each one
            // a stack of ReadPixels GPU stalls) instead of sharing one cached composite.
            var unique = new List<Texture>(masks.Count);
            var nameList = new List<string>(masks.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < masks.Count; i++)
            {
                if (masks[i] == null) continue;
                string n = string.IsNullOrEmpty(masks[i].name) ? masks[i].GetInstanceID().ToString() : masks[i].name;
                if (seen.Add(n)) { unique.Add(masks[i]); nameList.Add(n); }
            }
            masks = unique;
            if (masks.Count == 0) return null;
            if (masks.Count == 1) return masks[0];

            // key by texture name (stable across reward-screen / menu rebuilds where the masks
            // are reinstantiated). instance IDs change every transition so the old key never hit
            // the cache after the first apply and we re-composited from scratch every time.
            nameList.Sort();
            string key = string.Join(",", nameList);
            if (compositedMaskCache.TryGetValue(key, out var cached) && cached != null) return cached;
            int w = 0, h = 0;
            foreach (var m in masks) { if (m == null) continue; if (m.width > w) w = m.width; if (m.height > h) h = m.height; }
            if (w == 0 || h == 0) return masks[0];

            Color[] accum = null;
            try
            {
                foreach (var m in masks)
                {
                    if (m == null) continue;
                    var pixels = ReadTexturePixels(m, w, h);
                    if (pixels == null) continue;
                    if (accum == null) { accum = pixels; continue; }
                    for (int i = 0; i < accum.Length; i++)
                    {
                        var a = accum[i]; var b = pixels[i];
                        if (b.r > a.r) a.r = b.r;
                        if (b.g > a.g) a.g = b.g;
                        if (b.b > a.b) a.b = b.b;
                        if (b.a > a.a) a.a = b.a;
                        accum[i] = a;
                    }
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"composite failed: {ex.Message}"); return masks[0]; }

            if (accum == null) return masks[0];

            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            outTex.name = "BFG_CompositeMask_" + key;
            // pin so Resources.UnloadUnusedAssets on scene change doesn't nuke our cache. without
            // this the cache silently empties on every menu/reward transition and we re-composite
            // from scratch every time, which is the bulk of the 1-2s post-bean-spawn freeze.
            outTex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            outTex.SetPixels(accum);
            outTex.Apply(false, false);
            compositedMaskCache[key] = outTex;
            return outTex;
        }

        private static Color[] ReadTexturePixels(Texture src, int w, int h)
        {
            if (src == null) return null;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false, false);
                var px = tex.GetPixels();
                UnityEngine.Object.Destroy(tex);
                return px;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static string GetCostumeMaskKey(CostumeOption option)
        {
            if (option == null) return "";
            try
            {
                var r = option.costumeMaskReference;
                if (r != null)
                    return (r.AssetGUID ?? "") + "|" + (r.SubObjectName ?? "");
            }
            catch { }
            return GetGameCosmeticOptionId(option) + "|mask";
        }

        private IEnumerator ReapplyGameCosmeticMasksCoroutine(GameObject bean, CostumeOption clearOption)
        {
            if (bean == null) yield break;
            var fgch = bean.GetComponent<FallguyCustomisationHandler>();
            if (fgch == null) yield break;

            if (clearOption != null)
            {
                try
                {
                    var blank = fgch._noCostumeMask;
                    if (blank != null)
                    {
                        fgch.SetMask(clearOption, blank);
                    }
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"clear mask failed: {ex.Message}"); }
            }

            for (int i = 0; i < activeGameCosmetics.Count; i++)
            {
                var slot = activeGameCosmetics[i];
                if (slot == null || slot.option == null) continue;

                // if the mesh instance is missing on this bean, the mask alone would carve holes
                // with nothing filling them. reapply the full cosmetic (mesh + mask) instead.
                string key = MakeKey(bean, slot.id);
                bool meshPresent = appliedSkins.TryGetValue(key, out var entry) && entry?.instance != null;
                if (!meshPresent && !pendingKeys.Contains(key))
                {
                    appliedSkins.Remove(key);
                    pendingKeys.Add(key);
                    yield return ApplyGameCosmeticToBeanCoroutine(slot, bean, ApplyReason.FromMenu).WrapToIl2Cpp();
                    continue;
                }

                yield return ApplyGameCosmeticMaskCoroutine(slot.option, bean).WrapToIl2Cpp();
            }
        }

        private void ApplyGameColourPatternToAllBeans()
        {
            var beans = BeanMonitorService.GetTrackedBeans();
            for (int i = 0; i < beans.Count; i++)
                if (beans[i] != null && !SkipMenuSkinAutoApplyForThisBean(beans[i]))
                    ApplyGameColourPatternToBean(beans[i]);
        }

        private void ApplyGameColourPatternToBean(GameObject bean)
        {
            if (bean == null) { return; }
            // full-body UGC costume hides the base bean — colour/pattern/faceplate would never show
            if (LoadoutHidesBaseBody()) { return; }
            var fgch = bean.GetComponent<FallguyCustomisationHandler>();
            if (fgch == null) { return; }

            if (activeColour.On)
            {
                try { fgch.UpdateColourOption(activeColour.option); }
                catch (Exception ex) { Plugin.Log.LogWarning("colour failed: " + ex.Message); }
            }

            if (activePattern.On)
            {
                try { activePattern.option.LoadBlocking(); } catch { }
                try { fgch.UpdatePatternTexture(activePattern.option); }
                catch (Exception ex) { Plugin.Log.LogWarning("pattern failed: " + ex.Message); }
            }

            if (activeFaceplate.On)
            {
                try { fgch.UpdateFaceplateColours(activeFaceplate.option); }
                catch (Exception ex) { Plugin.Log.LogWarning("faceplate failed: " + ex.Message); }
            }

            // copy-colour costumes snapshot the body's base colour at bind time. on a view switch
            // the SMRs may still be parented under this bean's GEO but missing from the FCH's
            // top/bottom renderer lists (or pointing at the previous bean), so SetCostumeBaseCopyColor
            // would no-op. re-register every applied gamecosmetic's bound SMRs for THIS bean, then
            // re-run the copy so it picks up our activeColour.option.
            if (activeColour.On)
            {
                try
                {
                    if (fgch._topRenderers == null) fgch._topRenderers = new Il2CppSystem.Collections.Generic.List<SkinnedMeshRenderer>();
                    if (fgch._bottomRenderers == null) fgch._bottomRenderers = new Il2CppSystem.Collections.Generic.List<SkinnedMeshRenderer>();

                    foreach (var kvp in appliedSkins)
                    {
                        var entry = kvp.Value;
                        if (entry == null || entry.bean != bean || entry.boundRenderers == null) continue;
                        foreach (var go in entry.boundRenderers)
                        {
                            if (go == null) continue;
                            var smr = go.GetComponent<SkinnedMeshRenderer>();
                            if (smr == null) continue;
                            string n = smr.name != null ? smr.name.ToLowerInvariant() : "";
                            bool isBottom = n.Contains("bottom") || n.Contains("lower") || n.Contains("leg") || n.Contains("feet") || n.Contains("foot");
                            var list = isBottom ? fgch._bottomRenderers : fgch._topRenderers;
                            if (!list.Contains(smr)) list.Add(smr);
                        }
                    }
                }
                catch (Exception ex) { Plugin.Log.LogWarning("re-register SMRs failed: " + ex.Message); }

                try { fgch.SetCostumeBaseCopyColor(); }
                catch (Exception ex) { Plugin.Log.LogWarning("SetCostumeBaseCopyColor failed: " + ex.Message); }
            }
        }

        private void SpawnGameCosmeticPoof()
        {
            var bean = BeanMonitorService.LocalPlayerBean;
            if (bean == null)
            {
                var beans = BeanMonitorService.GetTrackedBeans();
                if (beans.Count > 0) bean = beans[0];
            }
            if (bean != null) AssetManager.SpawnPoof(bean.transform.position + Vector3.up);
        }

        private static string GetGameCosmeticName(CostumeOption option)
        {
            try { return option.CMSData.Name._text ?? option.name ?? ""; } catch { }
            try { return option.name ?? ""; } catch { }
            return "cosmetic";
        }

        private static string GetGameCosmeticId(CostumeOption option)
        {
            try
            {
                var cms = option.CMSData;
                if (cms != null && !string.IsNullOrEmpty(cms.FullItemId)) return cms.FullItemId;
                if (cms != null && !string.IsNullOrEmpty(cms.Id)) return cms.Id;
            }
            catch { }
            try { if (!string.IsNullOrEmpty(option.name)) return option.name; } catch { }
            return option.GetInstanceID().ToString();
        }

        private static string GetGameColourName(ColourOption option)
        {
            try { return option.CMSData.Name._text ?? option.name ?? ""; } catch { }
            try { return option.name ?? ""; } catch { }
            return "colour";
        }

        private static string GetGameColourId(ColourOption option)
        {
            try
            {
                var cms = option.CMSData;
                if (cms != null && !string.IsNullOrEmpty(cms.FullItemId)) return cms.FullItemId;
                if (cms != null && !string.IsNullOrEmpty(cms.Id)) return cms.Id;
            }
            catch { }
            try { if (!string.IsNullOrEmpty(option.name)) return option.name; } catch { }
            return option.GetInstanceID().ToString();
        }

        private static string GetGamePatternName(SkinPatternOption option)
        {
            try { return option.CMSData.Name._text ?? option.name ?? ""; } catch { }
            try { return option.name ?? ""; } catch { }
            return "pattern";
        }

        private static string GetGamePatternId(SkinPatternOption option)
        {
            try
            {
                var cms = option.CMSData;
                if (cms != null && !string.IsNullOrEmpty(cms.FullItemId)) return cms.FullItemId;
                if (cms != null && !string.IsNullOrEmpty(cms.Id)) return cms.Id;
            }
            catch { }
            try { if (!string.IsNullOrEmpty(option.name)) return option.name; } catch { }
            return option.GetInstanceID().ToString();
        }

        private static string GetGameFaceplateName(FaceplateOption option)
        {
            try { return ((ItemDefinitionSO)option).CMSData.Name._text ?? option.name ?? ""; } catch { }
            try { return option.name ?? ""; } catch { }
            return "faceplate";
        }

        private static string GetGameFaceplateId(FaceplateOption option)
        {
            try
            {
                var cms = ((ItemDefinitionSO)option).CMSData;
                if (cms != null && !string.IsNullOrEmpty(cms.FullItemId)) return cms.FullItemId;
                if (cms != null && !string.IsNullOrEmpty(cms.Id)) return cms.Id;
            }
            catch { }
            try { if (!string.IsNullOrEmpty(option.name)) return option.name; } catch { }
            return option.GetInstanceID().ToString();
        }
    }
}
