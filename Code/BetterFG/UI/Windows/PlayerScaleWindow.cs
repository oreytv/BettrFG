using System;
using BetterFG.Services;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Windows
{
    public class PlayerScaleWindow : BetterFGWindow
    {
        public PlayerScaleWindow(IntPtr ptr) : base(ptr) { }

        protected override float WindowWidth => 340f;
        protected override float WindowHeight => 118f;
        protected override string WindowTitle => "Player Scale";
        protected override string BgResourceName => "BetterFG.assets.ui.windows.generalbg.png";

        private static readonly Color HINT_COL = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color BTN_APPLY = new Color(0.45f, 0.35f, 0.25f, 1f);

        private Slider _slider;

        protected override void BuildContent(RectTransform contentRoot)
        {
            BgPosition = new Vector3(172.8f, 29.5f, 0);
            BgScale = new Vector3(1.2f, 4.7f, 1f);
            ContentPosition = new Vector3(175.9f, -103.5f, 0f);
            ContentScale = new Vector3(1f, 1f, 1f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(24.8f, -19.3f, 0);
            TitleScale = new Vector3(1.1f, 1.3f, 1f);

            float w = WindowWidth - PAD * 2f;
            float lh = 14f;
            float bh = 22f;
            float gap = 2f;
            float cy = -(TITLE_H + gap);  // start below title bar, going downward

            float savedScale = PlayerScaleService.GetPlayerScale();

            cy -= lh + gap;

            float btnW = 50f;
            float resetW = 42f;
            float spacing = 2f;
            float sliderW = w - btnW - resetW - spacing * 2f;

            _slider = UGUIShip.CreateSlider(
                contentRoot,
                PAD, cy, sliderW,
                "", savedScale, bh, 0f, FS_SM,
                new Action<float>(v =>
                {
                }),
                HINT_COL, new Color(0.7f, 0.7f, 0.7f, 1f)
            );
            _slider.minValue = 0.5f;
            _slider.maxValue = 1.5f;
            _slider.value = savedScale;

            float bx = PAD + sliderW + spacing;
            UGUIShip.CreateButton(contentRoot,
                new Rect(bx, cy, resetW, bh),
                "Reset", new Color(0.22f, 0.22f, 0.22f, 1f), WHITE, FS_SM,
                new Action(OnReset));

            UGUIShip.CreateButton(contentRoot,
                new Rect(bx + resetW + spacing, cy, btnW, bh),
                "Apply", BTN_APPLY, WHITE, FS_SM,
                new Action(OnApply));

            cy -= bh + gap;

            var visualLabel = MakeLabel(contentRoot,
                new Rect(PAD, cy, w, lh),
                "Only affects your visual.. hitboxes stay the same.",
                FS_SM,
                new Color(1f, 1f, 1f, 0.4f));
            visualLabel.transform.localPosition = new Vector3(-160.4f, 68.1997f, 0f);

            cy -= lh;

            MakeLabel(contentRoot,
                new Rect(PAD, cy, w, lh * 2f),
                "In a round this only works when you're solo, In main menu you'll scale anytime.",
                FS_SM,
                new Color(1f, 0.75f, 0.4f, 0.85f));
        }

        private void OnReset()
        {
            if (_slider == null) return;
            _slider.value = 1f;
            OnApply();
        }

        private void OnApply()
        {
            if (_slider == null) return;
            float scale = Mathf.Clamp(_slider.value, 0.5f, 1.5f);
            PlayerScaleService.SavePlayerScale(scale);

            var local = BetterFG.Network.RemoteProfileStore.LocalLoadout();
            if (local != null)
                foreach (var entry in local.skins)
                {
                    if (string.IsNullOrEmpty(entry.file)) continue;
                    if (BetterFG.Customization.Player.SkinTypeParser.FromString(entry.type) != BetterFG.Customization.Player.SkinType.Costume) continue;
                    SettingsService.SetSkinScale(entry.file, scale);
                }

            PlayerScaleService.ApplyToAll(scale, PlayerScaleService.ScaleReason.Manual);
        }
    }
}