using System;
using BetterFG.Features.TimePlacement;
using FGClient.FallFeed;

namespace BetterFG.Tweaks
{
    // stamps qualification fallfeeds (the ones with the fallfeed-race sprite) with the time the
    // player qualified at — the server's qualifyTime, captured by FeatureTimePlacement, so the stamp
    // matches the in-game leaderboard's time column exactly. driven from FallFeedNamePatch.Postfix so
    // we don't add another patch, but all the behaviour lives here.
    public class FallFeedQualTimeTweak : BfgTweak
    {
        public FallFeedQualTimeTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "fallfeed_qual_time";
        public override string TweakLabel => "Fall Feed Qualification Time";
        public override bool DefaultEnabled => false;

        public static FallFeedQualTimeTweak Instance { get; private set; }
        void Awake() => Instance = this;

        const string RaceSprite = "fallfeed-race";
        const string TimeColor = "#FFFF00"; // pure yellow

        public void Apply(FallFeedNotificationViewModel vm)
        {
            if (!IsEnabled || vm == null) return;
            try
            {
                var txt = vm._text;
                string cur = txt?.text;
                if (string.IsNullOrEmpty(cur)) return;
                if (!cur.Contains(RaceSprite)) return;   // only qualification feeds
                if (cur.Contains(TimeColor)) return;     // already stamped

                // server qualifyTime for this fallfeed's player, captured by FeatureTimePlacement. no
                // stored time = don't stamp (don't invent one off the live clock).
                if (!TryGetQualTime(vm, out string qualTime)) return;

                string stamp = $" <color={TimeColor}>{qualTime}</color>";
                int idx = cur.IndexOf("<sprite name=\"" + RaceSprite, StringComparison.OrdinalIgnoreCase);
                string next = idx >= 0 ? cur.Insert(idx, stamp + " ") : cur + stamp;

                txt.text = next;
                vm.SetTextLayout(next);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("FallFeed: qualtime " + ex.Message);
            }
        }

        // the server qualifyTime FeatureTimePlacement captured for THIS fallfeed's player, formatted
        // mm:ss:ms to match the leaderboard's time column. false if we have no stored time for them.
        static bool TryGetQualTime(FallFeedNotificationViewModel vm, out string formatted)
        {
            formatted = null;
            var keys = vm?._messageData?.PlayerKeys;
            if (keys == null || keys.Length == 0) return false;
            if (!FeatureTimePlacement.TryGetQualTime(keys[0], out float seconds)) return false;

            TimeSpan t = TimeSpan.FromSeconds(seconds);
            formatted = string.Format("{0:D2}:{1:D2}:{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
            return true;
        }
    }
}
