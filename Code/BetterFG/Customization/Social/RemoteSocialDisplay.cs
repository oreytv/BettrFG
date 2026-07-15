using System;
using System.Collections.Generic;
using System.IO;
using Character;
using FG.Common.CMS;
using FGClient;
using FGClient.Customiser;
using MPG.Utility;
using NAudio.Wave;
using UnityEngine;
using PlayerUtils = FallGuysLib.Players.PlayerUtils;

namespace BetterFG.Customization.Social
{
    // renders other players' shared custom emoticons/phrases. when a bean we hold entries for plays
    // the option sitting in one of its customised wheel slots, swap in the custom art/text/sound.
    // display-only for that one player — the local wheel and injection services are never touched.
    public static class RemoteSocialDisplay
    {
        public class Entry
        {
            public bool phrase;
            public int slot;
            public string text;
            public string imagePath;
            public string[] soundPaths;
        }

        private static readonly Dictionary<string, List<Entry>> _byPlayer = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int> _builtIds = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly System.Random Rng = new System.Random();
        private static int _nextId = 70000;

        public static void Clear() => _byPlayer.Clear();

        public static void Set(string cleanName, List<Entry> entries)
        {
            if (string.IsNullOrEmpty(cleanName)) return;
            if (entries == null || entries.Count == 0) _byPlayer.Remove(cleanName);
            else _byPlayer[cleanName] = entries;
        }

        internal static void TryRemap(GameObject bean, ref int optionId)
        {
            if (_byPlayer.Count == 0 || bean == null) return;
            try
            {
                var cpm = PlayerUtils.GetClientPlayerManager();
                if (cpm?._playerIdIndex == null) return;

                uint playerId = 0;
                string playerKey = null;
                foreach (var kvp in cpm._playerIdIndex)
                {
                    if (kvp.Value?.fgcc?.gameObject != bean) continue;
                    playerId = kvp.Key;
                    playerKey = kvp.Value.playerKey;
                    break;
                }
                if (string.IsNullOrEmpty(playerKey)) return;
                if (!_byPlayer.TryGetValue(PlayerUtils.CleanPlayerName(playerKey), out var entries)) return;

                // which of THIS player's wheel slots holds the option they just played
                var sel = cpm.GetPlayerMetadataFromPlayerId(playerId)?.Selections;
                if (sel == null) return;
                int slot = IndexOfSpeech(sel.FirstWheelOptions, optionId);
                if (slot < 0) slot = IndexOfSpeech(sel.SecondWheelOptions, optionId);
                if (slot < 0) return;

                var lookup = SingletonBehaviour<SpeechOptionsManager>.Instance?._speechOptionsLookup;
                if (lookup == null || !lookup.ContainsKey(optionId)) return;
                var played = lookup[optionId];
                string group = played?.CMSGroupID ?? "";
                bool isPhrase = group == "cosmetics_phrases";
                if (!isPhrase && group != "cosmetics_emoticons") return;

                Entry entry = null;
                foreach (var e in entries)
                    if (e.phrase == isPhrase && e.slot == slot) { entry = e; break; }
                if (entry == null) return;

                string key = playerKey + "|" + (isPhrase ? "p" : "e") + slot;
                if (!_builtIds.TryGetValue(key, out int customId) || !lookup.ContainsKey(customId))
                {
                    var src = played.TryCast<ImageSpeechOption>();
                    if (src == null) return;
                    customId = _nextId++;
                    var srcCms = src.CMSData?.Cast<CMSItemDefinition>();

                    if (isPhrase)
                    {
                        var cms = new CustomiserPhrases();
                        cms.Id = "bfg_rs_" + customId;
                        var def = cms.Cast<CMSItemDefinition>();
                        def.Name = new LocalisedString
                        {
                            Id = cms.Id + "_text",
                            Text = string.IsNullOrEmpty(entry.text) ? "..." : entry.text
                        };
                        def.IconName = srcCms?.IconName ?? cms.Id;
                        if (srcCms != null) def.ItemRarity = srcCms.ItemRarity;

                        var opt = ScriptableObject.CreateInstance<TextAndImageSpeechOption>();
                        opt.SetCMSData(cms);
                        opt.name = cms.Id;
                        opt._speechId = customId;
                        opt._speechDuration = 3f;
                        opt._speechHasDuration = true;
                        opt._audioBank = null;
                        opt._audioEvent = null;
                        opt._cachedAtlasSprite = src._cachedAtlasSprite;
                        opt._sprite = src._sprite;
                        opt._spriteAtlasLoadableAsset = src._spriteAtlasLoadableAsset;
                        opt.menuDisplaySpriteAtlasReference = src.menuDisplaySpriteAtlasReference;
                        opt.menuDisplaySpriteReference = src.menuDisplaySpriteReference;
                        if (SocialSpriteCache.TryGet(entry.imagePath, out var cSprite, out var cAtlas))
                        {
                            opt._sprite = cSprite;
                            opt._cachedAtlasSprite = cAtlas;
                        }
                        lookup[customId] = opt;
                    }
                    else
                    {
                        var cms = new CustomiserEmoticons();
                        cms.Id = "bfg_rs_" + customId;
                        cms.IconName = srcCms?.IconName ?? cms.Id;
                        if (srcCms != null) cms.ItemRarity = srcCms.ItemRarity;

                        var opt = ScriptableObject.CreateInstance<ImageSpeechOption>();
                        opt.SetCMSData(cms);
                        opt.name = cms.Id;
                        opt._speechId = customId;
                        opt._speechDuration = 3f;
                        opt._speechHasDuration = true;
                        opt._audioBank = null;
                        opt._audioEvent = null;
                        opt._cachedAtlasSprite = src._cachedAtlasSprite;
                        opt._sprite = src._sprite;
                        opt._spriteAtlasLoadableAsset = src._spriteAtlasLoadableAsset;
                        opt.menuDisplaySpriteAtlasReference = src.menuDisplaySpriteAtlasReference;
                        opt.menuDisplaySpriteReference = src.menuDisplaySpriteReference;
                        if (SocialSpriteCache.TryGet(entry.imagePath, out var cSprite, out var cAtlas))
                        {
                            opt._sprite = cSprite;
                            opt._cachedAtlasSprite = cAtlas;
                        }
                        lookup[customId] = opt;
                    }
                    _builtIds[key] = customId;
                }
                else
                {
                    // the cached option survives across games but its CacheableAtlasSprite doesn't
                    // (see SocialSpriteCache note) — the bubble then falls back to the original art.
                    // re-cache from the live played option + a fresh wrapper every play
                    var opt = lookup[customId].TryCast<ImageSpeechOption>();
                    var src = played.TryCast<ImageSpeechOption>();
                    if (opt != null && src != null)
                    {
                        opt._sprite = src._sprite;
                        opt._cachedAtlasSprite = src._cachedAtlasSprite;
                        if (SocialSpriteCache.TryGet(entry.imagePath, out var cSprite, out var cAtlas))
                        {
                            opt._sprite = cSprite;
                            opt._cachedAtlasSprite = cAtlas;
                        }
                    }
                }

                optionId = customId;
                PlaySound(entry);
            }
            catch (Exception ex) { Plugin.Log.LogWarning("remote social: " + ex.Message); }
        }

