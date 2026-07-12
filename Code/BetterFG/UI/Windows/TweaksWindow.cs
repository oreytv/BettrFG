using System;
using BetterFG.Tweaks;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Windows
{
    public class TweaksWindow : BetterFGWindow
    {
        public TweaksWindow(IntPtr ptr) : base(ptr) { }

        protected override float WindowWidth => 280f;
        protected override float WindowHeight => 160f;
        protected override string WindowTitle => "Tweaks";
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
                "search tweaks...", new Color(0f, 0f, 0f, 0.75f), Color.white, 12);
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

            TweaksWindowBuilder.BuildRows(listRt, "");

            var listCapture = listRt;
            searchField.onValueChanged.AddListener(new Action<string>(q =>
            {
                for (int i = listCapture.childCount - 1; i >= 0; i--)
                    GameObject.Destroy(listCapture.GetChild(i).gameObject);
                TweaksWindowBuilder.BuildRows(listCapture, q ?? "");
            }));
        }
    }

    internal static class TweaksWindowBuilder
    {
        private const float ROW_H = 22f;
        private const float TOGGLE_W = 36f;
        private const float TOGGLE_H = 16f;
        private const float PAD = 6f;
        private const float HEADER_H = 18f;       // tall enough that the scaled title isn't clipped
        private const float HEADER_LEFT = 22f;    // push right so the sidewheel frame doesn't cut it
        private const float HEADER_SCALE = 1.3f;  // literal Text gameObject localScale bump
        private static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color ROW_ODD = new Color(0f, 0f, 0f, 0f);
        private static readonly Color ON_COL = new Color(0.3f, 0.75f, 0.3f, 1f);
        private static readonly Color OFF_COL = new Color(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color HEADER_COL = Color.white;

        // last list root we built into + the query it was built with, so a custom button that
        // flips a tweak's own state (e.g. skip-rewards requeue mode) can redraw the rows and have
        // its label reflect the new state without the user reopening the window.
        private static RectTransform _lastRoot;
        private static string _lastQuery = "";

        public static void Refresh()
        {
            if (_lastRoot == null) return;
            for (int i = _lastRoot.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_lastRoot.GetChild(i).gameObject);
            BuildRows(_lastRoot, _lastQuery);
        }

        public static void BuildRows(RectTransform parent, string query = "")
        {
            _lastRoot = parent;
            _lastQuery = query ?? "";
            string q = (query ?? "").Trim().ToLowerInvariant();
            // section per category, header on top, then its tweaks. zebra resets each section.
            foreach (var cat in TweakRegistry.CategoryOrder)
            {
                // a category-name match shows the whole section; else filter tweaks individually.
                bool catMatch = q.Length > 0 && cat.ToString().ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) >= 0;
                int i = 0;
                bool any = false;
                foreach (var tweak in TweakRegistry.InCategory(cat))
                {
                    if (!catMatch && q.Length > 0
                        && (tweak.TweakLabel ?? "").ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) < 0
                        && (tweak.TweakId ?? "").ToLowerInvariant().IndexOf(q, StringComparison.Ordinal) < 0)
                        continue;
                    if (!any) { BuildHeader(parent, cat.ToString().ToUpperInvariant()); any = true; }
                    BuildRow(parent, tweak, i % 2 == 0 ? ROW_EVEN : ROW_ODD);
                    i++;
                }
            }
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
                HEADER_COL,
                TextAnchor.MiddleLeft);
            lbl.fontStyle = FontStyle.Bold;
            // literal gameObject scale, not just font size. anchor to the row's left-center and pivot
            // left-center so the scale grows rightward + vertically-centered (not up off the top).
            var rt = lbl.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(HEADER_LEFT, 0f);
            rt.localScale = new Vector3(HEADER_SCALE, HEADER_SCALE, 1f);
        }

        private static void BuildRow(RectTransform parent, BfgTweak tweak, Color bg)
        {
            var rowGo = new GameObject("Row_" + tweak.TweakId);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = bg;

            // label — stretch fill minus toggle area
            const float LABEL_X = PAD + 20f;
            var lbl = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(LABEL_X, 0f, 200f, ROW_H),
                tweak.TweakLabel, 13,
                new Color(1f, 1f, 1f, 0.85f),
                TextAnchor.MiddleLeft);

            // little "?" right after the label text (no delay tooltip) for tweaks with credits/notes
            if (!string.IsNullOrEmpty(tweak.TweakTooltip))
            {
                const float HELP_D = 14f;
                UGUIShip.CreateHelp(rowGo.transform,
                    new Rect(LABEL_X + lbl.preferredWidth + 5f, (ROW_H - HELP_D) * 0.5f, HELP_D, HELP_D),
                    tweak.TweakTooltip);
            }

            float rightOffset = PAD + TOGGLE_W;

            // inline numeric inputs — right-aligned, left of the toggle (and left of buttons).
            var inputs = tweak.GetInputFields();
            if (inputs != null && inputs.Count > 0)
            {
                for (int b = inputs.Count - 1; b >= 0; b--)
                {
                    var def = inputs[b];
                    float w = def.Width > 0f ? def.Width : 40f;
                    rightOffset += w + 3f;

                    var ifGo = new GameObject("InputField_" + b);
                    ifGo.transform.SetParent(rowGo.transform, false);
                    var ifRt = ifGo.AddComponent<RectTransform>();
                    ifRt.anchorMin = new Vector2(1f, 0.5f);
                    ifRt.anchorMax = new Vector2(1f, 0.5f);
                    ifRt.pivot = new Vector2(1f, 0.5f);
                    ifRt.anchoredPosition = new Vector2(-rightOffset + w, 0f);
                    ifRt.sizeDelta = new Vector2(w, TOGGLE_H);

                    var field = UGUIShip.CreateInputField(ifGo.transform,
                        new Rect(0f, 0f, w, TOGGLE_H),
                        def.Placeholder ?? "", null, null, 11);
                    field.text = def.Get?.Invoke() ?? "";
                    var capturedSet = def.Set;
                    field.onEndEdit.AddListener(new Action<string>(v => capturedSet?.Invoke(v)));
                }
            }

            // custom buttons — right-aligned, left of the toggle
            var customBtns = tweak.GetCustomButtons();
            if (customBtns != null && customBtns.Count > 0)
            {
                for (int b = customBtns.Count - 1; b >= 0; b--)
                {
                    var def = customBtns[b];
                    float w = def.Width > 0f ? def.Width : Mathf.Max(28f, def.Label.Length * 6f + 10f);
                    rightOffset += w + 3f;

                    var cbGo = new GameObject("CustomBtn_" + b);
                    cbGo.transform.SetParent(rowGo.transform, false);
                    var cbRt = cbGo.AddComponent<RectTransform>();
                    cbRt.anchorMin = new Vector2(1f, 0.5f);
                    cbRt.anchorMax = new Vector2(1f, 0.5f);
                    cbRt.pivot = new Vector2(1f, 0.5f);
                    cbRt.anchoredPosition = new Vector2(-rightOffset + w, 0f);
                    cbRt.sizeDelta = new Vector2(w, TOGGLE_H);

                    var captured = def.OnClick;
                    UGUIShip.CreateButton(cbGo.transform,
                        new Rect(0f, 0f, w, TOGGLE_H),
                        def.Label,
                        new Color(0.25f, 0.45f, 0.75f, 1f),
                        Color.white, 9)
                        .onClick.AddListener(new Action(() => captured?.Invoke()));
                }
            }

            // toggle button — right-anchored via child RectTransform
            var btnGo = new GameObject("Toggle");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(1f, 0.5f);
            btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-PAD, 0f);
            btnRt.sizeDelta = new Vector2(TOGGLE_W, TOGGLE_H);

            var capturedTweak = tweak;
            Text capturedLbl = null;

            var btn = UGUIShip.CreateButton(btnGo.transform,
                new Rect(0f, 0f, TOGGLE_W, TOGGLE_H),
                tweak.IsEnabled ? "ON" : "OFF",
                tweak.IsEnabled ? ON_COL : OFF_COL,
                Color.white, 9);

            capturedLbl = btn.GetComponentInChildren<Text>();

            // increments live in the expanded panel too, so they also need a full rebuild on toggle.
            bool hasSettings = capturedTweak.GetSettings()?.Count > 0 || capturedTweak.GetIncrements()?.Count > 0;
            btn.onClick.AddListener(new Action(() =>
            {
                capturedTweak.SetEnabled(!capturedTweak.IsEnabled);
                // a tweak with a settings panel needs the whole list rebuilt so the panel appears/
                // disappears under the row. plain tweaks just recolour the toggle in place.
                if (hasSettings) { Refresh(); return; }
                var img = btn.GetComponent<UnityEngine.UI.Image>();
                if (img != null) img.color = capturedTweak.IsEnabled ? ON_COL : OFF_COL;
                var cols = btn.colors;
                cols.normalColor = capturedTweak.IsEnabled ? ON_COL : OFF_COL;
                cols.highlightedColor = (capturedTweak.IsEnabled ? ON_COL : OFF_COL) * 1.2f;
                cols.pressedColor = (capturedTweak.IsEnabled ? ON_COL : OFF_COL) * 0.7f;
                btn.colors = cols;
                if (capturedLbl != null) capturedLbl.text = capturedTweak.IsEnabled ? "ON" : "OFF";
            }));

            // expanded settings panel, only while enabled. one sub-row per setting, same zebra bg as
            // the row so it reads as part of it. sits directly under the row (sibling order = draw order).
            if (tweak.IsEnabled)
            {
                var settings = tweak.GetSettings();
                if (settings != null)
                    foreach (var s in settings)
                        BuildSettingRow(parent, tweak, s, bg);

                var incs = tweak.GetIncrements();
                if (incs != null)
                    foreach (var inc in incs)
                        BuildIncrementRow(parent, tweak, inc, bg);
            }
        }

        // increment as a sub-row in the expanded panel: label on the left (indented, like a setting),
        // [-] value [+] on the right where the toggle sits on the parent row. gives the tweak a
        // two-line-tall look — toggle row on top, the stepper on its own line under it.
        private static void BuildIncrementRow(RectTransform parent, BfgTweak tweak, TweakIncrement inc, Color bg)
        {
            var rowGo = new GameObject("Inc_" + tweak.TweakId);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = bg;

            const float SET_LABEL_X = PAD + 34f;
            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(SET_LABEL_X, 0f, 200f, ROW_H),
                string.IsNullOrEmpty(inc.Label) ? "Limit" : inc.Label, 12,
                new Color(1f, 1f, 1f, 0.7f),
                TextAnchor.MiddleLeft);

            float w = inc.Width > 0f ? inc.Width : 62f;
            var incGo = new GameObject("IncCtrl");
            incGo.transform.SetParent(rowGo.transform, false);
            var incRt = incGo.AddComponent<RectTransform>();
            incRt.anchorMin = incRt.anchorMax = new Vector2(1f, 0.5f);
            incRt.pivot = new Vector2(1f, 0.5f);
            incRt.anchoredPosition = new Vector2(-PAD, 0f);
            incRt.sizeDelta = new Vector2(w, TOGGLE_H);

            UGUIShip.CreateIncrement(incGo.transform, new Rect(0f, 0f, w, TOGGLE_H),
                inc.Min, inc.Max, inc.Get, inc.Set, inc.Wrap, 11);
        }

        private static void BuildSettingRow(RectTransform parent, BfgTweak tweak, TweakSetting setting, Color bg)
        {
            var rowGo = new GameObject("Setting_" + tweak.TweakId + "_" + setting.Label);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = bg;

            // label, indented past where the toggle icon sits so it reads as a child setting
            const float SET_LABEL_X = PAD + 34f;
            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(SET_LABEL_X, 0f, 240f, ROW_H),
                setting.Label, 12,
                new Color(1f, 1f, 1f, 0.7f),
                TextAnchor.MiddleLeft);

            // cycling option button on the right, where the toggle lives on the parent row
            int idx = Mathf.Clamp(setting.Selected?.Invoke() ?? 0, 0, Mathf.Max(0, setting.Options.Length - 1));
            float w = 0f;
            foreach (var o in setting.Options) w = Mathf.Max(w, o.Length * 6.5f + 14f);
            w = Mathf.Max(52f, w);

            var btnGo = new GameObject("OptBtn");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(1f, 0.5f);
            btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-PAD, 0f);
            btnRt.sizeDelta = new Vector2(w, TOGGLE_H);

            var btn = UGUIShip.CreateButton(btnGo.transform,
                new Rect(0f, 0f, w, TOGGLE_H),
                setting.Options[idx],
                new Color(0.25f, 0.45f, 0.75f, 1f),
                Color.white, 9);

            var capturedSetting = setting;
            var capturedLbl = btn.GetComponentInChildren<Text>();
            btn.onClick.AddListener(new Action(() =>
            {
                int cur = capturedSetting.Selected?.Invoke() ?? 0;
                int next = (cur + 1) % capturedSetting.Options.Length;
                capturedSetting.OnPick?.Invoke(next);
                if (capturedLbl != null) capturedLbl.text = capturedSetting.Options[next];
            }));
        }
    }
}
