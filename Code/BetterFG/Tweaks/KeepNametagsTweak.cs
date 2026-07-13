using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FGClient;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BetterFG.Tweaks
{
    // qual screen tears down the gameplay root (----------------GAMEPLAY), which takes every bean's
    // InfoUI (nametag + creator code) with it. we reparent every InfoUI to scene root right after the
    // state switch so they survive, then reapply on MarkAsEliminated / Qualified (those are what
    // strip NameLayout + reset the texts on the corresponding bean).
    public class KeepNametagsTweak : BfgTweak
    {
        public KeepNametagsTweak(System.IntPtr ptr) : base(ptr) { }

        public override string TweakId => "keep_nametags";
        public override string TweakLabel => "Keep nametags on qual screen";
        public override bool DefaultEnabled => false;

        public static KeepNametagsTweak Instance { get; private set; }
        void Awake() => Instance = this;

        // detached InfoUIs miss the game's in-game "hide nametags" toggle (that pipe only walks
        // live NameTagViewModels attached to beans). mirror the state of a still-attached InfoUI
        // onto every snapshot so eliminated names hide/show together.
        //
        // the reference InfoUI is CACHED and the scene scan is throttled — FindObjectsOfType<Transform>
        // walks every transform in the game through interop, and running it per frame was THE
        // session-long fps killer (snapshots stayed populated after the round, so from the first
        // qual screen on, every frame paid a whole-scene scan). dead snapshots get pruned so this
        // path shuts off completely once the round's InfoUIs are gone.
        static Transform _liveInfoUi;
        static float _scanCooldown;
        static bool? _lastShow;

        void Update()
        {
            if (!IsOn || _snapshots.Count == 0) return;

            if (_liveInfoUi == null)
            {
                _scanCooldown -= Time.unscaledDeltaTime;
                if (_scanCooldown > 0f) return;
                _scanCooldown = 0.5f;

                PruneDeadSnapshots();
                if (_snapshots.Count == 0) return;

                foreach (var t in Object.FindObjectsOfType<Transform>())
                {
                    if (t == null || t.name != "InfoUI") continue;
                    if (_snapshots.ContainsKey(t)) continue;
                    if (t.Find("NameLayout") == null) continue;
                    _liveInfoUi = t;
                    break;
                }
                if (_liveInfoUi == null) return;
            }

            var liveLayout = _liveInfoUi.Find("NameLayout");
            if (liveLayout == null) { _liveInfoUi = null; _lastShow = null; return; }

            bool show = liveLayout.gameObject.activeSelf;
            if (_lastShow == show) return;
            _lastShow = show;

            // the snapshot has to move with the toggle. if we only SetActive here, the stored actives still
            // say what NameLayout was at reparent time, and the teardown restore re-asserts THAT for 4s —
            // toggle names on during the qual screen with them hidden at reparent and every name dies.
            var keys = new List<Transform>(_snapshots.Keys);
            foreach (var t in keys)
            {
                if (t == null) continue;
                var nameLayout = t.Find("NameLayout");
                if (nameLayout == null) continue;
                if (nameLayout.gameObject.activeSelf != show) nameLayout.gameObject.SetActive(show);

                var snap = _snapshots[t];
                var go = nameLayout.gameObject;
                for (int i = 0; i < snap.actives.Count; i++)
                    if (snap.actives[i].go == go) { snap.actives[i] = (go, show); break; }
                _snapshots[t] = snap;
            }
        }

        static void PruneDeadSnapshots()
        {
            List<Transform> dead = null;
            foreach (var kv in _snapshots)
                if (kv.Key == null) (dead ??= new List<Transform>()).Add(kv.Key);
            if (dead == null) return;
            foreach (var t in dead) _snapshots.Remove(t);
            if (_snapshots.Count == 0)
                Plugin.Log.LogInfo("KeepNametags: round's InfoUIs gone, snapshots cleared");
        }

        // InfoUI transform -> snapshot of every TMP text + every GameObject's active state under it,
        // taken right before the qual screen gets a chance to blank them.
        struct InfoSnapshot
        {
            public List<(TMP_Text tmp, string text)> texts;
            public List<(GameObject go, bool active)> actives;
            // qual screen teardown zeroes CrownRankBadgeViewModel._currentCrownRank, so the pip
            // comes back blank even after we restore the GameObject actives. cache the rank at
            // reparent time and re-Set it after every restore pass.
            public int crownRank;
            public bool hasCrownRank;
        }
        static readonly Dictionary<Transform, InfoSnapshot> _snapshots = new();

        internal static bool IsOn => Instance != null && Instance.IsEnabled;

        internal static void OnQualScreenSwitch()
        {
            if (!IsOn) return;
            Instance.StartCoroutine(RunAfterDelay().WrapToIl2Cpp());
        }

        // qual screen teardown fires one last "hide the nametag" pass on some beans (usually the ones
        // that got Qualified/Eliminated late), so their NameLayout is off going into gameplay. wait a
        // frame for teardown's stomps to land, then restore every snapshot we still have.
        internal static void OnQualScreenTeardown()
        {
            if (!IsOn) return;
            Instance.StartCoroutine(RestoreAllNextFrame().WrapToIl2Cpp());
        }

        static IEnumerator RestoreAllNextFrame()
        {
            // teardown keeps re-blanking the InfoUIs for a few frames after the state swap (the
            // MarkAsEliminated/Qualified pass re-fires on the cells as gameplay unwinds), so a single
            // next-frame restore gets stomped and every name vanishes. re-assert the snapshot over a
            // handful of frames so we get the last write.
            int restored = 0;
            for (int frame = 0; frame < 8; frame++)
            {
                yield return null;
                restored = RestoreAllSnapshots();
            }
            Plugin.Log.LogInfo($"KeepNametags: restored {restored} InfoUI(s) after qual teardown (8 frames)");

            // late stomps land past the frame window; realtime timer since timeScale is 0 through the load
            for (float t = 0f; t < 4f; t += 0.25f)
            {
                yield return new WaitForSecondsRealtime(0.25f);
                restored = RestoreAllSnapshots();
            }
            Plugin.Log.LogInfo($"KeepNametags: fallback restore done, {restored} InfoUI(s) still tracked");
        }

        static int RestoreAllSnapshots()
        {
            bool textOn = BetterFG.Nametag.CrownRankService.Enabled && BetterFG.Nametag.CrownRankService.TextOn
                          && !string.IsNullOrEmpty(BetterFG.Nametag.CrownRankService.RankText);

            int restored = 0;
            foreach (var kv in _snapshots)
            {
                var t = kv.Key;
                if (t == null) continue;
                var snap = kv.Value;

                // is THIS the local player's tag AND are we overriding the crown text? if so, force our crown
                // text instead of the snapshot's real value — this teardown restore was the last thing stomping
                // it back to 58. skip restoring the crown label's TMP text for the local override too.
                bool localOverride = textOn && InfoUiIsLocal(t);
                var localCrown = localOverride ? FindCrownRankBadge(t) : null;
                Transform crownT = localCrown != null ? localCrown.transform : null;

                foreach (var (go, active) in snap.actives)
                    if (go != null && go.activeSelf != active) go.SetActive(active);
                foreach (var (tmp, text) in snap.texts)
                {
                    if (tmp == null || text == null) continue;
                    if (crownT != null && tmp.transform.IsChildOf(crownT)) continue; // don't restore our crown label
                    if (tmp.text != text) tmp.text = text;
                }
                if (snap.hasCrownRank)
                {
                    var badge = localCrown ?? FindCrownRankBadge(t);
                    if (badge != null)
                    {
                        if (localOverride)
                        {
                            badge.CrownRankText = BetterFG.Nametag.CrownRankService.RankText;
                            badge.CrownRankActive = true;
                        }
                        else badge.SetCrownRank(snap.crownRank);
                    }
                }
                restored++;
            }
            return restored;
        }

        static IEnumerator RunAfterDelay()
        {
            yield return new WaitForSeconds(0.3f);
            _snapshots.Clear();
            _liveInfoUi = null;
            _lastShow = null;
            _scanCooldown = 0f;

            int moved = 0;
            foreach (var t in Object.FindObjectsOfType<Transform>())
            {
                if (t == null || t.name != "InfoUI") continue;
                var root = t;
                while (root.parent != null) root = root.parent;
                if (root.name == null || !root.name.ToUpperInvariant().Contains("GAMEPLAY")) continue;

                // strip Animators so the qual screen can't drive NameLayout off / blank the texts.
                foreach (var anim in t.GetComponentsInChildren<Animator>(true))
                    if (anim != null) Object.Destroy(anim);

                var badge = FindCrownRankBadge(t);

                // snapshot every text AND every descendant's active state so we can restore both in the postfix
                // hook (crown rank pip, platform icon, name row, etc all live under here). we cache the game's
                // real crown label/rank as-is here — the local player's override is re-asserted afterwards by
                // ForceCrownForBean, so other players keep their real value and ours wins for us.
                var snapTexts = new List<(TMP_Text, string)>();
                foreach (var tmp in t.GetComponentsInChildren<TMP_Text>(true))
                    if (tmp != null) snapTexts.Add((tmp, tmp.text));
                var snapActives = new List<(GameObject, bool)>();
                foreach (var child in t.GetComponentsInChildren<Transform>(true))
                    if (child != null) snapActives.Add((child.gameObject, child.gameObject.activeSelf));

                int crownRank = 0;
                bool hasCrown = false;
                if (badge != null) { crownRank = badge._currentCrownRank; hasCrown = true; }
                _snapshots[t] = new InfoSnapshot { texts = snapTexts, actives = snapActives, crownRank = crownRank, hasCrownRank = hasCrown };

                t.SetParent(null, true);
                moved++;
            }
            Plugin.Log.LogInfo($"KeepNametags: reparented {moved} InfoUI object(s) to root");
        }

        // called from the CellBehaviour postfixes below. the podium cell holds _fallGuy — the actual
        // bean transform. find its InfoUI (the one we reparented), turn NameLayout back on and
        // restore every text from the snapshot.
        internal static void OnCellStateChanged(CellBehaviour cell)
        {
            if (!IsOn || cell == null) return;
            var fg = cell._fallGuy;
            if (fg == null) { Plugin.Log.LogInfo($"KeepNametags: OnCellStateChanged cell={cell.name} but _fallGuy null, snaps={_snapshots.Count}"); return; }
            Plugin.Log.LogInfo($"KeepNametags: OnCellStateChanged fg={fg.name} snaps={_snapshots.Count}");
            // RestoreForBean AND RestoreAllSnapshots (teardown) both now force our crown override directly on the
            // local tag as part of the restore, so there's no separate racing reassert to lose.
            Instance.StartCoroutine(RestoreForBean(fg.transform).WrapToIl2Cpp());
        }

        // does this InfoUI's shown name belong to the local player? matched on the cleaned name against our
        // custom name (if set) or real FG username — survives profile name swaps. reads the name off THIS
        // InfoUI (already isolated / reparented), not the podium cell.
        static bool InfoUiIsLocal(Transform infoUi)
        {
            var nameLayout = infoUi.Find("NameLayout");
            if (nameLayout == null) return false;
            string shown = null;
            foreach (var tmp in nameLayout.GetComponentsInChildren<TMP_Text>(true))
            {
                if (tmp == null) continue;
                if (tmp.transform.parent != null && tmp.transform.parent.name != null
                    && tmp.transform.parent.name.Contains("CrownRank")) continue; // skip the crown digits
                if (!string.IsNullOrEmpty(tmp.text)) { shown = tmp.text; break; }
            }
            if (string.IsNullOrEmpty(shown)) return false;

            string clean = FallGuysLib.Players.PlayerUtils.CleanPlayerName(shown);
            string custom = BetterFG.Core.LocalPlayerInfo.CustomName;
            string real = BetterFG.Core.LocalPlayerInfo.FGlocalplayerusername;
            return (!string.IsNullOrEmpty(custom) && clean.Equals(FallGuysLib.Players.PlayerUtils.CleanPlayerName(custom), System.StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrEmpty(real)   && clean.Equals(FallGuysLib.Players.PlayerUtils.CleanPlayerName(real),   System.StringComparison.OrdinalIgnoreCase));
        }

        static IEnumerator RestoreForBean(Transform beanRoot)
        {
            // wait one frame so whatever MarkAsEliminated/Qualified did to the visuals lands first,
            // then we overwrite it.
            yield return null;
            if (beanRoot == null) yield break;

            // find the InfoUI belonging to this bean. we detached it from the bean when we reparented
            // to scene root, so we can't walk down from the bean any more — match by proximity /
            // by which snapshot's transform is closest to the bean.
            Transform target = null;
            float bestSqr = float.MaxValue;
            var beanPos = beanRoot.position;
            foreach (var kv in _snapshots)
            {
                var t = kv.Key;
                if (t == null) continue;
                float d = (t.position - beanPos).sqrMagnitude;
                if (d < bestSqr) { bestSqr = d; target = t; }
            }
            if (target == null) { Plugin.Log.LogInfo("KeepNametags: RestoreForBean no target matched"); yield break; }

            if (!_snapshots.TryGetValue(target, out var snap)) yield break;
            Plugin.Log.LogInfo($"KeepNametags: RestoreForBean matched target={target.name} distSqr={bestSqr:0.0} texts={snap.texts.Count} actives={snap.actives.Count}");

            bool textOn = BetterFG.Nametag.CrownRankService.Enabled && BetterFG.Nametag.CrownRankService.TextOn
                          && !string.IsNullOrEmpty(BetterFG.Nametag.CrownRankService.RankText);

            bool localOverride = false;
            CrownRankBadgeViewModel localCrown = null;
            try { localOverride = textOn && InfoUiIsLocal(target); localCrown = localOverride ? FindCrownRankBadge(target) : null; }
            catch (System.Exception ex) { Plugin.Log.LogWarning("KeepNametags: local check threw " + ex.Message); }
            Transform crownT = localCrown != null ? localCrown.transform : null;

            // CROWN FIRST — before the fragile actives/texts loops that can throw & kill the coroutine. this is
            // the whole point: our override lands even if the rest of the restore dies.
            if (snap.hasCrownRank)
            {
                var badge = localCrown ?? FindCrownRankBadge(target);
                if (badge != null)
                {
                    if (localOverride)
                    {
                        badge.CrownRankText = BetterFG.Nametag.CrownRankService.RankText;
                        badge.CrownRankActive = true;
                        Plugin.Log.LogInfo($"KeepNametags: forced crown text='{BetterFG.Nametag.CrownRankService.RankText}'");
                    }
                    else badge.SetCrownRank(snap.crownRank);
                }
            }

            try
            {
                foreach (var (go, active) in snap.actives)
                    if (go != null && go.activeSelf != active) go.SetActive(active);
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning("KeepNametags: actives loop threw " + ex.Message); }

            try
            {
                foreach (var (tmp, text) in snap.texts)
                {
                    if (tmp == null || text == null) continue;
                    if (crownT != null && tmp.transform.IsChildOf(crownT)) continue; // keep our crown label
                    if (tmp.text != text) tmp.text = text;
                }
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning("KeepNametags: texts loop threw " + ex.Message); }
        }

        // InfoUI/NameLayout/Generic_UI_CrownRankCounter_3D_Prefab, with a
        // CrownRankBadgeViewModel component.
        static CrownRankBadgeViewModel FindCrownRankBadge(Transform infoUi)
        {
            if (infoUi == null) return null;
            var nameLayout = infoUi.Find("NameLayout");
            if (nameLayout == null) return null;
            var t = nameLayout.Find("Generic_UI_CrownRankCounter_3D_Prefab");
            if (t == null) return null;
            return t.GetComponent<CrownRankBadgeViewModel>();
        }
    }

    [HarmonyPatch(typeof(CellBehaviour), nameof(CellBehaviour.MarkAsEliminated))]
    internal static class Patch_KeepNametags_MarkAsEliminated
    {
        [HarmonyPostfix]
        public static void Postfix(CellBehaviour __instance)
        {
            KeepNametagsTweak.OnCellStateChanged(__instance);
            // the game re-sets the real crown rank on elimination, stomping our rank-text override. re-assert
            // ours a frame later. only the text — style/recolour aren't touched by this path.
            CellCrownRank.ReassertText(__instance);
        }
    }

    [HarmonyPatch(typeof(CellBehaviour), nameof(CellBehaviour.Qualified))]
    internal static class Patch_KeepNametags_Qualified
    {
        [HarmonyPostfix]
        public static void Postfix(CellBehaviour __instance)
        {
            KeepNametagsTweak.OnCellStateChanged(__instance);
            CellCrownRank.ReassertText(__instance);
        }
    }

    internal static class CellCrownRank
    {
        public static void ReassertText(CellBehaviour cell)
        {
            if (cell == null) return;
            if (!BetterFG.Nametag.CrownRankService.Enabled || !BetterFG.Nametag.CrownRankService.TextOn
                || string.IsNullOrEmpty(BetterFG.Nametag.CrownRankService.RankText)) return;
            BetterFG.Services.BeanMonitorService.Instance?.StartCoroutine(NextFrame(cell).WrapToIl2Cpp());
        }

        static IEnumerator NextFrame(CellBehaviour cell)
        {
            yield return null;
            var t = cell != null ? cell.transform.Find("InfoUI/NameLayout/Generic_UI_CrownRankCounter_3D_Prefab") : null;
            var badge = t != null ? t.GetComponent<CrownRankBadgeViewModel>() : null;
            if (badge != null)
            {
                badge.CrownRankText = BetterFG.Nametag.CrownRankService.RankText;
                badge.CrownRankActive = true;
            }
        }
    }
}
