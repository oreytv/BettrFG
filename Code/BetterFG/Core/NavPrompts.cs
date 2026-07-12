using System;
using System.Collections.Generic;
using FallGuysLib.UI;
using FG.Common.CMS;
using FG.Common.UI;
using FGClient.UI.Core;
using Rewired;
using UnityEngine;
using Il2CppAemList = Il2CppSystem.Collections.Generic.List<Rewired.ActionElementMap>;

namespace BetterFG.Core
{
    // shared core for "spawn an FG nav-prompt + listen for its input" — used by Leave-on-loading,
    // the qual-screen Set-As-PB prompt, and anything else we add later. before this every caller
    // was hand-rolling: clone NavigationPromptData, register a CMS string, find NavigationOverlayManager,
    // pull _navPromptPrefab, instantiate, anchor + position the RectTransform, set up auto-resize for
    // long labels, and walk every joystick's action-element-map to bypass disabled Rewired categories.
    //
    // The single entry point is NavPromptCore.From(...). Chain options, finish with SpawnOn(parent).
    // The returned NavPromptHandle is the source of truth: poll IsPressed (or set OnPressed), call
    // Destroy when done. Don't go around it.
    public static class NavPromptCore
    {
        // built-in glyph sources we know about. NavPrompt is the in-game enum; cast int for any
        // value not in the enum (Favourite = 22 etc).
        public const NavPrompt Favourite = (NavPrompt)22;

        public static NavPromptBuilder From(NavPrompt source) => new NavPromptBuilder(source);

        // shared CMS-strings table lookup. keys are registered once per process, value-or-overwrite.
        internal static void RegisterCmsString(string key, string value)
        {
            var strings = CMSLoader.Instance._localisedStrings._localisedStrings;
            if (!strings.ContainsKey(key)) strings.Add(key, value);
        }

        // clone cache so we don't churn NavigationPromptData instances across re-spawns. keyed by
        // (source-glyph, cms-key) since two callers using the same glyph but different labels are
        // distinct clones.
        private static readonly Dictionary<(NavPrompt, string), NavigationPromptData> _cloneCache
            = new Dictionary<(NavPrompt, string), NavigationPromptData>();

        internal static NavigationPromptData GetOrCloneData(NavPrompt source, string labelKey, string labelText)
        {
            var key = (source, labelKey);
            if (_cloneCache.TryGetValue(key, out var existing) && existing != null) return existing;

            var mgr = UnityEngine.Object.FindObjectOfType<NavigationOverlayManager>();
            if (mgr == null) return null;
            var dict = mgr._navPromptsDictionary;
            if (dict == null || !dict.TryGetValue(source, out var srcData) || srcData == null) return null;

            RegisterCmsString(labelKey, labelText);
            var clone = UnityEngine.Object.Instantiate(srcData);
            clone.LocalisationKey = labelKey;
            UnityEngine.Object.DontDestroyOnLoad(clone);
            _cloneCache[key] = clone;
            return clone;
        }

        internal static GameObject GetPromptPrefab()
        {
            var mgr = UnityEngine.Object.FindObjectOfType<NavigationOverlayManager>();
            if (mgr == null) return null;
            var prefab = mgr._navPromptPrefab;
            return prefab != null ? prefab.gameObject : null;
        }

        // shared full-screen container under the game's UICanvas_Client_V2 root. lives there so
        // every prompt parented to it inherits the canvas scaleFactor BettrFG sets. recreated per
        // scene since the canvas itself is re-instantiated; find-or-create.
        internal static Transform GetCustomNavPromptRoot()
        {
            var canvas = GameObject.Find("UICanvas_Client_V2(Clone)");
            if (canvas == null) return null;

            var existing = canvas.transform.Find("CustomNavPrompt");
            if (existing != null) return existing;

            var go = new GameObject("CustomNavPrompt");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(canvas.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.SetAsLastSibling();
            return rt;
        }
    }

