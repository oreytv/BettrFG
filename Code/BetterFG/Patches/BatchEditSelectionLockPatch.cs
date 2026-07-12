using BetterFG.UI.Windows.Creative;
using HarmonyLib;
using LevelEditor;

namespace BetterFG.Patches
{
    // While the Batch Edit window is open, block the editor from confirming/placing the multi-selection
    // on a left-click over the object you're looking at. Diagnostic probing (we can't read the IL2CPP
    // bodies) showed the click fires PlaceMultiSelection exactly once — that's the confirm mutation; the
    // other selection methods only tick per-frame. Prefix-skip it while the window's up. Editor-only,
    // dead the moment the window closes. BettrFG never calls PlaceMultiSelection itself.
    //
    // We also stash the live handler so the window can fire one PlaceMultiSelection a frame after it
    // closes — commits the selection cleanly instead of leaving you to work off the click backlog.
    [HarmonyPatch(typeof(LevelEditorMultiSelectionHandler), "PlaceMultiSelection")]
    public class BatchEditBlockPlacePatch
    {
        public static LevelEditorMultiSelectionHandler LiveHandler { get; private set; }

        [HarmonyPrefix]
        public static bool Prefix(LevelEditorMultiSelectionHandler __instance)
        {
            LiveHandler = __instance;
            return !BatchEditWindow.AnyOpen;
        }
    }
}
