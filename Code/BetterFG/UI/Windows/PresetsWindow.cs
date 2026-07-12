using System;
using BetterFG.Customization.Presets;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Windows
{
    public class PresetsWindow : BetterFGWindow
    {
        public PresetsWindow(IntPtr ptr) : base(ptr) { }

        protected override float WindowWidth => 280f;
        protected override float WindowHeight => 160f;
        protected override string WindowTitle => "Presets";
        protected override string BgResourceName => "BetterFG.assets.ui.windows.generalbg.png";

        private const float ROW_H = 22f;
        private const float BTN_H = 16f;
        private const float ROW_PAD = 6f;
        private static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color ROW_ODD = new Color(0f, 0f, 0f, 0f);
        private static readonly Color BTN_LOAD = new Color(0.3f, 0.6f, 0.35f, 1f);
        private static readonly Color BTN_DEL = new Color(0.7f, 0.3f, 0.3f, 1f);
        private static readonly Color BTN_SAVE = new Color(0.25f, 0.45f, 0.75f, 1f);

        private InputField _nameField;

        protected override void BuildContent(RectTransform contentRoot)
        {
            BgPosition = new Vector3(148.7123f, 50.0206f, 0f);
            BgScale = new Vector3(1.2833f, 4.3332f, 1f);
            ContentPosition = new Vector3(138.3868f, -16.32f, 0f);
            ContentScale = new Vector3(1.0473f, 1f, 1f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(27.8546f, -20.2182f, 0);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            var scroll = UGUIShip.CreateScrollView(contentRoot,
                new Rect(0f, 0f, WindowWidth, WindowHeight - TITLE_H));
            var scrollRt = scroll.scrollRect.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = scrollRt.offsetMax = Vector2.zero;

            var listRt = scroll.content;
            var vlg = listRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            listRt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            BuildSaveRow(listRt);

            var names = PresetService.List();
            for (int i = 0; i < names.Count; i++)
                BuildPresetRow(listRt, names[i], i % 2 == 0 ? ROW_EVEN : ROW_ODD);
        }

        // top row: name field + Save button. saving rebuilds so the new preset shows up.
        private void BuildSaveRow(RectTransform parent)
        {
            var rowGo = new GameObject("Row_Save");
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = new Color(0.2f, 0.3f, 0.45f, 0.18f);

            _nameField = UGUIShip.CreateInputField(rowGo.transform,
                new Rect(ROW_PAD, (ROW_H - BTN_H) * 0.5f, 150f, BTN_H), "preset name…");

            var btnGo = new GameObject("SaveBtn");
            btnGo.transform.SetParent(rowGo.transform, false);
            var btnRt = btnGo.AddComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(1f, 0.5f);
            btnRt.anchorMax = new Vector2(1f, 0.5f);
            btnRt.pivot = new Vector2(1f, 0.5f);
            btnRt.anchoredPosition = new Vector2(-ROW_PAD, 0f);
            btnRt.sizeDelta = new Vector2(50f, BTN_H);

            UGUIShip.CreateButton(btnGo.transform, new Rect(0f, 0f, 50f, BTN_H), "SAVE",
                BTN_SAVE, Color.white, 9)
                .onClick.AddListener(new Action(() =>
                {
                    string n = _nameField != null ? _nameField.text : "";
                    if (string.IsNullOrWhiteSpace(n)) return;
                    PresetService.Save(n);
                    RebuildContent();
                }));
        }

        private void BuildPresetRow(RectTransform parent, string name, Color bg)
        {
            var rowGo = new GameObject("Row_" + name);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = bg;

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(ROW_PAD + 20f, 0f, 150f, ROW_H),
                name, 13, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);

            // right-anchored: Del then Load, walking in from the edge.
            float right = ROW_PAD;
            AddRowButton(rowGo, "DEL", 36f, ref right, BTN_DEL, () => { PresetService.Delete(name); RebuildContent(); });
            AddRowButton(rowGo, "LOAD", 44f, ref right, BTN_LOAD, () => PresetService.Load(name));
        }

        private void AddRowButton(GameObject row, string label, float w, ref float rightOffset, Color col, Action onClick)
        {
            var go = new GameObject(label + "Btn");
            go.transform.SetParent(row.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-rightOffset, 0f);
            rt.sizeDelta = new Vector2(w, BTN_H);
            rightOffset += w + 3f;

            UGUIShip.CreateButton(go.transform, new Rect(0f, 0f, w, BTN_H), label, col, Color.white, 9)
                .onClick.AddListener(new Action(() => onClick?.Invoke()));
        }
    }
}