    // anchor presets for the four corners + center-bottom. callers can override with raw anchor/
    // pivot/offset on the builder if they need something exotic.
    public enum NavPromptAnchor
    {
        BottomRight,
        BottomLeft,
        BottomCenter,
        TopRight,
        TopLeft,
        Custom,
    }

    public sealed class NavPromptBuilder
    {
        internal readonly NavPrompt Source;
        internal string LabelText = "Action";
        internal string LabelKey;
        internal NavPromptAnchor Anchor = NavPromptAnchor.BottomRight;
        internal Vector2? Offset;       // null = use the anchor's default edge margin
        internal Vector2? CustomAnchorMin;
        internal Vector2? CustomAnchorMax;
        internal Vector2? CustomPivot;
        internal Action OnPressed;
        internal bool OwnCanvas;       // spawn under the shared CustomNavPrompt container on the game UICanvas
        internal bool ResizeForLongLabel = true;
        internal float Width = 360f;
        internal bool AcceptEscapeKey;          // also fires when keyboard Escape is pressed
        // default on: ignore presses while the top surface on UICanvas_Client_V2/Default isn't
        // focused (menu open, popup up, etc.). opt out with .AllowWhileUnfocused() for prompts
        // that need to fire from a covering UI (LeaveOnLoadingScreen etc).
        internal bool RequireGameplayFocus = true;
        internal Func<string, bool> ElementNameFilter; // optional gate: only accept buttons whose Rewired name matches
        internal int[] PollActionIdOverride;    // poll these Rewired action ids instead of the NavigationPromptData's own

        internal NavPromptBuilder(NavPrompt source) { Source = source; }

        // labelKey doubles as both the CMS dictionary key (must be unique per text) and the cache key.
        // If you reuse a key, you reuse the cloned data — fine for the same label, surprising otherwise.
        public NavPromptBuilder WithLabel(string text, string cmsKey)
        {
            LabelText = text;
            LabelKey = cmsKey;
            return this;
        }

        // offset optional — leave it null and the anchor's default edge margin is used.
        public NavPromptBuilder AnchoredAt(NavPromptAnchor anchor, Vector2? offset = null)
        {
            Anchor = anchor;
            Offset = offset;
            return this;
        }

        // raw RectTransform setup for callers who need something the presets don't cover.
        public NavPromptBuilder CustomAnchors(Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 offset)
        {
            Anchor = NavPromptAnchor.Custom;
            CustomAnchorMin = anchorMin;
            CustomAnchorMax = anchorMax;
            CustomPivot = pivot;
            Offset = offset;
            return this;
        }

        public NavPromptBuilder OnPress(Action cb) { OnPressed = cb; return this; }

        // spawn under the shared CustomNavPrompt container on the game's UICanvas instead of the
        // caller-provided parent, so the prompt inherits BettrFG's canvas scaling and survives the
        // UI it floats over (e.g. the leave prompt during loading screens).
        public NavPromptBuilder OnOwnCanvas()
        {
            OwnCanvas = true;
            return this;
        }

        public NavPromptBuilder Width_(float width) { Width = width; return this; }

        public NavPromptBuilder NoAutoResize() { ResizeForLongLabel = false; return this; }

        public NavPromptBuilder AlsoAcceptEscape() { AcceptEscapeKey = true; return this; }

        // opt out of the default focus gate. only for prompts that must fire while another surface
        // has focus (loading-screen leave prompt etc).
        public NavPromptBuilder AllowWhileUnfocused() { RequireGameplayFocus = false; return this; }

        // filter joystick button presses by Rewired element name. used by Leave-on-loading to reject
        // X-on-PS (whose elementIdentifierId collides with B-on-Xbox across layouts) and only accept
        // Circle / B / generic-Button-1.
        public NavPromptBuilder FilterElement(Func<string, bool> predicate)
        {
            ElementNameFilter = predicate;
            return this;
        }

        // poll these Rewired action ids instead of the NavigationPromptData's own. needed when the
        // glyph's source data doesn't map cleanly to a controller binding in the disabled-category
        // poll path — e.g. NavPrompt.Back's InputActions don't include Menu_UICancel, so on
        // controller no joystick map fires. callers know which action they actually want.
        public NavPromptBuilder PollActions(params int[] actionIds)
        {
            PollActionIdOverride = actionIds;
            return this;
        }

