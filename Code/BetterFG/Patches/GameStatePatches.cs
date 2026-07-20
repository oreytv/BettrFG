using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BetterFG.Core;
using BetterFG.Nametag;
using BetterFG.Services;
using BetterFG.Customization.Player;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FG.Common;
using FGClient;
using FallGuysLib.Camera;
using HarmonyLib;
using UnityEngine;
using PlayerUtils = FallGuysLib.Players.PlayerUtils;
using BetterFG.UI.Tab;
using BetterFG.Network;
using BetterFG.Customization.Social;
using BetterFG.Customization.Menu;
using FG.Common.CMS;
using FG.Common.UI;
using FGClient.ShowSelector;
using FGClient.VictoryScreen;
using FGClient.Challenges;
using FGClient.UI;
using FGClient.UI.PrivateLobby;
using BetterFG.Features.UnityRound;
using BetterFG.Features.Stars;
using BetterFG.Features.QualificationTime;
using BetterFG.Features.AllCosmetics;
using BetterFG.Features.MorePlatformIcon;
using BetterFG.Features.TimePlacement;
using System.Runtime.InteropServices;
using BetterFG.UI;
using FallGuysLib.UI;
using static FG.Common.PartyService;

namespace BetterFG.Patches.GameStates
{
    // main menu + lobby

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OnMainMenuEntered), new[] { typeof(bool), typeof(bool) })]
    public class MainMenuBean
    {
        [HarmonyPostfix]
        public static void OnMainMenuEntered(MainMenuManager __instance)
        {
            if (__instance._lobbyVirtualCam != null)
            {
                MenuCustomizationApplication.Instance?.CacheCamBase(__instance._lobbyVirtualCam);
                // re-apply now that we have the real base — any earlier apply used the fallback base
                MenuCustomizationApplication.AutoApplyCamFromSettings();
            }

            MenuCustomizationApplication.Instance?.CacheBgImageBase();

            // enabled tweaks that react to entering the menu (anti-afk kill, lively fall guys reapply)
            BetterFG.Tweaks.BfgTweak.RaiseMainMenuEntered();

            BetterFG.UI.Tab.NametagTab.CacheNameAssets();
            BetterFG.UI.BetterFGUIMan.ResolveAsapFont();

            MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine().WrapToIl2Cpp());

            FontReplacementService.ReapplyFromSettings();
            // the game assigns fame/famepass nametag materials a few frames after the menu enters,
            // i.e. after the sweep above already swapped that text's font -> gold shader renders on the
            // wrong atlas = corruption. heal it once the materials have landed (reverts any touched text
            // that turned famepass). this is the no-toggle fix for startup/menu-open corruption.
            MenuCustomizationApplication.Instance.StartCoroutine(HealFontAfterMenuEntered().WrapToIl2Cpp());

            FeatureStars.CreateInMenu();
            FeatureQualificationTime.CreateInMenu();
            //__instance.StartCoroutine(FeatureAllCosmetics.ApplyAgainAfterMenuSettles().WrapToIl2Cpp());

            if (__instance._menuFallGuy != null)
                BeanMonitorService.PushBean(__instance._menuFallGuy);
            if (__instance._lobbyFallGuy != null)
                BeanMonitorService.PushBean(__instance._lobbyFallGuy);
            SkinApplicationService.Instance?.RestoreSavedGameCosmetics();

            if (BeanMonitorService.Instance != null)
            {
                if (__instance._menuFallGuy != null)
                    BeanMonitorService.Instance.StartCoroutine(CostumePollerKick.Kick(__instance._menuFallGuy).WrapToIl2Cpp());
                if (__instance._lobbyFallGuy != null)
                    BeanMonitorService.Instance.StartCoroutine(CostumePollerKick.Kick(__instance._lobbyFallGuy).WrapToIl2Cpp());
            }

            BetterFG.Patches.ShowSelectorBg.AttachApplier();

            // the level-editor menu backdrop is disabled at entry, and GameObject.Find can't see it while
            // hidden — transform.Find walks inactive. drop the OnEnable applier so it paints on the first
            // switch to the editor view and every one after.
            var creativeEditorBg = GameObject.Find("CameraRig")?.transform.Find("VirtualCameras/MainMenu_LevelEditor/Generic_UI_CreativeBackground_Prefab_Canvas");
            if (creativeEditorBg != null && creativeEditorBg.GetComponent<CreativeEditorBgApplier>() == null)
                creativeEditorBg.gameObject.AddComponent<CreativeEditorBgApplier>();

            MenuCustomizationApplication.Instance?.ReapplyToMainMenu();
            MenuCustomizationApplication.Instance?.SpawnMenuBg();

            if (MenuCustomizationApplication.Instance != null)
            {
                MenuCustomizationApplication.Instance._pendingFgReapply = true;
                MenuCustomizationApplication._fullCanvasReapplyPending = true;
            }

            // resolve the menu bean's scale whether or not a costume is on — PlayerScaleService decides
            // the right value (your global slider, or a costume's saved/baked scale). the old count==0
            // gate meant equipping a costume left the menu bean stuck at 1.
            if (__instance._menuFallGuy != null)
                PlayerScaleService.ApplyToBean(__instance._menuFallGuy, PlayerScaleService.GetPlayerScale(), PlayerScaleService.BeanScaleMode.Local);

            SkinApplicationService.Instance.TryAutoReapplyCustomTextureForBean(__instance._menuFallGuy);
            if (BeanMonitorService.Instance != null)
                BeanMonitorService.Instance.StartCoroutine(DelayedCustomSkinTextureReapply().WrapToIl2Cpp());

            try { NetworkClient.Instance?.RegisterLocalProfile(); } catch { }

            try
            {
                if (UnityEngine.Random.value < 0.0001f)
                {
                    var holder = GameObject.Find("3D Environment/MainMenu_Environment/PlinthRig/CharacterAndPlinthHolder_Main/ENV_Plinth_MO/CharacterHolder");
                    if (holder != null)
                    {
                        holder.transform.localScale = new Vector3(1f, -1f, 1f);
                        holder.transform.localPosition = new Vector3(0f, 7.04f, 0f);
                    }
                }
            }
            catch { }

            var localName = GlobalGameStateClient.Instance?.GetLocalPlayerName();
            if (!string.IsNullOrEmpty(localName))
            {
                LocalPlayerInfo.FGlocalplayerusername = localName;
                Plugin.Log.LogInfo($"MainMenuBean: FG username set: {localName}");
            }

#if PROFILES
            // match lobby party holders to enabled profiles (custom skins/plinth/nametag)
            NetworkClient.PrimeProfilesForLobby(force: true);
            BetterFG.Customization.Profiles.LobbyProfileService.ApplyToLobby();
#endif

            // leaving the lobby drops the menu onto view index -1 (no focused child) and nothing
            // re-grabs focus, so the game ends up out of focus. shove focus back onto the parent
            // switchable view the same way the modal close path does. do it a couple frames later
            // so the menu rebuild (MainMenuBuilder(Clone)) has settled before we focus.
            if (__instance != null)
                __instance.StartCoroutine(RefocusParentAfterMenuEntered(__instance).WrapToIl2Cpp());

            if (__instance != null)
                __instance.StartCoroutine(HideMenuClutter().WrapToIl2Cpp());

            if (BetterFG.Services.MenuMusicService.Enabled
                && !string.IsNullOrEmpty(BetterFG.Services.MenuMusicService.CurrentPath))
                BetterFG.Services.MenuMusicService.Play();
        }

        private static IEnumerator HideMenuClutter()
        {
            yield return new WaitForSeconds(1f);
            // full-path Find walks inactive children (GameObject.Find skips them once hidden).
            // the two live under different roots.
            var canvas = GameObject.Find("UICanvas_Client_V2(Clone)")?.transform;
            canvas?.Find("Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Main/Prime_UI_MainMenu_Canvas(Clone)/SafeArea/BottomRight_Group")?.gameObject.SetActive(true);
            canvas?.Find("Default/Topbar_Prime(Clone)/SafeArea/TabsHorizontalLayout/SeasonPassButton")?.gameObject.SetActive(true);
        }

        private static IEnumerator RefocusParentAfterMenuEntered(MainMenuManager mmm)
        {
            yield return null;
            yield return null;

            var focusHandler = mmm?._mainMenuBuilder?._focusHandler;
            if (focusHandler != null)
            {
                int idx = focusHandler._focusedSwitchableViewIndex;
                if (idx < 0) idx = 0;
                focusHandler.GainFocusOnSwitchableViewModel(idx);
            }

            // also tell the builder's focus handler the parent regained focus, matching the
            // modal close path that's known to work.
            var builder = GameObject.Find("UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)");
            builder?.GetComponent<SwitchableViewFocusHandler>()?.OnParentViewGainedFocus();

            // first menu entry: our NameTagViewModel postfix ran before the menu finished settling,
            // and our material/outline swap got overwritten by a follow-up game update. switching
            // views fixes it because it kicks the VM's UpdateDisplay again — do the same kick here
            // so the styling lands fully without the user having to switch views.
            BetterFG.Nametag.NametagPatchHub.RefreshRemoteNametags();
        }

        private static IEnumerator DelayedCustomSkinTextureReapply()
        {
            yield return new WaitForSeconds(0.3f);
            CustomSkinTextureTab.ReapplyAllEnabledFromSettings();
            SkinApplicationService.Instance?.ReapplyExpectedGameCosmeticVisuals();
        }

        // fame/famepass nametag materials get assigned a few frames into the menu, after the initial
        // font sweep. heal a couple times so any text we wrongly swapped before its gold material landed
        // gets reverted to its original atlas — no more famepass corruption on startup/menu open.
        private static IEnumerator HealFontAfterMenuEntered()
        {
            yield return new WaitForSeconds(0.3f);
            FontReplacementService.HealAndReapply();
            yield return new WaitForSeconds(0.7f);
            FontReplacementService.HealAndReapply();
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.ShowLobbyScreen), new[] { typeof(OnDisplayLobby) })]
    public class LobbyBean
    {
        [HarmonyPostfix]
        public static void OnShowLobbyScreen(MainMenuManager __instance)
        {
            if (__instance._lobbyFallGuy == null) return;
            BeanMonitorService.PushBean(__instance._lobbyFallGuy);
            var bean = __instance._lobbyFallGuy;
            MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.ApplyLobbyBGForegroundNextFrame().WrapToIl2Cpp());
            SkinApplicationService.Instance?.ReapplyExpectedGameCosmeticVisuals(bean);

            // lobby's ForegroundCanvas + the nav overlay only exist once the lobby screen opens.
            // wait one frame so their Canvas components are fully bound before we touch them —
            // applying same-frame doesn't stick.
            MenuCustomizationApplication.Instance?.StartCoroutine(ApplyScalingNextFrame().WrapToIl2Cpp());

#if PROFILES
            // make sure profiles are loaded, then match lobby party holders to them
            NetworkClient.PrimeProfilesForLobby(force: true);
            BetterFG.Customization.Profiles.LobbyProfileService.ApplyToLobby();
#endif
        }

        private static IEnumerator ApplyScalingNextFrame()
        {
            yield return null;
            UITab.ApplyCanvasScalingFromSettings();
        }
    }

    // a party member's bean finished animating in — mesh is bound and the nametag text is populated,
    // so this is the clean moment to match it to a profile and apply (no polling/race). __instance is
    // the MainMenuFallGuy that just settled.
    [HarmonyPatch(typeof(MainMenuFallGuy), nameof(MainMenuFallGuy.OnAnimatedInComplete))]
    public class MainMenuFallGuyAnimatedIn
    {
        [HarmonyPostfix]
        public static void Postfix(MainMenuFallGuy __instance)
        {
#if PROFILES
            NetworkClient.PrimeProfilesForLobby(force: true);
            BetterFG.Customization.Profiles.LobbyProfileService.ApplyToFallGuy(__instance);
#endif
        }
    }

    // a party member left — clear our tracked lobby state so a removed holder doesn't keep a stale
    // custom plinth/skin. (param names must match the real method: member/state, not a/a2.)
    [HarmonyPatch(typeof(PartyNameTag), nameof(PartyNameTag.HandlePlayerJoinedOrLeft))]
    public class PartyNameTagJoinLeave
    {
        [HarmonyPostfix]
        public static void Postfix(IPartyMember member, PartyService.RemoteMemberLoginState state)
        {
#if PROFILES
            if (state == PartyService.RemoteMemberLoginState.Left)
                BetterFG.Customization.Profiles.LobbyProfileService.ClearLobby();
#endif
        }
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.NavigateToView), new[] { typeof(MainMenuViews) })]
    public class Patch_NavigateToView
    {
        [HarmonyPostfix]
        public static void Postfix(MainMenuManager __instance, MainMenuViews view)
        {
            if (view == MainMenuViews.Lobby)
            {
                MenuCustomizationApplication.AutoApplyCamFromSettings();
                MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine().WrapToIl2Cpp());
            }
            SkinApplicationService.Instance?.ReapplyExpectedGameCosmeticVisuals();
        }
    }

    // PauseMusic fires when the game pauses (alt-tab / settings overlay etc). pause ours too.
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.PauseMusic))]
    public class Patch_MainMenuMusic_PauseSync
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            if (!BetterFG.Services.MenuMusicService.Enabled) return;
            BetterFG.Services.MenuMusicService.Pause();
        }
    }

    // game uses StopMusic to come back from pause (no ResumeMusic call observed) AND to kick off
    // the menu loop. resume ours, then pause the FMOD instance directly (the wrapper PauseMusic
    // gets clobbered by the immediate PlayMenuMusic in the game's sequence).
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.StopMusic))]
    public class Patch_MainMenuMusic_StopSync
    {
        [HarmonyPostfix]
        public static void Postfix(MainMenuManager __instance)
        {
            if (!BetterFG.Services.MenuMusicService.Enabled) return;
            BetterFG.Services.MenuMusicService.Resume();
            if (__instance != null)
                __instance.StartCoroutine(SilenceAfterStop(__instance).WrapToIl2Cpp());
        }

        private static IEnumerator SilenceAfterStop(MainMenuManager mmm)
        {
            yield return null;
            for (int i = 0; i < 40; i++)
            {
                try
                {
                    var inst = mmm?._menuMusic;
                    if (inst != null) { inst.SetPaused(true); yield break; }
                }
                catch { }
                yield return new WaitForSeconds(0.15f);
            }
        }
    }

    // the PB popup's return-to-menu now lives on PBPopupDestroyWatcher (fires on the popup's
    // actual OnDestroy) instead of hooking the OK click here, which softlocked when another
    // popup tore ours down.

    [HarmonyPatch(typeof(ModalMessageBaseViewModel), nameof(ModalMessageBaseViewModel.SetModalBaseData))]
    public class Patch_ModalMessageBase_SetData
    {
        [HarmonyPostfix]
        public static void Postfix(ModalMessageBaseViewModel __instance)
        {
            MenuCustomizationApplication.Instance?.ReapplyModalForeground(__instance);
        }
    }

    [HarmonyPatch]
    public class Patch_ModalMessage_SetData
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            Type[] types =
            {
                typeof(ModalMessagePopupViewModel),
                typeof(ModalMessageOptInPopupViewModel),
                typeof(ModalMessageWithOptionSelectionPopupViewModel),
                typeof(ModalMessageWithDropdownFieldViewModel),
                typeof(ModalMessageImagePopupViewModel),
                typeof(ModalMessageTagPopupViewModel),
                typeof(ModalMessageWithDropdownFieldWithFilterViewModel),
                typeof(ModalMessageWithInputFieldViewModel),
                typeof(ModalMessageWithDlcImagePopupViewModel)
            };

            for (int i = 0; i < types.Length; i++)
            {
                var method = AccessTools.DeclaredMethod(types[i], "SetData", new[] { typeof(Il2CppSystem.Object) });
                if (method != null && !method.ContainsGenericParameters)
                    yield return method;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Component __instance)
        {
            MenuCustomizationApplication.Instance?.ReapplyModalForeground(__instance);
        }
    }

    [HarmonyPatch(typeof(ShowSelectorViewModel), "ShowMainMenu")]
    public class Patch_ShowSelectorShowMainMenu
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            MenuCustomizationApplication.AutoApplyCamFromSettings();

            var app = MenuCustomizationApplication.Instance;
            if (app != null)
            {
                bool wasPendingFgReapply = app._pendingFgReapply;
                app._pendingFgReapply = false;

                var partymenu = GameObject.Find("UICanvas_Client_V2(Clone)/PartyMenu/").transform;

                Transform scopeRoot = null;
                if (MenuCustomizationApplication._fullCanvasReapplyPending)
                {
                    MenuCustomizationApplication._fullCanvasReapplyPending = false;
                    var rootGo = GameObject.Find("UICanvas_Client_V2(Clone)");
                    if (rootGo != null) scopeRoot = rootGo.transform;
                }
                else
                {
                    if (!wasPendingFgReapply)
                    {
                        return;
                    }

                    var rootGo = GameObject.Find("UICanvas_Client_V2(Clone)/Default/");
                    if (rootGo != null) scopeRoot = rootGo.transform;
                }

                MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine(scopeRoot).WrapToIl2Cpp());
            }
        }
    }

    public class Patch_BindMeshToFallguy
    {
        [HarmonyPostfix]
        public static void ReapplyCustomTexture(FallguyCustomisationHandler __instance, SkinnedMeshRenderer mesh)
        {
            var svc = SkinApplicationService.Instance;
            if (svc == null || __instance == null) return;
            if (svc.IsBindingGameCosmetic(__instance.gameObject)) return;
            svc.PollAndReapplyCustomTextureForBean(__instance.gameObject);
        }
    }


    // Kicks off phase 1 (download + cache) as early as possible.
    // Only fires if the round description matches the repo pattern.
    [HarmonyPatch(typeof(RoundLoader), "LoadViaShareCodeAndVersion")]
    public class UGCLevelLoad
    {
        [HarmonyPostfix]
        public static void Postfix(Round round)
        {
            BetterFG.Features.QualificationTime.FeatureQualificationTime.OnLoadViaShareCodeAndVersion(round);

            var desc = round?.RoundDescription?.Text;
            if (string.IsNullOrEmpty(desc))
            {
                BetterFGUnityRounds.ResetRoundState(unloadBundle: true);
                BetterFGUnityRounds.RestoreEnvironment();
                return;
            }
            Plugin.Log.LogInfo($"UGCLevelLoad: round desc: {desc}");
            if (!BetterFGUnityRounds.TryHandleDescription(desc))
            {
                BetterFGUnityRounds.ResetRoundState(unloadBundle: true);
                BetterFGUnityRounds.RestoreEnvironment();
            }
        }
    }

    [HarmonyPatch(typeof(LevelEditorStatePlay), "Initialise")]
    public class LevelEditorStatePlayPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            var bean = BeanMonitorService.CheckLevelEditorBean();
            var fgcc = bean != null ? bean.GetComponent<FallGuysCharacterController>() : null;
            CameraUtils.DisableXRayRenderer();

            var host = BeanMonitorService.Instance;
            if (host != null && fgcc != null)
                host.StartCoroutine(HandleServerStartRoundPa.DelayedDisableRagdoll(fgcc, 1f).WrapToIl2Cpp());
        }
    }


    // Phase 2: scene is ready � instantiate the cached prefab into it.
    [HarmonyPatch(typeof(RoundLoader), nameof(RoundLoader.NotifyLoadingFinished))]
    public class NotifyLoadingFinishedPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            BetterFGUnityRounds.MarkSceneReadyAndInstantiateQueuedRound();
        }
    }

    // Phase 2: scene is ready � instantiate the cached prefab into it.
    [HarmonyPatch(typeof(RoundLoader), nameof(RoundLoader.BeginShowUGCLoadingGameScreenViewModel))]
    public class BeginShowUGCLoadingGameScreenViewModelPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            //MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.AutoApplyCamFromSettingsCoroutine().WrapToIl2Cpp());
            MenuCustomizationApplication.Instance.ReapplyForegroundFromSettings();
        }
    }

    // the loading screen rewrites its description text in InitTexts/SetData, clobbering our swapped
    // round description. re-apply ours right after either runs. (no-op when not loading our round.)
    [HarmonyPatch(typeof(LoadingUGCGameScreenViewModel), "InitTexts")]
    public class LoadingUGCInitTextsPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => BetterFGUnityRounds.ApplyDescriptionNow();
    }

    [HarmonyPatch(typeof(LoadingUGCGameScreenViewModel), "SetData")]
    public class LoadingUGCSetDataPatch
    {
        [HarmonyPostfix]
        public static void Postfix() => BetterFGUnityRounds.ApplyDescriptionNow();
    }

    // Phase 2: scene is ready � instantiate the cached prefab into it.
    [HarmonyPatch(typeof(InGameMenuViewModel), nameof(InGameMenuViewModel.ToggleOpen))]
    public class InGameMenuViewModelToggleOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix(bool isInGameMenuOpen, bool playSound)
        {
            if (!isInGameMenuOpen) return;
            //MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.AutoApplyCamFromSettingsCoroutine().WrapToIl2Cpp());
            MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine().WrapToIl2Cpp());
        }
    }

    // Phase 2: scene is ready � instantiate the cached prefab into it.
    [HarmonyPatch(typeof(UGCLevelLikeScreenViewModel), nameof(UGCLevelLikeScreenViewModel.Initialise))]
    public class UGCLevelLikeScreenViewModelToggleOpenPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            //MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.AutoApplyCamFromSettingsCoroutine().WrapToIl2Cpp());
            MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine().WrapToIl2Cpp());
        }
    }

    [HarmonyPatch(typeof(PrivateLobbyShowListEntryViewModel), "SetData")]
    internal static class PrivateLobbyShowListEntryViewModelSetDataPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PrivateLobbyShowListEntryViewModel __instance)
        {
            MenuCustomizationApplication.Instance?.ReapplyPrivateLobbyShowEntryForeground(__instance.transform);
        }
    }

    [HarmonyPatch(typeof(ShowSelectorShowTileViewModel), nameof(ShowSelectorShowTileViewModel.SetIndividualShowData))]
    internal static class ShowSelectorShowTileViewModelSetIndividualShowDataPatch
    {
        private const string PANEL_FILL_PATH = "ShowTileHolder/NestedPanel/MainPanel/Panel_Fill";

        [HarmonyPostfix]
        public static void Postfix(ShowSelectorShowTileViewModel __instance, ShowSelectorShow showSelectorShow)
        {
            if (__instance == null) return;
            MenuCustomizationApplication.Instance?.ReapplyShowTileFill(__instance.transform);
            BetterFG.Features.Stars.FeatureStars.OnSetIndividualShowData(__instance, showSelectorShow);
            BetterFG.Tweaks.ShowTilePlaysTweak.OnSetShowData(__instance, showSelectorShow);
            //BetterFG.Tweaks.MultiShowSelectTweak.OnTileData(__instance); // WIP, shelved
        }
    }

    [HarmonyPatch(typeof(ChallengesScreenViewModel), "SetData")]
    internal static class ChallengesScreenViewModelSetDataPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ChallengesScreenViewModel __instance, ChallengesScreenData challengesScreenData)
        {
            if (__instance == null) return;
            MenuCustomizationApplication.Instance?.ReapplyForegroundFromSettings(__instance.transform);
        }
    }

    [HarmonyPatch(typeof(ChallengesScreenViewModel), "OnEnable")]
    internal static class ChallengesScreenViewModelOnEnablePatch
    {
        [HarmonyPostfix]
        public static void Postfix(ChallengesScreenViewModel __instance)
        {
            if (__instance == null || MenuCustomizationApplication.Instance == null) return;
            MenuCustomizationApplication.Instance.HideImageBg();
            MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine(__instance.transform).WrapToIl2Cpp());
        }
    }

    [HarmonyPatch(typeof(ChallengesScreenViewModel), "CloseChallenges")]
    internal static class ChallengesScreenViewModelCloseChallengesPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ChallengesScreenViewModel __instance)
        {
            var app = MenuCustomizationApplication.Instance;
            if (app == null || __instance == null) return;

            var view = __instance._mainMenuManager?.MainMenuBuilder?.SwitchableView;
            app.StartCoroutine(SetViewImplementationpatch.ApplyIndexZeroAfterFrames(view, 2).WrapToIl2Cpp());
        }
    }

    [HarmonyPatch(typeof(PrivateLobbyPlayerListEntryViewModel), "SetData")]
    internal static class PrivateLobbyPlayerListEntryViewModelSetDataPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PrivateLobbyPlayerListEntryViewModel __instance)
        {
            if (__instance == null) return;
            FeatureMorePlatformIcon.ApplyPrivateLobbyLocalName(__instance.transform, "", __instance.PlayerName);
            // SetData is the moment the game writes this row's name, so override it here too
            FeatureMorePlatformIcon.ApplyPrivateLobbyCustomName(__instance.transform, __instance.PlayerName);
        }
    }

    [HarmonyPatch(typeof(PrivateLobbyPlayerListViewModel), "OnEnable")]
    internal static class PrivateLobbyPlayerListViewModelNamePatch
    {
        [HarmonyPostfix]
        public static void Postfix(PrivateLobbyPlayerListViewModel __instance)
        {
            var app = MenuCustomizationApplication.Instance;
            if (__instance == null || app == null) return;

            app.StartCoroutine(app.ReapplySpecialForegroundNextFrame(MenuCustomizationApplication.SpecialScreen.PrivateLobbyPlayerList).WrapToIl2Cpp());
            app.StartCoroutine(FeatureMorePlatformIcon.ApplyPrivateLobbyAfterOpen(__instance).WrapToIl2Cpp());
        }
    }

    [HarmonyPatch(typeof(PrivateLobbyPlayerListViewModel), "UpdatePlayerList")]
    internal static class PrivateLobbyPlayerListViewModelUpdateListPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PrivateLobbyPlayerListViewModel __instance)
        {
            var app = MenuCustomizationApplication.Instance;
            if (__instance != null && app != null)
                app.StartCoroutine(app.ReapplySpecialForegroundNextFrame(MenuCustomizationApplication.SpecialScreen.PrivateLobbyPlayerList).WrapToIl2Cpp());

            FeatureMorePlatformIcon.QueuePrivateLobbyApply(__instance);
        }
    }

    // first open of the screen. SetUpPlayerList is a coroutine (its postfix fires before rows exist)
    // and UpdatePlayerList only runs on changes, so the screen-opened event is the reliable trigger.
    [HarmonyPatch(typeof(PrivateLobbyPlayerListViewModel), "OnOpened")]
    internal static class PrivateLobbyPlayerListViewModelOnOpenedPatch
    {
        [HarmonyPostfix]
        public static void Postfix(PrivateLobbyPlayerListViewModel __instance)
        {
            if (__instance != null)
                FeatureMorePlatformIcon.QueuePrivateLobbyApply(__instance);
        }
    }

    [HarmonyPatch(typeof(PrivateLobbyShowListViewModel), "Awake")]
    internal static class PrivateLobbyShowListViewModelAwakePatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            var app = MenuCustomizationApplication.Instance;
            app?.StartCoroutine(app.ReapplySpecialForegroundNextFrame(MenuCustomizationApplication.SpecialScreen.PrivateLobbyShowSelect).WrapToIl2Cpp());
            BetterFG.Tweaks.LobbyShowSearchTweak.OnShowListAwake();
        }
    }

    [HarmonyPatch(typeof(PrivateLobbyPlayerListViewModel), "Awake")]
    internal static class PrivateLobbyPlayerListViewModelAwakePatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            var app = MenuCustomizationApplication.Instance;
            app?.StartCoroutine(app.ReapplySpecialForegroundNextFrame(MenuCustomizationApplication.SpecialScreen.PrivateLobbyPlayerList).WrapToIl2Cpp());
        }
    }

    [HarmonyPatch(typeof(StateMatchmaking), nameof(StateMatchmaking.Initialise))]
    internal static class MatchmakingBeginpatch
    {
        [HarmonyPostfix]
        public static void Postfix() => BetterFG.Tweaks.MatchmakingQueueCountTweak.OnMatchmakingStart();
    }

    [HarmonyPatch(typeof(StateMatchmaking), nameof(StateMatchmaking.Teardown))]
    internal static class MatchmakingEndpatch
    {
        [HarmonyPostfix]
        public static void Postfix() => BetterFG.Tweaks.MatchmakingQueueCountTweak.OnMatchmakingEnd();
    }

    // fires once per player as they finish/are eliminated. shared by time-placement, stars and
    // qualification-time — each keeps its own logic in its own file, this just fans out.
    [HarmonyPatch(typeof(ClientGameManager), "HandleServerPlayerProgress")]
    internal static class HandleServerPlayerProgressHub
    {
        [HarmonyPostfix]
        public static void Postfix(ClientGameManager __instance, GameMessageServerPlayerProgress progressMessage)
        {
            BetterFG.Features.TimePlacement.FeatureTimePlacement.OnServerPlayerProgress(progressMessage);
            BetterFG.Features.Stars.FeatureStars.OnServerPlayerProgress(__instance, progressMessage);
            BetterFG.Features.QualificationTime.FeatureQualificationTime.OnServerPlayerProgress(__instance, progressMessage);
        }
    }

    // server-authoritative round result. this is the method that processes the results packet, so
    // by the postfix LocalPlayerSucceeded reflects this round's outcome. feeds the win-streak debug
    // overlay: local didn't succeed -> lost the show; succeeded on the final round -> won it.
    [HarmonyPatch(typeof(ClientGameManager), nameof(ClientGameManager.HandleServerRoundResults))]
    internal static class HandleServerRoundResultsHub
    {
        [HarmonyPostfix]
        public static void Postfix(ClientGameManager __instance)
        {
            //BetterFG.Features.WinStreakDebug.WinStreakDebugService.OnRoundResults();
        }
    }

    // UpdateDisplay is defined on the base LoadingScreenViewModel, but that VM also lives at the
    // main menu and ticks there — gating to the LoadingGameScreenViewModel subtype filters it down
    // to actual in-round loading screens so PB lookups don't poll endlessly at the menu.
    [HarmonyPatch(typeof(LoadingScreenViewModel), nameof(LoadingScreenViewModel.UpdateDisplay))]
    internal static class LoadingScreenUpdateDisplayHub
    {
        [HarmonyPostfix]
        public static void Postfix(LoadingScreenViewModel __instance)
        {
            if (__instance == null || __instance.TryCast<LoadingGameScreenViewModel>() == null) return;
            BetterFG.Tweaks.ChangeSplashScreenTweak.OnLoadingScreenUpdateDisplay();
            BetterFG.Features.QualificationTime.FeatureQualificationTime.OnLoadingScreenUpdateDisplay();
        }
    }

    // round spawn

    [HarmonyPatch(typeof(ClientGameManager), nameof(ClientGameManager.DoCharacterObjectSpawnPreparations))]
    internal static class RoundBeanSpawn
    {
        [HarmonyPostfix]
        public static void Postfix(MPGNetObject pNetObject, bool isLocalPlayer)
        {
            if (!isLocalPlayer || pNetObject == null) return;
            var bean = pNetObject.gameObject;
            if (bean == null) return;
            Plugin.Log.LogInfo($"RoundBeanSpawn: local bean: {bean.name}");
            BeanMonitorService.LocalPlayerBean = bean;
            BeanMonitorService.PushBean(bean);
            //FeatureAllCosmetics.ApplyCachedToBean(bean);

            // NOTE: colour/pattern/texture reapply for the local bean now happens in
            // CleanupLoadingScreens (behind the loading screen) instead of here on a delay - the
            // old 1s-after-spawn reapply made the skin visibly pop in during the intro cameras.
        }
    }

    // qualify screen

    [HarmonyPatch(typeof(CellBehaviour), nameof(CellBehaviour.AddFallGuy))]
    internal static class QualifyScreenOnBeanSpawn
    {
        [HarmonyPostfix]
        public static void Postfix(CellBehaviour __instance, Transform t)
        {
            if (!__instance._localPlayer || t == null) return;
            BeanMonitorService.LocalPlayerBean = t.gameObject;
            BeanMonitorService.PushBean(t.gameObject);
        }
    }

    [HarmonyPatch(typeof(CellBehaviour), nameof(CellBehaviour.SetName), new[] { typeof(string) })]
    internal static class QualCellSetNamePatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref string __0)
        {
            if (SettingsService.Get("nametag.enabled", "false") != "true") return;

            string custom = LocalPlayerInfo.CustomName;
            if (string.IsNullOrEmpty(custom)) return;

            string real = LocalPlayerInfo.FGlocalplayerusername;
            if (string.IsNullOrEmpty(real)) return;

            if (__0 != null && __0.Equals(real, StringComparison.Ordinal))
                __0 = custom;
        }
    }

    [HarmonyPatch(typeof(StateQualificationScreen), "DisplayFallguy")]
    internal static class QualificationScreenPlatformPatch
    {
        [HarmonyPostfix]
        public static void Postfix(GameObject __0, CellBehaviour __1, PlayerMetadata __2)
        {
            // one-frame delay so the game finishes binding the cell's nametag material (gold/fame
            // gets assigned right after this fires). running ApplyToNametag on every TMP under the
            // cell re-derives any drifted famepass material onto our atlas — same fix the live
            // watchdog uses, just scoped to this one cell at the right moment.
            string cellKey = __2 != null ? __2.PlayerKey : "";
            if (__1 != null && BeanMonitorService.Instance != null)
                BeanMonitorService.Instance.StartCoroutine(FixCellNameNextFrame(__1, cellKey).WrapToIl2Cpp());

            if (__1 != null && __1._localPlayer)
                BetterFG.Tweaks.DynamicQualScreenTweak.OnLocalPlayerDisplayed(__1);

            try
            {
                if (__1 == null || __2 == null) return;

                var sprite = FeatureMorePlatformIcon.SpriteForPlayerKey(__2.PlayerKey);
                if (sprite == null) return;

                var renderer = __1._platformIcon;
                if (renderer == null) return;

                renderer.sprite = sprite;
                renderer.gameObject.SetActive(true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("QualScreen: platform " + ex.Message);
            }
        }

        private static IEnumerator FixCellNameNextFrame(CellBehaviour cell, string cellKey)
        {
            yield return null;
            if (cell == null) yield break;
            foreach (var t in cell.GetComponentsInChildren<TMPro.TMP_Text>(true))
                if (t != null) BetterFG.Customization.Menu.FontReplacementService.ApplyToNametag(t);

            // LOCAL player only — matched by cleaned player key (survives profile name swaps), not object
            // identity. our crown override is ours; don't stamp it on every displayed fall guy.
            if (BetterFG.Nametag.CrownRankService.Enabled && BetterFG.Nametag.CrownRankService.IsLocalPlayerKey(cellKey))
                BetterFG.Nametag.CrownRankService.ApplyCrownTo(cell, BetterFG.Nametag.CrownRankService.CfgFromSettings());
        }
    }

    // qualification screen teardown is another window where the player can legitimately bail —
    // route it through the same leave flow as loading-screen back.
    [HarmonyPatch(typeof(StateQualificationScreen), nameof(StateQualificationScreen.Teardown))]
    internal static class QualScreenTeardownLeaveHook
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            BetterFG.Tweaks.LeaveOnLoadingScreenTweak.OnExternalLeaveTrigger();
            BetterFG.Tweaks.KeepNametagsTweak.OnQualScreenTeardown();
        }
    }

    // every end-of-round banner's OnOpened: reparent into BannersState so the leaderboard draws over
    // the banner, paint its colours, and let enabled tweaks react (respawn prompt teardown). the
    // three Eliminated variants also flip on the leave-window so the back-prompt shows. one patch
    // over all six via TargetMethods, branching the colour call by the banner's runtime type
    [HarmonyPatch]
    internal static class Patch_BannerScreens_OnOpened
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            Type[] types =
            {
                typeof(FGClient.UI.QualifiedScreenViewModel),
                typeof(FGClient.EliminatedScreenViewModel),
                typeof(FGClient.EliminatedSquadEarlyScreenViewModel),
                typeof(FGClient.EliminatedSquadScreenViewModel),
                typeof(FGClient.UI.WinnerScreenViewModel),
                typeof(FGClient.RoundEndedScreenViewModel),
            };
            foreach (var t in types)
            {
                var m = AccessTools.DeclaredMethod(t, "OnOpened");
                if (m != null) yield return m;
            }
        }

        [HarmonyPostfix]
        public static void Postfix(Component __instance)
        {
            BetterFG.Features.TimePlacement.TimePlacementBannerReparent.Apply(__instance);

            var app = MenuCustomizationApplication.Instance;
            bool eliminatedBanner = false;
            if (__instance.TryCast<FGClient.UI.QualifiedScreenViewModel>() != null)
                app?.ApplyQualifiedBannerColours(__instance);
            else if (__instance.TryCast<FGClient.EliminatedSquadScreenViewModel>() != null)
            { app?.ApplySquadBannerColours(__instance); eliminatedBanner = true; }
            else if (__instance.TryCast<FGClient.EliminatedScreenViewModel>() != null
                  || __instance.TryCast<FGClient.EliminatedSquadEarlyScreenViewModel>() != null)
            { app?.ApplyEliminatedBannerColours(__instance); eliminatedBanner = true; }
            else if (__instance.TryCast<FGClient.UI.WinnerScreenViewModel>() != null)
                app?.ApplyWinnerBannerColours(__instance);
            else
                app?.ApplyRoundOverBannerColours(__instance);

            if (eliminatedBanner)
            {
                var vm = __instance.TryCast<FGClient.UI.Core.ScreenViewModel>();
                BetterFG.Tweaks.LeaveOnLoadingScreenTweak.OnExternalLeaveTrigger();
                BetterFG.Tweaks.LeaveOnLoadingScreenTweak.ActiveEliminatedBannerClose =
                    () => { if (vm != null) vm.OnClosed(); };
            }

            BetterFG.Tweaks.BfgTweak.RaiseBannerShown();
        }
    }

    // OnClosed for the three Eliminated banners, clears the leave-window + close delegate. identical
    // body for all three, so one patch over the set
    [HarmonyPatch]
    internal static class Patch_EliminatedBanners_OnClosed
    {
        static IEnumerable<MethodBase> TargetMethods()
        {
            Type[] types =
            {
                typeof(FGClient.EliminatedScreenViewModel),
                typeof(FGClient.EliminatedSquadEarlyScreenViewModel),
                typeof(FGClient.EliminatedSquadScreenViewModel),
            };
            foreach (var t in types)
            {
                var m = AccessTools.DeclaredMethod(t, "OnClosed");
                if (m != null) yield return m;
            }
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            BetterFG.Tweaks.LeaveOnLoadingScreenTweak.OnExternalLeaveTriggerEnd();
            BetterFG.Tweaks.LeaveOnLoadingScreenTweak.ActiveEliminatedBannerClose = null;
        }
    }

    // reward screen

    [HarmonyPatch(typeof(StateRewardScreen), nameof(StateRewardScreen.OnSceneLoaded))]
    internal static class RewardScreen
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            BetterFG.Tweaks.LeaveOnLoadingScreenTweak.OnRewardScreenEntered();
            if (BeanMonitorService.Instance == null) return;
            BeanMonitorService.Instance.StartCoroutine(PollRewardScreen().WrapToIl2Cpp());

        }

        private static IEnumerator PollRewardScreen()
        {
            float elapsed = 0f;
            bool first = true;
            while (elapsed < 10f)
            {
                var root = GameObject.Find("3D Assets");
                if (root != null)
                {
                    var handler = root.GetComponentInChildren<RewardScreen3DReferencesHandler>();
                    var bean = handler?.FallGuyGameObject;
                    if (bean != null)
                    {
                        Plugin.Log.LogInfo($"RewardScreen: bean: {bean.name}");
                        BeanMonitorService.PushBean(bean);
                        SkinApplicationService.Instance?.ReapplyExpectedGameCosmeticVisuals(bean);
                        BeanMonitorService.Instance.StartCoroutine(BeanMonitorService.PollAndPushRewardPlinth().WrapToIl2Cpp());
                        BeanMonitorService.Instance.StartCoroutine(CostumePollerKick.Kick(bean).WrapToIl2Cpp());
                        yield break;
                    }
                }
                if (first) { yield return null; first = false; }
            }
            Plugin.Log.LogWarning("RewardScreen: timed out waiting for bean");
        }
    }

    // costume clone + game's own costume children both arrive async after the bean spawns, and
    // the poller's own Update only ticks every 1s. kick it every 2 frames so we hide late arrivals
    // quickly instead of letting the base bean show through for a beat. bails out the moment there
    // are no pollers (no custom skin -> nothing to do) or once the pollers report they've settled,
    // so on a plain bean this costs one hierarchy walk instead of ten.
    internal static class CostumePollerKick
    {
        public static IEnumerator Kick(GameObject bean)
        {
            // the costume clone (and its poller) load async — on first menu entry the bean is found
            // ~700ms before the poller exists. don't bail on "no pollers yet"; wait for one to show up
            // (the base bean + UGC costume are both visible during that window), then hammer.
            int seenAt = -1;
            int idleFrames = 0;
            for (int i = 0; i < 90 && bean != null; i++)
            {
                var pollers = bean.GetComponentsInChildren<BetterFG.Customization.Player.CostumePollerComponent>(true);
                if (pollers == null || pollers.Length == 0)
                {
                    // still waiting for the costume to load. give up if it never comes.
                    if (i >= 80) yield break;
                    yield return null;
                    continue;
                }
                if (seenAt < 0) seenAt = i;

                bool anyWork = false;
                foreach (var p in pollers)
                    if (p != null && p.PollNow()) anyWork = true;

                // settled for two kicks running -> late arrivals are all in, stop hammering.
                idleFrames = anyWork ? 0 : idleFrames + 1;
                if (idleFrames >= 2 && i - seenAt >= 2) yield break;

                yield return null;
                yield return null;
            }
        }
    }

    // fans out every game-state swap to the things that care: UnityRoundLoader (first, sets InLevelEditor),
    // the tweak fan-out, menu music, and the victory/qual-screen handling below. FallGuysLib patches the
    // state machine and raises GameStateEvents.OnStateChanged; this is subscribed in Plugin.Load.
    internal static class GameStateDispatcher
    {
        public static void OnStateChanged(GameStateMachine.IGameState newState)
        {
            try
            {
                // must run first, it sets InLevelEditor, which the tweaks' OnStateChanged reads
                BetterFG.Features.UnityRound.Editor.UnityRoundLoader.OnReplaceCurrentState(newState);
                // enabled tweaks reacting to the state swap (anti-afk re-enable, respawn prompt teardown,
                // server-info label kill in the editor). MenuMusicService is a service, stays explicit.
                BetterFG.Tweaks.BfgTweak.RaiseStateChanged(newState);
                BetterFG.Services.MenuMusicService.OnReplaceCurrentState(newState);

                if (newState == null) return;

                if (newState.TryCast<StateQualificationScreen>() != null)
                    BetterFG.Tweaks.KeepNametagsTweak.OnQualScreenSwitch();

                var victory = newState.TryCast<StateVictoryScreen>();
                if (victory == null) return;

                BetterFG.Tweaks.SkipVictoryTweak.ActiveState = victory;

                Plugin.Log.LogInfo($"VictoryScreen: ReplaceCurrentState -> StateVictoryScreen, starting handler");
                if (BeanMonitorService.Instance == null)
                {
                    Plugin.Log.LogWarning("VictoryScreen: BeanMonitorService.Instance is null, can't start coroutine");
                    return;
                }
                BeanMonitorService.Instance.StartCoroutine(HandleVictory(victory).WrapToIl2Cpp());
            }
            catch (Exception ex) { Plugin.Log.LogError($"VictoryScreen: postfix: {ex}"); }
        }

        private static IEnumerator HandleVictory(StateVictoryScreen victory)
        {
            // WinnersInfos isn't populated the instant the state swaps in (the winner data
            // lands a few hundred ms later), so poll until it's ready instead of a single wait.
            Il2CppSystem.Collections.Generic.List<WinnerInfo> infos = null;
            float elapsed = 0f;
            while (elapsed < 5f)
            {
                try
                {
                    var winnersInfos = victory._victoryScreenViewModel?.WinnersInfos;
                    if (winnersInfos != null && winnersInfos.IsPopulated)
                    {
                        infos = winnersInfos.WinnersInfo;
                        if (infos != null && infos.Count > 0) break;
                    }
                }
                catch (Exception ex) { Plugin.Log.LogError($"VictoryScreen: poll infos: {ex}"); yield break; }

                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }

            if (infos == null || infos.Count == 0)
            {
                Plugin.Log.LogWarning($"VictoryScreen: winner infos never populated after {elapsed:0.0}s");
                yield break;
            }

            WinnerInfo localWinner = null;
            try
            {
                string localkey = GlobalGameStateClient.Instance?.GetLocalPlayerKey();
                Plugin.Log.LogInfo($"VictoryScreen: localkey={localkey}, winnerCount={infos.Count}");
                if (string.IsNullOrEmpty(localkey)) yield break;

                // platform prefix varies for the same account (pc_steam_, xb1_, switch_, ...),
                // so just compare the username after the platform token.
                string localName = PlayerUtils.CleanPlayerName(localkey);

                for (int i = 0; i < infos.Count; i++)
                {
                    var info = infos[i];
                    string winnerKey = info?.PlayerMetadata?.PlayerKey;
                    Plugin.Log.LogInfo($"VictoryScreen: winner[{i}] key={winnerKey}");
                    if (string.IsNullOrEmpty(winnerKey)) continue;

                    if (PlayerUtils.CleanPlayerName(winnerKey).Equals(localName, StringComparison.OrdinalIgnoreCase))
                    {
                        localWinner = info;
                        Plugin.Log.LogInfo($"VictoryScreen: matched local winner at index {i}");
                        break;
                    }
                }
            }
            catch (Exception ex) { Plugin.Log.LogError($"VictoryScreen: resolve: {ex}"); yield break; }

            if (localWinner == null)
            {
                Plugin.Log.LogInfo("VictoryScreen: local player is not the winner, nothing to do");
                yield break;
            }

            // wait one frame before touching the nameplate — the game's own NameTagViewModel
            // setup runs in the same frame WinnersInfos becomes populated, and applying our
            // changes before that finishes leaves the custom name + backing missing (game
            // overwrites them right after). nickname is a separate object so it survived.
            yield return null;

            // local won — apply our nametag to its plate, just like any other nametag
            try
            {
                var vm = localWinner.NamePlateRef;
                Plugin.Log.LogInfo($"VictoryScreen: NamePlateRef={(vm != null)}");
                // we already know this is the local player, so pass the local username as the
                // key — that's what HandleViewModel matches on to route to the local path.
                if (vm != null)
                {
                    NametagPatchHub.HandleViewModel(vm, LocalPlayerInfo.FGlocalplayerusername);
                    // the icon X is positioned for the in-game HUD plate; the victory plate is a
                    // different width, so nudge the icon back into place a couple frames later
                    // (after the applicator's own next-frame reposition has run).
                    BeanMonitorService.Instance.StartCoroutine(NudgeVictoryIcon(vm).WrapToIl2Cpp());
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"VictoryScreen: nameplate: {ex}"); }

            BeanMonitorService.Instance.StartCoroutine(BeanMonitorService.PollAndPushVictoryPlinth().WrapToIl2Cpp());

            var spawn = localWinner._spawnTransform;
            Plugin.Log.LogInfo($"VictoryScreen: spawnTransform={(spawn != null ? spawn.name : "null")}");
            if (spawn != null)
                BeanMonitorService.Instance.StartCoroutine(PollForBean(spawn).WrapToIl2Cpp());
        }

        // how far to shove the icon on the victory plate vs where the HUD-tuned code put it.
        private const float VICTORY_ICON_NUDGE_X = 25f;
        private const float VICTORY_ICON_NUDGE_Y = 0f;

        private static IEnumerator NudgeVictoryIcon(NameTagViewModel vm)
        {
            yield return null;
            yield return null;

            RectTransform iconRt = null;
            try
            {
                var t = FindIconUnder(vm.transform);
                iconRt = t != null ? t.GetComponent<RectTransform>() : null;
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"VictoryScreen: nudge find: {ex.Message}"); yield break; }

            if (iconRt == null) yield break;
            iconRt.anchoredPosition += new Vector2(VICTORY_ICON_NUDGE_X, VICTORY_ICON_NUDGE_Y);
        }

        private static Transform FindIconUnder(Transform root)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].name == "BetterFG_UINametagIcon")
                    return all[i];
            return null;
        }

        private static IEnumerator PollForBean(Transform spawnTransform)
        {
            float elapsed = 0f;
            while (elapsed < 10f)
            {
                try
                {
                    var bean = spawnTransform.Find("FallGuy(Clone)");
                    if (bean != null)
                    {
                        BeanMonitorService.PushBean(bean.gameObject);
                        if (BeanMonitorService.Instance != null)
                            BeanMonitorService.Instance.StartCoroutine(CostumePollerKick.Kick(bean.gameObject).WrapToIl2Cpp());
                        yield break;
                    }
                }
                catch (Exception ex) { Plugin.Log.LogError($"VictoryScreen: poll: {ex.Message}"); yield break; }
                yield return new WaitForSeconds(0.25f);
                elapsed += 0.25f;
            }
            Plugin.Log.LogWarning("VictoryScreen: timed out waiting for bean");
        }
    }

    // victory screen "likes" panel keeps building itself out after our menu sweep already ran,
    // so its yellow heart fills end up unstyled. UpdateData is the right hook: fires every time
    // the panel rebuilds for the current round's level data. one-frame delay so the child images
    // exist by the time we recolour.
    [HarmonyPatch(typeof(FGClient.UI.UGCLevelLikeViewModel), nameof(FGClient.UI.UGCLevelLikeViewModel.UpdateData))]
    internal static class UGCLevelLikeViewModelForeground
    {
        [HarmonyPostfix]
        public static void Postfix(FGClient.UI.UGCLevelLikeViewModel __instance)
        {
            try
            {
                if (__instance == null || MenuCustomizationApplication.Instance == null) return;
                MenuCustomizationApplication.Instance.StartCoroutine(
                    MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine(__instance.transform).WrapToIl2Cpp());
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"VictoryScreen: likes panel reapply: {ex}"); }
        }
    }

    // cleanup loading screens + nametag queue

    [HarmonyPatch(typeof(RoundLoader), "CleanupLoadingScreens")]
    public class CleanupLoadingScreens
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            BetterFG.Features.QualificationTime.FeatureQualificationTime.OnCleanupLoadingScreens();

            BetterFGUnityRounds.RestoreMusic();

            var menuApp = MenuCustomizationApplication.Instance;
            if (menuApp != null)
                menuApp.StartCoroutine(ReapplyPinkGreyAfterDelay(menuApp).WrapToIl2Cpp());

            PlayerUtils.TryFindMatchPlayer();
            var fgcc = PlayerUtils.PlayerController;
            if (fgcc != null)
            {
                BeanMonitorService.LocalPlayerBean = fgcc.gameObject;
                Plugin.Log.LogInfo($"CLS: player set: {fgcc.gameObject.name}");

                // apply the local bean's colour/pattern/texture + game cosmetics from here (behind
                // the loading screen) instead of on spawn, so the skin doesn't visibly pop in during
                // the intro cameras. fire at 1s and 2s because the customisation handler / mesh may
                // not be bound on the first attempt - the second pass catches it.
                var beanHost = BeanMonitorService.Instance;
                if (beanHost != null)
                    beanHost.StartCoroutine(ReapplyLocalCosmeticsBehindLoadingScreen(fgcc.gameObject).WrapToIl2Cpp());
            }
            try
            {
                var indicator = PlayerUtils.PlayerLandingIndicator();
                if (indicator != null)
                    indicator.transform.localPosition = Vector3.zero;
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"CLS: landing indicator: {ex.Message}"); }

            CameraUtils.DisableXRayRenderer();
            NetworkClient.Instance?.OnRoundStart();

            var host = NametagTab.Instance;
            if (host != null)
                host.StartCoroutine(WaitForNametag().WrapToIl2Cpp());
            else
                NametagIconApplicator.ApplyNametag();

            // crown rank: apply from cleanup, NOT gated on WaitForNametag's sprite finder (that stays null until
            // StateGameInProgress ~18s later, which is why the badge only lit up after the intro). the badge
            // already exists here; give it a few frames to finish binding, then apply once.
            var crownHost = BeanMonitorService.Instance;
            if (crownHost != null)
                crownHost.StartCoroutine(ApplyCrownAfterFrames(5).WrapToIl2Cpp());

            NametagIconApplicator.ApplyPlatformIcon();

            // shadow resolution override only takes effect once a directional light exists in the
            // scene — wait until round assets are loaded, then push it.
            BetterFG.Tweaks.ShadowCustomResolutionTweak.ApplyIfEnabled();
        }

        private static IEnumerator ReapplyPinkGreyAfterDelay(MenuCustomizationApplication menuApp)
        {
            yield return new WaitForSeconds(1f);
            menuApp.ReapplyBakedPinkGreyTextures();
        }

        private static IEnumerator ReapplyLocalCosmeticsBehindLoadingScreen(GameObject bean)
        {
            // push the bean (same as the spawn path) so OnBeansFound applies colour/pattern +
            // cosmetics + texture. 1s then 2s: first pass usually lands, second catches the case
            // where the customisation handler / mesh wasn't bound yet on the first try.
            yield return new WaitForSeconds(1f);
            if (bean != null) BeanMonitorService.PushBean(bean);

            yield return new WaitForSeconds(1f);
            if (bean != null) BeanMonitorService.PushBean(bean);

            // the spawn-time scale fires before the roster settles, so GetOtherPlayerIds can read
            // solo when it isn't (or vice versa). the round's fully up by now — reapply with the
            // solo/public check trustworthy.
            if (bean != null)
                PlayerScaleService.RestorePlayerScaleToBean(bean);
        }

        private static IEnumerator ApplyCrownAfterFrames(int frames)
        {
            for (int i = 0; i < frames; i++) yield return null;
            BetterFG.Nametag.CrownRankService.ApplyLocal();
        }

        private static IEnumerator WaitForNametag()
        {
            float elapsed = 0f;
            while (elapsed < 10f)
            {
                if (NametagFinder.FindLocalNameTagSprite() != null)
                {
                    NametagIconApplicator.ApplyNametag();
                    // the game assigns the gold/famepass material a beat AFTER the nametag first appears,
                    // which overwrites our first font swap. re-apply a couple times so the custom font
                    // lands on the gold material on the FIRST round load, not only after HandleServerStartRound.
                    // (crown rank isn't touched here — it's applied once from the cleanup coroutine above.)
                    yield return new WaitForSeconds(0.3f);
                    NametagIconApplicator.ApplyNametag();
                    yield return new WaitForSeconds(0.6f);
                    NametagIconApplicator.ApplyNametag();
                    yield break;
                }
                yield return new WaitForSeconds(0.25f);
                elapsed += 0.25f;
            }
            Plugin.Log.LogWarning("CLS: nametag never appeared after 10s");
        }
    }

    // round start � bean + nametag + ragdoll + phrases

    [HarmonyPatch(typeof(GlobalGameStateClient), "HandleServerStartRound")]
    public class HandleServerStartRoundPa
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            foreach (var obj in Resources.FindObjectsOfTypeAll<PlayerInfoDisplayGameObject>())
                obj.SetText(BetterFG.Tweaks.StripSizeTagsTweak.StripRegex.Replace(obj._text.text, ""));
            foreach (var obj in Resources.FindObjectsOfTypeAll<PlayerInfoDisplayCanvas>())
                obj.SetText(BetterFG.Tweaks.StripSizeTagsTweak.StripRegex.Replace(obj._text.text, ""));
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            BetterFGUnityRounds.StartCustomMusicIfAny();

            MenuCustomizationApplication.Instance.StartCoroutine(MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine().WrapToIl2Cpp());

            var persis = GameObject.Find("UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/PersistentOverlayUI").transform;

            var speccount = GameObject.Find("UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates").transform.Find("PlayingState").Find("UI_SpectatorCount_Prefab");

            var specRT = speccount.GetComponent<RectTransform>();
            if (specRT != null)
            {
                specRT.anchorMin = new Vector2(1f, 1f);
                specRT.anchorMax = new Vector2(1f, 1f);
                specRT.pivot = new Vector2(1f, 1f);
                var parentRT = specRT.parent as RectTransform;
                var parentSize = parentRT != null ? parentRT.rect.size : new Vector2(1920f, 1080f);
                specRT.anchoredPosition = new Vector2(-parentSize.x * 0.18f, -parentSize.y * 0.18f);
            }
            var container = speccount.Find("Container");
            if (container != null)
                container.localPosition = Vector3.zero;

            persis.Find("PB_UI_FallFeed").localPosition = new Vector3(0f, -66.1997f, 0f);

            FeatureTimePlacement.SpawnList();

            PlayerUtils.TryFindMatchPlayer();
            var fgcc = PlayerUtils.PlayerController;
            if (fgcc != null)
                BeanMonitorService.LocalPlayerBean = fgcc.gameObject;

            NametagIconApplicator.ApplyNametag();
            NametagIconApplicator.ApplyPlatformIcon();

            var platformHost = BeanMonitorService.Instance;
            if (platformHost != null)
                platformHost.StartCoroutine(ApplyKnownNametagPlatformsAfterRoundStart().WrapToIl2Cpp());

            // beans that spawn a bit after round start miss the apply above, so re-run the full nametag
            // refresh once a second for 2s to catch late spawners.
            if (platformHost != null)
                platformHost.StartCoroutine(PollNametagsAfterRoundStart().WrapToIl2Cpp());

            // anti-grief <size> strip: only player-controlled nametag text can carry a malicious
            // <size> tag, so only sweep those two HUDs instead of every TMP_Text in the scene.
            foreach (var hudPath in new[]
            {
                "----------------CAMERAS/LevelCameras_LevelEditor/Main Camera Brain/PlayerInfoHUD",
                "UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/PersistentUnderlayUI/PB_InfoHUD/Parent",
            })
            {
                var hud = GameObject.Find(hudPath);
                if (hud == null) continue;
                foreach (var tmp in hud.GetComponentsInChildren<TMPro.TMP_Text>(true))
                {
                    if (tmp != null && !string.IsNullOrEmpty(tmp.text))
                        tmp.text = BetterFG.Tweaks.StripSizeTagsTweak.Strip(tmp.text);
                }
            }

            try
            {
                var primeHandler = UnityEngine.Object.FindObjectOfType<SocialPrimeHandler>();
                if (primeHandler != null)
                {
                    PhraseInjectionService.ReapplyToWheel(primeHandler);
                    EmoticonInjectionService.RestoreSlots(primeHandler);
                    EmoteInjectionService.RestoreSlots(primeHandler);
                    EmoticonInjectionService.InjectSlots(primeHandler);
                    EmoteInjectionService.InjectSlots(primeHandler);
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"HSR: phrase patch: {ex.Message}"); }

            // fan out round-start to every enabled tweak that overrides OnRoundStart (noises, camera
            // assist, server info, respawn, lively fall guys). each keeps its own logic; this is the
            // single call site. InstantLandingIndicator isn't registered as a tweak so it stays above.
            BetterFG.Tweaks.BfgTweak.RaiseRoundStart();

            var host = BeanMonitorService.Instance;
            if (host != null)
                host.StartCoroutine(DelayedDisableRagdoll(fgcc, 5.1f).WrapToIl2Cpp());
        }



        private static IEnumerator ApplyKnownNametagPlatformsAfterRoundStart()
        {
            yield return null;

            yield return new WaitForSeconds(0.75f);
            NametagIconApplicator.ApplyKnownPlatformIcons();
            NametagIconApplicator.ApplyRenderQueueToAllNametags();
        }

        private static IEnumerator PollNametagsAfterRoundStart()
        {
            for (int i = 0; i < 2; i++)
            {
                yield return new WaitForSeconds(1f);
                NametagPatchHub.RefreshRemoteNametags();
                NametagIconApplicator.ApplyNametag();
            }
        }

        public static IEnumerator DelayedDisableRagdoll(FallGuysCharacterController fgcc, float time)
        {
            yield return new WaitForSeconds(time);
            if (fgcc != null)
            {
                //Debug.Log("disabling upper body ragdoll");
                //PlayerUtils.DisablePlayerUpperBodyRagdoll(fgcc);
            }
        }
    }

    [HarmonyPatch(typeof(SwitchableView), nameof(SwitchableView.SetViewImplementation))]
    internal static class SetViewImplementationpatch
    {
        [HarmonyPostfix]
        static void SetViewImplementation(SwitchableView __instance)
        {
            // tear down any leftover PB popup on tab/view changes — but ONLY when our popup is
            // actually open. this used to nuke EVERY child of ModalMessage on every single view
            // switch, which wiped the game's own modal node and broke back/escape navigation in
            // nested settings views (options/controller switchboard).
            if (BetterFG.Features.QualificationTime.PBPopup.IsOpen)
            {
                var modalMessage = GameObject.Find("UICanvas_Client_V2(Clone)/ModalMessage");
                if (modalMessage != null)
                {
                    var t = modalMessage.transform;
                    for (int i = t.childCount - 1; i >= 0; i--)
                        UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
                }
                BetterFG.Features.QualificationTime.PBPopup.IsOpen = false;
            }

            // none of the reapplies below are menu-cosmetic concerns in the level editor — they're
            // font/nametag/menu-lighting/foreground sweeps. but saving/publishing in creative fires a
            // burst of view switches, and every switch here would run whole-scene FindObjectsOfType
            // sweeps over the editor scene (your entire placed level + all the editor UI). that stacks
            // into the multi-second save freeze people hit. nothing in the editor needs it, so skip.
            if (BetterFG.Features.UnityRound.Editor.UnityRoundLoader.InLevelEditor) return;

            // keep this hook quiet. SetViewImplementation fires on normal tab/view changes, and
            // repainting the whole UI canvas here makes private lobby/show selector feel awful.
            var app = MenuCustomizationApplication.Instance;
            if (app != null && __instance != null)
                app.StartCoroutine(ApplyIndexZeroAfterFrames(__instance, 1).WrapToIl2Cpp());
        }

        internal static IEnumerator ApplyIndexZeroAfterFrames(SwitchableView view, int frames)
        {
            for (var i = 0; i < frames; i++)
                yield return null;

            if (view != null && view.gameObject.name == "TabsHorizontalLayout") yield break;

            // a view switch only rebuilds the new view's subtree — the rest of the scene kept its
            // font/nametags/colours. resolve that subtree and scope all our reapplies to it instead
            // of doing whole-scene FindObjectsOfType sweeps every switch (that was the menu lag).
            Transform scope = null;
            try
            {
                var views = view?._views;
                int index = view != null ? view.CurrentViewIndex : -1;
                if (views != null && index >= 0 && index < views.Length && views[index] != null)
                    scope = views[index].transform;
            }
            catch { }

            // resolution/graphics changes rebuild the canvases and wipe our custom UI scale reference
            // res, so reassert it on every view switch (no-op when the toggle's off).
            UIScaleService.ApplySaved();

            // this already reapplies the custom textures internally, don't double it up
            SkinApplicationService.Instance?.ReapplyExpectedGameCosmeticVisuals();

            // ambient/sun get clobbered by the game's per-view lighting setup — reassert on every switch
            MenuCustomizationApplication.Instance?.ApplyAmbientFromSettings();
            MenuCustomizationApplication.Instance?.ApplySunFromSettings();

            // image bg hidden whenever the customiser screen is up
            MenuCustomizationApplication.Instance?.RefreshImageBgVisibility();

            // the game's nametag customisation screen rewrites the live nameplates, so re-assert ours.
            // scoped to the rebuilt view — nameplates elsewhere survived the switch.
            if (scope != null) NametagFinder.ReapplyNameplatesInScope(scope);
            else NametagFinder.ReapplyAllNameplates();

            // menu text gets rebuilt on view switches (the game re-instantiates panels), dropping the
            // custom font — re-swap. scoped to the new view; the rest of the scene still has our font.
            // RestoreUncovered still runs scene-wide to heal any text that just turned famepass.
            FontReplacementService.RestoreUncovered();
            if (scope != null) FontReplacementService.ApplyToScope(scope);
            else FontReplacementService.HealAndReapply();

            if (view == null || view.CurrentViewIndex != 0) yield break;

            MenuCustomizationApplication.AutoApplyCamFromSettings();

            var app = MenuCustomizationApplication.Instance;
            if (app == null) yield break;

            // a SetData patch already recolours the symphony show selector, so don't let the
            // view-switch repaint touch anything under it (double-applies / fights with that)
            if (scope != null)
                app.ReapplyForegroundFromSettings(scope, "Prime_UI_SymphonyShowSelector_Prefab_Canvas(Clone)");
        }
    }

    // shared hub for ClientGameManager.SwitchToSpectatorMode — every tweak/feature that needs to
    // react to the local player entering spectator mode routes through here instead of installing
    // its own patch. keeps the trampoline count on this hot method to one.
    [HarmonyPatch(typeof(ClientGameManager), nameof(ClientGameManager.SwitchToSpectatorMode))]
    internal static class Patch_SwitchToSpectatorMode
    {
        [HarmonyPrefix]
        private static void Prefix(ClientGameManager __instance)
        {
            BetterFG.Tweaks.SpectatorMusicTweak.ApplyIfWanted(__instance);
            BetterFG.Tweaks.SpectatorMusicTweak.ForceSpectatorMix(true);
        }

        [HarmonyPostfix]
        private static void Postfix(ClientGameManager __instance)
        {
            BetterFG.Tweaks.SpectatorMusicTweak.ApplyIfWanted(__instance);
            BetterFG.Tweaks.SpectatorMusicTweak.ForceSpectatorMix(true);
            BetterFG.Tweaks.BfgTweak.RaiseSpectatorMode();
            FeatureTimePlacement.OnSpectatorMode();
        }
    }

    // WIP Multi-Show Queue — shelved. launch confirms the highlighted show rather than the pinned set
    // (OnShowConfirmed seems to act on the game's own selected tile, not the instance we call it on).
    // patches commented out so the tweak installs nothing while parked.
    //[HarmonyPatch(typeof(FGClient.CatapultServices.MatchmakingService), nameof(FGClient.CatapultServices.MatchmakingService.GetPlayerMatchmakingAuthToken))]
    //internal static class MatchmakingServiceGetTokenPatch
    //{
    //    [HarmonyPrefix]
    //    private static void Prefix(Il2CppSystem.Collections.Generic.List<string> showIds)
    //    {
    //        BetterFG.Tweaks.MultiShowSelectTweak.AugmentShowIds(showIds);
    //    }
    //}

    //[HarmonyPatch(typeof(ShowSelectorShowTileViewModel), nameof(ShowSelectorShowTileViewModel.OnShowConfirmed))]
    //internal static class ShowSelectorTileConfirmPatch
    //{
    //    [HarmonyPrefix]
    //    private static bool Prefix(ShowSelectorShowTileViewModel __instance)
    //    {
    //        return !BetterFG.Tweaks.MultiShowSelectTweak.OnTileConfirm(__instance);
    //    }
    //}
}
