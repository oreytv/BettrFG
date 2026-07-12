using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BetterFG.Customization.Social
{
    // persists EmoteEntry list to Settings/emotes.txt.
    // format: id|bundlePath|clipName|slot|enabled|imagePath|soundPath
    public static class EmoteSettingsService
    {
        private static readonly string FilePath;
        private static readonly string Dir;

        static EmoteSettingsService()
        {
            string dll = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            Dir = Path.Combine(dll, "Settings");
            FilePath = Path.Combine(Dir, "emotes.txt");
        }

        public static List<EmoteEntry> Load()
        {
            var list = new List<EmoteEntry>();
            if (!File.Exists(FilePath)) return list;

            try
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] p = line.Split('|');
                    if (p.Length < 5) continue;
                    list.Add(new EmoteEntry
                    {
                        id = p[0].Trim(),
                        bundlePath = p[1].Trim(),
                        clipName = p[2].Trim(),
                        slot = int.TryParse(p[3].Trim(), out int s) ? s : 7,
                        enabled = p[4].Trim() == "1",
                        imagePath = p.Length > 5 ? p[5].Trim() : "",
                        soundPath = p.Length > 6 ? p[6].Trim() : ""
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EmoteSettings] Load failed: {ex.Message}");
            }

            return list;
        }

        public static void Save(List<EmoteEntry> entries)
        {
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                var lines = new List<string>();
                foreach (var e in entries)
                {
                    string bundle = (e.bundlePath ?? "").Replace("|", "");
                    string clip = (e.clipName ?? "").Replace("|", "");
                    string safePath = (e.imagePath ?? "").Replace("|", "");
                    string safeSound = (e.soundPath ?? "").Replace("|", "");
                    lines.Add($"{e.id}|{bundle}|{clip}|{e.slot}|{(e.enabled ? "1" : "0")}|{safePath}|{safeSound}");
                }
                File.WriteAllLines(FilePath, lines);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EmoteSettings] Save failed: {ex.Message}");
            }
        }
    }
}
