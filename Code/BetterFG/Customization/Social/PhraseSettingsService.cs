using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BetterFG.Customization.Social
{
    /// <summary>
    /// Persists PhraseEntry list to Settings/phrases.txt next to the DLL.
    /// Format: one entry per line, pipe-separated: id|phraseId|phraseText|slot|enabled|imagePath|sound0|sound1|sound2
    /// (old files had a single sound at index 6 — that still loads into sound slot 0)
    /// </summary>
    public static class PhraseSettingsService
    {
        private static readonly string FilePath;
        private static readonly string Dir;

        static PhraseSettingsService()
        {
            string dll = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            Dir = Path.Combine(dll, "Settings");
            FilePath = Path.Combine(Dir, "phrases.txt");
        }

        public static List<PhraseEntry> Load()
        {
            var list = new List<PhraseEntry>();
            if (!File.Exists(FilePath)) return list;

            try
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    string[] p = line.Split('|');
                    if (p.Length < 5) continue;
                    list.Add(new PhraseEntry
                    {
                        id = p[0].Trim(),
                        phraseId = p[1].Trim(),
                        phraseText = p[2].Trim(),
                        slot = int.TryParse(p[3].Trim(), out int s) ? s : 7,
                        enabled = p[4].Trim() == "1",
                        imagePath = p.Length > 5 ? p[5].Trim() : "",
                        soundPaths = new string[3]
                        {
                            p.Length > 6 ? p[6].Trim() : "",
                            p.Length > 7 ? p[7].Trim() : "",
                            p.Length > 8 ? p[8].Trim() : ""
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhraseSettings] Load failed: {ex.Message}");
            }

            return list;
        }

        public static void Save(List<PhraseEntry> entries)
        {
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                var lines = new List<string>();
                foreach (var e in entries)
                {
                    string safe = (e.phraseText ?? "").Replace("|", "");
                    string safeId = (e.phraseId ?? "").Replace("|", "");
                    string safePath = (e.imagePath ?? "").Replace("|", "");
                    string s0 = (e.soundPaths != null && e.soundPaths.Length > 0 ? e.soundPaths[0] ?? "" : "").Replace("|", "");
                    string s1 = (e.soundPaths != null && e.soundPaths.Length > 1 ? e.soundPaths[1] ?? "" : "").Replace("|", "");
                    string s2 = (e.soundPaths != null && e.soundPaths.Length > 2 ? e.soundPaths[2] ?? "" : "").Replace("|", "");
                    lines.Add($"{e.id}|{safeId}|{safe}|{e.slot}|{(e.enabled ? "1" : "0")}|{safePath}|{s0}|{s1}|{s2}");
                }
                File.WriteAllLines(FilePath, lines);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[PhraseSettings] Save failed: {ex.Message}");
            }
        }
    }
}