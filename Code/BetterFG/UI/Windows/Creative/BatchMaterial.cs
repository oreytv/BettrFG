using LevelEditor;

namespace BetterFG.UI.Windows.Creative
{
    // Batch surface/material over the selection. Objects carry surface on LevelEditorSurfaceDefinitionParameter.
    // We DON'T call SetParameterIndex — it drives UpdateParameterEntriesUI, which NREs when the parameter
    // menu isn't open (the crash we hit). Instead we apply the surface SO directly via ApplySurface and set
    // the selected-index backing field ourselves, so it sticks and round-trips for undo without touching UI.
    public static class BatchMaterial
    {
        public static int SetSlime() => Apply(slime: true);
        public static int SetNone() => Apply(slime: false);

        // does the selection contain anything with an unlit param, i.e. a sticker? gates the lighting row.
        public static bool AnySticker()
        {
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null) return false;
            foreach (var obj in sel)
                if (obj != null && BatchTargets.GetUnlitParam(obj) != null) return true;
            return false;
        }

        // stickers light like any other surface by default; unlit makes them ignore scene lighting and
        // render flat, which is what you want for signage/text. "lit" here is just the inverse of unlit.
        public static int SetLighting(bool lit)
        {
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null) { Plugin.Log.LogWarning("BatchMaterial: no selection"); return 0; }

            var entry = BatchEditHistory.Begin(lit ? "lighting on" : "lighting off");
            int touched = 0, skipped = 0;
            foreach (var obj in sel)
            {
                if (obj == null) continue;
                var unlit = BatchTargets.GetUnlitParam(obj);
                if (unlit == null) { skipped++; continue; }

                entry.Snaps.Add(new BatchEditHistory.ObjectSnap { Obj = obj, Unlit = unlit._unlitMode });
                BatchTargets.ApplyUnlit(unlit, !lit);
                touched++;
            }
            BatchEditHistory.Push(entry);
            Plugin.Log.LogInfo($"{touched} sticker(s) now {(lit ? "lit" : "unlit")}" + (skipped > 0 ? $", left {skipped} alone (not decals)" : ""));
            return touched;
        }

        private static int Apply(bool slime)
        {
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null) { Plugin.Log.LogWarning("BatchMaterial: no selection"); return 0; }

            var entry = BatchEditHistory.Begin(slime ? "material slime" : "material none");
            int touched = 0, noParam = 0, noSlime = 0;
            foreach (var obj in sel)
            {
                if (obj == null) continue;
                var surf = BatchTargets.GetSurfaceParam(obj);
                if (surf == null) { noParam++; continue; }

                int target;
                LevelEditorSurfaceDefinitionSO so;
                if (slime)
                {
                    target = FindSlimeIndex(surf, out so);
                    if (target < 0) { noSlime++; continue; }
                }
                else { target = 0; so = null; } // 0 = no surface, null SO clears it

                var snap = new BatchEditHistory.ObjectSnap { Obj = obj, SurfaceIndex = surf.SelectedParameterIndex };
                // clearing to none needs the responder refresh to repaint; applying a real surface
                // (slime) works with ApplySurface alone and breaks if we also refresh.
                BatchTargets.ApplySurfaceIndex(surf, target, so, refreshResponders: !slime);
                entry.Snaps.Add(snap);
                touched++;
            }
            BatchEditHistory.Push(entry);
            Plugin.Log.LogInfo($"BatchMaterial: {(slime ? "slime" : "none")} -> {touched} set, {noParam} no-surface-param, {noSlime} no-slime-option");
            return touched;
        }

        // find the slime entry in this object's enabled surfaces, returning its index + SO. scans the
        // collection by index and matches a slime key. logs the available keys if nothing matched.
        private static int FindSlimeIndex(LevelEditorSurfaceDefinitionParameter surf, out LevelEditorSurfaceDefinitionSO so)
        {
            so = null;
            var col = surf._enabledSurfaces;
            var names = LevelEditorSurfaceDefinitionParameter._enabledSurfaceNames;
            int count = names != null ? names.Count : 0;
            if (col == null || count == 0) { Plugin.Log.LogInfo("BatchMaterial: no enabled surfaces"); return -1; }

            for (int i = 0; i < count; i++)
            {
                var candidate = col.GetSurfaceByIndex(i);
                string key = candidate != null ? candidate.Key : names[i];
                if (!string.IsNullOrEmpty(key) && key.ToLowerInvariant().Contains("slime"))
                {
                    so = candidate;
                    return i;
                }
            }

            var joined = string.Empty;
            for (int i = 0; i < count; i++) joined += (i > 0 ? ", " : "") + i + ":" + names[i];
            Plugin.Log.LogInfo("BatchMaterial: slime not found in enabled surfaces: " + joined);
            return -1;
        }
    }
}
