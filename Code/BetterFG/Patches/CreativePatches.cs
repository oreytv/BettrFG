using BetterFG.UI.Windows.Creative;
using FG.Common;
using HarmonyLib;
using LevelEditor;
using UnityEngine;

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

    // Multiselect's rigidbody wakes the collider, so the destroy volume eats objects parked under the border
    [HarmonyPatch(typeof(LevelEditorDestroyVolume), "OnTriggerEnter")]
    public class CreativeDestroyVolumePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Collider other)
        {
            var handler = LevelEditorManager.Instance?.GetMultiselectHandler();
            if (handler == null) return true;
            if (!LevelEditorPlaceableObject.ColliderToPlaceable.TryGetValue(other, out var lepo)) return true;
            return !handler.ContainsInSelection(lepo, true);
        }
    }

    // Placement floor is MapPlacementBounds.min.y (-75, not 0); below it KeepObjectInMapBounds zeroes the move
    [HarmonyPatch(typeof(LevelEditorManager), "SetupMapBoundsAndVisuals")]
    public class CreativePlacementFloorPatch
    {
        private const float FloorDrop = 10000f;

        [HarmonyPostfix]
        public static void Postfix(LevelEditorManager __instance)
        {
            var b = __instance.MapPlacementBounds;
            var min = b.min;
            var max = b.max;
            min.y -= FloorDrop;
            __instance.MapPlacementBounds = new Bounds((min + max) * 0.5f, max - min);
        }
    }
}
