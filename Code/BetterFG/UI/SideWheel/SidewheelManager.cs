using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BetterFG.Services;
using BetterFG.UI.Windows;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.SideWheel
{
    public class SideWheelManager : MonoBehaviour
    {
        public SideWheelManager(IntPtr ptr) : base(ptr) { }

        public static SideWheelManager Instance { get; private set; }

        // ── Tweakable at runtime ──────────────────────────────────────────────
        public float RingDiameter = 340f;
        public float RingThickness = 36f;
        public float PeekAmount = 72f;
        public float IconSize = 38f;
        public float IconOrbitR = 137.9549f;
        public float IconScale = 1.2f;
        public float IconHoverScale = 1.5f;
        public float ScrollSpeed = 20f;
        public float ScrollSmooth = 20f;
        public float HitW = 120f;
        public float HitH = 400f;
        public float WindowX = 100f;
        public Color RingColor = new Color(0f, 0f, 0f, 1f);
        public Color SelTint = new Color(1f, 0.85f, 0.4f, 1f);

        // ── Animation curve for window open ───────────────────────────────────
        private static readonly AnimationCurve OpenCurve = new AnimationCurve(new Keyframe[]
        {
            new Keyframe(0f, 0f, 0f, 2.5f),
            new Keyframe(0.6f, 1.05f, 0.3f, 0.3f),
            new Keyframe(1f, 1f, 0f, 0f),
        });

        // ── Runtime refs ──────────────────────────────────────────────────────
        private Canvas _canvas;
        private RectTransform _wheelRt;
        private RingGraphic _ring;
        private CanvasGroup _canvasGroup;

        private float _currentAngle = 0f;
        private float _targetAngle = 0f;
        private int _hoveredIdx = -1;
        private bool _wheelVisible = false;
        // true only when the wheel is actually on screen — the toggle flag alone isn't enough,
        // because hiding the parent BettrFG UI drops the wheel's canvas alpha to 0 while leaving
        // _wheelVisible set. (without this, ControllerManager thinks the wheel is up and keeps
        // driving the cursor even when everything's hidden.)
        public bool IsWheelVisible => _wheelVisible && _canvasGroup != null && _canvasGroup.alpha > 0.01f;


        private readonly List<WheelEntry> _entries = new List<WheelEntry>();
        private readonly List<RectTransform> _iconRts = new List<RectTransform>();
        private readonly List<RawImage> _iconImgs = new List<RawImage>();
        private readonly List<RectTransform> _labelRts = new List<RectTransform>();

        private BetterFGWindow _openWindow;
        private int _selectedIdx = -1;
        private bool _animating = false;
        private int _windowIconIdx = -1;

        private struct WheelEntry
        {
            public string label;
            public Texture2D icon;
            public Func<BetterFGWindow> createWindow;
        }

        // ── Entry point ───────────────────────────────────────────────────────
        public static SideWheelManager Create()
        {
            var go = new GameObject("BetterFG_SideWheel");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go.AddComponent<SideWheelManager>();
        }

        public void RegisterEntry(string label, Texture2D icon, Func<BetterFGWindow> createWindow)
        {
            _entries.Add(new WheelEntry { label = label, icon = icon, createWindow = createWindow });
            RebuildIcons();
        }

        public void SetVisible(bool visible)
        {
            // Called by TabManager Shift+Z — only shows if wheel is also toggled on
            if (_canvasGroup == null) return;
            bool show = visible && _wheelVisible;
            _canvasGroup.alpha = show ? 1f : 0f;
            _canvasGroup.blocksRaycasts = show;
            _canvasGroup.interactable = show;
            if (!visible)
            {
                CloseWindow();
                LobbyAutokickConfigWindow.Instance?.Close();
            }
        }

        // controller bind entry point — same toggle the keyboard hotkey path uses.
        public void ToggleFromController() => SetWheelVisible(!_wheelVisible);

        // general "jump the user to this window" entry: force the wheel visible, open the entry whose window
        // type is T, and once its open animation lands hand the live window to onOpened (used to flash the
        // controls the user was pointed at). label must match what SidewheelRegistry registered T under.
        public void OpenWindow<T>(string label, System.Action<BetterFGWindow> onOpened = null)
            where T : BetterFGWindow
        {
            int idx = _entries.FindIndex(e => e.label == label);
            if (idx < 0) { Debug.LogWarning($"sidewheel: no entry '{label}' to open"); return; }

            if (!_wheelVisible) SetWheelVisible(true);
            // if this window's already the open one, don't re-open (that would toggle it shut) — just re-run
            // the callback against the existing instance.
            if (_selectedIdx == idx && _openWindow != null) { onOpened?.Invoke(_openWindow); return; }

            OnIconClicked(idx);
            if (onOpened != null && _openWindow != null)
                StartCoroutine(InvokeAfterOpen(_openWindow, onOpened).WrapToIl2Cpp());
        }

        private IEnumerator InvokeAfterOpen(BetterFGWindow window, System.Action<BetterFGWindow> cb)
        {
            while (_animating) yield return null;
            cb(window);
        }

        private void SetWheelVisible(bool visible)
        {
            _wheelVisible = visible;
            if (_canvasGroup == null) return;
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.blocksRaycasts = visible;
            _canvasGroup.interactable = visible;
            if (!visible)
            {
                CloseWindow();
                LobbyAutokickConfigWindow.Instance?.Close();
            }
            BetterFGUIMan.Instance?.OnWheelVisibilityChanged(visible);
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Awake()
        {
            Instance = this;
            BuildCanvas();
            SetVisible(false);
        }

        void Update()
        {
            var wheelKey = BetterFG.Services.KeybindService.Get(BetterFG.Services.KeybindId.ToggleWheel);
            if (wheelKey != KeyCode.None
                && Input.GetKeyDown(wheelKey)
                && !Input.GetKey(KeyCode.LeftShift)
                && !Input.GetKey(KeyCode.RightShift)
                && !IsTyping())
                SetWheelVisible(!_wheelVisible);

            if (!_wheelVisible) return;
            HandleScrollAndClick();
            SmoothRotation();
            PositionIcons();
            UpdateHover();
            ApplyRuntimeLayout();
            if (_openWindow != null) UpdateWindowTransform();

        }

        private static bool IsTyping()
        {
            var cur = UnityEngine.EventSystems.EventSystem.current;
            if (cur == null || cur.currentSelectedGameObject == null) return false;
            var go = cur.currentSelectedGameObject;
            if (!go.activeInHierarchy) return false;
            return go.GetComponent<UnityEngine.UI.InputField>() != null
                || go.GetComponent<TMPro.TMP_InputField>() != null;
        }

        private void ApplyRuntimeLayout()
        {
            if (_wheelRt != null)
            {
                _wheelRt.anchoredPosition = new Vector2(-(RingDiameter * 0.5f) + PeekAmount, 0f);
                _wheelRt.sizeDelta = new Vector2(RingDiameter, RingDiameter);
            }
            if (_ring != null)
                _ring.UpdateShape(RingDiameter * 0.5f, RingDiameter * 0.5f - RingThickness, RingColor);
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("SideWheelCanvas");
            canvasGo.hideFlags = HideFlags.HideAndDontSave;
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 998;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = BetterFG.Services.UIScaleService.CurrentRef;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            _canvasGroup = canvasGo.AddComponent<CanvasGroup>();

            var wheelGo = new GameObject("Wheel");
            wheelGo.transform.SetParent(canvasGo.transform, false);
            _wheelRt = wheelGo.AddComponent<RectTransform>();
            _wheelRt.anchorMin = new Vector2(0f, 0.5f);
            _wheelRt.anchorMax = new Vector2(0f, 0.5f);
            _wheelRt.pivot = new Vector2(0.5f, 0.5f);
            _wheelRt.anchoredPosition = new Vector2(-(RingDiameter * 0.5f) + PeekAmount, 0f);
            _wheelRt.sizeDelta = new Vector2(RingDiameter, RingDiameter);
            _wheelRt.localScale = new Vector2(1.2f, 1.2f);


            BuildRingMesh(wheelGo);
        }

        private void BuildRingMesh(GameObject parent)
        {
            const int SEGMENTS = 64;
            float outerR = RingDiameter * 0.5f;
            float innerR = outerR - RingThickness;

            var ringGo = new GameObject("Ring");
            ringGo.transform.SetParent(parent.transform, false);
            var ringRt = ringGo.AddComponent<RectTransform>();
            ringRt.anchorMin = new Vector2(0.5f, 0.5f);
            ringRt.anchorMax = new Vector2(0.5f, 0.5f);
            ringRt.pivot = new Vector2(0.5f, 0.5f);
            ringRt.sizeDelta = new Vector2(RingDiameter, RingDiameter);
            ringRt.anchoredPosition = Vector2.zero;

            _ring = ringGo.AddComponent<RingGraphic>();
            _ring.Init(outerR, innerR, SEGMENTS, RingColor);
        }

        private void RebuildIcons()
        {
            foreach (var rt in _iconRts)
                if (rt != null) UnityEngine.Object.Destroy(rt.gameObject);
            _iconRts.Clear();
            _iconImgs.Clear();
            _labelRts.Clear();

            for (int i = 0; i < _entries.Count; i++)
            {
                int captured = i;
                var entry = _entries[i];

                var iconGo = UGUIShip.CreateIconButton(
                    _wheelRt,
                    new Vector2(IconSize, IconSize),
                    entry.icon,
                    () => OnIconClicked(captured),
                    () => { _hoveredIdx = captured; },
                    () => { if (_hoveredIdx == captured) _hoveredIdx = -1; }
                ).gameObject;

                var rt = iconGo.GetComponent<RectTransform>();
                rt.localScale = new Vector3(IconScale, IconScale, IconScale);

                var raw = iconGo.GetComponent<RawImage>();

                // hover label — child of the icon. CreateLabel gives us the styled Text; we then
                // re-anchor to left-middle pivot so it grows outward, and drive position/rotation/
                // scale per-frame in PositionIcons to keep it on the icon's radial ray.
                var lbl = UGUIShip.CreateLabel(iconGo.transform, new Rect(0, 0, 880f, IconSize / LabelScale),
                    entry.label.ToUpperInvariant(), 16);
                lbl.fontStyle = FontStyle.Bold;
                lbl.horizontalOverflow = HorizontalWrapMode.Overflow;
                lbl.verticalOverflow = VerticalWrapMode.Overflow;
                var lblRt = lbl.rectTransform;
                lblRt.anchorMin = new Vector2(0.5f, 0.5f);
                lblRt.anchorMax = new Vector2(0.5f, 0.5f);
                lblRt.pivot = new Vector2(0f, 0.5f);
                lblRt.localScale = new Vector3(LabelScale, LabelScale, 1f);
                var outline = lbl.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                outline.effectDistance = new Vector2(2f, -2f);
                lbl.gameObject.SetActive(false);

                _iconRts.Add(rt);
                _iconImgs.Add(raw);
                _labelRts.Add(lblRt);
            }

            PositionIcons();
        }

        // ── Input ─────────────────────────────────────────────────────────────

        private void HandleScrollAndClick()
        {
            if (_entries.Count == 0) return;
            if (_canvasGroup != null && _canvasGroup.alpha < 0.01f) return;

            var mouse = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            if (!IsInHitRect(mouse)) return;

            float scroll = Input.mouseScrollDelta.y;
            if (_openWindow == null && Mathf.Abs(scroll) > 0.01f)
                _targetAngle -= scroll * ICON_STEP_DEG;

            if (Input.GetMouseButtonDown(0))
            {
                int hit = GetIconAtScreen(mouse);
                if (hit >= 0) OnIconClicked(hit);
            }
        }

        private bool IsInHitRect(Vector2 screenPos)
        {
            float cy = Screen.height * 0.5f;
            return screenPos.x >= 0f && screenPos.x <= HitW
                && screenPos.y >= cy - HitH * 0.5f && screenPos.y <= cy + HitH * 0.5f;
        }

        private int GetIconAtScreen(Vector2 screenPos)
        {
            float closest = float.MaxValue;
            int bestIdx = -1;
            for (int i = 0; i < _iconRts.Count; i++)
            {
                if (_iconRts[i] == null) continue;
                if (_iconImgs[i] != null && _iconImgs[i].color.a < 0.5f) continue;

                var corners = new Vector3[4];
                _iconRts[i].GetWorldCorners(corners);
                float minX = corners[0].x, maxX = corners[2].x;
                float minY = corners[0].y, maxY = corners[2].y;
                float pad = 10f;
                if (screenPos.x >= minX - pad && screenPos.x <= maxX + pad
                 && screenPos.y >= minY - pad && screenPos.y <= maxY + pad)
                {
                    float d = Vector2.Distance(screenPos,
                        new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f));
                    if (d < closest) { closest = d; bestIdx = i; }
                }
            }
            return bestIdx;
        }

        // ── Rotation ─────────────────────────────────────────────────────────

        private void SmoothRotation()
        {
            _currentAngle = Mathf.LerpAngle(_currentAngle, _targetAngle, Time.deltaTime * ScrollSmooth);
        }

        private void UpdateHover()
        {
            for (int i = 0; i < _iconRts.Count; i++)
            {
                if (_iconRts[i] == null) continue;
                bool hov = i == _hoveredIdx;
                float s = hov ? IconHoverScale : IconScale;
                _iconRts[i].localScale = new Vector3(s, s, s);
                // hide labels on the open window's icon and its two ring neighbours (they'd overlap the window)
                bool nearWindow = _windowIconIdx >= 0 && Mathf.Abs(i - _windowIconIdx) <= 1;
                bool showLabel = hov && !nearWindow;
                if (_labelRts[i] != null && _labelRts[i].gameObject.activeSelf != showLabel)
                    _labelRts[i].gameObject.SetActive(showLabel);
            }
        }

        private const float ICON_STEP_DEG = 25f;
        // hover label renders at a big font size then scales down here so text stays crisp
        private const float LabelScale = 0.68f;
        // how far past the icon center the label sits, along the radial ray (icon-local px)
        private const float LabelGap = 34f;
        // fraction of the ray angle the label tilts by; 1 = fully radial, 0 = always flat
        private const float LabelTiltStrength = 0.3f;

        private void PositionIcons()
        {
            if (_entries.Count == 0) return;
            for (int i = 0; i < _iconRts.Count; i++)
            {
                float angleDeg = _currentAngle - ICON_STEP_DEG * i;
                float angleRad = angleDeg * Mathf.Deg2Rad;
                var pos = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * IconOrbitR;
                _iconRts[i].anchoredPosition = pos;

                float vis = Mathf.Clamp01((pos.x + IconSize) / (IconSize * 2f));
                if (_iconImgs[i] != null) _iconImgs[i].color = new Color(1f, 1f, 1f, vis);

                // keep the hover label on the same radial ray as the icon, pushed further out and
                // rotated to read outward. the icon stays upright; the label pivot is placed along
                // the outward direction in the icon's local space (uniform IconScale, so divide).
                if (_labelRts[i] != null)
                {
                    var outward = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad));
                    _labelRts[i].anchoredPosition = outward * (LabelGap / IconScale);
                    _labelRts[i].localRotation = Quaternion.Euler(0f, 0f, angleDeg * LabelTiltStrength);
                }
            }
        }

        // Snap target so icon[idx] lands at 0° (3 o'clock, rightmost visible)
        private void RotateToIcon(int idx)
        {
            float iconAngle = (_currentAngle - ICON_STEP_DEG * idx) % 360f;
            if (iconAngle > 180f) iconAngle -= 360f;
            _targetAngle = _currentAngle - iconAngle;
        }

        // ── Window ────────────────────────────────────────────────────────────

        private void OnIconClicked(int idx)
        {
            if (_animating) return;

            if (_selectedIdx == idx && _openWindow != null)
            {
                CloseWindow();
                return;
            }

            CloseWindow();
            _selectedIdx = idx;
            _windowIconIdx = idx;
            RefreshIconTints();

            // Capture icon's current angle on the ring before rotation starts
            float spawnAngleDeg = (_currentAngle - ICON_STEP_DEG * idx) % 360f;
            if (spawnAngleDeg > 180f) spawnAngleDeg -= 360f;

            RotateToIcon(idx);

            var window = _entries[idx].createWindow();
            if (window == null) return;
            _openWindow = window;

            StartCoroutine(AnimateWindowOpen(window, spawnAngleDeg, Vector2.zero).WrapToIl2Cpp());
        }

        private IEnumerator AnimateWindowOpen(BetterFGWindow window, float startAngleDeg, Vector2 spawnLocalPos)
        {
            _animating = true;
            float elapsed = 0f;
            const float DUR = 0.18f;

            var wheelPos = _wheelRt.anchoredPosition;
            float restOffsetX = WindowX - wheelPos.x;

            // Start position: icon's world position on the ring at click time
            float startRad = startAngleDeg * Mathf.Deg2Rad;
            var startPos = wheelPos + new Vector2(Mathf.Cos(startRad), Mathf.Sin(startRad)) * IconOrbitR;

            // End position: window at rest (angle = 0 after RotateToIcon snaps)
            var endPos = new Vector2(WindowX, 0f);

            window.SetAnchorPosition(startPos);
            window.SetRotation(startAngleDeg);
            window.SetScaleX(0f);
            window.Show();
            AudioService.PlayTabOpen();

            while (elapsed < DUR)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / DUR);
                float curve = OpenCurve.Evaluate(t);

                // Track the icon's live position during ring rotation
                float liveAngle = _currentAngle - ICON_STEP_DEG * _windowIconIdx;
                float liveRad = liveAngle * Mathf.Deg2Rad;
                var liveWheelPos = _wheelRt.anchoredPosition;
                float liveOffsetX = WindowX - liveWheelPos.x;
                var livePos = liveWheelPos + new Vector2(Mathf.Cos(liveRad), Mathf.Sin(liveRad)) * liveOffsetX;

                window.SetAnchorPosition(livePos);
                window.SetRotation(liveAngle);
                window.SetScaleX(curve);
                yield return null;
            }

            window.SetScaleX(1f);
            _animating = false;
        }

        private IEnumerator AnimateWindowClose(BetterFGWindow window)
        {
            float elapsed = 0f;
            const float DUR = 0.12f;
            AudioService.PlayTabClose();

            while (elapsed < DUR)
            {
                elapsed += Time.deltaTime;
                float t = 1f - Mathf.Clamp01(elapsed / DUR);
                window.SetScaleX(t * t);
                yield return null;
            }
            window.SetScaleX(0f);
            UnityEngine.Object.Destroy(window.gameObject);
        }

        private void CloseWindow()
        {
            _selectedIdx = -1;
            _windowIconIdx = -1;
            RefreshIconTints();

            if (_openWindow != null)
            {
                var win = _openWindow;
                _openWindow = null;
                StartCoroutine(AnimateWindowClose(win).WrapToIl2Cpp());
            }
        }

        private void RefreshIconTints()
        {
            for (int i = 0; i < _iconImgs.Count; i++)
            {
                if (_iconImgs[i] == null) continue;
                _iconImgs[i].color = i == _selectedIdx ? SelTint : Color.white;
            }
        }

        private void UpdateWindowTransform()
        {
            if (_openWindow == null || _windowIconIdx < 0 || _animating) return;
            float angleDeg = _currentAngle - ICON_STEP_DEG * _windowIconIdx;
            float angleRad = angleDeg * Mathf.Deg2Rad;

            // Rotate the rest offset (window-to-ring-center distance at angle 0) around the ring center.
            var wheelPos = _wheelRt.anchoredPosition;
            float restOffsetX = WindowX - wheelPos.x;
            var rotated = new Vector2(
                Mathf.Cos(angleRad) * restOffsetX,
                Mathf.Sin(angleRad) * restOffsetX
            );
            _openWindow.SetAnchorPosition(wheelPos + rotated);
            _openWindow.SetRotation(angleDeg);
        }

        // ── Texture loader ────────────────────────────────────────────────────

        public static Texture2D LoadEmbedded(string resourceName)
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
            catch (Exception ex)
            {
                Debug.LogError($"[SideWheel] LoadEmbedded {resourceName}: {ex.Message}");
                return null;
            }
        }
    }

    // ── Ring graphic ──────────────────────────────────────────────────────────
    public class RingGraphic : Graphic
    {
        public RingGraphic(IntPtr ptr) : base(ptr) { }

        private float _outerR;
        private float _innerR;
        private int _segments;

        public void Init(float outerR, float innerR, int segments, Color col)
        {
            _outerR = outerR;
            _innerR = innerR;
            _segments = segments;
            color = col;
            SetVerticesDirty();
        }

        public void UpdateShape(float outerR, float innerR, Color col)
        {
            bool dirty = !Mathf.Approximately(_outerR, outerR)
                      || !Mathf.Approximately(_innerR, innerR)
                      || color != col;
            _outerR = outerR;
            _innerR = innerR;
            color = col;
            if (dirty) SetVerticesDirty();
        }

        public override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_segments == 0) return;
            float step = 2f * Mathf.PI / _segments;
            for (int i = 0; i < _segments; i++)
            {
                float a0 = step * i;
                float a1 = step * (i + 1);
                Vector2 o0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * _outerR;
                Vector2 o1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * _outerR;
                Vector2 i0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * _innerR;
                Vector2 i1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * _innerR;
                int b = i * 4;
                vh.AddVert(o0, color, Vector2.zero);
                vh.AddVert(o1, color, Vector2.zero);
                vh.AddVert(i1, color, Vector2.zero);
                vh.AddVert(i0, color, Vector2.zero);
                vh.AddTriangle(b, b + 1, b + 2);
                vh.AddTriangle(b, b + 2, b + 3);
            }
        }
    }
}   
