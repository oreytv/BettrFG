using System;
using BetterFG.Core;
using BetterFG.Customization.Menu;
using FG.Common;
using FGClient;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BetterFG.Tweaks
{
    // Shows the current round's description in the bottom-right of the screen while the pause menu is
    // open. The game's own description text flashes by too fast on the loading screen, so this keeps it
    // readable: pause whenever you want and read it. Lives under PartyMenu so it sits on the pause
    // overlay's canvas.
    public class LevelDescriptionOnPauseTweak : BfgTweak
    {
        public LevelDescriptionOnPauseTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "level_desc_on_pause";
        public override string TweakLabel => "Show Level Description on Pause";
        public override bool DefaultEnabled => true;

        public static LevelDescriptionOnPauseTweak Instance { get; private set; }
        void Awake() => Instance = this;

        private GameObject _root;
        private TMP_Text _body;

        public override void DisableTweak()
        {
            if (_root != null) _root.SetActive(false);
        }

        public void OnPauseToggled(bool isOpen)
        {
            if (!IsEnabled || !isOpen) { if (_root != null) _root.SetActive(false); return; }

            string desc = CurrentDescription();
            if (string.IsNullOrEmpty(desc)) { if (_root != null) _root.SetActive(false); return; }

            if (_root == null && !BuildLabel()) return;
            _body.text = desc;
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
        }

        private static string CurrentDescription()
        {
            var gsv = GlobalGameStateClient.Instance?.GameStateView;
            if (gsv == null || !gsv.GetLiveClientGameManager(out var cgm) || cgm?._round == null) return null;
            return cgm._round.RoundDescription?.Text;
        }

        // title in Titan One, body in Asap, both right-aligned and pinned to the screen's bottom-right.
        // body block sits at the corner, title stacks just above it.
        private bool BuildLabel()
        {
            var party = GameObject.Find("UICanvas_Client_V2(Clone)/PartyMenu");
            if (party == null) return false;

            _root = new GameObject("BettrFG_LevelDescOnPause");
            var rootRt = _root.AddComponent<RectTransform>();
            rootRt.SetParent(party.transform, false);
            rootRt.anchorMin = rootRt.anchorMax = rootRt.pivot = new Vector2(1f, 0f);
            rootRt.anchoredPosition = new Vector2(-40f, 40f);
            rootRt.sizeDelta = new Vector2(640f, 0f);

            const float bodyH = 400f;

            var bodyGo = new GameObject("Body");
            var bodyRt = bodyGo.AddComponent<RectTransform>();
            bodyRt.SetParent(rootRt, false);
            // top pinned just under the title, text flows downward from there
            bodyRt.anchorMin = bodyRt.anchorMax = bodyRt.pivot = new Vector2(1f, 1f);
            bodyRt.anchoredPosition = new Vector2(0f, bodyH);
            bodyRt.sizeDelta = new Vector2(640f, bodyH);
            _body = bodyGo.AddComponent<TextMeshProUGUI>();
            _body.font = AssetManager.NameFontAsset;
            _body.fontSize = 24f;
            _body.color = Color.white;
            _body.alignment = TextAlignmentOptions.TopRight;
            _body.enableWordWrapping = true;
            _body.overflowMode = TextOverflowModes.Overflow;
            _body.raycastTarget = false;

            var titleGo = new GameObject("Title");
            var titleRt = titleGo.AddComponent<RectTransform>();
            titleRt.SetParent(rootRt, false);
            titleRt.anchorMin = titleRt.anchorMax = titleRt.pivot = new Vector2(1f, 0f);
            titleRt.anchoredPosition = new Vector2(0f, bodyH + 8f);
            titleRt.sizeDelta = new Vector2(640f, 48f);
            var title = titleGo.AddComponent<TextMeshProUGUI>();
            title.font = FontReplacementService.GetFontAssetByName("TitanOne-Expanded SDF (Title)");
            title.fontSize = 32f;
            title.color = Color.white;
            title.alignment = TextAlignmentOptions.BottomRight;
            title.text = "LEVEL DESCRIPTION";
            title.raycastTarget = false;

            return true;
        }
    }

    [HarmonyPatch(typeof(InGameMenuViewModel), "ToggleOpen")]
    internal static class InGameMenuTogglePatch
    {
        [HarmonyPostfix]
        static void Postfix(bool isInGameMenuOpen)
            => LevelDescriptionOnPauseTweak.Instance?.OnPauseToggled(isInGameMenuOpen);
    }
}
