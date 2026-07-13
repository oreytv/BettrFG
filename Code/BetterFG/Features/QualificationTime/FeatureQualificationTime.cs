using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Mediatonic.Tools.MVVM;
using FGClient.UI;
using FGClient.UI.Core;
using FGClient;
using System.Runtime.InteropServices;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Wushu.Framework.ExtensionMethods;
using TMPro;
using FallGuysLib.UI;
using SRF;
using System.Linq;
using FG.Common.CMS;

using BetterFG.Services;
using BetterFG.Core;
using BetterFG.UI;
using BetterFG.Customization.Menu;
using BetterFG.Customization.Player;
using BetterFG.Utilities;
using FallGuysLib.NPC;

using FG.Common;
using Levels.Progression;
using Levels.ScoreZone;
using FG.Common.UI;
using UnityEngine.Playables;
using MPG.Utility;

namespace BetterFG.Features.QualificationTime
{
    internal class FeatureQualificationTime
    {
        public static readonly BfgFeature feature = new BfgFeature("pb", "Personal Bests", true, new List<FeatureSetting>
        {
            new FeatureSetting { id = "store", label = "Store PBs", defaultOn = true },
            new FeatureSetting { id = "qual", label = "Show PB on qual", defaultOn = true },
            new FeatureSetting { id = "loadscreen", label = "Show PB on load screen", defaultOn = true },
            new FeatureSetting { id = "play", label = "Show PB during play", defaultOn = true },
            new FeatureSetting { id = "timer", label = "Show live timer", defaultOn = true },
            new FeatureSetting { id = "menu", label = "Show PB button on menu", defaultOn = true },
            new FeatureSetting { id = "asksave", label = "Ask to save PB", defaultOn = false },
            new FeatureSetting { id = "ghost", label = "Ghost run", defaultOn = true },
        },
        choices: new List<FeatureChoice>
        {
            new FeatureChoice
            {
                id = "ghostmode",
                label = "Ghost run to show",
                optionIds = new List<string> { "current", "solos", "duos", "squads", "fastest", "all" },
                optionLabels = new List<string> { "Current", "Solos", "Duos", "Squads", "Fastest", "All" },
                defaultId = "current",
            },
        });

        static bool On(string setting) => BetterFG.Features.FeatureRegistry.IsOn("pb", setting);

