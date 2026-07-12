using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;

namespace BetterFG.Customization.Player
{
    public class SkinCatalogService : MonoBehaviour
    {
        public event Action<List<SkinInfo>> OnSkinsLoaded;
        public event Action<string> OnStatusUpdate;
        public event Action OnFetchStarted;
        public event Action OnFetchCompleted;
        public event Action<string, Texture2D> OnSkinCoverLoaded;


        private readonly List<SkinInfo> availableSkins = new List<SkinInfo>();
        private readonly Dictionary<string, Texture2D> _coverCache = new Dictionary<string, Texture2D>();
        private readonly HashSet<string> _coverLoading = new HashSet<string>();
        private readonly HashSet<string> _fetchedRepoUrls = new HashSet<string>();
        private int _pendingFetches = 0;
        private bool isFetching = false;
        private int _fetchGen = 0; // bumped on ClearCache, stale coroutines bail when gen mismatches
        private int _catalogTotal = 0; // total paths across catalogs we've parsed this session
        private readonly Dictionary<string, int> _catalogTotalByRepo = new Dictionary<string, int>();

        public List<SkinInfo> AvailableSkins => new List<SkinInfo>(availableSkins);
        public bool IsFetching => isFetching;
        public int CatalogTotal => _catalogTotal;     // how many entries the catalog(s) say exist
        public int FetchedCount => availableSkins.Count; // how many we've actually pulled info.json for
        public bool IsFetchedRepo(string githubUrl) => _fetchedRepoUrls.Contains(githubUrl);
        public int GetCatalogTotalForRepo(string repoRaw) => string.IsNullOrEmpty(repoRaw) ? 0 : _catalogTotalByRepo.TryGetValue(repoRaw, out int v) ? v : 0;
        public int GetFetchedCountForRepo(string repoRaw)
        {
            if (string.IsNullOrEmpty(repoRaw)) return 0;
            int c = 0;
            for (int i = 0; i < availableSkins.Count; i++)
                if (availableSkins[i].sourceRepo == repoRaw) c++;
            return c;
        }
        public bool TryGetCover(SkinInfo skin, out Texture2D tex)
        {
            string key = CoverKey(skin);
            if (!string.IsNullOrEmpty(key) && _coverCache.TryGetValue(key, out tex) && tex != null && tex.width > 0) return true;
            tex = null;
            return false;
        }

        public void FetchSkins() => FetchSkins(null);

        public void FetchSkins(SkinRepo repo)
        {
            var target = repo ?? RepoRegistry.Instance?.Active;
            if (target == null) { OnStatusUpdate?.Invoke("No repo selected"); return; }
            if (_fetchedRepoUrls.Contains(target.githubUrl)) return;
            _fetchedRepoUrls.Add(target.githubUrl);
            _pendingFetches++;
            isFetching = true;
            if (_pendingFetches == 1) OnFetchStarted?.Invoke();
            StartCoroutine(FetchCoroutine(target.RawBase, target, _fetchGen).WrapToIl2Cpp());
        }

        // call this only when u want a hard refresh (manual fetch button)
        public void ClearCache()
        {
            _fetchGen++; // invalidates any running coroutines
            _fetchedRepoUrls.Clear();
            availableSkins.Clear();
            _coverCache.Clear();
            _coverLoading.Clear();
            _catalogTotalByRepo.Clear();
            _pendingFetches = 0;
            _catalogTotal = 0;
            isFetching = false;
        }

