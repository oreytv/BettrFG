using System;
using System.Collections;
using System.Text.RegularExpressions;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FGClient;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Tweaks
{
    public class ShowTilePlaysTweak : BfgTweak
    {
        public ShowTilePlaysTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "showtile_plays";
        public override string TweakLabel => "Show Discovery play counts";
        public override bool DefaultEnabled => true;

        public static ShowTilePlaysTweak Instance { get; private set; }
        void Awake() => Instance = this;

        // stale suffix from a previous occupant of this pooled tile — strip before re-appending
        static readonly Regex _suffix = new Regex(@"\s-\s[\d,]+\sPLAYS$", RegexOptions.Compiled);

        public static void OnSetShowData(FGClient.ShowSelector.ShowSelectorShowTileViewModel tile, ShowSelectorShow show)
        {
            var inst = Instance;
            if (inst == null || !inst.IsEnabled || tile == null || show?.Stats == null) return;

            var tmp = tile.transform.Find("ShowTileHolder/DefaultTags1/GameMode/GameMode_Text")?.GetComponent<TextMeshProUGUI>();
            inst.Set(tmp, show.Stats.PlayCount);
        }

        void Set(TextMeshProUGUI tmp, int playCount)
        {
            if (tmp == null) return;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.text = $"{_suffix.Replace(tmp.text ?? "", "")} - {playCount:N0} PLAYS";
            tmp.ForceMeshUpdate();
            StartCoroutine(RefitNextFrame(tmp).WrapToIl2Cpp());
        }

        void Strip(TextMeshProUGUI tmp)
        {
            if (tmp == null) return;
            string stripped = _suffix.Replace(tmp.text ?? "", "");
            if (stripped == tmp.text) return;
            tmp.text = stripped;
            tmp.ForceMeshUpdate();
            StartCoroutine(RefitNextFrame(tmp).WrapToIl2Cpp());
        }

        // the layout-rebuild route kept losing to the game's own RefreshTagsLayout. instead, after the
        // game's pass settles, toggle the GameMode tag off then back on a frame later — the reactivate
        // makes Unity re-run its fit from scratch and the pill background snaps to our text.
        IEnumerator RefitNextFrame(TextMeshProUGUI tmp)
        {
            yield return null;
            if (tmp == null) yield break;

            var gameMode = tmp.transform.parent?.gameObject; // GameMode tag
            if (gameMode == null) yield break;

            gameMode.SetActive(false);
            yield return null;
            if (gameMode != null) gameMode.SetActive(true);
        }

        // toggled on mid-browse: stamp every live tile now instead of waiting for a scroll/re-open.
        public override void EnableTweak()
        {
            foreach (var tmp in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                if (tmp == null || tmp.gameObject.name != "GameMode_Text") continue;
                var stats = tmp.GetComponentInParent<FGClient.ShowSelector.ShowSelectorShowTileViewModel>()?._showData?.ShowSelectorShow?.Stats;
                if (stats != null) Set(tmp, stats.PlayCount);
            }
        }

        // toggled off mid-browse: wipe the suffix off every live tile so they clear right away.
        public override void DisableTweak()
        {
            foreach (var tmp in Resources.FindObjectsOfTypeAll<TextMeshProUGUI>())
            {
                if (tmp != null && tmp.gameObject.name == "GameMode_Text") Strip(tmp);
            }
        }
    }
}
