using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using FallGuysLib.UI;
using FGClient;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Mediatonic.Tools.MVVM;
using FGClient.Fraggle;
using BetterFG.UI;
using BetterFG.Services;
using BetterFG.Utilities;
using FG.Common.CMS;
using LayoutElement = UnityEngine.UI.LayoutElement;

namespace BetterFG.Features.QualificationTime
{
    // watches the popup root and fires the return-to-menu only once the popup is actually
    // destroyed. hooking the OK click directly softlocks if another popup spawns over ours and
    // tears it down out from under us, so we wait for the real OnDestroy instead.
    public class PBPopupDestroyWatcher : MonoBehaviour
    {
        public PBPopupDestroyWatcher(IntPtr ptr) : base(ptr) { }

        public void OnDestroy()
        {
            if (!PBPopup.IsOpen) return;
            PBPopup.IsOpen = false;

            var mmm = GameObject.Find("MainMenuManager")?.GetComponent<MainMenuManager>();
            mmm?.OnTitleScreenComplete();

            var builder = GameObject.Find("UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)");
            builder?.GetComponent<SwitchableViewFocusHandler>()?.OnParentViewGainedFocus();
        }
    }

    internal class PBPopup
    {
        static bool _showFeatured = false;
        static int _pageAll = 0;
        static int _pageFeatured = 0;
        static string _searchQuery = "";
        const int PageSize = 12;

        public static bool IsOpen = false;

