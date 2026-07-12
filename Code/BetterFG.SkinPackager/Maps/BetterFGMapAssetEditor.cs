using System.IO;
using UnityEditor;
using UnityEngine;

namespace BetterFG.Editor
{
    [CustomEditor(typeof(BetterFGMapAsset))]
    public class BetterFGMapAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var map = (BetterFGMapAsset)target;
            serializedObject.Update();

            // prefabName isn't shown — it's set automatically from the scene/round name by
            // Map Creator and the packager derives everything from the open scene.
            Section("Map Info");
            Prop("displayName", "Display Name");
            Prop("description", "Description");
            EditorGUILayout.HelpBox(
                "Shown on the round-selected loading screen when people play your level.",
                MessageType.None);
            Prop("keepExistingObjects", "Keep Existing Objects");
            EditorGUILayout.HelpBox(
                "Skybox / ambient / reflection / fog come from the scene's Lighting settings\n" +
                "(Window > Rendering > Lighting) — set them there, not here.",
                MessageType.Info);

            EditorGUILayout.Space(6);

            Section("Music");

            EditorGUILayout.HelpBox(
                "Optional. Pick an mp3 file — it plays during gameplay only.\n" +
                "Leave empty to use the default Fall Guys music.",
                MessageType.Info);

            EditorGUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PrefixLabel("Music File");
            string displayPath = string.IsNullOrEmpty(map.musicFilePath) ? "(none)" : Path.GetFileName(map.musicFilePath);
            EditorGUILayout.LabelField(displayPath, EditorStyles.textField, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("...", GUILayout.Width(28)))
            {
                string start = string.IsNullOrEmpty(map.musicFilePath) ? "" : Path.GetDirectoryName(map.musicFilePath);
                string picked = EditorUtility.OpenFilePanel("Select music file", start, "mp3");
                if (!string.IsNullOrEmpty(picked))
                {
                    map.musicFilePath = picked;
                    EditorUtility.SetDirty(map);
                }
            }
            if (!string.IsNullOrEmpty(map.musicFilePath) && GUILayout.Button("x", GUILayout.Width(20)))
            {
                map.musicFilePath = "";
                EditorUtility.SetDirty(map);
            }
            EditorGUILayout.EndHorizontal();

            serializedObject.ApplyModifiedProperties();
        }

        void Section(string label)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }

        void Prop(string name, string label)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(name), new GUIContent(label));
        }
    }
}