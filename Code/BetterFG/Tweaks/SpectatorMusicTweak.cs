using System;
using FGClient;
using FGClient.UI.PrivateLobby;
using HarmonyLib;
using UnityEngine;
using FgAudioSettings = FGClient.AudioSettings;

namespace BetterFG.Tweaks
{
    public class SpectatorMusicTweak : BfgTweak
    {
        public SpectatorMusicTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "spectator_music";
        public override string TweakLabel => "Spectator Music";
        public override bool DefaultEnabled => true;

        public static SpectatorMusicTweak Instance { get; private set; }
        private static ClientGameManager _lastGameManager;
        private static bool _lastSpectatorMode;

        internal static bool WantsMusic => Instance != null && Instance.IsEnabled;

        void Awake()
        {
            Instance = this;
        }

        public override void EnableTweak()
        {
            Apply(true);
            ForceSpectatorMix(_lastSpectatorMode);
        }

        public override void DisableTweak()
        {
            Apply(false);
            ForceSpectatorMix(false);
        }

        internal static void ApplyIfWanted()
        {
            var inst = Instance;
            if (inst != null && inst.IsEnabled) Apply(true);
        }

        internal static void ApplyIfWanted(ClientGameManager cgm)
        {
            if (cgm != null) _lastGameManager = cgm;
            var inst = Instance;
            if (inst != null && inst.IsEnabled)
            {
                ForceSpectatorMix(cgm != null && cgm.IsSpectatorMode);
            }
        }

        private static void Apply(bool enabled)
        {
        }


        internal static void ForceSpectatorMix(bool spectating)
        {
            _lastSpectatorMode = spectating;
            if (!WantsMusic && spectating) return;

            try
            {
                var audioManager = UnityEngine.Object.FindObjectOfType<AudioManager>();
                if (audioManager != null)
                    audioManager._spectatorMusic = WantsMusic;

                var master = AudioManager.EventMasterData;
                var param = master == null ? null : master.SpectatorMusicParam;
                if (string.IsNullOrEmpty(param)) param = "SpectatorMusic";

                float value = WantsMusic && spectating ? 1f : 0f;
                AudioManager.SetGlobalParam(param, value);

                var cgm = _lastGameManager;
                if (cgm != null && cgm._musicInstance != null)
                {
                    var music = cgm._musicInstance;
                    music.SetParameterByName(param, value);

                    var raw = music._eventInstance;
                    if (raw.isValid())
                        raw.setParameterByName(param, value, true);
                    ApplySpectatorFx(raw, WantsMusic && spectating);
                }
            }
            catch (Exception ex) { Plugin.Log?.LogWarning("SpectatorMusicTweak: param failed " + ex.Message); }
        }

        private static void ApplySpectatorFx(FMOD.Studio.EventInstance raw, bool spectating)
        {
            if (!raw.isValid()) return;

            try
            {
                var reverb = spectating ? 0.55f : 0f;
                FMOD.ChannelGroup group;
                var groupResult = raw.getChannelGroup(out group);
                if (groupResult == FMOD.RESULT.OK)
                {
                    FMOD.System system;
                    var systemResult = group.getSystemObject(out system);
                    if (systemResult == FMOD.RESULT.OK)
                    {
                        var props = new FMOD.REVERB_PROPERTIES(
    2100f,
    45f,
    90f,
    9000f,
    35f,
    100f,   // diffusion: max smear
    100f,   // density: remove discrete wall hits
    180f,
    -14f,
    5200f,
    85f,
    -7.5f   // keep your wet level
);
                        system.setReverbProperties(0, ref props);
                        system.setReverbProperties(1, ref props);
                    }
                }

                raw.setReverbLevel(0, reverb);
                raw.setReverbLevel(1, reverb);
                var dryVolume = spectating ? 0.6f : 1f;
                raw.setVolume(dryVolume);
            }
            catch (Exception ex) { Plugin.Log?.LogWarning("SpectatorMusicTweak: recreate reverb failed " + ex.Message); }
        }
    }


    // SwitchToSpectatorMode is patched in GameStatePatches (Patch_SwitchToSpectatorMode) — that hub
    // routes into ApplyIfWanted / ForceSpectatorMix here alongside the other tweaks that care.
}
