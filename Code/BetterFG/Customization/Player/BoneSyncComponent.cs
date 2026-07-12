using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using System;

namespace BetterFG.Customization.Player
{
    public class BoneSyncComponent : MonoBehaviour
    {
        public GameObject playerObject;
        public bool isRemote = false;
        public List<BoneOffsetEntry> boneOffsets = new List<BoneOffsetEntry>();

        private Dictionary<string, Vector3> boneOffsetMap = new Dictionary<string, Vector3>(StringComparer.Ordinal);
        private HashSet<string> loggedAppliedBones = new HashSet<string>();

        private Transform playerRoot;
        private Transform customSkinRoot;
        private readonly List<bonepair> cachedBones = new List<bonepair>();
        private int lastSyncFrame = -1;
    
        // When true, prefer mapping custom bones to player bones using the
        // SkinnedMeshRenderer.bones array from the custom skin's Body_LOD0.
        // This is only used for the local player when no explicit offsets exist.
        private bool useSmrBoneMapping = false;
        private SkinnedMeshRenderer customBodySmr;

        private Dictionary<string, Transform> _playerBonesByName = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private Transform _playerTorsoBone;
        private Transform _customTorsoBone;

        void Start()
        {
            RebuildOffsetMap();
            SetupBoneReferences();
        }

        public void SetBoneOffsets(List<BoneOffsetEntry> offsets)
        {
            boneOffsets = offsets ?? new List<BoneOffsetEntry>();
            RebuildOffsetMap();
            useSmrBoneMapping = false;
            customBodySmr = null;

            RebuildCachedBones();
            Debug.Log($"[BoneSync] {boneOffsetMap.Count} offsets registered on '{name}'");
        }

        public void SyncNow()
        {
            int frame = Time.frameCount;
            if (lastSyncFrame == frame) return;
            lastSyncFrame = frame;

            if (cachedBones.Count == 0) return;
            for (int i = 0; i < cachedBones.Count; i++)
            {
                var pair = cachedBones[i];
                if (pair.customBone == null || pair.playerBone == null) continue;

                pair.customBone.rotation = pair.playerBone.rotation;
                if (pair.hasOffset)
                    pair.customBone.localPosition = pair.offset;
                else
                    // world space direct copy -- local space math breaks when custom skin is scaled (e.g. 100x from blender)
                    pair.customBone.position = pair.playerBone.position;
            }

            ForceScaledTorsoLocalPos();
        }

        private void RebuildOffsetMap()
        {
            boneOffsetMap.Clear();
            loggedAppliedBones.Clear();
            if (boneOffsets == null) return;
            foreach (var entry in boneOffsets)
            {
                if (string.IsNullOrEmpty(entry.bone)) continue;
                string key = entry.bone.Trim();
                if (!boneOffsetMap.ContainsKey(key))
                    boneOffsetMap[key] = entry.localPosition;
                string lower = key.ToLowerInvariant();
                if (!boneOffsetMap.ContainsKey(lower))
                    boneOffsetMap[lower] = entry.localPosition;
            }
        }

        void LateUpdate()
        {
            if (playerObject == null || !playerObject.activeInHierarchy) return;
            SyncNow();
        }

        void Update()
        {
            if (isRemote) return;
            if (playerObject == null || !playerObject.activeInHierarchy)
                Destroy(gameObject);
        }

        private void SetupBoneReferences()
        {
            foreach (var skinned in playerObject.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.bones == null || skinned.bones.Length == 0) continue;
                playerRoot = skinned.bones.FirstOrDefault(b => b != null && b.name == "Root");

                foreach (var b in skinned.bones)
                {
                    if (b == null) continue;
                    if (!_playerBonesByName.ContainsKey(b.name))
                        _playerBonesByName[b.name] = b;
                    string lower = b.name.ToLowerInvariant();
                    if (!_playerBonesByName.ContainsKey(lower))
                        _playerBonesByName[lower] = b;
                }

                if (playerRoot != null) break;
            }

            if (playerRoot == null)
            {
                string[] paths = {
                    "Character/SKELETON/Root", "Character/Armature/Root",
                    "SKELETON/Root", "Armature/Root", "Character/GEO/Body_LOD0/Root"
                };
                foreach (string path in paths)
                {
                    playerRoot = playerObject.transform.Find(path);

                    if (playerRoot != null) break;
                }
            }

            if (playerRoot == null)
                playerRoot = playerObject.GetComponentsInChildren<Transform>(true)
                                         .FirstOrDefault(t => t.name == "Root");

            _playerTorsoBone = FindBoneDeep(playerObject.transform, "Torso_C_jnt_NoStrechSquash");

