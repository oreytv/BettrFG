using System;
using System.IO;
using System.Reflection;
using FG.Common;
using FGClient;
using NAudio.Wave;
using UnityEngine;

namespace BetterFG.Services
{
    // plays an arbitrary file as menu background music. doesn't touch MainMenuManager — the user
    // can mute fall guys' music themselves. volume tracks the game's MasterVolume * MusicVolume
    // (×VOLUME_SCALE) so it sits at a sane mix without us inventing our own slider.
    //
    // dispatches by extension to keep NAudio's Wasapi/MediaFoundation paths out (AudioFileReader
    // tries to load NAudio.Wasapi which isn't shipped — Mp3FileReader/WaveFileReader stay in
    // NAudio.Core). always loops.
    public static class MenuMusicService
    {
        private const string KEY_PATH = "menu.music.path";
        private const string KEY_ENABLED = "menu.music.enabled";
        private const float VOLUME_SCALE = 0.13f;

        private static IWavePlayer _output;
        private static WaveStream _reader;
        private static LoopStream _loop;
        private static VolumeWaveProvider16 _vol;
        private static string _path = "";
        private static bool _enabled;
        // bumped on every Play/Stop. a background Play task carries the gen it was started with
        // and tears itself down if it's stale by the time the file finished opening.
        private static int _gen;

        public static string CurrentPath => _path;
        public static bool Enabled => _enabled;
        public static bool IsPlaying => _output != null;

        // %APPDATA%\BettrFG\Music — where downloaded tracks land.
        public static string MusicDir
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dir = Path.Combine(appData, "BettrFG", "Music");
                try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        public static void Pause() { try { _output?.Pause(); } catch { } }
        public static void Resume() { try { _output?.Play(); } catch { } }

        public static void Init()
        {
            _path = SettingsService.Get(KEY_PATH, "");
            _enabled = SettingsService.Get(KEY_ENABLED, "false") == "true";
        }

        // sets the file; only autoplays if music is already playing OR custom mode is enabled.
        public static void SetPath(string path)
        {
            bool wasPlaying = IsPlaying;
            _path = path ?? "";
            SettingsService.Set(KEY_PATH, _path);
            if (wasPlaying || _enabled)
            {
                Stop();
                if (!string.IsNullOrEmpty(_path)) Play();
            }
        }

        // master on/off — when off, kills the current track AND unpauses the game's menu music
        // (since the StopMusic patch only silenced it because we asked it to). when on, starts our
        // track and silences the game's directly so the user doesn't have to wait for the next
        // StopMusic/PauseMusic call to fire.
        public static void SetEnabled(bool on)
        {
            _enabled = on;
            SettingsService.Set(KEY_ENABLED, on ? "true" : "false");
            if (on)
            {
                if (!IsPlaying && !string.IsNullOrEmpty(_path)) Play();
                SetGameMenuMusicPaused(true);
            }
            else
            {
                Stop();
                SetGameMenuMusicPaused(false);
            }
        }

        // direct FMOD pause/unpause on MainMenuManager._menuMusic. used by the on/off toggle so
        // the change takes effect immediately without waiting for a game state event.
        public static void SetGameMenuMusicPaused(bool paused)
        {
            try
            {
                var mmm = UnityEngine.Object.FindObjectOfType<FGClient.MainMenuManager>();
                var inst = mmm?._menuMusic;
                if (inst != null) inst.SetPaused(paused);
            }
            catch { }
        }

        public static void Play()
        {
            Stop();
            if (string.IsNullOrEmpty(_path) || !File.Exists(_path))
            {
                Plugin.Log.LogWarning($"MenuMusic: no file at '{_path}'");
                return;
            }
            // file open + mp3 decode init + WaveOutEvent driver init can take a few hundred ms.
            // we're called from hot Harmony postfixes (OnMainMenuEntered, state changes), so run
            // the whole setup on a background thread. _gen guards against stale Play tasks
            // landing their output on the service after a newer Stop/Play has happened.
            int gen = ++_gen;
            string path = _path;
            float vol = CurrentVolume();
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    WaveStream reader;
                    if (ext == ".mp3") reader = new Mp3FileReader(path);
                    else if (ext == ".wav") reader = new WaveFileReader(path);
                    else { Plugin.Log.LogWarning($"MenuMusic: unsupported extension '{ext}' (mp3/wav only)"); return; }

                    var loop = new LoopStream(reader);
                    var vp = new VolumeWaveProvider16(loop) { Volume = vol };
                    var output = new WaveOutEvent();
                    output.Init(vp);

                    if (gen != _gen)
                    {
                        try { output.Dispose(); } catch { }
                        try { reader.Dispose(); } catch { }
                        return;
                    }
                    output.Play();
                    _reader = reader;
                    _loop = loop;
                    _vol = vp;
                    _output = output;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"MenuMusic: play failed: {ex.Message}");
                }
            });
        }

        public static void Stop()
        {
            // grab the refs and null them out synchronously, then dispose on a background thread —
            // WaveOutEvent.Stop()+Dispose blocks until the playback thread joins (can be 100–500ms)
            // and we're called from Harmony postfixes on hot game paths (state changes etc).
            // bumping _gen invalidates any in-flight Play task so it doesn't republish stale refs.
            _gen++;
            var output = _output;
            var loop = _loop;
            var reader = _reader;
            _output = null;
            _reader = null;
            _loop = null;
            _vol = null;
            if (output == null && loop == null && reader == null) return;
            System.Threading.Tasks.Task.Run(() =>
            {
                try { output?.Stop(); } catch { }
                try { output?.Dispose(); } catch { }
                try { loop?.Dispose(); } catch { }
                try { reader?.Dispose(); } catch { }
            });
        }

        private static float CurrentVolume() => AudioService.GameMusicVolume * VOLUME_SCALE;

        // call every frame so live audio settings changes in the game pause menu actually take
        // effect on our currently-playing track.
        public static void TickVolume()
        {
            if (_vol != null) _vol.Volume = CurrentVolume();
        }

        // stop the custom track once the player leaves the menu / lobby / connecting flow. routed
        // through GameStateMachine.ReplaceCurrentState postfix in GameStatePatches.
        public static void OnReplaceCurrentState(GameStateMachine.IGameState newState)
        {
            if (!IsPlaying) return;
            if (newState == null) return;
            if (newState.TryCast<StateMainMenu>() != null) return;
            if (newState.TryCast<StateMatchmaking>() != null) return;
            if (newState.TryCast<StateDisconnectingFromServer>() != null) return;
            if (newState.TryCast<StatePrivateLobby>() != null) return;
            if (newState.TryCast<StateConnectionAuthentication>() != null) return;
            if (newState.TryCast<StateConnectToGame>() != null) return;
            Stop();
        }

        private sealed class LoopStream : WaveStream
        {
            private readonly WaveStream _src;

            public LoopStream(WaveStream source) { _src = source; }

            public override WaveFormat WaveFormat => _src.WaveFormat;
            public override long Length => _src.Length;
            public override long Position { get => _src.Position; set => _src.Position = value; }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int read = 0;
                while (read < count)
                {
                    int n = _src.Read(buffer, offset + read, count - read);
                    if (n == 0)
                    {
                        if (_src.Position == 0) break;
                        _src.Position = 0;
                    }
                    read += n;
                }
                return read;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _src?.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
