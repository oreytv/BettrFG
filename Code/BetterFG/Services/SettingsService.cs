using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace BetterFG.Services
{
    public static class SettingsService
    {
        private static readonly string OldSettingsDir;
        private static readonly string SettingsDir;
        private static readonly string BackupDir;
        private static readonly string OldFilePath;
        private static readonly string FilePath;
        private static readonly string OldPbFilePath;
        private static readonly string PbFilePath;
        private static readonly string SkinScaleFilePath;

        private static Dictionary<string, string> _data = new Dictionary<string, string>(StringComparer.Ordinal);
        private static bool _loaded = false;
        private static bool _initDone = false;

        // periodic backup gating — only writes a new backup if Set was called since the last one.
        private static bool _dirtySinceBackup;
        private static float _lastBackupTime;
        public const string KEY_BACKUP_ENABLED = "backup.enabled";
        public const string KEY_BACKUP_INTERVAL_MIN = "backup.intervalMin";
        public const int DEFAULT_BACKUP_INTERVAL_MIN = 5;

        private static Dictionary<string, float> _pbs = new Dictionary<string, float>(StringComparer.Ordinal);
        private static bool _pbsLoaded = false;

        private static Dictionary<string, float> _skinScales = new Dictionary<string, float>(StringComparer.Ordinal);
        private static bool _skinScalesLoaded = false;

        static SettingsService()
        {
            string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            OldSettingsDir = Path.Combine(dllDir, "Settings");
            SettingsDir = Path.Combine(appData, "BettrFG", "Settings");
            BackupDir = Path.Combine(SettingsDir, "Backup");
            OldFilePath = Path.Combine(OldSettingsDir, "last.txt");
            FilePath = Path.Combine(SettingsDir, "last.txt");
            OldPbFilePath = Path.Combine(OldSettingsDir, "pb.txt");
            PbFilePath = Path.Combine(SettingsDir, "pb.txt");
            SkinScaleFilePath = Path.Combine(SettingsDir, "skinScales.txt");
        }

        public static void Init()
        {
            EnsureInit();
        }

        public static string Get(string key, string defaultValue = "")
        {
            EnsureLoaded();
            return _data.TryGetValue(key, out var v) ? v : defaultValue;
        }

        public static void Set(string key, string value)
        {
            EnsureLoaded();
            _data[key] = value ?? "";
            _dirtySinceBackup = true;
            Save();
        }

        public static void Remove(string key)
        {
            EnsureLoaded();
            if (_data.Remove(key)) Save();
        }

        // for presets: grab every key under these prefixes (snapshot), or wipe those keys and write
        // a new set (values=null just wipes). one save either way.
        public static Dictionary<string, string> Snapshot(string[] prefixes)
        {
            EnsureLoaded();
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in _data)
                foreach (var pre in prefixes)
                    if (kv.Key.StartsWith(pre, StringComparison.Ordinal)) { result[kv.Key] = kv.Value; break; }
            return result;
        }

        public static void ReplacePrefixed(string[] prefixes, Dictionary<string, string> values)
        {
            EnsureLoaded();
            var toRemove = new List<string>();
            foreach (var key in _data.Keys)
                foreach (var pre in prefixes)
                    if (key.StartsWith(pre, StringComparison.Ordinal)) { toRemove.Add(key); break; }
            foreach (var key in toRemove) _data.Remove(key);
            if (values != null)
                foreach (var kv in values) _data[kv.Key] = kv.Value ?? "";
            Save();
        }

        public static bool TryGetPb(string levelName, out float pb)
        {
            EnsurePbsLoaded();
            return _pbs.TryGetValue(levelName, out pb);
        }

        public static void SetPb(string levelName, float time)
        {
            EnsurePbsLoaded();
            _pbs[levelName] = time;
            SavePbs();
        }

        public static bool IsNewPb(string levelName, float time) =>
            !TryGetPb(levelName, out float pb) || time < pb;

        // "Don't show again" for a specific update version. Persists to last.txt as a
        // comma-separated list under updateWindow.ignored.
        public const string KEY_IGNORED_UPDATES = "updateWindow.ignored";

        public static bool IsUpdateIgnored(string version)
        {
            if (string.IsNullOrEmpty(version)) return false;
            string raw = Get(KEY_IGNORED_UPDATES, "");
            if (string.IsNullOrEmpty(raw)) return false;
            foreach (var part in raw.Split(','))
                if (part.Trim() == version) return true;
            return false;
        }

        public static void IgnoreUpdate(string version)
        {
            if (string.IsNullOrEmpty(version) || IsUpdateIgnored(version)) return;
            string raw = Get(KEY_IGNORED_UPDATES, "");
            Set(KEY_IGNORED_UPDATES, string.IsNullOrEmpty(raw) ? version : raw + "," + version);
        }

        public static Dictionary<string, float> GetAllPbs()
        {
            EnsurePbsLoaded();
            return _pbs;
        }

        public static bool TryGetSkinScale(string file, out float scale)
        {
            EnsureSkinScalesLoaded();
            return _skinScales.TryGetValue(file, out scale);
        }

        public static void SetSkinScale(string file, float scale)
        {
            EnsureSkinScalesLoaded();
            _skinScales[file] = scale;
            SaveSkinScales();
        }

        public static void RemoveSkinScale(string file)
        {
            EnsureSkinScalesLoaded();
            if (_skinScales.Remove(file)) SaveSkinScales();
        }

        private static void EnsureInit()
        {
            if (_initDone) return;
            _initDone = true;

            try
            {
                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);

                if (!File.Exists(FilePath) && File.Exists(OldFilePath))
                {
                    File.Copy(OldFilePath, FilePath, false);
                    Debug.Log("[Settings] moved last.txt to appdata");
                }

                if (!File.Exists(PbFilePath) && File.Exists(OldPbFilePath))
                {
                    File.Copy(OldPbFilePath, PbFilePath, false);
                    Debug.Log("[Settings] moved pb.txt to appdata");
                }

                if (BackupEnabled) BackupNow("startup");
                Application.quitting += (Il2CppSystem.Action)(() => { if (BackupEnabled) BackupNow("quit"); });
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Settings] Init failed: {ex.Message}");
            }
        }

        // call every frame; takes a backup at most every 5 minutes, and only if something was
        // actually changed since the last one. wired into BetterFGUIMan.Update.
        public static void TickBackup()
        {
            if (!BackupEnabled) return;
            if (!_dirtySinceBackup) return;
            float now = Time.realtimeSinceStartup;
            if (now - _lastBackupTime < BackupIntervalMinutes * 60f) return;
            BackupNow("auto");
            _lastBackupTime = now;
            _dirtySinceBackup = false;
        }

        public static bool BackupEnabled
        {
            get => Get(KEY_BACKUP_ENABLED, "true") != "false";
            set => Set(KEY_BACKUP_ENABLED, value ? "true" : "false");
        }

        public static int BackupIntervalMinutes
        {
            get => int.TryParse(Get(KEY_BACKUP_INTERVAL_MIN, DEFAULT_BACKUP_INTERVAL_MIN.ToString()), out int v)
                ? Mathf.Clamp(v, 1, 120) : DEFAULT_BACKUP_INTERVAL_MIN;
            set => Set(KEY_BACKUP_INTERVAL_MIN, Mathf.Clamp(value, 1, 120).ToString());
        }

        public static string BackupFolderPath => BackupDir;

        // copies last.txt into Settings/Backup with a timestamped name. cheap insurance against
        // the file getting clobbered mid-session.
        private static void BackupNow(string reason)
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                if (!Directory.Exists(BackupDir)) Directory.CreateDirectory(BackupDir);

                string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string dest = Path.Combine(BackupDir, $"last_{stamp}_{reason}.txt");
                File.Copy(FilePath, dest, true);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Settings] backup ({reason}) failed: {ex.Message}");
            }
        }

        private static void EnsureLoaded()
        {
            EnsureInit();
            if (_loaded) return;
            _loaded = true;
            _data.Clear();

            if (!File.Exists(FilePath)) return;

            try
            {
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 1) continue;
                    string k = line.Substring(0, eq).Trim();
                    string v = line.Substring(eq + 1).Trim();
                    _data[k] = v;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Settings] Load failed: {ex.Message}");
            }
        }

        private static void Save()
        {
            EnsureInit();
            try
            {
                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);

                var lines = new List<string>();
                foreach (var kv in _data)
                    lines.Add($"{kv.Key}={kv.Value}");

                File.WriteAllLines(FilePath, lines);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Settings] Save failed: {ex.Message}");
            }
        }

        private static void EnsurePbsLoaded()
        {
            EnsureInit();
            if (_pbsLoaded) return;
            _pbsLoaded = true;
            _pbs.Clear();

            if (!File.Exists(PbFilePath)) return;

            try
            {
                foreach (var line in File.ReadAllLines(PbFilePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 1) continue;
                    string k = line.Substring(0, eq).Trim();
                    if (float.TryParse(line.Substring(eq + 1).Trim(), out float v))
                        _pbs[k] = v;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Settings] PB load failed: {ex.Message}");
            }
        }

        private static void SavePbs()
        {
            EnsureInit();
            try
            {
                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);

                var lines = new List<string>();
                foreach (var kv in _pbs)
                    lines.Add($"{kv.Key}={kv.Value:F3}");

                File.WriteAllLines(PbFilePath, lines);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Settings] PB save failed: {ex.Message}");
            }
        }

        private static void EnsureSkinScalesLoaded()
        {
            EnsureInit();
            if (_skinScalesLoaded) return;
            _skinScalesLoaded = true;
            _skinScales.Clear();

            if (!File.Exists(SkinScaleFilePath)) return;

            try
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                foreach (var line in File.ReadAllLines(SkinScaleFilePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 1) continue;
                    string k = line.Substring(0, eq).Trim();
                    if (float.TryParse(line.Substring(eq + 1).Trim(),
                        System.Globalization.NumberStyles.Float, ci, out float v))
                        _skinScales[k] = v;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Settings] SkinScale load failed: {ex.Message}");
            }
        }

        private static void SaveSkinScales()
        {
            EnsureInit();
            try
            {
                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);

                var ci = System.Globalization.CultureInfo.InvariantCulture;
                var lines = new List<string>();
                foreach (var kv in _skinScales)
                    lines.Add($"{kv.Key}={kv.Value.ToString("F3", ci)}");

                File.WriteAllLines(SkinScaleFilePath, lines);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Settings] SkinScale save failed: {ex.Message}");
            }
        }
    }
}
