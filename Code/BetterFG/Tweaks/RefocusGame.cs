using FG.Common.UI;
using FGClient;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class RefocusGame : BfgTweak
    {
        public RefocusGame(System.IntPtr ptr) : base(ptr) { }

        public override string TweakId => "refocus_game";
        public override string TweakLabel => "Refocus game (press C)";
        public override bool DefaultEnabled => true;


        void Update()
        {
            if (!IsEnabled) return;
            if (!Input.GetKeyDown(KeyCode.C)) return;

            var mmGo = GameObject.Find("MainMenuManager");
            if (mmGo == null) return;
            var mm = mmGo.GetComponent<MainMenuManager>();
            if (mm == null) return;
            var focusHandler = mm._mainMenuBuilder?._focusHandler;
            if (focusHandler == null) return;
            focusHandler.GainFocusOnSwitchableViewModel(focusHandler._focusedSwitchableViewIndex);
        }
    }
}