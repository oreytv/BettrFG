using System;
using System.Collections;
using System.Collections.Generic;
using BetterFG.Customization.Player;
using BetterFG.UI.Tab;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.UI.Windows
{
    public class SkinTextureCostumeWindow : BetterFGWindow
    {
        public SkinTextureCostumeWindow(IntPtr ptr) : base(ptr) { }

        public static SkinTextureCostumeWindow Instance { get; private set; }

        protected override float WindowWidth => 360f;
        protected override float WindowHeight => 245f;
        protected override string WindowTitle => "Costume";
        protected override string BgResourceName => "BetterFG.assets.ui.windows.generalbg.png";
        protected override bool DraggableFromTitle => true;

        private const float ROW_H = 24f;
        private const float BTN_H = 19f;
        private static readonly Color BTN_BLUE = new Color(0.22f, 0.34f, 0.55f, 1f);
        private static readonly Color BTN_GREEN = new Color(0.25f, 0.45f, 0.25f, 1f);
        private static readonly Color BTN_RED = new Color(0.45f, 0.22f, 0.22f, 1f);

        private CustomSkinTextureTab _tab;
        private InputField _searchField;
        private ScrollRect _scroll;
        private RectTransform _resultContent;
        private Text _status;
        private readonly List<CostumeOption> _results = new List<CostumeOption>();
        private readonly List<Button> _rows = new List<Button>();
        private int _selected = -1;

        public void Configure(CustomSkinTextureTab tab)
        {
            Instance = this;
            _tab = tab;
            SetAnchorPosition(new Vector2(520f, 0f));
            ShowWindow();
            RebuildContent();
        }

        public void Close()
        {
            if (Instance == this) Instance = null;
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            BgPosition = new Vector3(179.7451f, 83.66f, 0f);
            BgScale = new Vector3(1.2833f, 4.7441f, 1f);
            ContentPosition = new Vector3(177.6421f, -4.4f, 0f);
            ContentScale = new Vector3(1.0473f, 1f, 1f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(32.5674f, -7f, 0f);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            float w = WindowWidth - PAD * 2f;
            float y = 3f;
            float closeW = 28f;
            float fetchW = 58f;

            _searchField = UGUIShip.CreateInputField(contentRoot,
                new Rect(PAD, y, w - fetchW - closeW - 8f, BTN_H),
                "search costumes by name",
                Color.black,
                WHITE,
                FS_SM);
            _searchField.transition = Selectable.Transition.None;

            var fetchBtn = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD + w - fetchW - closeW - 4f, y, fetchW, BTN_H),
                "Fetch",
                BTN_BLUE,
                WHITE,
                FS_SM,
                new Action(OnFetch));
            fetchBtn.transition = Selectable.Transition.None;

            var closeBtn = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD + w - closeW, y, closeW, BTN_H),
                "X",
                BTN_RED,
                WHITE,
                FS_SM,
                new Action(Close));
            closeBtn.transition = Selectable.Transition.None;

            y += BTN_H + 3f;

            var scroll = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, y, w, 160f));
            _scroll = scroll.scrollRect;
            _resultContent = scroll.content;
            var layout = _resultContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 1f;
            _resultContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            y += 160f + 3f;

            var useBtn = UGUIShip.CreateButton(contentRoot,
                new Rect(PAD, y, w, BTN_H),
                "Use Selected",
                BTN_GREEN,
                WHITE,
                FS_SM,
                new Action(OnUseSelected));
            useBtn.transition = Selectable.Transition.None;
            y += BTN_H + 2f;

            _status = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, BTN_H), "", FS_SM, HINT, TextAnchor.MiddleCenter);
            RebuildRows();
        }

        private void OnFetch()
        {
            string filter = _searchField?.text?.Trim() ?? "";
            StartCoroutine(FetchRoutine(filter).WrapToIl2Cpp());
        }

        private IEnumerator FetchRoutine(string filter)
        {
            if (string.IsNullOrEmpty(filter)) { SetStatus("type something"); yield break; }

            _results.Clear();
            _selected = -1;
            RebuildRows();
            SetStatus("fetching...");

            for (int i = 0; i < 2; i++) yield return null;

            try
            {
                var raw = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<CostumeOption>());
                if (raw == null || raw.Length == 0) { SetStatus("none found"); yield break; }

                for (int i = 0; i < raw.Length && _results.Count < 60; i++)
                {
                    if (raw[i] == null) continue;
                    CostumeOption opt;
                    try { opt = raw[i].Cast<CostumeOption>(); } catch { continue; }

                    string name = CustomSkinTextureTab.GetDisplayName(opt);
                    if (name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                        _results.Add(opt);
                }
            }
            catch (Exception ex) { SetStatus("err: " + ex.Message); yield break; }

            SetStatus(_results.Count + " result(s)");
            RebuildRows();
        }

        private void RebuildRows()
        {
            _rows.Clear();
            if (_resultContent == null) return;

            for (int i = _resultContent.childCount - 1; i >= 0; i--)
            {
                var child = _resultContent.GetChild(i);
                if (child != null) Destroy(child.gameObject);
            }

            for (int i = 0; i < _results.Count; i++)
            {
                int idx = i;
                string label = CustomSkinTextureTab.GetDisplayName(_results[i]);
                var btn = UGUIShip.CreateButton(_resultContent,
                    new Rect(0f, 0f, 0f, ROW_H),
                    "",
                    new Color(0.12f, 0.12f, 0.12f, 1f),
                    WHITE,
                    FS_SM,
                    new Action(() => SelectRow(idx)));
                btn.transition = Selectable.Transition.None;
                var trigger = btn.GetComponent<EventTrigger>();
                if (trigger != null) Destroy(trigger);
                var rt = btn.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(0f, ROW_H);
                var le = btn.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;

                Sprite icon = null;
                try { icon = ((ItemDefinitionSO)_results[i])?.MenuDisplaySprite; } catch { }
                float textX = 5f;
                if (icon != null)
                {
                    var iconGo = new GameObject("Icon");
                    iconGo.transform.SetParent(btn.transform, false);
                    var iconRt = iconGo.AddComponent<RectTransform>();
                    iconRt.anchorMin = new Vector2(0f, 0.5f);
                    iconRt.anchorMax = new Vector2(0f, 0.5f);
                    iconRt.pivot = new Vector2(0f, 0.5f);
                    iconRt.anchoredPosition = new Vector2(3f, 0f);
                    iconRt.sizeDelta = new Vector2(ROW_H - 4f, ROW_H - 4f);
                    var img = iconGo.AddComponent<Image>();
                    img.sprite = icon;
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                    textX = ROW_H + 3f;
                }

                UGUIShip.CreateLabel(btn.transform, new Rect(textX, 0f, WindowWidth - PAD * 2f - textX - 8f, ROW_H),
                    label, FS_SM, WHITE, TextAnchor.MiddleLeft);
                _rows.Add(btn);
            }
        }

        private void SelectRow(int idx)
        {
            _selected = idx;
            for (int i = 0; i < _rows.Count; i++)
            {
                var img = _rows[i]?.GetComponent<Image>();
                if (img != null) img.color = i == idx ? SEL_COL : new Color(0.12f, 0.12f, 0.12f, 1f);
            }
            SetStatus(CustomSkinTextureTab.GetDisplayName(_results[idx]));
        }

        private void OnUseSelected()
        {
            if (_selected < 0 || _selected >= _results.Count) { SetStatus("pick one"); return; }
            _tab?.CacheCostumeFromWindow(_results[_selected]);
            Close();
        }

        private void SetStatus(string text)
        {
            if (_status != null) _status.text = text;
            _tab?.SetStatus(text);
        }
    }
}
