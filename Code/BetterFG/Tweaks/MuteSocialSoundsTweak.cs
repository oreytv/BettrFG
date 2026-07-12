using System;
using Character;
using FG.Common.Character;
using FGClient;
using HarmonyLib;
using MPG.Utility;

namespace BetterFG.Tweaks
{
    public class MuteSocialSoundsTweak : BfgTweak
    {
        public MuteSocialSoundsTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "mute_social_sounds";
        public override string TweakLabel => "Disable Ranked Emoticons";
        public override bool DefaultEnabled => false;

        // flipped by the toggle. the harmony patch below only acts when this is on,
        // and only fires when a speech option actually plays - no background loop.
        internal static bool Active;

        void Awake() => Active = IsEnabled;

        public override void EnableTweak() => Active = true;
        public override void DisableTweak() => Active = false;

        // the loud annoying rank emoticons/phrases all have these in their fmod event path,
        // e.g. event:/FallGuys/SFX/SFX_Phrase/SFX_Phrase_S18_RankedS1_Ace and ..._S11_4_Star
        internal static bool IsRankAudio(string audioEvent)
        {
            if (string.IsNullOrEmpty(audioEvent)) return false;
            var p = audioEvent.ToLowerInvariant();
            return p.Contains("rank") || p.Contains("ace") || p.Contains("star");
        }
    }

}
