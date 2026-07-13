using System;
using FGClient.FallFeed;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class MaxFallFeedTweak : BfgTweak
    {
        public MaxFallFeedTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "max_fallfeed";
        public override string TweakLabel => "Max Fall Feed Notifications";
        public override bool DefaultEnabled => false;
        public override string TweakTooltip => "Raise the game's 5-notification limit. Extra ones still fade out on their own timer.";

        public static MaxFallFeedTweak Instance { get; private set; }
        void Awake() => Instance = this;

        private const string KEY = "tweak.max_fallfeed.value";
        private int _max = -1;

        public int Max
        {
            get
            {
                if (_max < 0)
                    _max = int.TryParse(Services.SettingsService.Get(KEY, "10"), out int v) ? Mathf.Clamp(v, 1, 30) : 10;
                return _max;
            }
            private set
            {
                _max = Mathf.Clamp(value, 1, 30);
                Services.SettingsService.Set(KEY, _max.ToString());
            }
        }

        public override System.Collections.Generic.List<TweakIncrement> GetIncrements() => new System.Collections.Generic.List<TweakIncrement>
        {
            new TweakIncrement
            {
                Label = "Max notifications", Min = 1, Max = 30, Wrap = true,
                Get = () => Max,
                Set = v => { Max = v; },
            }
        };

    }

    [HarmonyPatch(typeof(FallFeedContainer), nameof(FallFeedContainer.ClearNotification))]
    internal static class FallFeedClearCapPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(FallFeedContainer __instance, FallFeedNotificationViewModel notification)
        {
            var t = MaxFallFeedTweak.Instance;
            if (t == null || !t.IsEnabled || __instance == null || notification == null || notification._disposed)
                return true;

            if (Time.time - notification._enableTime >= __instance.NotificationDuration)
                return true;

            var parent = __instance._notificationContainer;
            if (parent == null) return true;

            int live = 0;
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i)?.GetComponent<FallFeedNotificationViewModel>();
                if (child != null && !child._disposed) live++;
            }

            if (live > t.Max) return true;

            return false;
        }
    }
}
