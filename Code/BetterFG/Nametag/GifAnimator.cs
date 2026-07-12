using System;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.Nametag
{
    // Cycles a set of sprites on a SpriteRenderer (3D nametag) or Image (UI nameplate).
    // Lives on the icon GameObject so it dies automatically when the icon is destroyed.
    public class GifAnimator : MonoBehaviour
    {
        public GifAnimator(IntPtr ptr) : base(ptr) { }

        public string SourcePath;

        private Sprite[] _frames;
        private float[] _delays;
        private SpriteRenderer _sr;
        private Image _img;

        private int _index;
        private float _timer;

        public void Init(Sprite[] frames, float[] delays, SpriteRenderer sr, Image img)
        {
            _frames = frames;
            _delays = delays;
            _sr = sr;
            _img = img;
            _index = 0;
            _timer = 0f;
            ApplyFrame();
        }

        void Update()
        {
            if (_frames == null || _frames.Length < 2) return;
            _timer += Time.deltaTime;
            float wait = _delays != null && _index < _delays.Length ? _delays[_index] : 0.1f;
            if (_timer < wait) return;
            _timer -= wait;
            _index = (_index + 1) % _frames.Length;
            ApplyFrame();
        }

        private void ApplyFrame()
        {
            if (_frames == null || _index >= _frames.Length) return;
            var spr = _frames[_index];
            if (spr == null) return;
            if (_sr != null) _sr.sprite = spr;
            if (_img != null) _img.sprite = spr;
        }
    }
}
