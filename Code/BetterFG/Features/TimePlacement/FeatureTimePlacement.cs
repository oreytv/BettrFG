using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.UI;
using BetterFG.Utilities;
using FallGuysLib.Players;
using FallGuysLib.Round;
using FG.Common;
using FGClient;
using FGClient.UI.Core;
using HarmonyLib;
using Mediatonic.Tools.MVVM;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PlayerUtils = FallGuysLib.Players.PlayerUtils;

namespace BetterFG.Features.TimePlacement
{
    // shows a live finish-order leaderboard during a round by cloning the squad scores
    // panel and repurposing each entry: position number on the left, player name on the right.
    // entries get filled in as players finish (HandlePlayerFinished patch lives in its own class).
    internal static class FeatureTimePlacement
    {
        public static readonly bfgfeature feature = new bfgfeature("timeplacement", "In-game leaderboard", false, new List<featuresetting>
        {
            // also host the list under PlayingState so it's visible during live gameplay.
            new featuresetting { id = "gameplay", label = "Show in gameplay", defaultOn = true },
            // split squads of 3+ across two lines (two names top, rest below).
            new featuresetting { id = "twolines", label = "Two lines for big squads", defaultOn = true },
        },
        onOpen: () => OnToggled(),
        onClosed: () => OnToggled(),
        onSettingChanged: (id, val) => OnToggled(),
        choices: new List<featurechoice>
        {
            new featurechoice
            {
                id = "showeliminated",
                label = "Show eliminated players",
                optionIds = new List<string> { "always", "survival", "never" },
                optionLabels = new List<string> { "Always", "Only in Survival", "Never" },
                defaultId = "always",
            },
            new featurechoice
            {
                id = "soloinsquad",
                label = "Prefer per-player score over squads",
                optionIds = new List<string> { "no", "yes" },
                optionLabels = new List<string> { "No", "Yes" },
                defaultId = "no",
                hint = "In squad rounds, show one row per player instead of combined squad scores.",
            },
        });

        // "always" | "survival" | "never" — how the leaderboard treats eliminated players.
        public static string ShowEliminated => feature.GetChoice("showeliminated");

        // in scoring squad rounds, show each player on their own row (live solo points) instead of
        // the squad-aggregated rows. survival rounds get a live toggle via the LE_Zoom_Out nav prompt
        // which flips _perRoundSoloOverride for the duration of the round only.
        static bool PreferSoloInSquad => _perRoundSoloOverride ?? (feature.GetChoice("soloinsquad") == "yes");
        static bool? _perRoundSoloOverride;
        static NavPromptHandle _soloToggleprompt;

        // ClientGameManager probes for the non-GameRules stuff (team / tag / score managers) go
        // through here. GameRules itself comes from FallGuysLib.Round.GameRulesUtils.
        static ClientGameManager Cgm() => GameRulesUtils.Cgm();

        // should eliminated (OUT) players be listed right now, per the dropdown? "always" yes,
        // "never" no, "survival" only in survival rounds.
        static bool ShowEliminatedNow()
        {
            string mode = ShowEliminated;
            if (mode == "never") return false;
            if (mode == "survival") return GameRulesUtils.IsSurvivalRound();
            return true;
        }

        public static bool On(string setting) => featureRegistry.IsOn("timeplacement", setting);

        // is the feature actually allowed to show right now? (just the master enable now).
        static bool Enabled => feature.enabled;

        // react to any in-menu toggle of the feature or its settings. enable -> bring the list back
        // and resume updating; disable -> hide everything and stop updating. if we're mid-round and
        // nothing's spawned yet, a fresh enable will spawn on the next SpawnList (round start) — but
        // if a round is already live we respawn right away so it appears without waiting.
        static void OnToggled()
        {
            if (Enabled)
            {
                _updating = true;
                if (_panels.Count == 0) SpawnList();   // nothing built yet — build it now
                else SetContainersActive(true);        // already built, just show it again
            }
            else
            {
                _updating = false;
                SetContainersActive(false);
            }
        }

        static void SetContainersActive(bool on)
        {
            foreach (var c in _containers)
                if (c != null) c.SetActive(on);
        }

        // always show names — you can't see squad numbers in-game, so "Squad N" is never useful.
        static bool ShowNames => true;
        static bool TwoLines => On("twolines");
        static bool ShowInGameplay => On("gameplay");

        // the GameStates container holding PlayingState/SpectatorState/BannersState/etc.
        const string GameStatesPath =
            "UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates";

        // the squad-scores list container (parent of Panel) — we clone THIS so we never touch the
        // game's real one, then repurpose the clone's Panel.
        const string ListContainerPath =
            "UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates/PlayingState/GameplayScoringViewModel/TopLeftContainer/CanvasSquadScoresList";

        // only show the placement list while these states are active (so it's hidden during normal
        // play and only pops up on the spectator / banners screens). PlayingState is added when the
        // "Show in gameplay" toggle is on so the list is visible during live play too.
        static string[] HostStates =>
            (ShowInGameplay || QualStatusOn)   // qualify-highlight is a live-play call, force PlayingState
                ? new[] { "PlayingState", "SpectatorState", "BannersState" }
                : new[] { "SpectatorState", "BannersState" };

        // how many spots to show on the leaderboard. user-set in the features tab, stored as a plain
        // string setting (the feature toggle system is bool-only). default 10, clamped to 1..16.
        internal const string MaxRowsKey = "feature.timeplacement.maxrows";
        internal const int MaxRowsDefault = 10;
        internal const int MaxRowsMin = 1;
        internal const int MaxRowsMax = 16;
        static int MaxRows
        {
            get
            {
                if (!int.TryParse(Services.SettingsService.Get(MaxRowsKey, MaxRowsDefault.ToString()), out int n))
                    n = MaxRowsDefault;
                return Mathf.Clamp(n, MaxRowsMin, MaxRowsMax);
            }
        }
        const float RowHeight = 34f;                     // fixed vertical spacing between rows

        // top-left anchored position of the panel, in canvas reference units (no scaleFactor
        // division — the CanvasScaler handles screen scaling). negative X pokes the panel's left
        // edge out past the left side of the screen so you can see it tucked into the corner.
        static readonly Vector2 PanelAnchoredPos = new Vector2(-26f, -93.5635f);

        // one clone per host state — the active state shows its copy.
        static readonly List<GameObject> _containers = new List<GameObject>();
        static readonly List<GameObject> _panels = new List<GameObject>();
        static GameObject _liveSourcePanel;              // the real PlayingState Panel — entry template source
        static GameObject _srcContainer;                 // the game's source CanvasSquadScoresList we template from
        static GameObject _bannersStateClone;            // our BannersState clone — banner patches reparent under this
        static GameObject _cachedTemplate;               // persistent copy of a real entry, kept forever so
                                                         // rounds we enter mid-spectate can still build the list
        static bool _entriesCaptured;                    // entries grabbed lazily after round start
        static bool _updating = true;                    // false = paused via the in-menu toggle; polls/refresh no-op
        // _entryPool[place] = the row in each panel for that place (parallel across panels)
        static readonly List<List<GameObject>> _entryPool = new List<List<GameObject>>();
        static int _nextPlace;                           // 1-based, increments as people finish
        static readonly HashSet<uint> _seenPlayers = new HashSet<uint>();
        // survival finals have no completedLevel flip, so we track eliminations from the server
        // progress message (succeeded == false). keyed by playerKey since squads are keyed that way.
        static readonly HashSet<string> _eliminatedKeys = new HashSet<string>();
        // playerKey -> finish/qualify time (seconds), filled as players succeed. drives the time
        // column on the qualify-highlight roster.
        static readonly Dictionary<string, float> _qualTimes = new Dictionary<string, float>();
        static int _soloSig;                             // running hash of the current solo leaderboard
        static int _lastSoloSig;                         // last logged signature, to dedupe the spam
        // score-change patches only flip this; the poll drains it once per frame. a hunt round fires
        // 20-30 score events in a single frame and each used to trigger a full ForceMeshUpdate repaint
        // (48 mesh rebuilds) synchronously inside the patch — that burst is the visible stutter. one
        // coalesced repaint per frame kills it while staying frame-fresh.
        static bool _soloDirty;
        // bumped every Reset. the poll coroutines capture the generation they were spawned for and
        // exit when it changes — `while (_panels.Count > 0)` alone leaks them, because Reset+SpawnList
        // clears and refills _panels inside one frame while the old coroutines are asleep in their
        // 0.5s wait, so they wake to a non-empty list and run forever. 3 leaked polls per round = the
        // game getting laggier every round played.
        static int _spawnGen;

        // ── lifecycle ─────────────────────────────────────────────────────────

        public static GameObject GetBannersStateClone() => _bannersStateClone;

        public static void Reset()
        {
            foreach (var c in _containers)
                if (c != null) UnityEngine.Object.Destroy(c);
            _containers.Clear();
            _panels.Clear();
            _liveSourcePanel = null;
            _srcContainer = null;
            _bannersStateClone = null;
            _entriesCaptured = false;
            _entryPool.Clear();
            _seenPlayers.Clear();
            _eliminatedKeys.Clear();
            _qualTimes.Clear();
            _nextPlace = 0;
            _soloSig = 0;
            _lastSoloSig = 0;
            _perRoundSoloOverride = null;
            _soloToggleprompt?.Destroy();
            _soloToggleprompt = null;
            _spawnGen++;   // retire any poll coroutines from the previous round
        }

