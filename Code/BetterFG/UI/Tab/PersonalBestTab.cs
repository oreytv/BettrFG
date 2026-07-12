using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using BetterFG.Features.QualificationTime;
using BetterFG.Services;
using BetterFG.UI;
using BetterFG.Utilities;
using UnityEngine;
using UnityEngine.UI;

namespace BetterFG.UI.Tab
{
    // brand new, self contained PB tab. doesn't touch the old popup at all. search bar up top, a
    // tight scrolling list of every recorded pb under it. each row has a thumbnail, name, time, a
    // favorite star and a delete button. tiny vertical spacing so you can see a bunch at once.
    public class PersonalBestTab : BetterFGTab
    {
        public PersonalBestTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Personal Bests";

        static float PAD => UIScale.PAD;
        static float SH => UIScale.SH;
        static float BTN_H => UIScale.BTN_H;
        static int FS => UIScale.FS;
        static int FS_SM => UIScale.FS_SM;

        const float ROW_H = 48f;
        const float HEADER_H = 20f;

        static Texture2D _bgTex;
        static Texture2D _hoverTex;
        GameObject _bgHoverGo;

        // load the row icons once and reuse them across every row
        const string SP = "BetterFG.assets.ui.feature.qualificationtime.";
        static Sprite _starOn, _starOff, _delIdle, _delHover;
        static Sprite StarOn => _starOn ??= EmbeddedResourceandUnity.LoadSprite(SP + "featurequalificationtime_favoritedstar.png");
        static Sprite StarOff => _starOff ??= EmbeddedResourceandUnity.LoadSprite(SP + "featurequalificationtime_favoritestar.png");
        static Sprite DelIdle => _delIdle ??= EmbeddedResourceandUnity.LoadSprite(SP + "featurequalificationtime_delete_idle.png");
        static Sprite DelHover => _delHover ??= EmbeddedResourceandUnity.LoadSprite(SP + "featurequalificationtime_delete.png");

        static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        // zebra striping like the tweaks window: one row ~3% white, the next fully transparent
        static readonly Color ROW_ALT = new Color(1f, 1f, 1f, 0.03f);
        static readonly Color ROW_CLEAR = new Color(0f, 0f, 0f, 0f);
        static readonly Color TIME_COL = new Color(1f, 0.92f, 0.2f);
        static readonly Color GHOST_COL = new Color(0.75f, 0.9f, 1f);

        InputField _searchField;
        RectTransform _listContent;
        Text _statusLbl;
        string _query = "";

        // solos/duos/squads sub-tab. each shows only the entries that have a time for that show.
        PbType _subType = PbType.Solos;
        Button _btnSolos, _btnDuos, _btnSquads;
        static readonly Color SUBTAB_SEL = new Color(0.25f, 0.5f, 0.25f, 1f);
        static readonly Color SUBTAB_OFF = new Color(0.2f, 0.2f, 0.2f, 1f);

        // filters (none selected = show all), can combine. sort mode.
        bool _filterGhost, _filterCreative, _filterUnity, _filterFav;
        Button _filterBtn;
        enum SortMode { Time, Name, Date }
        SortMode _sort = SortMode.Date;
        bool _sortDesc = true; // date defaults newest first; time defaults fastest first (asc); name a→z (asc)

        void UpdateFilterBtnColor()
        {
            if (_filterBtn == null) return;
            bool any = _filterGhost || _filterCreative || _filterUnity || _filterFav;
            var lbl = _filterBtn.GetComponentInChildren<Text>();
            if (lbl != null) lbl.color = any ? new Color(1f, 0.85f, 0.2f) : Color.white;
        }

        // PB lists can get big and every row used to load a splash thumbnail up front — that's the
        // perf hazard. now we keep lightweight DATA for every pb and only build GameObjects for the
        // current page, so live objects/textures stay bounded no matter how many pbs you have.
        class RowData { public string name; public string rawId; public string haystack; public bool hasGhost; public bool isUgc; public bool isFav; public string date; public string solosDate, duosDate, squadsDate; public float? solos, duos, squads;
            public float? TimeFor(PbType t) => t == PbType.Solos ? solos : t == PbType.Duos ? duos : squads;
            public string DateFor(PbType t)
            {
                string d = t == PbType.Solos ? solosDate : t == PbType.Duos ? duosDate : squadsDate;
                return string.IsNullOrEmpty(d) ? date : d;
            } }
        readonly List<RowData> _data = new List<RowData>();   // all pbs
        List<RowData> _filtered = new List<RowData>();         // current show + search + filters + sort
        int _page = 0;
        const int PAGE_SIZE = 25;
        Button _prevBtn, _nextBtn;

