using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Services
{
    // runtime scale for the whole BettrFG UI. every one of our canvases uses ScaleWithScreenSize
    // at a 1920x1080 reference. shrinking that reference makes the canvas content render bigger, so
    // we just divide the reference by the scale and re-apply it to all our scalers. the baked-in
    // UIScale consts stay untouched — this rides on top of them.
    public static class UIScaleService
    {
        private const string KEY = "ui.scale";
        private const string KEY_ENABLED = "ui.scale.enabled";
        private static readonly Vector2 BaseRef = new Vector2(1920f, 1080f);

        // master switch — on by default at a 1.2 scale.
        public static bool Enabled
        {
            get => SettingsService.Get(KEY_ENABLED, "true") == "true";
            set => SettingsService.Set(KEY_ENABLED, value ? "true" : "false");
        }

        // flip the switch + apply right away. turning it off restores the stock 1.0 scale.
        public static void SetEnabled(bool on)
        {
            Enabled = on;
            Apply(on ? Current : 1f);
        }

        // our canvas GameObjects all end in "_Canvas" or are named below. cheap to just match names.
        private static readonly string[] CanvasNames =
        {
            "BetterFG_Canvas", "SideWheelCanvas", "BetterFGNotif_Canvas", "BetterFGUpdate_Canvas"
        };

        public static float Current
        {
            get
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                return float.TryParse(SettingsService.Get(KEY, "1.2"),
                    System.Globalization.NumberStyles.Float, ci, out float v) ? v : 1.2f;
            }
        }

        // the reference resolution our canvases should use right now (plain BaseRef when off). new
        // canvases set this at creation so they spawn already scaled instead of staying at 1x until
        // the next Apply — that's why opening a fresh window used to ignore the scale.
        public static Vector2 CurrentRef => Enabled && Current >= 0.1f ? BaseRef / Current : BaseRef;

        // apply without persisting — used by the live preview before the user confirms
        public static void Apply(float scale)
        {
            if (scale < 0.1f) scale = 1f;
            var newRef = BaseRef / scale;

            foreach (var sc in Resources.FindObjectsOfTypeAll<CanvasScaler>())
            {
                if (sc == null) continue;
                if (sc.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) continue;

                string n = sc.gameObject.name;
                bool ours = n.EndsWith("_Canvas") || System.Array.IndexOf(CanvasNames, n) >= 0;
                if (!ours) continue;

                sc.referenceResolution = newRef;
            }
        }

        public static void Save(float scale)
        {
            SetValue(scale);
            Apply(scale);
        }

        // persist the scale WITHOUT re-scaling any canvas. used while dragging the slider so the UI
        // doesn't rescale under the cursor every tick (that's what made it jitter) — the real apply
        // happens once on release.
        public static void SetValue(float scale)
            => SettingsService.Set(KEY, scale.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // call after the canvases exist (startup + every UI view switch) so our scale survives the
        // game rebuilding/relaying its canvases (e.g. a resolution change wipes the reference res).
        // no-op when the feature is off so we never fight the stock scale.
        public static void ApplySaved()
        {
            if (!Enabled) return;
            Apply(Current);
        }
    }
}
