using System;
using FG.Common;
using FGClient;
using UnityEngine;

namespace BetterFG.Tweaks
{
    // anti-afk kicker only matters in the menu. patching AFKManager.IsEnabledInMenus didn't actually
    // stop it, but disabling the AFKManager's gameobject does — that's just dangerous to do in-game.
    // so we kill the object when MainMenuManager.OnMainMenuEntered fires, and switch it back on the
    // moment we change to any state that isn't the main menu (driven off the ReplaceCurrentState patch
    // in GameStatePatches). no polling, stays in sync.
    public class DisableAntiAfkTweak : BfgTweak
    {
        public DisableAntiAfkTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "disable_anti_afk";
        public override string TweakLabel => "Disable Anti-AFK in Main Menu";
        public override bool DefaultEnabled => true;

        public override void EnableTweak() => ApplyForCurrentState();
        public override void DisableTweak() => SetAfkObjectActive(true);

        // in the menu, kill the kicker. only raised on enabled tweaks so no gate needed
        public override void OnMainMenuEntered() => SetAfkObjectActive(false);

        // re-enable the kicker any time we move to a state that isn't the main menu
        public override void OnStateChanged(GameStateMachine.IGameState newState)
        {
            bool inMenu = newState != null && newState.TryCast<StateMainMenu>() != null;
            if (!inMenu) SetAfkObjectActive(true);
        }

        // tweak just got toggled on — line up with wherever we are right now.
        private static void ApplyForCurrentState()
        {
            bool inMenu = false;
            try
            {
                inMenu = GlobalGameStateClient.Instance?._gameStateMachine?.CurrentState?.TryCast<StateMainMenu>() != null;
            }
            catch { }
            SetAfkObjectActive(!inMenu);
        }

        private static void SetAfkObjectActive(bool active)
        {
            // FindObjectsOfTypeAll so we still get it when it's already inactive (GameObject.Find won't).
            foreach (var afk in Resources.FindObjectsOfTypeAll<AFKManager>())
                if (afk != null && afk.gameObject != null && afk.gameObject.activeSelf != active)
                    afk.gameObject.SetActive(active);
        }
    }
}
