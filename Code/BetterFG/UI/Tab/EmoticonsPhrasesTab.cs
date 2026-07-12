using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Social;
using BetterFG.UI;
using BetterFG.Utilities;
using Character;
using FGClient;
using MPG.Utility;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    public class EmoticonsPhrasesTab : BetterFGTab
    {
        public EmoticonsPhrasesTab(IntPtr ptr) : base(ptr) { }

        // builds 3 stacked sound rows (Sound button + filename + red X to clear) for an entry that
        // has a string[3] soundPaths. onChanged is called after any pick/clear so the caller can save.
        private void BuildSoundRows(Transform parent, string[] soundPaths, float startY, float leftW,
            Action onChanged)
        {
            float gap = PAD * 0.5f;
            float soundBtnW = BTN_H * 2.2f;
            float xW = BTN_H * 0.9f;
            float lblW = leftW - soundBtnW - xW - PAD * 3f;
            for (int i = 0; i < 3; i++)
            {
                int slot = i;
                float rowY = startY + (RBTN_H + gap) * i;
                string name = string.IsNullOrEmpty(soundPaths[slot]) ? "No sound" : Path.GetFileName(soundPaths[slot]);
                var lbl = UGUIShip.CreateLabel(parent,
                    new Rect(PAD + soundBtnW + PAD, rowY, lblW, RBTN_H), name, FS_SM, HINT);
                UGUIShip.CreateButton(parent,
                    new Rect(PAD, rowY, soundBtnW, RBTN_H), "Sound " + (slot + 1),
                    new Color(0.22f, 0.32f, 0.42f, 1f), WHITE, FS_SM,
                    new Action(() => WinDialogs.PickAudio("Select sound", path =>
                    {
                        if (string.IsNullOrEmpty(path)) return;
                        soundPaths[slot] = path;
                        onChanged();
                        if (lbl != null) lbl.text = Path.GetFileName(path);
                    })));
                UGUIShip.CreateButton(parent,
                    new Rect(PAD + soundBtnW + PAD + lblW + PAD, rowY, xW, RBTN_H), "X",
                    BTN_RM, WHITE, FS_SM,
                    new Action(() =>
                    {
                        soundPaths[slot] = "";
                        onChanged();
                        if (lbl != null) lbl.text = "No sound";
                    }));
            }
        }

        public override string TabTitle => "Social";

        // ── Sub-tab state ─────────────────────────────────────────────────────
        private enum SubTab { Phrases, Emoticons, Emotes }
        private SubTab _sub = SubTab.Phrases;

        // ── Phrases state ─────────────────────────────────────────────────────
        private List<PhraseEntry> _entries = new List<PhraseEntry>();
        private string _editingPhraseId; // which phrase row is expanded into edit mode (sound buttons)

        // ── Emoticons state ───────────────────────────────────────────────────
        private List<EmoticonEntry> _emoticonEntries = new List<EmoticonEntry>();
        private string _editingEmoticonId; // which emoticon row is expanded into edit mode

        // ── Emotes state ──────────────────────────────────────────────────────
        private List<EmoteEntry> _emoteEntries = new List<EmoteEntry>();

        // ── Colors ────────────────────────────────────────────────────────────
        private static readonly Color WHITE = Color.white;
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color SEL = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color DARK = new Color(0.18f, 0.18f, 0.18f, 1f);
        private static readonly Color BTN_ADD = new Color(0.22f, 0.42f, 0.22f, 1f);
        private static readonly Color BTN_APPLY = new Color(0.45f, 0.35f, 0.25f, 1f);
        private static readonly Color BTN_RM = new Color(0.45f, 0.1f, 0.1f, 1f);
        private static readonly Color TOGGLE_ON = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color TOGGLE_OFF = new Color(0.28f, 0.28f, 0.28f, 1f);
        private static readonly Color STATUS_C = new Color(0.6f, 1f, 0.6f, 0.85f);
        private static readonly Color WARN_C = new Color(1f, 0.35f, 0.35f, 1f);



        // loaded preview textures keyed by entry id
        private readonly Dictionary<string, Texture2D> _previewTextures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, Texture2D> _emoticonPreviewTextures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, Texture2D> _emotePreviewTextures = new Dictionary<string, Texture2D>();

        private Texture2D LoadEmoticonPreview(EmoticonEntry e)
        {
            if (string.IsNullOrEmpty(e.imagePath) || !File.Exists(e.imagePath)) return null;
            if (_emoticonPreviewTextures.TryGetValue(e.id, out var cached)) return cached;
            try
            {
                byte[] data = File.ReadAllBytes(e.imagePath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(data);
                tex.Apply();
                tex.hideFlags = HideFlags.HideAndDontSave;
                _emoticonPreviewTextures[e.id] = tex;
                return tex;
            }
            catch { return null; }
        }

        private Texture2D LoadPreview(PhraseEntry e)
        {
            if (string.IsNullOrEmpty(e.imagePath) || !File.Exists(e.imagePath)) return null;
            if (_previewTextures.TryGetValue(e.id, out var cached)) return cached;
            try
            {
                byte[] data = File.ReadAllBytes(e.imagePath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(data);
                tex.Apply();
                tex.hideFlags = HideFlags.HideAndDontSave;
                _previewTextures[e.id] = tex;
                return tex;
            }
            catch { return null; }
        }
        private static float PAD => UIScale.PAD;
        private static float VPAD => UIScale.VPAD;
        private static float LH => UIScale.LH;
        private static float SH => UIScale.SH;
        private static float BTN_H => UIScale.BTN_H;
        private static int FS => UIScale.FS;
        private static int FS_SM => UIScale.FS_SM;

        private static float SUBTAB_H => BTN_H * 0.9f;
        private static float ROW_H => 82f * UIScale.S; // shorter rows
        // compact control height used inside list rows so 3 lines + minus fit the shorter row
        private static float RBTN_H => BTN_H * 0.8f;

        // ── Textures ──────────────────────────────────────────────────────────
        private static Texture2D _bgTex;
        private static Texture2D _bgHoverTex;
        private static Texture2D _wheelTex;
        private GameObject _bgHoverGo;

        // wheel visual that floats above the tab — hidden by default, fades in over 2s on open
        private CanvasGroup _wheelCg;
        private float _wheelTarget = 0f; // 1 when open, 0 when closed
        private const float WHEEL_FADE_DUR = 2f;

        // 7 Images around the wheel, slot 0 at the top going clockwise — populated from
        // whichever sub-tab is active so the player can see what's where without opening in-game.
        // game sprites can be displayed as-is; custom entries get their texture wrapped in a Sprite.
        private Image[] _wheelSlotImages;
        private RectTransform _wheelRt;
        private readonly Dictionary<Texture2D, Sprite> _slotSpriteCache = new Dictionary<Texture2D, Sprite>();
        // gate to keep RefreshWheelSlots from poking GlobalGameStateClient/CustomisationSelections
        // during scene/plugin load — Rewired-related code paths there blow up if touched too early
        private bool _userOpenedOnce;

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
                tex.LoadImage(b);
                tex.wrapMode = TextureWrapMode.Clamp;
                cache = tex;
            }
            catch (Exception ex) { Debug.LogError($"[EmoticonsPhrasesTab] {name}: {ex.Message}"); }
            return cache;
        }

        // ── UGUI refs ─────────────────────────────────────────────────────────
        private Button _btnSubPhrases, _btnSubEmoticons, _btnSubEmotes;
        private GameObject _phrasesPanel, _emoticonsPanel, _emotesPanel;
        private GameObject _phrasesBottomBar, _emoticonsBottomBar, _emotesBottomBar;
        private RectTransform _scrollContent;
        private RectTransform _emoticonScrollContent;
        private RectTransform _emoteScrollContent;
        private Text _statusLabel;
        private Text _emoticonStatusLabel;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        void Awake()
        {
            _entries = PhraseSettingsService.Load();
            PhraseInjectionService.SetEntries(_entries);

            _emoticonEntries = EmoticonSettingsService.Load();
            EmoticonInjectionService.SetEntries(_emoticonEntries);

            _emoteEntries = EmoteSettingsService.Load();
            EmoteInjectionService.SetEntries(_emoteEntries);
        }

        void Update()
        {
            WinDialogs.Tick();

            if (_wheelCg != null && _wheelCg.alpha != _wheelTarget)
            {
                float step = Time.unscaledDeltaTime / WHEEL_FADE_DUR;
                _wheelCg.alpha = Mathf.MoveTowards(_wheelCg.alpha, _wheelTarget, step);
            }

            // refresh the paste button when a new emote gets copied while we're sitting here
            if (_sub == SubTab.Emotes && _seenClipboardVersion != EmoteClipboard.Version)
            {
                _seenClipboardVersion = EmoteClipboard.Version;
                UpdatePasteRow();
            }
        }

        // ── Background ────────────────────────────────────────────────────────

        protected override void BuildBackground(RectTransform root)
        {
            var bgTex = LoadEmbedded("BetterFG.assets.ui.emoticonsphrases.bg.png", ref _bgTex);
            if (bgTex == null) return;

            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(root, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            bgRt.localScale = new Vector3(1.5015f, 1.3502f, 1f);
            bgRt.localPosition = new Vector3(267.7578f, 285.8921f, 0f);
            bgGo.AddComponent<RawImage>().texture = bgTex;

            var hoverTex = LoadEmbedded("BetterFG.assets.ui.bg_hover.png", ref _bgHoverTex);
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

            // wheel visual floating above the tab. hidden by default (alpha 0 via CanvasGroup),
            // fades in over 2s when the tab opens and back out on close (driven in Update).
            var wheelTex = LoadEmbedded("BetterFG.assets.ui.emoticonsphrases.wheel.png", ref _wheelTex);
            if (wheelTex != null)
            {
                float aspect = (float)wheelTex.width / wheelTex.height;
                float wheelW = TabWidth * 0.7f;
                float wheelH = wheelW / aspect;

                var wheelGo = new GameObject("WheelVisual");
                wheelGo.transform.SetParent(root, false);
                var wheelRt = wheelGo.AddComponent<RectTransform>();
                // anchor to top-center of the tab, sitting just above it
                wheelRt.anchorMin = new Vector2(0.5f, 1f);
                wheelRt.anchorMax = new Vector2(0.5f, 1f);
                wheelRt.pivot = new Vector2(0.5f, 0f);
                wheelRt.sizeDelta = new Vector2(wheelW, wheelH);
                wheelRt.anchoredPosition = new Vector2(0f, PAD);
                wheelGo.AddComponent<RawImage>().texture = wheelTex;
                _wheelRt = wheelRt;

                // 7 slot images around the wheel — 0 at top, clockwise. radius/size tuned to
                // roughly land on the wheel's icon ring. textures get filled in by RefreshWheelSlots.
                _wheelSlotImages = new Image[8];
                float slotSize = wheelW * 0.14f * 1.6f;
                float radius = wheelW * 0.46f;
                Vector2 center = new Vector2(0f, wheelH * 0.5f);
                for (int i = 0; i < 8; i++)
                {
                    float ang = (Mathf.PI * 2f) * (i / 8f) - Mathf.PI * 0.5f; // 0 at top, clockwise
                    float x = center.x + Mathf.Cos(ang) * radius;
                    float y = center.y - Mathf.Sin(ang) * radius;

                    var slotGo = new GameObject("WheelSlot_" + i);
                    slotGo.transform.SetParent(wheelGo.transform, false);
                    var slotRt = slotGo.AddComponent<RectTransform>();
                    slotRt.anchorMin = new Vector2(0.5f, 0f);
                    slotRt.anchorMax = new Vector2(0.5f, 0f);
                    slotRt.pivot = new Vector2(0.5f, 0.5f);
                    slotRt.sizeDelta = new Vector2(slotSize, slotSize);
                    slotRt.anchoredPosition = new Vector2(x, y);
                    var img = slotGo.AddComponent<Image>();
                    img.raycastTarget = false;
                    img.preserveAspect = true;
                    img.color = new Color(1f, 1f, 1f, 0f);
                    _wheelSlotImages[i] = img;
                }

                _wheelCg = wheelGo.AddComponent<CanvasGroup>();
                _wheelCg.alpha = 0f;
                _wheelCg.blocksRaycasts = false;
                _wheelCg.interactable = false;

                // fake image-based wheel is superseded by the real instantiated SocialPrimeHandler.
                // keep the build code above but leave it off.
                wheelGo.SetActive(false);
            }
        }

        public override void OnOpened() { _wheelTarget = 1f; _userOpenedOnce = true; RefreshWheelSlots(); SpawnPrimeVisualizer(); }
        public override void OnClosed() { _wheelTarget = 0f; DestroyPrimeVisualizer(); }

        // ── Real wheel visualizer ─────────────────────────────────────────────
        // clone the live (inactive HideAndDontSave) SocialPrimeHandler, park it in this tab's own
        // Tab_X GameObject, flip on its overlay, and DisplayWheel for the current sub-tab. rebuilt
        // fresh on every refresh (calling DisplayWheel twice on a live clone tears the wheel down),
        // and destroyed on close so nothing lingers.
        private GameObject _primeClone;

        // the game's OnPointerClick collapses the wheel; the DisplayWheelPatch postfix calls back here
        // to bring it right back. only our clone should react, so match on the GameObject.
        public static void OnPrimeClicked(GameObject clickedGo)
        {
            if (_activeVisualizer == null || _activeVisualizer._primeClone != clickedGo) return;
            _activeVisualizer.DisplayPrimeWheel();
        }
        private static EmoticonsPhrasesTab _activeVisualizer;

        private void SpawnPrimeVisualizer()
        {
            DestroyPrimeVisualizer();

            // the handler lives as an inactive HideAndDontSave template, so FindObjectOfType (active
            // only) misses it — sweep every loaded instance and take the first.
            var all = Resources.FindObjectsOfTypeAll<SocialPrimeHandler>();
            SocialPrimeHandler scenePrime = all != null && all.Length > 0 ? all[0] : null;
            if (scenePrime == null || Root == null) return;

            var cloneGo = UnityEngine.Object.Instantiate(scenePrime.gameObject, Root);
            cloneGo.name = "BettrFG_SocialPrimeVisualizer";
            cloneGo.hideFlags = HideFlags.None; // template was HideAndDontSave; let the clone render normally
            cloneGo.transform.localPosition = new Vector3(200.5452f, 678.8191f, 0f);
            cloneGo.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
            cloneGo.SetActive(true);
            _primeClone = cloneGo;
            _activeVisualizer = this;

            // enable the overlay child, and kill the dark full-screen scrim it drops behind the wheel
            foreach (var t in cloneGo.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "TabContentSocialPhraseOverlay") t.gameObject.SetActive(true);
                else if (t.name == "Generic_UI_Scrim_Prefab" || t.name == "scrim") t.gameObject.SetActive(false);
            }

            // slot numbers 0..7 ringed clockwise from the top so you can see which slot is which
            for (int i = 0; i < 8; i++)
            {
                float ang = (Mathf.PI * 2f) * (i / 8f) - Mathf.PI * 0.5f; // 0 at top, clockwise
                var numGo = new GameObject("SlotNum_" + i);
                numGo.transform.SetParent(cloneGo.transform, false);
                var numRt = numGo.AddComponent<RectTransform>();
                numRt.anchorMin = numRt.anchorMax = new Vector2(0.5f, 0.5f);
                numRt.pivot = new Vector2(0.5f, 0.5f);
                numRt.sizeDelta = new Vector2(60f, 60f);
                numRt.anchoredPosition = new Vector2(Mathf.Cos(ang) * 150f, -Mathf.Sin(ang) * 150f);
                var txt = numGo.AddComponent<Text>();
                txt.text = i.ToString();
                txt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                txt.fontSize = 40;
                txt.fontStyle = FontStyle.Bold;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.color = Color.white;
                txt.raycastTarget = false;
            }

            // let the clone settle a frame, then paint the wheel
            StartCoroutine(DisplayPrimeWheelNextFrame().WrapToIl2Cpp());
        }

        private IEnumerator DisplayPrimeWheelNextFrame()
        {
            yield return null;
            DisplayPrimeWheel();
        }

        // reuse the existing clone — just re-DisplayWheel for the current sub-tab (the DisplayWheel
        // Harmony prefix re-injects custom slots each call). only spawn fresh if we don't have one.
        private void RefreshPrimeVisualizer()
        {
            if (_primeClone == null) { SpawnPrimeVisualizer(); return; }
            DisplayPrimeWheel();
        }

        private void DisplayPrimeWheel()
        {
            var prime = _primeClone?.GetComponent<SocialPrimeHandler>();
            if (prime == null) return;
            var type = _sub == SubTab.Phrases ? WheelType.Phrases : WheelType.EmotesAndEmoticons;
            // set _wheelType BEFORE DisplayWheel: the injection prefix reads it (via FindObjectOfType)
            // to decide which wheel to inject, and DisplayWheel only sets it after the prefix runs.
            prime._wheelType = type;
            prime.DisplayWheel(type);
            // DisplayWheel grabs the game's camera/movement input by disabling the player's Rewired
            // maps (SetRewiredStatesForInputWheel(true)). we only want the wheel RENDERED here, not
            // controlling anything — so undo just that: a frame later, run the same coroutine with
            // false to hand input back. wheel stays fully drawn, camera/movement stay yours. doing it
            // next-frame avoids racing DisplayWheel's own (true) coroutine that's still applying.
            StartCoroutine(ReleaseWheelInputNextFrame(prime).WrapToIl2Cpp());
        }

        private IEnumerator ReleaseWheelInputNextFrame(SocialPrimeHandler prime)
        {
            yield return null;
            if (prime == null) yield break;
            var co = prime.SetRewiredStatesForInputWheel(false);
            while (co != null && co.MoveNext()) yield return co.Current;
        }

        private void DestroyPrimeVisualizer()
        {
            if (_activeVisualizer == this) _activeVisualizer = null;
            if (_primeClone != null)
            {
                // run the handler's own OnDestroy for a clean teardown (event unsubscribes etc).
                // the camera input lock is already handed back right after DisplayWheel now, so this
                // isn't load-bearing for that anymore — but plain Destroy() skips OnDestroy, so keep it.
                _primeClone.GetComponent<SocialPrimeHandler>()?.OnDestroy();
                Destroy(_primeClone);
                _primeClone = null;
            }
        }

        protected override void OnTitleHoverChanged(bool hovering)
        {
            if (_bgHoverGo != null) _bgHoverGo.SetActive(hovering);
        }

        // ── Content ───────────────────────────────────────────────────────────

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            // sub-tab bar — three even thirds
            float thirdW = (w - PAD) / 3f;
            _btnSubPhrases = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD, y, thirdW, SUBTAB_H), "Phrases",
                _sub == SubTab.Phrases ? SEL : DARK, WHITE, FS_SM,
                new Action(() => SetSubTab(SubTab.Phrases)));
            _btnSubEmoticons = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD + thirdW + PAD * 0.5f, y, thirdW, SUBTAB_H), "Emoticons",
                _sub == SubTab.Emoticons ? SEL : DARK, WHITE, FS_SM,
                new Action(() => SetSubTab(SubTab.Emoticons)));
            _btnSubEmotes = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD + (thirdW + PAD * 0.5f) * 2f, y, thirdW, SUBTAB_H), "Emotes",
                _sub == SubTab.Emotes ? SEL : DARK, WHITE, FS_SM,
                new Action(() => SetSubTab(SubTab.Emotes)));
            y += SUBTAB_H + SH;

            UGUIShip.CreateDivider(contentRoot, PAD, y, w); y += 1f + SH;

            // body + bottom bar
            float bottomBarH = BTN_H + PAD * 2f;
            float bodyH = TabHeight - y - bottomBarH;

            // phrases panel
            _phrasesPanel = new GameObject("PhrasesPanel");
            _phrasesPanel.transform.SetParent(contentRoot, false);
            var ppRt = _phrasesPanel.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(ppRt, new Rect(0f, y, TabWidth, bodyH));
            BuildPhrasesScrollArea(ppRt);

            // phrases bottom bar
            float barY = y + bodyH + PAD;
            float btnW = (w - PAD * 0.5f) / 2f;
            var pBarGo = new GameObject("PhrasesBottomBar");
            pBarGo.transform.SetParent(contentRoot, false);
            var pBarRt = pBarGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(pBarRt, new Rect(0f, barY - PAD, TabWidth, BTN_H + PAD * 2f));
            UGUIShip.CreateButton(pBarGo.transform,
                new Rect(PAD, PAD, btnW, BTN_H), "+ Add Phrase",
                BTN_ADD, WHITE, FS, new Action(OnAddPhrase));
            UGUIShip.CreateButton(pBarGo.transform,
                new Rect(PAD + btnW + PAD * 0.5f, PAD, btnW, BTN_H), "Apply All",
                BTN_APPLY, WHITE, FS, new Action(OnApplyAll));
            _phrasesBottomBar = pBarGo;

            // emoticons panel
            _emoticonsPanel = new GameObject("EmoticonsPanel");
            _emoticonsPanel.transform.SetParent(contentRoot, false);
            var epRt = _emoticonsPanel.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(epRt, new Rect(0f, y, TabWidth, bodyH));
            BuildEmoticonsScrollArea(epRt);

            // bottom bar — emoticons buttons live inside the panel so they hide with it
            float epBarY = y + bodyH + PAD;
            float epBtnW = (w - PAD * 0.5f) / 2f;
            var epBarGo = new GameObject("EmoticonsBottomBar");
            epBarGo.transform.SetParent(contentRoot, false);
            var epBarRt = epBarGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(epBarRt, new Rect(0f, epBarY - PAD, TabWidth, BTN_H + PAD * 2f));
            UGUIShip.CreateButton(epBarGo.transform,
                new Rect(PAD, PAD, epBtnW, BTN_H), "+ Add Emoticon",
                BTN_ADD, WHITE, FS, new Action(OnAddEmoticon));
            UGUIShip.CreateButton(epBarGo.transform,
                new Rect(PAD + epBtnW + PAD * 0.5f, PAD, epBtnW, BTN_H), "Apply All",
                BTN_APPLY, WHITE, FS, new Action(OnApplyAllEmoticons));

            // link panel + bar visibility together via a wrapper isn't worth it — just
            // track the bar go and toggle it in RefreshSubTabVisibility
            _emoticonsBottomBar = epBarGo;

            // emotes panel
            _emotesPanel = new GameObject("EmotesPanel");
            _emotesPanel.transform.SetParent(contentRoot, false);
            var emRt = _emotesPanel.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(emRt, new Rect(0f, y, TabWidth, bodyH));
            BuildEmotesScrollArea(emRt);

            float emBarY = y + bodyH + PAD;
            float emBtnW3 = (w - PAD) / 3f;
            var emBarGo = new GameObject("EmotesBottomBar");
            emBarGo.transform.SetParent(contentRoot, false);
            var emBarRt = emBarGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(emBarRt, new Rect(0f, emBarY - PAD, TabWidth, BTN_H + PAD * 2f));

            BuildPasteButton(emBarGo.transform, new Rect(PAD, PAD, emBtnW3, BTN_H));
            UGUIShip.CreateButton(emBarGo.transform,
                new Rect(PAD + emBtnW3 + PAD * 0.5f, PAD, emBtnW3, BTN_H), "+ Add Emote",
                BTN_ADD, WHITE, FS, new Action(OnAddEmote));
            UGUIShip.CreateButton(emBarGo.transform,
                new Rect(PAD + (emBtnW3 + PAD * 0.5f) * 2f, PAD, emBtnW3, BTN_H), "Apply All",
                BTN_APPLY, WHITE, FS, new Action(OnApplyAllEmotes));
            _emotesBottomBar = emBarGo;

            RefreshSubTabVisibility();
        }

        // ── Scroll area ───────────────────────────────────────────────────────

        private void BuildPhrasesScrollArea(RectTransform root)
        {
            var scroll = UGUIShip.CreateScrollView(root, new Rect(PAD, 0f, root.rect.width - PAD * 2f, root.rect.height));
            var sv = scroll.scrollRect;
            sv.scrollSensitivity = 60f;
            _scrollContent = scroll.content;
            _scrollContent.pivot = new Vector2(0.5f, 1f);
            _scrollContent.sizeDelta = Vector2.zero;

            var vlg = _scrollContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = PAD;
            vlg.padding = new RectOffset(0, 0, (int)PAD, (int)PAD);
            _scrollContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RefreshList();
        }

        // ── List ──────────────────────────────────────────────────────────────

        private void RefreshList()
        {
            if (_scrollContent == null) return;

            for (int i = _scrollContent.childCount - 1; i >= 0; i--)
            {
                var c = _scrollContent.GetChild(i);
                if (c != null) Destroy(c.gameObject);
            }

            for (int i = 0; i < _entries.Count; i++)
                CreateRow(_entries[i], i);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
            if (_userOpenedOnce) { RefreshWheelSlots(); RefreshPrimeVisualizer(); }
        }

        private void CreateRow(PhraseEntry entry, int index)
        {
            int captured = index;
            bool editing = _editingPhraseId == entry.id;
            // edit mode stacks 3 sound rows under the slot line
            float rowH = editing ? ROW_H + (RBTN_H + PAD * 0.5f) * 3f + PAD : ROW_H;

            var rowGo = new GameObject("PhraseRow_" + entry.id);
            rowGo.transform.SetParent(_scrollContent, false);
            rowGo.AddComponent<RectTransform>();

            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = rowH;
            le.flexibleWidth = 1f;

            // image preview column on the right: square, preserve aspect
            float imgColW = ROW_H; // square slot
            float leftW = TabWidth - PAD * 4f - imgColW - PAD;

            // ── Image column ──────────────────────────────────────────────────
            // pinned top-right as a fixed square so it doesn't stretch when the row expands
            var imgColGo = new GameObject("ImgCol");
            imgColGo.transform.SetParent(rowGo.transform, false);
            var imgColRt = imgColGo.AddComponent<RectTransform>();
            imgColRt.anchorMin = new Vector2(1f, 1f);
            imgColRt.anchorMax = new Vector2(1f, 1f);
            imgColRt.pivot = new Vector2(1f, 1f);
            imgColRt.sizeDelta = new Vector2(imgColW, ROW_H - PAD * 2f);
            imgColRt.anchoredPosition = new Vector2(-PAD, -PAD);

            var imgBg = imgColGo.AddComponent<Image>();
            imgBg.color = new Color(0.08f, 0.08f, 0.08f, 1f);

            Texture2D preview = LoadPreview(entry);
            if (preview != null)
            {
                var imgGo = new GameObject("Preview");
                imgGo.transform.SetParent(imgColGo.transform, false);
                var imgRt = imgGo.AddComponent<RectTransform>();
                imgRt.anchorMin = Vector2.zero;
                imgRt.anchorMax = Vector2.one;
                imgRt.offsetMin = imgRt.offsetMax = Vector2.zero;
                var rawImg = imgGo.AddComponent<RawImage>();
                rawImg.texture = preview;
                // preserve aspect ratio: scale to fit inside the square
                float aspect = (float)preview.width / preview.height;
                if (aspect >= 1f)
                {
                    // wider than tall — fill height, letterbox sides
                    float scaledW = imgColW * aspect;
                    imgRt.offsetMin = new Vector2((imgColW - scaledW) * 0.5f, 0f);
                    imgRt.offsetMax = new Vector2(-(imgColW - scaledW) * 0.5f, 0f);
                }
                // taller: just fill, it'll be clamped by the row height
            }
            else
            {
                UGUIShip.CreateLabel(imgColGo.transform,
                    new Rect(0f, 0f, imgColW, ROW_H - PAD * 2f),
                    "No img", FS_SM, HINT, TextAnchor.MiddleCenter);
            }

            // browse button below the image area — small, pinned to bottom of img col
            float browseBtnH = BTN_H * 0.8f;
            UGUIShip.CreateButton(imgColGo.transform,
                new Rect(0f, ROW_H - PAD * 2f - browseBtnH, imgColW, browseBtnH),
                "Browse", new Color(0.22f, 0.32f, 0.42f, 1f), WHITE, FS_SM,
                new Action(() => WinDialogs.PickPng("Select phrase image", path =>
                {
                    if (string.IsNullOrEmpty(path)) return;
                    _entries[captured].imagePath = path;
                    _previewTextures.Remove(_entries[captured].id);
                    PhraseSettingsService.Save(_entries);
                    RefreshList();
                })));

            // ── Left column: controls ─────────────────────────────────────────
            float gap = PAD * 0.5f;
            float line1Y = gap;
            float toggleW = BTN_H * 2.2f;
            float textW = leftW - toggleW - PAD;

            UGUIShip.CreateButton(rowGo.transform,
                new Rect(PAD, line1Y, toggleW, RBTN_H),
                entry.enabled ? "ON" : "OFF",
                entry.enabled ? TOGGLE_ON : TOGGLE_OFF,
                WHITE, FS_SM,
                new Action(() => OnToggleEnabled(captured)));

            var tf = UGUIShip.CreateInputField(rowGo.transform,
                new Rect(PAD + toggleW + PAD, line1Y, textW, RBTN_H),
                "phrase text...", new Color(0f, 0f, 0f, 1f), WHITE, FS_SM);
            tf.text = entry.phraseText;
            tf.onEndEdit.AddListener(new Action<string>(val =>
            {
                if (captured < _entries.Count) _entries[captured].phraseText = val ?? "";
                PhraseSettingsService.Save(_entries);
            }));

            // line 2: slot stepper (−/value/+), then Edit + minus on the far right
            float line2Y = line1Y + RBTN_H + gap;
            float slotLblW = LH * 3.5f;
            float stepW = BTN_H;
            float slotValW = BTN_H * 1.2f;
            float editW = BTN_H * 2.2f;
            float minusW = BTN_H * 1.4f;

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD, line2Y, slotLblW, RBTN_H), "Slot (0-7)", FS_SM, HINT);

            float psx = PAD + slotLblW + PAD;
            UGUIShip.CreateIncrement(rowGo.transform,
                new Rect(psx, line2Y, stepW * 2f + slotValW, RBTN_H), 0, 7,
                () => _entries[captured].slot,
                v => { _entries[captured].slot = v; PhraseSettingsService.Save(_entries); RefreshList(); },
                fontSize: FS_SM);

            UGUIShip.CreateButton(rowGo.transform,
                new Rect(leftW - minusW - editW, line2Y, editW, RBTN_H),
                editing ? "Done" : "Edit",
                editing ? new Color(0.2f, 0.4f, 0.25f, 1f) : new Color(0.22f, 0.32f, 0.42f, 1f),
                WHITE, FS_SM,
                new Action(() =>
                {
                    _editingPhraseId = editing ? null : entry.id;
                    RefreshList();
                }));
            UGUIShip.CreateButton(rowGo.transform,
                new Rect(leftW - minusW + PAD, line2Y, minusW, RBTN_H),
                "−", BTN_RM, WHITE, FS,
                new Action(() => OnRemoveEntry(captured)));

            // edit mode: 3 sound rows
            if (editing)
                BuildSoundRows(rowGo.transform, entry.soundPaths, line2Y + RBTN_H + gap, leftW,
                    () => PhraseSettingsService.Save(_entries));
        }

        // ── Sub-tab ───────────────────────────────────────────────────────────

        // jump straight to the Emotes sub-tab (used by the Customization tab's "Copy" -> open flow)
        public void ShowEmotesSubTab() => SetSubTab(SubTab.Emotes);

        private void SetSubTab(SubTab sub)
        {
            _sub = sub;
            UGUIShip.SetButtonSelected(_btnSubPhrases, sub == SubTab.Phrases, SEL);
            UGUIShip.SetButtonSelected(_btnSubEmoticons, sub == SubTab.Emoticons, SEL);
            UGUIShip.SetButtonSelected(_btnSubEmotes, sub == SubTab.Emotes, SEL);
            RefreshSubTabVisibility();
        }

        private void RefreshSubTabVisibility()
        {
            bool phrases = _sub == SubTab.Phrases;
            bool emoticons = _sub == SubTab.Emoticons;
            bool emotes = _sub == SubTab.Emotes;
            if (_phrasesPanel != null) _phrasesPanel.SetActive(phrases);
            if (_phrasesBottomBar != null) _phrasesBottomBar.SetActive(phrases);
            if (_emoticonsPanel != null) _emoticonsPanel.SetActive(emoticons);
            if (_emoticonsBottomBar != null) _emoticonsBottomBar.SetActive(emoticons);
            if (_emotesPanel != null) _emotesPanel.SetActive(emotes);
            if (_emotesBottomBar != null) _emotesBottomBar.SetActive(emotes);
            // RefreshSubTabVisibility runs during BuildContent too (initial layout), so still
            // gate on _userOpenedOnce — but once you've opened the tab, every switch refreshes.
            if (_userOpenedOnce) { RefreshWheelSlots(); RefreshPrimeVisualizer(); }
        }

        // paint the 7 Image slots around the wheel for the current sub-tab. base layer is the
        // live game wheel (real phrase/emoticon/emote icons by slot), and any enabled custom
        // entry overlays its own image on its slot. slot 0 = top, clockwise.
        private void RefreshWheelSlots()
        {
            if (_wheelSlotImages == null) return;

            var slotSprite = new Sprite[8];

            // base: each wheel option is an ItemDefinitionSO whose icon is an addressable
            // AssetReferenceSprite at menuDisplaySpriteReference. resolve fresh every refresh
            // (Asset is set when the game has already loaded it; otherwise kick an async load
            // that fills the slot in when it completes).
            try
            {
                var sel = GlobalGameStateClient.Instance?._playerProfile?.CustomisationSelections;
                if (sel != null)
                {
                    var list = _sub == SubTab.Phrases ? sel.SecondWheelOptions : sel.FirstWheelOptions;
                    if (list != null)
                    {
                        int n = Mathf.Min(8, list.Length);
                        for (int i = 0; i < n; i++)
                        {
                            var item = list[i];
                            if (item == null) continue;
                            var refSp = item.menuDisplaySpriteReference;
                            if (refSp == null) continue;
                            var already = refSp.Asset;
                            if (already != null)
                            {
                                slotSprite[i] = already.TryCast<Sprite>();
                                continue;
                            }
                            int slotI = i;
                            try
                            {
                                var op = refSp.LoadAssetAsync<Sprite>();
                                StartCoroutine(WaitForSlotSprite(op, slotI).WrapToIl2Cpp());
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }

            // overlay: enabled custom entries from the current sub-tab
            switch (_sub)
            {
                case SubTab.Phrases:
                    foreach (var e in _entries)
                    {
                        if (!e.enabled) continue;
                        int s = Mathf.Clamp(e.slot, 0, 7);
                        var sp = ToSlotSprite(LoadPreview(e));
                        if (sp != null) slotSprite[s] = sp;
                    }
                    break;
                case SubTab.Emoticons:
                case SubTab.Emotes:
                    // emoticons + emotes live on the same physical wheel, so show both sets in
                    // either sub-tab. emotes paint last so they win a slot collision (matches what
                    // EmoteInjectionService does — it overwrites the icon on its slot).
                    foreach (var e in _emoticonEntries)
                    {
                        if (!e.enabled) continue;
                        int s = Mathf.Clamp(e.slot, 0, 7);
                        var sp = ToSlotSprite(LoadEmoticonPreview(e));
                        if (sp != null) slotSprite[s] = sp;
                    }
                    foreach (var e in _emoteEntries)
                    {
                        if (!e.enabled) continue;
                        int s = Mathf.Clamp(e.slot, 0, 7);
                        var sp = ToSlotSprite(LoadEmotePreview(e));
                        if (sp != null) slotSprite[s] = sp;
                    }
                    break;
            }

            for (int i = 0; i < 8; i++)
            {
                var img = _wheelSlotImages[i];
                if (img == null) continue;
                var sp = slotSprite[i];
                img.sprite = sp;
                img.color = sp != null ? Color.white : new Color(1f, 1f, 1f, 0f);
            }
        }

        private IEnumerator WaitForSlotSprite(UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<Sprite> op, int slotI)
        {
            while (!op.IsDone) yield return null;
            if (_wheelSlotImages == null || slotI >= _wheelSlotImages.Length) yield break;
            var sp = op.Result;
            if (sp == null) yield break;
            var img = _wheelSlotImages[slotI];
            if (img != null && img.sprite == null)
            {
                img.sprite = sp;
                img.color = Color.white;
            }
        }

        private Sprite ToSlotSprite(Texture2D tex)
        {
            if (tex == null) return null;
            if (_slotSpriteCache.TryGetValue(tex, out var cached) && cached != null) return cached;
            var sp = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sp.hideFlags = HideFlags.HideAndDontSave;
            _slotSpriteCache[tex] = sp;
            return sp;
        }

        // ── Handlers ─────────────────────────────────────────────────────────

        private void OnAddPhrase()
        {
            _entries.Add(new PhraseEntry());
            PhraseSettingsService.Save(_entries);
            RefreshList();
        }

        private void OnToggleEnabled(int index)
        {
            if (index < 0 || index >= _entries.Count) return;
            _entries[index].enabled = !_entries[index].enabled;
            PhraseSettingsService.Save(_entries);
            RefreshList();
        }

        private void OnRemoveEntry(int index)
        {
            if (index < 0 || index >= _entries.Count) return;
            _entries.RemoveAt(index);
            PhraseSettingsService.Save(_entries);
            RefreshList();
        }

        private void OnApplyAll()
        {
            PhraseInjectionService.ApplyAll(_entries);
            SetStatus(PhraseInjectionService.LastStatus);
        }

        private void SetStatus(string msg)
        {
            if (_statusLabel != null) _statusLabel.text = msg;
        }

        // emote and emoticon injection both target the EmotesAndEmoticons wheel. two ways they
        // step on each other:
        //  1) two enabled custom entries on the same slot (emote paints last, wins)
        //  2) the underlying game slot's type doesn't match what you're trying to put on it —
        //     e.g. putting an emoticon on a slot that natively holds an EmotesOption. the
        //     injection still runs but the runtime treats the slot as its native type and one
        //     side gets ignored. we probe FirstWheelOptions[slot] to catch this even when the
        //     conflicting custom entry is disabled.
        private bool HasEmoteOnSlot(int slot)
        {
            foreach (var e in _emoteEntries) if (e.enabled && e.slot == slot) return true;
            return NativeSlotIsEmote(slot) == true;
        }
        private bool HasEmoticonOnSlot(int slot)
        {
            foreach (var e in _emoticonEntries) if (e.enabled && e.slot == slot) return true;
            return NativeSlotIsEmote(slot) == false;
        }
        // null = unknown (wheel not populated yet). true = slot holds an EmotesOption natively,
        // false = slot holds anything else (emoticon-style option).
        private bool? NativeSlotIsEmote(int slot)
        {
            if (!_userOpenedOnce) return null;
            try
            {
                var sel = GlobalGameStateClient.Instance?._playerProfile?.CustomisationSelections;
                var list = sel?.FirstWheelOptions;
                if (list == null || slot < 0 || slot >= list.Length) return null;
                var item = list[slot];
                if (item == null) return null;
                return item.TryCast<EmotesOption>() != null;
            }
            catch { return null; }
        }

        // ── Emoticons scroll area ─────────────────────────────────────────────

        private void BuildEmoticonsScrollArea(RectTransform root)
        {
            var scroll = UGUIShip.CreateScrollView(root, new Rect(PAD, 0f, root.rect.width - PAD * 2f, root.rect.height));
            var sv = scroll.scrollRect;
            sv.scrollSensitivity = 60f;
            _emoticonScrollContent = scroll.content;
            _emoticonScrollContent.pivot = new Vector2(0.5f, 1f);
            _emoticonScrollContent.sizeDelta = Vector2.zero;

            var vlg = _emoticonScrollContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = PAD;
            vlg.padding = new RectOffset(0, 0, (int)PAD, (int)PAD);
            _emoticonScrollContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RefreshEmoticonList();
        }

        private void RefreshEmoticonList()
        {
            if (_emoticonScrollContent == null) return;

            for (int i = _emoticonScrollContent.childCount - 1; i >= 0; i--)
            {
                var c = _emoticonScrollContent.GetChild(i);
                if (c != null) Destroy(c.gameObject);
            }

            for (int i = 0; i < _emoticonEntries.Count; i++)
                CreateEmoticonRow(_emoticonEntries[i], i);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_emoticonScrollContent);
            if (_userOpenedOnce) { RefreshWheelSlots(); RefreshPrimeVisualizer(); }
        }

        private void CreateEmoticonRow(EmoticonEntry entry, int index)
        {
            int captured = index;
            bool editing = _editingEmoticonId == entry.id;
            float rowH = editing ? ROW_H + (RBTN_H + PAD * 0.5f) * 3f + PAD : ROW_H;

            var rowGo = new GameObject("EmoticonRow_" + entry.id);
            rowGo.transform.SetParent(_emoticonScrollContent, false);
            rowGo.AddComponent<RectTransform>();

            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = rowH;
            le.flexibleWidth = 1f;

            float imgColW = ROW_H;
            float leftW = TabWidth - PAD * 4f - imgColW - PAD;

            // ── Image column ──────────────────────────────────────────────────
            // pinned top-right as a fixed square so it doesn't stretch when the row expands
            var imgColGo = new GameObject("ImgCol");
            imgColGo.transform.SetParent(rowGo.transform, false);
            var imgColRt = imgColGo.AddComponent<RectTransform>();
            imgColRt.anchorMin = new Vector2(1f, 1f);
            imgColRt.anchorMax = new Vector2(1f, 1f);
            imgColRt.pivot = new Vector2(1f, 1f);
            imgColRt.sizeDelta = new Vector2(imgColW, ROW_H - PAD * 2f);
            imgColRt.anchoredPosition = new Vector2(-PAD, -PAD);

            imgColGo.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 1f);

            Texture2D preview = LoadEmoticonPreview(entry);
            if (preview != null)
            {
                var imgGo = new GameObject("Preview");
                imgGo.transform.SetParent(imgColGo.transform, false);
                var imgRt = imgGo.AddComponent<RectTransform>();
                imgRt.anchorMin = Vector2.zero;
                imgRt.anchorMax = Vector2.one;
                imgRt.offsetMin = imgRt.offsetMax = Vector2.zero;
                var rawImg = imgGo.AddComponent<RawImage>();
                rawImg.texture = preview;
                float aspect = (float)preview.width / preview.height;
                if (aspect >= 1f)
                {
                    float scaledW = imgColW * aspect;
                    imgRt.offsetMin = new Vector2((imgColW - scaledW) * 0.5f, 0f);
                    imgRt.offsetMax = new Vector2(-(imgColW - scaledW) * 0.5f, 0f);
                }
            }
            else
            {
                UGUIShip.CreateLabel(imgColGo.transform,
                    new Rect(0f, 0f, imgColW, ROW_H - PAD * 2f),
                    "No img", FS_SM, HINT, TextAnchor.MiddleCenter);
            }

            float browseBtnH = BTN_H * 0.8f;
            UGUIShip.CreateButton(imgColGo.transform,
                new Rect(0f, ROW_H - PAD * 2f - browseBtnH, imgColW, browseBtnH),
                "Browse", new Color(0.22f, 0.32f, 0.42f, 1f), WHITE, FS_SM,
                new Action(() => WinDialogs.PickPng("Select emoticon image", path =>
                {
                    if (string.IsNullOrEmpty(path)) return;
                    _emoticonEntries[captured].imagePath = path;
                    _emoticonPreviewTextures.Remove(_emoticonEntries[captured].id);
                    EmoticonSettingsService.Save(_emoticonEntries);
                    RefreshEmoticonList();
                })));

            // ── Left column ───────────────────────────────────────────────────
            float gap = PAD * 0.5f;
            float line1Y = gap;
            float toggleW = BTN_H * 2.2f;
            float itemIdW = leftW - toggleW - PAD;

            UGUIShip.CreateButton(rowGo.transform,
                new Rect(PAD, line1Y, toggleW, RBTN_H),
                entry.enabled ? "ON" : "OFF",
                entry.enabled ? TOGGLE_ON : TOGGLE_OFF,
                WHITE, FS_SM,
                new Action(() => OnToggleEmoticonEnabled(captured)));

            var tf = UGUIShip.CreateInputField(rowGo.transform,
                new Rect(PAD + toggleW + PAD, line1Y, itemIdW, RBTN_H),
                "item id (e.g. emoticon_wheel_happy_heart)",
                new Color(0f, 0f, 0f, 1f), WHITE, FS_SM);
            tf.text = entry.itemId;
            tf.onEndEdit.AddListener(new Action<string>(val =>
            {
                if (captured < _emoticonEntries.Count) _emoticonEntries[captured].itemId = val ?? "";
                EmoticonSettingsService.Save(_emoticonEntries);
            }));

            // line 2: slot stepper (−/value/+), then Edit + minus on the far right
            float line2Y = line1Y + RBTN_H + gap;
            float slotLblW = LH * 3.5f;
            float stepW = BTN_H;
            float slotValW = BTN_H * 1.2f;
            float editW = BTN_H * 2.2f;
            float minusW = BTN_H * 1.4f;

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD, line2Y, slotLblW, RBTN_H), "Slot (0-7)", FS_SM, HINT);

            float esx = PAD + slotLblW + PAD;
            UGUIShip.CreateIncrement(rowGo.transform,
                new Rect(esx, line2Y, stepW * 2f + slotValW, RBTN_H), 0, 7,
                () => _emoticonEntries[captured].slot,
                v => { _emoticonEntries[captured].slot = v; EmoticonSettingsService.Save(_emoticonEntries); RefreshEmoticonList(); RefreshEmoteList(); },
                fontSize: FS_SM);

            UGUIShip.CreateButton(rowGo.transform,
                new Rect(leftW - minusW - editW, line2Y, editW, RBTN_H),
                editing ? "Done" : "Edit",
                editing ? new Color(0.2f, 0.4f, 0.25f, 1f) : new Color(0.22f, 0.32f, 0.42f, 1f),
                WHITE, FS_SM,
                new Action(() =>
                {
                    _editingEmoticonId = editing ? null : entry.id;
                    RefreshEmoticonList();
                }));
            UGUIShip.CreateButton(rowGo.transform,
                new Rect(leftW - minusW + PAD, line2Y, minusW, RBTN_H),
                "−", BTN_RM, WHITE, FS,
                new Action(() => OnRemoveEmoticonEntry(captured)));

            // collision warning — emote injection paints over the emoticon's slot icon and clip
            // override is silent in-game. surface it here so the user knows the row won't show up.
            if (entry.enabled && HasEmoteOnSlot(entry.slot))
            {
                float warnY = line2Y + RBTN_H + 1f;
                UGUIShip.CreateLabel(rowGo.transform,
                    new Rect(PAD, warnY, leftW - PAD * 2f, RBTN_H * 0.6f),
                    "Emote overrides this slot", FS_SM, WARN_C, TextAnchor.UpperLeft);
                _ = "emote-override warn";
            }

            // edit mode: 3 sound rows
            if (editing)
                BuildSoundRows(rowGo.transform, entry.soundPaths, line2Y + RBTN_H + gap, leftW,
                    () => EmoticonSettingsService.Save(_emoticonEntries));
        }

        // ── Emoticon handlers ─────────────────────────────────────────────────

        private void OnAddEmoticon()
        {
            _emoticonEntries.Add(new EmoticonEntry());
            EmoticonSettingsService.Save(_emoticonEntries);
            RefreshEmoticonList();
        }

        private void OnToggleEmoticonEnabled(int index)
        {
            if (index < 0 || index >= _emoticonEntries.Count) return;
            _emoticonEntries[index].enabled = !_emoticonEntries[index].enabled;
            EmoticonSettingsService.Save(_emoticonEntries);
            RefreshEmoticonList();
            RefreshEmoteList();
        }

        private void OnRemoveEmoticonEntry(int index)
        {
            if (index < 0 || index >= _emoticonEntries.Count) return;
            _emoticonEntries.RemoveAt(index);
            EmoticonSettingsService.Save(_emoticonEntries);
            RefreshEmoticonList();
            RefreshEmoteList();
        }

        private void OnApplyAllEmoticons()
        {
            EmoticonInjectionService.ApplyAll(_emoticonEntries);
            if (_emoticonStatusLabel != null)
                _emoticonStatusLabel.text = EmoticonInjectionService.LastStatus;
            // also push to the shared status label so user sees it regardless of sub-tab
            SetStatus(EmoticonInjectionService.LastStatus);
        }

        // ── Emotes scroll area ────────────────────────────────────────────────

        private void BuildEmotesScrollArea(RectTransform root)
        {
            var scroll = UGUIShip.CreateScrollView(root, new Rect(PAD, 0f, root.rect.width - PAD * 2f, root.rect.height));
            var sv = scroll.scrollRect;
            sv.scrollSensitivity = 60f;
            _emoteScrollContent = scroll.content;
            _emoteScrollContent.pivot = new Vector2(0.5f, 1f);
            _emoteScrollContent.sizeDelta = Vector2.zero;

            var vlg = _emoteScrollContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = PAD;
            vlg.padding = new RectOffset(0, 0, (int)PAD, (int)PAD);
            _emoteScrollContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            RefreshEmoteList();
        }

        private void RefreshEmoteList()
        {
            if (_emoteScrollContent == null) return;

            for (int i = _emoteScrollContent.childCount - 1; i >= 0; i--)
            {
                var c = _emoteScrollContent.GetChild(i);
                if (c != null) Destroy(c.gameObject);
            }

            for (int i = 0; i < _emoteEntries.Count; i++)
                CreateEmoteRow(_emoteEntries[i], i);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_emoteScrollContent);
            if (_userOpenedOnce) { RefreshWheelSlots(); RefreshPrimeVisualizer(); }
        }

        // "Paste" lives in the Emotes bottom bar (beside + Add Emote / Apply All). it fills in a
        // whole emote (bundle, sound, wheel image) from whatever was Copy'd in the Customization tab's
        // Emotes filter, and updates live when a new emote is copied (see Update + _seenClipboardVersion).
        private bool _pasting;
        private Text _pasteLabel;
        private RawImage _pasteCover; // copied emote's cover, cropped to fill the button at 10% opacity
        private RectTransform _pasteCoverRt;
        private RectTransform _pasteBtnRt;
        private int _seenClipboardVersion = -1;

        // the paste button's RectTransform, so the highlight arrow can point at it after switching tabs
        public RectTransform PasteButtonRect => _pasteBtnRt;

        private void BuildPasteButton(Transform parent, Rect rect)
        {
            var btn = UGUIShip.CreateButton(parent, rect, "", DARK, WHITE, FS, new Action(OnPasteEmote));
            _pasteLabel = btn.transform.Find("Label")?.GetComponent<Text>();
            _pasteBtnRt = btn.gameObject.GetComponent<RectTransform>();

            // cover fills the button (cropped, masked to its bounds) behind the label
            var btnGo = btn.gameObject;
            if (btnGo.GetComponent<RectMask2D>() == null) btnGo.AddComponent<RectMask2D>();

            var coverGo = new GameObject("PasteCover");
            coverGo.transform.SetParent(btn.transform, false);
            coverGo.transform.SetAsFirstSibling();
            _pasteCoverRt = coverGo.AddComponent<RectTransform>();
            _pasteCoverRt.anchorMin = Vector2.zero;
            _pasteCoverRt.anchorMax = Vector2.one;
            _pasteCoverRt.offsetMin = _pasteCoverRt.offsetMax = Vector2.zero;
            _pasteCover = coverGo.AddComponent<RawImage>();
            _pasteCover.raycastTarget = false;

            _seenClipboardVersion = EmoteClipboard.Version;
            UpdatePasteRow();
        }

        // repaint the paste button's label + cropped cover to match what's currently on the clipboard
        private void UpdatePasteRow()
        {
            if (_pasteLabel == null) return;
            bool has = EmoteClipboard.HasEmote;
            _pasteLabel.text = _pasting ? "Pasting..." : "Paste";
            _pasteLabel.color = has || _pasting ? WHITE : HINT;

            if (_pasteCover == null) return;
            if (has && EmoteClipboard.Cover != null)
            {
                var tex = EmoteClipboard.Cover;
                _pasteCover.texture = tex;
                _pasteCover.color = new Color(1f, 1f, 1f, 0.1f); // 10% opacity

                // cover-fit via uvRect: crop the texture's longer side so it fills the button
                // bounds with no empty space and no stretching.
                float btnAspect = _pasteCoverRt.rect.width / Mathf.Max(1f, _pasteCoverRt.rect.height);
                float texAspect = (float)tex.width / Mathf.Max(1, tex.height);
                if (texAspect > btnAspect)
                {
                    float wUv = btnAspect / texAspect; // show a centered horizontal slice
                    _pasteCover.uvRect = new Rect((1f - wUv) * 0.5f, 0f, wUv, 1f);
                }
                else
                {
                    float hUv = texAspect / btnAspect; // show a centered vertical slice
                    _pasteCover.uvRect = new Rect(0f, (1f - hUv) * 0.5f, 1f, hUv);
                }
            }
            else
            {
                _pasteCover.texture = null;
                _pasteCover.color = new Color(1f, 1f, 1f, 0f);
            }
        }

        private void OnPasteEmote()
        {
            if (_pasting) return;
            if (!EmoteClipboard.HasEmote)
            {
                SetStatus("Nothing copied. Go to Customization > Emotes and press Copy first.");
                return;
            }
            _pasting = true;
            if (_pasteLabel != null) _pasteLabel.text = "Pasting...";
            StartCoroutine(PasteEmoteCoroutine().WrapToIl2Cpp());
        }

        // download the copied emote's bundle (+ sound + cover) into Settings/PastedEmotes/<name>,
        // then add an EmoteEntry pointing at the local files and apply.
        private IEnumerator PasteEmoteCoroutine()
        {
            string name = EmoteClipboard.Name;
            string bundleUrl = EmoteClipboard.BundleUrl;
            string soundUrl = EmoteClipboard.SoundUrl;
            string coverUrl = EmoteClipboard.CoverUrl;
            string audioName = EmoteClipboard.AudioFileName;

            string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            string safe = MakeSafeFolderName(string.IsNullOrEmpty(name) ? "emote" : name);
            string dest = Path.Combine(Path.Combine(dllDir, "Settings", "PastedEmotes"), safe);
            try { Directory.CreateDirectory(dest); } catch { }

            // cache-buster appended to every download so an updated emote doesn't get served stale from
            // the github/CDN cache (without this the on-disk bundle/sound never changes on re-paste)
            string cb = "cb=" + DateTime.UtcNow.Ticks;

            // bundle (required)
            string bundleName = Uri.UnescapeDataString(Path.GetFileName(new Uri(bundleUrl).AbsolutePath));
            if (string.IsNullOrEmpty(bundleName)) bundleName = safe;
            string bundlePath = Path.Combine(dest, bundleName);
            SetStatus("Downloading emote bundle...");
            var bReq = UnityWebRequest.Get(bundleUrl + (bundleUrl.Contains("?") ? "&" : "?") + cb);
            yield return bReq.SendWebRequest();
            if (bReq.result != UnityWebRequest.Result.Success)
            {
                SetStatus("Emote bundle download failed: " + bReq.error);
                bReq.Dispose();
                FinishPaste();
                yield break;
            }
            try { File.WriteAllBytes(bundlePath, bReq.downloadHandler.data); }
            catch (Exception ex) { SetStatus("Couldn't save bundle: " + ex.Message); bReq.Dispose(); FinishPaste(); yield break; }
            bReq.Dispose();
            // drop any already-loaded copy of this bundle so the fresh file's clip is what loads
            EmoteInjectionService.EvictBundle(bundlePath);

            // sound (optional)
            string soundPath = "";
            if (!string.IsNullOrEmpty(soundUrl))
            {
                string soundFile = !string.IsNullOrEmpty(audioName) ? audioName
                    : Uri.UnescapeDataString(Path.GetFileName(new Uri(soundUrl).AbsolutePath));
                var sReq = UnityWebRequest.Get(soundUrl + (soundUrl.Contains("?") ? "&" : "?") + cb);
                yield return sReq.SendWebRequest();
                if (sReq.result == UnityWebRequest.Result.Success)
                {
                    try { File.WriteAllBytes(Path.Combine(dest, soundFile), sReq.downloadHandler.data); soundPath = Path.Combine(dest, soundFile); }
                    catch { }
                }
                sReq.Dispose();
            }

            // cover -> wheel image (optional, try jpg then png)
            string imagePath = "";
            foreach (string url in new[] { coverUrl, coverUrl.Replace(".jpg", ".png") })
            {
                if (string.IsNullOrEmpty(url)) continue;
                var cReq = UnityWebRequest.Get(url + (url.Contains("?") ? "&" : "?") + cb);
                yield return cReq.SendWebRequest();
                if (cReq.result == UnityWebRequest.Result.Success)
                {
                    string coverFile = "cover" + Path.GetExtension(url);
                    try { File.WriteAllBytes(Path.Combine(dest, coverFile), cReq.downloadHandler.data); imagePath = Path.Combine(dest, coverFile); }
                    catch { }
                    cReq.Dispose();
                    break;
                }
                cReq.Dispose();
            }

            var entry = new EmoteEntry
            {
                bundlePath = bundlePath,
                clipName = "", // always first clip in the bundle
                imagePath = imagePath,
                soundPath = soundPath,
                slot = 7,
                enabled = true,
            };
            _emoteEntries.Add(entry);
            EmoteSettingsService.Save(_emoteEntries);
            EmoteInjectionService.ApplyAll(_emoteEntries);
            SetStatus($"Pasted {name}. Set its slot, then it's ready in the wheel.");
            FinishPaste();
        }

        private void FinishPaste()
        {
            _pasting = false;
            UpdatePasteRow();
            RefreshEmoteList();
        }

        private static string MakeSafeFolderName(string s)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in s.Trim())
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            string r = sb.ToString();
            return string.IsNullOrEmpty(r) ? "emote" : r;
        }

        // which row is expanded into edit mode (by entry id). null = all collapsed.
        private string _editingEmoteId;

        private void CreateEmoteRow(EmoteEntry entry, int index)
        {
            int captured = index;
            bool editing = _editingEmoteId == entry.id;

            // collapsed shows only pic + name + slot; edit mode adds bundle + sound + image rows
            float rowH = editing ? ROW_H + RBTN_H * 2f + PAD * 2f : ROW_H;

            var rowGo = new GameObject("EmoteRow_" + entry.id);
            rowGo.transform.SetParent(_emoteScrollContent, false);
            rowGo.AddComponent<RectTransform>();

            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = rowH;
            le.flexibleWidth = 1f;

            float imgColW = ROW_H;
            float leftW = TabWidth - PAD * 4f - imgColW - PAD;

            // ── Image column (pic) ────────────────────────────────────────────
            var imgColGo = new GameObject("ImgCol");
            imgColGo.transform.SetParent(rowGo.transform, false);
            var imgColRt = imgColGo.AddComponent<RectTransform>();
            imgColRt.anchorMin = new Vector2(1f, 1f);
            imgColRt.anchorMax = new Vector2(1f, 1f);
            imgColRt.pivot = new Vector2(1f, 1f);
            imgColRt.sizeDelta = new Vector2(imgColW, imgColW);
            imgColRt.anchoredPosition = new Vector2(-PAD, -PAD);

            imgColGo.AddComponent<Image>().color = new Color(0.08f, 0.08f, 0.08f, 1f);

            Texture2D preview = LoadEmotePreview(entry);
            if (preview != null)
            {
                var imgGo = new GameObject("Preview");
                imgGo.transform.SetParent(imgColGo.transform, false);
                var imgRt = imgGo.AddComponent<RectTransform>();
                imgRt.anchorMin = Vector2.zero;
                imgRt.anchorMax = Vector2.one;
                imgRt.offsetMin = imgRt.offsetMax = Vector2.zero;
                var rawImg = imgGo.AddComponent<RawImage>();
                rawImg.texture = preview;
                float aspect = (float)preview.width / preview.height;
                if (aspect >= 1f)
                {
                    float scaledW = imgColW * aspect;
                    imgRt.offsetMin = new Vector2((imgColW - scaledW) * 0.5f, 0f);
                    imgRt.offsetMax = new Vector2(-(imgColW - scaledW) * 0.5f, 0f);
                }
            }
            else
            {
                UGUIShip.CreateLabel(imgColGo.transform,
                    new Rect(0f, 0f, imgColW, imgColW),
                    "No img", FS_SM, HINT, TextAnchor.MiddleCenter);
            }

            // ── Top line: ON/OFF + name + Edit ────────────────────────────────
            float gap = PAD * 0.5f;
            float line1Y = gap;
            float toggleW = BTN_H * 2.2f;
            float editW = BTN_H * 2.2f;
            float minusW = BTN_H * 1.4f;
            float nameW = leftW - toggleW - editW - PAD * 2f;

            UGUIShip.CreateButton(rowGo.transform,
                new Rect(PAD, line1Y, toggleW, RBTN_H),
                entry.enabled ? "ON" : "OFF",
                entry.enabled ? TOGGLE_ON : TOGGLE_OFF,
                WHITE, FS_SM,
                new Action(() => OnToggleEmoteEnabled(captured)));

            // name: derived from the bundle file (read-only label, no typing)
            string displayName = !string.IsNullOrEmpty(entry.bundlePath)
                ? Path.GetFileNameWithoutExtension(entry.bundlePath) : "(empty emote)";
            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD + toggleW + PAD, line1Y, nameW, RBTN_H), displayName, FS_SM, WHITE);

            UGUIShip.CreateButton(rowGo.transform,
                new Rect(PAD + toggleW + PAD + nameW + PAD, line1Y, editW, RBTN_H),
                editing ? "Done" : "Edit",
                editing ? new Color(0.2f, 0.4f, 0.25f, 1f) : new Color(0.22f, 0.32f, 0.42f, 1f),
                WHITE, FS_SM,
                new Action(() =>
                {
                    _editingEmoteId = editing ? null : entry.id;
                    RefreshEmoteList();
                }));

            // ── Slot line: label + −/value/+ stepper, then minus on the far right ──
            float line2Y = line1Y + RBTN_H + gap;
            float slotLblW = LH * 3.5f;
            float stepW = BTN_H;
            float slotValW = BTN_H * 1.2f;

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD, line2Y, slotLblW, RBTN_H), "Slot (0-7)", FS_SM, HINT);

            float sx = PAD + slotLblW + PAD;
            UGUIShip.CreateIncrement(rowGo.transform,
                new Rect(sx, line2Y, stepW * 2f + slotValW, RBTN_H), 0, 7,
                () => _emoteEntries[captured].slot,
                v => { _emoteEntries[captured].slot = v; EmoteSettingsService.Save(_emoteEntries); RefreshEmoteList(); RefreshEmoticonList(); },
                fontSize: FS_SM);

            UGUIShip.CreateButton(rowGo.transform,
                new Rect(leftW - minusW + PAD, line2Y, minusW, RBTN_H),
                "−", BTN_RM, WHITE, FS,
                new Action(() => OnRemoveEmoteEntry(captured)));

            // collision warning — when an emoticon is also on this slot, the emote wins (we paint
            // the emote sprite/clip over the emoticon's), so tell the user the emoticon won't fire
            if (entry.enabled && HasEmoticonOnSlot(entry.slot))
            {
                float warnY = line2Y + RBTN_H + 1f;
                UGUIShip.CreateLabel(rowGo.transform,
                    new Rect(PAD, warnY, leftW - PAD * 2f, RBTN_H * 0.6f),
                    "Overrides emoticon on this slot", FS_SM, WARN_C, TextAnchor.UpperLeft);
            }

            if (!editing) return;

            // ── Edit mode: bundle row ─────────────────────────────────────────
            float btnW = BTN_H * 2.6f;
            float line3Y = line2Y + RBTN_H + gap;
            UGUIShip.CreateButton(rowGo.transform,
                new Rect(PAD, line3Y, btnW, RBTN_H), "Bundle",
                new Color(0.22f, 0.32f, 0.42f, 1f), WHITE, FS_SM,
                new Action(() => WinDialogs.PickFile("Select emote AssetBundle", path =>
                {
                    if (string.IsNullOrEmpty(path)) return;
                    _emoteEntries[captured].bundlePath = path;
                    EmoteSettingsService.Save(_emoteEntries);
                    RefreshEmoteList();
                })));
            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD + btnW + PAD, line3Y, leftW - btnW - PAD, RBTN_H),
                string.IsNullOrEmpty(entry.bundlePath) ? "No bundle" : Path.GetFileName(entry.bundlePath),
                FS_SM, HINT);

            // image row
            float line4Y = line3Y + RBTN_H + gap;
            UGUIShip.CreateButton(rowGo.transform,
                new Rect(PAD, line4Y, btnW, RBTN_H), "Image",
                new Color(0.22f, 0.32f, 0.42f, 1f), WHITE, FS_SM,
                new Action(() => WinDialogs.PickPng("Select emote image", path =>
                {
                    if (string.IsNullOrEmpty(path)) return;
                    _emoteEntries[captured].imagePath = path;
                    _emotePreviewTextures.Remove(_emoteEntries[captured].id);
                    EmoteSettingsService.Save(_emoteEntries);
                    RefreshEmoteList();
                })));
            // sound button sits right after the image button, with the sound filename beside it
            float soundBtnX = PAD + btnW + PAD;
            UGUIShip.CreateButton(rowGo.transform,
                new Rect(soundBtnX, line4Y, btnW, RBTN_H), "Sound",
                new Color(0.22f, 0.32f, 0.42f, 1f), WHITE, FS_SM,
                new Action(() =>
                {
                    _emoteEntries[captured].soundPath = "";
                    EmoteSettingsService.Save(_emoteEntries);
                    WinDialogs.PickAudio("Select emote sound", path =>
                    {
                        if (string.IsNullOrEmpty(path)) return;
                        _emoteEntries[captured].soundPath = path;
                        EmoteSettingsService.Save(_emoteEntries);
                        RefreshEmoteList();
                    });
                }));
            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(soundBtnX + btnW + PAD, line4Y, leftW - (soundBtnX - PAD) - btnW - PAD, RBTN_H),
                string.IsNullOrEmpty(entry.soundPath) ? "No sound" : Path.GetFileName(entry.soundPath),
                FS_SM, HINT);
        }

        // ── Emote handlers ────────────────────────────────────────────────────

        private void OnAddEmote()
        {
            _emoteEntries.Add(new EmoteEntry());
            EmoteSettingsService.Save(_emoteEntries);
            RefreshEmoteList();
        }

        private void OnToggleEmoteEnabled(int index)
        {
            if (index < 0 || index >= _emoteEntries.Count) return;
            _emoteEntries[index].enabled = !_emoteEntries[index].enabled;
            EmoteSettingsService.Save(_emoteEntries);
            RefreshEmoteList();
            RefreshEmoticonList();
        }

        private void OnRemoveEmoteEntry(int index)
        {
            if (index < 0 || index >= _emoteEntries.Count) return;
            _emoteEntries.RemoveAt(index);
            EmoteSettingsService.Save(_emoteEntries);
            RefreshEmoteList();
            RefreshEmoticonList();
        }

        private void OnApplyAllEmotes()
        {
            EmoteInjectionService.ApplyAll(_emoteEntries);
            SetStatus(EmoteInjectionService.LastStatus);
        }

        private Texture2D LoadEmotePreview(EmoteEntry e)
        {
            if (string.IsNullOrEmpty(e.imagePath) || !File.Exists(e.imagePath)) return null;
            if (_emotePreviewTextures.TryGetValue(e.id, out var cached)) return cached;
            try
            {
                byte[] data = File.ReadAllBytes(e.imagePath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(data);
                tex.Apply();
                tex.hideFlags = HideFlags.HideAndDontSave;
                _emotePreviewTextures[e.id] = tex;
                return tex;
            }
            catch { return null; }
        }

    }
}
