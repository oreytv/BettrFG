using System;
using Cinemachine;
using FallGuysLib.Camera;

namespace BetterFG.Tweaks
{
    // close camera normally rebinds in SimpleFollowWithWorldUp so it swings behind the bean as you move.
    // WorldSpace freezes that binding. disabled = SimpleFollowWithWorldUp, enabled = WorldSpace.
    public class DisableCameraAssistTweak : BfgTweak
    {
        public DisableCameraAssistTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "disable_camera_assist";
        public override string TweakLabel => "Disable Camera Assist";
        public override bool DefaultEnabled => false;

        public static DisableCameraAssistTweak Instance { get; private set; }
        void Awake() => Instance = this;

        public override void EnableTweak() => Apply();
        public override void DisableTweak() => Apply();

        public override void OnRoundStart() => Apply();
        public override void OnLevelEditorPlaytest() => Apply();

        internal void Apply()
        {
            var freelook = CameraLocator.GetCameraDirector()?._closeCamera;
            if (freelook == null) return;

            freelook.m_BindingMode = IsEnabled
                ? CinemachineTransposer.BindingMode.WorldSpace
                : CinemachineTransposer.BindingMode.SimpleFollowWithWorldUp;
        }
    }
}
