using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace BetterFG.Editor.Map
{
    public static class MapInfoJson
    {
        static readonly CultureInfo CI = CultureInfo.InvariantCulture;

        public static void Write(string outputDir, BetterFGMapAsset map)
        {
            string bundleFileName = map.prefabName.ToLower();

            // environment comes straight from the scene's RenderSettings (Lighting window), not
            // hand-typed fields. the skybox material is bundled, so info.json just needs its name.
            string skyboxName = RenderSettings.skybox != null ? RenderSettings.skybox.name : "";

            var sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine($"  \"displayName\": \"{EscJ(map.displayName)}\",");
            sb.AppendLine($"  \"description\": \"{EscJ(map.description)}\",");
            sb.AppendLine($"  \"file\": \"{EscJ(bundleFileName)}\",");
            sb.AppendLine($"  \"prefab\": \"{EscJ(map.prefabName)}\",");
            sb.AppendLine($"  \"skybox\": \"{EscJ(skyboxName)}\",");
            sb.AppendLine($"  \"ambientMode\": \"{(int)RenderSettings.ambientMode}\",");
            sb.AppendLine($"  \"ambientLight\": \"{ColorStr(RenderSettings.ambientLight)}\",");
            sb.AppendLine($"  \"reflectionIntensity\": \"{RenderSettings.reflectionIntensity.ToString(CI)}\",");
            sb.AppendLine($"  \"fog\": \"{RenderSettings.fog.ToString().ToLower()}\",");
            sb.AppendLine($"  \"fogDensity\": \"{RenderSettings.fogDensity.ToString(CI)}\",");
            sb.AppendLine($"  \"fogColor\": \"{ColorStr(RenderSettings.fogColor)}\",");
            sb.AppendLine($"  \"keepExistingObjects\": \"{map.keepExistingObjects.ToString().ToLower()}\",");
            sb.AppendLine($"  \"music\": \"{EscJ(!string.IsNullOrEmpty(map.musicFilePath) ? Path.GetFileName(map.musicFilePath) : "")}\"");
            sb.AppendLine("}");
            File.WriteAllText(Path.Combine(outputDir, "info.json"), sb.ToString(), Encoding.UTF8);
        }

        static string ColorStr(Color c) =>
            $"{c.r.ToString(CI)} {c.g.ToString(CI)} {c.b.ToString(CI)} {c.a.ToString(CI)}";

        static string EscJ(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r", "").Replace("\n", "\\n") ?? "";
    }
}