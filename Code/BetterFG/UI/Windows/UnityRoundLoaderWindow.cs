using System;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Windows
{
    // Level-editor unity round loader. Pick a round's info.json (input field + browse). Load spawns
    // the round + applies its environment; the button reads "Reload" when the chosen file is already
    // loaded. Unload tears it back down. Mirrors how real unity rounds get spawned, but from disk.

    public class UnityRoundLoaderWindow : BetterFGWindow
    {
        public UnityRoundLoaderWindow(IntPtr ptr) : base(ptr) { }

        public static UnityRoundLoaderWindow Instance { get; private set; }

        protected override float WindowWidth => 340f;
        protected override float WindowHeight => 160f;
        protected override string WindowTitle => "Unity Round Loader";
        protected override string BgResourceName => "BetterFG.assets.ui.windows.generalbg.png";
        protected override bool DraggableFromTitle => true;

        protected override Vector3 InitialBgPosition => new Vector3(179.7451f, 93.1455f, 0f);
        protected override Vector3 InitialBgScale => new Vector3(1.2931f, 3.6616f, 1f);

        private const string PATH_KEY = "unityround.editor.jsonpath";

        private static readonly Color BTN_BROWSE = new Color(0.22f, 0.34f, 0.55f, 1f);
        private static readonly Color BTN_LOAD = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color BTN_UNLOAD = new Color(0.45f, 0.25f, 0.25f, 1f);
        private static readonly Color HINT_COL = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color OK_COL = new Color(0.55f, 0.85f, 0.55f, 1f);
        private static readonly Color ERR_COL = new Color(0.9f, 0.45f, 0.45f, 1f);

        private InputField _pathField;
        private Text _statusLabel;
        private Text _loadLabel;

        // ── api ───────────────────────────────────────────────────────────────

        public void Configure()
        {
            Instance = this;
            SetAnchorPosition(new Vector2(540f, 0f));
            ShowWindow();
            RebuildContent();
        }

        public void Close()
        {
            if (Instance == this) Instance = null;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // load button reads "Reload" only when the chosen file is the one already loaded
        private string LoadLabel()
        {
            string cur = _pathField != null ? (_pathField.text ?? "") : "";
            bool same = UnityRoundLoader.HasSpawned &&
                        string.Equals(cur.Trim().Trim('"'), UnityRoundLoader.LoadedJsonPath,
                            StringComparison.OrdinalIgnoreCase);
            return same ? "RELOAD" : "LOAD";
        }

        // ── content ───────────────────────────────────────────────────────────

        protected override void BuildContent(RectTransform contentRoot)
        {
            BgPosition = new Vector3(179.7451f, 64.46f, 0f);
            BgScale = new Vector3(1.3633f, 3.9f, 1f);
            ContentPosition = new Vector3(190.6421f, 4.4f, 0f);
            ContentScale = new Vector3(1.0473f, 1f, 1f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(32.5674f, -1f, 0f);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            float w = WindowWidth - PAD * 2f;
            float cy = PAD * 0.5f;
            float rh = 16f;
            float gap = 6f;

            MakeLabel(contentRoot, new Rect(PAD, cy, w, rh),
                "Round info.json path", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            cy += rh + gap;

            // path field + browse on the same row
            float browseW = 56f;
            float fieldW = w - browseW - 4f;
            _pathField = UGUIShip.CreateInputField(contentRoot,
                new Rect(PAD, cy, fieldW, rh + 4f),
                "C:\\path\\to\\info.json",
                new Color(0.12f, 0.12f, 0.12f, 1f),
                WHITE,
                FS_SM);
            UGUIShip.SetInputText(_pathField, SettingsService.Get(PATH_KEY, ""), false);
            _pathField.onEndEdit.AddListener(new Action<string>(v =>
            {
                SettingsService.Set(PATH_KEY, v ?? "");
                RefreshLoadLabel();
            }));

            UGUIShip.CreateButton(contentRoot,
                new Rect(PAD + fieldW + 4f, cy, browseW, rh + 4f),
                "BROWSE", BTN_BROWSE, WHITE, FS_SM,
                new Action(OnBrowse));
            cy += rh + 4f + gap;

            MakeSeparator(contentRoot, new Rect(PAD, cy, w, 1f));
            cy += 1f + gap;

            _statusLabel = MakeLabel(contentRoot, new Rect(PAD, cy, w, rh),
                UnityRoundLoader.HasSpawned ? $"loaded: {UnityRoundLoader.Spawned?.name}" : "",
                FS_SM, UnityRoundLoader.HasSpawned ? OK_COL : HINT_COL);

            // load + unload bottom-right of the window
            var windowRt = contentRoot.parent?.GetComponent<RectTransform>();
            if (windowRt != null)
            {
                float bw = 70f;
                float bh = 20f;
                float bgap = 4f;

                var loadGo = new GameObject("LoadBtn");
                loadGo.transform.SetParent(windowRt, false);
                var lrt = loadGo.AddComponent<RectTransform>();
                lrt.anchorMin = lrt.anchorMax = new Vector2(1f, 0f);
                lrt.pivot = new Vector2(1f, 0f);
                lrt.sizeDelta = new Vector2(bw, bh);
                lrt.anchoredPosition = new Vector2(-PAD, PAD);
                _loadLabel = UGUIShip.CreateButton(loadGo.transform, new Rect(0f, 0f, bw, bh),
                    LoadLabel(), BTN_LOAD, WHITE, FS_SM, new Action(OnLoad))
                    .GetComponentInChildren<Text>();

                var unloadGo = new GameObject("UnloadBtn");
                unloadGo.transform.SetParent(windowRt, false);
                var urt = unloadGo.AddComponent<RectTransform>();
                urt.anchorMin = urt.anchorMax = new Vector2(1f, 0f);
                urt.pivot = new Vector2(1f, 0f);
                urt.sizeDelta = new Vector2(bw, bh);
                urt.anchoredPosition = new Vector2(-(PAD + bw + bgap), PAD);
                UGUIShip.CreateButton(unloadGo.transform, new Rect(0f, 0f, bw, bh),
                    "UNLOAD", BTN_UNLOAD, WHITE, FS_SM, new Action(OnUnload));

                // obstacle textures (bottom-left)
                var texGo = new GameObject("TexturesBtn");
                texGo.transform.SetParent(windowRt, false);
                var trt = texGo.AddComponent<RectTransform>();
                trt.anchorMin = trt.anchorMax = new Vector2(0f, 0f);
                trt.pivot = new Vector2(0f, 0f);
                trt.sizeDelta = new Vector2(80f, bh);
                trt.anchoredPosition = new Vector2(PAD, PAD);
                UGUIShip.CreateButton(texGo.transform, new Rect(0f, 0f, 80f, bh),
                    "TEXTURES", BTN_BROWSE, WHITE, FS_SM, new Action(OnTextures));
            }
        }

        // LoadBtn/UnloadBtn live outside the content root, clean them up on rebuild
        protected new void RebuildContent()
        {
            var windowRt = _contentRt?.parent?.GetComponent<RectTransform>();
            if (windowRt != null)
            {
                for (int i = windowRt.childCount - 1; i >= 0; i--)
                {
                    var c = windowRt.GetChild(i);
                    if (c != null && (c.name == "LoadBtn" || c.name == "UnloadBtn" || c.name == "TexturesBtn"))
                        UnityEngine.Object.Destroy(c.gameObject);
                }
            }
            base.RebuildContent();
        }

        // ── actions ───────────────────────────────────────────────────────────

        private void OnBrowse()
        {
            WinDialogs.PickFile("Select round info.json", path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                if (_pathField != null) UGUIShip.SetInputText(_pathField, path, false);
                SettingsService.Set(PATH_KEY, path);
                RefreshLoadLabel();
            });
        }

        private void OnLoad()
        {
            string path = _pathField != null ? _pathField.text : SettingsService.Get(PATH_KEY, "");
            SettingsService.Set(PATH_KEY, path ?? "");

            if (UnityRoundLoader.LoadFromInfoJson(path, out string error))
                SetStatus($"loaded: {UnityRoundLoader.Spawned?.name}", OK_COL);
            else
                SetStatus($"error: {error}", ERR_COL);

            RefreshLoadLabel();
        }

        private void OnUnload()
        {
            UnityRoundLoader.Unload();
            SetStatus("unloaded", HINT_COL);
            RefreshLoadLabel();
        }

        private void OnTextures()
        {
            if (ObstacleTextureWindow.Instance != null) { ObstacleTextureWindow.Instance.ShowWindow(); return; }

            var go = new GameObject("BetterFG_ObstacleTextureWindow");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<ObstacleTextureWindow>().Configure();
        }

        private void RefreshLoadLabel()
        {
            if (_loadLabel != null) _loadLabel.text = LoadLabel();
        }

        private void SetStatus(string text, Color col)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = text;
            _statusLabel.color = col;
        }
    }
}
