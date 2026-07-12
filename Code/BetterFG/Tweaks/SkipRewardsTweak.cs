using System;
using System.Collections.Generic;
using BetterFG.Services;
using FGClient;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Tweaks
{
    // quick skip rewards screen. the skip button on the reward screen calls FastForward()
    // which only speeds the sequence up — it never actually ends the screen. so when the user
    // presses skip (FastForward), we run one of the reward screen's real exit paths instead:
    //   - Requeue mode (default): PlayAgain(), the same thing the "Play Again" button does, so
    //     you get dropped straight into another match without holding the button.
    //   - Menu mode: Continue(), which blows through the screen back to the main menu.
    // PlayAgain only makes sense where the game itself offers it (matchmade playlists that support
    // requeue). in a private lobby, a show, or a playlist that can't requeue, CanEnablePlayAgainButton
    // is false and we fall back to Continue() so skip still does *something*. that fallback is why
    // the reporter sometimes landed back in the lobby.
    public class SkipRewardsTweak : BfgTweak
    {
        public SkipRewardsTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "skip_rewards";
        public override string TweakLabel => "Quick Skip Rewards Screen";
        public override bool DefaultEnabled => false;

        public static SkipRewardsTweak Instance { get; private set; }
        void Awake() => Instance = this;

        private string RequeueKey => $"tweak.{TweakId}.requeue";
        // default true — this is the fix for "doesn't requeue automatically".
        internal bool RequeueMode => SettingsService.Get(RequeueKey, "true") == "true";

        // the prepared patch hands us the reward screen vm so Update can keep the button on
        internal static RewardScreenViewModel ActiveVm;

        private float _nextPoke;

        private void Update()
        {
            if (!IsEnabled || ActiveVm == null) return;
            if (Time.unscaledTime < _nextPoke) return;
            _nextPoke = Time.unscaledTime + 0.3f;

            try
            {
                // keep the skip button available so the user can press it
                ActiveVm.CanEnableSkipButton = true;
                ActiveVm.UpdateNavPrompts();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("skip-rewards: couldn't keep the skip button on, dropping the vm — " + ex.Message);
                ActiveVm = null;
            }
        }

        public override List<TweakSetting> GetSettings() => new List<TweakSetting>
        {
            new TweakSetting
            {
                Label = "Do what after skip?",
                Options = new[] { "Requeue", "Enter menu" },
                Selected = () => RequeueMode ? 0 : 1,
                OnPick = i => SettingsService.Set(RequeueKey, i == 0 ? "true" : "false")
            }
        };
    }

    // the skip button fires FastForward(). intercept it and run a real exit path instead.
    [HarmonyPatch(typeof(RewardScreenViewModel), nameof(RewardScreenViewModel.FastForward))]
    internal static class SkipRewardsFastForwardPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(RewardScreenViewModel __instance)
        {
            var tweak = SkipRewardsTweak.Instance;
            if (tweak == null || !tweak.IsEnabled || __instance == null) return true; // run normal FastForward

            try
            {
                // don't gate on CanEnablePlayAgainButton — that flag only says whether the button UI is
                // enabled yet (it's false mid-animation, which is exactly when we skip). PlayAgain() itself
                // just fires OnContinueAction(playAgain: true), so call it regardless in requeue mode.
                if (tweak.RequeueMode) __instance.PlayAgain();
                else __instance.Continue();
            }
            catch (Exception ex) { Plugin.Log?.LogWarning("skip-rewards: exit path threw, letting the screen sit — " + ex.Message); }
            return false; // skip the original FastForward
        }
    }

    // grab the reward screen vm when it gets set up, so Update can keep the button enabled.
    [HarmonyPatch(typeof(RewardScreenViewModel), nameof(RewardScreenViewModel.OnRewardScreenPrepared))]
    internal static class SkipRewardsPreparedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RewardScreenViewModel __instance)
        {
            if (__instance == null) return;
            SkipRewardsTweak.ActiveVm = __instance;
        }
    }

    // clear it when the screen tears down.
    [HarmonyPatch(typeof(RewardScreenViewModel), nameof(RewardScreenViewModel.OnDestroy))]
    internal static class SkipRewardsDestroyPatch
    {
        [HarmonyPostfix]
        public static void Postfix(RewardScreenViewModel __instance)
        {
            if (SkipRewardsTweak.ActiveVm == __instance)
                SkipRewardsTweak.ActiveVm = null;
        }
    }
}
