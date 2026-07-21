using System;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Services;
using BetterFG.UI;
using FG.Common;
using LevelEditor;
using Rewired;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Image = UnityEngine.UI.Image;
using RawImage = UnityEngine.UI.RawImage;

namespace BetterFG.Tweaks
{
    // Fall Guys' creative sticker picker is a one-at-a-time carousel: ~60 shapes, no preview, arrow through
    // them forever. This turns the sticker row into a button — press confirm on it and the whole set opens as
    // a grid of live previews you steer with keyboard, mouse or pad. Moving the highlight live-updates the
    // real decal (the game's own SetRangeIndex, which also saves); confirm keeps it, Back/Escape restores.
    //
    // No new Harmony patch and no BettrFG canvas: the grid lives inside the game's parameter-menu canvas.
    // While it's open we disable the parameter menu's own MenuInputHandler (so the carousel/rows are locked
    // until you pick or discard) but leave Rewired alone, and drive navigation through the game's own nav
    // actions off that handler's Rewired player — so d-pad, stick and keyboard all work exactly like the
    // rest of the parameter menu. Previews are the real decal rendered offscreen, so they're always correct.
    public class BetterStickerSelectionTweak : BfgTweak
    {
        public BetterStickerSelectionTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "better_sticker_selection";
        public override string TweakLabel => "Better Sticker Selection";
        public override bool DefaultEnabled => true;
        public override string TweakTooltip =>
            "In the creative parameter menu, press confirm on the sticker row to browse every sticker in a grid with previews. Move the highlight to preview it live, confirm to keep, Back to cancel.";

        public static BetterStickerSelectionTweak Instance { get; private set; }
        void Awake() => Instance = this;

        private StickerGridView _grid;
        private LevelEditorRangeParameter _range;
        private RenderTexture[] _previews;
        private int _original;

        private MenuInputHandler _handler;   // param-menu nav, disabled while the grid is up
        private MenuInputHandler _reArm;     // handler waiting to be switched back on once the keys are up
        private CanvasGroup _rows;           // the parameter rows, faded out while the grid is up
        private float _rowsAlpha;
        private Player _player;
        private int _actH, _actV, _actSubmit, _actCancel;

        private int _tipFor = -1;   // node index the hint is currently tracking
        private float _tipTimer;
        private bool _tipShown;

        private const float FirstRepeat = 0.32f;
        private const float NextRepeat = 0.09f;
        private int _repX, _repY;
        private float _repTimer;

        void Update()
        {
            if (!IsEnabled)
            {
                if (_grid != null) Close(false);
                ClearTip();
                return;
            }

            if (_grid != null) { TickOpen(); return; }
            if (_reArm != null) { TickReArm(); return; }

            if (!UnityRoundLoader.InLevelEditor) { ClearTip(); return; }
            if (!LevelEditorParameterMenuViewModel.IsParametersScreenOpen()) { ClearTip(); return; }
            TryOpenOnStickerRow();
        }

        private void TryOpenOnStickerRow()
        {
            var vm = LevelEditorParameterMenuViewModel._instance;
            if (vm == null) { ClearTip(); return; }

            if (vm.SelectedIndex != _tipFor) { ClearTip(); _tipFor = vm.SelectedIndex; }

            var handler = Handler(vm);
            if (handler == null) return;

            if (!IsStickerRow(vm, out var range, out var decal, out int count, out var root)) { ClearTip(); return; }

            // hint rides on the row itself, after the same hover delay the rest of our tooltips use
            _tipTimer += Time.unscaledDeltaTime;
            if (!_tipShown && _tipTimer >= Tooltip.HoverDelay)
            {
                var row = RowAt(vm, vm.SelectedIndex);
                if (row != null)
                {
                    var rt = row.TryCast<RectTransform>();
                    Vector3 top = rt != null
                        ? rt.TransformPoint(new Vector3(rt.rect.center.x, rt.rect.yMax, 0f))
                        : row.position;
                    BetterFGUIMan.Instance?.ShowTooltipOver(
                        "Press space or the usual confirm button on your joystick to see a better selection",
                        top, 3600f);
                    _tipShown = true;
                }
            }

            var player = handler._rewiredPlayer;
            if (!KbConfirmDown() && !(player != null && player.GetButtonDown(handler.SubmitAction))) return;

            ClearTip();
            Open(vm, handler, range, decal, count, root);
        }

