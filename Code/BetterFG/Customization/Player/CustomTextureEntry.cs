using System;
using System.Collections.Generic;
using UnityEngine;
using BetterFG.Services;

namespace BetterFG.Customization.Player
{
    /// <summary>
    /// Represents a saved custom texture entry
    /// </summary>
    [System.Serializable]
    public class CustomTextureEntry
    {
        public string name;           // unique name (cannot match existing CostumeOption.CMSData.Name._text)
        public string texturePath;    // path to PNG file
        public bool enabled;          // whether this entry is active

        public CustomTextureEntry() { }
        public CustomTextureEntry(string name, string texPath, bool en = true)
        {
            this.name = name;
            this.texturePath = texPath;
            this.enabled = en;
        }

        public override string ToString() => $"{name} ({(enabled ? "on" : "off")}) -> {texturePath}";
    }

    /// <summary>
    /// Wrapper for JsonUtility to handle List serialization
    /// </summary>
    [System.Serializable]
    public class CustomTextureEntryList
    {
        public CustomTextureEntry[] entries = new CustomTextureEntry[0];
    }

    public static class CustomTextureEntryManager
    {
        private const string ENTRIES_KEY = "skintex.entries";

        public static List<CustomTextureEntry> LoadEntries()
        {
            var entries = new List<CustomTextureEntry>();
            string json = SettingsService.Get(ENTRIES_KEY, "");
            if (string.IsNullOrEmpty(json)) return entries;

            try
            {
                var wrapper = UnityEngine.JsonUtility.FromJson<CustomTextureEntryList>(json);
                if (wrapper?.entries != null)
                    entries.AddRange(wrapper.entries);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"CustomTexEntry: Failed to load entries: {ex.Message}");
            }
            return entries;
        }

        public static void SaveEntries(List<CustomTextureEntry> entries)
        {
            try
            {
                var wrapper = new CustomTextureEntryList { entries = entries.ToArray() };
                // Manual JSON building to avoid IL2CPP JsonUtility limitations
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("{\"entries\":[");
                for (int i = 0; i < wrapper.entries.Length; i++)
                {
                    if (i > 0) sb.Append(",");
                    var e = wrapper.entries[i];
                    sb.Append("{\"name\":\"").Append(EscapeJson(e.name)).Append("\",");
                    sb.Append("\"texturePath\":\"").Append(EscapeJson(e.texturePath)).Append("\",");
                    sb.Append("\"enabled\":").Append(e.enabled ? "true" : "false").Append("}");
                }
                sb.Append("]}");
                string json = sb.ToString();
                SettingsService.Set(ENTRIES_KEY, json);
                Plugin.Log.LogInfo($"CustomTexEntry: Saved {entries.Count} entries");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"CustomTexEntry: Failed to save entries: {ex.Message}");
            }
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        public static bool EntryNameExists(string name, List<CustomTextureEntry> entries)
        {
            if (string.IsNullOrEmpty(name)) return false;
            foreach (var e in entries)
                if (e?.name == name) return true;
            return false;
        }

        public static CustomTextureEntry GetEntryByName(string name, List<CustomTextureEntry> entries)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var e in entries)
                if (e?.name == name) return e;
            return null;
        }

        public static void AddEntry(CustomTextureEntry entry, List<CustomTextureEntry> entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.name)) return;
            if (EntryNameExists(entry.name, entries))
            {
                Plugin.Log.LogWarning($"CustomTexEntry: Entry '{entry.name}' already exists");
                return;
            }
            entries.Add(entry);
            SaveEntries(entries);
        }

        public static void RemoveEntry(string name, List<CustomTextureEntry> entries)
        {
            entries.RemoveAll(e => e?.name == name);
            SaveEntries(entries);
        }

        public static void UpdateEntry(CustomTextureEntry entry, List<CustomTextureEntry> entries)
        {
            if (entry == null || string.IsNullOrEmpty(entry.name)) return;
            var existing = GetEntryByName(entry.name, entries);
            if (existing != null)
            {
                existing.texturePath = entry.texturePath;
                existing.enabled = entry.enabled;
                SaveEntries(entries);
            }
        }
    }
}
