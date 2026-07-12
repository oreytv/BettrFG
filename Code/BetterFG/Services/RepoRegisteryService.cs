using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
namespace BetterFG.Services
{
    public class SkinRepo
    {
        public string githubUrl;   // e.g. https://github.com/oreytv/BetterFGPublicSkins
        public string author;      // parsed from url
        public string repoName;    // parsed from url
        public bool isDefault;     // can't be removed

        public string RawBase => $"https://raw.githubusercontent.com/{author}/{repoName}/main";
        public string ApiBase => $"https://api.github.com/repos/{author}/{repoName}/contents";
        public string DisplayName => $"{author} / {repoName}";
    }

    public class RepoRegistry : MonoBehaviour
    {
        public static RepoRegistry Instance { get; private set; }

        public event Action OnReposChanged;
        public event Action<string> OnValidationStatus;
        public event Action<SkinRepo, Texture2D> OnCoverLoaded;

        public const string DEFAULT_GITHUB_URL = "https://github.com/oreytv/BetterFGPublicSkins";
        public const long MAX_BUNDLE_BYTES = 4_048_576; // temu 4 MB

        private const string SETTINGS_KEY = "repos.list";
        private readonly Dictionary<string, Texture2D> _coverCache = new Dictionary<string, Texture2D>();
        private readonly HashSet<string> _coverLoading = new HashSet<string>();

        private readonly List<SkinRepo> _repos = new List<SkinRepo>();
        public IReadOnlyList<SkinRepo> Repos => _repos;

        private SkinRepo _active;
        public SkinRepo Active => _active ?? (_repos.Count > 0 ? _repos[0] : null);

        private const string KEY_ACTIVE = "repos.active";

        public Texture2D GetCover(SkinRepo repo)
        {
            if (repo == null) return null;
            return _coverCache.TryGetValue(repo.githubUrl, out var tex) && tex != null && tex.width > 0 ? tex : null;
        }

        // returns sourceRepo if set, otherwise falls back to active repo raw base
        public static string ResolveRaw(string sourceRepo)
        {
            if (!string.IsNullOrEmpty(sourceRepo)) return sourceRepo;
            return Instance?.Active?.RawBase ?? DEFAULT_RAW;
        }

        public const string DEFAULT_RAW = "https://raw.githubusercontent.com/oreytv/BetterFGPublicSkins/main";

        public void FetchCover(SkinRepo repo)
        {
            if (repo == null) return;
            if (GetCover(repo) != null || _coverLoading.Contains(repo.githubUrl)) return;
            _coverLoading.Add(repo.githubUrl);
            StartCoroutine(FetchCoverCoroutine(repo).WrapToIl2Cpp());
        }

        private IEnumerator FetchCoverCoroutine(SkinRepo repo)
        {
            foreach (string coverUrl in new[] { $"{repo.RawBase}/cover.png", $"{repo.RawBase}/cover.jpg" })
            {
                var req = UnityWebRequest.Get(coverUrl);
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    var bytes = req.downloadHandler.data;
                    if (bytes != null && bytes.Length > 0)
                    {
                        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                        if (tex.LoadImage(bytes))
                        {
                            UnityEngine.Object.DontDestroyOnLoad(tex);
                            _coverCache[repo.githubUrl] = tex;
                            _coverLoading.Remove(repo.githubUrl);
                            OnCoverLoaded?.Invoke(repo, tex);
                            req.Dispose();
                            yield break;
                        }
                    }
                }
                req.Dispose();
            }
            _coverLoading.Remove(repo.githubUrl);
        }

