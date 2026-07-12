using System;
using UnityEngine;

namespace BetterFG.Services
{
    public enum KeybindId
    {
        ToggleUI,
        ToggleWheel,
    }

    public static class KeybindService
    {
        public const KeyCode DefaultToggleUI = KeyCode.Z;     // used with Shift modifier
        public const KeyCode DefaultToggleWheel = KeyCode.Z;  // no modifier

        private const string KEY_TOGGLE_UI = "keybind.ui_toggle";
        private const string KEY_TOGGLE_WHEEL = "keybind.wheel_toggle";

        private static KeyCode _toggleUI = DefaultToggleUI;
        private static KeyCode _toggleWheel = DefaultToggleWheel;
        private static bool _loaded = false;

        public static event Action<KeybindId> OnRebound;

        public static KeyCode Get(KeybindId id)
        {
            EnsureLoaded();
            return id switch
            {
                KeybindId.ToggleUI => _toggleUI,
                KeybindId.ToggleWheel => _toggleWheel,
                _ => KeyCode.None,
            };
        }

        public static void Set(KeybindId id, KeyCode key)
        {
            EnsureLoaded();
            switch (id)
            {
                case KeybindId.ToggleUI:
                    _toggleUI = key;
                    SettingsService.Set(KEY_TOGGLE_UI, key.ToString());
                    break;
                case KeybindId.ToggleWheel:
                    _toggleWheel = key;
                    SettingsService.Set(KEY_TOGGLE_WHEEL, key.ToString());
                    break;
            }
            OnRebound?.Invoke(id);
        }

        public static string Label(KeybindId id)
        {
            var k = Get(id);
            return id == KeybindId.ToggleUI ? "Shift + " + KeyName(k) : KeyName(k);
        }

        public static string KeyName(KeyCode k)
        {
            if (k == KeyCode.None) return "—";
            return k.ToString();
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _toggleUI = Parse(SettingsService.Get(KEY_TOGGLE_UI, DefaultToggleUI.ToString()), DefaultToggleUI);
            _toggleWheel = Parse(SettingsService.Get(KEY_TOGGLE_WHEEL, DefaultToggleWheel.ToString()), DefaultToggleWheel);
        }

        private static KeyCode Parse(string s, KeyCode fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            try { return (KeyCode)Enum.Parse(typeof(KeyCode), s, true); }
            catch { return fallback; }
        }

        // Scan all KeyCodes this frame; return the first one freshly pressed (excluding modifiers/mouse).
        public static KeyCode PollPressedKey()
        {
            if (!Input.anyKeyDown) return KeyCode.None;
            foreach (KeyCode kc in Enum.GetValues(typeof(KeyCode)))
            {
                if (kc == KeyCode.None) continue;
                if (IsExcluded(kc)) continue;
                if (Input.GetKeyDown(kc)) return kc;
            }
            return KeyCode.None;
        }

        private static bool IsExcluded(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                case KeyCode.LeftCommand:
                case KeyCode.RightCommand:
                case KeyCode.LeftWindows:
                case KeyCode.RightWindows:
                case KeyCode.Mouse0:
                case KeyCode.Mouse1:
                case KeyCode.Mouse2:
                case KeyCode.Mouse3:
                case KeyCode.Mouse4:
                case KeyCode.Mouse5:
                case KeyCode.Mouse6:
                    return true;
                default:
                    return false;
            }
        }
    }
}
