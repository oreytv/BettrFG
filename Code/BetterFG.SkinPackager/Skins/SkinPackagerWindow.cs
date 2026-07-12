using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using EditorMessageType = UnityEditor.MessageType;

namespace BetterFG.Editor
{
    public class SkinPackagerWindow : EditorWindow
    {
        private static SkinPackagerWindow current;

        [MenuItem("BettrFG/Skins/Skin Packager")]
        public static void Open() => GetWindow<SkinPackagerWindow>("Skin Packager");

        [MenuItem("GameObject/Add as Bone Offset", false, -200)]
        private static void AddSelectedAsBoneOffset()
        {
            var window = GetOpenWindow();
            Transform bone = Selection.activeTransform;
            if (window == null || bone == null) return;
            window.AddBoneOffset(bone);
        }

        [MenuItem("GameObject/Add as Bone Offset", true, -200)]
        private static bool CanAddSelectedAsBoneOffset()
        {
            return GetOpenWindow() != null && Selection.activeTransform != null;
        }

        [MenuItem("CONTEXT/Transform/Add as Bone Offset", false, -200)]
        private static void AddContextBoneOffset(MenuCommand command)
        {
            var window = GetOpenWindow();
            var bone = command.context as Transform;
            if (window == null || bone == null) return;
            window.AddBoneOffset(bone);
        }

        [MenuItem("CONTEXT/Transform/Add as Bone Offset", true, -200)]
        private static bool CanAddContextBoneOffset(MenuCommand command)
        {
            return GetOpenWindow() != null && command.context is Transform;
        }

        private const string PREF_LAST_COVER_DIR = "BetterFG.SkinPackager.LastCoverDir";
        private const string PREF_LAST_CATALOG_DIR = "BetterFG.SkinPackager.LastCatalogDir";

        private enum SkinKind { Costume, Accessory, Item, Plinth }
        private static readonly string[] KIND_LABELS = { "Costume", "Accessory", "Item", "Plinth" };

        private static readonly string[] NOTICES = {
            "You must make sure that the object/asset name is the same as the bundle name, or else BettrFG will not find your costume asset.",
            "You must make sure that the object/asset name is the same as the bundle name, or else BettrFG will not find your accessory asset.",
            "You must make sure that the object/asset name is the same as the bundle name, or else BettrFG will not find your item asset.",
            "You must make sure that the object/asset name is the same as the bundle name, or else BettrFG will not find your plinth asset.",
        };

        private SkinKind _kind = SkinKind.Costume;

        private string _bundlePath = "";
        private string BundleFileName => string.IsNullOrEmpty(_bundlePath) ? "" : Path.GetFileName(_bundlePath);

        private string _name = "";
        private string _author = "";
        private string _description = "";
        private string _group = "";
        private List<string> _knownGroups = new List<string>();
        private string _newGroup = "";

        private bool _keepBase = false;
        private float _skinScale = 0f;

        private float _itemScale = 1f;
        private Transform _leftTransform;
        private Transform _rightTransform;
        private string _boneSearch = "";

        public struct BoneRow
        {
            public Transform bone;
            public string boneName;
            public Vector3 localPos;
        }
        private List<BoneRow> _boneRows = new List<BoneRow>();

        // Build
        private GameObject _buildPrefab;
        private string _buildBundleName = "";
        private BuildTarget _buildTarget = BuildTarget.StandaloneWindows64;

        // Cover is loaded from disk, not from project
        private string _coverPath = "";
        private Texture2D _coverPreview;
        private const int COVER_W = 956;
        private const int COVER_H = 763;

        private string _outputDir = "";
        private string _repoRoot = "";
        private Vector2 _scroll;
        private string _statusMsg = "";
        private EditorMessageType _statusType = EditorMessageType.None;

        private GUIStyle _sectionStyle;
        private GUIStyle _noticeStyle;
        private bool _stylesReady;

        private void OnEnable()
        {
            current = this;
            if (string.IsNullOrEmpty(_repoRoot))
                _repoRoot = EditorPrefs.GetString(PREF_LAST_CATALOG_DIR, "");
            RefreshGroups();
        }

