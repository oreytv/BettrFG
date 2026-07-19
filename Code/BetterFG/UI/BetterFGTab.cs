using System;
using System.Collections.Generic;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI
{
    // shared config for the tab-title hover background ("BG_Hover"). by default it only shows on
    // hover; with AlwaysShow on it stays visible at IdleAlpha when not hovered and full alpha when
    // hovered, and Tint recolors it in both states. each BetterFGTab drives its OWN image and
    // registers itself here so option changes broadcast to every live tab. we key on the tab (a
    // managed MonoBehaviour) not the RawImage, because using IL2Cpp Unity objects as dictionary
    // keys is unreliable across the wrapper boundary.
    public static class TabHoverStyle
    {
        public const string KEY_ALWAYS = "ui.tabhover.always";
        public const string KEY_IDLE_ALPHA = "ui.tabhover.idleAlpha";
        public const string KEY_TINT_R = "ui.tabhover.tintR";
        public const string KEY_TINT_G = "ui.tabhover.tintG";
        public const string KEY_TINT_B = "ui.tabhover.tintB";

        private static readonly List<BetterFGTab> _tabs = new List<BetterFGTab>();
        // every button-shine overlay image, so a tint change recolors them live. keeps each image's
        // own alpha (0.4 idle / 1.0 hover) — we only override RGB.
        private static readonly List<Image> _shines = new List<Image>();

        public static bool AlwaysShow;
        public static float IdleAlpha = 0.25f;
        public static Color Tint = Color.white;

        private static readonly System.Globalization.CultureInfo CI = System.Globalization.CultureInfo.InvariantCulture;
        private static bool _loaded;

        // lazy — tabs can build before BetterFGUIMan.Awake runs (IL2Cpp doesn't always fire Awake
        // synchronously on AddComponent), so make sure the real values are read on first use.
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            LoadFromSettings();
        }

        public static void LoadFromSettings()
        {
            _loaded = true;
            AlwaysShow = SettingsService.Get(KEY_ALWAYS, "false") == "true";
            IdleAlpha = F(KEY_IDLE_ALPHA, 0.25f);
            Tint = new Color(F(KEY_TINT_R, 1f), F(KEY_TINT_G, 1f), F(KEY_TINT_B, 1f));
        }

        private static float F(string key, float def) =>
            float.TryParse(SettingsService.Get(key, def.ToString(CI)), System.Globalization.NumberStyles.Float, CI, out float v) ? v : def;

        public static void Save()
        {
            SettingsService.Set(KEY_ALWAYS, AlwaysShow ? "true" : "false");
            SettingsService.Set(KEY_IDLE_ALPHA, IdleAlpha.ToString(CI));
            SettingsService.Set(KEY_TINT_R, Tint.r.ToString(CI));
            SettingsService.Set(KEY_TINT_G, Tint.g.ToString(CI));
            SettingsService.Set(KEY_TINT_B, Tint.b.ToString(CI));
        }

        public static void Register(BetterFGTab tab)
        {
            if (tab == null || _tabs.Contains(tab)) return;
            _tabs.Add(tab);
        }

        // called by UGUIShip.BuildShine — the shine already has its idle color set; we just tint it
        // and remember it so future tint changes recolor it live.
        public static void RegisterShine(Image shine)
        {
            if (shine == null) return;
            EnsureLoaded();
            _shines.Add(shine);
            shine.color = new Color(Tint.r, Tint.g, Tint.b, shine.color.a);
        }

        // push current style to every live tab + shine (called when a slider/toggle changes). prunes
        // entries whose GameObject got destroyed on a slot swap.
        public static void ApplyAll()
        {
            _tabs.RemoveAll(t => t == null);
            foreach (var t in _tabs) t.ApplyHoverStyle();

            _shines.RemoveAll(s => s == null);
            foreach (var s in _shines) s.color = new Color(Tint.r, Tint.g, Tint.b, s.color.a);
        }
    }

    public class BetterFGTab : MonoBehaviour
    {
        public BetterFGTab(IntPtr ptr) : base(ptr) { }

        // the tab-title hover overlay, found under BG after BuildBackground runs
        private RawImage _hoverImg;
        private bool _hovered;

        public virtual string TabTitle => "Tab";

        public float TabWidth { get; set; } = UIScale.TAB_W;
        public float TabHeight { get; set; } = UIScale.TAB_CONTENT_H;

        // optional: override the local Y position when this tab is opened
        public float? OpenedTabLocalY { get; protected set; } = null;

        public static float TITLE_H => UIScale.TITLE_H;

        public RectTransform Root { get; private set; }
        public bool IsOpen { get; set; } = false;

        private bool _built = false;

        public void Initialize(RectTransform root) { Root = root; }

        public void EnsureBuilt()
        {
            if (_built) return;
            _built = true;
            BuildTab();
        }

        private void BuildTab()
        {
            BuildBackground(Root);
            // bg images are decorative — kill their raycast so they don't eat input on whatever's behind
            foreach (var img in Root.GetComponentsInChildren<Image>(true))
                if (img != null) img.raycastTarget = false;
            foreach (var raw in Root.GetComponentsInChildren<RawImage>(true))
                if (raw != null) raw.raycastTarget = false;

            TabHoverStyle.Register(this);
            ApplyHoverStyle();

            var windowGo = new GameObject("Content");
            windowGo.transform.SetParent(Root, false);
            var windowRt = windowGo.AddComponent<RectTransform>();
            windowRt.anchorMin = Vector2.zero;
            windowRt.anchorMax = Vector2.one;
            windowRt.offsetMin = windowRt.offsetMax = Vector2.zero;

            var titleGo = new GameObject("TitleBar");
            titleGo.transform.SetParent(windowGo.transform, false);
            var titleRt = titleGo.AddComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.offsetMin = new Vector2(0f, -UIScale.TITLE_H);
            titleRt.offsetMax = Vector2.zero;

            var t = UGUIShip.CreateLabel(titleGo.transform, default, TabTitle.ToUpper(), UIScale.FS_TITLE,
                new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleLeft);
            t.fontStyle = FontStyle.Bold;
            var labelRt = t.rectTransform;
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = new Vector2(UIScale.PAD * 3f, 0f);
            labelRt.offsetMax = Vector2.zero;

            var hoverGo = new GameObject("HoverTint");
            hoverGo.transform.SetParent(titleGo.transform, false);
            var hoverRt = hoverGo.AddComponent<RectTransform>();
            hoverRt.anchorMin = Vector2.zero;
            hoverRt.anchorMax = Vector2.one;
            hoverRt.offsetMin = hoverRt.offsetMax = Vector2.zero;
            hoverGo.AddComponent<Image>().color = Color.clear;
            var hoverTint = hoverGo.AddComponent<TabHoverTint>();
            hoverTint.Tab = this;

            BetterFGUIMan.MakeObjectTooltip(titleRt, "Right click to switch!");

            titleGo.AddComponent<Image>().color = Color.clear;
            var btn = titleGo.AddComponent<Button>();
            var cols = btn.colors;
            cols.normalColor = cols.highlightedColor = cols.pressedColor = Color.white;
            cols.colorMultiplier = 1f;
            btn.colors = cols;
            btn.transition = Selectable.Transition.None;
            var nav = btn.navigation;
            nav.mode = UnityEngine.UI.Navigation.Mode.None;
            btn.navigation = nav;
            btn.onClick.AddListener(new Action(() =>
            {
                OnTitleClicked();
                if (UnityEngine.EventSystems.EventSystem.current != null)
                    UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }));

            var contentGo = new GameObject("ContentArea");
            contentGo.transform.SetParent(windowGo.transform, false);
            var contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.anchorMin = Vector2.zero;
            contentRt.anchorMax = Vector2.one;
            contentRt.offsetMin = Vector2.zero;
            contentRt.offsetMax = new Vector2(0f, -UIScale.TITLE_H);

            BuildContent(contentRt);
        }

        protected virtual void BuildBackground(RectTransform root) { }
        protected virtual void OnTitleHoverChanged(bool hovering) { }
        // called by the UI manager when this tab is opened/closed
        public virtual void OnOpened() { }
        public virtual void OnClosed() { }
        internal void NotifyTitleHover(bool hovering)
        {
            _hovered = hovering;
            ApplyHoverStyle();
        }

        // the BG_Hover RawImage can go stale across a slot swap/rebuild (Unity fake-null), so re-find
        // it from the current hierarchy each time rather than trusting a cached ref.
        private RawImage FindHoverImg()
        {
            if (_hoverImg != null) return _hoverImg;
            if (Root == null) return null;
            foreach (var raw in Root.GetComponentsInChildren<RawImage>(true))
                if (raw != null && raw.gameObject.name == "BG_Hover") { _hoverImg = raw; break; }
            return _hoverImg;
        }

        // apply the shared hover-bg config to this tab's own overlay image, honoring current hover
        internal void ApplyHoverStyle()
        {
            var img = FindHoverImg();
            if (img == null) { OnTitleHoverChanged(_hovered); return; }
            TabHoverStyle.EnsureLoaded();
            bool visible = _hovered || TabHoverStyle.AlwaysShow;
            if (img.gameObject.activeSelf != visible) img.gameObject.SetActive(visible);
            if (!visible) return;
            float a = _hovered ? 1f : TabHoverStyle.IdleAlpha;
            var t = TabHoverStyle.Tint;
            img.color = new Color(t.r, t.g, t.b, a);
            img.SetAllDirty(); // RawImage.color alone doesn't always trigger a repaint under IL2Cpp uGUI
        }
        private void OnTitleClicked() { BetterFGUIMan.Instance?.ToggleTab(this); }
        protected virtual void BuildContent(RectTransform contentRoot) { }
    }
}