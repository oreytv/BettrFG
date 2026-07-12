using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using FG.Common.Character;
using FG.Common.Character.MotorSystem;
using FallGuysLib.Players;
using FallGuysLib.Round;
using FallGuysLib.UI;
using FG.Common;
using FGClient;
using UnityEngine;
using PlayerUtils = FallGuysLib.Players.PlayerUtils;
using Levels.Progression;
using BetterFG.Services;
using BetterFG.Features.UnityRound.Editor;

namespace BetterFG.Tweaks
{
    public class ImmediateRespawnTweak : BfgTweak
    {
        public ImmediateRespawnTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "immediate_respawn";
        public override string TweakLabel => "Immediate Respawn Button";
        public override bool DefaultEnabled => true;

        public static ImmediateRespawnTweak Instance { get; private set; }
        void Awake() => Instance = this;

        private static NavPromptHandle _prompt;
        private static bool _respawning;

        public override void EnableTweak()
        {
            var gs = GlobalGameStateClient.Instance?._gameStateMachine?.CurrentState;
            if (gs?.TryCast<StateGameInProgress>() != null) OnRoundStart();
        }
        public override void DisableTweak()
        {
            DestroyPrompt();
            _respawning = false;
        }

        public override void OnRoundStart()
        {
            var levelName = GlobalGameStateClient.Instance?.GameStateView?.CurrentGameLevelName;
            if (!(levelName?.StartsWith("ugc-") ?? false)) { DestroyPrompt(); _respawning = false; return; }
            SpawnPrompt();
        }

        public override void OnLevelEditorPlaytest() => SpawnPrompt();

        public override void OnLevelEditorPlaytestEnd()
        {
            DestroyPrompt();
            _respawning = false;
        }

        private void SpawnPrompt()
        {
            DestroyPrompt();
            _respawning = false;
            if (!IsEnabled
                || GameRulesUtils.IsSurvivalRound()
                || PlayerUtils.GetOtherPlayerIds().Count > 0)
                return;
            _prompt = NavPromptCore.From(NavPrompt.Random)
                .WithLabel("Respawn", "bfg_respawn_label")
                .AnchoredAt(NavPromptAnchor.BottomRight)
                .OnOwnCanvas()
                .PollActions(RewiredConsts.Action.Customiser_Random)
                .AllowWhileUnfocused()
                .SpawnOn(null);
        }

        public override void OnSpectatorMode() => OnBannerShown();

        public override void OnBannerShown()
        {
            DestroyPrompt();
            _respawning = false;
        }

        public override void OnStateChanged(GameStateMachine.IGameState newState)
        {
            if (newState == null || newState.TryCast<StateGameInProgress>() != null) return;
            DestroyPrompt();
            _respawning = false;
        }

        void Update()
        {
            if (!IsEnabled || _respawning || _prompt == null || !_prompt.IsPressed()) return;
            DoRespawn();
        }

        private static void DestroyPrompt()
        {
            _prompt?.Destroy();
            _prompt = null;
        }

        private static void DoRespawn()
        {
            if (UnityRoundLoader.InLevelEditor) { DoEditorRespawn(); return; }

            var localFgcc = PlayerUtils.PlayerController;
            if (localFgcc == null) return;

            uint localNetId = PlayerUtils.GetLocalNetObjectId();
            if (localNetId == 0) return;

            var checkpointMgr = UnityEngine.Object.FindObjectOfType<CheckpointManager>();
            if (checkpointMgr == null) return;

            var cpMap = checkpointMgr.NetIDToCheckpointMap;
            if (cpMap == null || !cpMap.TryGetValue((MPGNetID)localNetId, out uint cpId)) return;

            var zones = checkpointMgr._checkpointZones;
            CheckpointZonePositions targetZone = null;
            for (int i = 0; i < zones.Count; i++)
            {
                var czp = zones[i]?.TryCast<CheckpointZonePositions>();
                if (czp != null && czp.uniqueId == cpId) { targetZone = czp; break; }
            }
            if (targetZone == null) return;

            var spawnTransform = targetZone.GetRandomTransform();
            if (spawnTransform == null) return;

            TeleportTo(localFgcc, spawnTransform.position);
        }

        // editor playtest: LevelEditorManager knows the current respawn point (current checkpoint, or the level
        // start if none reached yet) — same source the kill-zones use. local bean is LevelEditorManager's player.
        private static void DoEditorRespawn()
        {
            var mgr = LevelEditorManager.Instance;
            if (mgr == null) return;

            var bean = BeanMonitorService.LocalPlayerBean;
            if (bean == null) return;

            var localFgcc = bean.GetComponent<FallGuysCharacterController>();
            if (localFgcc == null) return;

            if (!mgr.TryGetRespawnTransform(out Vector3 position, out _)) return;

            TeleportTo(localFgcc, position);
        }

        private static void TeleportTo(FallGuysCharacterController localFgcc, Vector3 position)
        {
            var teleport = localFgcc.TeleportMotorFunction;
            if (teleport == null) return;

            teleport.TeleportPosition = position;

            var states = teleport.OriginalStates;
            if (states == null || states.Count < 2) return;

            var activeState = states[1].TryCast<MotorFunctionTeleportStateActive>();
            if (activeState == null) return;

            _respawning = true;
            activeState.Begin(-1);
            Instance.StartCoroutine(FinishTeleport(activeState).WrapToIl2Cpp());
        }

        private static IEnumerator FinishTeleport(MotorFunctionTeleportStateActive activeState)
        {
            yield return new WaitForSeconds(0.5f);
            activeState.End(-1);
            _respawning = false;
        }
    }
}
