using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BetterFG.Customization.Player;
using BetterFG.Utilities;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FG.Common;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;
using BetterFG.Nametag;
using FGClient;
using BetterFG.Services;
using BetterFG.UI.Tab;

namespace BetterFG.Network
{
    public class remoteNametagInfo
    {
        public float r, g, b;
        public bool bold, italic;
        public string customName;
        public string iconMode;
        public string iconCountry;
        public string iconPath;
        public float iconScale;
        public float iconOffX, iconOffY;
        public string platformHide;
        public string platformCustom;
        public string nameStyle;
        public bool backingEnabled;
        public string backingPath;
        public float backingOffX, backingOffY;
        public float backingScale;
        public string nickname;
    }

    public class remoteSkinEntry
    {
        public string file;
        public string type;
        public string source;
        public string localPath;
        public string repoUrl; // raw base URL, e.g. https://raw.githubusercontent.com/oreytv/BetterFGPublicSkins/main
    }

    public class playerRemoteProfile
    {
        public uint playerID;
        public string episodeGUID;
        public float playerScale;
        public List<remoteSkinEntry> skins = new List<remoteSkinEntry>();
        public remoteNametagInfo nametag;
        public string playerKey;
        public string resolvedPlayerKey;
        // .bfgprofile profiles only apply to the bean whose clean name matches playerKey —
        // never positionally like debug_profiles entries
        public bool requireKeyMatch;
    }

    public class NetworkClient : MonoBehaviour
    {
        public NetworkClient(IntPtr ptr) : base(ptr) { }

        public static NetworkClient Instance { get; private set; }

        private static string GetSkinRepoRaw(remoteSkinEntry entry)
        {
            if (!string.IsNullOrEmpty(entry?.repoUrl))
                return entry.repoUrl.TrimEnd('/');
            return RepoRegistry.Instance?.Active?.RawBase
                   ?? "https://raw.githubusercontent.com/oreytv/BetterFGPublicSkins/main";
        }

        private List<playerRemoteProfile> _profiles = new List<playerRemoteProfile>();

        void Awake() => Instance = this;

        public void OnRoundStart()
        {
            RemoteProfileStore.Clear();
            LoadProfilesFromFile();
        }

        // lobby path: just (re)build the profile lookup maps so LobbyProfileService can match by name.
        // does NOT run the in-round apply coroutine (no round beans exist in the menu). throttled —
        // the party-menu nameplate updates call this once PER member PER graphics refresh, which
        // otherwise re-reads + re-unpacks every profile from disk many times a second.
        private static float _lastPrime = -999f;
        public static void PrimeProfilesForLobby(bool force = false)
        {
#if PROFILES
            if (!force && Time.realtimeSinceStartup - _lastPrime < 3f) return;
            _lastPrime = Time.realtimeSinceStartup;
            BetterFG.Customization.Profiles.ProfileService.GetRemoteProfiles();
#endif
        }

        public void RegisterLocalProfileFromApplication()
        {
            var app = GetApplicationService();
            if (app == null) return;

            var profile = new playerRemoteProfile();
            profile.playerScale = PlayerScaleService.GetPlayerScale();
            profile.skins = new List<remoteSkinEntry>();

            var slots = app.GetActiveSlots();
            if (slots != null)
            {
                foreach (var s in slots)
                {
                    if (s == null || s.skinInfo == null) continue;

                    var se = new remoteSkinEntry
                    {
                        file = s.skinInfo.file,
                        type = s.skinInfo.type,
                        source = "local",
                        localPath = s.skinInfo.localPath,
                        repoUrl = s.skinInfo.sourceRepo,
                    };

                    profile.skins.Add(se);
                }
            }

            try
            {
                profile.playerKey = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? "";
            }
            catch
            {
                profile.playerKey = "";
            }

            RemoteProfileStore.Register(profile, profile.playerKey);
        }

        private void LoadProfilesFromFile()
        {
            _profiles.Clear();

            string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "debug_profiles.json");
            if (File.Exists(path))
            {
                string json = null;
                try { json = File.ReadAllText(path); }
                catch (Exception ex) { Debug.LogError("[NetworkClient] read err: " + ex.Message); }

                if (json != null)
                    foreach (string entry in JsonUtil.GetRootArray(json))
                    {
                        var p = ParseProfile(entry);
                        if (p != null) _profiles.Add(p);
                    }
            }
            else Debug.Log("[NetworkClient] no debug_profiles.json");

#if PROFILES
            // saved player profiles (.bfgprofile) ride the same pipeline
            _profiles.AddRange(BetterFG.Customization.Profiles.ProfileService.GetRemoteProfiles());
#endif

