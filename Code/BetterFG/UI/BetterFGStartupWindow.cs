using System;
using System.Collections;
using System.Reflection;
using BetterFG.Services;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FallGuysLib.Utils;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI
{
    /// <summary>
    /// Full startup sequence:
    ///   1. Load betterfg_startup bundle (contains wow/ui/reveal shader)
    ///   2. Logo: wait 3s → fade in 2s + slight scale → fade out 2s
    ///   3. Disclaimer text fades in 2s, button fades in ~2s later
    ///   4. On "I understand": spawn old info window, animate revealer wipe 0→1 over 1s, destroy self
    ///
    /// Sorting: this canvas is 1001 — above tabs (999) and info window (1000).
    /// Everything else is under the revealer until the wipe completes.
    /// </summary>
    public class BetterFGStartupWindow : MonoBehaviour
    {
        public BetterFGStartupWindow(IntPtr ptr) : base(ptr) { }

        // ── Timing constants ───────────────────────────────────────────────────
        private const float LOGO_WAIT = 3f;
        private const float LOGO_FADE_IN = 2f;
        private const float LOGO_FADE_OUT = 2f;
        private const float DISC_FADE_IN = 2f;
        private const float BTN_DELAY = 2f;
        private const float REVEAL_DUR = 1f;

        private const float LOGO_SCALE_FROM = 1.00f;
        private const float LOGO_SCALE_TO = 1.08f;

        // ── Layout ─────────────────────────────────────────────────────────────
        private const float LOGO_W = 420f;
        private const float LOGO_H = 160f;
        private const float DISC_W = 720f;
        private const float DISC_H = 72f;
        private const float BTN_W = 190f;
        private const float BTN_H = 34f;
        private const int FS_DISC = 21;
        private const int FS_BTN = 13;

        // ── Keys ───────────────────────────────────────────────────────────────
        private const string KEY_SEEN = "startup.seen";

        // ── Colors ─────────────────────────────────────────────────────────────
        private static readonly Color WHITE = Color.white;
        private static readonly Color BTN_COLOR = new Color(0.12f, 0.12f, 0.14f, 1f);
        private static readonly Color BTN_TEXT = new Color(0.95f, 0.95f, 0.95f, 1f);

        // ── Resource paths ─────────────────────────────────────────────────────
        private const string BUNDLE_RES = "BetterFG.assets.bundles.betterfg_startup";
        private const string LOGO_RES = "BetterFG.assets.ui.betterfglogo.png";

        // ── Runtime refs ───────────────────────────────────────────────────────
        private Canvas _canvas;
        private CanvasGroup _logoGroup;
        private RectTransform _logoRt;
        private CanvasGroup _discGroup;
        private CanvasGroup _btnGroup;
        private Image _revealerImg;
        private Material _revealMat;

        private static Texture2D _logoTex;

        // ──────────────────────────────────────────────────────────────────────
        public static BetterFGStartupWindow Show()
        {
            if (SettingsService.Get(KEY_SEEN) == "true") return null;
            SettingsService.Set(KEY_SEEN, "true");

            var go = new GameObject("BetterFG_Startup");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go.AddComponent<BetterFGStartupWindow>();
        }

        void Awake()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 0f;

            // Load bundle — shader "wow/ui/reveal" lives inside it
            AssetBundleUtils.LoadEmbeddedBundle(Assembly.GetExecutingAssembly(), BUNDLE_RES);

            BuildCanvas();
            StartCoroutine(Sequence().WrapToIl2Cpp());
        }

        // ── Build ─────────────────────────────────────────────────────────────

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("StartupCanvas");
            canvasGo.hideFlags = HideFlags.HideAndDontSave;
            canvasGo.transform.SetParent(transform, false);
            UnityEngine.Object.DontDestroyOnLoad(canvasGo);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1001;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = BetterFG.Services.UIScaleService.CurrentRef;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            BuildRevealer(canvasGo.transform);
            BuildLogo(canvasGo.transform);
            BuildDisclaimer(canvasGo.transform);
        }

        private void BuildLogo(Transform canvasT)
        {
            var logoGo = new GameObject("Logo");
            logoGo.transform.SetParent(canvasT, false);

            _logoRt = logoGo.AddComponent<RectTransform>();
            _logoRt.anchorMin = new Vector2(0.5f, 0.5f);
            _logoRt.anchorMax = new Vector2(0.5f, 0.5f);
            _logoRt.pivot = new Vector2(0.5f, 0.5f);
            _logoRt.sizeDelta = new Vector2(LOGO_W, LOGO_H);
            _logoRt.anchoredPosition = Vector2.zero;

            _logoGroup = logoGo.AddComponent<CanvasGroup>();
            _logoGroup.alpha = 0f;
            _logoGroup.blocksRaycasts = false;

            var img = logoGo.AddComponent<RawImage>();
            img.texture = LoadLogoTexture();
        }

        private void BuildDisclaimer(Transform canvasT)
        {
            var discGo = new GameObject("Disclaimer");
            discGo.transform.SetParent(canvasT, false);

            var discRt = discGo.AddComponent<RectTransform>();
            discRt.anchorMin = new Vector2(0.5f, 0.5f);
            discRt.anchorMax = new Vector2(0.5f, 0.5f);
            discRt.pivot = new Vector2(0.5f, 0.5f);
            discRt.anchoredPosition = new Vector2(0f, -8f);
            discRt.sizeDelta = new Vector2(DISC_W, DISC_H + BTN_H + 20f);

            _discGroup = discGo.AddComponent<CanvasGroup>();
            _discGroup.alpha = 0f;
            _discGroup.blocksRaycasts = false;

            var vlg = discGo.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 14f;
            vlg.childAlignment = TextAnchor.MiddleCenter;

            // Disclaimer text
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(discGo.transform, false);
            textGo.AddComponent<RectTransform>();
            textGo.AddComponent<LayoutElement>().preferredHeight = DISC_H;
            var t = textGo.AddComponent<Text>();
            t.text = "Every single change in customization you make from this point onward\nwill be purely client-sided, and no one will see it.";
            t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            t.fontSize = FS_DISC;
            t.color = WHITE;
            t.alignment = TextAnchor.MiddleCenter;

            // Button wrapper (separate CanvasGroup for delayed fade-in)
            var btnWrap = new GameObject("BtnWrap");
            btnWrap.transform.SetParent(discGo.transform, false);
            btnWrap.AddComponent<RectTransform>();
            btnWrap.AddComponent<LayoutElement>().preferredHeight = BTN_H;

            _btnGroup = btnWrap.AddComponent<CanvasGroup>();
            _btnGroup.alpha = 0f;
            _btnGroup.blocksRaycasts = false;
            _btnGroup.interactable = false;

            var hLayout = btnWrap.AddComponent<HorizontalLayoutGroup>();
            hLayout.childForceExpandWidth = false;
            hLayout.childForceExpandHeight = false;
            hLayout.childAlignment = TextAnchor.MiddleCenter;

            UGUIShip.CreateButton(btnWrap.transform,
                new Rect(0f, 0f, BTN_W, BTN_H),
                "I understand.", BTN_COLOR, BTN_TEXT, FS_BTN,
                new Action(OnUnderstood));
        }

        private void BuildRevealer(Transform canvasT)
        {
            // Must be last child so it sits on top of bg, logo, disclaimer
            var revGo = new GameObject("revealer");
            revGo.transform.SetParent(canvasT, false);

            var revRt = revGo.AddComponent<RectTransform>();
            revRt.anchorMin = Vector2.zero;
            revRt.anchorMax = Vector2.one;
            revRt.offsetMin = revRt.offsetMax = Vector2.zero;

            _revealerImg = revGo.AddComponent<Image>();
            _revealerImg.raycastTarget = false;
            _revealerImg.color = Color.black;

            TryAssignRevealMaterial();
        }

        private void TryAssignRevealMaterial()
        {
            var shader = Shader.Find("wow/ui/reveal");
            if (shader == null) return;

            _revealMat = new Material(shader);
            _revealMat.SetColor("_Color", Color.black);
            _revealMat.SetFloat("_Progress", 0f);
            _revealMat.SetFloat("_Softness", 0.002f);

            if (_revealerImg != null)
                _revealerImg.material = _revealMat;
        }

        // ── Sequence ──────────────────────────────────────────────────────────

        private IEnumerator Sequence()
        {
            yield return WaitFor(LOGO_WAIT);

            // Logo fade-in + scale
            yield return LogoFadeScale(0f, 1f, LOGO_SCALE_FROM, LOGO_SCALE_TO, LOGO_FADE_IN);

            // Logo fade-out
            yield return FadeGroup(_logoGroup, 1f, 0f, LOGO_FADE_OUT);
            _logoRt.gameObject.SetActive(false);

            // Disclaimer fade-in
            yield return FadeGroup(_discGroup, 0f, 1f, DISC_FADE_IN);
            _discGroup.blocksRaycasts = true;

            // Button delayed fade-in
            yield return WaitFor(BTN_DELAY);
            _btnGroup.blocksRaycasts = true;
            _btnGroup.interactable = true;
            yield return FadeGroup(_btnGroup, 0f, 1f, 0.5f);
        }

        private void OnUnderstood()
        {
            _btnGroup.interactable = false;
            _btnGroup.blocksRaycasts = false;
            StartCoroutine(RevealSequence().WrapToIl2Cpp());
        }

        private IEnumerator RevealSequence()
        {
            // Spawn the info window — it's at sorting order 1000, under our revealer (1001)
            BetterFGInfoWindow.Show();

            // Try assigning shader if it wasn't ready during Awake
            if (_revealMat == null)
                TryAssignRevealMaterial();

            float elapsed = 0f;
            while (elapsed < REVEAL_DUR)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / REVEAL_DUR));
                SetRevealProgress(t);
                yield return null;
            }
            SetRevealProgress(1f);

            Time.timeScale = 1f;
            UnityEngine.Object.Destroy(gameObject);
        }

        private void SetRevealProgress(float t)
        {
            if (_revealMat != null)
                _revealMat.SetFloat("_Progress", t);
            else if (_revealerImg != null)
                _revealerImg.color = new Color(0f, 0f, 0f, 1f - t);
        }

        // ── Coroutine helpers ─────────────────────────────────────────────────

        private IEnumerator WaitFor(float seconds)
        {
            float e = 0f;
            while (e < seconds) { e += Time.unscaledDeltaTime; yield return null; }
        }

        private IEnumerator FadeGroup(CanvasGroup cg, float from, float to, float dur)
        {
            float e = 0f;
            cg.alpha = from;
            while (e < dur)
            {
                e += Time.unscaledDeltaTime;
                cg.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(e / dur));
                yield return null;
            }
            cg.alpha = to;
        }

        private IEnumerator LogoFadeScale(float aFrom, float aTo,
            float sFrom, float sTo, float dur)
        {
            float e = 0f;
            _logoGroup.alpha = aFrom;
            _logoRt.localScale = Vector3.one * sFrom;
            while (e < dur)
            {
                e += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(e / dur);
                _logoGroup.alpha = Mathf.Lerp(aFrom, aTo, t);
                float s = Mathf.Lerp(sFrom, sTo, t);
                _logoRt.localScale = new Vector3(s, s, 1f);
                yield return null;
            }
            _logoGroup.alpha = aTo;
            _logoRt.localScale = Vector3.one * sTo;
        }

        // ── Texture ───────────────────────────────────────────────────────────

        private static Texture2D LoadLogoTexture()
        {
            if (_logoTex != null) return _logoTex;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var s = asm.GetManifestResourceStream(LOGO_RES);
                if (s == null) return null;
                var b = new byte[s.Length];
                s.Read(b, 0, b.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                ImageConversion.LoadImage(tex, b);
                tex.wrapMode = TextureWrapMode.Clamp;
                _logoTex = tex;
            }
            catch (Exception ex) { Plugin.Log.LogError($"StartupWindow: logo load: {ex.Message}"); }
            return _logoTex;
        }
    }
}