        // pb dates are stored as "yyyy-MM-dd HH:mm:ss" (24h, invariant). for display we honor the
        // user's regional preference so someone on a 12h locale gets am/pm. fall back to the raw
        // string if it doesn't parse.
        static string FormatDateForDisplay(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return raw;
            if (System.DateTime.TryParseExact(raw, "yyyy-MM-dd HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dt))
                return dt.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
            if (System.DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out dt))
                return dt.ToString("g", System.Globalization.CultureInfo.CurrentCulture);
            return raw;
        }

        static Texture2D LoadTex(string resource, ref Texture2D cache)
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
            catch (Exception ex) { Debug.LogError("[BetterFG] Tex load failed: " + ex.Message); }
            return cache;
        }

        protected override void BuildBackground(RectTransform root)
        {
            var bgTex = LoadTex("BetterFG.assets.ui.tab.pb.png", ref _bgTex);
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

        protected override void OnTitleHoverChanged(bool hovering)
        {
            if (_bgHoverGo != null) _bgHoverGo.SetActive(hovering);
        }

        protected override void BuildContent(RectTransform contentRoot)
        {
            float w = TabWidth - PAD * 2f;
            float y = PAD;

            // header row: search (left) + Filters + Sort dropdowns + dir toggle + refresh (right)
            float ddW = 56f;
            float refreshW = 24f;
            float dirW = 24f;
            float searchW = w - (ddW + PAD) * 2f - (dirW + PAD) - (refreshW + PAD);

            _searchField = UGUIShip.CreateInputField(contentRoot, new Rect(PAD, y, searchW, HEADER_H),
                "search personal bests...", new Color(0f, 0f, 0f, 0.4f), Color.white, FS_SM);
            _searchField.onValueChanged.AddListener(new Action<string>(val =>
            {
                _query = val ?? "";
                ApplySearch();
            }));

            float ddX = PAD + searchW + PAD;
            float listW = 120f; // fixed, wider than the little header button

            // Filters: multi-select with checkmarks. button label stays "Filters", goes yellow when any on.
            _filterBtn = UGUIShip.CreateMultiSelectDropdown(contentRoot, new Rect(ddX, y, ddW, HEADER_H),
                "Filters",
                new List<string> { "Favorited", "Has ghost", "Creative level", "Unity level" },
                new List<bool> { _filterFav, _filterGhost, _filterCreative, _filterUnity },
                new Action<int, bool>((i, on) =>
                {
                    if (i == 0) _filterFav = on;
                    else if (i == 1) _filterGhost = on;
                    else if (i == 2) _filterCreative = on;
                    else if (i == 3) _filterUnity = on;
                    UpdateFilterBtnColor();
                    ApplySearch();
                }), FS_SM, listW);
            AddHeaderIcon(_filterBtn, "BetterFG.assets.ui.button.filter.png");

            // Sort: single-select, same control/look as Filters (radio checkmarks, closes on pick)
            var sortBtn = UGUIShip.CreateMultiSelectDropdown(contentRoot, new Rect(ddX + ddW + PAD, y, ddW, HEADER_H),
                "Sort",
                new List<string> { "Sort by time", "Sort by name", "Sort by date" },
                new List<bool> { _sort == SortMode.Time, _sort == SortMode.Name, _sort == SortMode.Date },
                new Action<int, bool>((i, on) =>
                {
                    _sort = i == 1 ? SortMode.Name : i == 2 ? SortMode.Date : SortMode.Time;
                    SortRows();
                }), FS_SM, listW, 20f, true, true);
            AddHeaderIcon(sortBtn, "BetterFG.assets.ui.button.sort.png");

            // direction toggle: ↑ asc / ↓ desc, flips the active sort
            Button dirBtn = null;
            dirBtn = UGUIShip.CreateButton(contentRoot,
                new Rect(ddX + (ddW + PAD) * 2f, y, dirW, HEADER_H),
                _sortDesc ? "↓" : "↑", SUBTAB_OFF, Color.white, FS_SM, new Action(() =>
                {
                    _sortDesc = !_sortDesc;
                    var lbl = dirBtn.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.text = _sortDesc ? "↓" : "↑";
                    SortRows();
                }));
            var dirTxt = dirBtn.GetComponentInChildren<Text>();
            if (dirTxt != null)
            {
                dirTxt.fontSize = FS + 2;
                dirTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
                dirTxt.verticalOverflow = VerticalWrapMode.Overflow;
            }

            // refresh — re-reads the store from disk. needed because the popup/qual screen mutates
            // pbs after this tab is built, and pages are diffed off _data which we cache.
            var refreshBtn = UGUIShip.CreateButton(contentRoot,
                new Rect(ddX + (ddW + PAD) * 2f + dirW + PAD, y, refreshW, HEADER_H),
                "↻", SUBTAB_OFF, Color.white, FS_SM, new Action(BuildList));
            // bump the glyph and let it overflow the small button rect instead of clipping away
            var refreshTxt = refreshBtn.GetComponentInChildren<Text>();
            if (refreshTxt != null)
            {
                refreshTxt.fontSize = FS + 6;
                refreshTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
                refreshTxt.verticalOverflow = VerticalWrapMode.Overflow;
            }

            y += HEADER_H + SH;

            // solos / duos / squads sub-tab bar
            float subH = BTN_H * 0.9f;
            float thirdW = (w - PAD) / 3f;
            _btnSolos = UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, thirdW, subH), "Solos",
                _subType == PbType.Solos ? SUBTAB_SEL : SUBTAB_OFF, Color.white, FS_SM,
                new Action(() => SetSubType(PbType.Solos)));
            _btnDuos = UGUIShip.CreateButton(contentRoot, new Rect(PAD + thirdW + PAD * 0.5f, y, thirdW, subH), "Duos",
                _subType == PbType.Duos ? SUBTAB_SEL : SUBTAB_OFF, Color.white, FS_SM,
                new Action(() => SetSubType(PbType.Duos)));
            _btnSquads = UGUIShip.CreateButton(contentRoot, new Rect(PAD + (thirdW + PAD * 0.5f) * 2f, y, thirdW, subH), "Squads",
                _subType == PbType.Squads ? SUBTAB_SEL : SUBTAB_OFF, Color.white, FS_SM,
                new Action(() => SetSubType(PbType.Squads)));

