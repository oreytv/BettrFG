using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FGClient;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public class MatchmakingQueueCountTweak : BfgTweak
    {
        public MatchmakingQueueCountTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "matchmaking_queue_count";
        public override string TweakLabel => "Matchmaking Queue Count";
        public override bool DefaultEnabled => true;

        public static MatchmakingQueueCountTweak Instance { get; private set; }

        private static TMPro.TMP_Text _label;
        private static bool _matchmaking;
        private static int _queuedPlayers;

        void Awake() => Instance = this;

        public override void EnableTweak() { }
        public override void DisableTweak() => DestroyLabel();

        internal static void OnMatchmakingStart()
        {
            _matchmaking = true;
            _queuedPlayers = 0;
            Plugin.Log.LogInfo("MatchmakingQueueCount: matchmaking started");
        }

        internal static void OnMatchmakingEnd()
        {
            _matchmaking = false;
            DestroyLabel();
            Plugin.Log.LogInfo("MatchmakingQueueCount: matchmaking ended");
        }

        internal static void OnQueuedPlayersUpdate(int count)
        {
            _queuedPlayers = count;
            Plugin.Log.LogInfo($"MatchmakingQueueCount: queue count: {count}");

            var inst = Instance;
            if (inst == null || !inst.IsEnabled) return;

            try
            {
                if (GlobalGameStateClient.Instance?._gameStateMachine?.CurrentState?.TryCast<StatePrivateLobby>() != null)
                    return;
            }
            catch { }

            if (_label != null)
            {
                _label.text = $"In queue: {count}";
                return;
            }

            inst.StartCoroutine(CreateLabel(count).WrapToIl2Cpp());
        }

        private static void DestroyLabel()
        {
            if (_label != null)
            {
                UnityEngine.Object.Destroy(_label.gameObject);
                _label = null;
            }
        }

        private static IEnumerator CreateLabel(int initialCount)
        {
            float elapsed = 0f;
            while (elapsed < 10f)
            {
                var source = GameObject.Find("Menu_Screen_Lobby(Clone)/ForegroundCanvas/Prefab_UI_Lobby/UI_Matchmaking_Prime/SafeArea/PlayersFound/PlayerNumText");
                if (source != null)
                {
                    var clone = UnityEngine.Object.Instantiate(source, source.transform.parent);
                    clone.name = "BFG_MatchmakingQueueText";

                    var srcTmp = source.GetComponent<TMPro.TMP_Text>();
                    _label = clone.GetComponent<TMPro.TMP_Text>();
                    if (_label != null)
                    {
                        _label.fontSize = srcTmp != null ? srcTmp.fontSize * 0.55f : _label.fontSize * 0.55f;
                        _label.text = $"In queue: {_queuedPlayers}";
                        _label.alignment = TMPro.TextAlignmentOptions.Center;
                        _label.fontStyle = srcTmp != null ? srcTmp.fontStyle & ~TMPro.FontStyles.UpperCase : _label.fontStyle & ~TMPro.FontStyles.UpperCase;
                    }

                    var srcRt = source.GetComponent<RectTransform>();
                    var rt = clone.GetComponent<RectTransform>();
                    if (rt != null && srcRt != null)
                        rt.anchoredPosition = new Vector2(0f, srcRt.anchoredPosition.y);

                    clone.SetActive(true);
                    Plugin.Log.LogInfo("MatchmakingQueueCount: label created");
                    yield break;
                }

                yield return new WaitForSeconds(0.25f);
                elapsed += 0.25f;
            }
            Plugin.Log.LogWarning("MatchmakingQueueCount: timed out finding PlayerNumText");
        }
    }

    [HarmonyPatch(typeof(FNMMSClientRemoteService), nameof(FNMMSClientRemoteService.ProcessMessageReceived))]
    internal static class MatchmakingQueueCountUpdatePatch
    {
        [HarmonyPostfix]
        public static void Postfix(string jsonMessage)
        {
            if (string.IsNullOrEmpty(jsonMessage)) return;
            if (!jsonMessage.Contains("\"Queued\"")) return;

            var key = "\"queuedPlayers\":";
            int ki = jsonMessage.IndexOf(key);
            if (ki < 0) return;
            int start = ki + key.Length;
            while (start < jsonMessage.Length && (jsonMessage[start] == ' ' || jsonMessage[start] == '"')) start++;
            int end = start;
            while (end < jsonMessage.Length && char.IsDigit(jsonMessage[end])) end++;
            if (end > start && int.TryParse(jsonMessage.Substring(start, end - start), out int count))
                MatchmakingQueueCountTweak.OnQueuedPlayersUpdate(count);
        }
    }
}
