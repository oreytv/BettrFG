using System;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Components
{
    // scrolls a RawImage's uvRect offset forever, so a Repeat-wrapped texture tiles and drifts.
    // keeps the uvRect size (tile count) alone — only the position moves. speed is uv units/sec.
    public class MoveScrollUvRaw : MonoBehaviour
    {
        public MoveScrollUvRaw(IntPtr ptr) : base(ptr) { }

        public Vector2 speed = new Vector2(0.04f, 0.02f);
        public bool useUnscaledTime = true; // menus run while paused

        private RawImage _img;

        void Awake() { _img = GetComponent<RawImage>(); }

        void Update()
        {
            if (_img == null) return;
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            var r = _img.uvRect;
            r.position += speed * dt;
            _img.uvRect = r;
        }
    }
}
