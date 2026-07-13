using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using BetterFG.Features.CreativeIncrements;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    // Creative-only tab. Subtabs: Load (pick a round info.json + load/unload), Custom Textures
    // (override obstacle textures), Args (player speed/gravity etc - not done yet). When you're not
    // in the level editor the whole body is just a "you're not in Creative" message, no subtabs.
    public class CreativeTab : BetterFGTab
    {
        public CreativeTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Creative";

        private static float PAD => UIScale.PAD;
        private static float VPAD => UIScale.VPAD;
        private static float SH => UIScale.SH;
        private static float BTN_H => UIScale.BTN_H;
        private static int FS => UIScale.FS;
        private static int FS_SM => UIScale.FS_SM;
        private static float subTabH => BTN_H * 0.9f;

        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color WHITE = Color.white;
        private static readonly Color OK = new Color(0.55f, 0.85f, 0.55f, 1f);
        private static readonly Color ERR = new Color(0.9f, 0.45f, 0.45f, 1f);
        private static readonly Color SEL_COLOR = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color BTN_BLUE = new Color(0.22f, 0.34f, 0.55f, 1f);
        private static readonly Color BTN_GREEN = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color BTN_RED = new Color(0.45f, 0.25f, 0.25f, 1f);
        private static readonly Color ROW_SEL = new Color(0.25f, 0.45f, 0.25f, 1f);
        private static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.04f);

        private const string PATH_KEY = "unityround.editor.jsonpath";
        private const string SHARE_URL_KEY = "unityround.editor.shareurl";
        private static float ROW_H => 26f * UIScale.S;

        private enum SubTab { Load, Textures, Args }
        private SubTab _sub = SubTab.Load;
        private Button _btnLoad, _btnTex, _btnArgs;
        private GameObject _bodyRoot, _loadPanel, _texPanel, _argsPanel, _notInCreative;

        // load panel
        private InputField _pathField;
        private Text _statusLabel;
        // share-code section: paste an info.json url, get the owner/repo/round_xxx string to put in
        // your level's description so it loads while playing online.
        private InputField _shareUrlField;
        private Text _shareStatus;

        // args panel
        private InputField _incMinField, _incStepField, _incMaxField, _incSpeedField;
        private Button _incToggle, _batchToggle;

        // textures panel
        private List<string> _texNames = new List<string>();
        private int _texSelected = -1;
        private Text _texStatus;
        private RectTransform _texContentRt;

        private static Texture2D _bgTex;
        private static Texture2D _hoverTex;
        private GameObject _bgHoverGo;
        private bool _wasInEditor;

        // ── background (same as the other tabs) ───────────────────────────────

        private static Texture2D LoadTex(string resource, ref Texture2D cache)
        {
            if (cache != null) return cache;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream(resource);
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.wrapMode = TextureWrapMode.Clamp;
                cache = tex;
            }
            catch (Exception ex) { Plugin.Log.LogError("BetterFG: Tex load failed: " + ex.Message); }
            return cache;
        }

        protected override void BuildBackground(RectTransform root)
        {
            var bgTex = LoadTex("BetterFG.assets.ui.tab.menu.png", ref _bgTex);
            if (bgTex == null) return;

            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(root, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            bgRt.localScale = new Vector3(1.5015f, 1.3502f, 1f);
            bgRt.localPosition = new Vector3(267.7578f, 285.8921f, 0);
            var raw = bgGo.AddComponent<RawImage>();
            raw.texture = bgTex;
            raw.raycastTarget = false;

            var hoverTex = LoadTex("BetterFG.assets.ui.bg_hover.png", ref _hoverTex);
            if (hoverTex != null)
            {
                var hoverGo = new GameObject("BG_Hover");
                hoverGo.transform.SetParent(bgGo.transform, false);
                var hoverRt = hoverGo.AddComponent<RectTransform>();
                hoverRt.anchorMin = Vector2.zero;
                hoverRt.anchorMax = Vector2.one;
                hoverRt.offsetMin = hoverRt.offsetMax = Vector2.zero;
                hoverGo.AddComponent<RawImage>().texture = hoverTex;
                hoverGo.SetActive(false);
                _bgHoverGo = hoverGo;
            }
        }

        protected override void OnTitleHoverChanged(bool hovering)
        {
            if (_bgHoverGo != null) _bgHoverGo.SetActive(hovering);
        }

        // ── build ─────────────────────────────────────────────────────────────

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            // subtab bar
            float third = (w - PAD) / 3f;
            _btnLoad = UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, third, subTabH),
                "Load", _sub == SubTab.Load ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetSub(SubTab.Load)));
            _btnTex = UGUIShip.CreateButton(contentRoot, new Rect(PAD + third + PAD * 0.5f, y, third, subTabH),
                "Custom Textures", _sub == SubTab.Textures ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetSub(SubTab.Textures)));
            _btnArgs = UGUIShip.CreateButton(contentRoot, new Rect(PAD + (third + PAD * 0.5f) * 2f, y, third, subTabH),
                "Args", _sub == SubTab.Args ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetSub(SubTab.Args)));
            y += subTabH + SH;

            UGUIShip.CreatePanel(contentRoot, new Rect(PAD, y, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            y += 1f + SH;

            float bodyH = TabHeight - y - VPAD;

            _bodyRoot = MakePanel(contentRoot, y, bodyH);

            _loadPanel = MakePanel(_bodyRoot.GetComponent<RectTransform>(), 0f, bodyH);
            BuildLoadPanel(_loadPanel.GetComponent<RectTransform>(), w, bodyH);

            _texPanel = MakePanel(_bodyRoot.GetComponent<RectTransform>(), 0f, bodyH);
            BuildTexPanel(_texPanel.GetComponent<RectTransform>(), w, bodyH);

            _argsPanel = MakePanel(_bodyRoot.GetComponent<RectTransform>(), 0f, bodyH);
            BuildArgsPanel(_argsPanel.GetComponent<RectTransform>(), w, bodyH);

            // "not in creative" message, shown over everything when out of the editor
            _notInCreative = MakePanel(contentRoot, y, bodyH);
            UGUIShip.CreateLabel(_notInCreative.transform, new Rect(PAD, bodyH * 0.5f - 12f, w, 24f),
                "You are not in Creative right now....", FS, HINT, TextAnchor.MiddleCenter);

            Refresh();
        }

        GameObject MakePanel(RectTransform parent, float y, float h)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(rt, new Rect(0f, y, TabWidth, h));
            return go;
        }

        // external nav entry point (e.g. BatchEditWindow's "increment settings" link) — same pattern
        // as EmoticonsPhrasesTab.ShowEmotesSubTab / UITab.ShowScreenSubTab.
        public void ShowArgsSubTab() => SetSub(SubTab.Args);

        void SetSub(SubTab sub)
        {
            _sub = sub;
            UGUIShip.SetButtonSelected(_btnLoad, sub == SubTab.Load, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnTex, sub == SubTab.Textures, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnArgs, sub == SubTab.Args, SEL_COLOR);
            Refresh();
        }

        void Refresh()
        {
            bool inEditor = UnityRoundLoader.InLevelEditor;

            // out of creative: hide the subtab bar + body entirely, just show the message
            _btnLoad.gameObject.SetActive(inEditor);
            _btnTex.gameObject.SetActive(inEditor);
            _btnArgs.gameObject.SetActive(inEditor);
            _bodyRoot.SetActive(inEditor);
            _notInCreative.SetActive(!inEditor);

            if (!inEditor) return;
            _loadPanel.SetActive(_sub == SubTab.Load);
            _texPanel.SetActive(_sub == SubTab.Textures);
            _argsPanel.SetActive(_sub == SubTab.Args);
        }

        void Update()
        {
            if (UnityRoundLoader.InLevelEditor != _wasInEditor)
            {
                _wasInEditor = UnityRoundLoader.InLevelEditor;
                if (_wasInEditor) RescanTextures();
                Refresh();
            }

            // texture subtab hotkeys: Enter picks a png for the selected row, Shift saves it out
            if (!IsOpen || !UnityRoundLoader.InLevelEditor || _sub != SubTab.Textures) return;
            if (_texSelected < 0 || _texSelected >= _texNames.Count) return;

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                string texName = _texNames[_texSelected];
                WinDialogs.PickPng("Select PNG for " + texName, path =>
                {
                    if (string.IsNullOrEmpty(path)) { SetTexStatus("cancelled", HINT); return; }
                    if (ObstacleTextureLoader.SetOverride(texName, path, out string error)) SetTexStatus("set " + texName, OK);
                    else SetTexStatus("error: " + error, ERR);
                    RebuildTexRows();
                });
            }

            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift))
            {
                string texName = _texNames[_texSelected];
                if (ObstacleTextureLoader.SaveTexturePng(texName, out string path, out string error)) SetTexStatus("saved " + Path.GetFileName(path), OK);
                else SetTexStatus("error: " + error, ERR);
            }
        }

        // ── Load panel ──────────────────────────────────────────────────────────

        void BuildLoadPanel(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            float rh = UIScale.LH;

            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh), "Round info.json path", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            cy += rh + SH;

            float browseW = 70f * UIScale.S;
            float fieldW = w - browseW - PAD;
            _pathField = UGUIShip.CreateInputField(root.transform, new Rect(PAD, cy, fieldW, BTN_H),
                "C:\\path\\to\\info.json", new Color(0.12f, 0.12f, 0.12f, 1f), WHITE, FS_SM);
            UGUIShip.SetInputText(_pathField, SettingsService.Get(PATH_KEY, ""), false);
            _pathField.onEndEdit.AddListener(new Action<string>(v => SettingsService.Set(PATH_KEY, v ?? "")));

            UGUIShip.CreateButton(root.transform, new Rect(PAD + fieldW + PAD, cy, browseW, BTN_H),
                "BROWSE", BTN_BLUE, WHITE, FS_SM, new Action(OnBrowse));
            cy += BTN_H + SH;

            // load / unload right under the path field
            float bw = (w - PAD) / 2f;
            UGUIShip.CreateButton(root.transform, new Rect(PAD, cy, bw, BTN_H), "LOAD", BTN_GREEN, WHITE, FS, new Action(OnLoad));
            UGUIShip.CreateButton(root.transform, new Rect(PAD + bw + PAD * 0.5f, cy, bw, BTN_H), "UNLOAD", BTN_RED, WHITE, FS, new Action(OnUnload));
            cy += BTN_H + SH;

            _statusLabel = UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh),
                UnityRoundLoader.HasSpawned ? $"loaded: {UnityRoundLoader.Spawned?.name}" : "",
                FS_SM, UnityRoundLoader.HasSpawned ? OK : HINT);
            cy += rh + SH * 2f;

            UGUIShip.CreatePanel(root.transform, new Rect(PAD, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + SH * 2f;

            // ── publish: paste your level's github info.json link, write it into the description ──
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh), "Publish (put your level's info.json on github, paste link)", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            cy += rh + SH;

            _shareUrlField = UGUIShip.CreateInputField(root.transform, new Rect(PAD, cy, w, BTN_H),
                "paste the github link to your level's info.json",
                new Color(0.12f, 0.12f, 0.12f, 1f), WHITE, FS_SM);
            UGUIShip.SetInputText(_shareUrlField, SettingsService.Get(SHARE_URL_KEY, ""), false);
            _shareUrlField.onEndEdit.AddListener(new Action<string>(v => SettingsService.Set(SHARE_URL_KEY, v ?? "")));
            cy += BTN_H + SH;

            UGUIShip.CreateButton(root.transform, new Rect(PAD, cy, w, BTN_H),
                "SET LEVEL DESCRIPTION", BTN_BLUE, WHITE, FS_SM, new Action(OnSetDescription));
            cy += BTN_H + SH;

            _shareStatus = UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh * 2f), "", FS_SM, HINT);
        }

        void OnSetDescription()
        {
            string url = _shareUrlField != null ? _shareUrlField.text : SettingsService.Get(SHARE_URL_KEY, "");
            SettingsService.Set(SHARE_URL_KEY, url ?? "");

            string code = ShareCodeFromUrl(url);
            if (string.IsNullOrEmpty(code)) { SetShareStatus("bad link — need a github link to a Rounds/round_xxx/info.json", ERR); return; }

            if (UnityRoundLoader.SetLevelDescription(code, out string error))
                SetShareStatus("description set! save your level and it'll load for everyone who plays it", OK);
            else
                SetShareStatus("couldn't set description: " + error, ERR);
        }

        void SetShareStatus(string text, Color col)
        {
            if (_shareStatus == null) return;
            _shareStatus.text = text;
            _shareStatus.color = col;
        }

        // turn a github link to a level's info.json into the owner/repo/round_xxx description code.
        // accepts both the browser link (github.com/owner/repo/blob/branch/Rounds/round_xxx/info.json)
        // and the raw link (raw.githubusercontent.com/owner/repo/branch/Rounds/round_xxx/info.json).
        static string ShareCodeFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            url = url.Trim().Trim('"');

            // chop off whichever github host prefix is there, leaving owner/repo/.../round_xxx/...
            string rest = null;
            foreach (var host in new[] { "raw.githubusercontent.com/", "github.com/" })
            {
                int i = url.IndexOf(host, StringComparison.OrdinalIgnoreCase);
                if (i >= 0) { rest = url.Substring(i + host.Length); break; }
            }
            if (rest == null) return "";

            var parts = rest.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return "";
            string owner = parts[0];
            string repo = parts[1];

            // grab whatever segment is the round_ folder, so blob/branch/Rounds in between don't matter
            string round = null;
            foreach (var p in parts)
                if (p.StartsWith("round_", StringComparison.OrdinalIgnoreCase)) { round = p; break; }
            if (string.IsNullOrEmpty(round)) return "";

            return $"{owner}/{repo}/{round}";
        }

        void OnBrowse()
        {
            WinDialogs.PickFile("Select round info.json", path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                UGUIShip.SetInputText(_pathField, path, false);
                SettingsService.Set(PATH_KEY, path);
            });
        }

        void OnLoad()
        {
            string path = _pathField != null ? _pathField.text : SettingsService.Get(PATH_KEY, "");
            SettingsService.Set(PATH_KEY, path ?? "");
            if (UnityRoundLoader.LoadFromInfoJson(path, out string error))
            {
                SetStatus($"loaded: {UnityRoundLoader.Spawned?.name}", OK);
                RescanTextures();
            }
            else SetStatus($"error: {error}", ERR);
        }

        void OnUnload()
        {
            UnityRoundLoader.UnloadAndForget();
            SetStatus("unloaded", HINT);
        }

        void SetStatus(string text, Color col)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = text;
            _statusLabel.color = col;
        }

        // ── Args panel ──────────────────────────────────────────────────────────

        // creative increment override. toggle + a step amount + a max. it generates 0..max in
        // that step procedurally and retargets the node's value closure so it actually applies.
        void BuildArgsPanel(RectTransform root, float w, float bodyH)
        {
            float cy = SH;
            float rh = UIScale.LH;

            float tglW = 70f * UIScale.S;

            // batch edit on/off — gates the multi-select nav prompt + Batch Edit window in the editor
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w - tglW - PAD, BTN_H), "Batch edit (multi-select)", FS_SM, WHITE, TextAnchor.MiddleLeft);
            bool batchOn = Windows.Creative.BatchEditWindow.FeatureEnabled;
            _batchToggle = UGUIShip.CreateButton(root.transform, new Rect(PAD + w - tglW, cy, tglW, BTN_H),
                batchOn ? "ON" : "OFF", batchOn ? BTN_GREEN : BTN_DARK, WHITE, FS_SM, new Action(() =>
                {
                    bool next = !Windows.Creative.BatchEditWindow.FeatureEnabled;
                    Windows.Creative.BatchEditWindow.FeatureEnabled = next;
                    UGUIShip.SetButtonSelected(_batchToggle, next, BTN_GREEN);
                    var lbl = _batchToggle.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = next ? "ON" : "OFF";
                }));
            cy += BTN_H + SH * 2f;

            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh), "Parameter increments", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            cy += rh + SH;

            // on/off toggle
            float tbw = 70f * UIScale.S;
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w - tbw - PAD, BTN_H), "Override editor steps", FS_SM, WHITE, TextAnchor.MiddleLeft);
            bool on = CreativeIncrements.Enabled;
            _incToggle = UGUIShip.CreateButton(root.transform, new Rect(PAD + w - tbw, cy, tbw, BTN_H),
                on ? "ON" : "OFF", on ? BTN_GREEN : BTN_DARK, WHITE, FS_SM, new Action(() =>
                {
                    bool next = !CreativeIncrements.Enabled;
                    CreativeIncrements.Enabled = next;
                    UGUIShip.SetButtonSelected(_incToggle, next, BTN_GREEN);
                    var lbl = _incToggle.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = next ? "ON" : "OFF";
                }));
            cy += BTN_H + SH * 2f;

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float thirdW = (w - PAD * 2f) / 3f;

            // min / increment / max across
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, thirdW, rh), "Min", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            UGUIShip.CreateLabel(root.transform, new Rect(PAD + (thirdW + PAD), cy, thirdW, rh), "Increment", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            UGUIShip.CreateLabel(root.transform, new Rect(PAD + (thirdW + PAD) * 2f, cy, thirdW, rh), "Max", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            cy += rh + SH;

            _incMinField = UGUIShip.CreateInputField(root.transform, new Rect(PAD, cy, thirdW, BTN_H),
                "0.25", new Color(0.12f, 0.12f, 0.12f, 1f), WHITE, FS_SM);
            UGUIShip.SetInputText(_incMinField, CreativeIncrements.Min.ToString(ci), false);
            _incMinField.onEndEdit.AddListener(new Action<string>(v =>
            {
                if (float.TryParse(v, System.Globalization.NumberStyles.Float, ci, out var f)) CreativeIncrements.Min = f;
                UGUIShip.SetInputText(_incMinField, CreativeIncrements.Min.ToString(ci), false);
            }));

            _incStepField = UGUIShip.CreateInputField(root.transform, new Rect(PAD + (thirdW + PAD), cy, thirdW, BTN_H),
                "0.25", new Color(0.12f, 0.12f, 0.12f, 1f), WHITE, FS_SM);
            UGUIShip.SetInputText(_incStepField, CreativeIncrements.Step.ToString(ci), false);
            _incStepField.onEndEdit.AddListener(new Action<string>(v =>
            {
                if (float.TryParse(v, System.Globalization.NumberStyles.Float, ci, out var f)) CreativeIncrements.Step = f;
                UGUIShip.SetInputText(_incStepField, CreativeIncrements.Step.ToString(ci), false);
            }));

            _incMaxField = UGUIShip.CreateInputField(root.transform, new Rect(PAD + (thirdW + PAD) * 2f, cy, thirdW, BTN_H),
                "10", new Color(0.12f, 0.12f, 0.12f, 1f), WHITE, FS_SM);
            UGUIShip.SetInputText(_incMaxField, CreativeIncrements.Max.ToString(ci), false);
            _incMaxField.onEndEdit.AddListener(new Action<string>(v =>
            {
                if (float.TryParse(v, System.Globalization.NumberStyles.Float, ci, out var f)) CreativeIncrements.Max = f;
                UGUIShip.SetInputText(_incMaxField, CreativeIncrements.Max.ToString(ci), false);
            }));
            cy += BTN_H + SH * 2f;

            // increment speed = nav cooldown (lower = scrolls faster when held)
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh), "Increment speed (lower = faster)", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            cy += rh + SH;

            _incSpeedField = UGUIShip.CreateInputField(root.transform, new Rect(PAD, cy, thirdW, BTN_H),
                "0.2", new Color(0.12f, 0.12f, 0.12f, 1f), WHITE, FS_SM);
            UGUIShip.SetInputText(_incSpeedField, CreativeIncrements.Speed.ToString(ci), false);
            _incSpeedField.onEndEdit.AddListener(new Action<string>(v =>
            {
                if (float.TryParse(v, System.Globalization.NumberStyles.Float, ci, out var f)) CreativeIncrements.Speed = f;
                UGUIShip.SetInputText(_incSpeedField, CreativeIncrements.Speed.ToString(ci), false);
            }));
            cy += BTN_H + SH;

            UGUIShip.CreateLabel(root.transform, new Rect(PAD, cy, w, rh * 2f),
                "reopen a parameter menu to apply. generates 0 to max in your step.", FS_SM, HINT);
        }

        // ── Custom Textures panel ───────────────────────────────────────────────

        void BuildTexPanel(RectTransform root, float w, float bodyH)
        {
            float rh = UIScale.LH;
            UGUIShip.CreateLabel(root.transform, new Rect(PAD, SH, w, rh),
                "Obstacle textures  (Enter=set PNG, Shift=save PNG)", FS_SM, new Color(1f, 1f, 1f, 0.72f));

            float listY = SH + rh + SH;
            float listH = bodyH - listY - BTN_H - PAD;

            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(root.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(scrollRt, new Rect(PAD, listY, w, listH));
            scrollGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var vpGo = new GameObject("Viewport");
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRt = vpGo.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = vpRt.offsetMax = Vector2.zero;
            vpGo.AddComponent<RectMask2D>();
            scroll.viewport = vpRt;

            var listGo = new GameObject("List");
            listGo.transform.SetParent(vpGo.transform, false);
            _texContentRt = listGo.AddComponent<RectTransform>();
            _texContentRt.anchorMin = new Vector2(0f, 1f);
            _texContentRt.anchorMax = new Vector2(1f, 1f);
            _texContentRt.pivot = new Vector2(0.5f, 1f);
            _texContentRt.offsetMin = _texContentRt.offsetMax = Vector2.zero;
            var layout = listGo.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 0f;
            listGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _texContentRt;

            float refreshW = 80f * UIScale.S;
            _texStatus = UGUIShip.CreateLabel(root.transform, new Rect(PAD, bodyH - BTN_H, w - refreshW - PAD, BTN_H), "", FS_SM, HINT);
            UGUIShip.CreateButton(root.transform, new Rect(PAD + w - refreshW, bodyH - BTN_H, refreshW, BTN_H),
                "REFRESH", BTN_BLUE, WHITE, FS_SM, new Action(() => { RescanTextures(); SetTexStatus($"found {_texNames.Count}", HINT); }));

            RebuildTexRows();
        }

        void RescanTextures()
        {
            _texNames = ObstacleTextureLoader.DiscoverTextureNames();
            if (_texSelected >= _texNames.Count) _texSelected = -1;
            RebuildTexRows();
        }

        void RebuildTexRows()
        {
            if (_texContentRt == null) return;
            for (int i = _texContentRt.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_texContentRt.GetChild(i).gameObject);

            if (_texNames.Count == 0)
            {
                UGUIShip.CreateLabel(_texContentRt, new Rect(PAD, 0f, TabWidth, ROW_H), "no placeable textures found — load a round first", FS_SM, HINT);
                return;
            }
            for (int i = 0; i < _texNames.Count; i++)
                BuildTexRow(i);
        }

        void BuildTexRow(int idx)
        {
            string texName = _texNames[idx];
            bool hasOverride = ObstacleTextureLoader.Overrides.ContainsKey(texName);
            bool isSel = idx == _texSelected;

            var rowGo = new GameObject("Row_" + idx);
            rowGo.transform.SetParent(_texContentRt, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = isSel ? ROW_SEL : (idx % 2 == 0 ? ROW_EVEN : new Color(0f, 0f, 0f, 0f));

            int captured = idx;
            var btn = rowGo.AddComponent<Button>();
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(new Action(() => { _texSelected = _texSelected == captured ? -1 : captured; RebuildTexRows(); }));

            float w = TabWidth - PAD * 2f;
            float thumb = ROW_H - 4f;
            var thumbGo = new GameObject("Thumb");
            thumbGo.transform.SetParent(rowGo.transform, false);
            var thumbRt = thumbGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(thumbRt, new Rect(4f, 2f, thumb, thumb));
            var raw = thumbGo.AddComponent<RawImage>();
            raw.raycastTarget = false;
            var liveTex = ObstacleTextureLoader.GetCurrentTexture(texName);
            if (liveTex != null) raw.texture = liveTex;
            else raw.color = new Color(0f, 0f, 0f, 0.4f);

            float nameX = 4f + thumb + 6f;
            float rmW = 24f * UIScale.S;
            UGUIShip.CreateLabel(rowGo.transform, new Rect(nameX, 0f, w - rmW - nameX, ROW_H),
                (hasOverride ? "● " : "") + texName, FS_SM, hasOverride ? OK : WHITE);

            if (hasOverride)
                UGUIShip.CreateButton(rowGo.transform, new Rect(w - rmW, 2f, rmW - 4f, ROW_H - 4f), "x", BTN_RED, WHITE, FS_SM, new Action(() =>
                {
                    ObstacleTextureLoader.RemoveOverride(texName);
                    SetTexStatus("removed " + texName, HINT);
                    RebuildTexRows();
                }));
        }

        void SetTexStatus(string text, Color col)
        {
            if (_texStatus == null) return;
            _texStatus.text = text;
            _texStatus.color = col;
        }
    }
}
