using System;
using System.Collections.Generic;
using System.IO;
using Character;
using FG.Common.Character;
using FG.Common.CMS;
using FG.Common.Definition;
using FGClient;
using FGClient.Customiser;
using MPG.Utility;
using NAudio.Wave;
using UnityEngine;
using SysSlotDict = System.Collections.Generic.Dictionary<int, int>;

namespace BetterFG.Customization.Social
{
    public static class EmoticonInjectionService
    {
        private static readonly SysSlotDict OriginalSlots = new SysSlotDict();
        // the actual option object each slot held before we overwrote it. restoring the object works
        // for slots that held an EmotesOption too (those have no _speechId to look up).
        private static readonly Dictionary<int, ItemDefinitionSO> OriginalSlotObjects = new Dictionary<int, ItemDefinitionSO>();
        internal static readonly Dictionary<int, int> Remap = new Dictionary<int, int>();
        // customSpeechId → entry id, so the play patch reads the entry's *current* sounds live
        internal static readonly Dictionary<int, string> SoundOwner = new Dictionary<int, string>();
        private static readonly System.Random Rng = new System.Random();

        private static List<EmoticonEntry> _entries = new List<EmoticonEntry>();

        public static string LastStatus { get; private set; } = "";

        // ── Public API ────────────────────────────────────────────────────────

        public static void SetEntries(List<EmoticonEntry> entries) => _entries = entries;

        public static void ApplyAll(List<EmoticonEntry> entries)
        {
            _entries = entries;
            RestoreSlots(null);
            InjectSlots(null);
        }

        internal static void ReapplyToWheel(SocialPrimeHandler primeHandler)
        {
            RestoreSlots(primeHandler);
            InjectSlots(primeHandler);
        }

