using UnityEngine;
using System.Collections.Generic;

namespace BetterFG.Customization.Player
{
    public class CostumePollerComponent : MonoBehaviour
    {
        public Transform beanGEO;
        public GameObject skinClone;
        public bool isRemote = false;
        public bool keepLocalDonor = false;

        private struct RendererState
        {
            public Material[] mats;
            public UnityEngine.Rendering.ShadowCastingMode shadowCasting;
            public bool receiveShadows;
        }

        // renderer -> original state, restored on destroy
        private Dictionary<Renderer, RendererState> _savedMats = new Dictionary<Renderer, RendererState>();
        private float _nextCheck;
        private const float CHECK_INTERVAL = 1f;

        // one fully-transparent material shared across all invisible renderers
        private static Material _invisibleMat;

        public static Material GetInvisibleMat()
        {
            if (_invisibleMat != null) return _invisibleMat;

            // If the project has an embedded prefab named "material_invisible", use its material as a template.
            try
            {
                if (BetterFG.Core.AssetManager.Instance != null && BetterFG.Core.AssetManager.Instance.prefabs != null)
                {
                    if (BetterFG.Core.AssetManager.Instance.prefabs.TryGetValue("material_invisible", out var matPrefab) && matPrefab != null)
                    {
                        var srcR = matPrefab.GetComponent<Renderer>() ?? matPrefab.GetComponentInChildren<Renderer>(true);
                        if (srcR != null && srcR.materials != null && srcR.materials.Length > 0)
                        {
                            // clone the first material from the prefab so we don't share instances
                            _invisibleMat = new Material(srcR.materials[0]);
                            // ensure fully transparent settings (in case the prefab isn't exact)
                            _invisibleMat.color = new Color(0f, 0f, 0f, 0f);
                            _invisibleMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                            return _invisibleMat;
                        }
                    }
                }
            }
            catch { }

            // Fallback: create a standard transparent material
            _invisibleMat = new Material(Shader.Find("Standard"));
            _invisibleMat.SetFloat("_Mode", 3f);                          // Transparent
            _invisibleMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _invisibleMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _invisibleMat.SetInt("_ZWrite", 0);
            _invisibleMat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            _invisibleMat.color = new Color(0f, 0f, 0f, 0f);
            _invisibleMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return _invisibleMat;
        }

        // Call this right after configuring the poller so the base bean is hidden on the same
        // frame the skin clone appears, instead of waiting a frame for Start(). Without this the
        // UGC costume + base bean both show for a beat on first menu entry.
        public void HideNow()
        {
            HideBeans();
            _nextCheck = Time.time + CHECK_INTERVAL;
        }

        void Start()
        {
            HideBeans();
            _nextCheck = Time.time + CHECK_INTERVAL;
        }

        void Update()
        {
            if (beanGEO == null) { Destroy(this); return; }
            if (Time.time < _nextCheck) return;
            _nextCheck = Time.time + CHECK_INTERVAL;
            HideBeans();
        }

        // returns true if this poll actually had something to hide (i.e. the bean isn't settled yet).
        // the menu-entry kick uses this to stop hammering once there's nothing left to do.
        public bool PollNow()
        {
            if (beanGEO == null) return false;
            bool did = HideBeans();
            _nextCheck = Time.time + CHECK_INTERVAL;
            return did;
        }

        private bool HideBeans()
        {
            if (!NeedsHidePass()) return false;
            if (isRemote) HideRemoteBeans();
            else HideLocalBeans();
            return true;
        }

        private bool NeedsHidePass()
        {
            if (beanGEO == null) return false;
            for (int i = 0; i < beanGEO.childCount; i++)
            {
                Transform child = beanGEO.GetChild(i).Cast<Transform>();
                if (child == null || child.gameObject == skinClone) continue;
                if (!child.gameObject.activeSelf) continue;
                if (ShouldHide(child.name)) return true;
            }
            return false;
        }

