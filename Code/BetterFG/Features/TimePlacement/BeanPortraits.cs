using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BetterFG.Features.TimePlacement
{
    internal static class BeanPortraits
    {
        static readonly Dictionary<string, Texture2D> _byKey = new Dictionary<string, Texture2D>();

        static RenderTexture _rt;
        static Texture2D _shotA;
        static Texture2D _shotB;

        public static Texture Get(string playerKey) =>
            !string.IsNullOrEmpty(playerKey) && _byKey.TryGetValue(playerKey, out var tex) ? tex : null;

        public static void Clear()
        {
            foreach (var tex in _byKey.Values)
                if (tex != null) UnityEngine.Object.Destroy(tex);
            _byKey.Clear();
        }

        public static IEnumerator CaptureCoroutine()
        {
            yield return new WaitForSeconds(1f);

            var players = BetterFG.Utilities.PlayerInformation.GetClientPlayerManager()?._players;
            if (players == null)
            {
                Plugin.Log.LogInfo("no player list a second into the round, no bean portraits this time");
                yield break;
            }

            int w = LeaderboardMugshotScene.Width, h = LeaderboardMugshotScene.Height;
            var camGo = new GameObject("BettrFG_BeanPortraitCam");
            var cam = LeaderboardMugshotScene.BuildCamera(camGo);
            _rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32);
            _rt.Create();
            _shotA = new Texture2D(w, h, TextureFormat.RGBA32, false);
            _shotB = new Texture2D(w, h, TextureFormat.RGBA32, false);

            int done = 0;
            for (int i = 0; i < players.Count; i++)
            {
                var p = players[i];
                if (p == null || string.IsNullOrEmpty(p.playerKey) || p.fgcc == null) continue;
                if (_byKey.ContainsKey(p.playerKey)) continue;

                var tex = Snap(cam, p.fgcc.gameObject);
                if (tex != null) { _byKey[p.playerKey] = tex; done++; }

                if ((i % 3) == 2) yield return null;
            }

            UnityEngine.Object.Destroy(camGo);
            UnityEngine.Object.Destroy(_shotA);
            UnityEngine.Object.Destroy(_shotB);
            _rt.Release();
            UnityEngine.Object.Destroy(_rt);
            _rt = null; _shotA = null; _shotB = null;

            FeatureTimePlacement.OnPortraitsReady();
            Plugin.Log.LogInfo($"snapped {done} bean portraits ({players.Count} in the round)");
        }

        static Texture2D Snap(Camera cam, GameObject bean)
        {
            var rends = new List<Renderer>();
            foreach (var r in bean.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (r != null && r.enabled && r.gameObject.activeInHierarchy) rends.Add(r);
            foreach (var r in bean.GetComponentsInChildren<MeshRenderer>(true))
                if (r != null && r.enabled && r.gameObject.activeInHierarchy && r.GetComponent<TMPro.TMP_Text>() == null)
                    rends.Add(r);
            if (rends.Count == 0) return null;

            // stale skinned bounds get the renderer frustum-culled out of a shot this tight
            var offscreen = new List<SkinnedMeshRenderer>();
            foreach (var r in rends)
            {
                var smr = r.TryCast<SkinnedMeshRenderer>();
                if (smr == null || smr.updateWhenOffscreen) continue;
                smr.updateWhenOffscreen = true;
                offscreen.Add(smr);
            }

            var body = rends[0].bounds;
            var layers = new int[rends.Count];
            for (int i = 0; i < rends.Count; i++)
            {
                body.Encapsulate(rends[i].bounds);
                layers[i] = rends[i].gameObject.layer;
                rends[i].gameObject.layer = LeaderboardMugshotScene.Layer;
            }

            Vector3 fwd = bean.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;
            fwd.Normalize();
            LeaderboardMugshotScene.FrameHead(cam, body, fwd);

            int w = LeaderboardMugshotScene.Width, h = LeaderboardMugshotScene.Height;
            var full = new Rect(0f, 0f, w, h);
            var prevActive = RenderTexture.active;
            cam.targetTexture = _rt;

            LeaderboardMugshotScene.PushLighting();
            cam.backgroundColor = LeaderboardMugshotScene.MaskA;
            cam.Render();
            RenderTexture.active = _rt;
            _shotA.ReadPixels(full, 0, 0);
            cam.backgroundColor = LeaderboardMugshotScene.MaskB;
            cam.Render();
            RenderTexture.active = _rt;
            _shotB.ReadPixels(full, 0, 0);
            LeaderboardMugshotScene.PopLighting();

            RenderTexture.active = prevActive;
            cam.targetTexture = null;
            for (int i = 0; i < rends.Count; i++)
                if (rends[i] != null) rends[i].gameObject.layer = layers[i];
            foreach (var smr in offscreen)
                if (smr != null) smr.updateWhenOffscreen = false;

            // alpha = how much the two backgrounds didn't show through; the black shot is premultiplied
            // by that alpha, so divide it back out or every edge pixel reads muddy
            var pa = _shotA.GetPixels();
            var pb = _shotB.GetPixels();
            for (int i = 0; i < pa.Length; i++)
            {
                Color a = pa[i], b = pb[i];
                float diff = (Mathf.Abs(b.r - a.r) + Mathf.Abs(b.g - a.g) + Mathf.Abs(b.b - a.b)) / 3f;
                float alpha = Mathf.Clamp01(1f - diff);
                pa[i] = alpha <= 0.02f
                    ? new Color(0f, 0f, 0f, 0f)
                    : new Color(a.r / alpha, a.g / alpha, a.b / alpha, alpha);
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels(pa);
            tex.Apply(false, true);
            return tex;
        }
    }
}
