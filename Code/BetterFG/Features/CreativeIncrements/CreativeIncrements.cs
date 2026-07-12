using System;
using System.Globalization;
using BetterFG.Services;
using HarmonyLib;

namespace BetterFG.Features.CreativeIncrements
{
    // the level editor float parameter nodes (position/rotation/scale per axis etc) step in a fixed
    // scale 0.25..10. they're all built through ParameterUtils.CreateFloatEntry(min, max, step, ...),
    // so we prefix-patch that and rewrite the range from settings. procedural: set a step + a max,
    // it steps 0..max. value gets re-snapped onto the grid so the object doesn't reset on rebuild.
    public static class CreativeIncrements
    {
        const string MinKey = "creative.increments.min";
        const string StepKey = "creative.increments.step";
        const string MaxKey = "creative.increments.max";
        const string SpeedKey = "creative.increments.cooldown";
        // defaults MUST match the game's own float entry defaults so an untouched install is a no-op
        const float DefaultMin = 0.25f;
        const float DefaultStep = 0.25f;
        const float DefaultMax = 10f;
        const float DefaultSpeed = 0.2f; // game default-ish nav cooldown

        public static bool Enabled
        {
            get => SettingsService.Get("creative.increments.on", "false") == "true";
            set => SettingsService.Set("creative.increments.on", value ? "true" : "false");
        }

        public static float Min
        {
            get => GetFloat(MinKey, DefaultMin);
            set => SettingsService.Set(MinKey, value.ToString(CultureInfo.InvariantCulture));
        }

        public static float Step
        {
            get => GetFloat(StepKey, DefaultStep);
            set => SettingsService.Set(StepKey, value.ToString(CultureInfo.InvariantCulture));
        }

        public static float Max
        {
            get => GetFloat(MaxKey, DefaultMax);
            set => SettingsService.Set(MaxKey, value.ToString(CultureInfo.InvariantCulture));
        }

        // nav cooldown = delay between repeat steps when you hold the input. lower = faster scroll.
        public static float Speed
        {
            get => GetFloat(SpeedKey, DefaultSpeed);
            set => SettingsService.Set(SpeedKey, value.ToString(CultureInfo.InvariantCulture));
        }

        static float GetFloat(string key, float def) =>
            float.TryParse(SettingsService.Get(key, def.ToString(CultureInfo.InvariantCulture)),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : def;

        // the game builds the float node as a list of formatted strings and selects the entry whose
        // string == value.ToString("G"). with a fine non-binary step the value rarely lands exactly
        // on a generated point (0+100*0.01 = 1.0000001 != "1") so the match fails and it sits at
        // index 0. so: we force min/step/max here AND remember each node's real value, then fix the
        // selected index by hand in OnOpened (round((value-min)/step)) — no string match, no fp miss.
        public static readonly System.Collections.Generic.Dictionary<string, float> LastValues
            = new System.Collections.Generic.Dictionary<string, float>();

        // runtime gate: the fine grid is only live while you're actually in the parameter-editing UI.
        // the editor rebuilds every object's position/rotation/scale nodes during a save/serialize
        // pass too, and with a fine step (e.g. 0.01) + big max (240) each CreateFloatEntry builds a
        // ~24k-entry string list. multiplied over every object that froze saves for 10-40s. so we only
        // flip this on when the parameter menu opens and off the moment you leave it (ReturnToSettings /
        // options screen close) — outside that window Rewrite is a no-op and the game builds its own
        // cheap default grid.
        private static bool _active;

        public static void SetActive(bool on) => _active = on;

        public static void Rewrite(string paramName, float value, ref float min, ref float max, ref float step, ref string numberFormat)
        {
            if (!_active) return;
            if (!Enabled) return;

            // only the GENUINE first build carries the object's real value + the game's own defaults.
            // the menu re-runs CreateFloatEntry afterwards with value=0 and our already-changed
            // min/step — if we stored those we'd clobber the real value and reopen at 0. so only
            // record when the incoming args still look like the game's untouched float entry.
            bool genuine = step != Step || min != Min || max != Max;

            float lo = Min, s = Step, m = Max;
            if (s <= 0f || m <= 0f) return;

            // never let our max sit below the value being shown, or it can't reach the real index
            float v = value < 0f ? -value : value;
            if (v > m) m = v;

            min = lo;
            step = s;
            max = m;
            numberFormat = NumberFormat(); // round entries to step precision so the match doesn't reset

            if (genuine && !string.IsNullOrEmpty(paramName)) LastValues[paramName] = value;
        }

        // index of value on the current grid, clamped to >= 0
        public static int IndexOf(float value)
        {
            float s = Step;
            if (s <= 0f) return 0;
            int idx = (int)Math.Round((value - Min) / s);
            return idx < 0 ? 0 : idx;
        }

        // number format that rounds every entry to the step's own precision. the game stores entries
        // as value.ToString(numberFormat) and matches the current value the same way; with the
        // default "G" a noisy 1.0000001 never equals "1" and selection resets. forcing fixed decimals
        // (F2 for step 0.01) makes both sides format identically so the match succeeds.
        public static string NumberFormat()
        {
            decimal s;
            try { s = (decimal)Step; } catch { return "G"; }
            s = Math.Abs(s);
            int decimals = 0;
            while (s != Math.Floor(s) && decimals < 6) { s *= 10m; decimals++; }
            return "F" + decimals;
        }
    }

