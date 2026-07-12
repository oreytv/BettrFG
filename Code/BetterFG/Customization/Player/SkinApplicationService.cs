using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Utilities;
using BetterFG.Services;
using BetterFG.Core;
using FGClient;
using FG.Common;
using FG.Common.CMS;
using BetterFG.Customization.Menu;
using BetterFG.Network;
using BetterFG.UI.Tab;
using Il2CppInterop.Runtime;

namespace BetterFG.Customization.Player
{
    public class SkinApplicationService : MonoBehaviour
    {
        public static SkinApplicationService Instance { get; private set; }

        public event Action<SkinApplyEvent> OnSkinApplied;
        public event Action<string> OnSkinRemoved;

        private static string GetRepoRaw(SkinInfo skinInfo) => RepoRegistry.ResolveRaw(skinInfo?.sourceRepo);

        void Awake()
        {
            Instance = this;
        }

        private Dictionary<string, AppliedSkinInfo> appliedSkins = new Dictionary<string, AppliedSkinInfo>();
        // single source of truth for ALL loaded bundles — local player AND remote
        private Dictionary<string, AssetBundle> loadedBundles = new Dictionary<string, AssetBundle>();
        // tracks which files are "remote-owned" so RemoveMenuSlots doesn't unload them
        private HashSet<string> remoteBundleFiles = new HashSet<string>();
        private List<ActiveSkinSlot> activeSlots = new List<ActiveSkinSlot>();
        private HashSet<string> redownloadingFiles = new HashSet<string>();
        private HashSet<string> pendingKeys = new HashSet<string>();
        private Dictionary<string, Dictionary<Renderer, Material[]>> hiddenRendererMaterials = new Dictionary<string, Dictionary<Renderer, Material[]>>();
        private int applyStamp = 1;
        private List<gamecosmeticSlot> activeGameCosmetics = new List<gamecosmeticSlot>();
        private Dictionary<string, Texture> gameCosmeticMaskCache = new Dictionary<string, Texture>();
        private HashSet<string> gameCosmeticMaskLoading = new HashSet<string>();
        private HashSet<int> gameCosmeticBindingBeans = new HashSet<int>();
        private bool gameCosmeticsRestoring;
        private ColourOption activeGameColour;
        private string activeGameColourId;
        private string activeGameColourName;
        private SkinPatternOption activeGamePattern;
        private string activeGamePatternId;
        private string activeGamePatternName;
        private FaceplateOption activeGameFaceplate;
        private string activeGameFaceplateId;
        private string activeGameFaceplateName;

        // ── Custom texture override ───────────────────────────────────────────
        // keyed by bean instance id — original materials per renderer before we touched them
        private Dictionary<int, List<customTexOriginal>> customTexOriginals = new Dictionary<int, List<customTexOriginal>>();
        private HashSet<int> customTexPollingBeans = new HashSet<int>();
        // beans we've already made our one real custom-tex attempt on (their GEO was fully built).
        // the game fires BindMeshToFallguy once per mesh, constantly (idle anim / LOD), and every
        // fire re-arms the reapply poll. without this, a bean whose costume DOESN'T contain the
        // target material never lands in customTexOriginals, so it re-polls forever -> the steady
        // background freeze after a texture is applied. cleared on revert so a settings/costume
        // change re-attempts cleanly.
        private HashSet<int> customTexAttemptedBeans = new HashSet<int>();
        private static readonly string[] customTexProps = { "_MainTex", "_BaseMap", "_BaseTexture", "_MainTex2" };

        private struct customTexOriginal
        {
            public Renderer renderer;
            public int matIdx;
            public string prop;
            public Texture texture;
            public string textureName;
        }

        public List<ActiveSkinSlot> GetActiveSlots() => new List<ActiveSkinSlot>(activeSlots);

        // per remote-profile bean: the costume options THAT PROFILE applied, so cosmetic masks
        // composite against the bean's own top/bottom costume instead of the local player's
        // (GlobalGameStateClient is always local). keyed by bean instance id.
        private Dictionary<int, List<CostumeOption>> profileBeanCostumes = new Dictionary<int, List<CostumeOption>>();

        // ── Startup restore ───────────────────────────────────────────────────

        private bool _restoredOnStartup = false;
        // files we're waiting to apply once their bundle arrives via SkinLoaderService
        private List<SkinInfo> _restoreQueue = new List<SkinInfo>();
        // skins deferred because their repo catalog fetch was triggered but not yet complete
        private List<(SkinInfo stub, MenuCustomizationApplication plinthApp, SkinLoaderService loader)> _deferredRestores
            = new List<(SkinInfo, MenuCustomizationApplication, SkinLoaderService)>();

        // saved skins don't need the catalog (they carry their own repo/file in skin.multi.*), so
        // kick the restore right at plugin load instead of waiting for the catalog fetch to finish.
        // with the disk cache the bundles are loaded before the menu even exists, and the skin lands
        // the moment the menu bean spawns instead of popping in seconds later. the OnFetchCompleted
        // hook stays as a fallback — _restoredOnStartup makes it a no-op when this already ran.
        public IEnumerator EarlyRestoreCoroutine()
        {
            // let RepoRegistry/settings finish their Awake loads first
            yield return null;
            yield return null;
            var loader = CustomizationServices.LoaderService;
            if (loader == null || _restoredOnStartup) yield break;
            var catalog = CustomizationServices.CatalogService;
            RestoreFromSettings(catalog != null ? catalog.AvailableSkins : null, loader, CustomizationServices.PlinthApp);
        }

        // called once after the active repo's catalog is fetched
        // force = true lets this run again after boot (preset switching) — the whole pipeline below
        // is live-safe (download-or-cached then additive ApplySkin), the guard is only there to stop
        // the boot path double-firing.
        public void RestoreFromSettings(List<SkinInfo> catalogSkins, SkinLoaderService loader, MenuCustomizationApplication plinthApp, bool force = false)
        {
            if (_restoredOnStartup && !force) return;
            _restoredOnStartup = true;

            var catalogService = CustomizationServices.CatalogService;
            var repoRegistry = CustomizationServices.RepoRegistry;

            // kick catalog fetches for any repos referenced by saved skins (so they populate the
            // UI list later). we no longer defer the restore on the catalog — saved skins download
            // directly via their known file/repo without waiting for the catalog to enumerate.
            if (catalogService != null && repoRegistry != null)
            {
                string savedRepos = SettingsService.Get("skin.multi.repos", "");
                if (!string.IsNullOrEmpty(savedRepos))
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string entry in savedRepos.Split(','))
                    {
                        string raw = entry.Trim();
                        if (string.IsNullOrEmpty(raw) || !seen.Add(raw)) continue;
                        foreach (var repo in repoRegistry.Repos)
                        {
                            if (string.Equals(repo.RawBase, raw, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(repo.githubUrl, raw, StringComparison.OrdinalIgnoreCase))
                            {
                                catalogService.FetchSkins(repo);
                                break;
                            }
                        }
                    }
                }
            }

            string multiFiles = SettingsService.Get("skin.multi.files", "");
            string multiSources = SettingsService.Get("skin.multi.sources", "");
            string multiPaths = SettingsService.Get("skin.multi.paths", "");
            string multiRepos = SettingsService.Get("skin.multi.repos", "");
            string multiTypes = SettingsService.Get("skin.multi.types", "");

            if (string.IsNullOrEmpty(multiFiles))
            {
                string legacyFile = SettingsService.Get("skin.file", "");
                if (!string.IsNullOrEmpty(legacyFile))
                {
                    multiFiles = legacyFile;
                    multiSources = SettingsService.Get("skin.source", "");
                    multiPaths = SettingsService.Get("skin.localPath", "");
                }
            }

            if (string.IsNullOrEmpty(multiFiles)) return;

            string[] files = multiFiles.Split(',');
            string[] sources = multiSources.Split(',');
            string[] paths = multiPaths.Split(',');
            string[] repos = multiRepos.Split(',');
            string[] types = multiTypes.Split(',');

            var handOverrides = new Dictionary<string, int>();
            string hoRaw = SettingsService.Get("skin.hand.overrides", "");
            if (!string.IsNullOrEmpty(hoRaw))
            {
                foreach (string part in hoRaw.Split(','))
                {
                    int colon = part.LastIndexOf(':');
                    if (colon < 1) continue;
                    string hfile = part.Substring(0, colon);
                    if (int.TryParse(part.Substring(colon + 1), out int ov))
                        handOverrides[hfile] = ov;
                }
            }

            for (int s = 0; s < files.Length; s++)
            {
                string file = files[s].Trim();
                if (string.IsNullOrEmpty(file)) continue;
                // already equipped (forced preset reload) — leave it alone, only apply what's missing
                if (HasActiveSlotForFile(file)) continue;
                string source = s < sources.Length ? sources[s].Trim() : "remote";
                string path = s < paths.Length ? paths[s].Trim() : "";
                string repo = s < repos.Length ? repos[s].Trim() : "";
                string type = s < types.Length ? types[s].Trim() : "";

                if (source == "local")
                {
                    if (!string.IsNullOrEmpty(path) && System.IO.Directory.Exists(path))
                    {
                        loader.OnSkinImported += OnRestoreImported;
                        loader.ImportSkinFromFolder(path);
                    }
                    continue;
                }

                SkinInfo skinInfo = null;
                if (catalogSkins != null)
                    foreach (var sk in catalogSkins)
                        if (sk.file == file) { skinInfo = sk; break; }

                // not in catalog yet (catalog still fetching, or file is in a repo we haven't
                // catalogued)? don't wait for OnSkinsLoaded — the per-skin loader pulls info.json
                // alongside the bundle anyway, and waiting for the catalog to finish enumerating
                // every other file in the repo added multi-second delays on menu enter when the
                // saved items lived in a different repo than the currently-active one.
                if (skinInfo == null)
                {
                    skinInfo = new SkinInfo { file = file, name = file, sourceRepo = repo, type = type };
                }
                else if (!string.IsNullOrEmpty(repo))
                    skinInfo.sourceRepo = repo;

                if (handOverrides.ContainsKey(file)) skinInfo.handOverride = handOverrides[file];
                RestoreOneSkin(skinInfo, loader, plinthApp);
            }
        }

        // ReapplyToMainMenu bails when the plinth app has no runtime _lastInfo (e.g. the game rebuilt
        // the plinth screen and our clone died, or the initial async restore never landed). the plinth
        // is still saved in skin.multi.*, so pull it back out of settings and re-run the restore rather
        // than leaving the game's default plinth showing while the tab says one's selected.
        public void RestorePlinthFromSettings()
        {
            string multiFiles = SettingsService.Get("skin.multi.files", "");
            if (string.IsNullOrEmpty(multiFiles)) return;
            string[] files = multiFiles.Split(',');
            string[] repos = SettingsService.Get("skin.multi.repos", "").Split(',');
            string[] types = SettingsService.Get("skin.multi.types", "").Split(',');

            var loader = CustomizationServices.LoaderService;
            var plinthApp = CustomizationServices.PlinthApp;
            if (loader == null || plinthApp == null) return;

            for (int s = 0; s < files.Length; s++)
            {
                string type = s < types.Length ? types[s].Trim() : "";
                if (SkinTypeParser.FromString(type) != SkinType.Plinth) continue;
                string file = files[s].Trim();
                if (string.IsNullOrEmpty(file)) continue;
                string repo = s < repos.Length ? repos[s].Trim() : "";
                RestoreOneSkin(new SkinInfo { file = file, name = file, sourceRepo = repo, type = type }, loader, plinthApp);
                return;
            }
        }

        private void OnDeferredCatalogArrived(List<SkinInfo> freshSkins)
        {
            var catalogService = CustomizationServices.CatalogService;
            if (catalogService != null) catalogService.OnSkinsLoaded -= OnDeferredCatalogArrived;

            foreach (var (stub, plinthApp, loader) in _deferredRestores)
            {
                SkinInfo found = null;
                foreach (var sk in freshSkins)
                    if (sk.file == stub.file) { found = sk; break; }

                if (found == null)
                {
                    Debug.LogWarning($"[SkinApplication] deferred restore: '{stub.file}' still not in catalog, downloading");
                    found = stub;
                }
                else if (!string.IsNullOrEmpty(stub.sourceRepo))
                    found.sourceRepo = stub.sourceRepo;

                if (stub.handOverride != 0) found.handOverride = stub.handOverride;
                RestoreOneSkin(found, loader, plinthApp);
            }

            _deferredRestores.Clear();
        }

        private void RestoreOneSkin(SkinInfo skinInfo, SkinLoaderService loader, MenuCustomizationApplication plinthApp)
        {
            if (SkinTypeParser.FromString(skinInfo.type) == SkinType.Plinth)
            {
                if (plinthApp != null)
                    StartCoroutine(RestorePlinthCoroutine(skinInfo, plinthApp).WrapToIl2Cpp());
                return;
            }

            if (loadedBundles.TryGetValue(skinInfo.file, out var existingBundle) && existingBundle != null)
            {
                ApplySkin(skinInfo, existingBundle, additive: true, reason: ApplyReason.AutoReapply);
                return;
            }

            // already waiting on this file from an earlier restore pass — don't queue it twice
            if (_restoreQueue.FindIndex(s => s.file == skinInfo.file) >= 0) return;

            _restoreQueue.Add(skinInfo);
            loader.OnSkinLoaded += OnRestoreDownloaded;

            string category = GetCategoryFolder(skinInfo.type);
            string folder = !string.IsNullOrEmpty(skinInfo.repoFolder) ? skinInfo.repoFolder : $"{category}/{skinInfo.file}";
            string repoRaw = RepoRegistry.ResolveRaw(skinInfo.sourceRepo);
            string url = $"{repoRaw}/{folder}/{skinInfo.file}";
            string infoUrl = $"{repoRaw}/{folder}/info.json";

            Debug.Log($"[BetterFG] Downloading: {url}");
            skinInfo.sourceRepo = repoRaw;
            loader.DownloadSkinWithInfo(skinInfo.file, url, infoUrl);
        }

        private void OnRestoreDownloaded(SkinInfo skinInfo, AssetBundle bundle)
        {
            int idx = _restoreQueue.FindIndex(s => s.file == skinInfo.file);
            if (idx < 0) return;
            SkinInfo queued = _restoreQueue[idx];
            _restoreQueue.RemoveAt(idx);

            // unsubscribe when queue drains
            if (_restoreQueue.Count == 0)
            {
                var loader = CustomizationServices.LoaderService;
                if (loader != null) loader.OnSkinLoaded -= OnRestoreDownloaded;
            }

            // the loader already fetched + parsed this skin's info.json (bone offsets included) while
            // downloading. carry that onto the queued stub so SetupBoneSyncCoroutine uses the offsets
            // directly and skips its fallback info.json request to github (~0.8s per skin) — even for
            // skins with zero offsets, since infoFetched proves there's nothing more to go get.
            // keepBase/skinScale MUST come across too: the restore stub is built blind from settings
            // (keepBase defaults false), so without this a keepBase=true overlay costume gets treated
            // as full-body and hides the bean it's meant to sit on.
            if (skinInfo != null && skinInfo.infoFetched)
            {
                queued.boneOffsets = skinInfo.boneOffsets;
                queued.infoFetched = true;
                queued.keepBase = skinInfo.keepBase;
                if (skinInfo.skinScale > 0f) queued.skinScale = skinInfo.skinScale;
            }

            ApplySkin(queued, bundle, additive: true, reason: ApplyReason.AutoReapply);
        }

        private void OnRestoreImported(SkinInfo skinInfo, AssetBundle bundle, Texture2D _cover)
        {
            var loader = CustomizationServices.LoaderService;
            if (loader != null) loader.OnSkinImported -= OnRestoreImported;
            ApplySkin(skinInfo, bundle, additive: true, reason: ApplyReason.AutoReapply);
        }

        private IEnumerator RestorePlinthCoroutine(SkinInfo info, MenuCustomizationApplication plinthApp)
        {
            if (plinthApp.TryGetBundle(info.file, out var cached)) { plinthApp.ApplyPlinth(info, cached); yield break; }

            string repoRaw = RepoRegistry.ResolveRaw(info.sourceRepo);
            string folder = !string.IsNullOrEmpty(info.repoFolder) ? info.repoFolder : $"Plinths/{info.file}";
            string url = $"{repoRaw}/{folder}/{info.file}";
            var loader = CustomizationServices.LoaderService;

            string cachePath = SkinLoaderService.CachePathFor(info.file);
            AssetBundle bundle = null;
            if (cachePath != null && System.IO.File.Exists(cachePath))
            {
                var fileReq = AssetBundle.LoadFromFileAsync(cachePath);
                yield return fileReq;
                bundle = fileReq.assetBundle;
                if (bundle == null) { try { System.IO.File.Delete(cachePath); } catch { } }
            }

            if (bundle == null)
            {
                var req = UnityWebRequest.Get(url);
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success) { req.Dispose(); yield break; }

                byte[] bytes = req.downloadHandler.data;
                req.Dispose();

                if (plinthApp.TryGetBundle(info.file, out var raced)) { plinthApp.ApplyPlinth(info, raced); yield break; }

                if (cachePath != null && loader != null)
                    loader.StartCoroutine(loader.SaveCacheCoroutine(cachePath, bytes).WrapToIl2Cpp());
                var loadReq = AssetBundle.LoadFromMemoryAsync(bytes);
                yield return loadReq;
                bundle = loadReq.assetBundle;
            }

