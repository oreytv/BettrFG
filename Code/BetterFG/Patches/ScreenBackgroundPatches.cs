using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Menu;
using FGClient;
using FGClient.UI.Core;
using FGClient.UI.RoundRevealCarousel;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Patches
{
    // applies per-screen gradient + pattern to the loading screens when they open.
    //
    // type tree: LoadingGameScreenViewModel <- LoadingUGCGameScreenViewModel <- LoadingUPGameScreenViewModel
    // we dispatch by the instance's actual runtime type so the right screen settings are used and we
    // don't double-apply when a derived VM doesn't override OnOpened (the base patch fires for it too).
    internal static class LoadingScreenBg
    {
        // the loading screen currently faded in (or most recent). lets the Screen tab's Apply update
        // it live if it happens to be showing.
        public static LoadingGameScreenViewModel Active;

        public static ScreenBackgroundService.Screen ScreenFor(LoadingGameScreenViewModel vm)
        {
            // check most-derived first: UP inherits UGC, so a UP instance also casts to UGC.
            //   UP  -> Explore
            //   UGC -> always the FinalRound screen
            //   base -> IsFinalRound ? FinalRound : LoadingLevel
            if (vm.TryCast<LoadingUPGameScreenViewModel>() != null)
                return ScreenBackgroundService.Screen.Explore;
            if (vm.TryCast<LoadingUGCGameScreenViewModel>() != null)
                return ScreenBackgroundService.Screen.FinalRound;
            return vm.IsFinalRound
                ? ScreenBackgroundService.Screen.FinalRound
                : ScreenBackgroundService.Screen.LoadingLevel;
        }

        public static void Dispatch(LoadingGameScreenViewModel vm)
        {
            if (vm == null || MenuCustomizationApplication.Instance == null) return;
            MenuCustomizationApplication.Instance.StartCoroutine(ApplyLoop(vm, ScreenFor(vm)).WrapToIl2Cpp());
        }

        // called by the Screen tab Apply — if a loading screen is live, repaint it now.
        public static void ReapplyActive()
        {
            if (Active == null) return;
            try { ScreenBackgroundService.ApplyUnder(ScreenFor(Active), Active.transform); }
            catch { Active = null; } // screen was destroyed
        }

        private static IEnumerator ApplyLoop(LoadingGameScreenViewModel vm, ScreenBackgroundService.Screen screen)
        {
            yield return null; // let the screen build its background first
            if (vm == null) yield break;
            ScreenBackgroundService.ApplyUnder(screen, vm.transform);
            ApplyForegroundToTopLeftGroup(vm);
        }

        // scope the menu foreground colour swap to the loading screen's TopLeft_Group only — the
        // round name / map preview area sits there and never gets touched by the main canvas sweep
        // (loading screens spawn outside the menu hierarchy).
        private static void ApplyForegroundToTopLeftGroup(LoadingGameScreenViewModel vm)
        {
            if (vm == null) return;
            Transform group = null;
            foreach (var t in vm.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == "TopLeft_Group") { group = t; break; }
            if (group == null) return;
            try { MenuCustomizationApplication.Instance?.ReapplyForegroundFromSettings(group); }
            catch (Exception ex) { Plugin.Log.LogWarning("ScreenBg: fg apply to TopLeft_Group: " + ex.Message); }
        }
    }

    // DoFadeIn lives on ScreenViewModel (every screen runs it), so we patch it and filter to loading
    // screens by casting __instance. fires for all three loading types and runs each time the screen
    // fades in, so it catches the live screen reliably. Dispatch picks the screen by runtime type.
    [HarmonyPatch(typeof(ScreenViewModel), "DoFadeIn")]
    internal static class ScreenViewModelDoFadeInPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ScreenViewModel __instance)
        {
            if (__instance == null) return;
            var loading = __instance.TryCast<LoadingGameScreenViewModel>();
            if (loading != null)
            {
                Plugin.Log.LogInfo($"ScreenBg: DoFadeIn caught loading screen {loading.GetType().Name}, screen={LoadingScreenBg.ScreenFor(loading)}");
                LoadingScreenBg.Active = loading;
                LoadingScreenBg.Dispatch(loading);
                return;
            }

            var reveal = __instance.TryCast<RoundRevealCarouselViewModel>();
            if (reveal != null)
            {
                Plugin.Log.LogInfo("ScreenBg: DoFadeIn caught RoundRevealCarouselViewModel");
                RoundRevealBg.Dispatch(reveal);
                BetterFG.Tweaks.LeaveOnLoadingScreenTweak.OnExternalLeaveTriggerEnd();
            }
        }
    }

    // round-reveal carousel uses the same SeasonS11 backdrop+circles layout as the title screen,
    // mounted at (instance)/Generic_UI_CurrentSeasonBackground_Canvas/.../Mask. paint it as the
    // FallForce screen so it follows the same custom colours.
    internal static class RoundRevealBg
    {
        public static void Dispatch(RoundRevealCarouselViewModel vm)
        {
            if (vm == null || MenuCustomizationApplication.Instance == null) return;
            MenuCustomizationApplication.Instance.StartCoroutine(ApplyLoop(vm).WrapToIl2Cpp());
        }

        private static IEnumerator ApplyLoop(RoundRevealCarouselViewModel vm)
        {
            yield return null; // wait a frame for the bg subtree to instantiate
            if (vm == null) yield break;
            Transform mask = FindMask(vm.transform);
            if (mask != null)
                ScreenBackgroundService.Apply(ScreenBackgroundService.Screen.FallForce, mask);
        }

        private static Transform FindMask(Transform root)
        {
            // direct path first; fall back to a subtree search in case the wrapper names change.
            var t = root.Find("Generic_UI_CurrentSeasonBackground_Canvas/Generic_UI_SeasonS11Background_Canvas_Variant/Mask");
            if (t != null) return t;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child != null && child.name == "Mask" && child.parent != null &&
                    child.parent.name.StartsWith("Generic_UI_SeasonS11Background"))
                    return child;
            return null;
        }
    }

    // the S10 show-selector screen has its own SeasonS10ShowSelectorBackground/Mask under the
    // Prime_UI_SymphonyShowSelector canvas — same Backdrop+Circles layout as every other screen.
    // no dedicated patch: Menu_Screen_Main's SwitchableView flipping to view 1 is what brings the
    // selector up, and GameStatePatches' SetViewImplementation hook already sees that and calls in.
    internal static class ShowSelectorBg
    {
        public static Transform FindMask(Transform root)
        {
            var t = root.Find("Generic_UI_SeasonS10ShowSelectorBackground_Canvas_Variant/Mask");
            if (t != null) return t;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child != null && child.name == "Mask" && child.parent != null &&
                    child.parent.name.StartsWith("Generic_UI_SeasonS10ShowSelectorBackground"))
                    return child;
            return null;
        }

        // the selector canvas is inactive at main-menu-entry, so GameObject.Find can't see it — walk
        // the transform path from the root (Find traverses inactive children).
        static Transform FindSelectorCanvas()
        {
            var root = GameObject.Find("UICanvas_Client_V2(Clone)");
            return root == null ? null : root.transform.Find(
                "Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Main/Prime_UI_SymphonyShowSelector_Prefab_Canvas(Clone)");
        }

        static Transform FindLiveMask()
        {
            var canvas = FindSelectorCanvas();
            return canvas == null ? null : FindMask(canvas);
        }

        // drop the OnEnable applier on the (currently disabled) S10 background mask at menu entry, so
        // it paints itself the first time the selector opens — and every open after.
        public static void AttachApplier()
        {
            var mask = FindLiveMask();
            if (mask == null) return;
            if (mask.GetComponent<ShowSelectorBgApplier>() == null)
                mask.gameObject.AddComponent<ShowSelectorBgApplier>();
        }

        static void Paint(Transform mask)
        {
            if (mask == null) return;
            if (ScreenBackgroundService.Enabled(ScreenBackgroundService.Screen.ShowSelector))
                ScreenBackgroundService.ApplyToContainer(ScreenBackgroundService.Screen.ShowSelector, mask);
            else
                ScreenBackgroundService.RevertContainer(mask);
        }

        // called by the Screen tab Apply — repaint (or revert) the selector bg if it's up right now.
        public static void ReapplyLive() => Paint(FindLiveMask());

        // the applier calls this from its own OnEnable
        public static void PaintSelf(Transform mask) => Paint(mask);
    }

    // sits on the S10 background Mask, repaints on every enable so the show-selector bg survives the
    // game rebuilding it on first open.
    public class ShowSelectorBgApplier : MonoBehaviour
    {
        public ShowSelectorBgApplier(IntPtr ptr) : base(ptr) { }
        void OnEnable() => ShowSelectorBg.PaintSelf(transform);
    }
}
