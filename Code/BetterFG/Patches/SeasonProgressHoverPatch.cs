using System.Collections.Generic;
using BetterFG.Customization.Menu;
using FGClient;
using FGClient.Tweening;
using HarmonyLib;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BetterFG.Patches
{
    // The season progress banner's Panel images are driven by UITween — our static recolour
    // gets overwritten every tween frame. Postfix UITween.ColorUpdate so whenever the tween
    // writes a colour onto an Image under a SeasonProgressViewModel, we hand it to
    // MenuCustomizationApplication.RecolourSeasonProgressImage, which owns the actual recolour
    // (and the blue/pink toggle gating). The patch is just a thin call site + hover tracking.
    internal static class SeasonProgressHoverPatch
    {
        // hovered state per banner (instance id of the SeasonProgressViewModel).
        private static readonly Dictionary<int, bool> _hoverState = new Dictionary<int, bool>();

        [HarmonyPatch(typeof(UITweenTriggerComponent), nameof(UITweenTriggerComponent.OnPointerEnter))]
        internal static class Patch_OnPointerEnter
        {
            [HarmonyPostfix]
            public static void Postfix(UITweenTriggerComponent __instance, PointerEventData eventData)
                => SetHover(__instance, true);
        }

        [HarmonyPatch(typeof(UITweenTriggerComponent), nameof(UITweenTriggerComponent.OnPointerExit))]
        internal static class Patch_OnPointerExit
        {
            [HarmonyPostfix]
            public static void Postfix(UITweenTriggerComponent __instance, PointerEventData eventData)
                => SetHover(__instance, false);
        }

        private static void SetHover(UITweenTriggerComponent trigger, bool hovered)
        {
            if (trigger == null) return;
            var banner = trigger.GetComponentInParent<SeasonProgressViewModel>();
            if (banner == null) return;
            _hoverState[banner.GetInstanceID()] = hovered;
        }

        [HarmonyPatch(typeof(UITween), nameof(UITween.ColorUpdate))]
        internal static class Patch_ColorUpdate
        {
            [HarmonyPostfix]
            public static void Postfix(UITweenLayerProxy proxy)
            {
                if (proxy == null) return;
                var comp = proxy.LayerComponent;
                if (comp == null) return;
                var img = comp.TryCast<Image>();
                if (img == null) return;

                var banner = img.GetComponentInParent<SeasonProgressViewModel>();
                if (banner == null) return;

                _hoverState.TryGetValue(banner.GetInstanceID(), out bool hovered);
                MenuCustomizationApplication.Instance?.RecolourSeasonProgressImage(img, hovered);
            }
        }
    }
}
