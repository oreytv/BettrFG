using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BetterFG.Editor
{
    public class GroupSkinsWindow : EditorWindow
    {
        private const string PREF_REPO = "BetterFG.SkinPackager.LastCatalogDir";

        private class SkinRow
        {
            public string infoPath;
            public string label;
            public string group;
            public bool selected;
        }

        private string _repoRoot = "";
        private string _group = "";
        private string _newGroup = "";
        private string _status = "";
        private Vector2 _scroll;
        private List<string> _groups = new List<string>();
        private List<SkinRow> _skins = new List<SkinRow>();

        [MenuItem("BettrFG/Skins/Group Skins")]
        public static void Open() => GetWindow<GroupSkinsWindow>("Group Skins");

        private void OnEnable()
        {
            _repoRoot = EditorPrefs.GetString(PREF_REPO, "");
            Refresh();
        }

        private void OnGUI()
        {
            GUILayout.Space(6);
            GUILayout.Label("Group Skins", EditorStyles.boldLabel);
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            _repoRoot = EditorGUILayout.TextField("Repo Folder", _repoRoot);
            if (EditorGUI.EndChangeCheck()) Refresh();
            if (GUILayout.Button("generate_catalog.bat...", GUILayout.Width(160)))
            {
                string picked = EditorUtility.OpenFilePanel("Select generate_catalog.bat", _repoRoot, "bat");
                if (!string.IsNullOrEmpty(picked))
                {
                    _repoRoot = Path.GetDirectoryName(picked);
                    EditorPrefs.SetString(PREF_REPO, _repoRoot);
                    Refresh();
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            var labels = new List<string> { "Unsorted" };
            labels.AddRange(_groups);
            int selectedGroup = 0;
            for (int i = 0; i < _groups.Count; i++)
                if (string.Equals(_groups[i], _group, StringComparison.OrdinalIgnoreCase))
                    selectedGroup = i + 1;
            int pickedGroup = EditorGUILayout.Popup("Move selected to", selectedGroup, labels.ToArray());
            _group = pickedGroup == 0 ? "" : _groups[pickedGroup - 1];

            GUILayout.BeginHorizontal();
            _newGroup = EditorGUILayout.TextField("Add new group", _newGroup);
            if (GUILayout.Button("Add", GUILayout.Width(70)) && !string.IsNullOrWhiteSpace(_newGroup))
            {
                _group = _newGroup.Trim();
                if (!_groups.Exists(g => string.Equals(g, _group, StringComparison.OrdinalIgnoreCase)))
                {
                    _groups.Add(_group);
                    _groups.Sort(StringComparer.OrdinalIgnoreCase);
                }
                _newGroup = "";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Select all", GUILayout.Width(90)))
                foreach (var skin in _skins) skin.selected = true;
            if (GUILayout.Button("Select none", GUILayout.Width(90)))
                foreach (var skin in _skins) skin.selected = false;
            if (GUILayout.Button("Refresh", GUILayout.Width(90))) Refresh();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Apply selected", GUILayout.Width(120))) Apply();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var skin in _skins)
            {
                GUILayout.BeginHorizontal();
                skin.selected = EditorGUILayout.Toggle(skin.selected, GUILayout.Width(18));
                GUILayout.Label(skin.label, GUILayout.Width(260));
                GUILayout.Label(string.IsNullOrEmpty(skin.group) ? "Unsorted" : skin.group, EditorStyles.miniLabel);
                GUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(_status))
                EditorGUILayout.HelpBox(_status, MessageType.Info);
        }

        private void Refresh()
        {
            var groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _skins.Clear();
            if (!string.IsNullOrWhiteSpace(_repoRoot) && Directory.Exists(_repoRoot))
            {
                EditorPrefs.SetString(PREF_REPO, _repoRoot);
                foreach (string category in new[] { "Costumes", "Accessories", "Items", "Plinths" })
                {
                    string root = Path.Combine(_repoRoot, category);
                    if (!Directory.Exists(root)) continue;
                    foreach (string info in Directory.GetFiles(root, "info.json", SearchOption.AllDirectories))
                    {
                        string group = SkinInfoJson.ReadStr(File.ReadAllText(info), "group").Trim();
                        if (!string.IsNullOrEmpty(group)) groups.Add(group);
                        _skins.Add(new SkinRow
                        {
                            infoPath = info,
                            label = category + " / " + new DirectoryInfo(Path.GetDirectoryName(info)).Name,
                            group = group,
                        });
                    }
                }
            }
            _groups = new List<string>(groups);
            _groups.Sort(StringComparer.OrdinalIgnoreCase);
            _skins.Sort((a, b) => string.Compare(a.label, b.label, StringComparison.OrdinalIgnoreCase));
            _status = _skins.Count + " skin(s)";
            Repaint();
        }

        private void Apply()
        {
            int changed = 0;
            foreach (var skin in _skins)
            {
                if (!skin.selected) continue;
                string json = File.ReadAllText(skin.infoPath);
                string oldGroup = SkinInfoJson.ReadStr(json, "group");
                string nextGroup = _group.Trim();
                if (!string.IsNullOrEmpty(oldGroup))
                    json = System.Text.RegularExpressions.Regex.Replace(json,
                        "\\s*,?\\s*\"group\"\\s*:\\s*\"[^\"]*\"",
                        "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!string.IsNullOrEmpty(nextGroup))
                {
                    int close = json.LastIndexOf('}');
                    string comma = json.Substring(0, close).TrimEnd().EndsWith("{") ? "" : ",";
                    json = json.Insert(close, comma + Environment.NewLine + "  \"group\": \"" + nextGroup.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" + Environment.NewLine);
                }
                File.WriteAllText(skin.infoPath, json);
                changed++;
            }
            Refresh();
            _status = "Updated " + changed + " skin(s)";
        }
    }
}
