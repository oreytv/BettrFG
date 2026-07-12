using System;
using System.IO;
using System.Reflection;
using FGClient;
using NAudio.Wave;
using UnityEngine;

namespace BetterFG.Services
{
    public static class AudioService
    {
        // live read of the game's master * music volume. used by anything we play through NAudio
        // (menu music, custom round music) so changing audio settings in the game pause menu
        // actually takes effect mid-track instead of staying at the value we captured at Start.
        public static float GameMusicVolume
        {
            get
            {
                try
                {
                    var audio = GlobalGameStateClient.Instance?.PlayerProfile?.AudioSettings;
                    if (audio != null)
                        return Mathf.Clamp01(audio.MasterVolume) * Mathf.Clamp01(audio.MusicVolume);
                }
                catch { }
                return 1f;
            }
        }

        private const string PREFIX = "BetterFG.assets.audio.";

        private static float _masterVolume = 1f;
        private static bool _hoverEnabled = true;
        private static bool _clickEnabled = true;
        private static bool _tabEnabled = true;

        public static void SetMasterVolume(float v) => _masterVolume = Mathf.Clamp01(v);
        public static void SetHoverEnabled(bool v) => _hoverEnabled = v;
        public static void SetClickEnabled(bool v) => _clickEnabled = v;
        public static void SetTabEnabled(bool v) => _tabEnabled = v;

        public static void PlayButtonHoverOn() { if (_hoverEnabled) Play("buttonhoveron.wav"); }
        public static void PlayButtonClick() { if (_clickEnabled) Play("buttonclick.wav"); }
        public static void PlayTabOpen() { if (_tabEnabled) Play("tabopen.wav"); }
        public static void PlaySlotDwopdmdmom() { if (_tabEnabled) Play("slotdropdown.wav"); }
        public static void PlayTooltipShow() { if (_tabEnabled) Play("tipshow.wav"); }
        public static void PlayTabClose() { if (_tabEnabled) Play("tabclose.wav"); }
        public static void PlayApply() { if (_clickEnabled) Play("apply.wav"); }
        public static void PlayPoof() { Play("poof003.wav"); }
        public static void PlayPB() { Play("pb.wav"); }
        public static void PlayStarsUp() { Play("starsup.wav"); }
        public static void PlayControllerClick() { if (_clickEnabled) Play("click.wav"); }
        public static void PlayHideUI() { Play("hideui.wav"); }
        public static void PlayShowUI() { Play("showui.wav"); }

        public static void Init()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (float.TryParse(SettingsService.Get("audio.master.volume", "0.4"),
                System.Globalization.NumberStyles.Float, ci, out float v))
                _masterVolume = Mathf.Clamp01(v);

            _hoverEnabled = SettingsService.Get("audio.sfx.hover", "false") != "false";
            _clickEnabled = SettingsService.Get("audio.sfx.click", "true") != "false";
            _tabEnabled = SettingsService.Get("audio.sfx.tab", "true") != "false";
        }

        private static void Play(string filename)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var stream = asm.GetManifestResourceStream(PREFIX + filename);
                if (stream == null)
                {
                    Debug.LogWarning($"[AudioService] Not found: {PREFIX + filename}");
                    return;
                }

                var ms = new MemoryStream();
                stream.CopyTo(ms);
                stream.Dispose();
                ms.Position = 0;

                var reader = new WaveFileReader(ms);
                var vol = new VolumeWaveProvider16(reader) { Volume = _masterVolume };
                var output = new WaveOutEvent();
                output.Init(vol);
                output.Play();

                output.PlaybackStopped += (_, __) =>
                {
                    output.Dispose();
                    reader.Dispose();
                    ms.Dispose();
                };
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AudioService] Play({filename}) failed: {ex.Message}");
            }
        }
    }
}