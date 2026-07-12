using System;
using System.Collections.Generic;
using BetterFG.Tweaks;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Windows
{
    public class LobbyAutokickConfigWindow : BetterFGWindow
    {
        public LobbyAutokickConfigWindow(IntPtr ptr) : base(ptr) { }

        public static LobbyAutokickConfigWindow Instance { get; private set; }

        protected override float WindowWidth => 320f;
        protected override float WindowHeight => 180f;
        protected override string WindowTitle => "Lobby Autokick";
        protected override string BgResourceName => "BetterFG.assets.ui.windows.generalbg.png";
        protected override bool DraggableFromTitle => true;

        private readonly List<string> _checks = new List<string>();
        private ScrollRect _scroll;
        private RectTransform _scrollRt;

        private const float ROW_H = 21f;
        private const float BTN_H = 16f;
        private static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.03f);
        private static readonly Color ROW_ODD = new Color(0f, 0f, 0f, 0f);
        private static readonly Color BTN_RED = new Color(0.45f, 0.22f, 0.22f, 1f);
        private static readonly Color BTN_BLUE = new Color(0.22f, 0.34f, 0.55f, 1f);

        public void Configure(LobbyAutokickTweak tweak)
        {
            Instance = this;
            Reload();
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

        private void Reload()
        {
            _checks.Clear();
            var loaded = LobbyAutokickTweak.LoadChecks();
            for (int i = 0; i < loaded.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(loaded[i]))
                    _checks.Add(loaded[i]);
            }
        }

        private void Save(bool trimEmpty)
        {
            if (trimEmpty)
            {
                for (int i = _checks.Count - 1; i >= 0; i--)
                {
                    if (string.IsNullOrWhiteSpace(_checks[i]))
                        _checks.RemoveAt(i);
                    else
                        _checks[i] = _checks[i].Trim();
                }
            }

            LobbyAutokickTweak.SaveChecks(_checks);
        }

        protected override void ManagedUpdate()
        {
            base.ManagedUpdate();
            if (_scroll == null || _scrollRt == null) return;

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) < 0.01f) return;

            var mouse = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            if (!RectTransformUtility.RectangleContainsScreenPoint(_scrollRt, mouse, null)) return;

            _scroll.verticalNormalizedPosition = Mathf.Clamp01(
                _scroll.verticalNormalizedPosition + wheel * 0.35f);
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            BgPosition = new Vector3(179.7451f, 70.46f, 0f);
            BgScale = new Vector3(1.2833f, 4.3332f, 1f);
            ContentPosition = new Vector3(177.6421f, -4.4f, 0f);
            ContentScale = new Vector3(1.0473f, 1f, 1f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(32.5674f, -7f, 0f);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            float w = WindowWidth - PAD * 2f;
            float y = PAD * 0.5f;

            MakeLabel(contentRoot,
                new Rect(PAD, y, w - 48f, BTN_H),
                "Auto kick if name contains:",
                FS_SM,
                new Color(1f, 1f, 1f, 0.72f));

            UGUIShip.CreateButton(contentRoot,
                new Rect(PAD + w - 42f, y, 42f, BTN_H),
                "ADD",
                BTN_BLUE,
                WHITE, FS_SM,
                new Action(() =>
                {
                    _checks.Add("");
                    Save(false);
                    RebuildContent();
                }));

            y += BTN_H + 4f;

            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(contentRoot, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(scrollRt, new Rect(PAD, y, w, WindowHeight - TITLE_H - y - PAD));
            scrollGo.AddComponent<Image>().color = Color.clear;
            _scrollRt = scrollRt;

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 18f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll = scroll;

            var vpGo = new GameObject("Viewport");
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRt = vpGo.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = vpRt.offsetMax = Vector2.zero;
            vpGo.AddComponent<RectMask2D>();
            scroll.viewport = vpRt;

            var listGo = new GameObject("List");
            listGo.transform.SetParent(vpGo.transform, false);
            var listRt = listGo.AddComponent<RectTransform>();
            listRt.anchorMin = new Vector2(0f, 1f);
            listRt.anchorMax = new Vector2(1f, 1f);
            listRt.pivot = new Vector2(0.5f, 1f);
            listRt.offsetMin = listRt.offsetMax = Vector2.zero;
            var layout = listGo.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 0f;
            var fit = listGo.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = listRt;

            for (int i = 0; i < _checks.Count; i++)
                BuildCheckRow(listRt, i, i % 2 == 0 ? ROW_EVEN : ROW_ODD);

            if (_checks.Count == 0)
                BuildEmptyRow(listRt);
        }

        private void BuildEmptyRow(RectTransform parent)
        {
            var rowGo = new GameObject("Row_empty");
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = ROW_EVEN;
            MakeLabel(rowGo.transform, new Rect(5f, 0f, WindowWidth - PAD * 2f - 10f, ROW_H),
                "No checks. Add one or hit default.", FS_SM, HINT);
        }

        private void BuildCheckRow(RectTransform parent, int idx, Color bg)
        {
            var rowGo = new GameObject("Row_" + idx);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = bg;

            int captured = idx;
            float removeW = 22f;
            float pad = 5f;
            var field = UGUIShip.CreateInputField(rowGo.transform,
                new Rect(0f, 0f, 10f, ROW_H),
                "name contains...",
                Color.clear,
                WHITE,
                FS_SM);
            var fieldRt = field.GetComponent<RectTransform>();
            if (fieldRt != null)
            {
                fieldRt.anchorMin = Vector2.zero;
                fieldRt.anchorMax = Vector2.one;
                fieldRt.offsetMin = Vector2.zero;
                fieldRt.offsetMax = Vector2.zero;
            }
            UGUIShip.SetInputText(field, _checks[idx] ?? "", false);
            field.onValueChanged.AddListener(new Action<string>(value =>
            {
                if (captured < 0 || captured >= _checks.Count) return;
                _checks[captured] = value ?? "";
                Save(false);
            }));
            field.onEndEdit.AddListener(new Action<string>(value =>
            {
                if (captured < 0 || captured >= _checks.Count) return;
                _checks[captured] = value ?? "";
                Save(true);
                RebuildContent();
            }));

            UGUIShip.CreateButton(rowGo.transform,
                new Rect(WindowWidth - PAD * 2f - removeW - pad, 2f, removeW, BTN_H),
                "X",
                BTN_RED,
                WHITE,
                FS_SM,
                new Action(() =>
                {
                    if (captured < 0 || captured >= _checks.Count) return;
                    _checks.RemoveAt(captured);
                    Save(true);
                    RebuildContent();
                }));
        }
    }
}
