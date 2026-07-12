using System;

namespace BetterFG.Customization.Social
{
    [Serializable]
    public class EmoticonEntry
    {
        public string id;
        public string itemId;    // CMSDataGroup.Id e.g. "emoticon_wheel_happy_heart"
        public int slot;
        public bool enabled;
        public string imagePath;
        public string[] soundPaths;   // up to 3 sounds, one fires at random when played

        public EmoticonEntry()
        {
            id = Guid.NewGuid().ToString("N").Substring(0, 8);
            itemId = "";
            slot = 7;
            enabled = true;
            imagePath = "";
            soundPaths = new string[3] { "", "", "" };
        }
    }
}
