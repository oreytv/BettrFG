using System;
using UnityEngine;

namespace BetterFG.UI.Components
{
    // drop this on a UI object to make its CanvasGroup alpha pulse up and down forever — a soft
    // attention-grabber for hint text. adds a CanvasGroup if there isn't one. oscillates between
    // min and max so it never fully disappears.
    public class AlphaPulseContinuousFade : MonoBehaviour
    {
        public AlphaPulseContinuousFade(IntPtr ptr) : base(ptr) { }

        public float speed = 1.1f;          // full cycles per second
        public float min = 0.25f;           // dim end
        public float max = 1f;              // bright end
        public bool useUnscaledTime = true; // menus run while paused, so default to unscaled

        private CanvasGroup _cg;
        private float _t;

        void Awake()
        {
            _cg = GetComponent<CanvasGroup>();
            if (_cg == null) _cg = gameObject.AddComponent<CanvasGroup>();
        }

        void OnEnable() { _t = 0f; }

        void Update()
        {
            if (_cg == null) return;
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            _t += dt * speed * Mathf.PI * 2f;
            _cg.alpha = Mathf.Lerp(min, max, (Mathf.Sin(_t) * 0.5f) + 0.5f);
        }
    }
}