        private static int IndexOfSpeech(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<ItemDefinitionSO> arr, int speechId)
        {
            if (arr == null) return -1;
            for (int i = 0; i < arr.Length; i++)
            {
                var so = arr[i]?.TryCast<SocialOption>();
                if (so != null && so._speechId == speechId) return i;
            }
            return -1;
        }

        private static void PlaySound(Entry entry)
        {
            if (entry.soundPaths == null) return;
            var sounds = new List<string>();
            foreach (string s in entry.soundPaths)
                if (!string.IsNullOrEmpty(s) && File.Exists(s)) sounds.Add(s);
            if (sounds.Count == 0) return;
            string path = sounds[Rng.Next(sounds.Count)];
            try
            {
                float vol = 0.6f;
                var audio = GlobalGameStateClient.Instance?.PlayerProfile?.AudioSettings;
                if (audio != null)
                    vol = Mathf.Clamp01(audio.MasterVolume) * Mathf.Clamp01(audio.SFXVolume) * 0.6f;
                var ms = new MemoryStream(File.ReadAllBytes(path));
                WaveStream reader = path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                    ? (WaveStream)new Mp3FileReader(ms)
                    : new WaveFileReader(ms);
                var volProv = new VolumeWaveProvider16(reader) { Volume = vol };
                var output = new WaveOutEvent();
                output.Init(volProv);
                output.Play();
                output.PlaybackStopped += (_, __) => { output.Dispose(); reader.Dispose(); ms.Dispose(); };
            }
            catch (Exception ex) { Plugin.Log.LogWarning("remote social sound: " + ex.Message); }
        }
    }
}
