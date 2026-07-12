using System;
using HarmonyLib;

namespace BetterFG.Features.CreativeIncrements
{
    // general creative editor tweaks, no settings — just makes it feel better. the carousels (object
    // browser etc) have a sluggish first-repeat delay when you hold a direction; drop it so navigation
    // feels snappier. _inputHandlers[0] on the view model is the LevelEditorCarrouselInputController,
    // and _navigationHoldFirstInputCooldown lives on its MenuInputHandler base.
    [HarmonyPatch(typeof(LevelEditorCarrouselViewModel), "OnOpened")]
    internal static class CreativeCarrouselSnappyPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorCarrouselViewModel __instance)
        {
            try
            {
                var handlers = __instance._inputHandlers;
                if (handlers == null || handlers.Count == 0) return;

                var ctrl = handlers[0]?.TryCast<MenuInputHandler>();
                if (ctrl != null) ctrl._navigationHoldFirstInputCooldown = 0.2f;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("[CreativePatches] carrousel snappy failed " + ex.Message);
            }
        }
    }
}
