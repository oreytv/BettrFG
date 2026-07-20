using System;
using System.IO;
using System.Reflection;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Menu;
using BetterFG.Services;
using BetterFG.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    public class UITab : BetterFGTab
    {
        public UITab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "User Interface";

        private static float PAD => UIScale.PAD;
        private static float VPAD => UIScale.VPAD;
        private static float LH => UIScale.LH;
        private static float SH => UIScale.SH;
        private static float BTN_H => UIScale.BTN_H;
        private static int FS => UIScale.FS;
        private static int FS_SM => UIScale.FS_SM;
        private static float subTabH => BTN_H * 0.9f;
        // height of rows inside the dropdown popout (the list that appears after clicking).
        // shorter than the header button so the list itself feels lighter.
        private static float dropdownRowH => BTN_H * 0.8f;

        // ── Sub-tabs ──────────────────────────────────────────────────────────
        private enum SubTab { Foreground, Font, Screen, Scaling }
        private SubTab _sub = SubTab.Foreground;
        private Button _btnSubFg, _btnSubFont, _btnSubScreen, _btnSubScaling;
        private GameObject _fgPanelGo, _fontPanelGo, _screenPanelGo, _scalingPanelGo;
        private GameObject _fgBtnRowGo;

        // ── Foreground "what to customise" picker ─────────────────────────────
        private enum FgWhat { CustomUI, Qualified, Eliminated, EliminatedSquad, Winner, RoundOver }
        private FgWhat _fgWhat = FgWhat.CustomUI;
        private GameObject _fgSelectorRowGo;
        private GameObject _fgCustomBodyGo, _fgQualBodyGo, _fgElimBodyGo, _fgSquadBodyGo, _fgWinBodyGo, _fgRoundBodyGo;
        private GameObject _bannerPreviewGo;

        private struct CachedImgColor { public Image img; public Color orig; public bool isHighlight; }
        private struct CachedTmpColor { public TMPro.TMP_Text tmp; public Color origFill; public Color origOutline; public Color origUnderlay; public bool hasOutline; public bool hasUnderlay; }
        private System.Collections.Generic.List<CachedImgColor> _previewImgCache = new System.Collections.Generic.List<CachedImgColor>();
        private System.Collections.Generic.List<CachedTmpColor> _previewTmpCache = new System.Collections.Generic.List<CachedTmpColor>();

        // ── Scaling subtab state ──────────────────────────────────────────────
        private int _edgesIdx = 1; // 0=Soft, 1=Default, 2=Hard
        private float _canvasScale = 1.3333f;
        private InputField _scaleInput;
        private Slider _scaleSlider;
        private Button _btnEdgeSoft, _btnEdgeDefault, _btnEdgeHard;
        private Button _btnCanvasScaleEnabled;
        private const string KEY_CANVAS_EDGES = "ui.canvas.edges";
        private const string KEY_CANVAS_SCALE = "ui.canvas.scale";
        private const string KEY_CANVAS_SCALE_ENABLED = "ui.canvas.scale.enabled";
        private static readonly float[] EdgesValues = { 125f, 100f, 85f };

        // ── Screen subtab state ───────────────────────────────────────────────
        private ScreenBackgroundService.Screen _screenSel = ScreenBackgroundService.Screen.FallForce;
        // the falling screen (lobby bg) isn't a ScreenBackgroundService.Screen — it recolours the
        // named blue-slot images in Menu_Screen_Lobby, not a gradient backdrop. it's a fifth dropdown
        // entry with its own body. this flag says the dropdown currently has it selected.
        private bool _fallingSel;
        private float _scTopR, _scTopG, _scTopB;
        private float _scBotR = 1f, _scBotG = 1f, _scBotB = 1f;
        private float _scBias, _scSmooth = 1f;
        private bool _scEnabled;
        private string _scPattern = "";
        private RawImage _scGradPreview;
        private Button _scEnabledBtn;
        private Text _scPatternLabel;
        private GameObject _screenBodyGo;
        private RectTransform _screenBodyParent;
        private float _screenBodyW, _screenBodyH;

        // ── Falling screen (lobby bg) slot colours ────────────────────────────
        private bool _lbEnabled;
        private float _lbSlot0R, _lbSlot0G, _lbSlot0B;
        private float _lbSlot1R, _lbSlot1G, _lbSlot1B;
        private float _lbSlot2R, _lbSlot2G, _lbSlot2B;
        private Image _lbSwatch0, _lbSwatch1, _lbSwatch2;
        private Button _lbEnabledBtn;

        // ── Font subtab state ─────────────────────────────────────────────────
        private System.Collections.Generic.List<FontOverride> _fontEntries =
            new System.Collections.Generic.List<FontOverride>();
        private int _fontSel = -1;
        private bool _fontMaster;

        private RectTransform _fontListContent;
        private Button _btnFontMaster;
        private Text _fontStatusLbl;

        // edit form refs
        private GameObject _fontFormGo;
        private bool _fontEditMode;
        private InputField _fontNameField;
        private Text _fontPathLbl;
        private string _fontFormPath = "";
        private System.Collections.Generic.List<string> _fontTargetNames =
            new System.Collections.Generic.List<string>();
        private Button _fontTargetDropdown;
        private RectTransform _fontTargetParent;
        private float _fontTargetX, _fontTargetY, _fontTargetW;
        private int _fontTargetIdx;
        private TMPro.TextMeshProUGUI _fontPreviewTmp;
        private Text _fontFormTitleLbl;
        private Button _fontConfirmBtn;

        private const float FONT_ROW_H = 18f;
        private const float FONT_FORM_H = 124f;
        private static readonly Color ROW_ON = new Color(0.14f, 0.14f, 0.14f, 1f);
        private static readonly Color ROW_OFF = new Color(0.08f, 0.08f, 0.08f, 1f);
        private static readonly Color BTN_EDIT_OPEN = new Color(0.45f, 0.38f, 0.1f, 1f); // dark yellow = form open on this row

        // tick WinDialogs so the file-picker callback fires
        void Update() => WinDialogs.Tick();

        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color WHITE = Color.white;
        private static readonly Color BTN_APPLY = new Color(0.45f, 0.35f, 0.25f, 1f);
        private static readonly Color BTN_REMOVE = new Color(0.55f, 0.15f, 0.15f, 1f);
        private static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color SEL_COLOR = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color BTN_ON = new Color(0.25f, 0.5f, 0.25f, 1f);

        // ── Textures ──────────────────────────────────────────────────────────
        private static Texture2D _bgTex;
        private static Texture2D _hoverTex;
        private GameObject _bgHoverGo;

        // screen overlay images (fallforce shared by fallforce + loading level)
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> _screenSprites =
            new System.Collections.Generic.Dictionary<string, Sprite>();

        private static Sprite ScreenSprite(ScreenBackgroundService.Screen s)
        {
            string file;
            switch (s)
            {
                case ScreenBackgroundService.Screen.FinalRound: file = "final"; break;
                case ScreenBackgroundService.Screen.Explore: file = "explore"; break;
                default: file = "fallforce"; break; // FallForce + LoadingLevel
            }
            return ScreenSpriteByName(file);
        }

        // load an assets/ui/uiscreen/<file>.png sprite by stem — the falling screen isn't a
        // ScreenBackgroundService.Screen so it grabs its overlay through here directly.
        private static Sprite ScreenSpriteByName(string file)
        {
            if (_screenSprites.TryGetValue(file, out var cached) && cached != null) return cached;
            Sprite sp = null;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("BetterFG.assets.ui.uiscreen." + file + ".png");
                if (stream != null)
                {
                    var bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    tex.LoadImage(bytes);
                    tex.wrapMode = TextureWrapMode.Clamp;
                    sp = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                }
            }
            catch (Exception ex) { Plugin.Log.LogError("UITab: screen sprite load failed: " + ex.Message); }
            _screenSprites[file] = sp;
            return sp;
        }

        // ── State: foreground ─────────────────────────────────────────────────
        private bool _fgCyanOn;
        private float _fgCyanR = 0f, _fgCyanG = 0.3f, _fgCyanB = 1f;
        private bool _fgBlackOn;
        private float _fgBlackR = 0.75f, _fgBlackG = 0.75f, _fgBlackB = 0.75f;
        private bool _fgYellowOn;
        private float _fgYellowR = 1f, _fgYellowG = 0.5f, _fgYellowB = 0f;
        private bool _fgBlueOn;
        private float _fgBlueR = 0.1f, _fgBlueG = 0.25f, _fgBlueB = 0.85f;
        private bool _fgPinkOn;
        private float _fgPinkR = 1f, _fgPinkG = 0.2f, _fgPinkB = 0.5f;
        private bool _fgOrangeOn;
        private float _fgOrangeR = 1f, _fgOrangeG = 0.55f, _fgOrangeB = 0.1f;

        private Button _btnCyanOn, _btnBlackOn, _btnYellowOn, _btnBlueOn, _btnPinkOn, _btnOrangeOn;
        private Image _swatchCyan, _swatchBlack, _swatchYellow, _swatchBlue, _swatchPink, _swatchOrange;
        private Image _fgCyanAreaBg, _fgBlackAreaBg, _fgYellowAreaBg, _fgBlueAreaBg, _fgPinkAreaBg, _fgOrangeAreaBg;

        // ── Embedded textures ─────────────────────────────────────────────────

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
            var bgTex = LoadTex("BetterFG.assets.ui.tab.ui.png", ref _bgTex);
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

        // ── Build ─────────────────────────────────────────────────────────────

        protected override void BuildContent(RectTransform contentRoot)
        {
            LoadSettings();

            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            // subtab bar
            float quarterTab = (w - PAD * 0.5f * 3f) / 4f;
            float qGap = PAD * 0.5f;
            _btnSubFg = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD, y, quarterTab, subTabH), "Foreground",
                _sub == SubTab.Foreground ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() => SetSubTab(SubTab.Foreground)));
            _btnSubFont = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD + (quarterTab + qGap) * 1f, y, quarterTab, subTabH), "Font",
                _sub == SubTab.Font ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() => SetSubTab(SubTab.Font)));
            _btnSubScreen = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD + (quarterTab + qGap) * 2f, y, quarterTab, subTabH), "Background",
                _sub == SubTab.Screen ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() => SetSubTab(SubTab.Screen)));
            _btnSubScaling = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD + (quarterTab + qGap) * 3f, y, quarterTab, subTabH), "Scaling",
                _sub == SubTab.Scaling ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() => SetSubTab(SubTab.Scaling)));
            y += subTabH + SH;

            UGUIShip.CreatePanel(contentRoot, new Rect(PAD, y, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            y += 1f + SH;

            float btnRowH = BTN_H + PAD * 2f + 1f;
            float bodyH = TabHeight - y - VPAD - btnRowH;

            // ── Foreground panel: selector row + 3 swappable bodies + apply/remove ─
            _fgPanelGo = new GameObject("FgPanel");
            _fgPanelGo.transform.SetParent(contentRoot, false);
            var fgPanelRt = _fgPanelGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(fgPanelRt, new Rect(0f, y, TabWidth, bodyH));

            // selector dropdown sits at the top of the foreground panel (mirrors the Screen subtab).
            // SetPixelRect uses top-left origin with y inverted, so y=0 IS the top.
            float selectorH = BTN_H + SH + 1f + SH;
            _fgSelectorRowGo = new GameObject("FgSelectorRow");
            _fgSelectorRowGo.transform.SetParent(fgPanelRt, false);
            var selRt = _fgSelectorRowGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(selRt, new Rect(0f, 0f, TabWidth, selectorH));
            BuildFgSelector(selRt, PAD, 0f, w);

            float subBodyH = bodyH - selectorH;

            // CustomUI body — existing fg content, sits BELOW the selector
            _fgCustomBodyGo = new GameObject("FgCustomBody");
            _fgCustomBodyGo.transform.SetParent(fgPanelRt, false);
            var customRt = _fgCustomBodyGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(customRt, new Rect(0f, selectorH, TabWidth, subBodyH));
            BuildFgPanel(customRt, PAD, 0f, w, subBodyH);

            // Qualified body
            _fgQualBodyGo = new GameObject("FgQualBody");
            _fgQualBodyGo.transform.SetParent(fgPanelRt, false);
            var qualRt = _fgQualBodyGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(qualRt, new Rect(0f, selectorH, TabWidth, subBodyH));
            BuildBannerPanel(qualRt, PAD, 0f, w, subBodyH, FgWhat.Qualified);

            // Eliminated body
            _fgElimBodyGo = new GameObject("FgElimBody");
            _fgElimBodyGo.transform.SetParent(fgPanelRt, false);
            var elimRt = _fgElimBodyGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(elimRt, new Rect(0f, selectorH, TabWidth, subBodyH));
            BuildBannerPanel(elimRt, PAD, 0f, w, subBodyH, FgWhat.Eliminated);

            // EliminatedSquad body
            _fgSquadBodyGo = new GameObject("FgSquadBody");
            _fgSquadBodyGo.transform.SetParent(fgPanelRt, false);
            var squadRt = _fgSquadBodyGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(squadRt, new Rect(0f, selectorH, TabWidth, subBodyH));
            BuildBannerPanel(squadRt, PAD, 0f, w, subBodyH, FgWhat.EliminatedSquad);

            // Winner body
            _fgWinBodyGo = new GameObject("FgWinBody");
            _fgWinBodyGo.transform.SetParent(fgPanelRt, false);
            var winRt = _fgWinBodyGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(winRt, new Rect(0f, selectorH, TabWidth, subBodyH));
            BuildBannerPanel(winRt, PAD, 0f, w, subBodyH, FgWhat.Winner);

            // RoundOver body
            _fgRoundBodyGo = new GameObject("FgRoundBody");
            _fgRoundBodyGo.transform.SetParent(fgPanelRt, false);
            var roundRt = _fgRoundBodyGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(roundRt, new Rect(0f, selectorH, TabWidth, subBodyH));
            BuildBannerPanel(roundRt, PAD, 0f, w, subBodyH, FgWhat.RoundOver);

            // selector goes last in sibling order so its expanded dropdown list renders over the bodies
            _fgSelectorRowGo.transform.SetAsLastSibling();

            _fgBtnRowGo = new GameObject("FgBtnRow");
            _fgBtnRowGo.transform.SetParent(contentRoot, false);
            UGUIShip.SetPixelRect(_fgBtnRowGo.AddComponent<RectTransform>(), new Rect(0f, 0f, TabWidth, TabHeight));
            float by = y + bodyH + PAD;
            UGUIShip.CreatePanel(_fgBtnRowGo.transform, new Rect(PAD, by, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            by += 1f + PAD;
            float btnw = (w - PAD * 0.5f) / 2f;
            UGUIShip.CreateButton(_fgBtnRowGo.transform, new Rect(PAD, by, btnw, BTN_H),
                "Apply", BTN_APPLY, WHITE, FS, new Action(OnFgApply));
            UGUIShip.CreateButton(_fgBtnRowGo.transform, new Rect(PAD + btnw + PAD * 0.5f, by, btnw, BTN_H),
                "Remove", BTN_REMOVE, WHITE, FS, new Action(OnFgRemove));

            RefreshFgWhatVisibility();

            // ── Font panel ────────────────────────────────────────────────────
            _fontPanelGo = new GameObject("FontPanel");
            _fontPanelGo.transform.SetParent(contentRoot, false);
            var fontPanelRt = _fontPanelGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(fontPanelRt, new Rect(0f, y, TabWidth, bodyH + btnRowH));
            BuildFontPanel(fontPanelRt, PAD, 0f, w, bodyH + btnRowH);

            // ── Screen panel (selector + per-screen gradient/pattern + apply/remove) ──
            _screenPanelGo = new GameObject("ScreenPanel");
            _screenPanelGo.transform.SetParent(contentRoot, false);
            var screenPanelRt = _screenPanelGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(screenPanelRt, new Rect(0f, y, TabWidth, bodyH + btnRowH));
            BuildScreenPanel(screenPanelRt, PAD, 0f, w, bodyH, btnRowH);

            // ── Scaling panel ─────────────────────────────────────────────────
            _scalingPanelGo = new GameObject("ScalingPanel");
            _scalingPanelGo.transform.SetParent(contentRoot, false);
            var scalingPanelRt = _scalingPanelGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(scalingPanelRt, new Rect(0f, y, TabWidth, bodyH + btnRowH));
            BuildScalingPanel(scalingPanelRt, PAD, 0f, w, bodyH + btnRowH);

            RefreshSubTabVisibility();
        }

        // ── Sub-tab switching ─────────────────────────────────────────────────

        public void ShowScreenSubTab() => SetSubTab(SubTab.Screen);

        private void SetSubTab(SubTab sub)
        {
            _sub = sub;
            UGUIShip.SetButtonSelected(_btnSubFg, sub == SubTab.Foreground, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnSubFont, sub == SubTab.Font, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnSubScreen, sub == SubTab.Screen, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnSubScaling, sub == SubTab.Scaling, SEL_COLOR);
            RefreshSubTabVisibility();
        }

        private void RefreshSubTabVisibility()
        {
            if (_fgPanelGo != null) _fgPanelGo.SetActive(_sub == SubTab.Foreground);
            if (_fgBtnRowGo != null) _fgBtnRowGo.SetActive(_sub == SubTab.Foreground);
            if (_fontPanelGo != null) _fontPanelGo.SetActive(_sub == SubTab.Font);
            if (_screenPanelGo != null) _screenPanelGo.SetActive(_sub == SubTab.Screen);
            if (_scalingPanelGo != null) _scalingPanelGo.SetActive(_sub == SubTab.Scaling);
        }

        // ── Scaling panel ─────────────────────────────────────────────────────

        private void BuildScalingPanel(RectTransform parent, float x, float y, float w, float h)
        {
            LoadScalingSettings();

            var (_, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));
            float ew = w - 26f;
            float cy = PAD;

            // Edges
            UGUIShip.CreateLabel(content, new Rect(x, cy, ew, LH), "EDGES", FS_SM, HINT);
            cy += LH + SH;

            float btnW3 = (ew - PAD) / 3f;
            _btnEdgeSoft = UGUIShip.CreateButton(content, new Rect(x, cy, btnW3, BTN_H), "Soft",
                _edgesIdx == 0 ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetEdges(0)));
            _btnEdgeDefault = UGUIShip.CreateButton(content, new Rect(x + btnW3 + PAD * 0.5f, cy, btnW3, BTN_H), "Default",
                _edgesIdx == 1 ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetEdges(1)));
            _btnEdgeHard = UGUIShip.CreateButton(content, new Rect(x + (btnW3 + PAD * 0.5f) * 2f, cy, btnW3, BTN_H), "Hard",
                _edgesIdx == 2 ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetEdges(2)));
            cy += BTN_H + PAD;

            UGUIShip.CreatePanel(content, new Rect(x, cy, ew, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // row geometry shared by both scaling rows: [toggle][slider][value]
            float togW = BTN_H * 1.5f;
            float inputW = BTN_H * 1.9f;
            float sliderW = ew - togW - inputW - PAD * 2f;
            float inputH = LH;            // the value field sits vertically short, centred in the row
            float inputY = (BTN_H - inputH) * 0.5f;

            // ── Fall Guys UI scale ────────────────────────────────────────────────
            UGUIShip.CreateLabel(content, new Rect(x, cy, ew, LH), "FALL GUYS UI SCALE", FS_SM, HINT);
            cy += LH + SH;

            const float scaleMin = 0.6f, scaleMax = 1.6f;
            _canvasScale = Mathf.Clamp(_canvasScale, scaleMin, scaleMax);

            bool canvasOn = CanvasScaleEnabled;
            _btnCanvasScaleEnabled = UGUIShip.CreateButton(content, new Rect(x, cy, togW, BTN_H),
                canvasOn ? "ON" : "OFF", canvasOn ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    bool on = !CanvasScaleEnabled;
                    SettingsService.Set(KEY_CANVAS_SCALE_ENABLED, on ? "true" : "false");
                    var lbl = _btnCanvasScaleEnabled?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = on ? "ON" : "OFF";
                    var img = _btnCanvasScaleEnabled?.GetComponent<Image>();
                    if (img != null) img.color = on ? BTN_ON : BTN_DARK;
                    ApplyCanvasScale();
                }));

            _scaleSlider = UGUIShip.CreateSlider(content, x + togW + PAD, cy + (BTN_H - LH) * 0.5f, sliderW, "",
                Mathf.InverseLerp(scaleMin, scaleMax, _canvasScale), LH, PAD, FS_SM,
                new Action<float>(t =>
                {
                    float f = Mathf.Lerp(scaleMin, scaleMax, t);
                    _canvasScale = f;
                    SettingsService.Set(KEY_CANVAS_SCALE, f.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (_scaleInput != null) _scaleInput.text = f.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    ApplyCanvasScale();
                }), reserveLabel: false);

            _scaleInput = UGUIShip.CreateInputField(content, new Rect(x + togW + PAD + sliderW + PAD, cy + inputY, inputW, inputH),
                "1.33", Color.black, WHITE, FS_SM);
            _scaleInput.contentType = InputField.ContentType.DecimalNumber;
            _scaleInput.text = _canvasScale.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            _scaleInput.onEndEdit.AddListener(new Action<string>(v =>
            {
                if (float.TryParse(v, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float f))
                {
                    f = Mathf.Clamp(f, scaleMin, scaleMax);
                    _canvasScale = f;
                    SettingsService.Set(KEY_CANVAS_SCALE, f.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (_scaleInput != null) _scaleInput.text = f.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
                    ApplyCanvasScale();
                    if (_scaleSlider != null) _scaleSlider.value = Mathf.InverseLerp(scaleMin, scaleMax, f);
                }
            }));
            cy += BTN_H + PAD;

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void LoadScalingSettings()
        {
            _edgesIdx = int.TryParse(SettingsService.Get(KEY_CANVAS_EDGES, "1"), out int e) ? Mathf.Clamp(e, 0, 2) : 1;
            if (float.TryParse(SettingsService.Get(KEY_CANVAS_SCALE, "1.3333"),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float s) && s > 0f)
                _canvasScale = s;
            else
                _canvasScale = 1.3333f;
        }

        private void SetEdges(int idx)
        {
            _edgesIdx = idx;
            SettingsService.Set(KEY_CANVAS_EDGES, idx.ToString());
            UGUIShip.SetButtonSelected(_btnEdgeSoft, idx == 0, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnEdgeDefault, idx == 1, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnEdgeHard, idx == 2, SEL_COLOR);
            ApplyCanvasEdges();
        }

        private static Canvas GetUICanvas() =>
            GameObject.Find("UICanvas_Client_V2(Clone)")?.GetComponent<Canvas>();
        private static Canvas GetLobbyForegroundCanvas() =>
            GameObject.Find("Menu_Screen_Lobby(Clone)/ForegroundCanvas")?.GetComponent<Canvas>();
        private static Canvas GetNavOverlayCanvas() =>
            GameObject.Find("Prefab_UI_NavigationOverlay(Clone)")?.GetComponent<Canvas>();

        private void ApplyCanvasEdges()
        {
            float ppu = EdgesValues[_edgesIdx];
            var ui = GetUICanvas();        if (ui != null) ui.referencePixelsPerUnit = ppu;
            var lf = GetLobbyForegroundCanvas(); if (lf != null) lf.referencePixelsPerUnit = ppu;
            var no = GetNavOverlayCanvas();      if (no != null) no.referencePixelsPerUnit = ppu;
        }

        private static bool CanvasScaleEnabled => SettingsService.Get(KEY_CANVAS_SCALE_ENABLED, "false") == "true";

        // each canvas's untouched scaleFactor, captured the first time we see it so OFF restores
        // the real stock value (some resolutions / overlay canvases aren't 1f at rest).
        private static float? _stockMain, _stockLobbyFg, _stockNavOverlay;

        private static void ApplyOne(Canvas canvas, ref float? stock, float target)
        {
            if (canvas == null) return;
            if (stock == null) stock = canvas.scaleFactor;
            canvas.scaleFactor = CanvasScaleEnabled ? target : stock.Value;
        }

        private void ApplyCanvasScale()
        {
            ApplyOne(GetUICanvas(),               ref _stockMain,        _canvasScale);
            ApplyOne(GetLobbyForegroundCanvas(),  ref _stockLobbyFg,     _canvasScale);
            ApplyOne(GetNavOverlayCanvas(),       ref _stockNavOverlay,  _canvasScale);
        }

        public static void ApplyCanvasScalingFromSettings()
        {
            int edgeIdx = 1;
            if (int.TryParse(SettingsService.Get("ui.canvas.edges", "1"), out int e))
                edgeIdx = Mathf.Clamp(e, 0, 2);
            float ppu = EdgesValues[edgeIdx];

            float scale = 1.3333f;
            float.TryParse(SettingsService.Get("ui.canvas.scale", "1.3333"),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out scale);

            void Apply(Canvas c, ref float? stock)
            {
                if (c == null) return;
                c.referencePixelsPerUnit = ppu;
                if (stock == null) stock = c.scaleFactor;
                c.scaleFactor = CanvasScaleEnabled && scale > 0f ? scale : stock.Value;
            }

            Apply(GetUICanvas(),               ref _stockMain);
            Apply(GetLobbyForegroundCanvas(),  ref _stockLobbyFg);
            Apply(GetNavOverlayCanvas(),       ref _stockNavOverlay);
        }

        // ── Screen panel ──────────────────────────────────────────────────────

        private void BuildScreenPanel(RectTransform parent, float x, float y, float w, float bodyH, float btnRowH)
        {
            LoadScreenSettings(_screenSel);

            float cy = PAD;

            // screen selector dropdown — each option has its screen image as an overlay, text right-aligned
            var screens = new System.Collections.Generic.List<ScreenBackgroundService.Screen>
            {
                ScreenBackgroundService.Screen.FallForce,
                ScreenBackgroundService.Screen.LoadingLevel,
                ScreenBackgroundService.Screen.FinalRound,
                ScreenBackgroundService.Screen.Explore,
                ScreenBackgroundService.Screen.ShowSelector,
            };
            var opts = new System.Collections.Generic.List<string>();
            var initial = new System.Collections.Generic.List<bool>();
            var sprites = new System.Collections.Generic.List<Sprite>();
            foreach (var s in screens) { opts.Add(ScreenBackgroundService.Label(s)); initial.Add(_fallingSel ? false : s == _screenSel); sprites.Add(ScreenSprite(s)); }
            // falling screen (lobby bg) — the fifth entry
            opts.Add("Falling Screen"); initial.Add(_fallingSel); sprites.Add(ScreenSpriteByName("fallingscreen"));
            int fallingIdx = screens.Count;

            string HeaderLabel() => _fallingSel ? "Falling Screen" : ScreenBackgroundService.Label(_screenSel);

            Button screenDd = null;
            screenDd = UGUIShip.CreateMultiSelectDropdown(parent, new Rect(x, cy, w, BTN_H),
                HeaderLabel(), opts, initial,
                new Action<int, bool>((idx, _) =>
                {
                    if (idx < 0 || idx > fallingIdx) return;
                    _fallingSel = idx == fallingIdx;
                    if (!_fallingSel) _screenSel = screens[idx];
                    var lbl = screenDd?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = HeaderLabel();
                    var headImg = screenDd?.transform.Find("HeaderImg")?.GetComponent<Image>();
                    if (headImg != null) headImg.sprite = _fallingSel ? ScreenSpriteByName("fallingscreen") : ScreenSprite(_screenSel);
                    RebuildScreenBody();
                }), FS_SM, w, dropdownRowH, true, true, false, sprites, true);
            cy += BTN_H + SH;

            UGUIShip.CreatePanel(parent, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + SH;

            // body host — rebuilt whenever the selected screen changes
            _screenBodyParent = parent;
            _screenBodyW = w;
            _screenBodyH = bodyH - cy - PAD;
            float bodyY = cy;

            _screenBodyGo = new GameObject("ScreenBody");
            _screenBodyGo.transform.SetParent(parent, false);
            var bodyRt = _screenBodyGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(bodyRt, new Rect(0f, bodyY, TabWidth, _screenBodyH));
            BuildScreenBody(bodyRt, x, 0f, w, _screenBodyH);

            // apply / remove row pinned at the bottom
            float by = y + bodyH + PAD;
            UGUIShip.CreatePanel(parent, new Rect(PAD, by, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            by += 1f + PAD;
            float btnw = (w - PAD * 0.5f) / 2f;
            UGUIShip.CreateButton(parent, new Rect(PAD, by, btnw, BTN_H),
                "Apply", BTN_APPLY, WHITE, FS, new Action(() => { if (_fallingSel) OnFallingApply(); else OnScreenApply(); }));
            UGUIShip.CreateButton(parent, new Rect(PAD + btnw + PAD * 0.5f, by, btnw, BTN_H),
                "Remove", BTN_REMOVE, WHITE, FS, new Action(() => { if (_fallingSel) OnFallingRemove(); else OnScreenRemove(); }));
        }

        private void RebuildScreenBody()
        {
            if (_screenBodyGo == null) return;
            if (_fallingSel) LoadFallingSettings();
            else LoadScreenSettings(_screenSel);
            for (int i = _screenBodyGo.transform.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_screenBodyGo.transform.GetChild(i).gameObject);
            if (_fallingSel)
                BuildFallingBody(_screenBodyGo.GetComponent<RectTransform>(), PAD, 0f, _screenBodyW, _screenBodyH);
            else
                BuildScreenBody(_screenBodyGo.GetComponent<RectTransform>(), PAD, 0f, _screenBodyW, _screenBodyH);
        }

        private void BuildScreenBody(RectTransform parent, float x, float y, float w, float h)
        {
            var (scrollRect, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));

            float cy = PAD;

            // single on/off — for FallForce this also flips the menu BG visibility so showing
            // custom colours always means showing the custom BG. (other screens have no separate
            // BG concept.)
            bool isFallForce = _screenSel == ScreenBackgroundService.Screen.FallForce;
            _scEnabledBtn = UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H),
                _scEnabled ? "Show custom bg: ON" : "Show custom bg: OFF", _scEnabled ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _scEnabled = !_scEnabled;
                    SettingsService.Set(ScreenBackgroundService.KeyEnabled(_screenSel), _scEnabled ? "true" : "false");
                    if (isFallForce)
                    {
                        SettingsService.Set(MenuCustomizationApplication.KEY_BG_ENABLED, _scEnabled ? "true" : "false");
                        MenuCustomizationApplication.Instance?.SetMenuBgEnabled(_scEnabled);
                    }
                    var lbl = _scEnabledBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _scEnabled ? "Show custom bg: ON" : "Show custom bg: OFF";
                    var img = _scEnabledBtn?.GetComponent<Image>();
                    if (img != null) img.color = _scEnabled ? BTN_ON : BTN_DARK;
                    ApplyScreenLive();
                }));
            cy += BTN_H + SH;
            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // gradient preview on the right, colour sliders on the left
            float previewW = w * 0.28f;
            float slidersW = w - previewW - PAD * 2f;

            float previewStartY = cy;
            var previewGo = new GameObject("ScGradPreview");
            previewGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(previewGo.AddComponent<RectTransform>(),
                new Rect(x + slidersW + PAD * 2f, cy, previewW, (LH + SH) * 6f + LH));
            previewGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.3f);
            var previewTexGo = new GameObject("ScGradTex");
            previewTexGo.transform.SetParent(previewGo.transform, false);
            var ptRt = previewTexGo.AddComponent<RectTransform>();
            ptRt.anchorMin = Vector2.zero; ptRt.anchorMax = Vector2.one;
            ptRt.offsetMin = ptRt.offsetMax = Vector2.zero;
            _scGradPreview = previewTexGo.AddComponent<RawImage>();
            _scGradPreview.raycastTarget = false;
            RefreshScreenPreview();

            // top color
            UGUIShip.CreateLabel(content, new Rect(x, cy, slidersW, LH), "TOP COLOR", FS_SM, HINT);
            cy += LH + SH;
            UGUIShip.CreateColorControls(content, x, ref cy, slidersW,
                () => _scTopR, () => _scTopG, () => _scTopB,
                v => _scTopR = v, v => _scTopG = v, v => _scTopB = v, () => RefreshScreenPreview(), out _, out _, out _);

            // bottom color
            UGUIShip.CreateLabel(content, new Rect(x, cy, slidersW, LH), "BOTTOM COLOR", FS_SM, HINT);
            cy += LH + SH;
            UGUIShip.CreateColorControls(content, x, ref cy, slidersW,
                () => _scBotR, () => _scBotG, () => _scBotB,
                v => _scBotR = v, v => _scBotG = v, v => _scBotB = v, () => RefreshScreenPreview(), out _, out _, out _);

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // shader (texture bake) params
            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "GRADIENT SHAPE", FS_SM, HINT);
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "Bias", _scBias, -1f, 1f, v => { _scBias = v; RefreshScreenPreview(); });
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "Smooth", _scSmooth, 0.1f, 8f, v => { _scSmooth = v; RefreshScreenPreview(); });
            cy += LH + PAD;

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // circles pattern
            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "CIRCLES PATTERN", FS_SM, HINT);
            cy += LH + SH;
            float patBtnW = BTN_H * 2.5f, resetW = BTN_H * 2f;
            float patLblW = w - patBtnW - resetW - PAD * 2f;
            _scPatternLabel = UGUIShip.CreateLabel(content, new Rect(x, cy, patLblW, BTN_H),
                string.IsNullOrEmpty(_scPattern) ? "none" : System.IO.Path.GetFileName(_scPattern),
                FS_SM, HINT, TextAnchor.MiddleLeft);
            UGUIShip.CreateButton(content, new Rect(x + patLblW + PAD, cy, patBtnW, BTN_H),
                "Browse", BTN_DARK, WHITE, FS_SM,
                new Action(() => WinDialogs.PickPng("Select pattern PNG", path =>
                {
                    if (string.IsNullOrEmpty(path)) return;
                    _scPattern = path;
                    SettingsService.Set(ScreenBackgroundService.KeyPattern(_screenSel), path);
                    if (_scPatternLabel != null) _scPatternLabel.text = System.IO.Path.GetFileName(path);
                })));
            UGUIShip.CreateButton(content, new Rect(x + patLblW + PAD + patBtnW + PAD, cy, resetW, BTN_H),
                "Reset", BTN_REMOVE, WHITE, FS_SM,
                new Action(() =>
                {
                    _scPattern = "";
                    SettingsService.Remove(ScreenBackgroundService.KeyPattern(_screenSel));
                    if (_scPatternLabel != null) _scPatternLabel.text = "none";
                    if (_screenSel == ScreenBackgroundService.Screen.FallForce)
                        MenuCustomizationApplication.Instance?.RestorePattern();
                }));
            cy += BTN_H + PAD;

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void RefreshScreenPreview()
        {
            if (_scGradPreview == null) return;
            const int W = 4, H = 64;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var top = new Color(_scTopR, _scTopG, _scTopB);
            var bot = new Color(_scBotR, _scBotG, _scBotB);
            for (int row = 0; row < H; row++)
            {
                float t = row / (float)(H - 1);
                float s = Mathf.Clamp01(t + _scBias * 0.5f);
                s = Mathf.Pow(s, Mathf.Max(0.1f, _scSmooth));
                var c = Color.Lerp(bot, top, s);
                for (int col = 0; col < W; col++) tex.SetPixel(col, row, c);
            }
            tex.Apply();
            _scGradPreview.texture = tex;
        }

        private void LoadScreenSettings(ScreenBackgroundService.Screen s)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float def) =>
                float.TryParse(SettingsService.Get(key, def.ToString(ci)), System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            _scTopR = P(ScreenBackgroundService.KeyTopR(s), 0f);
            _scTopG = P(ScreenBackgroundService.KeyTopG(s), 0f);
            _scTopB = P(ScreenBackgroundService.KeyTopB(s), 0f);
            _scBotR = P(ScreenBackgroundService.KeyBotR(s), 1f);
            _scBotG = P(ScreenBackgroundService.KeyBotG(s), 1f);
            _scBotB = P(ScreenBackgroundService.KeyBotB(s), 1f);
            _scBias = P(ScreenBackgroundService.KeyBias(s), 0f);
            _scSmooth = P(ScreenBackgroundService.KeySmooth(s), 1f);
            _scEnabled = SettingsService.Get(ScreenBackgroundService.KeyEnabled(s), "false") == "true";
            _scPattern = SettingsService.Get(ScreenBackgroundService.KeyPattern(s), "");
        }

        private void OnScreenApply()
        {
            var s = _screenSel;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            void S(string k, float v) => SettingsService.Set(k, v.ToString(ci));
            S(ScreenBackgroundService.KeyTopR(s), _scTopR);
            S(ScreenBackgroundService.KeyTopG(s), _scTopG);
            S(ScreenBackgroundService.KeyTopB(s), _scTopB);
            S(ScreenBackgroundService.KeyBotR(s), _scBotR);
            S(ScreenBackgroundService.KeyBotG(s), _scBotG);
            S(ScreenBackgroundService.KeyBotB(s), _scBotB);
            S(ScreenBackgroundService.KeyBias(s), _scBias);
            S(ScreenBackgroundService.KeySmooth(s), _scSmooth);
            SettingsService.Set(ScreenBackgroundService.KeyEnabled(s), _scEnabled ? "true" : "false");

            ApplyScreenLive();
        }

        // push the selected screen's current state to whatever is showing right now (live preview).
        // FallForce = the menu/title; loading screens = the active loading screen if one is up.
        private void ApplyScreenLive()
        {
            var s = _screenSel;
            if (s == ScreenBackgroundService.Screen.FallForce)
            {
                // bg.enabled is independent from custom colours — show/hide the BG GO from that.
                // _scEnabled (colour customisation) only decides whether to push our gradient onto
                // the BG mat or revert to defaults.
                bool bgOn = SettingsService.Get(MenuCustomizationApplication.KEY_BG_ENABLED, "false") == "true";
                MenuCustomizationApplication.Instance?.SetMenuBgEnabled(bgOn);
                if (_scEnabled)
                {
                    MenuCustomizationApplication.Instance?.ApplyGradient(
                        new Color(_scTopR, _scTopG, _scTopB), new Color(_scBotR, _scBotG, _scBotB), _scBias, _scSmooth);
                    MenuCustomizationApplication.Instance?.ApplyPatternFromSettings();
                }
                else
                {
                    // revert menu gradient + pattern to default
                    MenuCustomizationApplication.Instance?.ApplyGradient(Color.black, Color.white, 0f, 1f);
                    MenuCustomizationApplication.Instance?.RestorePattern();
                }
            }
            else if (s == ScreenBackgroundService.Screen.ShowSelector)
            {
                // its own live path — the selector isn't a loading screen
                BetterFG.Patches.ShowSelectorBg.ReapplyLive();
            }
            else
            {
                // ReapplyActive runs ApplyUnder, which applies when enabled and reverts when not
                BetterFG.Patches.LoadingScreenBg.ReapplyActive();
            }
        }

        private void OnScreenRemove()
        {
            var s = _screenSel;
            foreach (var k in new[]
            {
                ScreenBackgroundService.KeyTopR(s), ScreenBackgroundService.KeyTopG(s), ScreenBackgroundService.KeyTopB(s),
                ScreenBackgroundService.KeyBotR(s), ScreenBackgroundService.KeyBotG(s), ScreenBackgroundService.KeyBotB(s),
                ScreenBackgroundService.KeyBias(s), ScreenBackgroundService.KeySmooth(s),
                ScreenBackgroundService.KeyEnabled(s), ScreenBackgroundService.KeyPattern(s),
            })
                SettingsService.Remove(k);

            _scTopR = _scTopG = _scTopB = 0f;
            _scBotR = _scBotG = _scBotB = 1f;
            _scBias = 0f; _scSmooth = 1f; _scEnabled = false; _scPattern = "";

            if (s == ScreenBackgroundService.Screen.FallForce)
            {
                MenuCustomizationApplication.Instance?.ApplyGradient(Color.black, Color.white, 0f, 1f);
                MenuCustomizationApplication.Instance?.RestorePattern();
            }
            RebuildScreenBody();
        }

        // ── Falling screen (lobby bg) body ────────────────────────────────────
        // recolours the named DarkBlue/MedBlue/LightBlue images in Menu_Screen_Lobby. moved here out
        // of the Main Menu tab so the falling-screen colours live with the other screens.

        private void SyncLbSwatches()
        {
            if (_lbSwatch0 != null) _lbSwatch0.color = new Color(_lbSlot0R, _lbSlot0G, _lbSlot0B);
            if (_lbSwatch1 != null) _lbSwatch1.color = new Color(_lbSlot1R, _lbSlot1G, _lbSlot1B);
            if (_lbSwatch2 != null) _lbSwatch2.color = new Color(_lbSlot2R, _lbSlot2G, _lbSlot2B);
        }

        private void BuildFallingBody(RectTransform parent, float x, float y, float w, float h)
        {
            var (scrollRect, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));

            float cy = PAD;

            _lbEnabledBtn = UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H),
                _lbEnabled ? "Custom colours: ON" : "Custom colours: OFF", _lbEnabled ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _lbEnabled = !_lbEnabled;
                    SettingsService.Set(MenuCustomizationApplication.KEY_LOBBYBG_ENABLED, _lbEnabled ? "true" : "false");
                    var lbl = _lbEnabledBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _lbEnabled ? "Custom colours: ON" : "Custom colours: OFF";
                    var img = _lbEnabledBtn?.GetComponent<Image>();
                    if (img != null) img.color = _lbEnabled ? BTN_ON : BTN_DARK;
                    ApplyFallingLive();
                }));
            cy += BTN_H + SH;
            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            float swatchW = BTN_H;
            float lbSliderW = w - swatchW - PAD;

            // dark blue
            UGUIShip.CreateLabel(content, new Rect(x, cy, lbSliderW, LH), "DARK BLUE", FS_SM, HINT);
            cy += LH + SH;
            var s0go = new GameObject("LbSwatch0");
            s0go.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(s0go.AddComponent<RectTransform>(), new Rect(x + lbSliderW + PAD, cy, swatchW, (LH + SH) * 3f - SH));
            _lbSwatch0 = s0go.AddComponent<Image>();
            _lbSwatch0.color = new Color(_lbSlot0R, _lbSlot0G, _lbSlot0B);
            UGUIShip.CreateColorControls(content, x, ref cy, lbSliderW,
                () => _lbSlot0R, () => _lbSlot0G, () => _lbSlot0B,
                v => _lbSlot0R = v, v => _lbSlot0G = v, v => _lbSlot0B = v, () => SyncLbSwatches(), out _, out _, out _);

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // med blue
            UGUIShip.CreateLabel(content, new Rect(x, cy, lbSliderW, LH), "MED BLUE", FS_SM, HINT);
            cy += LH + SH;
            var s1go = new GameObject("LbSwatch1");
            s1go.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(s1go.AddComponent<RectTransform>(), new Rect(x + lbSliderW + PAD, cy, swatchW, (LH + SH) * 3f - SH));
            _lbSwatch1 = s1go.AddComponent<Image>();
            _lbSwatch1.color = new Color(_lbSlot1R, _lbSlot1G, _lbSlot1B);
            UGUIShip.CreateColorControls(content, x, ref cy, lbSliderW,
                () => _lbSlot1R, () => _lbSlot1G, () => _lbSlot1B,
                v => _lbSlot1R = v, v => _lbSlot1G = v, v => _lbSlot1B = v, () => SyncLbSwatches(), out _, out _, out _);

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // light blue
            UGUIShip.CreateLabel(content, new Rect(x, cy, lbSliderW, LH), "LIGHT BLUE", FS_SM, HINT);
            cy += LH + SH;
            var s2go = new GameObject("LbSwatch2");
            s2go.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(s2go.AddComponent<RectTransform>(), new Rect(x + lbSliderW + PAD, cy, swatchW, (LH + SH) * 3f - SH));
            _lbSwatch2 = s2go.AddComponent<Image>();
            _lbSwatch2.color = new Color(_lbSlot2R, _lbSlot2G, _lbSlot2B);
            UGUIShip.CreateColorControls(content, x, ref cy, lbSliderW,
                () => _lbSlot2R, () => _lbSlot2G, () => _lbSlot2B,
                v => _lbSlot2R = v, v => _lbSlot2G = v, v => _lbSlot2B = v, () => SyncLbSwatches(), out _, out _, out _);

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void LoadFallingSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float def) =>
                float.TryParse(SettingsService.Get(key, def.ToString(ci)), System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            _lbEnabled = SettingsService.Get(MenuCustomizationApplication.KEY_LOBBYBG_ENABLED, "false") == "true";
            _lbSlot0R = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_R, 0f);
            _lbSlot0G = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_G, 0f);
            _lbSlot0B = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_B, 1f);
            _lbSlot1R = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_R, 0f);
            _lbSlot1G = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_G, 0.5f);
            _lbSlot1B = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_B, 1f);
            _lbSlot2R = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_R, 0.8f);
            _lbSlot2G = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_G, 0.8f);
            _lbSlot2B = P(MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_B, 1f);
        }

        private void OnFallingApply()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            void S(string k, float v) => SettingsService.Set(k, v.ToString(ci));
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_R, _lbSlot0R);
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_G, _lbSlot0G);
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_B, _lbSlot0B);
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_R, _lbSlot1R);
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_G, _lbSlot1G);
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_B, _lbSlot1B);
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_R, _lbSlot2R);
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_G, _lbSlot2G);
            S(MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_B, _lbSlot2B);
            SettingsService.Set(MenuCustomizationApplication.KEY_LOBBYBG_ENABLED, _lbEnabled ? "true" : "false");
            ApplyFallingLive();
        }

        private void ApplyFallingLive()
        {
            if (_lbEnabled)
                MenuCustomizationApplication.Instance?.ApplyLobbyBgCustomColors(
                    new Color(_lbSlot0R, _lbSlot0G, _lbSlot0B),
                    new Color(_lbSlot1R, _lbSlot1G, _lbSlot1B),
                    new Color(_lbSlot2R, _lbSlot2G, _lbSlot2B));
            else
                MenuCustomizationApplication.Instance?.RevertLobbyBGForeground();
        }

        private void OnFallingRemove()
        {
            foreach (var k in new[]
            {
                MenuCustomizationApplication.KEY_LOBBYBG_ENABLED,
                MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_R, MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_G, MenuCustomizationApplication.KEY_LOBBYBG_SLOT0_B,
                MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_R, MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_G, MenuCustomizationApplication.KEY_LOBBYBG_SLOT1_B,
                MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_R, MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_G, MenuCustomizationApplication.KEY_LOBBYBG_SLOT2_B,
            })
                SettingsService.Remove(k);

            _lbEnabled = false;
            _lbSlot0R = 0f; _lbSlot0G = 0f; _lbSlot0B = 1f;
            _lbSlot1R = 0f; _lbSlot1G = 0.5f; _lbSlot1B = 1f;
            _lbSlot2R = 0.8f; _lbSlot2G = 0.8f; _lbSlot2B = 1f;
            MenuCustomizationApplication.Instance?.RevertLobbyBGForeground();
            RebuildScreenBody();
        }

        // ── Font panel (entry-based) ───────────────────────────────────────────

        private void BuildFontPanel(RectTransform parent, float x, float y, float w, float h)
        {
            _fontEntries = FontReplacementService.LoadAll();
            _fontMaster = FontReplacementService.MasterOn;

            float cy = PAD;

            // master on/off
            _btnFontMaster = UGUIShip.CreateButton(parent, new Rect(x, cy, w, BTN_H),
                _fontMaster ? "FONT REPLACEMENT: ON" : "FONT REPLACEMENT: OFF",
                _fontMaster ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(OnToggleFontMaster));
            cy += BTN_H + SH;

            UGUIShip.CreateButton(parent, new Rect(x, cy, w, BTN_H),
                "+ Add Font Override", new Color(0.3f, 0.3f, 0.15f, 1f), WHITE, FS, new Action(OnAddFontEntry));
            cy += BTN_H + 2f;

            // status pinned to the very bottom, form sits just above it, entry list fills the rest.
            float statusY = h - PAD - LH;
            float formY = statusY - SH - FONT_FORM_H;
            float listH = formY - 2f - cy;
            if (listH < FONT_ROW_H * 2f) listH = FONT_ROW_H * 2f;

            // entry list — fills the gap between the add button and the form (no dead space)
            var scroll = UGUIShip.CreateScrollView(parent, new Rect(x, cy, w, listH));
            _fontListContent = scroll.content;
            var vlg = _fontListContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.spacing = 2f;
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            _fontListContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            // edit form (hidden until add/edit), pinned above the status line
            _fontFormGo = BuildFontForm(parent, x, formY, w);
            _fontFormGo.SetActive(false);

            _fontStatusLbl = UGUIShip.CreateLabel(parent, new Rect(x, statusY, w, LH), "", FS_SM, HINT, TextAnchor.MiddleCenter);

            RefreshFontList();
        }

        private void OnToggleFontMaster()
        {
            _fontMaster = !_fontMaster;
            var lbl = _btnFontMaster?.GetComponentInChildren<Text>();
            if (lbl != null) lbl.text = _fontMaster ? "FONT REPLACEMENT: ON" : "FONT REPLACEMENT: OFF";
            UGUIShip.SetButtonSelected(_btnFontMaster, _fontMaster, SEL_COLOR);
            FontReplacementService.SetMaster(_fontMaster);
            FontReplacementService.RebuildAndApply();
        }

        private void RefreshFontList()
        {
            if (_fontListContent == null) return;
            for (int i = _fontListContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_fontListContent.GetChild(i).gameObject);

            if (_fontEntries.Count == 0)
            {
                UGUIShip.CreateLabel(_fontListContent, new Rect(6f, 0f, TabWidth, FONT_ROW_H),
                    "no overrides — click + Add", FS_SM, HINT, TextAnchor.MiddleLeft);
                return;
            }

            for (int i = 0; i < _fontEntries.Count; i++)
            {
                int idx = i;
                var entry = _fontEntries[i];

                var rowGo = new GameObject("FRow_" + i);
                rowGo.transform.SetParent(_fontListContent, false);
                rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, FONT_ROW_H);
                var le = rowGo.AddComponent<LayoutElement>();
                le.preferredHeight = FONT_ROW_H;
                le.flexibleWidth = 1f;
                var rowImg = rowGo.AddComponent<Image>();
                rowImg.color = entry.enabled ? ROW_ON : ROW_OFF;
                var btnSpr = UGUIShip.GetButtonSprite();
                if (btnSpr != null) { rowImg.sprite = btnSpr; rowImg.type = Image.Type.Simple; }

                float editW = 26f, toggleW = 26f, removeW = 20f;
                float nameW = TabWidth - PAD * 2f - editW - toggleW - removeW - 12f;
                string rowName = entry.entryName + "  →  " +
                    (string.IsNullOrEmpty(entry.targetFontName) ? "(no target)" : entry.targetFontName);
                UGUIShip.CreateLabel(rowGo.transform, new Rect(4f, 0f, nameW, FONT_ROW_H),
                    rowName, FS_SM, entry.enabled ? WHITE : HINT, TextAnchor.MiddleLeft);

                bool editOpen = _fontFormGo != null && _fontFormGo.activeSelf && _fontEditMode && _fontSel == idx;
                BuildFontRowBtn(rowGo.transform, -(removeW + toggleW + editW + 4f), editW, "edit",
                    editOpen ? BTN_EDIT_OPEN : BTN_DARK, () => OnEditFontEntry(idx));
                BuildFontRowBtn(rowGo.transform, -(removeW + toggleW + 2f), toggleW,
                    entry.enabled ? "on" : "off", entry.enabled ? BTN_APPLY : BTN_DARK,
                    () => OnToggleFontEntry(idx));
                BuildFontRowBtn(rowGo.transform, -2f, removeW, "x", BTN_REMOVE,
                    () => OnRemoveFontEntry(idx));
            }
        }

        private void BuildFontRowBtn(Transform parent, float anchoredX, float bw, string label,
            Color bg, Action onClick)
        {
            var go = new GameObject("FRBtn_" + label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(anchoredX, 0f);
            rt.sizeDelta = new Vector2(bw, FONT_ROW_H - 4f);
            var img = go.AddComponent<Image>();
            img.color = bg;
            var btnSpr = UGUIShip.GetButtonSprite();
            if (btnSpr != null) { img.sprite = btnSpr; img.type = Image.Type.Simple; }
            var btn = go.AddComponent<Button>();
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(new Action(onClick));
            UGUIShip.WireButtonAudio(go);
            UGUIShip.CreateLabel(go.transform, new Rect(0f, 0f, bw, FONT_ROW_H - 4f), label,
                FS_SM - 1, WHITE, TextAnchor.MiddleCenter);
        }

        private void OnToggleFontEntry(int idx)
        {
            _fontEntries[idx].enabled = !_fontEntries[idx].enabled;
            FontReplacementService.SaveAll(_fontEntries);
            RefreshFontList();
            FontReplacementService.RebuildAndApply();
        }

        private void OnRemoveFontEntry(int idx)
        {
            _fontEntries.RemoveAt(idx);
            FontReplacementService.SaveAll(_fontEntries);
            RefreshFontList();
            FontReplacementService.RebuildAndApply();
        }

        // ── Font edit form ──────────────────────────────────────────────────────

        private GameObject BuildFontForm(RectTransform parent, float x, float y, float w)
        {
            var formGo = new GameObject("FontForm");
            formGo.transform.SetParent(parent, false);
            var form = formGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(form, new Rect(x, y, w, FONT_FORM_H));
            var lp = form.localPosition; lp.y = -170f; form.localPosition = lp;
            formGo.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.65f);

            float fx = 4f, fy = 4f, fw = w - 8f;
            float previewSz = 54f;
            float leftW = fw - previewSz - 6f;

            _fontFormTitleLbl = UGUIShip.CreateLabel(form, new Rect(fx, fy, leftW, LH), "Name", FS_SM, HINT);
            fy += LH;

            _fontNameField = UGUIShip.CreateInputField(form, new Rect(fx, fy, leftW, BTN_H),
                "my font override", Color.black, WHITE, FS_SM);
            fy += BTN_H + 2f;

            float bw = 60f;
            UGUIShip.CreateButton(form, new Rect(fx, fy, bw, BTN_H), "Browse",
                BTN_DARK, WHITE, FS_SM, new Action(OnFontBrowse));
            _fontPathLbl = UGUIShip.CreateLabel(form, new Rect(fx + bw + 4f, fy, leftW - bw - 4f, BTN_H),
                "no file", FS_SM, HINT, TextAnchor.MiddleLeft);

            // preview box (top-right, shows the picked font on sample text)
            var prevGo = new GameObject("FontPreview");
            prevGo.transform.SetParent(form, false);
            UGUIShip.SetPixelRect(prevGo.AddComponent<RectTransform>(),
                new Rect(fx + fw - previewSz, 4f + LH, previewSz, previewSz + BTN_H));
            prevGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);
            var prevTmpGo = new GameObject("PreviewTmp");
            prevTmpGo.transform.SetParent(prevGo.transform, false);
            var prevRt = prevTmpGo.AddComponent<RectTransform>();
            prevRt.anchorMin = Vector2.zero; prevRt.anchorMax = Vector2.one;
            prevRt.offsetMin = new Vector2(3f, 3f); prevRt.offsetMax = new Vector2(-3f, -3f);
            _fontPreviewTmp = prevTmpGo.AddComponent<TMPro.TextMeshProUGUI>();
            _fontPreviewTmp.fontSize = 14f;
            _fontPreviewTmp.alignment = TMPro.TextAlignmentOptions.Center;
            _fontPreviewTmp.enableWordWrapping = true;
            _fontPreviewTmp.text = "Abg 123";
            _fontPreviewTmp.raycastTarget = false;
            fy += BTN_H + 2f;

            UGUIShip.CreateLabel(form, new Rect(fx, fy, fw, LH), "Replaces game font:", FS_SM, HINT);
            fy += LH;
            _fontTargetParent = form;
            _fontTargetX = fx; _fontTargetY = fy; _fontTargetW = fw;
            fy += BTN_H + 2f;

            _fontConfirmBtn = UGUIShip.CreateButton(form, new Rect(fx, fy, fw, BTN_H),
                "Add", new Color(0.3f, 0.3f, 0.15f, 1f), WHITE, FS_SM, new Action(OnConfirmFontForm));

            return formGo;
        }

        // only these two TMP_FontAssets are actually used in-game — the rest exist in memory but
        // never render. real game name → user-facing label.
        private static readonly (string real, string display)[] FontWhitelist =
        {
            ("TitanOne-Expanded SDF (Title)", "Titan One"),
            ("Asap-Bold SDF (Body)", "Asap"),
        };

        private void RefreshFontTargetDropdown(string selectedName)
        {
            if (_fontTargetParent == null) return;
            if (_fontTargetDropdown != null) Destroy(_fontTargetDropdown.gameObject);

            // _fontTargetNames stays as the REAL game names (what we save/apply against). the
            // dropdown options show the friendly display names instead, and each row's label gets
            // its actual game font swapped in below so the user sees what the font looks like.
            _fontTargetNames = new System.Collections.Generic.List<string>();
            var displayNames = new System.Collections.Generic.List<string>();
            var fontAssets = new System.Collections.Generic.List<TMPro.TMP_FontAsset>();
            foreach (var pair in FontWhitelist)
            {
                var fa = FontReplacementService.GetFontAssetByName(pair.real);
                if (fa == null) continue; // not loaded yet (open in menu first)
                _fontTargetNames.Add(pair.real);
                displayNames.Add(pair.display);
                fontAssets.Add(fa);
            }

            var opts = new System.Collections.Generic.List<string>();
            if (displayNames.Count == 0) opts.Add("(open the menu first)");
            else opts.AddRange(displayNames);

            int sel = string.IsNullOrEmpty(selectedName) ? 0 : _fontTargetNames.IndexOf(selectedName);
            _fontTargetIdx = Mathf.Clamp(sel < 0 ? 0 : sel, 0, Mathf.Max(0, opts.Count - 1));
            var initial = new System.Collections.Generic.List<bool>();
            for (int i = 0; i < opts.Count; i++) initial.Add(i == _fontTargetIdx);

            Button btn = null;
            TMPro.TextMeshProUGUI headerTmp = null;
            btn = UGUIShip.CreateMultiSelectDropdown(_fontTargetParent,
                new Rect(_fontTargetX, _fontTargetY, _fontTargetW, BTN_H), opts[_fontTargetIdx], opts, initial,
                new Action<int, bool>((idx, _) =>
                {
                    _fontTargetIdx = idx;
                    // update OUR TMP overlay (not the underlying UGUI Text — writing to that would
                    // bring back the default-font version on top of our preview)
                    if (headerTmp != null && idx >= 0 && idx < displayNames.Count && idx < fontAssets.Count)
                    {
                        headerTmp.font = fontAssets[idx];
                        headerTmp.text = displayNames[idx];
                    }
                }), FS_SM, _fontTargetW, 20f, true, true, true);
            _fontTargetDropdown = btn;

            // overlay TMP labels on every row + the header, each rendered in the row's actual game
            // font. mark them protected so the user's own font swap can't replace them.
            if (fontAssets.Count > 0)
                headerTmp = StyleFontDropdown(btn, displayNames, fontAssets);
        }

        // walk a freshly-built CreateMultiSelectDropdown and replace each row's plain Text with a
        // TextMeshProUGUI using the matching game font. originals are kept but blanked so the layout
        // (checkmark, hover, click) stays intact. returns the header TMP so the onToggle can repaint it.
        private TMPro.TextMeshProUGUI StyleFontDropdown(Button header,
            System.Collections.Generic.List<string> displayNames,
            System.Collections.Generic.List<TMPro.TMP_FontAsset> fonts)
        {
            if (header == null || header.transform == null) return null;
            var parent = header.transform.parent;
            if (parent == null) return null;

            // header — replace its Text with TMP using the currently-selected row's font
            int headIdx = Mathf.Clamp(_fontTargetIdx, 0, fonts.Count - 1);
            var headerTmp = ReplaceTextWithTmp(header.GetComponentInChildren<Text>(), displayNames[headIdx], fonts[headIdx]);

            // rows live under "MSPanel" sibling of the header
            Transform panel = null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var ch = parent.GetChild(i);
                if (ch != null && ch.name == "MSPanel") { panel = ch; break; }
            }
            if (panel == null) return headerTmp;

            for (int i = 0; i < panel.childCount && i < fonts.Count; i++)
            {
                var rowGo = panel.GetChild(i);
                if (rowGo == null) continue;
                // each row's first label is the option text (the second is the checkmark).
                var labels = rowGo.GetComponentsInChildren<Text>(true);
                if (labels == null || labels.Length == 0) continue;
                ReplaceTextWithTmp(labels[0], displayNames[i], fonts[i]);
            }
            return headerTmp;
        }

        private static TMPro.TextMeshProUGUI ReplaceTextWithTmp(Text src, string text, TMPro.TMP_FontAsset font)
        {
            if (src == null || font == null) return null;
            var go = src.gameObject;
            var align = src.alignment;
            // hide the underlying UGUI label hard — clearing .text isn't enough because other code
            // (the dropdown's own onToggle callback) writes the label back into it, which would
            // re-render in the default font on top of our TMP overlay.
            src.enabled = false;

            var tmpGo = new GameObject("TmpLabel");
            tmpGo.transform.SetParent(go.transform, false);
            var trt = tmpGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            var tmp = tmpGo.AddComponent<TMPro.TextMeshProUGUI>();
            tmp.font = font;
            tmp.text = text;
            tmp.fontSize = src.fontSize + 2; // TMP looks slightly smaller at the same px
            tmp.color = src.color;
            tmp.raycastTarget = false;
            tmp.alignment = align == TextAnchor.MiddleRight ? TMPro.TextAlignmentOptions.MidlineRight
                : align == TextAnchor.MiddleCenter ? TMPro.TextAlignmentOptions.Center
                : TMPro.TextAlignmentOptions.MidlineLeft;
            // never let the font sweep replace these previews with the user's chosen font.
            FontReplacementService.Protect(tmp);
            return tmp;
        }

        private void OnAddFontEntry()
        {
            if (_fontFormGo == null) return;
            if (_fontFormGo.activeSelf && !_fontEditMode) { _fontFormGo.SetActive(false); return; }
            bool wasEditing = _fontFormGo.activeSelf && _fontEditMode;
            SetFontFormAddMode();
            _fontFormGo.SetActive(true);
            if (wasEditing) RefreshFontList(); // clear the dark-yellow edit highlight
        }

        private void OnEditFontEntry(int idx)
        {
            // toggle: clicking edit on the row whose form is already open closes it
            if (_fontFormGo != null && _fontFormGo.activeSelf && _fontEditMode && _fontSel == idx)
            {
                _fontFormGo.SetActive(false);
                RefreshFontList();
                return;
            }
            _fontSel = idx;
            SetFontFormEditMode(idx);
            _fontFormGo.SetActive(true);
            RefreshFontList();
        }

        private void SetFontFormAddMode()
        {
            _fontEditMode = false;
            if (_fontFormTitleLbl != null) _fontFormTitleLbl.text = "New Override";
            if (_fontConfirmBtn != null)
            {
                var l = _fontConfirmBtn.GetComponentInChildren<Text>();
                if (l != null) l.text = "Add";
            }
            if (_fontNameField != null) _fontNameField.text = "";
            _fontFormPath = "";
            if (_fontPathLbl != null) _fontPathLbl.text = "no file";
            if (_fontPreviewTmp != null) _fontPreviewTmp.font = null;
            RefreshFontTargetDropdown(null);
        }

        private void SetFontFormEditMode(int idx)
        {
            _fontEditMode = true;
            var e = _fontEntries[idx];
            if (_fontFormTitleLbl != null) _fontFormTitleLbl.text = "Edit Override";
            if (_fontConfirmBtn != null)
            {
                var l = _fontConfirmBtn.GetComponentInChildren<Text>();
                if (l != null) l.text = "Save Changes";
            }
            if (_fontNameField != null) _fontNameField.text = e.entryName;
            _fontFormPath = e.fontPath;
            if (_fontPathLbl != null)
                _fontPathLbl.text = string.IsNullOrEmpty(e.fontPath) ? "no file" : Path.GetFileName(e.fontPath);
            RefreshFontTargetDropdown(e.targetFontName);
            UpdateFontPreview();
        }

        private void OnFontBrowse()
        {
            WinDialogs.PickFile("Pick a font (.ttf / .otf)", new Action<string>(path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                _fontFormPath = path;
                if (_fontPathLbl != null) _fontPathLbl.text = Path.GetFileName(path);
                UpdateFontPreview();
            }));
        }

        // build a throwaway asset from the picked file and show it on the preview text.
        private void UpdateFontPreview()
        {
            if (_fontPreviewTmp == null || string.IsNullOrEmpty(_fontFormPath)) return;
            var asset = FontReplacementService.BuildPreview(new FontOverride { fontPath = _fontFormPath });
            if (asset != null) _fontPreviewTmp.font = asset;
        }

        private void OnConfirmFontForm()
        {
            string name = _fontNameField?.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(name)) { SetFontStatus("name can't be empty"); return; }
            if (string.IsNullOrEmpty(_fontFormPath)) { SetFontStatus("pick a font file first"); return; }

            string target = "";
            if (_fontTargetNames.Count > 0 && _fontTargetIdx >= 0 && _fontTargetIdx < _fontTargetNames.Count)
                target = _fontTargetNames[_fontTargetIdx];
            if (string.IsNullOrEmpty(target)) { SetFontStatus("pick a game font to replace"); return; }

            FontOverride e;
            if (_fontEditMode && _fontSel >= 0 && _fontSel < _fontEntries.Count)
                e = _fontEntries[_fontSel];
            else { e = new FontOverride { enabled = true }; _fontEntries.Add(e); }

            e.entryName = name;
            e.fontPath = _fontFormPath;
            e.targetFontName = target;

            FontReplacementService.SaveAll(_fontEntries);
            _fontFormGo.SetActive(false);
            RefreshFontList();
            SetFontStatus("saved: " + name);
            FontReplacementService.RebuildAndApply();
        }

        private void SetFontStatus(string msg)
        {
            if (_fontStatusLbl != null) _fontStatusLbl.text = msg;
        }

        // ── Foreground panel ──────────────────────────────────────────────────

        private void BuildFgPanel(RectTransform parent, float x, float y, float w, float h)
        {
            float sectionH = LH + SH + BTN_H + SH + (LH + SH) * 2f + LH;

            var (scrollRect, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));

            float cy = PAD;
            float swatchW = BTN_H;
            float toggleW = BTN_H * 2.2f;
            float slidersW = w - swatchW - toggleW - PAD * 2f;
            float fullSliderW = slidersW + toggleW + swatchW + PAD;

            // ── Cyan replacement ──────────────────────────────────────────────

            float cyanStart = cy;
            var cyanBgGo = new GameObject("CyanAreaBg");
            cyanBgGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(cyanBgGo.AddComponent<RectTransform>(),
                new Rect(x - 3f, cyanStart - 3f, w + 6f, sectionH + 6f));
            _fgCyanAreaBg = cyanBgGo.AddComponent<Image>();
            _fgCyanAreaBg.sprite = UGUIShip.GetRadialGradCornerSprite();
            _fgCyanAreaBg.type = Image.Type.Simple;
            _fgCyanAreaBg.color = new Color(_fgCyanR, _fgCyanG, _fgCyanB, 0.18f);
            _fgCyanAreaBg.raycastTarget = false;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "CYAN REPLACEMENT", FS_SM, HINT);
            cy += LH + SH;

            _btnCyanOn = UGUIShip.CreateButton(content, new Rect(x, cy, toggleW, BTN_H),
                _fgCyanOn ? "ON" : "OFF", _fgCyanOn ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _fgCyanOn = !_fgCyanOn;
                    var lbl = _btnCyanOn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _fgCyanOn ? "ON" : "OFF";
                    UGUIShip.SetButtonSelected(_btnCyanOn, _fgCyanOn, SEL_COLOR);
                }));

            var swatchCyanGo = new GameObject("SwatchCyan");
            swatchCyanGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(swatchCyanGo.AddComponent<RectTransform>(),
                new Rect(x + toggleW + PAD, cy, swatchW, BTN_H));
            _swatchCyan = swatchCyanGo.AddComponent<Image>();
            _swatchCyan.color = new Color(_fgCyanR, _fgCyanG, _fgCyanB);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, fullSliderW,
                () => _fgCyanR, () => _fgCyanG, () => _fgCyanB,
                v => _fgCyanR = v, v => _fgCyanG = v, v => _fgCyanB = v, () => SyncCyan(), out _, out _, out _);

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // ── Black replacement ─────────────────────────────────────────────

            float blackStart = cy;
            var blackBgGo = new GameObject("BlackAreaBg");
            blackBgGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(blackBgGo.AddComponent<RectTransform>(),
                new Rect(x - 3f, blackStart - 3f, w + 6f, sectionH + 6f));
            _fgBlackAreaBg = blackBgGo.AddComponent<Image>();
            _fgBlackAreaBg.sprite = UGUIShip.GetRadialGradCornerSprite();
            _fgBlackAreaBg.type = Image.Type.Simple;
            _fgBlackAreaBg.color = new Color(_fgBlackR, _fgBlackG, _fgBlackB, 0.18f);
            _fgBlackAreaBg.raycastTarget = false;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "BLACK REPLACEMENT", FS_SM, HINT);
            cy += LH + SH;

            _btnBlackOn = UGUIShip.CreateButton(content, new Rect(x, cy, toggleW, BTN_H),
                _fgBlackOn ? "ON" : "OFF", _fgBlackOn ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _fgBlackOn = !_fgBlackOn;
                    var lbl = _btnBlackOn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _fgBlackOn ? "ON" : "OFF";
                    UGUIShip.SetButtonSelected(_btnBlackOn, _fgBlackOn, SEL_COLOR);
                }));

            var swatchBlackGo = new GameObject("SwatchBlack");
            swatchBlackGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(swatchBlackGo.AddComponent<RectTransform>(),
                new Rect(x + toggleW + PAD, cy, swatchW, BTN_H));
            _swatchBlack = swatchBlackGo.AddComponent<Image>();
            _swatchBlack.color = new Color(_fgBlackR, _fgBlackG, _fgBlackB);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, fullSliderW,
                () => _fgBlackR, () => _fgBlackG, () => _fgBlackB,
                v => _fgBlackR = v, v => _fgBlackG = v, v => _fgBlackB = v, () => SyncBlack(), out _, out _, out _);

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // ── Yellow replacement ────────────────────────────────────────────

            float yellowStart = cy;
            var yellowBgGo = new GameObject("YellowAreaBg");
            yellowBgGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(yellowBgGo.AddComponent<RectTransform>(),
                new Rect(x - 3f, yellowStart - 3f, w + 6f, sectionH + 6f));
            _fgYellowAreaBg = yellowBgGo.AddComponent<Image>();
            _fgYellowAreaBg.sprite = UGUIShip.GetRadialGradCornerSprite();
            _fgYellowAreaBg.type = Image.Type.Simple;
            _fgYellowAreaBg.color = new Color(_fgYellowR, _fgYellowG, _fgYellowB, 0.18f);
            _fgYellowAreaBg.raycastTarget = false;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "YELLOW REPLACEMENT", FS_SM, HINT);
            cy += LH + SH;

            _btnYellowOn = UGUIShip.CreateButton(content, new Rect(x, cy, toggleW, BTN_H),
                _fgYellowOn ? "ON" : "OFF", _fgYellowOn ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _fgYellowOn = !_fgYellowOn;
                    var lbl = _btnYellowOn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _fgYellowOn ? "ON" : "OFF";
                    UGUIShip.SetButtonSelected(_btnYellowOn, _fgYellowOn, SEL_COLOR);
                }));

            var swatchYellowGo = new GameObject("SwatchYellow");
            swatchYellowGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(swatchYellowGo.AddComponent<RectTransform>(),
                new Rect(x + toggleW + PAD, cy, swatchW, BTN_H));
            _swatchYellow = swatchYellowGo.AddComponent<Image>();
            _swatchYellow.color = new Color(_fgYellowR, _fgYellowG, _fgYellowB);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, fullSliderW,
                () => _fgYellowR, () => _fgYellowG, () => _fgYellowB,
                v => _fgYellowR = v, v => _fgYellowG = v, v => _fgYellowB = v, () => SyncYellow(), out _, out _, out _);

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // ── Blue replacement ──────────────────────────────────────────────

            float blueStart = cy;
            var blueBgGo = new GameObject("BlueAreaBg");
            blueBgGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(blueBgGo.AddComponent<RectTransform>(),
                new Rect(x - 3f, blueStart - 3f, w + 6f, sectionH + 6f));
            _fgBlueAreaBg = blueBgGo.AddComponent<Image>();
            _fgBlueAreaBg.sprite = UGUIShip.GetRadialGradCornerSprite();
            _fgBlueAreaBg.type = Image.Type.Simple;
            _fgBlueAreaBg.color = new Color(_fgBlueR, _fgBlueG, _fgBlueB, 0.18f);
            _fgBlueAreaBg.raycastTarget = false;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "BLUE REPLACEMENT", FS_SM, HINT);
            cy += LH + SH;

            _btnBlueOn = UGUIShip.CreateButton(content, new Rect(x, cy, toggleW, BTN_H),
                _fgBlueOn ? "ON" : "OFF", _fgBlueOn ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _fgBlueOn = !_fgBlueOn;
                    var lbl = _btnBlueOn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _fgBlueOn ? "ON" : "OFF";
                    UGUIShip.SetButtonSelected(_btnBlueOn, _fgBlueOn, SEL_COLOR);
                }));

            var swatchBlueGo = new GameObject("SwatchBlue");
            swatchBlueGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(swatchBlueGo.AddComponent<RectTransform>(),
                new Rect(x + toggleW + PAD, cy, swatchW, BTN_H));
            _swatchBlue = swatchBlueGo.AddComponent<Image>();
            _swatchBlue.color = new Color(_fgBlueR, _fgBlueG, _fgBlueB);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, fullSliderW,
                () => _fgBlueR, () => _fgBlueG, () => _fgBlueB,
                v => _fgBlueR = v, v => _fgBlueG = v, v => _fgBlueB = v, () => SyncBlue(), out _, out _, out _);

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // ── Pink replacement ──────────────────────────────────────────────

            float pinkStart = cy;
            var pinkBgGo = new GameObject("PinkAreaBg");
            pinkBgGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(pinkBgGo.AddComponent<RectTransform>(),
                new Rect(x - 3f, pinkStart - 3f, w + 6f, sectionH + 6f));
            _fgPinkAreaBg = pinkBgGo.AddComponent<Image>();
            _fgPinkAreaBg.sprite = UGUIShip.GetRadialGradCornerSprite();
            _fgPinkAreaBg.type = Image.Type.Simple;
            _fgPinkAreaBg.color = new Color(_fgPinkR, _fgPinkG, _fgPinkB, 0.18f);
            _fgPinkAreaBg.raycastTarget = false;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "PINK REPLACEMENT", FS_SM, HINT);
            cy += LH + SH;

            _btnPinkOn = UGUIShip.CreateButton(content, new Rect(x, cy, toggleW, BTN_H),
                _fgPinkOn ? "ON" : "OFF", _fgPinkOn ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _fgPinkOn = !_fgPinkOn;
                    var lbl = _btnPinkOn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _fgPinkOn ? "ON" : "OFF";
                    UGUIShip.SetButtonSelected(_btnPinkOn, _fgPinkOn, SEL_COLOR);
                }));

            var swatchPinkGo = new GameObject("SwatchPink");
            swatchPinkGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(swatchPinkGo.AddComponent<RectTransform>(),
                new Rect(x + toggleW + PAD, cy, swatchW, BTN_H));
            _swatchPink = swatchPinkGo.AddComponent<Image>();
            _swatchPink.color = new Color(_fgPinkR, _fgPinkG, _fgPinkB);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, fullSliderW,
                () => _fgPinkR, () => _fgPinkG, () => _fgPinkB,
                v => _fgPinkR = v, v => _fgPinkG = v, v => _fgPinkB = v, () => SyncPink(), out _, out _, out _);

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // ── Orange replacement ────────────────────────────────────────────

            float orangeStart = cy;
            var orangeBgGo = new GameObject("OrangeAreaBg");
            orangeBgGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(orangeBgGo.AddComponent<RectTransform>(),
                new Rect(x - 3f, orangeStart - 3f, w + 6f, sectionH + 6f));
            _fgOrangeAreaBg = orangeBgGo.AddComponent<Image>();
            _fgOrangeAreaBg.sprite = UGUIShip.GetRadialGradCornerSprite();
            _fgOrangeAreaBg.type = Image.Type.Simple;
            _fgOrangeAreaBg.color = new Color(_fgOrangeR, _fgOrangeG, _fgOrangeB, 0.18f);
            _fgOrangeAreaBg.raycastTarget = false;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "ORANGE REPLACEMENT", FS_SM, HINT);
            cy += LH + SH;

            _btnOrangeOn = UGUIShip.CreateButton(content, new Rect(x, cy, toggleW, BTN_H),
                _fgOrangeOn ? "ON" : "OFF", _fgOrangeOn ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _fgOrangeOn = !_fgOrangeOn;
                    var lbl = _btnOrangeOn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _fgOrangeOn ? "ON" : "OFF";
                    UGUIShip.SetButtonSelected(_btnOrangeOn, _fgOrangeOn, SEL_COLOR);
                }));

            var swatchOrangeGo = new GameObject("SwatchOrange");
            swatchOrangeGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(swatchOrangeGo.AddComponent<RectTransform>(),
                new Rect(x + toggleW + PAD, cy, swatchW, BTN_H));
            _swatchOrange = swatchOrangeGo.AddComponent<Image>();
            _swatchOrange.color = new Color(_fgOrangeR, _fgOrangeG, _fgOrangeB);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, fullSliderW,
                () => _fgOrangeR, () => _fgOrangeG, () => _fgOrangeB,
                v => _fgOrangeR = v, v => _fgOrangeG = v, v => _fgOrangeB = v, () => SyncOrange(), out _, out _, out _);

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void SyncOrange()
        {
            if (_swatchOrange != null) _swatchOrange.color = new Color(_fgOrangeR, _fgOrangeG, _fgOrangeB);
            if (_fgOrangeAreaBg != null) _fgOrangeAreaBg.color = new Color(_fgOrangeR, _fgOrangeG, _fgOrangeB, 0.18f);
        }

        private void SyncPink()
        {
            if (_swatchPink != null) _swatchPink.color = new Color(_fgPinkR, _fgPinkG, _fgPinkB);
            if (_fgPinkAreaBg != null) _fgPinkAreaBg.color = new Color(_fgPinkR, _fgPinkG, _fgPinkB, 0.18f);
        }

        private void SyncCyan()
        {
            if (_swatchCyan != null) _swatchCyan.color = new Color(_fgCyanR, _fgCyanG, _fgCyanB);
            if (_fgCyanAreaBg != null) _fgCyanAreaBg.color = new Color(_fgCyanR, _fgCyanG, _fgCyanB, 0.18f);
        }

        private void SyncBlack()
        {
            if (_swatchBlack != null) _swatchBlack.color = new Color(_fgBlackR, _fgBlackG, _fgBlackB);
            if (_fgBlackAreaBg != null) _fgBlackAreaBg.color = new Color(_fgBlackR, _fgBlackG, _fgBlackB, 0.18f);
        }

        private void SyncYellow()
        {
            if (_swatchYellow != null) _swatchYellow.color = new Color(_fgYellowR, _fgYellowG, _fgYellowB);
            if (_fgYellowAreaBg != null) _fgYellowAreaBg.color = new Color(_fgYellowR, _fgYellowG, _fgYellowB, 0.18f);
        }

        private void SyncBlue()
        {
            if (_swatchBlue != null) _swatchBlue.color = new Color(_fgBlueR, _fgBlueG, _fgBlueB);
            if (_fgBlueAreaBg != null) _fgBlueAreaBg.color = new Color(_fgBlueR, _fgBlueG, _fgBlueB, 0.18f);
        }

        // ── Foreground "what to customise" selector ───────────────────────────

        private void BuildFgSelector(RectTransform parent, float x, float y, float w)
        {
            var opts = new System.Collections.Generic.List<string> { "Custom UI colours", "Qualified banner", "Eliminated banner", "Squad eliminated banner", "Winner banner", "Round over banner" };
            var initial = new System.Collections.Generic.List<bool> { _fgWhat == FgWhat.CustomUI, _fgWhat == FgWhat.Qualified, _fgWhat == FgWhat.Eliminated, _fgWhat == FgWhat.EliminatedSquad, _fgWhat == FgWhat.Winner, _fgWhat == FgWhat.RoundOver };
            Button dd = null;
            dd = UGUIShip.CreateMultiSelectDropdown(parent, new Rect(PAD, PAD, w, BTN_H),
                opts[(int)_fgWhat], opts, initial,
                new Action<int, bool>((idx, _) =>
                {
                    _fgWhat = (FgWhat)idx;
                    var lbl = dd?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = opts[idx];
                    RefreshFgWhatVisibility();
                }), FS_SM, w, dropdownRowH, true, true, false);

            UGUIShip.CreatePanel(parent, new Rect(PAD, PAD + BTN_H + SH, w, 1f), new Color(1f, 1f, 1f, 0.06f));
        }

        private void RefreshFgWhatVisibility()
        {
            if (_fgCustomBodyGo != null) _fgCustomBodyGo.SetActive(_fgWhat == FgWhat.CustomUI);
            if (_fgQualBodyGo != null)   _fgQualBodyGo.SetActive(_fgWhat == FgWhat.Qualified);
            if (_fgElimBodyGo != null)   _fgElimBodyGo.SetActive(_fgWhat == FgWhat.Eliminated);
            if (_fgSquadBodyGo != null)  _fgSquadBodyGo.SetActive(_fgWhat == FgWhat.EliminatedSquad);
            if (_fgWinBodyGo != null)    _fgWinBodyGo.SetActive(_fgWhat == FgWhat.Winner);
            if (_fgRoundBodyGo != null)  _fgRoundBodyGo.SetActive(_fgWhat == FgWhat.RoundOver);
            RefreshBannerPreview();
        }

        private void RefreshBannerPreview()
        {
            if (_bannerPreviewGo != null) { GameObject.Destroy(_bannerPreviewGo); _bannerPreviewGo = null; }
            _previewImgCache.Clear();
            _previewTmpCache.Clear();

            Transform viewport = BannerViewport(_fgWhat);
            if (viewport == null) return;

            GameObject source = FindBannerSource(_fgWhat);
            if (source == null) return;

            _bannerPreviewGo = GameObject.Instantiate(source);
            _bannerPreviewGo.name = "BannerPreview";

            StartCoroutine(DisableBannerAnimatorsDelayed(_bannerPreviewGo).WrapToIl2Cpp());

            foreach (var t in _bannerPreviewGo.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == "Layout") t.gameObject.SetActive(false);

            _bannerPreviewGo.transform.SetParent(viewport, false);
            _bannerPreviewGo.transform.localPosition = new Vector3(205.4f, -44.6501f, 0f);
            _bannerPreviewGo.transform.localScale = new Vector3(0.8236f, 0.8236f, 0.6f);
            _bannerPreviewGo.SetActive(true);

            // preview is decorative — kill all raycast targets so it doesn't block clicks/drags on
            // the underlying scroll rect (the squad banner covers a big chunk of the panel).
            foreach (var g in _bannerPreviewGo.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                if (g != null) g.raycastTarget = false;

            if (_fgWhat == FgWhat.Winner)
            {
                foreach (var t in _bannerPreviewGo.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null || t.parent == null || t.parent.name != "Container") continue;
                    if (t.name == "background-starburst-top" || t.name == "UIParticleStars")
                        t.gameObject.SetActive(false);
                }
            }
            else if (_fgWhat == FgWhat.RoundOver)
            {
                foreach (var t in _bannerPreviewGo.GetComponentsInChildren<Transform>(true))
                    if (t != null && t.name == "text-ROUND")
                    {
                        t.localPosition = new Vector3(-5f, 0.3327f, 0f);
                        t.localScale = new Vector3(0.2f, 0.2f, 0.2f);
                        break;
                    }
            }
            else if (_fgWhat == FgWhat.EliminatedSquad)
            {
                ApplySquadPreviewLayout(_bannerPreviewGo);
            }

            foreach (var img in _bannerPreviewGo.GetComponentsInChildren<Image>(true))
            {
                if (img != null)
                {
                    bool hl = Customization.Menu.MenuCustomizationApplication.BannerColours.IsHighlight(img);
                    _previewImgCache.Add(new CachedImgColor { img = img, orig = img.color, isHighlight = hl });
                }
            }

            foreach (var binding in _bannerPreviewGo.GetComponentsInChildren<Mediatonic.Tools.MVVM.TMPTextBinding>(true))
                if (binding != null) GameObject.Destroy(binding);

            StartCoroutine(SetBannerTextNextFrame().WrapToIl2Cpp());
        }

        private System.Collections.IEnumerator DisableBannerAnimatorsDelayed(GameObject go)
        {
            yield return new WaitForSeconds(1.7f);
            if (go == null) yield break;
            foreach (var anim in go.GetComponentsInChildren<Animator>(true))
                if (anim != null) anim.enabled = false;
        }

        // squad preview: hide the Badge and reposition title/subtitle. runs immediately — no wait.
        // ContentSizeFitter / LayoutElement / animators on the text objects fight our sizeDelta, so
        // strip those before setting values. also disable the animator on this preview clone up-front.
        private void ApplySquadPreviewLayout(GameObject go)
        {
            if (go == null) return;
            foreach (var anim in go.GetComponentsInChildren<Animator>(true))
                if (anim != null) anim.enabled = false;

            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                if (t == null) continue;
                if (t.parent != null && t.parent.name == "Container" && t.name == "Badge")
                    t.gameObject.SetActive(false);
                else if (t.name == "text-title" || t.name == "text-subtitle")
                {
                    var fitter = t.GetComponent<ContentSizeFitter>();
                    if (fitter != null) GameObject.Destroy(fitter);
                    var le = t.GetComponent<LayoutElement>();
                    if (le != null) GameObject.Destroy(le);

                    if (t.name == "text-title")
                    {
                        t.localPosition = new Vector3(-301.564f, -10.9704f, 0f);
                        t.localScale = new Vector3(3f, 3f, 3f);
                        var rt = t as RectTransform;
                        if (rt != null) rt.sizeDelta = new Vector2(320f, -194.8501f);
                    }
                    else
                    {
                        t.localScale = new Vector3(3f, 3f, 3f);
                        t.localPosition = new Vector3(63.6912f, -50.9455f, 0f);
                        var rt = t as RectTransform;
                        if (rt != null) rt.sizeDelta = new Vector2(520f, 0f);
                    }
                }
            }
        }

        private System.Collections.IEnumerator SetBannerTextNextFrame()
        {
            yield return null;
            if (_bannerPreviewGo == null) yield break;

            foreach (var tmp in _bannerPreviewGo.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (tmp == null) continue;
                if (tmp.gameObject.name.StartsWith("text-"))
                    tmp.SetText("BEAUTY");

                tmp.ForceMeshUpdate();
                tmp.enabled = false;
                var entry = new CachedTmpColor { tmp = tmp, origFill = tmp.color };
                if (tmp.fontSharedMaterial != null)
                {
                    var mat = tmp.fontMaterial;
                    entry.hasOutline = mat.HasProperty(TMPro.ShaderUtilities.ID_OutlineColor);
                    if (entry.hasOutline) entry.origOutline = mat.GetColor(TMPro.ShaderUtilities.ID_OutlineColor);
                    entry.hasUnderlay = mat.HasProperty(TMPro.ShaderUtilities.ID_UnderlayColor);
                    if (entry.hasUnderlay) entry.origUnderlay = mat.GetColor(TMPro.ShaderUtilities.ID_UnderlayColor);
                }
                _previewTmpCache.Add(entry);
            }

            yield return null;

            for (int i = 0; i < _previewTmpCache.Count; i++)
            {
                var c = _previewTmpCache[i];
                if (c.tmp != null) c.tmp.enabled = true;
            }
            UpdateBannerPreviewColours();
        }

        // re-tints the preview clone from cached originals (so repeated slider drags don't tint an
        // already-tinted colour) using the exact same matcher the live banner apply uses.
        private void UpdateBannerPreviewColours()
        {
            if (_bannerPreviewGo == null) return;
            var set = PreviewBannerColours(_fgWhat);

            // matches the live ApplyWinnerRoundOverWhiteOverride: force the round-over-white image to
            // the yellow replacement so the preview reads the same as the in-game banner.
            UnityEngine.UI.Image winnerRoundOverWhiteImg = null;
            bool winnerOverrideOn = false;
            Color winnerOverrideColor = Color.white;
            if (_fgWhat == FgWhat.Winner)
            {
                var def = GetBannerDef(FgWhat.Winner);
                if (def != null && def.enabled)
                {
                    var yellow = def.slots[0]; // yellow is always slot 0 in the Winner def
                    if (yellow.ui.on)
                    {
                        winnerOverrideOn = true;
                        winnerOverrideColor = new Color(yellow.ui.r, yellow.ui.g, yellow.ui.b);
                    }
                }
                foreach (var t in _bannerPreviewGo.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null || t.gameObject.name != "round-over-white") continue;
                    winnerRoundOverWhiteImg = t.GetComponent<UnityEngine.UI.Image>();
                    if (winnerRoundOverWhiteImg != null) break;
                }
            }

            for (int i = 0; i < _previewImgCache.Count; i++)
            {
                var c = _previewImgCache[i];
                if (c.img == null) continue;
                if (winnerRoundOverWhiteImg != null && c.img == winnerRoundOverWhiteImg)
                {
                    c.img.color = winnerOverrideOn
                        ? new Color(winnerOverrideColor.r, winnerOverrideColor.g, winnerOverrideColor.b, c.orig.a)
                        : c.orig;
                    continue;
                }
                if (c.isHighlight && set.highlightOn)
                    c.img.color = new Color(set.highlight.r, set.highlight.g, set.highlight.b, c.orig.a);
                else if (set.TryMatch(c.orig, out var t))
                    c.img.color = new Color(t.r, t.g, t.b, c.orig.a);
                else
                    c.img.color = c.orig;
            }

            for (int i = 0; i < _previewTmpCache.Count; i++)
            {
                var c = _previewTmpCache[i];
                if (c.tmp == null) continue;
                c.tmp.color = set.TryMatch(c.origFill, out var tFill)
                    ? new Color(tFill.r, tFill.g, tFill.b, c.origFill.a) : c.origFill;

                if (c.tmp.fontSharedMaterial == null) continue;
                var mat = c.tmp.fontMaterial;
                if (c.hasOutline)
                    mat.SetColor(TMPro.ShaderUtilities.ID_OutlineColor,
                        set.TryMatch(c.origOutline, out var tOut)
                            ? new Color(tOut.r, tOut.g, tOut.b, c.origOutline.a) : c.origOutline);
                if (c.hasUnderlay)
                    mat.SetColor(TMPro.ShaderUtilities.ID_UnderlayColor,
                        set.TryMatch(c.origUnderlay, out var tUn)
                            ? new Color(tUn.r, tUn.g, tUn.b, c.origUnderlay.a) : c.origUnderlay);
            }
        }

        private Customization.Menu.MenuCustomizationApplication.BannerColours PreviewBannerColours(FgWhat what)
        {
            var def = GetBannerDef(what);
            var slots = new System.Collections.Generic.List<Customization.Menu.MenuCustomizationApplication.BannerSlot>();
            // banner-level toggle off = preview stays stock, matches the live apply path's early-out
            if (def == null || !def.enabled)
                return new Customization.Menu.MenuCustomizationApplication.BannerColours { slots = slots, highlightOn = false, highlight = Color.white };

            foreach (var s in def.slots)
                if (s.ui.on)
                    slots.Add(new Customization.Menu.MenuCustomizationApplication.BannerSlot
                    { bucket = s.bucket, target = new Color(s.ui.r, s.ui.g, s.ui.b) });

            var hl = def.highlight.ui;
            return new Customization.Menu.MenuCustomizationApplication.BannerColours
            {
                slots = slots,
                highlightOn = hl != null && hl.on,
                highlight = hl != null ? new Color(hl.r, hl.g, hl.b) : Color.white,
            };
        }

        private void OnFgApply()
        {
            if (_fgWhat == FgWhat.CustomUI) OnApply();
            else OnBannerApply(_fgWhat);
        }

        private void OnFgRemove()
        {
            if (_fgWhat == FgWhat.CustomUI) OnRemove();
            else OnBannerRemove(_fgWhat);
        }

        // ── Banner colour panel (Qualified / Eliminated) ──────────────────────
        // mirrors the Foreground "Custom UI colours" panel: cyan/pink/black/white replacement
        // sections, each with ON + swatch + R/G/B sliders. the picked replacement gets remapped
        // onto banner Image colours AND TMP text fill / outline / underlay.

        private class BannerColourUI
        {
            public bool on;
            public float r, g, b;
            public Button toggleBtn;
            public Image swatch;
            public Image areaBg;
        }

        // one editable replacement slot: a named hue bucket + its UI state + the settings key prefix
        // (".on/.r/.g/.b" appended). highlight is a slot too but matched by component, not hue.
        private class BannerSlotUI
        {
            public Customization.Menu.MenuCustomizationApplication.BannerBucket bucket;
            public string label;
            public string keyPrefix;
            public float dr, dg, db;
            public readonly BannerColourUI ui = new BannerColourUI();
        }

        private class BannerDef
        {
            public System.Collections.Generic.List<BannerSlotUI> slots;
            public BannerSlotUI highlight;
            public Transform viewport;
            public string enabledKey;
            public bool enabled;
            public Button enabledBtn;
        }

        private System.Collections.Generic.Dictionary<FgWhat, BannerDef> _bannerDefs;

        private BannerSlotUI MkSlot(Customization.Menu.MenuCustomizationApplication.BannerBucket bucket,
            string label, string keyPrefix, float dr, float dg, float db)
        {
            var s = new BannerSlotUI { bucket = bucket, label = label, keyPrefix = keyPrefix, dr = dr, dg = dg, db = db };
            s.ui.r = dr; s.ui.g = dg; s.ui.b = db;
            return s;
        }

        private BannerDef GetBannerDef(FgWhat what)
        {
            if (_bannerDefs == null) BuildBannerDefs();
            return _bannerDefs.TryGetValue(what, out var d) ? d : null;
        }

        private void BuildBannerDefs()
        {
            var B = Customization.Menu.MenuCustomizationApplication.BannerBucket.Black;
            var W = Customization.Menu.MenuCustomizationApplication.BannerBucket.White;
            var C = Customization.Menu.MenuCustomizationApplication.BannerBucket.Cyan;
            var P = Customization.Menu.MenuCustomizationApplication.BannerBucket.Pink;
            var Y = Customization.Menu.MenuCustomizationApplication.BannerBucket.Yellow;
            var O = Customization.Menu.MenuCustomizationApplication.BannerBucket.Orange;
            var Bl = Customization.Menu.MenuCustomizationApplication.BannerBucket.Blue;
            var BG = Customization.Menu.MenuCustomizationApplication.BannerBucket.BlackGrey;

            _bannerDefs = new System.Collections.Generic.Dictionary<FgWhat, BannerDef>
            {
                [FgWhat.Qualified] = new BannerDef
                {
                    slots = new System.Collections.Generic.List<BannerSlotUI>
                    {
                        MkSlot(C, "CYAN REPLACEMENT",  "menu.banner.qual.cyan",  0f,    0.78f, 1f),
                        MkSlot(P, "PINK REPLACEMENT",  "menu.banner.qual.pink",  1f,    0.2f,  0.5f),
                        MkSlot(B, "BLACK REPLACEMENT", "menu.banner.qual.black", 0.08f, 0.08f, 0.08f),
                        MkSlot(W, "WHITE REPLACEMENT", "menu.banner.qual.white", 1f,    1f,    1f),
                    },
                    highlight = MkSlot(W, "HIGHLIGHT REPLACEMENT", "menu.banner.qual.highlight", 1f, 1f, 1f),
                    enabledKey = Customization.Menu.MenuCustomizationApplication.KEY_BANNER_QUAL_ENABLED,
                },
                [FgWhat.Eliminated] = new BannerDef
                {
                    slots = new System.Collections.Generic.List<BannerSlotUI>
                    {
                        MkSlot(C, "CYAN REPLACEMENT",  "menu.banner.elim.cyan",  0f,    0.78f, 1f),
                        MkSlot(P, "PINK REPLACEMENT",  "menu.banner.elim.pink",  1f,    0.2f,  0.5f),
                        MkSlot(B, "BLACK REPLACEMENT", "menu.banner.elim.black", 0.08f, 0.08f, 0.08f),
                        MkSlot(W, "WHITE REPLACEMENT", "menu.banner.elim.white", 1f,    1f,    1f),
                    },
                    highlight = MkSlot(W, "HIGHLIGHT REPLACEMENT", "menu.banner.elim.highlight", 1f, 1f, 1f),
                    enabledKey = Customization.Menu.MenuCustomizationApplication.KEY_BANNER_ELIM_ENABLED,
                },
                [FgWhat.Winner] = new BannerDef
                {
                    slots = new System.Collections.Generic.List<BannerSlotUI>
                    {
                        MkSlot(Y, "YELLOW REPLACEMENT", "menu.banner.win.yellow", 1f,    0.85f, 0f),
                        MkSlot(O, "ORANGE REPLACEMENT", "menu.banner.win.orange", 1f,    0.55f, 0.1f),
                        MkSlot(W,  "WHITE REPLACEMENT", "menu.banner.win.white",  1f,    1f,    1f),
                        MkSlot(BG, "BLACK REPLACEMENT", "menu.banner.win.black",  0.08f, 0.08f, 0.08f),
                    },
                    highlight = MkSlot(W, "HIGHLIGHT REPLACEMENT", "menu.banner.win.highlight", 1f, 1f, 1f),
                    enabledKey = Customization.Menu.MenuCustomizationApplication.KEY_BANNER_WIN_ENABLED,
                },
                [FgWhat.RoundOver] = new BannerDef
                {
                    slots = new System.Collections.Generic.List<BannerSlotUI>
                    {
                        MkSlot(BG, "BLACK REPLACEMENT", "menu.banner.round.black", 0.08f, 0.08f, 0.08f),
                        MkSlot(P,  "PINK REPLACEMENT",  "menu.banner.round.pink",  1f,    0.2f,  0.5f),
                        MkSlot(C,  "CYAN REPLACEMENT",  "menu.banner.round.blue",  0f,    0.78f, 1f),
                        MkSlot(W,  "WHITE REPLACEMENT", "menu.banner.round.white", 1f,    1f,    1f),
                    },
                    highlight = MkSlot(W, "HIGHLIGHT REPLACEMENT", "menu.banner.round.highlight", 1f, 1f, 1f),
                    enabledKey = Customization.Menu.MenuCustomizationApplication.KEY_BANNER_ROUND_ENABLED,
                },
                [FgWhat.EliminatedSquad] = new BannerDef
                {
                    slots = new System.Collections.Generic.List<BannerSlotUI>
                    {
                        MkSlot(O,  "ORANGE REPLACEMENT", "menu.banner.squad.orange", 1f,    0.55f, 0.1f),
                        MkSlot(BG, "BLACK REPLACEMENT",  "menu.banner.squad.black",  0.08f, 0.08f, 0.08f),
                        MkSlot(P,  "PINK REPLACEMENT",   "menu.banner.squad.pink",   1f,    0.2f,  0.5f),
                        MkSlot(C,  "CYAN REPLACEMENT",   "menu.banner.squad.blue",   0f,    0.78f, 1f),
                        MkSlot(Y,  "YELLOW REPLACEMENT", "menu.banner.squad.yellow", 1f,    0.85f, 0f),
                        MkSlot(W,  "WHITE REPLACEMENT",  "menu.banner.squad.white",  1f,    1f,    1f),
                    },
                    highlight = MkSlot(W, "HIGHLIGHT REPLACEMENT", "menu.banner.squad.highlight", 1f, 1f, 1f),
                    enabledKey = Customization.Menu.MenuCustomizationApplication.KEY_BANNER_SQUAD_ENABLED,
                },
            };
        }

        // skip our own preview clone — FindObjectsOfTypeAll returns it alongside the game prefab,
        // and cloning a clone re-bakes previously-applied tints into the new "orig" cache.
        private static bool IsBannerPreviewClone(UnityEngine.Object obj)
        {
            if (obj == null) return false;
            var t = (obj as Component)?.transform;
            while (t != null)
            {
                if (t.gameObject.name == "BannerPreview") return true;
                t = t.parent;
            }
            return false;
        }

        private GameObject FindBannerSource(FgWhat what)
        {
            switch (what)
            {
                case FgWhat.Qualified:
                    foreach (var vm in Resources.FindObjectsOfTypeAll<FGClient.UI.QualifiedScreenViewModel>())
                        if (vm != null && vm.gameObject != null && !IsBannerPreviewClone(vm)) return vm.gameObject;
                    break;
                case FgWhat.Eliminated:
                    foreach (var vm in Resources.FindObjectsOfTypeAll<FGClient.EliminatedScreenViewModel>())
                        if (vm != null && vm.gameObject != null && !IsBannerPreviewClone(vm)) return vm.gameObject;
                    break;
                case FgWhat.EliminatedSquad:
                    foreach (var vm in Resources.FindObjectsOfTypeAll<FGClient.EliminatedSquadScreenViewModel>())
                        if (vm != null && vm.gameObject != null && !IsBannerPreviewClone(vm)) return vm.gameObject;
                    break;
                case FgWhat.Winner:
                    foreach (var vm in Resources.FindObjectsOfTypeAll<FGClient.UI.WinnerScreenViewModel>())
                        if (vm != null && vm.gameObject != null && !IsBannerPreviewClone(vm)) return vm.gameObject;
                    break;
                case FgWhat.RoundOver:
                    foreach (var vm in Resources.FindObjectsOfTypeAll<FGClient.RoundEndedScreenViewModel>())
                        if (vm != null && vm.gameObject != null && !IsBannerPreviewClone(vm)) return vm.gameObject;
                    break;
            }
            return null;
        }

        private Transform BannerViewport(FgWhat what) => GetBannerDef(what)?.viewport;

        private const float BANNER_PREVIEW_SPACE = 120f;

        private void BuildBannerPanel(RectTransform parent, float x, float y, float w, float h, FgWhat what)
        {
            var def = GetBannerDef(what);
            if (def == null) return;
            LoadBannerSettings(what);

            float sectionH = LH + SH + BTN_H + SH + (LH + SH) * 2f + LH;
            var (scrollRect, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));

            def.viewport = scrollRect.transform.Find("Viewport");

            float cy = BANNER_PREVIEW_SPACE;
            float swatchW = BTN_H;
            float toggleW = BTN_H * 2.2f;
            float slidersW = w - swatchW - toggleW - PAD * 2f;
            float fullSliderW = slidersW + toggleW + swatchW + PAD;

            // banner enable toggle — live-saved. when off, ApplyBannerColours skips this banner entirely.
            def.enabledBtn = UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H),
                def.enabled ? "CUSTOM COLOURS: ON" : "CUSTOM COLOURS: OFF",
                def.enabled ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    def.enabled = !def.enabled;
                    SettingsService.Set(def.enabledKey, def.enabled ? "true" : "false");
                    var lbl = def.enabledBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = def.enabled ? "CUSTOM COLOURS: ON" : "CUSTOM COLOURS: OFF";
                    UGUIShip.SetButtonSelected(def.enabledBtn, def.enabled, SEL_COLOR);
                    UpdateBannerPreviewColours();
                }));
            cy += BTN_H + SH;
            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            foreach (var slot in def.slots)
                BuildBannerSection(content, x, ref cy, w, sectionH, fullSliderW, swatchW, toggleW, slot.label, slot.ui);
            BuildBannerSection(content, x, ref cy, w, sectionH, fullSliderW, swatchW, toggleW, def.highlight.label, def.highlight.ui);

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void BuildBannerSection(Transform content, float x, ref float cy, float w, float sectionH,
            float fullSliderW, float swatchW, float toggleW, string title, BannerColourUI ch)
        {
            float sectionStart = cy;
            var bgGo = new GameObject(title + "_AreaBg");
            bgGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(bgGo.AddComponent<RectTransform>(),
                new Rect(x - 3f, sectionStart - 3f, w + 6f, sectionH + 6f));
            ch.areaBg = bgGo.AddComponent<Image>();
            ch.areaBg.sprite = UGUIShip.GetRadialGradCornerSprite();
            ch.areaBg.type = Image.Type.Simple;
            ch.areaBg.color = new Color(ch.r, ch.g, ch.b, 0.18f);
            ch.areaBg.raycastTarget = false;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), title, FS_SM, HINT);
            cy += LH + SH;

            ch.toggleBtn = UGUIShip.CreateButton(content, new Rect(x, cy, toggleW, BTN_H),
                ch.on ? "ON" : "OFF", ch.on ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    ch.on = !ch.on;
                    var lbl = ch.toggleBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = ch.on ? "ON" : "OFF";
                    UGUIShip.SetButtonSelected(ch.toggleBtn, ch.on, SEL_COLOR);
                    UpdateBannerPreviewColours();
                }));

            var swatchGo = new GameObject(title + "_Swatch");
            swatchGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(swatchGo.AddComponent<RectTransform>(),
                new Rect(x + toggleW + PAD, cy, swatchW, BTN_H));
            ch.swatch = swatchGo.AddComponent<Image>();
            ch.swatch.color = new Color(ch.r, ch.g, ch.b);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, fullSliderW,
                () => ch.r, () => ch.g, () => ch.b,
                v => ch.r = v, v => ch.g = v, v => ch.b = v, () => SyncBannerColour(ch), out _, out _, out _);

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;
        }

        private void SyncBannerColour(BannerColourUI ch)
        {
            if (ch.swatch != null) ch.swatch.color = new Color(ch.r, ch.g, ch.b);
            if (ch.areaBg != null) ch.areaBg.color = new Color(ch.r, ch.g, ch.b, 0.18f);
            UpdateBannerPreviewColours();
        }

        private void LoadBannerSettings(FgWhat what)
        {
            var def = GetBannerDef(what);
            if (def == null) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float d) =>
                float.TryParse(SettingsService.Get(key, d.ToString(ci)), System.Globalization.NumberStyles.Float, ci, out float v) ? v : d;

            void Load(BannerSlotUI s)
            {
                s.ui.on = SettingsService.Get(s.keyPrefix + ".on", "false") == "true";
                s.ui.r = P(s.keyPrefix + ".r", s.dr);
                s.ui.g = P(s.keyPrefix + ".g", s.dg);
                s.ui.b = P(s.keyPrefix + ".b", s.db);
            }

            def.enabled = SettingsService.Get(def.enabledKey, "false") == "true";
            foreach (var s in def.slots) Load(s);
            Load(def.highlight);
        }

        private void OnBannerApply(FgWhat what)
        {
            var def = GetBannerDef(what);
            if (def == null) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            void W(BannerSlotUI s)
            {
                SettingsService.Set(s.keyPrefix + ".on", s.ui.on ? "true" : "false");
                SettingsService.Set(s.keyPrefix + ".r", s.ui.r.ToString(ci));
                SettingsService.Set(s.keyPrefix + ".g", s.ui.g.ToString(ci));
                SettingsService.Set(s.keyPrefix + ".b", s.ui.b.ToString(ci));
            }

            foreach (var s in def.slots) W(s);
            W(def.highlight);
        }

        private void OnBannerRemove(FgWhat what)
        {
            var def = GetBannerDef(what);
            if (def == null) return;
            void Wipe(BannerSlotUI s)
            {
                SettingsService.Remove(s.keyPrefix + ".on");
                SettingsService.Remove(s.keyPrefix + ".r");
                SettingsService.Remove(s.keyPrefix + ".g");
                SettingsService.Remove(s.keyPrefix + ".b");
                BannerColourOff(s.ui);
            }

            SettingsService.Remove(def.enabledKey);
            def.enabled = false;
            var elbl = def.enabledBtn?.GetComponentInChildren<Text>();
            if (elbl != null) elbl.text = "CUSTOM COLOURS: OFF";
            UGUIShip.SetButtonSelected(def.enabledBtn, false, SEL_COLOR);

            foreach (var s in def.slots) Wipe(s);
            Wipe(def.highlight);
            UpdateBannerPreviewColours();
        }

        private static void BannerColourOff(BannerColourUI ch)
        {
            ch.on = false;
            var lbl = ch.toggleBtn?.GetComponentInChildren<Text>();
            if (lbl != null) lbl.text = "OFF";
            UGUIShip.SetButtonSelected(ch.toggleBtn, false, SEL_COLOR);
        }

        // ── Apply / Remove ────────────────────────────────────────────────────

        private void OnApply()
        {
            SaveSettings();
            MenuCustomizationApplication.Instance?.ReapplyForegroundFromSettings();
            MenuCustomizationApplication.Instance?.ReapplyBakedPinkGreyTextures();
            BetterFG.Features.QualificationTime.FeatureQualificationTime.ReapplyTimerColors();
        }

        private void OnRemove()
        {
            MenuCustomizationApplication.Instance?.RevertForeground();
            _fgCyanOn = false;
            _fgBlackOn = false;
            var lbl1 = _btnCyanOn?.GetComponentInChildren<Text>();
            if (lbl1 != null) lbl1.text = "OFF";
            UGUIShip.SetButtonSelected(_btnCyanOn, false, SEL_COLOR);
            var lbl2 = _btnBlackOn?.GetComponentInChildren<Text>();
            if (lbl2 != null) lbl2.text = "OFF";
            UGUIShip.SetButtonSelected(_btnBlackOn, false, SEL_COLOR);
            _fgYellowOn = false;
            var lbl3 = _btnYellowOn?.GetComponentInChildren<Text>();
            if (lbl3 != null) lbl3.text = "OFF";
            UGUIShip.SetButtonSelected(_btnYellowOn, false, SEL_COLOR);
            _fgBlueOn = false;
            var lbl4 = _btnBlueOn?.GetComponentInChildren<Text>();
            if (lbl4 != null) lbl4.text = "OFF";
            UGUIShip.SetButtonSelected(_btnBlueOn, false, SEL_COLOR);
            _fgPinkOn = false;
            var lbl5 = _btnPinkOn?.GetComponentInChildren<Text>();
            if (lbl5 != null) lbl5.text = "OFF";
            UGUIShip.SetButtonSelected(_btnPinkOn, false, SEL_COLOR);
            _fgOrangeOn = false;
            var lbl6 = _btnOrangeOn?.GetComponentInChildren<Text>();
            if (lbl6 != null) lbl6.text = "OFF";
            UGUIShip.SetButtonSelected(_btnOrangeOn, false, SEL_COLOR);
            SettingsService.Remove(MenuCustomizationApplication.KEY_FG_CYAN_ON);
            SettingsService.Remove(MenuCustomizationApplication.KEY_FG_BLACK_ON);
            SettingsService.Remove(MenuCustomizationApplication.KEY_FG_YELLOW_ON);
            SettingsService.Remove(MenuCustomizationApplication.KEY_FG_BLUE_ON);
            SettingsService.Remove(MenuCustomizationApplication.KEY_FG_PINK_ON);
            SettingsService.Remove(MenuCustomizationApplication.KEY_FG_ORANGE_ON);

            // flags are now off in settings -> this restores the cached original sprites
            MenuCustomizationApplication.Instance?.ReapplyBakedPinkGreyTextures();
        }

        // ── Settings ──────────────────────────────────────────────────────────

        private void LoadSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float def) =>
                float.TryParse(SettingsService.Get(key, def.ToString(ci)),
                    System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            _fgCyanOn = SettingsService.Get(MenuCustomizationApplication.KEY_FG_CYAN_ON, "false") == "true";
            _fgCyanR = P(MenuCustomizationApplication.KEY_FG_CYAN_R, 0f);
            _fgCyanG = P(MenuCustomizationApplication.KEY_FG_CYAN_G, 0.3f);
            _fgCyanB = P(MenuCustomizationApplication.KEY_FG_CYAN_B, 1f);
            _fgBlackOn = SettingsService.Get(MenuCustomizationApplication.KEY_FG_BLACK_ON, "false") == "true";
            _fgBlackR = P(MenuCustomizationApplication.KEY_FG_BLACK_R, 0.75f);
            _fgBlackG = P(MenuCustomizationApplication.KEY_FG_BLACK_G, 0.75f);
            _fgBlackB = P(MenuCustomizationApplication.KEY_FG_BLACK_B, 0.75f);
            _fgYellowOn = SettingsService.Get(MenuCustomizationApplication.KEY_FG_YELLOW_ON, "false") == "true";
            _fgYellowR = P(MenuCustomizationApplication.KEY_FG_YELLOW_R, 1f);
            _fgYellowG = P(MenuCustomizationApplication.KEY_FG_YELLOW_G, 0.5f);
            _fgYellowB = P(MenuCustomizationApplication.KEY_FG_YELLOW_B, 0f);
            _fgBlueOn = SettingsService.Get(MenuCustomizationApplication.KEY_FG_BLUE_ON, "false") == "true";
            _fgBlueR = P(MenuCustomizationApplication.KEY_FG_BLUE_R, 0.1f);
            _fgBlueG = P(MenuCustomizationApplication.KEY_FG_BLUE_G, 0.25f);
            _fgBlueB = P(MenuCustomizationApplication.KEY_FG_BLUE_B, 0.85f);
            _fgPinkOn = SettingsService.Get(MenuCustomizationApplication.KEY_FG_PINK_ON, "false") == "true";
            _fgPinkR = P(MenuCustomizationApplication.KEY_FG_PINK_R, 1f);
            _fgPinkG = P(MenuCustomizationApplication.KEY_FG_PINK_G, 0.2f);
            _fgPinkB = P(MenuCustomizationApplication.KEY_FG_PINK_B, 0.5f);
            _fgOrangeOn = SettingsService.Get(MenuCustomizationApplication.KEY_FG_ORANGE_ON, "false") == "true";
            _fgOrangeR = P(MenuCustomizationApplication.KEY_FG_ORANGE_R, 1f);
            _fgOrangeG = P(MenuCustomizationApplication.KEY_FG_ORANGE_G, 0.55f);
            _fgOrangeB = P(MenuCustomizationApplication.KEY_FG_ORANGE_B, 0.1f);
        }

        private void SaveSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            void S(string k, float v) => SettingsService.Set(k, v.ToString(ci));

            SettingsService.Set(MenuCustomizationApplication.KEY_FG_CYAN_ON, _fgCyanOn ? "true" : "false");
            S(MenuCustomizationApplication.KEY_FG_CYAN_R, _fgCyanR);
            S(MenuCustomizationApplication.KEY_FG_CYAN_G, _fgCyanG);
            S(MenuCustomizationApplication.KEY_FG_CYAN_B, _fgCyanB);
            SettingsService.Set(MenuCustomizationApplication.KEY_FG_BLACK_ON, _fgBlackOn ? "true" : "false");
            S(MenuCustomizationApplication.KEY_FG_BLACK_R, _fgBlackR);
            S(MenuCustomizationApplication.KEY_FG_BLACK_G, _fgBlackG);
            S(MenuCustomizationApplication.KEY_FG_BLACK_B, _fgBlackB);
            SettingsService.Set(MenuCustomizationApplication.KEY_FG_YELLOW_ON, _fgYellowOn ? "true" : "false");
            S(MenuCustomizationApplication.KEY_FG_YELLOW_R, _fgYellowR);
            S(MenuCustomizationApplication.KEY_FG_YELLOW_G, _fgYellowG);
            S(MenuCustomizationApplication.KEY_FG_YELLOW_B, _fgYellowB);
            SettingsService.Set(MenuCustomizationApplication.KEY_FG_BLUE_ON, _fgBlueOn ? "true" : "false");
            S(MenuCustomizationApplication.KEY_FG_BLUE_R, _fgBlueR);
            S(MenuCustomizationApplication.KEY_FG_BLUE_G, _fgBlueG);
            S(MenuCustomizationApplication.KEY_FG_BLUE_B, _fgBlueB);
            SettingsService.Set(MenuCustomizationApplication.KEY_FG_PINK_ON, _fgPinkOn ? "true" : "false");
            S(MenuCustomizationApplication.KEY_FG_PINK_R, _fgPinkR);
            S(MenuCustomizationApplication.KEY_FG_PINK_G, _fgPinkG);
            S(MenuCustomizationApplication.KEY_FG_PINK_B, _fgPinkB);
            SettingsService.Set(MenuCustomizationApplication.KEY_FG_ORANGE_ON, _fgOrangeOn ? "true" : "false");
            S(MenuCustomizationApplication.KEY_FG_ORANGE_R, _fgOrangeR);
            S(MenuCustomizationApplication.KEY_FG_ORANGE_G, _fgOrangeG);
            S(MenuCustomizationApplication.KEY_FG_ORANGE_B, _fgOrangeB);
        }

        // ── Slider helpers ────────────────────────────────────────────────────

        private Slider BuildSlider(Transform parent, float x, float y, float w,
            string lbl, float init, Action<float> onChange,
            Color? labelColor = null, Color? fillColor = null)
            => UGUIShip.CreateSlider(parent, x, y, w, lbl, init, LH, PAD, FS_SM, onChange, labelColor, fillColor);

        private Slider BuildSliderRaw(Transform parent, float x, float y, float w,
            string lbl, float init, float min, float max, Action<float> onChange)
            => UGUIShip.CreateSlider(parent, x, y, w, lbl, Mathf.InverseLerp(min, max, init),
                LH, PAD, FS_SM, t => onChange(Mathf.Lerp(min, max, t)));
    }
}
