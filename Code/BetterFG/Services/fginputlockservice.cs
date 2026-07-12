using System.Collections.Generic;
using Rewired;

namespace BetterFG.Services
{
    // when any of our input fields is focused, disable rewired player maps
    // so wasd/jump don't leak into the game. leaves the UI input module alone
    // so our overlay clicks still work. Tick() must be called every frame
    // because the game re-enables maps under us.
    //
    // IMPORTANT: this must ONLY engage while typing in BettrFG's own UI. there are
    // two independent sources of "typing" in our ui and they must not stomp each
    // other, so each gets its own latch and we lock if either is set:
    //   - real unity InputFields owned by our canvas (driven by BetterFGUIMan,
    //     which verifies the selected field lives under our canvas so a focused
    //     Fall Guys input field never trips it)
    //   - our fake text fields (custom carets, no real InputField) driven by the tabs
    public static class fginputlockservice
    {
        private static bool _realFieldLock;
        private static bool _fakeFieldLock;
        private static bool _editorUiLock;
        private static bool _controllerLock;

        public static bool IsLocked => _realFieldLock || _fakeFieldLock || _editorUiLock || _controllerLock;

        // back-compat: callers that used SetLocked were the fake-field caret paths
        public static void SetLocked(bool locked) => _fakeFieldLock = locked;

        public static void SetFakeFieldLock(bool locked) => _fakeFieldLock = locked;

        public static void SetRealFieldLock(bool locked) => _realFieldLock = locked;

        // locks the whole game input while our UI is up in the level editor — even when not
        // typing in a field — so editor hotkeys / movement don't fire under the open overlay.
        public static void SetEditorUiLock(bool locked) => _editorUiLock = locked;

        // locks game input while the player is steering our UI cursor with a controller, so the
        // same stick/buttons don't also drive menus or the camera underneath. ControllerManager
        // sets this on stick/button activity and clears it after a short idle.
        public static void SetControllerLock(bool locked) => _controllerLock = locked;

        // the keyboard/editor locks are fine being disable-only (the game restores them on its own
        // keyboard/mouse input). but the CONTROLLER lock deadlocks: with the pad's maps off the
        // game never sees the controller input that would re-enable them, so it stays dead until
        // you touch mouse/keyboard. so for the controller lock we re-enable on release — but ONLY
        // the maps WE turned off. a blanket SetAllMapsEnabled(true) force-ons maps the game had
        // intentionally off for a given screen (e.g. a confirm popup), which scrambles the bindings
        // (both buttons mapping to cancel, swapped glyphs). tracking the exact set avoids that.
        private static readonly HashSet<int> _ctrlDisabled = new HashSet<int>();
        private static bool _ctrlWasLocked;

        public static void Tick()
        {
            if (!ReInput.isReady) return;
            int n = ReInput.players.playerCount;

            // controller lock just turned off — restore EXACTLY what we disabled, then clear, and
            // do this BEFORE we re-evaluate IsLocked. previously this only ran when IsLocked went
            // false too, so a keyboard-typing lock immediately following a stick-released lock
            // never got the chance to restore, leaving controller maps permanently off (which the
            // game and our next lock both saw as "off", and any re-enable later doubled up the
            // input path → press / axis registering twice).
            if (_ctrlWasLocked && !_controllerLock)
            {
                _ctrlWasLocked = false;
                if (_ctrlDisabled.Count > 0)
                {
                    for (int i = 0; i < n; i++)
                    {
                        var p = ReInput.players.GetPlayer(i);
                        if (p == null) continue;
                        var maps = new Il2CppSystem.Collections.Generic.List<ControllerMap>();
                        p.controllers.maps.GetAllMaps(maps);
                        for (int m = 0; m < maps.Count; m++)
                        {
                            var map = maps[m];
                            if (map != null && _ctrlDisabled.Contains(map.id))
                                map.enabled = true;
                        }
                    }
                    _ctrlDisabled.Clear();
                }
            }

            if (IsLocked)
            {
                // controller lock just engaged — snapshot starts fresh so we don't carry stale ids
                // from a previous lock session.
                if (_controllerLock && !_ctrlWasLocked) _ctrlDisabled.Clear();

                for (int i = 0; i < n; i++)
                {
                    var p = ReInput.players.GetPlayer(i);
                    if (p == null) continue;
                    var maps = new Il2CppSystem.Collections.Generic.List<ControllerMap>();
                    p.controllers.maps.GetAllMaps(maps);
                    for (int m = 0; m < maps.Count; m++)
                    {
                        var map = maps[m];
                        if (map == null || !map.enabled) continue;
                        if (_controllerLock) _ctrlDisabled.Add(map.id);
                        map.enabled = false;
                    }
                }
                _ctrlWasLocked = _controllerLock;
                return;
            }
        }
    }
}