        // called from HandleServerStartRoundPa — clone the squad-scores list once per host state
        // (SpectatorState / BannersState) and parent each clone under that state, so the list only
        // shows while one of those states is active. entries are captured lazily on first finish.
        public static void SpawnList()
        {
            Reset();
            // toggled off at spawn time — don't spawn or update at all this round.
            if (!Enabled) return;
            _updating = true;

            // the container is DISABLED, so GameObject.Find can't see it — resolve by walking
            // transforms (incl. inactive).
            var srcContainer = FindByPath(ListContainerPath);
            if (srcContainer == null)
            {
                Plugin.Log.LogInfo("TimePlacement: list container not found, skipping");
                return;
            }

            var gameStates = FindByPath(GameStatesPath);
            if (gameStates == null)
            {
                Plugin.Log.LogInfo("TimePlacement: GameStates not found, skipping");
                return;
            }

            // the live source container's Panel holds the real entries the game builds — we pull a
            // template row from it so panels under inactive states can still be fully built.
            _liveSourcePanel = FindChildDeep(srcContainer.transform, "Panel");
            _srcContainer = srcContainer;

            foreach (var stateName in HostStates)
            {
                var state = FindChild(gameStates.transform, stateName);
                if (state == null)
                {
                    Plugin.Log.LogInfo($"TimePlacement: state '{stateName}' not found, skipping it");
                    continue;
                }

                var clone = UnityEngine.Object.Instantiate(srcContainer, state);
                clone.name = "BetterFG_TimePlacementList";

                // disable (don't destroy) the squad-scoring view models on our clone so they can't
                // run and register with the game's scoring system — destroying them can fire
                // OnDestroy side effects that toggle the REAL panel off. just switching enabled=false
                // before activation keeps them inert without touching the original.
                foreach (var slvm in clone.GetComponentsInChildren<SquadScoreListViewModel>(true))
                    slvm.enabled = false;
                foreach (var sevm in clone.GetComponentsInChildren<SquadScoreEntryViewModel>(true))
                    sevm.enabled = false;

                clone.SetActive(true);

                // remember the BannersState clone — banner Show() postfixes parent the banner under it
                // as the first sibling so the leaderboard sits in front.
                if (stateName == "BannersState") _bannersStateClone = clone;

                // the clone is the whole CanvasSquadScoresList container. it kept the anchoredPosition
                // it had under the game's TopLeftContainer, but we've re-parented it directly under the
                // state (full-screen), so that old offset now lands it in the middle of the screen.
                // re-anchor the CONTAINER to the screen's top-left corner — the inner Panel is then
                // laid out relative to this, so this is the one position that actually sticks.
                var cloneRt = clone.transform as RectTransform;
                if (cloneRt != null)
                {
                    cloneRt.anchorMin = new Vector2(0f, 1f);
                    cloneRt.anchorMax = new Vector2(0f, 1f);
                    cloneRt.pivot = new Vector2(0f, 1f);
                    cloneRt.anchoredPosition = PanelAnchoredPos;
                }

                Transform panelT = null;
                foreach (var t in clone.GetComponentsInChildren<Transform>(true))
                    if (t != null && t.name == "Panel") { panelT = t; break; }
                if (panelT == null)
                {
                    UnityEngine.Object.Destroy(clone);
                    continue;
                }
                panelT.gameObject.SetActive(true);

                // wipe whatever stale/empty entries came with the clone — we rebuild from the live
                // template in CaptureEntries.
                for (int i = panelT.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.DestroyImmediate(panelT.GetChild(i).gameObject);

                panelT.localScale = new Vector3(1.02f, 1.02f, 1.02f);
                var panelRt = panelT as RectTransform;
                if (panelRt != null)
                {
                    // the CONTAINER now carries the screen position (top-left + PanelAnchoredPos), so
                    // pin the Panel itself to the container's top-left at zero offset — its original
                    // offset is what was dragging the whole thing into the middle of the screen.
                    panelRt.anchorMin = new Vector2(0f, 1f);
                    panelRt.anchorMax = new Vector2(0f, 1f);
                    panelRt.pivot = new Vector2(0f, 1f);
                    // re-pin next frame too so the game's own layout pass can't stomp it back.
                    if (BetterFGUIMan.Instance != null)
                        BetterFGUIMan.Instance.StartCoroutine(AdjustPanelPositionAfterFrame(panelRt, cloneRt).WrapToIl2Cpp());
                    else
                        panelRt.anchoredPosition = Vector2.zero;
                }

                _containers.Add(clone);
                _panels.Add(panelT.gameObject);
            }

            // now that we've cloned, disable every game-owned CanvasSquadScoresList under GameStates
            // (all states) so the game's own list never shows alongside ours. skip srcContainer — it
            // has to keep running until CaptureEntries pulls a template row out of its live Panel
            // (it gets disabled in CaptureEntries once we have the template). skip our own clones too.
            SuppressGameLists();

            _entriesCaptured = false;
            _nextPlace = 0;
            _seenPlayers.Clear();
            Plugin.Log.LogInfo($"TimePlacement: spawned {_panels.Count} panel(s), entries captured on first finish");

            // ALWAYS start the squad poll — a real squad round always shows the squad-points
            // leaderboard, no toggle and no race/hunt check. _roundSquads isn't populated yet at
            // round start, so the coroutine keeps re-checking HasSquadData() and paints once it's
            // confirmed a squad round.
            if (BetterFGUIMan.Instance != null)
                BetterFGUIMan.Instance.StartCoroutine(SquadScorePollCoroutine().WrapToIl2Cpp());

            // ALWAYS start the solo-score poll — GameRules / _soloScoreManager usually aren't
            // populated yet at round start, so we can't decide here. the coroutine keeps re-checking
            // IsScoringRound() and only paints once it's actually a scoring round.
            if (BetterFGUIMan.Instance != null)
                BetterFGUIMan.Instance.StartCoroutine(SoloScorePollCoroutine().WrapToIl2Cpp());

            // qualification-status roster — only paints once QualStatusActive() confirms a solo
            // round with the toggle on, so it no-ops everywhere else.
            if (BetterFGUIMan.Instance != null)
                BetterFGUIMan.Instance.StartCoroutine(QualStatusPollCoroutine().WrapToIl2Cpp());

            // only spawn the squad/per-player toggle when we're actually in a squad round — solo
            // (SquadSize <= 1) would collide with the Respawn prompt on the same controller button.
            uint squadSize = 0;
            try { squadSize = Cgm()?.SquadSize ?? 0; }
            catch (Exception ex) { Plugin.Log.LogWarning("TimePlacement: SquadSize read failed: " + ex.Message); }
            if (squadSize > 1) SpawnSoloTogglePrompt();
        }

        // disable every game-owned CanvasSquadScoresList under GameStates (all states) so the game's
        // own list never shows alongside ours. skip srcContainer (still feeding CaptureEntries its
        // template row) and our own clones. run at round start AND again on spectator entry: when the
        // SpectatorState activates, its GameplayScoringViewModel.OnEnable re-enables the game's spectator
        // list we killed at round start, so it reappears over our clone and never toggles solo/squad.
        static void SuppressGameLists()
        {
            var gameStates = FindByPath(GameStatesPath);
            if (gameStates == null) return;

            // our clones are renamed to BetterFG_TimePlacementList, so the name filter already spares
            // them — only the game's real CanvasSquadScoresList objects match here.
            int disabled = 0;
            foreach (var t in gameStates.transform.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.name != "CanvasSquadScoresList") continue;
                if (_srcContainer != null && t.gameObject == _srcContainer) continue;
                if (t.gameObject.activeSelf) { t.gameObject.SetActive(false); disabled++; }
            }
            if (disabled > 0)
                Plugin.Log.LogInfo($"TimePlacement: disabled {disabled} game CanvasSquadScoresList object(s)");
        }

        // re-run the suppression for a handful of frames after spectator entry — the game's scoring
        // VM OnEnable that re-shows its list doesn't always land on the same frame as the postfix.
        static IEnumerator SuppressGameListsForFrames()
        {
            for (int i = 0; i < 4; i++)
            {
                yield return null;
                SuppressGameLists();
            }
        }

        // grab/clone the entry GameObjects in every panel up to MaxRows. parallel across panels:
        // _entryPool[place] holds the row for that place in each panel. done lazily because entries
        // are built after round start.
        static void CaptureEntries()
        {
            _entryPool.Clear();
            if (_panels.Count == 0) return;

            // find a real entry to use as the template. prefer the LIVE source panel's direct
            // children (the entries the game built), but fall back to ANY SquadScoreEntryViewModel
            // anywhere under the source container — incl. inactive ones. this matters when we die and
            // spectate early: PlayingState (and its populated Panel) may be torn down, but the
            // container prefab still carries an entry we can clone so the list still spawns.
            GameObject template = null;
            if (_liveSourcePanel != null)
            {
                foreach (var t in _liveSourcePanel.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null) continue;
                    if (t.parent != _liveSourcePanel.transform) continue;
                    if (t.GetComponent<SquadScoreEntryViewModel>() == null) continue;
                    template = t.gameObject;
                    break;
                }
            }
            if (template == null && _srcContainer != null)
            {
                foreach (var vm in _srcContainer.GetComponentsInChildren<SquadScoreEntryViewModel>(true))
                {
                    if (vm != null && vm.gameObject != null) { template = vm.gameObject; break; }
                }
                if (template != null)
                    Plugin.Log.LogInfo("TimePlacement: using fallback template from source container");
            }

            // first real entry we ever see → stash a persistent copy, kept alive across every round.
            if (template != null && _cachedTemplate == null)
            {
                _cachedTemplate = UnityEngine.Object.Instantiate(template);
                _cachedTemplate.hideFlags = HideFlags.HideAndDontSave;
                UnityEngine.Object.DontDestroyOnLoad(_cachedTemplate);
                _cachedTemplate.SetActive(false);
                // the live source we just cloned from may already have had its font swapped by the font
                // replacement sweep. if we leave that BFG_* font baked into the cache, every future row
                // cloned from this template inherits it and toggling font master off does nothing —
                // OnEnable's TryApplyTo early-returns on BFG_* fonts. revert each TMP back to vanilla so
                // the cache holds the original font; runtime clones then pick up the current master
                // state correctly via OnEnable.
                foreach (var tmp in _cachedTemplate.GetComponentsInChildren<TMPro.TMP_Text>(true))
                    Customization.Menu.FontReplacementService.RevertIfTouched(tmp);
                Plugin.Log.LogInfo("TimePlacement: cached entry template");
            }

            // no live entry this round (e.g. entered spectating) → reuse the cached copy.
            if (template == null) template = _cachedTemplate;

            if (template == null)
            {
                Plugin.Log.LogInfo("TimePlacement: no live template entry yet");
                return;
            }

