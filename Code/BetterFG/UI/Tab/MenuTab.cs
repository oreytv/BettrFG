using System;
using System.Reflection;
using BetterFG.Customization.Menu;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    public class MenuTab : BetterFGTab
    {
        public MenuTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Main Menu";

        private static float PAD => UIScale.PAD;
        private static float VPAD => UIScale.VPAD;
        private static float LH => UIScale.LH;
        private static float SH => UIScale.SH;
        private static float BTN_H => UIScale.BTN_H;
        private static int FS => UIScale.FS;
        private static int FS_SM => UIScale.FS_SM;

        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color WHITE = Color.white;
        private static readonly Color BTN_APPLY = new Color(0.45f, 0.35f, 0.25f, 1f);
        private static readonly Color BTN_REMOVE = new Color(0.55f, 0.15f, 0.15f, 1f);
        private static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color SEL_COLOR = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color BTN_ON = new Color(0.25f, 0.5f, 0.25f, 1f);

        private static float subTabH => BTN_H * 0.9f;

        // ── Sub-tab ───────────────────────────────────────────────────────────
        // falling-screen (lobby bg) colours moved to the UI tab's Background section, so this tab
        // is just Background + Camera now.
        private enum SubTab { Background, Camera }
        private SubTab _sub = SubTab.Background;
        private Button _btnSubBg, _btnSubCam;
        private GameObject _bgPanel, _camPanel;

        // ── Textures ──────────────────────────────────────────────────────────
        private static Texture2D _bgTex;
        private static Texture2D _hoverTex;
        private GameObject _bgHoverGo;

        // ── State: background ─────────────────────────────────────────────────
        private float _topR, _topG, _topB;
        private float _botR = 1f, _botG = 1f, _botB = 1f;
        private float _bias, _smooth = 1f;

        // ── State: background image ───────────────────────────────────────────
        private bool _bgImgOn;
        private float _bgImgPosX, _bgImgPosY, _bgImgPosZ;
        private float _bgImgScale = MenuCustomizationApplication.BG_IMG_SCALE_DEFAULT,
                      _bgImgScaleX = MenuCustomizationApplication.BG_IMG_SCALE_AXIS_DEFAULT,
                      _bgImgScaleY = MenuCustomizationApplication.BG_IMG_SCALE_AXIS_DEFAULT;
        private string _bgImgPath = "";
        private Button _bgImgToggleBtn;
        private RawImage _bgImgPreview;
        private Text _bgImgLabel;

        // ── State: ambient light + sun ────────────────────────────────────────
        private bool _ambientOn;
        private float _ambientR = 0.5f, _ambientG = 0.5f, _ambientB = 0.5f;
        private Button _ambientToggleBtn;
        private Image _ambientSwatch;
        private bool _sunOn;
        private float _sunRotX = 50f, _sunRotY, _sunRotZ;
        private Button _sunToggleBtn;

        // ── State: circles pattern ────────────────────────────────────────────
        // apply/restore + the original-texture cache live in MenuCustomizationApplication now so the
        // boot-time auto-apply and this UI share one path. we only keep the settings key + label here.
        private const string KEY_PATTERN_PATH = MenuCustomizationApplication.KEY_PATTERN_PATH;
        private Text _patternLabel;

        // ── State: camera ─────────────────────────────────────────────────────
        private bool _camOn;
        private Button _camToggleBtn;
        private float _fov = 40f;
        private float _camX, _camY, _camZ;
        private float _lookAtX, _lookAtY, _lookAtZ;

        // ── State: plinth colour ──────────────────────────────────────────────
        private bool _plinthColOn;
        private float _plinthColR = 1f, _plinthColG = 1f, _plinthColB = 1f;
        private Button _plinthColToggleBtn;
        private Image _plinthColSwatch;

        // ── UGUI refs ─────────────────────────────────────────────────────────
        private RawImage _gradPreview;
        private Button _bgToggleBtn;

        // ── Settings keys (bg shared with MenuCustomizationApplication) ───────
        private static string KEY_TOP_R => MenuCustomizationApplication.KEY_BG_TOP_R;
        private static string KEY_TOP_G => MenuCustomizationApplication.KEY_BG_TOP_G;
        private static string KEY_TOP_B => MenuCustomizationApplication.KEY_BG_TOP_B;
        private static string KEY_BOT_R => MenuCustomizationApplication.KEY_BG_BOT_R;
        private static string KEY_BOT_G => MenuCustomizationApplication.KEY_BG_BOT_G;
        private static string KEY_BOT_B => MenuCustomizationApplication.KEY_BG_BOT_B;
        private static string KEY_BIAS => MenuCustomizationApplication.KEY_BG_BIAS;
        private static string KEY_SMOOTH => MenuCustomizationApplication.KEY_BG_SMOOTH;

        private const string KEY_CAM_ENABLED = MenuCustomizationApplication.KEY_CAM_ENABLED;
        private const string KEY_CAM_FOV = MenuCustomizationApplication.KEY_CAM_FOV;
        private const string KEY_CAM_X = MenuCustomizationApplication.KEY_CAM_X;
        private const string KEY_CAM_Y = MenuCustomizationApplication.KEY_CAM_Y;
        private const string KEY_CAM_Z = MenuCustomizationApplication.KEY_CAM_Z;
        private const string KEY_CAM_LOOKAT_X = MenuCustomizationApplication.KEY_CAM_LOOKAT_X;
        private const string KEY_CAM_LOOKAT_Y = MenuCustomizationApplication.KEY_CAM_LOOKAT_Y;
        private const string KEY_CAM_LOOKAT_Z = MenuCustomizationApplication.KEY_CAM_LOOKAT_Z;

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

        // ── Build ─────────────────────────────────────────────────────────────

        protected override void BuildContent(RectTransform contentRoot)
        {
            LoadSettings();

            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            // subtab bar
            float halfw = (w - PAD * 0.5f) / 2f;
            _btnSubBg = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD, y, halfw, subTabH), "Background",
                _sub == SubTab.Background ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() => SetSubTab(SubTab.Background)));
            _btnSubCam = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD + halfw + PAD * 0.5f, y, halfw, subTabH), "Foreground",
                _sub == SubTab.Camera ? SEL_COLOR : BTN_DARK, WHITE, FS_SM,
                new Action(() => SetSubTab(SubTab.Camera)));
            y += subTabH + SH;

            UGUIShip.CreatePanel(contentRoot, new Rect(PAD, y, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            y += 1f + SH;

            float btnRowH = BTN_H + PAD * 2f + 1f;
            float bodyH = TabHeight - y - VPAD - btnRowH;

            // panels are scroll views; the viewport is inset by SCROLLBAR_INSET on both sides
            // (bar lives in the right one). content is laid out at x=PAD, so trim the width down
            // to the viewport's right edge or full-width controls get clipped under the bar.
            float panelW = w - (UGUIShip.SCROLLBAR_INSET * 2f - PAD);

            // background panel
            _bgPanel = new GameObject("BgPanel");
            _bgPanel.transform.SetParent(contentRoot, false);
            var bgPanelRt = _bgPanel.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(bgPanelRt, new Rect(0f, y, TabWidth, bodyH));
            BuildBgPanel(bgPanelRt, PAD, 0f, panelW, bodyH);

            // camera panel
            _camPanel = new GameObject("CamPanel");
            _camPanel.transform.SetParent(contentRoot, false);
            var camPanelRt = _camPanel.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(camPanelRt, new Rect(0f, y, TabWidth, bodyH));
            BuildCamPanel(camPanelRt, PAD, 0f, panelW, bodyH);

            // apply / remove always visible
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
            UGUIShip.SetButtonSelected(_btnSubBg, sub == SubTab.Background, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnSubCam, sub == SubTab.Camera, SEL_COLOR);
            RefreshSubTabVisibility();
        }

        private void RefreshSubTabVisibility()
        {
            if (_bgPanel != null) _bgPanel.SetActive(_sub == SubTab.Background);
            if (_camPanel != null) _camPanel.SetActive(_sub == SubTab.Camera);
        }

        // ── Background panel ──────────────────────────────────────────────────

        private void BuildBgPanel(RectTransform parent, float x, float y, float w, float h)
        {
            var (scrollRect, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));

            float cy = PAD;

            float noticeH = BTN_H * 1.4f;
            float beanW = noticeH * 0.6f;
            var beanTex = BetterFG.Utilities.EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.bean.bean_victorious.png");
            if (beanTex != null) UGUIShip.CreateImage(content, new Rect(x, cy, beanW, noticeH), beanTex, "NoticeBean");
            UGUIShip.CreateLinkText(content, new Rect(x + beanW + PAD, cy, w - beanW - PAD, noticeH),
                "Background gradient and pattern moved to the UI tab, under Background. Take me there",
                new Action(() => BetterFGUIMan.Instance?.OpenUIScreen()), fontSize: FS_SM);
            cy += noticeH + PAD;

            // ── Background image ──────────────────────────────────────────────

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "BACKGROUND IMAGE", FS_SM, HINT);
            cy += LH + SH;

            // preview on the right, controls on the left
            float imgPreviewW = w * 0.28f;
            float imgCtrlW = w - imgPreviewW - PAD * 2f;
            float imgPreviewStartY = cy;

            var imgPreviewGo = new GameObject("BgImgPreview");
            imgPreviewGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(imgPreviewGo.AddComponent<RectTransform>(),
                new Rect(x + imgCtrlW + PAD * 2f, cy, imgPreviewW, imgPreviewW));
            imgPreviewGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.3f);

            var imgPreviewTexGo = new GameObject("BgImgPreviewTex");
            imgPreviewTexGo.transform.SetParent(imgPreviewGo.transform, false);
            var imgPreviewTexRt = imgPreviewTexGo.AddComponent<RectTransform>();
            imgPreviewTexRt.anchorMin = Vector2.zero;
            imgPreviewTexRt.anchorMax = Vector2.one;
            imgPreviewTexRt.offsetMin = imgPreviewTexRt.offsetMax = Vector2.zero;
            _bgImgPreview = imgPreviewTexGo.AddComponent<RawImage>();
            _bgImgPreview.raycastTarget = false;
            RefreshBgImgPreview();

            // toggle
            _bgImgToggleBtn = UGUIShip.CreateButton(content, new Rect(x, cy, imgCtrlW, BTN_H),
                _bgImgOn ? "Image: ON" : "Image: OFF",
                _bgImgOn ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _bgImgOn = !_bgImgOn;
                    SettingsService.Set(MenuCustomizationApplication.KEY_BG_IMG_ENABLED, _bgImgOn ? "true" : "false");
                    MenuCustomizationApplication.Instance?.SetImageBgEnabled(_bgImgOn);
                    var lbl = _bgImgToggleBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _bgImgOn ? "Image: ON" : "Image: OFF";
                    var img = _bgImgToggleBtn?.GetComponent<Image>();
                    if (img != null) img.color = _bgImgOn ? BTN_ON : BTN_DARK;
                }));
            cy += BTN_H + SH;

            // browse + reset
            float imgBrowseW = BTN_H * 2.5f;
            float imgResetW = BTN_H * 2f;
            float imgLblW = imgCtrlW - imgBrowseW - imgResetW - PAD * 2f;
            _bgImgLabel = UGUIShip.CreateLabel(content, new Rect(x, cy, imgLblW, BTN_H),
                string.IsNullOrEmpty(_bgImgPath) ? "none" : System.IO.Path.GetFileName(_bgImgPath),
                FS_SM, HINT, TextAnchor.MiddleLeft);

            UGUIShip.CreateButton(content, new Rect(x + imgLblW + PAD, cy, imgBrowseW, BTN_H),
                "Browse", BTN_DARK, WHITE, FS_SM,
                new Action(() => WinDialogs.PickPng("Select background image", path =>
                {
                    if (string.IsNullOrEmpty(path)) return;
                    _bgImgPath = path;
                    SettingsService.Set(MenuCustomizationApplication.KEY_BG_IMG_PATH, path);
                    MenuCustomizationApplication.Instance?.ApplyImageBgTexture(path);
                    if (_bgImgLabel != null) _bgImgLabel.text = System.IO.Path.GetFileName(path);
                    RefreshBgImgPreview();
                })));

            UGUIShip.CreateButton(content, new Rect(x + imgLblW + PAD + imgBrowseW + PAD, cy, imgResetW, BTN_H),
                "Reset", BTN_REMOVE, WHITE, FS_SM,
                new Action(() =>
                {
                    _bgImgPath = "";
                    SettingsService.Remove(MenuCustomizationApplication.KEY_BG_IMG_PATH);
                    MenuCustomizationApplication.Instance?.ApplyImageBgTexture("");
                    if (_bgImgLabel != null) _bgImgLabel.text = "none";
                    RefreshBgImgPreview();
                }));
            cy += BTN_H + PAD;

            // position
            UGUIShip.CreateLabel(content, new Rect(x, cy, imgCtrlW, LH), "POSITION", FS_SM, HINT);
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, imgCtrlW, "X", _bgImgPosX,
                MenuCustomizationApplication.BG_IMG_POS_MIN, MenuCustomizationApplication.BG_IMG_POS_MAX,
                v => { _bgImgPosX = v; ApplyBgImgTransform(); });
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, imgCtrlW, "Y", _bgImgPosY,
                MenuCustomizationApplication.BG_IMG_POS_MIN, MenuCustomizationApplication.BG_IMG_POS_MAX,
                v => { _bgImgPosY = v; ApplyBgImgTransform(); });
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, imgCtrlW, "Z", _bgImgPosZ,
                MenuCustomizationApplication.BG_IMG_POS_MIN, MenuCustomizationApplication.BG_IMG_POS_MAX,
                v => { _bgImgPosZ = v; ApplyBgImgTransform(); });
            cy += LH + PAD;

            // scale
            UGUIShip.CreateLabel(content, new Rect(x, cy, imgCtrlW, LH), "SCALE", FS_SM, HINT);
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, imgCtrlW, "Uniform", _bgImgScale,
                MenuCustomizationApplication.BG_IMG_SCALE_MIN, MenuCustomizationApplication.BG_IMG_SCALE_MAX,
                v => { _bgImgScale = v; ApplyBgImgTransform(); });
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, imgCtrlW, "X", _bgImgScaleX,
                MenuCustomizationApplication.BG_IMG_SCALE_AXIS_MIN, MenuCustomizationApplication.BG_IMG_SCALE_AXIS_MAX,
                v => { _bgImgScaleX = v; ApplyBgImgTransform(); });
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, imgCtrlW, "Y", _bgImgScaleY,
                MenuCustomizationApplication.BG_IMG_SCALE_AXIS_MIN, MenuCustomizationApplication.BG_IMG_SCALE_AXIS_MAX,
                v => { _bgImgScaleY = v; ApplyBgImgTransform(); });
            cy += LH + PAD;

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // ── Ambient light ─────────────────────────────────────────────────

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "AMBIENT LIGHT", FS_SM, HINT);
            cy += LH + SH;

            float ambSwatchW = BTN_H;
            float ambSliderW = w - ambSwatchW - PAD;

            _ambientToggleBtn = UGUIShip.CreateButton(content, new Rect(x, cy, ambSliderW, BTN_H),
                _ambientOn ? "Ambient: ON" : "Ambient: OFF",
                _ambientOn ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _ambientOn = !_ambientOn;
                    MenuCustomizationApplication.Instance?.SetAmbientEnabled(_ambientOn);
                    var lbl = _ambientToggleBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _ambientOn ? "Ambient: ON" : "Ambient: OFF";
                    var img = _ambientToggleBtn?.GetComponent<Image>();
                    if (img != null) img.color = _ambientOn ? BTN_ON : BTN_DARK;
                }));

            var ambSwatchGo = new GameObject("AmbientSwatch");
            ambSwatchGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(ambSwatchGo.AddComponent<RectTransform>(), new Rect(x + ambSliderW + PAD, cy, ambSwatchW, BTN_H));
            _ambientSwatch = ambSwatchGo.AddComponent<Image>();
            _ambientSwatch.color = new Color(_ambientR, _ambientG, _ambientB);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, w,
                () => _ambientR, () => _ambientG, () => _ambientB,
                v => _ambientR = v, v => _ambientG = v, v => _ambientB = v, () => ApplyAmbient(), out _, out _, out _);

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // ── Main sun rotation ─────────────────────────────────────────────

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "MAIN SUN ROTATION", FS_SM, HINT);
            cy += LH + SH;

            _sunToggleBtn = UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H),
                _sunOn ? "Sun override: ON" : "Sun override: OFF",
                _sunOn ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _sunOn = !_sunOn;
                    MenuCustomizationApplication.Instance?.SetSunEnabled(_sunOn);
                    var lbl = _sunToggleBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _sunOn ? "Sun override: ON" : "Sun override: OFF";
                    var img = _sunToggleBtn?.GetComponent<Image>();
                    if (img != null) img.color = _sunOn ? BTN_ON : BTN_DARK;
                }));
            cy += BTN_H + SH;

            BuildSliderRaw(content, x, cy, w, "X", _sunRotX, 0f, 360f,
                v => { _sunRotX = v; ApplySun(); });
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "Y", _sunRotY, 0f, 360f,
                v => { _sunRotY = v; ApplySun(); });
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "Z", _sunRotZ, 0f, 360f,
                v => { _sunRotZ = v; ApplySun(); });
            cy += LH + PAD;

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void ApplyAmbient()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            SettingsService.Set(MenuCustomizationApplication.KEY_AMBIENT_R, _ambientR.ToString(ci));
            SettingsService.Set(MenuCustomizationApplication.KEY_AMBIENT_G, _ambientG.ToString(ci));
            SettingsService.Set(MenuCustomizationApplication.KEY_AMBIENT_B, _ambientB.ToString(ci));
            if (_ambientSwatch != null) _ambientSwatch.color = new Color(_ambientR, _ambientG, _ambientB);
            if (_ambientOn) MenuCustomizationApplication.Instance?.ApplyAmbient(new Color(_ambientR, _ambientG, _ambientB));
        }

        private void ApplySun()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            SettingsService.Set(MenuCustomizationApplication.KEY_SUN_ROT_X, _sunRotX.ToString(ci));
            SettingsService.Set(MenuCustomizationApplication.KEY_SUN_ROT_Y, _sunRotY.ToString(ci));
            SettingsService.Set(MenuCustomizationApplication.KEY_SUN_ROT_Z, _sunRotZ.ToString(ci));
            if (_sunOn) MenuCustomizationApplication.Instance?.ApplySunRotation(_sunRotX, _sunRotY, _sunRotZ);
        }

        private void ApplyBgImgTransform()
        {
            SettingsService.Set(MenuCustomizationApplication.KEY_BG_IMG_POS_X, _bgImgPosX.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SettingsService.Set(MenuCustomizationApplication.KEY_BG_IMG_POS_Y, _bgImgPosY.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SettingsService.Set(MenuCustomizationApplication.KEY_BG_IMG_POS_Z, _bgImgPosZ.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SettingsService.Set(MenuCustomizationApplication.KEY_BG_IMG_SCALE, _bgImgScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SettingsService.Set(MenuCustomizationApplication.KEY_BG_IMG_SCALE_X, _bgImgScaleX.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SettingsService.Set(MenuCustomizationApplication.KEY_BG_IMG_SCALE_Y, _bgImgScaleY.ToString(System.Globalization.CultureInfo.InvariantCulture));
            MenuCustomizationApplication.Instance?.ApplyImageBgTransform(_bgImgPosX, _bgImgPosY, _bgImgPosZ, _bgImgScale, _bgImgScaleX, _bgImgScaleY);
        }

        private void RefreshBgImgPreview()
        {
            if (_bgImgPreview == null) return;
            if (string.IsNullOrEmpty(_bgImgPath) || !System.IO.File.Exists(_bgImgPath))
            {
                _bgImgPreview.texture = null;
                _bgImgPreview.color = new Color(1f, 1f, 1f, 0f);
                return;
            }
            try
            {
                var bytes = System.IO.File.ReadAllBytes(_bgImgPath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.wrapMode = TextureWrapMode.Clamp;
                _bgImgPreview.texture = tex;
                _bgImgPreview.color = Color.white;
            }
            catch (Exception ex) { Plugin.Log.LogError("MenuTab: bg img preview failed: " + ex.Message); }
        }

        // ── Camera panel ──────────────────────────────────────────────────────

        private void BuildCamPanel(RectTransform parent, float x, float y, float w, float h)
        {
            var (scrollRect, content) = UGUIShip.CreateScrollView(parent, new Rect(0f, y, TabWidth, h));

            float cy = PAD;

            _camToggleBtn = UGUIShip.CreateButton(content, new Rect(x, cy, w, BTN_H),
                _camOn ? "Custom camera: ON" : "Custom camera: OFF",
                _camOn ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _camOn = !_camOn;
                    SettingsService.Set(KEY_CAM_ENABLED, _camOn ? "true" : "false");
                    if (_camOn) ApplyCam();
                    else MenuCustomizationApplication.Instance?.ResetCam();
                    var lbl = _camToggleBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _camOn ? "Custom camera: ON" : "Custom camera: OFF";
                    var img = _camToggleBtn?.GetComponent<Image>();
                    if (img != null) img.color = _camOn ? BTN_ON : BTN_DARK;
                }));
            cy += BTN_H + PAD;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "FOV", FS_SM, HINT);
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "FOV", _fov, 20f, 120f,
                v => _fov = v);
            cy += LH + PAD;

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "POSITION", FS_SM, HINT);
            cy += LH + SH;

            BuildSliderRaw(content, x, cy, w, "X", _camX, -5f, 5f, v => _camX = v);
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "Y", _camY, -5f, 5f, v => _camY = v);
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "Z", _camZ, -5f, 5f, v => _camZ = v);

            cy += LH + PAD;
            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "LOOK AT OFFSET", FS_SM, HINT);
            cy += LH + SH;

            BuildSliderRaw(content, x, cy, w, "X", _lookAtX, -5f, 5f, v => _lookAtX = v);
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "Y", _lookAtY, -5f, 5f, v => _lookAtY = v);
            cy += LH + SH;
            BuildSliderRaw(content, x, cy, w, "Z", _lookAtZ, -5f, 5f, v => _lookAtZ = v);
            cy += LH + PAD;

            UGUIShip.CreatePanel(content, new Rect(x, cy, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            cy += 1f + PAD;

            // ── Plinth colour ─────────────────────────────────────────────────
            UGUIShip.CreateLabel(content, new Rect(x, cy, w, LH), "PLINTH COLOUR", FS_SM, HINT);
            cy += LH + SH;

            float plinthSwatchW = BTN_H;
            float plinthToggleW = w - plinthSwatchW - PAD;

            _plinthColToggleBtn = UGUIShip.CreateButton(content, new Rect(x, cy, plinthToggleW, BTN_H),
                _plinthColOn ? "Plinth colour: ON" : "Plinth colour: OFF",
                _plinthColOn ? BTN_ON : BTN_DARK, WHITE, FS_SM,
                new Action(() =>
                {
                    _plinthColOn = !_plinthColOn;
                    MenuCustomizationApplication.Instance?.SetPlinthColorEnabled(_plinthColOn);
                    var lbl = _plinthColToggleBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _plinthColOn ? "Plinth colour: ON" : "Plinth colour: OFF";
                    var img = _plinthColToggleBtn?.GetComponent<Image>();
                    if (img != null) img.color = _plinthColOn ? BTN_ON : BTN_DARK;
                }));

            var plinthSwatchGo = new GameObject("PlinthColSwatch");
            plinthSwatchGo.transform.SetParent(content, false);
            UGUIShip.SetPixelRect(plinthSwatchGo.AddComponent<RectTransform>(), new Rect(x + plinthToggleW + PAD, cy, plinthSwatchW, BTN_H));
            _plinthColSwatch = plinthSwatchGo.AddComponent<Image>();
            _plinthColSwatch.color = new Color(_plinthColR, _plinthColG, _plinthColB);
            cy += BTN_H + SH;

            UGUIShip.CreateColorControls(content, x, ref cy, w,
                () => _plinthColR, () => _plinthColG, () => _plinthColB,
                v => _plinthColR = v, v => _plinthColG = v, v => _plinthColB = v, () => ApplyPlinthCol(), out _, out _, out _);

            content.sizeDelta = new Vector2(0f, cy + PAD);
        }

        private void ApplyPlinthCol()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            SettingsService.Set(MenuCustomizationApplication.KEY_PLINTH_COL_R, _plinthColR.ToString(ci));
            SettingsService.Set(MenuCustomizationApplication.KEY_PLINTH_COL_G, _plinthColG.ToString(ci));
            SettingsService.Set(MenuCustomizationApplication.KEY_PLINTH_COL_B, _plinthColB.ToString(ci));
            if (_plinthColSwatch != null) _plinthColSwatch.color = new Color(_plinthColR, _plinthColG, _plinthColB);
            if (_plinthColOn) MenuCustomizationApplication.Instance?.ApplyPlinthColor(new Color(_plinthColR, _plinthColG, _plinthColB));
        }

        // ── Gradient preview ──────────────────────────────────────────────────

        private void RefreshGradPreview()
        {
            if (_gradPreview == null) return;

            const int W = 4, H = 64;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var top = new Color(_topR, _topG, _topB);
            var bot = new Color(_botR, _botG, _botB);

            for (int row = 0; row < H; row++)
            {
                float t = row / (float)(H - 1);
                // match shader: bias offset then pow smoothness
                float s = Mathf.Clamp01(t + _bias * 0.5f);
                s = Mathf.Pow(s, Mathf.Max(0.1f, _smooth));
                var c = Color.Lerp(bot, top, s);
                for (int col = 0; col < W; col++)
                    tex.SetPixel(col, row, c);
            }

            tex.Apply();
            _gradPreview.texture = tex;
        }

        // ── Apply / Remove ────────────────────────────────────────────────────

        private void OnApply()
        {
            SaveSettings();
            if (_sub == SubTab.Background)
            {
                var app = MenuCustomizationApplication.Instance;
                if (app != null)
                    app.ApplyGradient(
                        new Color(_topR, _topG, _topB),
                        new Color(_botR, _botG, _botB),
                        _bias, _smooth);

                ApplyPatternFromSettings();
            }
            else
            {
                ApplyCam();
                ApplyPlinthCol();
            }
        }

        private void OnRemove()
        {
            if (_sub == SubTab.Background)
            {
                RemoveBgKeys();
                var app = MenuCustomizationApplication.Instance;
                if (app != null)
                    app.ApplyGradient(Color.black, Color.white, 0f, 1f);
                _topR = _topG = _topB = 0f;
                _botR = _botG = _botB = 1f;
                _bias = 0f; _smooth = 1f;
                RefreshGradPreview();

                RestorePattern();
                SettingsService.Remove(KEY_PATTERN_PATH);
                if (_patternLabel != null) _patternLabel.text = "none";
            }
            else
            {
                SettingsService.Set(KEY_CAM_ENABLED, "false");
                SettingsService.Remove(KEY_CAM_FOV);
                SettingsService.Remove(KEY_CAM_X);
                SettingsService.Remove(KEY_CAM_Y);
                SettingsService.Remove(KEY_CAM_Z);
                SettingsService.Remove(KEY_CAM_LOOKAT_X);
                SettingsService.Remove(KEY_CAM_LOOKAT_Y);
                SettingsService.Remove(KEY_CAM_LOOKAT_Z);
                _camOn = false;
                _fov = 40f; _camX = _camY = _camZ = 0f;
                _lookAtX = _lookAtY = _lookAtZ = 0f;
                MenuCustomizationApplication.Instance?.ResetCam();
                if (_camToggleBtn != null)
                {
                    var camLbl = _camToggleBtn.GetComponentInChildren<Text>();
                    if (camLbl != null) camLbl.text = "Custom camera: OFF";
                    var camImg = _camToggleBtn.GetComponent<Image>();
                    if (camImg != null) camImg.color = BTN_DARK;
                }

                SettingsService.Set(MenuCustomizationApplication.KEY_PLINTH_COL_ON, "false");
                SettingsService.Remove(MenuCustomizationApplication.KEY_PLINTH_COL_R);
                SettingsService.Remove(MenuCustomizationApplication.KEY_PLINTH_COL_G);
                SettingsService.Remove(MenuCustomizationApplication.KEY_PLINTH_COL_B);
                _plinthColOn = false;
                _plinthColR = _plinthColG = _plinthColB = 1f;
                MenuCustomizationApplication.Instance?.RevertPlinthColor();
                if (_plinthColSwatch != null) _plinthColSwatch.color = new Color(_plinthColR, _plinthColG, _plinthColB);
                if (_plinthColToggleBtn != null)
                {
                    var lbl = _plinthColToggleBtn.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = "Plinth colour: OFF";
                    var img = _plinthColToggleBtn.GetComponent<Image>();
                    if (img != null) img.color = BTN_DARK;
                }
            }
        }

        private void RemoveBgKeys()
        {
            SettingsService.Remove(KEY_TOP_R); SettingsService.Remove(KEY_TOP_G); SettingsService.Remove(KEY_TOP_B);
            SettingsService.Remove(KEY_BOT_R); SettingsService.Remove(KEY_BOT_G); SettingsService.Remove(KEY_BOT_B);
            SettingsService.Remove(KEY_BIAS); SettingsService.Remove(KEY_SMOOTH);
        }

        // both delegate to MenuCustomizationApplication so the boot-time auto-apply and the UI share
        // one cache of the original texture — otherwise Remove can't restore a pattern the app applied.
        private void ApplyPatternFromSettings()
            => MenuCustomizationApplication.Instance?.ApplyPatternFromSettings();

        private void RestorePattern()
            => MenuCustomizationApplication.Instance?.RestorePattern();

        private void ApplyCam()
        {
            if (!_camOn) { MenuCustomizationApplication.Instance?.ResetCam(); return; }
            MenuCustomizationApplication.Instance?.ApplyCam(
                new Vector3(_camX, _camY, _camZ), _fov,
                new Vector3(_lookAtX, _lookAtY, _lookAtZ));
        }

        // ── Settings ──────────────────────────────────────────────────────────

        private void LoadSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float def) =>
                float.TryParse(SettingsService.Get(key, def.ToString(ci)),
                    System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            _topR = P(KEY_TOP_R, 0f); _topG = P(KEY_TOP_G, 0f); _topB = P(KEY_TOP_B, 0f);
            _botR = P(KEY_BOT_R, 1f); _botG = P(KEY_BOT_G, 1f); _botB = P(KEY_BOT_B, 1f);
            _bias = P(KEY_BIAS, 0f);
            _smooth = P(KEY_SMOOTH, 1f);

            _bgImgOn = SettingsService.Get(MenuCustomizationApplication.KEY_BG_IMG_ENABLED, "false") == "true";
            _bgImgPath = SettingsService.Get(MenuCustomizationApplication.KEY_BG_IMG_PATH, "");
            _bgImgPosX = P(MenuCustomizationApplication.KEY_BG_IMG_POS_X, MenuCustomizationApplication.BG_IMG_POS_DEFAULT);
            _bgImgPosY = P(MenuCustomizationApplication.KEY_BG_IMG_POS_Y, MenuCustomizationApplication.BG_IMG_POS_DEFAULT);
            _bgImgPosZ = P(MenuCustomizationApplication.KEY_BG_IMG_POS_Z, MenuCustomizationApplication.BG_IMG_POS_DEFAULT);
            _bgImgScale = P(MenuCustomizationApplication.KEY_BG_IMG_SCALE, MenuCustomizationApplication.BG_IMG_SCALE_DEFAULT);
            _bgImgScaleX = P(MenuCustomizationApplication.KEY_BG_IMG_SCALE_X, MenuCustomizationApplication.BG_IMG_SCALE_AXIS_DEFAULT);
            _bgImgScaleY = P(MenuCustomizationApplication.KEY_BG_IMG_SCALE_Y, MenuCustomizationApplication.BG_IMG_SCALE_AXIS_DEFAULT);

            _ambientOn = SettingsService.Get(MenuCustomizationApplication.KEY_AMBIENT_ON, "false") == "true";
            _ambientR = P(MenuCustomizationApplication.KEY_AMBIENT_R, 0.5f);
            _ambientG = P(MenuCustomizationApplication.KEY_AMBIENT_G, 0.5f);
            _ambientB = P(MenuCustomizationApplication.KEY_AMBIENT_B, 0.5f);
            _sunOn = SettingsService.Get(MenuCustomizationApplication.KEY_SUN_ON, "false") == "true";
            _sunRotX = P(MenuCustomizationApplication.KEY_SUN_ROT_X, 50f);
            _sunRotY = P(MenuCustomizationApplication.KEY_SUN_ROT_Y, 0f);
            _sunRotZ = P(MenuCustomizationApplication.KEY_SUN_ROT_Z, 0f);

            _camOn = SettingsService.Get(KEY_CAM_ENABLED, "false") == "true";
            _fov = P(KEY_CAM_FOV, 40f);
            _camX = P(KEY_CAM_X, 0f);
            _camY = P(KEY_CAM_Y, 0f);
            _camZ = P(KEY_CAM_Z, 0f);
            _lookAtX = P(KEY_CAM_LOOKAT_X, 0f);
            _lookAtY = P(KEY_CAM_LOOKAT_Y, 0f);
            _lookAtZ = P(KEY_CAM_LOOKAT_Z, 0f);

            _plinthColOn = SettingsService.Get(MenuCustomizationApplication.KEY_PLINTH_COL_ON, "false") == "true";
            _plinthColR = P(MenuCustomizationApplication.KEY_PLINTH_COL_R, 1f);
            _plinthColG = P(MenuCustomizationApplication.KEY_PLINTH_COL_G, 1f);
            _plinthColB = P(MenuCustomizationApplication.KEY_PLINTH_COL_B, 1f);
        }

        private void SaveSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            void S(string k, float v) => SettingsService.Set(k, v.ToString(ci));

            S(KEY_TOP_R, _topR); S(KEY_TOP_G, _topG); S(KEY_TOP_B, _topB);
            S(KEY_BOT_R, _botR); S(KEY_BOT_G, _botG); S(KEY_BOT_B, _botB);
            S(KEY_BIAS, _bias);
            S(KEY_SMOOTH, _smooth);

            SettingsService.Set(KEY_CAM_ENABLED, _camOn ? "true" : "false");
            S(KEY_CAM_FOV, _fov);
            S(KEY_CAM_X, _camX); S(KEY_CAM_Y, _camY); S(KEY_CAM_Z, _camZ);
            S(KEY_CAM_LOOKAT_X, _lookAtX); S(KEY_CAM_LOOKAT_Y, _lookAtY); S(KEY_CAM_LOOKAT_Z, _lookAtZ);
        }

        // ── Slider helpers ────────────────────────────────────────────────────

        // 0..1 slider (for RGB/A)
        private Slider BuildSlider(Transform parent, float x, float y, float w,
            string lbl, float init, Action<float> onChange,
            Color? labelColor = null, Color? fillColor = null)
            => UGUIShip.CreateSlider(parent, x, y, w, lbl, init, LH, PAD, FS_SM, onChange, labelColor, fillColor);

        // arbitrary range slider
        private Slider BuildSliderRaw(Transform parent, float x, float y, float w,
            string lbl, float init, float min, float max, Action<float> onChange)
        {
            var s = UGUIShip.CreateSlider(parent, x, y, w, lbl, Mathf.InverseLerp(min, max, init),
                LH, PAD, FS_SM, t => onChange(Mathf.Lerp(min, max, t)));
            return s;
        }

        // overload for 0..1 sliders without range (matches nametag pattern)
        private Slider BuildSliderRaw(Transform parent, float x, float y, float w,
            string lbl, float init, Action<float> onChange)
            => BuildSlider(parent, x, y, w, lbl, init, onChange);

    }
}
