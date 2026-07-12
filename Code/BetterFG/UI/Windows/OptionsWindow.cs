using System;
using BetterFG.Services;
using BetterFG.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Windows
{
    public class OptionsWindow : BetterFGWindow
    {
        public OptionsWindow(IntPtr ptr) : base(ptr) { }

        protected override float WindowWidth => 280f;
        protected override float WindowHeight => 160f;
        protected override string WindowTitle => "BettrFG Options";
        protected override string BgResourceName => "BetterFG.assets.ui.windows.generalbg.png";

        protected override void BuildContent(RectTransform contentRoot)
        {
            BgPosition = new Vector3(139.3993f, 55.0135f, 0f);
            BgScale = new Vector3(1.3415f, 5.3623f, 1f);
            ContentPosition = new Vector3(-1.6132f, -17.32f, 0f);
            ContentScale = new Vector3(1.0473f, 1.04f, 1f);
            ContentOffsetMin = new Vector2(-1.6132f, -25.92f);
            ContentOffsetMax = new Vector2(-1.6132f, -18.72f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(20.0368f, -6.7966f, 0f);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            const float SEARCH_H = 22f;
            const float SEARCH_PAD = 3f;

            var searchField = UGUIShip.CreateInputField(contentRoot,
                new Rect(0f, 0f, 100f, SEARCH_H),
                "search options...", new Color(0f, 0f, 0f, 0.75f), Color.white, 12);
            var sfRt = searchField.GetComponent<RectTransform>();
            sfRt.anchorMin = new Vector2(0f, 1f);
            sfRt.anchorMax = new Vector2(1f, 1f);
            sfRt.pivot = new Vector2(0.5f, 1f);
            sfRt.offsetMin = new Vector2(SEARCH_PAD, -(SEARCH_H + SEARCH_PAD));
            sfRt.offsetMax = new Vector2(-SEARCH_PAD, -SEARCH_PAD);

            var scroll = UGUIShip.CreateScrollView(contentRoot,
                new Rect(0f, 0f, WindowWidth, WindowHeight - TITLE_H - SEARCH_H - SEARCH_PAD * 2f));
            var scrollRt = scroll.scrollRect.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = new Vector2(0f, -(SEARCH_H + SEARCH_PAD * 2f));

            // search field MUST sit on top in sibling order so the scroll viewport doesn't catch
            // its click events (children render+raycast in draw order, last sibling wins).
            sfRt.SetAsLastSibling();

            var listRt = scroll.content;
            var vlg = listRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            var csf = listRt.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            OptionsWindowBuilder.BuildRows(listRt);

            var listCapture = listRt;
            searchField.onValueChanged.AddListener(new Action<string>(q =>
                OptionsWindowBuilder.Filter(listCapture, q ?? "")));
        }
    }

    internal static class OptionsWindowBuilder
    {
        private const float ROW_H = 22f;
        private const float BTN_W = 80f;
        private const float BTN_H = 16f;
        private const float PAD = 6f;
        private const float HEADER_H = 18f;       // tall enough that the scaled title isn't clipped
        private const float HEADER_LEFT = 22f;    // push right so the sidewheel frame doesn't cut it
        private const float HEADER_SCALE = 1.3f;  // literal Text gameObject localScale bump
        private static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color ROW_ODD = new Color(0f, 0f, 0f, 0f);
        private static readonly Color BTN_IDLE = new Color(0.25f, 0.45f, 0.75f, 1f);
        private static readonly Color BTN_RECORDING = new Color(0.85f, 0.55f, 0.2f, 1f);

        private const string KEY_MAX_TABS = "ui.maxtabs";

        private struct Entry
        {
            public KeybindId Id;
            public string Label;
            public bool RequireShift;
        }

        // show/hide rows by label match. rows are named "<kind>_<label>", headers "Header_<TITLE>".
        // a header stays visible only if at least one row before the next header matches.
        public static void Filter(RectTransform parent, string query)
        {
            string q = query.Trim().ToLowerInvariant();
            int n = parent.childCount;

            var go = new GameObject[n];
            var isHeader = new bool[n];
            var matches = new bool[n];
            for (int i = 0; i < n; i++)
            {
                go[i] = parent.GetChild(i).gameObject;
                string name = go[i].name;
                int us = name.IndexOf('_');
                string label = us >= 0 ? name.Substring(us + 1) : name;
                isHeader[i] = name.StartsWith("Header_", StringComparison.Ordinal);
                matches[i] = q.Length == 0 || label.ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) >= 0;
            }

            // a header matching the query pulls in its whole section; then a row shows if it
            // matches directly or its section header did.
            for (int i = 0; i < n; i++)
            {
                if (!isHeader[i]) continue;
                if (!matches[i]) continue;
                for (int j = i + 1; j < n && !isHeader[j]; j++)
                    matches[j] = true;
            }

            for (int i = 0; i < n; i++)
            {
                if (isHeader[i])
                {
                    bool anyBelow = false;
                    for (int j = i + 1; j < n && !isHeader[j]; j++)
                        if (matches[j]) { anyBelow = true; break; }
                    go[i].SetActive(q.Length == 0 || anyBelow);
                }
                else
                {
                    go[i].SetActive(matches[i]);
                }
            }
        }

        public static void BuildRows(RectTransform parent)
        {
            // ── Scale ─────────────────────────────────────────────────────────
            BuildHeader(parent, "SCALE");
            const float uiMin = 0.6f, uiMax = 1.8f;
            BuildSliderRow(parent, "BettrFG UI", ROW_EVEN, uiMin, uiMax,
                () => UIScaleService.Current,
                v => UIScaleService.SetValue(v),
                v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                () => UIScaleService.SetEnabled(true));

            // ── Customization ─────────────────────────────────────────────────
            BuildHeader(parent, "CUSTOMIZATION");
            BuildBackdropRow(parent, ROW_ODD);
            BuildSliderRow(parent, "BG opacity", ROW_EVEN, 0f, 1f,
                () => BetterFGUIMan.BackdropOpacity,
                v => { if (BetterFGUIMan.Instance != null) BetterFGUIMan.Instance.SetBackdropOpacity(v); },
                v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));

            // ── Tab hover background ──────────────────────────────────────────
            // the faint pic behind a tab title on hover. optionally keep it visible always at a
            // low idle opacity, and tint it any colour (applies hovered or not).
            BuildHeader(parent, "TAB HOVER BG");
            TabHoverStyle.LoadFromSettings();
            var alwaysRow = BuildToggleRow(parent, "Always show", ROW_EVEN,
                () => TabHoverStyle.AlwaysShow,
                v => { TabHoverStyle.AlwaysShow = v; TabHoverStyle.Save(); TabHoverStyle.ApplyAll(); });

            // live tint swatch, sat just left of the toggle button
            const float swW = 18f, toggleW = 44f;
            var swGo = new GameObject("TintSwatch");
            swGo.transform.SetParent(alwaysRow, false);
            var swRt = swGo.AddComponent<RectTransform>();
            swRt.anchorMin = swRt.anchorMax = new Vector2(1f, 0.5f);
            swRt.pivot = new Vector2(1f, 0.5f);
            swRt.anchoredPosition = new Vector2(-(PAD + toggleW + PAD), 0f);
            swRt.sizeDelta = new Vector2(swW, BTN_H);
            var swImg = swGo.AddComponent<Image>();
            swImg.color = TabHoverStyle.Tint;

            BuildSliderRow(parent, "Idle opacity", ROW_ODD, 0f, 1f,
                () => TabHoverStyle.IdleAlpha,
                v => { TabHoverStyle.IdleAlpha = v; TabHoverStyle.Save(); TabHoverStyle.ApplyAll(); },
                v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            BuildColorRow(parent, "Tint", ROW_EVEN,
                () => TabHoverStyle.Tint,
                c => { TabHoverStyle.Tint = c; TabHoverStyle.Save(); TabHoverStyle.ApplyAll(); },
                c => { if (swImg != null) swImg.color = c; });

            // ── Tabs ──────────────────────────────────────────────────────────
            BuildHeader(parent, "TABS");
            BuildIncrementerRow(parent, "Max tab amount", ROW_ODD, 1, 5,
                () => int.TryParse(SettingsService.Get(KEY_MAX_TABS, "3"), out int v) ? Mathf.Clamp(v, 1, 5) : 3,
                v =>
                {
                    if (BetterFGUIMan.Instance != null) BetterFGUIMan.Instance.SetMaxTabs(v);
                    else SettingsService.Set(KEY_MAX_TABS, v.ToString());
                });

            // ── Keybinds ──────────────────────────────────────────────────────
            BuildHeader(parent, "KEYBOARD");
            var entries = new[]
            {
                new Entry { Id = KeybindId.ToggleUI,    Label = "Toggle BettrFG UI", RequireShift = true  },
                new Entry { Id = KeybindId.ToggleWheel, Label = "Toggle Sidewheel",   RequireShift = false },
            };
            for (int i = 0; i < entries.Length; i++)
                BuildRow(parent, entries[i], i % 2 == 0 ? ROW_EVEN : ROW_ODD);

            BuildHeader(parent, "CONTROLLER");
            BuildSliderRow(parent, "Cursor speed", ROW_EVEN,
                ControllerBindService.MinCursorSpeed, ControllerBindService.MaxCursorSpeed,
                () => ControllerBindService.CursorSpeed, v => ControllerBindService.CursorSpeed = v);
            BuildSliderRow(parent, "Scroll speed", ROW_ODD,
                ControllerBindService.MinScrollSpeed, ControllerBindService.MaxScrollSpeed,
                () => ControllerBindService.ScrollSpeed, v => ControllerBindService.ScrollSpeed = v);
            BuildControllerRow(parent, ControllerBindId.ToggleUI,    "Toggle BettrFG UI", ROW_EVEN);
            BuildControllerRow(parent, ControllerBindId.ToggleWheel, "Toggle Sidewheel",  ROW_ODD);
            BuildControllerRow(parent, ControllerBindId.LeftClick,   "Left click",        ROW_EVEN);
            BuildControllerRow(parent, ControllerBindId.RightClick,  "Right click",       ROW_ODD);

            // ── Backup ────────────────────────────────────────────────────────
            BuildHeader(parent, "BACKUP");
            BuildToggleRow(parent, "Backup last.txt", ROW_EVEN,
                () => SettingsService.BackupEnabled,
                v => SettingsService.BackupEnabled = v);
            BuildIncrementerRow(parent, "Interval (minutes)", ROW_ODD, 1, 120,
                () => SettingsService.BackupIntervalMinutes,
                v => SettingsService.BackupIntervalMinutes = v);
            BuildActionRow(parent, "Open backup folder", ROW_EVEN, () =>
            {
                try
                {
                    if (!System.IO.Directory.Exists(SettingsService.BackupFolderPath))
                        System.IO.Directory.CreateDirectory(SettingsService.BackupFolderPath);
                    System.Diagnostics.Process.Start("explorer.exe", SettingsService.BackupFolderPath);
                }
                catch (Exception ex)
                {
                    Plugin.Log?.LogWarning("couldn't open backup folder: " + ex.Message);
                }
            });
        }

        // simple label + button row that just fires an action, no state to track
        private static void BuildActionRow(RectTransform parent, string label, Color bg, Action onClick)
        {
            var rowGo = new GameObject("ActionRow_" + label);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = bg;

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD + 20f, 0f, 150f, ROW_H),
                label, 13, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);

            var btnGo = new GameObject("ActionBtn");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-PAD, 0f);
            btnRt.sizeDelta = new Vector2(BTN_W, BTN_H);

            var btn = UGUIShip.CreateButton(btnGo.transform,
                new Rect(0f, 0f, BTN_W, BTN_H), "Open", BTN_IDLE, Color.white, 11);
            btn.onClick.AddListener(new Action(onClick));
        }

        // ON/OFF toggle row, right-aligned button mirroring the EmoticonsPhrasesTab style.
        // returns the row transform so callers can drop extra widgets (e.g. a colour swatch) into it.
        private static Transform BuildToggleRow(RectTransform parent, string label, Color bg,
            Func<bool> get, Action<bool> set)
        {
            var rowGo = new GameObject("ToggleRow_" + label);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = bg;

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD + 20f, 0f, 200f, ROW_H),
                label, 13, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);

            var toggleW = 44f;
            var btnGo = new GameObject("Toggle");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-PAD, 0f);
            btnRt.sizeDelta = new Vector2(toggleW, BTN_H);

            bool state = get();
            var onColor = new Color(0.25f, 0.5f, 0.25f, 1f);
            var offColor = new Color(0.28f, 0.28f, 0.28f, 1f);
            var btn = UGUIShip.CreateButton(btnGo.transform,
                new Rect(0f, 0f, toggleW, BTN_H),
                state ? "ON" : "OFF",
                state ? onColor : offColor, Color.white, 11);
            btn.onClick.AddListener(new Action(() =>
            {
                bool nv = !get();
                set(nv);
                Apply(btn, nv ? "ON" : "OFF", nv ? onColor : offColor);
            }));
            return rowGo.transform;
        }

        // color picker: a "<label> R" / "G" / "B" trio of the same thin slider rows used elsewhere.
        // onChange (optional) fires after each slider move with the new colour — used to live-update
        // a swatch elsewhere in the window.
        private static void BuildColorRow(RectTransform parent, string label, Color bg,
            Func<Color> get, Action<Color> set, Action<Color> onChange = null)
        {
            var innerSet = set;
            set = c => { innerSet(c); onChange?.Invoke(c); };
            Func<float, string> fmt = v => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            BuildSliderRow(parent, label + " R", bg, 0f, 1f,
                () => get().r, v => { var c = get(); set(new Color(v, c.g, c.b)); }, fmt);
            BuildSliderRow(parent, label + " G", bg, 0f, 1f,
                () => get().g, v => { var c = get(); set(new Color(c.r, v, c.b)); }, fmt);
            BuildSliderRow(parent, label + " B", bg, 0f, 1f,
                () => get().b, v => { var c = get(); set(new Color(c.r, c.g, v)); }, fmt);
        }

        private static void BuildHeader(RectTransform parent, string title)
        {
            var rowGo = new GameObject("Header_" + title);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = HEADER_H;
            le.flexibleWidth = 1f;

            var lbl = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(HEADER_LEFT, 0f, 200f, HEADER_H),
                title, 10,
                Color.white,
                TextAnchor.MiddleLeft);
            lbl.fontStyle = FontStyle.Bold;
            var rt = lbl.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(HEADER_LEFT, 0f);
            rt.localScale = new Vector3(HEADER_SCALE, HEADER_SCALE, 1f);
        }

        // slider row: label on the left, a slider filling the middle, current value on the right.
        // fmt formats the right-hand value text (defaults to int). onCommit, if given, fires on
        // pointer-up so live-rescaling controls don't apply on every drag tick.
        private static void BuildSliderRow(RectTransform parent, string label, Color bg,
            float min, float max, Func<float> get, Action<float> set,
            Func<float, string> fmt = null, Action onCommit = null)
        {
            if (fmt == null) fmt = v => ((int)v).ToString();

            var rowGo = new GameObject("SliderRow_" + label);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = bg;

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD + 20f, 0f, 90f, ROW_H),
                label, 13, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);

            // value label hugs the right edge
            const float valW = 38f;
            var valLbl = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(0f, 0f, valW, ROW_H),
                fmt(get()), 12, Color.white, TextAnchor.MiddleRight);
            var valRt = valLbl.GetComponent<RectTransform>();
            valRt.anchorMin = valRt.anchorMax = new Vector2(1f, 0.5f);
            valRt.pivot = new Vector2(1f, 0.5f);
            valRt.sizeDelta = new Vector2(valW, ROW_H);
            valRt.anchoredPosition = new Vector2(-PAD, 0f);

            // slider stretches between the label and the value text
            var sldGo = new GameObject("Slider");
            sldGo.transform.SetParent(rowGo.transform, false);
            var sldRt = sldGo.AddComponent<RectTransform>();
            sldRt.anchorMin = new Vector2(0f, 0.5f);
            sldRt.anchorMax = new Vector2(1f, 0.5f);
            sldRt.pivot = new Vector2(0.5f, 0.5f);
            sldRt.offsetMin = new Vector2(PAD + 110f, -5f);
            sldRt.offsetMax = new Vector2(-(PAD + valW + 6f), 5f);

            var bgGo = new GameObject("Background");
            bgGo.transform.SetParent(sldGo.transform, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = new Vector2(0f, 0.3f);
            bgRt.anchorMax = new Vector2(1f, 0.7f);
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            bgGo.AddComponent<Image>().color = new Color(0.12f, 0.12f, 0.12f, 1f);

            var fillAreaGo = new GameObject("Fill Area");
            fillAreaGo.transform.SetParent(sldGo.transform, false);
            var faRt = fillAreaGo.AddComponent<RectTransform>();
            faRt.anchorMin = new Vector2(0f, 0.3f);
            faRt.anchorMax = new Vector2(1f, 0.7f);
            faRt.offsetMin = new Vector2(2f, 0f);
            faRt.offsetMax = new Vector2(-2f, 0f);
            var fillGo = new GameObject("Fill");
            fillGo.transform.SetParent(fillAreaGo.transform, false);
            var fillRt = fillGo.AddComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
            var fillImg = fillGo.AddComponent<Image>();
            fillImg.color = BTN_IDLE;
            fillImg.raycastTarget = false;

            var hsGo = new GameObject("Handle Slide Area");
            hsGo.transform.SetParent(sldGo.transform, false);
            var hsRt = hsGo.AddComponent<RectTransform>();
            hsRt.anchorMin = Vector2.zero;
            hsRt.anchorMax = Vector2.one;
            hsRt.offsetMin = new Vector2(6f, 0f);
            hsRt.offsetMax = new Vector2(-6f, 0f);
            var handleGo = new GameObject("Handle");
            handleGo.transform.SetParent(hsGo.transform, false);
            var handleRt = handleGo.AddComponent<RectTransform>();
            handleRt.anchorMin = new Vector2(0f, 0f);
            handleRt.anchorMax = new Vector2(0f, 1f);
            handleRt.pivot = new Vector2(0.5f, 0.5f);
            handleRt.sizeDelta = new Vector2(10f, 0f);
            var handleImg = handleGo.AddComponent<Image>();
            handleImg.color = Color.white;

            var slider = sldGo.AddComponent<Slider>();
            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = false;
            slider.value = Mathf.Clamp(get(), min, max);
            slider.onValueChanged.AddListener(new Action<float>(v =>
            {
                set(v);
                valLbl.text = fmt(v);
            }));

            if (onCommit != null)
            {
                var trig = sldGo.AddComponent<UnityEngine.EventSystems.EventTrigger>();
                var up = new UnityEngine.EventSystems.EventTrigger.Entry
                { eventID = UnityEngine.EventSystems.EventTriggerType.PointerUp };
                up.callback.AddListener(new Action<UnityEngine.EventSystems.BaseEventData>(_ => onCommit()));
                trig.triggers.Add(up);
            }
        }

        // [-]  value  [+]  row, right-aligned
        private static void BuildIncrementerRow(RectTransform parent, string label, Color bg,
            int min, int max, Func<int> get, Action<int> set)
        {
            var rowGo = new GameObject("IncRow_" + label);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = bg;

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD + 20f, 0f, 150f, ROW_H),
                label, 13, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);

            const float btnW = 18f, valW = 26f;
            float ctrlW = btnW * 2f + valW;

            // right-aligned [-] value [+] via a child holder, so the shared stepper can lay out in
            // simple local coords. loops min<->max like the other increments.
            var holderGo = new GameObject("IncHolder");
            holderGo.transform.SetParent(rowGo.transform, false);
            var holderRt = holderGo.AddComponent<RectTransform>();
            holderRt.anchorMin = holderRt.anchorMax = new Vector2(1f, 0.5f);
            holderRt.pivot = new Vector2(1f, 0.5f);
            holderRt.anchoredPosition = new Vector2(-PAD, 0f);
            holderRt.sizeDelta = new Vector2(ctrlW, BTN_H);

            UGUIShip.CreateIncrement(holderGo.transform, new Rect(0f, 0f, ctrlW, BTN_H),
                min, max, get, set);
        }

        // backdrop picker: [-]  <preview>  [+]  with the pattern name under it. swaps the
        // fullscreen backdrop live and persists via BetterFGUIMan.SetBackdrop.
        private static void BuildBackdropRow(RectTransform parent, Color bg)
        {
            const float PREV = 40f, STEP = 18f;
            var rowGo = new GameObject("BackdropRow_Backdrop");
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = PREV + 8f;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = bg;

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD + 20f, 0f, 120f, PREV + 8f),
                "Backdrop", 13, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);

            int idx = BetterFGUIMan.BackdropIndex;

            var prevGo = new GameObject("Preview");
            prevGo.transform.SetParent(rowGo.transform, false);
            var prevRt = prevGo.AddComponent<RectTransform>();
            prevRt.anchorMin = prevRt.anchorMax = new Vector2(1f, 0.5f);
            prevRt.pivot = new Vector2(1f, 0.5f);
            prevRt.anchoredPosition = new Vector2(-(PAD + STEP + 4f), 6f);
            prevRt.sizeDelta = new Vector2(PREV, PREV);
            prevGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);
            var prevImg = UGUIShip.CreateImage(prevGo.transform, new Rect(0f, 0f, PREV, PREV),
                BetterFG.Utilities.EmbeddedResourceandUnity.LoadTexture(BetterFGUIMan.BackdropPatterns[idx]), "PreviewImg");
            var prevImgRt = prevImg.GetComponent<RectTransform>();
            prevImgRt.anchorMin = Vector2.zero; prevImgRt.anchorMax = Vector2.one;
            prevImgRt.offsetMin = prevImgRt.offsetMax = Vector2.zero;

            var nameLbl = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(0f, 0f, PREV + STEP * 2f, 12f),
                BetterFGUIMan.BackdropNames[idx], 9, new Color(1f, 1f, 1f, 0.7f), TextAnchor.MiddleCenter);
            var nameRt = nameLbl.GetComponent<RectTransform>();
            nameRt.anchorMin = nameRt.anchorMax = new Vector2(1f, 0.5f);
            nameRt.pivot = new Vector2(1f, 0.5f);
            nameRt.sizeDelta = new Vector2(PREV + STEP * 2f + 8f, 12f);
            nameRt.anchoredPosition = new Vector2(-PAD, -(PREV / 2f + 2f));

            // shared mutable index across the two step buttons
            int[] cur = { idx };
            void Step(string text, float anchoredX, int delta)
            {
                var go = new GameObject("Step" + text);
                go.transform.SetParent(rowGo.transform, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(anchoredX, 6f);
                rt.sizeDelta = new Vector2(STEP, STEP);
                UGUIShip.CreateButton(go.transform, new Rect(0f, 0f, STEP, STEP), text, BTN_IDLE, Color.white, 12)
                    .onClick.AddListener(new Action(() =>
                    {
                        int n = BetterFGUIMan.BackdropPatterns.Length;
                        cur[0] = ((cur[0] + delta) % n + n) % n;
                        if (BetterFGUIMan.Instance != null) BetterFGUIMan.Instance.SetBackdrop(cur[0]);
                        prevImg.texture = BetterFG.Utilities.EmbeddedResourceandUnity.LoadTexture(BetterFGUIMan.BackdropPatterns[cur[0]]);
                        nameLbl.text = BetterFGUIMan.BackdropNames[cur[0]];
                    }));
            }
            Step("-", -(PAD + STEP + PREV + 8f), -1);
            Step("+", -PAD, +1);
        }

        // controller button bind row: label + a record button showing the bound button's name
        private static void BuildControllerRow(RectTransform parent, ControllerBindId id, string label, Color bg)
        {
            var rowGo = new GameObject("CtrlRow_" + label);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = bg;

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD + 20f, 0f, 150f, ROW_H),
                label, 13, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);

            var btnGo = new GameObject("RecordBtn");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-PAD, 0f);
            btnRt.sizeDelta = new Vector2(BTN_W, BTN_H);

            var btn = UGUIShip.CreateButton(btnGo.transform,
                new Rect(0f, 0f, BTN_W, BTN_H),
                ControllerBindService.ButtonName(ControllerBindService.GetButton(id)),
                BTN_IDLE, Color.white, 9);

            var capturedBtn = btn;
            var capturedId = id;
            btn.onClick.AddListener(new Action(() =>
            {
                KeybindRecorder.BeginController(capturedId, capturedBtn,
                    onDone: () => Apply(capturedBtn, ControllerBindService.ButtonName(ControllerBindService.GetButton(capturedId)), BTN_IDLE),
                    onStart: () => Apply(capturedBtn, "Press a button…", BTN_RECORDING));
            }));
        }

        private static void BuildRow(RectTransform parent, Entry entry, Color bg)
        {
            var rowGo = new GameObject("Row_" + entry.Label);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = bg;

            // label — same offset/style as TweaksWindow
            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD + 20f, 0f, 200f, ROW_H),
                entry.Label, 13,
                new Color(1f, 1f, 1f, 0.85f),
                TextAnchor.MiddleLeft);

            // record button — right-anchored via child RectTransform
            var btnGo = new GameObject("RecordBtn");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(1f, 0.5f);
            btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-PAD, 0f);
            btnRt.sizeDelta = new Vector2(BTN_W, BTN_H);

            var btn = UGUIShip.CreateButton(btnGo.transform,
                new Rect(0f, 0f, BTN_W, BTN_H),
                CurrentLabel(entry.Id),
                BTN_IDLE, Color.white, 9);

            var capturedBtn = btn;
            var capturedId = entry.Id;

            btn.onClick.AddListener(new Action(() =>
            {
                KeybindRecorder.Begin(capturedId, capturedBtn,
                    onDone: () => Apply(capturedBtn, CurrentLabel(capturedId), BTN_IDLE),
                    onStart: () => Apply(capturedBtn, "Press a key…", BTN_RECORDING));
            }));
        }

        private static string CurrentLabel(KeybindId id)
        {
            return id == KeybindId.ToggleUI
                ? "Shift + " + KeybindService.KeyName(KeybindService.Get(id))
                : KeybindService.KeyName(KeybindService.Get(id));
        }

        private static void Apply(Button btn, string text, Color color)
        {
            if (btn == null) return;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = color;
            var cols = btn.colors;
            cols.normalColor = color;
            cols.highlightedColor = color * 1.2f;
            cols.pressedColor = color * 0.7f;
            btn.colors = cols;
            var lbl = btn.GetComponentInChildren<Text>();
            if (lbl != null) lbl.text = text;
        }
    }

    // ── One-shot key recorder ─────────────────────────────────────────────────
    // records either a keyboard key (KeybindId) or a controller button (ControllerBindId),
    // depending on which Begin overload was used.
    public class KeybindRecorder : MonoBehaviour
    {
        public KeybindRecorder(IntPtr ptr) : base(ptr) { }

        private static KeybindRecorder _instance;

        // true while a row is waiting for a key/button, AND for a brief cooldown after — lets
        // ControllerManager ignore the press that was just recorded so the newly-bound button
        // doesn't immediately fire its own action (toggle/click) on the same hold.
        public static bool IsRecording =>
            (_instance != null && _instance._active) || Time.unscaledTime < _cooldownUntil;

        private static float _cooldownUntil;

        private KeybindId _id;
        private ControllerBindId _ctrlId;
        private bool _controllerMode;
        private Button _btn;
        private Action _onDone;
        private bool _active;
        private float _grace;

        public static void Begin(KeybindId id, Button btn, Action onDone, Action onStart)
        {
            BeginCommon(btn, onDone, onStart);
            _instance._id = id;
            _instance._controllerMode = false;
        }

        public static void BeginController(ControllerBindId id, Button btn, Action onDone, Action onStart)
        {
            BeginCommon(btn, onDone, onStart);
            _instance._ctrlId = id;
            _instance._controllerMode = true;
        }

        private static void BeginCommon(Button btn, Action onDone, Action onStart)
        {
            if (_instance == null)
            {
                var go = new GameObject("BetterFG_KeybindRecorder");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                _instance = go.AddComponent<KeybindRecorder>();
            }
            _instance._btn = btn;
            _instance._onDone = onDone;
            _instance._active = true;
            _instance._grace = 0.05f; // ignore the click-frame
            onStart?.Invoke();
        }

        void Update()
        {
            if (!_active) return;

            if (_grace > 0f)
            {
                _grace -= Time.deltaTime;
                return;
            }

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Finish();
                return;
            }

            if (_controllerMode)
            {
                int btn = ControllerBindService.PollPressedButton();
                if (btn < 0) return;
                ControllerBindService.SetButton(_ctrlId, btn);
                _cooldownUntil = Time.unscaledTime + 0.35f; // swallow the recorded press
                Finish();
                return;
            }

            var key = KeybindService.PollPressedKey();
            if (key == KeyCode.None) return;

            KeybindService.Set(_id, key);
            Finish();
        }

        private void Finish()
        {
            _active = false;
            _onDone?.Invoke();
            _onDone = null;
            _btn = null;
        }
    }
}
