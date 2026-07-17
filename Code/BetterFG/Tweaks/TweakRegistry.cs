using System.Collections.Generic;
using BetterFG.Tweaks;
using UnityEngine;

namespace BetterFG.Tweaks
{
    // category buckets. order here = order the sections show up in the window.
    public enum TweakCategory
    {
        Flow,
        Gameplay,
        Audio,
        Lighting,
        Utility,
        Misc,
    }

    public static class TweakRegistry
    {
        private static readonly List<BfgTweak> _tweaks = new List<BfgTweak>();
        // tweak -> which bucket it lives in. set when we Add() it below.
        private static readonly Dictionary<BfgTweak, TweakCategory> _cat = new Dictionary<BfgTweak, TweakCategory>();

        public static IReadOnlyList<BfgTweak> All => _tweaks;

        // the order sections render in. matches the enum order.
        public static readonly TweakCategory[] CategoryOrder =
        {
            TweakCategory.Flow,
            TweakCategory.Gameplay,
            TweakCategory.Audio,
            TweakCategory.Lighting,
            TweakCategory.Utility,
            TweakCategory.Misc,
        };

        // every tweak in a given bucket, in the order they were registered.
        public static IEnumerable<BfgTweak> InCategory(TweakCategory cat)
        {
            foreach (var t in _tweaks)
                if (_cat[t] == cat)
                    yield return t;
        }

        private static GameObject _host;

        public static void Init()
        {
            _host = new GameObject("BetterFG_Tweaks");
            Object.DontDestroyOnLoad(_host);
            _host.hideFlags = HideFlags.HideAndDontSave;

            // ── Flow ── stuff that changes how you move through menus / matches
            Add<SkipVictoryTweak>(TweakCategory.Flow);
            Add<SkipRewardsTweak>(TweakCategory.Flow);
            Add<LobbyShowSearchTweak>(TweakCategory.Flow);
            Add<DisableAntiAfkTweak>(TweakCategory.Flow);
            Add<StartupTitleScreenTweak>(TweakCategory.Flow);
            Add<LeaveOnLoadingScreenTweak>(TweakCategory.Flow);
            Add<DynamicQualScreenTweak>(TweakCategory.Flow);

            // ── Gameplay ──
            //Add<InstantLandingIndicatorTweak>(TweakCategory.Gameplay);
            Add<DisableCameraAssistTweak>(TweakCategory.Gameplay);
            Add<ImmediateRespawnTweak>(TweakCategory.Gameplay);

            // ── Audio ──
            Add<SpectatorMusicTweak>(TweakCategory.Audio);
            Add<MuteSocialSoundsTweak>(TweakCategory.Audio);
            Add<BringBackFallGuyNoisesTweak>(TweakCategory.Audio);

            // ── Lighting ──
            Add<ShadowDistanceTweak>(TweakCategory.Lighting);
            Add<ShadowCustomResolutionTweak>(TweakCategory.Lighting);
            Add<ShadowCascadeSplitTweak>(TweakCategory.Lighting);

            // ── Utility ──
            Add<LobbyAutokickTweak>(TweakCategory.Utility);
            Add<MatchmakingQueueCountTweak>(TweakCategory.Utility);
            Add<CustomCursorTweak>(TweakCategory.Utility);
            Add<HideCreatorCodeTweak>(TweakCategory.Utility);
            Add<StripSizeTagsTweak>(TweakCategory.Utility);
            Add<FallFeedQualTimeTweak>(TweakCategory.Utility);
            Add<MaxFallFeedTweak>(TweakCategory.Utility);
            Add<ShowServerInfoTweak>(TweakCategory.Utility);
            Add<ShowTilePlaysTweak>(TweakCategory.Utility);
            Add<AlwaysShowTimerTweak>(TweakCategory.Utility);
            Add<KeepNametagsTweak>(TweakCategory.Utility);
            Add<LevelDescriptionOnPauseTweak>(TweakCategory.Utility);
            Add<CreativeTypeValueTweak>(TweakCategory.Utility);
            Add<NotifyRoundStartTweak>(TweakCategory.Utility);
            //Add<MultiShowSelectTweak>(TweakCategory.Utility); // WIP — launch confirms the highlighted show, not the pinned set; shelved

            // ── Misc ── cosmetic / everything else
            Add<ChangeFallGuysLogo>(TweakCategory.Misc);
            Add<ChangeSplashScreenTweak>(TweakCategory.Misc);
            Add<LivelyFallGuysTweak>(TweakCategory.Misc);
            // Add<Background3dTweak>(TweakCategory.Misc); // 2D To 3D Background � disabled until custom bundles are available
            //Add<AlwaysShowTimerTweak>(TweakCategory.Misc);
            //Add<RefocusGame>(TweakCategory.Misc);
        }

        private static void Add<T>(TweakCategory cat) where T : BfgTweak
        {
            var t = _host.AddComponent<T>();
            _tweaks.Add(t);
            _cat[t] = cat;
        }
    }
}
