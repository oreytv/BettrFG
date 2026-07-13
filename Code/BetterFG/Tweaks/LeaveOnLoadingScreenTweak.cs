using System;
using BetterFG.Core;
using FallGuysLib.UI;
using FG.Common;
using FG.Common.CMS;
using FGClient;
using FGClient.UI;
using FGClient.UI.Core;
using FMOD;
using FMOD.Studio;
using FMODUnity;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Rewired;
using UnityEngine;
using static FGClient.UI.UIModalMessage;

namespace BetterFG.Tweaks
{
    // press back during a loading-ish game state and we open a confirm popup; OK calls ReloadGame
    // to drop the player back to the menu. the action id comes straight from RewiredConsts (the
    // generated property returns the live Rewired id for Menu_UICancel — Circle/B/A-cancel across
    // every controller layout) so we don't need to scrape NavigationOverlayManager and we don't
    // have a startup race where the id isn't cached yet.
    public class LeaveOnLoadingScreenTweak : BfgTweak
    {
        public LeaveOnLoadingScreenTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "leave_on_loading_screen";
        public override string TweakLabel => "Allow leaving during loading";
        public override bool DefaultEnabled => true;

        private static bool _popupOpen;
        private static NavPromptHandle _prompt;

        private const string TitleKey = "bfg_leaveloading_title";
        private const string MessageKey = "bfg_leaveloading_msg";
        private const string LeaveLabelKey = "bfg_leaveloading_label";

        void Update()
        {
            if (!IsEnabled) return;

            // popup can die without its callback firing (loading screen ends, another popup
            // replaces it, etc), leaving _popupOpen stuck true forever. clear it whenever we're
            // outside the leavable window — state changes naturally reset stale flags.
            if (!IsInLeavableLoadingState())
            {
                _popupOpen = false;
                DestroyPrompt();
                return;
            }

            EnsurePromptSpawned();

            if (_prompt == null || !_prompt.IsPressed()) return;
            // eliminated banner is a one-tap exit — no confirm popup, just close the banner.
            if (ActiveEliminatedBannerClose != null)
            {
                try { ActiveEliminatedBannerClose(); }
                catch (Exception ex) { Plugin.Log?.LogWarning("LeaveOnLoading: banner OnClosed: " + ex.Message); }
                ActiveEliminatedBannerClose = null;
                OnExternalLeaveTriggerEnd();
                DestroyPrompt();
                return;
            }
            ShowLeaveConfirm();
        }

        // called when StateRewardScreen takes over — kill any active prompt + state.
        public static void OnRewardScreenEntered()
        {
            ActiveEliminatedBannerClose = null;
            OnExternalLeaveTriggerEnd();
            DestroyPrompt();
            _popupOpen = false;
        }

        // spawn one copy of the game's own Back nav prompt onto our own overlay canvas. FG's
        // NavigationPromptButtonController handles all glyph switching itself (controller change
        // -> right glyph), so the core just Instantiates + Init's once and the visual stays
        // correct for whatever input the user picks up. element-name filter rejects X-on-PS
        // since across controller layouts the Menu_UICancel binding's elementIdentifierId
        // collides with Cross on DualShock.
        private static void EnsurePromptSpawned()
        {
            if (_prompt != null && _prompt.IsAlive) return;
            _prompt = NavPromptCore.From(NavPrompt.Back)
                .WithLabel("Leave", LeaveLabelKey)
                .AnchoredAt(NavPromptAnchor.BottomRight)
                .OnOwnCanvas()
                .AlsoAcceptEscape()
                // NavPrompt.Back's InputActions don't include Menu_UICancel, so the disabled-category
                // poll path wouldn't see B/Circle. force-poll Menu_UICancel directly (the action the
                // controller's back button is actually bound to) + filter element names to keep X/Cross
                // from sneaking through on PS.
                .PollActions(RewiredConsts.Action.Menu_UICancel)
                .FilterElement(IsBackElementName)
                .NoAutoResize()
                .AllowWhileUnfocused()
                .SpawnOn(null);
        }

        private static void DestroyPrompt()
        {
            _prompt?.Destroy();
            _prompt = null;
        }

        // popup title/message are looked up as CMS localisation keys, so register ours before show.
        private static void EnsureStringsRegistered()
        {
            var strings = CMSLoader.Instance._localisedStrings;
            if (!strings._localisedStrings.ContainsKey(TitleKey))
                strings._localisedStrings.Add(TitleKey, "Leave match?");
            if (!strings._localisedStrings.ContainsKey(MessageKey))
                strings._localisedStrings.Add(MessageKey, "Bail out of the current loading screen and return to the menu?");
        }

        private static void ShowLeaveConfirm()
        {
            // _popupOpen can get stuck true if a previous popup was torn down without its callback
            // firing (override by another popup, scene change mid-popup, etc). check the actual
            // modal container instead — if no popup is live on screen, it's safe to spawn ours.
            if (IsAnyModalLive())
            {
                _popupOpen = true;
                return;
            }
            _popupOpen = true;

            EnsureStringsRegistered();
            PopUp.ShowPopup(TitleKey, MessageKey, PopupInteractionType.Query, ModalType.MT_OK_CANCEL, OKButtonType.Disruptive, OnLeaveConfirmClosed);
        }

