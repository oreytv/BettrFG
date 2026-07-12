using System;

namespace BetterFG.Customization.Social
{
    [Serializable]
    public class PhraseEntry
    {
        public string id;
        public string phraseId;
        public string phraseText;
        public int slot;
        public bool enabled;
        public string imagePath;
        public string[] soundPaths;   // up to 3 sounds, one fires at random when played

        public PhraseEntry()
        {
            id = Guid.NewGuid().ToString("N").Substring(0, 8);
            phraseId = "";
            phraseText = "";
            slot = 7;
            enabled = true;
            imagePath = "";
            soundPaths = new string[3] { "", "", "" };
        }
    }
}
