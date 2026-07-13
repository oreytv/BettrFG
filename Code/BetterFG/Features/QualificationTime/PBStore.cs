using System.Collections.Generic;
using System.IO;
using BetterFG.Utilities;
using FGClient;
using UnityEngine;

namespace BetterFG.Features.QualificationTime
{
    // which show a time belongs to. solos is the default/fast one, duos/squads are the team shows.
    internal enum PbType { Solos, Duos, Squads }

    internal static class PBStore
    {
        static string FilePath => Path.Combine(SettingsDir, "betterfg_pbs.dat");
        static string BackupFilePath => Path.Combine(SettingsDir, "betterfg_pbs.bak");
        static string SettingsDir => Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "BettrFG", "Settings");

        // an entry now holds up to three times - one per show type. any of them can be null, which
        // just means "no time recorded for that show". null is a real, intended state and is never
        // touched by migration.
        internal struct Entry
        {
            public string displayName;
            public float? solos;
            public float? duos;
            public float? squads;
            public string rawId;
            // legacy single date kept for back-compat reads. new code writes per-show dates below;
            // GetDate falls back to this if a per-show date is missing (old json).
            public string date;
            public string solosDate;
            public string duosDate;
            public string squadsDate;

            public float? Get(PbType t) => t == PbType.Solos ? solos : t == PbType.Duos ? duos : squads;
            public void Set(PbType t, float? v) { if (t == PbType.Solos) solos = v; else if (t == PbType.Duos) duos = v; else squads = v; }
            public string GetDate(PbType t)
            {
                string d = t == PbType.Solos ? solosDate : t == PbType.Duos ? duosDate : squadsDate;
                return string.IsNullOrEmpty(d) ? date : d;
            }
            public void SetDate(PbType t, string d)
            {
                if (t == PbType.Solos) solosDate = d; else if (t == PbType.Duos) duosDate = d; else squadsDate = d;
            }
            // fastest non-null of the three, or null if the entry is somehow empty
            public float? Best()
            {
                float? b = null;
                if (solos.HasValue && (!b.HasValue || solos.Value < b.Value)) b = solos;
                if (duos.HasValue && (!b.HasValue || duos.Value < b.Value)) b = duos;
                if (squads.HasValue && (!b.HasValue || squads.Value < b.Value)) b = squads;
                return b;
            }
            public bool HasAny() => solos.HasValue || duos.HasValue || squads.HasValue;
        }

        // normalizedKey -> Entry
        static Dictionary<string, Entry> _cache;
        static HashSet<string> _featured;

        // fired after any mutation (set/feature/delete) so views can refresh themselves
        public static event System.Action OnChanged;
        static void RaiseChanged() { try { OnChanged?.Invoke(); } catch (System.Exception ex) { Plugin.Log.LogWarning("PBStore: OnChanged handler threw: " + ex.Message); } }

