using System;
using Rewired;

namespace BetterFG.Services
{
    // controller-side settings + button binds for the BettrFG UI. button binds are stored as a
    // rewired joystick button index (0=A/cross, 1=B/circle, 2=X/square, 3=Y/triangle, 4=LB, 5=RB,
    // 6=back, 7=start, ...). -1 = unbound. cursor speed is px/sec at full stick tilt.
    public enum ControllerBindId
    {
        ToggleUI,
        ToggleWheel,
        LeftClick,
        RightClick,
    }

    public static class ControllerBindService
    {
        // all binds default to unset (-1). people kept hitting them accidentally — opt-in only.
        public const int DefaultToggleUI = -1;
        public const int DefaultToggleWheel = -1;
        public const int DefaultLeftClick = -1;
        public const int DefaultRightClick = -1;
        public const float DefaultCursorSpeed = 1100f;
        public const float MinCursorSpeed = 400f;
        public const float MaxCursorSpeed = 1200f;
        public const float DefaultScrollSpeed = 20f; // wheel delta/sec at full right-stick tilt (120 = one notch)
        public const float MinScrollSpeed = 5f;
        public const float MaxScrollSpeed = 35f;

        private const string KEY_UI = "controller.toggle_ui";
        private const string KEY_WHEEL = "controller.toggle_wheel";
        private const string KEY_LEFTCLICK = "controller.left_click";
        private const string KEY_RIGHTCLICK = "controller.right_click";
        private const string KEY_SPEED = "controller.cursor_speed";
        private const string KEY_SCROLL = "controller.scroll_speed";

        private static int _toggleUI = DefaultToggleUI;
        private static int _toggleWheel = DefaultToggleWheel;
        private static int _leftClick = DefaultLeftClick;
        private static int _rightClick = DefaultRightClick;
        private static float _cursorSpeed = DefaultCursorSpeed;
        private static float _scrollSpeed = DefaultScrollSpeed;
        private static bool _loaded;

        public static event Action OnChanged;

        public static int GetButton(ControllerBindId id)
        {
            EnsureLoaded();
            switch (id)
            {
                case ControllerBindId.ToggleUI: return _toggleUI;
                case ControllerBindId.ToggleWheel: return _toggleWheel;
                case ControllerBindId.LeftClick: return _leftClick;
                default: return _rightClick;
            }
        }

        public static void SetButton(ControllerBindId id, int button)
        {
            EnsureLoaded();
            switch (id)
            {
                case ControllerBindId.ToggleUI: _toggleUI = button; SettingsService.Set(KEY_UI, button.ToString()); break;
                case ControllerBindId.ToggleWheel: _toggleWheel = button; SettingsService.Set(KEY_WHEEL, button.ToString()); break;
                case ControllerBindId.LeftClick: _leftClick = button; SettingsService.Set(KEY_LEFTCLICK, button.ToString()); break;
                default: _rightClick = button; SettingsService.Set(KEY_RIGHTCLICK, button.ToString()); break;
            }
            OnChanged?.Invoke();
        }

        public static float CursorSpeed
        {
            get { EnsureLoaded(); return _cursorSpeed; }
            set { EnsureLoaded(); _cursorSpeed = UnityEngine.Mathf.Clamp(value, MinCursorSpeed, MaxCursorSpeed); SettingsService.Set(KEY_SPEED, _cursorSpeed.ToString("0")); OnChanged?.Invoke(); }
        }

        public static float ScrollSpeed
        {
            get { EnsureLoaded(); return _scrollSpeed; }
            set { EnsureLoaded(); _scrollSpeed = UnityEngine.Mathf.Clamp(value, MinScrollSpeed, MaxScrollSpeed); SettingsService.Set(KEY_SCROLL, _scrollSpeed.ToString("0")); OnChanged?.Invoke(); }
        }

