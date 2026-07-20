using UnityEngine;

namespace BetterFG.Patches
{
    // fixes for creative-editor UI elements the game anchors badly, so they drift at non-default UI
    // scales. called from the matching DoFadeIn branches in ScreenBackgroundPatches (no new patch).
    internal static class CreativeUIPatches
    {
        // Library Prompt is stock-anchored top-centre with a fixed -887 y push, so it rides up the
        // screen at smaller UI scales. re-anchor bottom-centre so it tracks the bottom edge instead.
        // safeArea = the nav screen's "Safe Area" transform.
        public static void FixLibraryPrompt(Transform safeArea)
        {
            var lib = safeArea.Find("Library Prompt");
            if (lib == null) return;
            var rt = lib.TryCast<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 400f);
        }

        // the parameter menu box is stock-anchored top-left with a fixed +1630 x push, so it drifts off
        // the ObjectInfo panel at other UI scales. re-anchor top-right to sit under Panel_Full.
        // vmRoot = the LevelEditorParameterMenuViewModel's transform.
        public static void FixParametersMenu(Transform vmRoot)
        {
            foreach (var t in vmRoot.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == "UI_LevelEditorParametersMenuBG")
                {
                    var rt = t.TryCast<RectTransform>();
                    if (rt != null)
                    {
                        rt.anchorMin = new Vector2(1f, 1f);
                        rt.anchorMax = new Vector2(1f, 1f);
                        rt.anchoredPosition = new Vector2(-290f, -270f);
                    }
                    return;
                }
        }
    }
}
