using System.IO;
using UnityEditor;
using UnityEngine;

namespace BetterFG.Editor.Reference
{
    public static class ReferenceSpawner
    {
        const string ITEM_PKG_RESOURCE = "BetterFG.Creator.Reference.Files.itemdummy.unitypackage";
        const string ITEM_PREFAB_PATH = "Assets/assets/reference/Dummy.prefab";

        const string ANIM_PKG_RESOURCE = "BetterFG.Creator.Reference.Files.animationbean.unitypackage";
        const string ANIM_PREFAB_PATH = "Assets/assets/reference/animationbean/Animation Bean.prefab";

        // remembers which prefab to spawn after the (async) package import finishes
        static string _pendingPrefabPath;
        static string _pendingLabel;

        [MenuItem("BettrFG/References/Item Dummy")]
        static void SpawnItemDummy() => SpawnFromPackage(ITEM_PREFAB_PATH, ITEM_PKG_RESOURCE, "itemdummy", "Item Dummy");

        [MenuItem("BettrFG/References/Animation Dummy")]
        static void SpawnAnimationDummy() => SpawnFromPackage(ANIM_PREFAB_PATH, ANIM_PKG_RESOURCE, "animationbean", "Animation Dummy");

        static void SpawnFromPackage(string prefabPath, string resource, string tmpName, string label)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null) { Spawn(prefab, label); return; }

            string tmp = Path.Combine(Path.GetTempPath(), tmpName + ".unitypackage");
            using (var stream = typeof(ReferenceSpawner).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null)
                {
                    Debug.LogError("ReferenceSpawner: embedded package not found - " + resource);
                    return;
                }
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                    stream.CopyTo(fs);
            }

            _pendingPrefabPath = prefabPath;
            _pendingLabel = label;
            AssetDatabase.importPackageCompleted += OnImported;
            AssetDatabase.importPackageFailed += OnFailed;
            AssetDatabase.ImportPackage(tmp, true);
        }

        static void OnImported(string packageName)
        {
            AssetDatabase.importPackageCompleted -= OnImported;
            AssetDatabase.importPackageFailed -= OnFailed;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(_pendingPrefabPath);
            if (prefab == null)
            {
                Debug.LogError("ReferenceSpawner: imported ok but prefab not found at " + _pendingPrefabPath);
                return;
            }

            Spawn(prefab, _pendingLabel);
        }

        static void OnFailed(string packageName, string errorMessage)
        {
            AssetDatabase.importPackageCompleted -= OnImported;
            AssetDatabase.importPackageFailed -= OnFailed;
            Debug.LogError("ReferenceSpawner: import failed - " + errorMessage);
        }

        static void Spawn(GameObject prefab, string label)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            Undo.RegisterCreatedObjectUndo(go, "Spawn " + label);
            Selection.activeGameObject = go;
            SceneView.FrameLastActiveSceneView();
            Debug.Log("ReferenceSpawner: " + label + " spawned");
        }
    }
}
