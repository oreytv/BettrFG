using System;
using System.Collections.Generic;
using UnityEngine;
using BetterFG.Customization.Player;
using System.Collections;
using BetterFG.Customization.Menu;
using FG.Common;

namespace BetterFG.Services
{
    public enum PlinthType { MainMenu, Victory, Reward }

    // Represents a plinth slot in any screen (victory, reward, main menu etc.)
    // holderGO  = parent transform we instantiate the custom plinth under
    // meshGO    = the original ENV_Plinth_MO geometry child we hide
    public class PlinthSlot
    {
        public GameObject holderGO;
        public GameObject meshGO;
        public PlinthType type;
    }

    public class BeanMonitorService : MonoBehaviour
    {
        public static BeanMonitorService Instance { get; private set; }

        private static GameObject _localPlayerBean;
        public static GameObject LocalPlayerBean
        {
            get { return _localPlayerBean; }
            set
            {
                if (_localPlayerBean == value) return;
                _localPlayerBean = value;
                if (_localPlayerBean != null)
                {
                    SkinApplicationService.Instance?.PollAndReapplyCustomTextureForBean(_localPlayerBean);
                    PlayerScaleService.RestorePlayerScaleToBean(_localPlayerBean);
                }
            }
        }

        private SkinApplicationService skinApplicationService;
        private Dictionary<int, GameObject> loggedBeans = new Dictionary<int, GameObject>();

        // keyed by holderGO instance id
        private Dictionary<int, PlinthSlot> trackedPlinths = new Dictionary<int, PlinthSlot>();

        void Awake()
        {
            Instance = this;
        }

        // throttle for the font watchdog. the game re-slams the original gold/fame material onto nametags
        // sporadically (fame animation), corrupting our swapped font's glyphs; our event hooks don't catch
        // every re-set, so poll the small known-nametag set a couple times a second and re-derive any that
        // drifted. no-op (and basically free) when font replacement is off — see TickNametagWatchdog.
        private float _fontWatchdogTimer;

        void Update()
        {
            if (!FontReplacementService.MasterOnFast) return;
            _fontWatchdogTimer += Time.unscaledDeltaTime;
            if (_fontWatchdogTimer < 0.4f) return;
            _fontWatchdogTimer = 0f;
            FontReplacementService.TickNametagWatchdog();
        }

        // ── Beans ─────────────────────────────────────────────────────────────

        public static void PushBean(GameObject bean)
        {
            if (Instance == null || bean == null) return;
            Instance.HandleBean(bean);
        }

        public static void PushBeans(List<GameObject> beans)
        {
            if (Instance == null || beans == null) return;
            foreach (var b in beans)
                Instance.HandleBean(b);
        }

        public static List<GameObject> GetTrackedBeans()
        {
            if (Instance == null) return new List<GameObject>();
            var result = new List<GameObject>();
            foreach (var kvp in Instance.loggedBeans)
                if (kvp.Value != null) result.Add(kvp.Value);
            return result;
        }

        private void HandleBean(GameObject bean)
        {
            if (bean == null) return;

            if (skinApplicationService == null)
                skinApplicationService = SkinApplicationService.Instance;

            skinApplicationService?.OnBeansFound(new List<GameObject> { bean });

            int id = bean.GetInstanceID();
            if (loggedBeans.ContainsKey(id)) return;
            loggedBeans[id] = bean;
            Plugin.Log.LogInfo($"new bean, {bean.name}");
        }

        public static GameObject CheckLevelEditorBean()
        {
            var obj = LevelEditorManager.Instance._playerGameObject;
            if (obj != null && HasCharacterGEO(obj))
            {
                LocalPlayerBean = obj;
                PushBean(obj);
            }

            return obj;
        }

        private static bool HasCharacterGEO(GameObject obj)
        {
            if (obj == null) return false;
            return obj.transform.Find("Character/GEO") != null || obj.transform.Find("GEO") != null;
        }

        // ── Lobby remote beans ──────────────────────────────────────────────────
        //
        // completely separate from the local PushBean/loggedBeans path above — those run
        // OnBeansFound which slaps YOUR local skins on. lobby party members are other people,
        // so they ride their own list and only ever get a matched .bfgprofile look applied to
        // them (by LobbyProfileService). nothing here touches the local pipeline.

        // keyed by holder instance id -> the PB_UI_Character bean GameObject
        private Dictionary<int, GameObject> remoteLobbyBeans = new Dictionary<int, GameObject>();

        public static void PushRemoteLobbyBean(GameObject bean)
        {
            if (Instance == null || bean == null) return;
            int id = bean.GetInstanceID();
            if (Instance.remoteLobbyBeans.ContainsKey(id)) return;
            Instance.remoteLobbyBeans[id] = bean;
            Plugin.Log.LogInfo($"BeanMonitor: remote lobby bean: {bean.name}");
        }