        private static bool IsAnyModalLive()
        {
            var modal = GameObject.Find("UICanvas_Client_V2(Clone)/ModalMessage");
            if (modal == null) return false;
            var t = modal.transform;
            for (int i = 0; i < t.childCount; i++)
                if (t.GetChild(i).gameObject.activeSelf) return true;
            return false;
        }

        // set by the eliminated-banner OnOpened patch; cleared on OnClosed. when set, the leave
        // confirm closes the banner instead of ReloadGame'ing (banner has its own continue-out path).
        public static Action ActiveEliminatedBannerClose;

        private static void OnLeaveConfirmClosed(bool wasOk)
        {
            _popupOpen = false;
            if (!wasOk) return;
            if (ActiveEliminatedBannerClose != null)
            {
                try { ActiveEliminatedBannerClose(); }
                catch (Exception ex) { Plugin.Log?.LogWarning("LeaveOnLoading: banner OnClosed: " + ex.Message); }
                ActiveEliminatedBannerClose = null;
                return;
            }
            OnExternalLeaveTriggerEnd();
            DestroyPrompt();
            // every load screen spawns a SNAP_Mute_Ambience instance that's supposed to stop on
            // load complete. bailing mid-load orphans it — BUS_AMBIENCES stays at f=0 forever and
            // we never hear ambience until restart. kill all instances of that snapshot before
            // we reload so the orphan can't survive the transition.
            StopAmbienceMuteSnapshot();
            GlobalGameStateClient.Instance.ReloadGame(true, EnumDisconnectReasonGraceful.ConnectToGameFailed);
        }

        private static void StopAmbienceMuteSnapshot()
        {
            try
            {
                var sys = RuntimeManager.StudioSystem;
                if (!sys.isValid()) return;
                if (sys.getBankList(out Il2CppStructArray<Bank> banks) != RESULT.OK || banks == null) return;
                foreach (var bank in banks)
                {
                    if (!bank.isValid()) continue;
                    if (bank.getEventList(out Il2CppStructArray<EventDescription> descs) != RESULT.OK || descs == null) continue;
                    foreach (var d in descs)
                    {
                        if (!d.isValid()) continue;
                        if (d.getPath(out string path) != RESULT.OK) continue;
                        if (path != "snapshot:/Mix/SNAP_Mute_Ambience") continue;
                        if (d.getInstanceList(out Il2CppStructArray<EventInstance> insts) != RESULT.OK || insts == null) continue;
                        foreach (var inst in insts)
                        {
                            if (inst.isValid()) inst.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                        }
                    }
                }
            }
            catch (Exception ex) { Plugin.Log?.LogWarning("LeaveOnLoading: StopAmbienceMuteSnapshot failed: " + ex.Message); }
        }

        // external triggers (e.g. StateQualificationScreen.Teardown) flip this on so the leave
        // window also covers the post-round transition where we'd otherwise reject the back press.
        // window lasts up to 8s but ends early when the round-reveal carousel fades in — that's
        // the visual signal the next round is about to start and the bail moment is gone.
        private static float _externalWindowUntil;
        public static void OnExternalLeaveTrigger() => _externalWindowUntil = Time.realtimeSinceStartup + 8f;
        public static void OnExternalLeaveTriggerEnd() => _externalWindowUntil = 0f;

        private static bool IsInLeavableLoadingState()
        {
            if (Time.realtimeSinceStartup < _externalWindowUntil) return true;

            var state = GlobalGameStateClient.Instance?._gameStateMachine?.CurrentState;
            if (state == null) return false;
            bool stateOk = state.TryCast<StateConnectToGame>() != null
                || state.TryCast<StateConnectionAuthentication>() != null
                || state.TryCast<StateGameLoading>() != null
                || state.TryCast<StatePrivateLobbyMinimal>() != null;
            if (!stateOk) return false;

            // also require the actual LoadingScreen container to have any active child — otherwise
            // we'd offer "Leave" during connect/auth states where no loading UI is on screen.
            var loading = GameObject.Find("UICanvas_Client_V2(Clone)/LoadingScreen");
            if (loading == null) return false;
            var t = loading.transform;
            for (int i = 0; i < t.childCount; i++)
                if (t.GetChild(i).gameObject.activeSelf) return true;
            return false;
        }

        // names Rewired's templates use for the cancel/back face button across the controller
        // layouts the game ships with. Xbox = B, PS = Circle, generic DInput = Button 1 / 2.
        // anything else (Cross/A/X, shoulders, sticks, dpad) is rejected so X on PS doesn't fire.
        // passed to NavPromptCore's element-name filter so the core's joystick-poll loop only
        // accepts presses on actual back buttons.
        private static bool IsBackElementName(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            // case-insensitive contains-style match; Rewired names vary ("B Button", "Circle Button", etc)
            string s = n.ToLowerInvariant();
            if (s.Contains("circle")) return true;
            if (s == "b" || s.StartsWith("b ") || s.Contains("b button")) return true;
            if (s.Contains("button 1") || s.Contains("button1")) return true; // DInput generic
            return false;
        }
    }
}
