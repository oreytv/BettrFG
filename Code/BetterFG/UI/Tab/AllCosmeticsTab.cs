using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Player;
using FG.Common.CMS;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    public class AllCosmeticsTab : BetterFGTab
    {
        public AllCosmeticsTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "All Cosmetics";

        private static float PAD => UIScale.PAD;
        private static float VPAD => UIScale.VPAD;
        private static float BTN_H => UIScale.BTN_H;
        private static int FS => UIScale.FS;
        private static int FS_SM => UIScale.FS_SM;

        private static readonly Color WHITE = Color.white;
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.38f);
        private static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color BTN_BLUE = new Color(0.22f, 0.34f, 0.55f, 1f);
        private static readonly Color BTN_GREEN = new Color(0.25f, 0.45f, 0.25f, 1f);
        private static readonly Color BTN_RED = new Color(0.55f, 0.15f, 0.15f, 1f);
        private static readonly Color SEL_COLOR = new Color(0.08f, 0.55f, 0.16f, 1f);
        private static readonly Color ROW_COLOR = new Color(0.12f, 0.12f, 0.12f, 1f);
        private const float ROW_H = 24f;

        private enum SubTab { Costumes, Colours, Patterns, Faceplates }
        private SubTab _sub = SubTab.Costumes;

        private static Texture2D _bgTex;
        private static Texture2D _hoverTex;
        private GameObject _bgHoverGo;

        private InputField _search;
        private Button _btnCostumes, _btnColours, _btnPatterns, _btnFaceplates;
        private RectTransform _content;
        private RectTransform _appliedContent;
        private Text _status;
        private HashSet<string> _selectedIds = new HashSet<string>();
        private string _selectedColourId = "";
        private string _selectedPatternId = "";
        private string _selectedFaceplateId = "";
        private readonly List<UnityEngine.Object> _results = new List<UnityEngine.Object>();
        private readonly List<Button> _rows = new List<Button>();
        private bool _selectionPrimedFromApplied;
        private bool _subscribed;

        private void EnsureSubscribed()
        {
            if (_subscribed) return;
            var svc = SkinApplicationService.Instance;
            if (svc == null) return;
            svc.OnSkinApplied += OnAnySkinApplied;
            svc.OnSkinRemoved += OnAnySkinRemoved;
            _subscribed = true;
        }

        void Awake() => EnsureSubscribed();

        void OnDestroy()
        {
            var svc = SkinApplicationService.Instance;
            if (svc == null || !_subscribed) return;
            svc.OnSkinApplied -= OnAnySkinApplied;
            svc.OnSkinRemoved -= OnAnySkinRemoved;
            _subscribed = false;
        }

        private void OnAnySkinApplied(SkinApplyEvent e)
        {
            if (e == null || e.skinInfo == null || e.skinInfo.type != "Costume") return;
            if (string.IsNullOrEmpty(e.skinInfo.file) || !e.skinInfo.file.StartsWith("gamecosm:")) return;
            _selectionPrimedFromApplied = false;
            RebuildAppliedRows();
        }

        private void OnAnySkinRemoved(string _)
        {
            _selectionPrimedFromApplied = false;
            RebuildAppliedRows();
        }

        private static Texture2D LoadTex(string resource, ref Texture2D cache)
        {
            if (cache != null) return cache;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream(resource);
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.wrapMode = TextureWrapMode.Clamp;
                cache = tex;
            }
            catch (Exception ex) { Plugin.Log.LogWarning("AllCosmeticsTab: bg load fail: " + ex.Message); }
            return cache;
        }

        protected override void BuildBackground(RectTransform root)
        {
            var bgTex = LoadTex("BetterFG.assets.ui.tab.allcosm.png", ref _bgTex);
            if (bgTex == null) return;

            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(root, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            bgRt.localScale = new Vector3(1.5015f, 1.3502f, 1f);
            bgRt.localPosition = new Vector3(267.7578f, 285.8921f, 0);
            var raw = bgGo.AddComponent<RawImage>();
            raw.texture = bgTex;
            raw.raycastTarget = false;

            var hoverTex = LoadTex("BetterFG.assets.ui.bg_hover.png", ref _hoverTex);
            if (hoverTex != null)
            {
                var hoverGo = new GameObject("BG_Hover");
                hoverGo.transform.SetParent(bgGo.transform, false);
                var hRt = hoverGo.AddComponent<RectTransform>();
                hRt.anchorMin = Vector2.zero;
                hRt.anchorMax = Vector2.one;
                hRt.offsetMin = hRt.offsetMax = Vector2.zero;
                hoverGo.AddComponent<RawImage>().texture = hoverTex;
                hoverGo.SetActive(false);
                _bgHoverGo = hoverGo;
            }
        }

        protected override void OnTitleHoverChanged(bool hovering)
        {
            if (_bgHoverGo != null) _bgHoverGo.SetActive(hovering);
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            EnsureSubscribed();
            float w = TabWidth - PAD * 2f;
            float y = VPAD;
            float fetchW = 58f;

            float quarter = (w - PAD * 1.5f) / 4f;
            float step = quarter + PAD * 0.5f;
            _btnCostumes = UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, quarter, BTN_H * 0.9f),
                "Costumes", _sub == SubTab.Costumes ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetSubTab(SubTab.Costumes)));
            _btnColours = UGUIShip.CreateButton(contentRoot, new Rect(PAD + step, y, quarter, BTN_H * 0.9f),
                "Colours", _sub == SubTab.Colours ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetSubTab(SubTab.Colours)));
            _btnPatterns = UGUIShip.CreateButton(contentRoot, new Rect(PAD + step * 2f, y, quarter, BTN_H * 0.9f),
                "Patterns", _sub == SubTab.Patterns ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetSubTab(SubTab.Patterns)));
            _btnFaceplates = UGUIShip.CreateButton(contentRoot, new Rect(PAD + step * 3f, y, quarter, BTN_H * 0.9f),
                "Faces", _sub == SubTab.Faceplates ? SEL_COLOR : BTN_DARK, WHITE, FS_SM, new Action(() => SetSubTab(SubTab.Faceplates)));
            y += BTN_H + 4f;

            _search = UGUIShip.CreateInputField(contentRoot, new Rect(PAD, y, w - fetchW - 4f, BTN_H),
                "search by name", Color.black, WHITE, FS_SM);
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + w - fetchW, y, fetchW, BTN_H),
                "Fetch", BTN_BLUE, WHITE, FS_SM, new Action(OnFetch));
            y += BTN_H + 4f;

            var scroll = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, y, w, 96f));
            _content = scroll.content;
            var layout = _content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 2f;
            _content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            y += 96f + 4f;

            UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, w, BTN_H),
                "Apply Selected", BTN_GREEN, WHITE, FS, new Action(OnApplySelected));
            y += BTN_H + 4f;

            UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, BTN_H), "Applied", FS_SM, HINT, TextAnchor.MiddleLeft);
            y += BTN_H;

            var appliedScroll = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, y, w, 88f));
            _appliedContent = appliedScroll.content;
            var appliedLayout = _appliedContent.gameObject.AddComponent<VerticalLayoutGroup>();
            appliedLayout.childForceExpandWidth = true;
            appliedLayout.childForceExpandHeight = false;
            appliedLayout.spacing = 2f;
            _appliedContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            y += 88f + 4f;

            UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, w, BTN_H),
                "Remove Game Cosmetics", BTN_RED, WHITE, FS_SM, new Action(OnRemove));
            y += BTN_H + 3f;

            _status = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, BTN_H), "", FS_SM, HINT, TextAnchor.MiddleCenter);
            y += BTN_H;

            UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, BTN_H), "Credits to Floyzi",
                FS_SM, new Color(1f, 1f, 1f, 0.25f), TextAnchor.MiddleCenter);

            RebuildRows();
            RebuildAppliedRows();
        }

        private void SetSubTab(SubTab sub)
        {
            _sub = sub;
            UGUIShip.SetButtonSelected(_btnCostumes, sub == SubTab.Costumes, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnColours, sub == SubTab.Colours, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnPatterns, sub == SubTab.Patterns, SEL_COLOR);
            UGUIShip.SetButtonSelected(_btnFaceplates, sub == SubTab.Faceplates, SEL_COLOR);
            _results.Clear();
            RebuildRows();
            RebuildAppliedRows();
            SetStatus(sub == SubTab.Costumes ? "costumes" : sub == SubTab.Colours ? "colours" : sub == SubTab.Patterns ? "patterns" : "faceplates");
        }

        private void OnFetch()
        {
            string filter = _search?.text?.Trim() ?? "";
            StartCoroutine(FetchRoutine(filter).WrapToIl2Cpp());
        }

        private IEnumerator FetchRoutine(string filter)
        {
            _results.Clear();
            RebuildRows();
            SetStatus("fetching...");
            yield return null;

            try
            {
                var type = _sub == SubTab.Colours ? Il2CppType.Of<ColourOption>()
                    : _sub == SubTab.Patterns ? Il2CppType.Of<SkinPatternOption>()
                    : _sub == SubTab.Faceplates ? Il2CppType.Of<FaceplateOption>()
                    : Il2CppType.Of<CostumeOption>();
                var raw = Resources.FindObjectsOfTypeAll(type);
                if (raw == null || raw.Length == 0) { SetStatus("none found"); yield break; }

                for (int i = 0; i < raw.Length && _results.Count < 120; i++)
                {
                    if (raw[i] == null) continue;
                    UnityEngine.Object opt = null;
                    try
                    {
                        if (_sub == SubTab.Colours) opt = raw[i].Cast<ColourOption>();
                        else if (_sub == SubTab.Patterns) opt = raw[i].Cast<SkinPatternOption>();
                        else if (_sub == SubTab.Faceplates) opt = raw[i].Cast<FaceplateOption>();
                        else opt = raw[i].Cast<CostumeOption>();
                    }
                    catch { continue; }
                    string name = GetDisplayName(opt);
                    if (string.IsNullOrEmpty(filter) || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
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
            if (_content == null) return;

            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                var child = _content.GetChild(i);
                if (child != null) Destroy(child.gameObject);
            }

            if (_results.Count == 0)
            {
                UGUIShip.CreateLabel(_content, new Rect(6f, 0f, TabWidth, ROW_H), "fetch", FS_SM, HINT, TextAnchor.MiddleLeft);
                return;
            }

            for (int i = 0; i < _results.Count; i++)
            {
                int idx = i;
                string label = GetDisplayName(_results[i]);
                string id = GetOptionId(_results[i]);
                bool isSel = _sub == SubTab.Costumes ? _selectedIds.Contains(id)
                    : _sub == SubTab.Colours ? _selectedColourId == id
                    : _sub == SubTab.Patterns ? _selectedPatternId == id
                    : _selectedFaceplateId == id;
                var btn = UGUIShip.CreateButton(_content, new Rect(0f, 0f, 0f, ROW_H), "",
                    isSel ? SEL_COLOR : ROW_COLOR, WHITE, FS_SM, new Action(() => ToggleRow(idx)),
                    customSprite: false);
                btn.transition = Selectable.Transition.None;
                var trigger = btn.GetComponent<EventTrigger>();
                if (trigger != null) Destroy(trigger);
                SetRowColors(btn, isSel);
                var rt = btn.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(0f, ROW_H);
                var le = btn.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;

                Sprite icon = GetIconSprite(_results[i]);
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

                UGUIShip.CreateLabel(btn.transform, new Rect(textX, 0f, TabWidth - PAD * 2f - textX - 10f, ROW_H),
                    label, FS_SM, WHITE, TextAnchor.MiddleLeft);
                _rows.Add(btn);
            }
        }

        private void ToggleRow(int idx)
        {
            if (idx < 0 || idx >= _results.Count) return;
            string id = GetOptionId(_results[idx]);
            if (_sub == SubTab.Costumes)
            {
                if (_selectedIds.Contains(id)) _selectedIds.Remove(id);
                else _selectedIds.Add(id);
            }
            else if (_sub == SubTab.Colours)
                _selectedColourId = _selectedColourId == id ? "" : id;
            else if (_sub == SubTab.Patterns)
                _selectedPatternId = _selectedPatternId == id ? "" : id;
            else
                _selectedFaceplateId = _selectedFaceplateId == id ? "" : id;

            for (int i = 0; i < _rows.Count; i++)
            {
                string rowId = i >= 0 && i < _results.Count ? GetOptionId(_results[i]) : "";
                bool selected = _sub == SubTab.Costumes ? _selectedIds.Contains(rowId)
                    : _sub == SubTab.Colours ? _selectedColourId == rowId
                    : _sub == SubTab.Patterns ? _selectedPatternId == rowId
                    : _selectedFaceplateId == rowId;
                SetRowColors(_rows[i], selected);
                SetRowTextColor(_rows[i], WHITE);
            }

            SetStatus(_sub == SubTab.Costumes ? _selectedIds.Count + " selected" : (string.IsNullOrEmpty(id) ? "none" : "selected"));
        }

        private void OnApplySelected()
        {
            var svc = SkinApplicationService.Instance;
            if (svc == null) { SetStatus("SkinApplicationService not ready"); return; }

            if (_sub == SubTab.Colours)
            {
                if (string.IsNullOrEmpty(_selectedColourId))
                {
                    svc.RemoveGameColour();
                    SetStatus("removed colour");
                    RebuildAppliedRows();
                    return;
                }
                for (int i = 0; i < _results.Count; i++)
                    if (GetOptionId(_results[i]) == _selectedColourId)
                    {
                        svc.ApplyGameColour(_results[i].Cast<ColourOption>());
                        SetStatus("applied colour");
                        RebuildAppliedRows();
                        return;
                    }
                SetStatus("no colour selected");
                return;
            }

            if (_sub == SubTab.Patterns)
            {
                if (string.IsNullOrEmpty(_selectedPatternId))
                {
                    svc.RemoveGamePattern();
                    SetStatus("removed pattern");
                    RebuildAppliedRows();
                    return;
                }
                for (int i = 0; i < _results.Count; i++)
                    if (GetOptionId(_results[i]) == _selectedPatternId)
                    {
                        svc.ApplyGamePattern(_results[i].Cast<SkinPatternOption>());
                        SetStatus("applied pattern");
                        RebuildAppliedRows();
                        return;
                    }
                SetStatus("no pattern selected");
                return;
            }

            if (_sub == SubTab.Faceplates)
            {
                if (string.IsNullOrEmpty(_selectedFaceplateId))
                {
                    svc.RemoveGameFaceplate();
                    SetStatus("removed faceplate");
                    RebuildAppliedRows();
                    return;
                }
                for (int i = 0; i < _results.Count; i++)
                    if (GetOptionId(_results[i]) == _selectedFaceplateId)
                    {
                        svc.ApplyGameFaceplate(_results[i].Cast<FaceplateOption>());
                        SetStatus("applied faceplate");
                        RebuildAppliedRows();
                        return;
                    }
                SetStatus("no faceplate selected");
                return;
            }

            var chosen = new List<CostumeOption>();
            for (int i = 0; i < _results.Count; i++)
            {
                string id = GetOptionId(_results[i]);
                if (_selectedIds.Contains(id)) chosen.Add(_results[i].Cast<CostumeOption>());
            }

            bool changed = svc.ApplyGameCosmeticSelection(chosen, new HashSet<string>(_selectedIds));
            SetStatus(changed ? "applied selected set" : "no changes");
            _selectionPrimedFromApplied = true;
            RebuildAppliedRows();
        }

        private void OnRemove()
        {
            var svc = SkinApplicationService.Instance;
            if (svc == null) return;
            svc.RemoveAllGameCosmetics();
            svc.RemoveGameColour();
            svc.RemoveGamePattern();
            svc.RemoveGameFaceplate();
            _selectedIds.Clear();
            _selectedColourId = "";
            _selectedPatternId = "";
            _selectedFaceplateId = "";
            _selectionPrimedFromApplied = true;
            RebuildAppliedRows();
            SetStatus("removed");
        }

        private void RebuildAppliedRows()
        {
            if (_appliedContent == null) return;

            for (int i = _appliedContent.childCount - 1; i >= 0; i--)
            {
                var child = _appliedContent.GetChild(i);
                if (child != null) Destroy(child.gameObject);
            }

            var svc = SkinApplicationService.Instance;
            var applied = svc != null ? svc.GetAppliedGameCosmetics() : null;
            if (!_selectionPrimedFromApplied && svc != null)
            {
                _selectedIds.Clear();
                var ids = svc.GetAppliedGameCosmeticIds();
                foreach (var id in ids) _selectedIds.Add(id);
                _selectedColourId = svc.GetAppliedGameColourId();
                _selectedPatternId = svc.GetAppliedGamePatternId();
                _selectedFaceplateId = svc.GetAppliedGameFaceplateId();
                _selectionPrimedFromApplied = true;
            }
            RecolorResultRows();

            if (applied == null || applied.Count == 0)
            {
                UGUIShip.CreateLabel(_appliedContent, new Rect(6f, 0f, TabWidth, ROW_H), "none", FS_SM, HINT, TextAnchor.MiddleLeft);
                return;
            }

            for (int i = 0; i < applied.Count; i++)
            {
                var entry = applied[i];
                string id = entry.id;
                string label = string.IsNullOrEmpty(entry.name) ? id : entry.name;

                var rowGo = new GameObject("Applied_" + i);
                rowGo.transform.SetParent(_appliedContent, false);
                var rt = rowGo.AddComponent<RectTransform>();
                rt.sizeDelta = new Vector2(0f, ROW_H);
                var le = rowGo.AddComponent<UnityEngine.UI.LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;
                var img = rowGo.AddComponent<Image>();
                img.color = new Color(0.08f, 0.18f, 0.08f, 1f);
                img.raycastTarget = false;
                var spr = UGUIShip.GetButtonSprite();
                if (spr != null) { img.sprite = spr; img.type = Image.Type.Simple; }

                float textX = 5f;
                try
                {
                    var icon = GetIconSprite(entry.option);
                    if (icon != null)
                    {
                        var iconGo = new GameObject("Icon");
                        iconGo.transform.SetParent(rowGo.transform, false);
                        var irt = iconGo.AddComponent<RectTransform>();
                        irt.anchorMin = new Vector2(0f, 0.5f); irt.anchorMax = new Vector2(0f, 0.5f); irt.pivot = new Vector2(0f, 0.5f);
                        irt.anchoredPosition = new Vector2(3f, 0f); irt.sizeDelta = new Vector2(ROW_H - 4f, ROW_H - 4f);
                        var img2 = iconGo.AddComponent<Image>(); img2.sprite = icon; img2.preserveAspect = true; img2.raycastTarget = false;
                        textX = ROW_H + 3f;
                    }
                }
                catch { }
                // label fills from textX to just before the X button — anchored to the row's
                // real edges instead of guessing off TabWidth (the row is narrower than TabWidth
                // because the scrollbar eats width, which was clipping the X button)
                var lblTxt = UGUIShip.CreateLabel(rowGo.transform, new Rect(textX, 0f, 10f, ROW_H),
                    label, FS_SM, WHITE, TextAnchor.MiddleLeft);
                var lblRt = lblTxt.GetComponent<RectTransform>();
                lblRt.anchorMin = new Vector2(0f, 0f); lblRt.anchorMax = new Vector2(1f, 1f);
                lblRt.pivot = new Vector2(0f, 0.5f);
                lblRt.offsetMin = new Vector2(textX, 0f); lblRt.offsetMax = new Vector2(-30f, 0f);

                var xBtn = UGUIShip.CreateButton(rowGo.transform, new Rect(0f, 2f, 24f, ROW_H - 4f),
                    "X", BTN_RED, WHITE, FS_SM, new Action(() =>
                    {
                        var app = SkinApplicationService.Instance;
                        if (app == null) return;
                        if (entry.kind == "colour") { app.RemoveGameColour(); _selectedColourId = ""; }
                        else if (entry.kind == "pattern") { app.RemoveGamePattern(); _selectedPatternId = ""; }
                        else if (entry.kind == "faceplate") { app.RemoveGameFaceplate(); _selectedFaceplateId = ""; }
                        else { app.RemoveGameCosmetic(id); _selectedIds.Remove(id); }
                        _selectionPrimedFromApplied = true;
                        RebuildAppliedRows();
                        SetStatus("removed " + label);
                    }), customSprite: false);
                // pin the X to the row's right edge so it never falls outside the scroll viewport
                var xRt = xBtn.GetComponent<RectTransform>();
                xRt.anchorMin = new Vector2(1f, 0.5f); xRt.anchorMax = new Vector2(1f, 0.5f);
                xRt.pivot = new Vector2(1f, 0.5f);
                xRt.anchoredPosition = new Vector2(-3f, 0f);
                xRt.sizeDelta = new Vector2(24f, ROW_H - 4f);
            }
        }

        private void RecolorResultRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                string rowId = i >= 0 && i < _results.Count ? GetOptionId(_results[i]) : "";
                bool selected = _sub == SubTab.Costumes ? _selectedIds.Contains(rowId)
                    : _sub == SubTab.Colours ? _selectedColourId == rowId
                    : _sub == SubTab.Patterns ? _selectedPatternId == rowId
                    : _selectedFaceplateId == rowId;
                SetRowColors(_rows[i], selected);
                SetRowTextColor(_rows[i], WHITE);
            }
        }

        private void SetRowTextColor(Button btn, Color color)
        {
            if (btn == null) return;
            var texts = btn.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
                if (texts[i] != null) texts[i].color = color;
        }

        private void SetRowColors(Button btn, bool selected)
        {
            if (btn == null) return;
            var color = selected ? SEL_COLOR : ROW_COLOR;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = color;
            var cols = btn.colors;
            cols.normalColor = color;
            cols.highlightedColor = selected ? SEL_COLOR : new Color(0.22f, 0.22f, 0.22f, 1f);
            cols.pressedColor = selected ? new Color(0.05f, 0.38f, 0.12f, 1f) : ROW_COLOR * 0.7f;
            cols.colorMultiplier = 1f;
            cols.fadeDuration = 0f;
            btn.colors = cols;
        }

        public static string GetDisplayName(CostumeOption option)
        {
            try { return option.CMSData.Name._text ?? option.name ?? ""; } catch { }
            try { return option.name ?? ""; } catch { }
            return "";
        }

        public static string GetDisplayName(UnityEngine.Object option)
        {
            if (option == null) return "";
            try { if (option.TryCast<CostumeOption>() != null) return GetDisplayName(option.Cast<CostumeOption>()); } catch { }
            try { var c = option.Cast<ColourOption>(); return c.CMSData.Name._text ?? c.name ?? ""; } catch { }
            try { var p = option.Cast<SkinPatternOption>(); return p.CMSData.Name._text ?? p.name ?? ""; } catch { }
            try { var f = option.Cast<FaceplateOption>(); return ((ItemDefinitionSO)f).CMSData.Name._text ?? f.name ?? ""; } catch { }
            try { return option.name ?? ""; } catch { }
            return "";
        }

        private static Sprite GetIconSprite(UnityEngine.Object option)
        {
            if (option == null) return null;
            ItemDefinitionSO def = null;
            try { def = option.Cast<ItemDefinitionSO>(); } catch { }
            if (def == null) return null;
            try { var s = def.MenuDisplaySprite; if (s != null) return s; } catch { }
            try { var s = def._spriteAtlasLoadableAsset.AssetRef.LoadAsset<Sprite>().Result; if (s != null) return s; } catch { }
            return null;
        }

        private string GetOptionId(UnityEngine.Object option)
        {
            if (option == null) return "";
            try
            {
                if (_sub == SubTab.Colours) return SkinApplicationService.GetGameColourOptionId(option.Cast<ColourOption>());
                if (_sub == SubTab.Patterns) return SkinApplicationService.GetGamePatternOptionId(option.Cast<SkinPatternOption>());
                if (_sub == SubTab.Faceplates) return SkinApplicationService.GetGameFaceplateOptionId(option.Cast<FaceplateOption>());
                return SkinApplicationService.GetGameCosmeticOptionId(option.Cast<CostumeOption>());
            }
            catch { return ""; }
        }

        private void SetStatus(string text)
        {
            if (_status != null) _status.text = text;
            Plugin.Log.LogInfo(text);
        }
    }
}
