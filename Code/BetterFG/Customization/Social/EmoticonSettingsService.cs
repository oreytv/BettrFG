using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BetterFG.Customization.Social
{
    /// <summary>
    /// Persists EmoticonEntry list to %APPDATA%\BettrFG\Settings\emoticons.txt.
    /// Format: one entry per line, pipe-separated: id|itemId|slot|enabled|imagePath|sound0|sound1|sound2
    /// (old files had a single sound at index 5 — that still loads into sound slot 0)
    /// </summary>
    public static class EmoticonSettingsService
    {
        private static readonly string FilePath;
        private static readonly string Dir;

        static EmoticonSettingsService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            Dir = Path.Combine(appData, "BettrFG", "Settings");
            FilePath = Path.Combine(Dir, "emoticons.txt");

            string old = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "Settings", "emoticons.txt");
            if (!File.Exists(old)) return;
            try
            {
                Directory.CreateDirectory(Dir);
                if (!File.Exists(FilePath)) File.Copy(old, FilePath);
                if (File.Exists(FilePath)) { File.Delete(old); Plugin.Log.LogInfo("moved emoticons.txt into appdata"); }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"emoticons.txt didn't migrate, left it where it was: {ex.Message}"); }
        }

        public static List<EmoticonEntry> Load()
        {
            var list = new List<EmoticonEntry>();
            if (!File.Exists(FilePath)) return list;

            try
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] p = line.Split('|');
                    if (p.Length < 4) continue;
                    list.Add(new EmoticonEntry
                    {
                        id = p[0].Trim(),
                        itemId = p[1].Trim(),
                        slot = int.TryParse(p[2].Trim(), out int s) ? s : 7,
                        enabled = p[3].Trim() == "1",
                        imagePath = p.Length > 4 ? p[4].Trim() : "",
                        soundPaths = new string[3]
                        {
                            p.Length > 5 ? p[5].Trim() : "",
                            p.Length > 6 ? p[6].Trim() : "",
                            p.Length > 7 ? p[7].Trim() : ""
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"EmoticonSettings: Load failed: {ex.Message}");
            }

            return list;
        }

        public static void Save(List<EmoticonEntry> entries)
        {
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                var lines = new List<string>();
                foreach (var e in entries)
                {
                    string safeId = (e.itemId ?? "").Replace("|", "");
                    string safePath = (e.imagePath ?? "").Replace("|", "");
                    string s0 = (e.soundPaths != null && e.soundPaths.Length > 0 ? e.soundPaths[0] ?? "" : "").Replace("|", "");
                    string s1 = (e.soundPaths != null && e.soundPaths.Length > 1 ? e.soundPaths[1] ?? "" : "").Replace("|", "");
                    string s2 = (e.soundPaths != null && e.soundPaths.Length > 2 ? e.soundPaths[2] ?? "" : "").Replace("|", "");
                    lines.Add($"{e.id}|{safeId}|{e.slot}|{(e.enabled ? "1" : "0")}|{safePath}|{s0}|{s1}|{s2}");
                }
                File.WriteAllLines(FilePath, lines);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"EmoticonSettings: Save failed: {ex.Message}");
            }
        }
    }
}