        public void EnsureCover(SkinInfo skin, bool force = false)
        {
            if (skin == null || skin.isLocalImport || string.IsNullOrEmpty(skin.file) || string.IsNullOrEmpty(skin.sourceRepo)) return;
            string key = CoverKey(skin);
            if (string.IsNullOrEmpty(key)) return;
            if (!force && _coverCache.TryGetValue(key, out var tex) && tex != null && tex.width > 0) return;
            if (_coverLoading.Contains(key)) return;

            string folder = !string.IsNullOrEmpty(skin.repoFolder) ? skin.repoFolder : $"{GetCategoryFolder(skin.type)}/{skin.file}";
            string[] parts = folder.Split('/');
            if (parts.Length < 2) return;
            _coverLoading.Add(key);
            StartCoroutine(LoadCover(parts[0], parts[1], key, skin.sourceRepo).WrapToIl2Cpp());
        }

        private IEnumerator FetchCoroutine(string repoRaw, SkinRepo target, int gen)
        {
            string label = target?.repoName ?? repoRaw;
            OnStatusUpdate?.Invoke($"Fetching {label}...");

            // new catalog first: catalog2.json carries every skin's whole info.json inline, so the
            // list populates in ONE request instead of one info.json fetch per folder. the old
            // per-folder loop hammered github raw ~50+ times in a burst and got rate-limited (429s),
            // which is what turned a fetch into minutes. repos that predate catalog2.json 404 here and
            // fall through to the flat catalog.json path below — nothing breaks, they're just slower.
            UnityWebRequest newReq = UnityWebRequest.Get($"{repoRaw}/catalog2.json");
            yield return newReq.SendWebRequest();
            if (_fetchGen != gen) { newReq.Dispose(); yield break; }

            if (newReq.result == UnityWebRequest.Result.Success &&
                TryLoadNewCatalog(newReq.downloadHandler.text, repoRaw, label, out int newCount))
            {
                newReq.Dispose();
                _pendingFetches--;
                OnStatusUpdate?.Invoke($"{label}: {newCount} loaded");
                OnSkinsLoaded?.Invoke(new List<SkinInfo>(availableSkins));
                if (_pendingFetches <= 0) { _pendingFetches = 0; isFetching = false; OnFetchCompleted?.Invoke(); }
                yield break;
            }
            newReq.Dispose();

            // grab catalog.json from repo root instead of hitting the github api per folder
            UnityWebRequest catReq = UnityWebRequest.Get($"{repoRaw}/catalog.json");
            yield return catReq.SendWebRequest();

            if (_fetchGen != gen) { catReq.Dispose(); yield break; }

            if (catReq.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[SkinCatalog] catalog.json fetch failed: {catReq.error}");
                OnStatusUpdate?.Invoke($"Failed to fetch {label}");
                catReq.Dispose();
                _pendingFetches--;
                if (_pendingFetches <= 0) { _pendingFetches = 0; isFetching = false; OnFetchCompleted?.Invoke(); }
                yield break;
            }

            List<string> paths = ParseCatalog(catReq.downloadHandler.text);
            catReq.Dispose();
            _catalogTotal += paths.Count;
            _catalogTotalByRepo[repoRaw] = paths.Count;

            int total = paths.Count;
            int loaded = 0;
            foreach (string path in paths)
            {
                if (_fetchGen != gen) yield break;

                // path is like "Costumes/my_costume" — derive type from folder prefix
                string typeStr = GetTypeFromPath(path);
                if (string.IsNullOrEmpty(typeStr)) continue;
                string[] parts = path.Split('/');
                string folder = parts.Length >= 1 ? parts[0] : path;
                string sub = parts.Length >= 2 ? parts[1] : path;

                UnityWebRequest infoReq = UnityWebRequest.Get($"{repoRaw}/{path}/info.json");
                yield return infoReq.SendWebRequest();

                if (_fetchGen != gen) { infoReq.Dispose(); yield break; }

                if (infoReq.result == UnityWebRequest.Result.Success)
                {
                    SkinInfo skin = ParseSkinInfo(infoReq.downloadHandler.text, sub, typeStr);
                    if (skin != null)
                    {
                        skin.repoFolder = path;
                        skin.sourceRepo = repoRaw;
                        availableSkins.Add(skin);
                        EnsureCover(skin);
                    }
                }

                infoReq.Dispose();
                loaded++;
                OnStatusUpdate?.Invoke($"{label}: {loaded}/{total}");
            }

            if (_fetchGen != gen) yield break;

            _pendingFetches--;
            OnStatusUpdate?.Invoke($"{label}: {loaded}/{total} loaded");
            OnSkinsLoaded?.Invoke(new List<SkinInfo>(availableSkins));
            if (_pendingFetches <= 0)
            {
                _pendingFetches = 0;
                isFetching = false;
                OnFetchCompleted?.Invoke();
            }
        }

