using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.UI.SideWheel;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Components
{
    // "look here" flash for any control in any tab/window/subtab. give it the graphics you want to draw the
    // eye to; each one glows blue, holds solid for HOLD seconds, then fades back to its own colour over FADE.
    // driven off the sidewheel's live coroutine host so nothing new needs il2cpp registration.
    public static class alpha_highlightPulse
    {
        private static readonly Color BLUE = new Color(0.25f, 0.55f, 1f, 1f);
        private const float HOLD = 2f;
        private const float FADE = 1f;

        // pulse specific Graphics (Image/Text/RawImage all derive from Graphic). null entries are skipped.
        public static void Flash(IEnumerable<Graphic> targets)
        {
            var host = SideWheelManager.Instance;
            if (host == null || targets == null) return;

            var list = new List<Graphic>();
            var original = new List<Color>();
            foreach (var g in targets)
            {
                if (g == null) continue;
                list.Add(g);
                original.Add(g.color);
            }
            if (list.Count == 0) return;

            host.StartCoroutine(Run(list, original).WrapToIl2Cpp());
        }

        // find each named child under root (deep, il2cpp-safe) and pulse whatever Graphic sits on it — for
        // an InputField the visible box is the Image on the field's own GameObject, so the name should be the
        // field/control root's object name.
        public static void FlashChildren(Transform root, params string[] names)
        {
            if (root == null || names == null) return;
            var found = new List<Graphic>();
            foreach (var name in names)
            {
                var t = FindDeep(root, name);
                if (t == null) { Debug.LogWarning($"highlight: couldn't find '{name}' to flash under {root.name}"); continue; }
                var g = t.GetComponent<Graphic>();
                if (g != null) found.Add(g);
            }
            Flash(found);
        }

        private static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == name) return t;
            return null;
        }

        private static IEnumerator Run(List<Graphic> targets, List<Color> original)
        {
            for (int i = 0; i < targets.Count; i++)
                if (targets[i] != null) targets[i].color = BLUE;

            yield return new WaitForSeconds(HOLD);

            float t = 0f;
            while (t < FADE)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / FADE);
                for (int i = 0; i < targets.Count; i++)
                    if (targets[i] != null) targets[i].color = Color.Lerp(BLUE, original[i], k);
                yield return null;
            }

            for (int i = 0; i < targets.Count; i++)
                if (targets[i] != null) targets[i].color = original[i];
        }
    }
}
