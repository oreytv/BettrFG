using System;
using System.Collections.Generic;
using BetterFG.Features.CreativeIncrements;
using LevelEditor;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Windows.Creative
{
    // Batch-edit window for a multi-object level-editor selection. A carousel header ( ‹ Style › ) cycles
    // between three subtabs — Recolour, Scale, Material — each rebuilding the body below. Every op records
    // an undo entry (BatchEditHistory); the Undo button at the bottom reverts our edits only (Fall Guys'
    // own undo doesn't see them).
    //
    // Opened by CreativeSelectionWatcher's nav prompt. AnyOpen lets ControllerManager drive the cursor
    // while we're up (so the stick + A work on our sliders/buttons) without any polling here; it clears
    // on close so the game gets its cursor back.
    public class BatchEditWindow : BetterFGWindow
    {
        public BatchEditWindow(IntPtr ptr) : base(ptr) { }

        public static BatchEditWindow Instance { get; private set; }
        public static bool AnyOpen { get; private set; }

        // on/off toggle for the whole batch-edit feature (the nav prompt + window). default on.
        private const string ENABLED_KEY = "creative.batchedit.enabled";
        public static bool FeatureEnabled
        {
            get => Services.SettingsService.Get(ENABLED_KEY, "true") != "false";
            set => Services.SettingsService.Set(ENABLED_KEY, value ? "true" : "false");
        }

        protected override float WindowWidth => 310f;
        protected override float WindowHeight => 340f;
        protected override string WindowTitle => "Batch Edit";
        protected override string BgResourceName => "BetterFG.assets.ui.windows.generalbg_2.png";
        protected override string BgHoverResourceName => "BetterFG.assets.ui.windows.generalbg_2_hover.png";
        protected override bool DraggableFromTitle => true;

        protected override Vector3 InitialBgPosition => new Vector3(184f, 18.5f, 0f);
        protected override Vector3 InitialBgScale => new Vector3(1.41f, 1.6f, 1f);

        private static readonly Color BTN_STEP = new Color(0.22f, 0.34f, 0.55f, 1f);
        private static readonly Color BTN_APPLY = new Color(0.25f, 0.5f, 0.25f, 1f);
        private static readonly Color BTN_UNDO = new Color(0.45f, 0.35f, 0.2f, 1f);
        private static readonly Color BTN_ARROW = new Color(0.28f, 0.28f, 0.34f, 1f);
        private static readonly Color HINT_COL = new Color(1f, 1f, 1f, 0.55f);
        private static readonly Color OK_COL = new Color(0.55f, 0.85f, 0.55f, 1f);

        private static readonly string[] BUILTIN_SUBTABS = { "Recolour", "Scale", "Material" };
        // built-ins first, then whatever external DLLs registered (usually none). rebuilt each open so a
        // plugin that registers after the window's first build still shows up next time it opens.
        private string[] Subtabs()
        {
            var extras = BatchSubtabRegistry.Extras;
            if (extras.Count == 0) return BUILTIN_SUBTABS;
            var all = new string[BUILTIN_SUBTABS.Length + extras.Count];
            Array.Copy(BUILTIN_SUBTABS, all, BUILTIN_SUBTABS.Length);
            for (int i = 0; i < extras.Count; i++) all[BUILTIN_SUBTABS.Length + i] = extras[i].Name;
            return all;
        }
        private int _subtab;

        // recolour state
        private enum RecolourMode { SetColour, Modify }
        private RecolourMode _recolourMode = RecolourMode.SetColour;
        private Color _colour = new Color(1f, 0.4f, 0.2f, 1f);
        private float _modBright, _modContrast, _modHue, _modSat; // modify-mode sliders, 0 = no change
        private Image _preview;
        private Text _recolourModeLabel;
        // live preview session: originals snapshotted once, re-applied from every slider move, pushed as
        // ONE undo entry on commit (apply / subtab / mode switch / window close / selection change).
        private readonly Dictionary<LevelEditorPlaceableObject, Color> _colourOriginals
            = new Dictionary<LevelEditorPlaceableObject, Color>();
        private BatchEditHistory.BatchEntry _colourEntry;
        private int _colourSessionSelCount; // selection count when the session opened — commit if it changes

        // scale state — _offsets is the per-axis cumulative delta since the first nudge (the display,
        // persists across holds; resets on undo). _committedOffsets is how much of that total is already
        // baked into committed undo entries — each session only applies (_offsets - _committedOffsets)
        // against its fresh snapshot baseline, otherwise every new hold would re-apply the whole total
        // on top of already-scaled objects and compound.
        private readonly float[] _offsets = { 0f, 0f, 0f };
        private readonly float[] _committedOffsets = { 0f, 0f, 0f };
        private ScaleMode _scaleMode = ScaleMode.Individual;
        private readonly Text[] _valLabels = new Text[3];
        private Text _modeLabel;
        private readonly UGUIShip.HoldButtonState[] _scaleHold = new UGUIShip.HoldButtonState[6]; // -/+ per axis
        // one undo entry per hold session, not one per nudge tick — opened on the first nudge since
        // the last release, pushed when the button is released.
        private BatchEditHistory.BatchEntry _scaleEntry;
        private CanvasGroup _scaleRowsGroup; // fades/blocks the X/Y/Z rows when FromSelected has no pivot

        private Text _countLabel;
        private Text _statusLabel;

        // ── api ───────────────────────────────────────────────────────────────

        public void Configure()
        {
            Instance = this;
            AnyOpen = true;
            SetAnchorPosition(new Vector2(560f, 30f));
            ShowWindow();
            RebuildContent();
        }

        public void Close()
        {
            CommitColourEntry(); // don't lose a pending recolour on close
            if (Instance == this) Instance = null;
            AnyOpen = false;
            // commit the selection ourselves a frame after close (AnyOpen is false by then, so our own
            // block prefix lets it through) — saves you clicking off the backlog of blocked place attempts.
            CreativeSelectionWatcher.Instance?.PlaceAfterFrame();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            AnyOpen = false;
        }

        protected override void ManagedUpdate()
        {
            base.ManagedUpdate();
            // keep the mouse usable while we're open — the editor otherwise locks+hides it
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _scaleHold.Length; i++)
            {
                int a = i / 2;
                int dir = i % 2 == 0 ? -1 : +1;
                _scaleHold[i]?.Tick(dt, () => NudgeVal(a, dir));
            }

            if (_subtab == 1) UpdateScaleRowsDim(); // pivot can appear/vanish live in "from selected"

            int sel = BatchRecolour.SelectionCount();
            if (sel == 0) { Close(); return; }
            // selection changed mid-edit → checkpoint the pending recolour (its snapshot is now stale)
            if (_colourEntry != null && sel != _colourSessionSelCount) CommitColourEntry();
        }

        // close button in the title bar — the nav-prompt can't reopen/close while our input lock is up,
        // so the window needs its own way out (clickable by mouse or controller cursor).
        protected override void BuildTitleExtras(Transform titleRoot)
        {
            var go = new GameObject("CloseBtn");
            go.transform.SetParent(titleRoot, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(30f, 28f);
            rt.anchoredPosition = new Vector2(24f, 18f);
            UGUIShip.CreateButton(go.transform, new Rect(0f, 0f, 30f, 28f),
                "✕", new Color(0.5f, 0.22f, 0.22f, 1f), WHITE, FS_SM, new Action(Close));
        }

        // ── content ───────────────────────────────────────────────────────────

        protected override void BuildContent(RectTransform contentRoot)
        {
            ContentPosition = new Vector3(190.6421f, 4.4f, 0f);
            ContentScale = new Vector3(1.0473f, 1f, 1f);
            Pivot = new Vector2(0f, 0.5f);
            TitlePosition = new Vector3(32.5674f, -1f, 0f);
            TitleScale = new Vector3(1.1818f, 1.3491f, 1f);

            float w = WindowWidth - PAD * 2f;
            float y = PAD * 0.5f;

            var subtabs = Subtabs();
            if (_subtab >= subtabs.Length) _subtab = 0; // a registered extra vanished between opens

            // ── carousel header: ‹  Style  › ──
            float arrow = 22f;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, arrow, 22f),
                "‹", BTN_ARROW, WHITE, FS_BODY, new Action(() => CycleSubtab(-1)));
            MakeLabel(contentRoot, new Rect(PAD + arrow, y, w - arrow * 2f, 22f),
                subtabs[_subtab], FS_BODY, WHITE, TextAnchor.MiddleCenter);
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + w - arrow, y, arrow, 22f),
                "›", BTN_ARROW, WHITE, FS_BODY, new Action(() => CycleSubtab(+1)));
            y += 26f;

            _countLabel = MakeLabel(contentRoot, new Rect(PAD, y, w - 24f, 14f),
                CountText(), FS_SM, new Color(1f, 1f, 1f, 0.72f));
            y += 18f;

            MakeSeparator(contentRoot, new Rect(PAD, y, w, 1f));
            y += 6f;

            switch (_subtab)
            {
                case 0: BuildRecolour(contentRoot, w, ref y); break;
                case 1: BuildScale(contentRoot, w, ref y); break;
                case 2: BuildMaterial(contentRoot, w, ref y); break;
                default: BuildExtra(contentRoot, w, ref y); break;
            }

            // ── footer: undo + redo (left) + status (right) ──
            float footY = WindowHeight - TITLE_H - 24f;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD, footY, 64f, 20f),
                $"UNDO ({BatchEditHistory.Count})", BTN_UNDO, WHITE, FS_SM, new Action(DoUndo));
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + 68f, footY, 64f, 20f),
                $"REDO ({BatchEditHistory.RedoCount})", BTN_UNDO, WHITE, FS_SM, new Action(DoRedo));
            _statusLabel = MakeLabel(contentRoot, new Rect(PAD + 138f, footY, w - 138f, 20f), "", FS_SM, HINT_COL);
        }

        // ── recolour subtab ──────────────────────────────────────────────────

        private void BuildRecolour(RectTransform root, float w, ref float y)
        {
            // mode carousel:  ‹ set to colour / modify ›
            float arrow = 20f;
            MakeLabel(root, new Rect(PAD, y, 36f, 20f), "mode", FS_SM, HINT_COL);
            UGUIShip.CreateButton(root, new Rect(PAD + 40f, y, arrow, 20f), "‹", BTN_ARROW, WHITE, FS_BODY,
                new Action(() => CycleRecolourMode(-1)));
            _recolourModeLabel = MakeLabel(root, new Rect(PAD + 40f + arrow, y, w - 40f - arrow * 2f - 40f, 20f),
                RecolourModeName(_recolourMode), FS_SM, WHITE, TextAnchor.MiddleCenter);
            UGUIShip.CreateButton(root, new Rect(PAD + w - 40f - arrow, y, arrow, 20f), "›", BTN_ARROW, WHITE, FS_BODY,
                new Action(() => CycleRecolourMode(+1)));
            y += 26f;

            if (_recolourMode == RecolourMode.SetColour) BuildRecolourSet(root, w, ref y);
            else BuildRecolourModify(root, w, ref y);

            y += 4f;
            UGUIShip.CreateButton(root, new Rect(PAD, y, w, 24f),
                "APPLY", BTN_APPLY, WHITE, FS_SM, new Action(CommitColourEntry));
        }

        // "set to colour" — RGB sliders, live preview onto the whole selection.
        private void BuildRecolourSet(RectTransform root, float w, ref float y)
        {
            var pvGo = new GameObject("Preview");
            pvGo.transform.SetParent(root, false);
            UGUIShip.SetPixelRect(pvGo.AddComponent<RectTransform>(), new Rect(w - 20f + PAD, y, 20f, 20f));
            _preview = pvGo.AddComponent<Image>();
            _preview.color = _colour;

            UGUIShip.CreateSlider(root, PAD, y, w - 26f, "R", _colour.r, 16f, 4f, FS_SM,
                new Action<float>(v => { _colour.r = v; PreviewColourSet(); }),
                new Color(1f, 0.4f, 0.4f), new Color(1f, 0.3f, 0.3f));
            y += 22f;
            UGUIShip.CreateSlider(root, PAD, y, w, "G", _colour.g, 16f, 4f, FS_SM,
                new Action<float>(v => { _colour.g = v; PreviewColourSet(); }),
                new Color(0.4f, 1f, 0.4f), new Color(0.3f, 1f, 0.3f));
            y += 22f;
            UGUIShip.CreateSlider(root, PAD, y, w, "B", _colour.b, 16f, 4f, FS_SM,
                new Action<float>(v => { _colour.b = v; PreviewColourSet(); }),
                new Color(0.4f, 0.6f, 1f), new Color(0.3f, 0.5f, 1f));
            y += 26f;
        }

        // "modify" — brightness / contrast / hue / saturation adjust each object's OWN colour. sliders
        // are 0..1: signed params map 0.5→0 (no change), hue maps 0..1→0..360°.
        private void BuildRecolourModify(RectTransform root, float w, ref float y)
        {
            _preview = null; // no flat-colour preview chip in modify mode
            UGUIShip.CreateSlider(root, PAD, y, w, "Bright", _modBright * 0.5f + 0.5f, 16f, 4f, FS_SM,
                new Action<float>(v => { _modBright = (v - 0.5f) * 2f; PreviewColourModify(); }),
                new Color(0.9f, 0.9f, 0.6f), new Color(0.8f, 0.8f, 0.5f));
            y += 22f;
            UGUIShip.CreateSlider(root, PAD, y, w, "Contr", _modContrast * 0.5f + 0.5f, 16f, 4f, FS_SM,
                new Action<float>(v => { _modContrast = (v - 0.5f) * 2f; PreviewColourModify(); }),
                new Color(0.7f, 0.7f, 0.7f), new Color(0.6f, 0.6f, 0.6f));
            y += 22f;
            UGUIShip.CreateSlider(root, PAD, y, w, "Hue", _modHue / 360f, 16f, 4f, FS_SM,
                new Action<float>(v => { _modHue = v * 360f; PreviewColourModify(); }),
                new Color(0.8f, 0.5f, 0.9f), new Color(0.7f, 0.4f, 0.8f));
            y += 22f;
            UGUIShip.CreateSlider(root, PAD, y, w, "Sat", _modSat * 0.5f + 0.5f, 16f, 4f, FS_SM,
                new Action<float>(v => { _modSat = (v - 0.5f) * 2f; PreviewColourModify(); }),
                new Color(0.5f, 0.9f, 0.7f), new Color(0.4f, 0.8f, 0.6f));
            y += 26f;
        }

        // ── scale subtab ─────────────────────────────────────────────────────

        private void BuildScale(RectTransform root, float w, ref float y)
        {
            // the X/Y/Z nudge rows go in their own container with a CanvasGroup, so "from selected"
            // with no pivot yet can fade + disable them (scaling would do nothing meaningful) while
            // the mode carousel below stays live so you can still switch modes.
            var rowsGo = new GameObject("ScaleRows");
            rowsGo.transform.SetParent(root, false);
            var rowsRt = rowsGo.AddComponent<RectTransform>();
            rowsRt.anchorMin = Vector2.zero; rowsRt.anchorMax = Vector2.one;
            rowsRt.offsetMin = Vector2.zero; rowsRt.offsetMax = Vector2.zero;
            _scaleRowsGroup = rowsGo.AddComponent<CanvasGroup>();
            var rows = rowsRt;

            // one row per axis:  X   [-] 0.00 [+]   — offset always starts at 0 (no change); +/- add
            // or subtract directly onto the live scale, so it shrinks as easily as it grows.
            string[] axis = { "X", "Y", "Z" };
            for (int i = 0; i < 3; i++)
            {
                int a = i;
                float rx = PAD;
                MakeLabel(rows, new Rect(rx, y, 16f, 20f), axis[i], FS_SM, WHITE);
                rx += 20f;
                UGUIShip.CreateHoldButton(rows, new Rect(rx, y, 26f, 20f), "-", BTN_STEP, WHITE, FS_BODY,
                    new Action(() => NudgeVal(a, -1)), out _scaleHold[a * 2]);
                _scaleHold[a * 2].RepeatInterval = CreativeIncrements.Enabled ? CreativeIncrements.Speed : 0.05f;
                _scaleHold[a * 2].OnRelease = CommitScaleEntry;
                rx += 30f;
                _valLabels[a] = MakeLabel(rows, new Rect(rx, y, w - 20f - 30f * 2f, 20f), ValText(a), FS_SM, WHITE, TextAnchor.MiddleCenter);
                rx += w - 20f - 30f * 2f;
                UGUIShip.CreateHoldButton(rows, new Rect(rx, y, 26f, 20f), "+", BTN_STEP, WHITE, FS_BODY,
                    new Action(() => NudgeVal(a, +1)), out _scaleHold[a * 2 + 1]);
                _scaleHold[a * 2 + 1].RepeatInterval = CreativeIncrements.Enabled ? CreativeIncrements.Speed : 0.05f;
                _scaleHold[a * 2 + 1].OnRelease = CommitScaleEntry;
                y += 24f;
            }
            UpdateScaleRowsDim();

            y += 2f;
            // mode carousel:  ‹ mode ›
            float arrow = 20f;
            MakeLabel(root, new Rect(PAD, y, 40f, 20f), "mode", FS_SM, HINT_COL);
            UGUIShip.CreateButton(root, new Rect(PAD + 44f, y, arrow, 20f), "‹", BTN_ARROW, WHITE, FS_BODY,
                new Action(() => CycleMode(-1)));
            _modeLabel = MakeLabel(root, new Rect(PAD + 44f + arrow, y, w - 44f - arrow * 2f - 44f, 20f),
                ModeName(_scaleMode), FS_SM, WHITE, TextAnchor.MiddleCenter);
            UGUIShip.CreateButton(root, new Rect(PAD + w - 44f - arrow, y, arrow, 20f), "›", BTN_ARROW, WHITE, FS_BODY,
                new Action(() => CycleMode(+1)));
            y += 26f;

            MakeLabel(root, new Rect(PAD, y, w, 16f), "tap −/+ to nudge once, hold to shrink/grow continuously", FS_SM, HINT_COL);
            y += 16f;

            UGUIShip.CreateLinkText(root, new Rect(PAD, y, w, 16f), "change nudge amount/repeat speed →",
                new Action(() => BetterFGUIMan.Instance?.OpenCreativeArgs()), fontSize: FS_SM);
        }

        // live, no Apply press. Individual bakes into each object immediately, so it gets only this
        // session's share of the total (minus what previous commits already baked in). group modes set
        // the live owner's scale, which PERSISTS between holds until the game bakes it on deselect — so
        // they always get the full running total (factor = 1+total, can cross 0 into negative).
        private void ApplyScale()
        {
            _scaleEntry ??= BatchEditHistory.Begin("scale");
            var offset = _scaleMode == ScaleMode.Individual
                ? new Vector3(
                    _offsets[0] - _committedOffsets[0],
                    _offsets[1] - _committedOffsets[1],
                    _offsets[2] - _committedOffsets[2])
                : new Vector3(_offsets[0], _offsets[1], _offsets[2]);
            int n = BatchScale.ApplyInto(_scaleEntry, offset, _scaleMode);
            Status(n, "scaled");
        }

        // "from selected" needs an actual pivot object clicked before scaling means anything — while
        // there's none, fade the X/Y/Z rows and block them. all other modes are always ready.
        private bool ScaleReady() => _scaleMode != ScaleMode.FromSelected || BatchScale.PivotObject() != null;

        private void UpdateScaleRowsDim()
        {
            if (_scaleRowsGroup == null) return;
            bool ready = ScaleReady();
            _scaleRowsGroup.alpha = ready ? 1f : 0.35f;
            _scaleRowsGroup.interactable = ready;
            _scaleRowsGroup.blocksRaycasts = ready;
        }

        // resets the offset displays AND the committed baseline back to 0 — only called on undo/redo
        // (the running total no longer matches whatever history just changed). NOT called on commit:
        // the number persists across holds to show total scaling so far.
        private void ResetOffsets()
        {
            for (int i = 0; i < 3; i++)
            {
                _offsets[i] = 0f;
                _committedOffsets[i] = 0f;
                if (_valLabels[i] != null) _valLabels[i].text = ValText(i);
            }
        }

        // pushes the accumulated hold-session entry as a single undo step, so the next press starts a
        // fresh undo entry instead of growing this one. the display total persists — but the committed
        // baseline catches up to it, so the NEXT session only applies what's added after this point
        // (its snapshots already contain everything up to here).
        private void CommitScaleEntry()
        {
            if (_scaleEntry == null) return;
            BatchEditHistory.Push(_scaleEntry);
            _scaleEntry = null;
            for (int i = 0; i < 3; i++) _committedOffsets[i] = _offsets[i];
        }

        // ── material subtab ──────────────────────────────────────────────────

        private void BuildMaterial(RectTransform root, float w, ref float y)
        {
            MakeLabel(root, new Rect(PAD, y, w, 16f), "set surface on all selected:", FS_SM, HINT_COL);
            y += 22f;
            float half = (w - 6f) * 0.5f;
            UGUIShip.CreateButton(root, new Rect(PAD, y, half, 28f), "SLIME", BTN_APPLY, WHITE, FS_BODY,
                new Action(() => Status(BatchMaterial.SetSlime(), "slimed")));
            UGUIShip.CreateButton(root, new Rect(PAD + half + 6f, y, half, 28f), "NONE", BTN_STEP, WHITE, FS_BODY,
                new Action(() => Status(BatchMaterial.SetNone(), "cleared")));
            y += 32f;

            MakeLabel(root, new Rect(PAD, y, w, 16f), "visibility:", FS_SM, HINT_COL);
            y += 22f;
            UGUIShip.CreateButton(root, new Rect(PAD, y, half, 28f), "VISIBLE", BTN_APPLY, WHITE, FS_BODY,
                new Action(() => Status(BatchVisibility.SetVisible(true), "shown")));
            UGUIShip.CreateButton(root, new Rect(PAD + half + 6f, y, half, 28f), "INVISIBLE", BTN_STEP, WHITE, FS_BODY,
                new Action(() => Status(BatchVisibility.SetVisible(false), "hidden")));
            y += 32f;

            MakeLabel(root, new Rect(PAD, y, w, 16f), "collision:", FS_SM, HINT_COL);
            y += 22f;
            UGUIShip.CreateButton(root, new Rect(PAD, y, half, 28f), "ON", BTN_APPLY, WHITE, FS_BODY,
                new Action(() => Status(BatchCollision.SetCollisionEnabled(true), "collidable")));
            UGUIShip.CreateButton(root, new Rect(PAD + half + 6f, y, half, 28f), "OFF", BTN_STEP, WHITE, FS_BODY,
                new Action(() => Status(BatchCollision.SetCollisionEnabled(false), "non-collidable")));
        }

        // ── registered extra subtab ──────────────────────────────────────────

        // hands the external module a context and lets it lay out its own body from y downward. index into
        // Extras is offset by the built-in count. wrapped so a throwing module draws an error line instead
        // of taking the whole window down.
        private void BuildExtra(RectTransform root, float w, ref float y)
        {
            int idx = _subtab - BUILTIN_SUBTABS.Length;
            var extras = BatchSubtabRegistry.Extras;
            if (idx < 0 || idx >= extras.Count) return;

            var ctx = new BatchSubtabContext
            {
                Root = root,
                Width = w,
                Y = y,
                SelectionCount = BatchRecolour.SelectionCount(),
                SetStatus = (msg, ok) => SetStatus(msg, ok ? OK_COL : HINT_COL),
                MakeLabel = (parent, rect, text, fs, col, anchor) => MakeLabel(parent, rect, text, fs, col, anchor),
            };
            try { extras[idx].Build(ctx); }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"batch subtab '{extras[idx].Name}' threw while building: {ex}");
                MakeLabel(root, new Rect(PAD, y, w, 16f), "this page errored, check the log", FS_SM, HINT_COL);
            }
            y = ctx.Y;
        }

        // ── colour preview session ────────────────────────────────────────────

        // opens a preview session (snapshots each RGB object's original colour once) if one isn't
        // already open. re-applying colour then always recomputes from these originals, and the single
        // undo entry is these same originals.
        private void EnsureColourSession()
        {
            if (_colourEntry != null) return;
            BatchRecolour.SnapshotOriginals(_colourOriginals);
            if (_colourOriginals.Count == 0) return;
            var entry = BatchEditHistory.Begin(_recolourMode == RecolourMode.Modify ? "modify colour" : "recolour");
            foreach (var kv in _colourOriginals)
                entry.Snaps.Add(new BatchEditHistory.ObjectSnap { Obj = kv.Key, Colour = kv.Value });
            _colourEntry = entry;
            _colourSessionSelCount = BatchRecolour.SelectionCount();
        }

        private void PreviewColourSet()
        {
            if (_preview != null) _preview.color = _colour;
            EnsureColourSession();
            BatchRecolour.SetPreview(_colourOriginals, _colour);
            Status(_colourOriginals.Count, "recoloured");
        }

        private void PreviewColourModify()
        {
            EnsureColourSession();
            BatchRecolour.ModifyPreview(_colourOriginals, _modBright, _modContrast, _modHue, _modSat);
            Status(_colourOriginals.Count, "modified");
        }

        // pushes the open preview session as one undo entry and clears it. called on apply, subtab/mode
        // switch, window close, and selection change — the change stays applied, this just checkpoints it.
        private void CommitColourEntry()
        {
            if (_colourEntry == null) return;
            BatchEditHistory.Push(_colourEntry);
            _colourEntry = null;
            _colourOriginals.Clear();
            // reset modify sliders so the next session starts from "no change" (set-mode keeps its colour)
            _modBright = _modContrast = _modHue = _modSat = 0f;
        }

        // ── carousel / control handlers ──────────────────────────────────────

        private void CycleSubtab(int d)
        {
            CommitColourEntry(); // leaving Recolour checkpoints any pending edit
            int len = Subtabs().Length;
            _subtab = (_subtab + d + len) % len;
            RebuildContent();
        }

        private void CycleMode(int d)
        {
            _scaleMode = (ScaleMode)(((int)_scaleMode + d + 4) % 4);
            RebuildContent();
        }

        private void CycleRecolourMode(int d)
        {
            CommitColourEntry(); // switching set/modify checkpoints the pending edit first
            _recolourMode = (RecolourMode)(((int)_recolourMode + d + 2) % 2);
            RebuildContent();
        }

        private static string RecolourModeName(RecolourMode m) => m switch
        {
            RecolourMode.SetColour => "set to colour", RecolourMode.Modify => "modify", _ => "?"
        };

        // step size defaults to 0.25, or the creative increments step when that override is on, so
        // scale nudges match the editor's own params. adds/subtracts straight onto the live scale and
        // applies immediately — no separate Apply press.
        private void NudgeVal(int a, int dir)
        {
            float step = CreativeIncrements.Enabled ? CreativeIncrements.Step : 0.25f;
            _offsets[a] += dir * step;
            if (_valLabels[a] != null) _valLabels[a].text = ValText(a);
            ApplyScale();
        }

        // flush any pending live edit (scale hold / colour preview) into the undo stack so undo/redo
        // act on a settled history, not a half-open session.
        private void CommitPending()
        {
            CommitScaleEntry();
            CommitColourEntry();
        }

        private void DoUndo()
        {
            CommitPending();
            BatchScale.ResetOwnerScale(); // restores write straight to the objects — clear the live parent multiplier first
            string msg = BatchEditHistory.Undo();
            SetStatus(msg ?? "nothing to undo", msg != null ? OK_COL : HINT_COL);
            ResetOffsets(); // reverted scale no longer matches the running total
            RebuildContent(); // refresh undo/redo counters
        }

        private void DoRedo()
        {
            CommitPending();
            BatchScale.ResetOwnerScale();
            string msg = BatchEditHistory.Redo();
            SetStatus(msg ?? "nothing to redo", msg != null ? OK_COL : HINT_COL);
            ResetOffsets();
            RebuildContent();
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static string ModeName(ScaleMode m) => m switch
        {
            ScaleMode.FromOrigin => "from 0,0,0", ScaleMode.Individual => "individual", ScaleMode.FromCenter => "from center", ScaleMode.FromSelected => "from selected", _ => "?"
        };

        private string ValText(int a) => (_offsets[a] > 0f ? "+" : "") + _offsets[a].ToString("0.##");

        private void Status(int n, string verb)
        {
            SetStatus(n > 0 ? $"{verb} {n} object(s)" : "nothing applicable in selection", n > 0 ? OK_COL : HINT_COL);
            if (_countLabel != null) _countLabel.text = CountText();
        }

        private static string CountText() => BatchRecolour.SelectionCount() + " object(s) selected";

        private void SetStatus(string text, Color col)
        {
            if (_statusLabel == null) return;
            _statusLabel.text = text;
            _statusLabel.color = col;
        }
    }
}
