using System;
using System.IO;
using FGClient;
using NAudio.Wave;
using UnityEngine;

namespace BetterFG.Features.UnityRound
{
    // Custom round music, gameplay only. The game uses FMOD so Unity AudioSources are silent — we
    // play the song through NAudio (WaveOut) instead, and pause the game's FMOD music instance while
    // it runs. Started at round start, stopped on ClientGameManager.Shutdown().
    //
    // Only used by the normal (downloaded) round path, never the level editor.

    public static class RoundMusicService
    {
        private static byte[] _pendingBytes;   // downloaded song (mp3/wav), waiting for round start

        private static IWavePlayer _output;
        private static WaveStream _reader;
        private static LoopStream _loop;
        private static VolumeWaveProvider16 _vol;
        private static MemoryStream _ms;
        private static bool _playing;

        private static bool _fmodPaused;

        public static bool HasPending => _pendingBytes != null;

        // pause both our NAudio output and the game's FMOD music together, so alt-tabbing
        // doesn't leave one or the other playing. on resume we only restart NAudio if it was
        // actually running — Stop() nulls _output so we use _playing to know.
        public static void SetPaused(bool paused)
        {
            if (!_playing) return;
            try
            {
                if (paused) _output?.Pause();
                else _output?.Play();
            }
            catch { }
        }

        // ── phase 1: stash the downloaded bytes ─────────────────────────────────

        public static void SetPending(byte[] bytes, string fileNameOrExt)
        {
            _pendingBytes = bytes;
        }

        public static void ClearPending()
        {
            _pendingBytes = null;
        }

        // ── round start (gameplay): play it ─────────────────────────────────────

        public static void StartIfPending()
        {
            if (_pendingBytes == null) return;
            if (_playing) Stop();

            var bytes = _pendingBytes;
            _pendingBytes = null;   // consume it — otherwise the same song replays every round that skips the download path

            try
            {
                _ms = new MemoryStream(bytes);
                _reader = new Mp3FileReader(_ms);
                _loop = new LoopStream(_reader);
                _vol = new VolumeWaveProvider16(_loop) { Volume = CurrentVolume() };
                _output = new WaveOutEvent();
                _output.Init(_vol);
                _output.Play();
                _playing = true;

                TryPauseGameMusic();
                Plugin.Log.LogInfo("RoundMusicService: custom song started");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"RoundMusicService: start failed: {ex.Message}");
                Stop();
            }
        }

        // MasterVolume * MusicVolume from the player's audio settings, then 45% of that
        private const float VOLUME_SCALE = 0.45f;

        private static float CurrentVolume() => BetterFG.Services.AudioService.GameMusicVolume * VOLUME_SCALE;

        // call every frame so live audio settings changes in the game pause menu actually take
        // effect on our currently-playing track.
        public static void TickVolume()
        {
            if (_vol != null) _vol.Volume = CurrentVolume();
        }

        // returns true once the game's FMOD music has been paused (so a retry loop can stop).
        public static bool TryPauseGameMusic()
        {
            if (!_playing) return true;   // nothing playing -> nothing to gate on
            if (_fmodPaused) return true;
            try
            {
                var inst = GameMusicInstance();
                if (inst == null) return false;

                inst.SetPaused(true);
                _fmodPaused = true;
                return true;
            }
            catch (Exception ex) { return false; }
        }

        private static void ResumeGameMusic()
        {
            if (!_fmodPaused) return;
            try { GameMusicInstance()?.SetPaused(false); }
            catch { }
            _fmodPaused = false;
        }

        private static EventInstanceReference GameMusicInstance()
        {
            var gs = GlobalGameStateClient.Instance?._gameStateMachine?.CurrentState;
            var cgm = gs.TryCast<StateGameInProgress>()?._clientGameManager;
            return cgm?._musicInstance;
        }

        // ── shutdown / stop ─────────────────────────────────────────────────────

        public static void Stop()
        {
            try { _output?.Stop(); } catch { }
            try { _output?.Dispose(); } catch { }
            try { _loop?.Dispose(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _ms?.Dispose(); } catch { }
            _output = null; _loop = null; _reader = null; _ms = null; _vol = null;

            ResumeGameMusic();
            _playing = false;
        }
    }

    // loops a WaveStream forever (NAudio has no built-in loop provider)
    internal class LoopStream : WaveStream
    {
        private readonly WaveStream _source;
        public LoopStream(WaveStream source) { _source = source; }

        public override WaveFormat WaveFormat => _source.WaveFormat;
        public override long Length => _source.Length;
        public override long Position { get => _source.Position; set => _source.Position = value; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = _source.Read(buffer, offset + total, count - total);
                if (read == 0)
                {
                    if (_source.Position == 0) break; // empty source, avoid infinite loop
                    _source.Position = 0;             // loop back to the start
                }
                total += read;
            }
            return total;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _source?.Dispose();
            base.Dispose(disposing);
        }
    }
}
