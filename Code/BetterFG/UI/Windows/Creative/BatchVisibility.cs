using LevelEditor;

namespace BetterFG.UI.Windows.Creative
{
    // Batch visibility over the selection. Objects carry an int VisibilityParam indexing their own
    // _visibilityOptions array — confirmed in-game as 0 = invisible, 1 = visible (opposite of what
    // the array-order naming suggests). Combined visibility+collision objects satisfy the same cast
    // since LevelEditorVisibilityAndCollisionCombinedParameter : LevelEditorVisibilityParameter.
    public static class BatchVisibility
    {
        public static int SetVisible(bool visible)
        {
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null) { Plugin.Log.LogWarning("[BatchVisibility] no selection"); return 0; }

            var entry = BatchEditHistory.Begin(visible ? "set visible" : "set invisible");
            int touched = 0, noParam = 0;
            foreach (var obj in sel)
            {
                if (obj == null) continue;
                var vis = BatchTargets.GetVisibilityParam(obj);
                if (vis == null) { noParam++; continue; }

                var snap = new BatchEditHistory.ObjectSnap { Obj = obj, VisibilityParam = vis.VisibilityParam };
                vis.VisibilityParam = visible ? 1 : 0;
                vis.ApplyVisibilityParam(true);
                entry.Snaps.Add(snap);
                touched++;
            }
            BatchEditHistory.Push(entry);
            Plugin.Log.LogInfo($"[BatchVisibility] {(visible ? "visible" : "invisible")} -> {touched} set, {noParam} no-visibility-param");
            return touched;
        }
    }
}
