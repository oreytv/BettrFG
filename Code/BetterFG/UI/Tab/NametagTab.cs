using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BetterFG.Services;
using BetterFG.Core;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using BetterFG.UI;
using BetterFG.Nametag;
using BetterFG.Features.MorePlatformIcon;
using HarmonyLib;
using FGClient;

namespace BetterFG.UI.Tab
{
    // the nametag font + materials don't exist at the absolute start of the game (nothing loaded yet),
    // so cache them once the menu is entered and again when a loading screen shows — that's when the
    // game has those assets around. NametagTab.CacheNameAssets is idempotent (early-outs once filled).
    // both are driven from the shared OnMainMenuEntered + LoadingScreenViewModel.UpdateDisplay hubs.

    public class NametagTab : BetterFGTab
    {
        public NametagTab(IntPtr ptr) : base(ptr) { }

        public static NametagTab Instance { get; private set; }

        void Awake() => Instance = this;

        public override string TabTitle => "Nametag";

        // ── Sub-tab ───────────────────────────────────────────────────────────
        private enum SubTab { Colour, Icon, Nameplate, CrownRank }
        private SubTab _sub = SubTab.Colour;

        // ── Settings keys ─────────────────────────────────────────────────────
        private const string KEY_COLOR_R = "nametag.color.r";
        private const string KEY_COLOR_G = "nametag.color.g";
        private const string KEY_COLOR_B = "nametag.color.b";
        private const string KEY_BOLD = "nametag.bold";
        private const string KEY_ITALIC = "nametag.italic";
        private const string KEY_ENABLED = "nametag.enabled";
        private const string KEY_CUSTOM_NAME = "nametag.customname";
        private const string KEY_ICON_MODE = "nametag.icon.mode";
        private const string KEY_ICON_COUNTRY = "nametag.icon.country";
        private const string KEY_ICON_PATH = "nametag.icon.path";
        private const string KEY_ICON_SCALE = "nametag.icon.scale";
        private const string KEY_ICON_OFFSET_X = "nametag.icon.offset.x";
        private const string KEY_ICON_OFFSET_Y = "nametag.icon.offset.y";
        private const string KEY_PLATFORM_HIDE = "nametag.platform.hide";
        private const string KEY_PLATFORM_CUSTOM = "nametag.platform.custom";
        private const string KEY_NAME_STYLE = "nametag.namestyle";
        private const string KEY_BACKING_ENABLED = "nametag.backing.enabled";
        private const string KEY_BACKING_PATH = "nametag.backing.path";
        private const string KEY_BACKING_OFFSET_X = "nametag.backing.offset.x";
        private const string KEY_BACKING_OFFSET_Y = "nametag.backing.offset.y";
        private const string KEY_BACKING_SCALE = "nametag.backing.scale";
        private const string KEY_NICKNAME_ENABLED = "nametag.nickname.enabled";
        private const string KEY_NICKNAME_TEXT = "nametag.nickname.text";

        // ── Colors ────────────────────────────────────────────────────────────
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color WHITE = Color.white;
        private static readonly Color BTN_APPLY = new Color(0.45f, 0.35f, 0.25f, 1f);
        private static readonly Color BTN_REMOVE = new Color(0.55f, 0.15f, 0.15f, 1f);
        private static readonly Color PANEL_BG = new Color(0f, 0f, 0f, 0.35f);
        private static readonly Color SEL_COLOR = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color ICON_OFF = new Color(1f, 1f, 1f, 0.3f);

        // ── Textures ──────────────────────────────────────────────────────────
        private static Texture2D _bgTex;
        private static Texture2D _bgHoverTex;
        private GameObject _bgHoverGo;

        // ── Cached name assets ────────────────────────────────────────────────
        // the nametag font + materials only exist once the game has loaded into the menu.
        // building the preview at the absolute start of the game (nothing loaded) finds nothing,
        // so we cache them on menu-entered / loading-screen-show and the preview reads these.
        private static TMP_FontAsset _cachedFont;
        private static Material _cachedDefaultMat;
        private static Material _cachedGoldMat;

        public static void CacheNameAssets()
        {
            if (_cachedFont == null) _cachedFont = BetterFG.Core.AssetManager.NameFontAsset;
            if (_cachedDefaultMat == null) _cachedDefaultMat = BetterFG.Core.AssetManager.DefaultNameMaterial;
            if (_cachedGoldMat == null) _cachedGoldMat = BetterFG.Core.AssetManager.GoldNameMaterial;
            // if the tab is already open, the preview may have been built before a nametag existed to clone —
            // refresh it now that assets (and likely a live canvas) exist.
            if (Instance != null) Instance.RefreshPreview();
        }

        // ── State ─────────────────────────────────────────────────────────────
        private float _r = 1f, _g = 1f, _b = 1f;
        private bool _bold = false;
        private bool _italic = false;
        private string _customName = "";

        private enum IconMode { None, Flag, Custom }
        private IconMode _iconMode = IconMode.None;
        private string _selectedCountry = "";
        private string _customIconPath = "";
        private float _iconScale = 1f;
        private float _iconOffsetX = 0f;
        private float _iconOffsetY = 0f;
        private bool _iconTransformApplyPending = false;
        private Coroutine _iconTransformApplyRoutine = null;

        private enum PlatformHideMode { None, Self, Everyone }
        private PlatformHideMode _platformHide = PlatformHideMode.None;
        private string _platformCustom = "";

        private enum NameStyle { None, Default, Gold, GoldColored }
        private NameStyle _nameStyle = NameStyle.Default;

        private bool _backingEnabled = false;
        private string _backingPath = "";
        private const float BACKING_SCALE_MAX = 10f;
        private float _backingOffX = 0f;
        private float _backingOffY = 0f;
        private float _backingScale = 1f;
        private bool _backingApplyPending = false;
        private Coroutine _backingApplyRoutine = null;
        private bool _nicknameEnabled = false;
        private string _nicknameText = "";

        // ── UGUI refs ─────────────────────────────────────────────────────────
        private Button _btnSubColour, _btnSubIcon, _btnSubNameplate, _btnSubCrown;
        private GameObject _colourPanel, _iconPanel, _nameplatePanel, _crownPanel;

        // crown rank panel refs
        private Text _crownEnabledLabel, _crownRecolourOnLabel, _crownSwapLabel;
        private InputField _crownRankField;
        private RawImage _crownPreview;
        private float _crMainR, _crMainG, _crMainB;
        private float _crHiR, _crHiG, _crHiB;
        private float _crOutR, _crOutG, _crOutB;

        // nameplate panel refs
        private Text _backingEnabledLabel;
        private Text _backingPathLabel;
        private RawImage _backingPreview;
        private Slider _sliderBackingScale, _sliderBackingOffX, _sliderBackingOffY;
        private Text _nicknameEnabledLabel;
        private InputField _nicknameField;

        // colour panel refs
        private Slider _sliderR, _sliderG, _sliderB;
        private Text _boldBtnLabel, _italicBtnLabel;
        private Button _btnStyleNone, _btnStyleDefault, _btnStyleGold, _btnStyleGoldColored;

        // shared preview: a real cloned PlayerInfoDisplayCanvas driven to show the mod's name/icon/crown,
        // mirroring the in-game UI nametag. lives above the sub-tab panels.
        private GameObject _previewRoot;       // the "Preview" panel background
        private RectTransform _previewPanelRt;  // where the clone gets parented + centred
        private GameObject _previewClone;       // the instantiated PlayerInfoDisplayCanvas gameObject
        private PlayerInfoDisplayCanvas _previewCanvas;

        // icon panel refs
        private GameObject _flagListRoot;
        private GameObject _customIconRow;
        private GameObject _flagSection, _customSection;
        private Text _customIconLabel;
        private RectTransform _scrollContent;
        private RectTransform _platformIconContent;
        private Button _btnNone, _btnFlag, _btnCustom;
        private Slider _sliderIconScale, _sliderIconOffX, _sliderIconOffY;
        private Button _btnPlatNone, _btnPlatSelf, _btnPlatEveryone;

        // ── Layout shorthands ─────────────────────────────────────────────────
        private static float PAD => UIScale.PAD;
        private static float VPAD => UIScale.VPAD;
        private static float LH => UIScale.LH;
        private static float SH => UIScale.SH;
        private static float BTN_H => UIScale.BTN_H;
        private static int FS => UIScale.FS;
        private static int FS_SM => UIScale.FS_SM;

        private static float subTabH => BTN_H * 0.9f;
        private static float FLAG_ROW_H => UIScale.BTN_H * 0.72f;
        private static float FLAG_ICON_SIZE => FLAG_ROW_H * 0.75f;
        private static float CUSTOM_ICON_ROW_H => BTN_H + PAD + 1f + PAD + (LH + SH) * 3f;
        private const float PLATFORM_ICON_GRID_H = 117f;

