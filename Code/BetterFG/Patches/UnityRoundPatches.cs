using System;
using System.Collections;
using BetterFG.Services;
using BetterFG.Features.UnityRound;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FG.Common.Character;
using FG.Common.Character.MotorSystem;
using FallGuysLib.Players;
using HarmonyLib;
using UnityEngine;
using static FG.Common.Character.MotorFunctionMantle;
using FG.Common;
using FGClient;
using PlayerUtils = FallGuysLib.Players.PlayerUtils;

namespace BetterFG.Patches.BettrFGRounds
{
    // Patches that apply during custom unity rounds.
    // all this grab/motor/mantle stuff should ONLY do anything when a unity round is actually live.
    internal static class UnityRoundGate
    {
        public static bool RoundLive => BetterFGUnityRounds.ActiveRound != null;
    }

    // stop the custom round song + un-pause the game's FMOD music when the round tears down.
    [HarmonyPatch(typeof(ClientGameManager), "Shutdown")]
    public class RoundMusicShutdownPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            BetterFG.Features.UnityRound.RoundMusicService.Stop();
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            BetterFG.Features.QualificationTime.FeatureQualificationTime.OnClientGameManagerShutdown();
        }
    }


    [HarmonyPatch(typeof(MotorFunctionGrabStateConfirm), "ShouldQuitDueToUnconfirmedGrab")]
    public class ShouldQuitDueToUnconfirmedGrab
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (!UnityRoundGate.RoundLive) return;
            __result = false;
        }
    }

    [HarmonyPatch(typeof(MotorFunctionMantle), "CheckForMantleTarget")]
    public class CheckForMantleTargetPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref MantleTargetFailed __result)
        {
            if (!UnityRoundGate.RoundLive) return;
            if (__result == MantleTargetFailed.ServerSyncFailed || __result == MantleTargetFailed.ServerTargetValidationFailed)
                __result = MantleTargetFailed.None;
        }
    }

    [HarmonyPatch(typeof(MotorFunctionGrab), "IsValidTarget")]
    public class IsValidTargetPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result, ref InvalidGrabTargetResult details)
        {
            if (!UnityRoundGate.RoundLive) return;
            if (details == InvalidGrabTargetResult.TargetDoesntHaveMPGNetObject)
            {
                details = InvalidGrabTargetResult.None;
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(MotorFunctionMantleStateGrab), "Begin", new[] { typeof(int) })]
    public class MantleStateGrabBeginPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MotorFunctionMantleStateGrab __instance)
        {
            if (!UnityRoundGate.RoundLive) return;
            try
            {
                var mantle = __instance._motorFunction;
                var climbUp = mantle?.OriginalStates?[2];
                if (climbUp == null) return;
                BeanMonitorService.Instance?.StartCoroutine(DoClimbUp(__instance, climbUp).WrapToIl2Cpp());
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"MantleClimbUp: {ex.Message}"); }
        }

        private static IEnumerator DoClimbUp(MotorFunctionMantleStateGrab instance, MotorFunctionState climbUp)
        {
            yield return new WaitForSeconds(0.4f);
            climbUp.Begin(-1);
            yield return new WaitForSeconds(1.4f);
            climbUp.End(1);
            instance._motorFunction.FirstStart();
        }
    }

    // gate these to a live unity round. off-round they were killing the game's own AbortGrab/
    // AbortMantling globally, so a respawn that had to cancel an in-progress grab/mantle before
    // teleporting never did -- the motor stayed stuck and the checkpoint teleport never applied.
    // that's the "respawn runs but doesn't move you" bug, and it hit plain official rounds too.
    // returning true lets the original run exactly like vanilla, which is what happens off-round.
    [HarmonyPatch(typeof(MotorFunctionMantle), "AbortMantling")]
    public class AbortMantlingPatch
    {
        [HarmonyPrefix]
        public static bool Prefix() => !UnityRoundGate.RoundLive;
    }

    [HarmonyPatch(typeof(MotorFunctionGrab), "AbortGrab")]
    public class AbortGrabPatch
    {
        [HarmonyPrefix]
        public static bool Prefix() => !UnityRoundGate.RoundLive;
    }

    [HarmonyPatch(typeof(MotorFunctionGrab), "ShouldApplyStateSnapshot")]
    public class GrabShouldApplyStateSnapshotPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MotorFunctionGrab __instance, ref bool __result)
        {
            if (!UnityRoundGate.RoundLive) return;
            if (__instance.IsInGrabState || __instance.IsPerformingGrabAction)
                __result = false;
        }
    }

    [HarmonyPatch(typeof(MotorFunctionMantle), "ShouldApplyStateSnapshot")]
    public class MantleShouldApplyStateSnapshotPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref bool __result)
        {
            if (!UnityRoundGate.RoundLive) return;
            __result = false;
        }
    }

    [HarmonyPatch(typeof(MotorFunctionMantle), "ApplyUrgentUnbufferedStateSnapshot")]
    public class MantleUrgentSnapshotPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            if (!UnityRoundGate.RoundLive) return true;
            __result = false;
            return false;
        }
    }
    

    // NOTE: spawnpoint patches commented out — we now use the level editor to place checkpoints,
    // so the mod no longer teleports beans to custom spawnpoints.
    /*
    [HarmonyPatch(typeof(MotorFunctionTeleportStateActive), "Begin", new[] { typeof(int) })]
    public class TeleportRespawnPatch
    {
        [HarmonyPrefix]
        public static void Prefix(MotorFunctionTeleportStateActive __instance)
        {
            try
            {
                if (BetterFGUnityRounds.ActiveSpawnpoints == null || BetterFGUnityRounds.ActiveSpawnpoints.Length == 0) return;

                uint localId = PlayerUtils.GetLocalPlayerId();
                if (localId == 0) return;

                var localObj = PlayerUtils.GetPlayerObject(localId);
                if (__instance.MotorAgent?.gameObject != localObj) return;

                var pos = BetterFGUnityRounds.GetRandomSpawnpointPos();
                if (pos == null) return;

                var teleportFunc = __instance.MotorAgent?.GetMotorFunction<MotorFunctionTeleport>();
                if (teleportFunc == null) return;
                teleportFunc.TeleportPosition = pos.Value;
                Plugin.Log.LogInfo($"teleport respawn -> {pos.Value}");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"teleport respawn: {ex.Message}"); }
        }
    }

    [HarmonyPatch(typeof(ClientGameManager), nameof(ClientGameManager.DoCharacterObjectSpawnPreparations))]
    internal static class SpawnTeleportPatch
    {
        [HarmonyPostfix]
        public static void Postfix(MPGNetObject pNetObject, bool isLocalPlayer)
        {
            if (!isLocalPlayer || pNetObject == null) return;

            var bean = pNetObject.gameObject;
            if (bean == null) return;
            BetterFGUnityRounds.TeleportBeanToSpawn(bean, "spawn");
        }
    }
    */
}
