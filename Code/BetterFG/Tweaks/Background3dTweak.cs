using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FGClient;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterFG.Tweaks
{
    public class Background3dTweak : BfgTweak
    {
        public Background3dTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "background_3d";
        public override string TweakLabel => "2D To 3D Background";
        public override bool DefaultEnabled => false;

        public static Background3dTweak Instance { get; private set; }

        private static GameObject _spawnedTerrain;
        private static bool _busy;

        void Awake() => Instance = this;

        public override void EnableTweak()
        {
            if (!_busy) StartCoroutine(Apply().WrapToIl2Cpp());
        }

        public override void DisableTweak()
        {
            if (_spawnedTerrain != null)
            {
                Destroy(_spawnedTerrain);
                _spawnedTerrain = null;
            }
        }

        internal static void ApplyIfWanted()
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled) return;
            if (!_busy) inst.StartCoroutine(inst.Apply().WrapToIl2Cpp());
        }

        private System.Collections.IEnumerator Apply()
        {
            _busy = true;

            int sceneCount = SceneManager.sceneCount;
            for (int si = 0; si < sceneCount; si++)
            {
                var gameplayScene = SceneManager.GetSceneAt(si);
                if (!gameplayScene.isLoaded) continue;

                foreach (var root in gameplayScene.GetRootGameObjects())
                {
                    if (root == null || !root.activeInHierarchy || !root.name.StartsWith("Background_")) continue;

                    string bundleName = null;
                    string prefabName = null;
                    Vector3 spawnPosition = Vector3.zero;

                    switch (root.name)
                    {
                        case "Background_Vanilla_Sunny_GroundNoLake(Clone)":
                            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                            {
                                if (t == null || t.name != "CutoutSphere") continue;
                                t.gameObject.SetActive(false);
                                break;
                            }
                            bundleName = "23b2b3e76cd0c7d9f363ddc075be5755.bundle";
                            prefabName = "PB_FallGuy_BackGround_Terrain";
                            spawnPosition = new Vector3(0f, 136f, 0f);
                            break;
                        case "Background_Medieval_Dusk_GroundNoLake(Clone)":
                            bundleName = "74696ab4dbdf29bbe8804025b4eca82c.bundle";
                            prefabName = "PB_FallGuy_BackGround_Terrain_S02";
                            spawnPosition = new Vector3(0f, -70f, 0f);
                            break;
                    }

                    if (string.IsNullOrEmpty(bundleName) || string.IsNullOrEmpty(prefabName)) continue;
                    if (_spawnedTerrain != null) continue;

                    string path = Path.Combine(Application.streamingAssetsPath, "aa", "StandaloneWindows64", bundleName);
                    if (!File.Exists(path))
                        path = Path.Combine(Application.streamingAssetsPath, "aa", bundleName);
                    if (!File.Exists(path))
                    {
                        Plugin.Log?.LogWarning("[Background3dTweak] bundle missing: " + path);
                        continue;
                    }

                    AssetBundle bundle = null;
                    try { bundle = AssetBundle.LoadFromFile(path); }
                    catch (Exception ex) { Plugin.Log?.LogWarning("[Background3dTweak] bundle load failed: " + ex.Message); }
                    if (bundle == null) continue;

                    string scenePath = null;
                    try
                    {
                        var scenes = bundle.GetAllScenePaths();
                        if (scenes != null && scenes.Length > 0) scenePath = scenes[0];
                    }
                    catch { }
                    if (string.IsNullOrEmpty(scenePath))
                    {
                        Plugin.Log?.LogWarning("[Background3dTweak] no scene in bundle " + bundleName);
                        continue;
                    }

                    // The gameplay scene can share this scene's name, so snapshot existing scene
                    // handles and pick out the one that gets added by the additive load.
                    var existing = new HashSet<int>();
                    int before = SceneManager.sceneCount;
                    for (int x = 0; x < before; x++)
                        existing.Add(SceneManager.GetSceneAt(x).handle);

                    var load = SceneManager.LoadSceneAsync(scenePath, LoadSceneMode.Additive);
                    if (load != null) yield return load;

                    Scene addedScene = default;
                    int after = SceneManager.sceneCount;
                    for (int x = 0; x < after; x++)
                    {
                        var s = SceneManager.GetSceneAt(x);
                        if (!existing.Contains(s.handle)) { addedScene = s; break; }
                    }

                    if (!addedScene.IsValid() || !addedScene.isLoaded)
                    {
                        Plugin.Log?.LogWarning("[Background3dTweak] additive scene not found for " + bundleName);
                        continue;
                    }

                    // Find the terrain GameObject inside the additive scene and clone it while it's
                    // still active so it instantiates active (this is what keeps lighting correct).
                    GameObject source = FindInScene(addedScene, prefabName);
                    if (source != null)
                    {
                        _spawnedTerrain = Instantiate(source);
                        _spawnedTerrain.name = prefabName;
                        foreach (var t in _spawnedTerrain.GetComponentsInChildren<Transform>(true))
                            if (t != null) t.position += spawnPosition;
                        SceneManager.MoveGameObjectToScene(_spawnedTerrain, gameplayScene);
                        _spawnedTerrain.SetActive(true);
                        Plugin.Log?.LogInfo("[Background3dTweak] spawned " + prefabName);
                    }
                    else
                    {
                        Plugin.Log?.LogWarning("[Background3dTweak] " + prefabName + " not found in scene");
                    }

                    // Wait a moment, then disable every root in the additive scene. Something in the
                    // scene re-activates roots on load, so disabling immediately doesn't stick — the
                    // delay lets that settle first. The scene stays loaded for its baked lighting.
                    int targetHandle = addedScene.handle;
                    yield return new WaitForSeconds(1f);

                    Scene target = default;
                    int sc = SceneManager.sceneCount;
                    for (int x = 0; x < sc; x++)
                    {
                        var s = SceneManager.GetSceneAt(x);
                        if (s.handle == targetHandle) { target = s; break; }
                    }

                    if (target.IsValid() && target.isLoaded)
                    {
                        var roots = target.GetRootGameObjects();
                        int disabled = 0;
                        for (int r = 0; r < roots.Length; r++)
                        {
                            var sceneRoot = roots[r];
                            if (sceneRoot == null) continue;
                            sceneRoot.SetActive(false);
                            disabled++;
                        }
                        Plugin.Log?.LogInfo("[Background3dTweak] disabled " + disabled + " roots in additive scene '" + target.name + "' (handle " + targetHandle + ")");
                    }
                }
            }

            _busy = false;
        }

        private static GameObject FindInScene(Scene scene, string name)
        {
            foreach (var sceneRoot in scene.GetRootGameObjects())
            {
                if (sceneRoot == null) continue;
                foreach (var t in sceneRoot.GetComponentsInChildren<Transform>(true))
                {
                    if (t != null && t.name == name) return t.gameObject;
                }
            }
            return null;
        }
    }

    // 2D To 3D Background — disabled until custom bundles are available.
    // [HarmonyPatch(typeof(RoundLoader), nameof(RoundLoader.NotifyLoadingFinished))]
    public class Background3dNotifyLoadingFinishedPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            Background3dTweak.ApplyIfWanted();
        }
    }
}