        public static List<GameObject> GetRemoteLobbyBeans()
        {
            if (Instance == null) return new List<GameObject>();
            var result = new List<GameObject>();
            var toRemove = new List<int>();
            foreach (var kvp in Instance.remoteLobbyBeans)
            {
                if (kvp.Value == null) { toRemove.Add(kvp.Key); continue; }
                result.Add(kvp.Value);
            }
            foreach (var k in toRemove) Instance.remoteLobbyBeans.Remove(k);
            return result;
        }

        public static void ClearRemoteLobbyBeans()
        {
            if (Instance == null) return;
            Instance.remoteLobbyBeans.Clear();
        }

        // ── Plinths ───────────────────────────────────────────────────────────

        private const string PLINTH_MESH_NAME = "ENV_Plinth_MO";
        private const string VICTORY_PLINTH_HOLDER = "----------------ENVIRONMENT/AnimationSpawn/WinnersScreen_Prop_PenguinGuy_GRP_Prefab(Clone)/Prop_GRP/FG_vic_scene:ENV_Plinth_MO_";
        private const string REWARD_PLINTH_HOLDER = "3D Assets/Environment/CharacterAndPlinthHolder_RightSide/ENV_Plinth_MO";

        public static void PushVictoryPlinth() =>
            PushPlinthByPath(VICTORY_PLINTH_HOLDER, PLINTH_MESH_NAME, PlinthType.Victory);

        public static void PushRewardPlinth() =>
            PushPlinthByPath(REWARD_PLINTH_HOLDER, PLINTH_MESH_NAME, PlinthType.Reward);

        // polls until the victory plinth GO exists, then registers it
        public static IEnumerator PollAndPushVictoryPlinth()
        {
            float elapsed = 0f;
            while (elapsed < 10f)
            {
                if (GameObject.Find(VICTORY_PLINTH_HOLDER) != null)
                {
                    PushVictoryPlinth();
                    yield break;
                }
                yield return new WaitForSeconds(0.25f);
                elapsed += 0.25f;
            }
            Plugin.Log.LogWarning("PlinthMonitor: timed out waiting for victory plinth");
        }

        // polls until the reward plinth GO exists, then registers it
        public static IEnumerator PollAndPushRewardPlinth()
        {
            float elapsed = 0f;
            bool first = true;
            while (elapsed < 10f)
            {
                if (GameObject.Find(REWARD_PLINTH_HOLDER) != null)
                {
                    PushRewardPlinth();
                    yield break;
                }
                if (first) { yield return null; first = false; }
            }
            Plugin.Log.LogWarning("PlinthMonitor: timed out waiting for reward plinth");
        }

        public static void PushPlinthByPath(string holderPath, string meshSubPath, PlinthType type)
        {
            if (Instance == null) return;

            var holder = GameObject.Find(holderPath);
            if (holder == null)
            {
                Plugin.Log.LogWarning($"PlinthMonitor: holder not found: {holderPath}");
                return;
            }

            var meshT = holder.transform.Find(meshSubPath);
            if (meshT == null)
            {
                Plugin.Log.LogWarning($"PlinthMonitor: mesh child '{meshSubPath}' not found under {holderPath}");
                return;
            }

            PushPlinth(holder, meshT.gameObject, type);
        }

        public static void PushPlinth(GameObject holderGO, GameObject meshGO, PlinthType type)
        {
            if (Instance == null || holderGO == null || meshGO == null) return;

            int id = holderGO.GetInstanceID();
            if (Instance.trackedPlinths.ContainsKey(id))
            {
                Plugin.Log.LogInfo($"PlinthMonitor: already tracking plinth {type} ({holderGO.name})");
                return;
            }

            var slot = new PlinthSlot { holderGO = holderGO, meshGO = meshGO, type = type };
            Instance.trackedPlinths[id] = slot;
            Plugin.Log.LogInfo($"PlinthMonitor: registered plinth slot {type}");

            var app = MenuCustomizationApplication.Instance;
            if (app != null && app.HasPlinthApplied)
                app.ApplyToPlinthSlot(slot);
        }

        public static List<PlinthSlot> GetTrackedPlinths()
        {
            if (Instance == null) return new List<PlinthSlot>();
            var result = new List<PlinthSlot>();
            foreach (var kvp in Instance.trackedPlinths)
            {
                // prune destroyed GOs on the fly
                if (kvp.Value?.holderGO == null || kvp.Value.meshGO == null) continue;
                result.Add(kvp.Value);
            }
            return result;
        }

        public static void ClearDestroyedPlinths()
        {
            if (Instance == null) return;
            var toRemove = new List<int>();
            foreach (var kvp in Instance.trackedPlinths)
                if (kvp.Value?.holderGO == null || kvp.Value.meshGO == null)
                    toRemove.Add(kvp.Key);
            foreach (var k in toRemove)
                Instance.trackedPlinths.Remove(k);
        }
    }
}
