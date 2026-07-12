using System;
using System.Collections;
using FG.Common;
using FGClient;
using UnityEngine;
using BetterFG.Services;
using BepInEx.Unity.IL2CPP.Utils.Collections;

namespace BetterFG.Tweaks
{
    public class DynamicQualScreenTweak : BfgTweak
    {
        public DynamicQualScreenTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "dynamic_qual_screen";
        public override string TweakLabel => "Dynamic Qual Screen";
        public override bool DefaultEnabled => false;

        public static DynamicQualScreenTweak Instance { get; private set; }
        void Awake() => Instance = this;

        internal static void OnLocalPlayerDisplayed(CellBehaviour cell)
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled) return;
            inst.StartCoroutine(RunQualCameraSequence(cell).WrapToIl2Cpp());
        }

        private static IEnumerator RunQualCameraSequence(CellBehaviour cell)
        {
            var camerasRoot = GameObject.Find("----------------CAMERAS");
            if (camerasRoot == null) yield break;

            Camera mainCam = null;
            for (int i = 0; i < camerasRoot.transform.childCount; i++)
            {
                var child = camerasRoot.transform.GetChild(i);
                if (child.name == "Main Camera")
                {
                    child.gameObject.SetActive(true);
                    mainCam = child.GetComponent<Camera>();
                }
                else
                {
                    child.gameObject.SetActive(false);
                }
            }

            if (mainCam == null) yield break;

            var cellPos = cell.transform.position;
            var targetPos = new Vector3(cellPos.x, cellPos.y, cellPos.z - 10f);
            var originalPos = mainCam.transform.position;
            var originalRot = mainCam.transform.rotation;

            mainCam.transform.position = targetPos;
            mainCam.transform.LookAt(cellPos);

            yield return new WaitForSeconds(1f);

            float elapsed = 0f;
            var startPos = mainCam.transform.position;
            var startRot = mainCam.transform.rotation;
            while (elapsed < 2f)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = 1f - Mathf.Pow(1f - Mathf.Clamp01(elapsed / 2f), 3f);
                mainCam.transform.position = Vector3.Lerp(startPos, originalPos, t);
                mainCam.transform.rotation = Quaternion.Slerp(startRot, originalRot, t);
                yield return null;
            }

            mainCam.transform.position = originalPos;
            mainCam.transform.rotation = originalRot;

            for (int i = 0; i < camerasRoot.transform.childCount; i++)
                camerasRoot.transform.GetChild(i).gameObject.SetActive(true);
        }
    }
}