        // the row GameObject backing node `index` — Content's children sit in the same order as NodeEntries
        private static Transform RowAt(LevelEditorParameterMenuViewModel vm, int index)
        {
            var rows = MenuPanel(vm.transform)?.Find("Content");
            return rows != null && index >= 0 && index < rows.childCount ? rows.GetChild(index) : null;
        }

        private void ClearTip()
        {
            if (_tipShown) BetterFGUIMan.Instance?.HideTooltip();
            _tipShown = false;
            _tipTimer = 0f;
        }

        // is the highlighted parameter row a decal's sticker (range) node?
        private static bool IsStickerRow(LevelEditorParameterMenuViewModel vm,
            out LevelEditorRangeParameter range, out LevelEditorDecal decal, out int count, out Transform root)
        {
            range = null; decal = null; count = 0; root = null;

            var sel = LevelEditorManager.Instance?.SelectedObject;
            if (sel == null) return false;
            range = sel.GetComponentInChildren<LevelEditorRangeParameter>(true);
            decal = sel.GetComponentInChildren<LevelEditorDecal>(true);
            if (range == null || decal == null) return false;

            var entries = vm.NodeEntries;
            int si = vm.SelectedIndex;
            if (entries == null || si < 0 || si >= entries.Length) return false;
            var node = entries[si]?._nodeData;
            var names = range.RangeSettingNames;
            count = names != null ? names.Length : 0;
            if (count <= 1 || node?.SelectionItems == null || node.SelectionItems.Count != count) return false;

            root = sel.transform;
            return true;
        }

        private void Open(LevelEditorParameterMenuViewModel vm, MenuInputHandler handler,
            LevelEditorRangeParameter range, LevelEditorDecal decal, int count, Transform root)
        {
            _range = range;
            _original = range.RangeSettingIndex;

            _handler = handler;
            _player = handler._rewiredPlayer;
            _actH = handler.HorizontalAction; _actV = handler.VerticalAction;
            _actSubmit = handler.SubmitAction; _actCancel = handler.CancelAction;

            _previews = StickerPreviewRig.Capture(range, decal, count, root);

            var vmRoot = vm.transform;
            var panel = MenuPanel(vmRoot);
            if (panel == null) { StickerPreviewRig.Release(_previews); _previews = null; return; }

            // the grid takes the panel over, so the parameter rows step aside while it's up. fade them via a
            // CanvasGroup rather than SetActive — deactivating the rows makes the menu rebuild and snap its
            // selection back to the first row (the colour swatch) when they come back.
            var rows = panel.Find("Content");
            if (rows != null)
            {
                _rows = rows.GetComponent<CanvasGroup>() ?? rows.gameObject.AddComponent<CanvasGroup>();
                _rowsAlpha = _rows.alpha;
                _rows.alpha = 0f;
                _rows.blocksRaycasts = false;
            }

            _grid = new StickerGridView(panel, count, _original, _previews, RowSprite(vmRoot));
            _grid.OnHighlight = i => { try { _range.SetRangeIndex(i); } catch { } EditorSound(Snd.Move); };
            _grid.OnConfirm = i => { try { _range.SetRangeIndex(i); } catch { } EditorSound(Snd.Confirm); Close(false); };
            _grid.OnCancel = () => Close(true);

            _repX = _repY = 0; _repTimer = 0f;
            handler.enabled = false; // lock the parameter menu until pick/discard
            EditorSound(Snd.Open);
            Plugin.Log?.LogInfo($"sticker grid open — {count} shapes, started on {_original}");
        }

