using FGClient;
using MPG.Utility;
using System;
using UnityEngine;

namespace BetterFG.Utilities
{
    public class PlayerInformation
    {
        public static FallGuysCharacterController GetPlayerFGCCByName(string name)
        {
            if (GlobalGameStateClient.Instance == null)
            {
                Plugin.Log.LogInfo("Instance null");
                return null;
            }

            var gsv = GlobalGameStateClient.Instance.GameStateView;
            if (gsv == null)
            {
                Plugin.Log.LogInfo("GameStateView null");
                return null;
            }

            if (!gsv.GetLiveClientGameManager(out var cgm) || cgm == null)
            {
                Plugin.Log.LogInfo("cgm null");
                return null;
            }

            if (cgm._clientPlayerManager == null)
            {
                Plugin.Log.LogInfo("Player manager null");
                return null;
            }

            if (cgm._clientPlayerManager._playerNetIdIndex == null)
            {
                Plugin.Log.LogInfo("PlayerNetIdIndex null");
                return null;
            }

            foreach (var data in cgm._clientPlayerManager._playerNetIdIndex)
            {
                if (data.value == null)
                {
                    Plugin.Log.LogInfo($"Null player data for key {data.key}");
                    continue;
                }

                if (!string.IsNullOrEmpty(data.value.playerKey) &&
                    data.value.playerKey.Contains(name, StringComparison.CurrentCultureIgnoreCase))
                {
                    var netObj = cgm.GetNetObjectByID(data.key);
                    if (netObj == null)
                    {
                        Plugin.Log.LogInfo($"NetObject null for key {data.key}");
                        continue;
                    }

                    if (netObj.FGCharacterController == null)
                    {
                        Plugin.Log.LogInfo($"FGCharacterController null for key {data.key}");
                        continue;
                    }

                    return netObj.FGCharacterController;
                }
            }

            Plugin.Log.LogInfo($"No player found matching: {name}");
            return null;
        }

        // New helper functions based on what we learned

        /// <summary>
        /// Gets the ClientPlayerManager from the game
        /// </summary>
        public static ClientPlayerManager GetClientPlayerManager()
        {
            try
            {
                ClientGameManager clientGameManager;
                if (SingletonBehaviour<GlobalGameStateClient>.Instance.GameStateView.GetLiveClientGameManager(out clientGameManager))
                {
                    return clientGameManager._clientPlayerManager;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("GetClientPlayerManager failed: {ex}");
            }
            return null;
        }


        /// <summary>
        /// Gets the local player's ID
        /// </summary>
        public static uint GetLocalPlayerId()
        {
            try
            {
                var clientPlayerManager = GetClientPlayerManager();
                if (clientPlayerManager?._playerIdIndex == null)
                    return 0;

                foreach (var kvp in clientPlayerManager._playerIdIndex)
                {
                    if (kvp.Value?.fgcc != null && kvp.Value.fgcc.IsLocalPlayer)
                    {
                        return kvp.Key;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("GetLocalPlayerId failed: {ex}");
            }
            return 0;
        }

        /// <summary>
        /// Gets the local player's NetworkPlayerDataClient
        /// </summary>
        public static NetworkPlayerDataClient GetLocalPlayerData()
        {
            try
            {
                var clientPlayerManager = GetClientPlayerManager();
                if (clientPlayerManager?._playerIdIndex == null)
                    return null;

                foreach (var kvp in clientPlayerManager._playerIdIndex)
                {
                    if (kvp.Value?.fgcc != null && kvp.Value.fgcc.IsLocalPlayer)
                    {
                        return kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("GetLocalPlayerData failed: {ex}");
            }
            return null;
        }

        /// <summary>
        /// Gets the local player's FallGuysCharacterController directly
        /// </summary>
        public static FallGuysCharacterController GetLocalPlayerFGCC()
        {
            try
            {
                var clientPlayerManager = GetClientPlayerManager();
                if (clientPlayerManager?._playerIdIndex == null)
                    return null;

                foreach (var kvp in clientPlayerManager._playerIdIndex)
                {
                    if (kvp.Value?.fgcc != null && kvp.Value.fgcc.IsLocalPlayer)
                    {
                        return kvp.Value.fgcc;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"GetLocalPlayerFGCC failed: {ex}");
            }
            return null;
        }

    }
}
