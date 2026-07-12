using UnityEngine;
using FG.Common;
using LevelEditor;

namespace BetterFG.UI.Windows.Creative
{
    // Batch scale over the multi-selection. `offset` is the CUMULATIVE running total (from the window's
    // _offsets), re-sent in full every nudge — so each call restores objects to their session-original
    // scale/position (from the entry's snapshots) and re-applies the total. Nothing compounds, and the
    // group factor 1+offset can cross 0 into negative.
    //   Individual  → each object's scale = original + offset, positions untouched.
    //   FromCenter  → scale the WHOLE group as one rigid body via the level editor's own
    //                 MultiSelectRigidBodyOwner (move owner to the selection centre, reparent so objects
    //                 don't budge, scale owner by factor 1+offset, disown). uniform, no distortion,
    //                 negative factor mirrors the group like a normal negative transform scale.
    //   FromOrigin  → same rigid-body scaling but the owner sits at world 0,0,0.
    //   FromSelected→ same rigid-body scaling, but the pivot is the specific object you clicked (the
    //                 game's selection pivot) — the group scales from that object's POSITION so it
    //                 grows/shrinks toward it. Position only, not orientation: rotating the owner gets
    //                 baked into every object's rotation on deselect and corrupts their orientation.
    public enum ScaleMode { FromOrigin, Individual, FromCenter, FromSelected }

    public static class BatchScale
    {
        // first nudge of a session snapshots each object's original scale (+ position for group modes)
        // into the entry; every nudge then re-applies from those originals. one entry = one undo step.
        public static int ApplyInto(BatchEditHistory.BatchEntry entry, Vector3 offset, ScaleMode mode)
        {
            var sel = LevelEditorMultiSelectionHandler.Selection();
            if (sel == null) { Plugin.Log.LogWarning("asked to batch-scale but nothing's selected"); return 0; }

            if (mode == ScaleMode.Individual)
                return ApplyIndividual(entry, sel, offset);
            return ApplyViaOwner(entry, sel, offset, mode);
        }

        // each object's scale = its session-original + offset. positions untouched.
        private static int ApplyIndividual(BatchEditHistory.BatchEntry entry,
            Il2CppSystem.Collections.Generic.HashSet<LevelEditorPlaceableObject> sel, Vector3 offset)
        {
            int touched = 0, noScale = 0;
            foreach (var obj in sel)
            {
                if (obj == null) continue;
                var sp = obj._levelEditorScaleParameter;
                if (sp == null) { noScale++; continue; }

                Vector3 orig = SnapOnce(entry, obj, sp.CurrentScale, null).Scale.Value;
                sp.SetScale(orig + offset, true);
                touched++;
            }
            Plugin.Log.LogDebug($"scaled {touched} by {offset}" + (noScale > 0 ? $", {noScale} had no scale param" : ""));
            return touched;
        }

        // the selection is ALREADY parented under the game's live MultiSelectRigidBodyOwner while
        // multi-select is active — scaling that owner's transform directly is exactly what works from
        // UnityExplorer. so: no create, no reparent, no disown, no per-object restore. just set the
        // owner's localScale to 1+offset (full running total — the owner keeps its scale between holds,
        // the game bakes it into the objects itself when the selection ends). FromCenter additionally
        // moves the owner to the centroid once per session, compensating children's world positions so
        // nothing budges.
        private static int ApplyViaOwner(BatchEditHistory.BatchEntry entry,
            Il2CppSystem.Collections.Generic.HashSet<LevelEditorPlaceableObject> sel, Vector3 offset, ScaleMode mode)
        {
            var handler = LevelEditorManager.Instance?.GetMultiselectHandler();
            var owner = handler?._multiselectGlobalParent;
            if (owner == null) { Plugin.Log.LogWarning("wanted to group-scale but there's no live multiselect owner, bailing"); return 0; }

            bool sessionStart = entry.Snaps.Count == 0;

            int touched = 0, noScale = 0;
            foreach (var obj in sel)
            {
                if (obj == null) continue;
                var sp = obj._levelEditorScaleParameter;
                if (sp == null) { noScale++; continue; }
                SnapOnce(entry, obj, sp.CurrentScale, obj.Position);
                touched++;
            }
            if (touched == 0) { Plugin.Log.LogInfo("nothing in the selection could be scaled"); return 0; }

            if (sessionStart)
            {
                // park the owner on the pivot without moving anything: children are parented under it,
                // so save their world position+rotation, move/orient the owner, put them back. FromSelected
                // rotates the owner to the clicked object's frame so the group scales along THAT object's
                // local axes (the whole point of the mode). The game zeroes the owner's rotation itself
                // before it disowns/bakes, so nothing bad gets composed into the objects on deselect.
                Vector3 pivotPos = Vector3.zero;
                Quaternion pivotRot = Quaternion.identity;
                if (mode == ScaleMode.FromCenter) pivotPos = handler.GetCentroid();
                else if (mode == ScaleMode.FromSelected)
                {
                    var pick = PivotObject();
                    if (pick != null) { pivotPos = pick.transform.position; pivotRot = pick.transform.rotation; }
                    else pivotPos = handler.GetCentroid();
                }

                var kids = new System.Collections.Generic.List<(Transform t, Vector3 pos, Quaternion rot)>();
                foreach (var obj in sel)
                {
                    if (obj == null) continue;
                    kids.Add((obj.transform, obj.transform.position, obj.transform.rotation));
                }
                owner.transform.SetPositionAndRotation(pivotPos, pivotRot);
                foreach (var (t, pos, rot) in kids) t.SetPositionAndRotation(pos, rot);
            }

            owner.transform.localScale = new Vector3(1f + offset.x, 1f + offset.y, 1f + offset.z);

            Plugin.Log.LogDebug($"group scale, {mode}, factor {owner.transform.localScale} — {touched} objects, pivot at {owner.transform.position}");
            return touched;
        }

        // the specific object the selection pivots around — the game's static SelectionPivot (the one
        // you last clicked while multi-selecting, what in-game rotation pivots off), falling back to the
        // centermost object. null only when the game hasn't assigned a pivot yet. FromSelected uses this
        // and the window dims its controls while it's null.
        public static LevelEditorPlaceableObject PivotObject()
        {
            var pick = LevelEditorMultiSelectionHandler.SelectionPivot();
            if (pick != null) return pick;
            return LevelEditorManager.Instance?.GetMultiselectHandler()?.GetCentermostPlacementObjectFromSelection();
        }

        // put the owner's scale back to 1 — used when undo/redo restores objects directly, so the live
        // parent doesn't keep multiplying the restored values.
        public static void ResetOwnerScale()
        {
            var owner = LevelEditorManager.Instance?.GetMultiselectHandler()?._multiselectGlobalParent;
            if (owner != null) owner.transform.localScale = Vector3.one;
        }

        // returns the entry's existing snapshot for this object (its session-original), or creates one
        // from the passed current values on the first nudge. later nudges reuse the original.
        private static BatchEditHistory.ObjectSnap SnapOnce(BatchEditHistory.BatchEntry entry,
            LevelEditorPlaceableObject obj, Vector3 curScale, Vector3? curPos)
        {
            foreach (var existing in entry.Snaps) if (existing.Obj == obj) return existing;
            var snap = new BatchEditHistory.ObjectSnap { Obj = obj, Scale = curScale };
            if (curPos.HasValue) snap.Position = curPos.Value;
            entry.Snaps.Add(snap);
            return snap;
        }
    }
}