        void Awake()
        {
            Instance = this;
            Load();
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void SetActive(SkinRepo repo)
        {
            if (repo == null || !_repos.Contains(repo)) return;
            _active = repo;
            SettingsService.Set(KEY_ACTIVE, repo.githubUrl);
            OnReposChanged?.Invoke();
        }

        /// <summary>
        /// Validates then adds a repo. Fires OnValidationStatus with result, then OnReposChanged on success.
        /// </summary>
        public void AddRepo(string githubUrl)
        {
            string normalized = NormalizeUrl(githubUrl);
            if (string.IsNullOrEmpty(normalized))
            {
                OnValidationStatus?.Invoke("Invalid URL");
                return;
            }

            foreach (var r in _repos)
                if (string.Equals(r.githubUrl, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    OnValidationStatus?.Invoke("Already added");
                    return;
                }

            var repo = ParseRepo(normalized);
            if (repo == null) { OnValidationStatus?.Invoke("Couldn't parse repo URL"); return; }

            OnValidationStatus?.Invoke($"Validating {repo.DisplayName}...");
            StartCoroutine(ValidateAndAdd(repo).WrapToIl2Cpp());
        }

        public void RemoveRepo(SkinRepo repo)
        {
            if (repo == null || repo.isDefault) return;
            _repos.Remove(repo);
            if (_active == repo) _active = _repos.Count > 0 ? _repos[0] : null;
            Save();
            OnReposChanged?.Invoke();
        }

        // ── Validation ────────────────────────────────────────────────────────

        private IEnumerator ValidateAndAdd(SkinRepo repo)
        {
            bool ok = false;
            foreach (string marker in new[] { "betterfg", "bettrfg" })
            {
                string markerUrl = $"{repo.RawBase}/{marker}";
                var req = UnityWebRequest.Head(markerUrl);
                yield return req.SendWebRequest();

                ok = req.result == UnityWebRequest.Result.Success && req.responseCode == 200;
                req.Dispose();

                if (!ok)
                {
                    // try GET fallback, some raw hosts don't support HEAD
                    req = UnityWebRequest.Get(markerUrl);
                    yield return req.SendWebRequest();
                    ok = req.result == UnityWebRequest.Result.Success;
                    req.Dispose();
                }

                if (ok) break;
            }

            if (!ok)
            {
                OnValidationStatus?.Invoke($"Not a valid BetterFG repo: {repo.DisplayName}");
                yield break;
            }

            _repos.Add(repo);
            Save();
            OnValidationStatus?.Invoke($"Added {repo.DisplayName}");
            OnReposChanged?.Invoke();
        }

        // ── Bundle size gate (used by SkinLoaderService) ──────────────────────

        /// <summary>
        /// Checks Content-Length header before downloading. Returns false + error if over 1 MB.
        /// Call before issuing a full GET on a bundle URL.
        /// </summary>
        public static IEnumerator CheckBundleSize(string url, Action<bool, string> callback)
        {
            var req = UnityWebRequest.Head(url);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                req.Dispose();
                // can't check, let it through and fail at load if broken
                callback(true, null);
                yield break;
            }

            string cl = req.GetResponseHeader("Content-Length");
            req.Dispose();

            if (!string.IsNullOrEmpty(cl) && long.TryParse(cl, out long size) && size > MAX_BUNDLE_BYTES)
            {
                callback(false, $"Bundle too large ({size / 1024}KB > 1MB), skipping");
                yield break;
            }

            callback(true, null);
        }

        // ── Persist ───────────────────────────────────────────────────────────

        private void Load()
        {
            _repos.Clear();

            // default is always first and can't be removed
            var def = ParseRepo(DEFAULT_GITHUB_URL);
            def.isDefault = true;
            _repos.Add(def);

            string raw = SettingsService.Get(SETTINGS_KEY, "");
            if (!string.IsNullOrEmpty(raw))
            {
                foreach (string url in raw.Split('|'))
                {
                    string u = url.Trim();
                    if (string.IsNullOrEmpty(u)) continue;
                    if (string.Equals(u, DEFAULT_GITHUB_URL, StringComparison.OrdinalIgnoreCase)) continue;
                    var r = ParseRepo(u);
                    if (r != null) _repos.Add(r);
                }
            }

            string activeUrl = SettingsService.Get(KEY_ACTIVE, DEFAULT_GITHUB_URL);
            _active = _repos.Find(r => string.Equals(r.githubUrl, activeUrl, StringComparison.OrdinalIgnoreCase))
                      ?? _repos[0];
        }

        private void Save()
        {
            var parts = new List<string>();
            foreach (var r in _repos)
                if (!r.isDefault) parts.Add(r.githubUrl);
            SettingsService.Set(SETTINGS_KEY, string.Join("|", parts));
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static SkinRepo ParseRepo(string githubUrl)
        {
            try
            {
                // expected: https://github.com/{author}/{repo}[/...]
                string url = githubUrl.TrimEnd('/');
                // strip trailing segments past author/repo
                var uri = new Uri(url);
                string[] segs = uri.AbsolutePath.TrimStart('/').Split('/');
                if (segs.Length < 2) return null;
                return new SkinRepo
                {
                    githubUrl = $"https://github.com/{segs[0]}/{segs[1]}",
                    author = segs[0],
                    repoName = segs[1],
                };
            }
            catch { return null; }
        }

        private static string NormalizeUrl(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim();
            if (!raw.StartsWith("http")) raw = "https://" + raw;
            // must contain github.com
            if (!raw.Contains("github.com")) return null;
            return raw;
        }
    }
}
