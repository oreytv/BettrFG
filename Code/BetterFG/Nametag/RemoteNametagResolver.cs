using FGClient;
using UnityEngine;
using TMPro;

namespace BetterFG.Nametag
{
    /// <summary>
    /// Maps a round Fall Guy to its nametag through the game's HUD bookkeeping.
    /// </summary>
    public static class RemoteNametagResolver
    {
        public static PlayerInfoDisplay TryGetDisplayForBean(GameObject bean)
        {
            if (bean == null) return null;
            var fgcc = bean.GetComponent<FallGuysCharacterController>();
            if (fgcc == null) return null;

            var huds = Object.FindObjectsOfType<PlayerInfoHUDBase>(true);
            if (huds == null || huds.Length == 0) return null;

            for (int h = 0; h < huds.Length; h++)
            {
                var hud = huds[h];
                if (hud == null) continue;

                var spawned = hud._spawnedInfoObjects;
                if (spawned == null) continue;

                int n = spawned.Count;
                for (int i = 0; i < n; i++)
                {
                    var row = spawned[i];
                    if (row.fgcc != fgcc) continue;
                    return row.playerInfo;
                }
            }

            return null;
        }

        public static TMP_Text TryGetNameTextForBean(GameObject bean)
        {
            var display = TryGetDisplayForBean(bean);
            if (display == null) return null;
            return NametagIconApplicator.TryGetNameText(display);
        }
    }
}