        private void OnDisable()
        {
            if (current == this) current = null;
        }

        private static SkinPackagerWindow GetOpenWindow()
        {
            if (current != null) return current;
            current = HasOpenInstances<SkinPackagerWindow>() ? GetWindow<SkinPackagerWindow>() : null;
            return current;
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _sectionStyle = new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(8, 8, 6, 6) };
            _noticeStyle = new GUIStyle(EditorStyles.helpBox)
            {
                normal = { textColor = new Color(1f, 0.85f, 0.15f) },
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
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawSection("Build Bundle", DrawBuildBundle);
            DrawSection("Bundle", DrawBundle);

            EditorGUI.BeginChangeCheck();
            _kind = (SkinKind)GUILayout.Toolbar((int)_kind, KIND_LABELS);
            if (EditorGUI.EndChangeCheck()) _statusMsg = "";
            GUILayout.Space(4);

            GUILayout.Label("⚠  " + NOTICES[(int)_kind], _noticeStyle);
            GUILayout.Space(4);

            DrawSection("Info", DrawCommon);
            DrawSection("Group", DrawGroup);
            if (_kind == SkinKind.Costume) DrawSection("Costume Options", DrawCostume);
            if (_kind == SkinKind.Item) DrawSection("Item Options", DrawItem);
            if (_kind == SkinKind.Costume) DrawSection("Bone Offsets", DrawBoneOffsets);
            DrawSection("Cover Image  (956 x 763 - cropped to fill, no stretch)", DrawCover);
            DrawSection("Load Existing Skin", DrawLoad);
            DrawSection("Output", DrawOutput);

            if (!string.IsNullOrEmpty(_statusMsg))
            {
                GUILayout.Space(4);
                EditorGUILayout.HelpBox(_statusMsg, _statusType);
            }

            GUILayout.Space(8);
            EditorGUILayout.EndScrollView();
        }

        private void DrawSection(string title, Action content)
        {
            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label(title, EditorStyles.boldLabel);
            content();
            GUILayout.EndVertical();
            GUILayout.Space(4);
        }

        private void DrawBuildBundle()
        {
            EditorGUILayout.HelpBox("Please name the bundle to be the same as the root GameObject.", EditorMessageType.Info);
            GUILayout.Space(2);

            EditorGUI.BeginChangeCheck();
            _buildPrefab = (GameObject)EditorGUILayout.ObjectField("Prefab", _buildPrefab, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck() && _buildPrefab != null && string.IsNullOrEmpty(_buildBundleName))
                _buildBundleName = _buildPrefab.name.ToLower();

            _buildBundleName = EditorGUILayout.TextField("Bundle Name", _buildBundleName);
            _buildTarget = (BuildTarget)EditorGUILayout.EnumPopup("Platform", _buildTarget);

            GUILayout.Space(4);
            GUI.backgroundColor = new Color(0.25f, 0.55f, 0.9f);
            if (GUILayout.Button("Build AssetBundle", GUILayout.Height(24))) TryBuildBundle();
            GUI.backgroundColor = Color.white;
        }

        private void TryBuildBundle()
        {
            if (_buildPrefab == null) { Err("Pick a prefab to build."); return; }
            if (string.IsNullOrWhiteSpace(_buildBundleName)) { Err("Bundle name is required."); return; }

            string assetPath = AssetDatabase.GetAssetPath(_buildPrefab);
            if (string.IsNullOrEmpty(assetPath)) { Err("Prefab must be a project asset, not a scene object."); return; }

            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null) { Err("Couldn't get asset importer for that prefab."); return; }

            string prevBundle = importer.assetBundleName;
            importer.assetBundleName = _buildBundleName.ToLower();
            importer.SaveAndReimport();

            string outDir = "Assets/StreamingAssets";
            if (!Directory.Exists(Application.streamingAssetsPath))
                Directory.CreateDirectory(outDir);

            var builds = new AssetBundleBuild[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = _buildBundleName.ToLower(),
                    assetNames = new[] { assetPath }
                }
            };