        public static void Show()
        {
            // tear down any leftover modal before spawning a fresh popup
            var modalMessage = GameObject.Find("UICanvas_Client_V2(Clone)/ModalMessage");
            if (modalMessage != null)
            {
                var t = modalMessage.transform;
                for (int i = t.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(t.GetChild(i).gameObject);
            }
            IsOpen = false;

            _showFeatured = false;
            _pageAll = 0;
            _pageFeatured = 0;
            _searchQuery = "";
            BuildPopup();
        }

        static void BuildPopup()
        {
            var localized = CMSLoader.Instance._localisedStrings;
            if (!localized._localisedStrings.ContainsKey("bfgpbs"))
                localized._localisedStrings.Add("bfgpbs", "Personal Bests");

            PopUp.ShowPopup("bfgpbs", " ", FGClient.UI.PopupInteractionType.Info, FGClient.UI.UIModalMessage.ModalType.MT_OK, FGClient.UI.UIModalMessage.OKButtonType.Disruptive);

            IsOpen = true;

            var popupRoot = GameObject.Find("UICanvas_Client_V2(Clone)/ModalMessage/Generic_UI_ModalPopup_Variant(Clone)");
            popupRoot.transform.localScale *= 0.9f;

            // fire the return-to-menu when the popup is actually destroyed instead of on the OK
            // click, so another popup tearing this one down doesn't softlock us
            if (popupRoot.GetComponent<PBPopupDestroyWatcher>() == null)
                popupRoot.AddComponent<PBPopupDestroyWatcher>();

            var header = popupRoot.transform.Find("Panel/Title/Title_Text")?.GetComponent<TextMeshProUGUI>();
            if (header != null) { header.text = "Personal Bests"; header.ForceMeshUpdate(); }

            var contentArea = popupRoot.transform.Find("Content");
            if (contentArea == null) return;

            var contentText = contentArea.Find("Content_Text")?.GetComponent<TextMeshProUGUI>();
            TMP_FontAsset font = contentText?.font;
            Material fontMat = contentText?.fontSharedMaterial;
            if (contentText != null) contentText.gameObject.SetActive(false);

            // cleanup old
            var old = contentArea.Find("PB_ScrollView_All");
            if (old != null) UnityEngine.Object.Destroy(old.gameObject);
            var oldFeat = contentArea.Find("PB_ScrollView_Featured");
            if (oldFeat != null) UnityEngine.Object.Destroy(oldFeat.gameObject);
            var oldToggle = contentArea.Find("PB_ToggleBar");
            if (oldToggle != null) UnityEngine.Object.Destroy(oldToggle.gameObject);
            var oldSearch = contentArea.Find("PB_SearchBar");
            if (oldSearch != null) UnityEngine.Object.Destroy(oldSearch.gameObject);

            var leftBtnSrc = GameObject.Find("UICanvas_Client_V2(Clone)/ModalMessage/Generic_UI_ModalPopup_Variant(Clone)/ButtonContainer/LeftButton");

            // ── deprecation notice ──
            var noticeGo = new GameObject("PB_Notice");
            noticeGo.transform.SetParent(contentArea, false);
            var noticeRect = noticeGo.AddComponent<RectTransform>();
            noticeRect.sizeDelta = new Vector2(-20f, 52f);
            BetterFGUIMan.Instance.StartCoroutine(FixNoticePosition(noticeGo).WrapToIl2Cpp());
            var noticeTmp = noticeGo.AddComponent<TMPro.TextMeshProUGUI>();
            noticeTmp.font = font;
            noticeTmp.fontSharedMaterial = fontMat;
            noticeTmp.text = "This popup will no longer receive updates. Check out the new Personal Bests tab!";
            noticeTmp.fontSize = 26f;
            noticeTmp.color = new Color(1f, 0.85f, 0.3f, 0.9f);
            noticeTmp.alignment = TMPro.TextAlignmentOptions.Center;

            // ── search bar ──
            var searchBar = BuildSearchBar(contentArea, font, fontMat);
            BetterFGUIMan.Instance.StartCoroutine(FixStupidAssSearchBar(searchBar).WrapToIl2Cpp());


            // ── toggle bar ──
            var toggleBar = new GameObject("PB_ToggleBar");
            toggleBar.transform.SetParent(contentArea, false);
            var tbRect = toggleBar.AddComponent<RectTransform>();
            tbRect.anchorMin = new Vector2(0f, 1f);
            tbRect.anchorMax = new Vector2(1f, 1f);
            tbRect.pivot = new Vector2(0.5f, 1f);
            tbRect.anchoredPosition = new Vector2(0f, -70f);
            tbRect.sizeDelta = new Vector2(0f, 60f);
            var tbHlg = toggleBar.AddComponent<HorizontalLayoutGroup>();
            tbHlg.spacing = 8f;
            tbHlg.padding = new RectOffset(8, 8, 6, 6);
            tbHlg.childControlWidth = true;
            tbHlg.childControlHeight = false;
            tbHlg.childForceExpandWidth = true;
            tbHlg.childForceExpandHeight = false;

            var svAll = BuildScrollView(contentArea, font, fontMat, leftBtnSrc, false);
            var svFeatured = BuildScrollView(contentArea, font, fontMat, leftBtnSrc, true);
            svAll.SetActive(!_showFeatured);
            svFeatured.SetActive(_showFeatured);

            // wire search bar to rebuild active view
            var searchInput = searchBar.GetComponentInChildren<TMP_InputField>();
            if (searchInput != null)
            {
                searchInput.onValueChanged.AddListener((UnityEngine.Events.UnityAction<string>)(val =>
                {
                    _searchQuery = val ?? "";
                    _pageAll = 0;
                    _pageFeatured = 0;
                    RebuildScrollViewRows(svAll, font, fontMat, leftBtnSrc, false);
                    RebuildScrollViewRows(svFeatured, font, fontMat, leftBtnSrc, true);
                }));
            }

            SpawnTabButton(toggleBar.transform, "All PBs", leftBtnSrc, font, fontMat, !_showFeatured, () => {
                _showFeatured = false;
                svAll.SetActive(true);
                svFeatured.SetActive(false);
                TogglePageBars(contentArea);
                RefreshToggleBar(toggleBar.transform);
                for (int i = 0; i < toggleBar.transform.childCount; i++)
                {
                    var r = toggleBar.transform.GetChild(i).GetComponent<RectTransform>();
                    if (r != null) BetterFGUIMan.Instance.StartCoroutine(SetSizeDeltaNextFrame(r, new Vector2(488, 80)).WrapToIl2Cpp());
                }
            });
            SpawnTabButton(toggleBar.transform, "Favorites", leftBtnSrc, font, fontMat, _showFeatured, () => {
                _showFeatured = true;
                svAll.SetActive(false);
                svFeatured.SetActive(true);
                TogglePageBars(contentArea);
                RefreshToggleBar(toggleBar.transform);
                for (int i = 0; i < toggleBar.transform.childCount; i++)
                {
                    var r = toggleBar.transform.GetChild(i).GetComponent<RectTransform>();
                    if (r != null) BetterFGUIMan.Instance.StartCoroutine(SetSizeDeltaNextFrame(r, new Vector2(488, 80)).WrapToIl2Cpp());
                }
            });

            for (int i = 0; i < toggleBar.transform.childCount; i++)
            {
                var r = toggleBar.transform.GetChild(i).GetComponent<RectTransform>();
                if (r != null) BetterFGUIMan.Instance.StartCoroutine(SetSizeDeltaNextFrame(r, new Vector2(488, 80)).WrapToIl2Cpp());
            }

            BetterFGUIMan.Instance.StartCoroutine(deletethisshitnectframe(popupRoot).WrapToIl2Cpp());

            toggleBar.SetActive(true);

            ReapplyRowForeground(popupRoot.transform);
        }

        // our row overlays are tinted #26C6EC so the menu customization foreground pass treats them
        // as cyan. that pass normally runs on menu enter, but the popup (and its rows) are built
        // on-demand and rebuilt on paging/search/tab switches, so re-run it scoped to the popup
        // every time so a recoloured menu swaps our cyan to the user's chosen cyan.
        static void ReapplyRowForeground(Transform popupRoot)
        {
            if (popupRoot == null) return;
            var app = BetterFG.Customization.Menu.MenuCustomizationApplication.Instance;
            app?.ReapplyForegroundFromSettings(popupRoot);
        }

        static GameObject BuildSearchBar(Transform contentArea, TMP_FontAsset font, Material fontMat)
        {
            var bar = new GameObject("PB_SearchBar");
            bar.transform.SetParent(contentArea, false);
            var barRect = bar.AddComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.anchoredPosition = new Vector2(0f, -90f);
            barRect.sizeDelta = new Vector2(-20f, 52f);

            var barLE = bar.AddComponent<LayoutElement>();
            barLE.minHeight = 52f;
            barLE.preferredHeight = 52f;
            barLE.ignoreLayout = true;

            var bg = bar.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.3f);

            var inputGo = new GameObject("SearchInput");
            inputGo.transform.SetParent(bar.transform, false);
            var inputRect = inputGo.AddComponent<RectTransform>();
            inputRect.anchorMin = Vector2.zero;
            inputRect.anchorMax = Vector2.one;
            inputRect.offsetMin = new Vector2(12f, 4f);
            inputRect.offsetMax = new Vector2(-12f, -4f);

            var textAreaGo = new GameObject("TextArea");
            textAreaGo.transform.SetParent(inputGo.transform, false);
            var textAreaRect = textAreaGo.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = Vector2.zero;
            textAreaRect.offsetMax = Vector2.zero;
            textAreaGo.AddComponent<RectMask2D>();

            var textGo = new GameObject("Text");
            textGo.transform.SetParent(textAreaGo.transform, false);
            var textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            var textTmp = textGo.AddComponent<TextMeshProUGUI>();
            textTmp.font = font;
            textTmp.fontSharedMaterial = fontMat;
            textTmp.fontSize = 34f;
            textTmp.color = Color.white;
            textTmp.alignment = TextAlignmentOptions.MidlineLeft;

            var placeholderGo = new GameObject("Placeholder");
            placeholderGo.transform.SetParent(textAreaGo.transform, false);
            var phRect = placeholderGo.AddComponent<RectTransform>();
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.offsetMin = Vector2.zero;
            phRect.offsetMax = Vector2.zero;
            var phTmp = placeholderGo.AddComponent<TextMeshProUGUI>();
            phTmp.font = font;
            phTmp.fontSharedMaterial = fontMat;
            phTmp.fontSize = 34f;
            phTmp.color = new Color(1f, 1f, 1f, 0.35f);
            phTmp.text = "Search...";
            phTmp.alignment = TextAlignmentOptions.MidlineLeft;
            phTmp.fontStyle = FontStyles.Italic;

            var field = inputGo.AddComponent<TMP_InputField>();
            field.textViewport = textAreaRect;
            field.textComponent = textTmp;
            field.placeholder = phTmp;
            field.fontAsset = font;
            field.pointSize = 34f;
            field.text = _searchQuery;

            bar.SetActive(true);
            return bar;
        }

