using System;
using System.Collections;
using System.Collections.Generic;
using BetterFG.Services;
using BetterFG.Utilities;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using BetterFG.UI.SideWheel;
using UnityEngine.UI;
using BetterFG.Core;
using TMPro;

namespace BetterFG.UI
{
    // ── Tooltip component ─────────────────────────────────────────────────────
    public class Tooltip : MonoBehaviour
    {
        public Tooltip(IntPtr ptr) : base(ptr) { }

        public static float HoverDelay = 0.7f;

        private Text _label;
        private CanvasGroup _cg;
        private RectTransform _rt;

        public void Awake()
        {
            _rt = GetComponent<RectTransform>();
            _cg = GetComponent<CanvasGroup>();
        }

        // max tooltip width before text wraps to a new line. shorter text yields a narrower tooltip;
        // longer text gets capped here and flows into 2-3 lines.
        const float MaxWidth = 260f;
        const float PadX = 16f; // 8 left + 8 right (matches the label inset in BuildTooltipWidget)
        const float PadY = 10f; // 5 top + 5 bottom

        public void SetText(string text)
        {
            if (_label == null) _label = GetComponentInChildren<Text>();
            if (_label == null) return;
            _label.text = text;

            // size the tooltip to its content: width = text width capped at MaxWidth, height grows
            // to fit wrapped lines. without this the tooltip would either stretch the whole screen
            // (no cap) or always be MaxWidth (LayoutGroup forcing it). set width on the tooltip
            // rect FIRST so the stretch-anchored label inherits it, then read preferredHeight which
            // now reflects the wrapped line count.
            float textW = Mathf.Min(MaxWidth - PadX, _label.preferredWidth);
            if (_rt != null) _rt.sizeDelta = new Vector2(textW + PadX, _rt.sizeDelta.y);
            float textH = _label.preferredHeight;
            if (_rt != null) _rt.sizeDelta = new Vector2(textW + PadX, textH + PadY);
        }

        private bool _fixed;
        private Vector2 _fixedPos;

        public void SetFixed(bool isFixed, Vector2 canvasPos)
        {
            _fixed = isFixed;
            _fixedPos = canvasPos;
            if (isFixed && _rt != null)
            {
                _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
                _rt.pivot = new Vector2(0.5f, 0.5f);
                _rt.anchoredPosition = canvasPos;
            }
            else if (!isFixed && _rt != null)
            {
                _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
                _rt.pivot = new Vector2(0f, 0f);
            }
        }

        public void SetVisible(bool v)
        {
            if (_cg == null) return;
            _cg.alpha = v ? 1f : 0f;
            _cg.blocksRaycasts = false;
        }

        public bool IsShown => _cg != null && _cg.alpha > 0f;

        // height after SetText has sized it, so callers pinning it to something can clear that thing
        public float Height => _rt != null ? _rt.sizeDelta.y : 0f;

        private RectTransform _canvasRt;

        public void FollowMouse(Canvas canvas)
        {
            if (_fixed) return;
            if (_rt == null) return;
            if (!BetterFGUIMan.UnityMouseReady()) return;
            if (_canvasRt == null) _canvasRt = canvas.GetComponent<RectTransform>();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvasRt, Input.mousePosition, canvas.worldCamera, out var local);
            _rt.anchoredPosition = local + new Vector2(10f, 18f);
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class TabAutoAttribute : Attribute
    {
        public int Order;
        public TabAutoAttribute(int order = 0) { Order = order; }
    }

    // ── Tab registry ──────────────────────────────────────────────────────────
    public static class BetterFGTabRegistry
    {
        public struct TabEntry
        {
            public string Title;
            public Func<BetterFGTab> Factory;
        }

        private static readonly List<TabEntry> _entries = new List<TabEntry>();
        public static IReadOnlyList<TabEntry> All => _entries;

        // a tab's identity and its display name are both just its own TabTitle — the registry never
        // takes a separate string, so the two can never drift apart and one tab can't be added twice
        public static void Register<T>() where T : BetterFGTab
        {
            string title = ReadTitle<T>();
            if (string.IsNullOrEmpty(title)) return;

            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Title == title)
                    return;

            _entries.Add(new TabEntry { Title = title, Factory = () => NewTab<T>() });
        }

        public static BetterFGTab CreateTab(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Factory != null && _entries[i].Title == title)
                    return _entries[i].Factory();
            return null;
        }

        // build a tab instance directly by type — used for drill-in tabs that aren't registered (so
        // they never appear in the slot dropdown) but are reached from an in-tab button.
        public static T NewTab<T>() where T : BetterFGTab
        {
            var go = new GameObject("BetterFG_" + typeof(T).Name);
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go.AddComponent<T>();
        }

