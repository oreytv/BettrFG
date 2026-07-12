using BetterFG.Editor.Map;
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using EditorMessageType = UnityEditor.MessageType;

namespace BetterFG.Editor
{
    public class MapPackagerWindow : EditorWindow
    {
        [MenuItem("BettrFG/Round/Map Packager")]
        public static void Open() => GetWindow<MapPackagerWindow>("Map Packager");

        private const string PROJECT_OUTPUT_DIR = "Assets/Maps/Output";
        private const string PREF_LAST_CATALOG_DIR = "BettrFG.MapPackager.LastCatalogDir";

        private string _repoRoot = "";
        private BuildTarget _buildTarget = BuildTarget.StandaloneWindows64;
        private string _statusMsg = "";
        private EditorMessageType _statusType = EditorMessageType.None;
        private Vector2 _scroll;

        private GUIStyle _sectionStyle;
        private bool _stylesReady;

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _sectionStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(8, 8, 6, 6) };
            _stylesReady = true;
        }

        private void OnGUI()
        {
            EnsureStyles();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            // everything comes from the open scene — no drag, no typing
            var scene = SceneManager.GetActiveScene();
            string prefabName = scene.IsValid() ? scene.name : "";
            var map = FindMapAssetFor(prefabName);

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("Source", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Packs the currently open round scene. Open the scene you made with Map Creator,\n" +
                "then hit Pack Map.",
                EditorMessageType.Info);

            GUILayout.Space(2);
            EditorGUILayout.LabelField("Scene", string.IsNullOrEmpty(prefabName) ? "(no scene open)" : prefabName);
            if (map != null)
            {
                EditorGUILayout.LabelField("Display Name", map.displayName);
                EditorGUILayout.LabelField("Skybox", RenderSettings.skybox != null ? RenderSettings.skybox.name : "(none)");
            }
            else
            {
                EditorGUILayout.HelpBox("No map asset for this scene. Make it with Map Creator.", EditorMessageType.Warning);
            }
            GUILayout.EndVertical();
            GUILayout.Space(4);

            // build settings
            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("Build", EditorStyles.boldLabel);
            _buildTarget = (BuildTarget)EditorGUILayout.EnumPopup("Platform", _buildTarget);
            GUILayout.EndVertical();
            GUILayout.Space(4);

            // output
            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("Output", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Select the rounds repo's generate_catalog.bat. The round is placed in\n" +
                "Rounds/round_x/ automatically and the catalog is regenerated after packing.",
                EditorMessageType.Info);
            GUILayout.Space(2);

            if (string.IsNullOrEmpty(_repoRoot))
                _repoRoot = EditorPrefs.GetString(PREF_LAST_CATALOG_DIR, "");

            GUILayout.BeginHorizontal();
            _repoRoot = EditorGUILayout.TextField("Repo Folder", _repoRoot);
            if (GUILayout.Button("generate_catalog.bat...", GUILayout.Width(160)))
            {
                string seed = !string.IsNullOrEmpty(_repoRoot) ? _repoRoot : EditorPrefs.GetString(PREF_LAST_CATALOG_DIR, "");
                string picked = EditorUtility.OpenFilePanel("Select generate_catalog.bat", seed, "bat");
                if (!string.IsNullOrEmpty(picked))
                {
                    _repoRoot = Path.GetDirectoryName(picked);
                    EditorPrefs.SetString(PREF_LAST_CATALOG_DIR, _repoRoot);
                }
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(_repoRoot) && !string.IsNullOrEmpty(prefabName))
                EditorGUILayout.LabelField("Will pack to", Path.Combine(_repoRoot, "Rounds", prefabName), EditorStyles.miniLabel);

            GUILayout.Space(6);
            GUI.backgroundColor = new Color(0.25f, 0.7f, 0.35f);
            bool canPack = map != null && !string.IsNullOrWhiteSpace(_repoRoot);
            GUI.enabled = canPack;
            if (GUILayout.Button("Pack Map", GUILayout.Height(28))) TryPack(scene, map);
            GUI.enabled = true;
            GUI.backgroundColor = Color.white;
            GUILayout.EndVertical();

            if (!string.IsNullOrEmpty(_statusMsg))
            {
                GUILayout.Space(4);
                EditorGUILayout.HelpBox(_statusMsg, _statusType);
            }

            GUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        // the map asset lives next to its scene at Assets/Maps/round_x/round_x.asset
        private static BetterFGMapAsset FindMapAssetFor(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;
            string soPath = $"Assets/Maps/{prefabName}/{prefabName}.asset";
            return AssetDatabase.LoadAssetAtPath<BetterFGMapAsset>(soPath);
        }

        private void TryPack(Scene scene, BetterFGMapAsset map)
        {
            _statusMsg = "";

            if (map == null) { Err("No map asset for this scene."); return; }
            if (string.IsNullOrWhiteSpace(_repoRoot) || !Directory.Exists(_repoRoot)) { Err("Select the rounds repo's generate_catalog.bat first."); return; }

            string prefabName = scene.name;
            string bundleName = prefabName.ToLower();

            // keep the (hidden) asset name in sync with the scene so info.json matches the bundle
            if (map.prefabName != prefabName)
            {
                map.prefabName = prefabName;
                EditorUtility.SetDirty(map);
            }
            string mapDir = $"Assets/Maps/{prefabName}";
            string assetPath = $"{mapDir}/{prefabName}.prefab";

            // make sure the scene's latest edits are on disk before we snapshot it to a prefab
            if (scene.isDirty) EditorSceneManager.SaveScene(scene);

            // grab the round root out of the scene and (re)save it as the prefab we bundle
            GameObject sceneGo = FindRootInScene(scene, prefabName);
            if (sceneGo == null) { Err($"'{prefabName}' root not found in the open scene."); return; }

            if (!AssetDatabase.IsValidFolder(mapDir))
                AssetDatabase.CreateFolder("Assets/Maps", prefabName);
            PrefabUtility.SaveAsPrefabAsset(sceneGo, assetPath);
            AssetDatabase.Refresh();

            try
            {
                var importer = AssetImporter.GetAtPath(assetPath);
                if (importer == null) { Err("Couldn't get asset importer."); return; }

                string prevBundle = importer.assetBundleName;
                importer.assetBundleName = bundleName;
                importer.SaveAndReimport();

                var assetNames = new System.Collections.Generic.List<string> { assetPath };

                // bundle the scene's skybox material (from RenderSettings, set in the Lighting window)
                var skybox = RenderSettings.skybox;
                if (skybox != null)
                {
                    string skyboxPath = AssetDatabase.GetAssetPath(skybox);
                    if (!string.IsNullOrEmpty(skyboxPath))
                    {
                        assetNames.Add(skyboxPath);
                        Debug.Log($"[MapPackager] including skybox '{skybox.name}' from {skyboxPath}");
                    }
                    else Debug.LogWarning($"[MapPackager] skybox '{skybox.name}' isn't a project asset, can't bundle it");
                }

                var builds = new AssetBundleBuild[]
                {
                    new AssetBundleBuild
                    {
                        assetBundleName = bundleName,
                        assetNames      = assetNames.ToArray()
                    }
                };

                string tmpOut = Path.Combine(Application.temporaryCachePath, "bfgmapbuild");
                Directory.CreateDirectory(tmpOut);

                var manifest = BuildPipeline.BuildAssetBundles(tmpOut, builds, BuildAssetBundleOptions.ChunkBasedCompression, _buildTarget);

                importer.assetBundleName = prevBundle;
                importer.SaveAndReimport();
                AssetDatabase.Refresh();

                if (manifest == null) { Err("Bundle build failed. Check console."); return; }

                string builtBundle = Path.Combine(tmpOut, bundleName);
                if (!File.Exists(builtBundle)) { Err($"Built bundle not found at {builtBundle}"); return; }

                // write into the rounds repo (root/Rounds/round_x/) AND the project's Output folder
                string repoOut = Path.Combine(_repoRoot, "Rounds", prefabName);
                if (!AssetDatabase.IsValidFolder(PROJECT_OUTPUT_DIR))
                    AssetDatabase.CreateFolder("Assets/Maps", "Output");
                string projectOut = Path.GetFullPath(PROJECT_OUTPUT_DIR);

                foreach (string dir in new[] { repoOut, projectOut })
                {
                    Directory.CreateDirectory(dir);
                    File.Copy(builtBundle, Path.Combine(dir, bundleName), overwrite: true);
                    MapInfoJson.Write(dir, map);
                    CopyMusic(map, dir);
                }
                AssetDatabase.Refresh();

                RunCatalogBat();

                Ok($"Packed -> {repoOut}  (+ {PROJECT_OUTPUT_DIR})");
            }
            catch (Exception ex) { Err($"Pack failed: {ex.Message}"); }
        }

        private void RunCatalogBat()
        {
            string bat = Path.Combine(_repoRoot, "generate_catalog.bat");
            if (!File.Exists(bat)) return;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = bat,
                WorkingDirectory = _repoRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (var p = System.Diagnostics.Process.Start(psi))
                p.WaitForExit();
        }

        // find the round root by name in the scene, falling back to the first root object
        private static GameObject FindRootInScene(Scene scene, string prefabName)
        {
            var roots = scene.GetRootGameObjects();
            foreach (var r in roots)
                if (r.name == prefabName) return r;
            return roots.Length > 0 ? roots[0] : null;
        }

        // copy the chosen mp3 into the round folder (its name is written to info.json's "music")
        private static void CopyMusic(BetterFGMapAsset map, string dir)
        {
            if (string.IsNullOrEmpty(map.musicFilePath)) return;
            if (!File.Exists(map.musicFilePath)) { Debug.LogWarning($"[MapPackager] music file not found: {map.musicFilePath}"); return; }
            File.Copy(map.musicFilePath, Path.Combine(dir, Path.GetFileName(map.musicFilePath)), overwrite: true);
        }

        private void Ok(string msg) { _statusMsg = msg; _statusType = EditorMessageType.Info; Repaint(); }
        private void Err(string msg) { _statusMsg = msg; _statusType = EditorMessageType.Error; Repaint(); }
    }
}
