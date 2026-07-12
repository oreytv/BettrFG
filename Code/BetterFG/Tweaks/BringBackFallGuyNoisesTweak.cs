using System;
using FG.Common;
using FG.Common.Audio;
using FGClient;
using FMODUnity;
using HarmonyLib;
using UnityEngine;

namespace BetterFG.Tweaks
{
    // "Bring Back Fall Guy noises"
    //
    // somewhere in an update we stopped hearing other fall guys around us - their jump/dive still
    // play (those are physics/foley character audio), but their VO (the scream/effort/hit voices)
    // went silent. only the local bean's VO comes through now.
    //
    // all the VO events live on AudioEventMasterData as AudioEvent2D3DPairSO refs: VO_Effort,
    // VO_Falling, VO_Hit, VO_Jump. each pair has a 2D event (played for the local player, no
    // spatialisation) and a 3D event (played for everyone else, spatialised at the bean pos).
    // jump/dive working but VO not strongly suggests the 3D VO events are either empty in the
    // shipped master data, or the playback path is being gated for remote beans.
    //
    // for now this tweak is DISCOVERY: at round start it dumps everything we could patch -
    //   - every fmod bus path/name (AudioManager.BusPaths)
    //   - every AudioEvent2D3DPairSO on the master data, 2D vs 3D string, VO ones flagged
    //   - the suspect AudioManager gating bools (RemovePlayers/DynamicObject/etc.)
    //   - CanCharacterPlayAudio(...) for each character controller in the scene (local + remote)
    // so we can eyeball which lever actually brings the screams back. once we know, the real fix
    // (force the bool / patch CanCharacterPlayAudio / repoint the empty 3D event) goes in THIS
    // SAME CLASS - we are not bloating GameStatePatches.cs.
    public class BringBackFallGuyNoisesTweak : BfgTweak
    {
        public BringBackFallGuyNoisesTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "bring_back_fallguy_noises";
        public override string TweakLabel => "Bring Back Fall Guy noises";
        public override bool DefaultEnabled => true;

        public static BringBackFallGuyNoisesTweak Instance { get; private set; }

        internal static bool Active;

        // NOTE: do NOT override Start(). the base BfgTweak.Start() is what loads IsEnabled from
        // SettingsService and schedules EnableTweak when it was saved on - shadowing it with our
        // own Start() killed both the load and the save (that was the "setting won't stick" bug).
        // Active is driven entirely by Enable/DisableTweak, which the base toggles and persists.
        void Awake() => Instance = this;

        public override void EnableTweak() => Active = true;
        public override void DisableTweak() => Active = false;

        public override void OnRoundStart() => ApplyFix();

        // why we have to drive this ourselves: the falling "woo" is triggered from the in-air motor
        // state. the game only ticks motor states for the LOCALLY controlled bean - remote beans are
        // network puppets and never run their state locally, so the woo never fires for them. there's
        // no bool to flip; the game just doesn't simulate remotes.
        //
        // the hook is FallGuysCharacterController.OnManagedUpdate_Remote - the game runs this per
        // frame for each REMOTE bean that exists (driven by its own update manager, so no scene scan
        // and it scales with however many remotes are actually around). in there we watch IsFalling:
        // on the rising edge (bean just started falling) we fire its VO_Falling once, with a cooldown
        // so one fall = one woo. local bean is untouched - the game still woos it itself.
        private static readonly AudioParamContainer _params = new AudioParamContainer();
        private const float RewooCooldown = 1.25f; // one fire per event, not a machine gun
        // IsFalling alone trips the instant they leave the ground (every little hop), so the woo
        // also requires real downward speed. AnimatedPlayerVelocity.y is the replicated speed for
        // remotes; it's dampened so this is gentler than the game's local -18. -7 felt right.
        private const float FallVelThreshold = -3f;
        // low cutoff to drop resting/floor jitter - remote relativeVelocity runs small, so keep it
        // gentle or real bonks get skipped. the game's own threshold does the real filtering.
        private const float HitImpactThreshold = 0.5f;
        // how hard a landing has to be (downward speed when touching ground) to thump + grunt.
        // remote AnimatedPlayerVelocity.y is dampened so keep this low or real drops get skipped.
        private const float HardLandingThreshold = 4f;

