using System;
using System.Collections.Generic;
using System.Globalization;
using BetterFG.Services;
using UnityEngine;

namespace BetterFG.Tweaks
{
    // shadow draw distance. higher = farther shadows render, more GPU cost. game default is ~150.
    // we default to 60 (much cheaper) and only apply when the tweak is on.
    public class ShadowDistanceTweak : BfgTweak
    {
        public ShadowDistanceTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "shadowDistance";
        public override string TweakLabel => "Shadow distance";

        private const string KEY = "tweak.shadowDistance.value";
        private const float DEFAULT_VAL = 60f;
        private static float? _stockValue;

        private static float Current
        {
            get
            {
                var ci = CultureInfo.InvariantCulture;
                return float.TryParse(SettingsService.Get(KEY, DEFAULT_VAL.ToString(ci)),
                    NumberStyles.Float, ci, out float v) ? v : DEFAULT_VAL;
            }
            set => SettingsService.Set(KEY, value.ToString(CultureInfo.InvariantCulture));
        }

        public override void EnableTweak()
        {
            if (_stockValue == null) _stockValue = QualitySettings.shadowDistance;
            QualitySettings.shadowDistance = Mathf.Max(0f, Current);
        }

        public override void DisableTweak()
        {
            if (_stockValue.HasValue) QualitySettings.shadowDistance = _stockValue.Value;
        }

        public override List<TweakInputField> GetInputFields() => new List<TweakInputField>
        {
            new TweakInputField
            {
                Get = () => Current.ToString("0.##", CultureInfo.InvariantCulture),
                Set = v =>
                {
                    if (!float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) return;
                    Current = Mathf.Max(0f, f);
                    if (IsEnabled) QualitySettings.shadowDistance = Current;
                },
            }
        };
    }

    // bypass for QualitySettings.shadowResolution cap (max VeryHigh): set the main directional
    // light's shadowCustomResolution to an arbitrary pixel size. auto-reapplied each round via
    // CleanupLoadingScreens since the directional light is part of the round scene.
    public class ShadowCustomResolutionTweak : BfgTweak
    {
        public ShadowCustomResolutionTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "shadowCustomResolution";
        public override string TweakLabel => "Shadow custom px";

        private const string KEY = "tweak.shadowCustomResolution.value";
        private const int DEFAULT_VAL = 4096;

        private static int Current
        {
            get => int.TryParse(SettingsService.Get(KEY, DEFAULT_VAL.ToString()), out int v) ? Mathf.Max(0, v) : DEFAULT_VAL;
            set => SettingsService.Set(KEY, Mathf.Max(0, value).ToString());
        }

        public override void EnableTweak() => ApplyToMainLight(Current);
        public override void DisableTweak() => ApplyToMainLight(-1); // -1 = "use QualitySettings"

        // called from GameStatePatches.CleanupLoadingScreens to ride round transitions.
        public static void ApplyIfEnabled()
        {
            var inst = FindAnyInstance();
            if (inst == null || !inst.IsEnabled) return;
            ApplyToMainLight(Current);
        }

        private static void ApplyToMainLight(int px)
        {
            foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>(true))
            {
                if (l == null || l.type != LightType.Directional) continue;
                try { l.shadowCustomResolution = px; } catch { }
            }
        }

        private static ShadowCustomResolutionTweak FindAnyInstance()
        {
            foreach (var t in TweakRegistry.All)
                if (t is ShadowCustomResolutionTweak s) return s;
            return null;
        }

        public override List<TweakInputField> GetInputFields() => new List<TweakInputField>
        {
            new TweakInputField
            {
                Get = () => Current.ToString(),
                Set = v =>
                {
                    if (!int.TryParse(v, out int i)) return;
                    Current = Mathf.Max(0, i);
                    if (IsEnabled) ApplyToMainLight(Current);
                },
                Width = 52f,
            }
        };
    }

    // cascade split overrides for 4-cascade directional shadows. each cascade slice has a fixed
    // pixel budget; the smaller its world coverage, the denser pixels-per-meter it has. shrink
    // cascade 0 + 1 down to a tight near-camera band and the close-up shadow gets razor sharp.
    // values are world units (meters). cascade 3 always extends to shadowDistance.
    public class ShadowCascadeSplitTweak : BfgTweak
    {
        public ShadowCascadeSplitTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "shadowCascadeSplit";
        public override string TweakLabel => "Cascade distances";

        private const string KEY_NEAR = "tweak.shadowCascadeSplit.near";
        private const string KEY_MID = "tweak.shadowCascadeSplit.mid";
        private const float DEF_NEAR = 10f;
        private const float DEF_MID = 25f;
        private static Vector3? _stockSplit;

        private static float Near
        {
            get { var ci = CultureInfo.InvariantCulture; return float.TryParse(SettingsService.Get(KEY_NEAR, DEF_NEAR.ToString(ci)), NumberStyles.Float, ci, out float v) ? Mathf.Max(0.1f, v) : DEF_NEAR; }
            set => SettingsService.Set(KEY_NEAR, Mathf.Max(0.1f, value).ToString(CultureInfo.InvariantCulture));
        }
        private static float Mid
        {
            get { var ci = CultureInfo.InvariantCulture; return float.TryParse(SettingsService.Get(KEY_MID, DEF_MID.ToString(ci)), NumberStyles.Float, ci, out float v) ? Mathf.Max(0.2f, v) : DEF_MID; }
            set => SettingsService.Set(KEY_MID, Mathf.Max(0.2f, value).ToString(CultureInfo.InvariantCulture));
        }

        public override void EnableTweak()
        {
            if (_stockSplit == null) _stockSplit = QualitySettings.shadowCascade4Split;
            Apply();
        }

        public override void DisableTweak()
        {
            if (_stockSplit.HasValue) QualitySettings.shadowCascade4Split = _stockSplit.Value;
        }

        private static void Apply()
        {
            float dist = Mathf.Max(0.1f, QualitySettings.shadowDistance);
            float near = Mathf.Clamp(Near / dist, 0.001f, 0.97f);
            float mid = Mathf.Clamp(Mathf.Max(Mid / dist, near + 0.001f), 0.002f, 0.98f);
            // cascade 2 → 3 split: leave whatever the game had, but clamp to be after mid
            float prevC23 = _stockSplit?.z ?? 0.5f;
            float c23 = Mathf.Clamp(Mathf.Max(prevC23, mid + 0.01f), mid + 0.01f, 0.99f);
            QualitySettings.shadowCascade4Split = new Vector3(near, mid, c23);
        }

        public override List<TweakInputField> GetInputFields() => new List<TweakInputField>
        {
            new TweakInputField
            {
                Get = () => Near.ToString("0.##", CultureInfo.InvariantCulture),
                Set = v => { if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) { Near = f; if (IsEnabledStatic()) Apply(); } },
                Width = 38f,
                Placeholder = "c0",
            },
            new TweakInputField
            {
                Get = () => Mid.ToString("0.##", CultureInfo.InvariantCulture),
                Set = v => { if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) { Mid = f; if (IsEnabledStatic()) Apply(); } },
                Width = 38f,
                Placeholder = "c1",
            },
        };

        private static bool IsEnabledStatic()
        {
            foreach (var t in TweakRegistry.All)
                if (t is ShadowCascadeSplitTweak s) return s.IsEnabled;
            return false;
        }
    }

}
