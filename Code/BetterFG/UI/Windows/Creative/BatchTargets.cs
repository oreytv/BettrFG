using UnityEngine;
using LevelEditor;

namespace BetterFG.UI.Windows.Creative
{
    // Shared lookup + snapshot/restore over a placeable's parameter components. All three batch ops
    // (recolour/scale/material) and the undo stack go through here so the il2cpp component lookup (which
    // is finicky — the typed GetComponentInChildren<T> returns null on these, so we enumerate the base
    // handler and TryCast) lives in one place.
    public static class BatchTargets
    {
        public static LevelEditorColourChangerParameter GetColourParam(LevelEditorPlaceableObject obj)
        {
            if (obj == null) return null;
            var handlers = obj.GetComponentsInChildren<LevelEditorPlaceableParameterHandler>(true);
            if (handlers == null) return null;
            for (int i = 0; i < handlers.Length; i++)
            {
                var c = handlers[i]?.TryCast<LevelEditorColourChangerParameter>();
                if (c != null) return c;
            }
            return null;
        }

        public static LevelEditorSurfaceDefinitionParameter GetSurfaceParam(LevelEditorPlaceableObject obj)
        {
            if (obj == null) return null;
            var handlers = obj.GetComponentsInChildren<LevelEditorPlaceableParameterHandler>(true);
            if (handlers == null) return null;
            for (int i = 0; i < handlers.Length; i++)
            {
                var s = handlers[i]?.TryCast<LevelEditorSurfaceDefinitionParameter>();
                if (s != null) return s;
            }
            return null;
        }

        // LevelEditorVisibilityAndCollisionCombinedParameter : LevelEditorVisibilityParameter, so
        // this cast picks up combined objects too.
        public static LevelEditorVisibilityParameter GetVisibilityParam(LevelEditorPlaceableObject obj)
        {
            if (obj == null) return null;
            var handlers = obj.GetComponentsInChildren<LevelEditorPlaceableParameterHandler>(true);
            if (handlers == null) return null;
            for (int i = 0; i < handlers.Length; i++)
            {
                var v = handlers[i]?.TryCast<LevelEditorVisibilityParameter>();
                if (v != null) return v;
            }
            return null;
        }

        public static LevelEditorCollisionParameter GetCollisionParam(LevelEditorPlaceableObject obj)
        {
            if (obj == null) return null;
            var handlers = obj.GetComponentsInChildren<LevelEditorPlaceableParameterHandler>(true);
            if (handlers == null) return null;
            for (int i = 0; i < handlers.Length; i++)
            {
                var c = handlers[i]?.TryCast<LevelEditorCollisionParameter>();
                if (c != null) return c;
            }
            return null;
        }

        // read the object's CURRENT colour/surface/visibility/collision into `into`, but only for the
        // fields `template` carries — used to build an inverse snapshot for redo (see BatchEditHistory).
        public static void SnapCurrent(LevelEditorPlaceableObject obj,
            BatchEditHistory.ObjectSnap template, BatchEditHistory.ObjectSnap into)
        {
            if (template.Colour.HasValue)
            {
                var rgb = GetColourParam(obj);
                if (rgb != null) into.Colour = rgb.CurrentColour;
            }
            if (template.SurfaceIndex.HasValue)
            {
                var surf = GetSurfaceParam(obj);
                if (surf != null) into.SurfaceIndex = surf.SelectedParameterIndex;
            }
            if (template.VisibilityParam.HasValue)
            {
                var vis = GetVisibilityParam(obj);
                if (vis != null) into.VisibilityParam = vis.VisibilityParam;
            }
            if (template.CollisionEnabled.HasValue)
            {
                var col = GetCollisionParam(obj);
                if (col != null) into.CollisionEnabled = col.GetCollisionParam();
            }
        }

        // put back whatever colour/surface fields the snap captured.
        public static void Restore(LevelEditorPlaceableObject obj, BatchEditHistory.ObjectSnap snap)
        {
            if (snap.Colour.HasValue)
            {
                var rgb = GetColourParam(obj);
                if (rgb != null) rgb.SetColour(snap.Colour.Value);
            }
            if (snap.SurfaceIndex.HasValue)
            {
                var surf = GetSurfaceParam(obj);
                if (surf != null)
                {
                    int index = snap.SurfaceIndex.Value;
                    // index 0 is the "no surface" sentinel (see BatchMaterial.Apply), not a real
                    // position in _enabledSurfaces — looking it up there returns whatever surface
                    // happens to sit at slot 0 instead of clearing back to none.
                    var so = index == 0 ? null : surf._enabledSurfaces?.GetSurfaceByIndex(index);
                    ApplySurfaceIndex(surf, index, so, refreshResponders: so == null);
                }
            }
            if (snap.VisibilityParam.HasValue)
            {
                var vis = GetVisibilityParam(obj);
                if (vis != null) { vis.VisibilityParam = snap.VisibilityParam.Value; vis.ApplyVisibilityParam(true); }
            }
            if (snap.CollisionEnabled.HasValue)
            {
                var col = GetCollisionParam(obj);
                if (col != null) { col._collisionEnabled = snap.CollisionEnabled.Value; col.ApplyCollisionParam(true); }
            }
        }

        // apply a surface SO directly and set the selected-index backing field, bypassing
        // SetParameterIndex (whose updateResponders path NREs via UpdateParameterEntriesUI when the
        // param menu isn't open). refreshResponders drives OnParameterIndexChanged so the change shows
        // immediately — needed to CLEAR to none (ApplySurface(null) alone doesn't repaint until reselect),
        // but it breaks applying a real surface like slime, so slime uses ApplySurface alone.
        public static void ApplySurfaceIndex(LevelEditorSurfaceDefinitionParameter surf, int index,
            LevelEditorSurfaceDefinitionSO so, bool refreshResponders)
        {
            surf._SelectedParameterIndex_k__BackingField = index;
            surf.ApplySurface(so);
            if (refreshResponders)
            {
                try { surf.OnParameterIndexChanged(index); }
                catch (System.Exception ex) { Plugin.Log.LogWarning("[BatchMaterial] OnParameterIndexChanged failed: " + ex.Message); }
            }
        }
    }
}
