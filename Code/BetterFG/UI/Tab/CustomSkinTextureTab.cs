using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Services;
using BetterFG.Customization.Player;
using UnityEngine;
using UnityEngine.UI;
using BetterFG.UI.Windows;
using FG.Common;
using FG.Common.CMS;
using LayoutElement = UnityEngine.UI.LayoutElement;

namespace BetterFG.UI.Tab
{
    // one user-created texture override entry
    public class SkinTexEntry
    {
        public string entryName;
        public string texPath;
        public int matIdx;
        public bool enabled;
        public string costumeName;

        public List<Material> mats = new List<Material>();
        public List<string> matNames = new List<string>();
    }

    public class CustomSkinTextureTab : BetterFGTab
    {
        public CustomSkinTextureTab(IntPtr ptr) : base(ptr) { }

        public override string TabTitle => "Skin Texture";

        private static float PAD => UIScale.PAD;
        private static float VPAD => UIScale.VPAD;
        private static float LH => UIScale.LH;
        private static float SH => UIScale.SH;
        private static float BTN_H => UIScale.BTN_H;
        private static int FS => UIScale.FS;
        private static int FS_SM => UIScale.FS_SM;

        private static readonly Color BTN_DARK = new Color(0.2f, 0.2f, 0.2f, 1f);
        private static readonly Color BTN_APPLY = new Color(0.25f, 0.45f, 0.25f, 1f);
        private static readonly Color BTN_REMOVE = new Color(0.55f, 0.15f, 0.15f, 1f);
        private static readonly Color BTN_PICK = new Color(0.2f, 0.3f, 0.45f, 1f);
        private static readonly Color BTN_ADD = new Color(0.3f, 0.3f, 0.15f, 1f);
        private static readonly Color BTN_EDIT_OPEN = new Color(0.45f, 0.38f, 0.1f, 1f); // dark yellow = form open on this row
        private static readonly Color SEL_COLOR = new Color(0.25f, 0.45f, 0.25f, 1f);
        private static readonly Color HINT = new Color(1f, 1f, 1f, 0.35f);
        private static readonly Color WHITE = Color.white;
        private static readonly Color ROW_ON = new Color(0.14f, 0.14f, 0.14f, 1f);
        private static readonly Color ROW_OFF = new Color(0.08f, 0.08f, 0.08f, 1f);

        private const float ROW_H = 18f; // shorter rows

        // ── bg ───────────────────────────────────────────────────────────────
        private static Texture2D _bgTex;
        private static Texture2D _hoverTex;
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
            catch (Exception ex) { Debug.LogError("[CustomSkinTex] tex load fail: " + ex.Message); }
            return cache;
        }

