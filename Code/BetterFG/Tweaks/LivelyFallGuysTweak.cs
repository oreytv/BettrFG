using System;
using BetterFG.Core;
using FGClient;
using UnityEngine;

namespace BetterFG.Tweaks
{
    // looping eye-blink for every Fall Guy in the scene. directly samples the eye-blink clip into
    // the bean's transforms in LateUpdate, after the animator has written its pose — so we always
    // win on the eye-scale curves without ever touching the animator/PlayableGraph (which would
    // fight emotes, idle, anything else animating that rig).
    //
    // event-driven: reapplied on MainMenuEntered / HandleServerStartRound.
    public class LivelyFallGuysTweak : BfgTweak
    {
        public LivelyFallGuysTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "lively_fall_guys";
        public override string TweakLabel => "Lively Fall Guys (blinking)";
        public override bool DefaultEnabled => false;

        public static LivelyFallGuysTweak Instance { get; private set; }
        void Awake() => Instance = this;

        public override void EnableTweak() => ReapplyAll();
        public override void DisableTweak() => ReapplyAll();

        public override void OnRoundStart() => ReapplyAll();
        public override void OnLevelEditorPlaytest() => ReapplyAll();
        public override void OnMainMenuEntered() => ReapplyAll();

        public static void ReapplyAll()
        {
            if (Instance == null) return;
            if (AssetManager.Instance == null) return;
            if (!AssetManager.Instance.animClips.TryGetValue("bettrfg_anim_eyes", out var clip) || clip == null) return;

            bool on = Instance.IsEnabled;

            foreach (var fgcc in UnityEngine.Object.FindObjectsOfType<FallGuysCharacterController>())
            {
                if (fgcc == null) continue;
                ApplyToRoot(fgcc.gameObject, clip, on);
            }

            // menu/lobby fall guys are display rigs without an fgcc — pick them up directly
            var mm = UnityEngine.Object.FindObjectOfType<MainMenuManager>();
            if (mm != null)
            {
                if (mm._menuFallGuy != null) ApplyToRoot(mm._menuFallGuy, clip, on);
                if (mm._lobbyFallGuy != null) ApplyToRoot(mm._lobbyFallGuy, clip, on);
            }
        }

        private static void ApplyToRoot(GameObject root, AnimationClip clip, bool on)
        {
            if (root == null) return;
            // clip curve paths are "SKELETON/Root/.../Eye_L_jnt" — sample on the GO that holds
            // SKELETON as a direct child so paths resolve cleanly and we never touch the bean root.
            var skel = FindSkeletonRoot(root.transform);
            if (skel == null) return;
            var host = skel.parent != null ? skel.parent.gameObject : skel.gameObject;

            var drv = host.GetComponent<BlinkDriverComponent>();
            if (on)
            {
                if (drv == null)
                {
                    drv = host.AddComponent<BlinkDriverComponent>();
                    drv.Init(clip);
                }
                drv.enabled = true;
            }
            else if (drv != null)
            {
                drv.enabled = false;
            }
        }

        private static Transform FindSkeletonRoot(Transform root)
        {
            if (root == null) return null;
            // BFS for a child named "SKELETON"
            var stack = new System.Collections.Generic.Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                for (int i = 0; i < t.childCount; i++)
                {
                    var c = t.GetChild(i);
                    if (c.name == "SKELETON") return c;
                    stack.Push(c);
                }
            }
            return null;
        }
    }

    public class BlinkDriverComponent : MonoBehaviour
    {
        public BlinkDriverComponent(IntPtr ptr) : base(ptr) { }

        private AnimationClip _clip;
        private float _time;
        private float _speed = 1f;

        public void Init(AnimationClip clip)
        {
            _clip = clip;
            _speed = UnityEngine.Random.Range(0.8f, 1.25f);
            _time = UnityEngine.Random.Range(0f, Mathf.Max(0.05f, clip.length));
        }

        void LateUpdate()
        {
            if (_clip == null) return;
            _time += Time.deltaTime * _speed;
            if (_clip.length > 0f)
            {
                while (_time >= _clip.length) _time -= _clip.length;
                if (_time < 0f) _time = 0f;
            }

            // snapshot the host transform — SampleAnimation can write to the root itself when a
            // curve targets an empty path (or the clip's authoring root was at this level). restore
            // afterwards so we never move the bean.
            var t = transform;
            var pos = t.localPosition;
            var rot = t.localRotation;
            var scl = t.localScale;

            _clip.SampleAnimation(gameObject, _time);

            t.localPosition = pos;
            t.localRotation = rot;
            t.localScale = scl;
        }
    }
}
