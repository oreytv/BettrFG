using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterFG.Editor
{
    public class MapCreatorWindow : EditorWindow
    {
        [MenuItem("BettrFG/Round/Map Creator")]
        public static void Open() => GetWindow<MapCreatorWindow>("Map Creator");

        private const string TEMPLATE_PATH = "Assets/assets/reference/round_template/round_template.prefab";
        private const string DEFAULT_SKYBOX_PATH = "Assets/assets/reference/default_skybox.mat";
        private const string MAPS_DIR = "Assets/Maps";
        private const string OUTPUT_DIR = "Assets/Maps/Output";
        private const string PACKAGE_NAME = "roundtemplate.unitypackage";
        private const string PKG_RESOURCE = "BetterFG.Creator.Reference.Files.roundtemplate.unitypackage";

        private string _levelName = "";
        private string _statusMsg = "";
        private UnityEditor.MessageType _statusType = UnityEditor.MessageType.None;

        private GUIStyle _sectionStyle;
        private GUIStyle _warnStyle;
        private bool _stylesReady;

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _sectionStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(8, 8, 6, 6) };
            _warnStyle = new GUIStyle(EditorStyles.helpBox)
            {
                normal = { textColor = new Color(1f, 0.75f, 0.1f) },
                fontStyle = FontStyle.Bold,
                fontSize = 11,
                padding = new RectOffset(8, 8, 6, 6),
                wordWrap = true,
            };
            _stylesReady = true;
        }

        private void OnGUI()
        {
            EnsureStyles();

            bool templateReady = AssetDatabase.LoadAssetAtPath<GameObject>(TEMPLATE_PATH) != null;

            if (!templateReady)
            {
                GUILayout.BeginVertical(_sectionStyle);
                GUILayout.Label("Setup Required", EditorStyles.boldLabel);
                GUILayout.Label(
                    "round_template.unitypackage hasn't been imported yet.\n" +
                    "Click below to import it... you wont have to do it again",
                    _warnStyle);
                GUILayout.Space(4);

                GUI.backgroundColor = new Color(0.9f, 0.6f, 0.15f);
                if (GUILayout.Button("Import round_template.unitypackage", GUILayout.Height(28)))
                    TryImportPackage();
                GUI.backgroundColor = Color.white;
                GUILayout.EndVertical();
                GUILayout.Space(4);
            }

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("New Map", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Creates a new scene round_(levelname) in the map's folder, drops a detached copy\n" +
                "of the round template into it, and creates a BetterFGMapAsset.",
                UnityEditor.MessageType.Info);
            GUILayout.Space(4);

            GUI.enabled = templateReady;
            _levelName = EditorGUILayout.TextField("Level Name", _levelName);
            GUILayout.Space(6);

            GUI.backgroundColor = new Color(0.25f, 0.7f, 0.35f);
            if (GUILayout.Button("Create Map", GUILayout.Height(28))) TryCreate();
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;
            GUILayout.EndVertical();

            if (!string.IsNullOrEmpty(_statusMsg))
            {
                GUILayout.Space(4);
                EditorGUILayout.HelpBox(_statusMsg, _statusType);
            }

            GUILayout.Space(8);
        }

        private void TryImportPackage()
        {
            string tmp = Path.Combine(Path.GetTempPath(), PACKAGE_NAME);
            using (var stream = typeof(MapCreatorWindow).Assembly.GetManifestResourceStream(PKG_RESOURCE))
            {
                if (stream == null)
                {
                    Debug.LogError("embedded round template package missing - " + PKG_RESOURCE);
                    Err("Embedded package not found. Check build setup.");
                    return;
                }
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
                    stream.CopyTo(fs);
            }

            AssetDatabase.importPackageCompleted += OnPackageImported;
            AssetDatabase.importPackageFailed += OnPackageFailed;
            AssetDatabase.ImportPackage(tmp, interactive: true);
        }

        private void OnPackageImported(string packageName)
        {
            AssetDatabase.importPackageCompleted -= OnPackageImported;
            AssetDatabase.importPackageFailed -= OnPackageFailed;
            AssetDatabase.Refresh();
            EditorApplication.delayCall += Repaint;
        }

        private void OnPackageFailed(string packageName, string errorMessage)
        {
            AssetDatabase.importPackageCompleted -= OnPackageImported;
            AssetDatabase.importPackageFailed -= OnPackageFailed;
            Debug.LogError("round template import failed - " + errorMessage);
            Err("Import failed: " + errorMessage);
        }

        private void TryCreate()
        {
            _statusMsg = "";
            string trimmed = _levelName.Trim();

            if (string.IsNullOrEmpty(trimmed)) { Err("Level name can't be empty."); return; }
            if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { Err("Level name has invalid characters."); return; }

            var templateAsset = AssetDatabase.LoadAssetAtPath<GameObject>(TEMPLATE_PATH);
            if (templateAsset == null) { Err("Template not found — import the package first."); return; }

            string rootName = "round_" + trimmed.ToLower().Replace(" ", "_");
            string mapDir = $"{MAPS_DIR}/{rootName}";

            if (!AssetDatabase.IsValidFolder(MAPS_DIR))
                AssetDatabase.CreateFolder("Assets", "Maps");
            if (!AssetDatabase.IsValidFolder(mapDir))
                AssetDatabase.CreateFolder(MAPS_DIR, rootName);
            // sibling Output folder, created for the user to pack into
            if (!AssetDatabase.IsValidFolder(OUTPUT_DIR))
                AssetDatabase.CreateFolder(MAPS_DIR, "Output");

            // create the SO
            var soPath = $"{mapDir}/{rootName}.asset";
            var mapAsset = AssetDatabase.LoadAssetAtPath<BetterFGMapAsset>(soPath);
            if (mapAsset == null)
            {
                mapAsset = ScriptableObject.CreateInstance<BetterFGMapAsset>();
                AssetDatabase.CreateAsset(mapAsset, soPath);
            }

            mapAsset.displayName = trimmed;
            mapAsset.prefabName = rootName;

            EditorUtility.SetDirty(mapAsset);
            AssetDatabase.SaveAssets();

            // fresh scene in the working folder, made the active one, and the prefab goes into it
            string scenePath = $"{mapDir}/{rootName}.unity";
            GameObject go;
            try
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                // seed the scene's skybox; everything else (ambient/reflection/fog) is whatever
                // the user sets in the Lighting window and gets read at pack time.
                var sky = AssetDatabase.LoadAssetAtPath<Material>(DEFAULT_SKYBOX_PATH);
                if (sky != null) RenderSettings.skybox = sky;

                go = (GameObject)PrefabUtility.InstantiatePrefab(templateAsset, scene);
                go.name = rootName;
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                EditorSceneManager.SaveScene(scene, scenePath);
            }
            catch (System.Exception ex)
            {
                Err($"Create map scene failed: {ex.Message}");
                Debug.LogError($"map scene creation blew up: {ex}");
                return;
            }

            AssetDatabase.Refresh();
            Undo.RegisterCreatedObjectUndo(go, "Create BettrFG Map");

            // jump the Project window to the new map folder and ping it
            var folderObj = AssetDatabase.LoadAssetAtPath<Object>(mapDir);
            if (folderObj != null)
            {
                Selection.activeObject = folderObj;
                EditorGUIUtility.PingObject(folderObj);
            }

            Debug.Log($"created '{rootName}' - scene {scenePath}, asset {soPath}");
            Ok($"Created '{rootName}' scene. Edit your map, then use BettrFG > Round > Map Packager to pack it.");
        }

        private void Ok(string msg) { _statusMsg = msg; _statusType = UnityEditor.MessageType.Info; Repaint(); }
        private void Err(string msg) { _statusMsg = msg; _statusType = UnityEditor.MessageType.Error; Repaint(); }
    }
}