    // the per-axis position/scale/rotation nodes are built through ParameterUtils' float entry
    // builders. CreateFloatEntry is the single-node one; CreateSingleFloatEntryArray is the X/Y/Z
    // array one (what scale/position actually use). patch both.
    // bind by argument POSITION not name — il2cpp interop methods don't keep original param names,
    // so name-matched ref params can silently fail to bind. [HarmonyArgument(index)] is explicit.
    [HarmonyPatch(typeof(ParameterUtils), "CreateFloatEntry")]
    internal static class CreativeIncrementsFloatEntryPatch
    {
        // CreateFloatEntry(paramName=0, value=1, min=2, max=3, step=4, wrapMode=5, callback=6,
        //                  stringFormat=7, numberFormat=8, ...)
        [HarmonyPrefix]
        public static void Prefix(
            [HarmonyArgument(0)] string paramName,
            [HarmonyArgument(1)] float value,
            [HarmonyArgument(2)] ref float min,
            [HarmonyArgument(3)] ref float max,
            [HarmonyArgument(4)] ref float step,
            [HarmonyArgument(8)] ref string numberFormat)
            => CreativeIncrements.Rewrite(paramName, value, ref min, ref max, ref step, ref numberFormat);
    }

    [HarmonyPatch(typeof(ParameterUtils), "CreateSingleFloatEntryArray")]
    internal static class CreativeIncrementsSingleFloatArrayPatch
    {
        // CreateSingleFloatEntryArray(paramName=0, value=1, min=2, max=3, step=4, wrapMode=5,
        //                             callback=6, stringFormat=7, numberFormat=8, ...)
        [HarmonyPrefix]
        public static void Prefix(
            [HarmonyArgument(0)] string paramName,
            [HarmonyArgument(1)] float value,
            [HarmonyArgument(2)] ref float min,
            [HarmonyArgument(3)] ref float max,
            [HarmonyArgument(4)] ref float step,
            [HarmonyArgument(8)] ref string numberFormat)
            => CreativeIncrements.Rewrite(paramName, value, ref min, ref max, ref step, ref numberFormat);
    }

    // on open: set scroll speed, and fix each float node's selected index by hand. the game's own
    // value->index string match misses on fine steps (fp), parking everything at index 0 (= min).
    [HarmonyPatch(typeof(LevelEditorParameterMenuViewModel), "OnOpened")]
    internal static class CreativeIncrementsCooldownPatch
    {
        [HarmonyPostfix]
        public static void Postfix(LevelEditorParameterMenuViewModel __instance)
        {
            if (!CreativeIncrements.Enabled) return;
            try
            {
                var handlers = __instance._inputHandlers;
                if (handlers != null)
                    for (int h = 0; h < handlers.Count; h++)
                    {
                        var pmih = handlers[h]?.TryCast<ParametersMenuInputHandler>();
                        if (pmih != null) pmih._startNavigationInputCooldown = CreativeIncrements.Speed;
                    }

                var entries = __instance._nodeEntries;
                if (entries == null) return;
                for (int i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i];
                    if (entry == null) continue;
                    var data = entry._nodeData;
                    string name = data.ParamName;
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!CreativeIncrements.LastValues.TryGetValue(name, out float realValue)) continue;

                    int want = CreativeIncrements.IndexOf(realValue);
                    var items = data.SelectionItems;
                    int maxIdx = (items != null ? items.Count : 0) - 1;
                    if (maxIdx >= 0 && want > maxIdx) want = maxIdx;
                    if (want < 0) want = 0;

                    Plugin.Log?.LogInfo($"[CreativeIncrements] {name} FIX realValue={realValue} want={want} cur={data.SelectedIndex} count={(items != null ? items.Count : -1)}");

                    if (data.SelectedIndex != want)
                    {
                        data.SelectedIndex = want;
                        entry._nodeData = data;
                        entry.UpdateVm();
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("[CreativeIncrements] onopen fix failed " + ex.Message);
            }
        }
    }

    // The fine grid must only be live while the object's parameters screen is actually open — that's
    // the ONLY time you interact with the increment steppers. The editor also rebuilds every object's
    // position/rotation/scale nodes during its save/serialize pass, and if the gate were open then,
    // each CreateFloatEntry builds a ~24k-entry string list (fine step over a big max) → the 10-40s
    // save freeze. These two statics are the exact open/close points on the param menu VM, and they
    // fire no matter how you enter/leave (mouse, gamepad back, item switch), so the gate can't get
    // stuck. OpenParametersScreen runs before the nodes are built, so a prefix gates the very first
    // build correctly (the old OnOpened hook fired too late — first scale got the coarse grid).
    [HarmonyPatch(typeof(LevelEditorParameterMenuViewModel), nameof(LevelEditorParameterMenuViewModel.OpenParametersScreen))]
    internal static class CreativeIncrementsParamsOpenPatch
    {
        [HarmonyPrefix]
        public static void Prefix()
        {
            CreativeIncrements.SetActive(true);
        }
    }

    [HarmonyPatch(typeof(LevelEditorParameterMenuViewModel), nameof(LevelEditorParameterMenuViewModel.CloseParametersScreen))]
    internal static class CreativeIncrementsParamsClosePatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            CreativeIncrements.SetActive(false);
        }
    }

    // Belt-and-braces: also close the gate whenever we leave the level editor entirely, so it can
    // never survive across a session if some exotic path skipped CloseParametersScreen.
    [HarmonyPatch(typeof(FGClient.LevelEditorStateObjectMenu), nameof(FGClient.LevelEditorStateObjectMenu.Teardown))]
    internal static class CreativeIncrementsObjectMenuTeardownPatch
    {
        [HarmonyPostfix]
        public static void Postfix()
        {
            CreativeIncrements.SetActive(false);
        }
    }
}
