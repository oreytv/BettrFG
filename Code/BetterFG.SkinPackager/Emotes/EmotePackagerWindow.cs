using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using EditorMessageType = UnityEditor.MessageType;

namespace BetterFG.Editor
{
    // drag an AnimationClip in, pick generate_catalog.bat, and it builds a bundle of the clip and
    // drops the whole emote folder (info.json + cover.jpg + bundle) under root/Emotes/emote_x.
    public class EmotePackagerWindow : EditorWindow
    {
        [MenuItem("BettrFG/Emotes/Emote Packager")]
        public static void Open() => GetWindow<EmotePackagerWindow>("Emote Packager");

        private const string PREF_LAST_CATALOG_DIR = "BettrFG.EmotePackager.LastCatalogDir";
        private const string PREF_LAST_COVER_DIR = "BettrFG.EmotePackager.LastCoverDir";
        private const string PREF_LAST_AUDIO_DIR = "BettrFG.EmotePackager.LastAudioDir";
        private const int COVER_W = 956;
        private const int COVER_H = 763;
        private const long AUDIO_MAX_BYTES = 4L * 1024 * 1024; // 4mb

        private AnimationClip _clip;
        private string _name = "";
        private string _author = "";
        private string _description = "";

        private string _coverPath = "";
        private Texture2D _coverPreview;

        private string _audioPath = "";

        private string _repoRoot = "";
        private BuildTarget _buildTarget = BuildTarget.StandaloneWindows64;

        private Vector2 _scroll;
        private string _statusMsg = "";
        private EditorMessageType _statusType = EditorMessageType.None;

        private GUIStyle _sectionStyle;
        private bool _stylesReady;

