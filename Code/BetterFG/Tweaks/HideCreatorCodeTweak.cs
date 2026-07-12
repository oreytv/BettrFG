using FGClient;
using FGClient.Fraggle;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class HideCreatorCodeTweak : BfgTweak
    {
        public HideCreatorCodeTweak(System.IntPtr ptr) : base(ptr) { }

        public override string TweakId => "hide_creator_code";
        public override string TweakLabel => "Hide Creator Code";
        public override bool DefaultEnabled => false;

        public static HideCreatorCodeTweak Instance { get; private set; }

        void Awake() => Instance = this;

        public override void EnableTweak() => SetCreatorCodeVisible(false);
        public override void DisableTweak() => SetCreatorCodeVisible(true);

        internal static void SetCreatorCodeVisible(bool visible)
        {
            foreach (var vm in Object.FindObjectsOfType<CreatorIDViewModel>(true))
            {
                if (vm == null) continue;
                vm.gameObject.SetActive(visible);
            }
        }
    }

    // reapply on every round countdown so it survives scene reloads
    [HarmonyPatch(typeof(ClientGameManager), nameof(ClientGameManager.CountdownEnds))]
    public class CreatorCodeCountdownPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            var inst = HideCreatorCodeTweak.Instance;
            if (inst == null || !inst.IsEnabled) return;
            HideCreatorCodeTweak.SetCreatorCodeVisible(false);
        }
    }
}