            // build MaxRows rows in EVERY panel by cloning the live template into each one.
            var perPanel = new List<List<GameObject>>();
            foreach (var panel in _panels)
            {
                var rows = new List<GameObject>();
                for (int i = 0; i < MaxRows; i++)
                {
                    var row = UnityEngine.Object.Instantiate(template, panel.transform);
                    PrepEntry(row);
                    var vm = row.GetComponent<SquadScoreEntryViewModel>();
                    if (vm != null && vm._pointsLabel != null) vm._pointsLabel.text = "";
                    row.SetActive(false);
                    rows.Add(row);
                }
                perPanel.Add(rows);
            }

            if (perPanel.Count == 0)
            {
                Plugin.Log.LogInfo("TimePlacement: no panels to fill");
                return;
            }

            // transpose: _entryPool[place] = [row in panel0, row in panel1, ...]
            for (int place = 0; place < MaxRows; place++)
            {
                var slot = new List<GameObject>();
                foreach (var rows in perPanel)
                    if (place < rows.Count) slot.Add(rows[place]);
                _entryPool.Add(slot);
            }

            _entriesCaptured = true;
            Plugin.Log.LogInfo($"TimePlacement: captured {MaxRows} slots across {perPanel.Count} panel(s)");

            // we've got the template now — disable the game's source list too so it stops showing.
            if (_srcContainer != null) { _srcContainer.SetActive(false); _srcContainer = null; }
        }

        // walk a slash-delimited transform path from the root, traversing inactive children too
        // (GameObject.Find only sees active objects, which is useless for a disabled Panel).
        static GameObject FindByPath(string path)
        {
            var parts = path.Split('/');

            // first segment is a root object; find it among all root GOs (active or not)
            Transform cur = null;
            foreach (var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                if (root.name == parts[0]) { cur = root.transform; break; }

            // root might live in another loaded scene (UI canvas often does) — scan all transforms
            if (cur == null)
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                    if (t != null && t.parent == null && t.name == parts[0]) { cur = t; break; }
            }
            if (cur == null) return null;

            for (int i = 1; i < parts.Length; i++)
            {
                Transform next = null;
                for (int c = 0; c < cur.childCount; c++)
                    if (cur.GetChild(c).name == parts[i]) { next = cur.GetChild(c); break; }
                if (next == null) return null;
                cur = next;
            }
            return cur.gameObject;
        }