        public NavPromptHandle SpawnOn(Transform parent)
        {
            if (string.IsNullOrEmpty(LabelKey))
                LabelKey = "bfg_navprompt_" + LabelText.ToLowerInvariant().Replace(' ', '_');

            var data = NavPromptCore.GetOrCloneData(Source, LabelKey, LabelText);
            if (data == null) return null;

            var prefab = NavPromptCore.GetPromptPrefab();
            if (prefab == null) return null;

            Transform actualParent = parent;
            if (OwnCanvas)
            {
                // sit under the game's own UICanvas so we inherit its scaleFactor (BettrFG canvas
                // scaling lives there). A standalone overlay canvas would ignore that and stay at
                // stock size. CustomNavPrompt is a shared stretch-to-fill container we create once.
                actualParent = NavPromptCore.GetCustomNavPromptRoot();
                if (actualParent == null) return null;
            }

            var go = UnityEngine.Object.Instantiate(prefab, actualParent);
            go.name = "BettrFG_NavPrompt_" + LabelKey;

            var rt = go.GetComponent<RectTransform>();
            if (rt != null) ApplyAnchors(rt);

            if (ResizeForLongLabel && rt != null)
            {
                rt.sizeDelta = new Vector2(Width, rt.sizeDelta.y);
                foreach (var le in go.GetComponentsInChildren<UnityEngine.UI.LayoutElement>(true))
                {
                    le.minWidth = -1f;
                    le.preferredWidth = -1f;
                }
                foreach (var tmp in go.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                {
                    tmp.enableWordWrapping = false;
                    tmp.overflowMode = TMPro.TextOverflowModes.Overflow;
                }
            }

            // hand the button's own callback a flag flip — NavigationPromptButton wires Rewired
            // resolution internally, so whatever binding the glyph shows on THIS controller layout is
            // the same input it fires on. we set _gamePressed; the handle drains it in IsPressed().
            // ALSO stash the button on the handle so IsPressed can read its live elementIdentifierId
            // and poll that raw controller button directly — needed for glyphs like LE_Down whose
            // NavigationPromptData has no InputActions so the callback never fires on its own.
            var handle = new NavPromptHandle(go, data, OnPressed, AcceptEscapeKey, ElementNameFilter, PollActionIdOverride, RequireGameplayFocus);
            var btn = go.GetComponent<NavigationPromptButton>();
            if (btn != null) btn.Init(data, (Il2CppSystem.Action)(() => handle.MarkGamePressed()));
            handle.AttachPromptButton(btn);
            go.SetActive(true);

            return handle;
        }

        private void ApplyAnchors(RectTransform rt)
        {
            if (Anchor == NavPromptAnchor.Custom && CustomAnchorMin.HasValue)
            {
                rt.anchorMin = CustomAnchorMin.Value;
                rt.anchorMax = CustomAnchorMax.Value;
                rt.pivot = CustomPivot.Value;
                rt.anchoredPosition = Offset ?? Vector2.zero;
                return;
            }
            // default edge margin per anchor when the caller didn't pass an offset. sign points
            // inward from whichever corner/edge the anchor sits on.
            const float M = 70f;
            Vector2 a; Vector2 p; Vector2 def;
            switch (Anchor)
            {
                case NavPromptAnchor.BottomLeft:   a = new Vector2(0f, 0f);   p = new Vector2(0f, 0f);   def = new Vector2( M,  M); break;
                case NavPromptAnchor.BottomCenter: a = new Vector2(0.5f, 0f); p = new Vector2(0.5f, 0f); def = new Vector2( 0f,  M); break;
                case NavPromptAnchor.TopRight:     a = new Vector2(1f, 1f);   p = new Vector2(1f, 1f);   def = new Vector2(-M, -M); break;
                case NavPromptAnchor.TopLeft:      a = new Vector2(0f, 1f);   p = new Vector2(0f, 1f);   def = new Vector2( M, -M); break;
                default:                           a = new Vector2(1f, 0f);   p = new Vector2(1f, 0f);   def = new Vector2(-M,  M); break; // BottomRight
            }
            rt.anchorMin = a;
            rt.anchorMax = a;
            rt.pivot = p;
            rt.anchoredPosition = Offset ?? def;
        }
    }

