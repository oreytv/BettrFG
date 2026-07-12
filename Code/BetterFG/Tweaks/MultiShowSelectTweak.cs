using System;
using System.Collections.Generic;
using BetterFG.Core;
using FGClient;
using FGClient.ShowSelector;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Tweaks
{
    // brings back the old multi-show queue. confirming a show tile no longer matchmakes you straight
    // in - it just pins that show into a set (works with whatever confirm keybind, mouse/kb/pad, since
    // we hook the tile's OnShowConfirmed, not a click). inside the selector there's no play button, so
    // we float the Report nav-prompt top-left ("Start Queue") while shows are on screen; pressing it
    // fires the game's confirm/play flow, and GetPlayerMatchmakingAuthToken's showIds list gets every pinned show
    // spliced in (the request always took a List<string>; the server never stopped accepting several).
    // pinned tiles get a green outline so you can see the set.
    public class MultiShowSelectTweak : BfgTweak
    {
        public MultiShowSelectTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "multi_show_select";
        public override string TweakLabel => "Multi-Show Queue";
        public override bool DefaultEnabled => false;
        public override string TweakTooltip => "Confirm shows to add them to the set instead of matchmaking, then hit the Start Queue prompt to queue for all of them at once.";

        public static MultiShowSelectTweak Instance { get; private set; }
        void Awake() => Instance = this;

        static readonly Color PinColour = new Color(0.2f, 1f, 0.5f, 1f);
        const string StartLabelKey = "bfg_multishow_start";

        // pinned show ids -> display name (name is just for logging)
        readonly Dictionary<string, string> _pinned = new Dictionary<string, string>();
        // live tiles this browse session, so we can (re)apply outlines as the list rebuilds
        readonly List<ShowSelectorShowTileViewModel> _tiles = new List<ShowSelectorShowTileViewModel>();
        NavPromptHandle _startPrompt;

        // patched SetIndividualShowData routes here for every tile the game builds
        public static void OnTileData(ShowSelectorShowTileViewModel tile)
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled || tile == null) return;
            if (!inst._tiles.Contains(tile)) inst._tiles.Add(tile);
            inst.ApplyOutline(tile);
        }

        // set true for one confirm when the Start Queue prompt wants the real launch to go through
        static bool _allowLaunch;

        // tile confirm. returns true when the patch should swallow the confirm (pin only, no matchmake).
        // false = let the game confirm normally (tweak off, or we're intentionally launching).
        public static bool OnTileConfirm(ShowSelectorShowTileViewModel tile)
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled || tile == null) return false;
            if (_allowLaunch) { _allowLaunch = false; return false; } // real launch, don't swallow
            inst.TogglePin(tile);
            return true;
        }

        // the matchmake request. native fills showIds with the single selected show; we tack ours on
        public static void AugmentShowIds(Il2CppSystem.Collections.Generic.List<string> showIds)
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled || showIds == null || inst._pinned.Count == 0) return;

            int added = 0;
            foreach (var kv in inst._pinned)
            {
                if (showIds.Contains(kv.Key)) continue;
                showIds.Add(kv.Key);
                added++;
            }
            if (added > 0)
                Plugin.Log.LogInfo($"multi-show queue: sending {showIds.Count} shows ({added} pinned on top of the selection)");
        }

        void TogglePin(ShowSelectorShowTileViewModel tile)
        {
            var show = tile.ShowData?.ShowSelectorShow;
            string id = show?.Id;
            if (string.IsNullOrEmpty(id)) return;

            if (_pinned.Remove(id))
                Plugin.Log.LogInfo($"unpinned {show.ShowName} — {_pinned.Count} left in the queue set");
            else
            {
                _pinned[id] = show.ShowName;
                Plugin.Log.LogInfo($"pinned {show.ShowName} — {_pinned.Count} shows queued now");
            }
            ApplyOutline(tile);
        }

        void Update()
        {
            if (!IsEnabled) return;

            // selector "open" == we have live tiles on screen
            bool selectorOpen = false;
            for (int i = _tiles.Count - 1; i >= 0; i--)
            {
                var t = _tiles[i];
                if (t == null) { _tiles.RemoveAt(i); continue; }
                if (t.gameObject.activeInHierarchy) { selectorOpen = true; break; }
            }

            if (!selectorOpen || _pinned.Count == 0)
            {
                DestroyPrompt();
                return;
            }

            if (_startPrompt == null || !_startPrompt.IsAlive)
            {
                _startPrompt = NavPromptCore.From(NavPrompt.Report)
                    .WithLabel($"Start Queue ({_pinned.Count})", StartLabelKey + "_" + _pinned.Count)
                    .AnchoredAt(NavPromptAnchor.TopLeft)
                    .OnOwnCanvas()
                    .PollActions(RewiredConsts.Action.Menu_Report)
                    // the selector menu owns UI focus, so the default gameplay-focus gate would eat the press
                    .AllowWhileUnfocused()
                    .SpawnOn(null);
            }

            if (_startPrompt != null && _startPrompt.IsPressed())
                LaunchQueue();
        }

        void LaunchQueue()
        {
            // launch from a PINNED tile, not whatever's hovered — the primary must be part of the set.
            // prefer one that's also highlighted so it feels natural, else just take any pinned tile.
            ShowSelectorShowTileViewModel primary = null;
            for (int i = 0; i < _tiles.Count; i++)
            {
                var t = _tiles[i];
                var id = t?.ShowData?.ShowSelectorShow?.Id;
                if (string.IsNullOrEmpty(id) || !_pinned.ContainsKey(id)) continue;
                if (t.Selected) { primary = t; break; }
                if (primary == null) primary = t;
            }
            if (primary == null)
            {
                Plugin.Log.LogWarning("start queue pressed but none of the pinned shows are on screen to launch from");
                return;
            }

            Plugin.Log.LogInfo($"starting multi-show queue, {_pinned.Count} shows, primary is {primary.ShowData?.ShowSelectorShow?.ShowName}");
            _allowLaunch = true;
            primary.OnShowConfirmed();
            DestroyPrompt();
        }

        void DestroyPrompt()
        {
            _startPrompt?.Destroy();
            _startPrompt = null;
        }

        // our own overlay child so we don't fight the game's frame/fill colour logic
        void ApplyOutline(ShowSelectorShowTileViewModel tile)
        {
            var id = tile.ShowData?.ShowSelectorShow?.Id;
            bool pinned = !string.IsNullOrEmpty(id) && _pinned.ContainsKey(id);

            var existing = tile.transform.Find("BFG_PinOutline");
            if (!pinned)
            {
                if (existing != null) existing.gameObject.SetActive(false);
                return;
            }
            if (existing != null) { existing.gameObject.SetActive(true); return; }

            var go = new GameObject("BFG_PinOutline");
            go.transform.SetParent(tile.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            const float thick = 5f;
            MakeEdge(go.transform, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -thick), Vector2.zero);   // top
            MakeEdge(go.transform, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, thick));   // bottom
            MakeEdge(go.transform, Vector2.zero, new Vector2(0f, 1f), Vector2.zero, new Vector2(thick, 0f));   // left
            MakeEdge(go.transform, new Vector2(1f, 0f), Vector2.one, new Vector2(-thick, 0f), Vector2.zero);   // right
        }

        void MakeEdge(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offMin, Vector2 offMax)
        {
            var e = new GameObject("Edge");
            e.transform.SetParent(parent, false);
            var rt = e.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offMin;
            rt.offsetMax = offMax;
            var img = e.AddComponent<Image>();
            img.color = PinColour;
            img.raycastTarget = false;
        }

        public override void DisableTweak()
        {
            DestroyPrompt();
            _pinned.Clear();
            for (int i = 0; i < _tiles.Count; i++)
            {
                var t = _tiles[i];
                if (t == null) continue;
                var o = t.transform.Find("BFG_PinOutline");
                if (o != null) UnityEngine.Object.Destroy(o.gameObject);
            }
            _tiles.Clear();
        }
    }
}
