using System;

namespace BetterFG.Customization.Social
{
    [Serializable]
    public class EmoteEntry
    {
        public string id;
        public string bundlePath;     // path to an AssetBundle on disk holding the AnimationClip
        public string clipName;       // AnimationClip name inside the bundle (blank = first clip found)
        public int slot;
        public bool enabled;
        public string imagePath;      // custom wheel preview sprite
        public string soundPath;

        public EmoteEntry()
        {
            id = Guid.NewGuid().ToString("N").Substring(0, 8);
            bundlePath = "";
            clipName = "";
            slot = 7;
            enabled = true;
            imagePath = "";
            soundPath = "";
        }
    }
}