    // handle is the source of truth for the spawned prompt. callers either poll IsPressed each
    // frame, or set OnPress at build time and let the handle invoke it via Tick().
    public sealed class NavPromptHandle
    {
        public GameObject GameObject { get; private set; }
        private readonly NavigationPromptData _data;
        private readonly Action _onPressed;
        private readonly bool _acceptEscape;
        private readonly Func<string, bool> _elementFilter;
        private readonly int[] _pollActionIds;
        private readonly bool _requireGameplayFocus;

        // shared per-handle to avoid allocating a fresh Il2Cpp list every poll
        private readonly Il2CppAemList _aemBuf = new Il2CppAemList();

        internal NavPromptHandle(GameObject go, NavigationPromptData data,
            Action onPressed, bool acceptEscape, Func<string, bool> elementFilter, int[] pollActionIds,
            bool requireGameplayFocus)
        {
            GameObject = go;
            _data = data;
            _onPressed = onPressed;
            _acceptEscape = acceptEscape;
            _elementFilter = elementFilter;
            _pollActionIds = pollActionIds;
            _requireGameplayFocus = requireGameplayFocus;
        }

        // true when whatever surface currently owns input on the UI canvas is focused. shared
        // across every prompt in the process — GameObject.Find + GetComponentInChildren are heap
        // scans, so doing them per-handle per-frame builds cost linearly with prompt count. cache
        // the resolved refs and re-resolve only when they've been destroyed. we also throttle to
        // once per frame via Time.frameCount.
        private static int _focusCachedFrame = -1;
        private static bool _focusCachedResult;
        private static GameObject _focusBanners;
        private static readonly Transform[] _focusRootTransforms = new Transform[2];
        private static readonly FocusableViewModel[] _focusVms = new FocusableViewModel[2];
        private static readonly string[] _focusRootPaths = { "UICanvas_Client_V2(Clone)/Default", "UICanvas_Client_V2(Clone)/LoadingScreen" };

        private bool GameplayFocused()
        {
            int f = Time.frameCount;
            if (f == _focusCachedFrame) return _focusCachedResult;
            _focusCachedFrame = f;
            _focusCachedResult = ComputeFocused();
            return _focusCachedResult;
        }

        private static bool ComputeFocused()
        {
            // BannersState = elimination/qualification banner is playing. game is fully in gameplay
            // input mode here regardless of what any FocusableViewModel says. short-circuit true.
            if (_focusBanners == null)
                _focusBanners = GameObject.Find("UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates/BannersState");
            if (_focusBanners != null && _focusBanners.activeInHierarchy) return true;

            for (int r = 0; r < _focusRootPaths.Length; r++)
            {
                // cached FocusableViewModel is the winner from a prior frame — if it's still alive,
                // reuse it. cheaper than re-walking children every frame.
                var vm = _focusVms[r];
                if (vm != null) return vm._isInFocus;

                var root = _focusRootTransforms[r];
                if (root == null)
                {
                    var go = GameObject.Find(_focusRootPaths[r]);
                    if (go == null) continue;
                    root = _focusRootTransforms[r] = go.transform;
                }

                for (int i = 0; i < root.childCount; i++)
                {
                    vm = root.GetChild(i).GetComponentInChildren<FocusableViewModel>(false);
                    if (vm != null) { _focusVms[r] = vm; return vm._isInFocus; }
                }
            }
            return false;
        }

        public bool IsAlive => GameObject != null;

        // NavigationPromptButton's own Init callback pokes this; IsPressed drains it once. that way
        // whatever binding the glyph resolves to on the current controller layout fires us — no
        // guessing at action ids, no falling out when a NavPrompt like LE_Down has no InputActions.
        private bool _gamePressed;
        internal void MarkGamePressed() { _gamePressed = true; }

