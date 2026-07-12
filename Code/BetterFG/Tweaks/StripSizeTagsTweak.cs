using System.Text.RegularExpressions;
using FGClient.FallFeed;
using HarmonyLib;

namespace BetterFG.Tweaks
{
    public class StripSizeTagsTweak : BfgTweak
    {
        public StripSizeTagsTweak(System.IntPtr ptr) : base(ptr) { }

        public override string TweakId => "strip_size_tags";
        public override string TweakLabel => "Strip <size> Tags (anti-grief)";
        public override bool DefaultEnabled => true;

        public static StripSizeTagsTweak Instance { get; private set; }
        void Awake() => Instance = this;

        internal static readonly Regex StripRegex =
            new Regex(@"<size[^>]*>|</size>|<size[^>]*>|</size>", RegexOptions.IgnoreCase);

        internal static string Strip(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return Regex.Replace(text, @"<\s*size[^>]*>|<\s*/\s*size\s*>", "",
                RegexOptions.IgnoreCase);
        }
    }
}
