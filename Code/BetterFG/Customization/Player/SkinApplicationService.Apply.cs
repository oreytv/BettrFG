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
        // ── Costume ───────────────────────────────────────────────────────────

        private IEnumerator ApplyCostumeCoroutine(ActiveSkinSlot slot, GameObject bean, ApplyReason reason = ApplyReason.FromMenu)
        {
            string pendingKey = MakeKey(bean, slot.skinInfo.file);
            if (slot.bundle == null) { pendingKeys.Remove(pendingKey); yield break; }
            if (SlotDead(slot)) { pendingKeys.Remove(pendingKey); yield break; }

            Transform beanGEO = FindBeanGEO(bean);
            if (beanGEO == null) { Plugin.Log.LogError($"no GEO on {bean.name}"); pendingKeys.Remove(pendingKey); yield break; }

            var protectedClones = new HashSet<GameObject>();
            foreach (var kvp in appliedSkins)
                if (kvp.Key.StartsWith(bean.GetInstanceID() + "|") && kvp.Value?.instance != null)
                    protectedClones.Add(kvp.Value.instance);

            var disabled = new List<GameObject>();
            bool isRemote = IsRemoteInRoundBean(bean);
            bool keepLocalDonor = false;
            if (!slot.skinInfo.keepBase)
            {
                Transform donor = null;
                for (int i = 0; i < beanGEO.childCount; i++)
                {
                    Transform c = beanGEO.GetChild(i);
                    if (c == null || c.gameObject == null) continue;
                    if (!(c.name.Contains("Top") || c.name.Contains("Bottom") || c.name.Contains("CH_") || c.name.Contains("Body_LOD") || c.name.Contains("LOD"))) continue;
                    if (c.name.Contains("Body_LOD0")) { donor = c; break; }
                }
                if (donor == null)
                {
                    for (int i = 0; i < beanGEO.childCount; i++)
                    {
                        Transform c = beanGEO.GetChild(i);
                        if (c == null || c.gameObject == null) continue;
                        if (!(c.name.Contains("Top") || c.name.Contains("Bottom") || c.name.Contains("CH_") || c.name.Contains("Body_LOD") || c.name.Contains("LOD"))) continue;
                        var smr = c.GetComponentInChildren<SkinnedMeshRenderer>(true);
                        if (smr != null && smr.bones != null && smr.bones.Length > 0) { donor = c; break; }
                    }
                }
                if (donor == null)
                {
                    for (int i = 0; i < beanGEO.childCount; i++)
                    {
                        Transform c = beanGEO.GetChild(i);
                        if (c == null || c.gameObject == null) continue;
                        if (!(c.name.Contains("Top") || c.name.Contains("Bottom") || c.name.Contains("CH_") || c.name.Contains("Body_LOD") || c.name.Contains("LOD"))) continue;
                        if (c.name.Contains("Body_LOD")) { donor = c; break; }
                    }
                }

                for (int i = 0; i < beanGEO.childCount; i++)
                {
                    Transform child = beanGEO.GetChild(i)?.gameObject?.transform;
                    if (child == null || !child.gameObject.activeSelf) continue;
                    if (protectedClones.Contains(child.gameObject)) continue;

                    if (isRemote)
                    {
                        if (donor != null && child == donor)
                        {
                            var renderers = child.GetComponentsInChildren<Renderer>(true);
                            if (renderers != null && renderers.Length > 0)
                            {
                                var matMap = new Dictionary<Renderer, Material[]>();
                                Material invis = CostumePollerComponent.GetInvisibleMat();
                                foreach (var r in renderers)
                                {
                                    if (r == null) continue;
                                    matMap[r] = r.materials;
                                    var newMats = new Material[r.materials.Length];
                                    for (int j = 0; j < newMats.Length; j++) newMats[j] = invis;
                                    r.materials = newMats;
                                }
                                hiddenRendererMaterials[pendingKey + "_" + i] = matMap;
                            }
                        }
                        else
                        {
                            child.gameObject.SetActive(false);
                            disabled.Add(child.gameObject);
                        }
                    }
                    else if (keepLocalDonor && donor != null && child == donor)
                    {
                        var renderers = child.GetComponentsInChildren<Renderer>(true);
                        if (renderers != null && renderers.Length > 0)
                        {
                            var matMap = new Dictionary<Renderer, Material[]>();
                            Material invis = CostumePollerComponent.GetInvisibleMat();
                            foreach (var r in renderers)
                            {
                                if (r == null) continue;
                                matMap[r] = r.materials;
                                var newMats = new Material[r.materials.Length];
                                for (int j = 0; j < newMats.Length; j++) newMats[j] = invis;
                                r.materials = newMats;
                            }
                            hiddenRendererMaterials[pendingKey + "_" + i] = matMap;
                        }
                    }
                    else
                    {
                        child.gameObject.SetActive(false);
                        disabled.Add(child.gameObject);
                    }
                }

                var fgch = bean.GetComponent<FallguyCustomisationHandler>();
                if (fgch != null)
                {
                    var topObj = fgch._currentTopCostume != null ? fgch._currentTopCostume.CostumeObject : null;
                    var bottomObj = fgch._currentBottomCostume != null ? fgch._currentBottomCostume.CostumeObject : null;
                    if (topObj != null && topObj.activeSelf && !protectedClones.Contains(topObj) && !disabled.Contains(topObj))
                    { topObj.SetActive(false); disabled.Add(topObj); }
                    if (bottomObj != null && bottomObj.activeSelf && !protectedClones.Contains(bottomObj) && !disabled.Contains(bottomObj))
                    { bottomObj.SetActive(false); disabled.Add(bottomObj); }
                }
            }

            // local scale is resolved inside PlayerScaleService (single source of truth); pass the skin
            // we're applying so its baked scale is used even before the slot lands in activeSlots. remote
            // beans just take the skin's own baked scale.
            if (isRemote)
            {
                float bakedRemote = (slot.type == SkinType.Costume && slot.skinInfo.skinScale > 0f) ? slot.skinInfo.skinScale : 1f;
                PlayerScaleService.ApplyToBean(bean, bakedRemote, PlayerScaleService.BeanScaleMode.Remote);
            }
            else
                PlayerScaleService.ApplyLocalCostumeScale(bean, slot.type == SkinType.Costume ? slot.skinInfo : null);
            if (reason == ApplyReason.FromMenu)
                AssetManager.SpawnPoof(bean.transform.position + Vector3.up);
            AssetBundleRequest req = slot.bundle.LoadAssetAsync(slot.skinInfo.file);
            yield return req;

            try { _ = bean.name; }
            catch
            {
                pendingKeys.Remove(pendingKey);
                foreach (var go in disabled) { try { go.SetActive(true); } catch { } }
                yield break;
            }

            if (SlotDead(slot))
            {
                RestoreBaseBeanVisibility(pendingKey, new AppliedSkinInfo { disabledChildren = disabled, beanGEO = beanGEO, restoreAllBaseGEO = IsLocalInRoundBean(bean) });
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            if (req.asset == null)
            {
                RestoreBaseBeanVisibility(pendingKey, new AppliedSkinInfo { disabledChildren = disabled, beanGEO = beanGEO, restoreAllBaseGEO = IsLocalInRoundBean(bean) });
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            GameObject prefab = null;
            GameObject clone = null;
            Exception applyEx = null;
            try
            {
                    prefab = req.asset.Cast<GameObject>();
                    clone = GameObject.Instantiate(prefab, beanGEO.position, beanGEO.rotation);
            }
            catch (Exception ex) { applyEx = ex; }

            if (applyEx != null || clone == null)
            {
                Plugin.Log.LogError($"costume instantiate failed: {applyEx?.Message}");
                RestoreBaseBeanVisibility(pendingKey, new AppliedSkinInfo { disabledChildren = disabled, beanGEO = beanGEO, restoreAllBaseGEO = IsLocalInRoundBean(bean) });
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            bool localBindSkin = IsLocalInRoundBean(bean) &&
                                 (slot.skinInfo.boneOffsets == null || slot.skinInfo.boneOffsets.Count == 0);
            if (localBindSkin)
            {
                var mainFg = clone.transform.Find("Main FG");
                if (mainFg != null)
                {
                    var s = mainFg.localScale;
                    float avg = (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f;
                    localBindSkin = avg < 10f;
                    Plugin.Log.LogInfo($"Main FG scale={s}, bind={(localBindSkin ? "yes" : "no")}");
                }
            }

            clone.transform.SetParent(beanGEO, true);
            clone.transform.localScale = Vector3.one;

            if (GameObjectHelper.IsLobbyCharacter(bean)) clone.transform.localScale = Vector3.one;
            if (GameObjectHelper.IsUICharacter(bean) || GameObjectHelper.IsLobbyCharacter(bean))
                GameObjectHelper.SetLayerRecursively(clone, LayerMask.NameToLayer("PlayerUI"));
            yield return SetupBoneSyncCoroutine(clone, bean, slot.skinInfo, slot.bundle).WrapToIl2Cpp();

            if (SlotDead(slot))
            {
                RestoreBaseBeanVisibility(pendingKey, new AppliedSkinInfo { disabledChildren = disabled, beanGEO = beanGEO, restoreAllBaseGEO = IsLocalInRoundBean(bean) });
                if (clone != null) GameObject.Destroy(clone);
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            var sync = clone.GetComponent<BoneSyncComponent>();
            if (localBindSkin && (sync == null || sync.boneOffsets == null || sync.boneOffsets.Count == 0))
                if (BindSkinToLocalFallguy(clone, bean, "Costume") && sync != null)
                    sync.enabled = false;
                SetRenderQueue(clone, 3000);

            if (!slot.skinInfo.keepBase)
            {
                var poller = clone.AddComponent<CostumePollerComponent>();
                poller.beanGEO = beanGEO;
                poller.skinClone = clone;
                poller.isRemote = isRemote;
                poller.keepLocalDonor = keepLocalDonor;
                poller.HideNow();
                // in-game cosmetics worn alongside a UGC costume can spawn their meshes a few
                // hundred ms late — the 1s poll interval means they linger visibly before getting
                // hidden. burst-poll for the first second so they get yanked basically instantly.
                StartCoroutine(BurstPollPoller(poller).WrapToIl2Cpp());
            }

            KillExistingAppliedAtKey(pendingKey);
            appliedSkins[pendingKey] = new AppliedSkinInfo { instance = clone, bean = bean, type = SkinType.Costume, disabledChildren = disabled, beanGEO = beanGEO, restoreAllBaseGEO = IsLocalInRoundBean(bean) };
            pendingKeys.Remove(pendingKey);
            Plugin.Log.LogInfo($"done '{slot.skinInfo.name}' -> {bean.name}");
            OnSkinApplied?.Invoke(new SkinApplyEvent { skinInfo = slot.skinInfo, bean = bean, reason = reason });
            for (int f = 0; f < 5; f++) yield return null;
        }

        // ── Accessory ─────────────────────────────────────────────────────────

        private IEnumerator ApplyAccessoryCoroutine(ActiveSkinSlot slot, GameObject bean, ApplyReason reason = ApplyReason.FromMenu)
        {
            string pendingKey = MakeKey(bean, slot.skinInfo.file);
            if (slot.bundle == null) { pendingKeys.Remove(pendingKey); yield break; }
            if (SlotDead(slot)) { pendingKeys.Remove(pendingKey); yield break; }
            AssetBundleRequest req = slot.bundle.LoadAssetAsync(slot.skinInfo.file);
            yield return req;

            try { _ = bean.name; } catch { pendingKeys.Remove(pendingKey); yield break; }
            if (SlotDead(slot)) { pendingKeys.Remove(pendingKey); yield break; }

            if (req.asset == null) { Plugin.Log.LogError($"asset null for '{slot.skinInfo.file}'"); pendingKeys.Remove(pendingKey); yield break; }

            Transform beanGEO = FindBeanGEO(bean);
            Transform parent = beanGEO ?? bean.transform;

            GameObject clone = null;
            Exception applyEx = null;
            try
            {
                GameObject prefab = req.asset.Cast<GameObject>();
                clone = GameObject.Instantiate(prefab, parent.position, parent.rotation);
            }
            catch (Exception ex) { applyEx = ex; }

            if (applyEx != null || clone == null) { Plugin.Log.LogError($"instantiate failed: {applyEx?.Message}"); pendingKeys.Remove(pendingKey); yield break; }

            bool localBindSkin = IsLocalInRoundBean(bean) &&
                                 (slot.skinInfo.boneOffsets == null || slot.skinInfo.boneOffsets.Count == 0);
            if (localBindSkin)
            {
                var mainFg = clone.transform.Find("Main FG");
                if (mainFg != null)
                {
                    var s = mainFg.localScale;
                    float avg = (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f;
                    localBindSkin = avg < 10f;
                    Plugin.Log.LogInfo($"Main FG scale={s}, bind={(localBindSkin ? "yes" : "no")}");
                }
            }

            clone.transform.SetParent(parent, true);
            clone.transform.localScale = Vector3.one;

            if (GameObjectHelper.IsUICharacter(bean) || GameObjectHelper.IsLobbyCharacter(bean))
                GameObjectHelper.SetLayerRecursively(clone, LayerMask.NameToLayer("PlayerUI"));
            if (GameObjectHelper.IsLobbyCharacter(bean))
                clone.transform.localScale = Vector3.one;

            yield return SetupBoneSyncCoroutine(clone, bean, slot.skinInfo, slot.bundle).WrapToIl2Cpp();
            if (SlotDead(slot))
            {
                if (clone != null) GameObject.Destroy(clone);
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            var sync = clone.GetComponent<BoneSyncComponent>();
            if (localBindSkin && (sync == null || sync.boneOffsets == null || sync.boneOffsets.Count == 0))
                if (BindSkinToLocalFallguy(clone, bean, "Acc") && sync != null)
                    sync.enabled = false;

            SetRenderQueue(clone, 3000);

            KillExistingAppliedAtKey(pendingKey);
            appliedSkins[pendingKey] = new AppliedSkinInfo { instance = clone, bean = bean, type = SkinType.Accessory };
            pendingKeys.Remove(pendingKey);
            Plugin.Log.LogInfo($"done '{slot.skinInfo.name}' -> {bean.name}");
            OnSkinApplied?.Invoke(new SkinApplyEvent { skinInfo = slot.skinInfo, bean = bean, reason = reason });
        }

        // ── Item ──────────────────────────────────────────────────────────────

        private bool BindSkinToLocalFallguy(GameObject clone, GameObject bean, string logname)
        {
            var fgch = bean.GetComponent<FallguyCustomisationHandler>();
            if (fgch == null) return false;

            var fallguyBindposes = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
            var baseSmrs = bean.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int b = 0; b < baseSmrs.Length; b++)
            {
                var baseSmr = baseSmrs[b];
                if (baseSmr == null || baseSmr.sharedMesh == null || baseSmr.bones == null) continue;
                var bindposes = baseSmr.sharedMesh.bindposes;
                if (bindposes == null || bindposes.Length != baseSmr.bones.Length) continue;
                for (int bi = 0; bi < baseSmr.bones.Length; bi++)
                {
                    var bone = baseSmr.bones[bi];
                    if (bone == null) continue;
                    if (!fallguyBindposes.ContainsKey(bone.name)) fallguyBindposes[bone.name] = bindposes[bi];
                    string lower = bone.name.ToLowerInvariant();
                    if (!fallguyBindposes.ContainsKey(lower)) fallguyBindposes[lower] = bindposes[bi];
                }
            }

            var smrs = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int bound = 0;
            int customBoneSmrs = 0;
            for (int i = 0; i < smrs.Length; i++)
            {
                var smr = smrs[i];
                if (smr == null) continue;
                try
                {
                    var oldMesh = smr.sharedMesh;
                    var oldBindposes = oldMesh != null ? oldMesh.bindposes : null;
                    var oldWeights = oldMesh != null ? oldMesh.boneWeights : null;
                    bool hasCustomBones = false;
                    if (smr.bones != null)
                    {
                        for (int bi = 0; bi < smr.bones.Length; bi++)
                        {
                            var bone = smr.bones[bi];
                            if (bone == null) continue;
                            if (!fallguyBindposes.ContainsKey(bone.name) &&
                                !fallguyBindposes.ContainsKey(bone.name.ToLowerInvariant()))
                            {
                                hasCustomBones = true;
                                break;
                            }
                        }
                    }

                    if (hasCustomBones)
                    {
                        customBoneSmrs++;
                        Plugin.Log.LogInfo($"{logname}: leaving {smr.name} on custom bones");
                        continue;
                    }

                    fgch.BindMeshToFallguy(smr);
                    if (smr.sharedMesh != null && smr.bones != null && fallguyBindposes.Count > 0)
                    {
                        var newBindposes = new Matrix4x4[smr.bones.Length];
                        bool allMatched = true;
                        for (int bi = 0; bi < smr.bones.Length; bi++)
                        {
                            var bone = smr.bones[bi];
                            if (bone == null ||
                                (!fallguyBindposes.TryGetValue(bone.name, out newBindposes[bi]) &&
                                 !fallguyBindposes.TryGetValue(bone.name.ToLowerInvariant(), out newBindposes[bi])))
                            {
                                allMatched = false;
                                break;
                            }
                        }

                        if (allMatched)
                        {
                            var mesh = GameObject.Instantiate(smr.sharedMesh);
                            mesh.name = smr.sharedMesh.name + "_fallguybindposes";
                            if (oldMesh != null && oldBindposes != null && oldWeights != null &&
                                oldBindposes.Length == newBindposes.Length &&
                                oldWeights.Length == oldMesh.vertexCount)
                            {
                                var verts = oldMesh.vertices;
                                var normals = oldMesh.normals;
                                for (int v = 0; v < verts.Length; v++)
                                {
                                    var bw = oldWeights[v];
                                    Vector3 p = Vector3.zero;
                                    Vector3 n = Vector3.zero;

                                    if (bw.weight0 > 0f && bw.boneIndex0 < oldBindposes.Length)
                                    {
                                        var m = newBindposes[bw.boneIndex0].inverse * oldBindposes[bw.boneIndex0];
                                        p += m.MultiplyPoint3x4(verts[v]) * bw.weight0;
                                        if (normals != null && normals.Length == verts.Length) n += m.MultiplyVector(normals[v]) * bw.weight0;
                                    }
                                    if (bw.weight1 > 0f && bw.boneIndex1 < oldBindposes.Length)
                                    {
                                        var m = newBindposes[bw.boneIndex1].inverse * oldBindposes[bw.boneIndex1];
                                        p += m.MultiplyPoint3x4(verts[v]) * bw.weight1;
                                        if (normals != null && normals.Length == verts.Length) n += m.MultiplyVector(normals[v]) * bw.weight1;
                                    }
                                    if (bw.weight2 > 0f && bw.boneIndex2 < oldBindposes.Length)
                                    {
                                        var m = newBindposes[bw.boneIndex2].inverse * oldBindposes[bw.boneIndex2];
                                        p += m.MultiplyPoint3x4(verts[v]) * bw.weight2;
                                        if (normals != null && normals.Length == verts.Length) n += m.MultiplyVector(normals[v]) * bw.weight2;
                                    }
                                    if (bw.weight3 > 0f && bw.boneIndex3 < oldBindposes.Length)
                                    {
                                        var m = newBindposes[bw.boneIndex3].inverse * oldBindposes[bw.boneIndex3];
                                        p += m.MultiplyPoint3x4(verts[v]) * bw.weight3;
                                        if (normals != null && normals.Length == verts.Length) n += m.MultiplyVector(normals[v]) * bw.weight3;
                                    }

                                    verts[v] = p;
                                    if (normals != null && normals.Length == verts.Length && n.sqrMagnitude > 0.000001f)
                                        normals[v] = n.normalized;
                                }
                                mesh.vertices = verts;
                                if (normals != null && normals.Length == verts.Length) mesh.normals = normals;
                            }
                            mesh.bindposes = newBindposes;
                            mesh.RecalculateBounds();
                            smr.sharedMesh = mesh;
                        }
                    }
                    bound++;
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"{logname}: BindMeshToFallguy failed on {smr.name}: {ex.Message}"); }
            }

            if (bound > 0)
            {
                Plugin.Log.LogInfo($"{logname}: bound {bound} skin SMRs to local fallguy, customBoneSMRs={customBoneSmrs}");
            }
            return bound > 0 && customBoneSmrs == 0;
        }

        private IEnumerator ApplyItemCoroutine(ActiveSkinSlot slot, GameObject bean, ApplyReason reason = ApplyReason.FromMenu)
        {
            string pendingKey = MakeKey(bean, slot.skinInfo.file);
            if (slot.bundle == null) { pendingKeys.Remove(pendingKey); yield break; }
            if (SlotDead(slot)) { pendingKeys.Remove(pendingKey); yield break; }
            AssetBundleRequest req = slot.bundle.LoadAssetAsync(slot.skinInfo.file);
            yield return req;

            try { _ = bean.name; } catch { pendingKeys.Remove(pendingKey); yield break; }
            if (SlotDead(slot)) { pendingKeys.Remove(pendingKey); yield break; }

            if (req.asset == null)
            {
                Plugin.Log.LogError($"asset '{slot.skinInfo.file}' not in bundle");
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            GameObject prefab = null;
            Exception applyEx = null;
            try { prefab = req.asset.Cast<GameObject>(); }
            catch (Exception ex) { applyEx = ex; }

            if (applyEx != null || prefab == null) { pendingKeys.Remove(pendingKey); yield break; }

            bool hasLeft = slot.skinInfo.left != null;
            bool hasRight = slot.skinInfo.right != null;
            int ov = slot.skinInfo.handOverride;
            bool spawnLeft = hasLeft && (ov == 0 || ov == 1 || ov == 3);
            bool spawnRight = hasRight && (ov == 0 || ov == 2 || ov == 3);

            if (spawnLeft)
                if (!appliedSkins.ContainsKey(MakeKey(bean, slot.skinInfo.file + "_L")))
                    SpawnItemOnHand(prefab, bean, slot.skinInfo, isLeft: true, slot);
            if (spawnRight)
                if (!appliedSkins.ContainsKey(MakeKey(bean, slot.skinInfo.file + "_R")))
                    SpawnItemOnHand(prefab, bean, slot.skinInfo, isLeft: false, slot);

            if (!spawnLeft && !spawnRight && !appliedSkins.ContainsKey(pendingKey))
            {
                GameObject clone = GameObject.Instantiate(prefab, bean.transform.position, bean.transform.rotation);
                clone.transform.SetParent(bean.transform, true);
                clone.transform.localScale = Vector3.one * slot.skinInfo.scale;
                SetRenderQueue(clone, 3000);
                if (SlotDead(slot))
                {
                    GameObject.Destroy(clone);
                    pendingKeys.Remove(pendingKey);
                    yield break;
                }
                KillExistingAppliedAtKey(pendingKey);
                appliedSkins[pendingKey] = new AppliedSkinInfo { instance = clone, bean = bean, type = SkinType.Item };
                OnSkinApplied?.Invoke(new SkinApplyEvent { skinInfo = slot.skinInfo, bean = bean, reason = reason });
            }

            pendingKeys.Remove(pendingKey);
        }

        private void SpawnItemOnHand(GameObject prefab, GameObject bean, SkinInfo skinInfo, bool isLeft, ActiveSkinSlot slot = null)
        {
            if (SlotDead(slot)) return;
            ItemHandInfo handInfo = isLeft ? skinInfo.left : skinInfo.right;
            if (handInfo == null) return;

            string handBoneName = isLeft ? "Wrist_L_jnt" : "Wrist_R_jnt1";
            Transform searchRoot = bean.transform.Find("BetterFG_ScaleWrapper") ?? bean.transform;
            Transform handBone = FindBoneDeep(searchRoot, handBoneName);
            Transform parent = handBone ?? searchRoot;

            GameObject clone = GameObject.Instantiate(prefab, parent.position, parent.rotation);
            clone.transform.SetParent(parent, false);

            string hk = isLeft ? "l" : "r";
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float ox = float.TryParse(SettingsService.Get($"item{hk}offset.{skinInfo.file}.x", "0"), System.Globalization.NumberStyles.Float, ci, out float _ox) ? _ox : 0f;
            float oy = float.TryParse(SettingsService.Get($"item{hk}offset.{skinInfo.file}.y", "0"), System.Globalization.NumberStyles.Float, ci, out float _oy) ? _oy : 0f;
            float oz = float.TryParse(SettingsService.Get($"item{hk}offset.{skinInfo.file}.z", "0"), System.Globalization.NumberStyles.Float, ci, out float _oz) ? _oz : 0f;
            Vector3 basePos = new Vector3(handInfo.position[0], handInfo.position[1], handInfo.position[2]);
            clone.transform.localPosition = basePos + new Vector3(ox, oy, oz);

            float rrx = float.TryParse(SettingsService.Get($"item{hk}rot.{skinInfo.file}.x", "0"), System.Globalization.NumberStyles.Float, ci, out float _rrx) ? _rrx : 0f;
            float rry = float.TryParse(SettingsService.Get($"item{hk}rot.{skinInfo.file}.y", "0"), System.Globalization.NumberStyles.Float, ci, out float _rry) ? _rry : 0f;
            float rrz = float.TryParse(SettingsService.Get($"item{hk}rot.{skinInfo.file}.z", "0"), System.Globalization.NumberStyles.Float, ci, out float _rrz) ? _rrz : 0f;
            Vector3 baseRot = new Vector3(handInfo.rotation[0], handInfo.rotation[1], handInfo.rotation[2]);
            clone.transform.localEulerAngles = baseRot + new Vector3(rrx, rry, rrz);

            clone.transform.localScale = Vector3.one * skinInfo.scale;

            if (GameObjectHelper.IsUICharacter(bean) || GameObjectHelper.IsLobbyCharacter(bean))
                GameObjectHelper.SetLayerRecursively(clone, LayerMask.NameToLayer("PlayerUI"));

            string key = MakeKey(bean, skinInfo.file + (isLeft ? "_L" : "_R"));
            SetRenderQueue(clone, 3000);
            if (SlotDead(slot))
            {
                GameObject.Destroy(clone);
                return;
            }
            KillExistingAppliedAtKey(key);
            appliedSkins[key] = new AppliedSkinInfo { instance = clone, bean = bean, type = SkinType.Item };
            OnSkinApplied?.Invoke(new SkinApplyEvent { skinInfo = skinInfo, bean = bean, reason = ApplyReason.FromMenu });
        }

        private static Transform FindBoneDeep(Transform root, string boneName)
        {
            if (root == null) return null;
            if (root.name == boneName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var result = FindBoneDeep(root.GetChild(i), boneName);
                if (result != null) return result;
            }
            return null;
        }

        // ── BoneSync ──────────────────────────────────────────────────────────

        private IEnumerator SetupBoneSyncCoroutine(GameObject clone, GameObject bean, SkinInfo skinInfo, AssetBundle bundle)
        {
            var sync = clone.AddComponent<BoneSyncComponent>();
            sync.playerObject = bean;
            sync.isRemote = IsRemoteInRoundBean(bean);

            if (skinInfo.boneOffsets != null && skinInfo.boneOffsets.Count > 0)
            {
                sync.SetBoneOffsets(skinInfo.boneOffsets);
                yield break;
            }

            // info.json was already read upstream (catalog/loader) and it had no offsets — don't waste
            // a second github round-trip re-confirming that. this skin just uses its live costume bones.
            if (skinInfo.infoFetched || bundle == null) yield break;

            AssetBundleRequest infoReq = bundle.LoadAssetAsync("info.json");
            yield return infoReq;

            try { _ = clone.name; } catch { yield break; }

            bool bundleHadOffsets = false;
            if (infoReq.asset != null)
            {
                try
                {
                    var textAsset = infoReq.asset.Cast<TextAsset>();
                    if (textAsset != null)
                    {
                        var infoJson = JsonUtility.FromJson<InfoJson>(textAsset.text);
                        if (infoJson?.boneOffsets != null && infoJson.boneOffsets.Length > 0)
                        {
                            sync.SetBoneOffsets(new List<BoneOffsetEntry>(infoJson.boneOffsets));
                            bundleHadOffsets = true;
                        }
                    }
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"bad info.json in '{skinInfo.file}': {ex.Message}"); }
            }

            if (!bundleHadOffsets && !skinInfo.isLocalImport)
                yield return FetchAndAssignOffsets(clone, skinInfo).WrapToIl2Cpp();
        }

        private IEnumerator FetchAndAssignOffsets(GameObject clone, SkinInfo skinInfo)
        {
            // the skinInfo that lands here is often a stub (restore-from-settings, active-slot re-apply
            // onto a freshly-spawned bean like PB_UI_Character) with no boneOffsets and no repoFolder.
            // the catalog already fetched + parsed every skin's info.json, offsets included, so pull the
            // real folder (and offsets outright) from there by file before falling back to a guessed URL.
            var catalog = CustomizationServices.CatalogService;
            if (catalog != null)
            {
                foreach (var cat in catalog.AvailableSkins)
                {
                    if (cat.file != skinInfo.file) continue;
                    if (!string.IsNullOrEmpty(cat.repoFolder)) skinInfo.repoFolder = cat.repoFolder;
                    if (cat.boneOffsets != null && cat.boneOffsets.Count > 0)
                    {
                        var syncCat = clone.GetComponent<BoneSyncComponent>();
                        if (syncCat != null) syncCat.SetBoneOffsets(cat.boneOffsets);
                        Plugin.Log.LogInfo($"{cat.boneOffsets.Count} offsets recovered from catalog for '{skinInfo.file}'");
                        yield break;
                    }
                    break;
                }
            }

            // real layout is <repo>/Costumes/<folder>/info.json — the folder is NOT the file name, so a
            // catalog miss with no repoFolder can't build a valid URL. bail loudly instead of 404ing silent.
            if (string.IsNullOrEmpty(skinInfo.repoFolder))
            {
                Plugin.Log.LogWarning($"no repoFolder for '{skinInfo.file}' and not in catalog - can't fetch offsets, skin will use live costume bones only");
                yield break;
            }

            string url = $"{GetRepoRaw(skinInfo)}/{skinInfo.repoFolder}/info.json";
            Plugin.Log.LogInfo($"fetching offsets: {url}");

            UnityWebRequest www = UnityWebRequest.Get(url);
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Plugin.Log.LogWarning($"offset fetch failed ({www.result}) for {url}");
                www.Dispose();
                yield break;
            }
            string json = www.downloadHandler.text;
            www.Dispose();

            try { _ = clone.name; } catch { yield break; }
            if (string.IsNullOrEmpty(json)) yield break;

            var offsets = ParseBoneOffsetsFromJson(json);
            if (offsets == null || offsets.Count == 0)
            {
                Plugin.Log.LogInfo($"info.json at {url} had no offsets");
                yield break;
            }

            var sync = clone.GetComponent<BoneSyncComponent>();
            if (sync != null) sync.SetBoneOffsets(offsets);
            Plugin.Log.LogInfo($"{offsets.Count} offsets fetched for '{skinInfo.file}'");
        }

        private static List<BoneOffsetEntry> ParseBoneOffsetsFromJson(string json)
        {
            var offsets = new List<BoneOffsetEntry>();
            foreach (string obj in JsonUtil.GetArray(json, "boneOffsets"))
            {
                string bone = JsonUtil.GetValue(obj, "bone");
                if (string.IsNullOrEmpty(bone)) continue;
                offsets.Add(new BoneOffsetEntry
                {
                    bone = bone,
                    localPosition = new Vector3(JsonUtil.GetFloat(obj, "x"), JsonUtil.GetFloat(obj, "y"), JsonUtil.GetFloat(obj, "z")),
                });
            }
            return offsets;
        }
    }
}