        private IEnumerator LoadCover(string category, string folder, string key, string repoRaw)
        {
            // try jpg first, fall back to png
            string[] attempts = {
                $"{repoRaw}/{category}/{folder}/cover.jpg",
                $"{repoRaw}/{category}/{folder}/cover.png",
            };

            foreach (string url in attempts)
            {
                UnityWebRequest req = UnityWebRequestTexture.GetTexture(url);
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success)
                {
                    var coverTex = DownloadHandlerTexture.GetContent(req);
                    UnityEngine.Object.DontDestroyOnLoad(coverTex);
                    _coverCache[key] = coverTex;
                    _coverLoading.Remove(key);
                    OnSkinCoverLoaded?.Invoke(key, coverTex);
                    req.Dispose();
                    yield break;
                }
                req.Dispose();
            }
            _coverLoading.Remove(key);
        }

        private static string CoverKey(SkinInfo skin)
        {
            if (skin == null || string.IsNullOrEmpty(skin.file)) return null;
            string repo = !string.IsNullOrEmpty(skin.sourceRepo) ? skin.sourceRepo : "";
            return repo + "|" + skin.file;
        }

        private static string GetCategoryFolder(string typeStr)
        {
            switch (SkinTypeParser.FromString(typeStr))
            {
                case SkinType.Costume: return "Costumes";
                case SkinType.Accessory: return "Accessories";
                case SkinType.Item: return "Items";
                case SkinType.Plinth: return "Plinths";
                case SkinType.Emote: return "Emotes";
                default: return "Costumes";
            }
        }

        private static List<BoneOffsetEntry> ParseBoneOffsets(string json)
        {
            var offsets = new List<BoneOffsetEntry>();
            int boIdx = json.IndexOf("\"boneOffsets\"", StringComparison.OrdinalIgnoreCase);
            if (boIdx == -1) return offsets;
            int arrStart = json.IndexOf('[', boIdx);
            if (arrStart == -1) return offsets;
            int depth = 0, arrEnd = -1;
            for (int i = arrStart; i < json.Length; i++)
            {
                if (json[i] == '[') depth++;
                else if (json[i] == ']') { depth--; if (depth == 0) { arrEnd = i; break; } }
            }
            if (arrEnd == -1) return offsets;
            string arr = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
            int idx = 0;
            while (idx < arr.Length)
            {
                int os = arr.IndexOf('{', idx); if (os == -1) break;
                int oe = -1; int d = 0;
                for (int i = os; i < arr.Length; i++)
                {
                    if (arr[i] == '{') d++;
                    else if (arr[i] == '}') { d--; if (d == 0) { oe = i; break; } }
                }
                if (oe == -1) break;
                string obj = arr.Substring(os, oe - os + 1);
                string bone = GetJsonValue(obj, "bone");
                float x = GetJsonFloat(obj, "x"), y = GetJsonFloat(obj, "y"), z = GetJsonFloat(obj, "z");
                if (!string.IsNullOrEmpty(bone))
                    offsets.Add(new BoneOffsetEntry { bone = bone, localPosition = new Vector3(x, y, z) });
                idx = oe + 1;
            }
            return offsets;
        }