        // which remote sounds we drive in the per-frame tick. NOT jump (already has its own sound)
        // and NOT effort (mis-fired on grabs). object/bean hits go through OnCollisionEnter.
        //   Falling -> VO_Falling (the woooo)
        //   Dive    -> the Dive sfx pair (NOT a VO_ event - the dive "fwoomp")
        private enum Voe { Falling, Dive }

        // key = (instanceID, event). was the condition true last tick (edge detect) + last fire time.
        private static readonly System.Collections.Generic.Dictionary<long, bool> _was = new System.Collections.Generic.Dictionary<long, bool>();
        private static readonly System.Collections.Generic.Dictionary<long, float> _last = new System.Collections.Generic.Dictionary<long, float>();
        // per-bean landing detection: were they on the ground last tick, and their worst fall speed
        // while airborne (so a long drop thumps harder than a short hop).
        private static readonly System.Collections.Generic.Dictionary<int, bool> _wasGrounded = new System.Collections.Generic.Dictionary<int, bool>();
        private static readonly System.Collections.Generic.Dictionary<int, float> _peakFall = new System.Collections.Generic.Dictionary<int, float>();

        // called from the OnManagedUpdate_Remote patch for each remote bean, every frame it exists.
        internal static void TickRemote(FallGuysCharacterController c)
        {
            if (c == null) return;
            try
            {
                var master = AudioManager.EventMasterData;
                if (master == null) return;

                // real plunge -> woo
                bool falling = c.IsFalling && c.AnimatedPlayerVelocity.y <= FallVelThreshold;
                Fire(c, Voe.Falling, falling, master.VO_Falling); // the "woooo"
                Fire(c, Voe.Dive, c.IsDiving, master.Dive);       // dive sfx (not a VO_)

                // hard floor landing -> thump + ow. OnCollisionEnter doesn't reliably cover this for
                // remotes (and a face-plant after a big fall is a "hit"), so derive it: track the
                // worst downward speed while airborne, then when they touch ground, if it was a hard
                // drop, route it through the game's own impact audio.
                int id = c.GetInstanceID();
                bool grounded = c.IsTouchingGround;
                float yvel = c.AnimatedPlayerVelocity.y;

                if (!grounded)
                {
                    _peakFall.TryGetValue(id, out float peak);
                    if (yvel < peak) _peakFall[id] = yvel; // more negative = harder fall
                }

                _wasGrounded.TryGetValue(id, out bool wasGrounded);
                _wasGrounded[id] = grounded;

                if (grounded && !wasGrounded) // just landed
                {
                    _peakFall.TryGetValue(id, out float peak);
                    _peakFall[id] = 0f;
                    float landSpeed = -peak; // positive magnitude of the drop
                    if (landSpeed >= HardLandingThreshold)
                        PlayImpactAt(c, Mathf.Max(landSpeed, 14f));
                }
            }
            catch { }
        }

        // called from the OnCollisionEnter patch for remote beans. forwards the collision into the
        // game's own per-character collision-audio path (the exact thing that plays the local bean's
        // bonk), so remotes get Impact + VO_Hit with the game's own strength/threshold logic.
        //
        // remote beans are interpolated, not real physics, so collision.relativeVelocity is small &
        // noisy - gating on it skipped most real bonks (that's why it only fired a few times). so we
        // just use a low cutoff to drop resting/floor micro-contacts and hand the game a healthy
        // fixed strength, letting ITS internal threshold decide what's loud enough to play.
        internal static void HandleRemoteCollision(FallGuysCharacterController c, UnityEngine.Collision collision)
        {
            if (c == null || collision == null) return;
            try
            {
                float rel = collision.relativeVelocity.magnitude;
                if (rel < HitImpactThreshold) return; // drop feather touches / resting jitter

                var fx = c.FXController;
                if (fx == null) return;

                var contact = collision.GetContact(0);
                // feed a solid strength (clamped up) so the game's own audio threshold passes for a
                // real collision rather than getting filtered by the dampened remote velocity.
                float strength = Mathf.Max(rel, 12f);
                fx.HandleObjectCollisionAudio(collision.gameObject, contact.normal, strength, contact.point);
            }
            catch { }
        }

        // hard floor landing -> the game's own impact audio at the bean's position. surface Default
        // is fine; the game picks the thump + (if hard enough) the ow based on strength.
        private static void PlayImpactAt(FallGuysCharacterController c, float strength)
        {
            try
            {
                var fx = c.FXController;
                if (fx == null) return;
                fx.PlayImpactAudio(c.transform.position, AudioConsts.AudioMaterialType.None, strength);
            }
            catch { }
        }

