using System;
using FGClient;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class StartupTitleScreenTweak : BfgTweak
    {
        public StartupTitleScreenTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "startup_title_screen";
        public override string TweakLabel => "Startup Title Screen";
        public override bool DefaultEnabled => false;
        public override string TweakTooltip => "Credits to Floyzi for the code.";

        public static StartupTitleScreenTweak Instance { get; private set; }
        void Awake() => Instance = this;
    }

    // the title screen is gated by background login, not SkipTitleScreen — if background login can
    // start it bypasses the title screen on boot. force CanStartBackgroundLogin to return false while
    // the tweak is on and the title screen comes back.
    [HarmonyPatch(typeof(CatapultBackgroundLogin), nameof(CatapultBackgroundLogin.CanStartBackgroundLogin))]
    internal static class StartupTitleScreenPatch
    {
        [HarmonyPrefix]
        public static bool CanStartBackgroundLogin(ref bool __result)
        {
            var tweak = StartupTitleScreenTweak.Instance;
            if (tweak == null || !tweak.IsEnabled) return true;

            __result = false;
            return false;
        }
    }

    // the MainMenuBuilder GameObject lights up the menu UI behind the title screen. hide it while
    // the title screen is up, then turn it back on when ExitTitleScreen runs.
    [HarmonyPatch(typeof(TitleScreenViewModel), nameof(TitleScreenViewModel.OnEnable))]
    internal static class StartupTitleScreenHideMenuPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            var tweak = StartupTitleScreenTweak.Instance;
            if (tweak == null || !tweak.IsEnabled) return;

            var go = GameObject.Find("UICanvas_Client_V2(Clone)/Default/Topbar_Prime(Clone)");
            if (go != null) go.SetActive(false);
        }
    }

    [HarmonyPatch(typeof(TitleScreenViewModel), "LeaveTitleScreen")]
    internal static class StartupTitleScreenShowMenuPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            var go = GameObject.Find("UICanvas_Client_V2(Clone)/Default/Topbar_Prime(Clone)");
            if (go != null) go.SetActive(true);
        }
    }
}
