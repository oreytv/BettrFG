using System;
using System.Reflection;
using BetterFG.Core;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI
{
    /// <summary>
    /// The "Welcome to BetterFG" info window shown after the disclaimer is accepted,
    /// underneath the revealer wipe. Previously BetterFGStartupWindow.
    /// Sorting order 1000 — below the startup sequence canvas (1001).
    /// </summary>
    public class BetterFGInfoWindow : MonoBehaviour
    {
        public BetterFGInfoWindow(IntPtr ptr) : base(ptr) { }

        private const float W = 560f;
        private const float H = 290f;
        private const int FS_TITLE = 16;
        private const int FS_BODY = 13;
        private const int FS_BTN = 12;

        private static readonly Color WHITE = Color.white;
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.45f);
        private static readonly Color BTN_CLOSE = new Color(0.55f, 0.15f, 0.15f, 1f);
        private static readonly Color TRANSP = Color.clear;

        private static Texture2D _bgTex;
        private static Texture2D _bgHoverTex;
        private static Texture2D _oreyTex;

        private Canvas _canvas;
        private GameObject _root;
        private GameObject _bgHoverGo;
        private bool _dragging;
        private Vector2 _dragOffset;
        private RectTransform _rootRt;
        private RectTransform _titleHoverRt;
        private bool _titleHovering;

        // ── Entry point ───────────────────────────────────────────────────────
        public static BetterFGInfoWindow Show()
        {
            var go = new GameObject("BetterFG_InfoWindow");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go.AddComponent<BetterFGInfoWindow>();
        }

        void Awake() => Build();

        void Update()
        {
            HandleDrag();
            HandleTitleHover();
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void Build()
        {
            var canvasGo = new GameObject("InfoWindowCanvas");
            canvasGo.hideFlags = HideFlags.HideAndDontSave;
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1000;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            BetterFG.Services.UIScaleService.Register(_canvas);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            _root = new GameObject("Window");
            _root.transform.SetParent(canvasGo.transform, false);
            _rootRt = _root.AddComponent<RectTransform>();
            _rootRt.anchorMin = new Vector2(0.5f, 0.5f);
            _rootRt.anchorMax = new Vector2(0.5f, 0.5f);
            _rootRt.pivot = new Vector2(0.5f, 0.5f);
            _rootRt.sizeDelta = new Vector2(W, H);
            _rootRt.anchoredPosition = Vector2.zero;

            _root.AddComponent<Image>().color = TRANSP;

            BuildBackground();
            BuildContent();
        }

        private void BuildBackground()
        {
            var bgTex = LoadEmbedded("BetterFG.assets.ui.startupwindow.bg.png", ref _bgTex);
            if (bgTex == null) return;

            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(_root.transform, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            bgRt.localPosition = new Vector3(6.4727f, 75.7635f, 0);
            bgRt.localScale = new Vector3(1.2943f, 2.8985f, 1);
            bgGo.AddComponent<RawImage>().texture = bgTex;

            var hoverTex = LoadEmbedded("BetterFG.assets.ui.startupwindow.bg_hover.png", ref _bgHoverTex);
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

        private void BuildContent()
        {
            float pad = 12f;
            float titleH = 32f;
            float pfpW = 96f;
            float pfpH = 96f;

            var titleGo = new GameObject("TitleStrip");
            titleGo.transform.SetParent(_root.transform, false);
            _titleHoverRt = titleGo.AddComponent<RectTransform>();
            _titleHoverRt.anchorMin = new Vector2(0f, 1f);
            _titleHoverRt.anchorMax = new Vector2(1f, 1f);
            _titleHoverRt.pivot = new Vector2(0.5f, 1f);
            _titleHoverRt.offsetMin = new Vector2(0f, -titleH);
            _titleHoverRt.offsetMax = Vector2.zero;
            titleGo.AddComponent<Image>().color = TRANSP;

            AddLabel(_titleHoverRt, $"BetterFG {BetterFGInfo.Version}", FS_TITLE, WHITE,
                new Vector2(pad, 0f), Vector2.zero, TextAnchor.MiddleLeft, FontStyle.Bold);

            UGUIShip.CreateButton(_root.transform,
                new Rect(W - 62f, 5f, 52f, 22f),
                "Close", BTN_CLOSE, WHITE, FS_BTN, new Action(Dismiss));

            float leftW = W - pfpW - pad * 3f;
            var leftGo = new GameObject("Left");
            leftGo.transform.SetParent(_root.transform, false);
            var leftRt = leftGo.AddComponent<RectTransform>();
            leftRt.anchorMin = new Vector2(0f, 0f);
            leftRt.anchorMax = new Vector2(0f, 1f);
            leftRt.pivot = new Vector2(0f, 1f);
            leftRt.offsetMin = new Vector2(pad, pad);
            leftRt.offsetMax = new Vector2(pad + leftW, -titleH);

            var vlg = leftGo.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 4f;

            if (!BetterFGInfo.IsPublicRelease)
                AddFlowLabel(leftGo.transform, "Not for public release", FS_BODY, new Color(1f, 0.35f, 0.35f, 0.9f));

            AddFlowLabel(leftGo.transform, "Welcome to BetterFG!", FS_BODY, WHITE);
            AddFlowLabel(leftGo.transform, "BetterFG is a Fall Guys mod made with the ambition", FS_BODY - 2, WHITE);
            AddFlowLabel(leftGo.transform, "of enhancing customization by letting people create", FS_BODY - 2, WHITE);
            AddFlowLabel(leftGo.transform, "and use custom skins.", FS_BODY - 2, WHITE);
            AddFlowLabel(leftGo.transform, "", FS_BODY - 3, TRANSP);
            AddFlowLabel(leftGo.transform, "Start exploring ways to customize yourself with the", FS_BODY - 2, HINT);
            AddFlowLabel(leftGo.transform, "Tabs at the bottom of the screen.", FS_BODY - 2, HINT);

            float rightX = W - pfpW - pad;
            var rightGo = new GameObject("Right");
            rightGo.transform.SetParent(_root.transform, false);
            var rightRt = rightGo.AddComponent<RectTransform>();
            rightRt.anchorMin = new Vector2(0f, 0f);
            rightRt.anchorMax = new Vector2(0f, 0f);
            rightRt.pivot = new Vector2(0f, 0f);
            rightRt.anchoredPosition = new Vector2(rightX, pad);
            rightRt.sizeDelta = new Vector2(pfpW, pfpH + 16f);
            rightRt.localScale = new Vector3(1.1746f, 1f, 1f);

            var rvlg = rightGo.AddComponent<VerticalLayoutGroup>();
            rvlg.childForceExpandWidth = true;
            rvlg.childForceExpandHeight = false;
            rvlg.spacing = 3f;

            var pfpTex = LoadEmbedded("BetterFG.assets.ui.startupwindow.orey.png", ref _oreyTex);
            var pfpGo = new GameObject("Pfp");
            pfpGo.transform.SetParent(rightGo.transform, false);
            pfpGo.AddComponent<RectTransform>();
            pfpGo.AddComponent<LayoutElement>().preferredHeight = pfpH;
            pfpGo.transform.localScale = Vector3.one * 2;
            var pfpImg = pfpGo.AddComponent<RawImage>();
            pfpImg.texture = pfpTex;
            pfpImg.color = pfpTex != null ? WHITE : TRANSP;
        }

        // ── Drag ──────────────────────────────────────────────────────────────

        private void HandleDrag()
        {
            if (_rootRt == null || _titleHoverRt == null) return;
            var mouse = new Vector2(Input.mousePosition.x, Input.mousePosition.y);

            if (Input.GetMouseButtonDown(0) && IsTitleHit(mouse))
            {
                _dragging = true;
                _dragOffset = _rootRt.anchoredPosition - ScreenToCanvas(mouse);
            }
            if (Input.GetMouseButtonUp(0)) _dragging = false;
            if (_dragging) _rootRt.anchoredPosition = ScreenToCanvas(mouse) + _dragOffset;
        }

        private void HandleTitleHover()
        {
            if (_titleHoverRt == null) return;
            bool over = IsTitleHit(new Vector2(Input.mousePosition.x, Input.mousePosition.y));
            if (over == _titleHovering) return;
            _titleHovering = over;
            if (_bgHoverGo != null) _bgHoverGo.SetActive(_titleHovering);
        }

        private bool IsTitleHit(Vector2 mouse)
            => _titleHoverRt != null &&
               RectTransformUtility.RectangleContainsScreenPoint(_titleHoverRt, mouse, null);

        private Vector2 ScreenToCanvas(Vector2 mouse)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvas.GetComponent<RectTransform>(), mouse, null, out var local);
            return local;
        }

        private void Dismiss()
        {
            if (gameObject != null) Destroy(gameObject);
        }

        // ── Texture loader ────────────────────────────────────────────────────

        private static Texture2D LoadEmbedded(string name, ref Texture2D cache)
        {
            if (cache != null) return cache;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var s = asm.GetManifestResourceStream(name);
                if (s == null) return null;
                var b = new byte[s.Length];
                s.Read(b, 0, b.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                ImageConversion.LoadImage(tex, b);
                tex.wrapMode = TextureWrapMode.Clamp;
                cache = tex;
            }
            catch (Exception ex) { Plugin.Log.LogError($"InfoWindow: {name}: {ex.Message}"); }
            return cache;
        }

        // ── UGUI helpers ──────────────────────────────────────────────────────

        private static void AddLabel(RectTransform parent, string text, int fs, Color color,
            Vector2 offsetMin, Vector2 offsetMax, TextAnchor anchor, FontStyle style)
        {
            var t = UGUIShip.CreateLabel(parent, default, text, fs, color, anchor);
            t.fontStyle = style;
            var rt = t.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
        }

        private static void AddFlowLabel(Transform parent, string text, int fs, Color color)
        {
            var t = UGUIShip.CreateFlowLabel(parent, text, fs, color);
            t.GetComponent<LayoutElement>().preferredHeight = fs + 4f;
        }
    }
}
