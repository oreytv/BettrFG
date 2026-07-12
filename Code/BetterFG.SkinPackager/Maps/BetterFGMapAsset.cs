using UnityEngine;

namespace BetterFG.Editor
{
    // Environment (skybox / ambient / reflection / fog) is NOT stored here — the packager reads it
    // straight from the scene's RenderSettings (Window > Rendering > Lighting) at pack time.
    [CreateAssetMenu(menuName = "BettrFG/Map Asset", fileName = "NewMapAsset")]
    public class BetterFGMapAsset : ScriptableObject
    {
        public string displayName;

        [Tooltip("Shown on the round-selected loading screen when someone plays your level. Replaces the raw share code in the description.")]
        [TextArea(2, 4)]
        public string description;

        public string prefabName;

        [Tooltip("Keep all existing placeable objects (don't disable them) except the background CutoutSphere. Applies to normal rounds too.")]
        public bool keepExistingObjects;

        [Header("Music")]
        [Tooltip("Path to an mp3 file. Plays during gameplay only. Leave empty for default FG music.")]
        public string musicFilePath;
    }
}