            foreach (var profile in _profiles)
                RemoteProfileStore.Register(profile);

            if (_profiles.Count > 0)
                StartCoroutine(ApplyProfilesCoroutine().WrapToIl2Cpp());
        }

        private IEnumerator ApplyProfilesCoroutine()
        {
            yield return new WaitForSeconds(1f);

            var localBean = BeanMonitorService.LocalPlayerBean;
            var remotes = BeanNetworkUtil.GetRemotePlayerBeansSorted(localBean);
            if (remotes.Count == 0 && localBean == null) yield break;

            foreach (var profile in _profiles)
                RemoteProfileStore.Register(profile);

            RemoteProfileStore.ResolvePending(localBean);

            // First pass: pre-download and cache all unique skins
            var uniqueSkins = new Dictionary<string, remoteSkinEntry>();
            foreach (var profile in _profiles)
            {
                if (profile.skins != null && profile.skins.Count > 0)
                {
                    foreach (var skin in profile.skins)
                    {
                        if (!uniqueSkins.ContainsKey(skin.file))
                            uniqueSkins[skin.file] = skin;
                    }
                }
            }

            var appSvc = GetApplicationService();
            if (appSvc == null) yield break;

            // Pre-load all unique skins
            foreach (var kvp in uniqueSkins)
            {
                var skinEntry = kvp.Value;
                if (appSvc.TryGetLoadedBundle(skinEntry.file, out var existing) && existing != null)
                    continue;

                byte[] bytes = null;
                if (skinEntry.source == "local" && !string.IsNullOrEmpty(skinEntry.localPath))
                {
                    try { bytes = File.ReadAllBytes(skinEntry.localPath); }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[NetworkClient] local read failed: {ex.Message}");
                        continue;
                    }
                }
                else
                {
                    SkinType type = SkinTypeParser.FromString(skinEntry.type);
                    if (type == SkinType.Unknown) continue;

                    string category = GetCategoryFolder(type);
                    string repoRaw = GetSkinRepoRaw(skinEntry);
                    string url = $"{repoRaw}/{category}/{skinEntry.file}/{skinEntry.file}";

                    bool sizeOk = false;
                    string sizeErr = null;
                    yield return RepoRegistry.CheckBundleSize(url, (ok, err) => { sizeOk = ok; sizeErr = err; }).WrapToIl2Cpp();
                    if (!sizeOk)
                    {
                        Debug.LogWarning($"[NetworkClient] {sizeErr}");
                        continue;
                    }

                    UnityWebRequest www = UnityWebRequest.Get(url);
                    yield return www.SendWebRequest();

                    if (www.result == UnityWebRequest.Result.Success)
                        bytes = www.downloadHandler.data;
                    else
                    {
                        Debug.LogError($"[NetworkClient] dl failed {url} — {www.error}");
                        www.Dispose();
                        continue;
                    }

                    www.Dispose();
                }

                if (bytes != null)
                {
                    if (!appSvc.TryGetLoadedBundle(skinEntry.file, out var bundle))
                    {
                        var loadReq = AssetBundle.LoadFromMemoryAsync(bytes);
                        yield return loadReq;
                        bundle = loadReq.assetBundle;
                    }
                    if (bundle != null)
                        appSvc.RegisterRemoteBundle(skinEntry.file, bundle);
                }
            }

