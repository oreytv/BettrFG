using System.IO;
using UnityEditor;
using UnityEngine;

namespace BetterFG.Editor
{
    public class AssetBundleBuilder
    {
        [MenuItem("Assets/Build AssetBundle")]
        static void Build()
        {
            if (!Directory.Exists(Application.streamingAssetsPath))
                Directory.CreateDirectory(Application.streamingAssetsPath);

            // LZ4, not the default LZMA — LZMA bundles fully decompress on every load, which is a
            // ~1s main-thread stutter when the skin applies in game. LZ4 chunks decompress lazily.
            BuildPipeline.BuildAssetBundles(
                "Assets/StreamingAssets",
                BuildAssetBundleOptions.ChunkBasedCompression,
                EditorUserBuildSettings.activeBuildTarget
            );

            AssetDatabase.Refresh();
            Debug.Log("AssetBundleBuilder: done, built to Assets/StreamingAssets");
        }
    }
}