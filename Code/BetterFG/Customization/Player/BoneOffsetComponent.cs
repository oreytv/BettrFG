using System.Collections.Generic;
using UnityEngine;

namespace BetterFG.Customization.Player
{
    public class BoneOffsetComponent : MonoBehaviour
    {
        public BoneOffsetComponent(System.IntPtr ptr) : base(ptr) { }

        private struct BoneOffset
        {
            public Transform bone;
            public Vector3 position;
            public bool hasPosition;
            public Vector3 rotationOffset;
            public bool hasRotation;
        }

        private readonly List<BoneOffset> _offsets = new List<BoneOffset>();

        public void SetOffsets(List<BoneOffsetEntry> entries, Dictionary<string, Transform> skeletonLookup)
        {
            _offsets.Clear();
            if (entries == null) return;
            foreach (var e in entries)
            {
                if (string.IsNullOrEmpty(e.bone)) continue;
                if (!skeletonLookup.TryGetValue(e.bone, out var t)) continue;
                _offsets.Add(new BoneOffset
                {
                    bone = t,
                    position = e.localPosition,
                });
            }
        }

        void LateUpdate()
        {
            for (int i = 0; i < _offsets.Count; i++)
            {
                var o = _offsets[i];
                if (o.bone == null) continue;
                if (o.hasPosition)
                    o.bone.localPosition = o.position;
                if (o.hasRotation)
                    o.bone.localEulerAngles = o.bone.localEulerAngles + o.rotationOffset;
            }
        }
    }
}