using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using BetterFG.Features.UnityRound.Editor;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    // Drill-in tab reached from CreativeTab's Configuration step "Custom textures" button. Not in the
    // tab registry (never shows in the slot dropdown) — you get here by button and leave via the back
    // button, which swaps the slot back to Creative. Overrides live obstacle textures on the loaded
    // BettrFG unity round and persist to texture.json next to its info.json.
    public class CustomCreativeTextureTab : BetterFGTab
    {
        public CustomCreativeTextureTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Creative - Custom Textures";

        private static float PAD => UIScale.PAD;
        private static float VPAD => UIScale.VPAD;
        private static float SH => UIScale.SH;
        private static float BTN_H => UIScale.BTN_H;
        private static int FS => UIScale.FS;
        private static int FS_SM => UIScale.FS_SM;
        private static float ROW_H => 26f * UIScale.S;

        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color WHITE = Color.white;
        private static readonly Color OK = new Color(0.55f, 0.85f, 0.55f, 1f);
        private static readonly Color ERR = new Color(0.9f, 0.45f, 0.45f, 1f);
        private static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color BTN_BLUE = new Color(0.22f, 0.34f, 0.55f, 1f);
        private static readonly Color BTN_RED = new Color(0.45f, 0.25f, 0.25f, 1f);
        private static readonly Color ROW_SEL = new Color(0.25f, 0.45f, 0.25f, 1f);
        private static readonly Color ROW_EVEN = new Color(1f, 1f, 1f, 0.04f);

        private List<string> _texNames = new List<string>();
        private int _texSelected = -1;
        private Text _texStatus;
        private RectTransform _texContentRt;

        private static Texture2D _bgTex;
        private static Texture2D _hoverTex;
        private GameObject _bgHoverGo;

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
            catch (Exception ex) { Plugin.Log.LogError("BetterFG: Tex load failed: " + ex.Message); }
            return cache;
        }

        protected override void BuildBackground(RectTransform root)
        {
            var bgTex = LoadTex("BetterFG.assets.ui.tab.creative.png", ref _bgTex);
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
                var hoverRt = hoverGo.AddComponent<RectTransform>();
                hoverRt.anchorMin = Vector2.zero;
                hoverRt.anchorMax = Vector2.one;
                hoverRt.offsetMin = hoverRt.offsetMax = Vector2.zero;
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
            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            float backW = 70f * UIScale.S;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, backW, BTN_H), "< back", BTN_DARK, WHITE, FS_SM,
                new Action(() => BetterFGUIMan.Instance?.SwitchSlotTab(this, BetterFGTabRegistry.CreateTab("Creative"))));
            UGUIShip.CreateLabel(contentRoot, new Rect(PAD + backW + PAD, y, w - backW - PAD, BTN_H),
                "Enter=set PNG, Shift=save PNG", FS_SM, new Color(1f, 1f, 1f, 0.72f), TextAnchor.MiddleLeft);
            y += BTN_H + SH;

            float listH = TabHeight - y - BTN_H - VPAD - SH;

            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(contentRoot, false);
            var scrollRt = scrollGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(scrollRt, new Rect(PAD, y, w, listH));
            scrollGo.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.25f);

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            var vpGo = new GameObject("Viewport");
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpRt = vpGo.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero; vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = vpRt.offsetMax = Vector2.zero;
            vpGo.AddComponent<RectMask2D>();
            scroll.viewport = vpRt;

            var listGo = new GameObject("List");
            listGo.transform.SetParent(vpGo.transform, false);
            _texContentRt = listGo.AddComponent<RectTransform>();
            _texContentRt.anchorMin = new Vector2(0f, 1f);
            _texContentRt.anchorMax = new Vector2(1f, 1f);
            _texContentRt.pivot = new Vector2(0.5f, 1f);
            _texContentRt.offsetMin = _texContentRt.offsetMax = Vector2.zero;
            var layout = listGo.AddComponent<VerticalLayoutGroup>();
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 0f;
            listGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.content = _texContentRt;

            float refreshW = 80f * UIScale.S;
            float barY = TabHeight - BTN_H - VPAD;
            _texStatus = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, barY, w - refreshW - PAD, BTN_H), "", FS_SM, HINT);
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + w - refreshW, barY, refreshW, BTN_H),
                "REFRESH", BTN_BLUE, WHITE, FS_SM, new Action(() => { RescanTextures(); SetTexStatus($"found {_texNames.Count}", HINT); }));

            RescanTextures();
        }

        void Update()
        {
            if (!IsOpen || !UnityRoundLoader.HasSpawned) return;
            if (_texSelected < 0 || _texSelected >= _texNames.Count) return;

            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                string texName = _texNames[_texSelected];
                WinDialogs.PickPng("Select PNG for " + texName, path =>
                {
                    if (string.IsNullOrEmpty(path)) { SetTexStatus("cancelled", HINT); return; }
                    if (ObstacleTextureLoader.SetOverride(texName, path, out string error)) SetTexStatus("set " + texName, OK);
                    else SetTexStatus("error: " + error, ERR);
                    RebuildTexRows();
                });
            }

            if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift))
            {
                string texName = _texNames[_texSelected];
                if (ObstacleTextureLoader.SaveTexturePng(texName, out string path, out string error)) SetTexStatus("saved " + Path.GetFileName(path), OK);
                else SetTexStatus("error: " + error, ERR);
            }
        }

        void RescanTextures()
        {
            _texNames = ObstacleTextureLoader.DiscoverTextureNames();
            if (_texSelected >= _texNames.Count) _texSelected = -1;
            RebuildTexRows();
        }

        void RebuildTexRows()
        {
            if (_texContentRt == null) return;
            for (int i = _texContentRt.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_texContentRt.GetChild(i).gameObject);

            if (_texNames.Count == 0)
            {
                UGUIShip.CreateLabel(_texContentRt, new Rect(PAD, 0f, TabWidth, ROW_H), "no placeable textures found — load a round first", FS_SM, HINT);
                return;
            }
            for (int i = 0; i < _texNames.Count; i++)
                BuildTexRow(i);
        }

        void BuildTexRow(int idx)
        {
            string texName = _texNames[idx];
            bool hasOverride = ObstacleTextureLoader.Overrides.ContainsKey(texName);
            bool isSel = idx == _texSelected;

            var rowGo = new GameObject("Row_" + idx);
            rowGo.transform.SetParent(_texContentRt, false);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            rowGo.AddComponent<Image>().color = isSel ? ROW_SEL : (idx % 2 == 0 ? ROW_EVEN : new Color(0f, 0f, 0f, 0f));

            int captured = idx;
            var btn = rowGo.AddComponent<Button>();
            var nav = btn.navigation; nav.mode = Navigation.Mode.None; btn.navigation = nav;
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(new Action(() => { _texSelected = _texSelected == captured ? -1 : captured; RebuildTexRows(); }));

            float w = TabWidth - PAD * 2f;
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

            float nameX = 4f + thumb + 6f;
            float rmW = 24f * UIScale.S;
            UGUIShip.CreateLabel(rowGo.transform, new Rect(nameX, 0f, w - rmW - nameX, ROW_H),
                (hasOverride ? "● " : "") + texName, FS_SM, hasOverride ? OK : WHITE);

            if (hasOverride)
                UGUIShip.CreateButton(rowGo.transform, new Rect(w - rmW, 2f, rmW - 4f, ROW_H - 4f), "x", BTN_RED, WHITE, FS_SM, new Action(() =>
                {
                    ObstacleTextureLoader.RemoveOverride(texName);
                    SetTexStatus("removed " + texName, HINT);
                    RebuildTexRows();
                }));
        }

        void SetTexStatus(string text, Color col)
        {
            if (_texStatus == null) return;
            _texStatus.text = text;
            _texStatus.color = col;
        }
    }
}
