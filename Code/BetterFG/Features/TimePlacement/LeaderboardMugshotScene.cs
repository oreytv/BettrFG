using UnityEngine;
using UnityEngine.Rendering;

namespace BetterFG.Features.TimePlacement
{
    internal static class LeaderboardMugshotScene
    {
        public const int Width = 88;
        public const int Height = 64;
        public const int Layer = 31;

        public const float HeadDrop = 0.24f;
        public const float HeadSize = 0.30f;
        public const float Distance = 8f;

        // the bean's body shader writes alpha 0, so a transparent target renders a floating costume
        // with an invisible player in it. two shots on these backgrounds, and the difference is the mask
        public static readonly Color MaskA = Color.black;
        public static readonly Color MaskB = Color.white;

        public static readonly Color Ambient = new Color(0.24f, 0.24f, 0.28f);

        public static readonly Vector3 KeyAngles = new Vector3(16f, -30f, 0f);
        public static readonly Color KeyColour = new Color(1f, 0.97f, 0.9f);
        public const float KeyIntensity = 1.2f;

        public static readonly Vector3 FillAngles = new Vector3(-6f, 58f, 0f);
        public static readonly Color FillColour = new Color(0.74f, 0.82f, 1f);
        public const float FillIntensity = 0.5f;

        public static readonly Vector3 RimAngles = new Vector3(-22f, 165f, 0f);
        public static readonly Color RimColour = new Color(1f, 0.94f, 0.82f);
        public const float RimIntensity = 1.1f;

        public static Camera BuildCamera(GameObject host)
        {
            var cam = host.AddComponent<Camera>();
            cam.enabled = false;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.orthographic = true;
            cam.aspect = (float)Width / Height;
            cam.cullingMask = 1 << Layer;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = Distance * 4f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.useOcclusionCulling = false;

            AddLight(host.transform, "Key", KeyAngles, KeyColour, KeyIntensity);
            AddLight(host.transform, "Fill", FillAngles, FillColour, FillIntensity);
            AddLight(host.transform, "Rim", RimAngles, RimColour, RimIntensity);
            return cam;
        }

        static void AddLight(Transform camT, string name, Vector3 angles, Color colour, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(camT, false);
            go.transform.localRotation = Quaternion.Euler(angles);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.cullingMask = 1 << Layer;
            light.color = colour;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }

        // yawForward is the bean's facing flattened to yaw so a mid-drop tilt doesn't tip the portrait over
        public static void FrameHead(Camera cam, Bounds body, Vector3 yawForward)
        {
            float h = Mathf.Max(0.01f, body.size.y);
            var head = new Vector3(body.center.x, body.max.y - h * HeadDrop, body.center.z);
            cam.transform.position = head + yawForward * Distance;
            cam.transform.rotation = Quaternion.LookRotation(-yawForward, Vector3.up);
            cam.orthographicSize = h * HeadSize;
        }

        static bool _fog;
        static AmbientMode _ambientMode;
        static Color _ambientColour;

        public static void PushLighting()
        {
            _fog = RenderSettings.fog;
            _ambientMode = RenderSettings.ambientMode;
            _ambientColour = RenderSettings.ambientLight;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Ambient;
        }

        public static void PopLighting()
        {
            RenderSettings.fog = _fog;
            RenderSettings.ambientMode = _ambientMode;
            RenderSettings.ambientLight = _ambientColour;
        }
    }
}
