using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using BetterFG;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Customization.Player;
using BetterFG.UI.Windows;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using BetterFG.Customization.Menu;

namespace BetterFG.UI.Tab
{
    public class CustomizationTab : BetterFGTab
    {
        public CustomizationTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "UGC Customization";

        // ── Empty state ───────────────────────────────────────────────────────
        private const string EMPTY_NO_REPO = "No repository selected.";
        private const string EMPTY_NO_RESULTS = "No results for \"{0}\".";
        private const string EMPTY_NO_TYPE = "No {0}s in this repository";
        private const string EMPTY_BEAN_RES = "BetterFG.assets.ui.bean.bean_frighten.png";
        private static Texture2D _frightenTex;
        private Text _fetchCountLabel; // live "X / Y fetched" line under the empty state, updated per skin

        // ── Settings keys ─────────────────────────────────────────────────────
        private const string KEY_MULTI_FILES = "skin.multi.files";
        private const string KEY_MULTI_SOURCES = "skin.multi.sources";
        private const string KEY_MULTI_PATHS = "skin.multi.paths";
        private const string KEY_MULTI_TYPES = "skin.multi.types";
        private const string KEY_IMPORTED_PATHS = "skin.imported.paths";

        // sentinel value used as Active.githubUrl when "Imported Skins" repo is selected
        private const string IMPORTED_REPO_KEY = "__imported__";

        // legacy single-skin keys kept for restore only
        private const string KEY_SOURCE = "skin.source";
        private const string KEY_FILE = "skin.file";
        private const string KEY_LOCAL_PATH = "skin.localPath";

        // hand overrides per skin file: 0=default,1=left,2=right,3=both
        private const string KEY_HAND_OVERRIDES = "skin.hand.overrides";
        private Dictionary<string, int> _handOverrides = new Dictionary<string, int>();

        // ── Imported skins persistent list ────────────────────────────────────
        private List<string> _importedPaths = new List<string>(); // folder paths, persisted
        private bool _importedRepoSelected = false;
        private bool _featuredSelected = false; // "Featured Repos" dropdown entry active — list shows repos, not skins
        private Dictionary<string, RawImage> _featuredCoverImages = new Dictionary<string, RawImage>();
        private RectTransform _repoScrollContent; // dropdown scroll content, rebuilt after parent

        // open item config window
        private ItemConfigWindow _configWindow;
        private string _configWindowFile;

        // ── Selection limits ──────────────────────────────────────────────────
        private const int MAX_COSTUME = 1;
        private const int MAX_ACCESSORY = 3;
        private const int MAX_ITEM = 2;

        // ── Active filter tab ─────────────────────────────────────────────────
        private SkinType _activeFilter = SkinType.Costume;

        // ── Multi-selection ───────────────────────────────────────────────────
        private HashSet<int> selectedIndices = new HashSet<int>();

        // emote "Copy" is two-stage: first press copies + arms this row, second press opens Social>Emotes
        private int _copyArmedIndex = -1;

        private int SelectedCostumeIndex()
        {
            foreach (int i in selectedIndices)
            {
                if (i < 0 || i >= availableSkins.Count) continue;
                if (SkinTypeParser.FromString(availableSkins[i].type) == SkinType.Costume) return i;
            }
            return -1;
        }

        private int CountSelected(SkinType type)
        {
            int c = 0;
            foreach (int i in selectedIndices)
            {
                if (i < 0 || i >= availableSkins.Count) continue;
                if (SkinTypeParser.FromString(availableSkins[i].type) == type) c++;
            }
            return c;
        }



        // ── Textures ──────────────────────────────────────────────────────────
        private static Texture2D _hoverTex;
        private static Texture2D _bgTex;
        private GameObject _bgHoverGo;

        private static Texture2D LoadTex(string resource, ref Texture2D cache)
        {
            if (cache != null) return cache;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream(resource);
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(bytes);
                tex.wrapMode = TextureWrapMode.Clamp;
                cache = tex;
            }
            catch (Exception ex) { Plugin.Log.LogError("BetterFG: Tex load failed: " + ex.Message); }
            return cache;
        }

        protected override void OnTitleHoverChanged(bool hovering)
        {
            if (_bgHoverGo != null) _bgHoverGo.SetActive(hovering);
        }

