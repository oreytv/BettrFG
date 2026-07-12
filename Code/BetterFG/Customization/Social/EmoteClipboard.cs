using UnityEngine;

namespace BetterFG.Customization.Social
{
    // holds the emote the user pressed "Copy" on in the Customization tab, so the Emotes section
    // of the Emoticons & Phrases tab can paste it (download bundle/sound/cover, fill an EmoteEntry).
    public static class EmoteClipboard
    {
        public static bool HasEmote;
        public static string Name;
        public static string BundleUrl;    // full raw url to the bundle file
        public static string SoundUrl;     // full raw url to the mp3/wav, blank if none
        public static string CoverUrl;     // full raw url to cover.jpg/png, blank if none
        public static string AudioFileName; // original audio filename so we keep the extension
        public static Texture2D Cover;     // already-loaded cover, for the live paste-button preview

        // bumped every time something new is copied, so the paste UI can notice and rebuild
        public static int Version;

        public static void Set(string name, string bundleUrl, string soundUrl, string coverUrl, string audioFileName, Texture2D cover)
        {
            HasEmote = true;
            Name = name;
            BundleUrl = bundleUrl;
            SoundUrl = soundUrl;
            CoverUrl = coverUrl;
            AudioFileName = audioFileName;
            Cover = cover;
            Version++;
        }
    }
}