        private static Texture2D ResizeTo32(Texture2D src)
        {
            const int SIZE = 32;
            var rt = RenderTexture.GetTemporary(SIZE, SIZE, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;
            RenderTexture.active = rt;
            Graphics.Blit(src, rt);
            var dst = new Texture2D(SIZE, SIZE, TextureFormat.RGBA32, false);
            dst.ReadPixels(new Rect(0, 0, SIZE, SIZE), 0, 0);
            dst.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            return dst;
        }

        private static Texture2D LoadEmbedded(string resourceName, ref Texture2D cache)
        {
            if (cache != null) return cache;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream(resourceName);
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.wrapMode = TextureWrapMode.Clamp;
                cache = tex;
            }
            catch (Exception ex) { Plugin.Log.LogError($"NametagUI: LoadEmbedded {resourceName}: {ex.Message}"); }
            return cache;
        }

        // ── Tab overrides ─────────────────────────────────────────────────────

        protected override void BuildBackground(RectTransform root)
        {
            var bgTex = LoadEmbedded("BetterFG.assets.ui.nametag.bg.png", ref _bgTex);
            if (bgTex == null) return;

            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(root, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            bgRt.localScale = new Vector3(1.5015f, 1.3502f, 1f);
            bgRt.localPosition = new Vector3(267.7578f, 285.8921f, 0f);
            var raw = bgGo.AddComponent<RawImage>();
            raw.texture = bgTex;
            raw.raycastTarget = false;

            var hoverTex = LoadEmbedded("BetterFG.assets.ui.bg_hover.png", ref _bgHoverTex);
            if (hoverTex != null)
            {
                var hoverGo = new GameObject("BG_Hover");
                hoverGo.transform.SetParent(bgGo.transform, false);
                var hoverRt = hoverGo.AddComponent<RectTransform>();
                hoverRt.anchorMin = Vector2.zero;
                hoverRt.anchorMax = Vector2.one;
                hoverRt.offsetMin = hoverRt.offsetMax = Vector2.zero;
                hoverRt.localScale = Vector3.one;
                hoverRt.localPosition = Vector3.zero;
                hoverGo.AddComponent<RawImage>().texture = hoverTex;
                hoverGo.SetActive(false);
                _bgHoverGo = hoverGo;
            }
        }

        protected override void OnTitleHoverChanged(bool hovering)
        {
            if (_bgHoverGo != null) _bgHoverGo.SetActive(hovering);
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            LoadSettings();

            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            // sub-tab bar
            float qGap = PAD * 0.5f;
            float quarterw = (w - qGap * 3f) / 4f;
            _btnSubColour = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD, y, quarterw, subTabH), "Colour",
                _sub == SubTab.Colour ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() => SetSubTab(SubTab.Colour)));
            _btnSubIcon = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD + (quarterw + qGap) * 1f, y, quarterw, subTabH), "Icon",
                _sub == SubTab.Icon ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() => SetSubTab(SubTab.Icon)));
            _btnSubNameplate = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD + (quarterw + qGap) * 2f, y, quarterw, subTabH), "Nameplate",
                _sub == SubTab.Nameplate ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() => SetSubTab(SubTab.Nameplate)));
            _btnSubCrown = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD + (quarterw + qGap) * 3f, y, quarterw, subTabH), "Crown Rank",
                _sub == SubTab.CrownRank ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() => SetSubTab(SubTab.CrownRank)));
            y += subTabH + SH;

            UGUIShip.CreatePanel(contentRoot, new Rect(PAD, y, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            y += 1f + SH;

            float btnRowH = BTN_H + PAD;

            // shared live preview at the top of the Colour / Icon / Crown Rank sub-tabs — name + icon + crown
            // laid out like the in-game nametag (order follows the crown-side swap). hidden on Nameplate.
            float sharedPrevH = (TabHeight - y - VPAD - btnRowH - 1f - PAD) * 0.22f;
            BuildPreviewPanel(contentRoot, PAD, y, w, sharedPrevH);
            y += sharedPrevH + SH;
            UGUIShip.CreatePanel(contentRoot, new Rect(PAD, y, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            y += 1f + SH;

            float bodyH = TabHeight - y - VPAD - btnRowH - 1f - PAD;

            // colour panel
            _colourPanel = new GameObject("ColourPanel");
            _colourPanel.transform.SetParent(contentRoot, false);
            var cpRt = _colourPanel.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(cpRt, new Rect(0f, y, TabWidth, bodyH));
            BuildColourPanel(cpRt, PAD, 0f, w, bodyH);

            // icon panel
            _iconPanel = new GameObject("IconPanel");
            _iconPanel.transform.SetParent(contentRoot, false);
            var ipRt = _iconPanel.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(ipRt, new Rect(0f, y, TabWidth, bodyH));
            BuildIconPanel(ipRt, PAD, 0f, w, bodyH);

            // nameplate panel
            _nameplatePanel = new GameObject("NameplatePanel");
            _nameplatePanel.transform.SetParent(contentRoot, false);
            var npRt = _nameplatePanel.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(npRt, new Rect(0f, y, TabWidth, bodyH));
            BuildNameplatePanel(npRt, PAD, 0f, w, bodyH);

            // crown rank panel
            _crownPanel = new GameObject("CrownRankPanel");
            _crownPanel.transform.SetParent(contentRoot, false);
            var crRt = _crownPanel.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(crRt, new Rect(0f, y, TabWidth, bodyH));
            BuildCrownPanel(crRt, PAD, 0f, w, bodyH);

            // apply/remove always visible below both panels
            float by = y + bodyH + PAD;
            UGUIShip.CreatePanel(contentRoot, new Rect(PAD, by, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            by += 1f + PAD;
            float btnw = (w - PAD * 0.5f) / 2f;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD, by, btnw, BTN_H),
                "Apply", BTN_APPLY, WHITE, FS, new Action(OnApply));
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + btnw + PAD * 0.5f, by, btnw, BTN_H),
                "Remove", BTN_REMOVE, WHITE, FS, new Action(OnRemove));

            RefreshSubTabVisibility();
        }

        // ── Sub-tab switching ─────────────────────────────────────────────────

        private void SetSubTab(SubTab sub)
        {
            _sub = sub;
            UGUIShip.SetButtonSelected(_btnSubColour, sub == SubTab.Colour, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnSubIcon, sub == SubTab.Icon, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnSubNameplate, sub == SubTab.Nameplate, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnSubCrown, sub == SubTab.CrownRank, SEL_COLOR);
            RefreshSubTabVisibility();
        }

        private void RefreshSubTabVisibility()
        {
            if (_colourPanel != null) _colourPanel.SetActive(_sub == SubTab.Colour);
            if (_iconPanel != null) _iconPanel.SetActive(_sub == SubTab.Icon);
            if (_nameplatePanel != null) _nameplatePanel.SetActive(_sub == SubTab.Nameplate);
            if (_crownPanel != null) _crownPanel.SetActive(_sub == SubTab.CrownRank);

            // shared preview rides above Colour / Icon / Crown Rank; the Nameplate tab has its own preview.
            if (_previewRoot != null) _previewRoot.SetActive(_sub != SubTab.Nameplate);
            if (_sub != SubTab.Nameplate) RefreshPreview();
        }

        // ── Colour panel ──────────────────────────────────────────────────────

        private void BuildColourPanel(RectTransform parent, float x, float y, float w, float h)
        {
            float cy = y + PAD;
            float sw = w;

            UGUIShip.CreateLabel(parent, new Rect(x, cy, sw, LH), "COLOUR", FS_SM, HINT);
            cy += LH + SH;

            UGUIShip.CreateColorControls(parent, x, ref cy, sw,
                () => _r, () => _g, () => _b,
                v => _r = v, v => _g = v, v => _b = v, () => RefreshSampleText(),
                out _sliderR, out _sliderG, out _sliderB);

            UGUIShip.CreatePanel(parent, new Rect(x, cy, sw, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            UGUIShip.CreateLabel(parent, new Rect(x, cy, sw, LH), "STYLE", FS_SM, HINT);
            cy += LH + SH;

            float togglew = (sw - PAD * 0.5f) / 2f;
            var boldBtn = UGUIShip.CreateButton(parent,
                new Rect(x, cy, togglew, BTN_H),
                _bold ? "Bold: ON" : "Bold: OFF",
                BTN_DARK, WHITE, FS_SM, new Action(OnToggleBold));
            _boldBtnLabel = boldBtn.GetComponentInChildren<Text>();

            var italicBtn = UGUIShip.CreateButton(parent,
                new Rect(x + togglew + PAD * 0.5f, cy, togglew, BTN_H),
                _italic ? "Italic: ON" : "Italic: OFF",
                BTN_DARK, WHITE, FS_SM, new Action(OnToggleItalic));
            _italicBtnLabel = italicBtn.GetComponentInChildren<Text>();
            cy += BTN_H + PAD;

            UGUIShip.CreatePanel(parent, new Rect(x, cy, sw, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            UGUIShip.CreateLabel(parent, new Rect(x, cy, sw, LH), "PREDEFINED STYLE", FS_SM, HINT);
            cy += LH + SH;

            float stylew = (sw - PAD * 1.5f) / 4f;
            float stylestep = stylew + PAD * 0.5f;
            _btnStyleNone = UGUIShip.CreateButton(parent,
                new Rect(x, cy, stylew, BTN_H), "None",
                _nameStyle == NameStyle.None ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() => SetNameStyle(NameStyle.None)));
            _btnStyleDefault = UGUIShip.CreateButton(parent,
                new Rect(x + stylestep, cy, stylew, BTN_H), "Default",
                _nameStyle == NameStyle.Default ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() => SetNameStyle(NameStyle.Default)));
            _btnStyleGold = UGUIShip.CreateButton(parent,
                new Rect(x + stylestep * 2f, cy, stylew, BTN_H), "Gold",
                _nameStyle == NameStyle.Gold ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() => SetNameStyle(NameStyle.Gold)));
            _btnStyleGoldColored = UGUIShip.CreateButton(parent,
                new Rect(x + stylestep * 3f, cy, stylew, BTN_H), "Gold RGB",
                _nameStyle == NameStyle.GoldColored ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() => SetNameStyle(NameStyle.GoldColored)));
            cy += BTN_H + PAD;
        }

        // ── Preview panel ─────────────────────────────────────────────────────

        private void BuildPreviewPanel(RectTransform parent, float x, float y, float w, float h)
        {
            var panelGo = new GameObject("Preview");
            panelGo.transform.SetParent(parent, false);
            var rt = panelGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(rt, new Rect(x, y, w, h));
            panelGo.AddComponent<Image>().color = PANEL_BG;
            _previewRoot = panelGo;

            // the clone gets parented into this centred anchor. we don't build any of the nametag by hand — a
            // live PlayerInfoDisplayCanvas is cloned in on demand (EnsurePreviewClone) once one exists in the
            // scene, then driven to show the mod's name/icon/crown.
            var holderGo = new GameObject("PreviewHolder");
            holderGo.transform.SetParent(panelGo.transform, false);
            _previewPanelRt = holderGo.AddComponent<RectTransform>();
            _previewPanelRt.anchorMin = _previewPanelRt.anchorMax = new Vector2(0.5f, 0.5f);
            _previewPanelRt.pivot = new Vector2(0.5f, 0.5f);
            _previewPanelRt.sizeDelta = new Vector2(w, h);
            _previewPanelRt.anchoredPosition = Vector2.zero;
            // don't RefreshPreview here — assets/nametag don't exist at build time; it runs on sub-tab show.
        }

        // instantiate a real PlayerInfoDisplayCanvas into the preview once one exists in the scene. cheap after
        // the first success (clone is cached). returns false while no live canvas exists yet (menus pre-nametag).
        private bool EnsurePreviewClone()
        {
            if (_previewClone != null && _previewCanvas != null) return true;
            if (_previewPanelRt == null) return false;

            PlayerInfoDisplayCanvas src = null;
            foreach (var c in Resources.FindObjectsOfTypeAll<PlayerInfoDisplayCanvas>())
            {
                // skip our own clone and prefabs with no text wired.
                if (c == null || c.gameObject == _previewClone) continue;
                if (c._text != null) { src = c; break; }
            }
            if (src == null) return false;

            _previewClone = UnityEngine.Object.Instantiate(src.gameObject, _previewPanelRt);
            _previewClone.name = "PreviewNametagClone";
            _previewCanvas = _previewClone.GetComponent<PlayerInfoDisplayCanvas>();

            var crt = _previewClone.GetComponent<RectTransform>();
            if (crt != null)
            {
                crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
                crt.pivot = new Vector2(0.5f, 0.5f);
                crt.anchoredPosition = Vector2.zero;
                crt.localScale = Vector3.one;
            }
            _previewClone.SetActive(true);
            return true;
        }

        // ── Icon panel ────────────────────────────────────────────────────────

        private void BuildIconPanel(RectTransform parent, float x, float y, float w, float h)
        {
            // the shared preview above the sub-tabs shrank the body, so the platform section fell off the
            // bottom. wrap everything in one vertical ScrollRect (with the shared scrollbar) — each section is a
            // fixed-height child of a VerticalLayoutGroup content, so the whole panel scrolls as a unit. the
            // flag list is inline (no inner scroll) so there's a single scroll surface.
            var (scroll, content) = UGUIShip.CreateScrollView(parent, new Rect(x, y, w, h));
            // the sections are laid out at width w minus the scrollbar inset, so nothing sits under the bar.
            float cw = w - UGUIShip.SCROLLBAR_INSET;
            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
            vlg.spacing = SH; vlg.padding = new RectOffset(0, 0, (int)PAD, (int)PAD);
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            UGUIShip.CreateLabel(AddSection(content, LH, cw), new Rect(0f, 0f, cw, LH), "ICON", FS_SM, HINT);

            var modeRow = AddSection(content, BTN_H, cw);
            float modew = (cw - PAD) / 3f;
            _btnNone = UGUIShip.CreateButton(modeRow, new Rect(0f, 0f, modew, BTN_H), "None",
                _iconMode == IconMode.None ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetIconMode(IconMode.None)));
            _btnFlag = UGUIShip.CreateButton(modeRow, new Rect(modew + PAD * 0.5f, 0f, modew, BTN_H), "Flag",
                _iconMode == IconMode.Flag ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetIconMode(IconMode.Flag)));
            _btnCustom = UGUIShip.CreateButton(modeRow, new Rect((modew + PAD * 0.5f) * 2f, 0f, modew, BTN_H), "Custom",
                _iconMode == IconMode.Custom ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetIconMode(IconMode.Custom)));

            UGUIShip.CreatePanel(AddSection(content, 1f, cw), new Rect(0f, 0f, cw, 1f), new Color(1f, 1f, 1f, 0.06f));

            // flag list + custom row each get their own section; only the active mode's section is shown, so
            // the hidden one collapses out of the VLG (no empty gap). flag list is inline and sized to all its
            // rows so the outer scroll owns it.
            float flagListH = FlagAssets.GetAvailableCodes().Length * FLAG_ROW_H;
            _flagSection = AddSection(content, flagListH, cw).gameObject;
            BuildCountryList(_flagSection.transform, 0f, 0f, cw, flagListH);
            _customSection = AddSection(content, CUSTOM_ICON_ROW_H, cw).gameObject;
            BuildCustomIconRow(_customSection.transform, 0f, 0f, cw);

            UGUIShip.CreatePanel(AddSection(content, 1f, cw), new Rect(0f, 0f, cw, 1f), new Color(1f, 1f, 1f, 0.06f));

            BuildPlatformSection(AddSection(content, MeasurePlatformSectionH(), cw), 0f, 0f, cw);

            RefreshIconModeVisibility();
        }

        // a fixed-height, full-width child of the icon panel's VLG content, returned as a Transform to build
        // into with the existing absolute-Rect helpers.
        private Transform AddSection(Transform content, float height, float w)
        {
            var go = new GameObject("Section");
            go.transform.SetParent(content, false);
            go.AddComponent<RectTransform>();
            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height; le.preferredWidth = w; le.flexibleWidth = 1f;
            return go.transform;
        }

        // ── Flag / country list ───────────────────────────────────────────────

        private void BuildCountryList(Transform parent, float x, float y, float w, float h)
        {
            // inline list (no inner scroll) — the icon panel's single outer ScrollRect owns scrolling now. this
            // just fills its section with a VLG of country rows.
            _flagListRoot = new GameObject("FlagList");
            _flagListRoot.transform.SetParent(parent, false);
            var rt = _flagListRoot.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            _scrollContent = rt;

            var vlg = _flagListRoot.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            vlg.padding = new RectOffset((int)(PAD * 0.5f), (int)(PAD * 0.5f), 0, 0);

            Sprite btnSpr = UGUIShip.GetButtonSprite();

            foreach (string code in FlagAssets.GetAvailableCodes())
            {
                string captured = code;
                bool isSelected = string.Equals(code, _selectedCountry, StringComparison.OrdinalIgnoreCase);

                var rowGo = new GameObject("ISO_" + code);
                rowGo.transform.SetParent(_scrollContent, false);
                rowGo.AddComponent<RectTransform>();
                rowGo.AddComponent<LayoutElement>().preferredHeight = FLAG_ROW_H;

                var rowImg = rowGo.AddComponent<Image>();
                if (isSelected && btnSpr != null)
                {
                    rowImg.sprite = btnSpr;
                    rowImg.type = Image.Type.Simple;
                    rowImg.color = SEL_COLOR;
                }
                else
                {
                    rowImg.color = Color.clear;
                }

                var btn = rowGo.AddComponent<Button>();
                btn.targetGraphic = rowImg;
                var cols = btn.colors;
                cols.normalColor = isSelected ? SEL_COLOR : Color.clear;
                cols.highlightedColor = new Color(1f, 1f, 1f, 0.08f);
                btn.colors = cols;
                btn.onClick.AddListener(new Action(() => OnSelectCountry(captured)));

                var lbl = new GameObject("Label");
                lbl.transform.SetParent(rowGo.transform, false);
                var lblRt = lbl.AddComponent<RectTransform>();
                lblRt.anchorMin = Vector2.zero;
                lblRt.anchorMax = new Vector2(1f, 1f);
                lblRt.offsetMin = new Vector2(PAD, 0f);
                lblRt.offsetMax = new Vector2(-(FLAG_ICON_SIZE + PAD * 2f), 0f);
                var t = lbl.AddComponent<Text>();
                t.text = code;
                t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                t.fontSize = FS_SM;
                t.color = isSelected ? WHITE : new Color(1f, 1f, 1f, 0.7f);
                t.alignment = TextAnchor.MiddleLeft;

                Sprite flagSpr = FlagAssets.LoadFlag(code);
                if (flagSpr != null)
                {
                    var iconGo = new GameObject("FlagIcon");
                    iconGo.transform.SetParent(rowGo.transform, false);
                    var iconRt = iconGo.AddComponent<RectTransform>();
                    iconRt.anchorMin = new Vector2(1f, 0.5f);
                    iconRt.anchorMax = new Vector2(1f, 0.5f);
                    iconRt.pivot = new Vector2(1f, 0.5f);
                    iconRt.anchoredPosition = new Vector2(-PAD, 0f);
                    iconRt.sizeDelta = new Vector2(FLAG_ICON_SIZE * 1.5f, FLAG_ICON_SIZE);
                    var iconImg = iconGo.AddComponent<Image>();
                    iconImg.sprite = flagSpr;
                    iconImg.preserveAspect = true;
                    iconImg.raycastTarget = false;
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
        }

        private void BuildCustomIconRow(Transform parent, float x, float y, float w)
        {
            _customIconRow = new GameObject("CustomIconRow");
            _customIconRow.transform.SetParent(parent, false);
            var rt = _customIconRow.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(rt, new Rect(x, y, w, CUSTOM_ICON_ROW_H));

            float cy = 0f;
            float btnW = w * 0.45f;

            UGUIShip.CreateButton(_customIconRow.transform,
                new Rect(0f, cy, btnW, BTN_H),
                "Browse...",
                new Color(0.25f, 0.35f, 0.45f, 1f),
                WHITE, FS_SM, new Action(OnBrowseCustomIcon));

            _customIconLabel = UGUIShip.CreateLabel(_customIconRow.transform,
                new Rect(btnW + PAD, cy, w - btnW - PAD, BTN_H),
                string.IsNullOrEmpty(_customIconPath) ? "No file selected" : Path.GetFileName(_customIconPath),
                FS_SM, HINT);
            cy += BTN_H + PAD;

            UGUIShip.CreatePanel(_customIconRow.transform, new Rect(0f, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            _sliderIconScale = BuildSlider(_customIconRow.transform, 0f, cy, w,
                "S", _iconScale * 0.5f,
                val => { _iconScale = val * 2f; SaveIconTransform(); QueueIconTransformApply(); });
            cy += LH + SH;

            _sliderIconOffX = BuildSlider(_customIconRow.transform, 0f, cy, w,
                "X", _iconOffsetX + 0.5f,
                val => { _iconOffsetX = val - 0.5f; SaveIconTransform(); QueueIconTransformApply(); });
            cy += LH + SH;

            _sliderIconOffY = BuildSlider(_customIconRow.transform, 0f, cy, w,
                "Y", _iconOffsetY + 0.5f,
                val => { _iconOffsetY = val - 0.5f; SaveIconTransform(); QueueIconTransformApply(); });
        }

        // ── Platform section ──────────────────────────────────────────────────

        private float MeasurePlatformSectionH()
        {
            return LH + SH + BTN_H + PAD + 1f + PAD + LH + SH + PLATFORM_ICON_GRID_H + PAD;
        }

        private void BuildPlatformSection(Transform parent, float x, float y, float w)
        {
            float cy = y;

            UGUIShip.CreateLabel(parent, new Rect(x, cy, w, LH), "DISABLE PLATFORM ICON", FS_SM, HINT);
            cy += LH + SH;

            float modew = (w - PAD) / 3f;

            _btnPlatNone = UGUIShip.CreateButton(parent,
                new Rect(x, cy, modew, BTN_H), "None",
                _platformHide == PlatformHideMode.None ? SEL_COLOR : BTN_DARK,
                WHITE, FS_SM, new Action(() => SetPlatformHide(PlatformHideMode.None)));

            _btnPlatSelf = UGUIShip.CreateButton(parent,
                new Rect(x + modew + PAD * 0.5f, cy, modew, BTN_H), "Yourself",
                _platformHide == PlatformHideMode.Self ? SEL_COLOR : BTN_DARK,
                WHITE, FS_SM, new Action(() => SetPlatformHide(PlatformHideMode.Self)));

            _btnPlatEveryone = UGUIShip.CreateButton(parent,
                new Rect(x + (modew + PAD * 0.5f) * 2f, cy, modew, BTN_H), "Everyone",
                _platformHide == PlatformHideMode.Everyone ? SEL_COLOR : BTN_DARK,
                WHITE, FS_SM, new Action(() => SetPlatformHide(PlatformHideMode.Everyone)));

            cy += BTN_H + PAD;
            UGUIShip.CreatePanel(parent, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            UGUIShip.CreateLabel(parent, new Rect(x, cy, w, LH), "CUSTOM PLATFORM ICON (local)", FS_SM, HINT);
            cy += LH + SH;

            BuildPlatformIconButtons(parent, x, cy, w, PLATFORM_ICON_GRID_H);
        }

        private void BuildPlatformIconButtons(Transform parent, float x, float y, float w, float h)
        {
            var root = new GameObject("PlatformIconButtons");
            root.transform.SetParent(parent, false);
            _platformIconContent = root.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(_platformIconContent, new Rect(x, y, w, h));

            // "" leads as the None tile (its sprite is no.png), then the platform ids.
            var ids = new System.Collections.Generic.List<string> { "" };
            ids.AddRange(FeatureMorePlatformIcon.PlatformIconIds());
            int iconCols = 5;
            int rows = (ids.Count + iconCols - 1) / iconCols;
            float gap = 4f;
            float cellW = (w - gap * (iconCols - 1)) / iconCols;
            float cellH = (h - gap * (rows - 1)) / rows;

            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                int col = i % iconCols;
                int row = i / iconCols;
                bool selected = string.Equals(_platformCustom, id, StringComparison.OrdinalIgnoreCase);

                var btn = UGUIShip.CreateButton(root.transform,
                    new Rect(col * (cellW + gap), row * (cellH + gap), cellW, cellH),
                    "", Color.clear, WHITE, FS_SM, new Action(() => SetPlatformCustom(id)),
                    customSprite: false);
                btn.name = "PlatformIcon_" + id;
                btn.transition = Selectable.Transition.None;
                var bg = btn.GetComponent<Image>();
                if (bg != null) bg.color = Color.clear;
                var cols = btn.colors;
                cols.normalColor = Color.clear;
                cols.highlightedColor = Color.clear;
                cols.pressedColor = Color.clear;
                cols.disabledColor = Color.clear;
                cols.colorMultiplier = 1f;
                btn.colors = cols;

                var spr = string.IsNullOrEmpty(id) ? NoneIconSprite() : FeatureMorePlatformIcon.SpriteForName(id);
                if (spr != null)
                {
                    var iconGo = new GameObject("Icon");
                    iconGo.transform.SetParent(btn.transform, false);
                    var iconRt = iconGo.AddComponent<RectTransform>();
                    iconRt.anchorMin = new Vector2(0.5f, 0.5f);
                    iconRt.anchorMax = new Vector2(0.5f, 0.5f);
                    iconRt.pivot = new Vector2(0.5f, 0.5f);
                    iconRt.anchoredPosition = Vector2.zero;
                    iconRt.sizeDelta = new Vector2(cellH * 0.78f, cellH * 0.78f);
                    var img = iconGo.AddComponent<Image>();
                    img.sprite = spr;
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    img.color = selected ? WHITE : ICON_OFF;
                }
            }
        }

        private static Texture2D _noneIconTex;
        private static Sprite _noneIconSprite;
        private static Sprite NoneIconSprite()
        {
            if (_noneIconSprite != null) return _noneIconSprite;
            var tex = LoadEmbedded("BetterFG.assets.ui.feature.moreplatformicon.no.png", ref _noneIconTex);
            if (tex == null) return null;
            _noneIconSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            return _noneIconSprite;
        }

        // ── Nameplate panel ───────────────────────────────────────────────────

        private void BuildNameplatePanel(RectTransform parent, float x, float y, float w, float _h)
        {
            float cy = y + PAD;

            // toggle + browse on one row
            float colGap = PAD * 0.5f;
            float halfW = (w - colGap) / 2f;
            var enabledBtn = UGUIShip.CreateButton(parent,
                new Rect(x, cy, halfW, BTN_H),
                _backingEnabled ? "Backing: ON" : "Backing: OFF",
                BTN_DARK, WHITE, FS_SM, new Action(OnToggleBacking));
            _backingEnabledLabel = enabledBtn.GetComponentInChildren<Text>();

            UGUIShip.CreateButton(parent,
                new Rect(x + halfW + colGap, cy, halfW, BTN_H),
                "Browse...",
                new Color(0.25f, 0.35f, 0.45f, 1f),
                WHITE, FS_SM, new Action(OnBrowseBacking));
            cy += BTN_H + SH;

            _backingPathLabel = UGUIShip.CreateLabel(parent,
                new Rect(x, cy, w, LH),
                string.IsNullOrEmpty(_backingPath) ? "No file selected" : Path.GetFileName(_backingPath),
                FS_SM, HINT);
            cy += LH + SH;

            // preview box (matches nameplate aspect 606x166 ≈ 3.65:1)
            float prevW = w;
            float prevH = w / 3.65f;
            var prevGo = new GameObject("BackingPreview");
            prevGo.transform.SetParent(parent, false);
            var prevRt = prevGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(prevRt, new Rect(x, cy, prevW, prevH));
            prevGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

            var rawGo = new GameObject("Raw");
            rawGo.transform.SetParent(prevGo.transform, false);
            var rawRt = rawGo.AddComponent<RectTransform>();
            rawRt.anchorMin = Vector2.zero;
            rawRt.anchorMax = Vector2.one;
            rawRt.offsetMin = rawRt.offsetMax = Vector2.zero;
            _backingPreview = rawGo.AddComponent<RawImage>();
            _backingPreview.raycastTarget = false;
            cy += prevH + SH;

            _sliderBackingScale = BuildSlider(parent, x, cy, w,
                "S", _backingScale / BACKING_SCALE_MAX,
                val => { _backingScale = val * BACKING_SCALE_MAX; SaveBackingTransform(); RefreshBackingPreview(); QueueBackingApply(); });
            cy += LH + SH;

            _sliderBackingOffX = BuildSlider(parent, x, cy, w,
                "X", _backingOffX + 0.5f,
                val => { _backingOffX = val - 0.5f; SaveBackingTransform(); RefreshBackingPreview(); QueueBackingApply(); });
            cy += LH + SH;

            _sliderBackingOffY = BuildSlider(parent, x, cy, w,
                "Y", _backingOffY + 0.5f,
                val => { _backingOffY = val - 0.5f; SaveBackingTransform(); RefreshBackingPreview(); QueueBackingApply(); });
            cy += LH + PAD;

            UGUIShip.CreatePanel(parent, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // nickname subtext: toggle + input on one row
            float nickColGap = PAD * 0.5f;
            float toggleW = w * 0.42f;
            var nickBtn = UGUIShip.CreateButton(parent,
                new Rect(x, cy, toggleW, BTN_H),
                _nicknameEnabled ? "Nickname: ON" : "Nickname: OFF",
                BTN_DARK, WHITE, FS_SM, new Action(OnToggleNickname));
            _nicknameEnabledLabel = nickBtn.GetComponentInChildren<Text>();

            float nickFieldW = w - toggleW - nickColGap;
            _nicknameField = UGUIShip.CreateInputField(parent,
                new Rect(x + toggleW + nickColGap, cy, nickFieldW, BTN_H),
                "nickname", new Color(0.12f, 0.12f, 0.12f, 1f), WHITE, FS_SM);
            UGUIShip.SetInputText(_nicknameField, _nicknameText, false);
            _nicknameField.onEndEdit.AddListener(new Action<string>(OnNicknameEdited));

            RefreshBackingPreview();
        }

        private void OnToggleNickname()
        {
            _nicknameEnabled = !_nicknameEnabled;
            if (_nicknameEnabledLabel != null)
                _nicknameEnabledLabel.text = _nicknameEnabled ? "Nickname: ON" : "Nickname: OFF";
            SettingsService.Set(KEY_NICKNAME_ENABLED, _nicknameEnabled ? "true" : "false");
            ApplyNicknameNow();
        }

        private void OnNicknameEdited(string value)
        {
            _nicknameText = value ?? "";
            SettingsService.Set(KEY_NICKNAME_TEXT, _nicknameText);
            ApplyNicknameNow();
        }

        private void ApplyNicknameNow()
        {
            NametagFinder.ReapplyAllNameplates();
            var localTag = NametagFinder.FindLocalNameTagSprite();
            if (localTag != null)
                NametagIconApplicator.ApplyLocalNickname(localTag, party: false);
        }

        private void RefreshBackingPreview()
        {
            if (_backingPreview == null) return;
            if (string.IsNullOrEmpty(_backingPath))
            {
                _backingPreview.texture = null;
                _backingPreview.color = new Color(1f, 1f, 1f, 0f);
                return;
            }
            var spr = NametagIconApplicator.GetBackingPreviewSprite(_backingPath, _backingOffX, _backingOffY, _backingScale);
            _backingPreview.texture = spr != null ? spr.texture : null;
            _backingPreview.color = spr != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        }

        private void OnToggleBacking()
        {
            _backingEnabled = !_backingEnabled;
            if (_backingEnabledLabel != null)
                _backingEnabledLabel.text = _backingEnabled ? "Custom backing: ON" : "Custom backing: OFF";
        }

        private void OnBrowseBacking()
        {
            WinDialogs.PickPng("Select nameplate backing PNG", path =>
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                _backingPath = path;
                if (_backingPathLabel != null) _backingPathLabel.text = Path.GetFileName(_backingPath);
                RefreshBackingPreview();
            });
        }

        private void SaveBackingTransform()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            SettingsService.Set(KEY_BACKING_SCALE, _backingScale.ToString(ci));
            SettingsService.Set(KEY_BACKING_OFFSET_X, _backingOffX.ToString(ci));
            SettingsService.Set(KEY_BACKING_OFFSET_Y, _backingOffY.ToString(ci));
        }

        private void QueueBackingApply()
        {
            _backingApplyPending = true;
            if (_backingApplyRoutine == null)
                _backingApplyRoutine = StartCoroutine(ApplyBackingLoop().WrapToIl2Cpp());
        }

        private IEnumerator ApplyBackingLoop()
        {
            while (_backingApplyPending)
            {
                _backingApplyPending = false;
                if (_backingEnabled) NametagFinder.ReapplyAllNameplates();
                yield return new WaitForSeconds(0.15f);
            }
            _backingApplyRoutine = null;
        }

        // ── Crown rank panel ──────────────────────────────────────────────────

        private void BuildCrownPanel(RectTransform parent, float x, float y, float w, float h)
        {
            var (_, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));
            float cw = w - 26f;           // leave room for the scrollbar
            float cy = PAD;
            float bh = BTN_H * 0.85f;

            bool en = CrownRankService.Enabled;
            var enBtn = UGUIShip.CreateButton(content, new Rect(x, cy, cw, bh),
                en ? "Crown rank: ON" : "Crown rank: OFF",
                en ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(OnToggleCrownEnabled));
            _crownEnabledLabel = enBtn.GetComponentInChildren<Text>();
            cy += bh + PAD;

            UGUIShip.CreatePanel(content, new Rect(x, cy, cw, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // crown rank text (local): label on the left, input on the right. commits on enter / focus-out.
            float rankLblW = cw * 0.42f;
            UGUIShip.CreateLabel(content, new Rect(x, cy, rankLblW, bh), "RANK TEXT", FS_SM, HINT, TextAnchor.MiddleLeft);
            float rankFieldX = x + rankLblW + PAD;
            _crownRankField = UGUIShip.CreateInputField(content, new Rect(rankFieldX, cy, cw - rankLblW - PAD, bh),
                "rank text", new Color(0.12f, 0.12f, 0.12f, 1f), WHITE, FS_SM);
            UGUIShip.SetInputText(_crownRankField, CrownRankService.RankText, false);
            _crownRankField.onEndEdit.AddListener(new Action<string>(OnCrownRankEdited));
            cy += bh + PAD;

            bool swap = CrownRankService.SwapSide;
            var swapBtn = UGUIShip.CreateButton(content, new Rect(x, cy, cw, bh),
                swap ? "Crown side: LEFT" : "Crown side: RIGHT",
                swap ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(OnToggleCrownSwap));
            _crownSwapLabel = swapBtn.GetComponentInChildren<Text>();
            cy += bh + PAD;

            UGUIShip.CreatePanel(content, new Rect(x, cy, cw, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            var mc = CrownRankService.MainColour;
            var hc = CrownRankService.HighlightColour;
            var oc = CrownRankService.OutlineColour;
            _crMainR = mc.r; _crMainG = mc.g; _crMainB = mc.b;
            _crHiR = hc.r; _crHiG = hc.g; _crHiB = hc.b;
            _crOutR = oc.r; _crOutG = oc.g; _crOutB = oc.b;

            bool recol = CrownRankService.RecolourOn;
            var recolBtn = UGUIShip.CreateButton(content, new Rect(x, cy, cw, bh),
                recol ? "Recolour: ON" : "Recolour: OFF",
                recol ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(OnToggleCrownRecolour));
            _crownRecolourOnLabel = recolBtn.GetComponentInChildren<Text>();
            cy += bh + PAD;

            // preview swatch on the right, colour sliders on the left
            float prevW = cw * 0.28f;
            float slidersW = cw - prevW - PAD;
            float prevX = x + slidersW + PAD;
            float prevStartY = cy;

            var prevGo = new GameObject("CrownPreview");
            prevGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(prevGo.AddComponent<RectTransform>(),
                new Rect(prevX, cy, prevW, (LH + SH) * 6f + LH));
            prevGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.3f);
            var prevTexGo = new GameObject("Raw");
            prevTexGo.transform.SetParent(prevGo.transform, false);
            var prtRt = prevTexGo.AddComponent<RectTransform>();
            prtRt.anchorMin = Vector2.zero; prtRt.anchorMax = Vector2.one;
            prtRt.offsetMin = prtRt.offsetMax = Vector2.zero;
            _crownPreview = prevTexGo.AddComponent<RawImage>();
            _crownPreview.raycastTarget = false;

            UGUIShip.CreateLabel(content, new Rect(x, cy, slidersW, LH), "CROWN COLOUR", FS_SM, HINT);
            cy += LH + SH;
            UGUIShip.CreateColorControls(content, x, ref cy, slidersW,
                () => _crMainR, () => _crMainG, () => _crMainB,
                v => _crMainR = v, v => _crMainG = v, v => _crMainB = v, () => RefreshCrownPreview(),
                out _, out _, out _);

            UGUIShip.CreateLabel(content, new Rect(x, cy, slidersW, LH), "HIGHLIGHT COLOUR", FS_SM, HINT);
            cy += LH + SH;
            UGUIShip.CreateColorControls(content, x, ref cy, slidersW,
                () => _crHiR, () => _crHiG, () => _crHiB,
                v => _crHiR = v, v => _crHiG = v, v => _crHiB = v, () => RefreshCrownPreview(),
                out _, out _, out _);

            RefreshCrownPreview();

            cy = Mathf.Max(cy, prevStartY + (LH + SH) * 6f + LH) + PAD;
            UGUIShip.CreatePanel(content, new Rect(x, cy, cw, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            UGUIShip.CreateLabel(content, new Rect(x, cy, cw, LH), "OUTLINE COLOUR", FS_SM, HINT);
            cy += LH + SH;
            UGUIShip.CreateColorControls(content, x, ref cy, cw,
                () => _crOutR, () => _crOutG, () => _crOutB,
                v => _crOutR = v, v => _crOutG = v, v => _crOutB = v, () => { },
                out _, out _, out _);

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        // preview: top half = main colour, bottom half = highlight, so both sliders read at a glance.
        private void RefreshCrownPreview()
        {
            if (_crownPreview == null) return;
            const int W = 4, H = 32;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var main = new Color(_crMainR, _crMainG, _crMainB);
            var hi = new Color(_crHiR, _crHiG, _crHiB);
            for (int row = 0; row < H; row++)
            {
                var c = row < H / 2 ? hi : main;
                for (int col = 0; col < W; col++) tex.SetPixel(col, row, c);
            }
            tex.Apply();
            _crownPreview.texture = tex;
            _crownPreview.color = Color.white;
        }

        private void OnToggleCrownEnabled()
        {
            bool on = !CrownRankService.Enabled;
            CrownRankService.SetEnabled(on);
            if (_crownEnabledLabel != null) _crownEnabledLabel.text = on ? "Crown rank: ON" : "Crown rank: OFF";
            UGUIShip.SetButtonSelected(_crownEnabledLabel?.transform.parent?.GetComponent<Button>(), on, SEL_COLOR);
            CrownRankService.ApplyLocal();
            RefreshPreview();
        }

        // rank text committed from the inline field. empty text turns the override off (badge falls back to the
        // game's own rank); non-empty turns it on and forces the badge visible. keeps the feature enabled either
        // way so the colour/side options below still apply.
        private void OnCrownRankEdited(string val)
        {
            CrownRankService.RankText = val ?? "";
            CrownRankService.SetTextOn(!string.IsNullOrEmpty(val));
            CrownRankService.SetEnabled(true);
            CrownRankService.ApplyLocal();
            RefreshPreview();
        }

        private void OnToggleCrownRecolour()
        {
            bool on = !CrownRankService.RecolourOn;
            CrownRankService.SetRecolourOn(on);
            if (_crownRecolourOnLabel != null) _crownRecolourOnLabel.text = on ? "Recolour: ON" : "Recolour: OFF";
            UGUIShip.SetButtonSelected(_crownRecolourOnLabel?.transform.parent?.GetComponent<Button>(), on, SEL_COLOR);
            CrownRankService.ApplyLocal();
            RefreshPreview();
        }

        private void OnToggleCrownSwap()
        {
            bool on = !CrownRankService.SwapSide;
            CrownRankService.SetSwapSide(on);
            if (_crownSwapLabel != null) _crownSwapLabel.text = on ? "Crown side: LEFT" : "Crown side: RIGHT";
            UGUIShip.SetButtonSelected(_crownSwapLabel?.transform.parent?.GetComponent<Button>(), on, SEL_COLOR);
            // when there's an icon, the whole name+icon+crown group has to re-lay-out to the new side, so re-run
            // the icon apply once. no-icon case is handled by ApplyLocal's crown centre + swap postfix.
            if (SettingsService.Get("nametag.icon.mode", "none") != "none")
                NametagIconApplicator.ApplyIcon();
            CrownRankService.ApplyLocal();
            RefreshPreview();
        }

        // ── Icon mode logic ───────────────────────────────────────────────────

        private void SetIconMode(IconMode mode)
        {
            _iconMode = mode;
            UGUIShip.SetButtonSelected(_btnNone, mode == IconMode.None, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnFlag, mode == IconMode.Flag, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnCustom, mode == IconMode.Custom, SEL_COLOR);
            RefreshIconModeVisibility();
            RefreshPreview();
        }

        private void RefreshIconModeVisibility()
        {
            // toggle the SECTION wrappers so the hidden one collapses out of the VLG (no empty gap).
            if (_flagSection != null) _flagSection.SetActive(_iconMode == IconMode.Flag);
            if (_customSection != null) _customSection.SetActive(_iconMode == IconMode.Custom);
        }

        private void SetPlatformHide(PlatformHideMode mode)
        {
            _platformHide = mode;
            UGUIShip.SetButtonSelected(_btnPlatNone, mode == PlatformHideMode.None, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnPlatSelf, mode == PlatformHideMode.Self, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnPlatEveryone, mode == PlatformHideMode.Everyone, SEL_COLOR);

            string modeStr = mode == PlatformHideMode.Self ? "self"
                           : mode == PlatformHideMode.Everyone ? "everyone"
                           : "none";
            SettingsService.Set(KEY_PLATFORM_HIDE, modeStr);
            NametagIconApplicator.ApplyPlatformIcon();
            NametagIconApplicator.ApplyKnownPlatformIcons();
            RefreshPreview();
        }

        private void SetPlatformCustom(string id)
        {
            // "" is the explicit "None" tile: don't force a user icon, hand the local nametag back to the
            // auto MorePlatformIcon pass. real ids toggle off to "" too if you re-click the selected one.
            _platformCustom = string.Equals(_platformCustom, id, StringComparison.OrdinalIgnoreCase) ? "" : (id ?? "");
            SettingsService.Set(KEY_PLATFORM_CUSTOM, _platformCustom);
            RefreshPlatformIconButtons();

            if (string.IsNullOrEmpty(_platformCustom))
                NametagIconApplicator.RestoreKnownPlatformIcons();
            else
                NametagIconApplicator.ApplyPlatformIcon();
            NametagIconApplicator.ApplyKnownPlatformIcons();
            NametagFinder.ReapplyAllNameplates();
            RefreshPreview();
        }

        private void RefreshPlatformIconButtons()
        {
            if (_platformIconContent == null) return;
            for (int i = 0; i < _platformIconContent.childCount; i++)
            {
                var child = _platformIconContent.GetChild(i);
                if (child == null) continue;
                var img = child.Find("Icon")?.GetComponent<Image>();
                if (img == null) continue;
                string id = child.name.StartsWith("PlatformIcon_")
                    ? child.name.Substring("PlatformIcon_".Length)
                    : "";
                img.color = string.Equals(_platformCustom, id, StringComparison.OrdinalIgnoreCase) ? WHITE : ICON_OFF;
            }
        }

        private void OnSelectCountry(string code)
        {
            _selectedCountry = code;
            if (_scrollContent == null) return;

            Sprite btnSpr = UGUIShip.GetButtonSprite();
            for (int i = 0; i < _scrollContent.childCount; i++)
            {
                var child = _scrollContent.GetChild(i);
                if (child == null) continue;
                bool sel = child.name == "ISO_" + code;
                var img = child.GetComponent<Image>();
                if (img != null)
                {
                    if (sel && btnSpr != null)
                    {
                        img.sprite = btnSpr;
                        img.type = Image.Type.Simple;
                        img.color = SEL_COLOR;
                    }
                    else
                    {
                        img.sprite = null;
                        img.color = Color.clear;
                    }
                }
                var lbl = child.GetComponentInChildren<Text>();
                if (lbl != null) lbl.color = sel ? WHITE : new Color(1f, 1f, 1f, 0.7f);
            }
            RefreshPreview();
        }

        private void SaveIconTransform()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            SettingsService.Set(KEY_ICON_SCALE, _iconScale.ToString(ci));
            SettingsService.Set(KEY_ICON_OFFSET_X, _iconOffsetX.ToString(ci));
            SettingsService.Set(KEY_ICON_OFFSET_Y, _iconOffsetY.ToString(ci));
        }

        private void QueueIconTransformApply()
        {
            _iconTransformApplyPending = true;
            if (_iconTransformApplyRoutine == null)
                _iconTransformApplyRoutine = StartCoroutine(ApplyIconTransformLoop().WrapToIl2Cpp());
        }

        private IEnumerator ApplyIconTransformLoop()
        {
            while (_iconTransformApplyPending)
            {
                _iconTransformApplyPending = false;
                NametagIconApplicator.ApplyNametag();
                yield return new WaitForSeconds(0.1f);
            }
            _iconTransformApplyRoutine = null;
        }

        private void OnBrowseCustomIcon()
        {
            WinDialogs.PickImage("Select icon (PNG or GIF)", path =>
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
                _customIconPath = path;
                if (_customIconLabel != null) _customIconLabel.text = Path.GetFileName(_customIconPath);
                RefreshPreview();
            });
        }

        // ── Slider builder ────────────────────────────────────────────────────

        private Slider BuildSlider(Transform parent, float x, float y, float w, string lbl, float init, Action<float> onChange, Color? labelColor = null, Color? fillColor = null)
            => UGUIShip.CreateSlider(parent, x, y, w, lbl, init, LH, PAD, FS_SM, onChange, labelColor, fillColor);

        // ── Logic ─────────────────────────────────────────────────────────────

        private const string SAMPLE_NAME = "Example";

        // real name for the preview: custom mod name, else the stripped FG username, else the placeholder.
        private static string PreviewName()
        {
            string n = LocalPlayerInfo.DisplayName;
            return string.IsNullOrEmpty(n) ? SAMPLE_NAME : n;
        }

        // kept for the CacheNameAssets hook + colour-control callbacks — just re-runs the full preview now.
        private void RefreshSampleText() => RefreshPreview();

        // drive the cloned PlayerInfoDisplay to show the mod's current nametag by running the REAL apply
        // pipeline against it (name/style/icon via ApplyNametag, crown via CrownRankService.ApplyLocal) —
        // exactly what runs on the in-game nametag, no re-implementation. does nothing until a live display
        // exists to clone (menus before any nametag spawns).
        private void RefreshPreview()
        {
            if (_previewPanelRt == null) return;
            if (!EnsurePreviewClone()) return;

            var tmp = _previewCanvas != null ? _previewCanvas._text : null;
            if (tmp == null) return;

            // build configs straight from the live UI fields — no settings mutation, no global overrides. the
            // preview always renders enabled so styling shows before the user hits Apply.
            var nameCfg = new NametagIconApplicator.NametagCfg
            {
                enabled = true,
                r = _r, g = _g, b = _b, bold = _bold, italic = _italic,
                style = _nameStyle == NameStyle.None ? "none"
                    : _nameStyle == NameStyle.Gold ? "gold"
                    : _nameStyle == NameStyle.GoldColored ? "goldcolored" : "default",
                iconMode = _iconMode == IconMode.Flag ? "flag" : _iconMode == IconMode.Custom ? "custom" : "none",
                iconCountry = _selectedCountry ?? "",
                iconPath = _customIconPath ?? "",
            };
            var crownCfg = new CrownRankService.CrownCfg
            {
                enabled = CrownRankService.Enabled,
                textOn = CrownRankService.TextOn, recolourOn = CrownRankService.RecolourOn,
                swapSide = CrownRankService.SwapSide, text = CrownRankService.RankText,
                main = new Color(_crMainR, _crMainG, _crMainB),
                highlight = new Color(_crHiR, _crHiG, _crHiB),
                outline = new Color(_crOutR, _crOutG, _crOutB),
            };
            CrownRankService.InvalidateCache();

            // seed the name + light up the crown badge (only renders after the game's own crown setter runs).
            // feed it a rank of 1 when enabled just so the badge draws — our text override then stamps over it.
            tmp.text = PreviewName();
            _previewCanvas.SetCrownRankByCrownsEarned(crownCfg.enabled ? 1 : 0);

            NametagIconApplicator.ApplyNametagTo(_previewCanvas, nameCfg);
            CrownRankService.ApplyCrownTo(_previewCanvas, crownCfg);

            // platform icon: reuse the same apply the in-game path uses, driven with the live fields.
            bool platHide = _platformHide == PlatformHideMode.Self || _platformHide == PlatformHideMode.Everyone;
            NametagIconApplicator.ApplyPlatformIcon(_previewClone, platHide, _platformCustom ?? "");
        }

        private void OnToggleBold()
        {
            _bold = !_bold;
            if (_boldBtnLabel != null) _boldBtnLabel.text = _bold ? "Bold: ON" : "Bold: OFF";
            RefreshSampleText();
        }

        private void OnToggleItalic()
        {
            _italic = !_italic;
            if (_italicBtnLabel != null) _italicBtnLabel.text = _italic ? "Italic: ON" : "Italic: OFF";
            RefreshSampleText();
        }

        private void OnApply()
        {
            SaveSettings();

            // crown colours live on the service; commit the slider values and repaint the live badge.
            CrownRankService.MainColour = new Color(_crMainR, _crMainG, _crMainB);
            CrownRankService.HighlightColour = new Color(_crHiR, _crHiG, _crHiB);
            CrownRankService.OutlineColour = new Color(_crOutR, _crOutG, _crOutB);
            CrownRankService.InvalidateCache();
            CrownRankService.ApplyLocal();

            NametagIconApplicator.ApplyNametag();
            NametagIconApplicator.ApplyPlatformIcon();
            NametagFinder.ReapplyAllNameplates();
            // refresh the private lobby player list too (name + style on your row)
            BetterFG.Customization.Menu.MenuCustomizationApplication.Instance?.ReapplySpecialForeground(
                BetterFG.Customization.Menu.MenuCustomizationApplication.SpecialScreen.PrivateLobbyPlayerList);
        }

        private void OnRemove()
        {
            switch (_sub)
            {
                case SubTab.Colour: RemoveColour(); break;
                case SubTab.Icon: RemoveIcon(); break;
                case SubTab.Nameplate: RemoveNameplate(); break;
                case SubTab.CrownRank: RemoveCrown(); break;
            }
        }

        private void RemoveColour()
        {
            _r = _g = _b = 1f;
            _bold = _italic = false;
            _nameStyle = NameStyle.Default;

            SettingsService.Set(KEY_COLOR_R, "1");
            SettingsService.Set(KEY_COLOR_G, "1");
            SettingsService.Set(KEY_COLOR_B, "1");
            SettingsService.Set(KEY_BOLD, "false");
            SettingsService.Set(KEY_ITALIC, "false");
            SettingsService.Set(KEY_NAME_STYLE, "default");

            if (_sliderR != null) _sliderR.value = 1f;
            if (_sliderG != null) _sliderG.value = 1f;
            if (_sliderB != null) _sliderB.value = 1f;
            if (_boldBtnLabel != null) _boldBtnLabel.text = "Bold: OFF";
            if (_italicBtnLabel != null) _italicBtnLabel.text = "Italic: OFF";
            RefreshStyleButtons();
            RefreshSampleText();

            NametagIconApplicator.ApplyNametag();
            NametagFinder.ReapplyAllNameplates();
        }

        private void RemoveIcon()
        {
            _iconMode = IconMode.None;
            SettingsService.Set(KEY_ICON_MODE, "none");
            SetIconMode(IconMode.None);

            NametagIconApplicator.ApplyNametag();
            NametagFinder.ReapplyAllNameplates();
        }

        private void RemoveNameplate()
        {
            _backingEnabled = false;
            SettingsService.Set(KEY_BACKING_ENABLED, "false");
            if (_backingEnabledLabel != null) _backingEnabledLabel.text = "Custom backing: OFF";

            NametagFinder.ReapplyAllNameplates();
        }

        private void RemoveCrown()
        {
            CrownRankService.SetEnabled(false);
            CrownRankService.SetTextOn(false);
            CrownRankService.SetRecolourOn(false);
            if (_crownRankField != null) UGUIShip.SetInputText(_crownRankField, "", false);
            if (_crownEnabledLabel != null) _crownEnabledLabel.text = "Crown rank: OFF";
            if (_crownRecolourOnLabel != null) _crownRecolourOnLabel.text = "Recolour: OFF";
            UGUIShip.SetButtonSelected(_crownEnabledLabel?.transform.parent?.GetComponent<Button>(), false, SEL_COLOR);
            UGUIShip.SetButtonSelected(_crownRecolourOnLabel?.transform.parent?.GetComponent<Button>(), false, SEL_COLOR);
            CrownRankService.ApplyLocal();
        }

        // ── Settings ──────────────────────────────────────────────────────────

        private void LoadSettings()
        {
            _r = float.TryParse(SettingsService.Get(KEY_COLOR_R, "1"), out float r) ? r : 1f;
            _g = float.TryParse(SettingsService.Get(KEY_COLOR_G, "1"), out float g) ? g : 1f;
            _b = float.TryParse(SettingsService.Get(KEY_COLOR_B, "1"), out float b) ? b : 1f;
            _bold = SettingsService.Get(KEY_BOLD, "false") == "true";
            _italic = SettingsService.Get(KEY_ITALIC, "false") == "true";
            _customName = SettingsService.Get(KEY_CUSTOM_NAME, "");

            string modeStr = SettingsService.Get(KEY_ICON_MODE, "none");
            _iconMode = modeStr == "flag" ? IconMode.Flag
                      : modeStr == "custom" ? IconMode.Custom
                      : IconMode.None;

            _selectedCountry = SettingsService.Get(KEY_ICON_COUNTRY, "");
            _customIconPath = SettingsService.Get(KEY_ICON_PATH, "");

            var ci2 = System.Globalization.CultureInfo.InvariantCulture;
            _iconScale = float.TryParse(SettingsService.Get(KEY_ICON_SCALE, "1"), System.Globalization.NumberStyles.Float, ci2, out float sv) ? sv : 1f;
            _iconOffsetX = float.TryParse(SettingsService.Get(KEY_ICON_OFFSET_X, "0"), System.Globalization.NumberStyles.Float, ci2, out float ox) ? ox : 0f;
            _iconOffsetY = float.TryParse(SettingsService.Get(KEY_ICON_OFFSET_Y, "0"), System.Globalization.NumberStyles.Float, ci2, out float oy) ? oy : 0f;

            string platStr = SettingsService.Get(KEY_PLATFORM_HIDE, "none");
            _platformHide = platStr == "self" ? PlatformHideMode.Self
                          : platStr == "everyone" ? PlatformHideMode.Everyone
                          : PlatformHideMode.None;

            _platformCustom = SettingsService.Get(KEY_PLATFORM_CUSTOM, "");

            string nameStyleStr = SettingsService.Get(KEY_NAME_STYLE, "default");
            _nameStyle = nameStyleStr == "none" ? NameStyle.None
                       : nameStyleStr == "gold" ? NameStyle.Gold
                       : nameStyleStr == "goldcolored" ? NameStyle.GoldColored
                       : NameStyle.Default;

            _backingEnabled = SettingsService.Get(KEY_BACKING_ENABLED, "false") == "true";
            _backingPath = SettingsService.Get(KEY_BACKING_PATH, "");
            _backingScale = float.TryParse(SettingsService.Get(KEY_BACKING_SCALE, "1"), System.Globalization.NumberStyles.Float, ci2, out float bsv) ? bsv : 1f;
            _backingOffX = float.TryParse(SettingsService.Get(KEY_BACKING_OFFSET_X, "0"), System.Globalization.NumberStyles.Float, ci2, out float box) ? box : 0f;
            _backingOffY = float.TryParse(SettingsService.Get(KEY_BACKING_OFFSET_Y, "0"), System.Globalization.NumberStyles.Float, ci2, out float boy) ? boy : 0f;

            _nicknameEnabled = SettingsService.Get(KEY_NICKNAME_ENABLED, "false") == "true";
            _nicknameText = SettingsService.Get(KEY_NICKNAME_TEXT, "");
        }

        private void SaveSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            SettingsService.Set(KEY_COLOR_R, _r.ToString(ci));
            SettingsService.Set(KEY_COLOR_G, _g.ToString(ci));
            SettingsService.Set(KEY_COLOR_B, _b.ToString(ci));
            SettingsService.Set(KEY_BOLD, _bold ? "true" : "false");
            SettingsService.Set(KEY_ITALIC, _italic ? "true" : "false");
            // KEY_CUSTOM_NAME is owned by PlayerdetailsWindow — don't overwrite it with a potentially stale field value.
            // Re-read the live value so a SaveSettings call from the Nametag tab never clobbers it.
            SettingsService.Set(KEY_CUSTOM_NAME, SettingsService.Get(KEY_CUSTOM_NAME, _customName ?? ""));
            SettingsService.Set(KEY_ENABLED, "true");

            string modeStr = _iconMode == IconMode.Flag ? "flag"
                           : _iconMode == IconMode.Custom ? "custom"
                           : "none";
            SettingsService.Set(KEY_ICON_MODE, modeStr);
            SettingsService.Set(KEY_ICON_COUNTRY, _selectedCountry ?? "");
            SettingsService.Set(KEY_ICON_PATH, _customIconPath ?? "");
            SaveIconTransform();

            string platStr = _platformHide == PlatformHideMode.Self ? "self"
                           : _platformHide == PlatformHideMode.Everyone ? "everyone"
                           : "none";
            SettingsService.Set(KEY_PLATFORM_HIDE, platStr);
            SettingsService.Set(KEY_PLATFORM_CUSTOM, _platformCustom ?? "");

            string styleStr = _nameStyle == NameStyle.None ? "none"
                            : _nameStyle == NameStyle.Gold ? "gold"
                            : _nameStyle == NameStyle.GoldColored ? "goldcolored"
                            : "default";
            SettingsService.Set(KEY_NAME_STYLE, styleStr);

            SettingsService.Set(KEY_BACKING_ENABLED, _backingEnabled ? "true" : "false");
            SettingsService.Set(KEY_BACKING_PATH, _backingPath ?? "");
            SaveBackingTransform();

            SettingsService.Set(KEY_NICKNAME_ENABLED, _nicknameEnabled ? "true" : "false");
            SettingsService.Set(KEY_NICKNAME_TEXT, _nicknameText ?? "");
        }

        // ── Name style helpers ────────────────────────────────────────────────

        private void SetNameStyle(NameStyle style)
        {
            _nameStyle = style;
            RefreshStyleButtons();
            RefreshSampleText();
        }

        private void RefreshStyleButtons()
        {
            UGUIShip.SetButtonSelected(_btnStyleNone, _nameStyle == NameStyle.None, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnStyleDefault, _nameStyle == NameStyle.Default, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnStyleGold, _nameStyle == NameStyle.Gold, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnStyleGoldColored, _nameStyle == NameStyle.GoldColored, SEL_COLOR);
        }

        // ── UGUI helpers ──────────────────────────────────────────────────────

    }
}
