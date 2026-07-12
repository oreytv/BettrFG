using System;
using System.Collections;
using System.Reflection;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Windows
{
    public class BetterFGWindow : MonoBehaviour
    {
        public BetterFGWindow(IntPtr ptr) : base(ptr) { }

        protected const float S = 1.2f;
        protected const float PAD = 8f * S;
        protected const float TITLE_H = 24f * S;
        protected const int FS_TITLE = (int)(11f * S);
        protected const int FS_BODY = (int)(11f * S);
        protected const int FS_SM = (int)(10f * S);

        protected static readonly Color WHITE = Color.white;
        protected static readonly Color HINT = new Color(1f, 1f, 1f, 0.4f);
        protected static readonly Color PANEL_BG = new Color(0f, 0f, 0f, 0.4f);
        protected static readonly Color BTN_DARK = new Color(0.18f, 0.18f, 0.18f, 1f);
        protected static readonly Color SEL_COL = new Color(0.25f, 0.5f, 0.25f, 1f);
        protected static readonly Color TRANSP = Color.clear;

        protected virtual float WindowWidth => 300f;
        protected virtual float WindowHeight => 200f;
        protected virtual string WindowTitle => "Window";
        protected virtual string BgResourceName => "";
        protected virtual string BgHoverResourceName => "BetterFG.assets.ui.windows.generalbg_hover.png";

        protected virtual bool ShowTitleHover => false;
        protected virtual bool DraggableFromTitle => false;

        // Subclasses override to set BG transform before BuildBackground runs
        protected virtual Vector3 InitialBgPosition => new Vector3(192.2909f, 28.3637f, 0f);
        protected virtual Vector3 InitialBgScale => new Vector3(1.2931f, 4.5265f, 1f);

        public Vector3 BgPosition = new Vector3(192.2909f, 28.3637f, 0f);
        public Vector3 BgScale = new Vector3(1.2931f, 4.5265f, 1f);
        public Vector3 TitlePosition = new Vector3(32.5674f, -12f, 0f);
        public Vector3 TitleScale = new Vector3(1.1818f, 1.3491f, 1f);
        public Vector3 ContentPosition = new Vector3(190.6421f, -11.8564f, 0f);
        public Vector3 ContentScale = new Vector3(1.0473f, 1f, 1f);
        // per-window override for the stretched content rect's offsets. null = use the defaults
        // (full fill minus TITLE_H at the top). populate from BuildContent to retune the area.
        public Vector2? ContentOffsetMin = null;
        public Vector2? ContentOffsetMax = null;
        public Vector2 WindowPosition = Vector2.zero;
        public Vector2 Pivot = new Vector2(0f, 0.5f);

        private RectTransform _rootRt;
        private RectTransform _bgRt;
        private RectTransform _titleLabelRt;
        protected RectTransform _contentRt;
        private Transform _titleRoot;
        private RawImage _hoverImage;
        private CanvasGroup _canvasGroup;

        // Set by whoever spawns the window so we can hide when the tab closes
        public BetterFG.UI.BetterFGTab OwnerTab { get; set; }

        // ── API ───────────────────────────────────────────────────────────────

        public void SetRotation(float zDeg)
        {
            if (_rootRt != null) _rootRt.localRotation = Quaternion.Euler(0f, 0f, zDeg);
        }

        public void SetAnchorPosition(Vector2 pos)
        {
            WindowPosition = pos;
            if (_rootRt != null) _rootRt.anchoredPosition = pos;
        }

        public void SetScaleX(float sx)
        {
            if (_rootRt != null) _rootRt.localScale = new Vector3(sx, 1f, 1f);
        }

        public void HideWindow()
        {
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;
        }

        public bool IsVisible => _canvasGroup != null && _canvasGroup.alpha > 0f;

        public void ShowWindow()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = true;
                _canvasGroup.interactable = true;
            }
            SetScaleX(0f);
            StartCoroutine(AnimateOpen().WrapToIl2Cpp());
        }

        private static readonly AnimationCurve _openCurve = new AnimationCurve(new Keyframe[]
        {
            new Keyframe(0f,   0f,    0f,   2.5f),
            new Keyframe(0.6f, 1.05f, 0.3f, 0.3f),
            new Keyframe(1f,   1f,    0f,   0f),
        });

        private IEnumerator AnimateOpen()
        {
            const float DUR = 0.18f;
            float elapsed = 0f;
            while (elapsed < DUR)
            {
                elapsed += Time.deltaTime;
                SetScaleX(_openCurve.Evaluate(Mathf.Clamp01(elapsed / DUR)));
                yield return null;
            }
            SetScaleX(1f);
        }

        protected void RebuildContent()
        {
            if (_contentRt == null) return;
            var parent = _contentRt.parent?.GetComponent<RectTransform>();
            if (parent != null)
            {
                for (int i = parent.childCount - 1; i >= 0; i--)
                {
                    var c = parent.GetChild(i);
                    if (c != null && (c.name == "HandToggle" || c.name == "ResetBtn"))
                        UnityEngine.Object.Destroy(c.gameObject);
                }
            }
            for (int i = _contentRt.childCount - 1; i >= 0; i--)
            {
                var child = _contentRt.GetChild(i);
                if (child != null) UnityEngine.Object.Destroy(child.gameObject);
            }
            BuildContent(_contentRt);
        }

        protected void RebuildTitleExtras()
        {
            if (_titleRoot == null) return;
            for (int i = _titleRoot.childCount - 1; i >= 0; i--)
            {
                var child = _titleRoot.GetChild(i);
                if (child.name == "HandToggle" || child.name == "ResetBtn")
                    UnityEngine.Object.Destroy(child.gameObject);
            }
            BuildTitleExtras(_titleRoot);
        }

        public void Show() => gameObject.SetActive(true);

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Awake()
        {
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            Build();
        }

        void Update() => ManagedUpdate();

        protected virtual void ManagedUpdate()
        {
            ApplyRuntimeLayout();

            if (OwnerTab != null && !OwnerTab.IsOpen)
                HideWindow();
        }

        private void ApplyRuntimeLayout()
        {
            if (_rootRt != null)
                _rootRt.pivot = Pivot;
            if (_bgRt != null) { _bgRt.localPosition = BgPosition; _bgRt.localScale = BgScale; }
            if (_titleLabelRt != null) { _titleLabelRt.localPosition = TitlePosition; _titleLabelRt.localScale = TitleScale; }
            if (_contentRt != null)
            {
                _contentRt.localPosition = ContentPosition;
                _contentRt.localScale = ContentScale;
                if (ContentOffsetMin.HasValue) _contentRt.offsetMin = ContentOffsetMin.Value;
                if (ContentOffsetMax.HasValue) _contentRt.offsetMax = ContentOffsetMax.Value;
            }
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void Build()
        {
            // Seed from virtual props BEFORE BuildBackground runs
            BgPosition = InitialBgPosition;
            BgScale = InitialBgScale;

            var canvasGo = new GameObject(WindowTitle + "_Canvas");
            canvasGo.hideFlags = HideFlags.HideAndDontSave;
            canvasGo.transform.SetParent(transform, false);

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 997;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = BetterFG.Services.UIScaleService.CurrentRef;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            _canvasGroup = canvasGo.AddComponent<CanvasGroup>();

            var rootGo = new GameObject("Window");
            rootGo.transform.SetParent(canvasGo.transform, false);
            _rootRt = rootGo.AddComponent<RectTransform>();
            _rootRt.anchorMin = new Vector2(0f, 0.5f);
            _rootRt.anchorMax = new Vector2(0f, 0.5f);
            _rootRt.pivot = Pivot;
            _rootRt.sizeDelta = new Vector2(WindowWidth, WindowHeight);
            _rootRt.anchoredPosition = WindowPosition;
            _rootRt.localScale = new Vector3(0f, 1f, 1f); // hidden until ShowWindow()

            rootGo.AddComponent<Image>().color = TRANSP;

            BuildBackground(rootGo.transform);
            BuildTitleBar(rootGo.transform);

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(rootGo.transform, false);
            _contentRt = contentGo.AddComponent<RectTransform>();
            _contentRt.anchorMin = Vector2.zero;
            _contentRt.anchorMax = Vector2.one;
            _contentRt.offsetMin = new Vector2(0f, 0f);
            _contentRt.offsetMax = new Vector2(0f, -TITLE_H);
            _contentRt.localPosition = ContentPosition;
            _contentRt.localScale = ContentScale;

            BuildContent(_contentRt);
        }

        private void BuildBackground(Transform parent)
        {
            if (string.IsNullOrEmpty(BgResourceName)) return;
            var tex = LoadEmbedded(BgResourceName);
            if (tex == null) return;
            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(parent, false);
            _bgRt = bgGo.AddComponent<RectTransform>();
            _bgRt.anchorMin = Vector2.zero;
            _bgRt.anchorMax = Vector2.one;
            _bgRt.offsetMin = _bgRt.offsetMax = Vector2.zero;
            _bgRt.localPosition = BgPosition;
            _bgRt.localScale = BgScale;
            bgGo.AddComponent<RawImage>().texture = tex;

            if (DraggableFromTitle)
            {
                var hoverTex = LoadEmbedded(BgHoverResourceName);
                if (hoverTex != null)
                {
                    var hoverGo = new GameObject("BGHover");
                    hoverGo.transform.SetParent(bgGo.transform, false);
                    var hoverRt = hoverGo.AddComponent<RectTransform>();
                    hoverRt.anchorMin = Vector2.zero;
                    hoverRt.anchorMax = Vector2.one;
                    hoverRt.offsetMin = hoverRt.offsetMax = Vector2.zero;
                    hoverRt.localPosition = Vector3.zero;
                    hoverRt.localScale = Vector3.one;
                    _hoverImage = hoverGo.AddComponent<RawImage>();
                    _hoverImage.texture = hoverTex;
                    _hoverImage.color = Color.clear;
                }
            }
        }

        private void BuildTitleBar(Transform parent)
        {
            var titleGo = new GameObject("TitleBar");
            titleGo.transform.SetParent(parent, false);
            _titleRoot = titleGo.transform;
            var titleRt = titleGo.AddComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.offsetMin = new Vector2(0f, -TITLE_H);
            titleRt.offsetMax = Vector2.zero;
            titleGo.AddComponent<Image>().color = TRANSP;

            if (DraggableFromTitle)
            {
                var dragGo = new GameObject("DragArea");
                dragGo.transform.SetParent(titleGo.transform, false);
                var dragRt = dragGo.AddComponent<RectTransform>();
                dragRt.anchorMin = Vector2.zero;
                dragRt.anchorMax = Vector2.one;
                dragRt.offsetMin = Vector2.zero;
                dragRt.offsetMax = Vector2.zero;
                dragGo.AddComponent<Image>().color = TRANSP;
                dragGo.transform.position += new Vector3(0, 14, 0);
                dragGo.transform.localScale = new Vector3(1, 1.5f, 1);
                dragGo.AddComponent<WindowDragHandle>().Init(_rootRt, _hoverImage);
            }

            var lblGo = new GameObject("Title");
            lblGo.transform.SetParent(titleGo.transform, false);
            _titleLabelRt = lblGo.AddComponent<RectTransform>();
            _titleLabelRt.anchorMin = Vector2.zero;
            _titleLabelRt.anchorMax = Vector2.one;
            _titleLabelRt.offsetMin = new Vector2(PAD, 0f);
            _titleLabelRt.offsetMax = Vector2.zero;
            _titleLabelRt.localPosition = TitlePosition;
            _titleLabelRt.localScale = TitleScale;
            var t = lblGo.AddComponent<Text>();
            t.text = WindowTitle.ToUpper();
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = FS_TITLE;
            t.color = new Color(1f, 1f, 1f, 0.85f);
            t.alignment = TextAnchor.MiddleLeft;
            t.fontStyle = FontStyle.Bold;

            BuildTitleExtras(titleGo.transform);
        }

        protected virtual void BuildContent(RectTransform contentRoot) { }
        protected virtual void BuildTitleExtras(Transform titleRoot) { }

        // ── Helpers ───────────────────────────────────────────────────────────

        protected static Texture2D LoadEmbedded(string resourceName)
        {
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
                return tex;
            }
            catch (Exception ex) { Debug.LogError($"[Window] {resourceName}: {ex.Message}"); return null; }
        }

        protected static Text MakeLabel(Transform parent, Rect rect, string text, int fs, Color color,
            TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            UGUIShip.SetPixelRect(go.AddComponent<RectTransform>(), rect);
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = fs;
            t.color = color;
            t.alignment = anchor;
            return t;
        }

        protected static void MakeSeparator(Transform parent, Rect rect)
        {
            var go = new GameObject("Sep");
            go.transform.SetParent(parent, false);
            UGUIShip.SetPixelRect(go.AddComponent<RectTransform>(), rect);
            go.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);
        }
    }

    // ── Drag handle component ─────────────────────────────────────────────────

    public class WindowDragHandle : MonoBehaviour
    {
        public WindowDragHandle(IntPtr ptr) : base(ptr) { }

        private RectTransform _target;
        private RectTransform _selfRt;
        private RectTransform _canvasRt;
        private RawImage _hoverImage;
        private bool _dragging;
        private Vector2 _offset;
        private static WindowDragHandle _activeDrag;

        private static readonly Color HOVER_ON = new Color(1f, 1f, 1f, 0.08f);

        public void Init(RectTransform target, RawImage hoverImage = null)
        {
            _target = target;
            _selfRt = GetComponent<RectTransform>();
            _hoverImage = hoverImage;
        }

        void Start()
        {
            var p = _target?.parent;
            while (p != null)
            {
                if (p.GetComponent<Canvas>() != null)
                {
                    _canvasRt = p.GetComponent<RectTransform>();
                    break;
                }
                p = p.parent;
            }
        }

        void Update()
        {
            if (_selfRt == null || _target == null || _canvasRt == null) return;
            var mouse = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            bool over = RectTransformUtility.RectangleContainsScreenPoint(_selfRt, mouse, null);

            if (_hoverImage != null)
            {
                var t = TabHoverStyle.Tint;
                _hoverImage.color = (over || _dragging) ? new Color(t.r, t.g, t.b, HOVER_ON.a) : Color.clear;
            }

            if (Input.GetMouseButtonDown(0) && over && (_activeDrag == null || _activeDrag == this))
            {
                _dragging = true;
                _activeDrag = this;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRt, mouse, null, out var lp);
                _offset = _target.anchoredPosition - lp;
            }

            if (Input.GetMouseButtonUp(0))
            {
                _dragging = false;
                if (_activeDrag == this) _activeDrag = null;
            }

            if (_dragging && _activeDrag == this)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _canvasRt, mouse, null, out var lp);
                _target.anchoredPosition = lp + _offset;
            }
        }

        private void OnDestroy()
        {
            if (_activeDrag == this) _activeDrag = null;
        }
    }
}
