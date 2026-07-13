using System;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FGClient;
using FGClient.UI.PrivateLobby;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace BetterFG.Tweaks
{
    // adds a search bar above the private lobby show-select scroll list. type a name
    // and rows whose PrivateLobbyShowListEntryViewModel.ShowName doesn't contain the
    // query get hidden. we shove the viewport down a bit (RectMask2D padding + local pos)
    // to make room for the bar at the top.
    //
    // no real InputField here � we drive a fake caret off Input.inputString and lean on
    // FGInputLockService so wasd/etc don't leak into the game while typing, same as the tabs.
    public class LobbyShowSearchTweak : BfgTweak
    {
        public LobbyShowSearchTweak(IntPtr ptr) : base(ptr) { }

        public override string TweakId => "lobby_show_search";
        public override string TweakLabel => "Show Select Search Bar";
        public override bool DefaultEnabled => true;

        public static LobbyShowSearchTweak Instance { get; private set; }
        void Awake() => Instance = this;

        const string ShowSelectRoot = "UICanvas_Client_V2(Clone)/Default/Prefab_UI_PrivateLobbyShowSelect(Clone)";
        const string ViewportPath = "Container/ShowsScrollList/ShowsListViewport";
        const string ContentPath = "Container/ShowsScrollList/ShowsListViewport/ShowsListContent";

        static readonly Vector3 ViewportLocalPos = new Vector3(267.1405f, -67.3638f, 0f);
        static readonly Vector4 ViewportPadding = new Vector4(-20f, 75f, -20f, 0f); // left top right bottom

        private Transform _root;
        private Transform _content;
        private RectTransform _searchFieldRt;
        private TMP_Text _searchText;
        private TMP_Text _placeholder;
        private string _query = "";
        private bool _focused;

        // the show list VM rebuilds rows; this is bumped from the patch so we know to re-grab refs
        internal static int BuildToken;
        private int _seenToken = -1;

        internal static void OnShowListAwake()
        {
            BuildToken++;
            var inst = Instance;
            if (inst == null || !inst.IsEnabled) return;
            inst.StartCoroutine(inst.BuildWhenReady(BuildToken).WrapToIl2Cpp());
        }

        private IEnumerator BuildWhenReady(int token)
        {
            float elapsed = 0f;
            while (elapsed < 10f)
            {
                if (token != BuildToken) yield break; // a newer screen opened, bail

                var rootGo = GameObject.Find(ShowSelectRoot);
                var viewport = rootGo == null ? null : rootGo.transform.Find(ViewportPath);
                var content = rootGo == null ? null : rootGo.transform.Find(ContentPath);
                if (viewport != null && content != null)
                {
                    try
                    {
                        _root = rootGo.transform;
                        _content = content;
                        _query = "";
                        _focused = false;
                        _seenToken = token;

                        ApplyViewportLayout(viewport);
                        BuildSearchBar(viewport);
                        RefilterRows();
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log?.LogWarning("LobbyShowSearch: build failed " + ex.Message);
                    }
                    yield break;
                }

                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }
            Plugin.Log?.LogWarning("LobbyShowSearch: timed out finding show select screen");
        }

        private static void ApplyViewportLayout(Transform viewport)
        {
            viewport.localPosition = ViewportLocalPos;
            var mask = viewport.GetComponent<RectMask2D>();
            if (mask != null) mask.padding = ViewportPadding;
        }

        private void BuildSearchBar(Transform viewport)
        {
            // already built for this screen? (coroutine could fire twice). reuse it.
            var existing = viewport.parent.Find("BFG_ShowSearch");
            if (existing != null)
            {
                _searchFieldRt = existing.GetComponent<RectTransform>();
                _searchText = existing.Find("Text")?.GetComponent<TMP_Text>();
                _placeholder = existing.Find("Placeholder")?.GetComponent<TMP_Text>();
                UpdateCaret();
                return;
            }

            // grab a row's TMP_Text to copy font/material so the bar matches the screen
            TMP_Text fontSource = _content != null ? _content.GetComponentInChildren<TMP_Text>(true) : null;

            var vpRt = viewport.GetComponent<RectTransform>();

            var go = new GameObject("BFG_ShowSearch");
            go.transform.SetParent(viewport.parent, false);
            var rt = go.AddComponent<RectTransform>();
            // sit above the viewport. exact placement/scale dialed in by hand in-game.
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.localPosition = new Vector3(267.1405f, 276.418f, 0f);
            rt.localScale = new Vector3(1.0727f, 1f, 1f);
            rt.sizeDelta = new Vector2(vpRt != null ? vpRt.rect.width : 520f, 48f);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.55f);
            bg.raycastTarget = true;
            _searchFieldRt = rt;

            _placeholder = MakeLabel(go.transform, "Placeholder", "Search shows...", fontSource);
            _placeholder.color = new Color(1f, 1f, 1f, 0.35f);

            _searchText = MakeLabel(go.transform, "Text", "", fontSource);
            _searchText.color = Color.white;

            UpdateCaret();
        }

        private static TMP_Text MakeLabel(Transform parent, string name, string text, TMP_Text fontSource)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(14f, 0f);
            rt.offsetMax = new Vector2(-14f, 0f);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (fontSource != null)
            {
                tmp.font = fontSource.font;
                tmp.fontSharedMaterial = fontSource.fontSharedMaterial;
            }
            tmp.fontSize = 26f;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            tmp.text = text;
            return tmp;
        }

        private void Update()
        {
            if (!IsEnabled) return;
            if (_searchFieldRt == null || _searchText == null) return;

            // screen got rebuilt under us but our refs are stale � drop them, the patch
            // coroutine will rebuild. also bail if the root went away (left the screen).
            if (_seenToken != BuildToken || _root == null || _searchFieldRt == null)
            {
                SetFocus(false);
                _searchFieldRt = null;
                return;
            }

            HandleFocusClicks();

            if (!_focused) return;

            foreach (char c in Input.inputString)
            {
                if (c == '\b')
                {
                    if (_query.Length > 0) { _query = _query.Substring(0, _query.Length - 1); RefilterRows(); }
                }
                else if (c == '\n' || c == '\r' || c == '\x1b')
                {
                    SetFocus(false);
                }
                else
                {
                    _query += c;
                    RefilterRows();
                }
                UpdateCaret();
            }
        }

        private void HandleFocusClicks()
        {
            if (!Input.GetMouseButtonDown(0)) return;

            var mouse = (Vector2)Input.mousePosition;
            bool insideField = RectTransformUtility.RectangleContainsScreenPoint(_searchFieldRt, mouse, null);
            if (insideField) SetFocus(true);
            else if (_focused) SetFocus(false);
        }

        private void SetFocus(bool focused)
        {
            if (_focused == focused) return;
            _focused = focused;
            BetterFG.Services.FGInputLockService.SetFakeFieldLock(focused);
            UpdateCaret();
        }

        private void UpdateCaret()
        {
            if (_searchText == null) return;
            bool empty = string.IsNullOrEmpty(_query);
            _searchText.text = _query + (_focused ? "|" : "");
            if (_placeholder != null)
                _placeholder.gameObject.SetActive(empty && !_focused);
        }

        private void RefilterRows()
        {
            if (_content == null) return;

            string q = _query.Trim();
            bool showAll = q.Length == 0;

            int n = _content.childCount;
            for (int i = 0; i < n; i++)
            {
                var child = _content.GetChild(i);
                if (child == null) continue;

                if (showAll)
                {
                    if (!child.gameObject.activeSelf) child.gameObject.SetActive(true);
                    continue;
                }

                string name = null;
                var vm = child.GetComponent<PrivateLobbyShowListEntryViewModel>();
                if (vm != null)
                {
                    try { name = vm.ShowName; } catch { }
                }

                bool match = !string.IsNullOrEmpty(name) &&
                             name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                if (child.gameObject.activeSelf != match) child.gameObject.SetActive(match);
            }
        }

        public override void DisableTweak()
        {
            SetFocus(false);
            if (_searchFieldRt != null)
            {
                // un-filter and remove our bar; leave the viewport layout, it's harmless
                if (_content != null)
                    for (int i = 0; i < _content.childCount; i++)
                    {
                        var c = _content.GetChild(i);
                        if (c != null && !c.gameObject.activeSelf) c.gameObject.SetActive(true);
                    }
                UnityEngine.Object.Destroy(_searchFieldRt.gameObject);
                _searchFieldRt = null;
            }
        }
    }

}
