using System;
using FG.Common;
using FGClient;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Tweaks
{
    // skips the victory/celebration scene the moment it shows up. when the state machine
    // swaps to StateVictoryScreen we force the VM's skip gate open and keep poking
    // ShowSkipPrompt every 0.3s instead of waiting for the prompt delay/input.
    public class SkipVictoryTweak : BfgTweak
    {
        public SkipVictoryTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "skip_victory";
        public override string TweakLabel => "Quick Skip Victory Scene";
        public override bool DefaultEnabled => false;

        public static SkipVictoryTweak Instance { get; private set; }
        void Awake() => Instance = this;

        // the patch hands us the victory state when it swaps in; cleared when it dies
        internal static StateVictoryScreen ActiveState;

        private float _nextPoke;

        private void Update()
        {
            if (!IsEnabled || ActiveState == null) return;
            if (Time.unscaledTime < _nextPoke) return;
            _nextPoke = Time.unscaledTime + 0.3f;

            try
            {
                var vm = ActiveState._victoryScreenViewModel;
                if (vm == null) return;
                vm._canSkip = true;
                vm.ShowSkipPrompt();
                vm.UpdateNavPrompts();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("[SkipVictoryTweak] skip failed " + ex.Message);
                ActiveState = null;
            }
        }
    }

}
