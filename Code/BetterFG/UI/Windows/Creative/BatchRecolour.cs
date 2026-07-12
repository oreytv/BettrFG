using System.Collections.Generic;
using UnityEngine;
using LevelEditor;

namespace BetterFG.UI.Windows.Creative
{
    // Recolour ops over the level-editor multi-selection. Objects carry colour on
    // LevelEditorColourChangerParameter (RGB, SetColour). Lookup + snapshot live in BatchTargets.
    // "set to colour" applies a flat colour, "modify" adjusts each object's own colour (brightness/
    // contrast/hue/saturation) — both drive a live preview session that the window commits as one
    // undo entry on apply / subtab / mode switch / close.
    public static class BatchRecolour
    {
        public static int SelectionCount()
        {
            var sel = LevelEditorMultiSelectionHandler.Selection();
            return sel != null ? sel.Count : 0;
        }

        // "modify" mode operates on each RGB object's OWN original colour, so brightness/contrast/hue/sat
        // are relative adjustments — a live preview session snapshots originals once (BatchPreview), then
        // ModifyPreview recomputes from those each slider move so nothing compounds. brightness/contrast
        // are signed offsets around 0 (0 = no change); hue is degrees; saturation is a signed offset.
        public static Color Modify(Color c, float brightness, float contrast, float hue, float saturation)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            h = Mathf.Repeat(h + hue / 360f, 1f);
            s = Mathf.Clamp01(s + saturation);
            v = Mathf.Clamp01(v + brightness);
            var outc = Color.HSVToRGB(h, s, v);
            // contrast around mid-grey 0.5, in linear rgb (cheap + good enough for editor tinting)
            if (contrast != 0f)
            {
                float f = 1f + contrast;
                outc.r = Mathf.Clamp01((outc.r - 0.5f) * f + 0.5f);
                outc.g = Mathf.Clamp01((outc.g - 0.5f) * f + 0.5f);
                outc.b = Mathf.Clamp01((outc.b - 0.5f) * f + 0.5f);
            }
            outc.a = c.a;
            return outc;
        }

        // reads each selected RGB object's current colour into originals[obj] (skips swap-only objects —
        // they have no continuous colour to modify). used to start a modify/set preview session.
        public static int SnapshotOriginals(Dictionary<LevelEditorPlaceableObject, Color> originals)
        {
            originals.Clear();
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null) return 0;
            foreach (var obj in sel)
            {
                if (obj == null) continue;
                var rgb = BatchTargets.GetColourParam(obj);
                if (rgb != null) originals[obj] = rgb.CurrentColour;
            }
            return originals.Count;
        }

        // live-apply a modify adjustment onto every snapshotted object, recomputed from its ORIGINAL each
        // time (no compounding). does NOT touch the undo stack — the window pushes one entry on commit.
        public static void ModifyPreview(Dictionary<LevelEditorPlaceableObject, Color> originals,
            float brightness, float contrast, float hue, float saturation)
        {
            foreach (var kv in originals)
            {
                var obj = kv.Key;
                if (obj == null) continue;
                var rgb = BatchTargets.GetColourParam(obj);
                if (rgb != null) rgb.SetColour(Modify(kv.Value, brightness, contrast, hue, saturation));
            }
        }

        // live-apply a flat colour onto every snapshotted object. same no-undo preview contract.
        public static void SetPreview(Dictionary<LevelEditorPlaceableObject, Color> originals, Color colour)
        {
            foreach (var kv in originals)
            {
                var obj = kv.Key;
                if (obj == null) continue;
                var rgb = BatchTargets.GetColourParam(obj);
                if (rgb != null) rgb.SetColour(colour);
            }
        }
    }
}