        // new catalog: a json array of objects, each = one skin's whole info.json plus a "path"
        // field ("Costumes/foo"). we run the SAME ParseSkinInfo the per-skin path uses so a skin
        // applies identically whichever route loaded it (keepBase/boneOffsets included — getting
        // that wrong is exactly how a keepBase costume ends up hiding the bean). returns false if
        // the payload isn't the object-array shape (e.g. an old flat catalog.json served at this
        // name) so the caller falls back cleanly.
        private bool TryLoadNewCatalog(string json, string repoRaw, string label, out int count)
        {
            count = 0;
            if (string.IsNullOrEmpty(json)) return false;
            int arrStart = json.IndexOf('[');
            if (arrStart == -1) return false;
            // first non-space after '[' must be '{' for this to be the fat shape
            int p = arrStart + 1;
            while (p < json.Length && char.IsWhiteSpace(json[p])) p++;
            if (p >= json.Length || json[p] != '{') return false;

            int added = 0;
            int idx = arrStart + 1;
            while (idx < json.Length)
            {
                int os = json.IndexOf('{', idx);
                if (os == -1) break;
                int oe = FindMatchingBrace(json, os);
                if (oe == -1) break;
                string obj = json.Substring(os, oe - os + 1);
                idx = oe + 1;

                string path = GetJsonValue(obj, "path");
                if (string.IsNullOrEmpty(path)) continue;
                string typeStr = GetTypeFromPath(path);
                if (string.IsNullOrEmpty(typeStr)) continue;
                string[] parts = path.Split('/');
                string sub = parts.Length >= 2 ? parts[1] : path;

                SkinInfo skin = ParseSkinInfo(obj, sub, typeStr);
                if (skin == null) continue;
                skin.repoFolder = path;
                skin.sourceRepo = repoRaw;
                availableSkins.Add(skin);
                EnsureCover(skin);
                added++;
            }

            if (added == 0) return false;
            _catalogTotal += added;
            _catalogTotalByRepo[repoRaw] = added;
            count = added;
            Debug.Log($"[SkinCatalog] {label}: fat catalog2.json, {added} skins in one request");
            return true;
        }

        // parses catalog.json — just a flat json array of strings like ["Costumes/foo","Items/bar"]
        private static List<string> ParseCatalog(string json)
        {
            var paths = new List<string>();
            int i = json.IndexOf('[');
            if (i == -1) return paths;
            int end = json.LastIndexOf(']');
            if (end == -1) return paths;
            string inner = json.Substring(i + 1, end - i - 1);
            foreach (string part in inner.Split(','))
            {
                string trimmed = part.Trim().Trim('"');
                if (!string.IsNullOrEmpty(trimmed))
                    paths.Add(trimmed);
            }
            return paths;
        }

        private static string GetTypeFromPath(string path)
        {
            if (path.StartsWith("Costumes", StringComparison.OrdinalIgnoreCase)) return "costume";
            if (path.StartsWith("Accessories", StringComparison.OrdinalIgnoreCase)) return "accessory";
            if (path.StartsWith("Items", StringComparison.OrdinalIgnoreCase)) return "item";
            if (path.StartsWith("Plinths", StringComparison.OrdinalIgnoreCase)) return "plinth";
            if (path.StartsWith("Emotes", StringComparison.OrdinalIgnoreCase)) return "emote";
            return null;
        }

