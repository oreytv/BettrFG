using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Customization.Menu;
using BetterFG.Services;
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
                return;
            }

            var browser = __instance.TryCast<Wushu.LevelEditor.Runtime.UI.LevelBrowser.LevelBrowserScreenViewModel>();
            if (browser != null && MenuCustomizationApplication.Instance != null &&
                SettingsService.Get(MenuCustomizationApplication.KEY_CREATIVE_ENABLED, "false") == "true")
            {
                MenuCustomizationApplication.Instance.StartCoroutine(ApplyCreativeNextFrame(browser.transform).WrapToIl2Cpp());
                return;
            }

            var inst = MenuCustomizationApplication.Instance;
            if (inst == null) return;

            // the radial's 7 menu-item canvases instantiate as it opens, after DoFadeIn, so a
            // synchronous sweep misses them — delay it like the rulebook.
            var radial = __instance.TryCast<LevelEditor_RadialMenuViewModel>();
            if (radial != null)
            {
                inst.StartCoroutine(ApplyCreativeNextFrame(radial.transform, true).WrapToIl2Cpp());
                return;
            }

            // parameter menu builds its rows after fade-in (synchronous sweep here only lands on the
            // second open, once they persist), so delay the sweep a frame like the rulebook.
            if (__instance.TryCast<LevelEditorParameterMenuViewModel>() != null)
            {
                CreativeUIPatches.FixParametersMenu(__instance.transform);
                inst.StartCoroutine(ApplyCreativeNextFrame(__instance.transform, true, null, false).WrapToIl2Cpp());
                return;
            }

            // options + rulebook: recolour the whole screen immediately (no frame delay), leaving the creative
            // background canvas to ApplyCreativeBg (its own slot colours). options keeps its Description block
            // untouched; rulebook takes no shader.
            if (__instance.TryCast<LevelEditorOptionsViewModel>() != null)
            {
                ApplyCreativeNow(__instance.transform, "Description", false);
                return;
            }
            if (__instance.TryCast<LevelEditorRulebookViewModel>() != null)
            {
                ApplyCreativeNow(__instance.transform, null, true);
                return;
            }

            // exit popup: just the foreground colours, no shader.
            if (__instance.TryCast<LevelEditorExitPopupViewModel>() != null)
            {
                inst.ReapplyForegroundFromSettings(__instance.transform, null, true);
                return;
            }

            // editor nav bar: recolour the whole Safe Area (Play + Library prompts, no shader) when the nav shows.
            if (__instance.TryCast<LevelEditorNavigationScreenViewModel>() != null)
            {
                var safe = __instance.transform.Find("Safe Area");
                if (safe != null)
                {
                    inst.ReapplyForegroundFromSettings(safe, null, true);
                    CreativeUIPatches.FixLibraryPrompt(safe);
                }
                return;
            }

            // main + welcome editor screens: kill any creative-bg canvas outright, then just the foreground (no shader).
            if (__instance.TryCast<LevelEditorScreenViewModel>() != null ||
                __instance.TryCast<LevelEditorWelcomeScreenViewModel>() != null)
            {
                inst.StartCoroutine(EditorScreenForeground(__instance.transform).WrapToIl2Cpp());
                return;
            }

            // carousel items build as it opens, after DoFadeIn — generic sweep plus the by-name tile recolour.
            if (__instance.TryCast<LevelEditorCarrouselViewModel>() != null)
                LevelEditorCarrouselFolderPatch.ApplyCarrousel(__instance.transform);
        }

        // the browser rebuilds its bg canvas as it fades in, so wait a frame before recolouring.
        // sweepForeground is set for the options/rulebook screens: also recolour everything outside
        // the bg canvas once the screen's content has settled. extraExclude names another subtree to
        // leave alone (options' Description).
        internal static IEnumerator ApplyCreativeNextFrame(UnityEngine.Transform vmRoot, bool sweepForeground = false, string extraExclude = null, bool shader = true)
        {
            yield return null;
            if (vmRoot == null) yield break;
            UnityEngine.Transform canvas = null;
            foreach (var t in vmRoot.GetComponentsInChildren<UnityEngine.Transform>(true))
                if (t != null && t.name == "Generic_UI_CreativeBackground_Prefab_Canvas") { canvas = t; break; }
            var inst = MenuCustomizationApplication.Instance;
            if (inst == null) yield break;
            if (SettingsService.Get(MenuCustomizationApplication.KEY_CREATIVE_ENABLED, "false") == "true")
                inst.ApplyCreativeBg(canvas);
            if (!sweepForeground) yield break;

            // rulebook/parameter rows can take a couple more frames to populate, so give them time
            // before sweeping or the fg recolour lands on an empty list.
            yield return null;
            yield return null;
            if (vmRoot == null) yield break;
            // SettingsBackingSprite is a baked cyan texture behind a white tint — flat colour can't
            // touch it, so leave it out of the sweep and hand it the GPU shader instead.
            string exclude = "Generic_UI_CreativeBackground_Prefab_Canvas|SettingsBackingSprite" + (extraExclude != null ? "|" + extraExclude : "");
            inst.ReapplyForegroundFromSettings(vmRoot, exclude, true);
            if (shader) inst.ApplyEditorShader(vmRoot);
        }

        // same as above but synchronous — for screens whose content is already built at DoFadeIn (options,
        // rulebook) so they recolour on the same frame instead of flashing the game's colours first.
        internal static void ApplyCreativeNow(UnityEngine.Transform vmRoot, string extraExclude = null, bool shader = true)
        {
            if (vmRoot == null) return;
            var inst = MenuCustomizationApplication.Instance;
            if (inst == null) return;
            UnityEngine.Transform canvas = null;
            foreach (var t in vmRoot.GetComponentsInChildren<UnityEngine.Transform>(true))
                if (t != null && t.name == "Generic_UI_CreativeBackground_Prefab_Canvas") { canvas = t; break; }
            if (SettingsService.Get(MenuCustomizationApplication.KEY_CREATIVE_ENABLED, "false") == "true")
                inst.ApplyCreativeBg(canvas);
            string exclude = "Generic_UI_CreativeBackground_Prefab_Canvas|SettingsBackingSprite" + (extraExclude != null ? "|" + extraExclude : "");
            inst.ReapplyForegroundFromSettings(vmRoot, exclude, true);
            if (shader) inst.ApplyEditorShader(vmRoot);
        }

        // main editor screen: its content isn't built at DoFadeIn, so wait a frame. kill any creative-bg
        // canvas, then sweep foreground (no shader).
        static IEnumerator EditorScreenForeground(UnityEngine.Transform vmRoot)
        {
            yield return null;
            var inst = MenuCustomizationApplication.Instance;
            if (vmRoot == null || inst == null) yield break;
            foreach (var t in vmRoot.GetComponentsInChildren<UnityEngine.Transform>(true))
                if (t != null && t.name == "Generic_UI_CreativeBackground_Prefab_Canvas") t.gameObject.SetActive(false);
            inst.ReapplyForegroundFromSettings(vmRoot, null, true);
        }
    }

    // ObjectInfo derives from a plain ViewModel (not ScreenViewModel), so DoFadeIn never fires for it.
    // SetData runs each time the panel populates for the selected/highlighted object — sweep the whole
    // UI_ObjectInfo_Main then (GetComponentsInChildren(true) covers the disabled state objects too) so
    // it tracks live colour changes.
    [HarmonyPatch(typeof(LevelEditorObjectInfoViewModel), "SetData")]
    internal static class LevelEditorObjectInfoSetDataPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorObjectInfoViewModel __instance)
        {
            if (__instance == null || MenuCustomizationApplication.Instance == null) return;
            MenuCustomizationApplication.Instance.ReapplyForegroundFromSettings(__instance.transform, null, true);
        }
    }

    // UpdateButtonState repaints an item's fill to the game's selected/deselected colour on every
    // cursor move, wiping our tint off the whole radial. re-sweep the radial foreground after it.
    [HarmonyPatch(typeof(LevelEditor_RadialMenuItemViewModel), "UpdateButtonState")]
    internal static class RadialItemUpdateButtonStatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditor_RadialMenuItemViewModel __instance)
        {
            var inst = MenuCustomizationApplication.Instance;
            if (__instance == null || inst == null) return;
            if (SettingsService.Get(MenuCustomizationApplication.KEY_CREATIVE_ENABLED, "false") != "true") return;
            var content = __instance.transform.parent;
            inst.ReapplyForegroundFromSettings(content != null ? content : __instance.transform, null, true, true);
        }
    }

    // the carousel row and (once a type is opened) the "Folder Items" subtree both build their tiles a few
    // frames after their trigger. their Background_Fill/Background_Selected want the blue/cyan replacements
    // by name; the generic hue sweep handles everything else and skips those two.
    [HarmonyPatch(typeof(LevelEditorCarrouselViewModel), "OnFolderEvent")]
    internal static class LevelEditorCarrouselFolderPatch
    {
        // folder open: only the Folder Items subtree is new, so scope the work to it. re-sweeping the whole
        // carrousel here is what hitched.
        [HarmonyPostfix]
        public static void Postfix(LevelEditorCarrouselViewModel __instance)
        {
            if (__instance == null) return;
            Sweep(__instance.transform.Find("Folder Items"));
        }

        // carousel open: the whole carrousel needs the generic sweep once, plus the by-name tiles.
        internal static void ApplyCarrousel(Transform carrousel)
        {
            var inst = MenuCustomizationApplication.Instance;
            if (carrousel == null || inst == null) return;
            inst.StartCoroutine(ScreenViewModelDoFadeInPatch.ApplyCreativeNextFrame(carrousel, true, null, false).WrapToIl2Cpp());
            Sweep(carrousel.Find("Carousel_Items"));
        }

        static void Sweep(Transform tiles)
        {
            var inst = MenuCustomizationApplication.Instance;
            if (tiles == null || inst == null) return;
            inst.StartCoroutine(RecolourTiles(tiles).WrapToIl2Cpp());
        }

        static IEnumerator RecolourTiles(Transform tiles)
        {
            yield return null; yield return null; yield return null;
            var inst = MenuCustomizationApplication.Instance;
            if (tiles == null || inst == null) yield break;
            inst.ApplyFolderTileColours(tiles);
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

    // sits on the level-editor menu backdrop (the Creative prefab on the world-space CameraRig), which
    // is disabled at menu entry until the editor view shows. repaints on every enable.
    public class CreativeEditorBgApplier : MonoBehaviour
    {
        public CreativeEditorBgApplier(IntPtr ptr) : base(ptr) { }
        void OnEnable() => MenuCustomizationApplication.Instance?.RefreshCreativeCanvas(transform);
    }
}