            // Second pass: apply all cached skins to beans. key-match profiles (.bfgprofile) bind
            // to the bean whose clean name matches playerKey (local bean included); debug entries
            // keep the old positional behaviour.
            for (int i = 0; i < _profiles.Count; i++)
            {
                var profile = _profiles[i];
                GameObject bean = null;

                if (profile.requireKeyMatch)
                {
                    string want = FallGuysLib.Players.PlayerUtils.CleanPlayerName(profile.playerKey ?? "");

                    string localKey = "";
                    try { localKey = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? ""; } catch { }
                    if (localBean != null && FallGuysLib.Players.PlayerUtils.CleanPlayerName(localKey).Equals(want, StringComparison.OrdinalIgnoreCase))
                        bean = localBean;

                    if (bean == null)
                        foreach (var r in remotes)
                        {
                            string k = BeanNetworkUtil.TryGetPlayerKeyForBean(r);
                            if (!string.IsNullOrEmpty(k) && FallGuysLib.Players.PlayerUtils.CleanPlayerName(k).Equals(want, StringComparison.OrdinalIgnoreCase))
                            { bean = r; break; }
                        }

                    if (bean == null) { Debug.Log($"[NetworkClient] no bean named '{want}' for profile"); continue; }
                    profile.resolvedPlayerKey = profile.playerKey;
                    Debug.Log($"[NetworkClient] profile '{want}' -> {bean.name}");
                }
                else
                {
                    if (i >= remotes.Count) continue;
                    bean = remotes[i];
                }

                if (profile.playerScale > 0f)
                    PlayerScaleService.ApplySkinScaleToBean(bean, profile.playerScale, PlayerScaleService.BeanScaleMode.Remote);

                if (profile.skins != null && profile.skins.Count > 0)
                    yield return ApplySkinsToBeanCached(profile, bean, appSvc).WrapToIl2Cpp();

#if PROFILES
                if (profile.requireKeyMatch)
                    BetterFG.Customization.Profiles.ProfileService.ApplyExtras(profile.playerKey, bean,
                        profile.nametag != null && !string.IsNullOrEmpty(profile.resolvedPlayerKey) ? profile.nametag : null);
#endif
            }
        }

        internal static IEnumerator PollAndApplyNametagForBean(GameObject bean, remoteNametagInfo info)
        {
            float elapsed = 0f;
            while (elapsed < 8f)
            {
                var display = RemoteNametagResolver.TryGetDisplayForBean(bean);
                if (display != null)
                {
                    var tmp = NametagIconApplicator.TryGetNameText(display);
                    if (tmp != null)
                    {
                        Debug.Log($"[NetworkClient] nametag apply {bean.name} elapsed={elapsed:F2}s");
                        NametagIconApplicator.ApplyRemoteToDisplay(display, tmp.text, info);

                        // nameplate backing + nickname subtext from the profile (search from the
                        // name text's parent so NameTagBacking / NameTagUserNicknameText resolve)
                        var vm = tmp.transform.parent != null ? tmp.transform.parent : tmp.transform;
                        NametagIconApplicator.ApplyBacking(vm, info.backingEnabled, info.backingPath, info.backingOffX, info.backingOffY, info.backingScale);
                        NametagIconApplicator.ApplyNickname(vm, false, !string.IsNullOrEmpty(info.nickname), info.nickname ?? "");

                        bool hide = info.platformHide == "true";
                        string customSprite = info.platformCustom ?? "";
                        if (hide || !string.IsNullOrEmpty(customSprite))
                            yield return PollAndApplyPlatformIconForBean(bean, info).WrapToIl2Cpp();
                        yield break;
                    }
                }
                yield return new WaitForSeconds(0.25f);
                elapsed += 0.25f;
            }
            Debug.LogWarning($"[NetworkClient] nametag poll timed out for {bean.name}");
        }

        private static IEnumerator PollAndApplyPlatformIconForBean(GameObject bean, remoteNametagInfo info)
        {
            var fgcc = bean.GetComponent<FallGuysCharacterController>();
            if (fgcc == null) yield break;

            bool hide = info.platformHide == "true";
            string customSprite = info.platformCustom ?? "";

            float elapsed = 0f;
            while (elapsed < 5f)
            {
                var huds = UnityEngine.Object.FindObjectsOfType<PlayerInfoHUDBase>(true);
                if (huds != null)
                {
                    bool found = false;
                    for (int h = 0; h < huds.Length; h++)
                    {
                        var spawned = huds[h]?._spawnedInfoObjects;
                        if (spawned == null) continue;
                        for (int i = 0; i < spawned.Count; i++)
                        {
                            var row = spawned[i];
                            if (row == null || row.fgcc != fgcc) continue;
                            NametagIconApplicator.ApplyPlatformIcon(row.playerInfo?.gameObject, hide, customSprite);
                            found = true;
                            break;
                        }
                        if (found) yield break;
                    }
                }
                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }
            Debug.LogWarning($"[NetworkClient] timed out for '{bean.name}'");
        }

        private IEnumerator ApplySkinsToBeanCached(playerRemoteProfile profile, GameObject bean, SkinApplicationService appSvc)
        {
            foreach (var skinEntry in profile.skins)
            {
                SkinType type = SkinTypeParser.FromString(skinEntry.type);
                if (type == SkinType.Unknown) continue;

                // Bundle should already be loaded from pre-download phase
                if (!appSvc.TryGetLoadedBundle(skinEntry.file, out var bundle) || bundle == null)
                    continue;

                float skinScale = 0f;
                bool keepBase = false;
                string infoJson = null;
                string repoRaw = GetSkinRepoRaw(skinEntry);
                string category = GetCategoryFolder(type);
                string infoUrl = $"{repoRaw}/{category}/{skinEntry.file}/info.json";

                UnityWebRequest infoWww = UnityWebRequest.Get(infoUrl);
                yield return infoWww.SendWebRequest();

                if (infoWww.result == UnityWebRequest.Result.Success)
                {
                    infoJson = infoWww.downloadHandler.text;
                    skinScale = JsonUtil.GetFloat(infoJson, "skinScale", 0f);
                    // without this the remote path treated every UGC costume as full-body and hid the
                    // bean under it — an overlay/suit skin (keepBase=true) has nothing to replace, so it
                    // just erased the wearer. local path already read this; remote silently dropped it.
                    keepBase = JsonUtil.GetBool(infoJson, "keepBase");
                }

                infoWww.Dispose();

                var skinInfo = new SkinInfo
                {
                    name = skinEntry.file,
                    file = skinEntry.file,
                    type = skinEntry.type,
                    skinScale = skinScale,
                    keepBase = keepBase,
                    sourceRepo = repoRaw,
                };

                // items need hand placement (scale + left/right pos/rot) from info.json, else they
                // spawn at the bean origin instead of on the hand
                if (type == SkinType.Item && infoJson != null)
                    SkinCatalogService.FillItemInfoFromJson(skinInfo, infoJson);

                var slot = new ActiveSkinSlot
                {
                    skinInfo = skinInfo,
                    bundle = bundle,
                    type = type
                };

                yield return appSvc.ApplySkinToBean(slot, bean).WrapToIl2Cpp();
            }
        }

        private SkinApplicationService GetApplicationService() => CustomizationServices.ApplicationService;

        private static playerRemoteProfile ParseProfile(string json)
        {
            try
            {
                var p = new playerRemoteProfile
                {
                    playerID = (uint)JsonUtil.GetInt(json, "playerID"),
                    episodeGUID = JsonUtil.GetValue(json, "episodeGUID"),
                    playerKey = JsonUtil.GetValue(json, "playerKey"),
                    playerScale = JsonUtil.GetFloat(json, "playerScale", 1f),
                };

                var skinsArr = JsonUtil.GetArray(json, "skins");
                foreach (string s in skinsArr)
                {
                    p.skins.Add(new remoteSkinEntry
                    {
                        file = JsonUtil.GetValue(s, "file"),
                        type = JsonUtil.GetValue(s, "type"),
                        source = JsonUtil.GetValue(s, "source"),
                        localPath = JsonUtil.GetValue(s, "localPath"),
                        repoUrl = JsonUtil.GetValue(s, "repoUrl"),
                    });
                }

                string ntJson = JsonUtil.GetObject(json, "nametag");
                if (ntJson != null)
                    p.nametag = new remoteNametagInfo
                    {
                        r = JsonUtil.GetFloat(ntJson, "r", 1f),
                        g = JsonUtil.GetFloat(ntJson, "g", 1f),
                        b = JsonUtil.GetFloat(ntJson, "b", 1f),
                        bold = JsonUtil.GetBool(ntJson, "bold"),
                        italic = JsonUtil.GetBool(ntJson, "italic"),
                        customName = JsonUtil.GetValue(ntJson, "customName"),
                        iconMode = JsonUtil.GetValue(ntJson, "iconMode"),
                        iconCountry = JsonUtil.GetValue(ntJson, "iconCountry"),
                        iconPath = JsonUtil.GetValue(ntJson, "iconPath"),
                        iconScale = JsonUtil.GetFloat(ntJson, "iconScale", 1f),
                        iconOffX = JsonUtil.GetFloat(ntJson, "iconOffX"),
                        iconOffY = JsonUtil.GetFloat(ntJson, "iconOffY"),
                        platformHide = JsonUtil.GetValue(ntJson, "platformHide"),
                        platformCustom = JsonUtil.GetValue(ntJson, "platformCustom"),
                        nameStyle = JsonUtil.GetValue(ntJson, "nameStyle"),
                        backingPath = JsonUtil.GetValue(ntJson, "backingPath"),
                        backingOffX = JsonUtil.GetFloat(ntJson, "backingOffX"),
                        backingOffY = JsonUtil.GetFloat(ntJson, "backingOffY"),
                        backingScale = JsonUtil.GetFloat(ntJson, "backingScale", 1f),
                        nickname = JsonUtil.GetValue(ntJson, "nickname"),
                    };

                return p;
            }
            catch
            {
                return null;
            }
        }

        private static string GetCategoryFolder(SkinType type)
        {
            switch (type)
            {
                case SkinType.Costume: return "Costumes";
                case SkinType.Accessory: return "Accessories";
                case SkinType.Item: return "Items";
                default: return "Costumes";
            }
        }
    }

    public class AppliedRemoteSkin
    {
        public GameObject instance;
        public SkinType type;
    }
}