        // ── show type detection ───────────────────────────────────────────────
        // reads the live ClientGameManager. solos = not a squad show; duos/squads keyed off SquadSize.
        // outside a live show (menu) this has no answer, so callers that need a type pass it explicitly.
        public static PbType CurrentType()
        {
            try
            {
                ClientGameManager cgm;
                var gsv = FGClient.GlobalGameStateClient.Instance?.GameStateView;
                if (gsv != null && gsv.GetLiveClientGameManager(out cgm) && cgm != null)
                {
                    if (!cgm.IsSquadShow) return PbType.Solos;
                    return cgm.SquadSize == 2 ? PbType.Duos : PbType.Squads;
                }
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"PBStore: show type lookup failed: {ex.Message}"); }
            return PbType.Solos;
        }

        static void EnsureLoaded()
        {
            if (_cache != null) return;
            _cache = new Dictionary<string, Entry>();
            _featured = new HashSet<string>();

            bool changed = false;
            foreach (string path in GetLoadPaths())
            {
                if (!File.Exists(path)) continue;
                try
                {
                    changed |= LoadJson(PBObf.Decode(File.ReadAllBytes(path)));
                    Plugin.Log.LogInfo($"PBStore: merged pbs from {path}");
                }
                catch (System.Exception ex)
                {
                    Plugin.Log.LogWarning($"PBStore: load failed from {path}: {ex.Message}");
                }
            }

            changed |= TryImportOldPbTxt();
            changed |= MergeUgcRevisionKeys();
            MergeUgcRevisionFiles();
            // if anything was loaded or upgraded from the legacy single-time format, persist the
            // new shape once so we don't re-migrate every launch.
            if (_cache.Count > 0 || changed)
            {
                Plugin.Log.LogInfo($"PBStore: loaded {_cache.Count} entries, {_featured.Count} featured");
                Save();
            }
        }

        // one-shot migration: any cache key shaped like "ugc-…_<n>" gets merged into its canonical
        // (suffix-stripped) sibling. fixes pbs split across explore-mode revisions.
        static bool MergeUgcRevisionKeys()
        {
            var toMove = new List<string>();
            foreach (var k in _cache.Keys)
            {
                string c = CanonicalRoundId(k);
                if (c != k) toMove.Add(k);
            }
            foreach (var oldKey in toMove)
            {
                string newKey = CanonicalRoundId(oldKey);
                var entry = _cache[oldKey];
                if (string.IsNullOrEmpty(entry.rawId) || entry.rawId == oldKey)
                    entry.rawId = newKey;
                MergeInto(newKey, entry);
                bool wasFeatured = _featured.Remove(oldKey);
                _cache.Remove(oldKey);
                if (wasFeatured) _featured.Add(newKey);
            }
            return toMove.Count > 0;
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                string json = MakeJson();
                byte[] bytes = PBObf.Encode(json);
                if (File.Exists(FilePath))
                {
                    try
                    {
                        string oldJson = PBObf.Decode(File.ReadAllBytes(FilePath));
                        if (!string.IsNullOrWhiteSpace(oldJson) && oldJson.TrimStart().StartsWith("{"))
                            File.Copy(FilePath, BackupFilePath, true);
                    }
                    catch { }
                }
                File.WriteAllBytes(FilePath + ".tmp", bytes);
                if (File.Exists(FilePath))
                    File.Replace(FilePath + ".tmp", FilePath, null, true);
                else
                    File.Move(FilePath + ".tmp", FilePath);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"PBStore: save failed: {ex.Message}");
            }
        }

        static string MakeJson()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder();
            sb.Append("{\"pbs\":[");
            bool first = true;
            foreach (var kv in _cache)
            {
                if (!first) sb.Append(',');
                first = false;
                var e = kv.Value;
                string safeName = JsonEscape(e.displayName);
                string safeId = JsonEscape(!string.IsNullOrEmpty(e.rawId) ? e.rawId : kv.Key);
                bool isFeatured = _featured.Contains(kv.Key);
                sb.Append($"{{\"id\":\"{safeId}\",\"name\":\"{safeName}\"");
                // always write all three slots so the entry is unambiguously new-format. null stays
                // null in json, so it round-trips and migration never re-touches it.
                sb.Append(",\"solos\":").Append(e.solos.HasValue ? e.solos.Value.ToString(ci) : "null");
                sb.Append(",\"duos\":").Append(e.duos.HasValue ? e.duos.Value.ToString(ci) : "null");
                sb.Append(",\"squads\":").Append(e.squads.HasValue ? e.squads.Value.ToString(ci) : "null");
                sb.Append($",\"date\":\"{JsonEscape(e.date ?? "")}\"");
                sb.Append($",\"solosDate\":\"{JsonEscape(e.solosDate ?? "")}\"");
                sb.Append($",\"duosDate\":\"{JsonEscape(e.duosDate ?? "")}\"");
                sb.Append($",\"squadsDate\":\"{JsonEscape(e.squadsDate ?? "")}\"");
                sb.Append($",\"featured\":{(isFeatured ? "true" : "false")}}}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static IEnumerable<string> GetLoadPaths()
        {
            yield return FilePath;
            yield return BackupFilePath;
        }

        // reads a nullable float field. returns null if the key is missing OR explicitly null.
        static float? ReadNullableFloat(string json, string key)
        {
            string sk = $"\"{key}\":";
            int i = json.IndexOf(sk);
            if (i == -1) return null;
            i += sk.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            if (i + 4 <= json.Length && json.Substring(i, 4) == "null") return null;
            float v = JsonUtil.GetFloat(json, key, float.NaN);
            return float.IsNaN(v) ? (float?)null : v;
        }

        // returns true if the entry was a legacy single-time row that got upgraded (so we know to re-save)
        static bool LoadJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith("{")) return false;

            bool upgraded = false;
            var entries = JsonUtil.GetArray(json, "pbs");
            foreach (var entryJson in entries)
            {
                string id = JsonUtil.GetValue(entryJson, "id");
                string name = JsonUtil.GetValue(entryJson, "name");
                bool featured = JsonUtil.GetBool(entryJson, "featured");

                // an entry is "new format" only if it actually carries the per-show fields. if NONE
                // of solos/duos/squads exist, it's a legacy single-time row -> the old "time" becomes
                // the solos time. if even one of the three exists, it's already new format and we read
                // them verbatim, nulls included, and NEVER backfill from "time".
                bool hasNewFields =
                    entryJson.Contains("\"solos\":") ||
                    entryJson.Contains("\"duos\":") ||
                    entryJson.Contains("\"squads\":");

                Entry incoming = new Entry { displayName = name, rawId = id };
                if (hasNewFields)
                {
                    incoming.solos = ReadNullableFloat(entryJson, "solos");
                    incoming.duos = ReadNullableFloat(entryJson, "duos");
                    incoming.squads = ReadNullableFloat(entryJson, "squads");
                }
                else
                {
                    float legacy = JsonUtil.GetFloat(entryJson, "time");
                    if (legacy > 0f) incoming.solos = legacy;
                    upgraded = true; // format changed on disk -> re-save
                }

                // date is new. older entries either have no date OR have an old date-only string
                // (yyyy-MM-dd, no HH:mm:ss) -> stamp today's full timestamp and re-save so it sticks.
                incoming.date = JsonUtil.GetValue(entryJson, "date");
                if (string.IsNullOrEmpty(incoming.date) || !incoming.date.Contains(" "))
                { incoming.date = Today(); upgraded = true; }

                // per-show dates (newer than the single `date` field). missing -> empty, GetDate
                // falls back to `date`. if the slot has a time but no per-show date, seed it from
                // `date` so the row shows something sensible and future writes can diverge.
                incoming.solosDate = JsonUtil.GetValue(entryJson, "solosDate");
                incoming.duosDate = JsonUtil.GetValue(entryJson, "duosDate");
                incoming.squadsDate = JsonUtil.GetValue(entryJson, "squadsDate");
                if (incoming.solos.HasValue && string.IsNullOrEmpty(incoming.solosDate)) { incoming.solosDate = incoming.date; upgraded = true; }
                if (incoming.duos.HasValue && string.IsNullOrEmpty(incoming.duosDate)) { incoming.duosDate = incoming.date; upgraded = true; }
                if (incoming.squads.HasValue && string.IsNullOrEmpty(incoming.squadsDate)) { incoming.squadsDate = incoming.date; upgraded = true; }

                if ((string.IsNullOrEmpty(id) && string.IsNullOrEmpty(name)) || !incoming.HasAny())
                    continue;

                string key = NormalizeKey(id, name);
                MergeInto(key, incoming);
                if (featured) _featured.Add(key);
            }
            return upgraded;
        }

        // merge incoming into the cached entry, keeping the faster time per slot (and any name/id).
        static void MergeInto(string key, Entry incoming)
        {
            if (!_cache.TryGetValue(key, out var existing))
            {
                _cache[key] = incoming;
                return;
            }
            if (string.IsNullOrEmpty(existing.displayName) && !string.IsNullOrEmpty(incoming.displayName))
                existing.displayName = incoming.displayName;
            if (string.IsNullOrEmpty(existing.rawId) && !string.IsNullOrEmpty(incoming.rawId))
                existing.rawId = incoming.rawId;
            // legacy date stays the earliest seen, used only as a back-compat fallback now
            existing.date = Earlier(existing.date, incoming.date);
            // per slot: pair the kept time with its own date so they don't drift apart
            (existing.solos, existing.solosDate) = FasterWithDate(existing.solos, existing.solosDate, incoming.solos, incoming.solosDate);
            (existing.duos, existing.duosDate) = FasterWithDate(existing.duos, existing.duosDate, incoming.duos, incoming.duosDate);
            (existing.squads, existing.squadsDate) = FasterWithDate(existing.squads, existing.squadsDate, incoming.squads, incoming.squadsDate);
            _cache[key] = existing;
        }

        static (float?, string) FasterWithDate(float? a, string aDate, float? b, string bDate)
        {
            if (!a.HasValue) return (b, bDate);
            if (!b.HasValue) return (a, aDate);
            return b.Value < a.Value ? (b, bDate) : (a, aDate);
        }

        static float? Faster(float? a, float? b)
        {
            if (!a.HasValue) return b;
            if (!b.HasValue) return a;
            return b.Value < a.Value ? b : a;
        }

        static string Today() => System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // earliest of two yyyy-MM-dd HH:mm:ss strings (string compare works for that format). nulls ignored.
        static string Earlier(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            if (string.IsNullOrEmpty(b)) return a;
            return string.CompareOrdinal(a, b) <= 0 ? a : b;
        }

        // disk-side counterpart to MergeUgcRevisionKeys: any ghost file or splash jpg that's named
        // with a "ugc-…_<n>" id gets renamed to the canonical id, or deleted if the canonical file
        // already exists. silent best-effort — failures don't matter, the in-cache merge already
        // unified the user-visible PB list.
        static void MergeUgcRevisionFiles()
        {
            string ghostDir = Path.Combine(SettingsDir, "ghosts");
            MergeUgcRevisionFilesIn(ghostDir, ".ghost");
            string dllDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            MergeUgcRevisionFilesIn(Path.Combine(dllDir, "CachedRoundSplashScreens"), ".jpg");
        }

        static void MergeUgcRevisionFilesIn(string dir, string ext)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (var path in Directory.GetFiles(dir, "ugc-*" + ext))
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    // strip the optional __solos/__duos/__squads suffix before canonicalizing the id
                    string showTag = "";
                    foreach (var tag in new[] { "__solos", "__duos", "__squads" })
                        if (name.EndsWith(tag)) { showTag = tag; name = name.Substring(0, name.Length - tag.Length); break; }
                    string canon = CanonicalRoundId(name);
                    if (canon == name) continue;
                    string newPath = Path.Combine(dir, canon + showTag + ext);
                    try
                    {
                        if (File.Exists(newPath)) File.Delete(path);
                        else File.Move(path, newPath);
                    }
                    catch (System.Exception ex) { Plugin.Log.LogWarning($"PBStore: file merge failed {path} -> {newPath}: {ex.Message}"); }
                }
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"PBStore: file merge scan failed for {dir}: {ex.Message}"); }
        }

        static bool TryImportOldPbTxt()
        {
            string path = Path.Combine(SettingsDir, "pb.txt");
            if (!File.Exists(path)) return false;

            try
            {
                bool changed = false;
                foreach (string line in File.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 1) continue;
                    string name = line.Substring(0, eq).Trim();
                    if (!float.TryParse(line.Substring(eq + 1).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float time))
                        continue;

                    // old pb.txt only ever held solo times
                    string key = NormalizeKey(name, name);
                    string nowDate = Today();
                    MergeInto(key, new Entry { displayName = name, rawId = name, solos = time, date = nowDate, solosDate = nowDate });
                    changed = true;
                }
                if (changed) Plugin.Log.LogInfo("PBStore: imported old pb.txt");
                return changed;
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"PBStore: pb.txt import failed: {ex.Message}");
                return false;
            }
        }

        static string JsonEscape(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        // collapses the legacy id-keyed entry into the name-keyed one (same migration the old store
        // did). runs on read/write so different show IDs all converge onto one name entry.
        static void CollapseIdKey(string id, string displayName, string key)
        {
            if (IsUgc(id) || string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(id)) return;
            if (id == key) return;
            if (!_cache.TryGetValue(id, out var idEntry)) return;
            idEntry.displayName = displayName;
            MergeInto(key, idEntry);
            bool wasFeatured = _featured.Remove(id);
            _cache.Remove(id);
            if (wasFeatured) _featured.Add(key);
        }

        public static bool TryGet(string id, out float time, out string displayName, string displayNameHint = null)
            => TryGet(id, CurrentType(), out time, out displayName, displayNameHint);

        public static bool TryGet(string id, PbType type, out float time, out string displayName, string displayNameHint = null)
        {
            if (!BetterFG.Features.FeatureRegistry.IsOn("pb", "store"))
            {
                time = 0f;
                displayName = null;
                return false;
            }
            EnsureLoaded();
            string key = NormalizeKey(id, displayNameHint);

            bool collapsed = !IsUgc(id) && !string.IsNullOrEmpty(displayNameHint) && !string.IsNullOrEmpty(id) && _cache.ContainsKey(id);
            if (collapsed) { CollapseIdKey(id, displayNameHint, key); Save(); }

            if (_cache.TryGetValue(key, out var entry))
            {
                var t = entry.Get(type);
                if (t.HasValue) { time = t.Value; displayName = entry.displayName; return true; }
            }
            // fallback: name-prefixed key
            if (!key.StartsWith("name:") && !IsUgc(id))
            {
                if (_cache.TryGetValue("name:" + id, out entry))
                {
                    var t = entry.Get(type);
                    if (t.HasValue) { time = t.Value; displayName = entry.displayName; return true; }
                }
            }
            time = 0f;
            displayName = null;
            return false;
        }

        static bool IsUgc(string id) => !string.IsNullOrEmpty(id) && id.StartsWith("ugc-");

        // explore mode hands us ids like "ugc-2012-1608-1580_15" where the trailing "_<digits>" is
        // the revision number. the same level loaded from a share-code or normal play is just
        // "ugc-2012-1608-1580". collapse them so one level == one PB/ghost/splash regardless of
        // which entry point you came in through.
        public static string CanonicalRoundId(string id)
        {
            if (!IsUgc(id)) return id;
            int us = id.LastIndexOf('_');
            if (us <= 0 || us == id.Length - 1) return id;
            for (int i = us + 1; i < id.Length; i++)
                if (id[i] < '0' || id[i] > '9') return id;
            return id.Substring(0, us);
        }

        static string NormalizeKey(string id, string displayName)
        {
            id = CanonicalRoundId(id);
            if (IsUgc(id)) return id;
            if (!string.IsNullOrEmpty(displayName)) return "name:" + displayName;
            if (!string.IsNullOrEmpty(id)) return id;
            return "unknown";
        }

        // sets the time for the CURRENT show type. returns true if it's a new pb for that show.
        public static bool TrySet(string id, string displayName, float time)
            => TrySet(id, displayName, CurrentType(), time);

        public static bool TrySet(string id, string displayName, PbType type, float time)
        {
            if (!BetterFG.Features.FeatureRegistry.IsOn("pb", "store")) return false;
            EnsureLoaded();
            string key = NormalizeKey(id, displayName);
            CollapseIdKey(id, displayName, key);

            _cache.TryGetValue(key, out var entry);
            entry.displayName = displayName;
            entry.rawId = id;
            var existing = entry.Get(type);
            if (existing.HasValue && existing.Value <= time)
            {
                if (string.IsNullOrEmpty(entry.date)) entry.date = Today();
                if (string.IsNullOrEmpty(entry.GetDate(type))) entry.SetDate(type, entry.date);
                _cache[key] = entry;
                Save();
                RaiseChanged();
                return false;
            }
            // new/faster time for this show -> stamp the per-show date
            entry.Set(type, time);
            string now = Today();
            entry.SetDate(type, now);
            if (string.IsNullOrEmpty(entry.date)) entry.date = now;
            _cache[key] = entry;
            Save();
            RaiseChanged();
            return true;
        }

        // overwrite the PB for this show regardless of whether the new time is faster — used when
        // the player explicitly chooses to set a slower run as their PB (e.g. resetting after a
        // fluke time they don't want to defend). bypasses the faster-than gate TrySet enforces.
        public static void ForceSet(string id, string displayName, PbType type, float time)
        {
            if (!BetterFG.Features.FeatureRegistry.IsOn("pb", "store")) return;
            EnsureLoaded();
            string key = NormalizeKey(id, displayName);
            CollapseIdKey(id, displayName, key);

            _cache.TryGetValue(key, out var entry);
            entry.displayName = displayName;
            entry.rawId = id;
            entry.Set(type, time);
            string now = Today();
            entry.SetDate(type, now);
            if (string.IsNullOrEmpty(entry.date)) entry.date = now;
            _cache[key] = entry;
            Save();
            RaiseChanged();
        }

        // ── menu list helpers ─────────────────────────────────────────────────
        // these keep the (displayName, time, rawId) tuple shape the UI already consumes. the `time`
        // they hand back depends on what the caller asks for: a specific show type, or the fastest.

        // entries that have a time for `type`, deduped by display name, best time per name.
        public static Dictionary<string, (string displayName, float time, string rawId)> GetAllDeduped(PbType type)
            => Project(e => e.Get(type), onlyFeatured: false);

        // entries deduped by name, time = fastest of solos/duos/squads. used by the popup.
        public static Dictionary<string, (string displayName, float time, string rawId)> GetAllDedupedBest()
            => Project(e => e.Best(), onlyFeatured: false);

        public static Dictionary<string, (string displayName, float time, string rawId)> GetFeatured(PbType type)
            => Project(e => e.Get(type), onlyFeatured: true);

        public static Dictionary<string, (string displayName, float time, string rawId)> GetFeaturedBest()
            => Project(e => e.Best(), onlyFeatured: true);

        static Dictionary<string, (string displayName, float time, string rawId)> Project(System.Func<Entry, float?> pick, bool onlyFeatured)
        {
            var result = new Dictionary<string, (string displayName, float time, string rawId)>();
            if (!BetterFG.Features.FeatureRegistry.IsOn("pb", "store")) return result;
            EnsureLoaded();
            foreach (var kv in _cache)
            {
                if (onlyFeatured && !_featured.Contains(kv.Key)) continue;
                var t = pick(kv.Value);
                if (!t.HasValue) continue;
                string nameKey = !string.IsNullOrEmpty(kv.Value.displayName) ? kv.Value.displayName : kv.Key;
                if (!result.TryGetValue(nameKey, out var existing) || t.Value < existing.time)
                    result[nameKey] = (kv.Value.displayName, t.Value, kv.Value.rawId);
            }
            return result;
        }

        // every entry deduped by display name, carrying all three per-show times. used by the PB tab
        // so it can build rows once and switch shows without rebuilding.
        public static Dictionary<string, Entry> GetAllEntriesDeduped()
        {
            var result = new Dictionary<string, Entry>();
            if (!BetterFG.Features.FeatureRegistry.IsOn("pb", "store")) return result;
            EnsureLoaded();
            foreach (var kv in _cache)
            {
                string nameKey = !string.IsNullOrEmpty(kv.Value.displayName) ? kv.Value.displayName : kv.Key;
                if (!result.TryGetValue(nameKey, out var existing))
                {
                    result[nameKey] = kv.Value;
                    continue;
                }
                // merge: keep faster time per slot (two raw entries can collapse to the same name)
                existing.solos = Faster(existing.solos, kv.Value.solos);
                existing.duos = Faster(existing.duos, kv.Value.duos);
                existing.squads = Faster(existing.squads, kv.Value.squads);
                if (string.IsNullOrEmpty(existing.rawId)) existing.rawId = kv.Value.rawId;
                result[nameKey] = existing;
            }
            return result;
        }

        public static bool IsFeatured(string id, string displayNameHint = null)
        {
            if (!BetterFG.Features.FeatureRegistry.IsOn("pb", "store")) return false;
            EnsureLoaded();
            return _featured.Contains(NormalizeKey(id, displayNameHint));
        }

        // toggles featured state, returns new state
        public static bool TryFeature(string id, string displayNameHint = null)
        {
            if (!BetterFG.Features.FeatureRegistry.IsOn("pb", "store")) return false;
            EnsureLoaded();
            string key = NormalizeKey(id, displayNameHint);
            if (!_cache.ContainsKey(key)) return false;
            if (_featured.Contains(key))
                _featured.Remove(key);
            else
                _featured.Add(key);
            Save();
            RaiseChanged();
            return _featured.Contains(key);
        }

        public static bool TryDelete(string id, string displayNameHint = null)
        {
            if (!BetterFG.Features.FeatureRegistry.IsOn("pb", "store")) return false;
            EnsureLoaded();
            bool changed = false;
            string canonicalId = CanonicalRoundId(id);
            string key = NormalizeKey(id, displayNameHint);
            if (!string.IsNullOrEmpty(key))
            {
                changed |= _cache.Remove(key);
                _featured.Remove(key);
            }
            if (!IsUgc(canonicalId) && !string.IsNullOrEmpty(canonicalId))
            {
                changed |= _cache.Remove(canonicalId);
                _featured.Remove(canonicalId);
            }
            if (!string.IsNullOrEmpty(displayNameHint))
            {
                string nameKey = "name:" + displayNameHint;
                changed |= _cache.Remove(nameKey);
                _featured.Remove(nameKey);
            }
            if (changed) { Save(); RaiseChanged(); }
            return changed;
        }
    }

    internal static class PBObf
    {
        static readonly byte[] _magic = { 0x42, 0x46, 0x47, 0x50, 0x42, 0x32, 0x00 };
        static readonly byte[] _key = { 0x4B, 0x9F, 0x2D, 0xE1, 0x76, 0xC3, 0x58, 0xAA, 0x1E, 0x84, 0x3C, 0xF0, 0x67, 0xB2, 0x29, 0x5D };
        static readonly byte[] _shuffle = { 7, 2, 13, 0, 10, 5, 15, 3, 9, 14, 1, 6, 12, 4, 11, 8 };
        static readonly byte[] _unshuffle;

        static PBObf()
        {
            _unshuffle = new byte[16];
            for (int i = 0; i < 16; i++) _unshuffle[_shuffle[i]] = (byte)i;
        }

        public static byte[] Encode(string json)
        {
            byte[] data = System.Text.Encoding.UTF8.GetBytes(json);
            var result = new byte[_magic.Length + data.Length];
            System.Buffer.BlockCopy(_magic, 0, result, 0, _magic.Length);
            for (int i = 0; i < data.Length; i++)
                result[_magic.Length + i] = (byte)(data[i] ^ _key[i % _key.Length]);
            return result;
        }

        public static string Decode(byte[] data)
        {
            if (data == null || data.Length == 0) return "";

            if (HasMagic(data))
            {
                var fresh = new byte[data.Length - _magic.Length];
                for (int i = 0; i < fresh.Length; i++)
                    fresh[i] = (byte)(data[_magic.Length + i] ^ _key[i % _key.Length]);
                return System.Text.Encoding.UTF8.GetString(fresh);
            }

            string plain = System.Text.Encoding.UTF8.GetString(data);
            if (plain.TrimStart().StartsWith("{")) return plain;

            var result = new byte[data.Length];
            for (int b = 0; b < data.Length; b += 16)
            {
                int len = System.Math.Min(16, data.Length - b);
                if (len < 16) return "";
                for (int i = 0; i < len; i++) result[b + i] = data[b + _shuffle[i] % len];
            }
            for (int i = 0; i < result.Length; i++) result[i] ^= _key[i % _key.Length];
            return System.Text.Encoding.UTF8.GetString(result);
        }

        static bool HasMagic(byte[] data)
        {
            if (data.Length < _magic.Length) return false;
            for (int i = 0; i < _magic.Length; i++)
                if (data[i] != _magic[i]) return false;
            return true;
        }
    }
}