            customSkinRoot = transform.Find("Main FG/Body_LOD0 (merge)");
            if (customSkinRoot == null)
            {
                string[] customPaths = { "Body_LOD0 (merge)", "Main FG/Body_LOD0", "Body_LOD0" };
                foreach (string path in customPaths)
                {
                    customSkinRoot = transform.Find(path);
                    if (customSkinRoot != null) break;
                }
            }

            _customTorsoBone = FindBoneDeep(transform, "Torso_C_jnt_NoStrechSquash");

            // ensure the top-level custom skin root name maps to the player's Root transform
            if (playerRoot != null)
            {
                void AddMap(string name)
                {
                    if (string.IsNullOrEmpty(name)) return;
                    if (!_playerBonesByName.ContainsKey(name)) _playerBonesByName[name] = playerRoot;
                    string lower = name.ToLowerInvariant();
                    if (!_playerBonesByName.ContainsKey(lower)) _playerBonesByName[lower] = playerRoot;
                }

                AddMap(playerRoot.name);
                AddMap("Body_LOD0 (merge)");
                AddMap("Body_LOD0");
                AddMap("Main FG/Body_LOD0 (merge)");
                Debug.Log($"[BoneSync] added remote root mappings for {playerObject.name} -> {playerRoot.name} (playerBones={_playerBonesByName.Count})");
            }

            useSmrBoneMapping = false;
            customBodySmr = null;

            RebuildCachedBones();

            if (playerRoot == null || customSkinRoot == null)
                Debug.LogWarning($"[BoneSync] setup failed on {playerObject.name} - playerRoot={(playerRoot != null ? playerRoot.name : "null")} customRoot={(customSkinRoot != null ? customSkinRoot.name : "null")}");
            else
                Debug.Log($"[BoneSync] ready on {playerObject.name} - playerRoot={playerRoot.name} customRoot={customSkinRoot.name} playerBones={_playerBonesByName.Count} cached={cachedBones.Count}");
        }

        private void RebuildCachedBones()
        {
            cachedBones.Clear();
            if (customSkinRoot == null) return;

            if (useSmrBoneMapping && customBodySmr != null)
            {
                BuildCachedBonesFromSmr(customBodySmr);
                return;
            }

            CollectCachedBones(customSkinRoot);
        }

        private void BuildCachedBonesFromSmr(SkinnedMeshRenderer smr)
        {
            if (smr == null || smr.bones == null) return;
            int added = 0;
            for (int i = 0; i < smr.bones.Length; i++)
            {
                var customBone = smr.bones[i];
                if (customBone == null) continue;

                Transform playerBone = null;
                if (!_playerBonesByName.TryGetValue(customBone.name, out playerBone) || playerBone == null)
                    _playerBonesByName.TryGetValue(customBone.name.ToLowerInvariant(), out playerBone);

                if (playerBone != null)
                {
                    cachedBones.Add(new bonepair
                    {
                        customBone = customBone,
                        playerBone = playerBone,
                        hasOffset = false,
                        offset = Vector3.zero
                    });
                    added++;
                }
            }

            if (added > 0)
                Debug.Log($"[BoneSync] SMR-based mapping created with {added} bones for '{name}'");
            else
                Debug.LogWarning($"[BoneSync] SMR-based mapping found 0 matches for '{name}'");
        }

        private void CollectCachedBones(Transform customBone)
        {
            if (customBone == null) return;

            Transform playerBone = null;
            if (!_playerBonesByName.TryGetValue(customBone.name, out playerBone) || playerBone == null)
                _playerBonesByName.TryGetValue(customBone.name.ToLowerInvariant(), out playerBone);

            if (playerBone != null)
            {
                bool hasOffset = boneOffsetMap.TryGetValue(customBone.name, out Vector3 offset)
                              || boneOffsetMap.TryGetValue(customBone.name.Trim().ToLowerInvariant(), out offset);

                cachedBones.Add(new bonepair
                {
                    customBone = customBone,
                    playerBone = playerBone,
                    hasOffset = hasOffset,
                    offset = offset
                });

                if (hasOffset && !loggedAppliedBones.Contains(customBone.name))
                {
                    loggedAppliedBones.Add(customBone.name);
                    Debug.Log($"[BoneSync] offset on '{customBone.name}': ({offset.x:F3}, {offset.y:F3}, {offset.z:F3})");
                }
            }

            for (int i = 0; i < customBone.childCount; i++)
                CollectCachedBones(customBone.GetChild(i));
        }

        private void ForceScaledTorsoLocalPos()
        {
            if (_playerTorsoBone == null || _customTorsoBone == null) return;
            _customTorsoBone.localPosition = _playerTorsoBone.localPosition * 0.01f;
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

        private class bonepair
        {
            public Transform customBone;
            public Transform playerBone;
            public bool hasOffset;
            public Vector3 offset;
        }
    }
}