        internal static void PlayCustomSound(int speechId)
        {
            if (!SoundOwner.TryGetValue(speechId, out string ownerId)) return;
            EmoticonEntry entry = null;
            foreach (var e in _entries) if (e.id == ownerId) { entry = e; break; }
            if (entry?.soundPaths == null) return;
            var sounds = new List<string>();
            foreach (string s in entry.soundPaths) if (!string.IsNullOrEmpty(s)) sounds.Add(s);
            if (sounds.Count == 0) return;
            string path = sounds[Rng.Next(sounds.Count)];
            if (!File.Exists(path)) return;
            try
            {
                float vol = GetVolume();
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
            catch (Exception ex) { Debug.LogWarning($"[EmoticonInjection] sound play failed: {ex.Message}"); }
        }

        private static float GetVolume()
        {
            try
            {
                var audio = GlobalGameStateClient.Instance?.PlayerProfile?.AudioSettings;
                if (audio != null)
                    return Mathf.Clamp01(audio.MasterVolume) * Mathf.Clamp01(audio.SFXVolume) * 0.6f;
            }
            catch { }
            return 0.6f;
        }

        // ── Core ──────────────────────────────────────────────────────────────

        // resolve the EmotesAndEmoticons slot list. shared by Restore/Inject so both hit the same
        // list instance. handler null falls back to first found (Apply All button).
        private static Il2CppSystem.Collections.Generic.List<ItemDefinitionSO> ResolveSlots(SocialPrimeHandler handler)
        {
            var primeHandler = handler ?? UnityEngine.Object.FindObjectOfType<SocialPrimeHandler>();
            if (primeHandler == null) { LastStatus = "SocialPrimeHandler not found."; return null; }

            var wheelDict = primeHandler.HighlightedSocialWheel?.SocialItemsDictionary;
            if (wheelDict == null) { LastStatus = "SocialItemsDictionary not found."; return null; }

            foreach (var k in wheelDict.Keys)
                if (k.ToString() == "EmotesAndEmoticons") return wheelDict[k];

            LastStatus = "EmotesAndEmoticons key not in wheel dict.";
            return null;
        }

        // restore the actual object we displaced last cycle — handles emoticon AND emote slots.
        // MUST run before any service on this wheel injects (see EmoteInjectionService.RestoreSlots
        // for the lingering-icon bug this ordering fixes).
        internal static void RestoreSlots(SocialPrimeHandler handler = null)
        {
            try
            {
                var slots = ResolveSlots(handler);
                if (slots == null) return;

                foreach (var kvp in OriginalSlotObjects)
                {
                    int si = kvp.Key;
                    if (si >= slots.Count || kvp.Value == null) continue;
                    slots[si] = kvp.Value;
                }
                OriginalSlots.Clear();
                OriginalSlotObjects.Clear();
                Remap.Clear();
                SoundOwner.Clear();
            }
            catch (Exception ex) { Debug.LogError($"[EmoticonInjection] restore: {ex}"); }
        }

        internal static void InjectSlots(SocialPrimeHandler handler = null)
        {
            try
            {
                var speechMgr = SingletonBehaviour<SpeechOptionsManager>.Instance;
                if (speechMgr == null) { LastStatus = "Not in a round yet."; return; }

                var slots = ResolveSlots(handler);
                if (slots == null) return;

                var lookup = speechMgr._speechOptionsLookup;

                // find a reference emoticon option to clone metadata from, and index every emoticon
                // option by its CMS id so an entry's itemId can pull that exact emoticon's own
                // menuDisplaySpriteReference (the AssetReferenceSprite the wheel actually renders).
                // copying the ref's reference for every entry made them all share one sprite handle,
                // so the wheel only ever showed whatever emoticon was last loaded.
                ImageSpeechOption refOpt = null;
                var byId = new Dictionary<string, ImageSpeechOption>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in lookup)
                {
                    if (kvp.Value?.CMSGroupID != "cosmetics_emoticons") continue;
                    var iso = kvp.Value.TryCast<ImageSpeechOption>();
                    if (iso == null) continue;
                    if (refOpt == null) refOpt = iso;
                    var id = iso.CMSData?.Cast<CMSItemDefinition>()?.Id;
                    if (!string.IsNullOrEmpty(id)) byId[id] = iso;
                }
                if (refOpt == null) { LastStatus = "No reference emoticon found in lookup."; return; }

                // ── Step 2: inject enabled entries ─────────────────────────────
                int injected = 0;
                foreach (var e in _entries)
                {
                    if (!e.enabled) continue;

                    int slotIndex = Math.Max(0, Math.Min(e.slot, slots.Count - 1));

                    // slot may hold an EmotesOption (emotes share this wheel) — TryCast so it doesn't throw
                    int originalId = slots[slotIndex]?.TryCast<SocialOption>()?._speechId ?? -1;
                    if (!OriginalSlotObjects.ContainsKey(slotIndex))
                    {
                        OriginalSlots[slotIndex] = originalId;
                        OriginalSlotObjects[slotIndex] = slots[slotIndex];
                    }

                    int customId = 60000 + slotIndex * 100 + injected;

                    string cmsId = string.IsNullOrEmpty(e.itemId) ? $"bfg_emoticon_{e.id}" : e.itemId;

                    // clone metadata from the emoticon the itemId names (its own icon art), falling
                    // back to the generic ref when the id isn't found or none was given.
                    ImageSpeechOption src = refOpt;
                    if (!string.IsNullOrEmpty(e.itemId) && byId.TryGetValue(e.itemId, out var matched))
                        src = matched;

                    var cms = new CustomiserEmoticons();
                    cms.Id = cmsId;
                    var srcCms = src.CMSData?.Cast<CMSItemDefinition>();
                    cms.IconName = srcCms?.IconName ?? (string.IsNullOrEmpty(e.itemId) ? cmsId : e.itemId);
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

                    if (SocialSpriteCache.TryGet(e.imagePath, out var customSprite, out var cacheableSprite))
                    {
                        opt._sprite = customSprite;
                        opt._cachedAtlasSprite = cacheableSprite;
                    }

                    lookup[customId] = opt;
                    slots[slotIndex] = opt;

                    bool hasSound = false;
                    if (e.soundPaths != null)
                        foreach (string s in e.soundPaths)
                            if (!string.IsNullOrEmpty(s)) { hasSound = true; break; }
                    if (hasSound) SoundOwner[customId] = e.id;
                    if (originalId >= 0) Remap[originalId] = customId;

                    injected++;
                }

                LastStatus = injected > 0
                    ? $"Applied {injected} emoticon(s)."
                    : "No enabled emoticons.";
            }
            catch (Exception ex)
            {
                LastStatus = $"Error: {ex.Message}";
                Debug.LogError($"[EmoticonInjection] {ex}");
            }
        }

    }
}