        // NOTE: binds are stored as Rewired ELEMENT IDENTIFIER ids, not positional button indices.
        // the element id is the single source of truth shared by the live poll (GetButtonById) and
        // the name shown in the UI — so what shows always matches what actually fires. we just use
        // rewired's own element name (e.g. "Cross", "Share", "Left Stick Button"), which is already
        // correct for whatever pad is connected.
        public static string ButtonName(int elementId)
        {
            if (elementId < 0) return "—";
            if (!ReInput.isReady || ReInput.players.playerCount == 0) return "Btn " + elementId;
            var p = ReInput.players.GetPlayer(0);
            if (p == null) return "Btn " + elementId;
            var sticks = p.controllers.Joysticks;
            int n = p.controllers.joystickCount;
            for (int i = 0; i < n; i++)
            {
                var j = sticks[i];
                if (j == null) continue;
                int bc = j.buttonCount;
                var ids = j.ButtonElementIdentifiers;
                for (int b = 0; b < bc; b++)
                {
                    var id = ids[b];
                    if (id != null && id.id == elementId) return id.name;
                }
            }
            return "Btn " + elementId;
        }

        // true if the bound element went down THIS frame on any connected joystick. shared by the
        // runtime toggles and the keybind recorder so they agree on what "pressed" means.
        public static bool ButtonDownThisFrame(int elementId)
        {
            if (elementId < 0 || !ReInput.isReady || ReInput.players.playerCount == 0) return false;
            var p = ReInput.players.GetPlayer(0);
            if (p == null) return false;
            var sticks = p.controllers.Joysticks;
            int n = p.controllers.joystickCount;
            for (int i = 0; i < n; i++)
            {
                var j = sticks[i];
                if (j != null && j.GetButtonDownById(elementId)) return true;
            }
            return false;
        }

        // true if the bound element is currently HELD on any connected joystick. used by the click
        // binds, which need a press-and-hold state (down on press, up on release) not a rising edge.
        public static bool ButtonHeldById(int elementId)
        {
            if (elementId < 0 || !ReInput.isReady || ReInput.players.playerCount == 0) return false;
            var p = ReInput.players.GetPlayer(0);
            if (p == null) return false;
            var sticks = p.controllers.Joysticks;
            int n = p.controllers.joystickCount;
            for (int i = 0; i < n; i++)
            {
                var j = sticks[i];
                if (j != null && j.GetButtonById(elementId)) return true;
            }
            return false;
        }

        // element id of the first button that went down this frame on any joystick, or -1.
        public static int PollPressedButton()
        {
            if (!ReInput.isReady || ReInput.players.playerCount == 0) return -1;
            var p = ReInput.players.GetPlayer(0);
            if (p == null) return -1;
            var sticks = p.controllers.Joysticks;
            int n = p.controllers.joystickCount;
            for (int i = 0; i < n; i++)
            {
                var j = sticks[i];
                if (j == null) continue;
                int bc = j.buttonCount;
                var ids = j.ButtonElementIdentifiers;
                for (int b = 0; b < bc; b++)
                {
                    if (j.GetButtonDown(b))
                    {
                        var id = ids[b];
                        return id != null ? id.id : -1;
                    }
                }
            }
            return -1;
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _toggleUI = ParseInt(SettingsService.Get(KEY_UI, DefaultToggleUI.ToString()), DefaultToggleUI);
            _toggleWheel = ParseInt(SettingsService.Get(KEY_WHEEL, DefaultToggleWheel.ToString()), DefaultToggleWheel);
            _leftClick = ParseInt(SettingsService.Get(KEY_LEFTCLICK, DefaultLeftClick.ToString()), DefaultLeftClick);
            _rightClick = ParseInt(SettingsService.Get(KEY_RIGHTCLICK, DefaultRightClick.ToString()), DefaultRightClick);
            _cursorSpeed = ParseFloat(SettingsService.Get(KEY_SPEED, DefaultCursorSpeed.ToString("0")), DefaultCursorSpeed);
            _scrollSpeed = ParseFloat(SettingsService.Get(KEY_SCROLL, DefaultScrollSpeed.ToString("0")), DefaultScrollSpeed);
        }

        private static int ParseInt(string s, int fallback) => int.TryParse(s, out var v) ? v : fallback;
        private static float ParseFloat(string s, float fallback) => float.TryParse(s, out var v) ? v : fallback;
    }
}
