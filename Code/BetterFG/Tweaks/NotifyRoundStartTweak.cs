using System;
using BetterFG.Utilities;
using FGClient;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class NotifyRoundStartTweak : BfgTweak
    {
        public NotifyRoundStartTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "notify_round_start";
        public override string TweakLabel => "Notify round start";
        public override bool DefaultEnabled => false;
        public override string TweakTooltip => "Only works for Windows. Notification will appear when a round starts while you're tabbed out of the game, click it to jump back.";

        public override void OnRoundStart()
        {
            if (Application.isFocused) return;
            GlobalGameStateClient.Instance.GameStateView.GetLiveClientGameManager(out ClientGameManager cgm);
            Shell32Util.Toast("Round is starting", $"{cgm._round.DisplayNameUnindented} -- {GlobalGameStateClient.Instance.GameStateView.InitialRoundPlayerCount} players");
        }
    }
}
