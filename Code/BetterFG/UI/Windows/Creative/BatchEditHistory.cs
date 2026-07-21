using System.Collections.Generic;
using UnityEngine;
using LevelEditor;

namespace BetterFG.UI.Windows.Creative
{
    // Our own undo/redo stacks for batch edits. Fall Guys' creative undo doesn't record what we do
    // (we poke the parameter components directly), so we snapshot each affected object's touched fields
    // before an edit and restore them on Undo. One BatchEntry = one Apply press.
    //
    // Redo works by inversion: restoring an entry first reads each object's CURRENT (post-edit) values
    // into an inverse entry, then applies the snapshot. Undo pops _stack → pushes the inverse to _redo;
    // Redo pops _redo → pushes the inverse back to _stack. Any fresh edit clears _redo.
    //
    // Each ObjectSnap only carries the fields the edit actually changed (nullable), so undoing a colour
    // edit doesn't stomp a scale that was set afterwards by some other path.
    public static class BatchEditHistory
    {
        public sealed class ObjectSnap
        {
            public LevelEditorPlaceableObject Obj;
            public Vector3? Scale;
            public Vector3? Position;
            public Color? Colour;      // rgb changer colour
            public int? SurfaceIndex;  // surface/material index
            public int? VisibilityParam;   // visibility option index
            public bool? CollisionEnabled; // collision on/off
            public bool? Unlit;            // sticker unlit mode
        }

        public sealed class BatchEntry
        {
            public string Label;
            public readonly List<ObjectSnap> Snaps = new List<ObjectSnap>();
        }

        private static readonly List<BatchEntry> _stack = new List<BatchEntry>();
        private static readonly List<BatchEntry> _redo = new List<BatchEntry>();

        public static int Count => _stack.Count;
        public static int RedoCount => _redo.Count;

        public static BatchEntry Begin(string label) => new BatchEntry { Label = label };

        // commit a filled entry. no-op if it captured nothing (edit hit zero objects). a fresh edit
        // invalidates the redo stack (you've branched off the old redo path).
        public static void Push(BatchEntry entry)
        {
            if (entry == null || entry.Snaps.Count == 0) return;
            _stack.Add(entry);
            _redo.Clear();
        }

        public static string Undo()
        {
            if (_stack.Count == 0) return null;
            var entry = _stack[_stack.Count - 1];
            _stack.RemoveAt(_stack.Count - 1);
            int restored = RestoreInto(entry, out var inverse);
            _redo.Add(inverse);
            return $"undid \"{entry.Label}\" on {restored} object(s)";
        }

        public static string Redo()
        {
            if (_redo.Count == 0) return null;
            var entry = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            int restored = RestoreInto(entry, out var inverse);
            _stack.Add(inverse);
            return $"redid \"{entry.Label}\" on {restored} object(s)";
        }

        // applies `entry`'s snapshot onto its objects, and fills `inverse` with each object's values as
        // they were RIGHT BEFORE this restore (the same fields), so the caller can push it on the other
        // stack for the opposite direction.
        private static int RestoreInto(BatchEntry entry, out BatchEntry inverse)
        {
            inverse = new BatchEntry { Label = entry.Label };
            int restored = 0;
            foreach (var s in entry.Snaps)
            {
                var o = s.Obj;
                if (o == null) continue;

                var inv = new ObjectSnap { Obj = o };
                if (s.Scale.HasValue)
                {
                    var sp = o._levelEditorScaleParameter;
                    if (sp != null) { inv.Scale = sp.CurrentScale; sp.SetScale(s.Scale.Value, true); }
                }
                if (s.Position.HasValue) { inv.Position = o.Position; o.Position = s.Position.Value; }

                if (s.Colour.HasValue || s.SurfaceIndex.HasValue
                    || s.VisibilityParam.HasValue || s.CollisionEnabled.HasValue || s.Unlit.HasValue)
                {
                    BatchTargets.SnapCurrent(o, s, inv); // read current into inv for the fields s carries
                    BatchTargets.Restore(o, s);
                }

                inverse.Snaps.Add(inv);
                restored++;
            }
            return restored;
        }

        public static void Clear() { _stack.Clear(); _redo.Clear(); }
    }
}
