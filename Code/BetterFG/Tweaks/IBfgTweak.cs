using System;
using System.Collections.Generic;
using BetterFG.Services;
using FG.Common;
using FGClient;
using UnityEngine;

namespace BetterFG.Tweaks
{
    public struct TweakButton
    {
        public string Label;
        public Action OnClick;
        public float Width; // 0 = auto (fit label)
    }

    // optional inline input for tweaks that take a numeric value. shown left of the toggle.
    public struct TweakInputField
    {
        public Func<string> Get;       // current value as string
        public Action<string> Set;     // commit on end-edit
        public float Width;            // 0 = default (40)
        public string Placeholder;     // optional
    }

    // inline [-] value [+] stepper for tweaks that take a bounded int. shown left of the toggle,
    // same slot as TweakInputField.
    public struct TweakIncrement
    {
        public string Label;           // sub-row label; null = "Limit"
        public int Min;
        public int Max;
        public Func<int> Get;
        public Action<int> Set;
        public bool Wrap;              // loop min<->max instead of clamping
        public float Width;            // 0 = default (62)
    }

    // one setting shown in a tweak's expanded panel (below the row, when the tweak is enabled).
    // label on the left, a button on the right that cycles through Options; Selected is the current
    // index, OnPick commits the new index. use for anything more than a plain on/off toggle.
    public struct TweakSetting
    {
        public string Label;           // "Do what after skip?"
        public string[] Options;       // e.g. { "Requeue", "Enter menu" }
        public Func<int> Selected;     // current option index
        public Action<int> OnPick;     // called with the newly-picked index
    }

    public class BfgTweak : MonoBehaviour
    {
        public BfgTweak(System.IntPtr ptr) : base(ptr) { }

        public virtual string TweakId => "unknown";
        public virtual string TweakLabel => "Unknown";
        public virtual bool DefaultEnabled => false;

        // optional text shown by a small "?" after the label in TweaksWindow (instant on hover).
        public virtual string TweakTooltip => null;

        public bool IsEnabled { get; private set; }

        private string SettingKey => $"tweak.{TweakId}";

        // every tweak that's currently enabled. the game-state patches fan out lifecycle events over
        // this instead of naming each tweak by hand, a tweak overrides the hooks it cares about and
        // gets called only while it's on. tracks IsEnabled (set immediately), not the delayed
        // EnableTweak invoke, so a tweak toggled on mid-session reacts to the next round/state change
        private static readonly List<BfgTweak> _live = new List<BfgTweak>();

        void Start()
        {
            IsEnabled = SettingsService.Get(SettingKey, DefaultEnabled ? "true" : "false") == "true";
            if (IsEnabled)
            {
                if (!_live.Contains(this)) _live.Add(this);
                Invoke("EnableTweak", 4f);
            }
        }

        void OnDestroy() => _live.Remove(this);

        public void SetEnabled(bool enabled)
        {
            if (IsEnabled == enabled) return;
            IsEnabled = enabled;
            SettingsService.Set(SettingKey, enabled ? "true" : "false");
            if (enabled)
            {
                if (!_live.Contains(this)) _live.Add(this);
                EnableTweak();
            }
            else
            {
                _live.Remove(this);
                DisableTweak();
            }
        }

        public virtual void EnableTweak() { }
        public virtual void DisableTweak() { }

        // lifecycle hooks  overridden by tweaks that react to these game moments the base no-ops,
        // so a tweak only pays for the ones it wants. raised by the patches in Patches/ over _live
        public virtual void OnRoundStart() { }
        public virtual void OnStateChanged(GameStateMachine.IGameState newState) { }
        public virtual void OnBannerShown() { }
        public virtual void OnSpectatorMode() { }
        public virtual void OnMainMenuEntered() { }
        public virtual void OnLevelEditorPlaytest() { }
        public virtual void OnLevelEditorPlaytestEnd() { }

        // one enumeration, guarded per-tweak so one throwing tweak doesn't kill the rest of the fan-out
        private static void Raise(Action<BfgTweak> hook)
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var t = _live[i];
                if (t == null) { _live.RemoveAt(i); continue; }
                try { hook(t); } catch (Exception ex) { Plugin.Log.LogWarning($"{t.TweakId} lifecycle hook threw: {ex.Message}"); }
            }
        }

        public static void RaiseRoundStart() => Raise(t => t.OnRoundStart());
        public static void RaiseStateChanged(GameStateMachine.IGameState s) => Raise(t => t.OnStateChanged(s));
        public static void RaiseBannerShown() => Raise(t => t.OnBannerShown());
        public static void RaiseSpectatorMode() => Raise(t => t.OnSpectatorMode());
        public static void RaiseMainMenuEntered() => Raise(t => t.OnMainMenuEntered());
        public static void RaiseLevelEditorPlaytest() => Raise(t => t.OnLevelEditorPlaytest());
        public static void RaiseLevelEditorPlaytestEnd() => Raise(t => t.OnLevelEditorPlaytestEnd());

        // tweaks can return extra buttons that show up left of the toggle in TweaksWindow
        public virtual List<TweakButton> GetCustomButtons() => null;

        // tweaks can return inline numeric inputs that show up left of the toggle in TweaksWindow
        public virtual List<TweakInputField> GetInputFields() => null;

        // tweaks can return inline [-] value [+] steppers that show up left of the toggle
        public virtual List<TweakIncrement> GetIncrements() => null;

        // tweaks with settings beyond a plain toggle return them here. shown in an expanded panel
        // under the row while the tweak is enabled; hidden when it's off.
        public virtual List<TweakSetting> GetSettings() => null;
    }
}