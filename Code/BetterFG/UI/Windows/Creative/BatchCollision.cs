using LevelEditor;

namespace BetterFG.UI.Windows.Creative
{
    // Batch collision over the selection. Set the backing field then call ApplyCollisionParam(true)
    // so it pushes the value onto _colliders + _parameterCollisionResponders (see LEVELEDITOR_NOTES.md
    // — there's no public SetCollisionEnabled on this type despite the interop string table listing
    // that name; it belongs to the IParameterCollisionResponder interface instead).
    public static class BatchCollision
    {
        public static int SetCollisionEnabled(bool enabled)
        {
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null) { Plugin.Log.LogWarning("[BatchCollision] no selection"); return 0; }

            var entry = BatchEditHistory.Begin(enabled ? "collision on" : "collision off");
            int touched = 0, noParam = 0;
            foreach (var obj in sel)
            {
                if (obj == null) continue;
                var col = BatchTargets.GetCollisionParam(obj);
                if (col == null) { noParam++; continue; }

                var snap = new BatchEditHistory.ObjectSnap { Obj = obj, CollisionEnabled = col._collisionEnabled };
                col._collisionEnabled = enabled;
                col.ApplyCollisionParam(true);
                entry.Snaps.Add(snap);
                touched++;
            }
            BatchEditHistory.Push(entry);
            Plugin.Log.LogInfo($"[BatchCollision] {(enabled ? "on" : "off")} -> {touched} set, {noParam} no-collision-param");
            return touched;
        }
    }
}