        private void TickOpen()
        {
            // cancel first, even if it also tears the popup down, so a discard still restores
            if (KbCancelDown() || (_player != null && _player.GetButtonDown(_actCancel))) { EditorSound(Snd.Cancel); Close(true); return; }

            _grid.Tick(Time.unscaledDeltaTime);

            // wheel through Rewired's mouse (axis 2 on its standard template: 0 X, 1 Y, 2 wheel) rather
            // than raw Unity input, so it goes through the same stack as everything else here
            var mouse = ReInput.isReady ? ReInput.controllers.Mouse : null;
            if (mouse != null && mouse.axisCount > 2)
            {
                float wheel = mouse.GetAxis(2);
                if (Mathf.Abs(wheel) > 0.0001f) _grid.ScrollByRows(-wheel * 0.6f);
            }
            if (!LevelEditorParameterMenuViewModel.IsParametersScreenOpen()) { Close(false); return; }
            if (KbConfirmDown() || (_player != null && _player.GetButtonDown(_actSubmit))) { _grid.OnConfirm?.Invoke(_grid.Highlight); return; }

            ReadDir(out int dx, out int dy);
            StepRepeat(dx, dy);
        }

        // wait out the closing press before the parameter menu listens again
        private void TickReArm()
        {
            bool held = Input.GetKey(KeyCode.Return) || Input.GetKey(KeyCode.KeypadEnter)
                        || Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.Escape);
            if (!held && _player != null)
                held = _player.GetButton(_actSubmit) || _player.GetButton(_actCancel);
            if (held) return;

            _reArm.enabled = true;
            _reArm = null;
            _player = null;
        }

        private void StepRepeat(int dx, int dy)
        {
            if (dx == 0 && dy == 0) { _repX = _repY = 0; _repTimer = 0f; return; }
            if (dx != _repX || dy != _repY)
            {
                _grid.Move(dx, dy);
                _repX = dx; _repY = dy; _repTimer = FirstRepeat;
                return;
            }
            _repTimer -= Time.unscaledDeltaTime;
            if (_repTimer <= 0f) { _grid.Move(dx, dy); _repTimer = NextRepeat; }
        }

        private void Close(bool restore)
        {
            if (restore && _range != null) { try { _range.SetRangeIndex(_original); } catch { } }
            if (_rows != null) { _rows.alpha = _rowsAlpha; _rows.blocksRaycasts = true; }
            // hand the menu back only once confirm/cancel are released. switching it on in the same frame
            // let the game act on the very press that closed us — cancel tore down the whole parameter
            // screen, and confirm walked the selection on into the colour palette.
            _reArm = _handler;
            _grid?.Destroy();
            StickerPreviewRig.Release(_previews);
            _grid = null; _previews = null; _range = null; _handler = null; _rows = null;
        }

        // the editor's own FMOD cues, so the grid sounds like the rest of creative rather than like our menus
        private enum Snd { Open, Move, Confirm, Cancel }

        private static void EditorSound(Snd which)
        {
            var data = AudioManager.EventMasterData;
            if (data == null) return;
            string key;
            switch (which)
            {
                case Snd.Open: key = data.CreativeModeItemParamsEnter; break;
                case Snd.Move: key = data.CreativeModeLibraryTab; break;
                case Snd.Confirm: key = data.CreativeModeItemEditConfirm; break;
                default: key = data.CreativeModeItemParamsExit; break;
            }
            if (!string.IsNullOrEmpty(key)) AudioManager.PlayOneShot(key, Vector3.zero);
        }

        // the parameter menu's single nav handler
        private static MenuInputHandler Handler(LevelEditorParameterMenuViewModel vm)
        {
            var handlers = vm._inputHandlers;
            if (handlers == null || handlers.Count == 0) return null;
            return handlers[0];
        }

        // the parameter window box itself — the grid fills it, same transform CreativeUIPatches re-anchors
        private static RectTransform MenuPanel(Transform vmRoot)
        {
            foreach (var t in vmRoot.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == "UI_LevelEditorParametersMenuBG") return t.TryCast<RectTransform>();
            return null;
        }

