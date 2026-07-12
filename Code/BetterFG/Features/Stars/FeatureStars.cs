using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using FGClient;
using BetterFG.Utilities;
using Mediatonic.Tools.MVVM;
using TMPro;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.Features;
using BetterFG.UI;
using FGClient.UI;
using FGClient.ShowSelector;
using FG.Common;

namespace BetterFG.Features.Stars
{
    public class FeatureStars
    {
        public static readonly bfgfeature feature = new bfgfeature("stars", "Stars", true, new List<featuresetting>
        {
            new featuresetting { id = "store", label = "Store stars", defaultOn = true },
            new featuresetting { id = "qual", label = "Show star count on qual", defaultOn = true },
            new featuresetting { id = "menu", label = "Show star count in menu", defaultOn = true },
        });

        static bool On(string setting) => BetterFG.Features.featureRegistry.IsOn("stars", setting);

        static TextMeshProUGUI _counterText;
        static GameObject _cachedCrownCounter;

        static readonly AnimationCurve popOutCurve = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.5f, 1.1f),
            new Keyframe(1f, 0f)
        );

        public static void CreateInMenu()
        {
            if (!On("store") || !On("menu")) return;
            var menucrowndisplay = GameObject.Find(
                "UICanvas_Client_V2(Clone)/Default/Topbar_Prime(Clone)/SafeArea/TopRight_Group/CurrencyHorizontalLayout/Crowns_CurrencyCounter")?.transform;

            // Prime_UI_SymphonyShowSelector_Prefab_Canvas(Clone) is disabled so Find won't reach it
            // traverse manually from the last active ancestor
            Transform targetParent = null;
            var menuScreenMain = GameObject.Find(
                "UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Main")?.transform;
            if (menuScreenMain != null)
            {
                var symphonyCanvas = menuScreenMain.Find("Prime_UI_SymphonyShowSelector_Prefab_Canvas(Clone)");
                if (symphonyCanvas != null)
                    targetParent = symphonyCanvas.Find("ShowSelectorContent");
            }

            if (menucrowndisplay == null || targetParent == null)
            {
                return;
            }

            if (_cachedCrownCounter == null)
            {
                _cachedCrownCounter = UnityEngine.Object.Instantiate(menucrowndisplay.gameObject);
                UnityEngine.Object.DontDestroyOnLoad(_cachedCrownCounter);
                _cachedCrownCounter.SetActive(false);
            }

            

            var icon = menucrowndisplay.Find("Icon");
            if (icon != null)
            {
                var img = icon.GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                    img.sprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.feature.star.featurestar_star.png");
            }

            menucrowndisplay.SetParent(targetParent, false);
            var rt = menucrowndisplay.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.97f, 0.97f);
                rt.anchorMax = new Vector2(0.97f, 0.97f);
                rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = Vector2.zero;
            }
            else menucrowndisplay.localPosition = new Vector3(830f, 475f, 0f);
            menucrowndisplay.gameObject.SetActive(true);

            var amountTmp = menucrowndisplay.Find("AmountText");
            if (amountTmp != null)
            {
                var binding = amountTmp.GetComponent<PropertyToPropertyBinding>();
                if (binding != null) UnityEngine.Object.Destroy(binding);

                _counterText = amountTmp.GetComponent<TextMeshProUGUI>();
                RefreshCounter();
            }
        }

        public static void RefreshCounter()
        {
            if (_counterText == null) return;
            _counterText.text = StarStore.Count.ToString();
            _counterText.ForceMeshUpdate();
        }

        static bool IsUgcRound(string roundId)
        {
            return !string.IsNullOrEmpty(roundId) && roundId.StartsWith("ugc-");
        }

        static bool IsSolo()
        {
            var cpm = GlobalGameStateClient.Instance?._clientPlayerManager;
            if (cpm == null) return false;
            return cpm._players != null && cpm._players.Count == 1;
        }

        public static void TryRecordQualification(ClientGameManager cgm, GameMessageServerPlayerProgress msg)
        {
            if (!On("store")) return;

            if (cgm == null || msg == null) return;
            if (!msg.succeeded || msg.isSkipping) return;
            if (!cgm.IsMyLocalPlayer(msg.playerId)) return;

            if (!IsSolo())
            {
                Plugin.Log.LogInfo("Stars: not solo, skipping");
                return;
            }

            var round = cgm._round;
            string roundId = round?.Id ?? GlobalGameStateClient.Instance?.GameStateView?.CurrentGameLevelName;

            if (!IsUgcRound(roundId))
            {
                Plugin.Log.LogInfo($"Stars: not a UGC round ({roundId}), skipping");
                return;
            }

            int countBefore = StarStore.Count;
            bool isNew = StarStore.TryRecord(roundId);
            int countAfter = StarStore.Count;
            Plugin.Log.LogInfo($"Stars: recorded {roundId}, new={isNew}");

            if (isNew)
            {
                RefreshCounter();
                if (On("qual"))
                    BetterFGUIMan.Instance.StartCoroutine(ShowStarPopupDelayed(countBefore, countAfter).WrapToIl2Cpp());
            }
        }

        public static GameObject StampStarOverlay(ShowSelectorShowTileViewModel tile, string shareCode)
        {
            if (!On("store")) return null;
            if (tile == null) return null;
            if ((tile.gameObject.hideFlags & HideFlags.HideAndDontSave) != 0) return null;

            try
            {
                var tileHolder = tile.transform.Find("ShowTileHolder");
                if (tileHolder == null) return null;

                var existing = tileHolder.Find("StarOverlay_BFG");
                if (existing != null) return existing.gameObject;

                var overlay = tileHolder.Find("TargetConditionNotMet_Overlay");
                if (overlay == null) return null;

                var overlayClone = UnityEngine.Object.Instantiate(overlay.gameObject, tileHolder);
                overlayClone.name = "StarOverlay_BFG";
                overlayClone.SetActive(true);

                var rootImg = overlayClone.GetComponent<UnityEngine.UI.Image>();
                if (rootImg != null) UnityEngine.Object.Destroy(rootImg);

                var purpleOverlay = overlayClone.transform.Find("PurpleOverlay");
                if (purpleOverlay != null) purpleOverlay.gameObject.SetActive(false);
                var lockIcon = overlayClone.transform.Find("ConditionLockIcon");
                if (lockIcon != null) lockIcon.gameObject.SetActive(false);

                foreach (var b in overlayClone.GetComponentsInChildren<ActiveBinding>(true))
                    UnityEngine.Object.Destroy(b);
                foreach (var b in overlayClone.GetComponentsInChildren<PropertyToPropertyBinding>(true))
                    UnityEngine.Object.Destroy(b);
                foreach (var b in overlayClone.GetComponentsInChildren<TextBinding>(true))
                    UnityEngine.Object.Destroy(b);

                var bannerTr = overlayClone.transform.Find("ConditionText_Banner");
                if (bannerTr == null) return null;

                var condTr = bannerTr.Find("Condition_Text");
                if (condTr == null) return null;

                condTr.gameObject.SetActive(true);

                var tmp = condTr.GetComponent<TextMeshProUGUI>();
                if (tmp != null) UnityEngine.Object.Destroy(tmp);

                var starGo = new GameObject("StarIcon_BFG");
                starGo.transform.SetParent(condTr, false);
                var starRt = starGo.AddComponent<RectTransform>();
                starRt.anchorMin = Vector2.zero;
                starRt.anchorMax = Vector2.one;
                starRt.offsetMin = Vector2.zero;
                starRt.offsetMax = Vector2.zero;
                var starImg = starGo.AddComponent<UnityEngine.UI.Image>();
                starImg.sprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.feature.star.featurestar_star.png");
                starImg.preserveAspect = true;

                BetterFGUIMan.Instance.StartCoroutine(RecheckStampValidity(tile, shareCode, overlayClone).WrapToIl2Cpp());
                return overlayClone;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogInfo($"Stars: overlay err on tile: {ex.Message}");
                return null;
            }
        }

        static IEnumerator ShowStarPopupDelayed(int countBefore, int countAfter)
        {
            yield return new WaitForSeconds(2f);
            yield return ShowStarPopup(countBefore, countAfter);
        }

        static IEnumerator ShowStarPopup(int countBefore, int countAfter)
        {
            if (_cachedCrownCounter == null)
            {
                yield break;
            }

            var canvas = GameObject.Find("UICanvas_Client_V2(Clone)");
            if (canvas == null) yield break;

            var popup = UnityEngine.Object.Instantiate(_cachedCrownCounter, canvas.transform);
            popup.name = "StarPopup_Display";

            AudioService.PlayStarsUp();

            foreach (var b in popup.GetComponentsInChildren<ActiveBinding>(true))
                UnityEngine.Object.Destroy(b);
            foreach (var b in popup.GetComponentsInChildren<PropertyToPropertyBinding>(true))
                UnityEngine.Object.Destroy(b);

            var icon = popup.transform.Find("Icon");
            if (icon != null)
            {
                var img = icon.GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                    img.sprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.feature.star.featurestar_star.png");
            }

            var rt = popup.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                var canvasRt = canvas.GetComponent<RectTransform>();
                float canvasH = canvasRt != null ? canvasRt.rect.height : Screen.height;
                rt.anchoredPosition = new Vector2(0f, canvasH * 0.82f);
            }

            popup.transform.localScale = Vector3.zero;

            var amountTmp = popup.transform.Find("AmountText")?.GetComponent<TextMeshProUGUI>();
            if (amountTmp != null)
            {
                amountTmp.text = countBefore.ToString();
                amountTmp.ForceMeshUpdate();
            }

            popup.SetActive(true);

            const float baseScale = 2f;
            float t = 0f;
            float popInDur = 0.3f;
            while (t < popInDur)
            {
                t += Time.deltaTime;
                float p = t / popInDur;
                float s = p < 0.5f
                    ? Mathf.Lerp(0f, 1.1f, p / 0.5f)
                    : Mathf.Lerp(1.1f, 1f, (p - 0.5f) / 0.5f);
                popup.transform.localScale = Vector3.one * (s * baseScale);
                yield return null;
            }
            popup.transform.localScale = Vector3.one * baseScale;

            yield return new WaitForSeconds(0.5f);

            float pulseDur = 0.5f;
            t = 0f;
            var basePos = popup.transform.localPosition;
            while (t < pulseDur)
            {
                t += Time.deltaTime;
                float p = t / pulseDur;
                float s = Mathf.Lerp(1f, 0.85f, p) * baseScale;
                popup.transform.localScale = Vector3.one * s;
                float shakeAmt = Mathf.Lerp(4f, 0f, p);
                popup.transform.localPosition = basePos + (Vector3)(UnityEngine.Random.insideUnitCircle * shakeAmt);
                yield return null;
            }

            popup.transform.localScale = Vector3.one * (0.85f * baseScale);
            popup.transform.localPosition = basePos;

            if (amountTmp != null)
            {
                amountTmp.text = countAfter.ToString();
                amountTmp.ForceMeshUpdate();
            }

            float popOutDur = 0.25f;
            t = 0f;
            while (t < popOutDur)
            {
                t += Time.deltaTime;
                float p = t / popOutDur;
                float s = p < 0.5f
                    ? Mathf.Lerp(0.85f, 1.2f, p / 0.5f)
                    : Mathf.Lerp(1.2f, 1f, (p - 0.5f) / 0.5f);
                popup.transform.localScale = Vector3.one * (s * baseScale);
                yield return null;
            }
            popup.transform.localScale = Vector3.one * baseScale;

            float elapsed = popInDur + 0.5f + pulseDur + 0.25f;
            float remaining = 3f - elapsed;
            if (remaining > 0f)
                yield return new WaitForSeconds(remaining);

            float dismissDur = 0.4f;
            t = 0f;
            while (t < dismissDur)
            {
                if (popup == null) yield break;
                t += Time.deltaTime;
                float s = popOutCurve.Evaluate(t / dismissDur);
                popup.transform.localScale = Vector3.one * (s * baseScale);
                yield return null;
            }

            UnityEngine.Object.Destroy(popup);
        }

        static IEnumerator RecheckStampValidity(ShowSelectorShowTileViewModel tile, string stampedCode, GameObject overlayGo)
        {
            yield return new WaitForSeconds(1f);
            if (tile == null || tile.gameObject == null || overlayGo == null) yield break;

            var current = tile._showData?.ShowSelectorShow;
            string currentCode = current?.ShareCode ?? "(null)";

            if (currentCode != stampedCode)
            {
                UnityEngine.Object.Destroy(overlayGo);
            }
        }

        static IEnumerator StampStarOverlayDelayed(ShowSelectorShowTileViewModel tile, string shareCodeAtDispatch)
        {
            if (!On("store")) yield break;
            if (string.IsNullOrEmpty(shareCodeAtDispatch) || shareCodeAtDispatch == "(null)") yield break;
            yield return new WaitForSeconds(0.5f);
            if (tile == null || tile.gameObject == null) yield break;

            // read current sharecode from tile after delay
            var current = tile._showData?.ShowSelectorShow;
            string currentCode = current?.ShareCode ?? "(null)";

            var cleared = StarStore.GetAll();
            bool hasPlain = cleared.Contains(currentCode);
            bool hasPrefixed = cleared.Contains("ugc-" + currentCode);
            bool hasstar = hasPlain || hasPrefixed;

            if (shareCodeAtDispatch != currentCode)
            {
                yield break;
            }

            if (!hasstar)
            {
                // tile might have a leftover overlay from a previous occupant -- nuke it
                var tileHolder = tile.transform.Find("ShowTileHolder");
                var stale = tileHolder?.Find("StarOverlay_BFG");
                if (stale != null)
                {
                    UnityEngine.Object.Destroy(stale.gameObject);
                }
                yield break;
            }

            var stamped = StampStarOverlay(tile, currentCode);
            if (stamped != null)
                BetterFGUIMan.Instance.StartCoroutine(RecheckStampValidity(tile, currentCode, stamped).WrapToIl2Cpp());
        }

        // called from the shared SetIndividualShowData hub in GameStatePatches.
        public static void OnSetIndividualShowData(ShowSelectorShowTileViewModel tile, ShowSelectorShow showSelectorShow)
        {
            string code = showSelectorShow?.ShareCode ?? "(null)";
            if (!On("store") || string.IsNullOrEmpty(code) || code == "(null)") return;
            BetterFGUIMan.Instance.StartCoroutine(StampStarOverlayDelayed(tile, code).WrapToIl2Cpp());
        }

        // called from the shared HandleServerPlayerProgress hub in GameStatePatches.
        public static void OnServerPlayerProgress(ClientGameManager cgm, GameMessageServerPlayerProgress progressMessage)
        {
            TryRecordQualification(cgm, progressMessage);
        }
    }
}