        // For remote beans: find all children that should be hidden.
        // Keep the FIRST Body_LOD child alive (bones depend on its SMR) but make it fully invisible.
        // Everything else matching the hide filter also gets invisible materials.
        private void HideRemoteBeans()
        {
            // prefer Body_LOD0 as donor; disable all other matching children
            Transform boneDonor = null;
            for (int i = 0; i < beanGEO.childCount; i++)
            {
                Transform child = beanGEO.GetChild(i).Cast<Transform>();
                if (child == null) continue;
                if (child.gameObject == skinClone) continue;
                if (!ShouldHide(child.name)) continue;
                if (child.name.Contains("Body_LOD0")) { boneDonor = child; break; }
            }
            if (boneDonor == null)
            {
                for (int i = 0; i < beanGEO.childCount; i++)
                {
                    Transform child = beanGEO.GetChild(i).Cast<Transform>();
                    if (child == null) continue;
                    if (child.gameObject == skinClone) continue;
                    if (!ShouldHide(child.name)) continue;
                    if (child.name.Contains("Body_LOD")) { boneDonor = child; break; }
                }
            }

            for (int i = 0; i < beanGEO.childCount; i++)
            {
                Transform child = beanGEO.GetChild(i).Cast<Transform>();
                if (child == null) continue;
                if (child.gameObject == skinClone) continue;
                if (!ShouldHide(child.name)) continue;

                if (boneDonor != null && child == boneDonor)
                {
                    if (!child.gameObject.activeSelf) child.gameObject.SetActive(true);
                    MakeInvisible(child);
                }
                else
                {
                    if (child.gameObject.activeSelf)
                        child.gameObject.SetActive(false);
                }
            }

            Debug.Log($"[CostumePoller] HideRemoteBeans chosen donor={ (boneDonor != null ? boneDonor.name : "(none)") }");
        }

        private void HideLocalBeans()
        {
            Transform boneDonor = null;
            for (int i = 0; i < beanGEO.childCount; i++)
            {
                Transform child = beanGEO.GetChild(i).Cast<Transform>();
                if (child == null) continue;
                if (child.gameObject == skinClone) continue;
                if (!ShouldHide(child.name)) continue;
                if (child.name.Contains("Body_LOD0")) { boneDonor = child; break; }
            }
            if (boneDonor == null)
            {
                for (int i = 0; i < beanGEO.childCount; i++)
                {
                    Transform child = beanGEO.GetChild(i).Cast<Transform>();
                    if (child == null) continue;
                    if (child.gameObject == skinClone) continue;
                    if (!ShouldHide(child.name)) continue;
                    if (child.name.Contains("Body_LOD")) { boneDonor = child; break; }
                }
            }

            for (int i = 0; i < beanGEO.childCount; i++)
            {
                Transform child = beanGEO.GetChild(i).Cast<Transform>();
                if (child == null) continue;
                if (child.gameObject == skinClone) continue;
                if (!ShouldHide(child.name)) continue;

                if (keepLocalDonor && boneDonor != null && child == boneDonor)
                {
                    if (!child.gameObject.activeSelf) child.gameObject.SetActive(true);
                    continue;
                }

                if (child.gameObject.activeSelf)
                    child.gameObject.SetActive(false);
            }
        }

        private void MakeInvisible(Transform target)
        {
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers == null) return;
            Material invis = GetInvisibleMat();
            foreach (var r in renderers)
            {
                if (r == null || _savedMats.ContainsKey(r)) continue;
                if (r.materials == null || r.materials.Length == 0) continue;
                _savedMats[r] = new RendererState
                {
                    mats = r.materials,
                    shadowCasting = r.shadowCastingMode,
                    receiveShadows = r.receiveShadows
                };
                var invisible = new Material[r.materials.Length];
                for (int j = 0; j < invisible.Length; j++)
                    invisible[j] = invis;
                r.materials = invisible;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        private static bool ShouldHide(string name) =>
            name.Contains("Top") || name.Contains("Bottom") ||
            name.Contains("CH_") || name.Contains("Body_LOD") || name.Contains("LOD");

        void OnDestroy()
        {
            foreach (var kv in _savedMats)
            {
                if (kv.Key == null) continue;
                kv.Key.materials = kv.Value.mats;
                kv.Key.shadowCastingMode = kv.Value.shadowCasting;
                kv.Key.receiveShadows = kv.Value.receiveShadows;
            }
            _savedMats.Clear();
        }
    }
}