        private static int FindMatchingBrace(string s, int start)
        {
            int depth = 0;
            bool inString = false;
            for (int i = start; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\\' && inString) { i++; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static SkinInfo ParseSkinInfo(string json, string folder, string fallbackType)
        {
            try
            {
                var skin = new SkinInfo
                {
                    name = GetJsonValue(json, "name"),
                    author = GetJsonValue(json, "author"),
                    description = GetJsonValue(json, "description"),
                    group = GetJsonValue(json, "group"),
                    file = GetJsonValue(json, "file"),
                    type = fallbackType,
                    infoFetched = true,
                };

                if (string.IsNullOrEmpty(skin.name) || string.IsNullOrEmpty(skin.file))
                    return null;

                if (fallbackType == "costume")
                {
                    skin.keepBase = GetJsonBool(json, "keepBase");
                    skin.skinScale = GetJsonFloat(json, "skinScale");
                    skin.boneOffsets = ParseBoneOffsets(json);
                }
                else if (fallbackType == "item")
                {
                    skin.scale = GetJsonFloat(json, "scale", 1f);
                    skin.left = ParseHandInfo(json, "left");
                    skin.right = ParseHandInfo(json, "right");
                }
                else if (fallbackType == "emote")
                {
                    // clip name isn't stored — the mod always plays the first clip in the bundle
                    skin.audio = GetJsonValue(json, "audio");
                }

                return skin;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SkinCatalog] Parse error for {folder}: {e.Message}");
                return null;
            }
        }

        // fills the item-specific fields (scale + per-hand position/rotation) onto an existing
        // SkinInfo from a raw info.json string. used by the remote pipeline, which fetches info.json
        // but otherwise only reads skinScale — items need this hand data to place on the hand.
        public static void FillItemInfoFromJson(SkinInfo skin, string json)
        {
            if (skin == null || string.IsNullOrEmpty(json)) return;
            skin.scale = GetJsonFloat(json, "scale", 1f);
            skin.left = ParseHandInfo(json, "left");
            skin.right = ParseHandInfo(json, "right");
        }

        private static ItemHandInfo ParseHandInfo(string json, string key)
        {
            string searchKey = $"\"{key}\":";
            int ki = json.IndexOf(searchKey);
            if (ki == -1) return null;

            int objStart = json.IndexOf('{', ki + searchKey.Length);
            if (objStart == -1) return null;
            int objEnd = FindMatchingBrace(json, objStart);
            if (objEnd == -1) return null;

            string obj = json.Substring(objStart, objEnd - objStart + 1);

            var info = new ItemHandInfo();
            info.position = ParseFloatArray(obj, "position");
            info.rotation = ParseFloatArray(obj, "rotation");
            return info;
        }

        private static float[] ParseFloatArray(string json, string key)
        {
            string searchKey = $"\"{key}\":";
            int ki = json.IndexOf(searchKey);
            if (ki == -1) return new float[3];

            int arrStart = json.IndexOf('[', ki + searchKey.Length);
            if (arrStart == -1) return new float[3];
            int arrEnd = json.IndexOf(']', arrStart);
            if (arrEnd == -1) return new float[3];

            string inner = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
            string[] parts = inner.Split(',');
            var result = new float[3];
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            for (int i = 0; i < 3 && i < parts.Length; i++)
                float.TryParse(parts[i].Trim(), System.Globalization.NumberStyles.Float, ci, out result[i]);
            return result;
        }

        private static string GetJsonValue(string json, string key)
        {
            string searchKey = $"\"{key}\":";
            int i = json.IndexOf(searchKey);
            if (i == -1) return "";
            i += searchKey.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            if (i >= json.Length || json[i] != '"') return "";
            i++;
            int end = json.IndexOf('"', i);
            return end == -1 ? "" : json.Substring(i, end - i);
        }

        private static bool GetJsonBool(string json, string key)
        {
            string searchKey = $"\"{key}\":";
            int i = json.IndexOf(searchKey);
            if (i == -1) return false;
            i += searchKey.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            return i + 3 < json.Length && json.Substring(i, 4) == "true";
        }

        private static float GetJsonFloat(string json, string key, float defaultVal = 0f)
        {
            string searchKey = $"\"{key}\":";
            int i = json.IndexOf(searchKey);
            if (i == -1) return defaultVal;
            i += searchKey.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            int end = i;
            while (end < json.Length && "0123456789+-.eE".IndexOf(json[end]) != -1) end++;
            return float.TryParse(json.Substring(i, end - i),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : defaultVal;
        }
    }
}
