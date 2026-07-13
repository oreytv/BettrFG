using System.Collections.Generic;
using BetterFG;
using FG.Common;
using FallGuysLib.Players;
using UnityEngine;

namespace BetterFG.Utilities
{
    /// <summary>
    /// Resolves network identity for Fall Guy roots and lists remote beans in a stable order.
    /// </summary>
    public static class BeanNetworkUtil
    {
        public static MPGNetObject TryGetMpgNetObject(GameObject bean)
        {
            if (bean == null) return null;
            var n = bean.GetComponent<MPGNetObject>();
            if (n != null) return n;
            n = bean.GetComponentInChildren<MPGNetObject>(true);
            if (n != null) return n;
            return bean.GetComponentInParent<MPGNetObject>();
        }

        public static List<GameObject> GetRemotePlayerBeansSorted(GameObject localPlayerBean)
        {
            var result = new List<GameObject>();
            try
            {
                var cpm = PlayerUtils.GetClientPlayerManager();
                if (cpm?._playerIdIndex == null) return result;

                var entries = new List<(uint playerId, GameObject go)>();
                foreach (var kvp in cpm._playerIdIndex)
                {
                    var go = kvp.Value?.fgcc?.gameObject;
                    if (go == null || go == localPlayerBean) continue;
                    entries.Add((kvp.Key, go));
                }

                entries.Sort((a, b) => a.playerId.CompareTo(b.playerId));
                foreach (var e in entries)
                    result.Add(e.go);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError("BeanNetworkUtil: GetRemotePlayerBeansSorted: " + ex.Message);
            }

            return result;
        }

        public static string TryGetPlayerKeyForBean(GameObject bean)
        {
            if (bean == null) return null;
            try
            {
                var cpm = PlayerUtils.GetClientPlayerManager();
                if (cpm?._playerIdIndex == null) return null;

                foreach (var kvp in cpm._playerIdIndex)
                {
                    var data = kvp.Value;
                    if (data == null) continue;
                    var go = data.fgcc != null ? data.fgcc.gameObject : null;
                    if (go != bean) continue;
                    return data.playerKey ?? "";
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError("BeanNetworkUtil: TryGetPlayerKeyForBean: " + ex.Message);
            }

            return null;
        }
    }
}
