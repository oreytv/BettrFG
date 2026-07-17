using System;
using System.Globalization;
using BetterFG.Features.CreativeIncrements;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Services;
using UnityEngine;

namespace BetterFG.Tweaks
{
    // Type an exact number into a level editor parameter instead of holding an arrow key.
    // Enter on the selected parameter swaps its value text to "Write a number", you type, and Enter or a
    // click writes it. Escape restores the old text.
    //
    // Why this can't just move the selection: every numeric parameter is really a ParameterNodeType.String
    // node over a prebuilt list of formatted numbers, and the node's OnChangedIndex maps an index onto
    // min + index * step. Driving it can only ever land on a grid point, so it could never express 3.7 on a
    // 0.25 step, and reaching a big number would mean rebuilding a huge entry list (the thing that caused
    // the save freeze CreativeIncrements gates against). The Action<float> that actually writes the value is
    // captured in the closure ParameterUtils built inside CreateFloatEntry, and il2cpp exposes that
    // closure's fields, so we take the callback straight off it and hand it the number. No grid, no rebuild.
    //
    // Writing through that callback with the menu open and the object selected lands inside the editor's own
    // record lifecycle, so undo picks a typed value up for free. Don't add recording here: doing our own
    // RecordAction on top opened a second chain and the undo system rebuilt it into a duplicate object.
    public class CreativeTypeValueTweak : BfgTweak
    {
        public CreativeTypeValueTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "creative_type_value";
        public override string TweakLabel => "Type Parameter Values";
        public override bool DefaultEnabled => false;
        public override string TweakTooltip =>
            "In the creative parameter menu, press Enter on a number to type an exact value. Enter or a click writes it, Escape cancels. Ignores the increment steps, so any number goes in.";

        public static CreativeTypeValueTweak Instance { get; private set; }
        void Awake() => Instance = this;

        private const string Prompt = "Write a number";

        private ParameterNodeViewModelString _node;
        private string _typed;
        private string _restore;

        void Update()
        {
            if (!IsEnabled)
            {
                if (_node != null) Cancel();
                return;
            }

            if (_node == null)
            {
                if (EnterDown() && UnityRoundLoader.InLevelEditor) Begin();
                return;
            }

            // the node can go away under us (menu rebuild, object swap, menu closed) while we hold it
            if (_node == null || !LevelEditorParameterMenuViewModel.IsParametersScreenOpen()) { Cancel(); return; }

            if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }
            if (Input.GetMouseButtonDown(0)) { Commit(); return; }

            foreach (char c in Input.inputString)
            {
                if (c == '\n' || c == '\r') { Commit(); return; }
                if (c == '\b') { if (_typed.Length > 0) _typed = _typed.Substring(0, _typed.Length - 1); }
                else if (c == ',' || c == '.') { if (_typed.IndexOf('.') < 0) _typed += '.'; }
                else if (c == '-') { if (_typed.Length == 0) _typed += '-'; }
                else if (c >= '0' && c <= '9') _typed += c;
            }

            _node.Value = _typed.Length == 0 ? Prompt : _typed;
        }

        private static bool EnterDown() =>
            Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

        private void Begin()
        {
            var node = SelectedNode();
            if (node == null) return;

            // only numeric nodes carry a value closure. bail on the rest (bools, colours, buttons) so
            // Enter keeps doing whatever the editor does on them
            if (!Resolve(node, out _, out _, out _, out _)) return;

            _node = node;
            _typed = "";
            _restore = node.Value;
            node.Value = Prompt;
            FGInputLockService.SetParamTypeLock(true);
        }

        private void Cancel()
        {
            if (_node != null && _restore != null) _node.Value = _restore;
            End();
        }

        private void End()
        {
            _node = null;
            _typed = null;
            _restore = null;
            FGInputLockService.SetParamTypeLock(false);
        }

        private void Commit()
        {
            var node = _node;
            string typed = _typed;
            if (node == null) { End(); return; }

            if (!float.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            { Cancel(); return; }

            if (!Resolve(node, out var floatCb, out var intCb, out float min, out float step))
            { Cancel(); return; }

            try
            {
                if (intCb != null) { value = Mathf.Round(value); intCb.Invoke((int)value); }
                else floatCb.Invoke(value);
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("CreativeTypeValue: writing " + value + " failed " + ex.Message);
                Cancel();
                return;
            }

            // keep the node showing what we wrote, and park its index on the nearest grid point so a
            // follow-up arrow press steps from roughly here instead of jumping back to where it was
            node.Value = Format(node, value);
            if (step > 0f)
            {
                int idx = Mathf.Max(0, Mathf.RoundToInt((value - min) / step));
                var items = node.ParamData.SelectionItems;
                int count = items != null ? items.Count : 0;
                if (count > 0 && idx > count - 1) idx = count - 1;
                node.SelectedOption = idx;
            }

            // CreativeIncrements reopens each node at round((value - min) / step) from this map, so an
            // off-grid typed value still comes back to the right place next time the menu opens
            string paramName = node.ParamData.ParamName;
            if (!string.IsNullOrEmpty(paramName)) CreativeIncrements.LastValues[paramName] = value;

            End();
        }

        private static string Format(ParameterNodeViewModelString node, float value)
        {
            string fmt = node.ParamData.numberFormat;
            try { return value.ToString(string.IsNullOrEmpty(fmt) ? "G" : fmt, CultureInfo.InvariantCulture); }
            catch { return value.ToString(CultureInfo.InvariantCulture); }
        }

        // the node the parameter menu's own input handler says is selected. same route CreativeIncrements
        // takes to the handler, so no scene scan
        private static ParameterNodeViewModelString SelectedNode()
        {
            var vm = LevelEditorParameterMenuViewModel._instance;
            if (vm == null) return null;

            var handlers = vm._inputHandlers;
            if (handlers == null) return null;

            for (int i = 0; i < handlers.Count; i++)
            {
                var pmih = handlers[i]?.TryCast<ParametersMenuInputHandler>();
                if (pmih == null) continue;

                var go = pmih.GetSelectedObjectByIndex();
                if (go == null) return null;
                return go.GetComponent<ParameterNodeViewModelString>();
            }
            return null;
        }

        // digs the value-writing callback out of the closure ParameterUtils captured when it built the
        // node. CreateEntry wraps whatever CreateFloatEntry/CreateIntEntry passed it in a closure of its
        // own, so step through that first. min/step come back for the index we park the node on after.
        private static bool Resolve(ParameterNodeViewModelString node,
            out Il2CppSystem.Action<float> floatCallback, out Il2CppSystem.Action<int> intCallback,
            out float min, out float step)
        {
            floatCallback = null; intCallback = null; min = 0f; step = 0f;
            if (node == null) return false;

            var changed = node._onChangedIndex ?? node.ParamData.OnChangedIndex;
            var target = changed?.Target;
            if (target == null) return false;

            var wrapper = target.TryCast<ParameterUtils.__c__DisplayClass10_0>();
            if (wrapper != null) target = wrapper.onChangedIndex?.Target;
            if (target == null) return false;

            var asFloat = target.TryCast<ParameterUtils.__c__DisplayClass5_0>();
            if (asFloat != null)
            {
                min = asFloat.min; step = asFloat.step; floatCallback = asFloat.callback;
                return floatCallback != null;
            }

            var asInt = target.TryCast<ParameterUtils.__c__DisplayClass3_0>();
            if (asInt != null)
            {
                min = asInt.min; step = asInt.step; intCallback = asInt.callback;
                return intCallback != null;
            }

            return false;
        }
    }
}
