using System;
using UnityEngine;

namespace BetterFG.UI.Components
{
    // drop this on anything (UI RectTransform or world Transform) to make it bob/pulse back and
    // forth along an axis forever. flexible: set the axis, speed (cycles per second) and strength
    // (amplitude). oscillates around wherever it started, so it doesn't fight layout/anchoring.
    public class MovePulseContinuous : MonoBehaviour
    {
        public MovePulseContinuous(IntPtr ptr) : base(ptr) { }

        public Vector3 axis = Vector3.up;   // direction of travel (normalized on Awake)
        public float speed = 1.5f;          // full cycles per second
        public float strength = 10f;        // peak offset along axis (px for UI, units for world)
        public bool useUnscaledTime = true; // menus run while paused, so default to unscaled

        private RectTransform _rt;
        private Vector2 _baseAnchored;
        private Vector3 _baseLocal;
        private float _t;

        void Awake()
        {
            if (axis.sqrMagnitude < 0.0001f) axis = Vector3.up;
            axis = axis.normalized;
            _rt = GetComponent<RectTransform>();
            if (_rt != null) _baseAnchored = _rt.anchoredPosition;
            else _baseLocal = transform.localPosition;
        }

        void OnEnable()
        {
            // re-grab the base in case it moved while disabled
            if (_rt != null) _baseAnchored = _rt.anchoredPosition;
            else _baseLocal = transform.localPosition;
            _t = 0f;
        }

        void Update()
        {
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            _t += dt * speed * Mathf.PI * 2f;
            float offset = Mathf.Sin(_t) * strength;

            if (_rt != null)
                _rt.anchoredPosition = _baseAnchored + new Vector2(axis.x, axis.y) * offset;
            else
                transform.localPosition = _baseLocal + axis * offset;
        }
    }
}
