using System.Collections.Generic;
using System.IO;
using BetterFG.Utilities;
using BepInEx;
using UnityEngine;

namespace BetterFG.Features.Stars
{
    internal static class StarStore
    {
        static readonly string FilePath;

        static StarStore()
        {
            string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
            FilePath = Path.Combine(appData, "BettrFG", "Settings", "betterfg_stars.json");

            string old = Path.Combine(Paths.ConfigPath, "betterfg_stars.json");
            if (!File.Exists(old)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                if (!File.Exists(FilePath)) File.Copy(old, FilePath);
                if (File.Exists(FilePath)) { File.Delete(old); Plugin.Log.LogInfo("moved betterfg_stars.json into appdata"); }
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"stars json didn't migrate, left it in config: {ex.Message}"); }
        }

        static HashSet<string> _cleared;

        static void EnsureLoaded()
        {
            if (_cleared != null) return;
            _cleared = new HashSet<string>();

            if (!File.Exists(FilePath)) return;
            try
            {
                string json = File.ReadAllText(FilePath);
                var entries = JsonUtil.GetArray(json, "stars");
                foreach (var entry in entries)
                {
                    string id = JsonUtil.GetValue(entry, "id");
                    if (!string.IsNullOrEmpty(id))
                        _cleared.Add(id);
                }
                Plugin.Log.LogInfo($"StarStore: loaded {_cleared.Count} cleared levels");
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"StarStore: load failed: {ex.Message}");
            }
        }

        static void Save()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.Append("{\"stars\":[");
                bool first = true;
                foreach (var id in _cleared)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append($"{{\"id\":\"{id}\"}}");
                }
                sb.Append("]}");
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning($"StarStore: save failed: {ex.Message}");
            }
        }

        public static bool HasCleared(string roundId)
        {
            if (!BetterFG.Features.FeatureRegistry.IsOn("stars", "store")) return false;
            EnsureLoaded();
            return _cleared.Contains(roundId);
        }

        // returns true if this is a new clear
        public static bool TryRecord(string roundId)
        {
            if (!BetterFG.Features.FeatureRegistry.IsOn("stars", "store")) return false;
            EnsureLoaded();
            if (_cleared.Contains(roundId)) return false;
            _cleared.Add(roundId);
            Save();
            return true;
        }

        public static int Count
        {
            get
            {
                if (!BetterFG.Features.FeatureRegistry.IsOn("stars", "store")) return 0;
                EnsureLoaded();
                return _cleared.Count;
            }
        }

        public static HashSet<string> GetAll()
        {
            if (!BetterFG.Features.FeatureRegistry.IsOn("stars", "store")) return new HashSet<string>();
            EnsureLoaded();
            return _cleared;
        }
    }
}