        // direct child by name, including inactive children.
        static Transform FindChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == name) return parent.GetChild(i);
            return null;
        }

        // descendant by name (incl. inactive). returns the GameObject or null.
        static GameObject FindChildDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == name) return t.gameObject;
            return null;
        }

        // column X positions (pixels from the row's left edge) — tweak to taste.
        // order is placement -> points -> names, so long names overflow to the right with nothing
        // after them to collide with (e.g. "1st  56  Squad Name").
        const float PosX = 6f;        // placement (1st, 2nd...)
        const float PtsX = 50f;       // points column, right after placement
        const float NamesX = 120f;    // names start after points

        // build three fixed-position labels per row: placement, points, names (one object, can be 2
        // lines). each is anchored to the row's LEFT-CENTER so anchoredPosition.x is identical on
        // every row — that's what keeps the columns from drifting.
        static void PrepEntry(GameObject entryGo)
        {
            var vm = entryGo.GetComponent<SquadScoreEntryViewModel>();
            var src = vm != null ? vm._pointsLabel : null;
            if (src == null) return;

            // kill any leftover data bindings so the game doesn't fight us repainting it.
            foreach (var b in entryGo.GetComponentsInChildren<TMPTextBinding>(true))
                UnityEngine.Object.Destroy(b);
            foreach (var b in entryGo.GetComponentsInChildren<ActiveBinding>(true))
                UnityEngine.Object.Destroy(b);

            // the entry's own Image is the game's row background — drop it so only ours shows.
            var ownImg = entryGo.GetComponent<Image>();
            if (ownImg != null) UnityEngine.Object.Destroy(ownImg);

            // turn off every existing child — we draw our own labels.
            for (int i = 0; i < entryGo.transform.childCount; i++)
                entryGo.transform.GetChild(i).gameObject.SetActive(false);

            // transparent row background image behind the labels.
            var bgSprite = RowBgSprite();
            if (bgSprite != null)
            {
                var bgGo = new GameObject("BFG_RowBG");
                bgGo.transform.SetParent(entryGo.transform, false);
                var bgRt = bgGo.AddComponent<RectTransform>();
                bgRt.anchorMin = Vector2.zero;
                bgRt.anchorMax = Vector2.one;
                bgRt.offsetMin = Vector2.zero;
                bgRt.offsetMax = Vector2.zero;
                bgRt.localScale = new Vector3(4.5491f, 1f, 1f);   // tuned to span the row
                var bgImg = bgGo.AddComponent<Image>();
                bgImg.sprite = bgSprite;
                bgImg.type = Image.Type.Simple;
                bgImg.raycastTarget = false;
                bgGo.transform.SetSiblingIndex(0);   // behind the labels we add next
                bgGo.SetActive(true);
            }

            MakeLabel(entryGo.transform, src, "BFG_Pos", PosX, TextAlignmentOptions.Left);
            MakeLabel(entryGo.transform, src, "BFG_Pts", PtsX, TextAlignmentOptions.Left);
            MakeLabel(entryGo.transform, src, "BFG_Names", NamesX, TextAlignmentOptions.Left);
        }

        static Sprite _rowBgSprite;
        static Sprite RowBgSprite()
        {
            // Unity-aware null check: catches a sprite that got destroyed on a scene change so we
            // reload it for the next round instead of handing out a dead reference (= no image).
            // (the loader marks it HideAndDontSave, so this normally only loads once.)
            if (_rowBgSprite != null) return _rowBgSprite;
            try { _rowBgSprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.feature.timeplacement.rowbackground.png", 100f); }
            catch (Exception ex) { Plugin.Log.LogWarning("TimePlacement: row bg load failed: " + ex.Message); }
            return _rowBgSprite;
        }

        static void MakeLabel(Transform row, TextMeshProUGUI src, string name, float x, TextAlignmentOptions align)
        {
            var go = UnityEngine.Object.Instantiate(src.gameObject, row);
            go.name = name;
            foreach (var b in go.GetComponents<TMPTextBinding>()) UnityEngine.Object.Destroy(b);

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.enableAutoSizing = false;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.richText = true;
            tmp.alignment = align;
            tmp.lineSpacing = -18f;     // gap between the two name lines (less negative = more space)
            tmp.text = "";

            // anchor to the row's left-center and pin x by anchoredPosition (not localPosition) so
            // every row's columns line up regardless of the row's width/anchoring.
            var rt = tmp.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            go.SetActive(true);
        }

        static TextMeshProUGUI FindLabel(GameObject row, string name)
        {
            var t = row.transform.Find(name);
            return t != null ? t.GetComponent<TextMeshProUGUI>() : null;
        }

        // server says this player is out (succeeded == false in a survival final). record their key
        // so the survival point count drops, then repaint right away.
        public static void OnPlayerEliminated(uint remotePlayerId)
        {
            if (!IsSurvivalFinalSquadRound()) return;
            string key = PlayerKeyById(remotePlayerId);
            if (string.IsNullOrEmpty(key)) return;
            if (!_eliminatedKeys.Add(key)) return;       // already known
            if (_updating && Enabled) RefreshSquadScores();
        }

        // playerKey for a playerId via the client player manager's id index, or "".
        static string PlayerKeyById(uint remotePlayerId)
        {
            var idx = BetterFG.Utilities.PlayerInformation.GetClientPlayerManager()?._playerIdIndex;
            if (idx == null || !idx.ContainsKey(remotePlayerId)) return "";
            return idx[remotePlayerId]?.playerKey ?? "";
        }

        // ── per-finish update (called from the HandlePlayerFinished patch) ────────

        // called from the shared HandleServerPlayerProgress hub in GameStatePatches. fires once per
        // player as they finish (succeeded + playerId) — the live finish signal for the finish-order
        // feature (HandlePlayerFinished never fired in testing; the real per-player event is here).
        public static void OnServerPlayerProgress(GameMessageServerPlayerProgress progressMessage)
        {
            if (progressMessage == null || progressMessage.isSkipping) return;
            if (progressMessage.succeeded)
            {
                // remember when they qualified so the roster can show a time column. capture the live
                // gameplay clock ONCE here at the moment the player qualifies — keeps ms precision
                // (the server's qualifyTime is whole-second only), and everything else reads this one
                // stored value so the leaderboard and fall-feed stamp can never disagree.
                string key = PlayerKeyById(progressMessage.playerId);
                if (!string.IsNullOrEmpty(key) && !_qualTimes.ContainsKey(key))
                    _qualTimes[key] = GlobalGameStateClient.Instance?.GameStateView != null
                        ? GlobalGameStateClient.Instance.GameStateView.GameplayTimeElapsed : 0f;
                OnPlayerFinished(progressMessage.playerId);
            }
            else
                // survival-final elimination signal (no completedLevel flip in those rounds).
                OnPlayerEliminated(progressMessage.playerId);
        }

        // the qualify time we captured for a player in OnServerPlayerProgress (seconds). the fall-feed
        // qual-time tweak reads this so its stamp matches the leaderboard's time column exactly instead
        // of reading the clock again at render time.
        public static bool TryGetQualTime(string playerKey, out float seconds)
        {
            seconds = 0f;
            return !string.IsNullOrEmpty(playerKey) && _qualTimes.TryGetValue(playerKey, out seconds);
        }

        public static void OnPlayerFinished(uint remotePlayerId)
        {
            if (!_updating || !Enabled || _panels.Count == 0) return;
            // qualify-highlight: repaint the roster immediately on each qualification (this is THE
            // qualify signal) instead of waiting on the poll — eliminations still come from the poll.
            if (QualStatusActive()) { RefreshQualStatus(); return; }
            // scoring round with the toggle on: the live score view owns the rows, don't draw finish
            // order underneath it.
            if (QualStatusOn) return;
            // a real squad round always shows squad points — the poll owns the list, don't draw
            // finish order over it. check this first so a squad hunt doesn't fall into solo mode.
            if (HasSquadData()) return;
            // scoring rounds (hunt/bubble/score-target, non-squad) show live points from the solo
            // score manager — the poll owns the list, so don't draw finish order over it.
            if (IsScoringRound()) return;
            // only RACES have a real finish order. survival/final-fall/etc fire a batch of
            // "succeeded" progress with identical times when the round resolves — don't paint that
            // as a fake leaderboard (this is why a solo survival round showed 1st..16th @ same time
            // in the banner state).
            if (!IsRaceRound()) return;
            if (_seenPlayers.Contains(remotePlayerId)) return;
            _seenPlayers.Add(remotePlayerId);

            // entries are built after round start — grab them on the first finish.
            if (!_entriesCaptured) CaptureEntries();

            if (_nextPlace >= MaxRows) return;
            int place = ++_nextPlace;
            string suffix = Suffix(place);
            string name = ResolvePlayerName(remotePlayerId);

            // the server qualifyTime we stashed in OnServerPlayerProgress — same value the fall-feed
            // stamp reads, so the two always match.
            string fkey = PlayerKeyById(remotePlayerId);
            _qualTimes.TryGetValue(fkey, out float elapsed);
            TimeSpan t = TimeSpan.FromSeconds(elapsed);
            string time = string.Format("{0:D2}:{1:D2}:{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);

            // placement column (yellow), name column, time in the points column. this is the solo
            // non-points (race) path — the points column holds a wide time string, so push the name
            // 100 units right to clear it.
            SetRow(place - 1,
                $"<b><color=#FFFF00>{place}{suffix}</color></b>",
                name,
                $"<size=120%>{time}</size>",
                80f);
            Plugin.Log.LogInfo($"TimePlacement: {place}{suffix} -> {name} {time} (id {remotePlayerId})");
        }

        static string Suffix(int place) =>
            place == 1 ? "st" : place == 2 ? "nd" : place == 3 ? "rd" : "th";

        // set the three columns of a row across every panel, activating that row. namesXOffset
        // nudges the name column right (race rounds use it to clear the wide time string).
        static void SetRow(int index, string pos, string names, string pts, float namesXOffset = 0f)
        {
            if (index < 0 || index >= _entryPool.Count) return;
            foreach (var entryGo in _entryPool[index])
            {
                if (entryGo == null) continue;
                entryGo.SetActive(true);
                Apply(FindLabel(entryGo, "BFG_Pos"), pos);
                var namesLabel = FindLabel(entryGo, "BFG_Names");
                Apply(namesLabel, names);
                if (namesLabel != null)
                    namesLabel.rectTransform.anchoredPosition = new Vector2(NamesX + namesXOffset, 0f);
                Apply(FindLabel(entryGo, "BFG_Pts"), pts);
            }
        }

        static void Apply(TextMeshProUGUI tmp, string text)
        {
            if (tmp == null) return;
            tmp.text = text ?? "";
            tmp.ForceMeshUpdate();
        }

        // hide a row (used when squad count shrinks below what we last drew).
        static void HideRow(int index)
        {
            if (index < 0 || index >= _entryPool.Count) return;
            foreach (var entryGo in _entryPool[index])
                if (entryGo != null) entryGo.SetActive(false);
        }

        // ── squad-scores mode ─────────────────────────────────────────────────

        // repaint every row from the game's live squad scores. driven by a poll while squad mode
        // is on, since these update continuously during the round.
        static void RefreshSquadScores()
        {
            if (!_updating || !Enabled) return;
            // not a real squad round — don't paint squad rows; the finish-times path owns the list.
            if (!HasSquadData()) return;

            if (!_entriesCaptured) CaptureEntries();
            if (!_entriesCaptured) return;

            // user wants per-player rows in squad rounds. scoring rounds (hunt/bubble) have live solo
            // points — use the solo path. everything else (races, survivals, finals) has no per-player
            // score, so the qualify-status roster is the right per-player view.
            if (PreferSoloInSquad)
            {
                if (GameRulesAreScoring()) RefreshSoloScores();
                else RefreshQualStatus();
                return;
            }

            // squad FINALS don't score via solo/squad points — we derive points per the final type:
            //   tail (Royal Fumble): squad holding the tail gets +1
            //   survival (Hex-A-Gone etc): points = living members
            //   otherwise (crown grab / finish): points = members who qualified (completedLevel)
            if (IsFinalSquadRound()) { RefreshSquadFinal(); return; }

            // points squads: _allSquadScores is unreliable, so derive squad totals by summing each
            // member's solo score from the SoloScoreManager instead.
            if (IsScoringSquadRound()) { RefreshSquadScoresFromSolo(); return; }

            var mgr = ClientSquadManager.Instance;
            var scores = mgr != null ? mgr._allSquadScores : null;
            if (scores == null) return;

            // keys of our own squad — these names render hot pink.
            var highlight = MySquadKeys();

            // IL2CPP SortedList isn't C# foreach-able; index into its Values list instead.
            // it's already ordered by key (position). use the SortedList's own Count.
            var values = scores.Values;
            int count = values != null ? scores.Count : 0;

            int row = 0;
            for (int i = 0; i < count && row < MaxRows; i++)
            {
                var e = values[i];
                if (e == null) continue;

                string pos = !string.IsNullOrEmpty(e.positionString) ? e.positionString : (e.position + Suffix(e.position));
                string col = e.isInTieEliminatedPosition || e.isInDangerPosition ? "#FF5555" : "#FFFF00";
                string posText = $"<b><color={col}>{pos}</color></b>";
                string ptsText = $"<size=120%>{e.points} pts</size>";

                // names smaller (-30%)
                string Small(string s) => $"<size=70%>{s}</size>";

                string namesText;
                var names = ShowNames ? ResolveSquadNames(mgr, e.squadId, highlight) : null;
                if (names == null || names.Count == 0)
                {
                    namesText = Small(SquadLabel(e.squadId));
                }
                else if (names.Count == 2)
                {
                    // duos: one name per line.
                    namesText = Small($"{names[0]}\n{names[1]}");
                }
                else if (TwoLines && names.Count >= 3)
                {
                    // both lines live in the SAME names label, so they auto-align (no padding).
                    string top = string.Join(", ", names.GetRange(0, 2));
                    string bottom = string.Join(", ", names.GetRange(2, names.Count - 2));
                    namesText = Small($"{top}\n{bottom}");
                }
                else
                {
                    namesText = Small(string.Join(", ", names));
                }

                SetRow(row, posText, namesText, ptsText);
                row++;
            }

            // clear any rows we used previously but no longer need.
            for (int i = row; i < MaxRows; i++)
                HideRow(i);
        }

        // the round's score target (the points needed to "max out"). on team levels like Snowy Scrap
        // this is 80 even though the game's own normalized leaderboard shows 100 — so we use it to
        // rescale our raw team score onto a 0..100 display where ScoreTarget == 100 and 0 == 0.
        // returns 0 if there's no meaningful target (then we just show the raw number).
        static int ScoreTargetForRound() => GameRulesUtils.ScoreTarget();

        // rescale a raw score onto the 0..100 leaderboard display: target points -> 100, 0 -> 0.
        // if there's no target (target <= 0) just return the raw value untouched.
        static int NormalizeToTarget(int raw)
        {
            int target = ScoreTargetForRound();
            if (target <= 0) return raw;
            return (int)Math.Round(raw * 100f / target);
        }

        // playerKey -> team score, from the PlayerTeamManager. used as a fallback for team levels
        // that score via teams instead of solo points (where solo scores read 0). every member of a
        // team shares the team's Score. empty if there's no team manager.
        static Dictionary<string, int> BuildTeamScoreByKey()
        {
            var map = new Dictionary<string, int>();
            var teams = Cgm()?._playerTeamManager?._allTeams;
            if (teams == null) return map;
            foreach (var team in teams)
            {
                var members = team?._members;
                if (members == null) continue;
                int score = team.Score;
                for (int i = 0; i < members.Count; i++)
                {
                    var m = members[i];
                    if (m != null && !string.IsNullOrEmpty(m.playerKey))
                        map[m.playerKey] = score;
                }
            }
            return map;
        }

        // points-squad path: the game's _allSquadScores is unreliable in scoring squads, so build
        // squad totals ourselves by summing each member's solo score from the SoloScoreManager.
        // matches players to squads by playerKey, sorts squads by total desc, then paints. team
        // levels that score via PlayerTeamManager fall back to the team score (solo reads 0 there).
        static void RefreshSquadScoresFromSolo()
        {
            var mgr = ClientSquadManager.Instance;
            var squads = mgr != null ? mgr._roundSquads : null;
            if (squads == null) return;

            var solo = GetSoloScoreManager();
            var all = solo != null ? solo._allPlayers : null;
            if (all == null) return;

            // playerKey -> solo score, pulled out of the score manager.
            var scoreByKey = new Dictionary<string, int>();
            foreach (var v in all.Values)
            {
                if (v == null) continue;
                try
                {
                    var p = v._player;
                    if (p != null && !string.IsNullOrEmpty(p.playerKey))
                        scoreByKey[p.playerKey] = v.Score;
                }
                catch { }
            }

            // some team levels score via the PlayerTeamManager instead of solo points, so solo
            // scores come back 0. build a playerKey -> team score map as a fallback (every member of
            // a team shares the team's Score).
            var teamScoreByKey = BuildTeamScoreByKey();

            // keys of our own squad — these names render hot pink (our own uses our custom name).
            var highlight = MySquadKeys();

            // sum each squad's member scores, and grab the member display names while we're here.
            // qualTime: the moment the LAST member of a fully-qualified squad crossed the line, or
            // null if any member hasn't qualified yet. squads that all qualified sort to the top by
            // that time; everyone else falls back to points desc.
            var totals = new List<(uint squadId, int total, List<string> names, float? qualTime)>();
            foreach (var sid in squads.Keys)
            {
                var members = squads[sid];
                if (members == null || members.Count == 0) continue;
                int total = 0;
                int teamTotal = 0;
                var names = new List<string>();
                bool allQualified = true;
                float latestQualTime = 0f;
                for (int i = 0; i < members.Count; i++)
                {
                    var m = members[i];
                    if (m == null || string.IsNullOrEmpty(m.playerKey)) { allQualified = false; continue; }
                    if (scoreByKey.TryGetValue(m.playerKey, out int s)) total += s;
                    // every member maps to the SAME team score, so take it once (don't sum, or a
                    // 3-player team would show 3x the real score).
                    if (teamScoreByKey.TryGetValue(m.playerKey, out int ts) && ts > teamTotal) teamTotal = ts;
                    string n = ResolveDisplayName(m.playerKey, highlight);
                    if (!string.IsNullOrEmpty(n)) names.Add(n);
                    if (_qualTimes.TryGetValue(m.playerKey, out float qt))
                    {
                        if (qt > latestQualTime) latestQualTime = qt;
                    }
                    else allQualified = false;
                }
                // fall back to the team-manager score when solo points are stuck at 0. team levels
                // (Snowy Scrap etc) score via teams and have a ScoreTarget that isn't 100 — rescale
                // that raw team score onto the 0..100 display so it lines up with the game's own
                // normalized leaderboard (target -> 100, 0 -> 0). solo-summed totals are left raw.
                if (total == 0 && teamTotal != 0) total = NormalizeToTarget(teamTotal);
                totals.Add((sid, total, names, allQualified ? (float?)latestQualTime : null));
            }

            // fully-qualified squads pin to the top ordered by their last-member finish time (fastest
            // wins). unqualified squads keep the old points-desc ranking underneath. stable squadId
            // tiebreak so equal rows don't swap every tick.
            totals.Sort((a, b) =>
            {
                bool aq = a.qualTime.HasValue;
                bool bq = b.qualTime.HasValue;
                if (aq != bq) return aq ? -1 : 1;
                if (aq && bq && a.qualTime.Value != b.qualTime.Value)
                    return a.qualTime.Value.CompareTo(b.qualTime.Value);
                if (!aq && !bq && a.total != b.total) return b.total.CompareTo(a.total);
                return a.squadId.CompareTo(b.squadId);
            });

            string Small(string s) => $"<size=70%>{s}</size>";

            int row = 0;
            for (int i = 0; i < totals.Count && row < MaxRows; i++)
            {
                var e = totals[i];
                int place = i + 1;
                string posText = $"<b><color=#FFFF00>{place}{Suffix(place)}</color></b>";
                string ptsText;
                if (e.qualTime.HasValue)
                {
                    TimeSpan ts = TimeSpan.FromSeconds(e.qualTime.Value);
                    string timeStr = string.Format("{0:D2}:{1:D2}:{2:D3}", ts.Minutes, ts.Seconds, ts.Milliseconds);
                    ptsText = $"<size=90%>{e.total}pts</size>  <size=110%><color=#FFFF00>{timeStr}</color></size>";
                }
                else
                    ptsText = $"<size=120%>{e.total} pts</size>";
                // qualified rows have "Npts MM:SS.mmm" in the points column — push names right to
                // clear the wider column instead of overlapping.
                float nameOffset = e.qualTime.HasValue ? 90f : 0f;

                string namesText;
                var names = ShowNames ? e.names : null;
                if (names == null || names.Count == 0)
                    namesText = Small(SquadLabel(e.squadId));
                else if (TwoLines && names.Count >= 3)
                {
                    string top = string.Join(", ", names.GetRange(0, 2));
                    string bottom = string.Join(", ", names.GetRange(2, names.Count - 2));
                    namesText = Small($"{top}\n{bottom}");
                }
                else
                    namesText = Small(string.Join(", ", names));

                SetRow(row, posText, namesText, ptsText, nameOffset);
                row++;
            }

            for (int i = row; i < MaxRows; i++)
                HideRow(i);
        }

        // unified squad-FINAL path. computes points per squad by the final type:
        //   tail (Royal Fumble): squad holding the tail = 1, else 0
        //   survival (Hex-A-Gone etc): points = living members (squad size minus eliminations)
        //   crown grab / finish: points = members who qualified (completedLevel)
        // sorts squads by points desc, dims eliminated members, shows the points column.
        static void RefreshSquadFinal()
        {
            var mgr = ClientSquadManager.Instance;
            var squads = mgr != null ? mgr._roundSquads : null;
            if (squads == null) return;

            var highlight = MySquadKeys();
            var qualified = DeadPlayerKeys();            // completedLevel == true (= qualified/grabbed)
            bool tail = IsTailFinalSquadRound();
            bool survival = !tail && IsSurvivalFinalSquadRound();
            uint? tailSquad = tail ? TailHolderSquadId() : (uint?)null;

            // 2-team finals (Power Trip / Jinxed / Basketfall) score via teams, not completedLevel
            // or eliminations — and EntityVsGroupManager.Groups comes back empty so the tail/survival
            // logic can't resolve anything (everyone reads 0). when that's the case fall back to the
            // PlayerTeamManager's sorted team scores: playerKey -> their team's score. non-empty only
            // for these team rounds, so its presence is the switch into the team-score path.
            var teamScores = VsGroupsEmpty() ? BuildTeamScoreByKey() : null;
            bool teamScored = teamScores != null && teamScores.Count > 0;

            string Small(string s) => $"<size=70%>{s}</size>";
            // survival has no completedLevel flip, so elimination comes from _eliminatedKeys (server
            // progress with succeeded == false). dim those names. tail/crown don't dim.
            string NameFor(string playerKey)
            {
                if (survival && _eliminatedKeys.Contains(playerKey))
                    return $"<color=#FFFFFF33>{ResolveDisplayName(playerKey, null)}</color>";
                return ResolveDisplayName(playerKey, highlight);
            }

            // per-squad points.
            int PointsFor(uint sid, Il2CppSystem.Collections.Generic.List<NetworkPlayerDataCommon> members)
            {
                if (tail) return (tailSquad.HasValue && tailSquad.Value == sid) ? 1 : 0;
                if (members == null) return 0;
                // team-scored final: every member shares their team's score, so just take it once
                // off the first member we can map (don't sum, or a 2-player team doubles its score).
                if (teamScored)
                {
                    for (int i = 0; i < members.Count; i++)
                    {
                        var m = members[i];
                        if (m == null || string.IsNullOrEmpty(m.playerKey)) continue;
                        if (teamScores.TryGetValue(m.playerKey, out int ts)) return ts;
                    }
                    return 0;
                }
                int pts = 0;
                for (int i = 0; i < members.Count; i++)
                {
                    var m = members[i];
                    if (m == null || string.IsNullOrEmpty(m.playerKey)) continue;
                    if (survival) { if (!_eliminatedKeys.Contains(m.playerKey)) pts++; }   // living members
                    else { if (qualified.Contains(m.playerKey)) pts++; }                   // qualified (crown/finish)
                }
                return pts;
            }

            var totals = new List<(uint sid, int pts)>();
            foreach (var sid in squads.Keys)
                totals.Add((sid, PointsFor(sid, squads[sid])));

            // points desc, then squadId for stable ordering.
            totals.Sort((a, b) =>
            {
                if (a.pts != b.pts) return b.pts.CompareTo(a.pts);
                return a.sid.CompareTo(b.sid);
            });

            int row = 0;
            for (int s = 0; s < totals.Count && row < MaxRows; s++)
            {
                uint sid = totals[s].sid;
                var members = squads[sid];
                if (members == null || members.Count == 0) continue;

                var names = new List<string>();
                if (ShowNames)
                {
                    for (int i = 0; i < members.Count; i++)
                    {
                        var m = members[i];
                        if (m == null || string.IsNullOrEmpty(m.playerKey)) continue;
                        string n = NameFor(m.playerKey);
                        if (!string.IsNullOrEmpty(n)) names.Add(n);
                    }
                }

                string namesText;
                if (names.Count == 0)
                    namesText = Small(SquadLabel(sid));
                else if (TwoLines && names.Count >= 3)
                {
                    string top = string.Join(", ", names.GetRange(0, 2));
                    string bottom = string.Join(", ", names.GetRange(2, names.Count - 2));
                    namesText = Small($"{top}\n{bottom}");
                }
                else
                    namesText = Small(string.Join(", ", names));

                int place = row + 1;
                string ptsText = $"<size=120%>{totals[s].pts} pts</size>";
                SetRow(row, $"<b><color=#FFFF00>{place}{Suffix(place)}</color></b>", namesText, ptsText);
                row++;
            }

            for (int i = row; i < MaxRows; i++)
                HideRow(i);
        }

        // the squadId holding the tail in a tail final (Royal Fumble), or null. the tag manager's
        // FocusedVsGroup / FocusedEntity points at the chased (tail) entity; map its player to a squad.
        static uint? TailHolderSquadId()
        {
            var vgm = GetTagVsGroupManager();
            if (vgm == null) return null;

            // the focused entity is the tail holder; grab its first valid player.
            var holder = vgm.FocusedEntity?.GetFirstValidPlayer();
            if (holder == null)
            {
                var ents = vgm.FocusedVsGroup?.Entities;
                if (ents != null && ents.Count > 0) holder = ents[0]?.GetFirstValidPlayer();
            }
            if (holder == null || string.IsNullOrEmpty(holder.playerKey)) return null;

            var squads = ClientSquadManager.Instance?._roundSquads;
            if (squads == null) return null;
            foreach (var sid in squads.Keys)
            {
                var mem = squads[sid];
                if (mem == null) continue;
                for (int i = 0; i < mem.Count; i++)
                    if (mem[i] != null && mem[i].playerKey == holder.playerKey) return sid;
            }
            return null;
        }

        // squad members' display names from _roundSquads (squadId -> players). our own name uses our
        // custom name; our whole squad's names get hot pink via highlightKeys. empty if none.
        static List<string> ResolveSquadNames(ClientSquadManager mgr, uint squadId, HashSet<string> highlightKeys)
        {
            var names = new List<string>();
            var squads = mgr?._roundSquads;
            if (squads == null || !squads.ContainsKey(squadId)) return names;
            var members = squads[squadId];
            if (members == null) return names;
            for (int i = 0; i < members.Count; i++)
            {
                var m = members[i];
                if (m == null || string.IsNullOrEmpty(m.playerKey)) continue;
                string n = ResolveDisplayName(m.playerKey, highlightKeys);
                if (!string.IsNullOrEmpty(n)) names.Add(n);
            }
            return names;
        }

        static IEnumerator SquadScorePollCoroutine()
        {
            int gen = _spawnGen;
            var wait = new WaitForSeconds(0.5f);
            // runs the whole round — RefreshSquadScores() only paints once HasSquadData() confirms
            // a real squad round, so non-squad rounds just no-op here.
            while (gen == _spawnGen && _panels.Count > 0)
            {
                RefreshSquadScores();
                yield return wait;
            }
        }

        // spawn the LE_Zoom_Out (right-stick-click) prompt once when the leaderboard spawns. flips
        // the leaderboard between squad-aggregated and per-player views for the round only. torn down
        // in Reset. bail if a prompt is already alive so we don't stack duplicates.
        static void SpawnSoloTogglePrompt() => SpawnSoloTogglePromptWith(NavPrompt.Random);

        // called from the shared SwitchToSpectatorMode hub in GameStatePatches — respawn the toggle
        // with RewardsContinue so it doesn't share a button with the game's spectator controls.
        public static void OnSpectatorMode()
        {
            // belt-and-braces: if the spectator state's scoring VM ever re-enables a game squad list,
            // keep ours the only one. cheap no-op when there's nothing to disable.
            SuppressGameLists();
            if (BetterFGUIMan.Instance != null)
                BetterFGUIMan.Instance.StartCoroutine(SuppressGameListsForFrames().WrapToIl2Cpp());

            if (_soloToggleprompt == null) return;
            _soloToggleprompt.Destroy();
            _soloToggleprompt = null;
            SpawnSoloTogglePromptWith(NavPrompt.RewardsContinue);
        }

        static void SpawnSoloTogglePromptWith(NavPrompt glyph)
        {
            if (_soloToggleprompt != null && _soloToggleprompt.IsAlive) return;
            // parent under the shared CustomNavPrompt overlay (that's where input actually resolves).
            // position it to line up with the leaderboard's top-left in screen space: anchor to the
            // screen's top-left corner and offset by the same y the leaderboard container uses.
            var parent = NavPromptCore.GetCustomNavPromptRoot();
            if (parent == null) { Plugin.Log.LogInfo("TimePlacement: solo-toggle no overlay parent"); return; }
            _soloToggleprompt = NavPromptCore.From(glyph)
                .WithLabel("Toggle squads / per-player", "bfg_tp_toggle_solosquad")
                .CustomAnchors(new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f),
                    new Vector2(PanelAnchoredPos.x + 40f, PanelAnchoredPos.y - 250f))
                .SpawnOn(parent);
            if (_soloToggleprompt == null) { Plugin.Log.LogInfo("TimePlacement: solo-toggle prompt spawn failed"); return; }
            Plugin.Log.LogInfo("TimePlacement: solo-toggle prompt spawned (" + glyph + ")");
            if (BetterFGUIMan.Instance != null)
                BetterFGUIMan.Instance.StartCoroutine(SoloTogglePromptTicker(_soloToggleprompt).WrapToIl2Cpp());
        }

        // one ticker per prompt handle. OnSpectatorMode destroys the round-start prompt and respawns a
        // new one (different glyph), which starts a SECOND ticker — the old ticker's loop condition was
        // just `_soloToggleprompt.IsAlive`, so it kept running against the NEW static prompt. two tickers
        // then both saw the same button press on the same frame and flipped the override twice, cancelling
        // out (nothing changed in spectator). bind each ticker to the handle it was spawned for and bail
        // the instant the static no longer points at it, so a respawn cleanly retires the old one.
        static IEnumerator SoloTogglePromptTicker(NavPromptHandle mine)
        {
            while (mine != null && mine.IsAlive && ReferenceEquals(_soloToggleprompt, mine))
            {
                // show whenever ANY of our leaderboard containers is currently visible (PlayingState
                // during play, SpectatorState after we qualify/die, BannersState during banner
                // transitions). the containers themselves are auto-toggled by the game's state system.
                bool visible = false;
                foreach (var c in _containers)
                    if (c != null && c.activeInHierarchy) { visible = true; break; }
                var go = mine.GameObject;
                if (go != null && go.activeSelf != visible) go.SetActive(visible);

                if (visible && mine.IsPressed())
                {
                    _perRoundSoloOverride = !PreferSoloInSquad;
                    Plugin.Log.LogInfo("TimePlacement: squad/per-player toggled -> perPlayer=" + PreferSoloInSquad);
                    RefreshSquadScores();
                }
                yield return null;
            }
        }

        static IEnumerator AdjustPanelPositionAfterFrame(RectTransform panelRt, RectTransform containerRt)
        {
            yield return null; // next frame
            // container holds the screen position; panel sits at the container's top-left corner.
            if (containerRt != null)
            {
                containerRt.anchorMin = new Vector2(0f, 1f);
                containerRt.anchorMax = new Vector2(0f, 1f);
                containerRt.pivot = new Vector2(0f, 1f);
                containerRt.anchoredPosition = PanelAnchoredPos;
            }
            if (panelRt != null) panelRt.anchoredPosition = Vector2.zero;
        }

        // is this actually a squad round? (real squads with members). if not, fall back to the
        // finish-times list even when the squad-scores toggle is on.
        static bool HasSquadData()
        {
            var squads = ClientSquadManager.Instance?._roundSquads;
            if (squads == null || squads.Count == 0) return false;
            foreach (var k in squads.Keys)
            {
                var members = squads[k];
                if (members != null && members.Count > 0) return true;
            }
            return false;
        }

        // ── solo scoring mode (hunt / bubble / score-target) ──────────────────

        // does GameRules say this is a scoring round (hunt / bubble / score-target)? doesn't care
        // about squads — callers add the squad gate themselves.
        static bool GameRulesAreScoring() => GameRulesUtils.IsScoringRound();

        // solo scoring round (scoring + NOT squads) — show live solo points instead of finish order.
        // when the user prefers per-player rows, scoring squad rounds count as solo too so the solo
        // path owns the list (RefreshSquadScores hands off to it).
        static bool IsScoringRound() => GameRulesAreScoring() && (!HasSquadData() || PreferSoloInSquad);

        // does this round actually have a finish ORDER worth showing? only real races do — players
        // cross a line one by one with distinct times. survival / hunt / final-fall etc fire a batch
        // of "succeeded" progress with identical times when the round resolves; we must NOT paint
        // that as a fake leaderboard (otherwise a solo survival round shows "1st..16th" all at the
        // same elapsed time in the banner state).
        static bool IsRaceRound() => GameRulesUtils.IsRaceRound();

        // points-squad round (scoring + squads) — _allSquadScores is unreliable, sum solo instead.
        static bool IsScoringSquadRound() => HasSquadData() && GameRulesAreScoring();

        // any squad FINAL that isn't a race (these don't score via solo/squad points — we derive
        // points ourselves: survival = living members, tail = who holds the tail, otherwise = who
        // qualified/grabbed the crown).
        static bool IsFinalSquadRound() =>
            HasSquadData() && GameRulesUtils.IsFinalRound() && !GameRulesUtils.IsRaceRound();

        // survival final (final + survival + not race): points = number of living members.
        static bool IsSurvivalFinalSquadRound() =>
            HasSquadData() && GameRulesUtils.IsFinalRound() && GameRulesUtils.IsSurvivalRound() && !GameRulesUtils.IsRaceRound();

        // tail final (Royal Fumble): the squad holding the tail gets +1. detected by the tag manager
        // having a vs-group focus AND a populated Groups list (2-team finals like Power Trip have the
        // manager but empty Groups — those score off teams, not the tail).
        static bool IsTailFinalSquadRound() =>
            IsFinalSquadRound() && GetTagVsGroupManager() != null && !VsGroupsEmpty();

        // is the vs-group manager's Groups list empty (or absent)? true for the 2-team finals
        // (Power Trip / Jinxed / Basketfall) where there's no entity-vs-group setup to read — that's
        // our signal to score off the team manager's sorted team scores instead.
        static bool VsGroupsEmpty()
        {
            var groups = GetTagVsGroupManager()?.Groups;
            return groups == null || groups.Count == 0;
        }

        // the tag manager's EntityVsGroupManager, or null (only present in tail rounds like Royal Fumble).
        static FG.Common.EntityVsGroupManager GetTagVsGroupManager() => Cgm()?._tagManager?._entityVsGroupManager;

        // playerKeys of everyone who's done (completedLevel) — in a survival final that means dead.
        // built from the net-id index, whose values are NetworkPlayerDataClient (has completedLevel).
        static HashSet<string> DeadPlayerKeys()
        {
            var dead = new HashSet<string>();
            var idx = Cgm()?._clientPlayerManager?._playerNetIdIndex;
            if (idx == null) return dead;
            foreach (var kv in idx)
            {
                var data = kv.value;
                if (data != null && !string.IsNullOrEmpty(data.playerKey) && data.completedLevel)
                    dead.Add(data.playerKey);
            }
            return dead;
        }

        // grab the live SoloScoreManager off the active ClientGameManager, or null.
        static FG.Common.SoloScoreManager GetSoloScoreManager() => Cgm()?._soloScoreManager;

        // repaint every row from the game's live solo scores, ranked by PlayerScore.Ranking.
        static void RefreshSoloScores()
        {
            if (!_updating || !Enabled) return;
            if (!IsScoringRound()) return;

            if (!_entriesCaptured) CaptureEntries();
            if (!_entriesCaptured) return;

            var mgr = GetSoloScoreManager();
            var all = mgr != null ? mgr._allPlayers : null;
            if (all == null) return;

            // solo round: only our own name is highlighted. squad scoring rounds with "prefer solo"
            // on still want the whole squad pink — pull squad keys first and fall back to just us.
            string myKey = LocalPlayerKey();
            var squadKeys = MySquadKeys();
            HashSet<string> highlight = squadKeys != null && squadKeys.Count > 0
                ? squadKeys
                : (string.IsNullOrEmpty(myKey) ? null : new HashSet<string> { myKey });

            // qualify-highlight: colour each name by status (yellow = qualified, red = out). built
            // from _players so we can tag the live score rows without changing their score ordering.
            var statusByKey = QualStatusOn ? BuildQualStatusByKey() : null;

            // index .Values rather than foreach — same reason the squad path does (IL2CPP collections
            // don't play nice with C# foreach). then sort by ranking.
            var values = all.Values;
            var list = new List<FG.Common.PlayerScore>();
            foreach (var v in values)
                if (v != null) list.Add(v);
            // score desc first — Ranking flips between equal-score players every tick, so leading
            // with it makes same-points rows swap. score → playerKey gives a fully stable order.
            list.Sort((a, b) =>
            {
                // qualify-highlight: lock qualified players at the top ordered by their (fixed) finish
                // time, so the board stops reshuffling every tick as live scores move. unqualified
                // players keep score order below them.
                if (QualStatusOn)
                {
                    string aqk = a._player != null ? a._player.playerKey : null;
                    string bqk = b._player != null ? b._player.playerKey : null;
                    bool aq = aqk != null && _qualTimes.ContainsKey(aqk);
                    bool bq = bqk != null && _qualTimes.ContainsKey(bqk);
                    if (aq != bq) return aq ? -1 : 1;
                    if (aq && bq && _qualTimes[aqk] != _qualTimes[bqk])
                        return _qualTimes[aqk].CompareTo(_qualTimes[bqk]);
                }
                if (a.Score != b.Score) return b.Score.CompareTo(a.Score);
                string ak = a._player != null ? a._player.playerKey : "";
                string bk = b._player != null ? b._player.playerKey : "";
                return string.CompareOrdinal(ak, bk);
            });

            // hash the leaderboard order+scores BEFORE painting. if nothing changed since last
            // repaint, skip the whole label/mesh update — this is what makes 30-events-per-frame in
            // a hunt round cheap (30 hash walks, 0 or 1 real paint).
            int sig = 17;
            int rowsToShow = 0;
            for (int i = 0; i < list.Count && rowsToShow < MaxRows; i++)
            {
                var pe = list[i];
                var pp = pe._player;
                if (pp != null && IsGhost(pp.remotePlayerID)) continue;
                string kk = pp != null ? pp.playerKey : "";
                sig = (sig * 31) ^ pe.Score;
                sig = (sig * 31) ^ (kk != null ? kk.GetHashCode() : 0);
                rowsToShow++;
            }
            // also fold the last-drawn row count so shrinking (players leaving) still repaints.
            sig = (sig * 31) ^ rowsToShow;
            if (sig == _lastSoloSig) return;
            _soloSig = sig;

            int row = 0;
            for (int i = 0; i < list.Count && row < MaxRows; i++)
            {
                var e = list[i];
                var p = e._player;
                // ghost beans (PB ghosts) get added to the score manager too — skip their giant id.
                if (p != null && IsGhost(p.remotePlayerID)) continue;
                // place follows our stable score-then-key order — using the game's Ranking here
                // would make the number flip on ties even though the row didn't move.
                int place = row + 1;
                string posText = $"<b><color=#FFFF00>{place}{Suffix(place)}</color></b>";
                string ptsText = $"<size=120%>{e.Score} pts</size>";

                string name = "Player";
                try
                {
                    if (p != null && !string.IsNullOrEmpty(p.playerKey))
                        name = ResolveDisplayName(p.playerKey, highlight);
                }
                catch { }

                // tint the name by qualification status when the highlight toggle is on. SKIP the
                // yellow recolor for our own/squad names — ResolveDisplayName already painted them
                // pink and we want pink to win once we (or a squad member) qualify. red on
                // elimination still wins (loud signal that we're out).
                if (statusByKey != null && p != null && p.playerKey != null && statusByKey.ContainsKey(p.playerKey))
                {
                    int st = statusByKey[p.playerKey];
                    bool mine = IsHighlighted(p.playerKey, highlight);
                    if (st == 2) name = $"<color=#FF5555>{name}</color>";
                    else if (st == 0 && !mine) name = $"<color=#FFFF00>{name}</color>";
                }

                // qualify-highlight: tack the qualify time onto the end of the name text once they've
                // qualified (same label, after the name — no separate object).
                if (QualStatusOn && p != null && p.playerKey != null && _qualTimes.ContainsKey(p.playerKey))
                {
                    TimeSpan qts = TimeSpan.FromSeconds(_qualTimes[p.playerKey]);
                    name += $"   <color=#FFFF00>{string.Format("{0:D2}:{1:D2}:{2:D3}", qts.Minutes, qts.Seconds, qts.Milliseconds)}</color>";
                }

                // solo scoring (points, 1 player per row) — bigger name, this path has one name per
                // row so it has room to breathe (squad rows stay 70% since they pack 2-4 names).
                // nudge the name column ~20px right in points rounds.
                SetRow(row, posText, $"<size=100%>{name}</size>", ptsText, 20f);
                row++;
            }
            _lastSoloSig = _soloSig;
            Plugin.Log.LogDebug($"solo board repaint, {row} rows from {list.Count} scores");

            // clear any rows we used previously but no longer need.
            for (int i = row; i < MaxRows; i++)
                HideRow(i);
        }

        static IEnumerator SoloScorePollCoroutine()
        {
            int gen = _spawnGen;
            // drain per frame (not 0.5s): a frame with score events sets _soloDirty and we repaint
            // once here, coalescing the whole burst. also force a repaint on a slow ~0.5s cadence as
            // a safety net for anything that changes the board without going through the patches.
            float sinceForced = 0f;
            while (gen == _spawnGen && _panels.Count > 0)
            {
                if (IsScoringRound())
                {
                    sinceForced += Time.unscaledDeltaTime;
                    bool forced = sinceForced >= 0.5f;
                    if (_soloDirty || forced)
                    {
                        _soloDirty = false;
                        if (forced) sinceForced = 0f;
                        RefreshSoloScores();
                    }
                }
                yield return null;
            }
        }

        // called from the SoloScoreManager patches when a score changes — just mark dirty. the poll
        // drains it once next frame, so a burst of 24 score events in one frame is one repaint, not 24.
        public static void OnSoloScoreChanged()
        {
            if (!_updating || !Enabled || _panels.Count == 0) return;
            if (!IsScoringRound()) return;
            _soloDirty = true;
        }

        // ── qualification-status roster (yellow = in, red = out) ───────────────

        // qualify highlight is always on (yellow=in, red=out) — no toggle.
        static bool QualStatusOn => true;
        // when on, PB ghost beans (giant remotePlayerID) are NOT filtered out of the leaderboard.
        static bool IsGhost(uint remotePlayerID) => remotePlayerID >= 100000;

        // owns the list in non-squad, non-scoring rounds (races / survivals) where there's no live
        // score — there it shows finish times. scoring rounds keep their live score leaderboard
        // (RefreshSoloScores) but get the qual colouring applied to names. squad rounds keep squads.
        static bool QualStatusActive() => QualStatusOn && !HasSquadData() && !IsScoringRound();

        // playerKey -> status (0 = qualified, 1 = playing, 2 = eliminated) from _players, ghosts
        // skipped. used to tint the live score leaderboard's names in scoring rounds.
        static Dictionary<string, int> BuildQualStatusByKey()
        {
            var map = new Dictionary<string, int>();
            var cpm = BetterFG.Utilities.PlayerInformation.GetClientPlayerManager();
            var players = cpm != null ? cpm._players : null;
            if (players == null) return map;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null || string.IsNullOrEmpty(p.playerKey)) continue;
                if (IsGhost(p.remotePlayerID)) continue;
                map[p.playerKey] = p.completedLevel ? 0 : (p.fgcc == null ? 2 : 1);
            }
            return map;
        }

        static IEnumerator QualStatusPollCoroutine()
        {
            int gen = _spawnGen;
            var wait = new WaitForSeconds(0.5f);
            while (gen == _spawnGen && _panels.Count > 0)
            {
                if (QualStatusActive()) RefreshQualStatus();
                yield return wait;
            }
        }

        // qualify-highlight leaderboard: qualified players ranked by finish time (yellow, with time),
        // then eliminated players (red, no time). still-racing players get no row so the list starts
        // empty and fills in as people finish/die. ghost beans (PB ghosts) spawn with a giant
        // remotePlayerID (SpawnBeanUtils starts at 100000) so they're skipped.
        static void RefreshQualStatus()
        {
            if (!_updating || !Enabled) return;
            if (!_entriesCaptured) CaptureEntries();
            if (!_entriesCaptured) return;

            var cpm = BetterFG.Utilities.PlayerInformation.GetClientPlayerManager();
            var players = cpm != null ? cpm._players : null;
            if (players == null) return;

            // keep our own/squad names pink even after qualification. solos collapse to just us.
            string myKey = LocalPlayerKey();
            var squadKeys = MySquadKeys();
            HashSet<string> highlight = squadKeys != null && squadKeys.Count > 0
                ? squadKeys
                : (string.IsNullOrEmpty(myKey) ? null : new HashSet<string> { myKey });

            var quals = new List<(string name, float time, bool timed, uint id, bool mine)>();
            var outs = new List<(string name, uint id, bool mine)>();
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null || string.IsNullOrEmpty(p.playerKey)) continue;
                if (IsGhost(p.remotePlayerID)) continue;             // PB ghost bean (unless kept)
                bool mine = IsHighlighted(p.playerKey, highlight);
                if (p.completedLevel)
                {
                    bool timed = _qualTimes.TryGetValue(p.playerKey, out float t);
                    quals.Add((ResolveDisplayName(p.playerKey, highlight), t, timed, p.remotePlayerID, mine));
                }
                else if (p.fgcc == null && ShowEliminatedNow())
                    outs.Add((ResolveDisplayName(p.playerKey, highlight), p.remotePlayerID, mine));
                // else still playing — skip
            }

            // qualified ranked by finish time (timed first, fastest first); id as a stable tiebreak.
            quals.Sort((a, b) =>
            {
                if (a.timed != b.timed) return a.timed ? -1 : 1;
                if (a.timed && a.time != b.time) return a.time.CompareTo(b.time);
                return a.id.CompareTo(b.id);
            });
            outs.Sort((a, b) => a.id.CompareTo(b.id));

            int row = 0;
            for (int i = 0; i < quals.Count && row < MaxRows; i++)
            {
                var e = quals[i];
                int place = row + 1;
                string time = "";
                if (e.timed)
                {
                    TimeSpan ts = TimeSpan.FromSeconds(e.time);
                    time = $"<size=110%>{string.Format("{0:D2}:{1:D2}:{2:D3}", ts.Minutes, ts.Seconds, ts.Milliseconds)}</size>";
                }
                // ours: name keeps its pink wrap from ResolveDisplayName. others: yellow.
                string nameText = e.mine
                    ? $"<size=85%>{e.name}</size>"
                    : $"<size=85%><color=#FFFF00>{e.name}</color></size>";
                SetRow(row,
                    $"<b><color=#FFFF00>{place}{Suffix(place)}</color></b>",
                    nameText,
                    time, 80f);
                row++;
            }
            for (int i = 0; i < outs.Count && row < MaxRows; i++)
            {
                // eliminated: red wins even for our own — losing is loud.
                SetRow(row,
                    "<b><color=#FF5555>OUT</color></b>",
                    $"<size=85%><color=#FF5555>{outs[i].name}</color></size>",
                    "", 80f);
                row++;
            }

            for (int i = row; i < MaxRows; i++)
                HideRow(i);
        }

        const string HotPink = "#FF1FA6";
        static string _cachedLocalKey = "";   // remembered while alive so it survives death/spectate

        // the local player's key — pulled off our NetworkPlayerDataClient so it's the SAME playerKey
        // format the squad members / PlayerScores use (GetLocalPlayerKey returns a different format
        // that won't match). once we're dead/spectating GetLocalPlayerData() returns null (our fgcc
        // is gone), so we cache the key the first time we find it and keep using it.
        static string LocalPlayerKey()
        {
            string key = BetterFG.Utilities.PlayerInformation.GetLocalPlayerData()?.playerKey ?? "";

            // data path comes back empty a lot (no fgcc when finished/dead/spectating). fall back to
            // the GlobalGameStateClient key, which is the SAME playerKey but with a "pc_steam_" (or
            // other platform) prefix tacked on — strip the prefix so it matches the squad/score
            // playerKeys. without this our own custom name never gets applied on race finishes,
            // because the isMe check below never matches an empty key.
            if (string.IsNullOrEmpty(key))
            {
                string ggs = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? "";
                // ggs is "<platform>_<service>_<bareKey>" (e.g. "pc_steam_zmxnczxcnjzxcnjzx").
                // everyone else is keyed by just the bareKey, so strip the first two underscore-
                // delimited segments. don't use LastIndexOf — a bareKey could itself contain '_'
                // and we'd over-strip it.
                if (!string.IsNullOrEmpty(ggs))
                {
                    int first = ggs.IndexOf('_');
                    int second = first >= 0 ? ggs.IndexOf('_', first + 1) : -1;
                    key = second >= 0 && second < ggs.Length - 1 ? ggs.Substring(second + 1) : ggs;
                }
            }

            if (!string.IsNullOrEmpty(key)) _cachedLocalKey = key;
            else if (!string.IsNullOrEmpty(_cachedLocalKey)) key = _cachedLocalKey;
            return key;
        }

        // do two playerKeys point at the same player? our local key (myKey) comes out BARE (ggs key
        // with the platform prefix stripped) while squad members / score entries carry the RAW key, so
        // a plain == misses ourselves. clean both sides and compare those. this is the ONE place that
        // reconciles bare-vs-raw — every "is this me / my squad" check routes through here.
        static bool KeysMatch(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (a == b) return true;
            string ca = FallGuysLib.Players.PlayerUtils.CleanPlayerName(a);
            string cb = FallGuysLib.Players.PlayerUtils.CleanPlayerName(b);
            return !string.IsNullOrEmpty(ca) && ca == cb;
        }

        // playerKeys of everyone in MY squad — those names get colored hot pink. empty in non-squad
        // rounds (where only my own name is highlighted via the per-key path).
        static HashSet<string> MySquadKeys()
        {
            var keys = new HashSet<string>();
            string myKey = LocalPlayerKey();
            if (string.IsNullOrEmpty(myKey)) return keys;
            var squads = ClientSquadManager.Instance?._roundSquads;
            if (squads == null) return keys;
            foreach (var sid in squads.Keys)
            {
                var members = squads[sid];
                if (members == null) continue;
                bool mine = false;
                for (int i = 0; i < members.Count; i++)
                    if (members[i] != null && KeysMatch(members[i].playerKey, myKey)) { mine = true; break; }
                if (!mine) continue;
                for (int i = 0; i < members.Count; i++)
                    if (members[i] != null && !string.IsNullOrEmpty(members[i].playerKey))
                        keys.Add(members[i].playerKey);
                return keys;
            }
            return keys;
        }

        // the squadId our local player is in, or null. used to pink the "Squad N" fallback label.
        static uint? MySquadId()
        {
            string myKey = LocalPlayerKey();
            if (string.IsNullOrEmpty(myKey)) return null;
            var squads = ClientSquadManager.Instance?._roundSquads;
            if (squads == null) return null;
            foreach (var sid in squads.Keys)
            {
                var members = squads[sid];
                if (members == null) continue;
                for (int i = 0; i < members.Count; i++)
                    if (members[i] != null && KeysMatch(members[i].playerKey, myKey)) return sid;
            }
            return null;
        }

        // "Squad N" label, hot pink when it's our own squad (used when names are turned off).
        static string SquadLabel(uint sid)
        {
            var mine = MySquadId();
            string text = $"Squad {sid}";
            return (mine.HasValue && mine.Value == sid) ? $"<color={HotPink}>{text}</color>" : text;
        }

        // turn a playerKey into the display name: our own custom name for us, cleaned name otherwise.
        // wraps in hot pink when the key is in highlightKeys (us, or our whole squad).
        static string ResolveDisplayName(string playerKey, HashSet<string> highlightKeys)
        {
            if (string.IsNullOrEmpty(playerKey)) return "";
            string myKey = LocalPlayerKey();
            bool isMe = !string.IsNullOrEmpty(myKey) && KeysMatch(playerKey, myKey);
            // our own name: custom name if set, otherwise the cleaned real name. everyone else:
            // cleaned name. always run it through PlayerUtils so size/format tags are stripped.
            string name = (isMe && !string.IsNullOrEmpty(LocalPlayerInfo.CustomName))
                ? LocalPlayerInfo.CustomName
                : FallGuysLib.Players.PlayerUtils.CleanPlayerName(playerKey);
            if (IsHighlighted(playerKey, highlightKeys))
                return $"<color={HotPink}>{name}</color>";
            return name;
        }

        // membership test that handles the bare-vs-raw key mismatch (same reason KeysMatch exists).
        // a plain HashSet.Contains misses our own bare key against raw squad/score keys, so loop and
        // use KeysMatch — sets are tiny (≤ squad size).
        static bool IsHighlighted(string playerKey, HashSet<string> highlightKeys)
        {
            if (highlightKeys == null || string.IsNullOrEmpty(playerKey)) return false;
            if (highlightKeys.Contains(playerKey)) return true;
            foreach (var k in highlightKeys)
                if (KeysMatch(k, playerKey)) return true;
            return false;
        }

        static string ResolvePlayerName(uint remotePlayerId)
        {
            string key = PlayerKeyById(remotePlayerId);
            if (string.IsNullOrEmpty(key)) return "Player " + remotePlayerId;
            string myKey = LocalPlayerKey();
            if (!string.IsNullOrEmpty(myKey) && KeysMatch(key, myKey))
            {
                string n = !string.IsNullOrEmpty(LocalPlayerInfo.CustomName)
                    ? LocalPlayerInfo.CustomName
                    : FallGuysLib.Players.PlayerUtils.CleanPlayerName(key);
                return $"<color={HotPink}>{n}</color>";
            }
            return FallGuysLib.Players.PlayerUtils.CleanPlayerName(key);
        }
    }

    // solo scoring (hunt / bubble / score-target) fires these on the SoloScoreManager whenever a
    // player's points change. log them and kick an immediate leaderboard repaint so the points list
    // updates the instant a score lands instead of waiting for the poll tick.
    // Harmony binds params by name — we only need the int score arg, so we skip the MPGNetID
    // (avoids needing to name its interop type) and just take amount/value.
    [HarmonyPatch(typeof(FG.Common.SoloScoreManager), "AwardSoloPoints")]
    internal static class Patch_TimePlacement_AwardSoloPoints
    {
        [HarmonyPostfix]
        public static void Postfix() => FeatureTimePlacement.OnSoloScoreChanged();
    }

    [HarmonyPatch(typeof(FG.Common.SoloScoreManager), "SetSoloScore")]
    internal static class Patch_TimePlacement_SetSoloScore
    {
        [HarmonyPostfix]
        public static void Postfix() => FeatureTimePlacement.OnSoloScoreChanged();
    }

    // when a banner (Qualified / Eliminated / Winner / OutForNow / RoundEnded) opens, reparent its
    // GameObject into BannersState as the FIRST sibling so the leaderboard (also under BannersState,
    // sitting after the banner in sibling order) draws on top. SetParent(..., worldPositionStays:true)
    // keeps the banner's screen position/scale exactly as the game set it. patches live in
    // GameStatePatches.cs and call into here.
    public static class TimePlacementBannerReparent
    {
        public static void Apply(UnityEngine.Component banner)
        {
            if (banner == null) return;
            var clone = FeatureTimePlacement.GetBannersStateClone();
            if (clone == null) return;
            var bannersState = clone.transform.parent;   // our clone is parented under BannersState
            if (bannersState == null) return;
            var t = banner.transform;
            if (t.parent == bannersState && t.GetSiblingIndex() == 0) return;
            t.SetParent(bannersState, true);
            t.SetAsFirstSibling();
        }
    }
}
