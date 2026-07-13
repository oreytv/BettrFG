using System;
using BetterFG.Features;
using BetterFG.UI;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    public class FeaturesTab : BetterFGTab
    {
        public FeaturesTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Features";

        static readonly Color WHITE = Color.white;
        static readonly Color ROW_BG = new Color(0f, 0f, 0f, 0.55f);
        static readonly Color HEADER_BG = new Color(0f, 0f, 0f, 0.82f);
        static readonly Color ON = new Color(0.25f, 0.5f, 0.25f, 1f);
        static readonly Color OFF = new Color(0f, 0f, 0f, 1f);

        static float PAD => UIScale.PAD;
        static float VPAD => UIScale.VPAD;
        static float BTN_H => UIScale.BTN_H;
        static int FS => UIScale.FS;
        static int FS_SM => UIScale.FS_SM;

        const float HEADER_H = 58f;
        const float SETTING_H = 26f;
        const float TOGGLE_W = 54f;
        const float TOGGLE_H = 18f;

        static Texture2D _bgTex;
        static Texture2D _hoverTex;
        static readonly System.Collections.Generic.Dictionary<string, Sprite> _featurePics = new System.Collections.Generic.Dictionary<string, Sprite>();
        GameObject _bgHoverGo;
        RectTransform _listRt;
        float _viewportH;
        float _contentW;
        bool _wasOpen;

        void Update()
        {
            if (_wasOpen == IsOpen) return;
            _wasOpen = IsOpen;
            var all = FeatureRegistry.all;
            for (int i = 0; i < all.Count; i++)
            {
                if (IsOpen) all[i].OnOpen();
                else all[i].OnClosed();
            }
        }

        protected override void BuildBackground(RectTransform root)
        {
            var bgTex = LoadTex("BetterFG.assets.ui.tab.features.png", ref _bgTex);
            if (bgTex == null) return;

            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(root, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = new Vector2(0f, 0f);
            bgRt.offsetMax = new Vector2(0f, 1f);
            bgRt.localScale = new Vector3(1.5015f, 1.3502f, 1f);
            bgRt.localPosition = new Vector3(267.7578f, 285.8921f, 0f);
            var raw = bgGo.AddComponent<RawImage>();
            raw.texture = bgTex;
            raw.raycastTarget = false;

            var hoverTex = LoadTex("BetterFG.assets.ui.bg_hover.png", ref _hoverTex);
            if (hoverTex == null) return;
            _bgHoverGo = new GameObject("BG_Hover");
            _bgHoverGo.transform.SetParent(bgGo.transform, false);
            var hoverRt = _bgHoverGo.AddComponent<RectTransform>();
            hoverRt.anchorMin = Vector2.zero;
            hoverRt.anchorMax = Vector2.one;
            hoverRt.offsetMin = hoverRt.offsetMax = Vector2.zero;
            _bgHoverGo.AddComponent<RawImage>().texture = hoverTex;
            _bgHoverGo.SetActive(false);
        }

        protected override void OnTitleHoverChanged(bool hovering)
        {
            if (_bgHoverGo != null) _bgHoverGo.SetActive(hovering);
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            var scrollRect = new Rect(PAD, VPAD, TabWidth - PAD * 2f, TabHeight - VPAD * 2f);
            _viewportH = scrollRect.height;
            _contentW = scrollRect.width - 26f;
            var scroll = UGUIShip.CreateScrollView(contentRoot, scrollRect);
            scroll.scrollRect.scrollSensitivity = 45f;
            _listRt = scroll.content;

            var vlg = _listRt.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 0f;
            vlg.padding = new RectOffset(0, 0, 0, 0);

            _listRt.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            RefreshRows();
        }

        void RefreshRows()
        {
            if (_listRt == null) return;
            for (int i = _listRt.childCount - 1; i >= 0; i--)
                Destroy(_listRt.GetChild(i).gameObject);

            var all = FeatureRegistry.all;
            for (int i = 0; i < all.Count; i++)
                BuildFeature(i);

            if (all.Count > 0)
            {
                var last = all[all.Count - 1];
                float lastH = HEADER_H + (last.settings == null ? 0f : last.settings.Count * SETTING_H);
                float padH = Mathf.Max(0f, _viewportH - lastH);

                var spacer = new GameObject("BottomScrollPad");
                spacer.transform.SetParent(_listRt, false);
                spacer.AddComponent<RectTransform>();
                var le = spacer.AddComponent<LayoutElement>();
                le.preferredHeight = padH;
                le.flexibleWidth = 1f;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_listRt);
        }

        void BuildFeature(int featureIndex)
        {
            var feature = FeatureRegistry.all[featureIndex];
            var rowGo = new GameObject("Feature_" + feature.id);
            rowGo.transform.SetParent(_listRt, false);
            rowGo.AddComponent<RectTransform>();
            rowGo.AddComponent<Image>().color = HEADER_BG;
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = HEADER_H;
            le.flexibleWidth = 1f;

            float w = _contentW > 0f ? _contentW : TabWidth - PAD * 4f;
            AddFeaturePicture(rowGo.transform, feature.id);

            var title = UGUIShip.CreateLabel(rowGo.transform, new Rect(PAD + 18f, PAD + 5f, w - TOGGLE_W - PAD * 2f - 18f, BTN_H),
                feature.title, FS + 1, WHITE, TextAnchor.MiddleLeft);
            title.fontStyle = FontStyle.Bold;
            title.transform.localScale = new Vector3(1.06f, 1.06f, 1f);

            UGUIShip.CreateButton(rowGo.transform,
                new Rect(w - TOGGLE_W, HEADER_H - TOGGLE_H - PAD, TOGGLE_W, TOGGLE_H),
                feature.enabled ? "ON" : "OFF",
                feature.enabled ? ON : OFF,
                WHITE, FS_SM,
                new Action(() =>
                {
                    feature.SetEnabled(!feature.enabled);
                    RefreshRows();
                }));

            var settings = feature.settings;
            int rowCount = 0;
            if (settings != null)
            {
                for (int i = 0; i < settings.Count; i++)
                    BuildSettingRow(featureIndex, i, i % 2 == 0 ? ROW_BG : Color.clear);
                rowCount = settings.Count;
            }

            // every declared choice auto-renders as a dropdown row — no per-feature special casing.
            var choices = feature.choices;
            if (choices != null)
            {
                for (int i = 0; i < choices.Count; i++)
                {
                    BuildChoiceRow(feature, choices[i], rowCount % 2 == 0 ? ROW_BG : Color.clear);
                    rowCount++;
                }
            }

            // finish placement gets a stepper row: how many spots to show on the leaderboard.
            if (feature.id == "timeplacement")
                BuildMaxRowsRow(feature, rowCount % 2 == 0 ? ROW_BG : Color.clear);
        }

        // renders a FeatureChoice as a single-select dropdown row, wired straight to the feature's
        // GetChoice/SetChoice so the saved pick and its onChoiceChanged callback are handled for us.
        void BuildChoiceRow(BfgFeature feature, FeatureChoice choice, Color bg)
        {
            var rowGo = new GameObject("Choice_" + feature.id + "_" + choice.id);
            rowGo.transform.SetParent(_listRt, false);
            rowGo.AddComponent<RectTransform>();
            rowGo.AddComponent<Image>().color = bg;
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = SETTING_H;
            le.flexibleWidth = 1f;

            float w = _contentW > 0f ? _contentW : TabWidth - PAD * 4f;

            var labels = choice.optionLabels;
            // size the dropdown to the longest option so nothing gets clipped. estimate text width
            // from char count at the dropdown font (~0.62*fontSize per char in this UI's font), plus
            // room for the arrow/margins. min 110 so short lists still look uniform.
            int longest = 0;
            for (int i = 0; i < labels.Count; i++) if (labels[i].Length > longest) longest = labels[i].Length;
            float ddW = Mathf.Max(110f, longest * FS_SM * 0.62f + 28f);

            var choiceLabel = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD * 3f, 0f, w - ddW - PAD * 4f, SETTING_H),
                choice.label,
                FS,
                feature.enabled ? new Color(1f, 1f, 1f, 0.86f) : new Color(1f, 1f, 1f, 0.35f),
                TextAnchor.MiddleLeft);

            // hint provided -> wire a tooltip on the label and a 4% white hover background behind it
            // so it's obvious the label is hoverable.
            if (!string.IsNullOrEmpty(choice.hint))
            {
                var labelRt = choiceLabel.rectTransform;
                var hoverGo = new GameObject("HoverBG");
                hoverGo.transform.SetParent(labelRt, false);
                var hoverRt = hoverGo.AddComponent<RectTransform>();
                hoverRt.anchorMin = Vector2.zero;
                hoverRt.anchorMax = Vector2.one;
                hoverRt.offsetMin = Vector2.zero;
                hoverRt.offsetMax = Vector2.zero;
                var hoverImg = hoverGo.AddComponent<Image>();
                hoverImg.color = new Color(1f, 1f, 1f, 0.04f);
                hoverImg.raycastTarget = false;
                hoverGo.transform.SetSiblingIndex(0);
                hoverGo.SetActive(false);

                var trig = labelRt.gameObject.AddComponent<TooltipTrigger>();
                trig.text = choice.hint;
                trig.hoverImage = hoverGo;
            }

            int selected = choice.optionIds.IndexOf(feature.GetChoice(choice.id));
            if (selected < 0) selected = 0;

            var initial = new System.Collections.Generic.List<bool>();
            for (int i = 0; i < labels.Count; i++) initial.Add(i == selected);
            Button ddBtn = null;
            ddBtn = UGUIShip.CreateMultiSelectDropdown(rowGo.transform,
                new Rect(w - ddW, (SETTING_H - TOGGLE_H) * 0.5f - 2f, ddW, TOGGLE_H + 4f),
                labels[selected], labels, initial,
                new Action<int, bool>((idx, _) =>
                {
                    if (idx < 0 || idx >= choice.optionIds.Count) return;
                    feature.SetChoice(choice.id, choice.optionIds[idx]);
                    var lbl = ddBtn?.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = labels[idx];
                }), FS_SM, ddW, 20f, true, true, true);
        }

        void BuildMaxRowsRow(BfgFeature feature, Color bg)
        {
            var rowGo = new GameObject("Setting_timeplacement_maxrows");
            rowGo.transform.SetParent(_listRt, false);
            rowGo.AddComponent<RectTransform>();
            rowGo.AddComponent<Image>().color = bg;
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = SETTING_H;
            le.flexibleWidth = 1f;

            float w = _contentW > 0f ? _contentW : TabWidth - PAD * 4f;
            float btnW = 22f;
            float numW = 34f;
            float groupW = btnW * 2f + numW;

            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD * 3f, 0f, w - groupW - PAD * 4f, SETTING_H),
                "Players to show",
                FS,
                feature.enabled ? new Color(1f, 1f, 1f, 0.86f) : new Color(1f, 1f, 1f, 0.35f),
                TextAnchor.MiddleLeft);

            float btnY = (SETTING_H - TOGGLE_H) * 0.5f;
            string K = Features.TimePlacement.FeatureTimePlacement.MaxRowsKey;
            int Cur() => Mathf.Clamp(
                Services.SettingsService.Get(K, Features.TimePlacement.FeatureTimePlacement.MaxRowsDefault.ToString()) is var s
                    && int.TryParse(s, out int v) ? v : Features.TimePlacement.FeatureTimePlacement.MaxRowsDefault,
                Features.TimePlacement.FeatureTimePlacement.MaxRowsMin,
                Features.TimePlacement.FeatureTimePlacement.MaxRowsMax);

            Text numLabel = null;
            numLabel = UGUIShip.CreateLabel(rowGo.transform,
                new Rect(w - groupW + btnW, btnY, numW, TOGGLE_H),
                Cur().ToString(), FS, WHITE, TextAnchor.MiddleCenter);

            UGUIShip.CreateButton(rowGo.transform,
                new Rect(w - groupW, btnY, btnW, TOGGLE_H),
                "-", OFF, WHITE, FS_SM,
                new Action(() =>
                {
                    int n = Mathf.Clamp(Cur() - 1,
                        Features.TimePlacement.FeatureTimePlacement.MaxRowsMin,
                        Features.TimePlacement.FeatureTimePlacement.MaxRowsMax);
                    Services.SettingsService.Set(K, n.ToString());
                    if (numLabel != null) numLabel.text = n.ToString();
                }));

            UGUIShip.CreateButton(rowGo.transform,
                new Rect(w - btnW, btnY, btnW, TOGGLE_H),
                "+", ON, WHITE, FS_SM,
                new Action(() =>
                {
                    int n = Mathf.Clamp(Cur() + 1,
                        Features.TimePlacement.FeatureTimePlacement.MaxRowsMin,
                        Features.TimePlacement.FeatureTimePlacement.MaxRowsMax);
                    Services.SettingsService.Set(K, n.ToString());
                    if (numLabel != null) numLabel.text = n.ToString();
                }));
        }


        void AddFeaturePicture(Transform parent, string id)
        {
            var sprite = FeaturePic(id);
            if (sprite == null) return;

            var go = new GameObject("Picture");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-TOGGLE_W - PAD * 2.8f, 0f);
            rt.sizeDelta = new Vector2(74f, 54f);

            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = new Color(1f, 1f, 1f, 0.22f);
        }

        static Sprite FeaturePic(string id)
        {
            if (_featurePics.TryGetValue(id, out var cached)) return cached;

            string res = id == "pb"
                ? "BetterFG.assets.ui.feature.qualificationtime.featurequalificationtime_icon.png"
                : id == "stars"
                    ? "BetterFG.assets.ui.feature.star.featurestar_star.png"
                    : id == "moreplatformicon"
                        ? "BetterFG.assets.ui.feature.moreplatformicon.featuremoreplatformicon_platformicons.png"
                        : "BetterFG.assets.ui.tab.menu.png";

            var sprite = EmbeddedResourceandUnity.LoadSprite(res, 100f);
            _featurePics[id] = sprite;
            return sprite;
        }

        void BuildSettingRow(int featureIndex, int settingIndex, Color bg)
        {
            var feature = FeatureRegistry.all[featureIndex];
            var setting = feature.settings[settingIndex];
            var rowGo = new GameObject("Setting_" + feature.id + "_" + setting.id);
            rowGo.transform.SetParent(_listRt, false);
            rowGo.AddComponent<RectTransform>();
            rowGo.AddComponent<Image>().color = bg;
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = SETTING_H;
            le.flexibleWidth = 1f;

            float w = _contentW > 0f ? _contentW : TabWidth - PAD * 4f;
            UGUIShip.CreateLabel(rowGo.transform,
                new Rect(PAD * 3f, 0f, w - TOGGLE_W - PAD * 4f, SETTING_H),
                setting.label,
                FS,
                feature.enabled ? new Color(1f, 1f, 1f, 0.86f) : new Color(1f, 1f, 1f, 0.35f),
                TextAnchor.MiddleLeft);

            bool on = feature.GetRaw(setting.id);
            UGUIShip.CreateButton(rowGo.transform,
                new Rect(w - TOGGLE_W, (SETTING_H - TOGGLE_H) * 0.5f, TOGGLE_W, TOGGLE_H),
                on ? "ON" : "OFF",
                on ? ON : OFF,
                WHITE, FS_SM,
                new Action(() =>
                {
                    feature.Set(setting.id, !feature.GetRaw(setting.id));
                    RefreshRows();
                }));
        }

        static Texture2D LoadTex(string resource, ref Texture2D cache)
        {
            if (cache != null) return cache;
            try { cache = EmbeddedResourceandUnity.LoadTexture(resource); }
            catch (Exception ex) { Plugin.Log.LogError("featuresTab: bg load failed: " + ex.Message); }
            return cache;
        }
    }
}
