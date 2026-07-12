using System;
using BetterFG.Core;
using BetterFG.Nametag;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Windows
{
    public class PlayerdetailsWindow : BetterFGWindow
    {
        public PlayerdetailsWindow(IntPtr ptr) : base(ptr) { }

        public static PlayerdetailsWindow Instance { get; private set; }

        // exactly TweaksWindow's frame — same size, bg, title.
        protected override float WindowWidth => 280f;
        protected override float WindowHeight => 160f;
        protected override string WindowTitle => "Player Details";
        protected override string BgResourceName => "BetterFG.assets.ui.windows.generalbg.png";

        private const string KEY_ENABLED = "nametag.enabled";

        private InputField _nameField;

        protected override void BuildContent(RectTransform contentRoot)
        {
            Instance = this;

            // every transform value below is copied verbatim from TweaksWindow so the two windows match.
            BgPosition = new Vector3(139.3993f, 55.0135f, 0f);
            BgScale = new Vector3(1.3415f, 5.3623f, 1f);
            ContentPosition = new Vector3(-1.6132f, -17.32f, 0f);
            ContentScale = new Vector3(1.0473f, 1.04f, 1f);
            ContentOffsetMin = new Vector2(-1.6132f, -25.92f);
            ContentOffsetMax = new Vector2(-1.6132f, -18.72f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(20.0368f, -6.7966f, 0f);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            var scroll = UGUIShip.CreateScrollView(contentRoot,
                new Rect(0f, 0f, WindowWidth, WindowHeight - TITLE_H));
            var scrollRt = scroll.scrollRect.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;

            var listRt = scroll.content;
            var vlg = listRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            var csf = listRt.gameObject.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;


            PlayerDetailsRows.Build(listRt, this);

            var filler = UGUIShip.CreateLabel(contentRoot,
                new Rect(0f, 0f, WindowWidth, WindowHeight - TITLE_H),
                "Idk what else to add here", 11, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleCenter);
            var fillerRt = filler.GetComponent<RectTransform>();
            fillerRt.anchorMin = Vector2.zero;
            fillerRt.anchorMax = Vector2.one;
            fillerRt.offsetMin = Vector2.zero;
            fillerRt.offsetMax = Vector2.zero;
        }

        internal void SetNameField(InputField f) => _nameField = f;

        // ── Apply handlers ─────────────────────────────────────────────────────

        internal void ApplyName(string val)
        {
            LocalPlayerInfo.CustomName = val ?? "";
            NametagFinder.ReapplyAllNameplates();
            if (SettingsService.Get(KEY_ENABLED, "false") == "true")
                NametagIconApplicator.ApplyNametag();
            if (CrownRankService.Enabled)
                CrownRankService.ApplyLocal();
        }
    }

    // zebra rows, mirroring TweaksWindowBuilder's row metrics so the two windows are visually identical.
    internal static class PlayerDetailsRows
    {
        private const float ROW_H = 22f;
        private const float PAD = 6f;
        private const float HEADER_H = 18f;
        private const float HEADER_LEFT = 22f;
        private const float HEADER_SCALE = 1.3f;
        private const float LABEL_X = PAD + 20f;
        private const float FIELD_W = 90f;
        private const float BTN_W = 42f;
        private const float CTRL_H = 16f;

        private static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color HEADER_COL = Color.white;
        private static readonly Color BTN_COL = new Color(0.25f, 0.45f, 0.75f, 1f);

        public static void Build(RectTransform parent, PlayerdetailsWindow win)
        {
            BuildHeader(parent, "CUSTOM NAME");
            win.SetNameField(BuildRow(parent, ROW_EVEN, "Name", "leave empty to use real name",
                LocalPlayerInfo.CustomName, new Action<string>(win.ApplyName)));
        }

        private static void BuildHeader(RectTransform parent, string title)
        {
            var rowGo = new GameObject("Header_" + title);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = HEADER_H;
            le.flexibleWidth = 1f;

            var lbl = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(HEADER_LEFT, 0f, 200f, HEADER_H), title, 10, HEADER_COL, TextAnchor.MiddleLeft);
            lbl.fontStyle = FontStyle.Bold;
            var rt = lbl.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(HEADER_LEFT, 0f);
            rt.localScale = new Vector3(HEADER_SCALE, HEADER_SCALE, 1f);
        }

        private static InputField BuildRow(RectTransform parent, Color bg, string label, string placeholder,
            string initial, Action<string> onApply)
        {
            var rowGo = new GameObject("Row_" + label);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = bg;

            UGUIShip.CreateLabel(rowGo.transform, new Rect(LABEL_X, 0f, 120f, ROW_H),
                label, 13, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);

            // field, right-anchored left of the Apply button.
            var ifGo = new GameObject("Field");
            ifGo.transform.SetParent(rowGo.transform, false);
            var ifRt = ifGo.AddComponent<RectTransform>();
            ifRt.anchorMin = ifRt.anchorMax = new Vector2(1f, 0.5f);
            ifRt.pivot = new Vector2(1f, 0.5f);
            ifRt.anchoredPosition = new Vector2(-(PAD + BTN_W + 3f), 0f);
            ifRt.sizeDelta = new Vector2(FIELD_W, CTRL_H);

            var field = UGUIShip.CreateInputField(ifGo.transform, new Rect(0f, 0f, FIELD_W, CTRL_H),
                placeholder, new Color(0.12f, 0.12f, 0.12f, 1f), Color.white, 11);
            UGUIShip.SetInputText(field, initial ?? "", false);
            field.onEndEdit.AddListener(new Action<string>(onApply));

            var btnGo = new GameObject("Apply");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-PAD, 0f);
            btnRt.sizeDelta = new Vector2(BTN_W, CTRL_H);
            UGUIShip.CreateButton(btnGo.transform, new Rect(0f, 0f, BTN_W, CTRL_H),
                "Apply", BTN_COL, Color.white, 9)
                .onClick.AddListener(new Action(() => onApply(field.text)));

            

            return field;
        }
    }
}