        // read TabTitle off an inactive throwaway: keeping it inactive defers Awake so its bindings
        // never fire, and TabTitle is a plain constant getter that doesn't need the instance built
        private static string ReadTitle<T>() where T : BetterFGTab
        {
            var go = new GameObject("_tmpTab");
            go.SetActive(false);
            string title = go.AddComponent<T>().TabTitle;
            UnityEngine.Object.Destroy(go);
            return title;
        }
    }

    // ── Tab hover / right-click ────────────────────────────────────────────────
    public class TabHoverTint : MonoBehaviour
    {
        public TabHoverTint(IntPtr ptr) : base(ptr) { }
        public BetterFGTab Tab;
        private bool _hovering = false;
        private float _hoverTime = 0f;
        private bool _tooltipShown = false;
        private bool _idlePushed = false;
        private RectTransform _rt;

        void Awake() => _rt = GetComponent<RectTransform>();

        void Update()
        {
            if (BetterFGUIMan.Instance != null && !BetterFGUIMan.Instance.IsVisible) return;
            if (!BetterFGUIMan.UnityMouseReady()) { ClearHover(); return; }

            // NotifyTitleHover only fires on a hover *change*, so the idle style would never show until
            // the first hover-out. push it once here, on the first ready frame this tab's ticker runs —
            // this is per-tab and repaints reliably (same path a real hover uses).
            if (!_idlePushed) { _idlePushed = true; Tab?.ApplyHoverStyle(); }

            bool over = IsMouseOver();
            if (over != _hovering)
            {
                _hovering = over;
                Tab?.NotifyTitleHover(_hovering);
                if (!over)
                    ClearHover();
            }

            if (_hovering)
            {
                _hoverTime += Time.deltaTime;
                if (!_tooltipShown && _hoverTime >= Tooltip.HoverDelay && Tab != null)
                {
                    _tooltipShown = true;
                    BetterFGUIMan.Instance?.ShowTooltip(Tab.TabTitle);
                }

                if (Input.GetMouseButtonDown(1))
                    BetterFGUIMan.Instance?.RequestSlotDropdown(Tab);
            }
        }

        private bool IsMouseOver()
        {
            if (_rt == null) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(_rt,
                new Vector2(Input.mousePosition.x, Input.mousePosition.y), null);
        }

        private void ClearHover()
        {
            _hoverTime = 0f;
            if (_hovering)
            {
                _hovering = false;
                Tab?.NotifyTitleHover(false);
            }
            if (_tooltipShown) { BetterFGUIMan.Instance?.HideTooltip(); _tooltipShown = false; }
        }

        void OnDisable() { ClearHover(); }
    }

    internal class TooltipTrigger : MonoBehaviour
    {
        public TooltipTrigger(IntPtr ptr) : base(ptr) { }
        public string text = "";
        // no hover delay — pops the instant the pointer is over the trigger (used by the little
        // "?" credit markers next to tweak labels).
        public bool instant = false;
        // optional: a UI image (usually a faint background behind the trigger's label) shown only
        // while hovered, so the user can see something is hoverable before the tooltip pops.
        public GameObject hoverImage;
        private float _t = 0f;
        private bool _shown = false;
        private RectTransform _rt;
        private CanvasGroup _parentCg;
        private bool _cgCached;

        void Awake() => _rt = GetComponent<RectTransform>();

        void Update()
        {
            if (!BetterFGUIMan.UnityMouseReady())
            {
                if (_shown) { BetterFGUIMan.Instance?.HideTooltip(); _shown = false; }
                if (hoverImage != null) hoverImage.SetActive(false);
                _t = 0f;
                return;
            }

            // cached on first tick, after the trigger's been parented into its window
            if (!_cgCached) { _parentCg = GetComponentInParent<CanvasGroup>(); _cgCached = true; }
            var cg = _parentCg;
            if (cg != null && (!cg.blocksRaycasts || cg.alpha < 0.01f))
            {
                if (_shown) { BetterFGUIMan.Instance?.HideTooltip(); _shown = false; }
                if (hoverImage != null) hoverImage.SetActive(false);
                _t = 0f;
                return;
            }
            // instant markers live in standalone windows (Tweaks etc.), not the main panel, so don't
            // gate them on the main panel being open — only the timed tab tooltips need that.
            if (!instant && BetterFGUIMan.Instance != null && !BetterFGUIMan.Instance.IsVisible) return;
            if (_rt == null) return;
            bool over = RectTransformUtility.RectangleContainsScreenPoint(
                _rt, new Vector2(Input.mousePosition.x, Input.mousePosition.y), null);
            if (hoverImage != null) hoverImage.SetActive(over);
            if (!over) { if (_shown) { BetterFGUIMan.Instance?.HideTooltip(); _shown = false; } _t = 0f; return; }
            _t += Time.deltaTime;
            if (!_shown && _t >= (instant ? 0f : Tooltip.HoverDelay)) { _shown = true; BetterFGUIMan.Instance?.ShowTooltip(text); }
        }

        void OnDisable() { if (_shown) { BetterFGUIMan.Instance?.HideTooltip(); _shown = false; _t = 0f; } if (hoverImage != null) hoverImage.SetActive(false); }
    }

    // ── Manager ───────────────────────────────────────────────────────────────
    public class BetterFGUIMan : MonoBehaviour
    {
        public BetterFGUIMan(IntPtr ptr) : base(ptr) { }

        public static BetterFGUIMan Instance { get; private set; }
        public bool IsVisible => _visible;

        // UnityExplorer helper: pick a value in the enum dropdown and Evaluate to re-spawn
        // a standalone window. Bypasses each window's seen/gate logic so it always fires.
        public enum DebugWindow { Startup, Info, Update }

        public static void Spawn(DebugWindow which)
        {
            switch (which)
            {
                case DebugWindow.Startup: SpawnWindow<BetterFGStartupWindow>("BetterFG_Startup"); break;
                case DebugWindow.Info: BetterFGInfoWindow.Show(); break;
                case DebugWindow.Update: BetterFGUpdateWindow.Show(); break;
            }
        }

        private static void SpawnWindow<T>(string name) where T : MonoBehaviour
        {
            var go = new GameObject(name);
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<T>();
        }

        // how many tab slots show. user-set via the Options window (Tabs section), 1..5, default 3.
        // tabs are built at startup so a change takes effect on the next launch.
        public const int MAX_SLOTS_CAP = 5;
        public static int MAX_SLOTS =>
            Mathf.Clamp(int.TryParse(SettingsService.Get("ui.maxtabs", "3"), out int v) ? v : 3, 1, MAX_SLOTS_CAP);

        private static float TAB_W => UIScale.TAB_W;
        private static float TAB_CONTENT_H => UIScale.TAB_CONTENT_H;
        private static float TITLE_H => UIScale.TITLE_H;
        private static float TOTAL_H => TAB_CONTENT_H + TITLE_H;
        private static float TAB_GAP => UIScale.TAB_GAP;
        private static float TAB_MARGIN_X => UIScale.TAB_MARGIN_X;

        private const float ANIM_DURATION = 0.3f;

        private const string KEY_UI_HIDDEN = "ui.hidden";
        private const string KEY_SLOT_PREFIX = "ui.slot.";

        // tab slide curves
        private static readonly AnimationCurve OpenCurve = new AnimationCurve(new Keyframe[]
        {
            new Keyframe(0.0000f, 0.0000f, 0.0162f, 0.0162f),
            new Keyframe(0.2214f, 0.4801f, 1.8824f, 1.8824f),
            new Keyframe(1.0000f, 1.0000f, 0.0000f, 0.0000f),
        });
        private static readonly AnimationCurve CloseCurve = new AnimationCurve(new Keyframe[]
        {
            new Keyframe(0.0000f, 0.0000f, 0.0000f, 0.0000f),
            new Keyframe(0.6023f, 1.0271f, 0.1441f, 0.1441f),
            new Keyframe(1.0000f, 1.0000f, 0.0000f, 0.0000f),
        });

        // ── State ─────────────────────────────────────────────────────────────
        private float _peekedY;
        private float _raisedY;
        private float _startX;

        private Canvas _canvas;
        private Canvas _overlayCanvas;
        private bool _visible = true;
        private GameObject _rootGo;
        private GameObject _backdropGo;
        private RawImage _backdropImg;

        public const string KEY_BACKDROP = "ui.backdrop";
        public static readonly string[] BackdropPatterns =
        {
            "BetterFG.assets.ui.general.pattern_crown.png",
            "BetterFG.assets.ui.general.pattern_crown_blue.png",
            "BetterFG.assets.ui.general.pattern_crown_yellow.png",
            "BetterFG.assets.ui.general.pattern_ss3.png",
            "BetterFG.assets.ui.general.pattern_ss1.png",
            "BetterFG.assets.ui.general.pattern_ss2.png",
            "BetterFG.assets.ui.general.pattern_ss4.png",
        };
        public static readonly string[] BackdropNames =
            { "Grey Crowns", "Blue Crowns", "Yellow Crowns", "Underwater", "Stadium", "Space", "Digital" };

        public const string KEY_BACKDROP_OPACITY = "ui.backdrop.opacity";

        public static int BackdropIndex
        {
            get
            {
                int v = int.TryParse(Services.SettingsService.Get(KEY_BACKDROP, "0"), out int i) ? i : 0;
                return Mathf.Clamp(v, 0, BackdropPatterns.Length - 1);
            }
        }

        public static float BackdropOpacity
        {
            get
            {
                float v = float.TryParse(Services.SettingsService.Get(KEY_BACKDROP_OPACITY, "1"),
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float f) ? f : 1f;
                return Mathf.Clamp01(v);
            }
        }
        private GameObject _watermarkGo;
        private GameObject _controlsWatermarkGo;
        private GameObject _creativeHintGo;
        private TextMeshProUGUI _creativeHintText;
        private CanvasGroup _rootCg;

        private List<BetterFGTab> _tabs = new List<BetterFGTab>();
        private List<RectTransform> _tabRoots = new List<RectTransform>();
        private BetterFGTab _openTab = null;
        private Dictionary<BetterFGTab, Coroutine> _anims = new Dictionary<BetterFGTab, Coroutine>();

        private SlotDropdown _dropdown = new SlotDropdown();
        private BetterFGTab _pendingDropdownTab;
        private BetterFGTab _dropdownForcedOpenTab;

        internal static bool UnityMouseReady()
        {
            if (!Application.isFocused) return false;
            if (!Input.mousePresent) return false;

            var p = Input.mousePosition;
            return p.x >= 0f && p.y >= 0f && p.x <= Screen.width && p.y <= Screen.height;
        }

        private Tooltip _tooltip;

        // ── Lifecycle ─────────────────────────────────────────────────────────
        void Awake()
        {
            Instance = this;
            TabHoverStyle.LoadFromSettings();
            if (_canvas == null) InitCanvas();
        }

        void Start()
        {
            bool shouldHide = SettingsService.Get(KEY_UI_HIDDEN, "false") == "true";
            if (shouldHide) SetVisible(false);
        }

        void Update()
        {
            WinDialogs.Tick();
            Shell32Util.Init(); // one-shot; self-guards once the game window exists
            UpdateInputNavState();
            FGInputLockService.Tick();
            MenuMusicService.TickVolume();
            SettingsService.TickBackup();

            // Use simple rising-edge detection to avoid repeated toggles
            if (!IsTyping())
            {
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                var uiKey = KeybindService.Get(KeybindId.ToggleUI);
                bool zNow = uiKey != KeyCode.None && Input.GetKey(uiKey);
                if (zNow && !_prevZ && shift)
                    SetVisible(!_visible);
                _prevZ = zNow;

                bool f1Now = Input.GetKey(KeyCode.F1);
                if (f1Now && !_prevF1)
                    SetCursorFree(!_cursorFree);
                _prevF1 = f1Now;
            }

            // the game steals the cursor back (locks+hides it to center) the moment it sees controller
            // input, fighting our controller-driven cursor. re-assert our desired state every frame
            // while the UI/wheel is up so it can't win — F1 flips _cursorFree if the player wants it locked.
            if (_visible || (SideWheelManager.Instance?.IsWheelVisible ?? false))
                ApplyCursorState();

            if (_pendingDropdownTab != null)
            {
                var tab = _pendingDropdownTab;
                _pendingDropdownTab = null;
                OpenSlotDropdown(tab);
            }

            if (Input.GetMouseButtonDown(0))
            {
                if (_dropdown.IsOpen)
                {
                    if (!_dropdown.HitTest(new Vector2(Input.mousePosition.x, Input.mousePosition.y)))
                    {
                        _dropdown.Close();
                        if (_dropdownForcedOpenTab != null && _openTab == _dropdownForcedOpenTab)
                            ToggleTab(_dropdownForcedOpenTab);
                        _dropdownForcedOpenTab = null;
                    }
                }

                // deselect any focused input field when clicking outside it
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (es != null && es.currentSelectedGameObject != null)
                {
                    var sel = es.currentSelectedGameObject;
                    if (sel.activeInHierarchy
                        && (sel.GetComponent<UnityEngine.UI.InputField>() != null
                            || sel.GetComponent<TMPro.TMP_InputField>() != null))
                    {
                        var selRt = sel.GetComponent<RectTransform>();
                        if (selRt != null && !RectTransformUtility.RectangleContainsScreenPoint(
                                selRt, new Vector2(Input.mousePosition.x, Input.mousePosition.y), null))
                            es.SetSelectedGameObject(null);
                    }
                }
            }

            if (!UnityMouseReady())
                HideTooltip();
            else if (_tooltip != null && _tooltip.IsShown)
                _tooltip.FollowMouse(_overlayCanvas);

            _dropdown.Tick();
        }

        private void UpdateInputNavState()
        {
            // while our UI is up in the level editor, kill all game input even if no field is
            // focused — otherwise editor hotkeys/movement fire through the open overlay. the batch-edit
            // window counts too even when the main overlay's closed, else clicking a selected object
            // under the cursor deselects it out from under the batch op.
            bool inLE = Features.UnityRound.Editor.UnityRoundLoader.InLevelEditor;
            bool inputRestricted = inLE && (_visible || UI.Windows.Creative.BatchEditWindow.AnyOpen);
            FGInputLockService.SetEditorUiLock(inputRestricted);

            // show the hint whenever the UI is open (not just in creative), since game input is
            // always locked while BettrFG is up. font isn't loaded at startup so grab it lazily.
            // text itself is event-driven now (SetVisible / wheel toggle / rebind), only the lazy
            // font styling still needs a per-frame chance until the game font exists
            bool hintVisible = _visible || (SideWheelManager.Instance?.IsWheelVisible ?? false);
            if (hintVisible) EnsureHintStyle();
            if (_creativeHintGo != null && _creativeHintGo.activeSelf != hintVisible)
                _creativeHintGo.SetActive(hintVisible);

            var cur = UnityEngine.EventSystems.EventSystem.current;
            if (cur == null) return;

            bool blockNav = false;
            var selected = cur.currentSelectedGameObject;
            if (selected != null
                && (selected.GetComponent<UnityEngine.UI.InputField>() != null
                    || selected.GetComponent<TMP_InputField>() != null))
            {
                // only lock for OUR input fields — a focused Fall Guys input field
                // (chat, name entry, etc.) must never eat the player's controls.
                // our stuff lives under _canvas OR under a BetterFGWindow's own canvas
                // (those are separate canvases sorting at 997), so check both.
                // require the field to be LIVE on screen. two ways a selection goes stale and
                // would otherwise hold game input dead forever: a standalone window closes with
                // its field focused (its GameObject deactivates -> activeInHierarchy false), or
                // the MAIN UI is hidden with the keybind mid-type (the tab root isn't deactivated,
                // only its CanvasGroup alpha goes to 0, so we check that instead). fields under
                // the main _canvas need _visible; window fields just need to be active. we do NOT
                // deselect — that would yank focus out of a field you're actively typing in.
                bool underMain = _canvas != null && selected.transform.IsChildOf(_canvas.transform);
                blockNav = selected.activeInHierarchy
                    && (underMain ? _visible : IsOurField(selected.transform));
            }

            // write the real-field latch only; the fake-field caret paths own the
            // other latch, and the service locks if either is set, so we don't
            // stomp a fake field that's currently being typed in
            FGInputLockService.SetRealFieldLock(blockNav);
        }

        // ── Init ──────────────────────────────────────────────────────────────
        private void InitCanvas()
        {
            _canvas = CreateCanvas();

            // tooltip + creative hint live on their own small canvas so they keep working when the
            // main canvas is disabled (hidden UI): instant "?" tooltips in standalone windows and
            // the input hint with just the wheel open both outlive the main panel
            _overlayCanvas = CreateCanvas();
            _overlayCanvas.gameObject.name = "BetterFG_OverlayCanvas";
            _overlayCanvas.sortingOrder = 1001;

            _peekedY = -TAB_CONTENT_H;
            _raisedY = 0f;
            _startX = TAB_MARGIN_X;

            _rootGo = new GameObject("TabRoot");
            _rootGo.hideFlags = HideFlags.HideAndDontSave;
            _rootGo.transform.SetParent(_canvas.transform, false);
            var rootRt = _rootGo.AddComponent<RectTransform>();
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
            _rootCg = _rootGo.AddComponent<CanvasGroup>();

            BuildFullscreenBackdrop();
            BuildTooltipWidget();
            BuildWatermark();
            BuildControlsWatermark();
            BuildCreativeInputHint();
        }

        // ── Tab registration (initial slots) ──────────────────────────────────
        public void RegisterTab(BetterFGTab tab)
        {
            if (_canvas == null) InitCanvas();
            if (_tabs.Count >= MAX_SLOTS) return;
            AddTabToSlot(_tabs.Count, tab);
        }

        // ── Slot load from settings ───────────────────────────────────────────
        // Call after RegisterTab calls so defaults are placed first, then override
        public void LoadSavedSlots()
        {
            for (int i = 0; i < MAX_SLOTS; i++)
            {
                string saved = SettingsService.Get(KEY_SLOT_PREFIX + i, "");
                if (string.IsNullOrEmpty(saved)) continue;

                // if slot already has this tab, skip
                if (i < _tabs.Count && _tabs[i] != null && _tabs[i].TabTitle == saved) continue;

                SwapSlot(i, saved, save: false);
            }
        }

        private void SaveSlots()
        {
            for (int i = 0; i < _tabs.Count; i++)
                SettingsService.Set(KEY_SLOT_PREFIX + i,
                    _tabs[i] != null ? _tabs[i].TabTitle : "");
        }

        // ── Core slot management ──────────────────────────────────────────────
        private void AddTabToSlot(int idx, BetterFGTab tab)
        {
            while (_tabs.Count <= idx) _tabs.Add(null);
            while (_tabRoots.Count <= idx) _tabRoots.Add(null);

            RectTransform rt;
            GameObject rootGo;

            if (_tabRoots[idx] != null)
            {
                rootGo = _tabRoots[idx].gameObject;
                rt = _tabRoots[idx];

                var old = _tabs[idx];
                if (old != null)
                {
                    if (_openTab == old)
                    {
                        _openTab = null;
                        // the root is still slid up at the open Y; drop it back to closed so the
                        // replacement tab starts closed and in sync (otherwise first click is wasted)
                        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, _peekedY);
                    }
                    if (_anims.ContainsKey(old)) { StopCoroutine(_anims[old]); _anims.Remove(old); }
                    old.transform.SetParent(null, false);
                    UnityEngine.Object.Destroy(old.gameObject);
                }
                for (int i = rootGo.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(rootGo.transform.GetChild(i).gameObject);
            }
            else
            {
                float xPos = _startX + idx * (TAB_W + TAB_GAP);
                rootGo = new GameObject("Tab_" + idx);
                rootGo.hideFlags = HideFlags.HideAndDontSave;
                rootGo.transform.SetParent(_rootGo.transform, false);
                rt = rootGo.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0f, 0f);
                rt.sizeDelta = new Vector2(TAB_W, TOTAL_H);
                rt.anchoredPosition = new Vector2(xPos, _peekedY);
                rootGo.AddComponent<Image>().color = Color.clear;
                _tabRoots[idx] = rt;
            }

            _tabs[idx] = tab;

            tab.transform.SetParent(rootGo.transform, false);
            var tabRt = tab.gameObject.GetComponent<RectTransform>() ?? tab.gameObject.AddComponent<RectTransform>();
            tabRt.anchorMin = Vector2.zero;
            tabRt.anchorMax = Vector2.one;
            tabRt.offsetMin = tabRt.offsetMax = Vector2.zero;

            tab.TabWidth = TAB_W;
            tab.TabHeight = TAB_CONTENT_H;
            tab.Initialize(rt);
            tab.EnsureBuilt();
        }

        private void SwapSlot(int slotIdx, string tabName, bool save)
        {
            var newTab = BetterFGTabRegistry.CreateTab(tabName);
            if (newTab == null) return;
            AddTabToSlot(slotIdx, newTab);
            if (save) SaveSlots();
        }

        // ── Live max-tab change (from the Options window) ──────────────────────
        // persists ui.maxtabs and adds/removes slots right now instead of waiting for a relaunch.
        // growing fills new slots with the first registered tab not already shown.
        public void SetMaxTabs(int count)
        {
            count = Mathf.Clamp(count, 1, MAX_SLOTS_CAP);
            SettingsService.Set("ui.maxtabs", count.ToString());

            if (count < _tabs.Count)
            {
                for (int i = _tabs.Count - 1; i >= count; i--) RemoveSlot(i);
            }
            else
            {
                for (int i = _tabs.Count; i < count; i++)
                {
                    // honor a saved tab for this slot if it still resolves, else first free tab
                    string saved = SettingsService.Get(KEY_SLOT_PREFIX + i, "");
                    var tab = string.IsNullOrEmpty(saved) ? null : BetterFGTabRegistry.CreateTab(saved);
                    if (tab == null) tab = CreateFirstAvailableTab();
                    if (tab == null) break;
                    AddTabToSlot(i, tab);
                }
            }
            SaveSlots();
        }

        // first registered tab that isn't already sitting in a slot
        private BetterFGTab CreateFirstAvailableTab()
        {
            foreach (var e in BetterFGTabRegistry.All)
            {
                bool used = false;
                for (int i = 0; i < _tabs.Count; i++)
                    if (_tabs[i] != null && _tabs[i].TabTitle == e.Title)
                    { used = true; break; }
                if (!used) return e.Factory();
            }
            return null;
        }

        private void RemoveSlot(int idx)
        {
            if (idx < 0 || idx >= _tabs.Count) return;
            var tab = _tabs[idx];
            if (tab != null)
            {
                if (_openTab == tab) _openTab = null;
                if (_anims.ContainsKey(tab)) { StopCoroutine(_anims[tab]); _anims.Remove(tab); }
            }
            if (idx < _tabRoots.Count && _tabRoots[idx] != null)
            {
                UnityEngine.Object.Destroy(_tabRoots[idx].gameObject); // destroys the tab child too
                _tabRoots.RemoveAt(idx);
            }
            _tabs.RemoveAt(idx);
            SettingsService.Remove(KEY_SLOT_PREFIX + idx);
        }

        // ── Dropdown ──────────────────────────────────────────────────────────
        public void RequestSlotDropdown(BetterFGTab owner)
        {
            _pendingDropdownTab = owner;
        }

        private void OpenSlotDropdown(BetterFGTab owner)
        {
            int ownerIdx = _tabs.IndexOf(owner);
            if (owner == null || ownerIdx < 0) return;

            // force the tab open so the dropdown shows inside it. remember if WE opened it,
            // so clicking away can close it back.
            _dropdownForcedOpenTab = (_openTab != owner) ? owner : null;
            if (_openTab != owner) ToggleTab(owner);

            RectTransform tabRoot = (ownerIdx < _tabRoots.Count) ? _tabRoots[ownerIdx] : null;

            var allTitles = new string[BetterFGTabRegistry.All.Count];
            for (int i = 0; i < BetterFGTabRegistry.All.Count; i++)
                allTitles[i] = BetterFGTabRegistry.All[i].Title;

            var occupied = new string[_tabs.Count];
            for (int i = 0; i < _tabs.Count; i++)
                occupied[i] = _tabs[i] != null ? _tabs[i].TabTitle : "";

            _dropdown.Open(tabRoot, ownerIdx, owner.TabTitle, allTitles, allTitles, occupied);
        }

        // current-slot cell was clicked: keep the tab open, don't auto-close it
        public void KeepDropdownTabOpen() => _dropdownForcedOpenTab = null;

        public void SwapSlotFromDropdown(int slotIdx, string tabName)
        {
            // user picked a tab — forget the force-close, swap, then open the new tab
            _dropdownForcedOpenTab = null;
            SwapSlot(slotIdx, tabName, save: true);
            if (slotIdx >= 0 && slotIdx < _tabs.Count && _tabs[slotIdx] != null && _openTab != _tabs[slotIdx])
                ToggleTab(_tabs[slotIdx]);
        }

        // open the Social tab and jump to its Emotes sub-tab. if Social isn't in a slot yet,
        // drop it into the 3rd slot first. used by the Customization tab's "Copy -> open" flow.
        public void OpenSocialEmotes()
        {
            if (!_visible) SetVisible(true);

            var social = FindTab<BetterFG.UI.Tab.EmoticonsPhrasesTab>();
            if (social == null)
            {
                int slot = Math.Min(2, MAX_SLOTS - 1); // "3rd tab"
                SwapSlot(slot, "Phrases", save: true);
                social = (slot < _tabs.Count) ? _tabs[slot] as BetterFG.UI.Tab.EmoticonsPhrasesTab : null;
            }
            if (social == null) return;

            if (_openTab != social) ToggleTab(social);
            social.ShowEmotesSubTab();

            // point the arrow at the Paste button once the tab's slide/layout has settled
            StartCoroutine(HighlightPasteNextFrame(social).WrapToIl2Cpp());
        }

        private IEnumerator HighlightPasteNextFrame(BetterFG.UI.Tab.EmoticonsPhrasesTab social)
        {
            yield return null;
            yield return null;
            var rt = social != null ? social.PasteButtonRect : null;
            if (rt != null) HighlightObject(HighlightType.ArrowBottom, rt);
        }

        // open the UI tab and jump to its Screen sub-tab. if UI isn't in a slot yet,
        // drop it into the 3rd slot first. used by the Main Menu tab's "Take me there" notice.
        public void OpenUIScreen()
        {
            if (!_visible) SetVisible(true);

            var ui = FindTab<BetterFG.UI.Tab.UITab>();
            if (ui == null)
            {
                int slot = Math.Min(2, MAX_SLOTS - 1); // "3rd tab"
                SwapSlot(slot, "UI", save: true);
                ui = (slot < _tabs.Count) ? _tabs[slot] as BetterFG.UI.Tab.UITab : null;
            }
            if (ui == null) return;

            if (_openTab != ui) ToggleTab(ui);
            ui.ShowScreenSubTab();
        }

        // open the Creative tab and jump to its Args sub-tab. if Creative isn't in a slot yet,
        // drop it into the 3rd slot first. used by BatchEditWindow's "increment settings" link.
        public void OpenCreativeArgs()
        {
            if (!_visible) SetVisible(true);

            var creative = FindTab<BetterFG.UI.Tab.CreativeTab>();
            if (creative == null)
            {
                int slot = Math.Min(2, MAX_SLOTS - 1); // "3rd tab"
                SwapSlot(slot, "Creative", save: true);
                creative = (slot < _tabs.Count) ? _tabs[slot] as BetterFG.UI.Tab.CreativeTab : null;
            }
            if (creative == null) return;

            if (_openTab != creative) ToggleTab(creative);
            creative.ShowArgsSubTab();
        }

        private T FindTab<T>() where T : BetterFGTab
        {
            for (int i = 0; i < _tabs.Count; i++)
                if (_tabs[i] is T t) return t;
            return null;
        }

        // swap the slot a tab lives in for a fresh instance of another tab type and open it. used for
        // drill-in tabs like Creative <-> "Creative - Custom Textures" that aren't in the tab registry
        // (so they never show in the slot dropdown) and are reached only via an in-tab button. not
        // saved: on relaunch the slot restores to whatever registered tab was there.
        public BetterFGTab SwitchSlotTab(BetterFGTab from, BetterFGTab replacement)
        {
            int slot = _tabs.IndexOf(from);
            if (slot < 0 || replacement == null) return null;
            AddTabToSlot(slot, replacement);   // destroys `from`
            if (_openTab != replacement) ToggleTab(replacement);
            return replacement;
        }

        // ── Tab toggle ────────────────────────────────────────────────────────
        public void ToggleTab(BetterFGTab tab)
        {
            if (_openTab == tab)
            {
                AudioService.PlayTabClose();
                AnimateTab(tab, toOpen: false);
                tab.IsOpen = false;
                tab.OnClosed();
                _openTab = null;
            }
            else
            {
                if (_openTab != null) { AnimateTab(_openTab, toOpen: false); _openTab.IsOpen = false; _openTab.OnClosed(); }
                AudioService.PlayTabOpen();
                tab.IsOpen = true;
                _openTab = tab;
                tab.SetContentActive(true); // wake the content before it slides in
                AnimateTab(tab, toOpen: true);
                tab.OnOpened();
            }
        }

        private static bool IsTyping()
        {
            var cur = UnityEngine.EventSystems.EventSystem.current;
            if (cur == null || cur.currentSelectedGameObject == null) return false;
            return cur.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null
                || cur.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
        }

        // true if the transform sits under one of OUR canvases. window canvases are
        // separate GameObjects from _canvas, so walk up and match by canvas name.
        private bool IsOurField(Transform t)
        {
            var p = t;
            while (p != null)
            {
                if (_canvas != null && p == _canvas.transform) return true;
                if (p.GetComponent<Canvas>() != null && p.name.EndsWith("_Canvas")) return true;
                p = p.parent;
            }
            return false;
        }

        private bool _prevZ = false;
        private bool _prevF1 = false;
        private bool _cursorFree = true; // desired cursor state: true = unlocked+visible, false = locked+hidden

        // single owner of the OS cursor state. the game tries to re-lock/hide the cursor whenever it
        // sees controller input (recentering it), so while our UI is up we re-assert this every frame
        // in Update() — F1 and the controller-close both just flip _cursorFree and let that enforce.
        public void SetCursorFree(bool free)
        {
            _cursorFree = free;
            ApplyCursorState();
        }

        private void ApplyCursorState()
        {
            if (_cursorFree) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            else { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }

        public void OnWheelVisibilityChanged(bool visible)
        {
            UpdateCameraFreeze();
            UpdateCreativeHintText();
        }

        public void SetVisible(bool visible)
        {
            if (_visible != visible)
            {
                if (visible) Services.AudioService.PlayShowUI();
                else Services.AudioService.PlayHideUI();
            }
            _visible = visible;
            if (!visible) HideTooltip();
            // hiding the whole UI (not a tab switch) skips ToggleTab, so the open tab's OnClosed
            // never fires — the Social tab's wheel clone would linger and keep the game's camera
            // input locked. close the open tab here so it tears down cleanly.
            if (!visible && _openTab != null) { AnimateTab(_openTab, toOpen: false); _openTab.IsOpen = false; _openTab.OnClosed(); _openTab = null; }
            if (_rootCg != null)
            {
                _rootCg.alpha = visible ? 1f : 0f;
                _rootCg.blocksRaycasts = visible;
                _rootCg.interactable = visible;
            }
            // alpha 0 alone still submits every tab's geometry to the GPU each frame — disabling the
            // canvas actually stops the draw. GameObjects stay active so tab coroutines/events live on
            if (_canvas != null) _canvas.enabled = visible;
            UpdateCreativeHintText();

            SettingsService.Set(KEY_UI_HIDDEN, visible ? "false" : "true");
            if (_backdropGo != null) _backdropGo.SetActive(visible);
            if (_watermarkGo != null) _watermarkGo.SetActive(visible);
            if (_controlsWatermarkGo != null) _controlsWatermarkGo.SetActive(visible);
            SideWheelManager.Instance?.SetVisible(visible);

            UpdateCameraFreeze();
        }

        private void UpdateCameraFreeze()
        {
            bool anyOpen = _visible || (SideWheelManager.Instance?.IsWheelVisible ?? false);
            if (anyOpen)
            {
                SetCursorFree(true);
                FallGuysLib.Camera.CameraUtils.FreezePlayerCamera();
            }
            else
            {
                FallGuysLib.Camera.CameraUtils.RestorePlayerCamera();
            }
        }

        // ── Tab slide anim ────────────────────────────────────────────────────
        private void AnimateTab(BetterFGTab tab, bool toOpen)
        {
            int idx = _tabs.IndexOf(tab);
            if (idx < 0) return;
            var rt = _tabRoots[idx];
            if (_anims.ContainsKey(tab) && _anims[tab] != null) StopCoroutine(_anims[tab]);
            _anims[tab] = StartCoroutine(AnimCoroutine(rt, toOpen, tab).WrapToIl2Cpp());
        }

        private IEnumerator AnimCoroutine(RectTransform rt, bool toOpen, BetterFGTab tab = null)
        {
            float startY = rt.anchoredPosition.y;
            float openY = _raisedY;
            if (tab?.OpenedTabLocalY.HasValue == true)
                openY = _raisedY + tab.OpenedTabLocalY.Value + 540f;
            float targetY = toOpen ? openY : _peekedY;
            var curve = toOpen ? OpenCurve : CloseCurve;
            float elapsed = 0f;
            while (elapsed < ANIM_DURATION)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / ANIM_DURATION);
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x,
                    Mathf.LerpUnclamped(startY, targetY, curve.Evaluate(t)));
                yield return null;
            }
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, targetY);

            // once the tab has finished sliding back down, disable its content so it isn't repainted
            if (!toOpen && tab != null) tab.SetContentActive(false);
        }

        // ── UI scale confirm ────────────────────────────────────────────────────
        // a screen-center "keep this scale?" button. you've got 10s to click it or the scale
        // reverts to prevScale. used by the UI Scale window when the dropdown changes the scale.
        private GameObject _scaleConfirmGo;
        private int _scaleConfirmGen;

        public void ShowScaleConfirm(float newScale, float prevScale)
        {
            if (_canvas == null) return;
            if (_scaleConfirmGo != null) UnityEngine.Object.Destroy(_scaleConfirmGo);

            int gen = ++_scaleConfirmGen;

            var go = new GameObject("ScaleConfirm");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(_canvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(240f, 56f);
            go.AddComponent<Image>().color = Color.clear;
            _scaleConfirmGo = go;

            var btn = UGUIShip.CreateButton(go.transform,
                new Rect(0f, 0f, 240f, 56f),
                $"KEEP {(int)Mathf.Round(newScale * 100f)}% SCALE?\n(reverts in 10s)",
                new Color(0.2f, 0.45f, 0.2f, 1f), Color.white, 13,
                new Action(() =>
                {
                    if (gen != _scaleConfirmGen) return;
                    BetterFG.Services.UIScaleService.Save(newScale);
                    _scaleConfirmGen++;
                    if (_scaleConfirmGo != null) { UnityEngine.Object.Destroy(_scaleConfirmGo); _scaleConfirmGo = null; }
                }));
            var lbl = btn.GetComponentInChildren<Text>();
            if (lbl != null) { lbl.alignment = TextAnchor.MiddleCenter; lbl.fontStyle = FontStyle.Bold; }

            StartCoroutine(RevertScaleAfter(gen, prevScale).WrapToIl2Cpp());
        }

        private IEnumerator RevertScaleAfter(int gen, float prevScale)
        {
            yield return new WaitForSecondsRealtime(10f);
            if (gen != _scaleConfirmGen) yield break; // confirmed or superseded
            BetterFG.Services.UIScaleService.Apply(prevScale);
            _scaleConfirmGen++;
            if (_scaleConfirmGo != null) { UnityEngine.Object.Destroy(_scaleConfirmGo); _scaleConfirmGo = null; }
        }

        // ── Tooltip ───────────────────────────────────────────────────────────
        public static void MakeObjectTooltip(RectTransform rt, string text)
        {
            var t = rt.gameObject.AddComponent<TooltipTrigger>();
            t.text = text;
        }

        public void ShowTooltip(string text)
        {
            if (_tooltip == null) return;
            if (!UnityMouseReady()) return;
            AudioService.PlayTooltipShow();
            _tooltip.SetFixed(false, Vector2.zero);
            _tooltip.SetText(text);
            // place at the cursor BEFORE going visible — otherwise the first frame draws at the
            // last position (FollowMouse only runs in the next Update tick).
            _tooltip.FollowMouse(_overlayCanvas);
            _tooltip.SetVisible(true);
        }

        public void HideTooltip()
        {
            _tooltip?.SetVisible(false);
        }

        // show the tooltip and auto-hide it after `seconds`. used for one-off messages (e.g. the
        // emote "Copied" prompt) instead of a bespoke popup. a newer call cancels the older hide.
        private int _timedTipGen;
        public void ShowTooltipTimed(string text, float seconds)
        {
            ShowTooltip(text);
            if (_tooltip == null) return;
            int gen = ++_timedTipGen;
            StartCoroutine(HideTooltipAfter(gen, seconds).WrapToIl2Cpp());
        }

        // show the tooltip at a fixed canvas position (no cursor follow) and auto-hide after seconds.
        public void ShowTooltipFixed(string text, Vector2 canvasPos, float seconds)
        {
            if (_tooltip == null) return;
            AudioService.PlayTooltipShow();
            _tooltip.SetFixed(true, canvasPos);
            _tooltip.SetText(text);
            _tooltip.SetVisible(true);
            int gen = ++_timedTipGen;
            StartCoroutine(HideTooltipAfter(gen, seconds).WrapToIl2Cpp());
        }

        // same tooltip, pinned just above a UI element somewhere else (a game menu row, say) instead of
        // following the cursor. pass the element's TOP-edge world position; it sits clear of it rather
        // than covering it, which needs the height only known once the text has been laid out.
        public void ShowTooltipOver(string text, Vector3 topWorldPos, float seconds)
        {
            if (_tooltip == null || _overlayCanvas == null) return;
            var canvasRt = _overlayCanvas.GetComponent<RectTransform>();
            if (canvasRt == null) return;
            var screen = RectTransformUtility.WorldToScreenPoint(null, topWorldPos);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRt, screen, _overlayCanvas.worldCamera, out var local);
            ShowTooltipFixed(text, local, seconds);
            _tooltip.SetFixed(true, local + new Vector2(0f, _tooltip.Height * 0.5f + 8f));
        }

        private IEnumerator HideTooltipAfter(int gen, float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            if (gen == _timedTipGen) HideTooltip();
        }

        // ── Highlight ───────────────────────────────────────────────────────────
        public enum HighlightType { ArrowBottom }

        private static Texture2D _arrowTex;

        // pop a temporary attention-grabber over `target` (e.g. a down-arrow above a button).
        // shows for 3s and fades out over the last 2s, bobbing the whole time.
        public void HighlightObject(HighlightType type, RectTransform target)
        {
            if (target == null || _canvas == null) return;
            StartCoroutine(HighlightCoroutine(type, target).WrapToIl2Cpp());
        }

        private IEnumerator HighlightCoroutine(HighlightType type, RectTransform target)
        {
            var tex = LoadArrowTex();
            if (tex == null) yield break;

            float aspect = (float)tex.width / Mathf.Max(1, tex.height);
            float h = 40f * UIScale.S;
            float w = h * aspect;

            // parent to the target's PARENT (not the target itself — the Paste button has a
            // RectMask2D that would clip an arrow sitting above it). this inherits the same canvas +
            // scale, dodging the screen/world conversion that was dumping the arrow in a corner.
            Transform host = target.parent != null ? target.parent : target;
            var go = new GameObject("Highlight_Arrow");
            go.transform.SetParent(host, false);
            var rt = go.AddComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(w, h);

            // sit centered just above the target's top edge, in the shared parent's space
            Vector2 targetCenter = target.anchoredPosition + (Vector2.one * 0.5f - target.pivot) * target.rect.size;
            float targetTop = targetCenter.y + target.rect.height * 0.5f;
            rt.anchorMin = target.anchorMin;
            rt.anchorMax = target.anchorMax;
            rt.anchoredPosition = new Vector2(targetCenter.x, targetTop + 6f);

            var img = go.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;

            // bob up and down via the reusable pulse component
            var pulse = go.AddComponent<BetterFG.UI.Components.MovePulseContinuous>();
            pulse.axis = Vector3.up;
            pulse.speed = 1.6f;
            pulse.strength = 8f * UIScale.S;

            const float life = 3f;
            const float fade = 2f;
            float t = 0f;
            while (t < life)
            {
                t += Time.unscaledDeltaTime;
                float remaining = life - t;
                cg.alpha = remaining >= fade ? 1f : Mathf.Clamp01(remaining / fade);
                yield return null;
            }
            UnityEngine.Object.Destroy(go);
        }

        private static Texture2D LoadArrowTex()
        {
            if (_arrowTex != null) return _arrowTex;
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using var s = asm.GetManifestResourceStream("BetterFG.assets.ui.emoticonsphrases.arrow.png");
                if (s == null) return null;
                var b = new byte[s.Length];
                s.Read(b, 0, b.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(b);
                tex.wrapMode = TextureWrapMode.Clamp;
                _arrowTex = tex;
            }
            catch (Exception ex) { Plugin.Log.LogError("BetterFG: arrow tex load: " + ex.Message); }
            return _arrowTex;
        }

        public void UpdateTooltipText(string text)
        {
            _tooltip?.SetText(text);
        }

        private void BuildTooltipWidget()
        {
            var go = new GameObject("Tooltip");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(_overlayCanvas.transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = Vector2.zero;

            go.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.95f);
            var cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;

            _tooltip = go.AddComponent<Tooltip>();

            // label fills the tooltip with a small padding inset. tooltip sizes itself to text in
            // Tooltip.SetText, so no HorizontalLayoutGroup/ContentSizeFitter (those were forcing the
            // tooltip to always be the max width even on a 3-word hint).
            var t = UGUIShip.CreateLabel(go.transform, default, "", 13,
                new Color(1f, 1f, 1f, 0.9f), TextAnchor.MiddleLeft);
            t.fontStyle = FontStyle.Bold;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            var lblRt = t.rectTransform;
            lblRt.anchorMin = Vector2.zero;
            lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = new Vector2(8f, 5f);
            lblRt.offsetMax = new Vector2(-8f, -5f);

            _tooltip.SetVisible(false);
        }

        // ── Fullscreen backdrop ───────────────────────────────────────────────
        // full-screen tiled+scrolling crown pattern behind every tab. lives under _rootGo so it
        // fades with the UI toggle via _rootCg. sibling index 0 keeps it below the tabs.
        //
        // the BettrFG/BackdropGradient shader multiplies the pattern's alpha by a vertical
        // screen-space gradient (fades to nothing top and bottom) — a real feathered fade, not
        // the hard stencil cutout a Mask/RectMask2D gives. shader ships in the ui asset bundle;
        // if it isn't loaded yet we fall back to the plain pattern with no fade.
        private void BuildFullscreenBackdrop()
        {
            var patTex = BetterFG.Utilities.EmbeddedResourceandUnity.LoadTexture(BackdropPatterns[BackdropIndex]);
            if (patTex == null) return;
            patTex.wrapMode = TextureWrapMode.Repeat;

            // own canvas at 996 — below windows (997), the side wheel (998) and the main UI (999)
            // so the backdrop renders under everything. no GraphicRaycaster: it must never eat
            // clicks meant for the windows sitting right above it. toggled in SetVisible.
            var cgo = new GameObject("BetterFG_BackdropCanvas");
            cgo.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(cgo);
            var bcanvas = cgo.AddComponent<Canvas>();
            bcanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            bcanvas.sortingOrder = 996;
            cgo.AddComponent<GraphicRaycaster>();
            _backdropGo = cgo;

            var go = new GameObject("Backdrop");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(cgo.transform, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            var pimg = go.AddComponent<RawImage>();
            _backdropImg = pimg;
            pimg.texture = patTex;
            pimg.color = new Color(1f, 1f, 1f, BackdropOpacity);
            pimg.raycastTarget = true;
            pimg.uvRect = new Rect(0f, 0f, 9f, 9f * ((float)Screen.height / Mathf.Max(1, Screen.width)));

            var shader = Core.AssetManager.GetShader("bettrfg_backdropgradient");
            if (shader != null)
            {
                var mat = new Material(shader);
                // solid at the very bottom, faded to nothing by the middle, nothing above.
                mat.SetFloat("_FadeInStart", 0.0f);
                mat.SetFloat("_FadeInEnd", 0.0f);
                mat.SetFloat("_FadeOutStart", 0.0f);
                mat.SetFloat("_FadeOutEnd", 0.5f);
                pimg.material = mat;
            }

            var scroll = go.AddComponent<BetterFG.UI.Components.MoveScrollUvRaw>();
            scroll.speed = new Vector2(0.04f, 0.02f);
        }

        // swap the backdrop pattern live and persist the choice. index clamps into BackdropPatterns.
        public void SetBackdrop(int index)
        {
            index = Mathf.Clamp(index, 0, BackdropPatterns.Length - 1);
            Services.SettingsService.Set(KEY_BACKDROP, index.ToString());
            if (_backdropImg == null) return;
            var tex = BetterFG.Utilities.EmbeddedResourceandUnity.LoadTexture(BackdropPatterns[index]);
            if (tex == null) return;
            tex.wrapMode = TextureWrapMode.Repeat;
            _backdropImg.texture = tex;
        }

        public void SetBackdropOpacity(float alpha)
        {
            alpha = Mathf.Clamp01(alpha);
            Services.SettingsService.Set(KEY_BACKDROP_OPACITY,
                alpha.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (_backdropImg != null) _backdropImg.color = new Color(1f, 1f, 1f, alpha);
        }

        // ── Watermark ─────────────────────────────────────────────────────────
        private void BuildWatermark()
        {
            _watermarkGo = new GameObject("Watermark");
            _watermarkGo.transform.SetParent(_canvas.transform, false);

            var rt = _watermarkGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.25f, 1f);
            rt.anchorMax = new Vector2(0.25f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(0f, -16f);
            rt.sizeDelta = new Vector2(600f, 48f);

            var vlg = _watermarkGo.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 1f;

            var csf = _watermarkGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            AddWatermarkLine(BetterFGInfo.WatermarkLine1, new Color(1f, 1f, 1f, 0.28f));
            AddWatermarkLogoLine(BetterFGInfo.WatermarkLine2, new Color(1f, 1f, 1f, 0.28f));
            AddWatermarkLine(BetterFGInfo.WatermarkLine3, new Color(1f, 1f, 0f, 0.28f));
        }

        // line2 with the "BettrFG" name drawn as the logo image. a horizontal layout group
        // lays the logo and the build text side by side so the logo actually pushes the text.
        private void AddWatermarkLogoLine(string text, Color color)
        {
            var row = new GameObject("WMLogoLine");
            row.transform.SetParent(_watermarkGo.transform, false);
            row.AddComponent<RectTransform>();
            row.AddComponent<LayoutElement>().preferredHeight = 18f;
            var hlg = row.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.spacing = 5f;

            var tex = BetterFG.Utilities.EmbeddedResourceandUnity.LoadTexture("BetterFG.assets.ui.betterfglogo.png");
            if (tex != null)
            {
                float h = 16f, w = h * ((float)tex.width / tex.height);
                var logo = new GameObject("WMLogo");
                logo.transform.SetParent(row.transform, false);
                logo.AddComponent<RectTransform>().sizeDelta = new Vector2(w, h);
                var le = logo.AddComponent<LayoutElement>();
                le.preferredWidth = w;
                le.preferredHeight = h;
                var img = logo.AddComponent<RawImage>();
                img.texture = tex;
                img.color = color;
                img.raycastTarget = false;
            }

            var t = UGUIShip.CreateLabel(row.transform, default, text, 12, color, TextAnchor.MiddleLeft);
            t.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void BuildControlsWatermark()
        {
            _controlsWatermarkGo = new GameObject("ControlsWatermark");
            _controlsWatermarkGo.transform.SetParent(_canvas.transform, false);

            var rt = _controlsWatermarkGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(16f, -92f);
            rt.sizeDelta = new Vector2(420f, 64f);

            var vlg = _controlsWatermarkGo.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 1f;

            var csf = _controlsWatermarkGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

            PopulateControlsWatermark();
            KeybindService.OnRebound += _ => PopulateControlsWatermark();
        }

        private void PopulateControlsWatermark()
        {
            if (_controlsWatermarkGo == null) return;
            for (int i = _controlsWatermarkGo.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_controlsWatermarkGo.transform.GetChild(i).gameObject);

            var faded = new Color(1f, 1f, 1f, 0.8f);
            AddWatermarkLine(_controlsWatermarkGo.transform, "F1 - lock/unlock cursor", faded);
            AddWatermarkLine(_controlsWatermarkGo.transform, KeybindService.Label(KeybindId.ToggleUI) + " - hide/show BetterFG UI", faded);
            AddWatermarkLine(_controlsWatermarkGo.transform, KeybindService.Label(KeybindId.ToggleWheel) + " - toggle settings wheel", faded);
        }

        // ── Input-restricted hint ──────────────────────────────────────────────
        // pulsing text in the middle of the screen telling the player their input is locked
        // while the BettrFG UI is open, and how to toggle it off.
        private void BuildCreativeInputHint()
        {
            _creativeHintGo = new GameObject("CreativeInputHint");
            _creativeHintGo.hideFlags = HideFlags.HideAndDontSave;
            _creativeHintGo.transform.SetParent(_overlayCanvas.transform, false);

            var rt = _creativeHintGo.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(120f, 200f); // up a bit, nudged right
            rt.sizeDelta = new Vector2(1100f, 60f);

            _creativeHintText = _creativeHintGo.AddComponent<TextMeshProUGUI>();
            _creativeHintText.alignment = TextAlignmentOptions.Center;
            _creativeHintText.fontSize = 18;
            _creativeHintText.fontStyle = FontStyles.Bold;
            _creativeHintText.color = new Color(1f, 1f, 0f, 1f);
            _creativeHintText.raycastTarget = false;
            // font + outline get set lazily in EnsureHintStyle (Asap isn't loaded this early)

            _creativeHintGo.AddComponent<BetterFG.UI.Components.AlphaPulseContinuousFade>();

            UpdateCreativeHintText();
            KeybindService.OnRebound += _ => UpdateCreativeHintText();

            _creativeHintGo.SetActive(false);
        }

        private void UpdateCreativeHintText()
        {
            if (_creativeHintText == null) return;
            // the UI toggle also closes the wheel, so if the UI is up one key does everything.
            // only when the UI is closed and just the wheel is showing do you need the wheel key.
            var key = _visible ? KeybindId.ToggleUI : KeybindId.ToggleWheel;
            _creativeHintText.text = "Input restricted. Press " + KeybindService.Label(key) + " to toggle";
        }

        private bool _hintStyled;
        private void EnsureHintStyle()
        {
            if (_hintStyled || _creativeHintText == null) return;
            var f = GameAsapFont();
            if (f == null) return;

            _creativeHintText.font = f;
            _ = _creativeHintText.fontMaterial;
            _creativeHintText.fontMaterial.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.4f));
            _creativeHintText.fontMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.6f);
            _creativeHintText.fontMaterial.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
            _creativeHintText.fontMaterial.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0f);
            _creativeHintText.fontMaterial.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            _hintStyled = true;
        }

        private static TMP_FontAsset _asapFont;

        // the game's Asap font is loaded by the time the main menu exists, so the whole-heap scan
        // runs exactly once from the menu-entered hub — everything else just reads the cache
        public static void ResolveAsapFont()
        {
            if (_asapFont != null) return;
            try
            {
                foreach (var fa in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (fa == null || string.IsNullOrEmpty(fa.name)) continue;
                    if (fa.name.IndexOf("asap", StringComparison.OrdinalIgnoreCase) >= 0) { _asapFont = fa; break; }
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("BetterFG: asap font: " + ex.Message); }
            if (_asapFont == null) Plugin.Log.LogWarning("asap font not found at menu entry?");
        }

        private static TMP_FontAsset GameAsapFont() => _asapFont;

        private void AddWatermarkLine(string text, Color color)
            => AddWatermarkLine(_watermarkGo.transform, text, color);

        private void AddWatermarkLine(Transform parent, string text, Color color)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            var t = UGUIShip.CreateFlowLabel(parent, text, 12, color);
            t.GetComponent<LayoutElement>().preferredHeight = 18f;
        }

        private static Canvas CreateCanvas()
        {
            var go = new GameObject("BetterFG_Canvas");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = BetterFG.Services.UIScaleService.CurrentRef;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }
    }

    // ── Notifications ─────────────────────────────────────────────────────────
    public static class BetterFGNotif
    {
        private static Canvas _canvas;
        private static GameObject _stack;
        private const float NotifDuration = 4f;
        private const float FadeTime = 0.6f;

        private static void EnsureCanvas()
        {
            if (_canvas != null && _canvas.gameObject != null) return;

            var go = new GameObject("BetterFGNotif_Canvas");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 1000;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = BetterFG.Services.UIScaleService.CurrentRef;
            go.AddComponent<GraphicRaycaster>();

            // stack anchored to bottom-left
            var stackGo = new GameObject("NotifStack");
            stackGo.transform.SetParent(go.transform, false);
            var rt = stackGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(16f, -16f);
            rt.sizeDelta = new Vector2(500f, 0f);

            var vlg = stackGo.AddComponent<VerticalLayoutGroup>();
            vlg.childAlignment = TextAnchor.UpperLeft;
            vlg.spacing = 3f;
            vlg.padding = new RectOffset(0, 0, 0, 0);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var fitter = stackGo.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            _stack = stackGo;
        }

        public static void CreateNotification(string text, Color color)
        {
            EnsureCanvas();

            var itemGo = new GameObject("Notif");
            itemGo.transform.SetParent(_stack.transform, false);

            var le = itemGo.AddComponent<LayoutElement>();
            le.preferredHeight = 28f;
            le.flexibleWidth = 1f;

            var itemFitter = itemGo.AddComponent<ContentSizeFitter>();
            itemFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var txtGo = new GameObject("Text");
            txtGo.transform.SetParent(itemGo.transform, false);
            var trt = txtGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;

            var label = txtGo.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = 17;
            label.color = color;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.raycastTarget = false;
            label.outlineColor = new Color32(0, 0, 0, 255);
            label.outlineWidth = 0.2f;

            BetterFGUIMan.Instance.StartCoroutine(
                NotifLifetime(itemGo, label, NotifDuration, FadeTime).WrapToIl2Cpp()
            );
        }

        private static IEnumerator NotifLifetime(GameObject item, TextMeshProUGUI label, float duration, float fadeTime)
        {
            yield return new WaitForSeconds(duration);

            float elapsed = 0f;
            Color startColor = label.color;
            Color32 startOutline = label.outlineColor;
            while (elapsed < fadeTime)
            {
                elapsed += Time.deltaTime;
                float a = 1f - (elapsed / fadeTime);
                label.color = new Color(startColor.r, startColor.g, startColor.b, a);
                label.outlineColor = new Color32(0, 0, 0, (byte)(255 * a));
                yield return null;
            }

            UnityEngine.Object.Destroy(item);
        }
    }
}
