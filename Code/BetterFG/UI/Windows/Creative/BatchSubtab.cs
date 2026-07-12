using System;
using System.Collections.Generic;
using LevelEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Windows.Creative
{
    // Extension point for the batch-edit carousel. BettrFG ships three built-in subtabs (Recolour /
    // Scale / Material); anything registered here shows up as an extra carousel page after them. Nothing
    // registers by default, so the carousel is unchanged unless a separate plugin DLL calls Register in
    // its own Load(). That DLL owns its own IL2Cpp type registration + Harmony — this is only the seam
    // for slotting a page into the window.
    //
    // The build callback gets a BatchSubtabContext with the same primitives the built-in subtabs use
    // (UGUIShip is public, so an external assembly can build sliders/buttons the same way). Register once
    // at load; the window reads the list every time it (re)builds.
    public static class BatchSubtabRegistry
    {
        public static readonly List<BatchSubtab> Extras = new List<BatchSubtab>();

        public static void Register(string name, Action<BatchSubtabContext> build)
        {
            if (string.IsNullOrEmpty(name) || build == null) { Plugin.Log.LogWarning("batch subtab register ignored — needs a name and a build fn"); return; }
            foreach (var e in Extras) if (e.Name == name) { Plugin.Log.LogWarning($"batch subtab '{name}' already registered, skipping the dupe"); return; }
            Extras.Add(new BatchSubtab { Name = name, Build = build });
            Plugin.Log.LogInfo($"batch subtab '{name}' registered — {Extras.Count} extra page(s) now");
        }
    }

    public class BatchSubtab
    {
        public string Name;
        public Action<BatchSubtabContext> Build;
    }

    // handed to an extra subtab's build fn. Root is the window content rect (same one the built-ins get);
    // start laying out at Y and push it down as you add rows, exactly like the built-in Build* methods do.
    // SelectionCount / Selection give the live multi-selection so a module can act on it. Status writes the
    // window's footer line. Everything an external module needs to look/behave native, without reaching
    // into window internals.
    public class BatchSubtabContext
    {
        public RectTransform Root;
        public float Width;
        public float Y;
        public int SelectionCount;

        public Il2CppSystem.Collections.Generic.HashSet<LevelEditorPlaceableObject> Selection
            => LevelEditorMultiSelectionHandler.Selection();

        // footer status line (green on success, dim otherwise), mirroring the built-in subtabs' feel.
        public Action<string, bool> SetStatus;

        // the window's own label helper, so external rows match the built-in typography without the
        // module poking at protected members.
        public Func<Transform, Rect, string, int, Color, TextAnchor, Text> MakeLabel;
    }
}
