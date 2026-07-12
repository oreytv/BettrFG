using BetterFG.Services;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.UI
{
    public class SlotDropdown
    {
        public const float ITEM_H = 18f * UIScale.S;
        public const float ANIM_DUR = 0.22f;

        private static readonly AnimationCurve ScaleCurve = new AnimationCurve(new Keyframe[]
        {
            new Keyframe(0.0000f, 0.0000f, 0.0162f, 0.0162f),
            new Keyframe(0.2214f, 0.4801f, 1.8824f, 1.8824f),
            new Keyframe(1.0000f, 1.0000f, 0.0000f, 0.0000f),
        });


        private GameObject _go;
        private float _animElapsed = -1f;
        public bool IsOpen => _go != null;

        // tab visuals we hid while the dropdown is up, restored on close
        private readonly List<GameObject> _hidden = new List<GameObject>();
        private RectTransform _tabRoot;

        // zebra tints for the button bg sprite
        private static readonly Color ROW_ZEBRA_A = new Color(0.32f, 0.32f, 0.34f, 1f);
        private static readonly Color ROW_ZEBRA_B = new Color(0.24f, 0.24f, 0.26f, 1f);

        // parents into the owning tab root and fills the content area (full tab width, below title bar).
        // grows downward from the top like a real dropdown.
        public void Open(RectTransform tabRoot, int ownerIdx, string ownerName, string[] tabNames, string[] tabTitles, string[] occupiedNames)
        {
            Close();
            if (tabRoot == null) return;

            AudioService.PlaySlotDwopdmdmom();

            // how tall: two buttons per row, + header, capped so it never overruns the tab
            int tabCount = 0;
            if (tabNames != null)
                foreach (var n in tabNames) if (!string.IsNullOrEmpty(n)) tabCount++;
            int rowCount = (tabCount + 1) / 2;
            float panelH = (rowCount + 1) * (ITEM_H + 1f) + 8f;
            float maxH = UIScale.TAB_CONTENT_H - 12f;
            if (panelH > maxH) panelH = maxH;

            // hide the tab's title + content (the "Content" wrapper) but keep its background
            _tabRoot = tabRoot;
            _hidden.Clear();
            var contentT = tabRoot.Find("Content");
            if (contentT != null && contentT.gameObject.activeSelf)
            {
                contentT.gameObject.SetActive(false);
                _hidden.Add(contentT.gameObject);
            }

            _go = new GameObject("SlotDropdown");
            _go.hideFlags = HideFlags.HideAndDontSave;
            _go.transform.SetParent(tabRoot, false);
            _go.transform.SetAsLastSibling();

            // full tab width, anchored near the top (title is hidden so sit a bit higher),
            // height = content. grows downward. no background.
            const float TOP_MARGIN = 6f;
            var rt = _go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f); // top-center: grows downward
            rt.offsetMin = new Vector2(0f, -TOP_MARGIN - panelH);
            rt.offsetMax = new Vector2(0f, -TOP_MARGIN);
            rt.localScale = new Vector3(1f, 0f, 1f);

            var cg = _go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = true;

            // pinned header (NOT inside the scroll rect)
            float headerH = ITEM_H + 4f;
            var titleGo = new GameObject("Title");
            titleGo.transform.SetParent(_go.transform, false);
            var titleRt = titleGo.AddComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.sizeDelta = new Vector2(0f, headerH);
            titleRt.anchoredPosition = Vector2.zero;
            titleRt.offsetMin = new Vector2(10f, titleRt.offsetMin.y);
            var titleTxt = titleGo.AddComponent<Text>();
            titleTxt.text = "SWITCH TAB";
            titleTxt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleTxt.fontSize = 15;
            titleTxt.fontStyle = FontStyle.Bold;
            titleTxt.color = new Color(1f, 1f, 1f, 1f);
            titleTxt.alignment = TextAnchor.MiddleLeft;
            titleTxt.raycastTarget = false;

            // scroll (below the header)
            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(_go.transform, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = new Vector2(0f, -headerH);
            var sr = scrollGo.AddComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 40f;

            var viewGo = new GameObject("Viewport");
            viewGo.transform.SetParent(scrollGo.transform, false);
            var viewRt = viewGo.AddComponent<RectTransform>();
            viewRt.anchorMin = Vector2.zero;
            viewRt.anchorMax = Vector2.one;
            viewRt.offsetMin = viewRt.offsetMax = Vector2.zero;
            viewGo.AddComponent<RectMask2D>();
            sr.viewport = viewRt;

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(viewGo.transform, false);
            var contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.sizeDelta = Vector2.zero;
            var vlg = contentGo.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 1f;
            vlg.padding = new RectOffset(3, 3, 2, 4);
            contentGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sr.content = contentRt;

            // occupied slots
            var occupied = new HashSet<string>();
            if (occupiedNames != null)
            {
                foreach (var name in occupiedNames)
                    if (!string.IsNullOrEmpty(name)) occupied.Add(name);
            }

            if (tabNames == null) tabNames = new string[0];
            int cellIdx = 0;
            GameObject rowGo = null;
            for (int i = 0; i < tabNames.Length; i++)
            {
                string tabName = tabNames[i];
                if (string.IsNullOrEmpty(tabName)) continue;
                string tabTitle = (tabTitles != null && i < tabTitles.Length && !string.IsNullOrEmpty(tabTitles[i]))
                    ? tabTitles[i] : tabName;

                bool isCurrentSlot = tabTitle == ownerName;
                bool blocked = occupied.Contains(tabTitle) && !isCurrentSlot;
                string capturedName = tabName;
                int capturedIdx = ownerIdx;

                // current slot is clickable (just closes the dropdown, no swap); only blocked is dead
                bool dead = blocked;
                bool disabled = blocked || isCurrentSlot;

                // two buttons per row: open a new horizontal row every other cell
                if (cellIdx % 2 == 0)
                {
                    rowGo = new GameObject("Row");
                    rowGo.transform.SetParent(contentGo.transform, false);
                    rowGo.AddComponent<RectTransform>();
                    var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
                    hlg.childForceExpandWidth = true;
                    hlg.childForceExpandHeight = true;
                    hlg.spacing = 2f;
                    rowGo.AddComponent<LayoutElement>().preferredHeight = ITEM_H;
                }

                // zebra: alternate tint per cell
                Color bgColor = cellIdx++ % 2 == 0 ? ROW_ZEBRA_A : ROW_ZEBRA_B;

                Color textColor = isCurrentSlot
                    ? new Color(0.6f, 1f, 0.6f, 0.9f)
                    : disabled
                        ? new Color(1f, 1f, 1f, 0.07f)   // way more transparent, barely readable
                        : new Color(1f, 1f, 1f, 0.88f);

                Action clickAction = dead ? null : (isCurrentSlot
                    ? new Action(() =>
                    {
                        // just close and keep this tab open — don't swap, don't re-close it
                        var ui = BetterFGUIMan.Instance;
                        ui?.KeepDropdownTabOpen();
                        Close();
                    })
                    : new Action(() =>
                    {
                        Close();
                        var ui = BetterFGUIMan.Instance;
                        if (ui != null) ui.SwapSlotFromDropdown(capturedIdx, capturedName);
                    }));

                var btn = UGUIShip.CreateButton(rowGo.transform, tabTitle,
                    bgColor, textColor, 13, clickAction, dead, customSprite: true, shine: false);

                // keep the zebra tint on dead/current cells instead of the darkened disabled bg
                if (dead || isCurrentSlot)
                {
                    var c = btn.colors;
                    c.disabledColor = bgColor;
                    btn.colors = c;
                }

                var lbl = btn.transform.Find("Label")?.GetComponent<Text>();
                if (lbl != null)
                {
                    lbl.alignment = TextAnchor.MiddleLeft;
                    var lblRt = lbl.GetComponent<RectTransform>();
                    if (lblRt != null) lblRt.offsetMin = new Vector2(8f, lblRt.offsetMin.y);
                }

                if (dead) btn.interactable = false;
            }

            _animElapsed = 0f;
        }

        public void Close()
        {
            _animElapsed = -1f;
            if (_go != null) { UnityEngine.Object.Destroy(_go); _go = null; }

            // restore the tab visuals we hid
            foreach (var go in _hidden)
                if (go != null) go.SetActive(true);
            _hidden.Clear();
            _tabRoot = null;
        }

        // call from uiman's Update
        public void Tick()
        {
            if (_animElapsed < 0f || _go == null) return;
            _animElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_animElapsed / ANIM_DUR);
            _go.GetComponent<RectTransform>().localScale = new Vector3(1f, ScaleCurve.Evaluate(t), 1f);
            if (t >= 1f) _animElapsed = -1f;
        }

        public bool HitTest(Vector2 screenPos)
        {
            if (_go == null) return false;
            var rt = _go.GetComponent<RectTransform>();
            return rt != null && RectTransformUtility.RectangleContainsScreenPoint(rt, screenPos, null);
        }
    }
}