        static readonly AnimationCurve dismissCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.5f, 1.1f),
            new Keyframe(1f, 0f)
        );

        static readonly AnimationCurve popInCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.6f, 1.12f),
            new Keyframe(0.8f, 0.95f),
            new Keyframe(1f, 1f)
        );


        public static void CreateInMenu()
        {
            if (!On("menu")) return;
            var tabsLayout = GameObject.Find("UICanvas_Client_V2(Clone)/Default/Topbar_Prime(Clone)/SafeArea/TabsHorizontalLayout")?.transform;
            var shopBtn = tabsLayout?.Find("ShopButton");
            if (tabsLayout == null || shopBtn == null) return;
            if (tabsLayout.Find("ShopButton(Clone)") != null) return;

            var clone = UnityEngine.Object.Instantiate(shopBtn.gameObject, tabsLayout);
            clone.transform.SetSiblingIndex(9);

            tabsLayout.localScale = Vector3.one * 0.9f;

            var toggle = clone.GetComponent<UnityEngine.UI.Toggle>();
            if (toggle != null)
            {
                toggle.onValueChanged.RemoveAllListeners();
                toggle.onValueChanged.AddListener((UnityEngine.Events.UnityAction<bool>)(val => { if (val) ShowPbPopup(); }));
            }

            var icon = clone.transform.Find("Icon");
            if (icon != null)
            {
                var img = icon.GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                    img.sprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.feature.qualificationtime.featurequalificationtime_icon.png");
            }
            clone.SetActive(true);
        }

        // recolour the result/timer panel to our menu foreground replacements: the Qualified_Container
        // image goes yellow, its SlicedPanel goes orange. sweeps by name so it doesn't matter where
        // they sit in the hierarchy.
        static void ApplyTimerColors(GameObject root)
        {
            if (root == null) return;
            foreach (var img in root.GetComponentsInChildren<Image>(true))
            {
                if (img.name == "Qualified_Container")
                    img.color = MenuCustomizationApplication.YellowReplacement();
                else if (img.name == "SlicedPanel")
                    img.color = MenuCustomizationApplication.OrangeReplacement();
            }
        }

        // re-hit whatever timer/result UI is currently spawned, so pressing Apply in the UI tab
        // updates a live timer instead of only affecting the next spawn.
        public static void ReapplyTimerColors()
        {
            ApplyTimerColors(_liveTimerGo);
            var result = GameObject.Find("UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates")?.transform.Find("Thisisacustomname");
            if (result != null) ApplyTimerColors(result.gameObject);
        }

        public static void ShowQualificationTime(float elapsed)
        {
            Plugin.Log.LogInfo("QualTime: looking for TimeAttackResultViewModel...");

            // parent under InGameUiManager(Clone)/GameStates so the qual popup + its nav prompts
            // are gated by the same focus state as the rest of gameplay UI (menu open → hidden).
            var gameStates = GameObject.Find("UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates");
            if (gameStates == null)
            {
                Plugin.Log.LogInfo("QualTime: GameStates not found, bailing");
                return;
            }

            if (gameStates.transform.Find("Thisisacustomname") != null)
            {
                Plugin.Log.LogInfo("QualTime: existing result UI detected, skipping");
                return;
            }

            var clone = TakeLiveTimerForQual(gameStates.transform);
            if (clone == null)
            {
                var original = GetQualificationResultPrefab();
                if (original == null)
                {
                    Plugin.Log.LogInfo("QualTime: original is null, bailing");
                    return;
                }

                clone = UnityEngine.Object.Instantiate(original, gameStates.transform);
            }
            clone.transform.name = "Thisisacustomname";

            foreach (var binding in clone.GetComponentsInChildren<ActiveBinding>(true))
                UnityEngine.Object.Destroy(binding);

            var rt = clone.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                if (BetterFGUIMan.Instance != null)
                    BetterFGUIMan.Instance.StartCoroutine(AdjustQualTimerPositionAfterFrame(rt).WrapToIl2Cpp());
                else
                {
                    var parentCanvas = rt.GetComponentInParent<Canvas>();
                    var parentRect = parentCanvas != null ? parentCanvas.GetComponent<RectTransform>() : null;
                    float parentHeight = parentRect != null ? parentRect.rect.height : Screen.height;
                    rt.anchoredPosition = new Vector2(0f, -parentHeight * 0.26f);
                    Plugin.Log.LogInfo("QualTime: positioned at " + rt.anchoredPosition + " (parentHeight=" + parentHeight + ")");
                }
            }

            var popupTimeText = clone.transform.FindChild("Canvas")?.FindChild("LapTimeText")?.GetComponent<TextMeshProUGUI>();
            if (popupTimeText != null)
            {
                var popupTimeRt = popupTimeText.GetComponent<RectTransform>();
                if (popupTimeRt != null)
                {
                    popupTimeRt.anchorMin = new Vector2(0.5f, 0.5f);
                    popupTimeRt.anchorMax = new Vector2(0.5f, 0.5f);
                    popupTimeRt.pivot = new Vector2(1f, 0.5f);
                    popupTimeRt.anchoredPosition = new Vector2(120f, 0f);
                    popupTimeRt.sizeDelta = new Vector2(210f, popupTimeRt.sizeDelta.y);
                }
                popupTimeText.alignment = TextAlignmentOptions.MidlineRight;
                popupTimeText.enableAutoSizing = true;
                popupTimeText.fontSize = 25f;
                popupTimeText.fontSizeMax = 25f;
                popupTimeText.fontSizeMin = 25f;
                popupTimeText.transform.localScale = Vector3.one;
            }

            ApplyTimerColors(clone);

            var vm = clone.GetComponent<FGClient.TimeAttackResultViewModel>();
            if (vm == null)
            {
                Plugin.Log.LogInfo("QualTime: no vm on clone, bailing");
                return;
            }

            TimeSpan t = TimeSpan.FromSeconds(elapsed);
            string formatted = string.Format("{0:D2}:{1:D2}:{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
            Plugin.Log.LogInfo("QualTime: setting time to " + formatted);

            ClientGameManager cgm = null;
            var gsv = FGClient.GlobalGameStateClient.Instance?.GameStateView;
            if (gsv != null)
                gsv.GetLiveClientGameManager(out cgm);
            bool isFinal = cgm?._round?.Archetype?.Id == "archetype_final";
            int pos = isFinal ? 1 : (cgm != null ? cgm._qualifiedPlayerCount + 1 : 434);
            string suffix = pos == 1 ? "st" : pos == 2 ? "nd" : pos == 3 ? "rd" : "th";

            vm.TimeText = formatted;
            vm.PositionText = pos + suffix;
            vm.RaiseAllPropertiesChanged();
            clone.transform.localScale = Vector3.zero;
            clone.SetActive(true);
            BetterFGUIMan.Instance.StartCoroutine(PopInAnimation(clone).WrapToIl2Cpp());

            string cgmRoundId = null;
            string cgmRoundName = null;
            try
            {
                if (cgm?._round != null)
                {
                    cgmRoundId = cgm._round.Id;
                    cgmRoundName = cgm._round.DisplayNameUnindented;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("QualTime: cgm round lookup failed: " + ex.Message); }

            string roundId = _roundIdCache ?? cgmRoundId;
            if (string.IsNullOrEmpty(roundId))
            {
                try { roundId = GlobalGameStateClient.Instance?.GameStateView?.CurrentGameLevelName; }
                catch (Exception ex) { Plugin.Log.LogWarning("QualTime: round lookup failed: " + ex.Message); }
            }
            if (string.IsNullOrEmpty(roundId)) roundId = "unknown";
            roundId = PBStore.CanonicalRoundId(roundId);
            string roundName = _roundNameCache ?? cgmRoundName ?? roundId;
            bool isUnityRound = !roundId.StartsWith("ugc-");
            string roundCacheId = (isUnityRound && !string.IsNullOrEmpty(roundName)) ? roundName : roundId;
            Plugin.Log.LogInfo("QualTime: round=" + roundId + " name=" + roundName + " elapsed=" + elapsed);

            if (IsRaceRound())
            {
                _ghostRecording = false;
                bool usePb = On("store");
                float prevPb = 0f;
                bool isPb = false;
                bool canStorePb = usePb && roundId != "unknown" && !string.IsNullOrEmpty(roundCacheId) && (!isUnityRound || roundName != roundId);
                // "Ask to save PB" means don't touch the store on qualify — just work out whether this
                // run *would* be a new PB so the label/prompts read right, and let the Save prompt in
                // WaitForFeatureInput do the actual TrySet + ghost. off = old behavior, save immediately.
                bool askSave = On("asksave");
                if (canStorePb)
                {
                    bool hadPb = PBStore.TryGet(roundCacheId, out prevPb, out _);
                    if (askSave)
                    {
                        isPb = !hadPb || elapsed < prevPb;
                    }
                    else
                    {
                        isPb = PBStore.TrySet(roundCacheId, roundName, elapsed);
                        if (isPb && On("ghost"))
                            SaveGhost(roundCacheId, PBStore.CurrentType());
                    }
                }
                // override-pb path force-wrote the time + ghost itself; treat this re-spawn as a
                // PB so ShowPbLabel paints "Personal Best!" and the sound fires. prevPb was just
                // read as the new slow time (we overwrote the store) — swap in the real previous.
                if (_forceTreatAsPb) { isPb = true; _forceTreatAsPb = false; prevPb = _forcedPrevPb; _forcedPrevPb = 0f; }
                else if (usePb)
                    Plugin.Log.LogWarning("QualTime: no display name for Unity round, not saving PB as id");
                if (canStorePb && isUnityRound) SplashCache.TryRename(roundId, roundCacheId);
                Plugin.Log.LogInfo("QualTime: isPb=" + isPb);
                if (canStorePb && On("qual"))
                    ShowPbLabel(clone.transform, isPb, roundName, elapsed, prevPb);
                if (canStorePb)
                    BetterFGUIMan.Instance.StartCoroutine(WaitForFeatureInput(clone, roundCacheId, roundName, isPb, elapsed, PBStore.CurrentType()).WrapToIl2Cpp());
            }

            BetterFGUIMan.Instance.StartCoroutine(DismissAfterDelay(clone).WrapToIl2Cpp());

            Plugin.Log.LogInfo("QualTime: done");
        }

        static IEnumerator WaitForFeatureInput(GameObject clone, string roundId, string roundName, bool isPb, float elapsed, PbType type)
        {
            // Favorite prompt (Report glyph) always shows so you can toggle favorite on this PB
            // whether or not the run beat it. non-PB runs additionally get the Set-as-PB prompt
            // (Favourite glyph — different glyph so the two can't collide). both live in a
            // bottom-center HorizontalLayoutGroup so they lay out side by side and auto-center
            // whether there's one prompt or two. favorite label reflects live state.
            var rowGo = new GameObject("BettrFG_QualPromptRow");
            var rowRect = rowGo.AddComponent<RectTransform>();
            rowRect.SetParent(clone.transform, false);
            rowRect.anchorMin = new Vector2(0.5f, 0f);
            rowRect.anchorMax = new Vector2(0.5f, 0f);
            rowRect.pivot = new Vector2(0.5f, 0f);
            rowRect.anchoredPosition = new Vector2(0f, -190f);
            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleCenter;
            hlg.childControlWidth = false;
            hlg.childControlHeight = false;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            // fitter shrinks the row to exactly its children's width; with the pivot at 0.5 that
            // means the whole row is centered on the clone's center instead of growing rightward
            // from it (a zero-width container has nothing to center within).
            var csf = rowGo.AddComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // "Ask to save PB": gate the favorite/set-as-pb prompts behind a Save prompt (same
            // Favourite glyph/input the set-as-pb prompt uses). press it and it vanishes, then the
            // real prompts appear. off = old behavior, prompts show immediately.
            bool gatedBySave = On("asksave");
            if (gatedBySave)
            {
                var savePrompt = NavPromptCore.From(NavPromptCore.Favourite)
                    .WithLabel("Save PB", "bfg_savepb_label")
                    .AnchoredAt(NavPromptAnchor.Custom)
                    .SpawnOn(rowGo.transform);
                while (clone != null)
                {
                    if (savePrompt != null && savePrompt.IsPressed()) break;
                    yield return null;
                }
                savePrompt?.Destroy();
                if (clone == null)
                {
                    if (rowGo != null) UnityEngine.Object.Destroy(rowGo);
                    yield break;
                }
                // this is where the PB actually gets written now — nothing hit the store on qualify.
                // only commit if it really was a new/faster time; a slower run still goes through the
                // Set-as-PB prompt below (isPb stays false, so setPbPrompt shows).
                if (isPb)
                {
                    PBStore.TrySet(roundId, roundName, type, elapsed);
                    if (On("ghost")) SaveGhost(roundId, type);
                }
                // small beat before the real prompts pop in
                yield return new WaitForSeconds(0.5f);
                if (clone == null)
                {
                    if (rowGo != null) UnityEngine.Object.Destroy(rowGo);
                    yield break;
                }
            }

            var favPrompt = SpawnFavPrompt(rowGo.transform, roundId, roundName);

            NavPromptHandle setPbPrompt = null;
            if (!isPb)
            {
                setPbPrompt = NavPromptCore.From(NavPromptCore.Favourite)
                    .WithLabel("Set as PB", "bfg_setaspb_label")
                    .AnchoredAt(NavPromptAnchor.Custom)
                    .SpawnOn(rowGo.transform);
            }
            if (gatedBySave)
                BetterFGUIMan.Instance.StartCoroutine(PopInAnimation(rowGo).WrapToIl2Cpp());
            while (clone != null)
            {
                // if a popup is up (e.g. our own Favorites confirmation), B closes that popup — don't
                // let the same press also re-toggle the favorite, or closing it undoes the last action.
                var popupRoot = GameObject.Find("UICanvas_Client_V2(Clone)/ModalMessage")?.transform;
                bool popupOpen = popupRoot != null && popupRoot.childCount > 0;
                if (!popupOpen && favPrompt != null && favPrompt.IsPressed())
                {
                    bool nowFeatured = PBStore.TryFeature(roundId, roundName);
                    var strings = CMSLoader.Instance._localisedStrings;
                    string titleKey = "bfg_favpb_title";
                    string msgKey = nowFeatured ? "bfg_favpb_added_msg" : "bfg_favpb_removed_msg";
                    if (!strings._localisedStrings.ContainsKey(titleKey))
                        strings._localisedStrings.Add(titleKey, "Favorites");
                    if (!strings._localisedStrings.ContainsKey(msgKey))
                        strings._localisedStrings.Add(msgKey, nowFeatured ? "Added to your favorites!" : "Removed from your favorites.");
                    PopUp.ShowPopup(titleKey, msgKey, FGClient.UI.PopupInteractionType.Info, FGClient.UI.UIModalMessage.ModalType.MT_OK, FGClient.UI.UIModalMessage.OKButtonType.Disruptive);
                    // respawn with the flipped label so the same prompt now offers the opposite
                    // action instead of vanishing — you can favorite then unfavorite and back.
                    favPrompt.Destroy();
                    favPrompt = SpawnFavPrompt(rowGo.transform, roundId, roundName);
                    favPrompt?.GameObject.transform.SetSiblingIndex(0); // keep favorite on the left
                }
                if (setPbPrompt != null && setPbPrompt.IsPressed())
                    ShowOverridePbConfirm(roundId, roundName, elapsed, type);
                yield return null;
            }
            favPrompt?.Destroy();
            setPbPrompt?.Destroy();
            if (rowGo != null) UnityEngine.Object.Destroy(rowGo);
        }

        // Report-glyph prompt whose label follows the live favorite state. re-called after each
        // toggle so the text flips Favorite <-> Unfavorite. the two states use distinct cms keys so
        // NavPromptCore's clone cache keeps a separate clone per label. Custom anchor -> the parent
        // HorizontalLayoutGroup positions it.
        static NavPromptHandle SpawnFavPrompt(Transform parent, string roundId, string roundName)
        {
            bool fav = PBStore.IsFeatured(roundId, roundName);
            return NavPromptCore.From(NavPrompt.LE_Select)
                .WithLabel(fav ? "Unfavorite PB" : "Favorite PB", fav ? "bfg_unfavpb_label" : "bfg_favpb_label")
                .AnchoredAt(NavPromptAnchor.Custom)
                .PollActions(RewiredConsts.Action.LevelEditorReticle_MultiSelect_Paint_Selection)
                .SpawnOn(parent);
        }

        static void ShowOverridePbConfirm(string roundId, string roundName, float elapsed, PbType type)
        {
            string showLabel = ShowLabel(type);
            TimeSpan tNew = TimeSpan.FromSeconds(elapsed);
            string newTime = string.Format("{0:D2}:{1:D2}:{2:D3}", tNew.Minutes, tNew.Seconds, tNew.Milliseconds);
            string oldTime = "--:--:---";
            if (PBStore.TryGet(roundId, type, out float oldPb, out _, roundName))
            {
                TimeSpan tOld = TimeSpan.FromSeconds(oldPb);
                oldTime = string.Format("{0:D2}:{1:D2}:{2:D3}", tOld.Minutes, tOld.Seconds, tOld.Milliseconds);
            }

            var strings = CMSLoader.Instance._localisedStrings;
            string titleKey = "bfg_overridepb_title";
            string msgKey = "bfg_overridepb_msg_" + System.Guid.NewGuid().ToString("N").Substring(0, 8);
            if (!strings._localisedStrings.ContainsKey(titleKey))
                strings._localisedStrings.Add(titleKey, "Set as PB?");
            strings._localisedStrings.Add(msgKey,
                "Make " + newTime + " your new " + showLabel + " PB for " + roundName + "?\n" +
                "Current PB: " + oldTime + "\n" +
                "The saved ghost will be replaced with this run.");

            PopUp.ShowPopup(titleKey, msgKey,
                FGClient.UI.PopupInteractionType.Query,
                FGClient.UI.UIModalMessage.ModalType.MT_OK_CANCEL,
                FGClient.UI.UIModalMessage.OKButtonType.Disruptive,
                (System.Action<bool>)(ok =>
                {
                    if (!ok) return;
                    float prevPb = 0f;
                    PBStore.TryGet(roundId, type, out prevPb, out _, roundName);
                    PBStore.ForceSet(roundId, roundName, type, elapsed);
                    if (On("ghost")) SaveGhost(roundId, type);
                    Plugin.Log.LogInfo($"QualTime: force-set PB {roundName} [{type}] = {newTime}");
                    _forceTreatAsPb = true;
                    // ForceSet just overwrote the stored PB with elapsed, so the re-spawn's
                    // PBStore.TryGet would read the new slow time as "previous". stash the real
                    // previous to show under "Personal Best!" instead.
                    _forcedPrevPb = prevPb;
                    // old qual clone from the original run is still parented to the canvas (5s
                    // dismiss timer hasn't finished); ShowQualificationTime bails if it sees one.
                    var old = GameObject.Find("UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates")?.transform.Find("Thisisacustomname");
                    if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                    ShowQualificationTime(elapsed);
                }));
        }

        static IEnumerator PopInAnimation(GameObject target)
        {
            float duration = 0.4f;
            float t = 0f;
            while (t < duration)
            {
                if (target == null) yield break;
                t += Time.deltaTime;
                float s = popInCurve.Evaluate(t / duration);
                target.transform.localScale = Vector3.one * s;
                yield return null;
            }
            if (target != null)
                target.transform.localScale = Vector3.one;
        }

        static IEnumerator AdjustQualTimerPositionAfterFrame(RectTransform rt)
        {
            yield return null; // next frame
            if (rt == null) yield break;
            var parentCanvas = rt.GetComponentInParent<Canvas>();
            var parentRect = parentCanvas != null ? parentCanvas.GetComponent<RectTransform>() : null;
            float parentHeight = parentRect != null ? parentRect.rect.height : Screen.height;
            rt.anchoredPosition = new Vector2(0f, -parentHeight * 0.26f);
            Plugin.Log.LogInfo("QualTime: adjusted position after frame -> " + rt.anchoredPosition + " (parentHeight=" + parentHeight + ")");
        }

        static IEnumerator DismissAfterDelay(GameObject clone)
        {
            yield return new WaitForSeconds(10f);

            if (clone == null) yield break;

            float duration = 0.4f;
            float elapsed = 0f;
            var originalScale = clone.transform.localScale;

            while (elapsed < duration)
            {
                if (clone == null) yield break;
                elapsed += Time.deltaTime;
                float s = dismissCurve.Evaluate(elapsed / duration);
                clone.transform.localScale = originalScale * s;
                yield return null;
            }

            if (clone != null)
                UnityEngine.Object.Destroy(clone);
        }

        public static void ShowPbPopup()
        {
            GameObject.Find("MainMenuManager").GetComponent<MainMenuManager>().NavigateToView(MainMenuViews.Customiser);
            PBPopup.Show();
        }

        static void ShowPbLabel(Transform cloneRoot, bool isPb, string levelName, float elapsed, float prevPb)
        {
            var tmps = cloneRoot.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);

            var labelGo = UnityEngine.Object.Instantiate(tmps[0].gameObject, cloneRoot);
            foreach (var b in labelGo.GetComponents<Mediatonic.Tools.MVVM.TMPTextBinding>())
                UnityEngine.Object.Destroy(b);

            var label = labelGo.GetComponent<TMPro.TextMeshProUGUI>();
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0.5f, 0.5f);
            labelRt.anchorMax = new Vector2(0.5f, 0.5f);
            labelRt.pivot = new Vector2(0.5f, 0.5f);
            labelRt.anchoredPosition = new Vector2(0f, -90f);
            labelRt.sizeDelta = new Vector2(600f, 60f);

            string pbText;
            Color pbColor;
            if (isPb)
            {
                pbText = "Personal Best!";
                pbColor = new Color(1f, 1f, 0.3f);
            }
            else
            {
                TimeSpan pbSpan = TimeSpan.FromSeconds(prevPb);
                pbText = string.Format("PB  {0:D2}:{1:D2}:{2:D3}", pbSpan.Minutes, pbSpan.Seconds, pbSpan.Milliseconds);
                pbColor = Color.white;
            }

            label.text = pbText;
            label.color = pbColor;
            label.alignment = TMPro.TextAlignmentOptions.Center;
            label.ForceMeshUpdate();
            labelGo.SetActive(true);
            Plugin.Log.LogInfo("QualTime: PB label -> " + pbText);

            if (isPb && prevPb > 0f)
            {
                var subGo = UnityEngine.Object.Instantiate(tmps[0].gameObject, cloneRoot);
                foreach (var b in subGo.GetComponents<Mediatonic.Tools.MVVM.TMPTextBinding>())
                    UnityEngine.Object.Destroy(b);

                var sub = subGo.GetComponent<TMPro.TextMeshProUGUI>();
                var subRt = subGo.GetComponent<RectTransform>();
                subRt.anchorMin = new Vector2(0.5f, 0.5f);
                subRt.anchorMax = new Vector2(0.5f, 0.5f);
                subRt.pivot = new Vector2(0.5f, 0.5f);
                subRt.anchoredPosition = new Vector2(0f, -55f);
                subRt.sizeDelta = new Vector2(600f, 40f);

                TimeSpan prevSpan = TimeSpan.FromSeconds(prevPb);
                sub.text = string.Format("Previous {0:D2}:{1:D2}:{2:D3}", prevSpan.Minutes, prevSpan.Seconds, prevSpan.Milliseconds);
                sub.color = Color.white;
                sub.transform.localScale *= 0.65f;
                sub.alignment = TMPro.TextAlignmentOptions.Center;
                sub.ForceMeshUpdate();
                subGo.SetActive(true);
                Plugin.Log.LogInfo("QualTime: prev PB label -> " + sub.text);
            }

            if (isPb)
            {
                AudioService.PlayPB();

                /*
                var hintGo = UnityEngine.Object.Instantiate(tmps[0].gameObject, cloneRoot);
                foreach (var b in hintGo.GetComponents<Mediatonic.Tools.MVVM.TMPTextBinding>())
                    UnityEngine.Object.Destroy(b);

                var hint = hintGo.GetComponent<TMPro.TextMeshProUGUI>();
                var hintRt = hintGo.GetComponent<RectTransform>();
                hintRt.anchorMin = new Vector2(0.5f, 0.5f);
                hintRt.anchorMax = new Vector2(0.5f, 0.5f);
                hintRt.pivot = new Vector2(0.5f, 0.5f);
                hintRt.anchoredPosition = new Vector2(0f, -145f);
                hintRt.sizeDelta = new Vector2(700f, 40f);

                hint.text = "Press B to favorite this personal best!";
                hint.color = new Color(0.3f, 1f, 0.3f);
                hint.transform.localScale *= 0.6f;
                hint.alignment = TMPro.TextAlignmentOptions.Center;
                hint.ForceMeshUpdate();
                hintGo.SetActive(true);

                BetterFGUIMan.Instance.StartCoroutine(PulseHintLabel(hintGo).WrapToIl2Cpp());
                */
            }
        }

        static IEnumerator PulseHintLabel(GameObject hintGo)
        {
            float t = 0f;
            float speed = 1.8f;
            float minScale = 0.95f;
            float maxScale = 1.05f;
            var baseScale = hintGo.transform.localScale;

            while (hintGo != null)
            {
                t += Time.deltaTime * speed;
                float s = Mathf.Lerp(minScale, maxScale, (Mathf.Sin(t * Mathf.PI * 2f) + 1f) * 0.5f);
                hintGo.transform.localScale = baseScale * s;
                yield return null;
            }
        }

        // ── Live in-game timer ────────────────────────────────────────────────

        static GameObject _liveTimerGo;
        static FGClient.TimeAttackResultViewModel _liveTimerVm;
        // SpawnLiveTimer destroys the old timer and assigns the new one in the same frame, so the
        // previous round's 50Hz ticker wakes to a non-null _liveTimerGo (the NEW one) and never
        // exits — one extra ticker per race round, each spamming RaiseAllPropertiesChanged. the
        // ticker captures the generation it was spawned for and bails when a newer spawn takes over.
        static int _liveTimerGen;

        static bool? _isRaceRoundCache = null;
        static string _roundIdCache = null;
        static string _roundNameCache = null;
        static bool _forceTreatAsPb;
        static float _forcedPrevPb;

        static void ResetRaceRoundCache() => _isRaceRoundCache = null;

        static bool HasRaceArchetype()
        {
            try
            {
                ClientGameManager cgm;
                var gsv = GlobalGameStateClient.Instance?.GameStateView;
                if (gsv != null && gsv.GetLiveClientGameManager(out cgm))
                {
                    string archId = cgm?._round?.Archetype?.Id;
                    return archId == "archetype_race";
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("QualTime: archetype lookup failed: " + ex.Message); }
            return false;
        }

        static bool IsRaceRound()

        {

            if (_isRaceRoundCache.HasValue) return _isRaceRoundCache.Value;

            // GameRules is the source of truth now - archetype is obsolete on finals.
            // IsRaceRound is also true on hunt rounds (scoring/bubble/score-target), which we
            // count as races too, so this single check covers everything below.
            try
            {
                ClientGameManager cgm;
                var gsv = GlobalGameStateClient.Instance?.GameStateView;
                if (gsv != null && gsv.GetLiveClientGameManager(out cgm) && cgm?.GameRules != null)
                    return (_isRaceRoundCache = cgm.GameRules.IsRaceRound).Value;
            }
            catch (Exception ex) { Plugin.Log.LogWarning("QualTime: GameRules lookup failed: " + ex.Message); }

            return (_isRaceRoundCache = false).Value;

            /* old component-based detection - kept for reference
            if (HasRaceArchetype())
                return (_isRaceRoundCache = true).Value;

            var endZones = Resources.FindObjectsOfTypeAll<COMMON_ObjectiveReachEndZone>();

            foreach (var c in endZones)

                if (c != null && c.gameObject != null && c.gameObject.activeInHierarchy && c.gameObject.hideFlags == HideFlags.None)

                    return (_isRaceRoundCache = true).Value;

            var grabs = Resources.FindObjectsOfTypeAll<COMMON_GrabToQualify>();

            foreach (var c in grabs)

                if (c != null && c.gameObject != null && c.gameObject.activeInHierarchy && c.gameObject.hideFlags == HideFlags.None)

                    return (_isRaceRoundCache = true).Value;

            var bubble = Resources.FindObjectsOfTypeAll<COMMON_ScoringBubble>();

            foreach (var c in bubble)

                if (c != null && c.gameObject != null && c.gameObject.activeInHierarchy && c.gameObject.hideFlags == HideFlags.None)

                    return (_isRaceRoundCache = true).Value;

            var bubble2 = Resources.FindObjectsOfTypeAll<BubbleZone>();

            foreach (var c in bubble2)

                if (c != null && c.gameObject != null && c.gameObject.activeInHierarchy && c.gameObject.hideFlags == HideFlags.None)

                    return (_isRaceRoundCache = true).Value;

            var hoops = Resources.FindObjectsOfTypeAll<COMMON_Hoop>();

            foreach (var c in hoops)

                if (c != null && c.gameObject != null && c.gameObject.activeInHierarchy && c.gameObject.hideFlags == HideFlags.None)

                    return (_isRaceRoundCache = true).Value;

            var singleHoops = Resources.FindObjectsOfTypeAll<Levels.HoopHoopRevenge.COMMON_SingleScoreHoop>();

            foreach (var c in singleHoops)

                if (c != null && c.gameObject != null && c.gameObject.activeInHierarchy && c.gameObject.hideFlags == HideFlags.None)

                    return (_isRaceRoundCache = true).Value;

            var destructibles = Resources.FindObjectsOfTypeAll<LevelEditorDestructibleObjectParameter>();

            foreach (var c in destructibles)

                if (c != null && c.gameObject != null && c.gameObject.activeInHierarchy && c.gameObject.hideFlags == HideFlags.None && c._selectedPointsAwarded >= 1)

                    return (_isRaceRoundCache = true).Value;

            var triggerZones = Resources.FindObjectsOfTypeAll<LevelEditorTriggerZoneActiveBase>();

            foreach (var c in triggerZones)

                if (c != null && c.gameObject != null && c.gameObject.activeInHierarchy && c.gameObject.hideFlags == HideFlags.None && c._pointsScored >= 1)

                    return (_isRaceRoundCache = true).Value;

            return (_isRaceRoundCache = false).Value;
            */

        }

        static IEnumerator SpawnLiveTimerDeferred()
        {
            if (!On("timer"))
            {
                DestroyLiveTimer();
                yield break;
            }
            // one frame so cgm.GameRules is populated after CleanupLoadingScreens lands.
            yield return null;
            if (IsRaceRound()) SpawnLiveTimer();
        }

        static void SpawnLiveTimer()
        {
            if (!On("timer"))
            {
                DestroyLiveTimer();
                return;
            }

            DestroyLiveTimer();

            var original = GetQualificationResultPrefab();
            if (original == null)
            {
                Plugin.Log.LogInfo("QualTime: live timer original is null, bailing");
                return;
            }

            var canvas = GameObject.Find("UICanvas_Client_V2(Clone)");
            var playingState = canvas?.transform.Find("Default/InGameUiManager(Clone)/GameStates/PlayingState")?.gameObject;
            if (playingState == null)
            {
                Plugin.Log.LogInfo("QualTime: PlayingState not found, can't spawn live timer");
                return;
            }

            var clone = UnityEngine.Object.Instantiate(original, playingState.transform);

            foreach (var binding in clone.GetComponentsInChildren<ActiveBinding>(true))
                UnityEngine.Object.Destroy(binding);

            var vm = clone.GetComponent<FGClient.TimeAttackResultViewModel>();
            if (vm == null)
            {
                Plugin.Log.LogInfo("QualTime: live timer no vm on clone, bailing");
                UnityEngine.Object.Destroy(clone);
                return;
            }

            var rt = clone.GetComponent<RectTransform>();
            // anchor to the top-right corner so it sticks there across resolutions/aspect ratios,
            // instead of a fixed pixel offset from the parent pivot
            rt.anchorMin = new Vector2(1f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(70f, -250f);

            var timetexttmp = rt.transform.FindChild("Canvas").FindChild("LapTimeText").GetComponent<TextMeshProUGUI>();
            timetexttmp.alignment = TextAlignmentOptions.MidlineLeft;
            timetexttmp.enableAutoSizing = false;
            timetexttmp.transform.localPosition = new Vector3(0, 0, 0);

            ApplyTimerColors(clone);

            vm.PositionText = "";
            vm.TimeText = "00:00:000";
            vm.RaiseAllPropertiesChanged();
            clone.SetActive(true);

            // --- PB LABEL UNDER TIMER ---
            bool localSucceededLive = FGClient.GlobalGameStateClient.Instance._clientPlayerManager?.LocalPlayerSucceeded ?? false;

            var tmps = clone.GetComponentsInChildren<TextMeshProUGUI>(true);
            string roundId2 = _roundIdCache;
            string roundName2 = _roundNameCache;
            try
            {
                ClientGameManager pbCgm;
                var pbGsv = GlobalGameStateClient.Instance?.GameStateView;
                if (pbGsv != null && pbGsv.GetLiveClientGameManager(out pbCgm) && pbCgm?._round != null)
                {
                    if (string.IsNullOrEmpty(roundId2)) roundId2 = pbCgm._round.Id;
                    if (string.IsNullOrEmpty(roundName2)) roundName2 = pbCgm._round.DisplayNameUnindented;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("QualTime: live pb cgm round lookup failed: " + ex.Message); }
            if (string.IsNullOrEmpty(roundId2))
            {
                try { roundId2 = GlobalGameStateClient.Instance?.GameStateView?.CurrentGameLevelName; }
                catch (Exception ex) { Plugin.Log.LogWarning("QualTime: live pb round lookup failed: " + ex.Message); }
            }
            if (string.IsNullOrEmpty(roundName2)) roundName2 = roundId2;
            if (tmps.Length > 0 && !localSucceededLive && IsRaceRound() && On("play"))
            {
                var pbGo = UnityEngine.Object.Instantiate(tmps[0].gameObject, clone.transform);
                pbGo.name = "QualTimeLivePbLabel";

                foreach (var b in pbGo.GetComponents<Mediatonic.Tools.MVVM.TMPTextBinding>())
                    UnityEngine.Object.Destroy(b);

                var pbText = pbGo.GetComponent<TextMeshProUGUI>();
                var pbRt = pbGo.GetComponent<RectTransform>();

                pbRt.anchorMin = new Vector2(0.5f, 0.5f);
                pbRt.anchorMax = new Vector2(0.5f, 0.5f);
                pbRt.pivot = new Vector2(0.5f, 0.5f);
                pbRt.anchoredPosition = new Vector2(0f, -60f);
                pbRt.sizeDelta = new Vector2(400f, 40f);

                float pb = 0f;
                bool pbFound = !string.IsNullOrEmpty(roundId2) && PBStore.TryGet(roundId2, out pb, out _, roundName2);
                if (!pbFound && !string.IsNullOrEmpty(roundName2)) pbFound = PBStore.TryGet(roundName2, out pb, out _, null);
                if (pbFound)
                {
                    TimeSpan pbSpan = TimeSpan.FromSeconds(pb);
                    pbText.text = string.Format("PB {0:D2}:{1:D2}:{2:D3}", pbSpan.Minutes, pbSpan.Seconds, pbSpan.Milliseconds);
                }
                else
                {
                    pbText.text = "PB --:--:---";
                }

                pbText.color = new Color(1f, 1f, 1f, 0.8f);
                pbText.alignment = TextAlignmentOptions.MidlineRight;
                pbText.rectTransform.anchoredPosition = new Vector3(-120f, -55f, 0);
                pbText.transform.localScale = Vector3.one * 0.5f;
                pbText.ForceMeshUpdate();
                pbGo.SetActive(true);
            }

            _liveTimerGo = clone;
            _liveTimerVm = vm;
            _liveTimerGen++;

            BetterFGUIMan.Instance.StartCoroutine(LiveTimerTickCoroutine().WrapToIl2Cpp());
            Plugin.Log.LogInfo("QualTime: live timer spawned");
        }

        static GameObject GetQualificationResultPrefab()
        {
            var original = GetTimeAttackResultTemplate();
            if (original == null) return null;

            return AssetManager.RuntimePrefab("qualification_time_result", original.gameObject, go =>
            {
                foreach (var binding in go.GetComponentsInChildren<ActiveBinding>(true))
                    UnityEngine.Object.DestroyImmediate(binding);
            });
        }

        static GameObject TakeLiveTimerForQual(Transform canvas)
        {
            if (_liveTimerGo == null) return null;

            var go = _liveTimerGo;
            _liveTimerGo = null;
            _liveTimerVm = null;

            var pbLabel = go.transform.Find("QualTimeLivePbLabel");
            if (pbLabel != null) UnityEngine.Object.DestroyImmediate(pbLabel.gameObject);

            go.transform.SetParent(canvas, false);
            go.SetActive(false);
            return go;
        }

        static FGClient.TimeAttackResultViewModel GetTimeAttackResultTemplate()
        {
            var all = Resources.FindObjectsOfTypeAll<FGClient.TimeAttackResultViewModel>();
            foreach (var vm in all)
            {
                if (vm == null || vm.gameObject == null) continue;
                if (vm.transform.root.name.Contains("BetterFG")) continue;
                if (_liveTimerGo != null && vm.gameObject == _liveTimerGo) continue;
                if (vm.gameObject.name == "Thisisacustomname") continue;
                if (vm.gameObject.scene.name == "DontDestroyOnLoad") continue;
                return vm;
            }

            return null;
        }

        static void DestroyLiveTimer()
        {
            if (_liveTimerGo != null)
            {
                UnityEngine.Object.Destroy(_liveTimerGo);
                _liveTimerGo = null;
                _liveTimerVm = null;
            }
        }

        static IEnumerator LiveTimerTickCoroutine()
        {
            int gen = _liveTimerGen;
            var wait = new WaitForSeconds(1f / 50f);
            string last = null;
            while (gen == _liveTimerGen && _liveTimerGo != null)
            {
                //using (BetterFG.Utilities.PerfProbe.Time("qual.LiveTimerTick"))
                if (_liveTimerVm != null && GlobalGameStateClient.Instance?.GameStateView != null)
                {
                    float elapsed = GlobalGameStateClient.Instance.GameStateView.GameplayTimeElapsed;
                    TimeSpan t = TimeSpan.FromSeconds(elapsed);
                    string formatted = string.Format("{0:D2}:{1:D2}:{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
                    if (formatted != last)
                    {
                        _liveTimerVm.TimeText = formatted;
                        _liveTimerVm.RaiseAllPropertiesChanged();
                        last = formatted;
                    }
                }
                yield return wait;
            }
        }

        // ── Ghost run ─────────────────────────────────────────────────────────

        // negative magic distinguishes new format (with anim) from old (frame count first)
        const int GhostMagic = unchecked((int)0xBF670002);

        internal static List<(float t, Vector3 pos, Quaternion rot, int stateHash, float animTime)> _ghostFrames;
        static bool _ghostRecording;
        static int _ghostGen;
        static Material _ghostMat;
        // ghosts currently in the round. usually one, but "All" mode spawns up to three.
        static readonly List<GameObject> _ghostGos = new List<GameObject>();

        // ghost mode: which show's ghost(s) to play. a FeatureChoice on the feature, so it
        // auto-renders as a dropdown in the features tab and stores under "feature.pb.ghostmode".
        internal static string GhostMode => feature.GetChoice("ghostmode");

        // which shows to spawn ghosts for, given the mode and what's available for this round.
        static List<PbType> GhostTypesToSpawn(string cacheId)
        {
            var result = new List<PbType>();
            string mode = GhostMode;
            if (mode == "current") { var t = PBStore.CurrentType(); if (GhostExistsFor(cacheId, t)) result.Add(t); }
            else if (mode == "solos") { if (GhostExistsFor(cacheId, PbType.Solos)) result.Add(PbType.Solos); }
            else if (mode == "duos") { if (GhostExistsFor(cacheId, PbType.Duos)) result.Add(PbType.Duos); }
            else if (mode == "squads") { if (GhostExistsFor(cacheId, PbType.Squads)) result.Add(PbType.Squads); }
            else if (mode == "all")
            {
                foreach (var t in new[] { PbType.Solos, PbType.Duos, PbType.Squads })
                    if (GhostExistsFor(cacheId, t)) result.Add(t);
            }
            else // "fastest": whichever show has the fastest stored PB and a ghost on disk
            {
                PbType? best = null;
                float bestTime = float.MaxValue;
                foreach (var t in new[] { PbType.Solos, PbType.Duos, PbType.Squads })
                {
                    if (!GhostExistsFor(cacheId, t)) continue;
                    if (PBStore.TryGet(cacheId, t, out float time, out _) && time < bestTime)
                    {
                        bestTime = time;
                        best = t;
                    }
                    else if (!best.HasValue)
                        best = t; // no stored time but a ghost exists - still a candidate
                }
                if (best.HasValue) result.Add(best.Value);
            }
            return result;
        }

        static bool GhostExistsFor(string cacheId, PbType t)
        {
            try
            {
                if (File.Exists(GhostPath(cacheId, t))) return true;
                if (t == PbType.Solos && File.Exists(LegacyGhostPath(cacheId))) return true;
            }
            catch { }
            return false;
        }

        static string ShowLabel(PbType t) => t == PbType.Solos ? "Solos" : t == PbType.Duos ? "Duos" : "Squads";

        static string GetCurrentRoundCacheId()
        {
            string rid = PBStore.CanonicalRoundId(_roundIdCache ?? "");
            if (string.IsNullOrEmpty(rid)) return null;
            bool isUgc = rid.StartsWith("ugc-");
            return (!isUgc && !string.IsNullOrEmpty(_roundNameCache)) ? _roundNameCache : rid;
        }

        static string GhostDir =>
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "BettrFG", "Settings", "ghosts");

        static string Suffix(PbType t) => t == PbType.Solos ? "__solos" : t == PbType.Duos ? "__duos" : "__squads";

        // per-show ghost file. legacy ghosts have no suffix and are treated as solos.
        static string GhostPath(string cacheId, PbType t) =>
            Path.Combine(GhostDir, string.Concat(cacheId.Split(Path.GetInvalidFileNameChars())) + Suffix(t) + ".ghost");

        static string LegacyGhostPath(string cacheId) =>
            Path.Combine(GhostDir, string.Concat(cacheId.Split(Path.GetInvalidFileNameChars())) + ".ghost");

        static Material GetGhostMat()
        {
            if (_ghostMat != null) return _ghostMat;
            var go = AssetManager.SpawnPersistent("bettrfg_mat_ghost");
            if (go == null) return null;
            var mr = go.GetComponent<MeshRenderer>() ?? go.GetComponentInChildren<MeshRenderer>();
            if (mr != null) _ghostMat = mr.sharedMaterial;
            UnityEngine.Object.Destroy(go);
            return _ghostMat;
        }

        static Animator FindBeanAnimator(GameObject bean)
        {
            var wrapper = bean.transform.Find("BetterFG_ScaleWrapper");
            var charT = (wrapper != null ? wrapper : bean.transform).Find("Character");
            return charT?.GetComponent<Animator>();
        }

        static void SaveGhost(string cacheId, PbType type)
        {
            if (_ghostFrames == null || _ghostFrames.Count == 0) return;
            try
            {
                Directory.CreateDirectory(GhostDir);
                using (var bw = new BinaryWriter(File.Create(GhostPath(cacheId, type))))
                {
                    bw.Write(GhostMagic);
                    bw.Write(_ghostFrames.Count);
                    foreach (var (t, p, r, sh, at) in _ghostFrames)
                    {
                        bw.Write(t);
                        bw.Write(p.x); bw.Write(p.y); bw.Write(p.z);
                        bw.Write(r.x); bw.Write(r.y); bw.Write(r.z); bw.Write(r.w);
                        bw.Write(sh);
                        bw.Write(at);
                    }
                }
                Plugin.Log.LogInfo($"Ghost: saved {_ghostFrames.Count} frames for {cacheId}");
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Ghost: save failed: " + ex.Message); }
        }

        // deleting a PB should take its ghost run with it. the ghost is named after roundCacheId,
        // which is the round's display NAME for unity rounds and the ugc- id for ugc rounds. from
        // the delete button we don't know which it was, so just try both candidate ids - whichever
        // file exists gets nuked, the other GhostPath simply won't exist.
        // all candidate ghost paths for a cacheId: the three per-show files plus the legacy unsuffixed one.
        static IEnumerable<string> AllGhostPaths(string cacheId)
        {
            yield return GhostPath(cacheId, PbType.Solos);
            yield return GhostPath(cacheId, PbType.Duos);
            yield return GhostPath(cacheId, PbType.Squads);
            yield return LegacyGhostPath(cacheId);
        }

        internal static void DeleteGhost(params string[] cacheIds)
        {
            foreach (var cacheId in cacheIds)
            {
                if (string.IsNullOrEmpty(cacheId)) continue;
                foreach (var path in AllGhostPaths(cacheId))
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                            Plugin.Log.LogInfo($"Ghost: deleted ghost {path}");
                        }
                    }
                    catch (Exception ex) { Plugin.Log.LogWarning("Ghost: delete failed: " + ex.Message); }
                }
            }
        }

        // does this PB have any saved ghost (any show, or legacy)?
        internal static bool HasGhost(params string[] cacheIds)
        {
            foreach (var cacheId in cacheIds)
            {
                if (string.IsNullOrEmpty(cacheId)) continue;
                foreach (var path in AllGhostPaths(cacheId))
                    try { if (File.Exists(path)) return true; }
                    catch { }
            }
            return false;
        }

        // loads the ghost for a specific show. solos falls back to the legacy unsuffixed file so old
        // single-ghost recordings still play as the solos ghost.
        static List<(float, Vector3, Quaternion, int, float)> LoadGhost(string cacheId, PbType type)
        {
            string path = GhostPath(cacheId, type);
            if (!File.Exists(path) && type == PbType.Solos) path = LegacyGhostPath(cacheId);
            if (!File.Exists(path)) return null;
            try
            {
                using (var br = new BinaryReader(File.OpenRead(path)))
                {
                    int magic = br.ReadInt32();
                    if (magic != GhostMagic) { Plugin.Log.LogWarning("Ghost: old format, re-run to record animations"); return null; }
                    int n = br.ReadInt32();
                    var frames = new List<(float, Vector3, Quaternion, int, float)>(n);
                    for (int i = 0; i < n; i++)
                    {
                        float t = br.ReadSingle();
                        var p = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        var r = new Quaternion(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                        int sh = br.ReadInt32();
                        float at = br.ReadSingle();
                        frames.Add((t, p, r, sh, at));
                    }
                    return frames;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Ghost: load failed: " + ex.Message); return null; }
        }

        static IEnumerator GhostRecordCoroutine(int gen)
        {
            var wait = new WaitForSeconds(1f / 20f);
            FallGuysCharacterController local = null;
            Animator localAnim = null;
            while (_ghostRecording && _ghostGen == gen)
            {
                if (local == null)
                {
                    // cheap cached getter — NOT a FindObjectsOfTypeAll heap scan. when spectating there's
                    // no local player so this stays null all round; scanning the whole heap 20x/sec here
                    // was freezing the game every tick while spectating.
                    local = FallGuysLib.Players.PlayerUtils.PlayerController;
                    if (local != null && !local.IsLocalPlayer) local = null;
                    if (local != null)
                        localAnim = FindBeanAnimator(local.gameObject);
                }
                if (local != null)
                {
                    float t = GlobalGameStateClient.Instance?.GameStateView?.GameplayTimeElapsed ?? 0f;
                    int sh = 0; float at = 0f;
                    if (localAnim != null)
                    {
                        var info = localAnim.GetCurrentAnimatorStateInfo(0);
                        sh = info.shortNameHash;
                        at = info.normalizedTime;
                    }
                    _ghostFrames.Add((t, local.transform.position, local.transform.rotation, sh, at));
                }
                yield return wait;
            }
        }

        static IEnumerator SpawnGhostDeferred(int gen)
        {
            yield return new WaitForSeconds(2f);
            if (_ghostGen != gen) yield break;

            // _roundIdCache is cleared by the time this runs; read from game state directly
            string cacheId = null;
            try
            {
                ClientGameManager cgm;
                var gsv = GlobalGameStateClient.Instance?.GameStateView;
                if (gsv != null && gsv.GetLiveClientGameManager(out cgm) && cgm?._round != null)
                {
                    string rid = PBStore.CanonicalRoundId(cgm._round.Id);
                    string rname = cgm._round.DisplayNameUnindented;
                    bool isUgc = rid?.StartsWith("ugc-") ?? false;
                    cacheId = (!isUgc && !string.IsNullOrEmpty(rname)) ? rname : rid;
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Ghost: round lookup failed: " + ex.Message); }

            if (string.IsNullOrEmpty(cacheId)) { Plugin.Log.LogInfo("Ghost: no round id, skipping"); yield break; }

            var types = GhostTypesToSpawn(cacheId);
            if (types.Count == 0) { Plugin.Log.LogInfo("Ghost: nothing to spawn for mode " + GhostMode); yield break; }

            string playerName = string.IsNullOrEmpty(LocalPlayerInfo.DisplayName) ? "Ghost" : LocalPlayerInfo.DisplayName;

            // only tag the ghost with its show name when more than one show could be on screen, or
            // when the user explicitly picked a single show. for plain "fastest" with one ghost we
            // still show the show so you know which run it is. (Name) (PB - Solos/Duos/Squads)
            foreach (var type in types)
            {
                if (_ghostGen != gen) yield break;

                var frames = LoadGhost(cacheId, type);
                Plugin.Log.LogInfo($"Ghost: loaded {frames?.Count ?? -1} frames for {cacheId} [{type}]");
                if (frames == null || frames.Count == 0) continue;

                string ghostName = playerName + " (PB - " + ShowLabel(type) + ")";
                var ghost = SpawnBeanUtils.SpawnBean(ghostName, new NPCCustomization("", "", null, null, -1));
                Plugin.Log.LogInfo($"Ghost: SpawnBean result={ghost != null} [{type}]");
                if (ghost == null) continue;

                var ghostGo = ghost.gameObject;

                if (ghost._rigidbody != null)
                {
                    ghost._rigidbody.isKinematic = true;
                    ghost._rigidbody.useGravity = false;
                }
                if (ghost._ragdollController != null)
                    ghost._ragdollController._upperBodyEnabled = false;
                foreach (var col in ghostGo.GetComponentsInChildren<Collider>(true))
                    UnityEngine.Object.Destroy(col);

                var ghostAnim = FindBeanAnimator(ghostGo);
                UnityEngine.Object.Destroy(ghost);

                BetterFGUIMan.Instance.StartCoroutine(ApplyGhostSkinThenMatCoroutine(ghostGo, gen).WrapToIl2Cpp());
                RegisterGhostNametag(ghostName);
                _ghostGos.Add(ghostGo);
                // snapshot the ghost's PB NOW — if the local player beats it this run, PBStore gets
                // overwritten with the faster new time before the ghost finishes, and reading it at
                // fallfeed time would stamp the ghost with our new PB instead of its own.
                float ghostPb = PBStore.TryGet(cacheId, type, out float _pb, out _) ? _pb : frames[frames.Count - 1].Item1;
                BetterFGUIMan.Instance.StartCoroutine(GhostPlayback(ghostGo, ghostAnim, frames, ghostName, ghostPb).WrapToIl2Cpp());
                Plugin.Log.LogInfo($"Ghost: spawned for {cacheId} [{type}]");
            }
        }

        static void RegisterGhostNametag(string ghostName)
        {
            if (SettingsService.Get("nametag.enabled", "false") != "true") return;

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float F(string k, float d) => float.TryParse(SettingsService.Get(k, ""), System.Globalization.NumberStyles.Float, ci, out float v) ? v : d;
            var profile = new BetterFG.Network.PlayerRemoteProfile
            {
                nametag = new BetterFG.Network.RemoteNametagInfo
                {
                    r = F("nametag.color.r", 1f), g = F("nametag.color.g", 1f), b = F("nametag.color.b", 1f),
                    bold = SettingsService.Get("nametag.bold", "false") == "true",
                    italic = SettingsService.Get("nametag.italic", "false") == "true",
                    nameStyle = SettingsService.Get("nametag.namestyle", "default"),
                    iconMode = SettingsService.Get("nametag.icon.mode", "none"),
                    iconCountry = SettingsService.Get("nametag.icon.country", ""),
                    iconPath = SettingsService.Get("nametag.icon.path", ""),
                    iconScale = F("nametag.icon.scale", 1f),
                    iconOffX = F("nametag.icon.offset.x", 0f),
                    iconOffY = F("nametag.icon.offset.y", 0f),
                },
            };
            BetterFG.Network.RemoteProfileStore.Register(profile, ghostName);
        }

        static IEnumerator ApplyGhostSkinThenMatCoroutine(GameObject ghostGo, int gen)
        {
            var svc = SkinApplicationService.Instance;
            bool replacesBean = false;
            if (svc != null)
            {
                // use the same slot object references GetActiveSlots returns — they're in activeSlots,
                // so SlotDead's Contains check passes and applyStamp matches
                var slots = svc.GetActiveSlots();
                foreach (var slot in slots)
                {
                    if (_ghostGen != gen || ghostGo == null) yield break;
                    if (slot?.bundle == null) continue;
                    // ONLY a full-bean Costume replaces the fall guy. items + accessories are
                    // attachments that sit ON the bean, and a costume with keepBase keeps it too.
                    // treating an item as bean-replacing was nuking the whole body in the ghost,
                    // leaving just the item floating. respect type AND keepBase.
                    if (slot.skinInfo != null && slot.type == SkinType.Costume && !slot.skinInfo.keepBase)
                        replacesBean = true;
                    yield return svc.ApplySkinToBean(slot, ghostGo).WrapToIl2Cpp();
                }
                if (!replacesBean)
                    yield return svc.ApplyActiveGameCosmeticsToBeanCoroutine(ghostGo).WrapToIl2Cpp();
            }
            if (_ghostGen != gen || ghostGo == null) yield break;
            // skin coroutines may have applied their own scale — override with your exact resolved size
            // via the same wrapper mechanism you use, so the ghost matches you.
            PlayerScaleService.ApplyGhostScale(ghostGo);

            if (replacesBean)
            {
                foreach (var p in ghostGo.GetComponentsInChildren<CostumePollerComponent>(true))
                    UnityEngine.Object.Destroy(p);
                foreach (var t in ghostGo.GetComponentsInChildren<Transform>(true))
                    if (t != null && t.name.StartsWith("Body_LOD")) t.gameObject.SetActive(false);
            }

            var mat = GetGhostMat();
            if (mat == null) yield break;
            foreach (var smr in ghostGo.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mats = smr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                smr.sharedMaterials = mats;
            }
            foreach (var mr in ghostGo.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mats = mr.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                mr.sharedMaterials = mats;
            }
        }

        static IEnumerator GhostPlayback(GameObject ghostGo, Animator ghostAnim, List<(float t, Vector3 pos, Quaternion rot, int stateHash, float animTime)> frames, string ghostName, float ghostPb)
        {
            int idx = 0;
            bool finished = false;
            int lastState = 0;
            while (ghostGo != null && _ghostGos.Contains(ghostGo) && idx < frames.Count)
            {
                float elapsed = GlobalGameStateClient.Instance?.GameStateView != null
                    ? GlobalGameStateClient.Instance.GameStateView.GameplayTimeElapsed
                    : 0f;
                while (idx + 1 < frames.Count && frames[idx + 1].t <= elapsed)
                    idx++;
                if (idx + 1 < frames.Count)
                {
                    float den = frames[idx + 1].t - frames[idx].t;
                    float frac = den > 0f ? Mathf.Clamp01((elapsed - frames[idx].t) / den) : 0f;
                    ghostGo.transform.position = Vector3.Lerp(frames[idx].pos, frames[idx + 1].pos, frac);
                    ghostGo.transform.rotation = Quaternion.Slerp(frames[idx].rot, frames[idx + 1].rot, frac);
                }
                else
                {
                    ghostGo.transform.position = frames[idx].pos;
                    ghostGo.transform.rotation = frames[idx].rot;
                    // hit the last frame with live elapsed already past it — ghost has "qualified".
                    if (elapsed >= frames[idx].t) { finished = true; break; }
                }
                if (ghostAnim != null)
                {
                    // only when the RECORDING changes state. comparing against the animator's actual state
                    // instead meant every transition it made on its own counted as a mismatch, so we yanked
                    // it back and restarted the clip every few frames through a slide
                    if (frames[idx].stateHash != lastState)
                    {
                        lastState = frames[idx].stateHash;
                        ghostAnim.Play(lastState, 0, 0f);
                    }

                    float r = FallGuysCharacterController.GroundCheckSphereCastRadius;
                    bool grounded = Physics.SphereCast(ghostGo.transform.position + Vector3.up * (r + 0.1f),
                        r, Vector3.down, out var hit, 0.4f,
                        FallGuysCharacterController.groundMask, QueryTriggerInteraction.Ignore);
                    ghostAnim.SetBool(FallGuysCharacterController.groundedParam, grounded);
                    ghostAnim.SetFloat(FallGuysCharacterController.slopeAngleParam,
                        grounded ? Vector3.Angle(hit.normal, Vector3.up) : 0f);

                    // GameplayTimeElapsed is replicated, so neighbouring frames share a timestamp often
                    // enough that a dt=0 zero velocity would snap the ghost to idle mid-stride
                    float dt = idx + 1 < frames.Count ? frames[idx + 1].t - frames[idx].t : 0f;
                    if (dt > 0f)
                    {
                        Vector3 v = (frames[idx + 1].pos - frames[idx].pos) / dt;
                        Vector3 local = Quaternion.Inverse(frames[idx].rot) * new Vector3(v.x, 0f, v.z);
                        float mod = FallGuysCharacterController.AnimationVelocityParamModifier;
                        ghostAnim.SetFloat(FallGuysCharacterController.zVelParam, local.z * mod);
                        ghostAnim.SetFloat(FallGuysCharacterController.xVelParam, local.x * mod);
                        ghostAnim.SetFloat(FallGuysCharacterController.yVelParam, v.y * mod);
                        ghostAnim.SetFloat(FallGuysCharacterController.airOnlyYVelParam, grounded ? 0f : v.y * mod);
                    }
                }
                yield return null;
            }
            // ghost crossed the line — stamp the fallfeed with the PB snapshotted at spawn time.
            // reading PBStore here would return the new (faster) time if the local player just beat it.
            if (finished)
                FireGhostQualifyFallFeed(ghostName, ghostPb);
            if (ghostGo != null && _ghostGos.Contains(ghostGo))
            {
                _ghostGos.Remove(ghostGo);
                UnityEngine.Object.Destroy(ghostGo);
            }
        }

        // spawn a fallfeed for a ghost that just finished its playback. bakes the PB time straight
        // into the message when the qual-time tweak is on (its own postfix would no-op on this feed
        // because FeatureTimePlacement has no server qualifyTime for the ghost's fake player key).
        static void FireGhostQualifyFallFeed(string ghostName, float pbSeconds)
        {
            try
            {
                var mgr = UnityEngine.Object.FindObjectOfType<FGClient.FallFeed.FallFeedManager>();
                var container = mgr?._fallFeedContainer;
                if (container == null) return;

                string msg = ghostName + " <sprite name=\"fallfeed-race\" tint=1>";
                if (BetterFG.Tweaks.FallFeedQualTimeTweak.Instance?.IsEnabled == true)
                {
                    TimeSpan t = TimeSpan.FromSeconds(pbSeconds);
                    string stamp = string.Format("{0:D2}:{1:D2}:{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
                    msg = ghostName + " <color=#FFFF00>" + stamp + "</color> <sprite name=\"fallfeed-race\" tint=1>";
                }

                var data = new FGClient.FallFeed.FallFeedManager.FallFeedMessageData();
                data.Message = msg;
                container.CreateNotification(data, container._fontSize);
                container.PlaySounds(FGClient.FallFeed.FallFeedManager.FallFeedAudio.Qualification_squad_member);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("Ghost fallfeed failed: " + ex.Message); }
        }

        static void TryShowLocalQualifiedFromServerProgress(ClientGameManager cgm, GameMessageServerPlayerProgress msg)
        {
            if (cgm == null || msg == null) return;
            if (!msg.succeeded || msg.isSkipping) return;
            if (!cgm.IsMyLocalPlayer(msg.playerId)) return;
            if (!IsRaceRound()) return;

            float elapsed = GlobalGameStateClient.Instance?.GameStateView != null
                ? GlobalGameStateClient.Instance.GameStateView.GameplayTimeElapsed
                : 0f;

            if (elapsed <= 0f && msg.qualifyTime > 0)
                elapsed = msg.qualifyTime > 1000f ? msg.qualifyTime / 1000f : msg.qualifyTime;

            Plugin.Log.LogInfo("QualTime: server progress fired, elapsed=" + elapsed);
            ShowQualificationTime(elapsed);
        }

        // called from the shared ClientGameManager.Shutdown hub in UnityRoundPatches.
        public static void OnClientGameManagerShutdown() => _ghostRecording = false;

        // called from the shared HandleServerPlayerProgress hub in GameStatePatches.
        public static void OnServerPlayerProgress(ClientGameManager cgm, GameMessageServerPlayerProgress progressMessage)
        {
            TryShowLocalQualifiedFromServerProgress(cgm, progressMessage);
        }

        // ── Load-screen PB label ──────────────────────────────────────────────
        // Entry hooks (splash cache patch, share-code patch, LoadingScreenViewModel.UpdateDisplay)
        // all call OnLoadingScreenUpdateDisplay, which fires off SpawnPBLoadLabelCoroutine.
        // Dedupe is by checking the canvas for an already-spawned BettrFG_PBLoadLabel each tick —
        // multiple fires race to find the canvas; first one wins, rest no-op.

        const string PBLoadLabelName = "BettrFG_PBLoadLabel";
        const string LoadingScreenPath = "UICanvas_Client_V2(Clone)/LoadingScreen";

        public static void OnLoadingScreenUpdateDisplay()
        {
            if (!On("loadscreen") || !On("store")) return;
            BetterFGUIMan.Instance.StartCoroutine(SpawnPBLoadLabelCoroutine().WrapToIl2Cpp());
        }

        struct PbLoadInfo
        {
            public string cacheId;
            public PbType type;
            public float pb;
            public bool found;
            public bool isRaceRound;
        }

        // poll the live ClientGameManager for round id/name + squad size + race-round flag. returns
        // true once we have a usable round id AND a definite race-round answer (the qual-screen and
        // live-timer paths use the same gate, so we mirror it here).
        static IEnumerator ResolveLoadScreenPbInfo(System.Action<PbLoadInfo?> result)
        {
            string roundId = null;
            string roundName = null;
            PbType type = PbType.Solos;
            bool? isRace = null;
            float waited = 0f;
            while (waited < 8f)
            {
                try
                {
                    ClientGameManager cgm;
                    var gsv = GlobalGameStateClient.Instance?.GameStateView;
                    if (gsv != null && gsv.GetLiveClientGameManager(out cgm) && cgm != null)
                    {
                        if (cgm._round != null)
                        {
                            roundId = cgm._round.Id;
                            roundName = cgm._round.DisplayNameUnindented;
                        }
                        int sz = (int)cgm.SquadSize;
                        type = sz <= 1 ? PbType.Solos : (sz == 2 ? PbType.Duos : PbType.Squads);
                        if (cgm.GameRules != null) isRace = cgm.GameRules.IsRaceRound;
                    }
                }
                catch (Exception ex) { Plugin.Log.LogWarning("QualTime: loadscreen cgm lookup failed: " + ex.Message); }

                if (!string.IsNullOrEmpty(roundId) && isRace.HasValue) break;
                yield return new WaitForSeconds(0.1f);
                waited += 0.1f;
            }

            if (string.IsNullOrEmpty(roundId)) roundId = _roundIdCache;
            if (string.IsNullOrEmpty(roundName)) roundName = _roundNameCache;
            if (string.IsNullOrEmpty(roundId)) { result(null); yield break; }

            roundId = PBStore.CanonicalRoundId(roundId);
            bool isUgc = roundId.StartsWith("ugc-");
            string cacheId = (!isUgc && !string.IsNullOrEmpty(roundName)) ? roundName : roundId;

            float pb = 0f;
            bool found = PBStore.TryGet(cacheId, type, out pb, out _, roundName);
            if (!found && !string.IsNullOrEmpty(roundName) && roundName != cacheId)
                found = PBStore.TryGet(roundName, type, out pb, out _, null);

            result(new PbLoadInfo { cacheId = cacheId, type = type, pb = pb, found = found, isRaceRound = isRace == true });
        }

        static string FormatPbText(bool found, float pb)
        {
            if (!found) return "PB --:--:---";
            var t = TimeSpan.FromSeconds(pb);
            return string.Format("PB  {0:D2}:{1:D2}:{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
        }

        static IEnumerator SpawnPBLoadLabelCoroutine()
        {
            // wait for the loading-screen canvas to appear
            Transform canvas = null;
            float waited = 0f;
            while (waited < 15f)
            {
                var loading = GameObject.Find(LoadingScreenPath);
                if (loading != null && loading.activeInHierarchy)
                {
                    for (int i = 0; i < loading.transform.childCount; i++)
                    {
                        var c = loading.transform.GetChild(i);
                        if (c == null || !c.gameObject.activeInHierarchy) continue;
                        if (c.GetComponent<RectTransform>() == null) continue;
                        canvas = c;
                        break;
                    }
                    if (canvas != null) break;
                }
                yield return new WaitForSeconds(0.1f);
                waited += 0.1f;
            }
            if (canvas == null || canvas.Find(PBLoadLabelName) != null) yield break;

            // resolve PB info (round id, show type, race-round flag, stored pb)
            PbLoadInfo? infoBox = null;
            yield return BetterFGUIMan.Instance.StartCoroutine(
                ResolveLoadScreenPbInfo(r => infoBox = r).WrapToIl2Cpp());
            if (!infoBox.HasValue) yield break;
            var info = infoBox.Value;
            if (!info.isRaceRound) yield break; // matches the PB-store gate elsewhere

            // re-pick canvas right before spawning: the one we polled with could have been torn down
            // during the cgm-wait. also dodges IL2Cpp throwing NRE on GetComponent against a dead
            // transform later in the spawn block.
            var spawnLoading = GameObject.Find(LoadingScreenPath);
            if (spawnLoading == null || !spawnLoading.activeInHierarchy) yield break;
            Transform spawnCanvas = null;
            for (int i = 0; i < spawnLoading.transform.childCount; i++)
            {
                var c = spawnLoading.transform.GetChild(i);
                if (c == null || !c.gameObject.activeInHierarchy) continue;
                if (c.GetComponent<RectTransform>() == null) continue;
                spawnCanvas = c;
                break;
            }
            if (spawnCanvas == null || spawnCanvas.Find(PBLoadLabelName) != null) yield break;
            var safeArea = spawnCanvas.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "SafeArea") ?? spawnCanvas;

            string text = FormatPbText(info.found, info.pb);
            var labelTmp = SpawnPBLoadPanel(spawnCanvas, safeArea, text);

            // wait a frame before writing the PB text. the deleted TMPTextBinding's final Update()
            // still fires once after DestroyImmediate (Unity defers teardown to end-of-frame), so
            // setting text inline gets clobbered back to the round name. setting next frame races
            // past the binding's last tick.
            yield return null;
            if (labelTmp != null) labelTmp.text = text;

            Plugin.Log.LogInfo("QualTime: loadscreen PB " + text + " (" + info.cacheId + " " + info.type + ")");
        }

        // clones RoundName_Panel as our PB backdrop, re-anchored to the top-right of SafeArea,
        // mirrored horizontally. uses the panel's own RoundName_Text as the PB label (after
        // stripping its bindings/animators/etc), so the text inherits the same TMP styling /
        // material the game's round-name uses. returns the TMP so the caller can pin the text
        // a frame later — the binding's Update() can fire one more time after we DestroyImmediate
        // it (Unity defers actual component teardown to end-of-frame), which would otherwise
        // overwrite our text back to the round name. doing the set next frame races past that.
        static TMPro.TextMeshProUGUI SpawnPBLoadPanel(Transform spawnCanvas, Transform safeArea, string text)
        {
            var src = spawnCanvas.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "RoundName_Panel");
            if (src == null) return null;

            var go = UnityEngine.Object.Instantiate(src.gameObject, safeArea);
            go.name = "BettrFG_PBLoadPanel";

            // strip auto-sizing/auto-layout off the cloned root so its rect stays at our fixed size
            foreach (var c in go.GetComponents<UnityEngine.UI.ContentSizeFitter>()) UnityEngine.Object.Destroy(c);
            foreach (var c in go.GetComponents<UnityEngine.UI.LayoutGroup>()) UnityEngine.Object.Destroy(c);

            // anchor the panel itself to the top-right of SafeArea with zero offset. position +
            // scale are applied to the inner BettrFG_PBLoadContent child instead so we can tweak
            // those independently of the panel's pinning.
            var goRt = go.GetComponent<RectTransform>();
            goRt.anchorMin = new Vector2(1f, 1f);
            goRt.anchorMax = new Vector2(1f, 1f);
            goRt.pivot = new Vector2(1f, 1f);
            goRt.anchoredPosition = Vector2.zero;
            goRt.localScale = Vector3.one;

            // create a single content container as a child of the panel and re-parent every
            // existing child into it. that way one localPosition/localScale on the container
            // moves the curved sprite + the PB label together.
            var contentGo = new GameObject("BettrFG_PBLoadContent");
            var contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.SetParent(go.transform, false);
            contentRt.anchorMin = new Vector2(1f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(1f, 1f);
            contentRt.anchoredPosition = new Vector2(500f, 0f);
            contentRt.localScale = Vector3.one * 0.6f;
            BetterFGUIMan.Instance.StartCoroutine(PBLoadContentPopIn(contentRt).WrapToIl2Cpp());

            // move every original child (Panel, RoundName_Text, decorations) under the content
            // container. iterate by index but reparent from the front so the child count we walk
            // doesn't include the container itself (which is currently the last child).
            int originalChildCount = go.transform.childCount - 1; // -1 to skip the container we just made
            for (int i = 0; i < originalChildCount; i++)
                go.transform.GetChild(0).SetParent(contentRt, false);

            // disable every direct child of the container except Panel and RoundName_Text - strips
            // out the round-name icons/glyphs/bindings, leaving just the curved sprite + the text.
            for (int i = 0; i < contentRt.childCount; i++)
            {
                var child = contentRt.GetChild(i);
                if (child.name == "Panel")
                {
                    child.localScale = new Vector3(-1f, 1.2f, 1f);
                    child.localPosition = new Vector3(-220.7273f, -98.8166f, 0f);
                    var outline = child.FindChild("BottomPanel_Outline");
                    if (outline != null) outline.gameObject.SetActive(false);
                    //child.gameObject.SetActive(false);
                    continue;
                }
                if (child.name == "RoundName_Text") continue;
                if (child.parent.name == "Panel") continue;
                child.gameObject.SetActive(false);
            }

            // find the cloned RoundName_Text and re-instantiate it as our label so peer
            // animators/bindings under the panel don't fire against a stripped peer (which
            // NRE'd every frame when we mutated in-place).
            var origText = contentRt.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "RoundName_Text");
            TMPro.TextMeshProUGUI tmp = null;
            if (origText != null)
            {
                var labelGo = UnityEngine.Object.Instantiate(origText.gameObject, contentRt);
                labelGo.name = PBLoadLabelName;
                origText.gameObject.SetActive(false);


                // kill the TMPTextBinding on the clone or it'll re-bind to the round name and
                // overwrite our PB text. DestroyImmediate so the binding's Update() can't fire
                // one more time before it's actually gone (which is what Destroy lets happen).
                foreach (var b in labelGo.GetComponents<Mediatonic.Tools.MVVM.TMPTextBinding>())
                    UnityEngine.Object.DestroyImmediate(b);

                tmp = labelGo.GetComponent<TMPro.TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.alignment = TMPro.TextAlignmentOptions.Center;
                    tmp.enableWordWrapping = false;
                }
                labelGo.transform.localPosition = new Vector3(-414.0297f, -100.7729f, 0f);
                labelGo.transform.localScale = new Vector3(1.42f, 1.42f, 1.42f);
                labelGo.SetActive(true);
            }

            go.SetActive(true);
            return tmp;
        }

        // x: 500 -> 0 -> 20, total 0.6s. ease-out cubic on the slide-in, quick settle back.
        static IEnumerator PBLoadContentPopIn(RectTransform rt)
        {
            const float slideDur = 0.45f;
            const float settleDur = 0.15f;
            float t = 0f;
            while (t < slideDur)
            {
                if (rt == null) yield break;
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / slideDur);
                float eased = 1f - Mathf.Pow(1f - u, 3f); // ease-out cubic
                rt.anchoredPosition = new Vector2(Mathf.Lerp(500f, 0f, eased), rt.anchoredPosition.y);
                yield return null;
            }
            t = 0f;
            while (t < settleDur)
            {
                if (rt == null) yield break;
                t += Time.deltaTime;
                float u = Mathf.Clamp01(t / settleDur);
                float eased = u * u * (3f - 2f * u); // smoothstep
                rt.anchoredPosition = new Vector2(Mathf.Lerp(0f, 20f, eased), rt.anchoredPosition.y);
                yield return null;
            }
            if (rt != null) rt.anchoredPosition = new Vector2(20f, rt.anchoredPosition.y);
        }

        // called from the shared RoundLoader.CleanupLoadingScreens hub in GameStatePatches.
        public static void OnCleanupLoadingScreens()
        {
            ResetRaceRoundCache();
            _ghostRecording = false;
            _ghostFrames = null;
            foreach (var g in _ghostGos) if (g != null) UnityEngine.Object.Destroy(g);
            _ghostGos.Clear();
            if (On("ghost"))
            {
                int gen = ++_ghostGen;
                BetterFGUIMan.Instance.StartCoroutine(SpawnGhostDeferred(gen).WrapToIl2Cpp());
                _ghostFrames = new List<(float, Vector3, Quaternion, int, float)>();
                _ghostRecording = true;
                BetterFGUIMan.Instance.StartCoroutine(GhostRecordCoroutine(gen).WrapToIl2Cpp());
            }
            _roundIdCache = null;
            _roundNameCache = null;
            BetterFGUIMan.Instance.StartCoroutine(SpawnLiveTimerDeferred().WrapToIl2Cpp());
        }

        [HarmonyPatch(typeof(RoundLoader), "ShowLoadingGameScreenForSelectedRound")]
        public class Patch_RoundLoaderSplashCache
        {
            [HarmonyPostfix]
            public static void Postfix(object[] __args)
            {
                string roundId = null;
                string roundName = null;
                if (__args != null)
                {
                    foreach (var arg in __args)
                    {
                        var round = arg as Round;
                        if (round == null) continue;
                        roundId = round.Id;
                        roundName = round.DisplayNameUnindented;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(roundId))
                {
                    _roundIdCache = PBStore.CanonicalRoundId(roundId);
                    _roundNameCache = roundName;
                }

                BetterFGUIMan.Instance.StartCoroutine(SplashCache.TryCacheSplashForCurrentRound(_roundIdCache, roundName).WrapToIl2Cpp());
                OnLoadingScreenUpdateDisplay();
            }
        }

        // called from the shared RoundLoader.LoadViaShareCodeAndVersion hub in GameStatePatches.
        public static void OnLoadViaShareCodeAndVersion(Round round)
        {
            if (round != null)
            {
                _roundIdCache = PBStore.CanonicalRoundId(round.Id);
                _roundNameCache = round.DisplayNameUnindented;
            }
            BetterFGUIMan.Instance.StartCoroutine(SplashCache.TryCacheSplashForCurrentRound(_roundIdCache, round?.DisplayNameUnindented).WrapToIl2Cpp());
            OnLoadingScreenUpdateDisplay();
        }
    }

    internal static class SplashCache
    {
        static string CacheDir
        {
            get
            {
                // same folder as the dll
                string dllDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                return Path.Combine(dllDir, "CachedRoundSplashScreens");
            }
        }

        public static string GetCachePath(string roundId)
        {
            if (string.IsNullOrEmpty(roundId)) return null;
            roundId = PBStore.CanonicalRoundId(roundId);
            string safe = string.Concat(roundId.Split(Path.GetInvalidFileNameChars()));
            return Path.Combine(CacheDir, safe + ".jpg");
        }

        public static bool HasCached(string roundId) => File.Exists(GetCachePath(roundId));

        public static void TryRename(string oldId, string newId)
        {
            try
            {
                string oldPath = GetCachePath(oldId);
                string newPath = GetCachePath(newId);
                if (File.Exists(oldPath) && !File.Exists(newPath))
                    File.Move(oldPath, newPath);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"SplashCache: rename failed: {ex.Message}"); }
        }

        // keep loaded textures in memory so callers that rebuild their UI a lot (the PB tab re-renders
        // its row list on every sort/filter/subtab change) don't re-read the file + alloc a new texture
        // every time. without this each render churns ~25 disk reads + 25 Texture2D allocs.
        static readonly Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();

        public static Texture2D LoadCached(string roundId, string displayName = null)
        {
            roundId = PBStore.CanonicalRoundId(roundId);
            bool isUgc = !string.IsNullOrEmpty(roundId) && roundId.StartsWith("ugc-");
            string cacheKey = (!isUgc && !string.IsNullOrEmpty(displayName)) ? displayName : roundId;
            if (_texCache.TryGetValue(cacheKey, out var cached) && cached != null) return cached;

            string path = GetCachePath(cacheKey);
            if (!File.Exists(path)) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (tex.LoadImage(bytes)) { _texCache[cacheKey] = tex; return tex; }
                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"SplashCache: load failed for {roundId}: {ex.Message}"); }
            return null;
        }

        public static IEnumerator TryCacheSplashForCurrentRound(string roundIdHint = null, string roundNameHint = null)
        {
            yield return new WaitForSeconds(0.5f);

            // poll until the sprite texture is actually loaded, or the loading screen is gone
            Texture2D srcTex = null;
            float waited = 0f;
            while (waited < 6f)
            {
                var imgGo = GameObject.Find(
                    "UICanvas_Client_V2(Clone)/LoadingScreen/Prime_UI_RoundSelected_UP_Prefab_Canvas(Clone)/SafeArea/SelectedShow/ShowMask/ShowImage")
                    ?? GameObject.Find(
                    "UICanvas_Client_V2(Clone)/LoadingScreen/Prime_UI_RoundSelected_UGC_Prefab_Canvas(Clone)/SafeArea/SelectedShow/ShowMask/ShowImage")
                    ?? GameObject.Find(
                    "UICanvas_Client_V2(Clone)/LoadingScreen/Prime_UI_RoundSelected_Prefab_Canvas(Clone)/SafeArea/SelectedShow/ShowMask/ShowImage");
                if (imgGo != null)
                {
                    var _img = imgGo.GetComponent<Image>();
                    if (_img != null && _img.sprite != null && _img.sprite.texture != null)
                    {
                        srcTex = _img.sprite.texture;
                        break;
                    }
                }
                yield return new WaitForSeconds(0.2f);
                waited += 0.2f;
            }

            if (srcTex == null) { Plugin.Log.LogWarning("SplashCache: sprite never loaded in 6s, bailing"); yield break; }

            string roundId = roundIdHint;
            int lookupTries = 0;
            while (string.IsNullOrEmpty(roundId) && lookupTries < 3)
            {
                lookupTries++;
                try
                {
                    roundId = GlobalGameStateClient.Instance?.GameStateView?.CurrentGameLevelName;
                    if (string.IsNullOrEmpty(roundNameHint))
                    {
                        ClientGameManager cgm;
                        var gsv = GlobalGameStateClient.Instance?.GameStateView;
                        if (gsv != null && gsv.GetLiveClientGameManager(out cgm) && cgm?._round != null)
                        {
                            if (string.IsNullOrEmpty(roundId)) roundId = cgm._round.Id;
                            roundNameHint = cgm._round.DisplayNameUnindented;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"SplashCache: round lookup try {lookupTries} failed: {ex.Message}");
                }

                if (string.IsNullOrEmpty(roundId))
                    yield return new WaitForSeconds(0.5f);
            }
            if (string.IsNullOrEmpty(roundId)) { Plugin.Log.LogWarning("SplashCache: no roundId, bailing"); yield break; }
            roundId = PBStore.CanonicalRoundId(roundId);

            bool isUgc = roundId.StartsWith("ugc-");
            string cacheKey = roundId;
            if (!isUgc)
            {
                string dn = roundNameHint;
                if (!string.IsNullOrEmpty(dn)) cacheKey = dn;
            }

            if (HasCached(cacheKey)) { Plugin.Log.LogInfo($"SplashCache: already cached {cacheKey}"); yield break; }

            try
            {
                var rt = RenderTexture.GetTemporary(srcTex.width, srcTex.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(srcTex, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;

                var readable = new Texture2D(srcTex.width, srcTex.height, TextureFormat.RGB24, false);
                readable.ReadPixels(new Rect(0, 0, srcTex.width, srcTex.height), 0, 0);
                readable.Apply();

                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);

                byte[] jpg = readable.EncodeToJPG(62);
                UnityEngine.Object.Destroy(readable);

                Directory.CreateDirectory(CacheDir);
                string path = GetCachePath(cacheKey);
                File.WriteAllBytes(path, jpg);
                Plugin.Log.LogInfo($"SplashCache: saved {cacheKey} -> {jpg.Length / 1024}kb");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"SplashCache: exception: {ex.Message}");
            }
        }
    }

    public class GhostRecorderComponent : MonoBehaviour
    {
        public GhostRecorderComponent(IntPtr ptr) : base(ptr) { }

        private Animator _anim;
        private float _elapsed;
        const float Interval = 1f / 20f;

        void Awake()
        {
            var wrapper = transform.Find("BetterFG_ScaleWrapper");
            var charT = (wrapper != null ? wrapper : transform).Find("Character");
            _anim = charT?.GetComponent<Animator>();
        }

        void Update()
        {
            //using var _ = BetterFG.Utilities.PerfProbe.Time("ghost.RecorderUpdate");
            var frames = FeatureQualificationTime._ghostFrames;
            if (frames == null) return;
            _elapsed += Time.deltaTime;
            if (_elapsed < Interval) return;
            _elapsed = 0f;
            float t = GlobalGameStateClient.Instance?.GameStateView?.GameplayTimeElapsed ?? 0f;
            int sh = 0; float at = 0f;
            if (_anim != null)
            {
                var info = _anim.GetCurrentAnimatorStateInfo(0);
                sh = info.shortNameHash;
                at = info.normalizedTime;
            }
            frames.Add((t, transform.position, transform.rotation, sh, at));
        }
    }
}