        // the live NavigationPromptButton — we read its current elementIdentifierId every frame so
        // the raw-element poll follows controller layout swaps without stale ids.
        private NavigationPromptButton _promptButton;
        internal void AttachPromptButton(NavigationPromptButton btn) { _promptButton = btn; }

        // poll once. returns true if the prompt's action fired this frame. fires _onPressed too
        // if one was attached at build time.
        public bool IsPressed()
        {
            if (!IsAlive) return false;
            // still drain _gamePressed so a stale press from an unfocused frame doesn't fire the
            // instant focus returns; we just discard the result.
            bool gated = _requireGameplayFocus && !GameplayFocused();
            if (_gamePressed) { _gamePressed = false; if (gated) return false; _onPressed?.Invoke(); return true; }
            if (gated) return false;
            if (_acceptEscape && Input.GetKeyDown(KeyCode.Escape)) { _onPressed?.Invoke(); return true; }
            if (PollData()) { _onPressed?.Invoke(); return true; }
            return false;
        }

        // poll the actions declared on the NavigationPromptData so whatever glyph is currently
        // showing (keyboard or controller) is exactly what we accept here. survives a disabled
        // Rewired UI category by walking GetButtonMapsWithAction directly on each joystick + keyboard.
        // when an element-name filter is set we only accept presses on buttons whose Rewired name
        // matches — needed because across controller layouts the same elementIdentifierId means
        // Circle on one and Cross on another, and "Menu_UICancel" is bound to both.
        private bool PollData()
        {
            if (!ReInput.isReady || ReInput.players.playerCount == 0) return false;
            var p = ReInput.players.GetPlayer(0);

            int actionCount;
            if (_pollActionIds != null && _pollActionIds.Length > 0)
                actionCount = _pollActionIds.Length;
            else
            {
                if (_data == null) return false;
                var dataActions = _data.InputActions;
                if (dataActions == null || dataActions.Length == 0) return false;
                actionCount = dataActions.Length;
            }

            for (int a = 0; a < actionCount; a++)
            {
                int actionId = _pollActionIds != null && _pollActionIds.Length > 0
                    ? _pollActionIds[a]
                    : _data.InputActions[a];
                // GetButtonDown(actionId) only fires when the action's input category is enabled;
                // during loading states the Menu category is disabled, so we fall through to the
                // direct-poll path below if it returns false.
                if (_elementFilter == null && p.GetButtonDown(actionId)) return true;

                var sticks = p.controllers.Joysticks;
                int n = p.controllers.joystickCount;
                for (int i = 0; i < n; i++)
                {
                    var j = sticks[i];
                    _aemBuf.Clear();
                    int got = p.controllers.maps.GetButtonMapsWithAction(j, actionId, false, _aemBuf);
                    for (int k = 0; k < got; k++)
                    {
                        var aem = _aemBuf[k];
                        if (aem == null) continue;
                        if (!j.GetButtonDownById(aem.elementIdentifierId)) continue;
                        if (_elementFilter != null)
                        {
                            string elName = null;
                            try { elName = j.GetElementIdentifierById(aem.elementIdentifierId)?.name; } catch { }
                            if (!_elementFilter(elName)) continue;
                        }
                        return true;
                    }
                }

                var kb = p.controllers.Keyboard;
                if (kb != null)
                {
                    _aemBuf.Clear();
                    int got = p.controllers.maps.GetButtonMapsWithAction(kb, actionId, false, _aemBuf);
                    for (int k = 0; k < got; k++)
                    {
                        var aem = _aemBuf[k];
                        if (aem != null && kb.GetButtonDownById(aem.elementIdentifierId)) return true;
                    }
                }
            }
            return false;
        }

        public void Destroy()
        {
            // only kill our own prompt — the CustomNavPrompt container is shared, leave it.
            if (GameObject != null) UnityEngine.Object.Destroy(GameObject);
            GameObject = null;
        }
    }
}