            if (bundle == null) yield break;
                plinthApp.ApplyPlinth(info, bundle);
        }

        public bool TryGetLoadedBundle(string file, out AssetBundle bundle)
        {
            bundle = null;
            return !string.IsNullOrEmpty(file) && loadedBundles.TryGetValue(file, out bundle) && bundle != null;
        }

        // NetworkClient calls this to register its bundles into the shared registry
        public void RegisterRemoteBundle(string file, AssetBundle bundle)
        {
            if (string.IsNullOrEmpty(file) || bundle == null) return;
            loadedBundles[file] = bundle;
            remoteBundleFiles.Add(file);
        }

        public void UnloadBundleForFile(string file)
        {
            if (string.IsNullOrEmpty(file)) return;
            if (!loadedBundles.TryGetValue(file, out var b) || b == null) return;
            // never unload remote-owned bundles — NetworkClient still needs them
            if (remoteBundleFiles.Contains(file)) return;
            Debug.Log($"[SkinApplication] pre-unload bundle '{file}'");
            b.Unload(false);
            loadedBundles.Remove(file);
        }

        // called by NetworkClient.CleanupAllRemote so we can actually release remote bundles
        public void UnloadRemoteBundle(string file)
        {
            if (string.IsNullOrEmpty(file)) return;
            remoteBundleFiles.Remove(file);
            if (!loadedBundles.TryGetValue(file, out var b) || b == null) return;
            b.Unload(false);
            loadedBundles.Remove(file);
        }

        public void UpdateItemOffsets(string file, Vector3 offset)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var kvp in appliedSkins)
            {
                if (kvp.Value == null || kvp.Value.instance == null) continue;
                if (kvp.Value.type != SkinType.Item) continue;
                if (!kvp.Key.Contains("|" + file)) continue;

                bool isLeft = kvp.Key.EndsWith("_L");
                ActiveSkinSlot slot = null;
                foreach (var s in activeSlots)
                    if (s.skinInfo.file == file) { slot = s; break; }
                if (slot == null) continue;

                ItemHandInfo handInfo = isLeft ? slot.skinInfo.left : slot.skinInfo.right;
                if (handInfo == null) continue;

                string hk = isLeft ? "l" : "r";
                bool isEdited = (slot.skinInfo.handOverride == 1 && isLeft) || (slot.skinInfo.handOverride == 2 && !isLeft);
                Vector3 off;
                if (isEdited)
                    off = offset;
                else
                {
                    float ox = float.TryParse(SettingsService.Get($"item{hk}offset.{file}.x", "0"), System.Globalization.NumberStyles.Float, ci, out float vx) ? vx : 0f;
                    float oy = float.TryParse(SettingsService.Get($"item{hk}offset.{file}.y", "0"), System.Globalization.NumberStyles.Float, ci, out float vy) ? vy : 0f;
                    float oz = float.TryParse(SettingsService.Get($"item{hk}offset.{file}.z", "0"), System.Globalization.NumberStyles.Float, ci, out float vz) ? vz : 0f;
                    off = new Vector3(ox, oy, oz);
                }
                Vector3 basePos = new Vector3(handInfo.position[0], handInfo.position[1], handInfo.position[2]);
                kvp.Value.instance.transform.localPosition = basePos + off;
            }
        }

        public void UpdateItemRotations(string file, Vector3 eulerOffset)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var kvp in appliedSkins)
            {
                if (kvp.Value == null || kvp.Value.instance == null) continue;
                if (kvp.Value.type != SkinType.Item) continue;
                if (!kvp.Key.Contains("|" + file)) continue;

                bool isLeft = kvp.Key.EndsWith("_L");
                ActiveSkinSlot slot = null;
                foreach (var s in activeSlots)
                    if (s.skinInfo.file == file) { slot = s; break; }
                if (slot == null) continue;

                ItemHandInfo handInfo = isLeft ? slot.skinInfo.left : slot.skinInfo.right;
                if (handInfo == null) continue;

                string hk = isLeft ? "l" : "r";
                bool isEdited = (slot.skinInfo.handOverride == 1 && isLeft) || (slot.skinInfo.handOverride == 2 && !isLeft);
                Vector3 rot;
                if (isEdited)
                    rot = eulerOffset;
                else
                {
                    float rx = float.TryParse(SettingsService.Get($"item{hk}rot.{file}.x", "0"), System.Globalization.NumberStyles.Float, ci, out float vx) ? vx : 0f;
                    float ry = float.TryParse(SettingsService.Get($"item{hk}rot.{file}.y", "0"), System.Globalization.NumberStyles.Float, ci, out float vy) ? vy : 0f;
                    float rz = float.TryParse(SettingsService.Get($"item{hk}rot.{file}.z", "0"), System.Globalization.NumberStyles.Float, ci, out float vz) ? vz : 0f;
                    rot = new Vector3(rx, ry, rz);
                }
                Vector3 baseRot = new Vector3(handInfo.rotation[0], handInfo.rotation[1], handInfo.rotation[2]);
                kvp.Value.instance.transform.localEulerAngles = baseRot + rot;
            }
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void ApplySkin(SkinInfo skinInfo, AssetBundle bundle, bool additive = false, ApplyReason reason = ApplyReason.FromMenu)
        {
            if (!additive) RemoveMenuEquippedSlotsOnly();
            RemoveSlotByFile(skinInfo.file);

            SkinType type = SkinTypeParser.FromString(skinInfo.type);
            if (type == SkinType.Unknown)
            {
                Debug.LogWarning($"[SkinApplication] unknown type for {skinInfo.name}");
                return;
            }

            bundle = GetOrRegisterBundle(skinInfo.file, bundle);
            redownloadingFiles.Remove(skinInfo.file);

            var slot = new ActiveSkinSlot { skinInfo = skinInfo, bundle = bundle, type = type, applyStamp = applyStamp };
            activeSlots.Add(slot);

            // a full-body costume just went on — tear down any game-cosmetic meshes already sitting on
            // the local beans. they stay in the saved loadout and come back when this costume is removed.
            if (type == SkinType.Costume && !skinInfo.keepBase) StripAppliedGameCosmeticInstancesLocal();

            Debug.Log($"[SkinApplication] ApplySkin '{skinInfo.name}' type={type} additive={additive} reason={reason}");
            StartCoroutine(ApplySlotToAllBeansCoroutine(slot, reason).WrapToIl2Cpp());
        }

        public IEnumerator ApplySkinToBean(ActiveSkinSlot slot, GameObject bean)
        {
            if (bean == null || slot == null) yield break;
            slot.remotePipeline = true; // only the remote pipeline enters here
            string key = MakeKey(bean, slot.skinInfo.file);
            if (appliedSkins.ContainsKey(key) || pendingKeys.Contains(key)) yield break;
            pendingKeys.Add(key);
            yield return ApplySlotToBeanCoroutine(slot, bean, ApplyReason.AutoReapply).WrapToIl2Cpp();
        }

        public void OnBeansFound(List<GameObject> beans)
        {
            if (beans == null || beans.Count == 0) return;

            PruneDestroyedEntries();

            if (activeSlots.Count > 0)
            {
                foreach (var slot in activeSlots)
                {
                    if (redownloadingFiles.Contains(slot.skinInfo.file)) continue;
                    foreach (var bean in beans)
                    {
                        if (bean == null) continue;
                        if (SkipMenuSkinAutoApplyForThisBean(bean)) continue;
                        string key = MakeKey(bean, slot.skinInfo.file);
                        if (appliedSkins.ContainsKey(key) || pendingKeys.Contains(key)) continue;
                        if (slot.bundle == null) { TriggerReload(slot.skinInfo); continue; }
                        pendingKeys.Add(key);
                        StartCoroutine(ApplySlotToBeanCoroutine(slot, bean, ApplyReason.AutoReapply).WrapToIl2Cpp());
                    }
                }
            }

            if (activeGameCosmetics.Count > 0)
            {
                foreach (var slot in activeGameCosmetics)
                {
                    foreach (var bean in beans)
                    {
                        if (bean == null || SkipMenuSkinAutoApplyForThisBean(bean)) continue;
                        string key = MakeKey(bean, slot.id);
                        if (appliedSkins.ContainsKey(key) || pendingKeys.Contains(key)) continue;
                        pendingKeys.Add(key);
                        StartCoroutine(ApplyGameCosmeticToBeanCoroutine(slot, bean, ApplyReason.AutoReapply).WrapToIl2Cpp());
                    }
                }
            }

            foreach (var bean in beans)
            {
                if (bean == null || SkipMenuSkinAutoApplyForThisBean(bean)) continue;
                ApplyGameColourPatternToBean(bean);
                // the lobby/UI display beans get rebuilt by the game a few frames after they first
                // appear, which wipes the scale we just set. re-apply it over the next handful of frames
                // so it survives (the in-round FallGuy bean carries scale on a wrapper and doesn't need this).
                if (GameObjectHelper.IsLobbyCharacter(bean) || GameObjectHelper.IsUICharacter(bean))
                    StartCoroutine(ReapplyMenuBeanScaleCoroutine(bean).WrapToIl2Cpp());
                if (bean == BeanMonitorService.LocalPlayerBean) continue;
                PollAndReapplyCustomTextureForBean(bean);
            }
        }

        private IEnumerator ReapplyMenuBeanScaleCoroutine(GameObject bean)
        {
            for (int i = 0; i < 8 && bean != null; i++)
            {
                PlayerScaleService.ApplyToBean(bean, 1f, PlayerScaleService.BeanScaleMode.Local);
                yield return null;
            }
        }

        // Public method for auto-reapplying saved custom texture to any bean (called from BeanMonitorService)
        public int TryAutoReapplyCustomTextureForBean(GameObject bean)
        {
            if (bean == null) return 0;
            int beanId = bean.GetInstanceID();
            if (customTexOriginals.ContainsKey(beanId)) return 0; // already applied

            if (!int.TryParse(BetterFG.Services.SettingsService.Get("skintex.entryCount", "0"), out int count) || count <= 0) return 0;

            int total = 0;
            for (int i = 0; i < count; i++)
            {
                string enabled = BetterFG.Services.SettingsService.Get($"skintex.entry.{i}.enabled", "1");
                if (enabled != "1") continue;

                string texPath = BetterFG.Services.SettingsService.Get($"skintex.entry.{i}.texPath", "");
                if (string.IsNullOrEmpty(texPath) || !System.IO.File.Exists(texPath)) continue;

                if (!int.TryParse(BetterFG.Services.SettingsService.Get($"skintex.entry.{i}.matIdx", "0"), out int matIdx)) matIdx = 0;

                // rebuild match names from saved matNames so we only touch the right costume material
                string matNamesRaw = BetterFG.Services.SettingsService.Get($"skintex.entry.{i}.matNames", "");
                var matchNames = new HashSet<string>();
                if (!string.IsNullOrEmpty(matNamesRaw))
                {
                    var parts = matNamesRaw.Split('|');
                    if (matIdx < parts.Length && !string.IsNullOrEmpty(parts[matIdx]))
                        matchNames.Add(parts[matIdx]);
                }

                // no match name = skip, don't blast everything
                if (matchNames.Count == 0) continue;

                try
                {
                    var tex = GetCachedCustomTex(texPath);
                    if (tex != null) total += ApplyCustomTexture(bean, matIdx, tex, matchNames);
                }
                catch (Exception ex) { Debug.LogWarning($"[SkinApplication] auto-reapply entry {i}: {ex.Message}"); }
            }
            return total;
        }

        private void TryAutoReapplyCustomTex(GameObject bean)
        {
            TryAutoReapplyCustomTextureForBean(bean);
        }

        // process-wide cache of decoded custom textures. each bean push used to re-read the file
        // and re-decode the PNG/JPG on the main thread, which stacks freezes during state changes
        // (round load, qual, reward — each pushes the local bean and OnBeansFound iterates every
        // saved entry). keyed on path + write timestamp so editing the file invalidates.
        private static readonly Dictionary<string, (long stamp, Texture2D tex)> _customTexCache =
            new Dictionary<string, (long, Texture2D)>(StringComparer.OrdinalIgnoreCase);
        // decode every enabled entry's texture into _customTexCache up front (plugin load) so the
        // first per-bean auto-reapply is a cache hit. the auto-reapply path reads THIS cache, not the
        // tab's _texCache, so warming only the tab cache left the first real apply doing a full file
        // read + png decode on the frame the bean pushed.
        public static void PrewarmCustomTexCache()
        {
            if (!int.TryParse(BetterFG.Services.SettingsService.Get("skintex.entryCount", "0"), out int count) || count <= 0) return;
            for (int i = 0; i < count; i++)
            {
                if (BetterFG.Services.SettingsService.Get($"skintex.entry.{i}.enabled", "1") != "1") continue;
                string texPath = BetterFG.Services.SettingsService.Get($"skintex.entry.{i}.texPath", "");
                if (!string.IsNullOrEmpty(texPath)) GetCachedCustomTex(texPath);
            }
        }

        private static Texture2D GetCachedCustomTex(string path)
        {
            long stamp;
            try { stamp = System.IO.File.GetLastWriteTimeUtc(path).Ticks; } catch { return null; }
            if (_customTexCache.TryGetValue(path, out var hit) && hit.stamp == stamp && hit.tex != null)
                return hit.tex;
            byte[] data;
            try { data = System.IO.File.ReadAllBytes(path); }
            catch (Exception ex) { Debug.LogWarning($"[SkinApplication] read {path}: {ex.Message}"); return null; }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(data);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            _customTexCache[path] = (stamp, tex);
            return tex;
        }

        private void RevertLocalCustomTextures()
        {
            var local = BeanMonitorService.LocalPlayerBean;
            if (local != null) RevertCustomTexture(local);

            foreach (var bean in BeanMonitorService.GetTrackedBeans())
            {
                if (bean == null || bean == local) continue;
                if (IsRemoteInRoundBean(bean)) continue;
                RevertCustomTexture(bean);
            }
        }

        public void PollAndReapplyCustomTextureForBean(GameObject bean)
        {
            if (bean == null) return;
            // nothing saved to reapply -> don't spin a poll coroutine that scans the bean's renderers
            // every 0.5s for nothing. the game rebinds costume meshes constantly (animation/LOD) and
            // every rebind re-arms this via the BindMeshToFallguy postfix, so without this gate a
            // game-cosmetics-only loadout still pays a steady background poll. (auto-reapply itself
            // also no-ops when count<=0 — this just stops the churn before it starts.)
            if (!int.TryParse(SettingsService.Get("skintex.entryCount", "0"), out int texCount) || texCount <= 0) return;
            int beanId = bean.GetInstanceID();
            if (customTexOriginals.ContainsKey(beanId) || customTexPollingBeans.Contains(beanId) || customTexAttemptedBeans.Contains(beanId)) return;
            customTexPollingBeans.Add(beanId);
            StartCoroutine(PollReapplyCoroutine(bean).WrapToIl2Cpp());
        }

        private IEnumerator PollReapplyCoroutine(GameObject bean)
        {
            float elapsed = 0f;
            int beanId = bean != null ? bean.GetInstanceID() : 0;
            while (elapsed < 10f)
            {
                if (bean == null) break;
                if (customTexOriginals.ContainsKey(beanId)) break;
                Transform geo;
                int rendererCount = 0;
                    geo = FindBeanGEO(bean);
                    if (geo != null) rendererCount = geo.GetComponentsInChildren<Renderer>(true).Length;
                if (geo != null && rendererCount > 0)
                {
                    // bean is fully built — we've made our one real attempt. whether or not a
                    // texture matched, there's nothing left to wait for (a material that isn't
                    // on this bean won't appear by polling longer), so stop here either way.
                    // mark it attempted so the constant BindMeshToFallguy postfix doesn't re-arm
                    // this poll on every mesh rebind (the background-freeze-after-apply).
                    customTexAttemptedBeans.Add(beanId);
                    TryAutoReapplyCustomTextureForBean(bean);
                    break;
                }
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }
            if (bean != null && !customTexOriginals.ContainsKey(beanId))
                Debug.LogWarning($"[SkinApplication] PollReapply no texture matched for {bean.name}");
            if (beanId != 0)
                customTexPollingBeans.Remove(beanId);
        }

        public void RemoveMenuEquippedSlotsOnly()
        {
            applyStamp++;
            Debug.Log($"[SkinApplication] RemoveMenuEquippedSlotsOnly | activeSlots={activeSlots.Count}, applied={appliedSkins.Count}");
            // game cosmetics live in appliedSkins too (keyed by their id); never sweep those —
            // they're owned by activeGameCosmetics and removed via RemoveAllGameCosmetics only
            var gameCosmeticIds = new HashSet<string>();
            foreach (var gc in activeGameCosmetics)
                if (gc != null && !string.IsNullOrEmpty(gc.id))
                    gameCosmeticIds.Add(gc.id);

            var files = new HashSet<string>();
            foreach (var s in activeSlots)
                if (s?.skinInfo != null && !string.IsNullOrEmpty(s.skinInfo.file))
                    files.Add(s.skinInfo.file);
            var localBean = BeanMonitorService.LocalPlayerBean;
            foreach (var kvp in appliedSkins)
            {
                var bean = kvp.Value?.bean;
                if (localBean != null && bean != null && bean != localBean && IsRemoteInRoundBean(bean)) continue;
                string file = SkinFileFromAppliedKey(kvp.Key);
                if (!string.IsNullOrEmpty(file) && !gameCosmeticIds.Contains(file)) files.Add(file);
            }
            foreach (var f in files)
                RemoveSlotByFile(f);

            PruneLoadedBundlesNotUsedByAppliedSkins();
            redownloadingFiles.Clear();
            StartCoroutine(RestoreMenuBeanGEOSoon().WrapToIl2Cpp());
            PlayerScaleService.ApplyToAll(PlayerScaleService.HasUserSetScale() ? PlayerScaleService.GetPlayerScale() : 1f);
            OnSkinRemoved?.Invoke("Menu slots cleared");
        }

        // returns true if this file currently has an active menu-equipped slot
        public bool HasActiveSlotForFile(string file)
        {
            if (string.IsNullOrEmpty(file)) return false;
            foreach (var s in activeSlots)
                if (s?.skinInfo != null && s.skinInfo.file == file) return true;
            return false;
        }

        // the menu loadout has a full-body UGC costume (Costume with keepBase off) that hides the base
        // bean. when one's on, applying in-game cosmetics (game costumes/colour/pattern/faceplate) is
        // pointless — they'd be hidden under the costume and the CostumePoller would just spend forever
        // fighting to hide them again. so we skip applying them entirely while it's equipped. accessories
        // and items still apply (they sit ON the costume). taking the costume back off hits the normal
        // removal path (RestoreMenuBeanGEOSoon / RemoveMenuEquippedSlotsOnly) which re-composites the
        // game cosmetics fresh, so nothing's lost.
        public bool LoadoutHidesBaseBody()
        {
            foreach (var s in activeSlots)
                if (s?.skinInfo != null && s.type == SkinType.Costume && !s.skinInfo.keepBase) return true;
            return false;
        }

        // re-equip an already-loaded skin in place using its cached bundle (no redownload).
        // used when only a property like handOverride changed — we drop the old slot's instances
        // and respawn just this one, leaving everything else on the bean untouched.
        public bool TryReapplyLoadedSkin(SkinInfo skinInfo)
        {
            if (skinInfo == null || !TryGetLoadedBundle(skinInfo.file, out var bundle)) return false;
            // ApplySkin(additive:true) already drops the existing slot for this file before respawning
            ApplySkin(skinInfo, bundle, additive: true, reason: ApplyReason.FromMenu);
            return true;
        }

        // targeted unequip of a single menu skin — does NOT touch any other applied slot.
        // use this instead of RemoveMenuEquippedSlotsOnly when only one item changed so we
        // don't tear down + re-apply the whole loadout (no flicker, no pointless redownloads)
        public void RemoveOneSkinByFile(string file)
        {
            if (string.IsNullOrEmpty(file) || !HasActiveSlotForFile(file)) return;

            // figure out the slot type before we remove it. only COSTUMES hide/replace the shared
            // body GEO — taking one off knocks the composited game cosmetics off the bean, so they
            // MUST be re-composited. accessories and items are additive attachments (accessories
            // merge their own mesh onto the bones, they never hide Body_LOD0), so removing one
            // touches nothing else and can skip the heavy GEO/cosmetic restore.
            SkinType removedType = SkinType.Unknown;
            foreach (var s in activeSlots)
                if (s?.skinInfo != null && s.skinInfo.file == file) { removedType = s.type; break; }

            Debug.Log($"[SkinApplication] RemoveOneSkinByFile '{file}' type={removedType}");
            RemoveSlotByFile(file);
            PruneLoadedBundlesNotUsedByAppliedSkins();
            redownloadingFiles.Remove(file);

            // only re-composite cosmetics when a COSTUME came off AND something is actually on.
            // RestoreMenuBeanGEOSoon resets the bean GEO + reapplies game cosmetics, which briefly
            // flashes the bare bean — pointless churn for an additive accessory/item removal.
            // colour/pattern/faceplate count too: the UGC costume with keepBase=off hid Body_LOD0,
            // and after restore the revived body needs colour/pattern/faceplate re-stamped or the
            // lobby bean shows the default look (mainmenu bean gets rescued by its own UpdateColour
            // path, the lobby bean does not).
            bool hasGameLook = activeGameCosmetics.Count > 0 || activeGameColour != null || activeGamePattern != null || activeGameFaceplate != null;
            if (removedType == SkinType.Costume && hasGameLook)
            {
                // fast path: the cosmetics were parked (not destroyed) when this costume went on, so
                // flip them straight back on with no re-instantiate. only fall back to the slow
                // re-composite when nothing was stashed (cosmetics added after the costume, etc).
                if (!RestoreStashedGameCosmetics())
                    StartCoroutine(RestoreMenuBeanGEOSoon().WrapToIl2Cpp());
            }

            SpawnGameCosmeticPoof();
            OnSkinRemoved?.Invoke($"Unequipped {file}");
        }

        public void ApplyGameCosmetic(CostumeOption option)
        {
            if (option == null) return;
            ApplyGameCosmeticsOnly(new List<CostumeOption> { option });
        }

        public bool ApplyGameCosmeticsOnly(List<CostumeOption> options)
        {
            options ??= new List<CostumeOption>();

            var wantedIds = new HashSet<string>();
            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                if (option == null) continue;
                string id = GetGameCosmeticOptionId(option);
                wantedIds.Add(id);
            }

            return ApplyGameCosmeticSelection(options, wantedIds);
        }

        public bool ApplyGameCosmeticSelection(List<CostumeOption> options, HashSet<string> wantedIds)
        {
            options ??= new List<CostumeOption>();
            wantedIds ??= new HashSet<string>();
            SaveGameCosmeticIds(wantedIds);

            var wantedOptions = new Dictionary<string, CostumeOption>();
            for (int i = 0; i < options.Count; i++)
            {
                var option = options[i];
                if (option == null) continue;
                string id = GetGameCosmeticOptionId(option);
                if (wantedIds.Contains(id) && !wantedOptions.ContainsKey(id))
                    wantedOptions[id] = option;
            }

            bool changed = false;
            var current = new HashSet<string>();
            foreach (var slot in activeGameCosmetics)
                if (slot != null && !string.IsNullOrEmpty(slot.id))
                    current.Add(slot.id);

            var remove = new List<string>();
            foreach (var id in current)
                if (!wantedIds.Contains(id)) remove.Add(id);
            foreach (var id in remove)
            {
                RemoveGameCosmeticById(id);
                changed = true;
            }

            foreach (var kvp in wantedOptions)
            {
                if (current.Contains(kvp.Key)) continue;
                var slot = new gamecosmeticSlot
                {
                    option = kvp.Value,
                    id = kvp.Key,
                    name = GetGameCosmeticName(kvp.Value),
                    applyStamp = applyStamp
                };
                activeGameCosmetics.Add(slot);
                Debug.Log($"[SkinApplication] ApplyGameCosmetic '{slot.name}'");
                StartCoroutine(ApplyGameCosmeticToAllBeansCoroutine(slot, ApplyReason.FromMenu).WrapToIl2Cpp());
                changed = true;
            }

            if (changed) SpawnGameCosmeticPoof();
            return changed;
        }

        public void RemoveAllGameCosmetics()
        {
            var ids = new List<string>();
            foreach (var slot in activeGameCosmetics)
                if (slot != null && !string.IsNullOrEmpty(slot.id)) ids.Add(slot.id);
            foreach (var id in ids) RemoveGameCosmeticById(id);
            activeGameCosmetics.Clear();
            SaveGameCosmeticIds(null);
            if (ids.Count > 0) SpawnGameCosmeticPoof();
            OnSkinRemoved?.Invoke("Game cosmetics cleared");
        }

        public void RemoveGameCosmetic(string id)
        {
            bool had = false;
            foreach (var slot in activeGameCosmetics)
                if (slot != null && slot.id == id) { had = true; break; }
            RemoveGameCosmeticById(id);
            SaveGameCosmeticIds(GetAppliedGameCosmeticIds());
            if (had) SpawnGameCosmeticPoof();
            OnSkinRemoved?.Invoke("Game cosmetic removed");
        }

        public List<gamecosmeticEntry> GetAppliedGameCosmetics()
        {
            var list = new List<gamecosmeticEntry>();
            foreach (var slot in activeGameCosmetics)
                if (slot != null && !string.IsNullOrEmpty(slot.id))
                    list.Add(new gamecosmeticEntry { id = slot.id, name = slot.name, option = slot.option, kind = "costume" });
            if (activeGameColour != null && !string.IsNullOrEmpty(activeGameColourId))
                list.Add(new gamecosmeticEntry { id = activeGameColourId, name = activeGameColourName, option = activeGameColour, kind = "colour" });
            if (activeGamePattern != null && !string.IsNullOrEmpty(activeGamePatternId))
                list.Add(new gamecosmeticEntry { id = activeGamePatternId, name = activeGamePatternName, option = activeGamePattern, kind = "pattern" });
            if (activeGameFaceplate != null && !string.IsNullOrEmpty(activeGameFaceplateId))
                list.Add(new gamecosmeticEntry { id = activeGameFaceplateId, name = activeGameFaceplateName, option = activeGameFaceplate, kind = "faceplate" });
            return list;
        }

        public HashSet<string> GetAppliedGameCosmeticIds()
        {
            var ids = new HashSet<string>();
            foreach (var slot in activeGameCosmetics)
                if (slot != null && !string.IsNullOrEmpty(slot.id))
                    ids.Add(slot.id);
            return ids;
        }

        public static string GetGameCosmeticOptionId(CostumeOption option)
        {
            return "gamecosm:" + GetGameCosmeticId(option);
        }

        public static string GetGameColourOptionId(ColourOption option)
        {
            return "gamecolour:" + GetGameColourId(option);
        }

        public static string GetGamePatternOptionId(SkinPatternOption option)
        {
            return "gamepattern:" + GetGamePatternId(option);
        }

        public static string GetGameFaceplateOptionId(FaceplateOption option)
        {
            return "gamefaceplate:" + GetGameFaceplateId(option);
        }

        public string GetAppliedGameColourId() => activeGameColourId ?? "";
        public string GetAppliedGamePatternId() => activeGamePatternId ?? "";
        public string GetAppliedGameFaceplateId() => activeGameFaceplateId ?? "";

        public void ApplyGameColour(ColourOption option)
        {
            if (option == null) return;
            string id = GetGameColourOptionId(option);
            bool changed = activeGameColour == null || activeGameColourId != id;
            activeGameColour = option;
            activeGameColourId = id;
            activeGameColourName = GetGameColourName(option);
            SettingsService.Set("allcosmetics.colour", activeGameColourId);
            ApplyGameColourPatternToAllBeans();
            CustomSkinTextureTab.ReapplyAllEnabledFromSettings();
            if (changed) SpawnGameCosmeticPoof();
        }

        public void ApplyGamePattern(SkinPatternOption option)
        {
            if (option == null) return;
            string id = GetGamePatternOptionId(option);
            bool changed = activeGamePattern == null || activeGamePatternId != id;
            try { option.LoadBlocking(); } catch { }
            activeGamePattern = option;
            activeGamePatternId = id;
            activeGamePatternName = GetGamePatternName(option);
            SettingsService.Set("allcosmetics.pattern", activeGamePatternId);
            ApplyGameColourPatternToAllBeans();
            CustomSkinTextureTab.ReapplyAllEnabledFromSettings();
            if (changed) SpawnGameCosmeticPoof();
        }

        public void RemoveGameColour()
        {
            if (activeGameColour == null) return;
            activeGameColour = null;
            activeGameColourId = activeGameColourName = null;
            SettingsService.Set("allcosmetics.colour", "");
            RestoreDefaultColourPatternFaceplate();
            SpawnGameCosmeticPoof();
            OnSkinRemoved?.Invoke("Game colour removed");
        }

        public void RemoveGamePattern()
        {
            if (activeGamePattern == null) return;
            activeGamePattern = null;
            activeGamePatternId = activeGamePatternName = null;
            SettingsService.Set("allcosmetics.pattern", "");
            RestoreDefaultColourPatternFaceplate();
            SpawnGameCosmeticPoof();
            OnSkinRemoved?.Invoke("Game pattern removed");
        }

        public void ApplyGameFaceplate(FaceplateOption option)
        {
            if (option == null) return;
            string id = GetGameFaceplateOptionId(option);
            bool changed = activeGameFaceplate == null || activeGameFaceplateId != id;
            activeGameFaceplate = option;
            activeGameFaceplateId = id;
            activeGameFaceplateName = GetGameFaceplateName(option);
            SettingsService.Set("allcosmetics.faceplate", activeGameFaceplateId);
            ApplyGameColourPatternToAllBeans();
            if (changed) SpawnGameCosmeticPoof();
        }

        public void RemoveGameFaceplate()
        {
            if (activeGameFaceplate == null) return;
            activeGameFaceplate = null;
            activeGameFaceplateId = activeGameFaceplateName = null;
            SettingsService.Set("allcosmetics.faceplate", "");
            RestoreDefaultColourPatternFaceplate();
            SpawnGameCosmeticPoof();
            OnSkinRemoved?.Invoke("Game faceplate removed");
        }

        private void RestoreDefaultColourPatternFaceplate()
        {
            CustomisationSelections sel = null;
            try
            {
                var mm = GameObject.Find("MainMenuManager")?.GetComponent<MainMenuManager>();
                sel = mm?._playerProfile?.CustomisationSelections;
            }
            catch { }
            if (sel == null) return;

            var beans = GetBeans();
            for (int i = 0; i < beans.Count; i++)
            {
                var bean = beans[i];
                if (bean == null || SkipMenuSkinAutoApplyForThisBean(bean)) continue;
                var fgch = bean.GetComponent<FallguyCustomisationHandler>();
                if (fgch == null) continue;

                if (activeGameColour == null && sel.ColourOption != null)
                {
                    try { fgch.UpdateColourOption(sel.ColourOption); } catch { }
                }
                if (activeGamePattern == null && sel.PatternOption != null)
                {
                    try { sel.PatternOption.LoadBlocking(); } catch { }
                    try { fgch.UpdatePatternTexture(sel.PatternOption); } catch { }
                }
                if (activeGameFaceplate == null && sel.FaceplateOption != null)
                {
                    try { fgch.UpdateFaceplateColours(sel.FaceplateOption); } catch { }
                }
            }
        }

        private static void SaveGameCosmeticIds(IEnumerable<string> ids)
        {
            var list = new List<string>();
            if (ids != null)
                foreach (var id in ids)
                    if (!string.IsNullOrEmpty(id) && !list.Contains(id)) list.Add(id);
            SettingsService.Set("allcosmetics.ids", string.Join("|", list));
        }

        // Resources.FindObjectsOfTypeAll(<type>) is brutally expensive in il2cpp - it walks every
        // loaded object. The old code called it up to 4x per coroutine iteration AND up to 80 iters
        // (every 0.25s) per restore, which torched the main thread for 2-3 seconds on reward/menu
        // transitions. Cache the id→option maps with a short TTL so back-to-back applies (and the
        // retry loop) share one scan instead of one-per-call.
        private static Dictionary<string, CostumeOption> _costumeByIdCache;
        private static Dictionary<string, ColourOption> _colourByIdCache;
        private static Dictionary<string, SkinPatternOption> _patternByIdCache;
        private static Dictionary<string, FaceplateOption> _faceplateByIdCache;
        private static float _costumeCacheStamp, _colourCacheStamp, _patternCacheStamp, _faceplateCacheStamp;
        private const float CACHE_TTL = 2f;

        public static void InvalidateGameCosmeticLookupCaches()
        {
            _costumeByIdCache = null; _colourByIdCache = null;
            _patternByIdCache = null; _faceplateByIdCache = null;
        }

        private static Dictionary<string, CostumeOption> GetCostumeLookup()
        {
            if (_costumeByIdCache != null && Time.realtimeSinceStartup - _costumeCacheStamp < CACHE_TTL) return _costumeByIdCache;
            var map = new Dictionary<string, CostumeOption>();
            var raw = Resources.FindObjectsOfTypeAll(Il2CppType.Of<CostumeOption>());
            for (int i = 0; raw != null && i < raw.Length; i++)
            {
                CostumeOption opt;
                try { opt = raw[i].Cast<CostumeOption>(); } catch { continue; }
                if (opt == null) continue;
                string id = GetGameCosmeticOptionId(opt);
                if (!map.ContainsKey(id)) map[id] = opt;
            }
            _costumeByIdCache = map; _costumeCacheStamp = Time.realtimeSinceStartup;
            return map;
        }

        private static Dictionary<string, ColourOption> GetColourLookup()
        {
            if (_colourByIdCache != null && Time.realtimeSinceStartup - _colourCacheStamp < CACHE_TTL) return _colourByIdCache;
            var map = new Dictionary<string, ColourOption>();
            var raw = Resources.FindObjectsOfTypeAll(Il2CppType.Of<ColourOption>());
            for (int i = 0; raw != null && i < raw.Length; i++)
            {
                ColourOption opt;
                try { opt = raw[i].Cast<ColourOption>(); } catch { continue; }
                if (opt == null) continue;
                string id = GetGameColourOptionId(opt);
                if (!map.ContainsKey(id)) map[id] = opt;
            }
            _colourByIdCache = map; _colourCacheStamp = Time.realtimeSinceStartup;
            return map;
        }

        private static Dictionary<string, SkinPatternOption> GetPatternLookup()
        {
            if (_patternByIdCache != null && Time.realtimeSinceStartup - _patternCacheStamp < CACHE_TTL) return _patternByIdCache;
            var map = new Dictionary<string, SkinPatternOption>();
            var raw = Resources.FindObjectsOfTypeAll(Il2CppType.Of<SkinPatternOption>());
            for (int i = 0; raw != null && i < raw.Length; i++)
            {
                SkinPatternOption opt;
                try { opt = raw[i].Cast<SkinPatternOption>(); } catch { continue; }
                if (opt == null) continue;
                string id = GetGamePatternOptionId(opt);
                if (!map.ContainsKey(id)) map[id] = opt;
            }
            _patternByIdCache = map; _patternCacheStamp = Time.realtimeSinceStartup;
            return map;
        }

        private static Dictionary<string, FaceplateOption> GetFaceplateLookup()
        {
            if (_faceplateByIdCache != null && Time.realtimeSinceStartup - _faceplateCacheStamp < CACHE_TTL) return _faceplateByIdCache;
            var map = new Dictionary<string, FaceplateOption>();
            var raw = Resources.FindObjectsOfTypeAll(Il2CppType.Of<FaceplateOption>());
            for (int i = 0; raw != null && i < raw.Length; i++)
            {
                FaceplateOption opt;
                try { opt = raw[i].Cast<FaceplateOption>(); } catch { continue; }
                if (opt == null) continue;
                string id = GetGameFaceplateOptionId(opt);
                if (!map.ContainsKey(id)) map[id] = opt;
            }
            _faceplateByIdCache = map; _faceplateCacheStamp = Time.realtimeSinceStartup;
            return map;
        }

        public void RestoreSavedGameCosmetics()
        {
            string rawIds = SettingsService.Get("allcosmetics.ids", "");
            string colourId = SettingsService.Get("allcosmetics.colour", "");
            string patternId = SettingsService.Get("allcosmetics.pattern", "");
            string faceplateId = SettingsService.Get("allcosmetics.faceplate", "");
            if ((string.IsNullOrEmpty(rawIds) && string.IsNullOrEmpty(colourId) && string.IsNullOrEmpty(patternId) && string.IsNullOrEmpty(faceplateId)) || gameCosmeticsRestoring) return;
            StartCoroutine(RestoreSavedGameCosmeticsCoroutine(rawIds, colourId, patternId, faceplateId).WrapToIl2Cpp());
        }

        private IEnumerator RestoreSavedGameCosmeticsCoroutine(string rawIds, string colourId, string patternId, string faceplateId)
        {
            gameCosmeticsRestoring = true;
            var wanted = new HashSet<string>();
            foreach (var id in rawIds.Split('|'))
                if (!string.IsNullOrEmpty(id)) wanted.Add(id);
            if (wanted.Count == 0 && string.IsNullOrEmpty(colourId) && string.IsNullOrEmpty(patternId) && string.IsNullOrEmpty(faceplateId)) { gameCosmeticsRestoring = false; yield break; }

            // StartCoroutine runs to the first yield synchronously — without this, the whole first
            // try (4x FindObjectsOfTypeAll scan + pattern LoadBlocking, ~300ms) lands inside the
            // OnMainMenuEntered frame that's already stacked with menu-enter work
            yield return null;

            for (int tries = 0; tries < 80; tries++)
            {
                var chosen = new List<CostumeOption>();
                ColourOption foundColour = null;
                SkinPatternOption foundPattern = null;
                FaceplateOption foundFaceplate = null;

                if (wanted.Count > 0)
                {
                    var costumes = GetCostumeLookup();
                    foreach (var id in wanted)
                        if (costumes.TryGetValue(id, out var opt) && opt != null) chosen.Add(opt);
                }
                if (!string.IsNullOrEmpty(colourId)) GetColourLookup().TryGetValue(colourId, out foundColour);
                if (!string.IsNullOrEmpty(patternId)) GetPatternLookup().TryGetValue(patternId, out foundPattern);
                if (!string.IsNullOrEmpty(faceplateId)) GetFaceplateLookup().TryGetValue(faceplateId, out foundFaceplate);

                bool gotCostumes = wanted.Count == 0 || chosen.Count > 0;
                bool gotColour = string.IsNullOrEmpty(colourId) || foundColour != null;
                bool gotPattern = string.IsNullOrEmpty(patternId) || foundPattern != null;
                bool gotFaceplate = string.IsNullOrEmpty(faceplateId) || foundFaceplate != null;
                if (gotCostumes && gotColour && gotPattern && gotFaceplate)
                {
                    if (wanted.Count > 0) ApplyGameCosmeticSelection(chosen, wanted);
                    if (foundColour != null) { activeGameColour = foundColour; activeGameColourId = colourId; activeGameColourName = GetGameColourName(foundColour); }
                    if (foundPattern != null) { try { foundPattern.LoadBlocking(); } catch { } activeGamePattern = foundPattern; activeGamePatternId = patternId; activeGamePatternName = GetGamePatternName(foundPattern); }
                    if (foundFaceplate != null) { activeGameFaceplate = foundFaceplate; activeGameFaceplateId = faceplateId; activeGameFaceplateName = GetGameFaceplateName(foundFaceplate); }
                    ApplyGameColourPatternToAllBeans();
                    gameCosmeticsRestoring = false;
                    yield break;
                }
                // missed something - invalidate so the next try re-scans rather than reusing a stale
                // map that still won't have what we need
                InvalidateGameCosmeticLookupCaches();
                yield return new WaitForSeconds(0.25f);
            }
            gameCosmeticsRestoring = false;
        }

        // apply a PROFILE's game cosmetics (costumes + colour + pattern + faceplate) to ONE specific
        // bean, resolving the ids ourselves. does NOT touch the local player's global activeGame*
        // state - this is for other players' beans via the profiles feature.
        // onDone runs AFTER the costumes + colour land, so callers can apply skin textures then
        // (otherwise the costume meshes don't exist yet and texture matching finds nothing).
        public void ApplyProfileCosmeticsToBean(string rawIds, string colourId, string patternId, string faceplateId, GameObject bean, Action onDone = null)
        {
            if (bean == null) return;
            StartCoroutine(ApplyProfileCosmeticsToBeanCoroutine(rawIds, colourId, patternId, faceplateId, bean, onDone).WrapToIl2Cpp());
        }

        private IEnumerator ApplyProfileCosmeticsToBeanCoroutine(string rawIds, string colourId, string patternId, string faceplateId, GameObject bean, Action onDone)
        {
            yield return new WaitForSeconds(1f);
            if (bean == null) yield break;

            var wanted = new HashSet<string>();
            foreach (var id in (rawIds ?? "").Split('|'))
                if (!string.IsNullOrEmpty(id)) wanted.Add(id);

            var chosen = new List<CostumeOption>();
            ColourOption foundColour = null;
            SkinPatternOption foundPattern = null;
            FaceplateOption foundFaceplate = null;

            if (wanted.Count > 0)
            {
                var costumes = GetCostumeLookup();
                foreach (var id in wanted)
                    if (costumes.TryGetValue(id, out var opt) && opt != null) chosen.Add(opt);
            }
            if (!string.IsNullOrEmpty(colourId)) GetColourLookup().TryGetValue(colourId, out foundColour);
            if (!string.IsNullOrEmpty(patternId)) GetPatternLookup().TryGetValue(patternId, out foundPattern);
            if (!string.IsNullOrEmpty(faceplateId)) GetFaceplateLookup().TryGetValue(faceplateId, out foundFaceplate);

            // remember this profile's costume options for the bean, so its cosmetic masks composite
            // against ITS top/bottom costume rather than the local player's
            profileBeanCostumes[bean.GetInstanceID()] = new List<CostumeOption>(chosen);

            // colour/pattern/faceplate FIRST, so each costume mesh composites with them as it binds
            // (the local bean works because activeGameColour is set before costumes apply), then
            // again after for the base body.
            var fgch = bean.GetComponent<FallguyCustomisationHandler>();
            ApplyProfileColourPattern(fgch, foundColour, foundPattern, foundFaceplate);

            foreach (var opt in chosen)
            {
                string id = GetGameCosmeticOptionId(opt);
                string key = MakeKey(bean, id);
                if (appliedSkins.ContainsKey(key) || pendingKeys.Contains(key)) continue;
                var slot = new gamecosmeticSlot { option = opt, id = id, name = GetGameCosmeticName(opt), remotePipeline = true };
                pendingKeys.Add(key);
                Debug.Log($"[Profiles] cosmetic '{slot.name}' -> {bean.name}");
                yield return ApplyGameCosmeticToBeanCoroutine(slot, bean, ApplyReason.AutoReapply).WrapToIl2Cpp();
            }

            ApplyProfileColourPattern(fgch, foundColour, foundPattern, foundFaceplate);

            try { onDone?.Invoke(); } catch (Exception ex) { Debug.LogWarning("[Profiles] onDone: " + ex.Message); }
        }

        private void ApplyProfileColourPattern(FallguyCustomisationHandler fgch, ColourOption colour, SkinPatternOption pattern, FaceplateOption faceplate)
        {
            if (fgch == null) return;
            if (colour != null) { try { fgch.UpdateColourOption(colour); } catch (Exception ex) { Debug.LogWarning("[Profiles] colour: " + ex.Message); } }
            if (pattern != null) { try { pattern.LoadBlocking(); } catch { } try { fgch.UpdatePatternTexture(pattern); } catch (Exception ex) { Debug.LogWarning("[Profiles] pattern: " + ex.Message); } }
            if (faceplate != null) { try { fgch.UpdateFaceplateColours(faceplate); } catch (Exception ex) { Debug.LogWarning("[Profiles] faceplate: " + ex.Message); } }
        }

        public void ReapplyExpectedGameCosmeticMasks(GameObject bean = null)
        {
            if (activeGameCosmetics.Count == 0) return;

            if (bean != null)
            {
                StartCoroutine(ReapplyGameCosmeticMasksCoroutine(bean, null).WrapToIl2Cpp());
                return;
            }

            var beans = GetBeans();
            for (int i = 0; i < beans.Count; i++)
            {
                var b = beans[i];
                if (b == null || SkipMenuSkinAutoApplyForThisBean(b)) continue;
                StartCoroutine(ReapplyGameCosmeticMasksCoroutine(b, null).WrapToIl2Cpp());
            }
        }

        public void ReapplyExpectedGameCosmeticVisuals(GameObject bean = null)
        {
            ReapplyExpectedGameCosmeticMasks(bean);
            if (activeGameColour == null && activeGamePattern == null) return;
            if (bean != null)
            {
                if (!SkipMenuSkinAutoApplyForThisBean(bean)) ApplyGameColourPatternToBean(bean);
                CustomSkinTextureTab.ReapplyAllEnabledFromSettings();
                return;
            }
            var beans = GetBeans();
            for (int i = 0; i < beans.Count; i++)
                if (beans[i] != null && !SkipMenuSkinAutoApplyForThisBean(beans[i]))
                    ApplyGameColourPatternToBean(beans[i]);
            CustomSkinTextureTab.ReapplyAllEnabledFromSettings();
        }

        public bool IsBindingGameCosmetic(GameObject bean)
        {
            return bean != null && gameCosmeticBindingBeans.Contains(bean.GetInstanceID());
        }

        // removes all applied skins for a specific bean and restores its original mesh visibility
        public void RemoveAllSkins(GameObject bean)
        {
            if (bean == null) return;
            applyStamp++;
            RevertCustomTexture(bean);
            string prefix = bean.GetInstanceID() + "|";
            Debug.Log($"[SkinApplication] RemoveAllSkins for {bean.name}");

            var toKill = new List<string>();
            foreach (var kvp in appliedSkins)
            {
                if (!kvp.Key.StartsWith(prefix)) continue;
                var entry = kvp.Value;
                if (entry?.instance != null)
                {
                    var poller = entry.instance.GetComponent<CostumePollerComponent>();
                    if (poller != null) { poller.enabled = false; poller.beanGEO = null; }
                    DestroyAppliedInstance(entry.instance, entry.addressableInstance);
                }
                if (entry?.beanGEO != null)
                {
                    RestoreBaseBeanVisibility(kvp.Key, entry);
                }
                toKill.Add(kvp.Key);
            }
            foreach (var k in toKill) { appliedSkins.Remove(k); pendingKeys.Remove(k); }

            // remove active slots for this bean's files and unload bundles no longer used
            activeSlots.RemoveAll(s => toKill.Exists(k => SkinFileFromAppliedKey(k) == s.skinInfo.file));
            PruneLoadedBundlesNotUsedByAppliedSkins();
            redownloadingFiles.Clear();
            PlayerScaleService.ApplyToBean(bean,
                PlayerScaleService.HasUserSetScale() ? PlayerScaleService.GetPlayerScale() : 1f,
                IsRemoteInRoundBean(bean) ? PlayerScaleService.BeanScaleMode.Remote : PlayerScaleService.BeanScaleMode.Local);
            OnSkinRemoved?.Invoke($"Skins removed for {bean.name}");
        }



        // ── Custom texture API ────────────────────────────────────────────────
        // scans bean GEO so normal costumes, custom skins, and additive cosmetics all count
        public int ApplyCustomTexture(GameObject bean, int matSlotIdx, Texture2D tex, HashSet<string> matchTexNames)
        {
            if (bean == null || tex == null) return 0;

            var geo = FindBeanGEO(bean);
            if (geo == null) return 0;
            int beanId = bean.GetInstanceID();
            int texSlot = 0;

            return ApplyTextureToGameObject(geo.gameObject, matSlotIdx, tex, matchTexNames, beanId, ref texSlot);
        }

        private int ApplyTextureToGameObject(GameObject costumeObj, int matSlotIdx, Texture2D tex, HashSet<string> matchTexNames, int beanId, ref int texSlot)
        {
            int count = 0;
            var renderers = costumeObj.GetComponentsInChildren<Renderer>(true);
            bool alreadyHadOriginals = customTexOriginals.TryGetValue(beanId, out var originalList);
            if (originalList == null)
                originalList = new List<customTexOriginal>();

            foreach (var r in renderers)
            {
                if (r == null) continue;
                var mats = r.materials;
                if (mats == null) continue;
                bool touched = false;

                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;

                    bool hadTextureSlot = false;

                    foreach (var prop in customTexProps)
                    {
                        try
                        {
                            bool liveHasProp = m.HasProperty(prop);
                            if (!liveHasProp) continue;

                            var originalTex = FindCustomTexOriginal(originalList, r, i, prop, out string savedName);
                            if (originalTex == null)
                                originalTex = m.GetTexture(prop);
                            if (originalTex == null) continue;

                            hadTextureSlot = true;
                            string originalTexName = savedName ?? originalTex.name ?? "";
                            string matName = CleanMatName(m.name);
                            bool hasNameFilter = matchTexNames != null && matchTexNames.Count > 0;
                            bool nameMatch = hasNameFilter && (matchTexNames.Contains(originalTexName) || matchTexNames.Contains(matName));
                            bool slotMatch = texSlot == matSlotIdx;
                            if (hasNameFilter ? !nameMatch : !slotMatch) continue;

                            RememberCustomTexOriginal(originalList, r, i, prop, originalTex, originalTexName);

                            if (!string.IsNullOrEmpty(originalTexName))
                                tex.name = originalTexName;
                            m.SetTexture(prop, tex);
                            touched = true;
                            break;
                        }
                        catch { }
                    }

                    if (hadTextureSlot) texSlot++;
                }

                if (touched)
                {
                    r.materials = mats;
                    count++;
                }
            }
            if (count > 0 && !alreadyHadOriginals)
                customTexOriginals[beanId] = originalList;
            return count;
        }

        private static string CleanMatName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.EndsWith(" (Instance)") ? name.Substring(0, name.Length - 11) : name;
        }

        private static Texture FindCustomTexOriginal(List<customTexOriginal> originals, Renderer renderer, int matIdx, string prop, out string textureName)
        {
            textureName = null;
            if (originals == null) return null;

            for (int i = 0; i < originals.Count; i++)
            {
                var o = originals[i];
                if (o.renderer == renderer && o.matIdx == matIdx && o.prop == prop)
                {
                    textureName = o.textureName;
                    return o.texture;
                }
            }

            return null;
        }

        private static void RememberCustomTexOriginal(List<customTexOriginal> originals, Renderer renderer, int matIdx, string prop, Texture texture, string textureName)
        {
            if (originals == null || renderer == null || texture == null) return;

            for (int i = 0; i < originals.Count; i++)
            {
                var o = originals[i];
                if (o.renderer == renderer && o.matIdx == matIdx && o.prop == prop) return;
            }

            originals.Add(new customTexOriginal
            {
                renderer = renderer,
                matIdx = matIdx,
                prop = prop,
                texture = texture,
                textureName = textureName
            });
        }

        public void RevertCustomTexture(GameObject bean)
        {
            if (bean == null) return;
            int beanId = bean.GetInstanceID();
            // always re-enable a fresh attempt next time the bean rebinds — even for beans that
            // never matched (those aren't in customTexOriginals but DID get marked attempted).
            customTexAttemptedBeans.Remove(beanId);
            if (!customTexOriginals.TryGetValue(beanId, out var originals)) return;

            foreach (var o in originals)
            {
                var r = o.renderer;
                if (r == null) continue;
                try
                {
                    var mats = r.materials;
                    if (mats == null || o.matIdx < 0 || o.matIdx >= mats.Length) continue;
                    var m = mats[o.matIdx];
                    if (m == null || !m.HasProperty(o.prop)) continue;
                    m.SetTexture(o.prop, o.texture);
                    r.materials = mats;
                }
                catch { }
            }

            customTexOriginals.Remove(beanId);
        }

        // collect all renderers recursively (il2cpp safe), no LOD filtering
        private static void CollectAllRenderers(GameObject root, Dictionary<Renderer, Material[]> results)
        {
            if (root == null) return;
            CollectAllRenderersRecursive(root.transform, results);
        }

        private static void CollectAllRenderersRecursive(Transform t, Dictionary<Renderer, Material[]> results)
        {
            if (t == null) return;
            var r = t.GetComponent<Renderer>();
            if (r != null && !results.ContainsKey(r))
                results[r] = r.sharedMaterials;
            for (int i = 0; i < t.childCount; i++)
            {
                var child = t.GetChild(i);
                if (child != null) CollectAllRenderersRecursive(child, results);
            }
        }

        private void PruneLoadedBundlesNotUsedByAppliedSkins()
        {
            var stillUsed = new HashSet<string>();
            foreach (var k in appliedSkins.Keys)
            {
                string f = SkinFileFromAppliedKey(k);
                if (!string.IsNullOrEmpty(f)) stillUsed.Add(f);
            }
            // also keep remote-owned bundles alive
            foreach (var f in remoteBundleFiles)
                stillUsed.Add(f);

            var toUnload = new List<string>();
            foreach (var kvp in loadedBundles)
                if (!stillUsed.Contains(kvp.Key)) toUnload.Add(kvp.Key);
            foreach (var f in toUnload)
            {
                if (loadedBundles.TryGetValue(f, out var b) && b != null) b.Unload(false);
                loadedBundles.Remove(f);
            }
        }

        private static string SkinFileFromAppliedKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int pipe = key.IndexOf('|');
            if (pipe < 0 || pipe >= key.Length - 1) return null;
            string rest = key.Substring(pipe + 1);
            if (rest.EndsWith("_L", StringComparison.Ordinal)) return rest.Substring(0, rest.Length - 2);
            if (rest.EndsWith("_R", StringComparison.Ordinal)) return rest.Substring(0, rest.Length - 2);
            return rest;
        }

        // ── Internal ──────────────────────────────────────────────────────────

        private IEnumerator ApplySlotToAllBeansCoroutine(ActiveSkinSlot slot, ApplyReason reason = ApplyReason.FromMenu)
        {
            var beans = GetBeans();
            foreach (var bean in beans)
            {
                if (SlotDead(slot)) yield break;
                if (bean == null) continue;
                if (SkipMenuSkinAutoApplyForThisBean(bean)) continue;
                string key = MakeKey(bean, slot.skinInfo.file);
                if (appliedSkins.ContainsKey(key) || pendingKeys.Contains(key)) continue;
                pendingKeys.Add(key);
                yield return ApplySlotToBeanCoroutine(slot, bean, reason).WrapToIl2Cpp();
            }
        }

        private IEnumerator ApplySlotToBeanCoroutine(ActiveSkinSlot slot, GameObject bean, ApplyReason reason = ApplyReason.FromMenu)
        {
            if (SlotDead(slot)) yield break;
            switch (slot.type)
            {
                case SkinType.Costume: yield return ApplyCostumeCoroutine(slot, bean, reason).WrapToIl2Cpp(); break;
                case SkinType.Accessory: yield return ApplyAccessoryCoroutine(slot, bean, reason).WrapToIl2Cpp(); break;
                case SkinType.Item: yield return ApplyItemCoroutine(slot, bean, reason).WrapToIl2Cpp(); break;
            }
        }

        private void RemoveSlotByFile(string file)
        {
            activeSlots.RemoveAll((ActiveSkinSlot s) => s.skinInfo.file == file);

            // only destroy applied instances on the LOCAL bean — remote beans own their own applied entries
            var localBean = BeanMonitorService.LocalPlayerBean;
            string localPrefix = localBean != null ? (localBean.GetInstanceID() + "|") : null;

            var toKill = new List<string>();
            foreach (var kvp in appliedSkins)
            {
                if (SkinFileFromAppliedKey(kvp.Key) != file) continue;
                var bean = kvp.Value?.bean;
                if (localBean != null && bean != null && bean != localBean && IsRemoteInRoundBean(bean)) continue;
                if (localBean != null && bean == null && !kvp.Key.StartsWith(localPrefix)) continue;
                if (kvp.Value?.instance != null)
                {
                    var poller = kvp.Value.instance.GetComponent<CostumePollerComponent>();
                    if (poller != null) poller.enabled = false;
                    DestroyAppliedInstance(kvp.Value.instance, kvp.Value.addressableInstance);
                }
                if (kvp.Value?.beanGEO != null)
                {
                    RestoreBaseBeanVisibility(kvp.Key, kvp.Value);
                }
                toKill.Add(kvp.Key);
            }
            foreach (var k in toKill) { appliedSkins.Remove(k); pendingKeys.Remove(k); }
        }

        private bool SlotDead(ActiveSkinSlot slot)
        {
            if (slot == null || slot.skinInfo == null) return true;
            // remote-pipeline slots (debug_profiles / .bfgprofile) are one-shot applies to other
            // beans — they're never in activeSlots and nothing stamps them, so the menu-slot
            // liveness rules below would kill them instantly (and silently)
            if (slot.remotePipeline) return false;
            return slot.applyStamp != applyStamp || !activeSlots.Contains(slot);
        }

        private bool SlotDead(gamecosmeticSlot slot)
        {
            if (slot == null || slot.option == null) return true;
            if (slot.remotePipeline) return false; // one-shot remote-bean apply, not in activeGameCosmetics
            return slot.applyStamp != applyStamp || !activeGameCosmetics.Contains(slot);
        }

        private void RestoreBaseBeanVisibility(string key, AppliedSkinInfo entry)
        {
            if (entry == null) return;

            if (entry.disabledChildren != null)
                foreach (var go in entry.disabledChildren)
                    if (go != null) go.SetActive(true);

            if (entry.beanGEO == null) return;
            for (int i = 0; i < entry.beanGEO.childCount; i++)
            {
                Transform child = entry.beanGEO.GetChild(i).Cast<Transform>();
                if (child == null) continue;
                if (entry.instance != null && child.gameObject == entry.instance) continue;

                string matKey = key + "_" + i;
                if (hiddenRendererMaterials.TryGetValue(matKey, out var matMap))
                {
                    foreach (var kv in matMap)
                    {
                        if (kv.Key == null) continue;
                        kv.Key.materials = kv.Value;
                        kv.Key.enabled = true;
                        kv.Key.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                        kv.Key.receiveShadows = true;
                    }
                    hiddenRendererMaterials.Remove(matKey);
                }

                if (entry.restoreAllBaseGEO)
                    child.gameObject.SetActive(true);
            }
        }

        private IEnumerator BurstPollPoller(CostumePollerComponent poller)
        {
            float elapsed = 0f;
            while (poller != null && elapsed < 1f)
            {
                poller.PollNow();
                yield return new WaitForSeconds(0.05f);
                elapsed += 0.05f;
            }
        }

        private IEnumerator RestoreMenuBeanGEOSoon()
        {
            Debug.Log($"[RestoreMenu] start, activeGameCosmetics={activeGameCosmetics.Count}");
            // if cosmetics are parked under a just-removed costume, flip them straight back on — the
            // re-instantiate loop below only needs to handle ones that were never stashed.
            RestoreStashedGameCosmetics();
            for (int i = 0; i < 4; i++)
            {
                RestoreMenuBeanGEO();
                yield return null;
            }

            Debug.Log($"[RestoreMenu] after GEO restore, activeGameCosmetics={activeGameCosmetics.Count}");
            bool anyGameLook = activeGameCosmetics.Count > 0 || activeGameColour != null || activeGamePattern != null || activeGameFaceplate != null;
            if (!anyGameLook) { Debug.Log("[RestoreMenu] done"); yield break; }

            // re-composite cosmetics on EVERY non-remote bean, not just the menu plinth bean.
            // the falling-screen LobbyCharacter is a separate bean — a keepBase-off costume hid its
            // cosmetic meshes, and if we only restored _menuFallGuy the lobby bean stayed bare
            // (all game cosmetics "gone" on the falling screen). _menuFallGuy is in GetBeans() too.
            var beans = GetBeans();
            foreach (var bean in beans)
            {
                if (bean == null || SkipMenuSkinAutoApplyForThisBean(bean)) continue;
                Debug.Log($"[RestoreMenu] reapply on bean={bean.name}");
                // colour/pattern FIRST so the base body is the right colour before each cosmetic
                // rebinds — SetCostumeBaseCopyColor (in BindGameCosmeticToFallguy) copies the base
                // colour onto copy-colour costumes, so binding before this copies the wrong colour.
                ApplyGameColourPatternToBean(bean);
                foreach (var slot in new List<gamecosmeticSlot>(activeGameCosmetics))
                {
                    if (slot == null) continue;
                    string key = MakeKey(bean, slot.id);
                    bool had = appliedSkins.ContainsKey(key);
                    // already live (un-stashed by RestoreStashedGameCosmetics, instance intact)? leave
                    // it — destroying + re-instantiating it is exactly the 0.5s pop-in we're avoiding.
                    if (appliedSkins.TryGetValue(key, out var live) && live != null && !live.stashed && live.instance != null)
                        continue;
                    // Destroy the previous clone + reparented mesh GOs first — otherwise the
                    // reapply leaves the old CH_*(Clone) meshes orphaned under GEO, and they
                    // never get cleaned up by RemoveGameCosmeticById later.
                    if (appliedSkins.TryGetValue(key, out var prev) && prev != null)
                    {
                        DestroyAppliedInstance(prev.instance, prev.addressableInstance);
                        if (prev.boundRenderers != null)
                            foreach (var go in prev.boundRenderers)
                                if (go != null) GameObject.Destroy(go);
                    }
                    appliedSkins.Remove(key);
                    pendingKeys.Remove(key);
                    // RemoveMenuEquippedSlotsOnly incremented applyStamp, so the slot is now
                    // "dead" and ApplyGameCosmeticToBeanCoroutine would silently early-exit.
                    // Refresh so the reapply actually runs.
                    slot.applyStamp = applyStamp;
                    Debug.Log($"[RestoreMenu] cleared key for '{slot.id}' on {bean.name} (had={had})");
                }
                yield return ApplyActiveGameCosmeticsToBeanCoroutine(bean).WrapToIl2Cpp();
            }
            Debug.Log("[RestoreMenu] done");
        }

        private void RestoreMenuBeanGEO()
        {
            // reset base GEO visibility on every non-remote bean (menu plinth AND the falling-screen
            // LobbyCharacter), so removing a keepBase-off costume doesn't leave any of them bare.
            var beans = GetBeans();
            foreach (var bean in beans)
            {
                if (bean == null || SkipMenuSkinAutoApplyForThisBean(bean)) continue;
                var geo = FindBeanGEO(bean);
                if (geo == null) continue;

                for (int i = 0; i < geo.childCount; i++)
                {
                    var child = geo.GetChild(i).Cast<Transform>();
                    if (child == null || child.gameObject == null) continue;

                    bool bodyLod = child.name.StartsWith("Body_LOD", StringComparison.OrdinalIgnoreCase);
                    bool keepOff = bodyLod && !child.name.EndsWith("0", StringComparison.OrdinalIgnoreCase);
                    child.gameObject.SetActive(!keepOff);
                }
            }
        }

        private void PruneDestroyedEntries()
        {
            var toRemove = new List<string>();
            foreach (var kvp in appliedSkins)
                if (kvp.Value == null || kvp.Value.instance == null) toRemove.Add(kvp.Key);
            foreach (var k in toRemove) appliedSkins.Remove(k);
        }

        // before we overwrite appliedSkins[key] with a fresh clone, blow away whatever was already
        // sitting there. otherwise the old clone stays parented under GEO with its CostumePoller still
        // alive, and every leftover poller keeps running its 1s HideBeans sweep -> the exact-1s freeze
        // that gets worse every equip/unequip. reapplies (RestoreMenuBeanGEOSoon, auto-reapply) hit the
        // same key repeatedly, so without this they pile up pollers forever.
        private void KillExistingAppliedAtKey(string key)
        {
            if (!appliedSkins.TryGetValue(key, out var prev) || prev == null) return;
            DestroyAppliedInstance(prev.instance, prev.addressableInstance);
            if (prev.boundRenderers != null)
                foreach (var go in prev.boundRenderers)
                    if (go != null) GameObject.Destroy(go);
        }

        // game/UGC cosmetic instances come from Addressables InstantiateAsync — releasing through
        // Addressables decrements the handle ref count AND destroys the GameObject. plain
        // GameObject.Destroy skips the release so the loaded prefab leaks (the equip/unequip leak).
        // ReleaseInstance returns false for anything it isn't tracking, so fall back to Destroy.
        private static void DestroyAppliedInstance(GameObject go, bool addressable)
        {
            if (go == null) return;
            if (addressable)
            {
                try { if (UnityEngine.AddressableAssets.Addressables.ReleaseInstance(go)) return; }
                catch { }
            }
            GameObject.Destroy(go);
        }

        private static string MakeKey(GameObject bean, string skinFile) =>
            bean.GetInstanceID() + "|" + skinFile;

        // strip already-applied game-cosmetic meshes (costumes + their bound renderers) off the local
        // menu beans WITHOUT clearing activeGameCosmetics. used when a keepBase-off UGC costume becomes
        // active: the game cosmetics stay in the saved loadout (so they reappear when the costume comes
        // off) but their meshes are gone so the poller has nothing to keep fighting. colour/pattern/
        // faceplate are bean-state, not instances, and get re-stamped by the costume removal path.
        private void StripAppliedGameCosmeticInstancesLocal()
        {
            if (activeGameCosmetics.Count == 0) return;
            var ids = new HashSet<string>();
            foreach (var slot in activeGameCosmetics)
                if (slot != null && !string.IsNullOrEmpty(slot.id)) ids.Add(slot.id);

            // park the cosmetic meshes (SetActive false) instead of destroying them. the UGC costume
            // hides them anyway, and keeping the instances alive means RestoreStashedGameCosmetics can
            // flip them back on the instant the costume comes off — no re-instantiate, no 0.5s pop-in.
            // the poller is disabled so it doesn't fight to re-hide cosmetics it can't see.
            var localBean = BeanMonitorService.LocalPlayerBean;
            foreach (var kvp in appliedSkins)
            {
                string file = SkinFileFromAppliedKey(kvp.Key);
                if (file == null || !ids.Contains(file)) continue;
                var bean = kvp.Value?.bean;
                if (localBean != null && bean != null && bean != localBean && IsRemoteInRoundBean(bean)) continue;
                if (kvp.Value == null) continue;
                if (kvp.Value.instance != null)
                {
                    var poller = kvp.Value.instance.GetComponent<CostumePollerComponent>();
                    if (poller != null) poller.enabled = false;
                    kvp.Value.instance.SetActive(false);
                }
                if (kvp.Value.boundRenderers != null)
                    foreach (var go in kvp.Value.boundRenderers)
                        if (go != null) go.SetActive(false);
                kvp.Value.stashed = true;
            }
        }

        // re-show the game-cosmetic meshes StripAppliedGameCosmeticInstancesLocal parked when a UGC
        // costume went on. instant flip-back, no re-instantiate. returns true if any entry was stashed
        // (so the caller knows it doesn't need the slow re-composite). re-enables the poller so it
        // keeps the cosmetics composited again now that the base body is back.
        private bool RestoreStashedGameCosmetics()
        {
            var localBean = BeanMonitorService.LocalPlayerBean;
            bool any = false;
            foreach (var kvp in appliedSkins)
            {
                var entry = kvp.Value;
                if (entry == null || !entry.stashed) continue;
                var bean = entry.bean;
                if (localBean != null && bean != null && bean != localBean && IsRemoteInRoundBean(bean)) continue;
                if (entry.instance != null)
                {
                    entry.instance.SetActive(true);
                    var poller = entry.instance.GetComponent<CostumePollerComponent>();
                    if (poller != null) poller.enabled = true;
                }
                if (entry.boundRenderers != null)
                    foreach (var go in entry.boundRenderers)
                        if (go != null) go.SetActive(true);
                entry.stashed = false;
                any = true;
            }
            if (any)
            {
                var beans = GetBeans();
                foreach (var bean in beans)
                    if (bean != null && !SkipMenuSkinAutoApplyForThisBean(bean))
                        ApplyGameColourPatternToBean(bean);
            }
            return any;
        }

        private void RemoveGameCosmeticById(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            CostumeOption removedOption = null;
            foreach (var slot in activeGameCosmetics)
            {
                if (slot == null || slot.id != id) continue;
                removedOption = slot.option;
                break;
            }
            activeGameCosmetics.RemoveAll(s => s == null || s.id == id);

            var localBean = BeanMonitorService.LocalPlayerBean;
            var toKill = new List<string>();
            var affectedBeans = new List<GameObject>();
            foreach (var kvp in appliedSkins)
            {
                if (SkinFileFromAppliedKey(kvp.Key) != id) continue;
                var bean = kvp.Value?.bean;
                if (localBean != null && bean != null && bean != localBean && IsRemoteInRoundBean(bean)) continue;
                if (bean != null && !affectedBeans.Contains(bean)) affectedBeans.Add(bean);
                if (kvp.Value?.instance != null)
                {
                    var poller = kvp.Value.instance.GetComponent<CostumePollerComponent>();
                    if (poller != null) poller.enabled = false;
                    DestroyAppliedInstance(kvp.Value.instance, kvp.Value.addressableInstance);
                }
                if (kvp.Value?.boundRenderers != null)
                {
                    foreach (var go in kvp.Value.boundRenderers)
                        if (go != null) GameObject.Destroy(go);
                }
                if (kvp.Value?.beanGEO != null)
                    RestoreBaseBeanVisibility(kvp.Key, kvp.Value);
                toKill.Add(kvp.Key);
            }
            foreach (var k in toKill) { appliedSkins.Remove(k); pendingKeys.Remove(k); }

            foreach (var bean in affectedBeans)
                StartCoroutine(ReapplyGameCosmeticMasksCoroutine(bean, removedOption).WrapToIl2Cpp());
        }

        // Applies the currently active game (unlocked) cosmetics, colour and pattern to one specific bean.
        // Used by callers that spawn their own beans (e.g. the PB ghost) which aren't picked up by the
        // auto-apply-to-all-beans passes.
        public IEnumerator ApplyActiveGameCosmeticsToBeanCoroutine(GameObject bean, ApplyReason reason = ApplyReason.FromMenu)
        {
            Debug.Log($"[ApplyActive] bean={(bean != null ? bean.name : "NULL")} slots={activeGameCosmetics.Count}");
            if (bean == null) yield break;
            foreach (var slot in new List<gamecosmeticSlot>(activeGameCosmetics))
            {
                if (slot == null) continue;
                string key = MakeKey(bean, slot.id);
                bool inApplied = appliedSkins.ContainsKey(key);
                bool inPending = pendingKeys.Contains(key);
                Debug.Log($"[ApplyActive] slot='{slot.id}' inApplied={inApplied} inPending={inPending}");
                if (inApplied || inPending) continue;
                pendingKeys.Add(key);
                yield return ApplyGameCosmeticToBeanCoroutine(slot, bean, reason).WrapToIl2Cpp();
            }
            ApplyGameColourPatternToBean(bean);
        }

        private IEnumerator ApplyGameCosmeticToAllBeansCoroutine(gamecosmeticSlot slot, ApplyReason reason)
        {
            var beans = GetBeans();
            foreach (var bean in beans)
            {
                if (SlotDead(slot)) yield break;
                if (bean == null || SkipMenuSkinAutoApplyForThisBean(bean)) continue;
                string key = MakeKey(bean, slot.id);
                if (appliedSkins.ContainsKey(key) || pendingKeys.Contains(key)) continue;
                pendingKeys.Add(key);
                yield return ApplyGameCosmeticToBeanCoroutine(slot, bean, reason).WrapToIl2Cpp();
            }
        }

        private IEnumerator ApplyGameCosmeticToBeanCoroutine(gamecosmeticSlot slot, GameObject bean, ApplyReason reason)
        {
            string pendingKey = MakeKey(bean, slot.id);
            if (SlotDead(slot) || bean == null) { pendingKeys.Remove(pendingKey); yield break; }
            // full-body UGC costume on the local loadout hides this anyway — don't apply it (profile
            // beans come in via remotePipeline and are exempt; their costumes are their own concern)
            if (!slot.remotePipeline && LoadoutHidesBaseBody()) { pendingKeys.Remove(pendingKey); yield break; }

            Transform beanGEO = FindBeanGEO(bean);
            if (beanGEO == null) { Debug.LogWarning($"[SA:GameCosm] no GEO on {bean.name}"); pendingKeys.Remove(pendingKey); yield break; }

            var disabled = new List<GameObject>();

            GameObject clone = null;
            string err = null;
            UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject> op;
            try { op = slot.option.costumePrefabReference.InstantiateAsync(); }
            catch (Exception ex) { err = ex.Message; op = default; }

            if (err == null)
            {
                // bounded wait instead of a bare `yield return op`. if the addressable load hangs
                // (round transition thrashing addressables) or the bean/slot dies underneath us, a
                // plain yield would suspend forever and leave pendingKey stuck — every later reapply
                // then sees inPending=true and skips, so the cosmetic silently never comes back.
                float waited = 0f;
                while (!op.IsDone && waited < 8f)
                {
                    if (bean == null || SlotDead(slot)) break;
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
                if (op.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
                    clone = op.Result;
                else if (!op.IsDone)
                    err = $"instantiate timed out after {waited:0.0}s";
                else
                {
                    try { err = op.OperationException != null ? op.OperationException.Message : "instantiate failed"; }
                    catch { err = "instantiate failed"; }
                }
            }

            if (err != null || clone == null)
            {
                Debug.LogWarning($"[SA:GameCosm] instantiate failed: {err}");
                // release the handle so a load that finishes after our timeout doesn't leak an
                // orphaned addressable instance (ReleaseInstance no-ops if it isn't done yet, but the
                // handle release still decrements the ref count once it resolves)
                try { if (op.IsValid()) UnityEngine.AddressableAssets.Addressables.Release(op); } catch { }
                RestoreBaseBeanVisibility(pendingKey, new AppliedSkinInfo { disabledChildren = disabled });
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            if (SlotDead(slot))
            {
                RestoreBaseBeanVisibility(pendingKey, new AppliedSkinInfo { disabledChildren = disabled });
                DestroyAppliedInstance(clone, true);
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.transform.SetParent(beanGEO, true);
            clone.transform.position = beanGEO.position;
            clone.transform.rotation = beanGEO.rotation;
            clone.transform.localScale = Vector3.one;

            if (GameObjectHelper.IsUICharacter(bean) || GameObjectHelper.IsLobbyCharacter(bean))
                GameObjectHelper.SetLayerRecursively(clone, LayerMask.NameToLayer("PlayerUI"));

            var info = new SkinInfo { name = slot.name, file = slot.id, type = "Costume", keepBase = true };

            // Snapshot beanGEO children before bind; BindMeshToFallguy reparents/spawns mesh
            // GameObjects directly under GEO (e.g. CH_Digi_Top(Clone), CH_Digi_Bottom(Clone)) that
            // are NOT children of `clone` — so destroying clone alone leaves them visible.
            var preBindGeoChildren = new HashSet<int>();
            for (int i = 0; i < beanGEO.childCount; i++)
            {
                var c = beanGEO.GetChild(i);
                if (c != null && c.gameObject != null) preBindGeoChildren.Add(c.gameObject.GetInstanceID());
            }

            var boundRenderers = BindGameCosmeticToFallguy(clone, bean);

            for (int i = 0; i < beanGEO.childCount; i++)
            {
                var c = beanGEO.GetChild(i);
                if (c == null || c.gameObject == null) continue;
                var go = c.gameObject;
                if (go == clone) continue;
                if (preBindGeoChildren.Contains(go.GetInstanceID())) continue;
                if (!boundRenderers.Contains(go)) boundRenderers.Add(go);
            }

            yield return ApplyGameCosmeticMaskCoroutine(slot.option, bean).WrapToIl2Cpp();
            ApplyGameColourPatternToBean(bean);

            KillExistingAppliedAtKey(pendingKey);
            appliedSkins[pendingKey] = new AppliedSkinInfo { instance = clone, bean = bean, type = SkinType.Costume, disabledChildren = disabled, boundRenderers = boundRenderers, addressableInstance = true };
            pendingKeys.Remove(pendingKey);
            CustomSkinTextureTab.ReapplyAllEnabledFromSettings();
            OnSkinApplied?.Invoke(new SkinApplyEvent { skinInfo = info, bean = bean, reason = reason });
        }

        private List<GameObject> BindGameCosmeticToFallguy(GameObject clone, GameObject bean)
        {
            var boundGos = new List<GameObject>();
            var fgch = bean.GetComponent<FallguyCustomisationHandler>();
            if (fgch == null)
            {
                Debug.LogWarning($"[SA:GameCosm] no FallguyCustomisationHandler on {bean.name}");
                return boundGos;
            }
            int bound = 0;
            var smrs = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int beanId = bean.GetInstanceID();
            gameCosmeticBindingBeans.Add(beanId);
            try
            {
                if (fgch._topRenderers == null)
                    fgch._topRenderers = new Il2CppSystem.Collections.Generic.List<SkinnedMeshRenderer>();
                if (fgch._bottomRenderers == null)
                    fgch._bottomRenderers = new Il2CppSystem.Collections.Generic.List<SkinnedMeshRenderer>();

                for (int i = 0; i < smrs.Length; i++)
                {
                    var smr = smrs[i];
                    if (smr == null) continue;
                    try
                    {
                        fgch.BindMeshToFallguy(smr);
                        bound++;
                        // BindMeshToFallguy reparents the SMR's GameObject out of `clone`.
                        // Track it so removal can destroy it — otherwise the visible mesh persists
                        // after we Destroy(clone) because the SMR is no longer a child of clone.
                        if (smr != null && smr.gameObject != null && !boundGos.Contains(smr.gameObject))
                            boundGos.Add(smr.gameObject);

                        // copy-colour costumes use the Mediatonic/CH_Costume_ColorCopy shader which
                        // only recolours once the SMR is registered as a top/bottom renderer AND
                        // SetCostumeBaseCopyColor runs after that assignment. so: assign, then call.
                        string n = smr.name != null ? smr.name.ToLowerInvariant() : "";
                        bool isBottom = n.Contains("bottom") || n.Contains("lower") || n.Contains("leg") || n.Contains("feet") || n.Contains("foot");
                        if (isBottom)
                        {
                            if (!fgch._bottomRenderers.Contains(smr)) fgch._bottomRenderers.Add(smr);
                        }
                        else
                        {
                            if (!fgch._topRenderers.Contains(smr)) fgch._topRenderers.Add(smr);
                        }
                        try { fgch.SetCostumeBaseCopyColor(); }
                        catch (Exception ex) { Debug.LogWarning($"[SA:GameCosm] SetCostumeBaseCopyColor failed: {ex.Message}"); }
                    }
                    catch (Exception ex) { Debug.LogWarning($"[SA:GameCosm] bind failed on {smr.name}: {ex.Message}"); }
                }
            }
            finally
            {
                gameCosmeticBindingBeans.Remove(beanId);
            }


            return boundGos;
        }

        private IEnumerator ApplyGameCosmeticMaskCoroutine(CostumeOption option, GameObject bean)
        {
            if (option == null || bean == null) yield break;

            var fgch = bean.GetComponent<FallguyCustomisationHandler>();
            if (fgch == null) yield break;

            // load this cosmetic's mask
            Texture mask = null;
            bool loaded = false;
            yield return LoadCostumeMaskCoroutine(option, "cosm", r => { mask = r; loaded = true; }).WrapToIl2Cpp();
            if (!loaded || mask == null)
            {
                Debug.LogWarning($"[SA:GameCosm] no costume mask for {GetGameCosmeticName(option)}");
                yield break;
            }

            // collect real costume masks (top + bottom) so we can union them with ours —
            // SetMask replaces per slot so we must composite all the masks into one texture.
            // for a remote PROFILE bean use ITS costume options (recorded when the profile applied),
            // not GlobalGameStateClient which is always the local player.
            var realOpts = GetRealCostumeOptionsForBean(bean);

            var maskList = new List<Texture> { mask };
            foreach (var ro in realOpts)
            {
                if (ro == null) continue;
                Texture rm = null;
                bool rl = false;
                yield return LoadCostumeMaskCoroutine(ro, "real", r => { rm = r; rl = true; }).WrapToIl2Cpp();
                if (rl && rm != null) maskList.Add(rm);
            }

            Texture finalMask = maskList.Count == 1 ? mask : CompositeMasks(maskList);

            try
            {
                fgch.SetMask(option, finalMask);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SA:GameCosm] SetMask failed: {ex.Message}");
            }
        }

        // which top/bottom costume options to union a cosmetic mask against for this bean. remote
        // profile beans use the costumes that profile applied; everyone else falls back to the local
        // player's selections (correct for the local bean).
        private List<CostumeOption> GetRealCostumeOptionsForBean(GameObject bean)
        {
            var result = new List<CostumeOption>();
            if (bean != null && profileBeanCostumes.TryGetValue(bean.GetInstanceID(), out var profileCostumes) && profileCostumes != null)
            {
                foreach (var c in profileCostumes) if (c != null) result.Add(c);
                return result;
            }

            var sel = GlobalGameStateClient.Instance?.PlayerProfile?.CustomisationSelections;
            if (sel != null)
            {
                if (sel.CostumeTopOption != null) result.Add(sel.CostumeTopOption);
                if (sel.CostumeBottomOption != null) result.Add(sel.CostumeBottomOption);
            }
            // ALSO union every active all-cosmetics cosmetic, not just the equipped top/bottom. with
            // multiple cosmetics on, each one's mask must hide the base body under ALL of them — not
            // only under itself + the real costume — otherwise the body pokes through wherever another
            // cosmetic sits. dupes (incl. the cosmetic currently being applied) are harmless: the
            // composite takes a channel-wise max so adding the same mask twice changes nothing.
            foreach (var slot in activeGameCosmetics)
                if (slot?.option != null) result.Add(slot.option);
            return result;
        }

        private IEnumerator LoadCostumeMaskCoroutine(CostumeOption option, string label, Action<Texture> done)
        {
            if (option == null) { done?.Invoke(null); yield break; }

            Texture mask = null;
            try { mask = option.CostumeMask; } catch { }
            string maskKey = GetCostumeMaskKey(option);
            if (mask == null && !string.IsNullOrEmpty(maskKey) && gameCosmeticMaskCache.TryGetValue(maskKey, out var cached) && cached != null)
                mask = cached;

            if (mask == null)
            {
                UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<Texture> op;
                bool started = false;
                bool waitForOtherLoad = false;
                try
                {
                    if (option.costumeMaskReference != null)
                    {
                        var already = option.costumeMaskReference.Asset;
                        if (already != null) mask = already.TryCast<Texture>();

                        if (mask == null && !string.IsNullOrEmpty(maskKey) && gameCosmeticMaskLoading.Contains(maskKey))
                            waitForOtherLoad = true;

                        if (mask == null && !waitForOtherLoad)
                        {
                            if (!string.IsNullOrEmpty(maskKey)) gameCosmeticMaskLoading.Add(maskKey);
                            op = option.costumeMaskReference.LoadAssetAsync<Texture>();
                            started = true;
                        }
                        else op = default;
                    }
                    else op = default;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[SA:GameCosm] {label} mask load start failed: {ex.Message}");
                    op = default;
                }

                if (waitForOtherLoad)
                {
                    float waited = 0f;
                    while (waited < 5f && gameCosmeticMaskLoading.Contains(maskKey))
                    {
                        yield return null;
                        waited += Time.deltaTime;
                    }
                    if (gameCosmeticMaskCache.TryGetValue(maskKey, out cached) && cached != null)
                        mask = cached;
                }

                if (started)
                {
                    yield return op;
                    try
                    {
                        if (op.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded)
                            mask = op.Result;
                    }
                    catch (Exception ex) { Debug.LogWarning($"[SA:GameCosm] {label} mask result failed: {ex.Message}"); }
                    if (!string.IsNullOrEmpty(maskKey)) gameCosmeticMaskLoading.Remove(maskKey);
                }
            }

            if (mask != null && !string.IsNullOrEmpty(maskKey)) gameCosmeticMaskCache[maskKey] = mask;
            done?.Invoke(mask);
        }

        // composite multiple masks by taking channel-wise max (union of hidden regions).
        // CPU path via RenderTexture readback so we don't depend on a custom shader.
        private Dictionary<string, Texture2D> compositedMaskCache = new Dictionary<string, Texture2D>();
        private Texture CompositeMasks(List<Texture> masks)
        {
            if (masks == null || masks.Count == 0) return null;

            // dedupe by name FIRST. every cosmetic passes its own mask plus the union of all
            // active cosmetics, so its own mask is in the list twice — the duplicate made each
            // cosmetic's cache key unique and all of them re-composited from scratch (each one
            // a stack of ReadPixels GPU stalls) instead of sharing one cached composite.
            var unique = new List<Texture>(masks.Count);
            var nameList = new List<string>(masks.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < masks.Count; i++)
            {
                if (masks[i] == null) continue;
                string n = string.IsNullOrEmpty(masks[i].name) ? masks[i].GetInstanceID().ToString() : masks[i].name;
                if (seen.Add(n)) { unique.Add(masks[i]); nameList.Add(n); }
            }
            masks = unique;
            if (masks.Count == 0) return null;
            if (masks.Count == 1) return masks[0];

            // key by texture name (stable across reward-screen / menu rebuilds where the masks
            // are reinstantiated). instance IDs change every transition so the old key never hit
            // the cache after the first apply and we re-composited from scratch every time.
            nameList.Sort();
            string key = string.Join(",", nameList);
            if (compositedMaskCache.TryGetValue(key, out var cached) && cached != null) return cached;
            int w = 0, h = 0;
            foreach (var m in masks) { if (m == null) continue; if (m.width > w) w = m.width; if (m.height > h) h = m.height; }
            if (w == 0 || h == 0) return masks[0];

            Color[] accum = null;
            try
            {
                foreach (var m in masks)
                {
                    if (m == null) continue;
                    var pixels = ReadTexturePixels(m, w, h);
                    if (pixels == null) continue;
                    if (accum == null) { accum = pixels; continue; }
                    for (int i = 0; i < accum.Length; i++)
                    {
                        var a = accum[i]; var b = pixels[i];
                        if (b.r > a.r) a.r = b.r;
                        if (b.g > a.g) a.g = b.g;
                        if (b.b > a.b) a.b = b.b;
                        if (b.a > a.a) a.a = b.a;
                        accum[i] = a;
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[SA:GameCosm] composite failed: {ex.Message}"); return masks[0]; }

            if (accum == null) return masks[0];

            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            outTex.name = "BFG_CompositeMask_" + key;
            // pin so Resources.UnloadUnusedAssets on scene change doesn't nuke our cache. without
            // this the cache silently empties on every menu/reward transition and we re-composite
            // from scratch every time, which is the bulk of the 1-2s post-bean-spawn freeze.
            outTex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            outTex.SetPixels(accum);
            outTex.Apply(false, false);
            compositedMaskCache[key] = outTex;
            return outTex;
        }

        private static Color[] ReadTexturePixels(Texture src, int w, int h)
        {
            if (src == null) return null;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false, false);
                var px = tex.GetPixels();
                UnityEngine.Object.Destroy(tex);
                return px;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static string GetCostumeMaskKey(CostumeOption option)
        {
            if (option == null) return "";
            try
            {
                var r = option.costumeMaskReference;
                if (r != null)
                    return (r.AssetGUID ?? "") + "|" + (r.SubObjectName ?? "");
            }
            catch { }
            return GetGameCosmeticOptionId(option) + "|mask";
        }

        private IEnumerator ReapplyGameCosmeticMasksCoroutine(GameObject bean, CostumeOption clearOption)
        {
            if (bean == null) yield break;
            var fgch = bean.GetComponent<FallguyCustomisationHandler>();
            if (fgch == null) yield break;

            if (clearOption != null)
            {
                try
                {
                    var blank = fgch._noCostumeMask;
                    if (blank != null)
                    {
                        fgch.SetMask(clearOption, blank);
                    }
                }
                catch (Exception ex) { Debug.LogWarning($"[SA:GameCosm] clear mask failed: {ex.Message}"); }
            }

            for (int i = 0; i < activeGameCosmetics.Count; i++)
            {
                var slot = activeGameCosmetics[i];
                if (slot == null || slot.option == null) continue;

                // if the mesh instance is missing on this bean, the mask alone would carve holes
                // with nothing filling them. reapply the full cosmetic (mesh + mask) instead.
                string key = MakeKey(bean, slot.id);
                bool meshPresent = appliedSkins.TryGetValue(key, out var entry) && entry?.instance != null;
                if (!meshPresent && !pendingKeys.Contains(key))
                {
                    appliedSkins.Remove(key);
                    pendingKeys.Add(key);
                    yield return ApplyGameCosmeticToBeanCoroutine(slot, bean, ApplyReason.FromMenu).WrapToIl2Cpp();
                    continue;
                }

                yield return ApplyGameCosmeticMaskCoroutine(slot.option, bean).WrapToIl2Cpp();
            }
        }

        private void ApplyGameColourPatternToAllBeans()
        {
            var beans = GetBeans();
            for (int i = 0; i < beans.Count; i++)
                if (beans[i] != null && !SkipMenuSkinAutoApplyForThisBean(beans[i]))
                    ApplyGameColourPatternToBean(beans[i]);
        }

        private void ApplyGameColourPatternToBean(GameObject bean)
        {
            if (bean == null) { return; }
            // full-body UGC costume hides the base bean — colour/pattern/faceplate would never show
            if (LoadoutHidesBaseBody()) { return; }
            var fgch = bean.GetComponent<FallguyCustomisationHandler>();
            if (fgch == null) { return; }

            if (activeGameColour != null)
            {
                try { fgch.UpdateColourOption(activeGameColour); }
                catch (Exception ex) { Debug.LogWarning("[SA:GameCosm] colour failed: " + ex.Message); }
            }

            if (activeGamePattern != null)
            {
                try { activeGamePattern.LoadBlocking(); } catch { }
                try { fgch.UpdatePatternTexture(activeGamePattern); }
                catch (Exception ex) { Debug.LogWarning("[SA:GameCosm] pattern failed: " + ex.Message); }
            }

            if (activeGameFaceplate != null)
            {
                try { fgch.UpdateFaceplateColours(activeGameFaceplate); }
                catch (Exception ex) { Debug.LogWarning("[SA:GameCosm] faceplate failed: " + ex.Message); }
            }

            // copy-colour costumes snapshot the body's base colour at bind time. on a view switch
            // the SMRs may still be parented under this bean's GEO but missing from the FCH's
            // top/bottom renderer lists (or pointing at the previous bean), so SetCostumeBaseCopyColor
            // would no-op. re-register every applied gamecosmetic's bound SMRs for THIS bean, then
            // re-run the copy so it picks up our activeGameColour.
            if (activeGameColour != null)
            {
                try
                {
                    if (fgch._topRenderers == null) fgch._topRenderers = new Il2CppSystem.Collections.Generic.List<SkinnedMeshRenderer>();
                    if (fgch._bottomRenderers == null) fgch._bottomRenderers = new Il2CppSystem.Collections.Generic.List<SkinnedMeshRenderer>();

                    foreach (var kvp in appliedSkins)
                    {
                        var entry = kvp.Value;
                        if (entry == null || entry.bean != bean || entry.boundRenderers == null) continue;
                        foreach (var go in entry.boundRenderers)
                        {
                            if (go == null) continue;
                            var smr = go.GetComponent<SkinnedMeshRenderer>();
                            if (smr == null) continue;
                            string n = smr.name != null ? smr.name.ToLowerInvariant() : "";
                            bool isBottom = n.Contains("bottom") || n.Contains("lower") || n.Contains("leg") || n.Contains("feet") || n.Contains("foot");
                            var list = isBottom ? fgch._bottomRenderers : fgch._topRenderers;
                            if (!list.Contains(smr)) list.Add(smr);
                        }
                    }
                }
                catch (Exception ex) { Debug.LogWarning("[SA:GameCosm] re-register SMRs failed: " + ex.Message); }

                try { fgch.SetCostumeBaseCopyColor(); }
                catch (Exception ex) { Debug.LogWarning("[SA:GameCosm] SetCostumeBaseCopyColor failed: " + ex.Message); }
            }
        }

        private void SpawnGameCosmeticPoof()
        {
            var bean = BeanMonitorService.LocalPlayerBean;
            if (bean == null)
            {
                var beans = GetBeans();
                if (beans.Count > 0) bean = beans[0];
            }
            if (bean != null) AssetManager.SpawnPoof(bean.transform.position + Vector3.up);
        }

        private static string GetGameCosmeticName(CostumeOption option)
        {
            try { return option.CMSData.Name._text ?? option.name ?? ""; } catch { }
            try { return option.name ?? ""; } catch { }
            return "cosmetic";
        }

        private static string GetGameCosmeticId(CostumeOption option)
        {
            try
            {
                var cms = option.CMSData;
                if (cms != null && !string.IsNullOrEmpty(cms.FullItemId)) return cms.FullItemId;
                if (cms != null && !string.IsNullOrEmpty(cms.Id)) return cms.Id;
            }
            catch { }
            try { if (!string.IsNullOrEmpty(option.name)) return option.name; } catch { }
            return option.GetInstanceID().ToString();
        }

        private static string GetGameColourName(ColourOption option)
        {
            try { return option.CMSData.Name._text ?? option.name ?? ""; } catch { }
            try { return option.name ?? ""; } catch { }
            return "colour";
        }

        private static string GetGameColourId(ColourOption option)
        {
            try
            {
                var cms = option.CMSData;
                if (cms != null && !string.IsNullOrEmpty(cms.FullItemId)) return cms.FullItemId;
                if (cms != null && !string.IsNullOrEmpty(cms.Id)) return cms.Id;
            }
            catch { }
            try { if (!string.IsNullOrEmpty(option.name)) return option.name; } catch { }
            return option.GetInstanceID().ToString();
        }

        private static string GetGamePatternName(SkinPatternOption option)
        {
            try { return option.CMSData.Name._text ?? option.name ?? ""; } catch { }
            try { return option.name ?? ""; } catch { }
            return "pattern";
        }

        private static string GetGamePatternId(SkinPatternOption option)
        {
            try
            {
                var cms = option.CMSData;
                if (cms != null && !string.IsNullOrEmpty(cms.FullItemId)) return cms.FullItemId;
                if (cms != null && !string.IsNullOrEmpty(cms.Id)) return cms.Id;
            }
            catch { }
            try { if (!string.IsNullOrEmpty(option.name)) return option.name; } catch { }
            return option.GetInstanceID().ToString();
        }

        private static string GetGameFaceplateName(FaceplateOption option)
        {
            try { return ((ItemDefinitionSO)option).CMSData.Name._text ?? option.name ?? ""; } catch { }
            try { return option.name ?? ""; } catch { }
            return "faceplate";
        }

        private static string GetGameFaceplateId(FaceplateOption option)
        {
            try
            {
                var cms = ((ItemDefinitionSO)option).CMSData;
                if (cms != null && !string.IsNullOrEmpty(cms.FullItemId)) return cms.FullItemId;
                if (cms != null && !string.IsNullOrEmpty(cms.Id)) return cms.Id;
            }
            catch { }
            try { if (!string.IsNullOrEmpty(option.name)) return option.name; } catch { }
            return option.GetInstanceID().ToString();
        }

        private static bool SkipMenuSkinAutoApplyForThisBean(GameObject bean)
        {
            if (bean == null) return true;
            return IsRemoteInRoundBean(bean);
        }

        private static bool IsRemoteInRoundBean(GameObject bean)
        {
            if (bean == null) return false;
            if (bean == BeanMonitorService.LocalPlayerBean) return false;
            if (GameObjectHelper.IsLobbyCharacter(bean)) return false;
            if (GameObjectHelper.IsUICharacter(bean)) return false;
            if (bean.name == "LevelEditor_FallGuy(Clone)") return false;
            return !string.IsNullOrEmpty(BeanNetworkUtil.TryGetPlayerKeyForBean(bean));
        }

        private static bool IsLocalInRoundBean(GameObject bean)
        {
            if (bean == null) return false;
            if (GameObjectHelper.IsLobbyCharacter(bean)) return false;
            if (GameObjectHelper.IsUICharacter(bean)) return false;
            if (bean.name == "LevelEditor_FallGuy(Clone)") return false;
            if (bean != BeanMonitorService.LocalPlayerBean) return false;
            return bean.transform.parent == null && bean.name.StartsWith("FallGuy");
        }

        private void TriggerReload(SkinInfo skinInfo)
        {
            if (redownloadingFiles.Contains(skinInfo.file)) return;

            // bundle still alive in registry — patch slot ref back, no download needed
            if (loadedBundles.TryGetValue(skinInfo.file, out var existing) && existing != null)
            {
                foreach (var s in activeSlots)
                    if (s.skinInfo.file == skinInfo.file) s.bundle = existing;
                return;
            }

            redownloadingFiles.Add(skinInfo.file);

            GameObject uiObj = GameObject.Find("BetterFG_UI");
            if (uiObj == null) { redownloadingFiles.Remove(skinInfo.file); return; }
            var loader = uiObj.GetComponent<SkinLoaderService>();
            if (loader == null) { redownloadingFiles.Remove(skinInfo.file); return; }

            if (skinInfo.isLocalImport && !string.IsNullOrEmpty(skinInfo.localPath))
                loader.ImportSkinFromFolder(System.IO.Path.GetDirectoryName(skinInfo.localPath));
            else
            {
                string category = GetCategoryFolder(skinInfo.type);
                string folder = !string.IsNullOrEmpty(skinInfo.repoFolder) ? skinInfo.repoFolder : $"{category}/{skinInfo.file}";
                loader.DownloadSkinWithInfo(skinInfo.file, $"{GetRepoRaw(skinInfo)}/{folder}/{skinInfo.file}", $"{GetRepoRaw(skinInfo)}/{folder}/info.json");
            }
        }

        private static string GetCategoryFolder(string typeStr)
        {
            switch (SkinTypeParser.FromString(typeStr))
            {
                case SkinType.Costume: return "Costumes";
                case SkinType.Accessory: return "Accessories";
                case SkinType.Item: return "Items";
                default: return "Costumes";
            }
        }

        public AssetBundle GetOrRegisterBundle(string file, AssetBundle incoming)
        {
            if (string.IsNullOrEmpty(file)) return incoming;

            if (loadedBundles.TryGetValue(file, out var existing) && existing != null)
                return existing;

            if (incoming != null)
                loadedBundles[file] = incoming;

            return incoming;
        }

        public AssetBundle GetOrLoadBundle(string file, byte[] bytes)
        {
            if (loadedBundles.TryGetValue(file, out var existing) && existing != null)
                return existing;

            AssetBundle bundle;
                bundle = AssetBundle.LoadFromMemory(bytes);
            loadedBundles[file] = bundle;
            return bundle;
        }

        // ── Costume ───────────────────────────────────────────────────────────

        private IEnumerator ApplyCostumeCoroutine(ActiveSkinSlot slot, GameObject bean, ApplyReason reason = ApplyReason.FromMenu)
        {
            string pendingKey = MakeKey(bean, slot.skinInfo.file);
            if (slot.bundle == null) { pendingKeys.Remove(pendingKey); yield break; }
            if (SlotDead(slot)) { pendingKeys.Remove(pendingKey); yield break; }

            Transform beanGEO = FindBeanGEO(bean);
            if (beanGEO == null) { Debug.LogError($"[SA:Costume] no GEO on {bean.name}"); pendingKeys.Remove(pendingKey); yield break; }

            var protectedClones = new HashSet<GameObject>();
            foreach (var kvp in appliedSkins)
                if (kvp.Key.StartsWith(bean.GetInstanceID() + "|") && kvp.Value?.instance != null)
                    protectedClones.Add(kvp.Value.instance);

            var disabled = new List<GameObject>();
            bool isRemote = IsRemoteInRoundBean(bean);
            bool keepLocalDonor = false;
            if (!slot.skinInfo.keepBase)
            {
                Transform donor = null;
                for (int i = 0; i < beanGEO.childCount; i++)
                {
                    Transform c = beanGEO.GetChild(i);
                    if (c == null || c.gameObject == null) continue;
                    if (!(c.name.Contains("Top") || c.name.Contains("Bottom") || c.name.Contains("CH_") || c.name.Contains("Body_LOD") || c.name.Contains("LOD"))) continue;
                    if (c.name.Contains("Body_LOD0")) { donor = c; break; }
                }
                if (donor == null)
                {
                    for (int i = 0; i < beanGEO.childCount; i++)
                    {
                        Transform c = beanGEO.GetChild(i);
                        if (c == null || c.gameObject == null) continue;
                        if (!(c.name.Contains("Top") || c.name.Contains("Bottom") || c.name.Contains("CH_") || c.name.Contains("Body_LOD") || c.name.Contains("LOD"))) continue;
                        var smr = c.GetComponentInChildren<SkinnedMeshRenderer>(true);
                        if (smr != null && smr.bones != null && smr.bones.Length > 0) { donor = c; break; }
                    }
                }
                if (donor == null)
                {
                    for (int i = 0; i < beanGEO.childCount; i++)
                    {
                        Transform c = beanGEO.GetChild(i);
                        if (c == null || c.gameObject == null) continue;
                        if (!(c.name.Contains("Top") || c.name.Contains("Bottom") || c.name.Contains("CH_") || c.name.Contains("Body_LOD") || c.name.Contains("LOD"))) continue;
                        if (c.name.Contains("Body_LOD")) { donor = c; break; }
                    }
                }

                for (int i = 0; i < beanGEO.childCount; i++)
                {
                    Transform child = beanGEO.GetChild(i)?.gameObject?.transform;
                    if (child == null || !child.gameObject.activeSelf) continue;
                    if (protectedClones.Contains(child.gameObject)) continue;

                    if (isRemote)
                    {
                        if (donor != null && child == donor)
                        {
                            var renderers = child.GetComponentsInChildren<Renderer>(true);
                            if (renderers != null && renderers.Length > 0)
                            {
                                var matMap = new Dictionary<Renderer, Material[]>();
                                Material invis = CostumePollerComponent.GetInvisibleMat();
                                foreach (var r in renderers)
                                {
                                    if (r == null) continue;
                                    matMap[r] = r.materials;
                                    var newMats = new Material[r.materials.Length];
                                    for (int j = 0; j < newMats.Length; j++) newMats[j] = invis;
                                    r.materials = newMats;
                                }
                                hiddenRendererMaterials[pendingKey + "_" + i] = matMap;
                            }
                        }
                        else
                        {
                            child.gameObject.SetActive(false);
                            disabled.Add(child.gameObject);
                        }
                    }
                    else if (keepLocalDonor && donor != null && child == donor)
                    {
                        var renderers = child.GetComponentsInChildren<Renderer>(true);
                        if (renderers != null && renderers.Length > 0)
                        {
                            var matMap = new Dictionary<Renderer, Material[]>();
                            Material invis = CostumePollerComponent.GetInvisibleMat();
                            foreach (var r in renderers)
                            {
                                if (r == null) continue;
                                matMap[r] = r.materials;
                                var newMats = new Material[r.materials.Length];
                                for (int j = 0; j < newMats.Length; j++) newMats[j] = invis;
                                r.materials = newMats;
                            }
                            hiddenRendererMaterials[pendingKey + "_" + i] = matMap;
                        }
                    }
                    else
                    {
                        child.gameObject.SetActive(false);
                        disabled.Add(child.gameObject);
                    }
                }

                var fgch = bean.GetComponent<FallguyCustomisationHandler>();
                if (fgch != null)
                {
                    var topObj = fgch._currentTopCostume != null ? fgch._currentTopCostume.CostumeObject : null;
                    var bottomObj = fgch._currentBottomCostume != null ? fgch._currentBottomCostume.CostumeObject : null;
                    if (topObj != null && topObj.activeSelf && !protectedClones.Contains(topObj) && !disabled.Contains(topObj))
                    { topObj.SetActive(false); disabled.Add(topObj); }
                    if (bottomObj != null && bottomObj.activeSelf && !protectedClones.Contains(bottomObj) && !disabled.Contains(bottomObj))
                    { bottomObj.SetActive(false); disabled.Add(bottomObj); }
                }
            }

            // local scale is resolved inside PlayerScaleService (single source of truth); pass the skin
            // we're applying so its baked scale is used even before the slot lands in activeSlots. remote
            // beans just take the skin's own baked scale.
            if (isRemote)
            {
                float bakedRemote = (slot.type == SkinType.Costume && slot.skinInfo.skinScale > 0f) ? slot.skinInfo.skinScale : 1f;
                PlayerScaleService.ApplyToBean(bean, bakedRemote, PlayerScaleService.BeanScaleMode.Remote);
            }
            else
                PlayerScaleService.ApplyLocalCostumeScale(bean, slot.type == SkinType.Costume ? slot.skinInfo : null);
            if (reason == ApplyReason.FromMenu)
                AssetManager.SpawnPoof(bean.transform.position + Vector3.up);
            AssetBundleRequest req = slot.bundle.LoadAssetAsync(slot.skinInfo.file);
            yield return req;

            try { _ = bean.name; }
            catch
            {
                pendingKeys.Remove(pendingKey);
                foreach (var go in disabled) { try { go.SetActive(true); } catch { } }
                yield break;
            }

            if (SlotDead(slot))
            {
                RestoreBaseBeanVisibility(pendingKey, new AppliedSkinInfo { disabledChildren = disabled, beanGEO = beanGEO, restoreAllBaseGEO = IsLocalInRoundBean(bean) });
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            if (req.asset == null)
            {
                //Debug.LogError($"[SA:Costume] asset null for '{slot.skinInfo.file}'");
                RestoreBaseBeanVisibility(pendingKey, new AppliedSkinInfo { disabledChildren = disabled, beanGEO = beanGEO, restoreAllBaseGEO = IsLocalInRoundBean(bean) });
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            GameObject prefab = null;
            GameObject clone = null;
            Exception applyEx = null;
            try
            {
                    prefab = req.asset.Cast<GameObject>();
                    clone = GameObject.Instantiate(prefab, beanGEO.position, beanGEO.rotation);
            }
            catch (Exception ex) { applyEx = ex; }

            if (applyEx != null || clone == null)
            {
                Debug.LogError($"costume instantiate failed: {applyEx?.Message}");
                RestoreBaseBeanVisibility(pendingKey, new AppliedSkinInfo { disabledChildren = disabled, beanGEO = beanGEO, restoreAllBaseGEO = IsLocalInRoundBean(bean) });
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            bool localBindSkin = IsLocalInRoundBean(bean) &&
                                 (slot.skinInfo.boneOffsets == null || slot.skinInfo.boneOffsets.Count == 0);
            if (localBindSkin)
            {
                var mainFg = clone.transform.Find("Main FG");
                if (mainFg != null)
                {
                    var s = mainFg.localScale;
                    float avg = (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f;
                    localBindSkin = avg < 10f;
                    Debug.Log($"[SA:Costume] Main FG scale={s}, bind={(localBindSkin ? "yes" : "no")}");
                }
            }

            clone.transform.SetParent(beanGEO, true);
            clone.transform.localScale = Vector3.one;

            if (GameObjectHelper.IsLobbyCharacter(bean)) clone.transform.localScale = Vector3.one;
            if (GameObjectHelper.IsUICharacter(bean) || GameObjectHelper.IsLobbyCharacter(bean))
                GameObjectHelper.SetLayerRecursively(clone, LayerMask.NameToLayer("PlayerUI"));
            yield return SetupBoneSyncCoroutine(clone, bean, slot.skinInfo, slot.bundle).WrapToIl2Cpp();

            if (SlotDead(slot))
            {
                RestoreBaseBeanVisibility(pendingKey, new AppliedSkinInfo { disabledChildren = disabled, beanGEO = beanGEO, restoreAllBaseGEO = IsLocalInRoundBean(bean) });
                if (clone != null) GameObject.Destroy(clone);
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            var sync = clone.GetComponent<BoneSyncComponent>();
            if (localBindSkin && (sync == null || sync.boneOffsets == null || sync.boneOffsets.Count == 0))
                if (BindSkinToLocalFallguy(clone, bean, "Costume") && sync != null)
                    sync.enabled = false;
                SetRenderQueue(clone, 3000);

            if (!slot.skinInfo.keepBase)
            {
                var poller = clone.AddComponent<CostumePollerComponent>();
                poller.beanGEO = beanGEO;
                poller.skinClone = clone;
                poller.isRemote = isRemote;
                poller.keepLocalDonor = keepLocalDonor;
                poller.HideNow();
                // in-game cosmetics worn alongside a UGC costume can spawn their meshes a few
                // hundred ms late — the 1s poll interval means they linger visibly before getting
                // hidden. burst-poll for the first second so they get yanked basically instantly.
                StartCoroutine(BurstPollPoller(poller).WrapToIl2Cpp());
            }

            KillExistingAppliedAtKey(pendingKey);
            appliedSkins[pendingKey] = new AppliedSkinInfo { instance = clone, bean = bean, type = SkinType.Costume, disabledChildren = disabled, beanGEO = beanGEO, restoreAllBaseGEO = IsLocalInRoundBean(bean) };
            pendingKeys.Remove(pendingKey);
            Debug.Log($"[SA:Costume] done '{slot.skinInfo.name}' -> {bean.name}");
            OnSkinApplied?.Invoke(new SkinApplyEvent { skinInfo = slot.skinInfo, bean = bean, reason = reason });
            for (int f = 0; f < 5; f++) yield return null;
        }

        // ── Accessory ─────────────────────────────────────────────────────────

        private IEnumerator ApplyAccessoryCoroutine(ActiveSkinSlot slot, GameObject bean, ApplyReason reason = ApplyReason.FromMenu)
        {
            string pendingKey = MakeKey(bean, slot.skinInfo.file);
            if (slot.bundle == null) { pendingKeys.Remove(pendingKey); yield break; }
            if (SlotDead(slot)) { pendingKeys.Remove(pendingKey); yield break; }
            AssetBundleRequest req = slot.bundle.LoadAssetAsync(slot.skinInfo.file);
            yield return req;

            try { _ = bean.name; } catch { pendingKeys.Remove(pendingKey); yield break; }
            if (SlotDead(slot)) { pendingKeys.Remove(pendingKey); yield break; }

            if (req.asset == null) { Debug.LogError($"[SA:Acc] asset null for '{slot.skinInfo.file}'"); pendingKeys.Remove(pendingKey); yield break; }

            Transform beanGEO = FindBeanGEO(bean);
            Transform parent = beanGEO ?? bean.transform;

            GameObject clone = null;
            Exception applyEx = null;
            try
            {
                GameObject prefab = req.asset.Cast<GameObject>();
                clone = GameObject.Instantiate(prefab, parent.position, parent.rotation);
            }
            catch (Exception ex) { applyEx = ex; }

            if (applyEx != null || clone == null) { Debug.LogError($"[SA:Acc] instantiate failed: {applyEx?.Message}"); pendingKeys.Remove(pendingKey); yield break; }

            bool localBindSkin = IsLocalInRoundBean(bean) &&
                                 (slot.skinInfo.boneOffsets == null || slot.skinInfo.boneOffsets.Count == 0);
            if (localBindSkin)
            {
                var mainFg = clone.transform.Find("Main FG");
                if (mainFg != null)
                {
                    var s = mainFg.localScale;
                    float avg = (Mathf.Abs(s.x) + Mathf.Abs(s.y) + Mathf.Abs(s.z)) / 3f;
                    localBindSkin = avg < 10f;
                    Debug.Log($"[SA:Acc] Main FG scale={s}, bind={(localBindSkin ? "yes" : "no")}");
                }
            }

            clone.transform.SetParent(parent, true);
            clone.transform.localScale = Vector3.one;

            if (GameObjectHelper.IsUICharacter(bean) || GameObjectHelper.IsLobbyCharacter(bean))
                GameObjectHelper.SetLayerRecursively(clone, LayerMask.NameToLayer("PlayerUI"));
            if (GameObjectHelper.IsLobbyCharacter(bean))
                clone.transform.localScale = Vector3.one;

            yield return SetupBoneSyncCoroutine(clone, bean, slot.skinInfo, slot.bundle).WrapToIl2Cpp();
            if (SlotDead(slot))
            {
                if (clone != null) GameObject.Destroy(clone);
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            var sync = clone.GetComponent<BoneSyncComponent>();
            if (localBindSkin && (sync == null || sync.boneOffsets == null || sync.boneOffsets.Count == 0))
                if (BindSkinToLocalFallguy(clone, bean, "Acc") && sync != null)
                    sync.enabled = false;

            SetRenderQueue(clone, 3000);

            KillExistingAppliedAtKey(pendingKey);
            appliedSkins[pendingKey] = new AppliedSkinInfo { instance = clone, bean = bean, type = SkinType.Accessory };
            pendingKeys.Remove(pendingKey);
            Debug.Log($"[SA:Acc] done '{slot.skinInfo.name}' -> {bean.name}");
            OnSkinApplied?.Invoke(new SkinApplyEvent { skinInfo = slot.skinInfo, bean = bean, reason = reason });
        }

        // ── Item ──────────────────────────────────────────────────────────────

        private bool BindSkinToLocalFallguy(GameObject clone, GameObject bean, string logname)
        {
            var fgch = bean.GetComponent<FallguyCustomisationHandler>();
            if (fgch == null) return false;

            var fallguyBindposes = new Dictionary<string, Matrix4x4>(StringComparer.Ordinal);
            var baseSmrs = bean.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int b = 0; b < baseSmrs.Length; b++)
            {
                var baseSmr = baseSmrs[b];
                if (baseSmr == null || baseSmr.sharedMesh == null || baseSmr.bones == null) continue;
                var bindposes = baseSmr.sharedMesh.bindposes;
                if (bindposes == null || bindposes.Length != baseSmr.bones.Length) continue;
                for (int bi = 0; bi < baseSmr.bones.Length; bi++)
                {
                    var bone = baseSmr.bones[bi];
                    if (bone == null) continue;
                    if (!fallguyBindposes.ContainsKey(bone.name)) fallguyBindposes[bone.name] = bindposes[bi];
                    string lower = bone.name.ToLowerInvariant();
                    if (!fallguyBindposes.ContainsKey(lower)) fallguyBindposes[lower] = bindposes[bi];
                }
            }

            var smrs = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int bound = 0;
            int customBoneSmrs = 0;
            for (int i = 0; i < smrs.Length; i++)
            {
                var smr = smrs[i];
                if (smr == null) continue;
                try
                {
                    var oldMesh = smr.sharedMesh;
                    var oldBindposes = oldMesh != null ? oldMesh.bindposes : null;
                    var oldWeights = oldMesh != null ? oldMesh.boneWeights : null;
                    bool hasCustomBones = false;
                    if (smr.bones != null)
                    {
                        for (int bi = 0; bi < smr.bones.Length; bi++)
                        {
                            var bone = smr.bones[bi];
                            if (bone == null) continue;
                            if (!fallguyBindposes.ContainsKey(bone.name) &&
                                !fallguyBindposes.ContainsKey(bone.name.ToLowerInvariant()))
                            {
                                hasCustomBones = true;
                                break;
                            }
                        }
                    }

                    if (hasCustomBones)
                    {
                        customBoneSmrs++;
                        Debug.Log($"[SA:{logname}] leaving {smr.name} on custom bones");
                        continue;
                    }

                    fgch.BindMeshToFallguy(smr);
                    if (smr.sharedMesh != null && smr.bones != null && fallguyBindposes.Count > 0)
                    {
                        var newBindposes = new Matrix4x4[smr.bones.Length];
                        bool allMatched = true;
                        for (int bi = 0; bi < smr.bones.Length; bi++)
                        {
                            var bone = smr.bones[bi];
                            if (bone == null ||
                                (!fallguyBindposes.TryGetValue(bone.name, out newBindposes[bi]) &&
                                 !fallguyBindposes.TryGetValue(bone.name.ToLowerInvariant(), out newBindposes[bi])))
                            {
                                allMatched = false;
                                break;
                            }
                        }

                        if (allMatched)
                        {
                            var mesh = GameObject.Instantiate(smr.sharedMesh);
                            mesh.name = smr.sharedMesh.name + "_fallguybindposes";
                            if (oldMesh != null && oldBindposes != null && oldWeights != null &&
                                oldBindposes.Length == newBindposes.Length &&
                                oldWeights.Length == oldMesh.vertexCount)
                            {
                                var verts = oldMesh.vertices;
                                var normals = oldMesh.normals;
                                for (int v = 0; v < verts.Length; v++)
                                {
                                    var bw = oldWeights[v];
                                    Vector3 p = Vector3.zero;
                                    Vector3 n = Vector3.zero;

                                    if (bw.weight0 > 0f && bw.boneIndex0 < oldBindposes.Length)
                                    {
                                        var m = newBindposes[bw.boneIndex0].inverse * oldBindposes[bw.boneIndex0];
                                        p += m.MultiplyPoint3x4(verts[v]) * bw.weight0;
                                        if (normals != null && normals.Length == verts.Length) n += m.MultiplyVector(normals[v]) * bw.weight0;
                                    }
                                    if (bw.weight1 > 0f && bw.boneIndex1 < oldBindposes.Length)
                                    {
                                        var m = newBindposes[bw.boneIndex1].inverse * oldBindposes[bw.boneIndex1];
                                        p += m.MultiplyPoint3x4(verts[v]) * bw.weight1;
                                        if (normals != null && normals.Length == verts.Length) n += m.MultiplyVector(normals[v]) * bw.weight1;
                                    }
                                    if (bw.weight2 > 0f && bw.boneIndex2 < oldBindposes.Length)
                                    {
                                        var m = newBindposes[bw.boneIndex2].inverse * oldBindposes[bw.boneIndex2];
                                        p += m.MultiplyPoint3x4(verts[v]) * bw.weight2;
                                        if (normals != null && normals.Length == verts.Length) n += m.MultiplyVector(normals[v]) * bw.weight2;
                                    }
                                    if (bw.weight3 > 0f && bw.boneIndex3 < oldBindposes.Length)
                                    {
                                        var m = newBindposes[bw.boneIndex3].inverse * oldBindposes[bw.boneIndex3];
                                        p += m.MultiplyPoint3x4(verts[v]) * bw.weight3;
                                        if (normals != null && normals.Length == verts.Length) n += m.MultiplyVector(normals[v]) * bw.weight3;
                                    }

                                    verts[v] = p;
                                    if (normals != null && normals.Length == verts.Length && n.sqrMagnitude > 0.000001f)
                                        normals[v] = n.normalized;
                                }
                                mesh.vertices = verts;
                                if (normals != null && normals.Length == verts.Length) mesh.normals = normals;
                            }
                            mesh.bindposes = newBindposes;
                            mesh.RecalculateBounds();
                            smr.sharedMesh = mesh;
                        }
                    }
                    bound++;
                }
                catch (Exception ex) { Debug.LogWarning($"[SA:{logname}] BindMeshToFallguy failed on {smr.name}: {ex.Message}"); }
            }

            if (bound > 0)
            {
                Debug.Log($"[SA:{logname}] bound {bound} skin SMRs to local fallguy, customBoneSMRs={customBoneSmrs}");
            }
            return bound > 0 && customBoneSmrs == 0;
        }

        private IEnumerator ApplyItemCoroutine(ActiveSkinSlot slot, GameObject bean, ApplyReason reason = ApplyReason.FromMenu)
        {
            string pendingKey = MakeKey(bean, slot.skinInfo.file);
            if (slot.bundle == null) { pendingKeys.Remove(pendingKey); yield break; }
            if (SlotDead(slot)) { pendingKeys.Remove(pendingKey); yield break; }
            AssetBundleRequest req = slot.bundle.LoadAssetAsync(slot.skinInfo.file);
            yield return req;

            try { _ = bean.name; } catch { pendingKeys.Remove(pendingKey); yield break; }
            if (SlotDead(slot)) { pendingKeys.Remove(pendingKey); yield break; }

            if (req.asset == null)
            {
                Debug.LogError($"[SkinApplication] asset '{slot.skinInfo.file}' not in bundle");
                pendingKeys.Remove(pendingKey);
                yield break;
            }

            GameObject prefab = null;
            Exception applyEx = null;
            try { prefab = req.asset.Cast<GameObject>(); }
            catch (Exception ex) { applyEx = ex; }

            if (applyEx != null || prefab == null) { pendingKeys.Remove(pendingKey); yield break; }

            bool hasLeft = slot.skinInfo.left != null;
            bool hasRight = slot.skinInfo.right != null;
            int ov = slot.skinInfo.handOverride;
            bool spawnLeft = hasLeft && (ov == 0 || ov == 1 || ov == 3);
            bool spawnRight = hasRight && (ov == 0 || ov == 2 || ov == 3);

            if (spawnLeft)
                if (!appliedSkins.ContainsKey(MakeKey(bean, slot.skinInfo.file + "_L")))
                    SpawnItemOnHand(prefab, bean, slot.skinInfo, isLeft: true, slot);
            if (spawnRight)
                if (!appliedSkins.ContainsKey(MakeKey(bean, slot.skinInfo.file + "_R")))
                    SpawnItemOnHand(prefab, bean, slot.skinInfo, isLeft: false, slot);

            if (!spawnLeft && !spawnRight && !appliedSkins.ContainsKey(pendingKey))
            {
                GameObject clone = GameObject.Instantiate(prefab, bean.transform.position, bean.transform.rotation);
                clone.transform.SetParent(bean.transform, true);
                clone.transform.localScale = Vector3.one * slot.skinInfo.scale;
                SetRenderQueue(clone, 3000);
                if (SlotDead(slot))
                {
                    GameObject.Destroy(clone);
                    pendingKeys.Remove(pendingKey);
                    yield break;
                }
                KillExistingAppliedAtKey(pendingKey);
                appliedSkins[pendingKey] = new AppliedSkinInfo { instance = clone, bean = bean, type = SkinType.Item };
                OnSkinApplied?.Invoke(new SkinApplyEvent { skinInfo = slot.skinInfo, bean = bean, reason = reason });
            }

            pendingKeys.Remove(pendingKey);
        }

        private void SpawnItemOnHand(GameObject prefab, GameObject bean, SkinInfo skinInfo, bool isLeft, ActiveSkinSlot slot = null)
        {
            if (SlotDead(slot)) return;
            ItemHandInfo handInfo = isLeft ? skinInfo.left : skinInfo.right;
            if (handInfo == null) return;

            string handBoneName = isLeft ? "Wrist_L_jnt" : "Wrist_R_jnt1";
            Transform searchRoot = bean.transform.Find("BetterFG_ScaleWrapper") ?? bean.transform;
            Transform handBone = FindBoneDeep(searchRoot, handBoneName);
            Transform parent = handBone ?? searchRoot;

            GameObject clone = GameObject.Instantiate(prefab, parent.position, parent.rotation);
            clone.transform.SetParent(parent, false);

            string hk = isLeft ? "l" : "r";
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float ox = float.TryParse(SettingsService.Get($"item{hk}offset.{skinInfo.file}.x", "0"), System.Globalization.NumberStyles.Float, ci, out float _ox) ? _ox : 0f;
            float oy = float.TryParse(SettingsService.Get($"item{hk}offset.{skinInfo.file}.y", "0"), System.Globalization.NumberStyles.Float, ci, out float _oy) ? _oy : 0f;
            float oz = float.TryParse(SettingsService.Get($"item{hk}offset.{skinInfo.file}.z", "0"), System.Globalization.NumberStyles.Float, ci, out float _oz) ? _oz : 0f;
            Vector3 basePos = new Vector3(handInfo.position[0], handInfo.position[1], handInfo.position[2]);
            clone.transform.localPosition = basePos + new Vector3(ox, oy, oz);

            float rrx = float.TryParse(SettingsService.Get($"item{hk}rot.{skinInfo.file}.x", "0"), System.Globalization.NumberStyles.Float, ci, out float _rrx) ? _rrx : 0f;
            float rry = float.TryParse(SettingsService.Get($"item{hk}rot.{skinInfo.file}.y", "0"), System.Globalization.NumberStyles.Float, ci, out float _rry) ? _rry : 0f;
            float rrz = float.TryParse(SettingsService.Get($"item{hk}rot.{skinInfo.file}.z", "0"), System.Globalization.NumberStyles.Float, ci, out float _rrz) ? _rrz : 0f;
            Vector3 baseRot = new Vector3(handInfo.rotation[0], handInfo.rotation[1], handInfo.rotation[2]);
            clone.transform.localEulerAngles = baseRot + new Vector3(rrx, rry, rrz);

            clone.transform.localScale = Vector3.one * skinInfo.scale;

            if (GameObjectHelper.IsUICharacter(bean) || GameObjectHelper.IsLobbyCharacter(bean))
                GameObjectHelper.SetLayerRecursively(clone, LayerMask.NameToLayer("PlayerUI"));

            string key = MakeKey(bean, skinInfo.file + (isLeft ? "_L" : "_R"));
            SetRenderQueue(clone, 3000);
            if (SlotDead(slot))
            {
                GameObject.Destroy(clone);
                return;
            }
            KillExistingAppliedAtKey(key);
            appliedSkins[key] = new AppliedSkinInfo { instance = clone, bean = bean, type = SkinType.Item };
            OnSkinApplied?.Invoke(new SkinApplyEvent { skinInfo = skinInfo, bean = bean, reason = ApplyReason.FromMenu });
        }

        private static Transform FindBoneDeep(Transform root, string boneName)
        {
            if (root == null) return null;
            if (root.name == boneName) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var result = FindBoneDeep(root.GetChild(i), boneName);
                if (result != null) return result;
            }
            return null;
        }

        // ── BoneSync ──────────────────────────────────────────────────────────

        private IEnumerator SetupBoneSyncCoroutine(GameObject clone, GameObject bean, SkinInfo skinInfo, AssetBundle bundle)
        {
            Debug.Log("[SA:BoneSync] AddComponent BoneSyncComponent");
            var sync = clone.AddComponent<BoneSyncComponent>();
            sync.playerObject = bean;
            sync.isRemote = IsRemoteInRoundBean(bean);

            if (skinInfo.boneOffsets != null && skinInfo.boneOffsets.Count > 0)
            {
                sync.SetBoneOffsets(skinInfo.boneOffsets);
                Debug.Log($"[SA:BoneSync] {skinInfo.boneOffsets.Count} pre-fetched offsets applied instantly");
                yield break;
            }

            // info.json was already read upstream (catalog/loader) and it had no offsets — don't waste
            // a second github round-trip re-confirming that. this skin just uses its live costume bones.
            if (skinInfo.infoFetched)
            {
                Debug.Log("[SA:BoneSync] info already fetched, no offsets - using live costume bones");
                yield break;
            }

            if (bundle == null)
            {
                Debug.Log("[SA:BoneSync] no bundle, using live costume bones only");
                yield break;
            }

            Debug.Log("[SA:BoneSync] LoadAssetAsync info.json...");
            AssetBundleRequest infoReq = bundle.LoadAssetAsync("info.json");
            yield return infoReq;

            try { _ = clone.name; } catch { Debug.Log("[SA:BoneSync] clone destroyed, abort"); yield break; }

            bool bundleHadOffsets = false;
            if (infoReq.asset != null)
            {
                try
                {
                    var textAsset = infoReq.asset.Cast<TextAsset>();
                    if (textAsset != null)
                    {
                        var infoJson = JsonUtility.FromJson<InfoJson>(textAsset.text);
                        if (infoJson?.boneOffsets != null && infoJson.boneOffsets.Length > 0)
                        {
                            sync.SetBoneOffsets(new List<BoneOffsetEntry>(infoJson.boneOffsets));
                            bundleHadOffsets = true;
                            Debug.Log($"[SA:BoneSync] {infoJson.boneOffsets.Length} offsets from bundle");
                        }
                    }
                }
                catch (Exception ex) { Debug.LogWarning($"[SA:BoneSync] parse failed: {ex.Message}"); }
            }
            else Debug.Log("[SA:BoneSync] no info.json in bundle");

            if (!bundleHadOffsets && !skinInfo.isLocalImport)
                yield return FetchAndAssignOffsets(clone, skinInfo).WrapToIl2Cpp();
        }

        private IEnumerator FetchAndAssignOffsets(GameObject clone, SkinInfo skinInfo)
        {
            // the skinInfo that lands here is often a stub (restore-from-settings, active-slot re-apply
            // onto a freshly-spawned bean like PB_UI_Character) with no boneOffsets and no repoFolder.
            // the catalog already fetched + parsed every skin's info.json, offsets included, so pull the
            // real folder (and offsets outright) from there by file before falling back to a guessed URL.
            var catalog = CustomizationServices.CatalogService;
            if (catalog != null)
            {
                foreach (var cat in catalog.AvailableSkins)
                {
                    if (cat.file != skinInfo.file) continue;
                    if (!string.IsNullOrEmpty(cat.repoFolder)) skinInfo.repoFolder = cat.repoFolder;
                    if (cat.boneOffsets != null && cat.boneOffsets.Count > 0)
                    {
                        var syncCat = clone.GetComponent<BoneSyncComponent>();
                        if (syncCat != null) syncCat.SetBoneOffsets(cat.boneOffsets);
                        Debug.Log($"[SA:BoneSync] {cat.boneOffsets.Count} offsets recovered from catalog for '{skinInfo.file}'");
                        yield break;
                    }
                    break;
                }
            }

            // real layout is <repo>/Costumes/<folder>/info.json — the folder is NOT the file name, so a
            // catalog miss with no repoFolder can't build a valid URL. bail loudly instead of 404ing silent.
            if (string.IsNullOrEmpty(skinInfo.repoFolder))
            {
                Debug.LogWarning($"[SA:BoneSync] no repoFolder for '{skinInfo.file}' and not in catalog - can't fetch offsets, skin will use live costume bones only");
                yield break;
            }

            string url = $"{GetRepoRaw(skinInfo)}/{skinInfo.repoFolder}/info.json";
            Debug.Log($"[SA:BoneSync] fetching offsets: {url}");

            UnityWebRequest www = UnityWebRequest.Get(url);
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[SA:BoneSync] offset fetch failed ({www.result}) for {url}");
                www.Dispose();
                yield break;
            }
            string json = www.downloadHandler.text;
            www.Dispose();

            try { _ = clone.name; } catch { yield break; }
            if (string.IsNullOrEmpty(json)) yield break;

            var offsets = ParseBoneOffsetsFromJson(json);
            if (offsets == null || offsets.Count == 0)
            {
                Debug.Log($"[SA:BoneSync] info.json at {url} had no offsets");
                yield break;
            }

            var sync = clone.GetComponent<BoneSyncComponent>();
            if (sync != null) sync.SetBoneOffsets(offsets);
            Debug.Log($"[SA:BoneSync] {offsets.Count} offsets fetched for '{skinInfo.file}'");
        }

        private static List<BoneOffsetEntry> ParseBoneOffsetsFromJson(string json)
        {
            var offsets = new List<BoneOffsetEntry>();
            int boIdx = json.IndexOf("\"boneOffsets\"", StringComparison.OrdinalIgnoreCase);
            if (boIdx == -1) return offsets;
            int arrStart = json.IndexOf('[', boIdx);
            if (arrStart == -1) return offsets;
            int arrEnd = FindMatchingBracket(json, arrStart, '[', ']');
            if (arrEnd == -1) return offsets;
            string arrayJson = json.Substring(arrStart + 1, arrEnd - arrStart - 1);
            int idx = 0;
            while (idx < arrayJson.Length)
            {
                int objStart = arrayJson.IndexOf('{', idx);
                if (objStart == -1) break;
                int objEnd = FindMatchingBracket(arrayJson, objStart, '{', '}');
                if (objEnd == -1) break;
                string obj = arrayJson.Substring(objStart, objEnd - objStart + 1);
                string bone = ExtractStringValue(obj, "bone");
                float lx = ExtractFloatValue(obj, "x");
                float ly = ExtractFloatValue(obj, "y");
                float lz = ExtractFloatValue(obj, "z");
                if (!string.IsNullOrEmpty(bone))
                    offsets.Add(new BoneOffsetEntry { bone = bone, localPosition = new Vector3(lx, ly, lz) });
                idx = objEnd + 1;
            }
            return offsets;
        }

        private static int FindMatchingBracket(string s, int start, char open, char close)
        {
            int depth = 0;
            for (int i = start; i < s.Length; i++)
            {
                if (s[i] == open) depth++;
                if (s[i] == close) { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        private static string ExtractStringValue(string json, string key)
        {
            int k = json.IndexOf($"\"{key}\"", StringComparison.OrdinalIgnoreCase);
            if (k == -1) return null;
            int colon = json.IndexOf(':', k);
            if (colon == -1) return null;
            int q1 = json.IndexOf('"', colon + 1);
            if (q1 == -1) return null;
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 == -1) return null;
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }

        private static float ExtractFloatValue(string json, string key)
        {
            int k = json.IndexOf($"\"{key}\"", StringComparison.OrdinalIgnoreCase);
            if (k == -1) k = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (k == -1) return 0f;
            int colon = json.IndexOf(':', k);
            if (colon == -1) return 0f;
            int vs = colon + 1;
            while (vs < json.Length && (char.IsWhiteSpace(json[vs]) || json[vs] == '"')) vs++;
            int ve = vs;
            while (ve < json.Length && "0123456789+-.eE".IndexOf(json[ve]) != -1) ve++;
            return float.TryParse(json.Substring(vs, ve - vs),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
        }

        // ── Bean finding ──────────────────────────────────────────────────────

        private static List<GameObject> GetBeans()
        {
            var beans = new List<GameObject>();

            foreach (var ctrl in GameObject.FindObjectsOfType<FallGuysCharacterControllerInput>())
            {
                var obj = ctrl.gameObject;
                if (obj.transform.parent == null
                    && obj.name.StartsWith("FallGuy")
                    && obj.layer == LayerMask.NameToLayer("Player")
                    && HasCharacterGEO(obj))
                    beans.Add(obj);
            }

            string[] paths = {
                "----------------ENVIRONMENT/MainFallGuySpawn/FallGuy(Clone)",
                "3D Environment/MainMenu_Environment/PlinthRig/CharacterAndPlinthHolder_Main/ENV_Plinth_MO/CharacterHolder/PB_UI_Character",
                "3D Assets/Environment/CharacterAndPlinthHolder_RightSide/PB_UI_Character",
                "Menu_Screen_Lobby(Clone)/PartyPlacementTransforms/PlayerCharacter/StartPos/LobbyCharacter",
                "LevelEditor_FallGuy(Clone)"
            };

            foreach (string path in paths)
            {
                var obj = GameObject.Find(path);
                if (obj != null && HasCharacterGEO(obj)) beans.Add(obj);
            }

            return beans;
        }

        public static void SetRenderQueue(GameObject root, int queue)
        {
            if (root == null) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mats = r.materials;
                foreach (var m in mats)
                    if (m != null) m.renderQueue = queue;
                r.materials = mats;
            }
        }

        public static IEnumerator ForceRenderQueue(GameObject root, int queue, int frames = 5)
        {
            for (int f = 0; f < frames; f++)
            {
                if (root == null) yield break;

                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null) continue;

                    var mats = r.materials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null) continue;
                        mats[i].renderQueue = queue;
                    }
                    r.materials = mats;
                }

                yield return null; // wait next frame
            }
        }

        private static bool HasCharacterGEO(GameObject obj)
        {
            if (obj == null) return false;
            if (obj.transform.Find("Character/GEO") != null) return true;
            if (obj.transform.Find("GEO") != null) return true;
            if (obj.transform.Find("BetterFG_ScaleWrapper/Character/GEO") != null) return true;
            if (obj.transform.Find("BetterFG_ScaleWrapper/GEO") != null) return true;
            return false;
        }

        private static Transform FindBeanGEO(GameObject bean)
        {
            var f = bean.transform.Find("Character/GEO");
            if (f != null) return f.Cast<Transform>();
            f = bean.transform.Find("GEO");
            if (f != null) return f.Cast<Transform>();
            f = bean.transform.Find("BetterFG_ScaleWrapper/Character/GEO");
            if (f != null) return f.Cast<Transform>();
            f = bean.transform.Find("BetterFG_ScaleWrapper/GEO");
            if (f != null) return f.Cast<Transform>();
            return null;
        }

        private static float GetRemotePreScale(GameObject bean, SkinInfo skinInfo)
        {
            if (skinInfo != null && skinInfo.skinScale > 0f)
                return skinInfo.skinScale;

            string playerKey = BeanNetworkUtil.TryGetPlayerKeyForBean(bean);
            var profile = RemoteProfileStore.TryGet(playerKey);
            if (profile != null && profile.playerScale > 0f)
                return profile.playerScale;

            return 1f;
        }
    }

    public class ActiveSkinSlot
    {
        public SkinInfo skinInfo;
        public AssetBundle bundle;
        public SkinType type;
        public int applyStamp;
        public bool remotePipeline; // came in via debug_profiles/.bfgprofile, exempt from SlotDead
    }

    internal class gamecosmeticSlot
    {
        public CostumeOption option;
        public string id;
        public string name;
        public int applyStamp;
        public bool remotePipeline; // one-shot apply to a remote bean (profile), exempt from SlotDead
    }

    public class gamecosmeticEntry
    {
        public string id;
        public string name;
        public UnityEngine.Object option;
        public string kind;
    }
}