        // FG's own parameter-row background sprite, so the grid reads native
        private static Sprite RowSprite(Transform vmRoot)
        {
            foreach (var img in vmRoot.GetComponentsInChildren<Image>(true))
                if (img != null && img.gameObject.name == "Background" && img.sprite != null) return img.sprite;
            return null;
        }

        // ── directional: game nav actions (d-pad/stick/keyboard) via Rewired, plus WASD/arrows ──
        private void ReadDir(out int dx, out int dy)
        {
            dx = dy = 0;
            if (_player != null)
            {
                if (_player.GetButton(_actH)) dx = 1; else if (_player.GetNegativeButton(_actH)) dx = -1;
                if (_player.GetButton(_actV)) dy = -1; else if (_player.GetNegativeButton(_actV)) dy = 1; // positive vertical = up = previous row
            }
            if (dx == 0)
                dx = (Held(KeyCode.RightArrow) || Held(KeyCode.D) ? 1 : 0) - (Held(KeyCode.LeftArrow) || Held(KeyCode.A) ? 1 : 0);
            if (dy == 0)
                dy = (Held(KeyCode.DownArrow) || Held(KeyCode.S) ? 1 : 0) - (Held(KeyCode.UpArrow) || Held(KeyCode.W) ? 1 : 0);
        }