            AddButtonOverlay(_btnSolos, "BetterFG.assets.ui.pb.solosbutton.png");
            AddButtonOverlay(_btnDuos, "BetterFG.assets.ui.pb.duosbutton.png");
            AddButtonOverlay(_btnSquads, "BetterFG.assets.ui.pb.squadsbutton.png");
            y += subH + SH;

            UGUIShip.CreateDivider(contentRoot, PAD, y, w);
            y += 1f + SH;

            // pager + status pinned to bottom, list fills the rest
            float statusH = UIScale.LH;
            float statusY = TabHeight - PAD - statusH;
            float listH = statusY - SH - y;

            var (_, content) = UGUIShip.CreateScrollView(contentRoot, new Rect(PAD, y, w, listH));
            _listContent = content;
            var vlg = _listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.spacing = 1f;
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            _listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit =
                ContentSizeFitter.FitMode.PreferredSize;

            // bottom bar: [< prev]   status   [next >]
            float pageBtnW = 52f;
            _prevBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD, statusY, pageBtnW, statusH), "‹ Prev",
                SUBTAB_OFF, Color.white, FS_SM, new Action(() => { _page--; RenderPage(); }));
            _nextBtn = UGUIShip.CreateButton(contentRoot, new Rect(PAD + w - pageBtnW, statusY, pageBtnW, statusH), "Next ›",
                SUBTAB_OFF, Color.white, FS_SM, new Action(() => { _page++; RenderPage(); }));
            _statusLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD + pageBtnW, statusY, w - pageBtnW * 2f, statusH),
                "", FS_SM, HINT, TextAnchor.MiddleCenter);

            BuildList();

            // any pb mutation (new time set from the qual screen, fav/delete from popup or here)
            // refreshes the cached row data so the list reflects it without a manual ↻
            PBStore.OnChanged += BuildList;
        }

        void OnDestroy() { PBStore.OnChanged -= BuildList; }

        // switching shows is cheap now: rows are built once for every map, so we just re-show the
        // ones that have a time in the new show, rewrite their time text, re-sort and re-stripe.
        // drops an image overlay inside a sub-tab button, behind the text. not a raycast target so
        // it never steals the button's clicks. the label was added before this, so SetSiblingIndex(0)
        // puts the overlay underneath it.
        // small icon left of the centered label on the header dropdowns. square, sized to the label
        // glyph height so it lines up with the text regardless of font scale. shifts the label right
        // and parks the icon at the (shifted) label's left edge so icon+text read as one centered
        // block instead of the icon floating off in the left margin alone.
        static void AddHeaderIcon(Button btn, string resource)
        {
            if (btn == null) return;
            var sprite = EmbeddedResourceandUnity.LoadSprite(resource);
            if (sprite == null) return;
            var lbl = btn.GetComponentInChildren<Text>();
            float size = (lbl != null ? lbl.fontSize : FS_SM) * 0.75f;
            float gap = 3f;
            float shift = (size + gap) * 0.5f;

            if (lbl != null)
            {
                var lrt = lbl.GetComponent<RectTransform>();
                if (lrt != null) lrt.anchoredPosition += new Vector2(shift, 0f);
            }

            // build, then measure the text and place the icon at the label's left edge
            var go = new GameObject("HeaderIcon");
            go.transform.SetParent(btn.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;

            float textW = lbl != null ? lbl.preferredWidth : 0f;
            rt.anchoredPosition = new Vector2(shift - textW * 0.5f - gap, 0f);
        }

        static void AddButtonOverlay(Button btn, string resource)
        {
            if (btn == null) return;
            var sprite = EmbeddedResourceandUnity.LoadSprite(resource);
            if (sprite == null) return;

            var go = new GameObject("Overlay");
            go.transform.SetParent(btn.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            go.transform.SetSiblingIndex(0);
        }

        void SetSubType(PbType type)
        {
            _subType = type;
            UGUIShip.SetButtonSelected(_btnSolos, type == PbType.Solos, SUBTAB_SEL);
            UGUIShip.SetButtonSelected(_btnDuos, type == PbType.Duos, SUBTAB_SEL);
            UGUIShip.SetButtonSelected(_btnSquads, type == PbType.Squads, SUBTAB_SEL);
            ApplySearch();
        }

        // loads lightweight DATA for every pb. no GameObjects here — those are built per page in
        // RenderPage. only called on open and after a delete.
        void BuildList()
        {
            _data.Clear();
            var all = PBStore.GetAllEntriesDeduped();
            foreach (var kv in all)
            {
                var e = kv.Value;
                string label = string.IsNullOrEmpty(e.displayName) ? kv.Key : e.displayName;
                string rawId = e.rawId ?? kv.Key;
                _data.Add(new RowData
                {
                    name = label,
                    rawId = rawId,
                    haystack = (label + " " + kv.Key).ToLowerInvariant(),
                    hasGhost = rawId != null && FeatureQualificationTime.HasGhost(label, rawId),
                    isUgc = rawId != null && rawId.Contains("ugc-"),
                    isFav = PBStore.IsFeatured(rawId, label),
                    date = e.date,
                    solosDate = e.solosDate,
                    duosDate = e.duosDate,
                    squadsDate = e.squadsDate,
                    solos = e.solos,
                    duos = e.duos,
                    squads = e.squads
                });
            }
            ApplySearch();
        }

        // recompute the filtered+sorted subset for the active show, then render the current page.
        void ApplySearch()
        {
            string q = (_query ?? "").ToLowerInvariant();
            bool searching = q.Length > 0;

            var matched = _data.Where(r =>
            {
                if (!r.TimeFor(_subType).HasValue) return false; // belongs to the active show only
                if (searching && !r.haystack.Contains(q)) return false;
                if (_filterFav && !r.isFav) return false;
                if (_filterGhost && !r.hasGhost) return false;
                if (_filterCreative && !r.isUgc) return false;
                if (_filterUnity && r.isUgc) return false; // unity = non-creative
                return true;
            });

            // sort: direction flips around each field's natural meaning.
            // time: asc = fastest first. name: asc = a→z. date: desc = newest first (we store ISO-ish strings so lex order == chrono).
            IOrderedEnumerable<RowData> ordered;
            if (_sort == SortMode.Name)
                ordered = _sortDesc
                    ? matched.OrderByDescending(r => r.name, StringComparer.OrdinalIgnoreCase)
                    : matched.OrderBy(r => r.name, StringComparer.OrdinalIgnoreCase);
            else if (_sort == SortMode.Date)
                ordered = _sortDesc
                    ? matched.OrderByDescending(r => r.DateFor(_subType) ?? "")
                    : matched.OrderBy(r => r.DateFor(_subType) ?? "");
            else
                ordered = _sortDesc
                    ? matched.OrderByDescending(r => r.TimeFor(_subType) ?? float.MinValue)
                    : matched.OrderBy(r => r.TimeFor(_subType) ?? float.MaxValue);
            _filtered = ordered.ToList();

            _page = 0;
            RenderPage();
        }

        void SortRows() => ApplySearch();

        // build GameObjects for just the current page's slice of _filtered
        void RenderPage()
        {
            if (_listContent == null) return;
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                GameObject.Destroy(_listContent.GetChild(i).gameObject);

            int total = _filtered.Count;
            int pageCount = Math.Max(1, (total + PAGE_SIZE - 1) / PAGE_SIZE);
            _page = Mathf.Clamp(_page, 0, pageCount - 1);
            int start = _page * PAGE_SIZE;
            int end = Math.Min(start + PAGE_SIZE, total);

            for (int i = start; i < end; i++)
            {
                var r = _filtered[i];
                float t = r.TimeFor(_subType) ?? 0f;
                var go = BuildRow(r, t, out _);
                var bg = go.GetComponent<Image>();
                if (bg != null) bg.color = ((i - start) % 2 == 0) ? ROW_ALT : ROW_CLEAR;
            }

            // pager + status
            if (_prevBtn != null) _prevBtn.gameObject.SetActive(pageCount > 1);
            if (_nextBtn != null) _nextBtn.gameObject.SetActive(pageCount > 1);
            if (_statusLbl != null)
            {
                if (_data.Count == 0) _statusLbl.text = "no personal bests recorded yet";
                else if (total == 0) _statusLbl.text = "no results";
                else if (pageCount > 1) _statusLbl.text = string.Format("{0} pbs  ·  page {1}/{2}", total, _page + 1, pageCount);
                else _statusLbl.text = total + (total == 1 ? " pb" : " pbs");
            }
        }

        GameObject BuildRow(RowData row, float time, out Text timeTxtOut)
        {
            string name = row.name;
            string rawId = row.rawId;
            var rowGo = new GameObject("PBRow");
            rowGo.transform.SetParent(_listContent, false);
            rowGo.AddComponent<RectTransform>().sizeDelta = new Vector2(0f, ROW_H);
            var le = rowGo.AddComponent<LayoutElement>();
            le.preferredHeight = ROW_H;
            le.flexibleWidth = 1f;
            // flat bg (no sprite) so the zebra alpha reads cleanly; ApplySearch sets the actual color
            var rowImg = rowGo.AddComponent<Image>();
            rowImg.color = ROW_CLEAR;

            float thumbW = ROW_H * 2.4f;
            float starW = 26f, delW = 24f;
            float rowW = TabWidth - PAD * 2f;

            // thumbnail (left) — wider, masked to the row's height so the round splash's top/bottom
            // gets cut off. mask container is the row height, the image inside is taller and
            // overflows, then RectMask2D clips it.
            var thumb = SplashCache.LoadCached(rawId, name);
            if (thumb != null)
            {
                var maskGo = new GameObject("Thumb");
                maskGo.transform.SetParent(rowGo.transform, false);
                var mRt = maskGo.AddComponent<RectTransform>();
                mRt.anchorMin = new Vector2(0f, 0f);
                mRt.anchorMax = new Vector2(0f, 1f);
                mRt.pivot = new Vector2(0f, 0.5f);
                mRt.anchoredPosition = new Vector2(0f, 0f);
                mRt.sizeDelta = new Vector2(thumbW, 0f);
                maskGo.AddComponent<RectMask2D>();

                var imgGo = new GameObject("Img");
                imgGo.transform.SetParent(maskGo.transform, false);
                var iRt = imgGo.AddComponent<RectTransform>();
                iRt.anchorMin = Vector2.zero;
                iRt.anchorMax = Vector2.one;
                iRt.pivot = new Vector2(0.5f, 0.5f);
                // image keeps its aspect by filling the width; the extra height bleeds past the
                // row and gets clipped by the mask above
                float imgH = thumbW / ((float)thumb.width / thumb.height);
                iRt.offsetMin = new Vector2(0f, -(imgH - ROW_H) * 0.5f);
                iRt.offsetMax = new Vector2(0f, (imgH - ROW_H) * 0.5f);
                var raw = imgGo.AddComponent<RawImage>();
                raw.texture = thumb;
                raw.raycastTarget = false;
            }

            // time, anchored top-right (above the star/delete), bigger than the rest of the row text
            float timeW = 80f;
            var t = TimeSpan.FromSeconds(time);
            string timeStr = string.Format("{0:D2}:{1:D2}.{2:D3}", t.Minutes, t.Seconds, t.Milliseconds);
            var timeGo = new GameObject("Time");
            timeGo.transform.SetParent(rowGo.transform, false);
            var tRt = timeGo.AddComponent<RectTransform>();
            tRt.anchorMin = new Vector2(1f, 1f);
            tRt.anchorMax = new Vector2(1f, 1f);
            tRt.pivot = new Vector2(1f, 1f);
            tRt.anchoredPosition = new Vector2(-(starW + delW + 8f), -3f);
            tRt.sizeDelta = new Vector2(timeW, FS + 4f);
            var tTxt = timeGo.AddComponent<Text>();
            tTxt.text = timeStr;
            tTxt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            tTxt.fontSize = FS;
            tTxt.color = TIME_COL;
            tTxt.alignment = TextAnchor.UpperRight;
            tTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            tTxt.raycastTarget = false;
            timeTxtOut = tTxt;

            // ugc/creative share code, pulled out of the raw id (e.g. "...ugc-1234_5678" -> "1234")
            string ugcCode = null;
            if (rawId != null && rawId.Contains("ugc-"))
            {
                int idx = rawId.IndexOf("ugc-");
                ugcCode = rawId.Substring(idx + 4);
                int cut = ugcCode.IndexOf('_');
                if (cut >= 0) ugcCode = ugcCode.Substring(0, cut);
            }

            // name + optional code, anchored to the bottom of the text column. the time used to live
            // bottom-right; now it's top-right, so the name has the whole bottom line to itself and
            // only needs to leave room for the star/delete buttons.
            float textX = (thumb != null ? thumbW + 6f : 4f);
            float textW = rowW - textX - starW - delW - 12f;
            bool hasGhost = rawId != null && FeatureQualificationTime.HasGhost(name, rawId);

            // date+time the pb was set, tiny + dim, pinned to the top-left of the text column.
            // per-show: shows the date for the active sub-tab, not the entry's shared date.
            string rowDate = row.DateFor(_subType);
            if (!string.IsNullOrEmpty(rowDate))
            {
                var dateGo = new GameObject("Date");
                dateGo.transform.SetParent(rowGo.transform, false);
                var dRt2 = dateGo.AddComponent<RectTransform>();
                dRt2.anchorMin = new Vector2(0f, 1f);
                dRt2.anchorMax = new Vector2(0f, 1f);
                dRt2.pivot = new Vector2(0f, 1f);
                dRt2.anchoredPosition = new Vector2(textX, -3f);
                dRt2.sizeDelta = new Vector2(textW, FS_SM);
                var dTxt = dateGo.AddComponent<Text>();
                dTxt.text = FormatDateForDisplay(rowDate);
                dTxt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                dTxt.fontSize = FS_SM - 2;
                dTxt.color = new Color(1f, 1f, 1f, 0.4f);
                dTxt.alignment = TextAnchor.UpperLeft;
                dTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
                dTxt.verticalOverflow = VerticalWrapMode.Truncate;
                dTxt.raycastTarget = false;
            }

            // masked container at the column width; RectMask2D clips DESCENDANT graphics, so the
            // name Text goes INSIDE it (not on the same object) and gets hard-cut at the column edge
            // instead of running over the time
            var nameMask = new GameObject("NameMask");
            nameMask.transform.SetParent(rowGo.transform, false);
            var nmRt = nameMask.AddComponent<RectTransform>();
            nmRt.anchorMin = new Vector2(0f, 0f);
            nmRt.anchorMax = new Vector2(0f, 0f);
            nmRt.pivot = new Vector2(0f, 0f);
            nmRt.anchoredPosition = new Vector2(textX, 3f);
            nmRt.sizeDelta = new Vector2(textW, FS_SM + 4f);
            nameMask.AddComponent<RectMask2D>();

            var nameGo = new GameObject("Name");
            nameGo.transform.SetParent(nameMask.transform, false);
            var nRt = nameGo.AddComponent<RectTransform>();
            nRt.anchorMin = new Vector2(0f, 0f);
            nRt.anchorMax = new Vector2(0f, 1f);
            nRt.pivot = new Vector2(0f, 0f);
            nRt.anchoredPosition = Vector2.zero;
            // wider than the mask so a long name overflows to the right and gets clipped
            nRt.sizeDelta = new Vector2(textW + 400f, 0f);
            var nTxt = nameGo.AddComponent<Text>();
            nTxt.text = name;
            nTxt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            nTxt.fontSize = FS_SM;
            nTxt.color = hasGhost ? GHOST_COL : Color.white;
            nTxt.alignment = TextAnchor.LowerLeft;
            nTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
            nTxt.verticalOverflow = VerticalWrapMode.Overflow;
            nTxt.raycastTarget = false;

            if (ugcCode != null)
            {
                var codeGo = new GameObject("Code");
                codeGo.transform.SetParent(rowGo.transform, false);
                var cRt = codeGo.AddComponent<RectTransform>();
                cRt.anchorMin = new Vector2(0f, 0f);
                cRt.anchorMax = new Vector2(0f, 0f);
                cRt.pivot = new Vector2(0f, 0f);
                cRt.anchoredPosition = new Vector2(textX, 3f + FS_SM + 3f);
                cRt.sizeDelta = new Vector2(textW, FS_SM);
                var cTxt = codeGo.AddComponent<Text>();
                cTxt.text = ugcCode;
                cTxt.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                cTxt.fontSize = FS_SM - 2;
                cTxt.color = new Color(1f, 1f, 1f, 0.4f);
                cTxt.alignment = TextAnchor.LowerLeft;
                cTxt.horizontalOverflow = HorizontalWrapMode.Overflow;
                cTxt.verticalOverflow = VerticalWrapMode.Truncate;
                cTxt.raycastTarget = false;
            }

            // favorite star — same sprites the popup uses, toggles on click
            var starOn = StarOn;
            var starOff = StarOff;
            var (_, starImg) = UGUIShip.CreateSpriteButton(rowGo.transform,
                new Rect(0f, 0f, starW, starW), row.isFav ? starOn : starOff, null, null);
            var sRt = starImg.GetComponent<RectTransform>();
            sRt.anchorMin = sRt.anchorMax = new Vector2(1f, 0.5f);
            sRt.pivot = new Vector2(1f, 0.5f);
            sRt.anchoredPosition = new Vector2(-(delW + 6f), 0f);
            starImg.GetComponent<Button>().onClick.AddListener(new Action(() =>
            {
                bool now = PBStore.TryFeature(rawId, name);
                row.isFav = now;
                var spr = now ? starOn : starOff;
                if (spr != null) starImg.sprite = spr;
                // if the favorited filter is on, an unfavorited row should drop out
                if (_filterFav && !now) ApplySearch();
            }));

            // delete — idle + hover sprite swap
            var (_, delImg) = UGUIShip.CreateSpriteButton(rowGo.transform,
                new Rect(0f, 0f, delW, delW), DelIdle, DelHover, new Action(() =>
                {
                    if (PBStore.TryDelete(rawId, name))
                    {
                        FeatureQualificationTime.DeleteGhost(name, rawId);
                        BuildList();
                    }
                }));
            var dRt = delImg.GetComponent<RectTransform>();
            dRt.anchorMin = dRt.anchorMax = new Vector2(1f, 0.5f);
            dRt.pivot = new Vector2(1f, 0.5f);
            dRt.anchoredPosition = new Vector2(-3f, 0f);

            return rowGo;
        }
    }
}