            // LZ4 — LZMA (the None default) costs a ~1s decompress stutter in game every time the skin loads
            var manifest = BuildPipeline.BuildAssetBundles(outDir, builds, BuildAssetBundleOptions.ChunkBasedCompression, _buildTarget);

            importer.assetBundleName = prevBundle;
            importer.SaveAndReimport();
            AssetDatabase.Refresh();

            if (manifest == null) { Err("Build failed. Check the console."); return; }

            string builtPath = Path.Combine(Application.streamingAssetsPath, _buildBundleName.ToLower());
            if (File.Exists(builtPath))
            {
                _bundlePath = builtPath;
                if (string.IsNullOrEmpty(_name)) _name = _buildBundleName;
            }

            Ok($"Built -> {builtPath}");
        }

        private void DrawBundle()
        {
            GUILayout.BeginHorizontal();
            EditorGUILayout.TextField("Bundle File", BundleFileName);
            if (GUILayout.Button("Browse...", GUILayout.Width(70)))
            {
                string picked = EditorUtility.OpenFilePanel("Select AssetBundle", _bundlePath, "");
                if (!string.IsNullOrEmpty(picked))
                {
                    _bundlePath = picked;
                    if (string.IsNullOrEmpty(_name))
                        _name = Path.GetFileNameWithoutExtension(picked);
                    Repaint();
                }
            }
            GUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Select the built AssetBundle file. Usually has no extension.", EditorMessageType.None);
        }

        private void DrawCommon()
        {
            _name = EditorGUILayout.TextField("Display name", _name);
            _author = EditorGUILayout.TextField("Author", _author);
            _description = EditorGUILayout.TextField("Description", _description);
        }

        private void DrawGroup()
        {
            EditorGUILayout.HelpBox("Blank means Unsorted. Grouping only adds metadata, it does not move files.", EditorMessageType.None);

            var labels = new List<string> { "Unsorted" };
            labels.AddRange(_knownGroups);
            int selected = 0;
            for (int i = 0; i < _knownGroups.Count; i++)
                if (string.Equals(_knownGroups[i], _group, StringComparison.OrdinalIgnoreCase))
                    selected = i + 1;
            int picked = EditorGUILayout.Popup("Group", selected, labels.ToArray());
            _group = picked == 0 ? "" : _knownGroups[picked - 1];

            GUILayout.Space(4);
            GUILayout.Label("Add new group", EditorStyles.miniBoldLabel);
            GUILayout.BeginHorizontal();
            _newGroup = EditorGUILayout.TextField(_newGroup);
            if (GUILayout.Button("Add", GUILayout.Width(70)) && !string.IsNullOrWhiteSpace(_newGroup))
            {
                _group = _newGroup.Trim();
                if (!_knownGroups.Exists(g => string.Equals(g, _group, StringComparison.OrdinalIgnoreCase)))
                {
                    _knownGroups.Add(_group);
                    _knownGroups.Sort(StringComparer.OrdinalIgnoreCase);
                }
                _newGroup = "";
            }
            GUILayout.EndHorizontal();
        }

        private void RefreshGroups()
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(_repoRoot) && Directory.Exists(_repoRoot))
            {
                foreach (string category in new[] { "Costumes", "Accessories", "Items", "Plinths" })
                {
                    string path = Path.Combine(_repoRoot, category);
                    if (!Directory.Exists(path)) continue;
                    foreach (string info in Directory.GetFiles(path, "info.json", SearchOption.AllDirectories))
                    {
                        string group = SkinInfoJson.ReadStr(File.ReadAllText(info), "group").Trim();
                        if (!string.IsNullOrEmpty(group)) found.Add(group);
                    }
                }
            }
            _knownGroups = new List<string>(found);
            _knownGroups.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private void DrawCostume()
        {
            _keepBase = EditorGUILayout.Toggle("Keep Fall Guy on application", _keepBase);
            _skinScale = EditorGUILayout.FloatField("Skin Scale (0 = off)", _skinScale);
        }

        private void DrawItem()
        {
            _itemScale = EditorGUILayout.FloatField("Scale of item gameobject", _itemScale);
            GUILayout.Space(6);
            GUILayout.Label("Left Hand", EditorStyles.miniBoldLabel);
            _leftTransform = (Transform)EditorGUILayout.ObjectField("Transform", _leftTransform, typeof(Transform), true);
            GUILayout.Space(4);
            GUILayout.Label("Right Hand", EditorStyles.miniBoldLabel);
            _rightTransform = (Transform)EditorGUILayout.ObjectField("Transform", _rightTransform, typeof(Transform), true);
        }

        private void DrawBoneOffsets()
        {
            EditorGUILayout.HelpBox(
                "If you edited the position of Fall Guys armature's bones, please reference the Transforms of each bone you modified, this will make BettrFG respect the edits.",
                EditorMessageType.Info);
            GUILayout.Space(2);

            GUILayout.BeginHorizontal();
            _boneSearch = EditorGUILayout.TextField("Search", _boneSearch);
            if (GUILayout.Button("x", GUILayout.Width(22)))
                _boneSearch = "";
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = Selection.activeTransform != null;
            if (GUILayout.Button("Add All From Selection", GUILayout.Width(150)))
                AddBoneOffsetsRecursive(Selection.activeTransform);
            GUI.enabled = true;
            if (GUILayout.Button("Clear All", GUILayout.Width(90)))
            {
                _boneRows.Clear();
                Ok("Cleared all bone offsets");
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            for (int i = 0; i < _boneRows.Count; i++)
            {
                if (!BoneMatchesSearch(_boneRows[i])) continue;

                GUILayout.BeginHorizontal();
                var row = _boneRows[i];
                var pickedBone = (Transform)EditorGUILayout.ObjectField(row.bone, typeof(Transform), true);
                if (pickedBone != row.bone)
                {
                    row.bone = pickedBone;
                    if (pickedBone != null)
                    {
                        row.boneName = pickedBone.name;
                        row.localPos = pickedBone.localPosition;
                    }
                }
                if (GUILayout.Button("x", GUILayout.Width(22))) { _boneRows.RemoveAt(i); GUILayout.EndHorizontal(); break; }
                _boneRows[i] = row;
                GUILayout.EndHorizontal();

                EditorGUI.indentLevel++;
                row.boneName = EditorGUILayout.TextField("Bone", row.boneName ?? "");
                row.localPos = EditorGUILayout.Vector3Field("Local Pos", row.localPos);
                _boneRows[i] = row;
                EditorGUI.indentLevel--;
                GUILayout.Space(2);
            }

            GUILayout.Space(2);
            if (GUILayout.Button("+ Add Row", GUILayout.Width(90)))
                _boneRows.Add(new BoneRow());
        }

        private void AddBoneOffset(Transform bone)
        {
            if (bone == null) return;

            for (int i = 0; i < _boneRows.Count; i++)
            {
                var row = _boneRows[i];
                if (string.Equals(row.boneName, bone.name, StringComparison.Ordinal))
                {
                    row.bone = bone;
                    row.boneName = bone.name;
                    row.localPos = bone.localPosition;
                    _boneRows[i] = row;
                    Ok($"Updated bone offset -> {bone.name}");
                    return;
                }
            }

            _boneRows.Add(new BoneRow
            {
                bone = bone,
                boneName = bone.name,
                localPos = bone.localPosition
            });
            Ok($"Added bone offset -> {bone.name}");
        }

        private void AddBoneOffsetsRecursive(Transform root)
        {
            if (root == null) return;
            AddBoneOffsetsRecursiveInner(root);
            Ok($"Added bone offsets from {root.name}");
        }

        private void AddBoneOffsetsRecursiveInner(Transform bone)
        {
            if (bone == null) return;
            if (BoneMatchesSearch(bone.name))
                AddBoneOffset(bone);

            for (int i = 0; i < bone.childCount; i++)
                AddBoneOffsetsRecursiveInner(bone.GetChild(i));
        }

        private bool BoneMatchesSearch(BoneRow row)
        {
            return BoneMatchesSearch(row.bone != null ? row.bone.name : row.boneName);
        }

        private bool BoneMatchesSearch(string boneName)
        {
            if (string.IsNullOrWhiteSpace(_boneSearch)) return true;
            if (string.IsNullOrWhiteSpace(boneName)) return false;
            return boneName.IndexOf(_boneSearch, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawCover()
        {
            GUILayout.BeginHorizontal();
            EditorGUILayout.TextField("Image File", string.IsNullOrEmpty(_coverPath) ? "" : Path.GetFileName(_coverPath));
            if (GUILayout.Button("Browse...", GUILayout.Width(70)))
            {
                string seed = !string.IsNullOrEmpty(_coverPath)
                    ? _coverPath
                    : EditorPrefs.GetString(PREF_LAST_COVER_DIR, "");
                string picked = EditorUtility.OpenFilePanel("Select Cover Image", seed, "png,jpg,jpeg");
                if (!string.IsNullOrEmpty(picked) && File.Exists(picked))
                {
                    _coverPath = picked;
                    EditorPrefs.SetString(PREF_LAST_COVER_DIR, Path.GetDirectoryName(picked));
                    LoadCoverPreview(picked);
                    Repaint();
                }
            }
            if (!string.IsNullOrEmpty(_coverPath) && GUILayout.Button("x", GUILayout.Width(22)))
            {
                _coverPath = "";
                if (_coverPreview != null) { DestroyImmediate(_coverPreview); _coverPreview = null; }
                Repaint();
            }
            GUILayout.EndHorizontal();

            if (_coverPreview != null)
            {
                GUILayout.Space(4);

                const float previewW = 200f;
                const float previewH = 159f;

                float texAspect = (float)_coverPreview.width / _coverPreview.height;
                float boxAspect = previewW / previewH;

                Rect outer = GUILayoutUtility.GetRect(previewW, previewH, GUILayout.ExpandWidth(false));
                outer.x = (EditorGUIUtility.currentViewWidth - previewW) * 0.5f;

                float drawW, drawH;
                if (texAspect > boxAspect)
                {
                    drawW = previewW;
                    drawH = previewW / texAspect;
                }
                else
                {
                    drawH = previewH;
                    drawW = previewH * texAspect;
                }

                var drawRect = new Rect(
                    outer.x + (previewW - drawW) * 0.5f,
                    outer.y + (previewH - drawH) * 0.5f,
                    drawW,
                    drawH
                );

                EditorGUI.DrawRect(outer, new Color(0.1f, 0.1f, 0.1f));
                GUI.DrawTexture(drawRect, _coverPreview, ScaleMode.StretchToFill);
            }
        }

        private void LoadCoverPreview(string path)
        {
            if (_coverPreview != null) { DestroyImmediate(_coverPreview); _coverPreview = null; }
            byte[] bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(bytes))
                _coverPreview = tex;
            else
                DestroyImmediate(tex);
        }

        private void DrawLoad()
        {
            EditorGUILayout.HelpBox("Use this to add an existing packed skin folder to edit it. It will read info.json and re-fills all fields.", EditorMessageType.None);
            GUILayout.Space(2);
            GUI.backgroundColor = new Color(0.6f, 0.4f, 0.9f);
            if (GUILayout.Button("Load Skin Folder.....", GUILayout.Height(24))) TryLoad();
            GUI.backgroundColor = Color.white;
        }

        private void TryLoad()
        {
            string dir = EditorUtility.OpenFolderPanel("Select skin folder", _outputDir, "");
            if (string.IsNullOrEmpty(dir)) return;

            string infoPath = Path.Combine(dir, "info.json");
            if (!File.Exists(infoPath)) { Err("No info.json found in that folder"); return; }

            try
            {
                string json = File.ReadAllText(infoPath);

                _name = SkinInfoJson.ReadStr(json, "name");
                _author = SkinInfoJson.ReadStr(json, "author");
                _description = SkinInfoJson.ReadStr(json, "description");
                _group = SkinInfoJson.ReadStr(json, "group");
                string file = SkinInfoJson.ReadStr(json, "file");
                string type = SkinInfoJson.ReadStr(json, "type");

                switch (type)
                {
                    case "accessory": _kind = SkinKind.Accessory; break;
                    case "item": _kind = SkinKind.Item; break;
                    case "plinth": _kind = SkinKind.Plinth; break;
                    default: _kind = SkinKind.Costume; break;
                }

                if (!string.IsNullOrEmpty(file))
                {
                    string bp = Path.Combine(dir, file);
                    if (File.Exists(bp)) _bundlePath = bp;
                }

                if (_kind == SkinKind.Costume)
                {
                    _keepBase = SkinInfoJson.ReadBool(json, "keepBase");
                    _skinScale = SkinInfoJson.ReadFloat(json, "skinScale");
                    _boneRows = SkinInfoJson.ReadBoneOffsets(json);
                    RebindBoneRowsFromScene();
                }
                else _boneRows.Clear();

                if (_kind == SkinKind.Item)
                {
                    _itemScale = SkinInfoJson.ReadFloat(json, "scale", 1f);
                    RebindItemTransformsFromScene(
                        SkinInfoJson.ReadItemBoneName(json, "left"),
                        SkinInfoJson.ReadItemBoneName(json, "right"));
                }
                else
                {
                    _leftTransform = null;
                    _rightTransform = null;
                }

                _outputDir = dir;

                // If loaded from inside the public skins repo (…/<Kind>/<folder>),
                // back-fill the repo root so re-packing targets the same place.
                var parent = Directory.GetParent(dir);
                var grandparent = parent?.Parent;
                if (grandparent != null &&
                    File.Exists(Path.Combine(grandparent.FullName, "select generate_catalog.bat")))
                {
                    _repoRoot = grandparent.FullName;
                    RefreshGroups();
                }

                string coverPath = Path.Combine(dir, "cover.jpg");
                if (!File.Exists(coverPath)) coverPath = Path.Combine(dir, "cover.png");
                if (File.Exists(coverPath))
                {
                    _coverPath = coverPath;
                    LoadCoverPreview(coverPath);
                }

                Ok($"Loaded <- {dir}");
            }
            catch (Exception ex) { Err($"Load failed: {ex.Message}"); }
        }

        private void DrawOutput()
        {
            EditorGUILayout.HelpBox(
                "Select the public skins repo's generate_catalog.bat. The skin is placed in the right subfolder (Costumes / Accessories / Items / Plinths) automatically, named after the bundle file. The catalog is regenerated after packing.",
                EditorMessageType.Info);
            GUILayout.Space(2);

            GUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            _repoRoot = EditorGUILayout.TextField("Repo Folder", _repoRoot);
            if (EditorGUI.EndChangeCheck()) RefreshGroups();
            if (GUILayout.Button("generate_catalog.bat...", GUILayout.Width(160)))
            {
                string seed = !string.IsNullOrEmpty(_repoRoot)
                    ? _repoRoot
                    : EditorPrefs.GetString(PREF_LAST_CATALOG_DIR, "");
                string picked = EditorUtility.OpenFilePanel("Select generate_catalog.bat", seed, "bat");
                if (!string.IsNullOrEmpty(picked))
                {
                    _repoRoot = Path.GetDirectoryName(picked);
                    EditorPrefs.SetString(PREF_LAST_CATALOG_DIR, _repoRoot);
                    RefreshGroups();
                }
            }
            GUILayout.EndHorizontal();

            string dest = ComputeDestDir();
            if (!string.IsNullOrEmpty(dest))
                EditorGUILayout.LabelField("Will pack to", dest, EditorStyles.miniLabel);

            GUILayout.Space(6);
            GUI.backgroundColor = new Color(0.25f, 0.7f, 0.35f);
            if (GUILayout.Button("Pack Skin", GUILayout.Height(28))) TryPack();
            GUI.backgroundColor = Color.white;
        }

        private static string KindFolder(SkinKind k)
        {
            switch (k)
            {
                case SkinKind.Costume: return "Costumes";
                case SkinKind.Accessory: return "Accessories";
                case SkinKind.Item: return "Items";
                case SkinKind.Plinth: return "Plinths";
                default: return "Costumes";
            }
        }

        private string ComputeDestDir()
        {
            if (string.IsNullOrWhiteSpace(_repoRoot) || string.IsNullOrEmpty(BundleFileName)) return "";
            return Path.Combine(_repoRoot, KindFolder(_kind), BundleFileName);
        }

        private void TryPack()
        {
            _statusMsg = "";

            if (string.IsNullOrWhiteSpace(_bundlePath) || !File.Exists(_bundlePath)) { Err("Select a valid bundle file first."); return; }
            if (string.IsNullOrWhiteSpace(_name)) { Err("Name is required."); return; }
            if (string.IsNullOrWhiteSpace(_repoRoot) || !Directory.Exists(_repoRoot)) { Err("Select the repo's generate_catalog.bat first."); return; }

            string dest = ComputeDestDir();

            try
            {
                Directory.CreateDirectory(dest);
                _outputDir = dest;
                File.Copy(_bundlePath, Path.Combine(dest, BundleFileName), overwrite: true);
                WriteInfoJson();
                WriteCover();
                RunCatalogBat();
                WriteNewCatalog(_repoRoot);
                Ok($"Packed -> {dest}");
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

        // writes catalog2.json alongside the repo's flat catalog.json. this is the whole point of the
        // per-repo file: the mod pulls the entire skin list in one request off catalog2.json instead
        // of fetching every folder's info.json separately (which got the user rate-limited by github
        // raw). we do it here in C# rather than relying on the repo's bat, so every repo — even old
        // ones whose generate_catalog.bat predates this — gets catalog2.json the next time anyone
        // packs. each entry is the folder's info.json copied verbatim with a "path" field prepended,
        // so the mod parses it through the exact same path as a live info.json fetch.
        private static void WriteNewCatalog(string repoRoot)
        {
            if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot)) return;

            var entries = new List<string>();
            foreach (string category in new[] { "Costumes", "Accessories", "Items", "Plinths", "Emotes" })
            {
                string root = Path.Combine(repoRoot, category);
                if (!Directory.Exists(root)) continue;
                foreach (string dir in Directory.GetDirectories(root))
                {
                    string info = Path.Combine(dir, "info.json");
                    if (!File.Exists(info)) continue;
                    string body = File.ReadAllText(info).Trim();
                    int open = body.IndexOf('{');
                    int close = body.LastIndexOf('}');
                    if (open < 0 || close <= open) continue;
                    // trim both ends: a trailing newline before the source '}' would otherwise land
                    // between our inner text and the next field's comma, e.g. "]\n, group" — legal
                    // whitespace but ugly, and it stacks up across entries. also drop a trailing comma
                    // some info.json files carry before their closing brace.
                    string inner = body.Substring(open + 1, close - open - 1).Trim().TrimEnd(',').Trim();
                    string path = category + "/" + new DirectoryInfo(dir).Name;
                    string pathField = "\"path\": \"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                    string obj = inner.Length == 0 ? "{ " + pathField + " }" : "{ " + pathField + ", " + inner + " }";
                    entries.Add(obj);
                }
            }

            var sb = new System.Text.StringBuilder();
            sb.Append('[');
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('\n');
                sb.Append(entries[i]);
            }
            if (entries.Count > 0) sb.Append('\n');
            sb.Append(']');
            File.WriteAllText(Path.Combine(repoRoot, "catalog2.json"), sb.ToString(), new System.Text.UTF8Encoding(false));
        }

        private void WriteInfoJson()
        {
            var bones = new List<(string, Vector3)>();
            foreach (var r in _boneRows)
            {
                string boneName = r.bone != null ? r.bone.name : r.boneName;
                Vector3 localPos = r.bone != null ? r.bone.localPosition : r.localPos;
                if (!string.IsNullOrWhiteSpace(boneName))
                    bones.Add((boneName, localPos));
            }

            SkinInfoJson.Write(_outputDir, BundleFileName, _name, _author, _description, _group, KindStr(_kind),
                keepBase: _keepBase, skinScale: _skinScale,
                itemScale: _itemScale,
                leftBoneName: _leftTransform != null ? _leftTransform.name : null,
                rightBoneName: _rightTransform != null ? _rightTransform.name : null,
                leftPos: _leftTransform != null ? _leftTransform.localPosition : Vector3.zero,
                leftRot: _leftTransform != null ? _leftTransform.localEulerAngles : Vector3.zero,
                rightPos: _rightTransform != null ? _rightTransform.localPosition : Vector3.zero,
                rightRot: _rightTransform != null ? _rightTransform.localEulerAngles : Vector3.zero,
                boneOffsets: bones);
        }

        private void RebindBoneRowsFromScene()
        {
            for (int i = 0; i < _boneRows.Count; i++)
            {
                var row = _boneRows[i];
                row.bone = FindSceneTransform(row.boneName);
                _boneRows[i] = row;
            }
        }

        private void RebindItemTransformsFromScene(string leftBoneName, string rightBoneName)
        {
            _leftTransform = FindSceneTransform(leftBoneName);
            _rightTransform = FindSceneTransform(rightBoneName);
        }

        private static Transform FindSceneTransform(string boneName)
        {
            if (string.IsNullOrWhiteSpace(boneName)) return null;

            var all = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;
                if (EditorUtility.IsPersistent(t)) continue;
                if (string.Equals(t.name, boneName, StringComparison.Ordinal))
                    return t;
            }

            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;
                if (EditorUtility.IsPersistent(t)) continue;
                if (string.Equals(t.name, boneName, StringComparison.OrdinalIgnoreCase))
                    return t;
            }

            return null;
        }

        private void WriteCover()
        {
            if (string.IsNullOrEmpty(_coverPath) || !File.Exists(_coverPath)) return;

            byte[] srcBytes = File.ReadAllBytes(_coverPath);
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!src.LoadImage(srcBytes)) { DestroyImmediate(src); return; }

            float srcAspect = (float)src.width / src.height;
            float dstAspect = (float)COVER_W / COVER_H;

            int cropX, cropY, cropW, cropH;
            if (srcAspect > dstAspect)
            {
                cropH = src.height;
                cropW = Mathf.RoundToInt(src.height * dstAspect);
                cropX = (src.width - cropW) / 2;
                cropY = 0;
            }
            else
            {
                cropW = src.width;
                cropH = Mathf.RoundToInt(src.width / dstAspect);
                cropX = 0;
                cropY = (src.height - cropH) / 2;
            }

            Color[] cropped = src.GetPixels(cropX, cropY, cropW, cropH);
            DestroyImmediate(src);

            var tmp = new Texture2D(cropW, cropH, TextureFormat.RGB24, false);
            tmp.SetPixels(cropped);
            tmp.Apply();

            var rt = RenderTexture.GetTemporary(COVER_W, COVER_H, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            Graphics.Blit(tmp, rt);
            DestroyImmediate(tmp);

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var final = new Texture2D(COVER_W, COVER_H, TextureFormat.RGB24, false);
            final.ReadPixels(new Rect(0, 0, COVER_W, COVER_H), 0, 0);
            final.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            File.WriteAllBytes(Path.Combine(_outputDir, "cover.jpg"), final.EncodeToJPG(92));
            DestroyImmediate(final);
        }

        private static string KindStr(SkinKind k)
        {
            switch (k)
            {
                case SkinKind.Costume: return "costume";
                case SkinKind.Accessory: return "accessory";
                case SkinKind.Item: return "item";
                case SkinKind.Plinth: return "plinth";
                default: return "costume";
            }
        }

        private void Ok(string msg) { _statusMsg = msg; _statusType = EditorMessageType.Info; Repaint(); }
        private void Err(string msg) { _statusMsg = msg; _statusType = EditorMessageType.Error; Repaint(); }
    }
}
