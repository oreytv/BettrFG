using System;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Windows
{
    public class AudioSettingsWindow : BetterFGWindow
    {
        public AudioSettingsWindow(IntPtr ptr) : base(ptr) { }

        protected override float WindowWidth => 340f;
        protected override float WindowHeight => 90f;
        protected override string WindowTitle => "Audio Settings";
        protected override string BgResourceName => "BetterFG.assets.ui.windows.audiosettingsbg.png";

        private const string KEY_MASTER_VOL = "audio.master.volume";
        private const string KEY_SFX_HOVER = "audio.sfx.hover";
        private const string KEY_SFX_CLICK = "audio.sfx.click";
        private const string KEY_SFX_TAB = "audio.sfx.tab";

        private static readonly Color HINT_COL = new Color(1f, 1f, 1f, 0.35f);

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = WindowWidth - PAD * 2f;
            float lh = UIScale.LH;
            float bh = 14f;
            float halfW = w * 0.5f - PAD * 0.5f;
            float tbw = 28f;
            float cy = PAD;

            // ── Left: master volume ───────────────────────────────────────────
            float vol = LoadFloat(KEY_MASTER_VOL, 1f);
            MakeLabel(contentRoot, new Rect(PAD, cy, halfW, lh), "MASTER VOLUME", FS_SM, HINT_COL);
            cy += lh + 2f;

            UGUIShip.CreateSlider(contentRoot, PAD, cy, halfW,
                "", vol, lh, 0f, FS_SM,
                new Action<float>(v =>
                {
                    SettingsService.Set(KEY_MASTER_VOL,
                        v.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    AudioService.SetMasterVolume(v);
                }),
                HINT_COL, new Color(0.7f, 0.7f, 0.7f, 1f));

            // ── Right: per-type toggles ───────────────────────────────────────
            float rx = PAD + halfW + PAD;
            float rcy = PAD;

            MakeLabel(contentRoot, new Rect(rx, rcy, halfW, lh), "SOUNDS", FS_SM, HINT_COL);
            rcy += lh + 2f;

            BuildToggleRow(contentRoot, rx, rcy, halfW, tbw, bh, "Hover", KEY_SFX_HOVER,
                v => AudioService.SetHoverEnabled(v));
            rcy += bh + 1f;

            BuildToggleRow(contentRoot, rx, rcy, halfW, tbw, bh, "Click", KEY_SFX_CLICK,
                v => AudioService.SetClickEnabled(v));
            rcy += bh + 1f;

            BuildToggleRow(contentRoot, rx, rcy, halfW, tbw, bh, "Tab", KEY_SFX_TAB,
                v => AudioService.SetTabEnabled(v));
        }

        private void BuildToggleRow(RectTransform parent, float x, float y, float w,
            float tbw, float bh, string labelText, string key, Action<bool> onChange)
        {
            bool on = LoadBool(key, true);

            MakeLabel(parent, new Rect(x, y, w - tbw - PAD, bh), labelText, FS_SM, WHITE);

            var btn = UGUIShip.CreateButton(parent,
                new Rect(x + w - tbw, y, tbw, bh),
                on ? "ON" : "OFF",
                on ? SEL_COL : BTN_DARK,
                WHITE, FS_SM,
                new Action(() =>
                {
                    bool cur = LoadBool(key, true);
                    bool next = !cur;
                    SettingsService.Set(key, next ? "true" : "false");
                    onChange(next);
                }));

            var capturedBtn = btn;
            var capturedKey = key;
            capturedBtn.onClick.AddListener(new Action(() =>
            {
                bool cur = LoadBool(capturedKey, true);
                UGUIShip.SetButtonSelected(capturedBtn, cur, SEL_COL);
                var lbl = capturedBtn.GetComponentInChildren<Text>();
                if (lbl != null) lbl.text = cur ? "ON" : "OFF";
            }));
        }

        private static float LoadFloat(string key, float def)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return float.TryParse(SettingsService.Get(key, def.ToString(ci)),
                System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;
        }

        private static bool LoadBool(string key, bool def) =>
            SettingsService.Get(key, def ? "true" : "false") != "false";
    }
}