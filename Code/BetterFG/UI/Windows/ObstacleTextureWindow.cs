using System;
using System.Collections.Generic;
using System.IO;
using BetterFG.Features.UnityRound.Editor;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Windows
{
    // Level-editor only. Lists the unique textures found on the kept placeable obstacles in scene 0,
    // one row each. Select a row + press Enter to pick a PNG for it; assigning creates/updates the
    // row and autosaves to texture.json. The unity packager never touches that file.

    public class ObstacleTextureWindow : BetterFGWindow
    {
        public ObstacleTextureWindow(IntPtr ptr) : base(ptr) { }

        public static ObstacleTextureWindow Instance { get; private set; }

        protected override float WindowWidth => 340f;
        protected override float WindowHeight => 240f;
        protected override string WindowTitle => "Obstacle Textures";
        protected override string BgResourceName => "BetterFG.assets.ui.windows.generalbg.png";
        protected override bool DraggableFromTitle => true;

        protected override Vector3 InitialBgPosition => new Vector3(179.7451f, 93.1455f, 0f);
        protected override Vector3 InitialBgScale => new Vector3(1.2931f, 3.6616f, 1f);

        private static readonly Color BTN_REFRESH = new Color(0.22f, 0.34f, 0.55f, 1f);
        private static readonly Color BTN_PICK = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color BTN_RED = new Color(0.45f, 0.22f, 0.22f, 1f);
        private static readonly Color ROW_SEL = new Color(0.25f, 0.45f, 0.25f, 1f);
        private static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.04f);
        private static readonly Color ROW_ODD = new Color(0f, 0f, 0f, 0f);
        private static readonly Color HINT_COL = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color OK_COL = new Color(0.55f, 0.85f, 0.55f, 1f);
        private static readonly Color ERR_COL = new Color(0.9f, 0.45f, 0.45f, 1f);

        private const float ROW_H = 20f;

        private List<string> _texNames = new List<string>();
        private int _selected = -1;
        private bool _awaitingPng;        // true after Enter -> we asked for a png
        private string _pendingTex;       // which texture the png is for

        private ScrollRect _scroll;
        private RectTransform _scrollRt;
        private Text _statusLabel;

        // ── api ───────────────────────────────────────────────────────────────

        public void Configure()
        {
            Instance = this;
            SetAnchorPosition(new Vector2(540f, -40f));
            Rescan();
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

        private void Rescan()
        {
            _texNames = ObstacleTextureLoader.DiscoverTextureNames();
            if (_selected >= _texNames.Count) _selected = -1;
        }

        // ── update: scroll wheel + Enter to start a png pick ─────────────────────

        protected override void ManagedUpdate()
        {
            base.ManagedUpdate();

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                BeginPickForSelected();

            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift))
                DownloadSelected();

            if (_scroll == null || _scrollRt == null) return;
            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) < 0.01f) return;
            var mouse = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
            if (!RectTransformUtility.RectangleContainsScreenPoint(_scrollRt, mouse, null)) return;
            _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition + wheel * 0.3f);
        }

        // ── content ───────────────────────────────────────────────────────────

        protected override void BuildContent(RectTransform contentRoot)
        {
            BgPosition = new Vector3(179.7451f, 104.46f, 0f);
            BgScale = new Vector3(1.3633f, 5.8f, 1f);
            ContentPosition = new Vector3(190.6421f, 4.4f, 0f);
            ContentScale = new Vector3(1.0473f, 1f, 1f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(32.5674f, -1f, 0f);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            float w = WindowWidth - PAD * 2f;
            float y = PAD * 0.5f;

            MakeLabel(contentRoot, new Rect(PAD, y, w, 16f),
                "Obstacle textures  (Enter=set PNG, Shift=save PNG)", FS_SM, new Color(1f, 1f, 1f, 0.72f));
            y += 18f;

            // scroll list
            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(contentRoot, false);
            _scrollRt = scrollGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(_scrollRt, new Rect(PAD, y, w, WindowHeight - TITLE_H - y - 26f));
            scrollGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            _scroll = scrollGo.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;

            var vpGo = new GameObject("Viewport");
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRt = vpGo.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = vpRt.offsetMax = Vector2.zero;
            vpGo.AddComponent<RectMask2D>();
            _scroll.viewport = vpRt;

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
            listGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _scroll.content = listRt;

            if (_texNames.Count == 0)
                MakeLabel(listRt, new Rect(6f, 0f, w, ROW_H), "no placeable textures found — load a round first", FS_SM, HINT_COL);
            else
                for (int i = 0; i < _texNames.Count; i++)
                    BuildRow(listRt, i);

            _statusLabel = MakeLabel(contentRoot, new Rect(PAD, WindowHeight - TITLE_H - 22f, w - 76f, 16f),
                _awaitingPng ? "pick a PNG..." : "", FS_SM, HINT_COL);

            // refresh bottom-right
            var windowRt = contentRoot.parent?.GetComponent<RectTransform>();
            if (windowRt != null)
            {
                float bw = 70f, bh = 20f;
                var go = new GameObject("RefreshBtn");
                go.transform.SetParent(windowRt, false);
                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(1f, 0f);
                rt.sizeDelta = new Vector2(bw, bh);
                rt.anchoredPosition = new Vector2(-PAD, PAD);
                UGUIShip.CreateButton(go.transform, new Rect(0f, 0f, bw, bh),
                    "REFRESH", BTN_REFRESH, WHITE, FS_SM, new Action(OnRefresh));
            }
        }

        protected new void RebuildContent()
        {
            var windowRt = _contentRt?.parent?.GetComponent<RectTransform>();
            if (windowRt != null)
                for (int i = windowRt.childCount - 1; i >= 0; i--)
                {
                    var c = windowRt.GetChild(i);
                    if (c != null && c.name == "RefreshBtn") UnityEngine.Object.Destroy(c.gameObject);
                }
            base.RebuildContent();
        }

        private void BuildRow(RectTransform parent, int idx)
        {
            string texName = _texNames[idx];
            bool hasOverride = ObstacleTextureLoader.Overrides.ContainsKey(texName);
            bool isSel = idx == _selected;

            var rowGo = new GameObject("Row_" + idx);
            rowGo.transform.SetParent(parent, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            var img = rowGo.AddComponent<Image>();
            img.color = isSel ? ROW_SEL : (idx % 2 == 0 ? ROW_EVEN : ROW_ODD);

            int captured = idx;
            var btn = rowGo.AddComponent<Button>();
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(new Action(() => SelectRow(captured)));

            float w = WindowWidth - PAD * 2f;
            float removeW = 22f;

            // texture thumbnail to the left of the name
            float thumb = ROW_H - 4f;
            var thumbGo = new GameObject("Thumb");
            thumbGo.transform.SetParent(rowGo.transform, false);
            var thumbRt = thumbGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(thumbRt, new Rect(4f, 2f, thumb, thumb));
            var raw = thumbGo.AddComponent<RawImage>();
            raw.raycastTarget = false;
            var liveTex = ObstacleTextureLoader.GetCurrentTexture(texName);
            if (liveTex != null) raw.texture = liveTex;
            else raw.color = new Color(0f, 0f, 0f, 0.4f);

            float nameX = 4f + thumb + 4f;
            string label = (hasOverride ? "● " : "") + texName;
            MakeLabel(rowGo.transform, new Rect(nameX, 0f, w - removeW - nameX - 6f, ROW_H),
                label, FS_SM, hasOverride ? OK_COL : WHITE);

            if (hasOverride)
                UGUIShip.CreateButton(rowGo.transform, new Rect(w - removeW - 6f, 2f, removeW, ROW_H - 4f),
                    "x", BTN_RED, WHITE, FS_SM, new Action(() =>
                    {
                        ObstacleTextureLoader.RemoveOverride(texName);
                        SetStatus("removed " + texName, HINT_COL);
                        RebuildContent();
                    }));
        }

        // ── actions ───────────────────────────────────────────────────────────

        private void SelectRow(int idx)
        {
            _selected = (_selected == idx) ? -1 : idx;
            RebuildContent();
        }

        private void BeginPickForSelected()
        {
            if (_awaitingPng) return;
            if (_selected < 0 || _selected >= _texNames.Count) return;

            _awaitingPng = true;
            _pendingTex = _texNames[_selected];
            SetStatus($"pick a PNG for {_pendingTex}...", HINT_COL);

            WinDialogs.PickPng("Select PNG for " + _pendingTex, path =>
            {
                _awaitingPng = false;
                if (string.IsNullOrEmpty(path)) { SetStatus("cancelled", HINT_COL); return; }

                if (ObstacleTextureLoader.SetOverride(_pendingTex, path, out string error))
                    SetStatus("set " + _pendingTex, OK_COL);
                else
                    SetStatus("error: " + error, ERR_COL);

                RebuildContent();
            });
        }

        private void DownloadSelected()
        {
            if (_awaitingPng) return;
            if (_selected < 0 || _selected >= _texNames.Count) return;

            string texName = _texNames[_selected];
            if (ObstacleTextureLoader.SaveTexturePng(texName, out string path, out string error))
                SetStatus("saved " + Path.GetFileName(path), OK_COL);
            else
                SetStatus("error: " + error, ERR_COL);
        }

        private void OnRefresh()
        {
            Rescan();
            SetStatus($"found {_texNames.Count} texture(s)", HINT_COL);
            RebuildContent();
        }

        private void SetStatus(string text, Color col)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = text;
            _statusLabel.color = col;
        }
    }
}