        protected override void BuildBackground(RectTransform root)
        {
            var bgTex = LoadTex("BetterFG.assets.ui.tab.customskintexture.png", ref _bgTex);
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
                var hRt = hoverGo.AddComponent<RectTransform>();
                hRt.anchorMin = Vector2.zero;
                hRt.anchorMax = Vector2.one;
                hRt.offsetMin = hRt.offsetMax = Vector2.zero;
                hoverGo.AddComponent<RawImage>().texture = hoverTex;
                hoverGo.SetActive(false);
                _bgHoverGo = hoverGo;
            }
        }

        protected override void OnTitleHoverChanged(bool hovering)
        {
            if (_bgHoverGo != null) _bgHoverGo.SetActive(hovering);
        }

        // ── settings keys ─────────────────────────────────────────────────────
        private const string KEY_ENTRY_COUNT = "skintex.entryCount";
        private static string EK(int i, string f) => $"skintex.entry.{i}.{f}";

        // ── instance ──────────────────────────────────────────────────────────
        public static CustomSkinTextureTab Instance { get; private set; }
        void Awake() => Instance = this;

        private float _tickTimer;
        private const float TICK_INTERVAL = 0.1f;
        void Update()
        {
            _tickTimer += Time.deltaTime;
            if (_tickTimer < TICK_INTERVAL) return;
            _tickTimer = 0f;
            WinDialogs.Tick();
        }

        // ── state ─────────────────────────────────────────────────────────────
        private List<SkinTexEntry> _entries = new List<SkinTexEntry>();
        private int _selectedEntry = -1;

        // mats read off the last cached costume clone. we destroy the clone right after reading so it
        // doesn't get left parked in the scene (creative round load/unload re-enables parked objects,
        // making headless CH_xxx(Clone) corpses pop back into the level). these hold the result.
        private readonly List<Material> _cachedMats = new List<Material>();
        private readonly List<string> _cachedMatNames = new List<string>();
        private string _cachedCostumeName = "";
        private bool _formCachedCostume = false;

        // ── ui refs ───────────────────────────────────────────────────────────
        private RectTransform _entryContent;
        private Text _statusLbl;

        // add/edit form — shared for both add and edit
        private GameObject _addFormGo;
        private InputField _addNameField;
        private Text _addTexPathLbl;
        private string _addTexPath = "";
        private Texture2D _addTexLoaded;
        private RawImage _addTexPreview;
        private Dropdown _addMatDropdown;
        private bool _editMode = false; // true = editing _selectedEntry, false = adding new
        private Text _addFormTitleLbl;
        private Button _confirmBtn;

        // ── build ─────────────────────────────────────────────────────────────
        protected override void BuildContent(RectTransform contentRoot)
        {
            LoadEntries();

            float w = TabWidth - PAD * 2f;
            float y = VPAD;

            UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, w, BTN_H),
                "+ Add Texture", BTN_ADD, WHITE, FS, new Action(OnAddEntryClicked));
            y += BTN_H + 2f;

            BuildScrollView("EntryScroll", contentRoot, PAD, y, w, 122f, out _entryContent);
            y += 122f + 2f;

            float halfW = (w - PAD * 0.5f) / 2f;
            UGUIShip.CreateButton(contentRoot, new Rect(PAD, y, halfW, BTN_H),
                "Apply Selected", BTN_APPLY, WHITE, FS_SM, new Action(OnApplySelected));
            UGUIShip.CreateButton(contentRoot, new Rect(PAD + halfW + PAD * 0.5f, y, halfW, BTN_H),
                "Revert All", BTN_REMOVE, WHITE, FS_SM, new Action(OnRevert));
            y += BTN_H + 2f;

            // ── ADD/EDIT FORM (hidden by default) ─────────────────────────────
            _addFormGo = BuildAddForm(contentRoot, PAD, y, w);
            _addFormGo.SetActive(false);
            float formH = 178f;
            y += formH + 2f;

            _statusLbl = UGUIShip.CreateLabel(contentRoot, new Rect(PAD, y, w, LH), "", FS_SM, HINT, TextAnchor.MiddleCenter);

            RefreshEntryList();
        }

        // ── entry list ────────────────────────────────────────────────────────
        private void RefreshEntryList()
        {
            if (_entryContent == null) return;

            for (int i = _entryContent.childCount - 1; i >= 0; i--)
            {
                var ch = _entryContent.GetChild(i);
                if (ch != null) GameObject.Destroy(ch.gameObject);
            }

            if (_entries.Count == 0)
            {
                UGUIShip.CreateLabel(_entryContent,
                    new Rect(6f, 0f, TabWidth, ROW_H), "no entries — click + Add", FS_SM, HINT, TextAnchor.MiddleLeft);
                return;
            }

            for (int i = 0; i < _entries.Count; i++)
            {
                int idx = i;
                var entry = _entries[i];
                bool isSel = idx == _selectedEntry;

                // row button via UGUIShip — select/deselect on click
                Color rowBase = isSel ? SEL_COLOR : (entry.enabled ? ROW_ON : ROW_OFF);
                var rowGo = new GameObject("ERow_" + i);
                rowGo.transform.SetParent(_entryContent, false);
                var rowRt = rowGo.AddComponent<RectTransform>();
                rowRt.sizeDelta = new Vector2(0f, ROW_H);
                var le = rowGo.AddComponent<LayoutElement>();
                le.preferredHeight = ROW_H;
                le.flexibleWidth = 1f;
                var rowImg = rowGo.AddComponent<Image>();
                rowImg.color = rowBase;

                // use UGUIShip button sprite / audio on the row
                var btnSpr = UGUIShip.GetButtonSprite();
                if (btnSpr != null) { rowImg.sprite = btnSpr; rowImg.type = Image.Type.Simple; }

                var rowBtn = rowGo.AddComponent<Button>();
                var cols = rowBtn.colors;
                cols.normalColor = rowBase;
                cols.highlightedColor = isSel ? SEL_COLOR * 1.15f : new Color(0.22f, 0.22f, 0.22f, 1f);
                cols.pressedColor = rowBase * 0.7f;
                cols.colorMultiplier = 1f;
                rowBtn.colors = cols;
                var nav = rowBtn.navigation;
                nav.mode = UnityEngine.UI.Navigation.Mode.None;
                rowBtn.navigation = nav;
                rowBtn.onClick.AddListener(new Action(() => SelectEntry(idx)));
                rowBtn.transition = Selectable.Transition.None;

                UGUIShip.WireButtonAudio(rowGo);
                var rowTrigger = rowGo.GetComponent<UnityEngine.EventSystems.EventTrigger>();
                if (rowTrigger != null) GameObject.Destroy(rowTrigger);

                // side action buttons — using UGUIShip.CreateButton with anchored positioning
                float editW = 26f, toggleW = 26f, removeW = 20f;
                float nameW = TabWidth - PAD * 2f - editW - toggleW - removeW - 12f;

                UGUIShip.CreateLabel(rowGo.transform,
                    new Rect(4f, 0f, nameW, ROW_H), entry.entryName,
                    FS_SM, entry.enabled ? WHITE : HINT, TextAnchor.MiddleLeft);

                bool editOpen = _addFormGo != null && _addFormGo.activeSelf && _editMode && _selectedEntry == idx;
                BuildRowUGUIBtn(rowGo.transform, -(removeW + toggleW + editW + 4f), ROW_H, editW,
                    "edit", editOpen ? BTN_EDIT_OPEN : BTN_DARK, () => OnEditEntry(idx));

                BuildRowUGUIBtn(rowGo.transform, -(removeW + toggleW + 2f), ROW_H, toggleW,
                    entry.enabled ? "on" : "off",
                    entry.enabled ? BTN_APPLY : BTN_DARK,
                    () => ToggleEntry(idx));

                BuildRowUGUIBtn(rowGo.transform, -2f, ROW_H, removeW,
                    "x", BTN_REMOVE, () => RemoveEntry(idx));
            }
        }

        // anchor-right button using UGUIShip button sprite + audio
        private void BuildRowUGUIBtn(Transform parent, float anchoredX, float rowH, float bw,
            string label, Color bg, Action onClick)
        {
            var go = new GameObject("RBtn_" + label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(1f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(anchoredX, 0f);
            rt.sizeDelta = new Vector2(bw, rowH - 4f);

            var img = go.AddComponent<Image>();
            img.color = bg;
            var btnSpr = UGUIShip.GetButtonSprite();
            if (btnSpr != null) { img.sprite = btnSpr; img.type = Image.Type.Simple; }

            var btn = go.AddComponent<Button>();
            var cols = btn.colors;
            cols.normalColor = bg;
            cols.highlightedColor = bg * 1.2f;
            cols.pressedColor = bg * 0.7f;
            cols.colorMultiplier = 1f;
            btn.colors = cols;
            var nav = btn.navigation;
            nav.mode = UnityEngine.UI.Navigation.Mode.None;
            btn.navigation = nav;
            btn.onClick.AddListener(new Action(onClick));

            UGUIShip.WireButtonAudio(go);

            UGUIShip.CreateLabel(go.transform,
                new Rect(0f, 0f, bw, rowH - 4f), label, FS_SM - 1, WHITE, TextAnchor.MiddleCenter);
        }

        private void SelectEntry(int idx)
        {
            if (_selectedEntry == idx)
            {
                _selectedEntry = -1;
                RefreshEntryList();
                return;
            }
            _selectedEntry = idx;
            RefreshEntryList();
            SetStatus(_entries[idx].entryName + " selected");
        }

        private void ToggleEntry(int idx)
        {
            _entries[idx].enabled = !_entries[idx].enabled;
            SaveEntries();
            RefreshEntryList();
            RevertAllEnabled();
        }

        private void RemoveEntry(int idx)
        {
            bool wasEnabled = _entries[idx].enabled;
            _entries.RemoveAt(idx);
            if (_selectedEntry >= _entries.Count) _selectedEntry = _entries.Count - 1;
            SaveEntries();
            RefreshEntryList();

            if (wasEnabled) RevertAllEnabled();
        }

        // ── add/edit form ─────────────────────────────────────────────────────
        private GameObject BuildAddForm(RectTransform parent, float x, float y, float w)
        {
            var formGo = new GameObject("AddForm");
            formGo.transform.SetParent(parent, false);
            var rt = formGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(rt, new Rect(x, y, w, 178f));
            formGo.AddComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.65f);

            float fx = 4f, fy = 4f, fw = w - 8f;
            var form = formGo.GetComponent<RectTransform>();

            _addFormTitleLbl = UGUIShip.CreateLabel(form, new Rect(fx, fy, fw, LH), "Name", FS_SM, HINT);
            fy += LH;
            float previewSz = 58f;
            float leftW = fw - previewSz - 6f;

            _addNameField = BuildInputField(form, fx, fy, leftW, "my texture override");
            fy += BTN_H + 2f;

            float bw = 60f;
            UGUIShip.CreateButton(form, new Rect(fx, fy, bw, BTN_H),
                "Browse", BTN_DARK, WHITE, FS_SM, new Action(OnAddBrowseTex));
            _addTexPathLbl = UGUIShip.CreateLabel(form,
                new Rect(fx + bw + 4f, fy, leftW - bw - 4f, BTN_H),
                "no file", FS_SM, HINT, TextAnchor.MiddleLeft);

            var previewGo = new GameObject("TexPreview");
            previewGo.transform.SetParent(form, false);
            var previewRt = previewGo.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(previewRt, new Rect(fx + fw - previewSz, fy - BTN_H - 2f, previewSz, previewSz));
            previewGo.AddComponent<Image>().color = Color.black;
            var rawGo = new GameObject("Raw");
            rawGo.transform.SetParent(previewGo.transform, false);
            var rawRt = rawGo.AddComponent<RectTransform>();
            rawRt.anchorMin = Vector2.zero;
            rawRt.anchorMax = Vector2.one;
            rawRt.offsetMin = rawRt.offsetMax = Vector2.zero;
            _addTexPreview = rawGo.AddComponent<RawImage>();
            _addTexPreview.raycastTarget = false;
            fy += BTN_H + 2f;

            float pickW = 104f;
            UGUIShip.CreateLabel(form, new Rect(fx, fy, fw - pickW - 4f, BTN_H), "Skin texture to change:", FS_SM, HINT);
            UGUIShip.CreateButton(form, new Rect(fx + fw - pickW, fy, pickW, BTN_H),
                "Choose", BTN_PICK, WHITE, FS_SM, new Action(OpenCostumeFetchWindow));
            fy += BTN_H + 2f;

            _addMatDropdown = BuildSimpleDropdown(form, fx, fy, fw);
            fy += BTN_H + 2f;

            _confirmBtn = UGUIShip.CreateButton(form, new Rect(fx, fy, fw, BTN_H),
                "Add", BTN_ADD, WHITE, FS_SM, new Action(OnConfirmForm));

            return formGo;
        }

        private void OnAddEntryClicked()
        {
            if (_addFormGo == null) return;
            bool isOpen = _addFormGo.activeSelf;
            if (isOpen && _editMode)
            {
                // was in edit mode, toggle to add mode
                SetFormToAddMode();
                RefreshEntryList();
                return;
            }
            if (isOpen)
            {
                _addFormGo.SetActive(false);
                return;
            }
            SetFormToAddMode();
            _addFormGo.SetActive(true);
        }

        private void OnEditEntry(int idx)
        {
            // toggle: clicking edit on the row whose form is already open closes it
            if (_addFormGo != null && _addFormGo.activeSelf && _editMode && _selectedEntry == idx)
            {
                _addFormGo.SetActive(false);
                RefreshEntryList();
                return;
            }
            _selectedEntry = idx;
            SetFormToEditMode(idx);
            _addFormGo.SetActive(true);
            RefreshEntryList();
        }

        private void SetFormToAddMode()
        {
            _editMode = false;
            _formCachedCostume = false;
            _cachedCostumeName = "";
            _cachedMats.Clear();
            _cachedMatNames.Clear();
            if (_addFormTitleLbl != null) _addFormTitleLbl.text = "Name";
            if (_confirmBtn != null)
            {
                var lbl = _confirmBtn.GetComponentInChildren<Text>();
                if (lbl != null) lbl.text = "Add";
                var img = _confirmBtn.GetComponent<Image>();
                if (img != null) img.color = BTN_ADD;
            }
            if (_addNameField != null) _addNameField.text = "";
            _addTexPath = "";
            if (_addTexPathLbl != null) _addTexPathLbl.text = "no file";
            if (_addTexPreview != null) _addTexPreview.texture = null;
            RefreshMatDropdown(null, 0);
        }

        private void SetFormToEditMode(int idx)
        {
            _editMode = true;
            _formCachedCostume = false;
            _cachedCostumeName = "";
            _cachedMats.Clear();
            _cachedMatNames.Clear();
            var entry = _entries[idx];
            if (_addFormTitleLbl != null) _addFormTitleLbl.text = "Edit Entry";
            if (_confirmBtn != null)
            {
                var lbl = _confirmBtn.GetComponentInChildren<Text>();
                if (lbl != null) lbl.text = "Save Changes";
                var img = _confirmBtn.GetComponent<Image>();
                if (img != null) img.color = BTN_APPLY;
            }
            if (_addNameField != null) _addNameField.text = entry.entryName;
            _addTexPath = entry.texPath;
            if (_addTexPathLbl != null)
                _addTexPathLbl.text = string.IsNullOrEmpty(entry.texPath) ? "no file" : Path.GetFileName(entry.texPath);

            if (!string.IsNullOrEmpty(entry.texPath) && File.Exists(entry.texPath))
            {
                try
                {
                    byte[] data = File.ReadAllBytes(entry.texPath);
                    if (_addTexLoaded == null) _addTexLoaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    _addTexLoaded.LoadImage(data);
                    _addTexLoaded.Apply();
                    if (_addTexPreview != null) _addTexPreview.texture = _addTexLoaded;
                }
                catch { }
            }

            RefreshMatDropdown(entry.matNames, entry.matIdx);
        }

        private void RefreshMatDropdown(List<string> names, int selected)
        {
            if (_addMatDropdown == null) return;
            _addMatDropdown.ClearOptions();
            if (names == null || names.Count == 0)
                _addMatDropdown.options.Add(new Dropdown.OptionData("(choose costume first)"));
            else
                foreach (var n in names)
                    _addMatDropdown.options.Add(new Dropdown.OptionData(n));
            _addMatDropdown.value = Mathf.Clamp(selected, 0, Mathf.Max(0, _addMatDropdown.options.Count - 1));
            _addMatDropdown.RefreshShownValue();
        }

        private void OpenCostumeFetchWindow()
        {
            SkinTextureCostumeWindow.Instance?.Close();
            var go = new GameObject("BetterFG_SkinTextureCostumeWindow");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            var win = go.AddComponent<SkinTextureCostumeWindow>();
            win.Configure(this);
        }

        private void OnAddBrowseTex()
        {
            WinDialogs.PickPng("Select PNG Texture", path =>
            {
                if (string.IsNullOrEmpty(path)) return;
                _addTexPath = path;
                if (_addTexPathLbl != null) _addTexPathLbl.text = Path.GetFileName(path);
                try
                {
                    byte[] data = File.ReadAllBytes(path);
                    if (_addTexLoaded == null) _addTexLoaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    _addTexLoaded.LoadImage(data);
                    _addTexLoaded.Apply();
                    if (_addTexPreview != null) _addTexPreview.texture = _addTexLoaded;
                }
                catch (Exception e) { SetStatus("preview fail: " + e.Message); }
            });
        }

        private void OnConfirmForm()
        {
            if (_editMode) OnConfirmEditEntry();
            else OnConfirmAddEntry();
        }

        private void OnConfirmAddEntry()
        {
            string entryName = _addNameField?.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(entryName)) { SetStatus("entry name can't be empty"); return; }
            if (string.IsNullOrEmpty(_addTexPath)) { SetStatus("pick a texture first"); return; }

            foreach (var e in _entries)
                if (e.entryName == entryName) { SetStatus("name already used"); return; }

            var entry = new SkinTexEntry
            {
                entryName = entryName,
                texPath = _addTexPath,
                matIdx = _addMatDropdown != null ? _addMatDropdown.value : 0,
                enabled = true,
                costumeName = _cachedCostumeName
            };

            if (_cachedMats.Count > 0)
            {
                entry.mats.AddRange(_cachedMats);
                entry.matNames.AddRange(_cachedMatNames);
            }

            _entries.Add(entry);
            SaveEntries();
            SetFormToAddMode();
            _addFormGo.SetActive(false);
            RefreshEntryList();
            SetStatus("added: " + entryName);
            RevertAllEnabled();
        }

        private void OnConfirmEditEntry()
        {
            if (_selectedEntry < 0 || _selectedEntry >= _entries.Count) return;

            string entryName = _addNameField?.text?.Trim() ?? "";
            if (string.IsNullOrEmpty(entryName)) { SetStatus("entry name can't be empty"); return; }

            // allow same name if it's this entry's own name
            for (int i = 0; i < _entries.Count; i++)
            {
                if (i == _selectedEntry) continue;
                if (_entries[i].entryName == entryName) { SetStatus("name already used"); return; }
            }

            var entry = _entries[_selectedEntry];
            bool wasEnabled = entry.enabled;

            entry.entryName = entryName;
            if (!string.IsNullOrEmpty(_addTexPath))
                entry.texPath = _addTexPath;
            if (_addMatDropdown != null)
                entry.matIdx = _addMatDropdown.value;

            // update mat cache only if this edit actually chose a costume.
            if (_formCachedCostume && _cachedMats.Count > 0)
            {
                entry.mats.Clear();
                entry.matNames.Clear();
                entry.mats.AddRange(_cachedMats);
                entry.matNames.AddRange(_cachedMatNames);
                entry.costumeName = _cachedCostumeName;
            }

            SaveEntries();
            _addFormGo.SetActive(false);
            RefreshEntryList();
            SetStatus("saved: " + entryName);

            if (wasEnabled && entry.enabled)
                RevertAllEnabled();
        }

        public void CacheCostumeFromWindow(CostumeOption option)
        {
            if (option == null) return;
            StartCoroutine(CacheCostumeRoutine(option).WrapToIl2Cpp());
        }

        private IEnumerator CacheCostumeRoutine(CostumeOption option)
        {
            SetStatus("caching...");

            GameObject instance = null;
            bool done = false;
            Exception err = null;

            try
            {
                var op = option.costumePrefabReference.InstantiateAsync();
                StartCoroutine(WaitForAsyncOp(op,
                    r => { instance = r; done = true; },
                    e => { err = e; done = true; }).WrapToIl2Cpp());
            }
            catch (Exception e) { SetStatus("instantiate fail: " + e.Message); yield break; }

            float elapsed = 0f;

            if (!done || instance == null)
            {
                SetStatus(err != null ? "err: " + err.Message : "timed out");
                yield break;
            }

            // read the mats off the clone, then kill it right away. we keep the Material refs (they
            // belong to the prefab/bundle, not the clone) but never keep the clone GameObject around.
            instance.SetActive(false);
            _cachedMats.Clear();
            _cachedMatNames.Clear();
            CollectAllMaterials(instance, _cachedMats, _cachedMatNames);
            GameObject.Destroy(instance);

            try { _cachedCostumeName = option.name ?? ""; } catch { _cachedCostumeName = ""; }
            _formCachedCostume = true;

            RefreshMatDropdown(_cachedMatNames, 0);

            // if editing, push into the selected entry too
            if (_editMode && _selectedEntry >= 0 && _selectedEntry < _entries.Count)
            {
                var entry = _entries[_selectedEntry];
                entry.mats.Clear();
                entry.matNames.Clear();
                entry.mats.AddRange(_cachedMats);
                entry.matNames.AddRange(_cachedMatNames);
                try { entry.costumeName = option.name ?? ""; } catch { }
                SaveEntries();
            }

            SetStatus($"cached {_cachedMats.Count} mat(s) from {GetDisplayName(option)}");
        }

        // ── apply / revert ────────────────────────────────────────────────────
        private void OnApplySelected()
        {
            if (_selectedEntry < 0 || _selectedEntry >= _entries.Count)
            {
                SetStatus("select an entry first");
                return;
            }
            var entry = _entries[_selectedEntry];
            if (!entry.enabled) { SetStatus("entry is disabled"); return; }
            RevertAllEnabled();
        }

        private void ApplyEntry(SkinTexEntry entry)
        {
            if (string.IsNullOrEmpty(entry.texPath)) { SetStatus("no tex path on entry"); return; }

            var svc = SkinApplicationService.Instance;
            if (svc == null) { SetStatus("SkinApplicationService not ready"); return; }

            Texture2D tex;
            try { tex = LoadTexCached(entry.texPath); }
            catch (Exception e) { SetStatus("tex load err: " + e.Message); return; }

            var matchNames = BuildMatchNames(entry);
            int total = 0;
            foreach (var bean in GatherBeans())
                total += svc.ApplyCustomTexture(bean, entry.matIdx, tex, matchNames);

            SetStatus(total > 0 ? $"applied {entry.entryName}" : "nothing matched");
        }

        // revert everything, then reapply all currently-enabled entries
        private void RevertAllEnabled()
            => ReapplyAllEnabledFromSettings(_entries, SetStatus);

        public static void ReapplyAllEnabledFromSettings()
            => ReapplyAllEnabledFromSettings(LoadEntriesFromSettings(), _ => { });

        private static void ReapplyAllEnabledFromSettings(List<SkinTexEntry> entries, Action<string> status)
        {
            var svc = SkinApplicationService.Instance;
            if (svc == null) return;

            foreach (var bean in GatherBeans())
                svc.RevertCustomTexture(bean);

            foreach (var entry in entries)
                if (entry.enabled) ApplyEntryStatic(entry, status);
        }

        // decoded textures cached by path so reapply (which fires on every view switch) doesn't
        // re-read + re-decode + re-upload the same png every time
        static readonly Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();

        static Texture2D LoadTexCached(string path)
        {
            if (_texCache.TryGetValue(path, out var cached) && cached != null) return cached;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(path));
            tex.Apply();
            _texCache[path] = tex;
            return tex;
        }

        // read + decode every enabled entry's png up front (at plugin load) so the first
        // auto-reapply on menu entry is a pure cache hit instead of a synchronous whole-file
        // read + png decode on the exact frame the menu appears.
        public static void PrewarmTextureCache()
        {
            foreach (var entry in LoadEntriesFromSettings())
            {
                if (!entry.enabled || string.IsNullOrEmpty(entry.texPath)) continue;
                try { LoadTexCached(entry.texPath); }
                catch (Exception e) { Debug.LogWarning($"[SkinTex] prewarm failed for {entry.texPath}: {e.Message}"); }
            }
        }

        private static void ApplyEntryStatic(SkinTexEntry entry, Action<string> status)
        {
            if (string.IsNullOrEmpty(entry.texPath)) return;

            var svc = SkinApplicationService.Instance;
            if (svc == null) return;

            Texture2D tex;
            try { tex = LoadTexCached(entry.texPath); }
            catch (Exception e) { status?.Invoke("tex load err: " + e.Message); return; }

            var matchNames = BuildMatchNames(entry);
            int total = 0;
            foreach (var bean in GatherBeans())
                total += svc.ApplyCustomTexture(bean, entry.matIdx, tex, matchNames);

            status?.Invoke(total > 0 ? $"applied {entry.entryName}" : "nothing matched");
        }

        private void OnRevert()
        {
            var svc = SkinApplicationService.Instance;
            if (svc == null) return;
            foreach (var bean in GatherBeans())
                svc.RevertCustomTexture(bean);
            SetStatus("reverted");
        }

        private static HashSet<string> BuildMatchNames(SkinTexEntry entry)
        {
            var matchNames = new HashSet<string>();
            if (entry.matNames.Count > 0 && entry.matIdx >= 0 && entry.matIdx < entry.matNames.Count)
            {
                var name = entry.matNames[entry.matIdx];
                if (!string.IsNullOrEmpty(name)) matchNames.Add(name);
            }

            if (matchNames.Count == 0 && entry.mats.Count > 0 && entry.matIdx >= 0 && entry.matIdx < entry.mats.Count)
            {
                var mat = entry.mats[entry.matIdx];
                if (mat != null)
                {
                    if (!string.IsNullOrEmpty(mat.name)) matchNames.Add(CleanMatName(mat.name));
                    foreach (var prop in new[] { "_MainTex", "_BaseMap", "_BaseTexture", "_MainTex2" })
                    {
                        try
                        {
                            if (mat.HasProperty(prop))
                            {
                                var t = mat.GetTexture(prop);
                                if (t != null && !string.IsNullOrEmpty(t.name)) { matchNames.Add(t.name); break; }
                            }
                        }
                        catch { }
                    }
                }
            }
            return matchNames;
        }

        private static string CleanMatName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.EndsWith(" (Instance)") ? name.Substring(0, name.Length - 11) : name;
        }

        private static List<GameObject> GatherBeans()
        {
            var beans = new List<GameObject>();
            if (BeanMonitorService.LocalPlayerBean != null)
                beans.Add(BeanMonitorService.LocalPlayerBean);
            foreach (var b in BeanMonitorService.GetTrackedBeans())
                if (b != null && !beans.Contains(b)) beans.Add(b);
            return beans;
        }

        // ── persist ───────────────────────────────────────────────────────────
        private void SaveEntries()
        {
            SettingsService.Set(KEY_ENTRY_COUNT, _entries.Count.ToString());
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                SettingsService.Set(EK(i, "name"), e.entryName);
                SettingsService.Set(EK(i, "texPath"), e.texPath);
                SettingsService.Set(EK(i, "matIdx"), e.matIdx.ToString());
                SettingsService.Set(EK(i, "enabled"), e.enabled ? "1" : "0");
                SettingsService.Set(EK(i, "costume"), e.costumeName);
                // pipe-joined so auto-reapply knows which texture name to match per slot
                SettingsService.Set(EK(i, "matNames"), string.Join("|", e.matNames));
            }
        }

        private void LoadEntries()
        {
            _entries.Clear();
            _entries.AddRange(LoadEntriesFromSettings());
        }

        private static List<SkinTexEntry> LoadEntriesFromSettings()
        {
            var entries = new List<SkinTexEntry>();
            if (!int.TryParse(SettingsService.Get(KEY_ENTRY_COUNT, "0"), out int count)) return entries;
            for (int i = 0; i < count; i++)
            {
                var e = new SkinTexEntry
                {
                    entryName = SettingsService.Get(EK(i, "name"), "entry " + i),
                    texPath = SettingsService.Get(EK(i, "texPath"), ""),
                    matIdx = 0,
                    enabled = SettingsService.Get(EK(i, "enabled"), "1") == "1",
                    costumeName = SettingsService.Get(EK(i, "costume"), "")
                };
                if (int.TryParse(SettingsService.Get(EK(i, "matIdx"), "0"), out int mi))
                    e.matIdx = mi;

                // restore matNames so BuildMatchNames works without recaching
                string matNamesRaw = SettingsService.Get(EK(i, "matNames"), "");
                if (!string.IsNullOrEmpty(matNamesRaw))
                {
                    foreach (var n in matNamesRaw.Split('|'))
                        if (!string.IsNullOrEmpty(n)) e.matNames.Add(n);
                }

                entries.Add(e);
            }
            return entries;
        }

        // ── shared helpers ────────────────────────────────────────────────────
        public static string GetDisplayName(CostumeOption option)
        {
            try { return option.CMSData.Name._text ?? option.name ?? ""; } catch { }
            try { return option.name ?? ""; } catch { }
            return "";
        }

        private void BuildScrollView(string goName, RectTransform parent,
            float x, float y, float w, float h, out RectTransform content)
        {
            var scroll = UGUIShip.CreateScrollView(parent, new Rect(x, y, w, h));
            content = scroll.content;

            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(2, 2, 2, 2);
            vlg.spacing = 2f;
            vlg.childControlHeight = false;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childForceExpandWidth = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private InputField BuildInputField(RectTransform parent, float x, float y, float w, string placeholder)
        {
            return UGUIShip.CreateInputField(parent, new Rect(x, y, w, BTN_H), placeholder,
                Color.black, WHITE, FS_SM);
        }

        private Dropdown BuildSimpleDropdown(RectTransform parent, float x, float y, float w)
        {
            var go = new GameObject("Dropdown");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            UGUIShip.SetPixelRect(rt, new Rect(x, y, w, BTN_H));

            var bg = go.AddComponent<Image>();
            bg.color = BTN_DARK;
            var btnSpr = UGUIShip.GetButtonSprite();
            if (btnSpr != null) { bg.sprite = btnSpr; bg.type = Image.Type.Simple; }

            var dd = go.AddComponent<Dropdown>();
            dd.transition = Selectable.Transition.None;
            dd.alphaFadeSpeed = 0f; // no fade in/out on the popup
            UGUIShip.WireButtonAudio(go);

            var lblGo = new GameObject("Label");
            lblGo.transform.SetParent(go.transform, false);
            var lblRt = lblGo.AddComponent<RectTransform>();
            lblRt.anchorMin = Vector2.zero;
            lblRt.anchorMax = Vector2.one;
            lblRt.offsetMin = new Vector2(6f, 2f);
            lblRt.offsetMax = new Vector2(-24f, -2f);
            var lbl = lblGo.AddComponent<Text>();
            lbl.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            lbl.fontSize = FS_SM;
            lbl.color = WHITE;
            lbl.alignment = TextAnchor.MiddleLeft;
            dd.captionText = lbl;

            var templateGo = new GameObject("Template");
            templateGo.transform.SetParent(go.transform, false);
            var tRt = templateGo.AddComponent<RectTransform>();
            tRt.anchorMin = new Vector2(0f, 0f);
            tRt.anchorMax = new Vector2(1f, 0f);
            tRt.pivot = new Vector2(0.5f, 1f);
            tRt.anchoredPosition = Vector2.zero;
            tRt.sizeDelta = new Vector2(0f, 120f);
            templateGo.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f, 1f);
            var sr = templateGo.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true; sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 20f;

            var vpGo = new GameObject("Viewport");
            vpGo.transform.SetParent(templateGo.transform, false);
            var vpRt = vpGo.AddComponent<RectTransform>();
            vpRt.anchorMin = Vector2.zero;
            vpRt.anchorMax = Vector2.one;
            vpRt.offsetMin = vpRt.offsetMax = Vector2.zero;
            vpGo.AddComponent<Image>();
            vpGo.AddComponent<Mask>().showMaskGraphic = false;
            sr.viewport = vpRt;

            var cGo = new GameObject("Content");
            cGo.transform.SetParent(vpGo.transform, false);
            var cRt = cGo.AddComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0f, 1f);
            cRt.anchorMax = new Vector2(1f, 1f);
            cRt.pivot = new Vector2(0.5f, 1f);
            cRt.anchoredPosition = Vector2.zero;
            cRt.sizeDelta = new Vector2(0f, 28f);
            sr.content = cRt;

            var itemGo = new GameObject("Item");
            itemGo.transform.SetParent(cGo.transform, false);
            var itemRt = itemGo.AddComponent<RectTransform>();
            itemRt.anchorMin = new Vector2(0f, 0.5f);
            itemRt.anchorMax = new Vector2(1f, 0.5f);
            itemRt.sizeDelta = new Vector2(0f, 20f);
            itemGo.AddComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
            var tog = itemGo.AddComponent<Toggle>();
            tog.transition = Selectable.Transition.None;

            var iLblGo = new GameObject("Item Label");
            iLblGo.transform.SetParent(itemGo.transform, false);
            var iLblRt = iLblGo.AddComponent<RectTransform>();
            iLblRt.anchorMin = Vector2.zero;
            iLblRt.anchorMax = Vector2.one;
            iLblRt.offsetMin = new Vector2(4f, 0f);
            iLblRt.offsetMax = Vector2.zero;
            var iLbl = iLblGo.AddComponent<Text>();
            iLbl.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            iLbl.fontSize = FS_SM;
            iLbl.color = WHITE;
            iLbl.alignment = TextAnchor.MiddleLeft;

            tog.targetGraphic = itemGo.GetComponent<Image>();
            tog.isOn = true;
            dd.itemText = iLbl;
            dd.template = tRt;
            templateGo.SetActive(false);

            dd.ClearOptions();
            dd.options.Add(new Dropdown.OptionData("(cache a costume first)"));
            dd.RefreshShownValue();

            return dd;
        }

        private static void CollectAllMaterials(GameObject root, List<Material> mats, List<string> names)
        {
            if (root == null) return;
            CollectMatsRecursive(root.transform, mats, names);
        }

        private static void CollectMatsRecursive(Transform t, List<Material> mats, List<string> names)
        {
            if (t == null) return;
            var r = t.GetComponent<Renderer>();
            if (r != null)
            {
                var sharedMats = r.sharedMaterials;
                if (sharedMats != null)
                {
                    foreach (var m in sharedMats)
                    {
                        if (m == null) continue;
                        var mainTex = m.mainTexture;
                        string texName = mainTex != null ? mainTex.name : m.name;
                        if (!string.IsNullOrEmpty(texName) && !names.Contains(texName))
                        {
                            mats.Add(m);
                            names.Add(texName);
                        }
                    }
                }
            }
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                if (child != null) CollectMatsRecursive(child, mats, names);
            }
        }

        private IEnumerator WaitForAsyncOp(
            UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject> op,
            Action<GameObject> onDone, Action<Exception> onFail)
        {
            yield return op;
            try
            {
                if (op.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
                    onDone?.Invoke(op.Result);
                else
                    onFail?.Invoke(new Exception("op failed: " + op.OperationException?.Message));
            }
            catch (Exception e) { onFail?.Invoke(e); }
        }

        public void SetStatus(string msg)
        {
            if (_statusLbl != null) _statusLbl.text = msg;
        }
    }
}