        private void OnEnable()
        {
            if (string.IsNullOrEmpty(_repoRoot))
                _repoRoot = EditorPrefs.GetString(PREF_LAST_CATALOG_DIR, "");
        }

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

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("Animation Clip", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Drag your AnimationClip here. Make it on the Animation Dummy (BettrFG > References > Animation Dummy).", EditorMessageType.Info);
            EditorGUI.BeginChangeCheck();
            _clip = (AnimationClip)EditorGUILayout.ObjectField("Clip", _clip, typeof(AnimationClip), false);
            if (EditorGUI.EndChangeCheck() && _clip != null && string.IsNullOrEmpty(_name))
                _name = _clip.name;
            GUILayout.EndVertical();
            GUILayout.Space(4);

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("Info", EditorStyles.boldLabel);
            _name = EditorGUILayout.TextField("Display name", _name);
            _author = EditorGUILayout.TextField("Author", _author);
            _description = EditorGUILayout.TextField("Description", _description);
            GUILayout.EndVertical();
            GUILayout.Space(4);

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("Cover Image  (956 x 763 - cropped to fill, no stretch)", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            EditorGUILayout.TextField("Image File", string.IsNullOrEmpty(_coverPath) ? "" : Path.GetFileName(_coverPath));
            if (GUILayout.Button("Browse...", GUILayout.Width(70)))
            {
                string seed = !string.IsNullOrEmpty(_coverPath) ? _coverPath : EditorPrefs.GetString(PREF_LAST_COVER_DIR, "");
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
                if (texAspect > boxAspect) { drawW = previewW; drawH = previewW / texAspect; }
                else { drawH = previewH; drawW = previewH * texAspect; }
                var drawRect = new Rect(outer.x + (previewW - drawW) * 0.5f, outer.y + (previewH - drawH) * 0.5f, drawW, drawH);
                EditorGUI.DrawRect(outer, new Color(0.1f, 0.1f, 0.1f));
                GUI.DrawTexture(drawRect, _coverPreview, ScaleMode.StretchToFill);
            }
            GUILayout.EndVertical();
            GUILayout.Space(4);

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("Audio  (optional, mp3 or wav)", EditorStyles.boldLabel);
            GUILayout.BeginHorizontal();
            EditorGUILayout.TextField("Audio File", string.IsNullOrEmpty(_audioPath) ? "" : Path.GetFileName(_audioPath));
            if (GUILayout.Button("Browse...", GUILayout.Width(70)))
            {
                string seed = !string.IsNullOrEmpty(_audioPath) ? _audioPath : EditorPrefs.GetString(PREF_LAST_AUDIO_DIR, "");
                string picked = EditorUtility.OpenFilePanel("Select Audio File", seed, "mp3,wav");
                if (!string.IsNullOrEmpty(picked) && File.Exists(picked))
                {
                    _audioPath = picked;
                    EditorPrefs.SetString(PREF_LAST_AUDIO_DIR, Path.GetDirectoryName(picked));
                    Repaint();
                }
            }
            if (!string.IsNullOrEmpty(_audioPath) && GUILayout.Button("x", GUILayout.Width(22)))
            {
                _audioPath = "";
                Repaint();
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_audioPath) && File.Exists(_audioPath) && new FileInfo(_audioPath).Length > AUDIO_MAX_BYTES)
                EditorGUILayout.HelpBox("The audio will not load because of its size. Please keep it under 4mb.", EditorMessageType.Warning);
            GUILayout.EndVertical();
            GUILayout.Space(4);

            GUILayout.BeginVertical(_sectionStyle);
            GUILayout.Label("Output", EditorStyles.boldLabel);
            _buildTarget = (BuildTarget)EditorGUILayout.EnumPopup("Platform", _buildTarget);
            EditorGUILayout.HelpBox("Select the public emotes repo's generate_catalog.bat. The emote is placed in Emotes/emote_x automatically and the catalog is regenerated after packing.", EditorMessageType.Info);
            GUILayout.Space(2);
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

            string folder = EmoteFolderName();
            if (!string.IsNullOrWhiteSpace(_repoRoot) && !string.IsNullOrEmpty(folder))
                EditorGUILayout.LabelField("Will pack to", Path.Combine(_repoRoot, "Emotes", folder), EditorStyles.miniLabel);

            GUILayout.Space(6);
            GUI.backgroundColor = new Color(0.25f, 0.7f, 0.35f);
            if (GUILayout.Button("Pack Emote", GUILayout.Height(28))) TryPack();
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

        // emote_<sanitized name>, falling back to the clip name
        private string EmoteFolderName()
        {
            string baseName = !string.IsNullOrWhiteSpace(_name) ? _name : (_clip != null ? _clip.name : "");
            if (string.IsNullOrWhiteSpace(baseName)) return "";
            var sb = new StringBuilder();
            foreach (char c in baseName.Trim().ToLower())
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return "emote_" + sb.ToString();
        }

        private void TryPack()
        {
            _statusMsg = "";

            if (_clip == null) { Err("Drag in an AnimationClip first."); return; }
            if (string.IsNullOrWhiteSpace(_name)) { Err("Name is required."); return; }
            if (string.IsNullOrWhiteSpace(_repoRoot) || !Directory.Exists(_repoRoot)) { Err("Select the emotes repo's generate_catalog.bat first."); return; }

            string clipPath = AssetDatabase.GetAssetPath(_clip);
            if (string.IsNullOrEmpty(clipPath)) { Err("Clip must be a project asset."); return; }

            string bundleName = _clip.name.ToLower();
            string folder = EmoteFolderName();
            string dest = Path.Combine(_repoRoot, "Emotes", folder);

            var importer = AssetImporter.GetAtPath(clipPath);
            if (importer == null) { Err("Couldn't get asset importer for that clip."); return; }

            string prevBundle = importer.assetBundleName;
            try
            {
                // tag the clip with a bundle name and build it into a temp dir
                importer.assetBundleName = bundleName;
                importer.SaveAndReimport();

                var builds = new[]
                {
                    new AssetBundleBuild
                    {
                        assetBundleName = bundleName,
                        assetNames = new[] { clipPath }
                    }
                };

                string tmpOut = Path.Combine(Application.temporaryCachePath, "bfgemotebuild");
                Directory.CreateDirectory(tmpOut);
                var manifest = BuildPipeline.BuildAssetBundles(tmpOut, builds, BuildAssetBundleOptions.ChunkBasedCompression, _buildTarget);

                importer.assetBundleName = prevBundle;
                importer.SaveAndReimport();
                AssetDatabase.Refresh();

                if (manifest == null) { Err("Bundle build failed. Check console."); return; }

                string builtBundle = Path.Combine(tmpOut, bundleName);
                if (!File.Exists(builtBundle)) { Err($"Built bundle not found at {builtBundle}"); return; }

                Directory.CreateDirectory(dest);
                File.Copy(builtBundle, Path.Combine(dest, bundleName), overwrite: true);

                // copy the audio in next to the bundle. warn (but still pack) if it's too big to load.
                string audioFile = "";
                bool audioTooBig = false;
                if (!string.IsNullOrEmpty(_audioPath) && File.Exists(_audioPath))
                {
                    audioFile = Path.GetFileName(_audioPath);
                    File.Copy(_audioPath, Path.Combine(dest, audioFile), overwrite: true);
                    audioTooBig = new FileInfo(_audioPath).Length > AUDIO_MAX_BYTES;
                }

                WriteInfoJson(dest, bundleName, audioFile);
                WriteCover(dest);

                RunCatalogBat();
                if (audioTooBig) Err($"Packed -> {dest}\nThe audio will not load because of its size. Please keep it under 4mb.");
                else Ok($"Packed -> {dest}");
            }
            catch (Exception ex)
            {
                importer.assetBundleName = prevBundle;
                importer.SaveAndReimport();
                Err($"Pack failed: {ex.Message}");
            }
        }

        private void WriteInfoJson(string dest, string bundleName, string audioFile)
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"name\": \"" + EscJ(_name) + "\",");
            sb.AppendLine("  \"author\": \"" + EscJ(_author) + "\",");
            sb.AppendLine("  \"description\": \"" + EscJ(_description) + "\",");
            sb.AppendLine("  \"file\": \"" + EscJ(bundleName) + "\",");
            // no "clip" — the mod always plays the first AnimationClip in the bundle
            if (!string.IsNullOrEmpty(audioFile)) sb.AppendLine("  \"audio\": \"" + EscJ(audioFile) + "\",");
            sb.AppendLine("  \"type\": \"emote\"");
            sb.AppendLine("}");
            File.WriteAllText(Path.Combine(dest, "info.json"), sb.ToString(), Encoding.UTF8);
        }

        private static string EscJ(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

        private void WriteCover(string dest)
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

            File.WriteAllBytes(Path.Combine(dest, "cover.jpg"), final.EncodeToJPG(92));
            DestroyImmediate(final);
        }

        private void LoadCoverPreview(string path)
        {
            if (_coverPreview != null) { DestroyImmediate(_coverPreview); _coverPreview = null; }
            byte[] bytes = File.ReadAllBytes(path);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (tex.LoadImage(bytes)) _coverPreview = tex;
            else DestroyImmediate(tex);
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

        private void Ok(string msg) { _statusMsg = msg; _statusType = EditorMessageType.Info; Repaint(); }
        private void Err(string msg) { _statusMsg = msg; _statusType = EditorMessageType.Error; Repaint(); }
    }
}