        private static bool Held(KeyCode k) => Input.GetKey(k);
        private static bool KbConfirmDown() =>
            Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space);
        private static bool KbCancelDown() => Input.GetKeyDown(KeyCode.Escape);
    }

    // Correct previews by construction: render the real decal offscreen, once per shape. We drive the live
    // range parameter through every index, snap each to its own RenderTexture with a throwaway camera, then
    // put the decal back exactly where it started. It all runs synchronously in one frame and offscreen, so
    // the player never sees the decal cycle — and there's no CPU pixel work, so no long freeze like the old
    // atlas reader had.
    internal static class StickerPreviewRig
    {
        private const int Res = 128;
        private const int PreviewLayer = 31; // spare layer; camera renders only this so nothing else leaks in

        public static RenderTexture[] Capture(LevelEditorRangeParameter range, LevelEditorDecal decal, int count, Transform root)
        {
            var renderer = decal._renderer;
            if (renderer == null) { Plugin.Log?.LogWarning("sticker preview: decal has no renderer, cells will be blank"); return null; }

            var go = renderer.gameObject;
            int origLayer = go.layer;
            int original = range.RangeSettingIndex;
            bool origUnlit = decal._unlitModeEnabled;
            Color origColour = decal._decalColour;

            // previews show the shape itself, not this particular sticker: square it up and drop the tint so
            // a decal the player stretched wide (or coloured) doesn't skew every cell. restored below.
            // the stretch lives on the ROOT placeable's transform (e.g. 1.8, 0.9, 1) — squaring the decal
            // child alone does nothing, and the decal caches that root scale for its UVs, so SetScale too.
            Vector3 origScale = root.localScale;
            float uniform = Mathf.Max(0.0001f, Mathf.Max(origScale.x, Mathf.Max(origScale.y, origScale.z)));
            var square = new Vector3(uniform, uniform, uniform);
            root.localScale = square;
            try { decal.SetScale(square); } catch { }
            try { decal.SetColour(Color.white); } catch { }

            var camGo = new GameObject("BettrFG_StickerPreviewCam");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false; // we drive it by hand with Render()
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.orthographic = true;
            cam.cullingMask = 1 << PreviewLayer;
            cam.nearClipPlane = 0.001f;
            cam.farClipPlane = 50f;
            cam.allowHDR = false;
            cam.allowMSAA = false;

            var b = renderer.bounds;
            var t = renderer.transform;
            // view the face the sticker is actually printed on: sit BEHIND the quad's forward and look
            // along +forward. sitting in front and looking back showed the reverse side, mirroring every shape.
            Vector3 normal = t.forward;
            float reach = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
            camGo.transform.position = b.center - normal * (reach * 4f + 1f);
            camGo.transform.rotation = Quaternion.LookRotation(normal, t.up);

            // square pixels: force aspect 1 to match the square target, and size the view to the larger
            // in-plane extent. leaving aspect to default stretched wide decals across the whole cell.
            Vector3 e = b.extents, right = camGo.transform.right, up = camGo.transform.up;
            float halfW = Mathf.Abs(e.x * right.x) + Mathf.Abs(e.y * right.y) + Mathf.Abs(e.z * right.z);
            float halfH = Mathf.Abs(e.x * up.x) + Mathf.Abs(e.y * up.y) + Mathf.Abs(e.z * up.z);
            cam.aspect = 1f;
            cam.orthographicSize = Mathf.Max(0.01f, Mathf.Max(halfW, halfH)) * 1.08f;

            SetLayer(go, PreviewLayer);
            try { decal.SetUnlitMode(true); } catch { }

            var rts = new RenderTexture[count];
            for (int i = 0; i < count; i++)
            {
                try { range.SetRangeIndex(i); } catch { }
                // SetRangeIndex rebuilds the property block off the cached root scale, so re-square after it
                try { decal.SetScale(square); } catch { }
                var rt = new RenderTexture(Res, Res, 16, RenderTextureFormat.ARGB32) { wrapMode = TextureWrapMode.Clamp };
                rt.Create();
                cam.targetTexture = rt;
                cam.Render();
                rts[i] = rt;
            }

            cam.targetTexture = null;
            try { range.SetRangeIndex(original); } catch { }
            try { decal.SetUnlitMode(origUnlit); } catch { }
            try { decal.SetColour(origColour); } catch { }
            root.localScale = origScale;
            try { decal.SetScale(origScale); } catch { }
            SetLayer(go, origLayer);
            UnityEngine.Object.Destroy(camGo);
            return rts;
        }

        public static void Release(RenderTexture[] rts)
        {
            if (rts == null) return;
            foreach (var rt in rts) if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
        }

        private static void SetLayer(GameObject go, int layer)
        {
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;
        }
    }

    // The sticker grid: a scrollable board of live-rendered preview cells that fills the game's parameter
    // window, in place of the parameter rows (the tweak hides those while this is up). Columns and cell size
    // are derived from the panel's real width, so it fits whatever the editor's layout/UI scale gives us.
    // No MonoBehaviour of its own; the tweak's Update feeds it input and it raises confirm/cancel back up.
    internal class StickerGridView
    {
        public GameObject Root { get; private set; }
        public int Highlight { get; private set; }
        public int Count { get; private set; }

        public Action<int> OnHighlight;   // live preview: highlighted index changed
        public Action<int> OnConfirm;     // keep this index
        public Action OnCancel;           // restore + close

        private const float Pad = 12f;
        // the panel's top strip is taken by its own header, so the usable area starts lower than the rect.
        // measured against the live window: 50 from the top clears it, the bottom only needs the normal pad.
        private const float TopPad = 50f;
        private const float Gap = 6f;
        private const float TargetCell = 74f;
        private const float Inset = 10f;

        // vertical movement eases instead of snapping. driven from the tweak's Update via Tick().
        private const float ScrollTime = 1f;
        private float _scrollFrom, _scrollTarget, _scrollT = 1f;

        private int _columns;
        private float _cell;
        private float _viewH;
        private RectTransform _content;
        private Image[] _cellBgs;
        private RenderTexture[] _previews;
        private readonly Color _idle = new Color(0f, 0f, 0f, 0.30f);
        private readonly Color _sel = new Color(1f, 0.86f, 0.3f, 0.95f);

        public StickerGridView(RectTransform panel, int count, int current, RenderTexture[] previews, Sprite cellSprite)
        {
            Count = Mathf.Max(1, count);
            Highlight = Mathf.Clamp(current, 0, Count - 1);
            _previews = previews;
            Build(panel, cellSprite);
            Sync(false, snap: true);
        }

        private void Build(RectTransform panel, Sprite cellSprite)
        {
            float panelW = panel.rect.width, panelH = panel.rect.height;
            float availW = panelW - Pad * 2f;
            _columns = Mathf.Max(1, Mathf.FloorToInt((availW + Gap) / (TargetCell + Gap)));
            _cell = (availW - Gap * (_columns - 1)) / _columns;
            // ONE definition of the visible height, shared by the viewport rect and every scroll clamp.
            // it used to be derived separately from the panel, so the two disagreed and the last row could
            // never be reached (asking for 201 gave back 163).
            _viewH = panelH - TopPad - Pad;

            int rows = Mathf.CeilToInt(Count / (float)_columns);

            Root = new GameObject("BettrFG_StickerGrid");
            var rootRt = Root.AddComponent<RectTransform>();
            Root.transform.SetParent(panel, false);
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
            rootRt.localScale = new Vector3(0.94f, 0.94f, 0.94f);
            Root.transform.SetAsLastSibling();

            var vpGo = new GameObject("Viewport");
            var viewport = vpGo.AddComponent<RectTransform>();
            vpGo.transform.SetParent(Root.transform, false);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(Pad, Pad);
            viewport.offsetMax = new Vector2(-Pad, -TopPad);
            vpGo.AddComponent<RectMask2D>();

            var contentGo = new GameObject("Content");
            _content = contentGo.AddComponent<RectTransform>();
            contentGo.transform.SetParent(viewport, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = new Vector2(0f, rows * _cell + (rows - 1) * Gap + Inset * 2f);

            // deliberately NO ScrollRect: it clamps content against its own view bounds every LateUpdate,
            // which fought the positions written here and capped the scroll short of the last row. the
            // RectMask2D does the clipping and this class owns the position outright. the tweak feeds the
            // wheel in through ScrollByRows, read off Rewired's mouse like the rest of our input.

            _cellBgs = new Image[Count];
            for (int i = 0; i < Count; i++)
            {
                int col = i % _columns, row = i / _columns;
                var cellGo = new GameObject("Cell_" + i);
                var crt = cellGo.AddComponent<RectTransform>();
                cellGo.transform.SetParent(_content, false);
                crt.anchorMin = crt.anchorMax = new Vector2(0f, 1f);
                crt.pivot = new Vector2(0f, 1f);
                crt.sizeDelta = new Vector2(_cell, _cell);
                crt.anchoredPosition = new Vector2(col * (_cell + Gap), -(Inset + row * (_cell + Gap)));

                var cbg = cellGo.AddComponent<Image>();
                cbg.color = _idle;
                if (cellSprite != null) { cbg.sprite = cellSprite; cbg.type = Image.Type.Sliced; }
                cbg.raycastTarget = true;
                _cellBgs[i] = cbg;

                var pv = new GameObject("Preview");
                var prt = pv.AddComponent<RectTransform>();
                pv.transform.SetParent(cellGo.transform, false);
                prt.anchorMin = Vector2.zero; prt.anchorMax = Vector2.one;
                prt.offsetMin = new Vector2(6f, 6f);
                prt.offsetMax = new Vector2(-6f, -6f);
                var img = pv.AddComponent<RawImage>();
                var tex = _previews != null && i < _previews.Length ? _previews[i] : null;
                img.texture = tex;
                img.raycastTarget = false;
                img.color = tex != null ? Color.white : new Color(1f, 1f, 1f, 0.06f);

                WireCell(cellGo, i);
            }
        }

        private void WireCell(GameObject cell, int index)
        {
            var trig = cell.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(new Action<BaseEventData>(_ => SetHighlight(index, true)));
            trig.triggers.Add(enter);
            var click = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            click.callback.AddListener(new Action<BaseEventData>(_ => OnConfirm?.Invoke(index)));
            trig.triggers.Add(click);
        }

        public void Move(int dx, int dy)
        {
            int col = Highlight % _columns, row = Highlight / _columns;
            int rows = Mathf.CeilToInt(Count / (float)_columns);
            if (dx != 0) col = Mathf.Clamp(col + dx, 0, _columns - 1);
            if (dy != 0) row = Mathf.Clamp(row + dy, 0, rows - 1);
            SetHighlight(Mathf.Clamp(row * _columns + col, 0, Count - 1), true);
        }

        public void SetHighlight(int index, bool preview)
        {
            index = Mathf.Clamp(index, 0, Count - 1);
            if (index == Highlight && _cellBgs[index].color == _sel) return;
            Highlight = index;
            Sync(preview, snap: false);
        }

        private void Sync(bool preview, bool snap)
        {
            for (int i = 0; i < _cellBgs.Length; i++)
                _cellBgs[i].color = i == Highlight ? _sel : _idle;
            ScrollTo(Highlight, snap);
            if (preview) OnHighlight?.Invoke(Highlight);
        }

        // keep the highlighted row inside the viewport. snap on the first sync (opening frame), ease after.
        private void ScrollTo(int index, bool snap)
        {
            int row = index / _columns;
            float top = Inset + row * (_cell + Gap);
            float bottom = top + _cell;
            float y = _scrollT >= 1f ? _content.anchoredPosition.y : _scrollTarget;
            // keep Inset of slack on whichever edge we're scrolling toward, so the cell lands fully clear
            if (top - Inset < y) y = top - Inset;
            else if (bottom + Inset - y > _viewH) y = bottom + Inset - _viewH;
            float maxY = Mathf.Max(0f, _content.sizeDelta.y - _viewH);
            y = Mathf.Clamp(y, 0f, maxY);

            if (snap)
            {
                SetY(y);
                _scrollTarget = y; _scrollT = 1f;
                return;
            }
            if (Mathf.Approximately(y, _scrollTarget) && _scrollT < 1f) return;
            if (Mathf.Approximately(y, _content.anchoredPosition.y)) { _scrollTarget = y; _scrollT = 1f; return; }
            _scrollFrom = _content.anchoredPosition.y;
            _scrollTarget = y;
            _scrollT = 0f;
        }

        // free wheel scrolling: moves the view without dragging the highlight along, so it can leave the
        // selected cell behind (hovering a cell picks it back up). cancels any easing in flight.
        public void ScrollByRows(float rows)
        {
            _scrollT = 1f;
            SetY(_content.anchoredPosition.y + rows * (_cell + Gap), clampToHighlight: false);
        }

        // ease-out toward the target row. called every frame while the grid is open.
        public void Tick(float dt)
        {
            if (_scrollT >= 1f) return;
            _scrollT = Mathf.Min(1f, _scrollT + dt / ScrollTime);
            float e = 1f - Mathf.Pow(1f - _scrollT, 3f);
            SetY(Mathf.Lerp(_scrollFrom, _scrollTarget, e));
        }

        // the ease is slow enough that holding a direction outruns it, which left the selected cell half
        // off the top. so whatever the animation is doing, never let it park the highlight outside the
        // viewport: the eased value gets pulled into the band where that cell is fully visible.
        private void SetY(float y, bool clampToHighlight = true)
        {
            float maxY = Mathf.Max(0f, _content.sizeDelta.y - _viewH);
            if (clampToHighlight)
            {
                int row = Highlight / _columns;
                float top = Inset + row * (_cell + Gap);
                float lo = Mathf.Max(0f, top + _cell - _viewH);
                float hi = Mathf.Min(maxY, top);
                y = lo > hi ? Mathf.Clamp(y, 0f, maxY) : Mathf.Clamp(y, lo, hi);
            }
            else y = Mathf.Clamp(y, 0f, maxY);
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, y);
        }

        public void Destroy()
        {
            if (Root != null) UnityEngine.Object.Destroy(Root);
            Root = null;
        }
    }
}
