using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BetterFG.Core;
using BetterFG.Tweaks;
using BetterFG.Network;
using BetterFG.Patches;
using BetterFG.Services;
using BetterFG.Customization.Player;
using BetterFG.Customization.Social;
using BetterFG.UI;
using BetterFG.UI.Components;
using BetterFG.UI.SideWheel;
using BetterFG.UI.Tab;
using BetterFG.UI.Windows;
using BetterFG.Customization.Menu;
using BetterFG.Features.UnityRound;
using BetterFG.Features.UnityRound.Behaviours;
using BetterFG.Features.QualificationTime;
using FGClient;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using System;
using System.IO;
using System.Reflection;
using static LevelEditor.LevelEditorWallResizer;
using BepInEx.Unity.IL2CPP.Utils.Collections;

namespace BetterFG
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BasePlugin
    {
        internal static new ManualLogSource Log;

        public override void Load()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveNAudio;

            Log = base.Log;
            Log.LogInfo($"{BettrFGMeta.DisplayName} {BetterFGInfo.Version} [{BetterFGInfo.BuildHash}] loaded");

            SettingsService.Init();
            BetterFGConfig.Init();
            AudioService.Init();
            MenuMusicService.Init();

            try { TMPro.TMP_Settings.instance.m_warningsDisabled = true; } catch { }

            RegisterIl2CppTypes();

            // Gateway/auth has been causing crashes in some environments. Disable creating
            // the `BetterFG_Gateway` here and initialize the mod directly so core features
            // and UI are available even without remote auth.
            InitGameObjects(0);
            BetterFGStartupWindow.Show();
            BetterFGUpdateWindow.Show();
            var wheel = SideWheelManager.Create();
            SidewheelRegistry.RegisterAll(wheel);

            var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
            {
                try { new HarmonyLib.PatchClassProcessor(harmony, type).Patch(); }
                catch (Exception ex) { Log.LogError($"Harmony: Failed to patch {type.FullName}: {ex.Message}"); }
            }

            // FallGuysLib owns the shared game-state patch and re-raises it; we subscribe instead of
            // patching GameStateMachine.ReplaceCurrentState ourselves (one patch across all FGLib mods).
            FallGuysLib.Game.GameStateEvents.OnStateChanged += BetterFG.Patches.GameStates.GameStateDispatcher.OnStateChanged;
            FallGuysLib.Game.LevelEditorEvents.OnLevelEditorPlaytest += BetterFG.Tweaks.BfgTweak.RaiseLevelEditorPlaytest;
            FallGuysLib.Game.LevelEditorEvents.OnLevelEditorPlaytestEnd += BetterFG.Tweaks.BfgTweak.RaiseLevelEditorPlaytestEnd;

            // decode saved custom skin textures now, at load, so the first auto-reapply on menu enter
            // hits the cache instead of reading + decoding the whole png on that frame.
            BetterFG.UI.Tab.CustomSkinTextureTab.PrewarmTextureCache();
            BetterFG.Customization.Player.SkinApplicationService.PrewarmCustomTexCache();

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // our NAudio-driven menu/round music bypasses Unity's mixer, so AudioSettings.MuteAudioOnFocusLost
            // doesn't touch it — players hear our tracks keep playing when they alt-tab. pause both on focus loss.
            Application.add_focusChanged((Action<bool>)(focused =>
            {
                // honour the game's own "Mute Audio on Focus Lost" setting — Unity already does this
                // for FMOD/AudioSource based on it, but our NAudio output bypasses that, so mirror it.
                bool mute = false;
                try { mute = GlobalGameStateClient.Instance?.PlayerProfile?.AudioSettings?.MuteAudioOnFocusLost ?? false; } catch { }
                if (!mute) return;
                if (focused) MenuMusicService.Resume();
                else MenuMusicService.Pause();
                BetterFG.Features.UnityRound.RoundMusicService.SetPaused(!focused);
            }));
        }

        private static Assembly ResolveNAudio(object _, ResolveEventArgs args)
        {
            if (!args.Name.StartsWith("NAudio")) return null;
            string libs = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Libs");
            string path = Path.Combine(libs, new AssemblyName(args.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }

        private static void RegisterIl2CppTypes()
        {
            // services
            ClassInjector.RegisterTypeInIl2Cpp<SkinCatalogService>();
            ClassInjector.RegisterTypeInIl2Cpp<SkinLoaderService>();
            ClassInjector.RegisterTypeInIl2Cpp<SkinApplicationService>();
            ClassInjector.RegisterTypeInIl2Cpp<BeanMonitorService>();
            ClassInjector.RegisterTypeInIl2Cpp<PlayerScaleService>();
            ClassInjector.RegisterTypeInIl2Cpp<CostumePollerComponent>();
            ClassInjector.RegisterTypeInIl2Cpp<BoneSyncComponent>();
            ClassInjector.RegisterTypeInIl2Cpp<MenuCustomizationApplication>();
            ClassInjector.RegisterTypeInIl2Cpp<RepoRegistry>();
            ClassInjector.RegisterTypeInIl2Cpp<AssetManager>();
            ClassInjector.RegisterTypeInIl2Cpp<NetworkClient>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFGUnityRounds>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.UnityRound.Editor.CreativeRoundMemory>();

            ClassInjector.RegisterTypeInIl2Cpp<CustomEndzoneTrigger>();

            // ui
            ClassInjector.RegisterTypeInIl2Cpp<BetterFGUIMan>();
            ClassInjector.RegisterTypeInIl2Cpp<ControllerManager>();
            ClassInjector.RegisterTypeInIl2Cpp<TabHoverTint>();
            ClassInjector.RegisterTypeInIl2Cpp<Tooltip>();
            ClassInjector.RegisterTypeInIl2Cpp<TooltipTrigger>();
            ClassInjector.RegisterTypeInIl2Cpp<GradientImage>();
            ClassInjector.RegisterTypeInIl2Cpp<MovePulseContinuous>();
            ClassInjector.RegisterTypeInIl2Cpp<AlphaPulseContinuousFade>();
            ClassInjector.RegisterTypeInIl2Cpp<MoveScrollUvRaw>();
            ClassInjector.RegisterTypeInIl2Cpp<DragHandler>();
            ClassInjector.RegisterTypeInIl2Cpp<LinkHover>();
            ClassInjector.RegisterTypeInIl2Cpp<SideWheelManager>();
            ClassInjector.RegisterTypeInIl2Cpp<RingGraphic>();
            ClassInjector.RegisterTypeInIl2Cpp<AutoFetchTrigger>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Patches.ShowSelectorBgApplier>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.QualificationTime.PBPopupDestroyWatcher>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Nametag.GifAnimator>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Tweaks.BlinkDriverComponent>();

            // tabs
            ClassInjector.RegisterTypeInIl2Cpp<CustomizationTab>();
            ClassInjector.RegisterTypeInIl2Cpp<MenuTab>();
            ClassInjector.RegisterTypeInIl2Cpp<NametagTab>();
            ClassInjector.RegisterTypeInIl2Cpp<UITab>();
            ClassInjector.RegisterTypeInIl2Cpp<EmoticonsPhrasesTab>();
            ClassInjector.RegisterTypeInIl2Cpp<FeaturesTab>();
            ClassInjector.RegisterTypeInIl2Cpp<CustomSkinTextureTab>();
            ClassInjector.RegisterTypeInIl2Cpp<AllCosmeticsTab>();
            ClassInjector.RegisterTypeInIl2Cpp<CreativeTab>();
            ClassInjector.RegisterTypeInIl2Cpp<PersonalBestTab>();

            // windows
            ClassInjector.RegisterTypeInIl2Cpp<BetterFGWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFGStartupWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFGInfoWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFGUpdateWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<AudioSettingsWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<MenuMusicWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<PlayerdetailsWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<PlayerScaleWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<ItemConfigWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<LobbyAutokickConfigWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<UnityRoundLoaderWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<ObstacleTextureWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.UI.Windows.Creative.BatchEditWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<BetterFG.UI.Windows.Creative.CreativeSelectionWatcher>();
            ClassInjector.RegisterTypeInIl2Cpp<SkinTextureCostumeWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<WindowDragHandle>();
            ClassInjector.RegisterTypeInIl2Cpp<TweaksWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<OptionsWindow>();
            ClassInjector.RegisterTypeInIl2Cpp<PresetsWindow>();
#if PROFILES
            ClassInjector.RegisterTypeInIl2Cpp<ProfilesWindow>();
#endif
            ClassInjector.RegisterTypeInIl2Cpp<KeybindRecorder>();



            // tweaks
            ClassInjector.RegisterTypeInIl2Cpp<BfgTweak>(); //ts first
            ClassInjector.RegisterTypeInIl2Cpp<ChangeFallGuysLogo>();
            ClassInjector.RegisterTypeInIl2Cpp<ChangeSplashScreenTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<HideCreatorCodeTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<LobbyAutokickTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<SpectatorMusicTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<MuteSocialSoundsTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<BringBackFallGuyNoisesTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<StripSizeTagsTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<FallFeedQualTimeTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<MaxFallFeedTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<Background3dTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<MatchmakingQueueCountTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<AlwaysShowTimerTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<SkipVictoryTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<SkipRewardsTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<LobbyShowSearchTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<DisableCameraAssistTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<CustomCursorTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<DisableAntiAfkTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<StartupTitleScreenTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<ShowServerInfoTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<ShowTilePlaysTweak>();
            //ClassInjector.RegisterTypeInIl2Cpp<MultiShowSelectTweak>(); // WIP, shelved (see TweakRegistry)
            ClassInjector.RegisterTypeInIl2Cpp<ShadowDistanceTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<ShadowCustomResolutionTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<ShadowCascadeSplitTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<LeaveOnLoadingScreenTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<ImmediateRespawnTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<DynamicQualScreenTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<InstantLandingIndicatorTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<KeepNametagsTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<LivelyFallGuysTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<LevelDescriptionOnPauseTweak>();
            ClassInjector.RegisterTypeInIl2Cpp<CreativeTypeValueTweak>();
            //ClassInjector.RegisterTypeInIl2Cpp<BetterFG.Features.WinStreakDebug.WinStreakDebugService>();
        }

        private static void InitGameObjects(ulong seed)
        {
            Spawn<AssetManager>("BetterFG_AssetManager", persist: true);
            Spawn<NetworkClient>("BetterFG_NetworkClient", persist: true);
            Spawn<PlayerScaleService>("BetterFG_PlayerScale", persist: true);
            Spawn<BetterFGUnityRounds>("BetterFG_UnityRounds", persist: true);
            Spawn<BetterFG.Features.UnityRound.Editor.CreativeRoundMemory>("BetterFG_CreativeRoundMemory", persist: true);
            Spawn<BetterFG.UI.Windows.Creative.CreativeSelectionWatcher>("BetterFG_CreativeSelectionWatcher", persist: true);

            Spawn<BeanMonitorService>("BetterFG_BeanMonitor", persist: false);

            //Spawn<BetterFG.Features.WinStreakDebug.WinStreakDebugService>("BetterFG_WinStreakDebug", persist: true);

            TweakRegistry.Init();

            var svcGo = new GameObject("BetterFG_CustomizationServices");
            svcGo.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(svcGo);

            var repoRegistry = svcGo.AddComponent<RepoRegistry>();
            var catalogService = svcGo.AddComponent<SkinCatalogService>();
            var applicationService = svcGo.AddComponent<SkinApplicationService>();
            var loaderService = svcGo.AddComponent<SkinLoaderService>();
            loaderService.skinApp = applicationService;
            var plinthApp = svcGo.AddComponent<MenuCustomizationApplication>();

            CustomizationServices.Provide(repoRegistry, catalogService, applicationService, loaderService, plinthApp);

            PhraseInjectionService.SetEntries(PhraseSettingsService.Load());
            EmoticonInjectionService.SetEntries(EmoticonSettingsService.Load());
            EmoteInjectionService.SetEntries(EmoteSettingsService.Load());

            applicationService.StartCoroutine(applicationService.EarlyRestoreCoroutine().WrapToIl2Cpp());

            // fallback: also restore after the first catalog fetch
            catalogService.OnFetchCompleted += () =>
            {
                applicationService.RestoreFromSettings(catalogService.AvailableSkins, loaderService, plinthApp);
            };

            if (BetterFGConfig.AutoFetchOnStartup.Value)
            {
                var triggerGo = new GameObject("BetterFG_AutoFetchTrigger");
                triggerGo.hideFlags = HideFlags.HideAndDontSave;
                var trigger = triggerGo.AddComponent<AutoFetchTrigger>();
                trigger.CatalogService = catalogService;
                trigger.RepoRegistry = repoRegistry;
            }

            BetterFGTabRegistry.Register("Customization", () => MakeTabGo<CustomizationTab>("BetterFG_CustomizationTab"));
            BetterFGTabRegistry.Register("Menu", () => MakeTabGo<MenuTab>("BetterFG_MenuTab"), "Main Menu");
            BetterFGTabRegistry.Register("UI", () => MakeTabGo<UITab>("BetterFG_UITab"));
            BetterFGTabRegistry.Register("Nametag", () => MakeTabGo<NametagTab>("BetterFG_NametagTab"));
            BetterFGTabRegistry.Register("Phrases", () => MakeTabGo<EmoticonsPhrasesTab>("BetterFG_EPTab"), "Social");
            BetterFGTabRegistry.Register("Features", () => MakeTabGo<FeaturesTab>("BetterFG_FeaturesTab"));
            BetterFGTabRegistry.Register("Skin Texture", () => MakeTabGo<CustomSkinTextureTab>("BetterFG_SkinTexTab"));
            BetterFGTabRegistry.Register("All Cosmetics", () => MakeTabGo<AllCosmeticsTab>("BetterFG_AllCosmeticsTab"));
            BetterFGTabRegistry.Register("Creative", () => MakeTabGo<CreativeTab>("BetterFG_CreativeTab"));
            BetterFGTabRegistry.Register("Personal Bests", () => MakeTabGo<PersonalBestTab>("BetterFG_PersonalBestTab"));

            var uiManGo = new GameObject("BetterFG_UI");
            uiManGo.hideFlags = HideFlags.HideAndDontSave;
            var uiMan = uiManGo.AddComponent<BetterFGUIMan>();

            for (int i = 0; i < BetterFGUIMan.MAX_SLOTS && i < BetterFGTabRegistry.All.Count; i++)
                uiMan.RegisterTab(BetterFGTabRegistry.All[i].Factory());

            uiMan.LoadSavedSlots();

            ControllerManager.Create();
        }

        private static T Spawn<T>(string name, bool persist) where T : MonoBehaviour
        {
            var go = new GameObject(name);
            go.hideFlags = HideFlags.HideAndDontSave;
            if (persist) UnityEngine.Object.DontDestroyOnLoad(go);
            return go.AddComponent<T>();
        }

        private static T MakeTabGo<T>(string name) where T : MonoBehaviour
        {
            var go = new GameObject(name);
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go.AddComponent<T>();
        }
    }

    // for ugc only
    public static class CustomizationServices
    {
        public static RepoRegistry RepoRegistry { get; private set; }
        public static SkinCatalogService CatalogService { get; private set; }
        public static SkinApplicationService ApplicationService { get; private set; }
        public static SkinLoaderService LoaderService { get; private set; }
        public static MenuCustomizationApplication PlinthApp { get; private set; }

        public static void Provide(
            RepoRegistry repo,
            SkinCatalogService catalog,
            SkinApplicationService app,
            SkinLoaderService loader,
            MenuCustomizationApplication plinth)
        {
            RepoRegistry = repo;
            CatalogService = catalog;
            ApplicationService = app;
            LoaderService = loader;
            PlinthApp = plinth;
        }
    }

    // Delayed auto-fetch for repos and UGC on startup
    public class AutoFetchTrigger : MonoBehaviour
    {
        public AutoFetchTrigger(IntPtr ptr) : base(ptr) { }

        public SkinCatalogService CatalogService;
        public RepoRegistry RepoRegistry;

        void Start()
        {
            Invoke("DoFetch", 5f);
            Invoke("DoFetchPreload", 10f);
        }

        private void DoFetch()
        {
            var active = RepoRegistry?.Active;
            if (active != null) CatalogService?.FetchSkins(active);
            Destroy(gameObject);
        }
    }
}
