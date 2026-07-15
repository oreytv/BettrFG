using System;
using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Features.UnityRound.Editor;
using BetterFG.Services;
using FGClient;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BetterFG.Features.UnityRound
{
    // Round description pattern: owner/repo/round_xxx
    // Fetches: https://raw.githubusercontent.com/owner/repo/main/Rounds/round_xxx/info.json
    // Then the bundle from the same folder.
    //
    // Two-phase loading:
    //   Phase 1 (early, triggered by LoadViaShareCodeAndVersion patch):
    //     Download info.json + bundle, load AssetBundle, load prefab + skybox -> store pending.
    //   Phase 2 (triggered by NotifyLoadingFinished patch, scene is ready):
    //     Instantiate prefab into fraggle scene, then hand off to BetterFGRoundPostmodifier.

    public class BetterFGUnityRounds : MonoBehaviour
    {
        public static BetterFGUnityRounds Instance { get; private set; }

        public static GameObject ActiveRound { get; private set; }
        public static MapInfo ActiveMapInfo { get; private set; }
        public static Transform[] ActiveSpawnpoints { get; private set; }
        public static GameObject[] ActiveEndgoals { get; internal set; }
        public static Vector3? CcccPosition { get; internal set; }
        public static Transform CcccTransform { get; internal set; }

        private static readonly Regex DESC_PATTERN = new Regex(
            @"^([a-zA-Z0-9_\-]+)/([a-zA-Z0-9_\-]+)/(round_[a-zA-Z0-9_\-]+)$",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private AssetBundle _loadedBundle;
        private bool _loadedBundleOwned;
        private static GameObject _pendingPrefab;
        private static Material _pendingSkybox;
        private static string _pendingTextureJson;
        private static string _pendingTextureBaseUrl;
        private static byte[] _pendingMusicBank;
        private static byte[] _pendingMusicStringsBank;
        private static string _roundLoadKey;
        private static bool _roundLoadBusy;
        private static int _roundLoadTicket;
        private static bool _sceneReadyForCustomRound;
        private static FMOD.Studio.Bank _loadedMusicBank;
        private static FMOD.Studio.Bank _loadedMusicStringsBank;
        private static string _originalMusicBankName;
        private static string _originalMusicEventName;
        private static FMOD.Studio.EventInstance _customMusicInstance;
        private static bool _savedRenderSettings;
        private static AmbientMode _oldAmbientMode;
        private static Color _oldAmbientLight;
        private static bool _oldFog;
        private static float _oldFogDensity;
        private static Color _oldFogColor;
        private static Material _oldSkybox;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update() => RoundMusicService.TickVolume();

        public static bool TryHandleDescription(string desc)
        {
            if (string.IsNullOrEmpty(desc)) return false;

            // if the description is one we wrote from the editor (LRM + <color> + "BettrFG level "),
            // pull the bare owner/repo/round_xxx code out of it before matching.
            string extracted = Editor.UnityRoundLoader.ExtractShareCode(desc);
            if (!string.IsNullOrEmpty(extracted)) desc = extracted;

            var m = DESC_PATTERN.Match(desc.Trim());
            if (!m.Success) return false;

            if (Instance == null)
            {
                Plugin.Log.LogError("BetterFGUnityRounds: Instance is null, not spawned");
                return false;
            }

            string key = $"{m.Groups[1].Value}/{m.Groups[2].Value}/{m.Groups[3].Value}";
            if (string.Equals(_roundLoadKey, key, StringComparison.OrdinalIgnoreCase))
            {
                if (_roundLoadBusy)
                {
                    Plugin.Log.LogInfo($"BetterFGUnityRounds: already loading {key}, skipping duplicate");
                    return true;
                }

                if (_pendingPrefab != null || ActiveRound != null)
                {
                    Plugin.Log.LogInfo($"BetterFGUnityRounds: already have {key}, skipping duplicate");
                    InstantiateQueuedRound();
                    return true;
                }
            }

            int ticket = ++_roundLoadTicket;
            _roundLoadKey = key;
            _roundLoadBusy = true;

            Instance.StartCoroutine(Instance.DownloadAndCacheRound(
                m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, ticket).WrapToIl2Cpp());
            return true;
        }

        public static void ResetRoundState(bool unloadBundle)
        {
            if (ActiveRound != null)
                Destroy(ActiveRound);

            ActiveRound = null;
            Patches.BettrFGRounds.UnityRoundAbortHooks.Remove();
            ActiveMapInfo = null;
            ActiveSpawnpoints = null;
            ActiveEndgoals = null;
            CcccPosition = null;
            CcccTransform = null;
            _pendingPrefab = null;
            _pendingSkybox = null;
            _pendingTextureJson = null;
            _pendingTextureBaseUrl = null;
            _pendingMusicBank = null;
            _pendingMusicStringsBank = null;
            _sceneReadyForCustomRound = false;
            ObstacleTextureLoader.RevertAll();
            ObstacleTextureLoader.ClearRuntime();
            RoundMusicService.Stop();
            RoundMusicService.ClearPending();

            if (unloadBundle && Instance != null && Instance._loadedBundle != null)
            {
                if (Instance._loadedBundleOwned)
                    Instance._loadedBundle.Unload(true);
                Instance._loadedBundle = null;
                Instance._loadedBundleOwned = false;
            }

            if (unloadBundle)
            {
                _roundLoadBusy = false;
                _roundLoadKey = null;
                _roundLoadTicket++;
            }
        }

        public static void RestoreEnvironment()
        {
            if (!_savedRenderSettings) return;
            RenderSettings.ambientMode = _oldAmbientMode;
            RenderSettings.ambientLight = _oldAmbientLight;
            RenderSettings.fog = _oldFog;
            RenderSettings.fogDensity = _oldFogDensity;
            RenderSettings.fogColor = _oldFogColor;
            RenderSettings.skybox = _oldSkybox;
            _savedRenderSettings = false;
            Plugin.Log.LogInfo("BetterFGUnityRounds: environment restored");
        }

        public static void SaveEnvironmentIfNeeded()
        {
            if (_savedRenderSettings) return;
            _oldAmbientMode = RenderSettings.ambientMode;
            _oldAmbientLight = RenderSettings.ambientLight;
            _oldFog = RenderSettings.fog;
            _oldFogDensity = RenderSettings.fogDensity;
            _oldFogColor = RenderSettings.fogColor;
            _oldSkybox = RenderSettings.skybox;
            _savedRenderSettings = true;
        }

        // spawnpoint teleport helpers commented out; checkpoints come from the level editor now.
        /*
        public static Vector3? GetRandomSpawnpointPos()
        {
            if (ActiveSpawnpoints == null || ActiveSpawnpoints.Length == 0) return null;
            return ActiveSpawnpoints[UnityEngine.Random.Range(0, ActiveSpawnpoints.Length)].position;
        }

        public static void TeleportPlayerToRandomSpawn(FallGuysCharacterController fgcc)
        {
            if (fgcc == null) return;
            var pos = GetRandomSpawnpointPos();
            if (pos == null) { Plugin.Log.LogWarning("BetterFGUnityRounds: TeleportPlayerToRandomSpawn: no spawnpoints"); return; }
            var teleport = fgcc.TeleportMotorFunction;
            if (teleport == null) { Plugin.Log.LogWarning("BetterFGUnityRounds: TeleportPlayerToRandomSpawn: no TeleportMotorFunction"); return; }
            teleport.TeleportPosition = pos.Value;
            Plugin.Log.LogInfo($"BetterFGUnityRounds: teleporting to {pos.Value}");
        }

        public static bool TeleportBeanToSpawn(GameObject bean, string reason = "spawn")
        {
            if (bean == null) return false;
            if (ActiveSpawnpoints == null || ActiveSpawnpoints.Length == 0) return false;

            string episodeGuid = "";
            try { episodeGuid = GlobalGameStateClient.Instance?.CachedEpisodeGuid ?? ""; } catch { }

            int idx = StableHash(bean.name + episodeGuid) % ActiveSpawnpoints.Length;
            if (idx < 0) idx += ActiveSpawnpoints.Length;

            var target = ActiveSpawnpoints[idx];
            if (target == null) return false;

            bean.transform.position = target.position;
            bean.transform.rotation = target.rotation;

            var rb = bean.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            Plugin.Log.LogInfo($"BetterFGUnityRounds: {reason}: '{bean.name}' -> spawnpoint {idx}/{ActiveSpawnpoints.Length} ({target.name})");
            return true;
        }

        static IEnumerator TeleportLocalBeanWhenReady()
        {
            for (int i = 0; i < 80; i++)
            {
                var bean = BeanMonitorService.LocalPlayerBean;
                if (TeleportBeanToSpawn(bean, "late spawn"))
                    yield break;
                yield return null;
            }
            Plugin.Log.LogWarning("BetterFGUnityRounds: late spawn: no local bean/spawnpoints");
        }

        static int StableHash(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            unchecked { int h = 17; foreach (char c in s) h = h * 31 + c; return h; }
        }
        */

        // Phase 2 -- scene is ready, instantiate then postmodify.
        public static void MarkSceneReadyAndInstantiateQueuedRound()
        {
            _sceneReadyForCustomRound = true;
            InstantiateQueuedRound();
        }

        public static void InstantiateQueuedRound()
        {
            if (!_sceneReadyForCustomRound) return;
            if (_pendingPrefab == null) return;

            Scene targetScene = default;
            int sceneCount = SceneManager.sceneCount;
            for (int i = 0; i < sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && (s.name.Contains("Fraggle") || s.name.Contains("FallGuy_") || s.name.Contains("ugc")))
                {
                    targetScene = s;
                    break;
                }
            }

            if (!targetScene.IsValid())
            {
                Plugin.Log.LogError("BetterFGUnityRounds: no fraggle scene found, aborting");
                return;
            }

            if (ActiveRound != null)
            {
                Destroy(ActiveRound);
                ActiveRound = null;
            }

            var instance = Instantiate(_pendingPrefab);
            instance.name = _pendingPrefab.name;
            instance.transform.position = Vector3.zero;
            SceneManager.MoveGameObjectToScene(instance, targetScene);

            ActiveRound = instance;
            Patches.BettrFGRounds.UnityRoundAbortHooks.Install();
            _pendingPrefab = null;

            Plugin.Log.LogInfo($"BetterFGUnityRounds: instantiated '{instance.name}' into '{targetScene.name}'");

            BetterFGRoundPostmodifier.Apply(ActiveRound, ActiveMapInfo, ref _pendingSkybox);
            if (!string.IsNullOrEmpty(_pendingTextureJson) && Instance != null)
                Instance.StartCoroutine(Instance.ApplyRemoteObstacleTextures(_pendingTextureBaseUrl, _pendingTextureJson, _roundLoadTicket).WrapToIl2Cpp());

            // spawnpoint registration + bean teleport commented out; checkpoints come from the level editor now.
            /*
            var spawnRoot = ActiveRound.transform.Find("spawnpoints");
            if (spawnRoot != null && spawnRoot.childCount > 0)
            {
                ActiveSpawnpoints = new Transform[spawnRoot.childCount];
                for (int i = 0; i < spawnRoot.childCount; i++)
                    ActiveSpawnpoints[i] = spawnRoot.GetChild(i);
            }
            else ActiveSpawnpoints = null;

            if (Instance != null)
                Instance.StartCoroutine(TeleportLocalBeanWhenReady().WrapToIl2Cpp());
            */
        }

        private static void Notify(string text, Color color)
        {
            Plugin.Log.LogInfo($"BetterFGUnityRounds: {text}");
            try { BetterFG.UI.BetterFGNotif.CreateNotification(text, color); } catch { }
        }

        // the round-selected loading screen shows the raw level description (our wrapped share code).
        // swap it for the loaded map's real description from info.json. the prefab clone name varies,
        // so we go LoadingScreen -> child 0 -> SafeArea/InfoGroup/Description_Text.
        public static void ShowRoundDescriptionOnLoadingScreen()
        {
            if (Instance != null)
                Instance.StartCoroutine(SwapLoadingScreenDescription(_roundLoadTicket).WrapToIl2Cpp());
        }

        // the description we want to show, or null if we're not loading one of our rounds.
        private static string CurrentRoundDescriptionText()
        {
            if (ActiveMapInfo == null) return null;
            if (!string.IsNullOrEmpty(ActiveMapInfo.description)) return ActiveMapInfo.description;
            if (!string.IsNullOrEmpty(ActiveMapInfo.displayName)) return ActiveMapInfo.displayName;
            return null;
        }

        // set the loading-screen description text right now (no polling). called by the patches on
        // the loading VM's InitTexts/SetData so our text re-applies after the game rewrites it.
        public static void ApplyDescriptionNow()
        {
            string text = CurrentRoundDescriptionText();
            if (text == null) return;
            try
            {
                var loading = GameObject.Find("UICanvas_Client_V2(Clone)/LoadingScreen");
                if (loading == null || loading.transform.childCount == 0) return;
                var descT = loading.transform.GetChild(0).Find("SafeArea/InfoGroup/Description_Text");
                var tmp = descT != null ? descT.GetComponent<TMPro.TMP_Text>() : null;
                if (tmp != null) tmp.text = text;
            }
            catch { }
        }

        private static IEnumerator SwapLoadingScreenDescription(int ticket)
        {
            float elapsed = 0f;
            while (elapsed < 12f)
            {
                if (ticket != _roundLoadTicket) yield break;
                if (CurrentRoundDescriptionText() == null) yield break;

                var loading = GameObject.Find("UICanvas_Client_V2(Clone)/LoadingScreen");
                if (loading != null && loading.transform.childCount > 0)
                {
                    var descT = loading.transform.GetChild(0).Find("SafeArea/InfoGroup/Description_Text");
                    var tmp = descT != null ? descT.GetComponent<TMPro.TMP_Text>() : null;
                    if (tmp != null)
                    {
                        tmp.text = CurrentRoundDescriptionText();
                        yield break;
                    }
                }

                yield return new WaitForSeconds(0.15f);
                elapsed += 0.15f;
            }
        }


        // gameplay only: if the round shipped a custom song, wait out the intro then play it (NAudio)
        // and pause the game's FMOD music.
        public static void StartCustomMusicIfAny()
        {
            if (!RoundMusicService.HasPending) return;
            if (Instance != null)
                Instance.StartCoroutine(StartCustomMusicAfterIntro().WrapToIl2Cpp());
        }

        private static IEnumerator StartCustomMusicAfterIntro()
        {
            yield return new WaitForSeconds(5f);
            RoundMusicService.StartIfPending();

            for (int i = 0; i < 40; i++)
            {
                if (RoundMusicService.TryPauseGameMusic()) yield break;
                yield return new WaitForSeconds(0.25f);
            }
        }

        public static void RestoreMusic()
        {
            if (_customMusicInstance.isValid())
            {
                _customMusicInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                _customMusicInstance = default;
            }

            if (_loadedMusicStringsBank.isValid()) { _loadedMusicStringsBank.unload(); _loadedMusicStringsBank = default; }
            if (_loadedMusicBank.isValid()) { _loadedMusicBank.unload(); _loadedMusicBank = default; }

            if (string.IsNullOrEmpty(_originalMusicEventName)) return;

            try
            {
                var gs = GlobalGameStateClient.Instance?._gameStateMachine?.CurrentState;
                var cgm = gs.TryCast<FGClient.StateGameInProgress>()?._clientGameManager;
                if (cgm != null)
                {
                    cgm._musicSoundBankName = _originalMusicBankName;
                    cgm._musicEventName = _originalMusicEventName;
                    if (cgm._musicInstance != null)
                        cgm._musicInstance._eventName = _originalMusicEventName;
                }
            }
            catch { }

            _originalMusicBankName = null;
            _originalMusicEventName = null;
            Plugin.Log.LogInfo("BetterFGUnityRounds: music restored");
        }

        // Phase 1 -- download, cache prefab + skybox. Never touches scene objects.
        private IEnumerator DownloadAndCacheRound(string owner, string repo, string round, int ticket)
        {
            ResetRoundState(unloadBundle: true);
            _roundLoadTicket = ticket;
            _roundLoadKey = $"{owner}/{repo}/{round}";
            _roundLoadBusy = true;
            SaveEnvironmentIfNeeded();

            string rawBase = $"https://raw.githubusercontent.com/{owner}/{repo}/main/Rounds/{round}";
            string infoUrl = $"{rawBase}/info.json";

            Plugin.Log.LogInfo($"BetterFGUnityRounds: fetching {infoUrl}");

            var infoReq = UnityWebRequest.Get(infoUrl);
            yield return infoReq.SendWebRequest();
            if (ticket != _roundLoadTicket) { infoReq.Dispose(); yield break; }

            if (infoReq.result != UnityWebRequest.Result.Success)
            {
                Plugin.Log.LogError($"BetterFGUnityRounds: info.json fetch failed: {infoReq.error}");
                infoReq.Dispose();
                _roundLoadBusy = false;
                yield break;
            }

            var info = ParseInfo(infoReq.downloadHandler.text);
            infoReq.Dispose();

            if (info == null || string.IsNullOrEmpty(info.file) || string.IsNullOrEmpty(info.prefab))
            {
                Plugin.Log.LogError("BetterFGUnityRounds: invalid info.json");
                _roundLoadBusy = false;
                yield break;
            }

            ActiveMapInfo = info;
            ShowRoundDescriptionOnLoadingScreen();
            _pendingTextureBaseUrl = rawBase;
            yield return DownloadTextureJson(rawBase, ticket).WrapToIl2Cpp();
            if (ticket != _roundLoadTicket) yield break;

            yield return DownloadMusic(rawBase, info.music, ticket).WrapToIl2Cpp();
            if (ticket != _roundLoadTicket) yield break;

            var alreadyLoadedBundle = FindLoadedBundleWithAsset(info.prefab);
            if (alreadyLoadedBundle != null)
            {
                Plugin.Log.LogInfo($"BetterFGUnityRounds: reusing already loaded bundle for '{info.prefab}'");
                yield return CacheRoundFromBundle(alreadyLoadedBundle, info, ticket, owned: false).WrapToIl2Cpp();
                yield break;
            }

            string bundleUrl = $"{rawBase}/{info.file}";
            Plugin.Log.LogInfo($"BetterFGUnityRounds: downloading bundle {bundleUrl}");

            var bundleReq = UnityWebRequest.Get(bundleUrl);
            yield return bundleReq.SendWebRequest();
            if (ticket != _roundLoadTicket) { bundleReq.Dispose(); yield break; }

            if (bundleReq.result != UnityWebRequest.Result.Success)
            {
                Plugin.Log.LogError($"BetterFGUnityRounds: bundle fetch failed: {bundleReq.error}");
                bundleReq.Dispose();
                _roundLoadBusy = false;
                yield break;
            }

            byte[] bytes = bundleReq.downloadHandler.data;
            bundleReq.Dispose();

            AssetBundle bundle = null;
            Exception loadEx = null;
            try { bundle = AssetBundle.LoadFromMemory(bytes); }
            catch (Exception ex)
            {
                loadEx = ex;
            }

            if (loadEx != null)
            {
                Plugin.Log.LogError($"BetterFGUnityRounds: LoadFromMemory failed: {loadEx.Message}");
                bundle = FindLoadedBundleWithAsset(info.prefab);
                if (bundle != null)
                {
                    Plugin.Log.LogInfo($"BetterFGUnityRounds: LoadFromMemory failed but prefab is already loaded, reusing '{info.prefab}'");
                    yield return CacheRoundFromBundle(bundle, info, ticket, owned: false).WrapToIl2Cpp();
                    yield break;
                }
                _roundLoadBusy = false;
                yield break;
            }

            if (bundle == null)
            {
                bundle = FindLoadedBundleWithAsset(info.prefab);
                if (bundle != null)
                {
                    Plugin.Log.LogInfo($"BetterFGUnityRounds: AssetBundle was null but prefab is already loaded, reusing '{info.prefab}'");
                    yield return CacheRoundFromBundle(bundle, info, ticket, owned: false).WrapToIl2Cpp();
                    yield break;
                }

                Plugin.Log.LogError("BetterFGUnityRounds: AssetBundle is null after load");
                _roundLoadBusy = false;
                yield break;
            }
            if (ticket != _roundLoadTicket) { bundle.Unload(true); yield break; }

            yield return CacheRoundFromBundle(bundle, info, ticket, owned: true).WrapToIl2Cpp();
        }

        private IEnumerator DownloadMusic(string rawBase, string musicFile, int ticket)
        {
            RoundMusicService.ClearPending();
            if (string.IsNullOrEmpty(musicFile)) yield break;

            string path = musicFile.Replace("\\", "/").TrimStart('/');
            string url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : $"{rawBase}/{path}";

            var req = UnityWebRequest.Get(url);
            yield return req.SendWebRequest();
            if (ticket != _roundLoadTicket) { req.Dispose(); yield break; }

            if (req.result == UnityWebRequest.Result.Success)
            {
                RoundMusicService.SetPending(req.downloadHandler.data, path);
                Plugin.Log.LogInfo($"BetterFGUnityRounds: round music '{musicFile}' ready");
            }
            else
            {
                Plugin.Log.LogInfo($"BetterFGUnityRounds: no round music ({req.responseCode})");
            }
            req.Dispose();
        }

        private IEnumerator DownloadTextureJson(string rawBase, int ticket)
        {
            _pendingTextureJson = null;

            var req = UnityWebRequest.Get($"{rawBase}/texture.json");
            yield return req.SendWebRequest();
            if (ticket != _roundLoadTicket) { req.Dispose(); yield break; }

            if (req.result == UnityWebRequest.Result.Success)
            {
                _pendingTextureJson = req.downloadHandler.text;
                Plugin.Log.LogInfo("BetterFGUnityRounds: texture.json ready");
            }
            else
            {
                Plugin.Log.LogInfo($"BetterFGUnityRounds: no texture.json for round ({req.responseCode})");
            }

            req.Dispose();
        }

        private IEnumerator ApplyRemoteObstacleTextures(string rawBase, string textureJson, int ticket)
        {
            foreach (var pair in ObstacleTextureLoader.ParseTextureJson(textureJson))
            {
                if (ticket != _roundLoadTicket) yield break;
                if (string.IsNullOrEmpty(pair.Key) || string.IsNullOrEmpty(pair.Value)) continue;

                string path = pair.Value.Replace("\\", "/").TrimStart('/');
                string url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : $"{rawBase}/{path}";

                var req = UnityWebRequest.Get(url);
                yield return req.SendWebRequest();
                if (ticket != _roundLoadTicket) { req.Dispose(); yield break; }

                if (req.result == UnityWebRequest.Result.Success)
                {
                    if (ObstacleTextureLoader.SetOverrideBytes(pair.Key, req.downloadHandler.data, url, out string error))
                        Plugin.Log.LogInfo($"BetterFGUnityRounds: applied texture {pair.Key}");
                    else
                        Plugin.Log.LogWarning($"BetterFGUnityRounds: texture {pair.Key}: {error}");
                }
                else
                {
                    Plugin.Log.LogWarning($"BetterFGUnityRounds: texture fetch failed {url}: {req.error}");
                }

                req.Dispose();
            }
        }

        private IEnumerator CacheRoundFromBundle(AssetBundle bundle, MapInfo info, int ticket, bool owned)
        {
            if (bundle == null || info == null) yield break;

            _loadedBundle = bundle;
            _loadedBundleOwned = owned;

            var prefabReq = bundle.LoadAssetAsync(info.prefab);
            yield return prefabReq;
            if (ticket != _roundLoadTicket) yield break;

            if (prefabReq.asset == null)
            {
                Plugin.Log.LogError($"BetterFGUnityRounds: prefab '{info.prefab}' not found in bundle");
                _roundLoadBusy = false;
                yield break;
            }

            GameObject prefab;
            try { prefab = prefabReq.asset.Cast<GameObject>(); }
            catch (Exception ex) { Plugin.Log.LogError($"BetterFGUnityRounds: prefab cast failed: {ex.Message}"); _roundLoadBusy = false; yield break; }

            if (prefab == null) { Plugin.Log.LogError("BetterFGUnityRounds: prefab is null after cast"); _roundLoadBusy = false; yield break; }

            _pendingPrefab = prefab;

            if (!string.IsNullOrEmpty(info.skybox))
            {
                var skyReq = bundle.LoadAssetAsync(info.skybox);
                yield return skyReq;
                if (ticket != _roundLoadTicket) yield break;

                if (skyReq.asset != null)
                {
                    try
                    {
                        _pendingSkybox = skyReq.asset.Cast<Material>();
                        Plugin.Log.LogInfo($"BetterFGUnityRounds: skybox '{info.skybox}' loaded");
                    }
                    catch (Exception ex) { Plugin.Log.LogWarning($"BetterFGUnityRounds: skybox cast failed: {ex.Message}"); }
                }
                else
                {
                    Plugin.Log.LogWarning($"BetterFGUnityRounds: skybox '{info.skybox}' not found in bundle");
                }
            }

            Plugin.Log.LogInfo($"BetterFGUnityRounds: bundle ready, waiting for scene -- prefab '{prefab.name}'");
            _roundLoadBusy = false;
            InstantiateQueuedRound();
        }

        internal static AssetBundle FindLoadedBundleWithAsset(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) return null;

            string loose = assetName.ToLowerInvariant();
            try
            {
                var bundles = Resources.FindObjectsOfTypeAll<AssetBundle>();
                foreach (var bundle in bundles)
                {
                    if (bundle == null) continue;
                    string[] names;
                    try { names = bundle.GetAllAssetNames(); }
                    catch { continue; }

                    foreach (string name in names)
                    {
                        if (string.IsNullOrEmpty(name)) continue;
                        string n = name.ToLowerInvariant();
                        if (n == loose || n.EndsWith("/" + loose) || n.EndsWith("/" + loose + ".prefab"))
                            return bundle;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"BetterFGUnityRounds: loaded bundle scan failed: {ex.Message}");
            }

            return null;
        }

        public static MapInfo ParseInfo(string json)
        {
            try
            {
                return new MapInfo
                {
                    displayName = ReadStr(json, "displayName"),
                    description = ReadStr(json, "description"),
                    file = ReadStr(json, "file"),
                    prefab = ReadStr(json, "prefab"),
                    skybox = ReadStr(json, "skybox"),
                    music = ReadStr(json, "music"),
                    musicBank = ReadStr(json, "musicBank"),
                    musicEvent = ReadStr(json, "musicEvent"),
                    musicStringsBank = ReadStr(json, "musicStringsBank"),
                    ambientMode = (AmbientMode)ReadInt(json, "ambientMode", (int)AmbientMode.Flat),
                    ambientLight = ReadColor(json, "ambientLight", new Color(0.2f, 0.2f, 0.2f)),
                    reflectionIntensity = ReadFloat(json, "reflectionIntensity", 1f),
                    fog = ReadBool(json, "fog", false),
                    fogDensity = ReadFloat(json, "fogDensity", 0.01f),
                    fogColor = ReadColor(json, "fogColor", new Color(0.5f, 0.5f, 0.5f)),
                    keepExistingObjects = ReadBool(json, "keepExistingObjects", false),
                };
            }
            catch { return null; }
        }

        private static string ReadStr(string json, string key)
        {
            string search = "\"" + key + "\"";
            int ki = json.IndexOf(search);
            if (ki < 0) return "";
            int colon = json.IndexOf(':', ki + search.Length);
            if (colon < 0) return "";
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 < 0) return "";
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return "";
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }

        private static float ReadFloat(string json, string key, float def = 0f)
        {
            string s = ReadStr(json, key);
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : def;
        }

        private static int ReadInt(string json, string key, int def = 0)
        {
            string s = ReadStr(json, key);
            return int.TryParse(s, out int v) ? v : def;
        }

        private static bool ReadBool(string json, string key, bool def = false)
        {
            string s = ReadStr(json, key).ToLower();
            if (s == "true") return true;
            if (s == "false") return false;
            return def;
        }

        // Colors stored as "R G B A" space-separated invariant floats.
        private static Color ReadColor(string json, string key, Color def)
        {
            string s = ReadStr(json, key);
            if (string.IsNullOrEmpty(s)) return def;
            var p = s.Split(' ');
            if (p.Length < 3) return def;
            float r = float.Parse(p[0], CultureInfo.InvariantCulture);
            float g = float.Parse(p[1], CultureInfo.InvariantCulture);
            float b = float.Parse(p[2], CultureInfo.InvariantCulture);
            float a = p.Length > 3 ? float.Parse(p[3], CultureInfo.InvariantCulture) : 1f;
            return new Color(r, g, b, a);
        }
    }

    public class MapInfo
    {
        public string displayName;
        public string description;
        public string file;
        public string prefab;
        public string skybox;
        public string music;
        public string musicBank;
        public string musicEvent;
        public string musicStringsBank;

        public AmbientMode ambientMode = AmbientMode.Flat;
        public Color ambientLight = new Color(0.2f, 0.2f, 0.2f);
        public float reflectionIntensity = 1f;
        public bool fog;
        public float fogDensity = 0.01f;
        public Color fogColor = new Color(0.5f, 0.5f, 0.5f);

        // keep all placeable objects (don't disable) except the background CutoutSphere
        public bool keepExistingObjects;
    }
}
