using System;
using BetterFG.Services;
using BetterFG.Customization.Player;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Windows
{
    public class ItemConfigWindow : BetterFGWindow
    {
        public ItemConfigWindow(IntPtr ptr) : base(ptr) { }

        protected override float WindowWidth => 320f;
        protected override float WindowHeight => 220f;
        protected override string WindowTitle => "Item Config";
        protected override string BgResourceName => "BetterFG.assets.ui.windows.generalbg.png";

        protected override bool DraggableFromTitle => true;

        protected override Vector3 InitialBgPosition => new Vector3(179.7451f, 93.1455f, 0f);
        protected override Vector3 InitialBgScale => new Vector3(1.2931f, 3.6616f, 1f);

        private SkinInfo _targetSkin;
        private SkinApplicationService _appService;
        private int _handOverride = 0;

        private float _flushTimer = 0f;
        private bool _dirty = false;
        private const float THROTTLE = 0.1f;

        private static readonly Color BTN_HAND = new Color(0.22f, 0.22f, 0.22f, 1f);
        private static readonly Color BTN_RESET = new Color(0.45f, 0.25f, 0.25f, 1f);
        private static readonly Color HINT_COL = new Color(1f, 1f, 1f, 0.35f);

        private Text _handLabel;

        // ── Public API ────────────────────────────────────────────────────────

        public void Configure(SkinInfo skin, SkinApplicationService appService, BetterFG.UI.BetterFGTab ownerTab = null)
        {
            _targetSkin = skin;
            _appService = appService;
            _handOverride = (skin?.handOverride == 2) ? 2 : 1;
            if (ownerTab != null) OwnerTab = ownerTab;
            ShowWindow();
            RebuildContent();
        }

        private string HandKey(string axis) => $"item{(_handOverride == 2 ? "r" : "l")}offset.{_targetSkin.file}.{axis}";
        private string RotKey(string axis) => $"item{(_handOverride == 2 ? "r" : "l")}rot.{_targetSkin.file}.{axis}";
        private string HandLabel() => _handOverride == 2 ? "R" : "L";

        private void OnReset()
        {
            if (_targetSkin == null) return;
            string f = _targetSkin.file;
            SettingsService.Remove($"itemloffset.{f}.x"); SettingsService.Remove($"itemloffset.{f}.y"); SettingsService.Remove($"itemloffset.{f}.z");
            SettingsService.Remove($"itemroffset.{f}.x"); SettingsService.Remove($"itemroffset.{f}.y"); SettingsService.Remove($"itemroffset.{f}.z");
            SettingsService.Remove($"itemlrot.{f}.x"); SettingsService.Remove($"itemlrot.{f}.y"); SettingsService.Remove($"itemlrot.{f}.z");
            SettingsService.Remove($"itemrrot.{f}.x"); SettingsService.Remove($"itemrrot.{f}.y"); SettingsService.Remove($"itemrrot.{f}.z");
            _handOverride = 1;
            _targetSkin.handOverride = 1;
            if (_handLabel != null) _handLabel.text = HandLabel();
            RebuildContent();
            Dirty();
        }

        // ── Update / flush ────────────────────────────────────────────────────

        protected override void ManagedUpdate()
        {
            base.ManagedUpdate();
            if (!_dirty) return;
            _flushTimer -= Time.deltaTime;
            if (_flushTimer > 0f) return;
            _dirty = false;
            Flush();
        }

        private void Flush()
        {
            if (_appService == null || _targetSkin == null) return;
            _targetSkin.handOverride = _handOverride;
            _appService.UpdateItemOffsets(_targetSkin.file, new Vector3(
                ReadF(HandKey("x")), ReadF(HandKey("y")), ReadF(HandKey("z"))));
            _appService.UpdateItemRotations(_targetSkin.file, new Vector3(
                ReadF(RotKey("x")), ReadF(RotKey("y")), ReadF(RotKey("z"))));
        }

        private void Dirty() { _dirty = true; if (!(_flushTimer > 0f)) _flushTimer = THROTTLE; }

        private static float ReadF(string key)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return float.TryParse(SettingsService.Get(key, "0"),
                System.Globalization.NumberStyles.Float, ci, out float v) ? v : 0f;
        }

        // ── Content ───────────────────────────────────────────────────────────

        protected override void BuildContent(RectTransform contentRoot)
        {
            BgPosition = new Vector3(179.7451f, 86.46f, 0f);
            BgScale = new Vector3(1.2833f, 4.3332f, 1f);
            ContentPosition = new Vector3(190.6421f, 4.4f, 0f);
            ContentScale = new Vector3(1.0473f, 1f, 1f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(32.5674f, -1f, 0f);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            if (_targetSkin == null) return;

            float w = WindowWidth - PAD * 2f;
            float cy = PAD * 0.5f;
            float rh = 14f;
            float gap = 5f;
            float lw = 12f;
            float vw = 34f;
            float sw = w - lw - vw - PAD * 0.5f;

            // Item name label
            MakeLabel(contentRoot, new Rect(PAD, cy, w, rh),
                _targetSkin.name, FS_SM, new Color(1f, 1f, 1f, 0.6f));
            cy += rh + gap;

            var windowRt = contentRoot.parent?.GetComponent<RectTransform>();
            if (windowRt != null)
            {
                float bh = 18f;
                float btnGap = 3f;
                float handW = 28f;
                float resetW = 44f;
                float btnY = TITLE_H * 0.5f;
                float yOff = -(btnY - bh * 0.5f);

                var handGo = new GameObject("HandToggle");
                handGo.transform.SetParent(windowRt, false);
                var handRt = handGo.AddComponent<RectTransform>();
                handRt.anchorMin = new Vector2(1f, 1f);
                handRt.anchorMax = new Vector2(1f, 1f);
                handRt.pivot = new Vector2(1f, 1f);
                handRt.sizeDelta = new Vector2(handW, bh);
                handRt.anchoredPosition = new Vector2(-(PAD + resetW + btnGap), yOff);
                _handLabel = UGUIShip.CreateButton(handGo.transform, new Rect(0f, 0f, handW, bh),
                    HandLabel(), BTN_HAND, WHITE, FS_SM, new Action(() =>
                    {
                        _handOverride = _handOverride == 1 ? 2 : 1;
                        if (_targetSkin != null) _targetSkin.handOverride = _handOverride;
                        if (_handLabel != null) _handLabel.text = HandLabel();
                        RebuildContent();
                        Dirty();
                    })).GetComponentInChildren<Text>();

                var resetGo = new GameObject("ResetBtn");
                resetGo.transform.SetParent(windowRt, false);
                var resetRt = resetGo.AddComponent<RectTransform>();
                resetRt.anchorMin = new Vector2(1f, 1f);
                resetRt.anchorMax = new Vector2(1f, 1f);
                resetRt.pivot = new Vector2(1f, 1f);
                resetRt.sizeDelta = new Vector2(resetW, bh);
                resetRt.anchoredPosition = new Vector2(-PAD, yOff);
                UGUIShip.CreateButton(resetGo.transform, new Rect(0f, 0f, resetW, bh),
                    "RESET", BTN_RESET, WHITE, FS_SM, new Action(OnReset));
            }

            MakeLabel(contentRoot, new Rect(PAD, cy, w, rh), "POSITION", FS_SM, HINT_COL);
            cy += rh + gap;
            BuildPosRow(contentRoot, cy, lw, sw, vw, rh, "X", "x"); cy += rh + gap;
            BuildPosRow(contentRoot, cy, lw, sw, vw, rh, "Y", "y"); cy += rh + gap;
            BuildPosRow(contentRoot, cy, lw, sw, vw, rh, "Z", "z"); cy += rh + gap * 2f;

            MakeSeparator(contentRoot, new Rect(PAD, cy, w, 1f)); cy += 1f + gap;

            MakeLabel(contentRoot, new Rect(PAD, cy, w, rh), "ROTATION", FS_SM, HINT_COL);
            cy += rh + gap;
            BuildRotRow(contentRoot, cy, lw, sw, vw, rh, "X", "x"); cy += rh + gap;
            BuildRotRow(contentRoot, cy, lw, sw, vw, rh, "Y", "y"); cy += rh + gap;
            BuildRotRow(contentRoot, cy, lw, sw, vw, rh, "Z", "z");
        }

        private void BuildPosRow(RectTransform parent, float cy, float lw, float sw, float vw, float rh, string label, string axis)
        {
            string key = HandKey(axis);
            float cur = ReadF(key);
            MakeLabel(parent, new Rect(PAD, cy, lw, rh), label, FS_SM, WHITE);
            var valLbl = MakeLabel(parent, new Rect(PAD + lw + sw + 2f, cy, vw, rh), cur.ToString("F2"), FS_SM, WHITE);
            var sl = UGUIShip.CreateSlider(parent, PAD + lw, cy, sw, "", (cur + 0.3f) / 0.6f, rh, 0f, FS_SM,
                new Action<float>(_ => { }), HINT_COL, new Color(0.7f, 0.7f, 0.7f, 1f));
            sl.onValueChanged.AddListener(new Action<float>(v =>
            {
                float m = v * 0.6f - 0.3f;
                SettingsService.Set(key, m.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (valLbl != null) valLbl.text = m.ToString("F2");
                Dirty();
            }));
        }

        private void BuildRotRow(RectTransform parent, float cy, float lw, float sw, float vw, float rh, string label, string axis)
        {
            string key = RotKey(axis);
            float cur = ReadF(key);
            MakeLabel(parent, new Rect(PAD, cy, lw, rh), label, FS_SM, WHITE);
            var valLbl = MakeLabel(parent, new Rect(PAD + lw + sw + 2f, cy, vw, rh), cur.ToString("F0"), FS_SM, WHITE);
            var sl = UGUIShip.CreateSlider(parent, PAD + lw, cy, sw, "", cur / 360f, rh, 0f, FS_SM,
                new Action<float>(_ => { }), HINT_COL, new Color(0.7f, 0.7f, 0.7f, 1f));
            sl.onValueChanged.AddListener(new Action<float>(v =>
            {
                float m = v * 360f;
                SettingsService.Set(key, m.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (valLbl != null) valLbl.text = m.ToString("F0");
                Dirty();
            }));
        }
    }
}