using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using FG.Common.Character;
using FG.Common.Character.MotorSystem;
using FG.Common.CMS;
using FGClient;
using NAudio.Wave;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace BetterFG.Customization.Social
{
    // custom emotes. a real AnimationClip can't come from a raw file at runtime, so the clip ships
    // inside an AssetBundle loaded from disk. AnimationClipLoadable is a GUID-backed addressable ref
    // with no way to hold a runtime clip, so we can't swap it on the option. instead we map our clip
    // to the option's CMSGroupID and play it ourselves in the PlayEmote hook (see SocialPatches).
    public static class EmoteInjectionService
    {
        private static List<EmoteEntry> _entries = new List<EmoteEntry>();

        // slot index (0-7, matches MotorFunctionEmote._emoteOptions / PlayEmote's emoteIndex) -> our
        // clip / sound. keyed by index because the option instance at playback differs from the wheel
        // slot we injected, and CMSGroupID is shared/empty so it matched every emote.
        internal static readonly Dictionary<int, AnimationClip> CustomClips = new Dictionary<int, AnimationClip>();
        internal static readonly Dictionary<int, string> SoundPaths = new Dictionary<int, string>();

        // cache loaded bundles by path (LoadFromMemory twice on the same bundle throws)
        private static readonly Dictionary<string, AssetBundle> _bundles = new Dictionary<string, AssetBundle>();
        private static readonly System.Random _cabRng = new System.Random();

        // options whose icon sprite we replaced -> their original sprite, so we can put it back
        private static readonly Dictionary<EmotesOption, ItemDefinitionSO.CacheableAtlasSprite> OriginalSprites
            = new Dictionary<EmotesOption, ItemDefinitionSO.CacheableAtlasSprite>();

        public static string LastStatus { get; private set; } = "";

        public static void SetEntries(List<EmoteEntry> entries) => _entries = entries;

        public static void ApplyAll(List<EmoteEntry> entries)
        {
            _entries = entries;
            RestoreSlots(null);
            InjectSlots(null);
        }

        internal static void ReapplyToWheel(SocialPrimeHandler primeHandler)
        {
            RestoreSlots(primeHandler);
            InjectSlots(primeHandler);
        }

        // drop a cached bundle (after its file was re-downloaded) so the next LoadClip reads the new
        // file from disk. Unload(false) keeps any clip a currently-playing emote still holds.
        internal static void EvictBundle(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            if (_bundles.TryGetValue(path, out var b))
            {
                try { b?.Unload(false); } catch { }
                _bundles.Remove(path);
            }
        }

        private static AnimationClip LoadClip(EmoteEntry e)
        {
            if (string.IsNullOrEmpty(e.bundlePath) || !File.Exists(e.bundlePath)) return null;
            try
            {
                if (!_bundles.TryGetValue(e.bundlePath, out var bundle) || bundle == null)
                {
                    bundle = AssetBundle.LoadFromMemory(UniquifyCab(File.ReadAllBytes(e.bundlePath)));
                    _bundles[e.bundlePath] = bundle;
                }
                if (bundle == null) return null;

                // use the NON-generic LoadAsset — the generic LoadAsset<T>/LoadAllAssets<T> hits a
                // stripped il2cpp method ("Method unstripping failed"). load by name then Cast.
                if (!string.IsNullOrEmpty(e.clipName))
                {
                    var named = bundle.LoadAsset(e.clipName);
                    return named != null ? named.TryCast<AnimationClip>() : null;
                }

                // no name given — walk the bundle and return the first asset that is an AnimationClip
                foreach (string name in bundle.GetAllAssetNames())
                {
                    var asset = bundle.LoadAsset(name);
                    var clip = asset != null ? asset.TryCast<AnimationClip>() : null;
                    if (clip != null) return clip;
                }
                return null;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"EmoteInjection: clip load failed ({e.bundlePath}): {ex.Message}");
                return null;
            }
        }

        // custom emote bundles are often exported with the same internal serialized-file name
        // ("CAB-<32 hex>"), so loading a second one throws "another AssetBundle with the same files is
        // already loaded". rewrite that id to a random one (identical length so no offsets shift)
        // before LoadFromMemory. only touches uncompressed/plaintext headers; if no CAB id is found
        // the bytes are returned untouched and we just load as before.
        private static byte[] UniquifyCab(byte[] bytes)
        {
            try
            {
                const string hex = "0123456789abcdef";
                for (int i = 0; i <= bytes.Length - 36; i++)
                {
                    if (bytes[i] != (byte)'C' || bytes[i + 1] != (byte)'A' || bytes[i + 2] != (byte)'B' || bytes[i + 3] != (byte)'-')
                        continue;
                    var oldId = new byte[36];
                    Array.Copy(bytes, i, oldId, 0, 36);
                    bool valid = true;
                    for (int k = 4; k < 36; k++)
                    {
                        byte c = oldId[k];
                        if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) { valid = false; break; }
                    }
                    if (!valid) continue;

                    var newId = new byte[36];
                    Array.Copy(oldId, 0, newId, 0, 4);
                    for (int k = 4; k < 36; k++) newId[k] = (byte)hex[_cabRng.Next(16)];

                    // swap every occurrence of the same id (it shows up in the directory and the
                    // serialized file's self-reference) so they still point at each other
                    for (int j = 0; j <= bytes.Length - 36; j++)
                    {
                        bool match = true;
                        for (int k = 0; k < 36; k++) if (bytes[j + k] != oldId[k]) { match = false; break; }
                        if (match) { Array.Copy(newId, 0, bytes, j, 36); j += 35; }
                    }
                    break;
                }
            }
            catch { }
            return bytes;
        }

        // handler is the wheel we're injecting into. the DisplayWheel patch passes the specific
        // instance being shown (our Social-tab visualizer clone), so its own wheel dict gets the
        // custom slots — FindObjectOfType would grab whichever handler is first and inject into the
        // wrong one, leaving the clone blank. null falls back to first found (Apply All button).
        // find the wheel list that holds EmotesOption slots (key varies by version). shared by
        // Restore/Inject so both operate on the exact same list instance.
        private static Il2CppSystem.Collections.Generic.List<ItemDefinitionSO> ResolveSlots(SocialPrimeHandler handler)
        {
            var primeHandler = handler ?? UnityEngine.Object.FindObjectOfType<SocialPrimeHandler>();
            if (primeHandler == null) { LastStatus = "Not in a round yet."; return null; }

            var wheelDict = primeHandler.HighlightedSocialWheel?.SocialItemsDictionary;
            if (wheelDict == null) { LastStatus = "SocialItemsDictionary not found."; return null; }

            foreach (var k in wheelDict.Keys)
            {
                var list = wheelDict[k];
                if (list == null || list.Count == 0) continue;
                for (int i = 0; i < list.Count; i++)
                    if (list[i] != null && list[i].TryCast<EmotesOption>() != null) return list;
            }
            LastStatus = "No emote wheel found.";
            return null;
        }

        // restore every icon sprite we swapped last cycle and drop our maps. MUST run (for every
        // service on this wheel) before anyone injects — the emoticon service snapshots whatever
        // object is in the slot, so if our sprite override is still live on a native EmotesOption
        // when it captures, that emote-sprited option gets stamped back and the emoticon icon lingers
        // in a slot it no longer owns. restore-all-then-inject-all keeps snapshots clean.
        internal static void RestoreSlots(SocialPrimeHandler handler)
        {
            try
            {
                foreach (var kvp in OriginalSprites)
                    if (kvp.Key != null) kvp.Key._cachedAtlasSprite = kvp.Value;
                OriginalSprites.Clear();
                CustomClips.Clear();
                SoundPaths.Clear();
            }
            catch (Exception ex) { Plugin.Log.LogError($"EmoteInjection: restore: {ex}"); }
        }

        internal static void InjectSlots(SocialPrimeHandler handler)
        {
            try
            {
                var slots = ResolveSlots(handler);
                if (slots == null) return;

                int injected = 0;
                foreach (var e in _entries)
                {
                    if (!e.enabled) continue;

                    var clip = LoadClip(e);
                    if (clip == null) { LastStatus = $"Couldn't load clip for {e.id}."; continue; }

                    int slotIndex = Math.Max(0, Math.Min(e.slot, slots.Count - 1));
                    var opt = slots[slotIndex]?.TryCast<EmotesOption>();
                    if (opt == null) continue;

                    // NOTE: AnimationClipLoadable is a GUID-backed addressable ref — there's no API to
                    // make it return a runtime clip. the clip swap has to happen at playback instead
                    // (see PlayEmotePatch). here we just record which clip belongs to this option.
                    CustomClips[slotIndex] = clip;
                    if (!string.IsNullOrEmpty(e.soundPath)) SoundPaths[slotIndex] = e.soundPath;

                    // optional custom wheel preview sprite (cached, rebuilt only when the file changes)
                    if (SocialSpriteCache.TryGet(e.imagePath, out _, out var cacheableSprite))
                    {
                        // back up the original sprite once so we can restore it on disable/remove
                        if (!OriginalSprites.ContainsKey(opt))
                            OriginalSprites[opt] = opt._cachedAtlasSprite;
                        opt._cachedAtlasSprite = cacheableSprite;
                    }

                    injected++;
                }

                LastStatus = injected > 0 ? $"Applied {injected} emote(s)." : "No enabled emotes.";
            }
            catch (Exception ex)
            {
                LastStatus = $"Error: {ex.Message}";
                Plugin.Log.LogError($"EmoteInjection: {ex}");
            }
        }

        // fade the clip in/out over this long so the emote doesn't pop in/out hard
        private const float FadeDuration = 0.3f;

        // one live graph per animator so a new emote tears down the previous one
        private static readonly Dictionary<int, PlayableGraph> _graphs = new Dictionary<int, PlayableGraph>();
        // the layer mixer per graph so the release coroutine can ramp our clip's weight
        private static readonly Dictionary<int, AnimationLayerMixerPlayable> _graphMixers = new Dictionary<int, AnimationLayerMixerPlayable>();
        // keep the animator per graph so we can rebind it (reconnect its controller) on teardown
        private static readonly Dictionary<int, Animator> _graphAnimators = new Dictionary<int, Animator>();
        // keep the motor agent per graph so we can kick the bean back into its move state on teardown
        private static readonly Dictionary<int, MotorAgent> _graphAgents = new Dictionary<int, MotorAgent>();
        // keep the active emote state per graph so we can End(-1) it on teardown
        private static readonly Dictionary<int, MotorFunctionEmoteStateEmote> _graphEmoteStates = new Dictionary<int, MotorFunctionEmoteStateEmote>();
        // bumped every time an emote starts on a key. a cancelled emote's release coroutine keeps
        // counting its old clip length, so we tag each coroutine with its generation and ignore it if
        // a newer emote has since started — otherwise the stale timer ends the new emote early.
        private static readonly Dictionary<int, int> _graphGen = new Dictionary<int, int>();


        // play our clip on the bean's animator via a one-shot PlayableGraph, then fire the sound.
        // the graph fully drives the animator while alive, so we MUST destroy it when the clip ends
        // (or another emote starts) — otherwise the game can't reclaim the animator and the bean
        // stays frozen in the last pose for the rest of the match.
        internal static void PlayClipOnBean(Animator animator, AnimationClip clip, int slotIndex, MotorAgent agent, MotorFunctionEmoteStateEmote emoteState)
        {
            DestroyGraph(animator.GetInstanceID());

            int key = animator.GetInstanceID();
            var graph = PlayableGraph.Create("BetterFG_Emote_" + key);
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(graph, "EmoteOut", animator);

            // keep the animator's OWN controller inside the graph as the base layer, with our clip
            // layered on top. that way when we destroy the graph the controller is still intact and
            // drives the bean — we never orphan the animator, so no frozen pose / stuck input.
            var mixer = AnimationLayerMixerPlayable.Create(graph, 2);
            var ctrl = AnimatorControllerPlayable.Create(graph, animator.runtimeAnimatorController);
            var clipPlayable = AnimationClipPlayable.Create(graph, clip);
            clipPlayable.SetApplyFootIK(false);

            graph.Connect(ctrl, 0, mixer, 0);
            graph.Connect(clipPlayable, 0, mixer, 1);
            mixer.SetInputWeight(0, 1f);
            mixer.SetInputWeight(1, 0f); // start at 0, fade up in ReleaseWhenDone
            output.SetSourcePlayable(mixer);

            var boneSnap = SnapshotBones(animator);

            graph.Play();
            _graphs[key] = graph;
            _graphMixers[key] = mixer;
            _graphAnimators[key] = animator;
            _graphAgents[key] = agent;
            _graphEmoteStates[key] = emoteState;

            int gen = _graphGen.TryGetValue(key, out int g) ? g + 1 : 1;
            _graphGen[key] = gen;

            // disable upper body ragdoll during emote
            var fgcc = LocalFgcc(agent);
            if (fgcc != null)
                FallGuysLib.Players.PlayerUtils.DisablePlayerUpperBodyRagdoll(fgcc);

            PlayCustomSound(slotIndex);

            var host = BeanMonitorService.Instance;
            if (host != null)
                host.StartCoroutine(ReleaseWhenDone(key, gen, clip.length, boneSnap).WrapToIl2Cpp());
        }

        private struct BoneSnapshot
        {
            public Transform bone;
            public Vector3 baseLocalPos;
            public Vector3 baseLocalScale;
            public bool isRoot;     // first 4 roots: no scaling at all. children: scale <=1.5x baseline, movement <=2 units
        }

        private static BoneSnapshot[] SnapshotBones(Animator animator)
        {
            var list = new System.Collections.Generic.List<BoneSnapshot>();
            try
            {
                var root = animator.transform;
                // first 4 roots (walk firstborn chain) — these may not be scaled at all
                Transform t = root;
                for (int depth = 0; depth < 4 && t != null; depth++)
                {
                    list.Add(new BoneSnapshot
                    {
                        bone = t,
                        baseLocalPos = t.localPosition,
                        baseLocalScale = t.localScale,
                        isRoot = true,
                    });
                    t = t.childCount > 0 ? t.GetChild(0) : null;
                }
                // all other descendants are children — scale capped at 1.5x, movement capped at 2 units
                var stack = new System.Collections.Generic.Stack<Transform>();
                for (int i = 0; i < root.childCount; i++) stack.Push(root.GetChild(i));
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    // skip bones already added as part of the root chain
                    bool already = false;
                    for (int i = 0; i < list.Count; i++) if (list[i].bone == cur) { already = true; break; }
                    if (!already)
                        list.Add(new BoneSnapshot
                        {
                            bone = cur,
                            baseLocalPos = cur.localPosition,
                            baseLocalScale = cur.localScale,
                            isRoot = false,
                        });
                    for (int i = 0; i < cur.childCount; i++) stack.Push(cur.GetChild(i));
                }
            }
            catch { }
            return list.ToArray();
        }

        private static bool BoneValuesExtreme(BoneSnapshot[] snap)
        {
            try
            {
                for (int i = 0; i < snap.Length; i++)
                {
                    var s = snap[i];
                    if (s.bone == null) continue;
                    var ls = s.bone.localScale;
                    if (s.isRoot)
                    {
                        // first 4 roots: any scaling at all is not allowed
                        var d = ls - s.baseLocalScale;
                        if (Mathf.Abs(d.x) > 0.001f || Mathf.Abs(d.y) > 0.001f || Mathf.Abs(d.z) > 0.001f)
                            return true;
                    }
                    else
                    {
                        // children: scale may not exceed 1.5x baseline
                        if (ls.x > s.baseLocalScale.x * 1.5f || ls.y > s.baseLocalScale.y * 1.5f || ls.z > s.baseLocalScale.z * 1.5f)
                            return true;
                        // children: may not move more than 2 units from baseline
                        var delta = s.bone.localPosition - s.baseLocalPos;
                        if (Mathf.Abs(delta.x) > 2f || Mathf.Abs(delta.y) > 2f || Mathf.Abs(delta.z) > 2f)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static void RestoreBones(BoneSnapshot[] snap)
        {
            try
            {
                for (int i = 0; i < snap.Length; i++)
                {
                    if (snap[i].bone == null) continue;
                    snap[i].bone.localPosition = snap[i].baseLocalPos;
                    snap[i].bone.localScale = snap[i].baseLocalScale;
                }
            }
            catch { }
        }

        private static IEnumerator ReleaseWhenDone(int key, int gen, float clipLen, BoneSnapshot[] boneSnap)
        {
            // grab the agent up front: it's how we read velocity (in the editor the local player isn't
            // registered so PlayerUtils.PlayerController is null — the emoting bean carries the fgcc).
            _graphAgents.TryGetValue(key, out var agent);
            float t = 0f;
            bool endedByMove = false;
            bool endedByExtreme = false;

            // fade window can't exceed half the clip or the in/out would overlap
            float fade = Mathf.Min(FadeDuration, clipLen * 0.5f);

            while (t < clipLen)
            {
                t += Time.deltaTime;
                // a newer emote replaced ours on this bean — let its coroutine own teardown, bail.
                if (!_graphGen.TryGetValue(key, out int cur) || cur != gen) yield break;

                // ramp our clip's weight: up over the first `fade` seconds, down over the last `fade`.
                if (fade > 0f && _graphMixers.TryGetValue(key, out var mix) && mix.IsValid())
                {
                    float w = 1f;
                    if (t < fade) w = t / fade;
                    else if (t > clipLen - fade) w = Mathf.Max(0f, (clipLen - t) / fade);
                    mix.SetInputWeight(1, Mathf.Clamp01(w));
                }

                var fgcc = LocalFgcc(agent);
                if (fgcc != null && fgcc.RigidBody.velocity.sqrMagnitude > 0.5f) { endedByMove = true; break; }
                if (boneSnap != null && BoneValuesExtreme(boneSnap)) { endedByExtreme = true; break; }
                yield return null;
            }

            // still ours? then end it.
            if (!_graphGen.TryGetValue(key, out int g2) || g2 != gen) yield break;

            if (endedByExtreme && boneSnap != null)
                RestoreBones(boneSnap);

            DestroyGraph(key);

            if (endedByExtreme)
            {
                BetterFG.UI.BetterFGUIMan.Instance?.ShowTooltipFixed(
                    "This emote has extreme values that can mess with your gameplay, and so your bean didn't want to perform it.",
                    Vector2.zero, 5f);
            }

            // when the emote ends because the player started walking, the bean's animation ends up
            // messed up unless we kick the movement function's move state. Begin(-1) on it fixes it.
            // wait a frame first so the emote-state reset (in DestroyGraph) lands before we touch the
            // move state — doing both same-frame left the emote function in a state that needed an
            // action (e.g. grab) before you could emote again.
            if (endedByMove)
            {
                yield return null;
                NudgeMoveState(agent);
            }
        }

        // re-Begin the movement function's move state (index 1) so the walk animation isn't left
        // broken after an emote was cut short by moving.
        private static void NudgeMoveState(MotorAgent agent)
        {
            try
            {
                var move = agent?.GetMotorFunction<MotorFunctionMovement>();
                move?._originalStates[1]?.Begin(-1);
            }
            catch { }
        }

        private static void DestroyGraph(int key)
        {
            if (_graphs.TryGetValue(key, out var g) && g.IsValid()) g.Destroy();
            _graphs.Remove(key);
            _graphMixers.Remove(key);

            StopCustomSound();
            _graphAgents.TryGetValue(key, out var agent);
            EnablePlayerUpperBodyRagdoll(agent);

            if (_graphAnimators.TryGetValue(key, out var anim) && anim != null) anim.Rebind();
            _graphAnimators.Remove(key);

            _graphEmoteStates.TryGetValue(key, out var emoteState);
            _graphEmoteStates.Remove(key);
            _graphAgents.Remove(key);
            _graphGen.Remove(key);
            if (agent == null) return;

            ResetEmoteFunction(agent, emoteState);
        }

        // public entry: drive the emote function back to inactive. used when an emote is blocked
        // (e.g. moving) so the function isn't left stuck in StateEmote.
        public static void ResetEmote(MotorAgent agent, MotorFunctionEmoteStateEmote emoteState)
        {
            if (agent == null) return;
            ResetEmoteFunction(agent, emoteState);
        }

        // emote function gets stuck at CurrentStateIndex=1 (StateEmote) after a custom emote, which
        // blocks re-emoting. drive it back to the inactive state. the state-change methods take the
        // state's hashed ID, NOT the array index (passing 0 errors "State with id 0 does not exist"),
        // so read the real ID off _originalStates[0]. RequestStateChange(id, Forced) is what moves it.
        private static void ResetEmoteFunction(MotorAgent agent, MotorFunctionEmoteStateEmote emoteState)
        {
            try
            {
                var ef = agent.GetMotorFunction<MotorFunctionEmote>();
                if (ef == null) return;
                emoteState?.End(-1);
                ef.RequestStateChange(ef._originalStates[0].ID, MotorAgent.ResourceRequestMode.Forced);
            }
            catch { }
        }

        // stop the emote currently playing on a given agent's bean (e.g. they grabbed).
        public static void StopEmoteForAgent(MotorAgent agent)
        {
            if (agent == null) return;
            int found = -1;
            foreach (var kvp in _graphAgents)
                if (kvp.Value == agent) { found = kvp.Key; break; }
            if (found != -1) DestroyGraph(found);
        }

        // stop everything (e.g. on round change) so no graph lingers holding an animator
        public static void StopAll()
        {
            foreach (var kvp in _graphs)
                if (kvp.Value.IsValid()) kvp.Value.Destroy();
            _graphs.Clear();
            _graphMixers.Clear();
            EnablePlayerUpperBodyRagdoll();
        }

        // the local player's fgcc. in the level editor the local player isn't registered in the player
        // manager so PlayerUtils.PlayerController is null — fall back to the emoting bean, which carries
        // the fgcc itself (LevelEditor_FallGuy(Clone)).
        private static FallGuysCharacterController LocalFgcc(MotorAgent agent)
        {
            var fgcc = FallGuysLib.Players.PlayerUtils.PlayerController;
            if (fgcc != null) return fgcc;
            return agent != null && agent.gameObject != null
                ? agent.gameObject.GetComponent<FallGuysCharacterController>() : null;
        }

        // re-enable upper body ragdoll after emote finishes
        private static void EnablePlayerUpperBodyRagdoll(MotorAgent agent = null)
        {
            try
            {
                var fgcc = LocalFgcc(agent);
                if (fgcc != null)
                {
                    Transform ragdollObject = fgcc.transform.Find("Ragdoll");
                    if (ragdollObject != null)
                    {
                        RagdollController rc = ragdollObject.GetComponent<RagdollController>();
                        if (rc != null)
                        {
                            rc._upperBodyEnabled = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("EmoteInjection: EnablePlayerUpperBodyRagdoll failed: " + ex.Message);
            }
        }

        // the currently-playing emote sound, so we can cut it when the emote ends
        private static WaveOutEvent _emoteAudio;

        internal static void PlayCustomSound(int slotIndex)
        {
            StopCustomSound();
            if (!SoundPaths.TryGetValue(slotIndex, out string path)) return;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                var audio = GlobalGameStateClient.Instance?.PlayerProfile?.AudioSettings;
                float vol = audio != null ? Mathf.Clamp01(audio.MasterVolume) * Mathf.Clamp01(audio.SFXVolume) * 0.6f : 0.6f;
                var ms = new MemoryStream(File.ReadAllBytes(path));
                WaveStream reader = path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)
                    ? (WaveStream)new Mp3FileReader(ms)
                    : new WaveFileReader(ms);
                var volProv = new VolumeWaveProvider16(reader) { Volume = vol };
                var output = new WaveOutEvent();
                output.Init(volProv);
                output.Play();
                output.PlaybackStopped += (_, __) => { output.Dispose(); reader.Dispose(); ms.Dispose(); };
                _emoteAudio = output;
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"EmoteInjection: sound failed: {ex.Message}"); }
        }

        internal static void StopCustomSound()
        {
            try { _emoteAudio?.Stop(); } catch { }
            _emoteAudio = null;
        }
    }
}
