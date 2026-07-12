using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using FG.Common;
using UnityEngine;

namespace BetterFG.Features.UnityRound.Editor
{
    // Remembers which local unity round you loaded into each creative level and auto-loads it back
    // when you re-open that level. Persistent, lives next to the other BetterFG services in Plugin.cs
    // so it works whether or not the Creative tab is ever opened.
    //
    // We key on the level editor's share code: the root DDOL object
    //   "(singleton) FG.Common.LevelEditorManagerProxy"
    // carries a LevelEditorManagerProxy, whose CurrentLevel.ShareCode identifies the level. A fresh
    // unsaved level has no share code yet, so we just hold the last-known load until one shows up.
    //
    // Setting persisted per level:   unityround.level.<shareCode> = <info.json path>
    // Removing the round in the editor wipes the entry (so "keep it removed" sticks); loading writes
    // it. On entering a level with a saved path that still exists on disk, we load it automatically.

    public class CreativeRoundMemory : MonoBehaviour
    {
        public CreativeRoundMemory(IntPtr ptr) : base(ptr) { }

        public static CreativeRoundMemory Instance { get; private set; }

        private const string KEY_PREFIX = "unityround.level.";

        // the share code we last saw / acted on, so we only react when it actually changes
        private string _lastSeenCode;
        // share code we already auto-loaded for, so we don't keep re-loading the same level
        private string _autoLoadedFor;
        private float _pollTimer;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            // out of the editor: reset so re-entering re-triggers a clean auto-load
            if (!UnityRoundLoader.InLevelEditor)
            {
                _lastSeenCode = null;
                _autoLoadedFor = null;
                return;
            }

            _pollTimer += Time.unscaledDeltaTime;
            if (_pollTimer < 0.5f) return;
            _pollTimer = 0f;

            string code = GetCurrentShareCode();
            if (string.IsNullOrEmpty(code)) return;
            if (code == _lastSeenCode) return;

            _lastSeenCode = code;
            TryAutoLoadFor(code);
        }

        // ── current level share code ─────────────────────────────────────────────

        public static string GetCurrentShareCode()
        {
            try
            {
                var level = LevelEditorManagerProxy.CurrentLevel;
                string code = level != null ? level.ShareCode : null;
                return string.IsNullOrEmpty(code) ? null : code;
            }
            catch { return null; }
        }

        // ── persistence ───────────────────────────────────────────────────────────

        private static string KeyFor(string shareCode) => KEY_PREFIX + shareCode;

        // call after a successful load: bind the round to whatever level we're in right now.
        public static void RememberLoaded(string infoJsonPath)
        {
            if (string.IsNullOrEmpty(infoJsonPath)) return;
            string code = GetCurrentShareCode();
            if (string.IsNullOrEmpty(code)) return;
            SettingsService.Set(KeyFor(code), infoJsonPath);
            if (Instance != null) Instance._autoLoadedFor = code;
            Debug.Log($"[CreativeRoundMemory] bound round '{infoJsonPath}' to level {code}");
        }

        // call after an unload: forget the round for the current level so it stays gone next time.
        public static void ForgetForCurrentLevel()
        {
            string code = GetCurrentShareCode();
            if (string.IsNullOrEmpty(code)) return;
            SettingsService.Remove(KeyFor(code));
            if (Instance != null) Instance._autoLoadedFor = code;
            Debug.Log($"[CreativeRoundMemory] cleared round for level {code}");
        }

        // ── auto-load ─────────────────────────────────────────────────────────────

        private void TryAutoLoadFor(string code)
        {
            if (code == _autoLoadedFor) return;

            string path = SettingsService.Get(KeyFor(code), "");
            if (string.IsNullOrEmpty(path)) { _autoLoadedFor = code; return; }

            if (!System.IO.File.Exists(path))
            {
                Debug.Log($"[CreativeRoundMemory] saved round for {code} missing on disk: {path}");
                _autoLoadedFor = code;
                return;
            }

            // already loaded the same thing (e.g. you loaded it manually then re-entered)? skip.
            if (UnityRoundLoader.HasSpawned && string.Equals(UnityRoundLoader.LoadedJsonPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _autoLoadedFor = code;
                return;
            }

            _autoLoadedFor = code;
            // give the editor scene a beat to finish populating before we instantiate into it
            StartCoroutine(LoadAfterDelay(path, code).WrapToIl2Cpp());
        }

        private IEnumerator LoadAfterDelay(string path, string code)
        {
            yield return new WaitForSeconds(0.75f);

            // bail if you left the level / editor in the meantime
            if (!UnityRoundLoader.InLevelEditor) yield break;
            if (GetCurrentShareCode() != code) yield break;

            if (UnityRoundLoader.LoadFromInfoJson(path, out string error))
                Debug.Log($"[CreativeRoundMemory] auto-loaded round for {code}: {path}");
            else
                Debug.LogWarning($"[CreativeRoundMemory] auto-load failed for {code}: {error}");
        }
    }
}
