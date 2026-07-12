using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

namespace BetterFG.Services
{
    // fetches the github release asset list for the "music" tag, and downloads individual
    // tracks on demand into MenuMusicService.MusicDir. caches the parsed track list in memory.
    public static class MenuMusicCatalog
    {
        private const string RELEASE_API =
            "https://api.github.com/repos/oreytv/BettrFG/releases/tags/music";

        public struct Track
        {
            public string name;   // filename (e.g. "song.mp3")
            public string url;    // browser_download_url
        }

        private static readonly List<Track> _tracks = new List<Track>();
        public static IReadOnlyList<Track> Tracks => _tracks;
        public static bool Loaded { get; private set; }

        // matches each {"name":"x.mp3", ... "browser_download_url":"https://..."} asset entry.
        private static readonly Regex AssetRegex = new Regex(
            "\"name\"\\s*:\\s*\"(?<name>[^\"]+\\.(?:mp3|wav))\"[\\s\\S]*?\"browser_download_url\"\\s*:\\s*\"(?<url>[^\"]+)\"",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static IEnumerator Fetch(Action onDone = null)
        {
            var req = UnityWebRequest.Get(RELEASE_API);
            req.SetRequestHeader("User-Agent", "BettrFG");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[MenuMusicCatalog] fetch failed: {req.error}");
                req.Dispose();
                onDone?.Invoke();
                yield break;
            }

            string json = req.downloadHandler.text ?? "";
            req.Dispose();

            _tracks.Clear();
            foreach (Match m in AssetRegex.Matches(json))
                _tracks.Add(new Track { name = m.Groups["name"].Value, url = m.Groups["url"].Value });
            Loaded = true;
            onDone?.Invoke();
        }

        public static string CachedPath(Track t) =>
            Path.Combine(MenuMusicService.MusicDir, t.name);

        public static bool IsCached(Track t) => File.Exists(CachedPath(t));

        public static IEnumerator Download(Track t, Action<bool> onDone = null)
        {
            string dest = CachedPath(t);
            var req = UnityWebRequest.Get(t.url);
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[MenuMusicCatalog] download {t.name} failed: {req.error}");
                req.Dispose();
                onDone?.Invoke(false);
                yield break;
            }

            try { File.WriteAllBytes(dest, req.downloadHandler.data); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[MenuMusicCatalog] write {t.name} failed: {ex.Message}");
                req.Dispose();
                onDone?.Invoke(false);
                yield break;
            }
            req.Dispose();
            onDone?.Invoke(true);
        }
    }
}
