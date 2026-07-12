using System;
using UnityEngine;

namespace BetterFG.Tweaks
{
    // shrinks the local bean's landing indicator fade so it snaps in/out instead of easing.
    // cache the game's original values before we stomp them so toggling off restores them.
    public class InstantLandingIndicatorTweak : BfgTweak
    {
        public InstantLandingIndicatorTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "instant_landing_indicator";
        public override string TweakLabel => "Instant Landing Indicator";
        public override bool DefaultEnabled => false;
        public override string TweakTooltip => "Credits to abab, original creator.";

        public static InstantLandingIndicatorTweak Instance { get; private set; }
        void Awake() => Instance = this;

        private bool _cached;
        private float _origFadeIn, _origFadeOut, _origSpeed;

        // fires via the BfgTweak round-start fan-out once this is re-registered in TweakRegistry
        public override void OnRoundStart()
        {
            //Apply();
        }

        // toggled on mid-round — apply straight away instead of waiting for next round start
        public override void EnableTweak()
        {
            //Apply();
        }

        //private LandingIndicator FindLocal()
        //{
        //    var bean = BetterFG.Services.BeanMonitorService.LocalPlayerBean;
        //    if (bean == null) return null;
        //    return bean.GetComponentInChildren<LandingIndicator>(true);
        //}

        //private void Apply()
        //{
        //    var li = FindLocal();
        //    if (li == null) return;

        //    if (!_cached)
        //    {
        //        _origFadeIn = li._fadeInHeight;
        //        _origFadeOut = li._fadeOutHeight;
        //        _origSpeed = li._fadeSpeed;
        //        _cached = true;
        //    }

        //    li._fadeInHeight = 0f;
        //    li._fadeOutHeight = 0f;
        //    li._fadeSpeed = 12f;
        //}

        public override void DisableTweak()
        {
            //var li = FindLocal();
            //if (li == null || !_cached) return;
            //li._fadeInHeight = _origFadeIn;
            //li._fadeOutHeight = _origFadeOut;
            //li._fadeSpeed = _origSpeed;
        }
    }
}