        // edge-triggered one-shot: plays `pair` for `c` the moment `condition` goes true, then sits
        // on cooldown so a held state (or a frame of jitter) doesn't machine-gun it.
        private static void Fire(FallGuysCharacterController c, Voe ev, bool condition, AudioEvent2D3DPairSO pair)
        {
            long key = Key(c, ev);

            _was.TryGetValue(key, out bool was);
            _was[key] = condition;
            if (!condition || was) return; // rising edge only

            if (IsOnCooldown(c, ev)) return;
            StampCooldown(c, ev);
            PlayPair(c, pair);
        }

        private static long Key(FallGuysCharacterController c, Voe ev) => ((long)c.GetInstanceID() << 4) | (long)ev;

        private static bool IsOnCooldown(FallGuysCharacterController c, Voe ev)
        {
            _last.TryGetValue(Key(c, ev), out float last);
            return Time.time - last < RewooCooldown;
        }

        private static void StampCooldown(FallGuysCharacterController c, Voe ev) => _last[Key(c, ev)] = Time.time;

        private static void PlayPair(FallGuysCharacterController c, AudioEvent2D3DPairSO pair)
        {
            if (pair == null) return;
            AudioManager.PlayCharacterAudio(pair, c, c.transform.position, _params);
        }

        // the actual fix, applied at round start:
        //   - RemovePlayersAudioActive is TRUE by default -> it culls other players' audio
        //   - the remote VO rides bus:/MASTER/BUS_VO/BUS_VO_3D, which can be muted/zeroed
        // so: kill the cull flag and make sure the VO buses aren't muted/paused/silent.
        internal static void ApplyFix()
        {
            try
            {
                var am = UnityEngine.Object.FindObjectOfType<AudioManager>();
                if (am != null) am.RemovePlayersAudioActive = false;
            }
            catch { }

            UnmuteBus("bus:/MASTER/BUS_VO");
            UnmuteBus("bus:/MASTER/BUS_VO/BUS_VO_3D");
            UnmuteBus("bus:/MASTER/BUS_VO/BUS_VO_2D");
        }

        private static void UnmuteBus(string path)
        {
            try
            {
                var bus = RuntimeManager.GetBus(path);
                if (!bus.isValid()) return;

                bus.getMute(out bool muted);
                bus.getPaused(out bool paused);
                bus.getVolume(out float vol, out float _);

                if (muted) bus.setMute(false);
                if (paused) bus.setPaused(false);
                if (vol <= 0.001f) bus.setVolume(1f);
            }
            catch { }
        }
    }

    // THE fix. the game runs OnManagedUpdate_Remote on each remote bean every frame it exists. we
    // postfix it and let TickRemote watch IsFalling and fire the woo on the falling edge. only
    // remote beans ever reach this method, so we don't touch the local bean (which woos itself).
    [HarmonyPatch(typeof(FallGuysCharacterController), nameof(FallGuysCharacterController.OnManagedUpdate_Remote))]
    internal static class BringBackFallGuyNoises_RemoteTick
    {
        [HarmonyPostfix]
        public static void Postfix(FallGuysCharacterController __instance)
        {
            if (!BringBackFallGuyNoisesTweak.Active) return;
            BringBackFallGuyNoisesTweak.TickRemote(__instance);
        }
    }

    // hits. Unity fires OnCollisionEnter on remote beans too (they have colliders + rigidbodies),
    // but the game gates the resulting bonk audio to the local player. so for REMOTE beans we route
    // the collision through the game's own collision-audio path (Impact + VO_Hit, proper strength).
    [HarmonyPatch(typeof(FallGuysCharacterController), nameof(FallGuysCharacterController.OnCollisionEnter))]
    internal static class BringBackFallGuyNoises_Collision
    {
        [HarmonyPostfix]
        public static void Postfix(FallGuysCharacterController __instance, UnityEngine.Collision collision)
        {
            if (!BringBackFallGuyNoisesTweak.Active || __instance == null) return;
            if (__instance.IsLocalPlayer) return; // game already does the local bean's bonk
            BringBackFallGuyNoisesTweak.HandleRemoteCollision(__instance, collision);
        }
    }
}
