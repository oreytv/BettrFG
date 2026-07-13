using System;
using System.Runtime.InteropServices;
using Rewired;
using BetterFG.Services;
using UnityEngine;

namespace BetterFG.UI
{
    // moves the OS cursor with the stick while the BettrFG UI is open, and lets a button
    // left-click. reads the pad through Rewired (which the game already runs) so deadzones
    // and connected-controller detection are handled for us. cursor pos is what unity's
    // Input.mousePosition reads back, so all the existing hover/click code just works.
    public class ControllerManager : MonoBehaviour
    {
        public ControllerManager(IntPtr ptr) : base(ptr) { }

        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;

        private const float DEADZONE = 0.2f;

        private bool _prevRight, _leftHeld;
        private float _scrollAccum;

        public static ControllerManager Create()
        {
            var go = new GameObject("BetterFG_Controller");
            go.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(go);
            return go.AddComponent<ControllerManager>();
        }

        void Update()
        {
            if (!ReInput.isReady || ReInput.players.playerCount == 0) return;

            var p = ReInput.players.GetPlayer(0);
            if (p == null) return;

            // toggle binds run even while the UI is hidden so a controller-only player can open it.
            // skip while a bind is being recorded so the press being captured doesn't also toggle.
            if (Windows.KeybindRecorder.IsRecording) return;
            int uiBtn = ControllerBindService.GetButton(ControllerBindId.ToggleUI);
            int wheelBtn = ControllerBindService.GetButton(ControllerBindId.ToggleWheel);
            bool toggledUI = uiBtn >= 0 && ControllerBindService.ButtonDownThisFrame(uiBtn) && BetterFGUIMan.Instance != null;
            bool toggledWheel = wheelBtn >= 0 && wheelBtn != uiBtn && ControllerBindService.ButtonDownThisFrame(wheelBtn);
            if (toggledUI) BetterFGUIMan.Instance.SetVisible(!BetterFGUIMan.Instance.IsVisible);
            if (toggledWheel) SideWheel.SideWheelManager.Instance?.ToggleFromController();

            // closing the UI with the pad leaves the OS cursor stranded on screen with no stick to move
            // it. once EVERYTHING is closed (main UI AND wheel), hide+lock the cursor — it comes back when
            // they reopen the UI (SetVisible unlocks it) or hit F1.
            if (toggledUI || toggledWheel)
            {
                bool anyUp = (BetterFGUIMan.Instance != null && BetterFGUIMan.Instance.IsVisible)
                          || (SideWheel.SideWheelManager.Instance != null && SideWheel.SideWheelManager.Instance.IsWheelVisible);
                if (!anyUp) BetterFGUIMan.Instance?.SetCursorFree(false);
            }

            // drive the cursor whenever EITHER our main UI or the sidewheel is showing — both put a
            // mouse cursor on screen, so the stick should move it in both.
            bool uiUp = (BetterFGUIMan.Instance != null && BetterFGUIMan.Instance.IsVisible)
                     || Windows.Creative.BatchEditWindow.AnyOpen; // our standalone editor window also wants the cursor
            bool wheelUp = SideWheel.SideWheelManager.Instance != null && SideWheel.SideWheelManager.Instance.IsWheelVisible;
            if (!uiUp && !wheelUp)
            {
                if (_leftHeld) { mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero); _leftHeld = false; }
                _prevRight = false;
                _scrollAccum = 0f;
                FGInputLockService.SetControllerLock(false);
                return;
            }

            // pull the raw sticks straight off any connected joystick so we don't depend on the game's
            // action-map names. axes 0/1 = left stick X/Y (cursor), axes 2/3 = right stick X/Y (scroll).
            // (the interop IList has no Count, so loop on joystickCount and index it.) the click buttons
            // come from the user's binds via element id, not a fixed position.
            float mx = 0f, my = 0f, sy = 0f;
            var sticks = p.controllers.Joysticks;
            int n = p.controllers.joystickCount;
            for (int i = 0; i < n; i++)
            {
                var j = sticks[i];
                if (j == null) continue;
                if (j.axisCount >= 2) { mx += j.GetAxis(0); my += j.GetAxis(1); }
                if (j.axisCount >= 4) sy += j.GetAxis(3);
            }
            bool clickNow = ControllerBindService.ButtonHeldById(ControllerBindService.GetButton(ControllerBindId.LeftClick));
            bool rightNow = ControllerBindService.ButtonHeldById(ControllerBindService.GetButton(ControllerBindId.RightClick));
            mx = Mathf.Clamp(mx, -1f, 1f);
            my = Mathf.Clamp(my, -1f, 1f);
            sy = Mathf.Clamp(sy, -1f, 1f);

            if (Mathf.Abs(mx) > DEADZONE || Mathf.Abs(my) > DEADZONE)
            {
                GetCursorPos(out var c);
                float step = ControllerBindService.CursorSpeed * Time.unscaledDeltaTime;
                int nx = c.X + (int)Math.Round(mx * step);
                int ny = c.Y - (int)Math.Round(my * step); // screen Y is inverted vs stick
                SetCursorPos(nx, ny);
            }

            // right stick Y = scroll wheel. accumulate fractional deltas so slow tilts still scroll.
            bool scrolling = Mathf.Abs(sy) > DEADZONE;
            if (scrolling)
            {
                _scrollAccum += sy * ControllerBindService.ScrollSpeed * Time.unscaledDeltaTime;
                int notches = (int)_scrollAccum;
                if (notches != 0)
                {
                    _scrollAccum -= notches;
                    mouse_event(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)notches), IntPtr.Zero);
                }
            }
            else _scrollAccum = 0f;

            // A/cross = press-and-hold left mouse: down on press, up on release. that gives a plain
            // click on a quick tap, and a drag when you hold it and move the left stick.
            if (clickNow && !_leftHeld) { mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero); _leftHeld = true; AudioService.PlayControllerClick(); }
            else if (!clickNow && _leftHeld) { mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero); _leftHeld = false; }

            // B/circle = right click (rising edge so a hold is one click)
            if (rightNow && !_prevRight) { mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, IntPtr.Zero); mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, IntPtr.Zero); }
            _prevRight = rightNow;

            // we only reach here while our UI/wheel is up, so keep the game's pad input locked the
            // WHOLE time it's open — not just on stick activity. gating on activity left a gap
            // between presses where a fresh button push fired in the game before the lock re-armed
            // (the "some presses leak through" bug).
            FGInputLockService.SetControllerLock(true);
        }
    }
}
