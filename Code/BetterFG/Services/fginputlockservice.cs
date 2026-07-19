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
    //   - CreativeTypeValueTweak, typing into the GAME's parameter node rather than our own ui
    public static class FGInputLockService
    {
        private static bool _realFieldLock;
        private static bool _fakeFieldLock;
        private static bool _editorUiLock;
        private static bool _controllerLock;
        private static bool _paramTypeLock;

        public static bool IsLocked => _realFieldLock || _fakeFieldLock || _editorUiLock || _controllerLock || _paramTypeLock;

        // back-compat: callers that used SetLocked were the fake-field caret paths
        public static void SetLocked(bool locked) => _fakeFieldLock = locked;

        public static void SetFakeFieldLock(bool locked) => _fakeFieldLock = locked;

        public static void SetRealFieldLock(bool locked) => _realFieldLock = locked;

        // held while a number is being typed into a level editor parameter node
        public static void SetParamTypeLock(bool locked) => _paramTypeLock = locked;

        // locks the whole game input while our UI is up in the level editor — even when not
        // typing in a field — so editor hotkeys / movement don't fire under the open overlay.
        public static void SetEditorUiLock(bool locked) => _editorUiLock = locked;

        // locks game input while the player is steering our UI cursor with a controller, so the
        // same stick/buttons don't also drive menus or the camera underneath. ControllerManager
        // sets this on stick/button activity and clears it after a short idle.
        public static void SetControllerLock(bool locked) => _controllerLock = locked;

        // the other locks can be disable-only because the game restores maps on mouse/keyboard input.
        // these two can't: with their maps off nothing reaches the game to trigger that, so input stays
        // dead (pad unusable, or the editor ignoring Escape after you type until you jog the mouse).
        // re-enable ONLY the maps we turned off, a blanket one force-ons maps the game meant to be off
        // and scrambles the bindings.
        private static bool RestoreOnRelease => _controllerLock || _paramTypeLock;

        private static readonly HashSet<int> _selfDisabled = new HashSet<int>();
        private static bool _selfWasLocked;
        // reused across ticks — the controller lock holds every frame the UI is up with a pad, and a
        // fresh interop list per player per frame was constant GC churn
        private static Il2CppSystem.Collections.Generic.List<ControllerMap> _mapsBuf;

        public static void Tick()
        {
            if (!ReInput.isReady) return;
            int n = ReInput.players.playerCount;

            // must run BEFORE we re-evaluate IsLocked: a typing lock starting the same frame a stick
            // lock ends would otherwise never restore, leaving those maps off for good (and any later
            // re-enable doubles the input path, so presses register twice).
            if (_selfWasLocked && !RestoreOnRelease)
            {
                _selfWasLocked = false;
                if (_selfDisabled.Count > 0)
                {
                    for (int i = 0; i < n; i++)
                    {
                        var p = ReInput.players.GetPlayer(i);
                        if (p == null) continue;
                        var maps = _mapsBuf ??= new Il2CppSystem.Collections.Generic.List<ControllerMap>();
                        maps.Clear();
                        p.controllers.maps.GetAllMaps(maps);
                        for (int m = 0; m < maps.Count; m++)
                        {
                            var map = maps[m];
                            if (map != null && _selfDisabled.Contains(map.id))
                                map.enabled = true;
                        }
                    }
                    _selfDisabled.Clear();
                }
            }

            if (IsLocked)
            {
                // fresh snapshot so we don't carry stale ids from a previous lock session
                if (RestoreOnRelease && !_selfWasLocked) _selfDisabled.Clear();

                for (int i = 0; i < n; i++)
                {
                    var p = ReInput.players.GetPlayer(i);
                    if (p == null) continue;
                    var maps = _mapsBuf ??= new Il2CppSystem.Collections.Generic.List<ControllerMap>();
                    maps.Clear();
                    p.controllers.maps.GetAllMaps(maps);
                    for (int m = 0; m < maps.Count; m++)
                    {
                        var map = maps[m];
                        if (map == null || !map.enabled) continue;
                        if (RestoreOnRelease) _selfDisabled.Add(map.id);
                        map.enabled = false;
                    }
                }
                _selfWasLocked = RestoreOnRelease;
                return;
            }
        }
    }
}
