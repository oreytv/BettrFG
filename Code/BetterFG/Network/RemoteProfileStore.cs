using System;
using System.Collections.Generic;
using BetterFG.Nametag;
using BetterFG.Services;
using FallGuysLib.Players;
using UnityEngine;

namespace BetterFG.Network
{
    public static class RemoteProfileStore
    {
        private static readonly Dictionary<string, PlayerRemoteProfile> _byKey
            = new Dictionary<string, PlayerRemoteProfile>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<PlayerRemoteProfile> _pending
            = new List<PlayerRemoteProfile>();

        // ── Local loadout ───────────────────────────────────────────────────────
        // the local player's equipped skins live in skin.multi.* (CustomizationTab is the writer).
        // this store is the one front door that reads them back as a typed PlayerRemoteProfile, so the
        // rest of the mod asks "what am I wearing" here instead of scraping the apply service's live
        // scene slots. one entry per equipped item; legacy single-skin keys fold in for old saves.
        public static PlayerRemoteProfile LocalLoadout()
        {
            string files = SettingsService.Get("skin.multi.files", "");
            string sources = SettingsService.Get("skin.multi.sources", "");
            string paths = SettingsService.Get("skin.multi.paths", "");
            string repos = SettingsService.Get("skin.multi.repos", "");
            string types = SettingsService.Get("skin.multi.types", "");

            if (string.IsNullOrEmpty(files))
            {
                files = SettingsService.Get("skin.file", "");
                if (string.IsNullOrEmpty(files)) return null;
                sources = SettingsService.Get("skin.source", "");
                paths = SettingsService.Get("skin.localPath", "");
                repos = types = "";
            }

            string[] f = files.Split(',');
            string[] src = sources.Split(',');
            string[] pth = paths.Split(',');
            string[] rp = repos.Split(',');
            string[] ty = types.Split(',');

            var profile = new PlayerRemoteProfile();
            for (int i = 0; i < f.Length; i++)
            {
                string file = f[i].Trim();
                if (string.IsNullOrEmpty(file)) continue;
                profile.skins.Add(new RemoteSkinEntry
                {
                    file = file,
                    source = i < src.Length ? src[i].Trim() : "remote",
                    localPath = i < pth.Length ? pth[i].Trim() : "",
                    repoUrl = i < rp.Length ? rp[i].Trim() : "",
                    type = i < ty.Length ? ty[i].Trim() : "",
                });
            }
            return profile.skins.Count > 0 ? profile : null;
        }

        public static Dictionary<string, int> LocalHandOverrides()
        {
            var map = new Dictionary<string, int>();
            string raw = SettingsService.Get("skin.hand.overrides", "");
            if (string.IsNullOrEmpty(raw)) return map;
            foreach (string part in raw.Split(','))
            {
                int colon = part.LastIndexOf(':');
                if (colon < 1) continue;
                if (int.TryParse(part.Substring(colon + 1), out int ov))
                    map[part.Substring(0, colon)] = ov;
            }
            return map;
        }

        public static bool LocalHasFile(string file)
        {
            if (string.IsNullOrEmpty(file)) return false;
            var local = LocalLoadout();
            if (local == null) return false;
            foreach (var e in local.skins)
                if (e.file == file) return true;
            return false;
        }

        public static void Clear()
        {
            _byKey.Clear();
            _pending.Clear();
        }

        public static void Register(PlayerRemoteProfile profile, string playerKey = null)
        {
            if (!string.IsNullOrEmpty(playerKey))
            {
                Add(profile, playerKey);
                profile.resolvedPlayerKey = playerKey;
                NametagPatchHub.RefreshRemoteNametags();
                return;
            }

            if (!string.IsNullOrEmpty(profile.playerKey))
            {
                Add(profile, profile.playerKey);
                profile.resolvedPlayerKey = profile.playerKey;
                NametagPatchHub.RefreshRemoteNametags();
                return;
            }

            _pending.Add(profile);
        }

        private static void Add(PlayerRemoteProfile profile, string key)
        {
            _byKey[key] = profile;
            string clean = PlayerUtils.CleanPlayerName(key);
            if (!string.IsNullOrEmpty(clean))
                _byKey[clean] = profile;
        }

        /// <summary>
        /// Looks up by full key (e.g. xb1_oreytv) or clean name (e.g. oreytv).
        /// </summary>
        public static PlayerRemoteProfile TryGet(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_byKey.TryGetValue(key, out var p)) return p;
            string cleaned = PlayerUtils.CleanPlayerName(key);
            if (cleaned != key && _byKey.TryGetValue(cleaned, out var pc)) return pc;
            return null;
        }

        public static void ResolvePending(GameObject localBean)
        {
            if (_pending.Count == 0) return;

            try
            {
                var cpm = PlayerUtils.GetClientPlayerManager();
                if (cpm?._playerIdIndex == null) return;

                var remotes = new List<(uint id, string key)>();
                foreach (var kvp in cpm._playerIdIndex)
                {
                    var go = kvp.Value?.fgcc?.gameObject;
                    if (go == null || go == localBean) continue;
                    remotes.Add((kvp.Key, kvp.Value.playerKey ?? ""));
                }
                remotes.Sort((a, b) => a.id.CompareTo(b.id));

                for (int i = 0; i < _pending.Count && i < remotes.Count; i++)
                {
                    var profile = _pending[i];

                    if (profile.playerID != 0)
                    {
                        bool matched = false;
                        foreach (var r in remotes)
                        {
                            if (r.id != profile.playerID) continue;
                            Add(profile, r.key);
                            profile.resolvedPlayerKey = r.key;
                            matched = true;
                            break;
                        }
                        if (matched) continue;
                    }

                    string fullKey = remotes[i].key;
                    if (!string.IsNullOrEmpty(fullKey))
                    {
                        Add(profile, fullKey);
                        profile.resolvedPlayerKey = fullKey;
                        Plugin.Log.LogInfo($"positional match profile[{i}] -> {fullKey}");
                    }
                }

                _pending.Clear();
                NametagPatchHub.RefreshRemoteNametags();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("ResolvePending: " + ex.Message);
            }
        }
    }
}
