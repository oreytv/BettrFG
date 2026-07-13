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
    public class RemoteNametagInfo
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

    public class RemoteSkinEntry
    {
        public string file;
        public string type;
        public string source;
        public string localPath;
        public string repoUrl; // raw base URL, e.g. https://raw.githubusercontent.com/oreytv/BetterFGPublicSkins/main
    }

    public class PlayerRemoteProfile
    {
        public uint playerID;
        public string episodeGUID;
        public float playerScale;
        public List<RemoteSkinEntry> skins = new List<RemoteSkinEntry>();
        public RemoteNametagInfo nametag;
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

        private List<PlayerRemoteProfile> _profiles = new List<PlayerRemoteProfile>();

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

        // keyed only, never positional: without a key this lands in _pending and ResolvePending
        // stamps our loadout onto some remote bean
        public void RegisterLocalProfile()
        {
            string key;
            try { key = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? ""; }
            catch { key = ""; }
            if (string.IsNullOrEmpty(key)) return;

            var profile = RemoteProfileStore.LocalLoadout() ?? new PlayerRemoteProfile();
            profile.playerKey = key;
            profile.playerScale = PlayerScaleService.GetPlayerScale();

            RemoteProfileStore.Register(profile, key);
        }

        private void LoadProfilesFromFile()
        {
            _profiles.Clear();

            string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "debug_profiles.json");
            if (File.Exists(path))
            {
                string json = null;
                try { json = File.ReadAllText(path); }
                catch (Exception ex) { Plugin.Log.LogError("NetworkClient: read err: " + ex.Message); }

                if (json != null)
                    foreach (string entry in JsonUtil.GetRootArray(json))
                    {
                        var p = ParseProfile(entry);
                        if (p != null) _profiles.Add(p);
                    }
            }
            else Plugin.Log.LogInfo("NetworkClient: no debug_profiles.json");

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

            var appSvc = CustomizationServices.ApplicationService;
            var loader = CustomizationServices.LoaderService;
            if (appSvc == null || loader == null) yield break;

            // key-match profiles (.bfgprofile) bind to the bean whose clean name matches playerKey
            // (local bean included); debug entries keep the old positional behaviour.
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

                    if (bean == null) { Plugin.Log.LogInfo($"NetworkClient: no bean named '{want}' for profile"); continue; }
                    profile.resolvedPlayerKey = profile.playerKey;
                    Plugin.Log.LogInfo($"NetworkClient: profile '{want}' -> {bean.name}");
                }
                else
                {
                    if (i >= remotes.Count) continue;
                    bean = remotes[i];
                }

                if (profile.playerScale > 0f)
                    PlayerScaleService.ApplySkinScaleToBean(bean, profile.playerScale, PlayerScaleService.BeanScaleMode.Remote);

                foreach (var entry in profile.skins)
                {
                    ActiveSkinSlot slot = null;
                    yield return loader.ResolveProfileSlot(entry, s => slot = s).WrapToIl2Cpp();
                    if (slot != null)
                        yield return appSvc.ApplySkinToBean(slot, bean).WrapToIl2Cpp();
                }

#if PROFILES
                if (profile.requireKeyMatch)
                    BetterFG.Customization.Profiles.ProfileService.ApplyExtras(profile.playerKey, bean,
                        profile.nametag != null && !string.IsNullOrEmpty(profile.resolvedPlayerKey) ? profile.nametag : null);
#endif
            }
        }

        internal static IEnumerator PollAndApplyNametagForBean(GameObject bean, RemoteNametagInfo info)
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
                        Plugin.Log.LogInfo($"NetworkClient: nametag apply {bean.name} elapsed={elapsed:F2}s");
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
            Plugin.Log.LogWarning($"NetworkClient: nametag poll timed out for {bean.name}");
        }

        private static IEnumerator PollAndApplyPlatformIconForBean(GameObject bean, RemoteNametagInfo info)
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
            Plugin.Log.LogWarning($"NetworkClient: timed out for '{bean.name}'");
        }

        private static PlayerRemoteProfile ParseProfile(string json)
        {
            try
            {
                var p = new PlayerRemoteProfile
                {
                    playerID = (uint)JsonUtil.GetInt(json, "playerID"),
                    episodeGUID = JsonUtil.GetValue(json, "episodeGUID"),
                    playerKey = JsonUtil.GetValue(json, "playerKey"),
                    playerScale = JsonUtil.GetFloat(json, "playerScale", 1f),
                };

                var skinsArr = JsonUtil.GetArray(json, "skins");
                foreach (string s in skinsArr)
                {
                    p.skins.Add(new RemoteSkinEntry
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
                    p.nametag = new RemoteNametagInfo
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

    }

    public class AppliedRemoteSkin
    {
        public GameObject instance;
        public SkinType type;
    }
}