        // ── Colors ────────────────────────────────────────────────────────────
        private static readonly Color BTN_FETCH = new Color(0.25f, 0.45f, 0.25f, 1f);
        private static readonly Color BTN_IMPORT = new Color(0.25f, 0.35f, 0.45f, 1f);
        private static readonly Color BTN_APPLY = new Color(0.45f, 0.35f, 0.25f, 1f);
        private static readonly Color BTN_REMOVE = new Color(0.55f, 0.15f, 0.15f, 1f);
        private static readonly Color BTN_DARK = Color.black;
        private static readonly Color BTN_SEL = new Color(0.28f, 0.28f, 0.28f, 1f);
        private static readonly Color BTN_FILTER_ACTIVE = new Color(0.1f, 0.32f, 0.1f, 1f);
        private static readonly Color ITEM_BG = new Color(0f, 0f, 0f, 0f);
        private static readonly Color WHITE = Color.white;
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.45f);
        private static readonly Color GOLD = new Color(1f, 0.8f, 0f, 1f);
        private static readonly Color GREEN = new Color(0f, 1f, 0f, 1f);
        private static readonly Color CYAN = new Color(0f, 0.8f, 1f, 1f);
        private static readonly Color ORANGE = new Color(1f, 0.55f, 0.1f, 1f);
        private static readonly Color YELLOW = new Color(1f, 1f, 0f, 1f);

        // ── Layout ────────────────────────────────────────────────────────────
        private static float PAD => UIScale.PAD;
        private static float VPAD => UIScale.VPAD;
        private static float LH => UIScale.LH;
        private static float SH => UIScale.SH;
        private static float BTN_H => UIScale.BTN_H;
        private static float ROW_H => UIScale.ROW_H;
        private static float COVER_W => UIScale.COVER_W;
        private static float COVER_H => UIScale.COVER_H;
        private static float SEL_W => UIScale.SEL_W;
        private static int FS => UIScale.FS;
        private static int FS_SM => UIScale.FS_SM;

        // ── UGUI refs ─────────────────────────────────────────────────────────
        private RectTransform _scrollContent;
        private RectTransform _scrollViewRt;      // scroll view root, moved/grown when the filter bar hides
        private RectTransform _filterDivider2Rt;  // divider between filter bar and search, hidden with the bar
        private Rect _searchRectNormal;           // search field rect in normal (filter-bar-visible) mode
        private Rect _scrollRectNormal;           // scroll view rect in normal mode
        private float _filterCollapse;            // vertical space to reclaim when the filter bar is hidden
        private float _rowIndent;                 // left space rows reclaim to reach the dropdown edge; re-added as text indent
        private Text _searchText;
        private Text _searchPlaceholder;
        private RectTransform _searchFieldRt;
        private bool _searchActive;
        private string _searchQuery = "";
        private Dictionary<string, bool> _groupExpanded = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // filter bar buttons
        private Button _btnCostumes, _btnAccessories, _btnItems, _btnPlinths, _btnEmotes;

        private Dictionary<string, Image> _coverImages = new Dictionary<string, Image>();

        // per-row togglable visuals, keyed by skin index, so selecting/deselecting can repaint
        // just the affected rows in place instead of rebuilding the whole list (the freeze)
        private class RowVisual
        {
            public GameObject gradient;   // selection gradient overlay (toggled active)
            public GameObject configBtn;  // item "Configure" button (toggled active)
            public Text selectLabel;      // Select button label (recoloured)
            public Color selColor;        // this row's accent colour
        }
        private Dictionary<int, RowVisual> _rowVisuals = new Dictionary<int, RowVisual>();

        // ── State ─────────────────────────────────────────────────────────────
        private List<SkinInfo> availableSkins = new List<SkinInfo>();
        private Dictionary<string, RawImage> _repoCoverImages = new Dictionary<string, RawImage>();
        private Dictionary<string, Texture2D> skinCovers = new Dictionary<string, Texture2D>();

        // ── Services ──────────────────────────────────────────────────────────
        private SkinCatalogService catalogService;
        private SkinLoaderService loaderService;
        private SkinApplicationService applicationService;
        private RepoRegistry repoRegistry;
        private MenuCustomizationApplication _plinthApp;

        // ── Repo dropdown ─────────────────────────────────────────────────────
        private RectTransform _repoRowsParent;
        private bool _repoDropdownOpen = false;
        private string _repoSearchQuery = "";     // filters the open repo list
        private InputField _repoSearchField;      // real search bar shown in place of the header while open
        private bool _fakeInputLocked = false;
        private GameObject _belowRepos; // everything under the repo section, hidden while the dropdown is open

        private List<SkinInfo> _pendingApplyQueue = new List<SkinInfo>();
        private int _pendingTotal = 0;

        // files that failed to match during restore — retried on each OnSkinsLoaded
        private List<(string file, string repo)> _pendingRestoreFiles = new List<(string, string)>();

        // ── Lifecycle ─────────────────────────────────────────────────────────
        private void Awake()
        {
            BindServices();
            LoadImportedPaths();
            SeedImportedSkins();

            // if catalog already has data (tab was swapped back in), seed immediately
            if (catalogService != null)
            {
                var existing = catalogService.AvailableSkins;
                if (existing != null && existing.Count > 0)
                {
                    availableSkins = existing;
                    _restoredOnce = true;
                    // restore cached covers so thumbnails don't go blank
                    foreach (var skin in availableSkins)
                        if (catalogService.TryGetCover(skin, out var tex) && tex != null)
                            skinCovers[CoverKey(skin)] = tex;
                }
            }
        }

        private void SeedImportedSkins()
        {
            foreach (string folder in _importedPaths)
            {
                if (availableSkins.FindIndex(s => s.isLocalImport && !string.IsNullOrEmpty(s.localPath) &&
                    string.Equals(Path.GetDirectoryName(s.localPath), folder, StringComparison.OrdinalIgnoreCase)) >= 0)
                    continue;
                var skin = LoadImportedFromFolder(folder);
                if (skin != null) availableSkins.Add(skin);
            }
        }

        // builds a lightweight SkinInfo from a persisted import folder's info.json (no bundle load)
        private SkinInfo LoadImportedFromFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
            string infoPath = Path.Combine(folder, "info.json");
            if (!File.Exists(infoPath)) return null;
            try
            {
                string json = File.ReadAllText(infoPath);
                string name = GetJsonValueSimple(json, "name");
                string file = GetJsonValueSimple(json, "file");
                string type = GetJsonValueSimple(json, "type");
                string author = GetJsonValueSimple(json, "author");
                string group = GetJsonValueSimple(json, "group");
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(file)) return null;
                string bundlePath = Path.Combine(folder, file);
                if (!File.Exists(bundlePath)) return null;
                return new SkinInfo
                {
                    name = name, file = file, type = type, author = author, group = group,
                    isLocalImport = true, localPath = bundlePath, sourceRepo = folder,
                };
            }
            catch { return null; }
        }

        private static string GetJsonValueSimple(string json, string key)
        {
            string sk = $"\"{key}\":";
            int i = json.IndexOf(sk);
            if (i == -1) return "";
            i += sk.Length;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            if (i >= json.Length || json[i] != '"') return "";
            i++;
            int end = json.IndexOf('"', i);
            return end == -1 ? "" : json.Substring(i, end - i);
        }

        private SkinRepo _lastFetchedRepo;

        private void FetchActiveRepo()
        {
            if (catalogService == null) return;
            var active = repoRegistry?.Active;
            if (active == null) return;
            catalogService.FetchSkins(active);
        }

        private void OnReposChanged()
        {
            RefreshRepoRows();
            FetchActiveRepo();
            RefreshSkinList();
        }

        private void OnDestroy()
        {
            SetFakeInputLock(false);

            if (repoRegistry != null)
            {
                repoRegistry.OnReposChanged -= OnReposChanged;
                repoRegistry.OnValidationStatus -= SetStatus;
                repoRegistry.OnCoverLoaded -= OnRepoCoverLoaded;
                repoRegistry.OnFeaturedLoaded -= OnFeaturedLoaded;
            }
            if (catalogService != null)
            {
                catalogService.OnSkinsLoaded -= OnSkinsLoaded;
                catalogService.OnFetchCompleted -= OnFetchCompleted;
                catalogService.OnStatusUpdate -= SetStatus;
                catalogService.OnSkinCoverLoaded -= OnSkinCoverLoaded;
            }
            if (loaderService != null)
            {
                loaderService.OnSkinLoaded -= OnSkinDownloaded;
                loaderService.OnSkinImported -= OnSkinImported;
            }
        }

        private void Update()
        {
            WinDialogs.Tick();

            SetFakeInputLock(_searchActive);
            if (!_searchActive) return;

            if (Input.GetMouseButtonDown(0))
            {
                var mousePos = new Vector2(Input.mousePosition.x, Input.mousePosition.y);
                if (_searchFieldRt != null &&
                    !RectTransformUtility.RectangleContainsScreenPoint(_searchFieldRt, mousePos, null))
                {
                    _searchActive = false;
                    UpdateSearchCaret();
                }
                return;
            }

            foreach (char c in Input.inputString)
            {
                if (c == '\b')
                { if (_searchQuery.Length > 0) { _searchQuery = _searchQuery.Substring(0, _searchQuery.Length - 1); RefreshSkinList(); } }
                else if (c == '\n' || c == '\r' || c == '\x1b') { _searchActive = false; }
                else { _searchQuery += c; RefreshSkinList(); }
                UpdateSearchCaret();
            }
        }

        private void UpdateSearchCaret()
        {
            if (_searchText == null) return;
            bool empty = string.IsNullOrEmpty(_searchQuery);
            _searchText.text = empty && !_searchActive ? "" : _searchQuery + (_searchActive ? "|" : "");
            if (_searchPlaceholder != null)
                _searchPlaceholder.color = empty && !_searchActive
                    ? new Color(1f, 1f, 1f, 0.2f) : new Color(1f, 1f, 1f, 0f);
        }

        private void SetFakeInputLock(bool active)
        {
            if (_fakeInputLocked == active) return;
            _fakeInputLocked = active;
            BetterFG.Services.FGInputLockService.SetFakeFieldLock(active);
        }

        // ── Build ─────────────────────────────────────────────────────────────

        protected override void BuildBackground(RectTransform root)
        {
            var bgTex = LoadTex("BetterFG.assets.ui.cskins.bg.png", ref _bgTex);
            if (bgTex == null) return;

            var bgGo = new GameObject("BG");
            bgGo.transform.SetParent(root, false);
            var bgRt = bgGo.AddComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
            bgRt.localScale = new Vector3(1.5015f, 1.3502f, 1f);
            bgRt.localPosition = new Vector3(267.7578f, 285.8921f, 0);
            var raw = bgGo.AddComponent<RawImage>();
            raw.texture = bgTex;
            raw.raycastTarget = false;

            var hoverTex = LoadTex("BetterFG.assets.ui.bg_hover.png", ref _hoverTex);
            if (hoverTex != null)
            {
                var hoverGo = new GameObject("BG_Hover");
                hoverGo.transform.SetParent(bgGo.transform, false);
                var hoverRt = hoverGo.AddComponent<RectTransform>();
                hoverRt.anchorMin = Vector2.zero;
                hoverRt.anchorMax = Vector2.one;
                hoverRt.offsetMin = hoverRt.offsetMax = Vector2.zero;
                hoverGo.AddComponent<RawImage>().texture = hoverTex;
                hoverGo.SetActive(false);
                _bgHoverGo = hoverGo;
            }
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            // ── Repo section ───────────────────────────────────────────────────
            y = BuildRepoSection(contentRoot, y, w);
            UGUIShip.CreatePanel(contentRoot, PR(y, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            y += 1f + SH;

            // everything under the repo section lives in one container so it can be hidden wholesale
            // while the repo dropdown is open (the dropdown expands to fill this whole area)
            _belowRepos = new GameObject("BelowRepos");
            _belowRepos.transform.SetParent(contentRoot, false);
            var belowRt = _belowRepos.AddComponent<RectTransform>();
            belowRt.anchorMin = Vector2.zero; belowRt.anchorMax = Vector2.one;
            belowRt.offsetMin = belowRt.offsetMax = Vector2.zero;

            // ── Filter bar: Costumes | Accessories | Items ─────────────────────
            BuildFilterBar(belowRt, y, w);
            y += BTN_H + SH;

            _filterDivider2Rt = UGUIShip.CreatePanel(belowRt, PR(y, w, 1f), new Color(1f, 1f, 1f, 0.06f));
            y += 1f + SH;

            // filter bar block (bar + its spacing + this divider + spacing) is reclaimed when hidden
            _filterCollapse = BTN_H + 1f + 2f * SH;

            _searchRectNormal = new Rect(PAD, y, w, LH);
            BuildSearchField(belowRt, y, w);
            y += LH + SH;

            const float BOTTOM_PAD = 6f;
            float scrollH = TabHeight - y - BTN_H - SH - VPAD - BOTTOM_PAD;
            _scrollRectNormal = new Rect(PAD, y, w, scrollH);
            BuildScrollView(belowRt, y, w, scrollH);
            y += scrollH + SH;

            float singleW = (w - 3f * (PAD * 0.5f)) / 4f;
            float gap = PAD * 0.5f;
            float bx = PAD;

            UGUIShip.CreateButton(belowRt, new Rect(bx, y, singleW, BTN_H), "Fetch", BTN_FETCH, WHITE, FS, new Action(OnFetch)); bx += singleW + gap;
            UGUIShip.CreateButton(belowRt, new Rect(bx, y, singleW, BTN_H), "Import", BTN_IMPORT, WHITE, FS, new Action(OnImport)); bx += singleW + gap;
            UGUIShip.CreateButton(belowRt, new Rect(bx, y, singleW, BTN_H), "Apply", BTN_APPLY, WHITE, FS, new Action(OnApply)); bx += singleW + gap;
            UGUIShip.CreateButton(belowRt, new Rect(bx, y, singleW, BTN_H), "Remove All", BTN_REMOVE, WHITE, FS, new Action(OnRemoveAll));

            // now that _belowRepos exists, populate the repo rows (RefreshRepoRows toggles it)
            RefreshRepoRows();
        }

        // ── Repo section builder ──────────────────────────────────────────────

        private float BuildRepoSection(RectTransform parent, float y, float w)
        {
            // Repo rows container -- shows active repo row by default
            // "+ Add" and extra rows become visible when dropdown is toggled

            // reserve one row height for the active repo
            // rows container created below

            // repo rows container -- height grows with rows
            var rowsGo = new GameObject("RepoRows");
            rowsGo.transform.SetParent(parent, false);
            _repoRowsParent = rowsGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(_repoRowsParent, new Rect(PAD, y, w, 0f)); // height set by RefreshRepoRows
            var vlg = rowsGo.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = 2f;
            rowsGo.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // estimate height for layout: one row visible when closed
            float estimatedH = BTN_H + 2f;
            y += estimatedH + SH;

            return y;
        }

        private void RefreshRepoRows()
        {
            if (_repoRowsParent == null) return;
            for (int i = _repoRowsParent.childCount - 1; i >= 0; i--)
                Destroy(_repoRowsParent.GetChild(i).gameObject);
            _repoCoverImages.Clear();
            _repoScrollContent = null;

            if (repoRegistry == null) return;

            var activeRepo = repoRegistry.Active;

            // Active row (always visible)
            var activeGo = new GameObject("RepoActiveRow");
            activeGo.transform.SetParent(_repoRowsParent, false);
            var activeRt = activeGo.AddComponent<RectTransform>();
            activeRt.sizeDelta = new Vector2(0f, BTN_H);
            var activeLE = activeGo.AddComponent<LayoutElement>();
            activeLE.preferredHeight = BTN_H;

            var aHlg = activeGo.AddComponent<HorizontalLayoutGroup>();
            aHlg.childForceExpandHeight = true;
            aHlg.childForceExpandWidth = false;
            aHlg.spacing = 3f;
            aHlg.padding = new RectOffset(0, 0, 0, 0);

            if (_repoDropdownOpen)
            {
                // while open, the header is a live search bar that filters the list below
                _repoSearchField = UGUIShip.CreateInputField(activeGo.transform, new Rect(0f, 0f, 100f, BTN_H),
                    "search repositories...", new Color(0f, 0f, 0f, 0.4f), WHITE, FS_SM);
                _repoSearchField.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
                UGUIShip.SetInputText(_repoSearchField, _repoSearchQuery, false);
                _repoSearchField.onValueChanged.AddListener(new Action<string>(val =>
                {
                    _repoSearchQuery = val ?? "";
                    if (_repoScrollContent != null)
                    {
                        PopulateRepoEntries(_repoScrollContent);
                        LayoutRebuilder.ForceRebuildLayoutImmediate(_repoScrollContent);
                    }
                }));
            }
            else
            {
                // Create left button via UGUIShip so shine + hover works, and left-align label
                string activeLabelText = _featuredSelected
                    ? "Featured Repositories (selected)"
                    : _importedRepoSelected
                        ? "Imported Skins (selected)"
                        : activeRepo != null ? activeRepo.DisplayName + " (selected)" : "No repository";
                Color activeLabelColor = _featuredSelected ? GOLD
                    : _importedRepoSelected ? ORANGE
                    : activeRepo != null ? YELLOW : HINT;
                var nameBtn = UGUIShip.CreateButton(activeGo.transform, activeLabelText,
                    BTN_DARK, activeLabelColor, FS_SM, new Action(() => { _repoDropdownOpen = true; _repoSearchQuery = ""; RefreshRepoRows(); }));
                var nameLbl = nameBtn.transform.Find("Label")?.GetComponent<Text>();
                if (nameLbl != null)
                {
                    nameLbl.alignment = TextAnchor.MiddleLeft;
                    nameLbl.color = activeLabelColor;
                    var nameLblRt = nameLbl.GetComponent<RectTransform>();
                    if (nameLblRt != null) nameLblRt.offsetMin = new Vector2(14f, nameLblRt.offsetMin.y);
                }
                var nameLE = nameBtn.gameObject.GetComponent<LayoutElement>() ?? nameBtn.gameObject.AddComponent<LayoutElement>();
                nameLE.flexibleWidth = 1f;

                // cover stretches behind the name button only (not shown for imported repo)
                var activeCoverGo = new GameObject("RepoCover");
                activeCoverGo.transform.SetParent(nameBtn.transform, false);
                activeCoverGo.transform.SetAsFirstSibling();
                var activeCoverRt = activeCoverGo.AddComponent<RectTransform>();
                activeCoverRt.anchorMin = Vector2.zero;
                activeCoverRt.anchorMax = Vector2.one;
                activeCoverRt.offsetMin = activeCoverRt.offsetMax = Vector2.zero;
                var activeCoverImg = activeCoverGo.AddComponent<RawImage>();
                activeCoverImg.color = new Color(1f, 1f, 1f, 0f);
                activeCoverImg.raycastTarget = false;
                if (!_importedRepoSelected && !_featuredSelected && activeRepo != null)
                {
                    _repoCoverImages[activeRepo.githubUrl] = activeCoverImg;
                    var cachedTex = repoRegistry.GetCover(activeRepo);
                    if (cachedTex != null) { activeCoverImg.texture = cachedTex; activeCoverImg.color = Color.white; }
                    else repoRegistry.FetchCover(activeRepo);
                }
            }

            // caret button on right (small)
            var caretBtn = UGUIShip.CreateButton(activeGo.transform, _repoDropdownOpen ? "▴" : "▾", BTN_DARK, CYAN, FS_SM, new Action(() => { _repoDropdownOpen = !_repoDropdownOpen; _repoSearchQuery = ""; RefreshRepoRows(); }));
            caretBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = BTN_H;

            // if dropdown open: pinned entries stay fixed at the top, only the added repos scroll
            if (_repoDropdownOpen)
            {
                // pinned system entries, fixed order, never scroll: default repo, Imported, Featured
                SkinRepo defaultRepo = null;
                foreach (var repo in repoRegistry.Repos)
                    if (repo.isDefault) { defaultRepo = repo; break; }
                if (defaultRepo != null) BuildRepoEntryRow(_repoRowsParent, defaultRepo);
                BuildImportedEntryRow(_repoRowsParent);
                BuildFeaturedEntryRow(_repoRowsParent);

                bool anyAdded = false;
                foreach (var repo in repoRegistry.Repos)
                    if (!repo.isDefault) { anyAdded = true; break; }

                if (anyAdded)
                {
                    // fixed section header — sits tight above the scroll, aligned to the row's left edge
                    var secGo = new GameObject("RepoSectionLabel");
                    secGo.transform.SetParent(_repoRowsParent, false);
                    secGo.AddComponent<RectTransform>();
                    secGo.AddComponent<LayoutElement>().preferredHeight = FS + 18f;
                    var secLbl = UGUIShip.CreateStretchLabel(secGo.transform, "Added repositories", FS, WHITE);
                    secLbl.alignment = TextAnchor.LowerLeft;
                    var secLblRt = secLbl.GetComponent<RectTransform>();
                    secLblRt.offsetMin = new Vector2(14f, secLblRt.offsetMin.y);

                    // the one scrollable area — fills what's left under the fixed rows and above the Add button
                    float viewportH = TabHeight - VPAD * 2f - BTN_H * 5f - (FS + 18f) - 30f;
                    if (viewportH < BTN_H * 2f) viewportH = BTN_H * 2f;

                    var scroll = UGUIShip.CreateScrollView(_repoRowsParent, new Rect(0f, 0f, 100f, viewportH));
                    scroll.scrollRect.scrollSensitivity = 24f;
                    scroll.scrollRect.gameObject.AddComponent<LayoutElement>().preferredHeight = viewportH;
                    // drop the viewport's left inset so the added rows line up with the label/pinned rows above
                    var vp = scroll.scrollRect.viewport;
                    if (vp != null) vp.offsetMin = new Vector2(0f, vp.offsetMin.y);

                    var cVlg = scroll.content.gameObject.AddComponent<VerticalLayoutGroup>();
                    cVlg.childControlWidth = true;
                    cVlg.childControlHeight = true;
                    cVlg.childForceExpandWidth = true;
                    cVlg.childForceExpandHeight = false;
                    cVlg.spacing = 2f;
                    scroll.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                    _repoScrollContent = scroll.content;
                    PopulateRepoEntries(scroll.content);
                }

                // Add button row — outside the scroll, always visible
                var addRow = new GameObject("RepoAddRow");
                addRow.transform.SetParent(_repoRowsParent, false);
                addRow.AddComponent<RectTransform>();
                var addLE = addRow.AddComponent<LayoutElement>();
                addLE.preferredHeight = BTN_H;
                var addHlg = addRow.AddComponent<HorizontalLayoutGroup>();
                addHlg.childForceExpandHeight = true;
                addHlg.childForceExpandWidth = false;
                addHlg.spacing = 3f;
                addHlg.padding = new RectOffset(0, 0, 0, 0);

                var addBtn = UGUIShip.CreateButton(addRow.transform, "Add Repositories",
                    BTN_DARK, CYAN, FS_SM, new Action(OnAddRepo));
                var addBtnLE = addBtn.gameObject.AddComponent<LayoutElement>();
                addBtnLE.preferredWidth = 180f;
                addBtnLE.preferredHeight = BTN_H;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_repoRowsParent);
            // the parent rebuild above gives the scroll host its real width; now rebuild
            // the scroll content so its rows stretch to that width (otherwise width=0 = invisible)
            if (_repoScrollContent != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(_repoScrollContent);

            // hide everything below the repo section while the dropdown is open (it expands to fill
            // that area), and bring the dropdown to front so it isn't occluded
            if (_repoDropdownOpen)
            {
                _belowRepos.SetActive(false);
                _repoRowsParent.SetAsLastSibling();
            }
            else
            {
                _belowRepos.SetActive(true);
                _repoRowsParent.SetAsFirstSibling();
            }
        }

        // fills the open dropdown's scroll content: default repo, Imported, Featured pinned at the top in
        // fills the scrollable area with just the user-added repos, filtered by the search query. the
        // pinned entries + section label are built as fixed rows outside the scroll, so typing only
        // re-lists this content and never tears down the search field itself
        private void PopulateRepoEntries(RectTransform contentRt)
        {
            for (int i = contentRt.childCount - 1; i >= 0; i--)
                Destroy(contentRt.GetChild(i).gameObject);

            string q = (_repoSearchQuery ?? "").Trim().ToLower();
            foreach (var repo in repoRegistry.Repos)
            {
                if (repo.isDefault) continue;
                if (q.Length > 0 && !repo.DisplayName.ToLower().Contains(q)) continue;
                BuildRepoEntryRow(contentRt, repo);
            }
        }

        // strip the hover EventTrigger that UGUIShip adds — it implements drag handlers and
        // swallows scroll-drag, blocking the parent ScrollRect (same fix as SkinTextureCostumeWindow)
        private static void MakeButtonScrollable(Button btn)
        {
            if (btn == null) return;
            btn.transition = Selectable.Transition.None;
            var trigger = btn.GetComponent<EventTrigger>();
            if (trigger != null) Destroy(trigger);
        }

        // flip an L/R hand button between on (cyan/black text) and off (dark/white text) in place,
        // without rebuilding the list. mirrors the colours used when the button is first created.
        private static void RecolorHandButton(Button btn, bool on)
        {
            if (btn == null) return;
            Color bg = on ? CYAN : BTN_DARK;
            var img = btn.GetComponent<Image>();
            if (img != null) img.color = bg;
            var cols = btn.colors;
            cols.normalColor = bg;
            cols.highlightedColor = bg;
            cols.pressedColor = bg;
            cols.selectedColor = bg;
            cols.fadeDuration = 0f;
            btn.colors = cols;
            var label = btn.transform.Find("Label")?.GetComponent<Text>();
            if (label != null) label.color = on ? Color.black : WHITE;
        }

        // one selectable repo row inside the dropdown scroll content
        private void BuildRepoEntryRow(RectTransform parent, SkinRepo repo)
        {
            var rowGo = new GameObject("RepoRow_" + repo.repoName);
            rowGo.transform.SetParent(parent, false);
            rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, BTN_H);
            rowGo.AddComponent<LayoutElement>().preferredHeight = BTN_H;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.spacing = 3f;
            hlg.padding = new RectOffset(0, 0, 0, 0);

            var capturedRepo = repo;

            var selBtnComp = UGUIShip.CreateButton(rowGo.transform, capturedRepo.DisplayName, BTN_DARK, WHITE, FS_SM, new Action(() => { _importedRepoSelected = false; _featuredSelected = false; repoRegistry.SetActive(capturedRepo); _repoDropdownOpen = false; RefreshRepoRows(); RefreshSkinList(); }));
            MakeButtonScrollable(selBtnComp);
            var selLbl = selBtnComp.transform.Find("Label")?.GetComponent<Text>();
            if (selLbl != null)
            {
                selLbl.alignment = TextAnchor.MiddleLeft;
                var selLblRt = selLbl.GetComponent<RectTransform>();
                if (selLblRt != null) selLblRt.offsetMin = new Vector2(14f, selLblRt.offsetMin.y);
            }
            selBtnComp.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var coverGo = new GameObject("RepoCover");
            coverGo.transform.SetParent(selBtnComp.transform, false);
            coverGo.transform.SetAsFirstSibling();
            var coverRt = coverGo.AddComponent<RectTransform>();
            coverRt.anchorMin = Vector2.zero;
            coverRt.anchorMax = Vector2.one;
            coverRt.offsetMin = coverRt.offsetMax = Vector2.zero;
            var coverImg = coverGo.AddComponent<RawImage>();
            coverImg.color = new Color(1f, 1f, 1f, 0f);
            coverImg.raycastTarget = false;
            _repoCoverImages[capturedRepo.githubUrl] = coverImg;
            var cachedCoverTex = repoRegistry.GetCover(capturedRepo);
            if (cachedCoverTex != null) { coverImg.texture = cachedCoverTex; coverImg.color = Color.white; }
            else repoRegistry.FetchCover(capturedRepo);

            if (repo.isDefault)
            {
                var star = UGUIShip.CreateStretchLabel(rowGo.transform, "★", FS_SM, GOLD);
                var le = star.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = BTN_H; le.minWidth = BTN_H; le.flexibleWidth = 0f;
            }
            else
            {
                var removeBtn = UGUIShip.CreateButton(rowGo.transform, "−",
                    BTN_REMOVE, WHITE, FS_SM, new Action(() => { repoRegistry.RemoveRepo(capturedRepo); RefreshRepoRows(); }));
                MakeButtonScrollable(removeBtn);
                var le = removeBtn.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = BTN_H; le.minWidth = BTN_H; le.flexibleWidth = 0f;
            }
        }

        // the permanent "Featured Repos" entry inside the dropdown scroll content
        private void BuildFeaturedEntryRow(RectTransform parent)
        {
            var rowGo = new GameObject("RepoRow_Featured");
            rowGo.transform.SetParent(parent, false);
            rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, BTN_H);
            rowGo.AddComponent<LayoutElement>().preferredHeight = BTN_H;

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.spacing = 3f;
            hlg.padding = new RectOffset(0, 0, 0, 0);

            var btn = UGUIShip.CreateButton(rowGo.transform, "Featured Repositories", BTN_DARK, GOLD, FS_SM, new Action(() =>
            {
                _featuredSelected = true;
                _importedRepoSelected = false;
                _repoDropdownOpen = false;
                repoRegistry?.FetchFeatured();
                RefreshRepoRows();
                RefreshSkinList();
            }));
            MakeButtonScrollable(btn);
            var lbl = btn.transform.Find("Label")?.GetComponent<Text>();
            if (lbl != null)
            {
                lbl.alignment = TextAnchor.MiddleLeft;
                var lblRt = lbl.GetComponent<RectTransform>();
                if (lblRt != null) lblRt.offsetMin = new Vector2(14f, lblRt.offsetMin.y);
            }
            btn.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var star = UGUIShip.CreateStretchLabel(rowGo.transform, "★", FS_SM, GOLD);
            var starLE = star.gameObject.AddComponent<LayoutElement>();
            starLE.preferredWidth = BTN_H; starLE.minWidth = BTN_H; starLE.flexibleWidth = 0f;
        }

        // the permanent "Imported Skins" entry inside the dropdown scroll content
        private void BuildImportedEntryRow(RectTransform parent)
        {
            var impRowGo = new GameObject("RepoRow_Imported");
            impRowGo.transform.SetParent(parent, false);
            impRowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, BTN_H);
            impRowGo.AddComponent<LayoutElement>().preferredHeight = BTN_H;

            var impHlg = impRowGo.AddComponent<HorizontalLayoutGroup>();
            impHlg.childForceExpandHeight = true;
            impHlg.childForceExpandWidth = false;
            impHlg.spacing = 3f;
            impHlg.padding = new RectOffset(0, 0, 0, 0);

            var impBtn = UGUIShip.CreateButton(impRowGo.transform, "Imported Skins", BTN_DARK, ORANGE, FS_SM, new Action(() =>
            {
                _importedRepoSelected = true;
                _featuredSelected = false;
                _repoDropdownOpen = false;
                RefreshRepoRows();
                RefreshSkinList();
            }));
            MakeButtonScrollable(impBtn);
            var impLbl = impBtn.transform.Find("Label")?.GetComponent<Text>();
            if (impLbl != null)
            {
                impLbl.alignment = TextAnchor.MiddleLeft;
                var impLblRt = impLbl.GetComponent<RectTransform>();
                if (impLblRt != null) impLblRt.offsetMin = new Vector2(14f, impLblRt.offsetMin.y);
            }
            impBtn.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            var lockLbl = UGUIShip.CreateStretchLabel(impRowGo.transform, "★", FS_SM, ORANGE);
            var lockLE = lockLbl.gameObject.AddComponent<LayoutElement>();
            lockLE.preferredWidth = BTN_H; lockLE.minWidth = BTN_H; lockLE.flexibleWidth = 0f;
        }

        private void OnAddRepo()
        {
            if (_repoRowsParent == null) return;
            if (_repoRowsParent.Find("RepoInputRow") != null) return;

            // swap the field in where the Add button sits (bottom) so there's no jump back to the top
            var addRow = _repoRowsParent.Find("RepoAddRow");
            int addIdx = addRow != null ? addRow.GetSiblingIndex() : _repoRowsParent.childCount;
            if (addRow != null) addRow.gameObject.SetActive(false);

            var rowGo = new GameObject("RepoInputRow");
            rowGo.transform.SetParent(_repoRowsParent, false);
            rowGo.transform.SetSiblingIndex(addIdx);
            rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, BTN_H);
            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.spacing = 3f;
            rowGo.AddComponent<LayoutElement>().preferredHeight = BTN_H;

            var field = UGUIShip.CreateInputField(rowGo.transform, new Rect(0f, 0f, 100f, BTN_H),
                "https://github.com/author/repo", null, CYAN, FS_SM);
            field.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

            Action commit = () =>
            {
                string url = (field.text ?? "").Trim();
                if (addRow != null) addRow.gameObject.SetActive(true);
                Destroy(rowGo);
                if (!string.IsNullOrEmpty(url))
                    repoRegistry?.AddRepo(url);
            };

            var ok = UGUIShip.CreateButton(rowGo.transform, "OK",
                new Color(0.15f, 0.3f, 0.15f, 1f), GREEN, FS_SM, commit);
            ok.gameObject.AddComponent<LayoutElement>().preferredWidth = BTN_H * 1.5f;

            var cancel = UGUIShip.CreateButton(rowGo.transform, "✕",
                BTN_DARK, HINT, FS_SM, new Action(() =>
                {
                    if (addRow != null) addRow.gameObject.SetActive(true);
                    Destroy(rowGo);
                }));
            cancel.gameObject.AddComponent<LayoutElement>().preferredWidth = BTN_H;

            LayoutRebuilder.ForceRebuildLayoutImmediate(_repoRowsParent);
            field.ActivateInputField();
        }

        private void SaveImportedPaths()
        {
            SettingsService.Set(KEY_IMPORTED_PATHS, string.Join("|", _importedPaths));
        }

        private void LoadImportedPaths()
        {
            _importedPaths.Clear();
            string raw = SettingsService.Get(KEY_IMPORTED_PATHS, "");
            if (string.IsNullOrEmpty(raw)) return;
            foreach (string p in raw.Split('|'))
            {
                string trimmed = p.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    _importedPaths.Add(trimmed);
            }
        }

        private void BuildFilterBar(RectTransform parent, float y, float w)
        {
            float gap = PAD * 0.5f;
            float btnW = (w - gap * 4f) / 5f;
            float bx = PAD;

            _btnCostumes = UGUIShip.CreateButton(parent, new Rect(bx, y, btnW, BTN_H), "Costumes", GetFilterBg(SkinType.Costume), WHITE, FS_SM, new Action(() => SetFilter(SkinType.Costume))); bx += btnW + gap;
            _btnAccessories = UGUIShip.CreateButton(parent, new Rect(bx, y, btnW, BTN_H), "Accessories", GetFilterBg(SkinType.Accessory), WHITE, FS_SM, new Action(() => SetFilter(SkinType.Accessory))); bx += btnW + gap;
            _btnItems = UGUIShip.CreateButton(parent, new Rect(bx, y, btnW, BTN_H), "Items", GetFilterBg(SkinType.Item), WHITE, FS_SM, new Action(() => SetFilter(SkinType.Item))); bx += btnW + gap;
            _btnPlinths = UGUIShip.CreateButton(parent, new Rect(bx, y, btnW, BTN_H), "Plinths", GetFilterBg(SkinType.Plinth), WHITE, FS_SM, new Action(() => SetFilter(SkinType.Plinth))); bx += btnW + gap;
            _btnEmotes = UGUIShip.CreateButton(parent, new Rect(bx, y, btnW, BTN_H), "Emotes", GetFilterBg(SkinType.Emote), WHITE, FS_SM, new Action(() => SetFilter(SkinType.Emote)));
        }

        private void SetFilter(SkinType type)
        {
            _activeFilter = type;
            RefreshFilterBar();
            RefreshSkinList();
        }

        private void RefreshFilterBar()
        {
            UGUIShip.SetButtonSelected(_btnCostumes, _activeFilter == SkinType.Costume, BTN_FILTER_ACTIVE);
            UGUIShip.SetButtonSelected(_btnAccessories, _activeFilter == SkinType.Accessory, BTN_FILTER_ACTIVE);
            UGUIShip.SetButtonSelected(_btnItems, _activeFilter == SkinType.Item, BTN_FILTER_ACTIVE);
            UGUIShip.SetButtonSelected(_btnPlinths, _activeFilter == SkinType.Plinth, BTN_FILTER_ACTIVE);
            UGUIShip.SetButtonSelected(_btnEmotes, _activeFilter == SkinType.Emote, BTN_FILTER_ACTIVE);
        }

        private Color GetFilterBg(SkinType type) =>
            _activeFilter == type ? BTN_FILTER_ACTIVE : BTN_DARK;

        // ── Services ──────────────────────────────────────────────────────────

        private void BindServices()
        {
            repoRegistry = CustomizationServices.RepoRegistry;
            catalogService = CustomizationServices.CatalogService;
            applicationService = CustomizationServices.ApplicationService;
            loaderService = CustomizationServices.LoaderService;
            _plinthApp = CustomizationServices.PlinthApp;

            if (repoRegistry != null)
            {
                repoRegistry.OnReposChanged += OnReposChanged;
                repoRegistry.OnValidationStatus += SetStatus;
                repoRegistry.OnCoverLoaded += OnRepoCoverLoaded;
                repoRegistry.OnFeaturedLoaded += OnFeaturedLoaded;
            }
            if (catalogService != null)
            {
                catalogService.OnSkinsLoaded += OnSkinsLoaded;
                catalogService.OnFetchCompleted += OnFetchCompleted;
                catalogService.OnStatusUpdate += SetStatus;
                catalogService.OnSkinCoverLoaded += OnSkinCoverLoaded;
            }
            if (applicationService != null)
            {
                applicationService.OnSkinApplied += e => SetStatus($"Applied {e.skinInfo.name} to {e.bean?.name}");
                applicationService.OnSkinRemoved += SetStatus;
            }
            if (loaderService != null)
            {
                loaderService.OnSkinLoaded += OnSkinDownloaded;
                loaderService.OnSkinImported += OnSkinImported;
                loaderService.OnProgress += SetStatus;
                loaderService.OnError += err => SetStatus("Error: " + err);
            }
            if (_plinthApp != null)
                _plinthApp.OnStatus += SetStatus;
        }

        // ── Scroll / list ─────────────────────────────────────────────────────

        private void BuildScrollView(RectTransform parent, float y, float width, float height)
        {
            var scroll = UGUIShip.CreateScrollView(parent, new Rect(PAD, y, width, height));
            var scrollRect = scroll.scrollRect;
            scrollRect.scrollSensitivity = 60f;
            _scrollViewRt = scrollRect.GetComponent<RectTransform>();
            _scrollContent = scroll.content;
            _scrollContent.pivot = new Vector2(0.5f, 1f);
            _scrollContent.sizeDelta = Vector2.zero;

            // reclaim the viewport's left inset (only the right side needs it, for the scrollbar) and
            // the content's left padding so rows can extend to the scroll view's left edge — which
            // lines up with the repo dropdown. the reclaimed width is re-added per row as text indent
            float viewportLeftInset = scrollRect.viewport != null ? scrollRect.viewport.offsetMin.x : 0f;
            if (scrollRect.viewport != null)
                scrollRect.viewport.offsetMin = new Vector2(0f, scrollRect.viewport.offsetMin.y);
            _rowIndent = viewportLeftInset + PAD;

            var vlg = _scrollContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true; // rows size to their content so multi-line descriptions grow the row down
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = PAD * 0.5f;
            vlg.padding = new RectOffset(0, (int)PAD, (int)(PAD * 0.5f), (int)(PAD * 0.5f));

            _scrollContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private void RefreshSkinList()
        {
            if (_scrollContent == null) return;
            _fetchCountLabel = null; // rebuilt below only if the empty state shows
            _coverImages.Clear();
            _featuredCoverImages.Clear();
            _rowVisuals.Clear();
            for (int i = _scrollContent.childCount - 1; i >= 0; i--)
            {
                var child = _scrollContent.GetChild(i);
                if (child != null) Destroy(child.gameObject);
            }

            // Featured Repos section: filter bar makes no sense (a repo isn't sortable by costume/item)
            SetFilterBarVisible(!_featuredSelected);
            if (_featuredSelected)
            {
                RenderFeaturedRepos();
                LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
                return;
            }

            string q = _searchQuery?.ToLower() ?? "";
            string activeRaw = repoRegistry?.Active?.RawBase;
            int display = 0;
            var shownGroups = new Dictionary<string, List<(SkinInfo skin, int index)>>(StringComparer.OrdinalIgnoreCase);
            var groupNames = new List<string>();

            for (int i = 0; i < availableSkins.Count; i++)
            {
                var s = availableSkins[i];
                if (_importedRepoSelected)
                {
                    if (!s.isLocalImport) continue;
                }
                else
                {
                    if (s.isLocalImport) continue;
                    if (!string.IsNullOrEmpty(activeRaw) && s.sourceRepo != activeRaw) continue;
                }
                if (SkinTypeParser.FromString(s.type) != _activeFilter) continue;
                if (!string.IsNullOrEmpty(q) && !s.name.ToLower().Contains(q) && !s.author.ToLower().Contains(q)) continue;
                string group = string.IsNullOrWhiteSpace(s.group) ? "Unsorted" : s.group.Trim();
                if (!shownGroups.TryGetValue(group, out var groupSkins))
                {
                    groupSkins = new List<(SkinInfo skin, int index)>();
                    shownGroups[group] = groupSkins;
                    groupNames.Add(group);
                }
                groupSkins.Add((s, i));
            }

            if (shownGroups.Count > 0)
            {
                foreach (string groupName in groupNames)
                {
                    bool expanded = !_groupExpanded.TryGetValue(groupName, out bool savedExpanded) || savedExpanded;
                    var groupGo = new GameObject("SkinGroup_" + groupName);
                    groupGo.transform.SetParent(_scrollContent, false);
                    groupGo.AddComponent<RectTransform>();
                    var groupVlg = groupGo.AddComponent<VerticalLayoutGroup>();
                    groupVlg.childForceExpandWidth = true;
                    groupVlg.childForceExpandHeight = false;
                    groupVlg.spacing = PAD * 0.5f;

                    var groupBtn = UGUIShip.CreateButton(groupGo.transform,
                        (expanded ? "▾ " : "▸ ") + groupName,
                        Color.clear, WHITE, FS_SM,
                        new Action(() => { _groupExpanded[groupName] = !expanded; RefreshSkinList(); }),
                        customSprite: false);
                    var groupBtnLE = groupBtn.gameObject.AddComponent<LayoutElement>();
                    groupBtnLE.preferredHeight = FS_SM + 6f;
                    var groupBtnColors = groupBtn.colors;
                    groupBtnColors.normalColor = Color.clear;
                    groupBtnColors.highlightedColor = new Color(1f, 1f, 1f, 0.2f);
                    groupBtnColors.pressedColor = new Color(1f, 1f, 1f, 0.2f);
                    groupBtnColors.selectedColor = Color.clear;
                    groupBtnColors.fadeDuration = 0f;
                    groupBtn.colors = groupBtnColors;
                    groupBtn.transition = Selectable.Transition.None;
                    var groupHoverGo = new GameObject("Hover");
                    groupHoverGo.transform.SetParent(groupBtn.transform, false);
                    groupHoverGo.transform.SetAsFirstSibling();
                    var groupHoverRt = groupHoverGo.AddComponent<RectTransform>();
                    groupHoverRt.anchorMin = Vector2.zero;
                    groupHoverRt.anchorMax = Vector2.one;
                    groupHoverRt.offsetMin = groupHoverRt.offsetMax = Vector2.zero;
                    var groupHoverImg = groupHoverGo.AddComponent<Image>();
                    groupHoverImg.color = Color.clear;
                    groupHoverImg.raycastTarget = false;
                    var groupBtnTrigger = groupBtn.GetComponent<EventTrigger>() ?? groupBtn.gameObject.AddComponent<EventTrigger>();
                    var groupEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                    groupEnter.callback.AddListener(new Action<BaseEventData>(_ => groupHoverImg.color = new Color(1f, 1f, 1f, 0.2f)));
                    groupBtnTrigger.triggers.Add(groupEnter);
                    var groupExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                    groupExit.callback.AddListener(new Action<BaseEventData>(_ => groupHoverImg.color = Color.clear));
                    groupBtnTrigger.triggers.Add(groupExit);
                    var groupBtnLabel = groupBtn.transform.Find("Label")?.GetComponent<Text>();
                    if (groupBtnLabel != null)
                    {
                        groupBtnLabel.alignment = TextAnchor.MiddleLeft;
                        var labelRt = groupBtnLabel.GetComponent<RectTransform>();
                        // keep the header text where it was while the row extends to the dropdown edge
                        labelRt.offsetMin = new Vector2(PAD + _rowIndent, labelRt.offsetMin.y);
                    }

                    if (expanded)
                    {
                        foreach (var shown in shownGroups[groupName])
                            CreateSkinItem(groupGo.transform, shown.skin, shown.index, display++);
                    }
                }
            }
            else
            {
                bool hasRepo = repoRegistry?.Active != null;
                string msg = !hasRepo
                    ? EMPTY_NO_REPO
                    : !string.IsNullOrEmpty(q)
                        ? string.Format(EMPTY_NO_RESULTS, q)
                        : string.Format(EMPTY_NO_TYPE, _activeFilter.ToString().ToLower());

                var emptyGo = new GameObject("EmptyState");
                emptyGo.transform.SetParent(_scrollContent, false);
                var emptyRt = emptyGo.AddComponent<RectTransform>();
                emptyRt.sizeDelta = new Vector2(0f, 120f);
                var emptyLE = emptyGo.AddComponent<LayoutElement>();
                emptyLE.preferredHeight = 120f;

                var vlg = emptyGo.AddComponent<VerticalLayoutGroup>();
                vlg.childAlignment = TextAnchor.MiddleCenter;
                vlg.childForceExpandWidth = false;
                vlg.childForceExpandHeight = false;
                vlg.spacing = 6f;

                // bean image
                var tex = LoadTex(EMPTY_BEAN_RES, ref _frightenTex);
                if (tex != null)
                {
                    var imgGo = new GameObject("BeanImg");
                    imgGo.transform.SetParent(emptyGo.transform, false);
                    var imgRt = imgGo.AddComponent<RectTransform>();
                    float beanH = 48f;
                    float beanW = tex.height > 0 ? beanH * ((float)tex.width / tex.height) : beanH;
                    imgRt.sizeDelta = new Vector2(beanW, beanH);
                    var beanLE = imgGo.AddComponent<LayoutElement>();
                    beanLE.preferredWidth = beanW;
                    beanLE.preferredHeight = beanH;
                    beanLE.flexibleWidth = 0f;
                    var raw = imgGo.AddComponent<RawImage>();
                    raw.texture = tex;
                    raw.raycastTarget = false;
                    raw.color = new Color(1f, 1f, 1f, 0.55f);
                }

                var lbl = UGUIShip.CreateFlowLabel(emptyGo.transform, msg, FS_SM, HINT);
                lbl.alignment = TextAnchor.MiddleCenter;
                var lblLE = lbl.GetComponent<LayoutElement>();
                lblLE.preferredHeight = LH;
                lblLE.flexibleWidth = 1f;

                // show how many we've actually pulled vs how many the catalog says exist, so it's
                // clear when the list is empty just because stuff is still loading in
                int repoTotal = catalogService != null ? catalogService.GetCatalogTotalForRepo(activeRaw) : 0;
                if (repoTotal > 0)
                {
                    var cnt = UGUIShip.CreateFlowLabel(emptyGo.transform,
                        $"{catalogService.GetFetchedCountForRepo(activeRaw)} / {repoTotal} fetched",
                        FS_SM - 2, new Color(HINT.r, HINT.g, HINT.b, HINT.a * 0.7f));
                    cnt.alignment = TextAnchor.MiddleCenter;
                    var cntLE = cnt.GetComponent<LayoutElement>();
                    cntLE.preferredHeight = LH;
                    cntLE.flexibleWidth = 1f;
                    _fetchCountLabel = cnt;
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(_scrollContent);
        }

        private void CreateSkinItem(Transform parent, SkinInfo skin, int index, int displayIndex)
        {
            bool isSelected = selectedIndices.Contains(index);
            SkinType type = SkinTypeParser.FromString(skin.type);

            Color selColor = type == SkinType.Costume ? GREEN
                             : type == SkinType.Accessory ? CYAN
                             : type == SkinType.Emote ? YELLOW
                             : ORANGE;
            Color gradColor = new Color(selColor.r, selColor.g, selColor.b, 0.4f);
            Color btnTxtColor = isSelected ? selColor : WHITE;

            float rowH = type == SkinType.Item ? ROW_H + (FS_SM + 4f) * 2f : ROW_H;

            GameObject rowConfigBtn = null; // set when this is an item row

            var rowGo = new GameObject("SkinItem_" + index);
            rowGo.transform.SetParent(parent, false);
            var rowRt = rowGo.AddComponent<RectTransform>();
            rowRt.sizeDelta = new Vector2(0f, rowH);
            rowGo.AddComponent<Image>().color = ITEM_BG;

            // always build the gradient; just toggle it active by selection so a select/deselect
            // doesn't need to add/remove a GameObject (which would force a list rebuild)
            var gradGo = new GameObject("SelGradient");
            gradGo.transform.SetParent(rowGo.transform, false);
            var gradRt = gradGo.AddComponent<RectTransform>();
            gradRt.anchorMin = Vector2.zero;
            gradRt.anchorMax = Vector2.one;
            gradRt.offsetMin = gradRt.offsetMax = Vector2.zero;
            gradGo.AddComponent<Image>().color = Color.white;
            var grad = gradGo.AddComponent<GradientImage>();
            grad.Vertical = true;
            grad.TopColor = new Color(gradColor.r, gradColor.g, gradColor.b, 0f);
            grad.BottomColor = gradColor;
            gradGo.SetActive(isSelected);

            var hlg = rowGo.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.spacing = PAD;
            // left padding carries the reclaimed indent so text stays put while the row extends left
            hlg.padding = new RectOffset((int)(PAD * 2f + _rowIndent), (int)(PAD * 2f), (int)(PAD * 0.5f), (int)(PAD * 0.5f));

            // Info column
            var infoGo = new GameObject("Info");
            infoGo.transform.SetParent(rowGo.transform, false);
            infoGo.AddComponent<RectTransform>();
            var infoLE = infoGo.AddComponent<LayoutElement>();
            infoLE.preferredWidth = 100f * UIScale.S; infoLE.flexibleWidth = 1f;
            var infoVlg = infoGo.AddComponent<VerticalLayoutGroup>();
            infoVlg.childForceExpandHeight = false;
            infoVlg.childForceExpandWidth = true;
            infoVlg.spacing = 0f;
            infoVlg.padding = new RectOffset(0, 0, (int)(PAD * 0.5f), (int)(PAD * 0.5f));

            UGUIShip.CreateFlowLabel(infoGo.transform, skin.name, FS, WHITE);
            UGUIShip.CreateFlowLabel(infoGo.transform, "by " + skin.author, FS_SM, HINT);
            if (!string.IsNullOrEmpty(skin.description))
                UGUIShip.CreateFlowLabel(infoGo.transform, skin.description, FS_SM, HINT, multiline: true);
            if (skin.isLocalImport)
                UGUIShip.CreateFlowLabel(infoGo.transform, "[Local]", FS_SM, GOLD);

            if (type == SkinType.Item && (skin.left != null || skin.right != null))
            {
                string fileKey = skin.file;
                if (!_handOverrides.ContainsKey(fileKey))
                    _handOverrides[fileKey] = 3;

                int ov = _handOverrides[fileKey];
                bool lOn = ov == 1 || ov == 3;
                bool rOn = ov == 2 || ov == 3;

                var handRow = new GameObject("HandRow");
                handRow.transform.SetParent(infoGo.transform, false);
                handRow.AddComponent<RectTransform>();
                var handLE = handRow.AddComponent<LayoutElement>();
                handLE.preferredHeight = FS_SM + 4f;
                var handHlg = handRow.AddComponent<HorizontalLayoutGroup>();
                handHlg.childForceExpandHeight = false;
                handHlg.childForceExpandWidth = false;
                handHlg.spacing = 2f;

                // declared up here so each button's click handler can recolor BOTH in place —
                // toggling a hand only changes these two buttons' colors, so doing a full
                // RefreshSkinList() (destroy + rebuild every row) just to flip a colour was the
                // source of the click freeze. recolor locally instead.
                Button lBtn = null, rBtn = null;
                Action repaintHands = () =>
                {
                    int o = _handOverrides.ContainsKey(fileKey) ? _handOverrides[fileKey] : 3;
                    bool l = o == 1 || o == 3;
                    bool r = o == 2 || o == 3;
                    if (lBtn != null) RecolorHandButton(lBtn, l);
                    if (rBtn != null) RecolorHandButton(rBtn, r);
                };

                if (skin.left != null)
                {
                    string capturedFile = fileKey;
                    lBtn = UGUIShip.CreateButton(handRow.transform, "L",
                        lOn ? CYAN : BTN_DARK, lOn ? Color.black : WHITE, FS_SM, new Action(() =>
                        {
                            int cur = _handOverrides.ContainsKey(capturedFile) ? _handOverrides[capturedFile] : 3;
                            bool wasOn = cur == 1 || cur == 3;
                            bool rStillOn = cur == 2 || cur == 3;
                            _handOverrides[capturedFile] = !wasOn ? rStillOn ? 3 : 1 : rStillOn ? 2 : 0;
                            SaveHandOverrides(); repaintHands();
                        }));
                    lBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = SEL_W * 0.5f;
                }

                if (skin.right != null)
                {
                    string capturedFile = fileKey;
                    rBtn = UGUIShip.CreateButton(handRow.transform, "R",
                        rOn ? CYAN : BTN_DARK, rOn ? Color.black : WHITE, FS_SM, new Action(() =>
                        {
                            int cur = _handOverrides.ContainsKey(capturedFile) ? _handOverrides[capturedFile] : 3;
                            bool lStillOn = cur == 1 || cur == 3;
                            bool wasOn = cur == 2 || cur == 3;
                            _handOverrides[capturedFile] = !wasOn ? lStillOn ? 3 : 2 : lStillOn ? 1 : 0;
                            SaveHandOverrides(); repaintHands();
                        }));
                    rBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = SEL_W * 0.5f;
                }

                // always build Configure; toggle active by selection (no structural change on click)
                string cfgFile = fileKey;
                var cfgBtn = UGUIShip.CreateButton(handRow.transform, "Configure",
                    BTN_DARK, ORANGE, FS_SM, new Action(() => OpenConfigWindow(cfgFile)));
                cfgBtn.gameObject.AddComponent<LayoutElement>().preferredWidth = SEL_W;
                cfgBtn.gameObject.SetActive(isSelected);
                rowConfigBtn = cfgBtn.gameObject;
            }

            // Cover
            var coverGo = new GameObject("Cover");
            coverGo.transform.SetParent(rowGo.transform, false);
            coverGo.AddComponent<RectTransform>();
            var coverLE = coverGo.AddComponent<LayoutElement>();
            coverLE.preferredWidth = COVER_W;
            coverLE.preferredHeight = COVER_H;
            coverLE.minWidth = COVER_W;
            var coverImg = coverGo.AddComponent<Image>();
            coverImg.color = new Color(0.04f, 0.04f, 0.04f, 1f);
            string coverKey = CoverKey(skin);
            _coverImages[coverKey] = coverImg;

            Texture2D coverTex = null;
            if (!skinCovers.TryGetValue(coverKey, out coverTex) || coverTex == null)
                catalogService?.TryGetCover(skin, out coverTex);

            if (coverTex != null)
            {
                skinCovers[coverKey] = coverTex;
                try { ApplyCover(coverImg, coverTex); }
                catch
                {
                    skinCovers.Remove(coverKey);
                    coverImg.color = new Color(0.04f, 0.04f, 0.04f, 1f);
                    UGUIShip.CreateStretchLabel(coverGo.transform, "No Preview", FS_SM, HINT);
                }
            }
            else
            {
                catalogService?.EnsureCover(skin, true);
                UGUIShip.CreateStretchLabel(coverGo.transform, "No Preview", FS_SM, HINT);
            }

            // Action button. emotes are copy-to-clipboard (not select+apply) — everything else uses Select
            int captured = index;
            if (type == SkinType.Emote)
            {
                var copyBtn = UGUIShip.CreateButton(
                    rowGo.transform,
                    "Copy",
                    new Color(0.18f, 0.18f, 0.18f, 1f),
                    YELLOW,
                    FS_SM,
                    new Action(() => OnCopyEmote(captured))
                );
                var copyLE = copyBtn.gameObject.AddComponent<LayoutElement>();
                copyLE.preferredWidth = SEL_W;
                copyLE.minWidth = SEL_W;
                copyLE.preferredHeight = ROW_H - PAD;
            }
            else
            {
                var selBtn = UGUIShip.CreateButton(
                    rowGo.transform,
                    "Select",
                    new Color(0.18f, 0.18f, 0.18f, 1f),
                    btnTxtColor,
                    FS_SM,
                    new Action(() => OnToggleSkin(captured))
                );
                var btnGo = selBtn.gameObject;
                var btnLE = btnGo.AddComponent<LayoutElement>();
                btnLE.preferredWidth = SEL_W;
                btnLE.minWidth = SEL_W;
                btnLE.preferredHeight = ROW_H - PAD;

                // register this row's togglable bits so OnToggleSkin can repaint it in place
                _rowVisuals[index] = new RowVisual
                {
                    gradient = gradGo,
                    configBtn = rowConfigBtn,
                    selectLabel = selBtn.transform.Find("Label")?.GetComponent<Text>(),
                    selColor = selColor,
                };
            }

            // delete button — only visible when "Imported Skins" repo is active
            if (_importedRepoSelected && skin.isLocalImport && !string.IsNullOrEmpty(skin.localPath))
            {
                string capturedFolder = Path.GetDirectoryName(skin.localPath);
                var delBtn = UGUIShip.CreateButton(
                    rowGo.transform, "−", BTN_REMOVE, WHITE, FS_SM,
                    new Action(() => OnDeleteImportedSkin(capturedFolder))
                ).gameObject;
                var delLE = delBtn.AddComponent<LayoutElement>();
                delLE.preferredWidth = BTN_H;
                delLE.minWidth = BTN_H;
                delLE.preferredHeight = ROW_H - PAD;
            }
        }

        // ── Featured repos ────────────────────────────────────────────────────

        private void SetFilterBarVisible(bool visible)
        {
            if (_btnCostumes != null) _btnCostumes.gameObject.SetActive(visible);
            if (_btnAccessories != null) _btnAccessories.gameObject.SetActive(visible);
            if (_btnItems != null) _btnItems.gameObject.SetActive(visible);
            if (_btnPlinths != null) _btnPlinths.gameObject.SetActive(visible);
            if (_btnEmotes != null) _btnEmotes.gameObject.SetActive(visible);
            if (_filterDivider2Rt != null) _filterDivider2Rt.gameObject.SetActive(visible);

            // absolute layout: hiding the bar alone leaves an empty gap, so pull the search field and
            // scroll view up by the freed height (and grow the scroll so its bottom stays put)
            float dy = visible ? 0f : _filterCollapse;
            if (_searchFieldRt != null)
                UGUIShip.SetPixelRect(_searchFieldRt, new Rect(_searchRectNormal.x, _searchRectNormal.y - dy, _searchRectNormal.width, _searchRectNormal.height));
            if (_scrollViewRt != null)
                UGUIShip.SetPixelRect(_scrollViewRt, new Rect(_scrollRectNormal.x, _scrollRectNormal.y - dy, _scrollRectNormal.width, _scrollRectNormal.height + dy));
        }

        private void RenderFeaturedRepos()
        {
            var featured = repoRegistry?.Featured;
            if (featured == null || featured.Count == 0)
            {
                CreateFeaturedEmptyLabel(repoRegistry != null && repoRegistry.FeaturedFetched
                    ? "No featured repositories yet."
                    : "Loading featured repositories...");
                return;
            }

            string q = _searchQuery?.ToLower() ?? "";
            int shown = 0;
            foreach (var f in featured)
            {
                var repo = RepoRegistry.ParseRepo(f.url);
                if (repo == null) continue;
                if (!string.IsNullOrEmpty(q) && !repo.DisplayName.ToLower().Contains(q)
                    && !(f.description ?? "").ToLower().Contains(q)) continue;
                CreateFeaturedRepoItem(repo, f.description);
                shown++;
            }

            if (shown == 0) CreateFeaturedEmptyLabel(string.Format(EMPTY_NO_RESULTS, _searchQuery));
        }

        // one card in the Featured list: a tall vertical card — repo cover banner on top (with margin),
        // then name/author/description, then a full-width action button. the gradient scrim is kept as
        // the card background
        private void CreateFeaturedRepoItem(SkinRepo repo, string description)
        {
            bool added = repoRegistry != null && repoRegistry.HasRepo(repo.githubUrl);

            var rowGo = new GameObject("FeaturedRepo_" + repo.repoName);
            rowGo.transform.SetParent(_scrollContent, false);
            rowGo.AddComponent<RectTransform>();
            rowGo.AddComponent<Image>().color = ITEM_BG;

            // kept background: the gradient scrim stretched over the whole card, behind the content
            var overlaySprite = BetterFG.Utilities.EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.cskins.repo_overlay.png");
            if (overlaySprite != null)
            {
                var ovGo = new GameObject("CardBg");
                ovGo.transform.SetParent(rowGo.transform, false);
                var ovRt = ovGo.AddComponent<RectTransform>();
                ovRt.anchorMin = Vector2.zero;
                ovRt.anchorMax = Vector2.one;
                ovRt.offsetMin = ovRt.offsetMax = Vector2.zero;
                ovGo.AddComponent<LayoutElement>().ignoreLayout = true;
                var ovImg = ovGo.AddComponent<Image>();
                ovImg.sprite = overlaySprite;
                ovImg.raycastTarget = false;
            }

            // vertical stack with a margin around the content (left carries the reclaimed row indent)
            var vlg = rowGo.AddComponent<VerticalLayoutGroup>();
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.spacing = PAD * 0.75f;
            vlg.padding = new RectOffset((int)(PAD + _rowIndent), (int)PAD, (int)PAD, (int)PAD);

            // cover banner on top — fixed height, aspect-fit inside (no stretch), centered
            float bannerH = COVER_H * 0.5f;
            var bannerGo = new GameObject("CoverBanner");
            bannerGo.transform.SetParent(rowGo.transform, false);
            bannerGo.AddComponent<RectTransform>();
            var bannerLE = bannerGo.AddComponent<LayoutElement>();
            bannerLE.preferredHeight = bannerH;
            bannerLE.minHeight = bannerH;
            bannerGo.AddComponent<RectMask2D>(); // clip the cover that overfills the banner width

            var coverGo = new GameObject("Cover");
            coverGo.transform.SetParent(bannerGo.transform, false);
            var coverRt = coverGo.AddComponent<RectTransform>();
            coverRt.anchorMin = Vector2.zero;
            coverRt.anchorMax = Vector2.one;
            coverRt.offsetMin = coverRt.offsetMax = Vector2.zero;
            // envelope so every banner fills the full card width uniformly (crop overflow, no letterbox)
            var arf = coverGo.AddComponent<AspectRatioFitter>();
            arf.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            var coverImg = coverGo.AddComponent<RawImage>();
            coverImg.color = new Color(1f, 1f, 1f, 0f);
            coverImg.raycastTarget = false;
            _featuredCoverImages[repo.githubUrl] = coverImg;
            var cachedTex = repoRegistry.GetCover(repo);
            if (cachedTex != null)
            {
                coverImg.texture = cachedTex;
                coverImg.color = Color.white;
                if (cachedTex.height > 0) arf.aspectRatio = (float)cachedTex.width / cachedTex.height;
            }
            else repoRegistry.FetchCover(repo);

            // text block
            var infoGo = new GameObject("Info");
            infoGo.transform.SetParent(rowGo.transform, false);
            infoGo.AddComponent<RectTransform>();
            var infoVlg = infoGo.AddComponent<VerticalLayoutGroup>();
            infoVlg.childForceExpandHeight = false;
            infoVlg.childForceExpandWidth = true;
            infoVlg.spacing = 0f;

            UGUIShip.CreateFlowLabel(infoGo.transform, repo.repoName, FS, WHITE);
            UGUIShip.CreateFlowLabel(infoGo.transform, "by " + repo.author, FS_SM, HINT);
            if (!string.IsNullOrEmpty(description))
                UGUIShip.CreateFlowLabel(infoGo.transform, description, FS_SM, HINT, multiline: true);

            // full-width action button at the bottom
            string capturedUrl = repo.githubUrl;
            var actBtn = added
                ? UGUIShip.CreateButton(rowGo.transform, "Select", new Color(0.18f, 0.18f, 0.18f, 1f), YELLOW, FS_SM, new Action(() => OnSelectFeatured(capturedUrl)))
                : UGUIShip.CreateButton(rowGo.transform, "Add", BTN_FETCH, WHITE, FS_SM, new Action(() => OnAddFeatured(capturedUrl)));
            actBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = BTN_H;
        }

        private void CreateFeaturedEmptyLabel(string msg)
        {
            var lbl = UGUIShip.CreateFlowLabel(_scrollContent, msg, FS_SM, HINT);
            lbl.alignment = TextAnchor.MiddleCenter;
            var le = lbl.GetComponent<LayoutElement>();
            le.preferredHeight = LH * 2f;
            le.flexibleWidth = 1f;
        }

        // AddRepo validates (marker + dedupe) async; on success OnReposChanged rebuilds the list
        // and this card flips to "Select"
        private void OnAddFeatured(string url) => repoRegistry?.AddRepo(url);

        private void OnSelectFeatured(string url)
        {
            var repo = repoRegistry?.FindRepo(url);
            if (repo == null) return;
            _featuredSelected = false;
            repoRegistry.SetActive(repo);
            _repoDropdownOpen = false;
            RefreshRepoRows();
            RefreshSkinList();
        }

        private void OnDeleteImportedSkin(string folderPath)
        {
            _importedPaths.Remove(folderPath);
            SaveImportedPaths();

            for (int i = availableSkins.Count - 1; i >= 0; i--)
            {
                var s = availableSkins[i];
                if (!s.isLocalImport || string.IsNullOrEmpty(s.localPath)) continue;
                if (!string.Equals(Path.GetDirectoryName(s.localPath), folderPath, StringComparison.OrdinalIgnoreCase)) continue;
                // fix selectedIndices for the removed entry shifting everything above it down
                selectedIndices.Remove(i);
                var shifted = new HashSet<int>();
                foreach (int idx in selectedIndices)
                    shifted.Add(idx > i ? idx - 1 : idx);
                selectedIndices = shifted;
                availableSkins.RemoveAt(i);
                break;
            }

            SaveSelection();
            RefreshSkinList();
        }

        // ── Emote copy ────────────────────────────────────────────────────────

        // stash the emote's bundle/clip/sound/cover urls so the Emotes section of the
        // Emoticons & Phrases tab can paste it (download + fill an EmoteEntry).
        private void OnCopyEmote(int index)
        {
            if (index < 0 || index >= availableSkins.Count) return;

            // second press on an already-copied row: open Social > Emotes (as the 3rd tab)
            if (_copyArmedIndex == index)
            {
                _copyArmedIndex = -1;
                BetterFGUIMan.Instance?.OpenSocialEmotes();
                return;
            }

            var skin = availableSkins[index];

            string repoRaw = RepoRegistry.ResolveRaw(skin.sourceRepo);
            string folder = !string.IsNullOrEmpty(skin.repoFolder) ? skin.repoFolder : $"Emotes/{skin.file}";
            string bundleUrl = $"{repoRaw}/{folder}/{skin.file}";
            string soundUrl = string.IsNullOrEmpty(skin.audio) ? "" : $"{repoRaw}/{folder}/{skin.audio}";
            string coverUrl = $"{repoRaw}/{folder}/cover.jpg"; // paste falls back to .png if this 404s

            // hand over the cover we already have loaded so the paste button can preview it instantly
            Texture2D cover = null;
            if (!skinCovers.TryGetValue(CoverKey(skin), out cover) || cover == null)
                catalogService?.TryGetCover(skin, out cover);

            BetterFG.Customization.Social.EmoteClipboard.Set(skin.name, bundleUrl, soundUrl, coverUrl, skin.audio ?? "", cover);

            // arm this row — the prompt to open lives in the tooltip, never on the button
            _copyArmedIndex = index;
            BetterFGUIMan.Instance?.ShowTooltipTimed("Copied, click again to open Social>Emotes as 3rd tab", 3f);
        }

        // ── Selection toggle ──────────────────────────────────────────────────

        private void OnToggleSkin(int index)
        {
            if (index < 0 || index >= availableSkins.Count) return;
            SkinInfo skin = availableSkins[index];
            SkinType type = SkinTypeParser.FromString(skin.type);

            // track every row whose selected-state changed so we can repaint just those in place
            // instead of rebuilding the entire list (the click freeze). a single click can flip
            // two rows: the clicked one + whatever costume/plinth got auto-deselected.
            var changed = new List<int> { index };

            if (selectedIndices.Contains(index))
            {
                selectedIndices.Remove(index);
            }
            else
            {
                switch (type)
                {
                    case SkinType.Costume:
                        int existing = SelectedCostumeIndex();
                        if (existing != -1) { selectedIndices.Remove(existing); changed.Add(existing); }
                        selectedIndices.Add(index);
                        break;

                    case SkinType.Accessory:
                        if (CountSelected(SkinType.Accessory) >= MAX_ACCESSORY)
                        { SetStatus($"Max {MAX_ACCESSORY} accessories."); return; }
                        selectedIndices.Add(index);
                        break;

                    case SkinType.Item:
                        if (CountSelected(SkinType.Item) >= MAX_ITEM)
                        { SetStatus($"Max {MAX_ITEM} items."); return; }
                        selectedIndices.Add(index);
                        break;

                    case SkinType.Plinth:
                        // deselect any existing plinth first
                        int existingPlinth = -1;
                        foreach (int pi in selectedIndices)
                        {
                            if (pi < 0 || pi >= availableSkins.Count) continue;
                            if (SkinTypeParser.FromString(availableSkins[pi].type) == SkinType.Plinth) { existingPlinth = pi; break; }
                        }
                        if (existingPlinth != -1) { selectedIndices.Remove(existingPlinth); changed.Add(existingPlinth); }
                        selectedIndices.Add(index);
                        break;

                    default:
                        selectedIndices.Add(index);
                        break;
                }
            }

            SaveSelection();
            foreach (int ci in changed) RepaintRowSelection(ci);
        }

        // flip a single row's selection visuals (gradient, Configure btn, Select label colour)
        // to match its current selectedIndices state — no list rebuild, no layout pass
        private void RepaintRowSelection(int index)
        {
            if (!_rowVisuals.TryGetValue(index, out var rv) || rv == null) return;
            bool sel = selectedIndices.Contains(index);
            if (rv.gradient != null) rv.gradient.SetActive(sel);
            if (rv.configBtn != null) rv.configBtn.SetActive(sel);
            if (rv.selectLabel != null) rv.selectLabel.color = sel ? rv.selColor : WHITE;
        }

        // ── Persist & restore ─────────────────────────────────────────────────

        private const string KEY_MULTI_REPOS = "skin.multi.repos";

        private void SaveSelection()
        {
            var files = new List<string>();
            var sources = new List<string>();
            var paths = new List<string>();
            var repos = new List<string>();
            var types = new List<string>();

            foreach (int i in selectedIndices)
            {
                if (i < 0 || i >= availableSkins.Count) continue;
                SkinInfo s = availableSkins[i];
                files.Add(s.file);
                sources.Add(s.isLocalImport ? "local" : "remote");
                paths.Add(s.isLocalImport && !string.IsNullOrEmpty(s.localPath)
                    ? Path.GetDirectoryName(s.localPath) : "");
                repos.Add(s.sourceRepo ?? "");
                types.Add(s.type ?? "");
            }

            SettingsService.Set(KEY_MULTI_FILES, string.Join(",", files));
            SettingsService.Set(KEY_MULTI_SOURCES, string.Join(",", sources));
            SettingsService.Set(KEY_MULTI_PATHS, string.Join(",", paths));
            SettingsService.Set(KEY_MULTI_REPOS, string.Join(",", repos));
            SettingsService.Set(KEY_MULTI_TYPES, string.Join(",", types));
        }

        private void SaveHandOverrides()
        {
            var parts = new List<string>();
            foreach (var kvp in _handOverrides)
                parts.Add($"{kvp.Key}:{kvp.Value}");
            SettingsService.Set(KEY_HAND_OVERRIDES, string.Join(",", parts));
        }

        private void LoadHandOverrides()
        {
            _handOverrides.Clear();
            string raw = SettingsService.Get(KEY_HAND_OVERRIDES, "");
            if (string.IsNullOrEmpty(raw)) return;
            foreach (string part in raw.Split(','))
            {
                int colon = part.LastIndexOf(':');
                if (colon < 1) continue;
                string file = part.Substring(0, colon);
                if (int.TryParse(part.Substring(colon + 1), out int ov))
                    _handOverrides[file] = ov;
            }
        }

        private void TryRestoreSelection()
        {
            LoadHandOverrides();
            string multiFiles = SettingsService.Get(KEY_MULTI_FILES);
            string multiSources = SettingsService.Get(KEY_MULTI_SOURCES);
            string multiPaths = SettingsService.Get(KEY_MULTI_PATHS);
            string multiRepos = SettingsService.Get(KEY_MULTI_REPOS);

            if (string.IsNullOrEmpty(multiFiles))
            {
                string legacyFile = SettingsService.Get(KEY_FILE);
                if (!string.IsNullOrEmpty(legacyFile))
                {
                    multiFiles = legacyFile;
                    multiSources = SettingsService.Get(KEY_SOURCE);
                    multiPaths = SettingsService.Get(KEY_LOCAL_PATH);
                }
            }

            if (string.IsNullOrEmpty(multiFiles)) return;

            string[] files = multiFiles.Split(',');
            string[] sources = multiSources?.Split(',') ?? new string[0];
            string[] paths = multiPaths?.Split(',') ?? new string[0];
            string[] repos = multiRepos?.Split(',') ?? new string[0];

            selectedIndices.Clear();
            _pendingRestoreFiles.Clear();

            for (int s = 0; s < files.Length; s++)
            {
                string file = files[s].Trim();
                string source = s < sources.Length ? sources[s].Trim() : "remote";
                string path = s < paths.Length ? paths[s].Trim() : "";
                string repo = s < repos.Length ? repos[s].Trim() : "";

                if (source == "local") continue; // local imports are handled by SkinApplicationService.RestoreFromSettings

                int idx = availableSkins.FindIndex((sk) => sk.file == file);
                if (idx >= 0)
                {
                    if (!string.IsNullOrEmpty(repo)) availableSkins[idx].sourceRepo = repo;
                    selectedIndices.Add(idx);
                }
                else
                    _pendingRestoreFiles.Add((file, repo));
            }

            RefreshSkinList();
            // NOTE: do NOT call OnApply here — SkinApplicationService.RestoreFromSettings handles actual application on boot
        }

        private void RetryPendingRestore()
        {
            if (_pendingRestoreFiles.Count == 0) return;
            var stillPending = new List<(string file, string repo)>();
            bool anyFound = false;

            foreach (var (file, repo) in _pendingRestoreFiles)
            {
                int idx = availableSkins.FindIndex((sk) => sk.file == file);
                if (idx >= 0)
                {
                    if (!string.IsNullOrEmpty(repo)) availableSkins[idx].sourceRepo = repo;
                    selectedIndices.Add(idx);
                    anyFound = true;
                }
                else stillPending.Add((file, repo));
            }

            _pendingRestoreFiles = stillPending;
            if (anyFound) RefreshSkinList();
        }

        // ── Callbacks ─────────────────────────────────────────────────────────

        private void OnSkinsLoaded(List<SkinInfo> skins)
        {
            availableSkins = MergeImported(skins);
            RefreshSkinList();
            SetStatus($"Loaded {skins.Count} customizations");
            // sync selection from already-applied slots every time new skins arrive
            // handles the case where a skin came from a repo that was fetched late (e.g. plinth repo)
            SyncSelectedFromApplied();
            RetryPendingRestore();
        }

        private bool _restoredOnce = false;

        private void OnFetchCompleted()
        {
            availableSkins = MergeImported(catalogService?.AvailableSkins);
            RefreshSkinList();

            if (!_restoredOnce)
            {
                _restoredOnce = true;
                TryRestoreSelection();
            }

            RetryPendingRestore();
        }

        // catalog callbacks return only remote skins — fold the persisted local imports
        // back in (re-seeding from disk if they were dropped) so they survive re-fetches
        private List<SkinInfo> MergeImported(List<SkinInfo> catalogSkins)
        {
            var merged = catalogSkins != null ? new List<SkinInfo>(catalogSkins) : new List<SkinInfo>();

            // keep any local imports already live in availableSkins
            foreach (var s in availableSkins)
                if (s.isLocalImport && !string.IsNullOrEmpty(s.localPath) &&
                    merged.FindIndex(x => x.isLocalImport && x.localPath == s.localPath) < 0)
                    merged.Add(s);

            // re-seed any persisted imports that aren't present anymore (e.g. after ClearCache)
            foreach (string folder in _importedPaths)
            {
                if (string.IsNullOrEmpty(folder)) continue;
                if (merged.FindIndex(x => x.isLocalImport && !string.IsNullOrEmpty(x.localPath) &&
                    string.Equals(Path.GetDirectoryName(x.localPath), folder, StringComparison.OrdinalIgnoreCase)) >= 0)
                    continue;
                var seeded = LoadImportedFromFolder(folder);
                if (seeded != null) merged.Add(seeded);
            }

            return merged;
        }

        // reflect already-applied slots back into the UI selection state
        private void SyncSelectedFromApplied()
        {
            if (applicationService == null) return;
            var applied = applicationService.GetActiveSlots();
            foreach (var slot in applied)
            {
                int idx = availableSkins.FindIndex(s => s.file == slot.skinInfo.file);
                if (idx >= 0 && !selectedIndices.Contains(idx))
                    selectedIndices.Add(idx);
            }
            if (selectedIndices.Count > 0) RefreshSkinList();
        }

        private void OnRepoCoverLoaded(SkinRepo repo, Texture2D tex)
        {
            if (repo == null || tex == null) return;
            if (_repoCoverImages.TryGetValue(repo.githubUrl, out var img) && img != null)
            { img.texture = tex; img.color = Color.white; }
            if (_featuredCoverImages.TryGetValue(repo.githubUrl, out var fimg) && fimg != null)
            {
                fimg.texture = tex; fimg.color = Color.white;
                var arf = fimg.GetComponent<AspectRatioFitter>();
                if (arf != null && tex.height > 0) arf.aspectRatio = (float)tex.width / tex.height;
            }
        }

        private void OnFeaturedLoaded()
        {
            if (_featuredSelected) RefreshSkinList();
        }

        private void OnSkinCoverLoaded(string key, Texture2D tex)
        {
            if (tex == null || tex.width == 0) return;
            skinCovers[key] = tex;
            if (_coverImages.TryGetValue(key, out Image img) && img != null)
                try { ApplyCover(img, tex); }
                catch (Exception ex) { Plugin.Log.LogWarning($"SkinUI: Cover sprite failed: {ex.Message}"); }
        }

        private static void ApplyCover(Image img, Texture2D tex)
        {
            for (int i = img.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(img.transform.GetChild(i).gameObject);
            img.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
            img.preserveAspect = true;
            img.color = Color.white;
        }

        private static string CoverKey(SkinInfo skin)
        {
            if (skin == null || string.IsNullOrEmpty(skin.file)) return "";
            string repo = !string.IsNullOrEmpty(skin.sourceRepo) ? skin.sourceRepo : skin.localPath ?? "";
            return repo + "|" + skin.file;
        }

        private void OnSkinDownloaded(SkinInfo _, AssetBundle bundle)
        {
            if (_pendingApplyQueue.Count == 0) return;

            SkinInfo skin = _pendingApplyQueue[0];
            _pendingApplyQueue.RemoveAt(0);

            SetStatus($"Downloaded {skin.name}, applying...");
            applicationService.ApplySkin(skin, bundle, additive: true);
            KickNextPending();
        }

        private void OnSkinImported(SkinInfo skinInfo, AssetBundle bundle, Texture2D cover)
        {
            int idx = availableSkins.FindIndex((s) => s.file == skinInfo.file);
            if (idx < 0) { availableSkins.Add(skinInfo); idx = availableSkins.Count - 1; }
            else availableSkins[idx] = skinInfo;

            if (!selectedIndices.Contains(idx)) selectedIndices.Add(idx);

            if (cover != null) skinCovers[CoverKey(skinInfo)] = cover;

            // persist the imported folder path
            if (!string.IsNullOrEmpty(skinInfo.localPath))
            {
                string folder = Path.GetDirectoryName(skinInfo.localPath);
                if (!string.IsNullOrEmpty(folder) && !_importedPaths.Contains(folder))
                {
                    _importedPaths.Add(folder);
                    SaveImportedPaths();
                }
            }

            RefreshSkinList();
            SetStatus($"Imported {skinInfo.name}, applying...");
            applicationService.ApplySkin(skinInfo, bundle, additive: true);
            // remove from queue (KickNextPending left it in when it triggered the import) then advance
            int qi = _pendingApplyQueue.FindIndex(s => s.file == skinInfo.file);
            if (qi >= 0) _pendingApplyQueue.RemoveAt(qi);
            KickNextPending();
            SaveSelection();
        }

        // OnStatusUpdate fires per skin during a fetch ("Loading... (N)"). The count label only
        // exists in the empty-state branch, and as soon as the first skin for the active section
        // lands the list stops being empty and the label is gone — so just rebuilding it here goes
        // stale the moment rows show up. Rebuild the whole list per tick while a fetch is running so
        // new rows AND the count come in live, instead of only when you click a section to redraw.
        private void SetStatus(string msg)
        {
            if (catalogService != null && catalogService.IsFetching)
            {
                RefreshSkinList();
                return;
            }

            if (_fetchCountLabel != null && catalogService != null)
            {
                string activeRaw = repoRegistry?.Active?.RawBase;
                int repoTotal = catalogService.GetCatalogTotalForRepo(activeRaw);
                if (repoTotal > 0)
                    _fetchCountLabel.text = $"{catalogService.GetFetchedCountForRepo(activeRaw)} / {repoTotal} fetched";
            }
        }

        private void OpenConfigWindow(string file)
        {
            if (_configWindow != null)
            {
                if (_configWindowFile == file)
                {
                    // If it's just hidden (tab was closed), re-show it
                    if (!_configWindow.IsVisible)
                    {
                        _configWindow.Configure(availableSkins.Find(s => s.file == file), applicationService, this);
                        return;
                    }
                    // Visible and same file — toggle close
                    Destroy(_configWindow.gameObject);
                    _configWindow = null;
                    _configWindowFile = null;
                    return;
                }
                Destroy(_configWindow.gameObject);
            }
            var skin = availableSkins.Find(s => s.file == file);
            if (skin == null) return;
            var go = new GameObject("BetterFG_ItemConfig");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _configWindow = go.AddComponent<ItemConfigWindow>();
            _configWindow.Configure(skin, applicationService, this);
            _configWindowFile = file;
        }

        // ── Button handlers ───────────────────────────────────────────────────

        private void OnFetch()
        {
            if (catalogService == null) return;
            catalogService.ClearCache();
            availableSkins.Clear();
            skinCovers.Clear();
            _restoredOnce = false;
            RefreshSkinList();
            FetchActiveRepo();
        }

        private void OnImport()
        {
            WinDialogs.PickFolder("Select Skin Folder", path =>
            {
                if (!string.IsNullOrEmpty(path)) loaderService?.ImportSkinFromFolder(path);
            });
        }

        private void OnApply()
        {
            _pendingApplyQueue.Clear();

            // build the set of files the user currently wants equipped (non-plinth), and
            // pick out the selected plinth (if any) separately — plinth has its own apply path
            var wantedFiles = new HashSet<string>();
            SkinInfo pendingPlinth = null;
            var wantedSkins = new List<SkinInfo>();
            // snapshot what's currently equipped (file -> live hand override) BEFORE we mutate
            // any skinInfo. the active slot shares the same SkinInfo reference as availableSkins,
            // so writing skin.handOverride below also changes the slot's value — we'd never see
            // the change if we compared after. capture it up front.
            var liveHandOverrides = new Dictionary<string, int>();
            foreach (var slot in applicationService.GetActiveSlots())
                if (slot?.skinInfo != null)
                    liveHandOverrides[slot.skinInfo.file] = slot.skinInfo.handOverride;

            foreach (int i in selectedIndices)
            {
                if (i < 0 || i >= availableSkins.Count) continue;
                var skin = availableSkins[i];
                if (SkinTypeParser.FromString(skin.type) == SkinType.Plinth)
                {
                    pendingPlinth = skin;
                    continue;
                }
                if (_handOverrides.ContainsKey(skin.file))
                    skin.handOverride = _handOverrides[skin.file];
                wantedFiles.Add(skin.file);
                wantedSkins.Add(skin);
            }

            // DIFF instead of nuke-everything: only unequip what's no longer selected, and
            // only download/apply what isn't already on. unchanged slots are left untouched so
            // changing one item (or the plinth) doesn't re-run the whole loadout = no flicker
            foreach (var slot in applicationService.GetActiveSlots())
            {
                if (slot?.skinInfo == null) continue;
                if (!wantedFiles.Contains(slot.skinInfo.file))
                    applicationService.RemoveOneSkinByFile(slot.skinInfo.file);
            }

            foreach (var skin in wantedSkins)
            {
                if (applicationService.HasActiveSlotForFile(skin.file))
                {
                    // already equipped. the only per-item change the menu can make to a live skin
                    // is the L/R hand override — compare against the snapshot (not the live slot,
                    // which now shares skin's mutated value). unchanged = leave it fully alone.
                    int wasOverride = liveHandOverrides.TryGetValue(skin.file, out int v) ? v : skin.handOverride;
                    if (wasOverride == skin.handOverride)
                        continue;
                    // hand override changed — respawn just this item from its cached bundle
                    // (no redownload, nothing else on the bean disturbed). only queue a full
                    // download if the bundle somehow isn't loaded anymore.
                    if (applicationService.TryReapplyLoadedSkin(skin))
                        continue;
                }
                _pendingApplyQueue.Add(skin);
            }
            _pendingTotal = _pendingApplyQueue.Count;

            // Costumes first, then accessories, then items
            _pendingApplyQueue.Sort((a, b) =>
            {
                int ScoreOf(SkinInfo s)
                {
                    switch (SkinTypeParser.FromString(s.type))
                    {
                        case SkinType.Costume: return 0;
                        case SkinType.Accessory: return 1;
                        case SkinType.Item: return 2;
                        default: return 3;
                    }
                }
                return ScoreOf(a).CompareTo(ScoreOf(b));
            });

            // plinth: if nothing's selected but one's applied, take it off; otherwise only
            // (re)apply when the selected plinth actually differs from what's already on
            if (pendingPlinth == null)
            {
                if (_plinthApp != null && _plinthApp.HasPlinthApplied) _plinthApp.RemovePlinth();
            }
            else if (_plinthApp == null || _plinthApp.ActiveFile != pendingPlinth.file)
            {
                StartCoroutine(ApplyPlinthDownload(pendingPlinth).WrapToIl2Cpp());
            }

            SaveSelection();
            KickNextPending();
        }

        private void KickNextPending()
        {
            if (_pendingApplyQueue.Count == 0)
            {
                SetStatus($"Applied {_pendingTotal} customization(s).");
                StartCoroutine(ReapplySkinTexturesAfterApply().WrapToIl2Cpp());
                return;
            }

            SkinInfo next = _pendingApplyQueue[0];
            SetStatus($"Loading {next.name}... ({_pendingTotal - _pendingApplyQueue.Count + 1}/{_pendingTotal})");

            if (next.isLocalImport && !string.IsNullOrEmpty(next.localPath))
            {
                loaderService?.ImportSkinFromFolder(Path.GetDirectoryName(next.localPath));
                return;
            }

            string repoRaw = RepoRegistry.ResolveRaw(next.sourceRepo);

            string category = GetCategoryFolder(next.type);
            string folder = !string.IsNullOrEmpty(next.repoFolder) ? next.repoFolder : $"{category}/{next.file}";
            string url = $"{repoRaw}/{folder}/{next.file}";
            string infoUrl = $"{repoRaw}/{folder}/info.json";

            Plugin.Log.LogInfo($"BetterFG: Downloading: {url}");

            // stamp sourceRepo so downstream (SkinApplicationService) can resolve correctly
            next.sourceRepo = repoRaw;

            // size gate before kicking the download
            StartCoroutine(KickWithSizeCheck(next, url, infoUrl).WrapToIl2Cpp());
        }

        private System.Collections.IEnumerator ReapplySkinTexturesAfterApply()
        {
            yield return new WaitForSeconds(0.35f);
            CustomSkinTextureTab.ReapplyAllEnabledFromSettings();
        }

        private System.Collections.IEnumerator KickWithSizeCheck(SkinInfo next, string url, string infoUrl)
        {
            bool sizeOk = false;
            string sizeErr = null;
            yield return RepoRegistry.CheckBundleSize(url, (ok, err) => { sizeOk = ok; sizeErr = err; });
            if (!sizeOk)
            {
                SetStatus($"Skipped {next.name}: {sizeErr}");
                if (_pendingApplyQueue.Count > 0 && _pendingApplyQueue[0] == next)
                    _pendingApplyQueue.RemoveAt(0);
                KickNextPending();
                yield break;
            }
            loaderService?.DownloadSkinWithInfo(next.file, url, infoUrl);
        }

        private System.Collections.IEnumerator ApplyPlinthDownload(SkinInfo info)
        {
            if (_plinthApp == null) yield break;

            // if the bundle is already live, skip the entire download/load and apply immediately
            if (_plinthApp.TryGetBundle(info.file, out var cachedBundle))
            {
                _plinthApp.ApplyPlinth(info, cachedBundle);
                yield break;
            }

            byte[] bytes = null;

            if (info.isLocalImport && !string.IsNullOrEmpty(info.localPath))
            {
                SetStatus($"Loading plinth {info.name}...");
                try { bytes = System.IO.File.ReadAllBytes(info.localPath); }
                catch (Exception ex) { SetStatus($"Plinth read failed: {ex.Message}"); yield break; }
                yield return null;
            }
            else
            {
                string repoRaw = RepoRegistry.ResolveRaw(info.sourceRepo);
                string folder = !string.IsNullOrEmpty(info.repoFolder) ? info.repoFolder : $"Plinths/{info.file}";
                string url = $"{repoRaw}/{folder}/{info.file}";

                SetStatus($"Downloading plinth {info.name}...");

                var req = UnityEngine.Networking.UnityWebRequest.Get(url);
                yield return req.SendWebRequest();

                if (req.result != UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    SetStatus($"Plinth dl failed: {req.error}");
                    req.Dispose();
                    yield break;
                }

                bytes = req.downloadHandler.data;
                req.Dispose();
            }

            // check again after the async download in case another coroutine raced us
            if (_plinthApp.TryGetBundle(info.file, out var raceBundle))
            {
                _plinthApp.ApplyPlinth(info, raceBundle);
                yield break;
            }

            AssetBundle bundle = null;
            try { bundle = AssetBundle.LoadFromMemory(bytes); }
            catch (Exception ex) { Plugin.Log.LogWarning($"Plinth: bundle load failed: {ex.Message}"); }

            if (bundle == null) { SetStatus("Plinth: bundle load failed"); yield break; }

            _plinthApp.ApplyPlinth(info, bundle);
        }

        private static string GetCategoryFolder(string typeStr)
        {
            switch (SkinTypeParser.FromString(typeStr))
            {
                case SkinType.Costume: return "Costumes";
                case SkinType.Accessory: return "Accessories";
                case SkinType.Item: return "Items";
                case SkinType.Plinth: return "Plinths";
                case SkinType.Emote: return "Emotes";
                default: return "Costumes";
            }
        }

        private void OnRemoveAll()
        {
            SkinType filter = _activeFilter;

            if (filter == SkinType.Plinth)
            {
                _plinthApp?.RemovePlinth();
            }
            else
            {
                // clear the UI selection for this filter
                var toRemove = new List<int>();
                foreach (int i in selectedIndices)
                {
                    if (i < 0 || i >= availableSkins.Count) continue;
                    if (SkinTypeParser.FromString(availableSkins[i].type) == filter)
                        toRemove.Add(i);
                }
                foreach (int i in toRemove)
                {
                    selectedIndices.Remove(i);
                    if (filter == SkinType.Item && availableSkins[i] != null)
                        _handOverrides.Remove(availableSkins[i].file);
                }
                _pendingApplyQueue.RemoveAll(s => SkinTypeParser.FromString(s.type) == filter);

                // strip whatever is actually APPLIED to the bean of this type. the UI selection
                // can drift from what's equipped (selected-not-applied, or applied-not-selected),
                // so walk the active slots directly instead of trusting selectedIndices — GetActiveSlots
                // returns a copy so removing while iterating is fine
                if (applicationService != null)
                {
                    foreach (var slot in applicationService.GetActiveSlots())
                    {
                        if (slot?.skinInfo == null || string.IsNullOrEmpty(slot.skinInfo.file)) continue;
                        if (slot.type != filter) continue;
                        applicationService.RemoveOneSkinByFile(slot.skinInfo.file);
                        if (filter == SkinType.Item)
                            _handOverrides.Remove(slot.skinInfo.file);
                    }
                }

                if (filter == SkinType.Item && _configWindow != null)
                {
                    Destroy(_configWindow.gameObject);
                    _configWindow = null;
                    _configWindowFile = null;
                }
            }

            SaveSelection();
            SaveHandOverrides();
            RefreshSkinList();
            SetStatus($"Removed all {filter.ToString().ToLower()}s.");
        }

        // ── UGUI helpers ──────────────────────────────────────────────────────

        private void BuildSearchField(Transform parent, float y, float w)
        {
            var go = new GameObject("SearchField");
            go.transform.SetParent(parent, false);
            _searchFieldRt = go.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(_searchFieldRt, new Rect(PAD, y, w, LH));

            // search icon on the left — same sizing as AddSearchIcon (0.75 * fontSize)
            float iconSize = FS_SM * 0.75f;
            float iconLeft = 2f;
            float textLeft = iconLeft + iconSize + 4f;
            var iconSprite = BetterFG.Utilities.EmbeddedResourceandUnity.LoadSprite("BetterFG.assets.ui.button.search.png");
            if (iconSprite != null)
            {
                var iconGo = new GameObject("SearchIcon");
                iconGo.transform.SetParent(go.transform, false);
                var iRt = iconGo.AddComponent<RectTransform>();
                iRt.anchorMin = new Vector2(0f, 0.5f);
                iRt.anchorMax = new Vector2(0f, 0.5f);
                iRt.pivot = new Vector2(0f, 0.5f);
                iRt.anchoredPosition = new Vector2(iconLeft, 0f);
                iRt.sizeDelta = new Vector2(iconSize, iconSize);
                var iImg = iconGo.AddComponent<Image>();
                iImg.sprite = iconSprite;
                iImg.preserveAspect = true;
                iImg.raycastTarget = false;
            }

            _searchPlaceholder = UGUIShip.CreateLabel(go.transform, default, "search...", FS_SM,
                new Color(1f, 1f, 1f, 0.2f), TextAnchor.MiddleLeft);
            _searchPlaceholder.fontStyle = FontStyle.Italic;
            var phRt = _searchPlaceholder.rectTransform;
            phRt.anchorMin = Vector2.zero; phRt.anchorMax = Vector2.one;
            phRt.offsetMin = new Vector2(textLeft, 0f); phRt.offsetMax = Vector2.zero;

            _searchText = UGUIShip.CreateLabel(go.transform, default, "", FS_SM, WHITE, TextAnchor.MiddleLeft);
            var txtRt = _searchText.rectTransform;
            txtRt.anchorMin = Vector2.zero; txtRt.anchorMax = Vector2.one;
            txtRt.offsetMin = new Vector2(textLeft, 0f); txtRt.offsetMax = Vector2.zero;

            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            var nav = btn.navigation;
            nav.mode = Navigation.Mode.None;
            btn.navigation = nav;
            btn.onClick.AddListener(new Action(() => { _searchActive = true; SetFakeInputLock(true); UpdateSearchCaret(); }));
            go.AddComponent<Image>().color = Color.clear;
        }

        private static Rect PR(float y, float w, float h) => new Rect(PAD, y, w, h);

    }
}