        static void RebuildScrollViewRows(GameObject svGo, TMP_FontAsset font, Material fontMat, GameObject leftBtnSrc, bool featured)
        {
            var container = svGo.transform.Find("Viewport/ItemContainer");
            if (container == null) return;

            // clear existing rows and page bar
            for (int i = container.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(container.GetChild(i).gameObject);
            var oldPageBar = svGo.transform.parent?.Find(featured ? "PB_PageBar_Featured" : "PB_PageBar_All");
            if (oldPageBar != null) UnityEngine.Object.Destroy(oldPageBar.gameObject);

            PopulateRows(container, svGo.transform, font, fontMat, leftBtnSrc, featured);

            var cRect = container.GetComponent<RectTransform>();
            if (cRect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(cRect);

            // freshly rebuilt rows have new overlay images - recolour them like the popup build does
            ReapplyRowForeground(container);
        }

        static void PopulateRows(Transform container, Transform svRoot, TMP_FontAsset font, Material fontMat, GameObject leftBtnSrc, bool featured)
        {
            // popup rows show the fastest of solos/duos/squads per map
            var allPbs = featured ? PBStore.GetFeaturedBest() : PBStore.GetAllDedupedBest();
            bool isSearching = !string.IsNullOrEmpty(_searchQuery);

            // when searching, pull from all pbs regardless of featured flag so we always search everything
            var source = isSearching
                ? (featured ? PBStore.GetFeaturedBest() : PBStore.GetAllDedupedBest())
                : allPbs;

            var sorted = source.OrderBy(x => x.Value.time).ToList();

            if (isSearching)
            {
                string q = _searchQuery.ToLowerInvariant();
                sorted = sorted.Where(x =>
                    (!string.IsNullOrEmpty(x.Value.displayName) && x.Value.displayName.ToLowerInvariant().Contains(q)) ||
                    x.Key.ToLowerInvariant().Contains(q)
                ).ToList();
            }

            if (sorted.Count == 0)
            {
                string msg = isSearching
                    ? "No results for \"" + _searchQuery + "\""
                    : (featured ? "No favorited PBs yet. Star a PB from the All PBs tab!" : "No personal bests recorded yet..... :(");
                SpawnRow(container, msg, "", null, font, fontMat, false, null, null);
                return;
            }

            if (isSearching)
            {
                bool alt = false;
                foreach (var kv in sorted)
                {
                    TimeSpan t = TimeSpan.FromSeconds(kv.Value.time);
                    string time = string.Format("{0:D2}:{1:D2}:{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
                    string label = string.IsNullOrEmpty(kv.Value.displayName) ? kv.Key : kv.Value.displayName;
                    string rawId = kv.Value.rawId ?? kv.Key;
                    var thumb = SplashCache.LoadCached(rawId, kv.Value.displayName);
                    SpawnRow(container, label, time, rawId, font, fontMat, alt, thumb, () => RebuildScrollViewRows(svRoot.gameObject, font, fontMat, leftBtnSrc, featured));
                    alt = !alt;
                }
            }
            else
            {
                int page = featured ? _pageFeatured : _pageAll;
                int totalPages = (sorted.Count + PageSize - 1) / PageSize;
                if (page >= totalPages) page = totalPages - 1;
                if (page < 0) page = 0;
                if (featured) _pageFeatured = page; else _pageAll = page;

                int start = page * PageSize;
                int end = Mathf.Min(start + PageSize, sorted.Count);
                bool alt = false;
                for (int i = start; i < end; i++)
                {
                    var kv = sorted[i];
                    TimeSpan t = TimeSpan.FromSeconds(kv.Value.time);
                    string time = string.Format("{0:D2}:{1:D2}:{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
                    string label = string.IsNullOrEmpty(kv.Value.displayName) ? kv.Key : kv.Value.displayName;
                    string rawId = kv.Value.rawId ?? kv.Key;
                    var thumb = SplashCache.LoadCached(rawId, kv.Value.displayName);
                    SpawnRow(container, label, time, rawId, font, fontMat, alt, thumb, () => RebuildScrollViewRows(svRoot.gameObject, font, fontMat, leftBtnSrc, featured));
                    alt = !alt;
                }

                if (totalPages > 1)
                    SpawnPageBar(svRoot, font, fontMat, leftBtnSrc, featured, page, totalPages, container, svRoot);
            }
        }

        static void SpawnPageBar(Transform svRoot, TMP_FontAsset font, Material fontMat, GameObject leftBtnSrc, bool featured, int currentPage, int totalPages, Transform container, Transform svRootRef)
        {
            var bar = new GameObject(featured ? "PB_PageBar_Featured" : "PB_PageBar_All");
            bar.transform.SetParent(svRoot.parent, false);

            // sibling of the scroll view under Content, anchored to the bottom of Content
            var barRect = bar.AddComponent<RectTransform>();
            barRect.anchorMin = new Vector2(0f, 0f);
            barRect.anchorMax = new Vector2(1f, 0f);
            barRect.pivot = new Vector2(0.5f, 0f);
            barRect.anchoredPosition = new Vector2(0f, 4f);
            barRect.sizeDelta = new Vector2(0f, 60f);

            var hlg = bar.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 128f;
            hlg.padding = new RectOffset(8, 8, 4, 4);
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleCenter;

            void SpawnPageBtn(string label, bool enabled, System.Action onClick)
            {
                GameObject btnGo;
                if (leftBtnSrc != null)
                {
                    btnGo = UnityEngine.Object.Instantiate(leftBtnSrc, bar.transform);
                    btnGo.SetActive(true);
                    var existingBtn = btnGo.GetComponent<Button>();
                    if (existingBtn != null)
                    {
                        existingBtn.onClick.RemoveAllListeners();
                        if (enabled) existingBtn.onClick.AddListener((UnityEngine.Events.UnityAction)onClick);
                        existingBtn.interactable = enabled;
                    }
                    var tmp = btnGo.transform.Find("Content/Text")?.GetComponent<TextMeshProUGUI>();
                    if (tmp != null) { UnityEngine.Object.Destroy(tmp.GetComponent<TextBinding>()); tmp.text = label; tmp.ForceMeshUpdate(); }
                    var controlGlyph = btnGo.transform.Find("Content/ControlGlyphButton");
                    if (controlGlyph != null) UnityEngine.Object.Destroy(controlGlyph.gameObject);
                    var le = btnGo.GetComponent<LayoutElement>() ?? btnGo.AddComponent<LayoutElement>();
                    le.preferredWidth = 160f;
                    le.preferredHeight = 52f;
                    le.minWidth = 160f;
                    le.minHeight = 52f;
                    le.ignoreLayout = false;
                    btnGo.transform.Find("Panel_Selected")?.gameObject.SetActive(false);
                    btnGo.transform.Find("Panel_Unselected")?.gameObject.SetActive(true);
                }
                else
                {
                    btnGo = new GameObject("PageBtn");
                    btnGo.transform.SetParent(bar.transform, false);
                    var btn = btnGo.AddComponent<Button>();
                    if (enabled) btn.onClick.AddListener((UnityEngine.Events.UnityAction)onClick);
                    btn.interactable = enabled;
                    var img = btnGo.AddComponent<Image>();
                    img.color = enabled ? new Color(1f, 1f, 1f, 0.15f) : new Color(0.5f, 0.5f, 0.5f, 0.1f);
                    var le = btnGo.AddComponent<LayoutElement>();
                    le.preferredWidth = 160f; le.preferredHeight = 52f;
                    var tgo = new GameObject("T"); tgo.transform.SetParent(btnGo.transform, false);
                    var tr = tgo.AddComponent<RectTransform>();
                    tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
                    tr.offsetMin = Vector2.zero; tr.offsetMax = Vector2.zero;
                    var t = tgo.AddComponent<TextMeshProUGUI>();
                    t.font = font; t.fontSharedMaterial = fontMat;
                    t.text = label; t.fontSize = 28f; t.color = Color.white;
                    t.alignment = TextAlignmentOptions.Center;
                }
            }

            SpawnPageBtn("< Prev", currentPage > 0, () => {
                if (featured) _pageFeatured--; else _pageAll--;
                RebuildScrollViewRows(svRootRef.gameObject, font, fontMat, leftBtnSrc, featured);
            });

            var pageLabel = new GameObject("PageLabel");
            pageLabel.transform.SetParent(bar.transform, false);
            var plLe = pageLabel.AddComponent<LayoutElement>();
            plLe.preferredWidth = 160f; plLe.preferredHeight = 52f;
            var plTmp = pageLabel.AddComponent<TextMeshProUGUI>();
            plTmp.font = font;
            plTmp.fontSharedMaterial = fontMat;
            plTmp.text = (currentPage + 1) + " / " + totalPages;
            plTmp.fontSize = 30f;
            plTmp.color = new Color(1f, 1f, 1f, 0.7f);
            plTmp.alignment = TextAlignmentOptions.Center;

            SpawnPageBtn("Next >", currentPage < totalPages - 1, () => {
                if (featured) _pageFeatured++; else _pageAll++;
                RebuildScrollViewRows(svRootRef.gameObject, font, fontMat, leftBtnSrc, featured);
            });

            // only show this tab's page bar when its tab is the active one
            bar.SetActive(featured == _showFeatured);
        }

        static void TogglePageBars(Transform contentArea)
        {
            var allBar = contentArea.Find("PB_PageBar_All");
            if (allBar != null) allBar.gameObject.SetActive(!_showFeatured);
            var featBar = contentArea.Find("PB_PageBar_Featured");
            if (featBar != null) featBar.gameObject.SetActive(_showFeatured);
        }

        static void RefreshToggleBar(Transform toggleBarTransform)
        {
            for (int i = 0; i < toggleBarTransform.childCount; i++)
            {
                var child = toggleBarTransform.GetChild(i);
                bool isAll = child.name == "TabBtn_All PBs";
                bool isActive = isAll ? !_showFeatured : _showFeatured;
                child.Find("Panel_Selected")?.gameObject.SetActive(isActive);
                child.Find("Panel_Unselected")?.gameObject.SetActive(!isActive);
            }
        }

        static void SpawnTabButton(Transform parent, string label, GameObject srcBtn, TMP_FontAsset font, Material fontMat, bool active, System.Action onClick)
        {
            GameObject btnGo;
            if (srcBtn != null)
            {
                btnGo = UnityEngine.Object.Instantiate(srcBtn, parent);
                btnGo.SetActive(true);
                var btnRect = btnGo.GetComponent<RectTransform>();
                if (btnRect != null)
                    BetterFGUIMan.Instance.StartCoroutine(SetSizeDeltaNextFrame(btnRect, new Vector2(488, 80)).WrapToIl2Cpp());
                var btnLE = btnGo.GetComponent<LayoutElement>() ?? btnGo.AddComponent<LayoutElement>();
                btnLE.minHeight = 80f;
                btnLE.preferredHeight = 80f;
                // strip existing listeners
                var existingBtn = btnGo.GetComponent<Button>();
                if (existingBtn != null)
                {
                    existingBtn.onClick.RemoveAllListeners();
                    existingBtn.onClick.AddListener((UnityEngine.Events.UnityAction)onClick);
                }
                // set text
                var tmp = btnGo.transform.Find("Content/Text")?.GetComponent<TextMeshProUGUI>();
                UnityEngine.Object.Destroy(tmp.gameObject.GetComponent<TextBinding>());
                var controlGlyph = tmp.transform.parent.Find("ControlGlyphButton");
                if (controlGlyph != null) UnityEngine.Object.Destroy(controlGlyph.gameObject);
                if (tmp != null) { tmp.text = label; tmp.ForceMeshUpdate(); }
                btnGo.transform.Find("Panel_Selected")?.gameObject.SetActive(active);
                btnGo.transform.Find("Panel_Unselected")?.gameObject.SetActive(!active);
            }
            else
            {
                btnGo = new GameObject("TabBtn_" + label);
                btnGo.transform.SetParent(parent, false);
                var btn = btnGo.AddComponent<Button>();
                btn.onClick.AddListener((UnityEngine.Events.UnityAction)onClick);
                var bg = btnGo.AddComponent<Image>();
                bg.color = active ? new Color(1f, 1f, 1f, 0.2f) : new Color(0f, 0f, 0f, 0.15f);
                var txtGo = new GameObject("Text");
                txtGo.transform.SetParent(btnGo.transform, false);
                var txtRect = txtGo.AddComponent<RectTransform>();
                txtRect.anchorMin = Vector2.zero;
                txtRect.anchorMax = Vector2.one;
                txtRect.offsetMin = Vector2.zero;
                txtRect.offsetMax = Vector2.zero;
                var tmp = txtGo.AddComponent<TextMeshProUGUI>();
                tmp.font = font;
                tmp.fontSharedMaterial = fontMat;
                tmp.text = label;
                tmp.fontSize = 28f;
                tmp.color = active ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                tmp.alignment = TextAlignmentOptions.Center;
                btnGo.SetActive(true);
                BetterFGUIMan.Instance.StartCoroutine(SetSizeDeltaNextFrame(txtRect, new Vector2(488, 90)).WrapToIl2Cpp());
            }
            btnGo.name = "TabBtn_" + label;
        }

        static GameObject BuildScrollView(Transform contentArea, TMP_FontAsset font, Material fontMat, GameObject leftBtnSrc, bool featured)
        {
            var svGo = new GameObject(featured ? "PB_ScrollView_Featured" : "PB_ScrollView_All");
            svGo.transform.SetParent(contentArea, false);

            var svRect = svGo.AddComponent<RectTransform>();
            svRect.anchorMin = Vector2.zero;
            svRect.anchorMax = Vector2.one;
            svRect.offsetMin = new Vector2(6f, 6f);
            svRect.offsetMax = new Vector2(-6f, -140f);

            var svLE = svGo.AddComponent<LayoutElement>();
            svLE.flexibleWidth = 1f;
            svLE.flexibleHeight = 1f;
            svLE.minHeight = 530f;

            svGo.AddComponent<RectMask2D>();

            var sv = svGo.AddComponent<ScrollRect>();
            sv.horizontal = false;
            sv.vertical = true;
            sv.scrollSensitivity = 30f;
            sv.movementType = ScrollRect.MovementType.Elastic;
            sv.elasticity = 0.1f;

            var vpGo = new GameObject("Viewport");
            vpGo.transform.SetParent(svGo.transform, false);
            var vpRect = vpGo.AddComponent<RectTransform>();
            vpRect.anchorMin = Vector2.zero;
            vpRect.anchorMax = Vector2.one;
            vpRect.offsetMin = Vector2.zero;
            vpRect.offsetMax = new Vector2(0f, -64f); // leave room at bottom for page bar
            vpGo.AddComponent<RectMask2D>();
            sv.viewport = vpRect;

            var cGo = new GameObject("ItemContainer");
            cGo.transform.SetParent(vpGo.transform, false);
            var cRect = cGo.AddComponent<RectTransform>();
            cRect.anchorMin = new Vector2(0f, 1f);
            cRect.anchorMax = new Vector2(1f, 1f);
            cRect.pivot = new Vector2(0.5f, 1f);
            cRect.offsetMin = Vector2.zero;
            cRect.offsetMax = Vector2.zero;
            sv.content = cRect;

            var vlg = cGo.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 3f;
            vlg.padding = new RectOffset(4, 4, 4, 4);
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var csf = cGo.AddComponent<ContentSizeFitter>();
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            PopulateRows(cGo.transform, svGo.transform, font, fontMat, leftBtnSrc, featured);

            LayoutRebuilder.ForceRebuildLayoutImmediate(cRect);
            LayoutRebuilder.ForceRebuildLayoutImmediate(svRect);
            return svGo;
        }

        static IEnumerator SetSizeDeltaNextFrame(RectTransform rect, Vector2 size)
        {
            yield return null;
            yield return new WaitForSeconds(0.015f);
            if (rect != null) rect.sizeDelta = size;
        }

        static IEnumerator deletethisshitnectframe(GameObject recte)
        {
            yield return null;
            yield return new WaitForSeconds(0.015f);
            UnityEngine.Object.Destroy(recte.GetComponent<ContentSizeFitter>());

            // the game relays out the modal a frame after the fitter is gone, which clobbers our
            // positions. re-apply them across the next few frames so ours win.
            BetterFGUIMan.Instance.StartCoroutine(ApplyPopupTransforms(recte).WrapToIl2Cpp());
        }

        static IEnumerator ApplyPopupTransforms(GameObject recte)
        {
            for (int pass = 0; pass < 4; pass++)
            {
                yield return null;
                if (recte == null) yield break;

                recte.transform.localPosition = new Vector3(272.8359f, -58.1818f, 0f);

                var content = recte.transform.Find("Content");
                if (content != null)
                {
                    content.localPosition = new Vector3(0f, -20f, 0f);

                    var notice = content.Find("PB_Notice");
                    if (notice != null)
                        notice.localPosition = new Vector3(0f, 384.8372f, 0f);

                    var searchBar = content.Find("PB_SearchBar");
                    if (searchBar != null)
                        searchBar.localPosition = new Vector3(0f, 244.5512f, 0f);

                    foreach (var svName in new[] { "PB_ScrollView_All", "PB_ScrollView_Featured" })
                    {
                        var scroll = content.Find(svName);
                        if (scroll != null)
                        {
                            scroll.localPosition = new Vector3(0f, -32.7629f, 0f);
                            scroll.localScale = new Vector3(1.1f, 1.1f, 1.1f);
                        }
                    }

                }
            }

            // the layout only settles correctly after Content is toggled off then on
            var c = recte.transform.Find("Content");
            if (c != null)
            {
                c.gameObject.SetActive(false);
                yield return null;
                c.gameObject.SetActive(true);
                yield return null;

                var noticeAfter = c.Find("PB_Notice");
                if (noticeAfter != null)
                    noticeAfter.localPosition = new Vector3(0f, 384.8372f, 0f);

                // re-size the tab buttons after the relayout settles, same as a tab click does,
                // otherwise the spacing collapses on first spawn
                var toggleBar = c.Find("PB_ToggleBar");
                if (toggleBar != null)
                {
                    for (int i = 0; i < toggleBar.childCount; i++)
                    {
                        var r = toggleBar.GetChild(i).GetComponent<RectTransform>();
                        if (r != null) r.sizeDelta = new Vector2(488f, 80f);
                    }
                }
            }
        }

        static IEnumerator SetLocalPositionNextFrame(RectTransform rect, Vector3 pos)
        {
            yield return null;
            yield return new WaitForSeconds(0.015f);
            if (rect != null) rect.localPosition = pos;
        }

        static FGClient.Fraggle.CreatorIDViewModel _cachedCreatorPrefab;
        static FGClient.Fraggle.CreatorIDViewModel GetCreatorPrefabCached()
        {
            if (_cachedCreatorPrefab != null && _cachedCreatorPrefab.gameObject != null) return _cachedCreatorPrefab;
            var prefabs = Resources.FindObjectsOfTypeAll<FGClient.Fraggle.CreatorIDViewModel>();
            _cachedCreatorPrefab = prefabs?.FirstOrDefault(x => x != null && x.gameObject != null && x.gameObject.name == "Generic_UI_LE_CreatorInfoSlim");
            return _cachedCreatorPrefab;
        }

        static void SpawnRow(Transform parent, string level, string time, string roundId, TMP_FontAsset font, Material fontMat, bool alt, Texture2D thumb, System.Action refreshRows)
        {
            var row = new GameObject("Row");
            row.transform.SetParent(parent, false);

            var rRect = row.AddComponent<RectTransform>();
            rRect.sizeDelta = new Vector2(0f, 130f);

            var bg = row.AddComponent<Image>();
            bg.color = alt ? new Color(1f, 1f, 1f, 0.06f) : new Color(0f, 0f, 0f, 0f);

            GameObject imgGo = null;

            if (thumb != null)
            {
                var splashGo = new GameObject("SplashBG");
                splashGo.transform.SetParent(row.transform, false);
                splashGo.transform.SetSiblingIndex(0);

                var splashRect = splashGo.AddComponent<RectTransform>();
                splashRect.anchorMin = Vector2.zero;
                splashRect.anchorMax = Vector2.one;
                splashRect.offsetMin = Vector2.zero;
                splashRect.offsetMax = Vector2.zero;

                splashGo.AddComponent<LayoutElement>().ignoreLayout = true;
                splashGo.AddComponent<RectMask2D>().softness = new Vector2Int(120, 0);

                imgGo = new GameObject("Img");
                imgGo.transform.SetParent(splashGo.transform, false);

                var imgRect = imgGo.AddComponent<RectTransform>();
                imgRect.anchorMin = new Vector2(0f, 0f);
                imgRect.anchorMax = new Vector2(0f, 1f);
                imgRect.pivot = new Vector2(0f, 0.5f);

                var arf = imgGo.AddComponent<AspectRatioFitter>();
                arf.aspectMode = AspectRatioFitter.AspectMode.HeightControlsWidth;
                arf.aspectRatio = (float)thumb.width / thumb.height;

                var splashImg = imgGo.AddComponent<RawImage>();
                splashImg.texture = thumb;
                splashImg.color = new Color(1f, 1f, 1f, 0.87f);
            }

            // overlay across the whole row, sitting above the splash image but below text/buttons
            // (those get added after this, so they end up on top by sibling order).
            // tinted rgb(42, 176, 209) (a cyan) and named with "BG" so the menu customization
            // foreground pass picks it up and swaps it to the user's chosen cyan if they've
            // recoloured the menu.
            var overlaySprite = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.feature.qualificationtime.bg.png");
            if (overlaySprite != null)
            {
                var overlayGo = new GameObject("Overlay_BG");
                overlayGo.transform.SetParent(row.transform, false);
                var overlayRect = overlayGo.AddComponent<RectTransform>();
                overlayRect.anchorMin = Vector2.zero;
                overlayRect.anchorMax = Vector2.one;
                overlayRect.offsetMin = Vector2.zero;
                overlayRect.offsetMax = Vector2.zero;
                overlayGo.AddComponent<LayoutElement>().ignoreLayout = true;
                var overlayImg = overlayGo.AddComponent<Image>();
                overlayImg.sprite = overlaySprite;
                overlayImg.raycastTarget = false;
                // rgb(42, 176, 209)
                overlayImg.color = new Color(42 / 255f, 176 / 255f, 209 / 255f, 1f);
            }

            string ugcTag = null;
            if (roundId != null && roundId.Contains("ugc-"))
            {
                int idx = roundId.IndexOf("ugc-");
                ugcTag = roundId.Substring(idx + 4);
                int cut = ugcTag.IndexOf('_');
                if (cut >= 0) ugcTag = ugcTag.Substring(0, cut);
            }

            // name text -- pushed in from left edge
            var nameGo = new GameObject("Name");
            nameGo.transform.SetParent(row.transform, false);
            var nameRect = nameGo.AddComponent<RectTransform>();
            bool emptyRow = string.IsNullOrEmpty(time);
            nameRect.anchorMin = new Vector2(0f, emptyRow ? 0f : 0f);
            nameRect.anchorMax = new Vector2(emptyRow ? 1f : 0.65f, emptyRow ? 1f : 0f);
            nameRect.pivot = new Vector2(emptyRow ? 0.5f : 0f, emptyRow ? 0.5f : 0f);
            nameRect.anchoredPosition = emptyRow ? new Vector2(0f, 0f) : new Vector2(80f, 10f);
            nameRect.sizeDelta = emptyRow ? new Vector2(0f, 0f) : new Vector2(-10f, 56f);
            nameGo.AddComponent<LayoutElement>().ignoreLayout = true;

            var nameTmp = nameGo.AddComponent<TextMeshProUGUI>();
            nameTmp.font = font;
            nameTmp.fontSharedMaterial = fontMat;
            nameTmp.text = level;
            nameTmp.fontSize = 40f;
            // pbs that have a saved ghost get a very light baby blue name so you can spot them
            bool hasGhost = !emptyRow && roundId != null && FeatureQualificationTime.HasGhost(level, roundId);
            nameTmp.color = hasGhost ? new Color(0.75f, 0.9f, 1f) : Color.white;
            nameTmp.alignment = emptyRow ? TextAlignmentOptions.Center : TextAlignmentOptions.BottomLeft;
            nameTmp.enableWordWrapping = emptyRow;
            nameTmp.overflowMode = emptyRow ? TextOverflowModes.Overflow : TextOverflowModes.Ellipsis;

            if (ugcTag != null)
            {
                // cached - the prefab lives in DontDestroyOnLoad so it never goes away once found,
                // and SpawnRow gets called once per PB row (often 10+) so the old per-row scan was
                // a heap walk per row for no reason
                var creatorPrefab = GetCreatorPrefabCached();
                if (creatorPrefab != null)
                {
                    var creatorInst = UnityEngine.Object.Instantiate(creatorPrefab.gameObject, row.transform);
                    creatorInst.SetActive(true);

                    BetterFGUIMan.Instance.StartCoroutine(FixNextFrame(creatorInst, ugcTag, imgGo).WrapToIl2Cpp());
                }
            }
            else if (imgGo != null)
            {
                BetterFGUIMan.Instance.StartCoroutine(FixImageOnly(imgGo).WrapToIl2Cpp());
            }

            if (!string.IsNullOrEmpty(time))
            {
                var timeGo = new GameObject("Time");
                timeGo.transform.SetParent(row.transform, false);
                var timeRect = timeGo.AddComponent<RectTransform>();
                timeRect.anchorMin = new Vector2(1f, 0f);
                timeRect.anchorMax = new Vector2(1f, 0f);
                timeRect.pivot = new Vector2(1f, 0f);
                // pushed in from right edge
                timeRect.anchoredPosition = new Vector2(-80f, 10f);
                timeRect.sizeDelta = new Vector2(200f, 56f);
                timeGo.AddComponent<LayoutElement>().ignoreLayout = true;

                var timeTmp = timeGo.AddComponent<TextMeshProUGUI>();
                timeTmp.font = font;
                timeTmp.fontSharedMaterial = fontMat;
                timeTmp.text = time;
                timeTmp.fontSize = 48f;
                timeTmp.color = new Color(1f, 1f, 0f);
                timeTmp.alignment = TextAlignmentOptions.BottomRight;
                timeTmp.enableWordWrapping = false;
            }

            if (roundId != null)
            {
                bool isFeatured = PBStore.IsFeatured(roundId, level);

                var starGo = new GameObject("StarBtn");
                starGo.transform.SetParent(row.transform, false);
                var starRect = starGo.AddComponent<RectTransform>();
                starRect.anchorMin = new Vector2(1f, 1f);
                starRect.anchorMax = new Vector2(1f, 1f);
                starRect.pivot = new Vector2(1f, 1f);
                // pushed in from right edge
                starRect.anchoredPosition = new Vector2(-80f, -8f);
                starRect.sizeDelta = new Vector2(48f, 48f);
                starGo.AddComponent<LayoutElement>().ignoreLayout = true;

                var starImg = starGo.AddComponent<Image>();
                starImg.color = Color.white;
                starImg.preserveAspect = true;

                void ApplyStarSprite(bool featured)
                {
                    var sprite = EmbeddedResourceandUnity.LoadSprite(
                        featured
                            ? "BetterFG.assets.ui.feature.qualificationtime.featurequalificationtime_favoritedstar.png"
                            : "BetterFG.assets.ui.feature.qualificationtime.featurequalificationtime_favoritestar.png"
                    );
                    if (sprite != null) starImg.sprite = sprite;
                }

                ApplyStarSprite(isFeatured);

                var starBtn = starGo.AddComponent<Button>();
                System.Action starAction = () =>
                {
                    bool nowFeatured = PBStore.TryFeature(roundId, level);
                    ApplyStarSprite(nowFeatured);
                };
                starBtn.onClick.AddListener((UnityEngine.Events.UnityAction)starAction);

                starGo.SetActive(true);

                var deleteGo = new GameObject("DeleteBtn");
                deleteGo.transform.SetParent(row.transform, false);
                var deleteRect = deleteGo.AddComponent<RectTransform>();
                deleteRect.anchorMin = new Vector2(1f, 1f);
                deleteRect.anchorMax = new Vector2(1f, 1f);
                deleteRect.pivot = new Vector2(1f, 1f);
                deleteRect.anchoredPosition = new Vector2(-136f, -8f);
                deleteRect.sizeDelta = new Vector2(48f, 48f);
                deleteGo.AddComponent<LayoutElement>().ignoreLayout = true;

                var deleteImg = deleteGo.AddComponent<Image>();
                deleteImg.color = Color.white;
                deleteImg.preserveAspect = true;
                var deleteIdle = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.feature.qualificationtime.featurequalificationtime_delete_idle.png");
                var deleteHover = EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.feature.qualificationtime.featurequalificationtime_delete.png");
                if (deleteIdle != null) deleteImg.sprite = deleteIdle;

                var deleteBtn = deleteGo.AddComponent<Button>();
                deleteBtn.targetGraphic = deleteImg;
                deleteBtn.transition = Selectable.Transition.SpriteSwap;
                var deleteState = deleteBtn.spriteState;
                deleteState.highlightedSprite = deleteHover;
                deleteState.pressedSprite = deleteHover;
                deleteState.selectedSprite = deleteHover;
                deleteBtn.spriteState = deleteState;

                var trigger = deleteGo.AddComponent<EventTrigger>();
                var hoverEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                hoverEntry.callback.AddListener((UnityAction<BaseEventData>)(_ => AudioService.PlayButtonHoverOn()));
                trigger.triggers.Add(hoverEntry);

                System.Action deleteAction = () =>
                {
                    AudioService.PlayButtonClick();
                    if (PBStore.TryDelete(roundId, level))
                    {
                        // kill the ghost too - it's named after either the display name (unity
                        // rounds) or the ugc id, so hand DeleteGhost both and let it sort it out.
                        FeatureQualificationTime.DeleteGhost(level, roundId);
                        refreshRows?.Invoke();
                    }
                };
                deleteBtn.onClick.AddListener((UnityEngine.Events.UnityAction)deleteAction);
                deleteGo.SetActive(true);
            }

            row.SetActive(true);
        }

        static IEnumerator FixNextFrame(GameObject creatorInst, string ugcTag, GameObject imgGo)
        {
            yield return null;
            yield return new WaitForSeconds(0.015f);

            var containerContent = creatorInst.transform.Find("Container_Content");

            UnityEngine.Object.Destroy(containerContent.parent.GetComponent<CreatorIDViewModel>());
            if (containerContent != null)
            {
                containerContent.gameObject.SetActive(true);

                var shareCodeTmp = containerContent.Find("Text_ShareCode")?.GetComponent<TextMeshProUGUI>();
                if (shareCodeTmp != null) shareCodeTmp.text = ugcTag;
            }

            var rect = creatorInst.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 0f);
                rect.pivot = new Vector2(0f, 0f);
                // pushed in from left edge to match name text offset
                rect.anchoredPosition = new Vector2(80f, 60f);
                rect.sizeDelta = new Vector2(300f, 40f);
                rect.transform.localPosition = new Vector3(-447.2503f, -5f, 0f);
            }

            var textcreator = containerContent.transform.Find("BG").Find("Text_Creator");
            textcreator.gameObject.SetActive(false);

            if (imgGo != null) ApplyImageFix(imgGo);
        }

        static IEnumerator FixImageOnly(GameObject imgGo)
        {
            yield return null;
            ApplyImageFix(imgGo);
        }

        static IEnumerator FixStupidAssSearchBar(GameObject imgGo)
        {
            yield return null;
            imgGo.transform.localPosition = new Vector3(0f, 244.5512f, 0f);
        }

        static IEnumerator FixNoticePosition(GameObject noticeGo)
        {
            yield return null;
            if (noticeGo != null)
                noticeGo.transform.localPosition = new Vector3(0f, 384.8372f, 0f);
        }


        static void ApplyImageFix(GameObject imgGo)
        {
            var rect = imgGo.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0f, 0f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(-250f, -40f);
                rect.localScale = new Vector3(3f, 3f, 3f);
            }
        }
    }
}
