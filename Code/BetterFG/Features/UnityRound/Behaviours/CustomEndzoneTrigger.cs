using BepInEx.Unity.IL2CPP.Utils.Collections;
using FallGuysLib.Camera;
using FallGuysLib.Players;
using FG.Common.Character;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace BetterFG.Features.UnityRound.Behaviours
{
    public class CustomEndzoneTrigger : MonoBehaviour
    {
        private bool _triggered;

        private void OnTriggerEnter(Collider other)
        {
            if (_triggered) return;
            if (!IsLocalPlayer(other.gameObject)) return;
            _triggered = true;
            StartCoroutine(DoQualify().WrapToIl2Cpp());
        }

        private static bool IsLocalPlayer(GameObject go)
        {
            try
            {
                uint localId = PlayerUtils.GetLocalPlayerId();
                if (localId == 0) return false;
                var localObj = PlayerUtils.GetPlayerObject(localId);
                return localObj != null && (go == localObj || go.transform.IsChildOf(localObj.transform));
            }
            catch { return false; }
        }

        private IEnumerator DoQualify()
        {
            var cccc = BetterFGUnityRounds.CcccTransform;
            if (cccc == null)
            {
                Plugin.Log.LogWarning("CustomEndzoneTrigger: CCCC not set, skipping qualify sequence");
                yield break;
            }

            yield return new WaitForSeconds(0.16f);

            try { CameraUtils.GetGameplayCameras()?.SetActive(false); } catch { }

            yield return null; // next frame

            FallGuysCharacterController fgcc = null;
            try
            {
                uint localId = PlayerUtils.GetLocalPlayerId();
                fgcc = PlayerUtils.GetPlayerController(localId);
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"CustomEndzoneTrigger: teleport failed: {ex.Message}"); }

            if (fgcc != null)
                yield return PlayerUtils.ForceTeleport(fgcc, cccc).WrapToIl2Cpp();
        }
    }
}
