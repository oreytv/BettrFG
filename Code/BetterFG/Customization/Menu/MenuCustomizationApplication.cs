using System;
using System.Collections;
using System.Collections.Generic;
using Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Services;
using BetterFG.Customization.Player;
using System.Numerics;
using Vector3 = UnityEngine.Vector3;
using Quaternion = UnityEngine.Quaternion;
using FGClient;

namespace BetterFG.Customization.Menu
{
    public class MenuCustomizationApplication : MonoBehaviour
    {
        public static MenuCustomizationApplication Instance { get; private set; }

        public event Action<string> OnStatus;

        private const string PLINTH_PATH = "3D Environment/MainMenu_Environment/PlinthRig/CharacterAndPlinthHolder_Main/ENV_Plinth_MO";
        private const string PLINTH_MESH_PATH = PLINTH_PATH + "/ENV_Plinth_MO";
        private const string FGUI_CUSTOM_BACKDROP_PARENT_PATH = "3D Environment/Generic_UI_CurrentSeasonBackground_Container_MainMenu_Variant/Generic_UI_SeasonS11Background_Canvas_Variant/Mask";
        private const string FGUI_ORIGINAL_BACKDROP_PATH = "3D Environment/Generic_UI_CurrentSeasonBackground_Container_MainMenu_Variant/Generic_UI_SeasonS11Background_Canvas_Variant/Mask/Backdrop";
        private const string FGUI_ORIGINAL_CIRCLES_PATH = "3D Environment/Generic_UI_CurrentSeasonBackground_Container_MainMenu_Variant/Generic_UI_SeasonS11Background_Canvas_Variant/Mask/Circles";

        private Vector3 FG_UI_ORIGINAL_BACKDROP_PREFERREDLOCALPOS = new Vector3(-0f, 6f, -15);
        private Vector3 FG_UI_ORIGINAL_CIRCLES_PREFERREDLOCALPOS = new Vector3(0f, 0f, -95.4f);
        private Vector3 FG_UI_CUSTOM_BACKDROP_PREFERREDLOCALPOS = new Vector3(-0, -121f, -50.5f);

        public Vector3 internaloffset = new Vector3(0f, 2.4387f, 0f);
        public Vector3 internaloffsetVictory = new Vector3(-0.565f, 0.7669f, 0.381f);

        // main-menu slot
        private GameObject _appliedPlinth;
        private string _appliedFile;
        private bool _origActive = true;

        // extra slots (victory, reward, etc.) keyed by holderGO instance id
        private readonly Dictionary<int, GameObject> _extraApplied = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, bool> _extraOrigActive = new Dictionary<int, bool>();

        // one bundle per file — never double-load
        private readonly Dictionary<string, AssetBundle> _bundles = new Dictionary<string, AssetBundle>();

        // last applied — so BeanMonitorService.PushPlinth can immediately apply to late-arriving slots
        private SkinInfo _lastInfo;
        private AssetBundle _lastBundle;

        // menu background
        private GameObject _menuBgGo;
        private Material _menuBgMat;
        // BG GO active toggle — separate from screen.fallforce.enabled (which now only governs
        // whether the custom gradient/pattern apply). users wanted to choose "use the BettrFG bg"
        // independent of "use my custom colours".
        public const string KEY_BG_ENABLED = "screen.fallforce.bg.enabled";

        // title screen background — same SeasonS11Background Mask as the menu one but under the
        // TitleScreen prefab. handled by ScreenBackgroundService (FallForce screen).
        private const string FGUI_TITLE_BACKDROP_PARENT_PATH = "UICanvas_Client_V2(Clone)/Default/Prefab_UI_TitleScreen(Clone)/Generic_UI_CurrentSeasonBackground_Container_Prefab/Generic_UI_SeasonS11Background_Canvas_Variant/Mask";

        // circles pattern — FallForce screen's pattern key (menu + title share it)
        public const string KEY_PATTERN_PATH = "screen.fallforce.pattern.path";
        // cached on first apply so remove can restore it. ONCE set, never overwritten by ApplyPatternFromSettings
        // (the reapply coroutine would otherwise re-read the live texture, which is our own custom one, and
        // RestorePattern would then restore the custom texture instead of the real original).
        private Texture _originalPatternTex;
        private bool _originalPatternCaptured;
        private Texture2D _appliedPatternTex; // the custom tex we last set, so we can destroy it on swap/remove

        // menu background image (sibling quad, unlit texture)
        private GameObject _menuBgImageGo;
        private Material _menuBgImageMat;
        private Texture2D _menuBgImageTex;
        public const string KEY_BG_IMG_ENABLED = "menu.bg.img.enabled";
        public const string KEY_BG_IMG_PATH = "menu.bg.img.path";
        public const string KEY_BG_IMG_POS_X = "menu.bg.img.pos.x";
        public const string KEY_BG_IMG_POS_Y = "menu.bg.img.pos.y";
        public const string KEY_BG_IMG_POS_Z = "menu.bg.img.pos.z";
        public const string KEY_BG_IMG_SCALE = "menu.bg.img.scale";
        public const string KEY_BG_IMG_SCALE_X = "menu.bg.img.scale.x";
        public const string KEY_BG_IMG_SCALE_Y = "menu.bg.img.scale.y";

        // bg image slider ranges + defaults — single source of truth (UI reads these too)
        public const float BG_IMG_POS_MIN = -10f;
        public const float BG_IMG_POS_MAX = 10f;
        public const float BG_IMG_POS_DEFAULT = 0f;
        public const float BG_IMG_SCALE_MIN = 0f;
        public const float BG_IMG_SCALE_MAX = 15f;
        public const float BG_IMG_SCALE_DEFAULT = 5f;
        public const float BG_IMG_SCALE_AXIS_MIN = 0.1f;
        public const float BG_IMG_SCALE_AXIS_MAX = 3f;
        public const float BG_IMG_SCALE_AXIS_DEFAULT = 1f;

        // ambient light (flat override)
        public const string KEY_AMBIENT_ON = "menu.ambient.on";
        public const string KEY_AMBIENT_R = "menu.ambient.r";
        public const string KEY_AMBIENT_G = "menu.ambient.g";
        public const string KEY_AMBIENT_B = "menu.ambient.b";

        // main sun rotation
        private const string SUN_LIGHT_PATH = "3D Environment/SUN Light";
        public const string KEY_SUN_ON = "menu.sun.on";
        public const string KEY_SUN_ROT_X = "menu.sun.rot.x";
        public const string KEY_SUN_ROT_Y = "menu.sun.rot.y";
        public const string KEY_SUN_ROT_Z = "menu.sun.rot.z";

        private bool _ambientSaved;
        private AmbientMode _ambientOldMode;
        private Color _ambientOldLight;
        private bool _sunSaved;
        private Quaternion _sunOldRot;

        // camera
        // fallback base only — the real base is the vcam's untouched localPosition, cached once
        // in the OnMainMenuEntered postfix (content updates move the cam, so a hardcoded base
        // de-centers the bean). never re-cached anywhere else.
        private static readonly Vector3 CAM_BASE_POS = new Vector3(0f, 3.43f, -5.2f);
        private Vector3 _camBasePos = CAM_BASE_POS;
        private bool _camBaseCached;
        private CinemachineVirtualCamera _vcam;
        private Vector3 _camOffset;
        private float _camFov = 40f;
        private Vector3 _camLookAtOffset;

        public const string KEY_CAM_ENABLED = "menu.cam.enabled";
        public const string KEY_CAM_FOV = "menu.cam.fov";
        public const string KEY_CAM_X = "menu.cam.x";
        public const string KEY_CAM_Y = "menu.cam.y";
        public const string KEY_CAM_Z = "menu.cam.z";
        public const string KEY_CAM_LOOKAT_X = "menu.cam.lookat.x";
        public const string KEY_CAM_LOOKAT_Y = "menu.cam.lookat.y";
        public const string KEY_CAM_LOOKAT_Z = "menu.cam.lookat.z";

        // plinth colour
        public const string KEY_PLINTH_COL_ON = "menu.plinth.col.on";
        public const string KEY_PLINTH_COL_R = "menu.plinth.col.r";
        public const string KEY_PLINTH_COL_G = "menu.plinth.col.g";
        public const string KEY_PLINTH_COL_B = "menu.plinth.col.b";
        private bool _plinthColSaved;
        private Color _plinthColOld;

        // foreground keys — shared with MenuTab
        public const string KEY_FG_CYAN_ON = "menu.fg.cyan.on";
        public const string KEY_FG_CYAN_R = "menu.fg.cyan.r";
        public const string KEY_FG_CYAN_G = "menu.fg.cyan.g";
        public const string KEY_FG_CYAN_B = "menu.fg.cyan.b";
        public const string KEY_FG_BLACK_ON = "menu.fg.black.on";
        public const string KEY_FG_BLACK_R = "menu.fg.black.r";
        public const string KEY_FG_BLACK_G = "menu.fg.black.g";
        public const string KEY_FG_BLACK_B = "menu.fg.black.b";
        public const string KEY_FG_YELLOW_ON = "menu.fg.yellow.on";
        public const string KEY_FG_YELLOW_R = "menu.fg.yellow.r";
        public const string KEY_FG_YELLOW_G = "menu.fg.yellow.g";
        public const string KEY_FG_YELLOW_B = "menu.fg.yellow.b";
        public const string KEY_FG_BLUE_ON = "menu.fg.blue.on";
        public const string KEY_FG_BLUE_R = "menu.fg.blue.r";
        public const string KEY_FG_BLUE_G = "menu.fg.blue.g";
        public const string KEY_FG_BLUE_B = "menu.fg.blue.b";
        public const string KEY_FG_PINK_ON = "menu.fg.pink.on";
        public const string KEY_FG_PINK_R = "menu.fg.pink.r";
        public const string KEY_FG_PINK_G = "menu.fg.pink.g";
        public const string KEY_FG_PINK_B = "menu.fg.pink.b";
        public const string KEY_FG_ORANGE_ON = "menu.fg.orange.on";
        public const string KEY_FG_ORANGE_R = "menu.fg.orange.r";
        public const string KEY_FG_ORANGE_G = "menu.fg.orange.g";
        public const string KEY_FG_ORANGE_B = "menu.fg.orange.b";

        // cache: instanceID → original color, so we can revert
        private readonly Dictionary<int, Color> _fgOriginals = new Dictionary<int, Color>();
        // parallel ref cache so RevertForeground doesn't have to GetComponentsInChildren the
        // entire UICanvas just to find the ~dozens of images we actually touched. id -> Image.
        private readonly Dictionary<int, UnityEngine.UI.Image> _fgTouchedImages = new Dictionary<int, UnityEngine.UI.Image>();
        private readonly Dictionary<int, UnityEngine.Sprite> _showSelectOriginalSprites = new Dictionary<int, UnityEngine.Sprite>();

        // lobby bg texture cache: instanceID → original sprite (cached once, never destroyed)
        private readonly Dictionary<int, UnityEngine.Sprite> _lobbyTexOriginals = new Dictionary<int, UnityEngine.Sprite>();

        // lobby bg color cache: instanceID → original color (cached once, never overwritten)
        private readonly Dictionary<int, Color> _lobbyColorOriginals = new Dictionary<int, Color>();

        // set true on main menu enter, consumed (set false) once fg is applied on ShowMainMenu
        public bool _pendingFgReapply = true;

        // true = next ShowMainMenu reapply hits full UICanvas_Client_V2(Clone), then flips false
        public static bool _fullCanvasReapplyPending = false;


        public static IEnumerator AutoApplyCamFromSettingsCoroutine()
        {
            yield return null;
            yield return new WaitForSeconds(0.1f);
            AutoApplyCamFromSettings();
        }

        public static IEnumerator ReapplyForegroundFromSettingsCoroutine(Transform scopeRoot = null)
        {
            yield return null;
            yield return new WaitForSeconds(0.01f);
            Instance.ReapplyForegroundFromSettings(scopeRoot);
        }

        public void ReapplyForegroundFromSettings(Transform scopeRoot = null, string excludeSubtreeName = null, bool anyImage = false, bool refreshOriginals = false)
        {
            bool fullCanvas = scopeRoot == null;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float def) =>
                float.TryParse(SettingsService.Get(key, ""), System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            bool cyanOn = SettingsService.Get(KEY_FG_CYAN_ON, "false") == "true";
            bool blackOn = SettingsService.Get(KEY_FG_BLACK_ON, "false") == "true";
            bool yellowOn = SettingsService.Get(KEY_FG_YELLOW_ON, "false") == "true";
            bool blueOn = SettingsService.Get(KEY_FG_BLUE_ON, "false") == "true";
            bool pinkOn = SettingsService.Get(KEY_FG_PINK_ON, "false") == "true";
            bool orangeOn = SettingsService.Get(KEY_FG_ORANGE_ON, "false") == "true";

            if (!cyanOn && !blackOn && !yellowOn && !blueOn && !pinkOn && !orangeOn)
            {
                if (fullCanvas)
                    ApplyKnownSpecialForegroundScreens();
                return;
            }

            ApplyForeground(
                cyanOn, new Color(P(KEY_FG_CYAN_R, 0f), P(KEY_FG_CYAN_G, 0.3f), P(KEY_FG_CYAN_B, 1f)),
                blackOn, new Color(P(KEY_FG_BLACK_R, 0.75f), P(KEY_FG_BLACK_G, 0.75f), P(KEY_FG_BLACK_B, 0.75f)),
                yellowOn, new Color(P(KEY_FG_YELLOW_R, 1f), P(KEY_FG_YELLOW_G, 0.5f), P(KEY_FG_YELLOW_B, 0f)),
                blueOn, new Color(P(KEY_FG_BLUE_R, 0.1f), P(KEY_FG_BLUE_G, 0.25f), P(KEY_FG_BLUE_B, 0.85f)),
                pinkOn, new Color(P(KEY_FG_PINK_R, 1f), P(KEY_FG_PINK_G, 0.2f), P(KEY_FG_PINK_B, 0.5f)),
                orangeOn, new Color(P(KEY_FG_ORANGE_R, 1f), P(KEY_FG_ORANGE_G, 0.55f), P(KEY_FG_ORANGE_B, 0.1f)),
                scopeRoot, excludeSubtreeName, anyImage, refreshOriginals
            );

            if (fullCanvas)
                ApplyKnownSpecialForegroundScreens();
        }

        public void ReapplyShowTileFill(Transform tileRoot)
        {
            if (tileRoot == null) return;
            var fillParent = tileRoot.Find("ShowTileHolder/NestedPanel/MainPanel/Panel_Fill");
            if (fillParent == null) return;

            bool cyanOn = SettingsService.Get(KEY_FG_CYAN_ON, "false") == "true";
            bool blackOn = SettingsService.Get(KEY_FG_BLACK_ON, "false") == "true";
            bool yellowOn = SettingsService.Get(KEY_FG_YELLOW_ON, "false") == "true";
            bool blueOn = SettingsService.Get(KEY_FG_BLUE_ON, "false") == "true";
            bool pinkOn = SettingsService.Get(KEY_FG_PINK_ON, "false") == "true";
            if (!cyanOn && !blackOn && !yellowOn && !blueOn && !pinkOn) return;

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float def) => float.TryParse(SettingsService.Get(key, ""), System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            Color cyanTarget = new Color(P(KEY_FG_CYAN_R, 0f), P(KEY_FG_CYAN_G, 0.3f), P(KEY_FG_CYAN_B, 1f));
            Color blackTarget = new Color(P(KEY_FG_BLACK_R, 0.75f), P(KEY_FG_BLACK_G, 0.75f), P(KEY_FG_BLACK_B, 0.75f));
            Color yellowTarget = new Color(P(KEY_FG_YELLOW_R, 1f), P(KEY_FG_YELLOW_G, 0.5f), P(KEY_FG_YELLOW_B, 0f));
            Color blueTarget = new Color(P(KEY_FG_BLUE_R, 0.1f), P(KEY_FG_BLUE_G, 0.25f), P(KEY_FG_BLUE_B, 0.85f));
            Color pinkTarget = new Color(P(KEY_FG_PINK_R, 1f), P(KEY_FG_PINK_G, 0.2f), P(KEY_FG_PINK_B, 0.5f));

            for (int i = 0; i < fillParent.childCount; i++)
            {
                var child = fillParent.GetChild(i);
                if (child == null) continue;
                var img = child.GetComponent<UnityEngine.UI.Image>();
                if (img == null) continue;

                int id = img.GetInstanceID();
                Color c = _fgOriginals.TryGetValue(id, out var orig) ? orig : img.color;
                Color.RGBToHSV(c, out float h, out float s, out float bv);

                bool matchCyan = cyanOn && s > 0.3f && h >= 0.47f && h <= 0.58f;
                bool matchBlue = blueOn && s > 0.3f && h > 0.58f && h <= 0.70f;
                bool matchBlack = blackOn && bv < 0.15f && s < 0.15f;
                bool matchYellow = yellowOn && s > 0.3f && h >= 0.1f && h <= 0.2f;
                bool matchPink = pinkOn && s > 0.3f && (h >= 0.88f || h <= 0.05f) && bv > 0.3f;
                if (!matchCyan && !matchBlue && !matchBlack && !matchYellow && !matchPink) continue;

                if (!_fgOriginals.ContainsKey(id))
                {
                    _fgOriginals[id] = c;
                    _fgTouchedImages[id] = img;
                }

                Color target = matchBlue ? blueTarget : matchCyan ? cyanTarget : matchPink ? pinkTarget : matchYellow ? yellowTarget : blackTarget;
                img.color = new Color(target.r, target.g, target.b, img.color.a);
            }
        }

        private static readonly Vector3 LOOKAT_BASE = new Vector3(0f, 2.44f, 0f);

        private void EnsureVcam()
        {
            // the game rebuilds the lobby vcam per menu entry, so a cached ref goes dead. an il2cpp
            // object whose native side is gone compares != null but throws/no-ops on use, so re-fetch
            // the live one whenever we can find a MainMenuManager rather than trusting the cache.
            var mm = FindObjectOfType<MainMenuManager>();
            if (mm != null && mm._lobbyVirtualCam != null) _vcam = mm._lobbyVirtualCam;
        }

        // called from the OnMainMenuEntered postfix. the game rebuilds the lobby vcam on every menu
        // entry, so always adopt the live one — holding a stale ref means ApplyCam writes to a dead
        // cam while the real one sits at base (the "camera resets on re-entry" bug).
        // base is snapshotted once, straight off the live transform. we never touch the transform
        // before this caches (ApplyCam bails until _camBaseCached), so the live pos IS the pristine
        // base — don't subtract _camOffset, that folds the saved offset into the base and cancels it.
        public void CacheCamBase(CinemachineVirtualCamera vcam)
        {
            if (vcam == null) return;
            _vcam = vcam;
            if (_camBaseCached) return;
            _camBasePos = vcam.gameObject.transform.localPosition;
            _camBaseCached = true;
        }

        public void ApplyCam(Vector3 offset, float fov, Vector3 lookAtOffset = default)
        {
            _camOffset = offset;
            _camFov = fov;
            _camLookAtOffset = lookAtOffset;

            // don't touch the cam until OnMainMenuEntered has cached the real base, otherwise we'd
            // offset from the stale hardcoded fallback and the bean sits off-centre. the postfix
            // re-applies once the base is in, so skipping here is safe.
            if (!_camBaseCached) return;

            EnsureVcam();
            if (_vcam == null) return;

            _vcam.gameObject.transform.localPosition = _camBasePos + _camOffset;
            var lens = _vcam.m_Lens;
            lens.FieldOfView = _camFov;
            _vcam.m_Lens = lens;

            if (_vcam.LookAt != null)
                _vcam.LookAt.localPosition = LOOKAT_BASE + _camLookAtOffset;
        }

        public void ResetCam()
        {
            _camOffset = Vector3.zero;
            _camFov = 40f;
            _camLookAtOffset = Vector3.zero;

            EnsureVcam();
            if (_vcam == null) return;

            _vcam.gameObject.transform.localPosition = _camBasePos;
            var lens = _vcam.m_Lens;
            lens.FieldOfView = 40f;
            _vcam.m_Lens = lens;

            if (_vcam.LookAt != null)
                _vcam.LookAt.localPosition = LOOKAT_BASE;
        }

        public static void AutoApplyCamFromSettings()
        {
            // off by default — the game moves the lobby cam around on content updates, so we don't
            // touch it unless the user explicitly turns on custom camera position.
            if (SettingsService.Get(KEY_CAM_ENABLED, "false") != "true") return;

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float def) =>
                float.TryParse(SettingsService.Get(key, ""), System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            bool hasCam = SettingsService.Get(KEY_CAM_FOV, "") != "" || SettingsService.Get(KEY_CAM_X, "") != "";
            bool hasLookAt = SettingsService.Get(KEY_CAM_LOOKAT_X, "") != ""
                          || SettingsService.Get(KEY_CAM_LOOKAT_Y, "") != ""
                          || SettingsService.Get(KEY_CAM_LOOKAT_Z, "") != "";

            if (!hasCam && !hasLookAt) return;

            Instance?.ApplyCam(
                new Vector3(P(KEY_CAM_X, 0f), P(KEY_CAM_Y, 0f), P(KEY_CAM_Z, 0f)),
                P(KEY_CAM_FOV, 40f),
                new Vector3(P(KEY_CAM_LOOKAT_X, 0f), P(KEY_CAM_LOOKAT_Y, 0f), P(KEY_CAM_LOOKAT_Z, 0f))
            );
        }

        // ── Foreground ────────────────────────────────────────────────────────

        public void ApplyForeground(bool cyanOn, Color cyanTarget, bool blackOn, Color blackTarget, bool yellowOn, Color yellowTarget, bool blueOn = false, Color blueTarget = default, bool pinkOn = false, Color pinkTarget = default, bool orangeOn = false, Color orangeTarget = default, Transform scopeRoot = null, string excludeSubtreeName = null, bool anyImage = false, bool refreshOriginals = false)
        {
            if (scopeRoot == null)
            {
                RevertForeground();
                var uiCanvas = GameObject.Find("UICanvas_Client_V2(Clone)");
                if (uiCanvas == null) return;
                scopeRoot = uiCanvas.transform;
            }

            var images = scopeRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            // excludeSubtreeName may be several names joined by '|' (e.g. the creative bg canvas plus
            // the options screen's Description block); anything under one of them is left alone.
            string[] excludes = excludeSubtreeName == null ? null : excludeSubtreeName.Split('|');

            foreach (var img in images)
            {
                if (img == null) continue;
                var p = img.transform;
                bool inStarPopup = false;
                bool inExcluded = false;
                bool inTeamContainer = false;
                bool inPhraseOverlay = false;
                while (p != null)
                {
                    if (p.name.StartsWith("StarPopup_")) inStarPopup = true;
                    if (p.name == "TeamContainer") inTeamContainer = true;
                    if (p.name == "TabContentSocialPhraseOverlay") inPhraseOverlay = true;
                    if (excludes != null) foreach (var ex in excludes) if (p.name == ex) { inExcluded = true; break; }
                    p = p.parent;
                }
                if (inStarPopup || inExcluded || inTeamContainer || inPhraseOverlay) continue;
                string n = img.gameObject.name;
                // carousel/folder tiles are recoloured by name in ApplyFolderTileColours (Fill->blue,
                // Selected->cyan); the hue sweep mis-buckets Background_Fill as cyan, so leave both to it.
                if (!anyImage && (n == "Background_Fill" || n == "Background_Selected")) continue;
                bool isFill = n.Contains("Fill") || n.Contains("Background") || n.Contains("Outline") || n.Contains("Inline") || n.Contains("BG");
                bool isCrowns = n.Equals("crowns");
                bool isBacking = n.Contains("Backing");
                // level-editor screens (anyImage) recolour every hue-matching image, not just the
                // menu's Fill/Backing/crowns-named ones — their controls are named Slider/Overlay/etc.
                if (!anyImage && !isFill && !isBacking && !isCrowns) continue;

                int id = img.GetInstanceID();
                // refreshOriginals: the game just repainted this image to a new state colour (radial
                // select/deselect/disable), so the live colour is the real source hue, not the stale cache.
                Color c = (!refreshOriginals && _fgOriginals.TryGetValue(id, out var cachedOrig)) ? cachedOrig : img.color;
                Color.RGBToHSV(c, out float h, out float s, out float v);

                bool matchCyan = cyanOn && s > 0.3f && h >= 0.47f && h <= 0.58f;

                // crowns follow cyan → treated as blue
                bool matchBlue = blueOn && (
                    (s > 0.3f && h > 0.58f && h <= 0.70f) ||
                    (isCrowns && matchCyan)
                );

                bool matchBlack = blackOn && v < 0.15f && s < 0.15f;
                bool matchYellow = yellowOn && s > 0.3f && h >= 0.1f && h <= 0.2f;
                bool matchPink = pinkOn && s > 0.3f && (h >= 0.88f || h <= 0.05f) && v > 0.3f;
                // orange sits between red/pink and yellow on the wheel
                bool matchOrange = orangeOn && s > 0.3f && h > 0.05f && h < 0.1f && v > 0.3f;

                if (isBacking && !isFill) matchBlack = matchYellow = false;

                if (!matchCyan && !matchBlue && !matchBlack && !matchYellow && !matchPink && !matchOrange) continue;

                if (refreshOriginals || !_fgOriginals.ContainsKey(id))
                {
                    _fgOriginals[id] = c;
                    _fgTouchedImages[id] = img;
                }

                Color target =
                    matchBlue ? blueTarget :
                    matchCyan ? cyanTarget :
                    matchPink ? pinkTarget :
                    matchOrange ? orangeTarget :
                    matchYellow ? yellowTarget :
                    blackTarget;

                img.color = new Color(target.r, target.g, target.b, img.color.a);
            }

            ApplyMainPlayButtonUnselectedFill(scopeRoot, yellowOn, yellowTarget);
        }

        // level-editor variation folder: recolour by name only. Background_Fill -> blue, Background_Selected
        // -> cyan. covers the disabled tile states too (GetComponentsInChildren(true)); originals tracked so
        // RevertForeground puts them back.
        public void ApplyFolderTileColours(Transform folderRoot)
        {
            bool blueOn = SettingsService.Get(KEY_FG_BLUE_ON, "false") == "true";
            bool cyanOn = SettingsService.Get(KEY_FG_CYAN_ON, "false") == "true";
            if (!blueOn && !cyanOn) return;

            Color blue = BlueReplacement();
            Color cyan = new Color(ParseF(KEY_FG_CYAN_R, 0f), ParseF(KEY_FG_CYAN_G, 0.3f), ParseF(KEY_FG_CYAN_B, 1f));

            foreach (var img in folderRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img == null) continue;
                string n = img.gameObject.name;
                bool fill = blueOn && n == "Background_Fill";
                bool sel = cyanOn && n == "Background_Selected";
                if (!fill && !sel) continue;

                int id = img.GetInstanceID();
                if (!_fgOriginals.ContainsKey(id)) { _fgOriginals[id] = img.color; _fgTouchedImages[id] = img; }
                Color target = fill ? blue : cyan;
                img.color = new Color(target.r, target.g, target.b, img.color.a);
            }
        }

        public void RevertForeground()
        {
            if (_fgOriginals.Count == 0) return;

            // restore via the tracked Image refs — avoids a full UICanvas GetComponentsInChildren
            // walk every state-change reapply, which was visibly freezing transitions.
            foreach (var kv in _fgTouchedImages)
            {
                var img = kv.Value;
                if (img == null) continue;
                if (_fgOriginals.TryGetValue(kv.Key, out var orig))
                    img.color = orig;
            }

            _fgOriginals.Clear();
            _fgTouchedImages.Clear();
        }

        // the season progress banner is driven by UITween so it can't be recoloured once-and-done —
        // SeasonProgressHoverPatch calls this every tween frame with the live Image and whether the
        // banner is hovered. idle = blue, hover = pink. only stomp the tween's colour if that colour's
        // foreground toggle is actually on; otherwise leave the game's colour alone.
        public void RecolourSeasonProgressImage(UnityEngine.UI.Image img, bool hovered)
        {
            if (img == null) return;

            if (hovered)
            {
                if (SettingsService.Get(KEY_FG_PINK_ON, "false") != "true") return;
                var p = PinkReplacement();
                img.color = new Color(p.r, p.g, p.b, img.color.a);
            }
            else
            {
                if (SettingsService.Get(KEY_FG_BLUE_ON, "false") != "true") return;
                var b = BlueReplacement();
                img.color = new Color(b.r, b.g, b.b, img.color.a);
            }
        }

        public enum SpecialScreen
        {
            PrivateLobbyShowSelect,
            PrivateLobbyPlayerList
        }

        public IEnumerator ReapplySpecialForegroundNextFrame(SpecialScreen screen)
        {
            yield return null;
            ReapplySpecialForeground(screen);
        }

        public void ReapplySpecialForeground(SpecialScreen screen)
        {
            switch (screen)
            {
                case SpecialScreen.PrivateLobbyShowSelect:
                {
                    var root = GameObject.Find("UICanvas_Client_V2(Clone)/Default/Prefab_UI_PrivateLobbyShowSelect(Clone)");
                    if (root == null) return;
                    ReapplyForegroundFromSettings(root.transform);
                    ApplyPrivateLobbyShowSelect(root.transform);
                    break;
                }
                case SpecialScreen.PrivateLobbyPlayerList:
                {
                    var root = GameObject.Find("UICanvas_Client_V2(Clone)/Default/Prime_UI_PrivateLobbyPlayerList(Clone)");
                    if (root == null) return;
                    ReapplyForegroundFromSettings(root.transform);
                    SetImageColor(root.transform, "SafeArea/PlayerListArea/PlayerListBackground/Background_Fill/OuterBorder", BlueReplacement(), true);
                    BetterFG.Features.MorePlatformIcon.FeatureMorePlatformIcon.ApplyPrivateLobbyNamesFromScene(root.transform);
                    break;
                }
            }
        }

        public void ApplyKnownSpecialForegroundScreens()
        {
            ReapplySpecialForeground(SpecialScreen.PrivateLobbyShowSelect);
            ReapplySpecialForeground(SpecialScreen.PrivateLobbyPlayerList);

            // navigation overlay sits outside UICanvas_Client_V2, so the main sweep misses it.
            var nav = GameObject.Find("Prefab_UI_NavigationOverlay(Clone)");
            if (nav != null) ReapplyForegroundFromSettings(nav.transform);
        }

        private void ApplyMainPlayButtonUnselectedFill(Transform scopeRoot, bool yellowOn, Color yellowTarget)
        {
            if (scopeRoot == null || !yellowOn) return;

            string[] roots =
            {
                "Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Main/Prime_UI_MainMenu_Canvas(Clone)/SafeArea/BottomRight_Group/Generic_UI_PlayButton2_Prefab/Generic_UI_GenericButton_Prefab/",
                "MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Main/Prime_UI_MainMenu_Canvas(Clone)/SafeArea/BottomRight_Group/Generic_UI_PlayButton2_Prefab/Generic_UI_GenericButton_Prefab/",
                "Prime_UI_MainMenu_Canvas(Clone)/SafeArea/BottomRight_Group/Generic_UI_PlayButton2_Prefab/Generic_UI_GenericButton_Prefab/",
                "SafeArea/BottomRight_Group/Generic_UI_PlayButton2_Prefab/Generic_UI_GenericButton_Prefab/"
            };

            foreach (var root in roots)
            {
                var unselected = scopeRoot.Find(root + "Panel_Unselected/Panel_Fill");
                var img = unselected == null ? null : unselected.GetComponent<UnityEngine.UI.Image>();
                if (img == null) continue;

                int id = img.GetInstanceID();
                if (!_fgOriginals.ContainsKey(id))
                {
                    _fgOriginals[id] = img.color;
                    _fgTouchedImages[id] = img;
                }

                img.color = new Color(yellowTarget.r, yellowTarget.g, yellowTarget.b, img.color.a);
                return;
            }
        }

        public void ReapplyPrivateLobbyShowEntryForeground(Transform entry)
        {
            if (entry == null) return;

            ReapplyForegroundFromSettings(entry);
            var outline = entry.Find("ButtonImage/Outline");
            var img = outline == null ? null : outline.GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.color = Color.white;
        }

        public void ReapplyModalForeground(Component modal)
        {
            if (modal == null) return;
            ReapplyForegroundFromSettings(modal.transform);
        }

        private void ApplyPrivateLobbyShowSelect(Transform root)
        {
            if (root == null) return;

            var container = root.Find("Container");
            var image = container == null ? null : container.GetComponent<UnityEngine.UI.Image>();
            if (image != null && image.sprite != null)
            {
                int id = image.GetInstanceID();
                if (!_showSelectOriginalSprites.TryGetValue(id, out var original) || original == null)
                {
                    original = image.sprite;
                    _showSelectOriginalSprites[id] = original;
                }

                image.sprite = ProcessShowSelectSprite(original);
            }

            SetImageColor(root, "Container/ShowsScrollList/TabScrollbar", BlueReplacement(), true);

            var content = root.Find("Container/ShowsScrollList/ShowsListViewport/ShowsListContent");
            if (content == null) return;

            foreach (var img in content.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img != null && img.name == "Outline")
                    img.color = Color.white;
            }
        }

        private void SetImageColor(Transform root, string path, Color color, bool requireBlueOn = false)
        {
            if (root == null || requireBlueOn && SettingsService.Get(KEY_FG_BLUE_ON, "false") != "true") return;

            var t = root.Find(path);
            var img = t == null ? null : t.GetComponent<UnityEngine.UI.Image>();
            if (img != null) img.color = new Color(color.r, color.g, color.b, img.color.a);
        }

        private static Color BlueReplacement()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float def) =>
                float.TryParse(SettingsService.Get(key, ""), System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            return new Color(
                P(KEY_FG_BLUE_R, 0.1f),
                P(KEY_FG_BLUE_G, 0.25f),
                P(KEY_FG_BLUE_B, 0.85f),
                1f
            );
        }

        private static Color PinkReplacement() =>
            new Color(ParseF(KEY_FG_PINK_R, 1f), ParseF(KEY_FG_PINK_G, 0.2f), ParseF(KEY_FG_PINK_B, 0.5f), 1f);

        private static Color BlackReplacement() =>
            new Color(ParseF(KEY_FG_BLACK_R, 0f), ParseF(KEY_FG_BLACK_G, 0f), ParseF(KEY_FG_BLACK_B, 0f), 1f);

        public static Color YellowReplacement() =>
            new Color(ParseF(KEY_FG_YELLOW_R, 1f), ParseF(KEY_FG_YELLOW_G, 0.5f), ParseF(KEY_FG_YELLOW_B, 0f), 1f);

        public static Color OrangeReplacement() =>
            new Color(ParseF(KEY_FG_ORANGE_R, 1f), ParseF(KEY_FG_ORANGE_G, 0.55f), ParseF(KEY_FG_ORANGE_B, 0.1f), 1f);

        // these gameplay images have hot-pink + dark-grey baked into their TEXTURE, not the image
        // colour, so colour tinting can't touch them. we recolour the actual pixels at runtime:
        // hot pink -> pink replacement, dark grey -> black replacement. cached per-image so we only
        // build the swapped texture once.
        //
        // paths are RELATIVE to the GameStates root so we can resolve them with transform.Find,
        // which (unlike GameObject.Find) also finds INACTIVE children — needed because only one
        // GameState is active at a time (CountdownState vs PlayingState).
        private const string GameStatesRootPath =
            "UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/GameStates";


        // shader-material recolour. the hot-pink/dark-grey HUD bits are baked into the TEXTURE, not the
        // image colour, so a tint can't touch them. instead of reading pixels back and rebuilding textures
        // on the CPU (the old bake — a per-round GPU stall) we assign a custom UI material whose shader
        // remaps those hue/sat bands on the GPU at draw time. zero readback, zero per-round cost.
        private UnityEngine.Material _pinkGreyMat;

        // every Image we've put our material on, so we can clear it when the feature turns off.
        private readonly HashSet<UnityEngine.UI.Image> _shadedImages = new HashSet<UnityEngine.UI.Image>();

        private UnityEngine.Material GetPinkGreyMaterial()
        {
            if (_pinkGreyMat != null) return _pinkGreyMat;
            var sh = BetterFG.Core.AssetManager.GetShader("bettrfg_ui_shader");
            if (sh == null) { Plugin.Log.LogWarning("bettrfg_ui_shader missing, menu recolour is off"); return null; }
            _pinkGreyMat = new UnityEngine.Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(_pinkGreyMat);
            return _pinkGreyMat;
        }

        public void ReapplyBakedPinkGreyTextures()
        {
            bool pinkOn = SettingsService.Get(KEY_FG_PINK_ON, "false") == "true";
            bool blackOn = SettingsService.Get(KEY_FG_BLACK_ON, "false") == "true";

            // feature off and nothing ever shaded -> nothing to do.
            if (!pinkOn && !blackOn && _shadedImages.Count == 0) return;

            // turned off: strip our material from everything we touched, then bail.
            if (!pinkOn && !blackOn)
            {
                foreach (var i in _shadedImages) if (i != null) i.material = null;
                _shadedImages.Clear();
                return;
            }

            var mat = GetPinkGreyMaterial();
            if (mat == null) return;

            Color pink = PinkReplacement();
            Color black = BlackReplacement();
            mat.SetColor("_PinkColor", pink);
            mat.SetColor("_BlackColor", black);
            mat.SetFloat("_PinkOn", pinkOn ? 1f : 0f);
            mat.SetFloat("_BlackOn", blackOn ? 1f : 0f);

            var gameStates = GameObject.Find(GameStatesRootPath);
            if (gameStates == null) return;
            var root = gameStates.transform;

            // only full-white images under one of these four gameplay view-models get the shader.
            foreach (var img in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img == null) continue;

                var c = img.color;
                // white = full rgb, alpha ignored (some are faded in/out)
                if (c.r < 0.99f || c.g < 0.99f || c.b < 0.99f) continue;

                bool inScope = false;
                bool inCreatorInfoSlim = false;
                bool inTeamContainer = false;
                for (var p = img.transform; p != null; p = p.parent)
                {
                    var n = p.name;
                    if (n == "Generic_UI_LE_CreatorInfoSlim") { inCreatorInfoSlim = true; break; }
                    if (n == "TeamContainer") { inTeamContainer = true; break; }
                    if (n == "GameplayTimerViewModel" || n == "GameplayScoringViewModel"
                        || n == "GameplayInstructionsViewModel" || n == "GameplayTimeAttackViewModel")
                    {
                        inScope = true;
                    }
                }
                if (inCreatorInfoSlim || inTeamContainer || !inScope) continue;
                if (img.gameObject.name.Contains("Glyph")) continue;

                if (img.material != mat) img.material = mat;
                _shadedImages.Add(img);
            }
        }

        // editor screens (radial, settings backing, etc) bake their colours into the texture behind a
        // white image tint, so a flat colour swap can't reach them — the same reason the ingame HUD
        // needs a shader. white-tinted images get the pink/grey UI shader (with a cyan band added) so
        // the baked cyan/pink/grey remap on the GPU while white edges stay white. its own material keeps
        // this config off the ingame _pinkGreyMat.
        private UnityEngine.Material _editorShaderMat;

        public void ApplyEditorShader(Transform scopeRoot)
        {
            bool cyanOn = SettingsService.Get(KEY_FG_CYAN_ON, "false") == "true";
            bool pinkOn = SettingsService.Get(KEY_FG_PINK_ON, "false") == "true";
            bool blackOn = SettingsService.Get(KEY_FG_BLACK_ON, "false") == "true";
            bool anyOn = cyanOn || pinkOn || blackOn;

            if (anyOn)
            {
                if (_editorShaderMat == null)
                {
                    var sh = BetterFG.Core.AssetManager.GetShader("bettrfg_ui_shader");
                    if (sh == null) { Plugin.Log.LogWarning("bettrfg_ui_shader missing, editor recolour is off"); return; }
                    _editorShaderMat = new UnityEngine.Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                    UnityEngine.Object.DontDestroyOnLoad(_editorShaderMat);
                }
                _editorShaderMat.SetColor("_CyanColor", new Color(ParseF(KEY_FG_CYAN_R, 0f), ParseF(KEY_FG_CYAN_G, 0.3f), ParseF(KEY_FG_CYAN_B, 1f), 1f));
                _editorShaderMat.SetColor("_PinkColor", PinkReplacement());
                _editorShaderMat.SetColor("_BlackColor", BlackReplacement());
                _editorShaderMat.SetFloat("_CyanOn", cyanOn ? 1f : 0f);
                _editorShaderMat.SetFloat("_PinkOn", pinkOn ? 1f : 0f);
                _editorShaderMat.SetFloat("_BlackOn", blackOn ? 1f : 0f);
            }

            foreach (var img in scopeRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img == null) continue;
                if (img.gameObject.name.IndexOf("glyph", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                // row SelectedStateOverlay highlights are light cyan/blue; the flat sweep turns them
                // into the cyan replacement, but they should read as faint white instead.
                if (img.gameObject.name == "SelectedStateOverlay")
                {
                    if (cyanOn) img.color = new Color(1f, 1f, 1f, 0.2f);
                    continue;
                }

                // white-tinted images carry their colour in the texture — hand them the shader.
                var c = img.color;
                if (c.r >= 0.99f && c.g >= 0.99f && c.b >= 0.99f)
                    img.material = anyOn ? _editorShaderMat : null;
            }
        }

        private static UnityEngine.Sprite ProcessShowSelectSprite(UnityEngine.Sprite sprite)
        {
            bool cyanOn = SettingsService.Get(KEY_FG_CYAN_ON, "false") == "true";
            if (!cyanOn) return sprite;

            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float P(string key, float def) =>
                float.TryParse(SettingsService.Get(key, ""), System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            var cyan = new Color(P(KEY_FG_CYAN_R, 0f), P(KEY_FG_CYAN_G, 0.3f), P(KEY_FG_CYAN_B, 1f), 1f);
            var src = sprite.texture;
            var rect = sprite.textureRect;
            int w = Mathf.RoundToInt(rect.width);
            int h = Mathf.RoundToInt(rect.height);
            if (src == null || w <= 0 || h <= 0) return sprite;

            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            var old = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.name = src.name + "_betterfg_private_lobby";
            tex.filterMode = src.filterMode;
            tex.wrapMode = src.wrapMode;
            tex.anisoLevel = src.anisoLevel;
            tex.mipMapBias = src.mipMapBias;
            tex.ReadPixels(new Rect(rect.x, rect.y, w, h), 0, 0);
            tex.Apply(false, false);

            RenderTexture.active = old;
            RenderTexture.ReleaseTemporary(rt);

            var pixels = tex.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                var c = pixels[i];
                if (c.a <= 0.01f) continue;

                Color.RGBToHSV(c, out float hue, out float sat, out float val);
                if (val > 0.82f && sat < 0.2f)
                    pixels[i] = new Color(1f, 1f, 1f, c.a);
                else if (sat > 0.25f && hue >= 0.45f && hue <= 0.6f)
                    pixels[i] = new Color(cyan.r, cyan.g, cyan.b, c.a);
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);

            var made = UnityEngine.Sprite.Create(
                tex,
                new Rect(0, 0, w, h),
                new UnityEngine.Vector2(sprite.pivot.x / w, sprite.pivot.y / h),
                sprite.pixelsPerUnit,
                0,
                SpriteMeshType.FullRect,
                sprite.border
            );
            made.name = sprite.name + "_betterfg";
            return made;
        }

        // falling screen (lobby bg) — only ever driven by the falling-screen custom slot colours now.
        // the Foreground cyan/colour toggles no longer touch the lobby bg at all.
        public void ApplyLobbyBGForeground()
        {
            if (SettingsService.Get(KEY_LOBBYBG_ENABLED, "false").Equals("true"))
                ApplyLobbyBgCustomColors(
                    new Color(ParseF(KEY_LOBBYBG_SLOT0_R, 0f), ParseF(KEY_LOBBYBG_SLOT0_G, 0f), ParseF(KEY_LOBBYBG_SLOT0_B, 1f)),
                    new Color(ParseF(KEY_LOBBYBG_SLOT1_R, 0f), ParseF(KEY_LOBBYBG_SLOT1_G, 0.5f), ParseF(KEY_LOBBYBG_SLOT1_B, 1f)),
                    new Color(ParseF(KEY_LOBBYBG_SLOT2_R, 0.8f), ParseF(KEY_LOBBYBG_SLOT2_G, 0.8f), ParseF(KEY_LOBBYBG_SLOT2_B, 1f)));
            else
                RevertLobbyBGForeground();
        }

        public void RevertLobbyBGForeground()
        {
            if (_lobbyTexOriginals.Count == 0) return;

            var lobbyRoot = GameObject.Find("Menu_Screen_Lobby(Clone)/BackgroundCanvas/Prefab_UI_Lobby");
            if (lobbyRoot == null) return;

            var images = lobbyRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true);
            foreach (var img in images)
            {
                if (img == null) continue;
                int id = img.GetInstanceID();
                if (_lobbyTexOriginals.TryGetValue(id, out var origSprite))
                    img.sprite = origSprite;
                if (_lobbyColorOriginals.TryGetValue(id, out var origColor))
                    img.color = origColor;
            }

            foreach (var id in _lobbyTexOriginals.Keys) { _fgOriginals.Remove(id); _fgTouchedImages.Remove(id); }
            _lobbyTexOriginals.Clear();
            _lobbyColorOriginals.Clear();
        }

        public static IEnumerator ApplyLobbyBGForegroundNextFrame()
        {
            yield return null;
            Instance?.ApplyLobbyBGForeground();
        }

        // keys for lobby bg custom slot colours
        // enabled gate for the falling-screen (lobby bg) custom colours — independent of the
        // Foreground cyan toggle now that it has its own UI in the UI tab's Background section.
        public const string KEY_LOBBYBG_ENABLED = "menu.lobbybg.enabled";
        public const string KEY_LOBBYBG_SLOT0_R = "menu.lobbybg.slot0.r";
        public const string KEY_LOBBYBG_SLOT0_G = "menu.lobbybg.slot0.g";
        public const string KEY_LOBBYBG_SLOT0_B = "menu.lobbybg.slot0.b";
        public const string KEY_LOBBYBG_SLOT1_R = "menu.lobbybg.slot1.r";
        public const string KEY_LOBBYBG_SLOT1_G = "menu.lobbybg.slot1.g";
        public const string KEY_LOBBYBG_SLOT1_B = "menu.lobbybg.slot1.b";
        public const string KEY_LOBBYBG_SLOT2_R = "menu.lobbybg.slot2.r";
        public const string KEY_LOBBYBG_SLOT2_G = "menu.lobbybg.slot2.g";
        public const string KEY_LOBBYBG_SLOT2_B = "menu.lobbybg.slot2.b";

        // scans Prefab_UI_Lobby images and returns up to 3 dominant clustered colours from originals
        // slot indices: 0=DarkBlue, 1=MedBlue, 2=LightBlue
        private static readonly string[] LobbyBgSlotNames = { "DarkBlue", "MedBlue", "LightBlue" };

        public Color[] ScanLobbyBgColors()
        {
            var lobbyRoot = GameObject.Find("Menu_Screen_Lobby(Clone)/BackgroundCanvas/Prefab_UI_Lobby");
            var result = new Color[] { new Color(0.05f, 0.1f, 0.3f), new Color(0.1f, 0.3f, 0.7f), new Color(0.3f, 0.6f, 1f) };
            if (lobbyRoot == null) return result;

            var images = lobbyRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true);

            for (int slot = 0; slot < LobbyBgSlotNames.Length; slot++)
            {
                float r = 0f, g = 0f, b = 0f;
                int count = 0;
                foreach (var img in images)
                {
                    if (img == null || !img.gameObject.name.Contains(LobbyBgSlotNames[slot])) continue;
                    int id = img.GetInstanceID();
                    Color c = _lobbyColorOriginals.TryGetValue(id, out var orig) ? orig : img.color;
                    if (c.a < 0.05f) continue;
                    r += c.r; g += c.g; b += c.b;
                    count++;
                }
                if (count > 0)
                    result[slot] = new Color(r / count, g / count, b / count);
            }

            return result;
        }

        private static float ParseF(string key, float def) =>
            float.TryParse(SettingsService.Get(key, ""), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : def;

        // applies 3 custom slot colours to lobby bg images by name-based group match (DarkBlue/MedBlue/LightBlue)
        public void ApplyLobbyBgCustomColors(Color slot0, Color slot1, Color slot2)
        {
            var lobbyRoot = GameObject.Find("Menu_Screen_Lobby(Clone)/BackgroundCanvas/Prefab_UI_Lobby");
            if (lobbyRoot == null) return;

            var slotColors = new Color[] { slot0, slot1, slot2 };
            var images = lobbyRoot.GetComponentsInChildren<UnityEngine.UI.Image>(true);

            foreach (var img in images)
            {
                if (img == null) continue;

                int slotIdx = -1;
                for (int k = 0; k < LobbyBgSlotNames.Length; k++)
                    if (img.gameObject.name.Contains(LobbyBgSlotNames[k])) { slotIdx = k; break; }
                if (slotIdx < 0) continue;

                int id = img.GetInstanceID();
                if (!_lobbyColorOriginals.ContainsKey(id))
                    _lobbyColorOriginals[id] = img.color;
                if (!_lobbyTexOriginals.ContainsKey(id))
                    _lobbyTexOriginals[id] = img.sprite;

                Color original = _lobbyColorOriginals[id];
                if (original.a < 0.05f) continue;

                img.sprite = null;
                var target = slotColors[slotIdx];
                img.color = new Color(target.r, target.g, target.b, original.a);
            }

        }

        // ── Creative (level browser) background ────────────────────────────────
        // Generic_UI_CreativeBackground_Prefab_Canvas is a flat paper-craft UI with named image
        // groups, not a gradient. one colour per slot; Drawings also covers the Grid image.
        // originals cached per Image id so remove restores the game's own colours.
        public enum CreativeSlot { Backdrop, Glows, Drawings, Vignette }
        public const string KEY_CREATIVE_ENABLED = "screen.creative.enabled";

        public static Color CreativeSlotColor(CreativeSlot slot)
        {
            string k = $"screen.creative.{slot}";
            Color d = slot == CreativeSlot.Backdrop ? new Color(0.98f, 0.93f, 0.82f)
                    : slot == CreativeSlot.Glows ? new Color(1f, 0.98f, 0.9f)
                    : slot == CreativeSlot.Drawings ? new Color(0.2f, 0.3f, 0.45f)
                    : Color.black;
            return new Color(ParseF(k + ".r", d.r), ParseF(k + ".g", d.g), ParseF(k + ".b", d.b));
        }

        private readonly Dictionary<int, Color> _creativeOriginals = new Dictionary<int, Color>();

        // the prefab shows up in two spots, both full-path pinned (the level editor's other variants
        // share the name): the level browser popup, and the level-editor menu backdrop on the
        // world-space CameraRig (view index 3 of the MainMenuBuilder switcher).
        private static Transform CreativeBrowserCanvas()
        {
            var go = GameObject.Find("UICanvas_Client_V2(Clone)/Popup/Prime_UI_LE_LevelBrowser(Clone)/Generic_UI_CreativeBackground_Prefab_Canvas");
            return go != null ? go.transform : null;
        }

        public static Transform CreativeEditorCanvas()
        {
            var go = GameObject.Find("CameraRig/VirtualCameras/MainMenu_LevelEditor/Generic_UI_CreativeBackground_Prefab_Canvas");
            return go != null ? go.transform : null;
        }

        public void ApplyCreativeBg(Transform canvas)
        {
            if (canvas == null) return;
            RecolorCreative(canvas.Find("Backdrop"), CreativeSlot.Backdrop);
            RecolorCreativeChildren(canvas.Find("Glows"), CreativeSlot.Glows);
            RecolorCreative(canvas.Find("Grid"), CreativeSlot.Drawings);
            RecolorCreativeChildren(canvas.Find("Drawings"), CreativeSlot.Drawings);
            RecolorCreative(canvas.Find("Vignette"), CreativeSlot.Vignette);
        }

        public void RevertCreativeBg(Transform canvas)
        {
            if (canvas == null) return;
            foreach (var g in canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                if (g != null && _creativeOriginals.TryGetValue(g.GetInstanceID(), out var c)) g.color = c;
        }

        // apply if enabled, else revert — for one creative canvas. the editor canvas' OnEnable applier
        // and the Screen tab both go through here.
        public void RefreshCreativeCanvas(Transform canvas)
        {
            if (canvas == null) return;
            if (SettingsService.Get(KEY_CREATIVE_ENABLED, "false") == "true") ApplyCreativeBg(canvas);
            else RevertCreativeBg(canvas);
        }

        // Screen tab Apply/Remove/toggle — hit whichever creative canvas is up right now
        public void ReapplyCreativeBgLive()
        {
            RefreshCreativeCanvas(CreativeBrowserCanvas());
            RefreshCreativeCanvas(CreativeEditorCanvas());
        }

        private void RecolorCreativeChildren(Transform parent, CreativeSlot slot)
        {
            if (parent == null) return;
            for (int i = 0; i < parent.childCount; i++) RecolorCreative(parent.GetChild(i), slot);
        }

        private void RecolorCreative(Transform t, CreativeSlot slot)
        {
            var g = t != null ? t.GetComponent<UnityEngine.UI.Graphic>() : null;
            if (g == null) return;
            int id = g.GetInstanceID();
            if (!_creativeOriginals.ContainsKey(id)) _creativeOriginals[id] = g.color;
            var c = CreativeSlotColor(slot);
            g.color = new Color(c.r, c.g, c.b, g.color.a);
        }

        // gradient settings keys — these are the FallForce screen's keys (menu + title share them).
        // the Screen tab edits the same screen.fallforce.* keys via ScreenBackgroundService.
        public const string KEY_BG_TOP_R = "screen.fallforce.top.r";
        public const string KEY_BG_TOP_G = "screen.fallforce.top.g";
        public const string KEY_BG_TOP_B = "screen.fallforce.top.b";
        public const string KEY_BG_TOP_A = "screen.fallforce.top.a";
        public const string KEY_BG_BOT_R = "screen.fallforce.bot.r";
        public const string KEY_BG_BOT_G = "screen.fallforce.bot.g";
        public const string KEY_BG_BOT_B = "screen.fallforce.bot.b";
        public const string KEY_BG_BOT_A = "screen.fallforce.bot.a";
        public const string KEY_BG_BIAS = "screen.fallforce.bias";
        public const string KEY_BG_SMOOTH = "screen.fallforce.smooth";

        void Awake() { Instance = this; MigrateOldBgKeys(); MigrateBgSplit(); }

        // one-shot: previously screen.fallforce.enabled drove BOTH the GO active state AND the
        // custom-colour apply. it's now split — bg.enabled drives the GO. for users who already
        // had the screen enabled, carry that state into the new bg.enabled so the bg stays on.
        private static void MigrateBgSplit()
        {
            if (SettingsService.Get("screen.bgsplit.migrated", "false") == "true") return;
            if (SettingsService.Get("screen.fallforce.enabled", "false") == "true")
                SettingsService.Set(KEY_BG_ENABLED, "true");
            SettingsService.Set("screen.bgsplit.migrated", "true");
        }

        // one-time copy of the old menu.bg.* gradient/pattern values into the new screen.fallforce.*
        // keys so existing setups keep their look after the migration to per-screen settings.
        private static void MigrateOldBgKeys()
        {
            if (SettingsService.Get("screen.migrated", "false") == "true") return;
            (string oldK, string newK)[] map =
            {
                ("menu.bg.top.r", KEY_BG_TOP_R), ("menu.bg.top.g", KEY_BG_TOP_G), ("menu.bg.top.b", KEY_BG_TOP_B),
                ("menu.bg.bot.r", KEY_BG_BOT_R), ("menu.bg.bot.g", KEY_BG_BOT_G), ("menu.bg.bot.b", KEY_BG_BOT_B),
                ("menu.bg.bias", KEY_BG_BIAS), ("menu.bg.smooth", KEY_BG_SMOOTH),
                ("menu.bg.enabled", "screen.fallforce.enabled"),
                ("menu.bg.pattern.path", "screen.fallforce.pattern.path"),
            };
            foreach (var (oldK, newK) in map)
            {
                var v = SettingsService.Get(oldK, "");
                if (!string.IsNullOrEmpty(v)) SettingsService.Set(newK, v);
            }
            SettingsService.Set("screen.migrated", "true");
        }

        // ── Bundle registry ───────────────────────────────────────────────────

        public bool TryGetBundle(string file, out AssetBundle bundle)
        {
            bundle = null;
            if (string.IsNullOrEmpty(file)) return false;
            return _bundles.TryGetValue(file, out bundle) && bundle != null;
        }

        public AssetBundle GetOrRegisterBundle(string file, AssetBundle incoming)
        {
            if (string.IsNullOrEmpty(file)) return incoming;
            if (_bundles.TryGetValue(file, out var existing) && existing != null) return existing;
            if (incoming != null) _bundles[file] = incoming;
            return incoming;
        }

        // ── Public API ────────────────────────────────────────────────────────

        public void ReapplyToMainMenu()
        {
            if (_lastInfo == null || _lastBundle == null)
            {
                // runtime state's gone (game rebuilt the plinth screen, or initial restore never
                // landed) but a plinth is still saved — pull it back from settings and re-apply.
                SkinApplicationService.Instance?.RestorePlinthFromSettings();
                return;
            }
            if (_appliedPlinth != null) { Destroy(_appliedPlinth); _appliedPlinth = null; }
            _origActive = true;
            StartCoroutine(ApplyToMainMenuCoroutine(_lastInfo, _lastBundle).WrapToIl2Cpp());
        }

        public void ApplyPlinth(SkinInfo info, AssetBundle bundle)
        {
            if (info == null || bundle == null) return;

            bundle = GetOrRegisterBundle(info.file, bundle);
            _lastInfo = info;
            _lastBundle = bundle;
            // commit the active file NOW (not at the end of the async coroutine) so a second Apply
            // press while this one is still loading sees ActiveFile==file and skips re-applying
            _appliedFile = info.file;

            StartCoroutine(ApplyToMainMenuCoroutine(info, bundle).WrapToIl2Cpp());

            BeanMonitorService.ClearDestroyedPlinths();
            foreach (var slot in BeanMonitorService.GetTrackedPlinths())
                StartCoroutine(ApplyToSlotCoroutine(slot, info, bundle).WrapToIl2Cpp());
        }

        public void ApplyToPlinthSlot(PlinthSlot slot)
        {
            if (slot == null || _lastInfo == null || _lastBundle == null) return;
            StartCoroutine(ApplyToSlotCoroutine(slot, _lastInfo, _lastBundle).WrapToIl2Cpp());
        }

        // applies a profile's plinth (loaded from raw bytes) to one specific holder slot WITHOUT
        // touching the local plinth state (_lastInfo/_appliedPlinth). used for lobby remote players —
        // each party holder can get a different person's plinth.
        public void ApplyProfilePlinthToSlot(SkinInfo info, byte[] bytes, PlinthSlot slot)
        {
            if (info == null || bytes == null || slot == null) return;
            StartCoroutine(ApplyProfilePlinthToSlotCoroutine(info, bytes, slot).WrapToIl2Cpp());
        }

        private IEnumerator ApplyProfilePlinthToSlotCoroutine(SkinInfo info, byte[] bytes, PlinthSlot slot)
        {
            AssetBundle bundle;
            if (!TryGetBundle(info.file, out bundle) || bundle == null)
            {
                var loadReq = AssetBundle.LoadFromMemoryAsync(bytes);
                yield return loadReq;
                bundle = loadReq.assetBundle;
                if (bundle == null) { Plugin.Log.LogWarning($"lobby plinth bundle wouldn't load: {info.file}"); yield break; }
                bundle = GetOrRegisterBundle(info.file, bundle);
            }
            yield return ApplyToSlotCoroutine(slot, info, bundle).WrapToIl2Cpp();
        }

        // ── Menu background ───────────────────────────────────────────────────

        public void SpawnMenuBg()
        {
            // gradient prefab: spawn once
            if (_menuBgGo == null)
            {
                TweakFallGuysBgForBetterfg();

                var go = AssetManager.SpawnPersistent("betterfg_menubg");
                if (go == null) { Plugin.Log.LogWarning("menubg prefab missing from the bundle"); return; }

                _menuBgGo = go;
                _menuBgGo.transform.SetParent(GameObject.Find(FGUI_CUSTOM_BACKDROP_PARENT_PATH).transform, true);
                _menuBgGo.transform.localPosition = FG_UI_CUSTOM_BACKDROP_PREFERREDLOCALPOS;
                _menuBgGo.transform.localRotation = Quaternion.Euler(270, 0, 0);
                _menuBgGo.layer = LayerMask.NameToLayer("PlayerUI");
                _menuBgGo.name = "BetterFG_MenuBg";

                var rend = _menuBgGo.GetComponent<Renderer>();
                if (rend != null) _menuBgMat = rend.material;
            }

            // restore everything every menu enter (not just first time)
            ApplyGradientFromSettings();
            BetterFG.UI.Tab.UITab.ApplyCanvasScalingFromSettings();

            bool bgEnabled = SettingsService.Get(KEY_BG_ENABLED, "false") == "true";
            if (_menuBgGo != null) _menuBgGo.SetActive(bgEnabled);

            EnsureImageBg();
            ApplyImageBgFromSettings();

            // sun GO is fresh each menu enter — recapture its original rotation before applying.
            // ambient/sun must run after the game finishes its own scene lighting setup, else it
            // overwrites RenderSettings/the sun transform — so defer a frame.
            _sunSaved = false;
            _ambientSaved = false;
            _plinthColSaved = false;
            StartCoroutine(ApplyAmbientAndSunNextFrame().WrapToIl2Cpp());

        }

        // title screen uses the FallForce screen's gradient + pattern (same look as the menu).
        // the bg respawns over the first few hundred ms, so re-assert a few times to win the race.
        public void ReapplyTitleScreenBg()
        {
            StartCoroutine(ReapplyTitleScreenBgLoop().WrapToIl2Cpp());
        }

        private IEnumerator ReapplyTitleScreenBgLoop()
        {
            yield return null; // let the title screen finish building its background first
            for (int i = 0; i < 6; i++)
            {
                var maskGo = GameObject.Find(FGUI_TITLE_BACKDROP_PARENT_PATH);
                if (maskGo != null)
                    ScreenBackgroundService.Apply(ScreenBackgroundService.Screen.FallForce, maskGo.transform);
                yield return new WaitForSeconds(0.1f);
            }
        }

        private IEnumerator ApplyAmbientAndSunNextFrame()
        {
            yield return null;
            ApplyAmbientFromSettings();
            ApplySunFromSettings();
            ApplyPlinthColorFromSettings();
            ApplyPatternFromSettings();

            // game re-runs its own scene lighting setup a bit into the menu and stomps our ambient.
            // coming back from a game the scene takes longer to settle than a single 0.1s window, so
            // reassert a handful of times over ~1s to win the race regardless of how late it lands.
            for (int i = 0; i < 8; i++)
            {
                yield return new WaitForSeconds(0.12f);
                ApplyAmbientFromSettings();
            }
        }

        // ── Menu background image ─────────────────────────────────────────────

        // fallback base only — the real base is PB_UI_Character's world position, cached once in the
        // OnMainMenuEntered postfix (content updates move it, same story as the cam). never re-cached.
        private static readonly Vector3 BG_IMG_BASE_WORLD_POS = new Vector3(0f, 3.5f, 4.3f);
        private Vector3 _bgImgBasePos = BG_IMG_BASE_WORLD_POS;
        private bool _bgImgBaseCached;

        public void CacheBgImageBase()
        {
            if (_bgImgBaseCached) return;
            var t = GameObject.Find("3D Environment/MainMenu_Environment/PlinthRig/CharacterAndPlinthHolder_Main/ENV_Plinth_MO/CharacterHolder/PB_UI_Character");
            if (t == null) return;
            _bgImgBasePos = t.transform.position;
            _bgImgBaseCached = true;
        }

        private void EnsureImageBg()
        {
            // ?. against a destroyed Unity object returns true for == null, so this also recreates after scene unload
            if (_menuBgImageGo != null) return;

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "BetterFG_MenuBgImage";
            var col = quad.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // unparented + scene-bound: gets cleaned up when the menu scene unloads,
            // so it never leaks into rounds.
            quad.transform.position = _bgImgBasePos;
            quad.transform.rotation = Quaternion.Euler(0, 0, 0);
            quad.transform.localScale = Vector3.one;
            quad.layer = LayerMask.NameToLayer("PlayerUI");

            Shader shader = null;
            foreach (var name in new[] { "Unlit/Texture", "Unlit/Transparent", "Universal Render Pipeline/Unlit", "Sprites/Default", "UI/Default" })
            {
                shader = Shader.Find(name);
                if (shader != null) break;
            }
            if (shader == null) return;

            var mat = new Material(shader);
            mat.color = Color.white;
            var rend = quad.GetComponent<Renderer>();
            rend.material = mat;
            // re-read the renderer's actual instance — assigning .material may instantiate a copy
            _menuBgImageMat = rend.material;
            _menuBgImageMat.renderQueue = 2990; // just behind plinth, in front of gradient backdrop

            _menuBgImageGo = quad;
        }

        // image bg is hidden whenever the customiser prefab canvas or store screen is active
        private const string CUSTOMISER_CANVAS_PATH = "UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Customiser/Prime_UI_Customizer_Prefab_Canvas(Clone)";
        private const string STORE_SCREEN_PATH = "UICanvas_Client_V2(Clone)/Default/MainMenuBuilder(Clone)/MainScreensParent/Menu_Screen_Store";

        public void SetImageBgEnabled(bool enabled)
        {
            SettingsService.Set(KEY_BG_IMG_ENABLED, enabled ? "true" : "false");
            EnsureImageBg();
            RefreshImageBgVisibility();
        }

        // true when the customiser prefab canvas is active in the hierarchy
        private static bool IsCustomiserOpen()
        {
            var go = GameObject.Find(CUSTOMISER_CANVAS_PATH);
            return go != null && go.activeInHierarchy;
        }

        private static bool IsStoreOpen()
        {
            var go = GameObject.Find(STORE_SCREEN_PATH);
            return go != null && go.activeInHierarchy;
        }

        public void RefreshImageBgVisibility()
        {
            if (_menuBgImageGo == null) return;
            bool enabled = SettingsService.Get(KEY_BG_IMG_ENABLED, "false") == "true";
            _menuBgImageGo.SetActive(enabled && !IsCustomiserOpen() && !IsStoreOpen());
        }

        public void HideImageBg()
        {
            if (_menuBgImageGo != null) _menuBgImageGo.SetActive(false);
        }

        public void ApplyImageBgTexture(string path)
        {
            EnsureImageBg();
            if (_menuBgImageMat == null) return;

            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
            {
                _menuBgImageMat.mainTexture = null;
                return;
            }

            try
            {
                var bytes = System.IO.File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes)) { Plugin.Log.LogWarning($"not a decodable image: {System.IO.Path.GetFileName(path)}"); return; }
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.Apply();
                if (_menuBgImageTex != null) Destroy(_menuBgImageTex);
                _menuBgImageTex = tex;

                foreach (var prop in new[] { "_MainTex", "_BaseMap", "_BaseColorMap", "_UnlitColorMap", "_Texture", "_Tex" })
                    if (_menuBgImageMat.HasProperty(prop))
                        _menuBgImageMat.SetTexture(prop, tex);
                _menuBgImageMat.mainTexture = tex;
            }
            catch (Exception ex) { Plugin.Log.LogError($"menu bg image '{System.IO.Path.GetFileName(path)}' failed to load: {ex.Message}"); }
        }

        public void ApplyImageBgTransform(float posX, float posY, float posZ, float scaleUniform, float scaleX, float scaleY)
        {
            EnsureImageBg();
            if (_menuBgImageGo == null) return;

            _menuBgImageGo.transform.position =
                _bgImgBasePos + new Vector3(posX, posY, posZ);
            _menuBgImageGo.transform.localScale =
                new Vector3(scaleUniform * scaleX, scaleUniform * scaleY, 1f);
        }

        public void ApplyImageBgFromSettings()
        {
            EnsureImageBg();
            if (_menuBgImageGo == null) return;

            ApplyImageBgTexture(SettingsService.Get(KEY_BG_IMG_PATH, ""));
            ApplyImageBgTransform(
                ParseF(KEY_BG_IMG_POS_X, BG_IMG_POS_DEFAULT),
                ParseF(KEY_BG_IMG_POS_Y, BG_IMG_POS_DEFAULT),
                ParseF(KEY_BG_IMG_POS_Z, BG_IMG_POS_DEFAULT),
                ParseF(KEY_BG_IMG_SCALE, BG_IMG_SCALE_DEFAULT),
                ParseF(KEY_BG_IMG_SCALE_X, BG_IMG_SCALE_AXIS_DEFAULT),
                ParseF(KEY_BG_IMG_SCALE_Y, BG_IMG_SCALE_AXIS_DEFAULT));

            RefreshImageBgVisibility();
        }

        // ── Ambient light (flat) ──────────────────────────────────────────────

        public void SetAmbientEnabled(bool enabled)
        {
            SettingsService.Set(KEY_AMBIENT_ON, enabled ? "true" : "false");
            if (enabled)
                ApplyAmbient(new Color(ParseF(KEY_AMBIENT_R, 0.5f), ParseF(KEY_AMBIENT_G, 0.5f), ParseF(KEY_AMBIENT_B, 0.5f)));
            else
                RevertAmbient();
        }

        public void ApplyAmbient(Color color)
        {
            if (!_ambientSaved)
            {
                _ambientOldMode = RenderSettings.ambientMode;
                _ambientOldLight = RenderSettings.ambientLight;
                _ambientSaved = true;
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = color;
        }

        public void RevertAmbient()
        {
            if (!_ambientSaved) return;
            RenderSettings.ambientMode = _ambientOldMode;
            RenderSettings.ambientLight = _ambientOldLight;
            _ambientSaved = false;
        }

        public void ApplyAmbientFromSettings()
        {
            // only apply in the menu scene — gate on the root MainMenuManager existing
            if (GameObject.Find("MainMenuManager") == null) return;
            if (SettingsService.Get(KEY_AMBIENT_ON, "false") != "true") return;
            ApplyAmbient(new Color(ParseF(KEY_AMBIENT_R, 0.5f), ParseF(KEY_AMBIENT_G, 0.5f), ParseF(KEY_AMBIENT_B, 0.5f)));
        }

        // ── Main sun rotation ─────────────────────────────────────────────────

        private Transform FindSun()
        {
            var go = GameObject.Find(SUN_LIGHT_PATH);
            return go != null ? go.transform : null;
        }

        public void SetSunEnabled(bool enabled)
        {
            SettingsService.Set(KEY_SUN_ON, enabled ? "true" : "false");
            if (enabled)
                ApplySunRotation(ParseF(KEY_SUN_ROT_X, 50f), ParseF(KEY_SUN_ROT_Y, 0f), ParseF(KEY_SUN_ROT_Z, 0f));
            else
                RevertSun();
        }

        public void ApplySunRotation(float x, float y, float z)
        {
            var sun = FindSun();
            if (sun == null) return;
            if (!_sunSaved)
            {
                _sunOldRot = sun.localRotation;
                _sunSaved = true;
            }
            sun.localRotation = Quaternion.Euler(x, y, z);
        }

        public void RevertSun()
        {
            if (!_sunSaved) return;
            var sun = FindSun();
            if (sun != null) sun.localRotation = _sunOldRot;
            _sunSaved = false;
        }

        public void ApplySunFromSettings()
        {
            if (GameObject.Find("MainMenuManager") == null) return;
            if (SettingsService.Get(KEY_SUN_ON, "false") != "true") return;
            ApplySunRotation(ParseF(KEY_SUN_ROT_X, 50f), ParseF(KEY_SUN_ROT_Y, 0f), ParseF(KEY_SUN_ROT_Z, 0f));
        }

        // ── Plinth colour ─────────────────────────────────────────────────────

        private Renderer FindPlinthRenderer()
        {
            var go = GameObject.Find(PLINTH_MESH_PATH);
            return go != null ? go.GetComponent<Renderer>() : null;
        }

        public void SetPlinthColorEnabled(bool enabled)
        {
            SettingsService.Set(KEY_PLINTH_COL_ON, enabled ? "true" : "false");
            if (enabled)
                ApplyPlinthColor(new Color(ParseF(KEY_PLINTH_COL_R, 1f), ParseF(KEY_PLINTH_COL_G, 1f), ParseF(KEY_PLINTH_COL_B, 1f)));
            else
                RevertPlinthColor();
        }

        public void ApplyPlinthColor(Color color)
        {
            var rend = FindPlinthRenderer();
            if (rend == null || rend.material == null) return;
            if (!_plinthColSaved)
            {
                _plinthColOld = rend.material.color;
                _plinthColSaved = true;
            }
            rend.material.color = new Color(color.r, color.g, color.b, rend.material.color.a);
        }

        public void RevertPlinthColor()
        {
            if (!_plinthColSaved) return;
            var rend = FindPlinthRenderer();
            if (rend != null && rend.material != null)
                rend.material.color = _plinthColOld;
            _plinthColSaved = false;
        }

        public void ApplyPlinthColorFromSettings()
        {
            if (GameObject.Find("MainMenuManager") == null) return;
            if (SettingsService.Get(KEY_PLINTH_COL_ON, "false") != "true") return;
            ApplyPlinthColor(new Color(ParseF(KEY_PLINTH_COL_R, 1f), ParseF(KEY_PLINTH_COL_G, 1f), ParseF(KEY_PLINTH_COL_B, 1f)));
        }

        public void SetMenuBgEnabled(bool enabled)
        {
            SettingsService.Set(KEY_BG_ENABLED, enabled ? "true" : "false");
            if (_menuBgGo != null)
                _menuBgGo.SetActive(enabled);
        }

        public void TweakFallGuysBgForBetterfg()
        {
            GameObject.Find(FGUI_ORIGINAL_BACKDROP_PATH).transform.localPosition = FG_UI_ORIGINAL_BACKDROP_PREFERREDLOCALPOS;
            GameObject.Find(FGUI_ORIGINAL_CIRCLES_PATH).transform.localPosition = FG_UI_ORIGINAL_CIRCLES_PREFERREDLOCALPOS;
        }

        // applies the saved circles pattern texture onto the Circles image material. safe to call
        // repeatedly — the Circles GO is fresh each menu enter so we re-resolve it every time, and
        // we cache the untouched original on first apply so RestorePattern can put it back.
        public void ApplyPatternFromSettings()
        {
            var circlesGo = GameObject.Find(FGUI_ORIGINAL_CIRCLES_PATH);
            if (circlesGo == null) return; // not up yet — retry loop will catch it

            var img = circlesGo.GetComponent<UnityEngine.UI.Image>();
            if (img == null || img.material == null) return;

            // cache the REAL original exactly once per session. only safe to read here when we haven't
            // applied a custom yet — once we have, GetTexture("_Pattern") returns our own custom tex.
            if (!_originalPatternCaptured && _appliedPatternTex == null)
            {
                _originalPatternTex = img.material.GetTexture("_Pattern");
                _originalPatternCaptured = true;
            }

            string path = SettingsService.Get(KEY_PATTERN_PATH, "");
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;

            try
            {
                byte[] data = System.IO.File.ReadAllBytes(path);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(data);
                tex.Apply();

                if (_appliedPatternTex != null) Destroy(_appliedPatternTex);
                _appliedPatternTex = tex;

                img.material.SetTexture("_Pattern", tex);
                Plugin.Log.LogInfo($"menu pattern -> {System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex) { Plugin.Log.LogError($"menu pattern failed: {ex.Message}"); }
        }

        public void RestorePattern()
        {
            // nothing to do if we never applied a custom in the first place
            if (!_originalPatternCaptured && _appliedPatternTex == null) return;

            var circlesGo = GameObject.Find(FGUI_ORIGINAL_CIRCLES_PATH);
            if (circlesGo == null) return;
            var img = circlesGo.GetComponent<UnityEngine.UI.Image>();
            if (img == null || img.material == null) return;

            img.material.SetTexture("_Pattern", _originalPatternTex);

            if (_appliedPatternTex != null)
            {
                Destroy(_appliedPatternTex);
                _appliedPatternTex = null;
            }
        }

        public void ApplyGradient(Color top, Color bot, float bias, float smoothness)
        {
            if (_menuBgMat == null) return;
            _menuBgMat.SetColor("_TopColor", top);
            _menuBgMat.SetColor("_BottomColor", bot);
            _menuBgMat.SetFloat("_Bias", bias);
            _menuBgMat.SetFloat("_Smoothness", smoothness);
        }

        public void ApplyGradientFromSettings()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float Parse(string key, float def) =>
                float.TryParse(SettingsService.Get(key, def.ToString(ci)), System.Globalization.NumberStyles.Float, ci, out float v) ? v : def;

            var top = new Color(Parse(KEY_BG_TOP_R, 0f), Parse(KEY_BG_TOP_G, 0f), Parse(KEY_BG_TOP_B, 0f), Parse(KEY_BG_TOP_A, 1f));
            var bot = new Color(Parse(KEY_BG_BOT_R, 1f), Parse(KEY_BG_BOT_G, 1f), Parse(KEY_BG_BOT_B, 1f), Parse(KEY_BG_BOT_A, 1f));
            float bias = Parse(KEY_BG_BIAS, 0f);
            float smooth = Parse(KEY_BG_SMOOTH, 1f);
            ApplyGradient(top, bot, bias, smooth);
        }

        public void RemovePlinth()
        {
            if (_appliedPlinth != null)
            {
                Destroy(_appliedPlinth);
                _appliedPlinth = null;
            }

            var mesh = GameObject.Find(PLINTH_MESH_PATH);
            if (mesh != null) mesh.SetActive(true);

            foreach (var kvp in _extraApplied)
                if (kvp.Value != null) Destroy(kvp.Value);
            _extraApplied.Clear();

            foreach (var slot in BeanMonitorService.GetTrackedPlinths())
            {
                if (slot.meshGO == null) continue;
                slot.meshGO.SetActive(true);
            }
            _extraOrigActive.Clear();

            foreach (var kvp in _bundles)
                if (kvp.Value != null) kvp.Value.Unload(false);
            _bundles.Clear();

            _lastInfo = null;
            _lastBundle = null;
            _appliedFile = null;
            _origActive = true;

        }

        public bool HasPlinthApplied => _appliedPlinth != null || _lastInfo != null;
        public string ActiveFile => _appliedFile;

        // ── Coroutines ────────────────────────────────────────────────────────

        private IEnumerator ApplyToMainMenuCoroutine(SkinInfo info, AssetBundle bundle)
        {
            var holder = GameObject.Find(PLINTH_PATH);
            var mesh = GameObject.Find(PLINTH_MESH_PATH);

            if (holder == null || mesh == null)
            {
                Plugin.Log.LogWarning("no plinth holder/mesh in the main menu, skipping");
                OnStatus?.Invoke("Plinth: mesh not found");
                yield break;
            }

            if (_appliedPlinth != null)
            {
                Destroy(_appliedPlinth);
                _appliedPlinth = null;
            }

            _origActive = mesh.activeSelf;
            mesh.SetActive(false);

            string prefabName = FindPrefabName(bundle);
            if (prefabName == null)
            {
                Plugin.Log.LogWarning("plinth bundle has no prefab");
                OnStatus?.Invoke("Plinth: bad bundle");
                mesh.SetActive(_origActive);
                yield break;
            }

            var req = bundle.LoadAssetAsync<GameObject>(prefabName);
            yield return req;

            var prefab = req.asset?.Cast<GameObject>();
            if (prefab == null)
            {
                Plugin.Log.LogWarning("plinth prefab cast failed");
                OnStatus?.Invoke("Plinth: load failed");
                mesh.SetActive(_origActive);
                yield break;
            }

            var clone = Instantiate(prefab, holder.transform);
            clone.transform.localPosition = internaloffset;
            clone.transform.localRotation = Quaternion.identity;
            clone.layer = LayerMask.NameToLayer("PlayerUI");
            clone.name = "BetterFG_Plinth";

            yield return null;

            SkinApplicationService.SetRenderQueue(clone, 3000);

            _appliedPlinth = clone;
            _appliedFile = info.file;

            Plugin.Log.LogInfo($"plinth {info.name} on the main menu");
            OnStatus?.Invoke($"Plinth: {info.name}");
        }

        private IEnumerator ApplyToSlotCoroutine(PlinthSlot slot, SkinInfo info, AssetBundle bundle)
        {
            if (slot?.holderGO == null || slot.meshGO == null) yield break;

            int id = slot.holderGO.GetInstanceID();

            if (_extraApplied.TryGetValue(id, out var existing) && existing != null)
            {
                Destroy(existing);
                _extraApplied.Remove(id);
            }

            if (!_extraOrigActive.ContainsKey(id))
                _extraOrigActive[id] = slot.meshGO.activeSelf;

            slot.meshGO.SetActive(false);

            string prefabName = FindPrefabName(bundle);
            if (prefabName == null)
            {
                Plugin.Log.LogWarning($"plinth bundle has no prefab, slot {slot.type}");
                slot.meshGO.SetActive(_extraOrigActive[id]);
                yield break;
            }

            var req = bundle.LoadAssetAsync<GameObject>(prefabName);
            yield return req;

            var prefab = req.asset?.Cast<GameObject>();
            if (prefab == null)
            {
                Plugin.Log.LogWarning($"plinth prefab cast failed, slot {slot.type}");
                slot.meshGO.SetActive(_extraOrigActive[id]);
                yield break;
            }

            var clone = Instantiate(prefab, slot.holderGO.transform);
            clone.transform.localPosition = slot.type == PlinthType.Victory ? internaloffsetVictory : internaloffset;
            clone.transform.localRotation = Quaternion.identity;
            clone.layer = LayerMask.NameToLayer("PlayerUI");
            clone.name = "BetterFG_Plinth";

            yield return null;

            SkinApplicationService.SetRenderQueue(clone, 3000);

            _extraApplied[id] = clone;
            Plugin.Log.LogInfo($"plinth {info.name} -> slot {slot.type}");
        }

        // ── Banner colour overrides (Qualified / Eliminated) ──────────────────
        // each banner subtree gets its Image fills + TMP text/outline/underlay swapped to user colours.
        // four per-banner channels: bg, text, outline, underlay. each with its own on flag + rgb.
        // applied straight from the OnOpened patch — the banner builds its visuals before OnOpened fires,
        // so a single sweep of its children is enough.

        // per-banner master enable. when off, ApplyBannerColours skips the recolour entirely and the
        // banner stays stock — regardless of per-slot .on flags. live-saved from the UI toggle.
        public const string KEY_BANNER_QUAL_ENABLED  = "menu.banner.qual.enabled";
        public const string KEY_BANNER_ELIM_ENABLED  = "menu.banner.elim.enabled";
        public const string KEY_BANNER_WIN_ENABLED   = "menu.banner.win.enabled";
        public const string KEY_BANNER_ROUND_ENABLED = "menu.banner.round.enabled";

        // per-banner replacement colours (cyan / pink / black / white). same shape as KEY_FG_*:
        // each replacement remaps the matching source colour wherever it appears on the banner's
        // Images, TMP text fills, outline material colour, and underlay material colour.
        public const string KEY_BANNER_QUAL_CYAN_ON  = "menu.banner.qual.cyan.on";
        public const string KEY_BANNER_QUAL_CYAN_R   = "menu.banner.qual.cyan.r";
        public const string KEY_BANNER_QUAL_CYAN_G   = "menu.banner.qual.cyan.g";
        public const string KEY_BANNER_QUAL_CYAN_B   = "menu.banner.qual.cyan.b";
        public const string KEY_BANNER_QUAL_PINK_ON  = "menu.banner.qual.pink.on";
        public const string KEY_BANNER_QUAL_PINK_R   = "menu.banner.qual.pink.r";
        public const string KEY_BANNER_QUAL_PINK_G   = "menu.banner.qual.pink.g";
        public const string KEY_BANNER_QUAL_PINK_B   = "menu.banner.qual.pink.b";
        public const string KEY_BANNER_QUAL_BLACK_ON = "menu.banner.qual.black.on";
        public const string KEY_BANNER_QUAL_BLACK_R  = "menu.banner.qual.black.r";
        public const string KEY_BANNER_QUAL_BLACK_G  = "menu.banner.qual.black.g";
        public const string KEY_BANNER_QUAL_BLACK_B  = "menu.banner.qual.black.b";
        public const string KEY_BANNER_QUAL_WHITE_ON     = "menu.banner.qual.white.on";
        public const string KEY_BANNER_QUAL_WHITE_R      = "menu.banner.qual.white.r";
        public const string KEY_BANNER_QUAL_WHITE_G      = "menu.banner.qual.white.g";
        public const string KEY_BANNER_QUAL_WHITE_B      = "menu.banner.qual.white.b";
        public const string KEY_BANNER_QUAL_HIGHLIGHT_ON = "menu.banner.qual.highlight.on";
        public const string KEY_BANNER_QUAL_HIGHLIGHT_R  = "menu.banner.qual.highlight.r";
        public const string KEY_BANNER_QUAL_HIGHLIGHT_G  = "menu.banner.qual.highlight.g";
        public const string KEY_BANNER_QUAL_HIGHLIGHT_B  = "menu.banner.qual.highlight.b";

        public const string KEY_BANNER_ELIM_CYAN_ON  = "menu.banner.elim.cyan.on";
        public const string KEY_BANNER_ELIM_CYAN_R   = "menu.banner.elim.cyan.r";
        public const string KEY_BANNER_ELIM_CYAN_G   = "menu.banner.elim.cyan.g";
        public const string KEY_BANNER_ELIM_CYAN_B   = "menu.banner.elim.cyan.b";
        public const string KEY_BANNER_ELIM_PINK_ON  = "menu.banner.elim.pink.on";
        public const string KEY_BANNER_ELIM_PINK_R   = "menu.banner.elim.pink.r";
        public const string KEY_BANNER_ELIM_PINK_G   = "menu.banner.elim.pink.g";
        public const string KEY_BANNER_ELIM_PINK_B   = "menu.banner.elim.pink.b";
        public const string KEY_BANNER_ELIM_BLACK_ON = "menu.banner.elim.black.on";
        public const string KEY_BANNER_ELIM_BLACK_R  = "menu.banner.elim.black.r";
        public const string KEY_BANNER_ELIM_BLACK_G  = "menu.banner.elim.black.g";
        public const string KEY_BANNER_ELIM_BLACK_B  = "menu.banner.elim.black.b";
        public const string KEY_BANNER_ELIM_WHITE_ON     = "menu.banner.elim.white.on";
        public const string KEY_BANNER_ELIM_WHITE_R      = "menu.banner.elim.white.r";
        public const string KEY_BANNER_ELIM_WHITE_G      = "menu.banner.elim.white.g";
        public const string KEY_BANNER_ELIM_WHITE_B      = "menu.banner.elim.white.b";
        public const string KEY_BANNER_ELIM_HIGHLIGHT_ON = "menu.banner.elim.highlight.on";
        public const string KEY_BANNER_ELIM_HIGHLIGHT_R  = "menu.banner.elim.highlight.r";
        public const string KEY_BANNER_ELIM_HIGHLIGHT_G  = "menu.banner.elim.highlight.g";
        public const string KEY_BANNER_ELIM_HIGHLIGHT_B  = "menu.banner.elim.highlight.b";

        // winner banner: yellow / orange / white / black (+ highlight)
        public const string KEY_BANNER_WIN_YELLOW_ON = "menu.banner.win.yellow.on";
        public const string KEY_BANNER_WIN_YELLOW_R  = "menu.banner.win.yellow.r";
        public const string KEY_BANNER_WIN_YELLOW_G  = "menu.banner.win.yellow.g";
        public const string KEY_BANNER_WIN_YELLOW_B  = "menu.banner.win.yellow.b";
        public const string KEY_BANNER_WIN_ORANGE_ON = "menu.banner.win.orange.on";
        public const string KEY_BANNER_WIN_ORANGE_R  = "menu.banner.win.orange.r";
        public const string KEY_BANNER_WIN_ORANGE_G  = "menu.banner.win.orange.g";
        public const string KEY_BANNER_WIN_ORANGE_B  = "menu.banner.win.orange.b";
        public const string KEY_BANNER_WIN_WHITE_ON  = "menu.banner.win.white.on";
        public const string KEY_BANNER_WIN_WHITE_R   = "menu.banner.win.white.r";
        public const string KEY_BANNER_WIN_WHITE_G   = "menu.banner.win.white.g";
        public const string KEY_BANNER_WIN_WHITE_B   = "menu.banner.win.white.b";
        public const string KEY_BANNER_WIN_BLACK_ON  = "menu.banner.win.black.on";
        public const string KEY_BANNER_WIN_BLACK_R   = "menu.banner.win.black.r";
        public const string KEY_BANNER_WIN_BLACK_G   = "menu.banner.win.black.g";
        public const string KEY_BANNER_WIN_BLACK_B   = "menu.banner.win.black.b";
        public const string KEY_BANNER_WIN_HIGHLIGHT_ON = "menu.banner.win.highlight.on";
        public const string KEY_BANNER_WIN_HIGHLIGHT_R  = "menu.banner.win.highlight.r";
        public const string KEY_BANNER_WIN_HIGHLIGHT_G  = "menu.banner.win.highlight.g";
        public const string KEY_BANNER_WIN_HIGHLIGHT_B  = "menu.banner.win.highlight.b";

        // round over banner: black / pink / blue / white (+ highlight)
        public const string KEY_BANNER_ROUND_BLACK_ON = "menu.banner.round.black.on";
        public const string KEY_BANNER_ROUND_BLACK_R  = "menu.banner.round.black.r";
        public const string KEY_BANNER_ROUND_BLACK_G  = "menu.banner.round.black.g";
        public const string KEY_BANNER_ROUND_BLACK_B  = "menu.banner.round.black.b";
        public const string KEY_BANNER_ROUND_PINK_ON  = "menu.banner.round.pink.on";
        public const string KEY_BANNER_ROUND_PINK_R   = "menu.banner.round.pink.r";
        public const string KEY_BANNER_ROUND_PINK_G   = "menu.banner.round.pink.g";
        public const string KEY_BANNER_ROUND_PINK_B   = "menu.banner.round.pink.b";
        public const string KEY_BANNER_ROUND_BLUE_ON  = "menu.banner.round.blue.on";
        public const string KEY_BANNER_ROUND_BLUE_R   = "menu.banner.round.blue.r";
        public const string KEY_BANNER_ROUND_BLUE_G   = "menu.banner.round.blue.g";
        public const string KEY_BANNER_ROUND_BLUE_B   = "menu.banner.round.blue.b";
        public const string KEY_BANNER_ROUND_WHITE_ON = "menu.banner.round.white.on";
        public const string KEY_BANNER_ROUND_WHITE_R  = "menu.banner.round.white.r";
        public const string KEY_BANNER_ROUND_WHITE_G  = "menu.banner.round.white.g";
        public const string KEY_BANNER_ROUND_WHITE_B  = "menu.banner.round.white.b";
        public const string KEY_BANNER_ROUND_HIGHLIGHT_ON = "menu.banner.round.highlight.on";
        public const string KEY_BANNER_ROUND_HIGHLIGHT_R  = "menu.banner.round.highlight.r";
        public const string KEY_BANNER_ROUND_HIGHLIGHT_G  = "menu.banner.round.highlight.g";
        public const string KEY_BANNER_ROUND_HIGHLIGHT_B  = "menu.banner.round.highlight.b";

        public const string KEY_BANNER_SQUAD_ENABLED     = "menu.banner.squad.enabled";

        // (bucket, key-prefix) describes one slot. the prefix is the settings key minus the trailing
        // .on/.r/.g/.b — e.g. "menu.banner.qual.cyan". highlight is matched by component, not hue.
        private struct BannerSlotKeys { public BannerBucket bucket; public string prefix; }

        private static readonly BannerSlotKeys[] QualSlots =
        {
            new BannerSlotKeys { bucket = BannerBucket.Cyan,  prefix = "menu.banner.qual.cyan"  },
            new BannerSlotKeys { bucket = BannerBucket.Pink,  prefix = "menu.banner.qual.pink"  },
            new BannerSlotKeys { bucket = BannerBucket.Black, prefix = "menu.banner.qual.black" },
            new BannerSlotKeys { bucket = BannerBucket.White, prefix = "menu.banner.qual.white" },
        };
        private static readonly BannerSlotKeys[] ElimSlots =
        {
            new BannerSlotKeys { bucket = BannerBucket.Cyan,  prefix = "menu.banner.elim.cyan"  },
            new BannerSlotKeys { bucket = BannerBucket.Pink,  prefix = "menu.banner.elim.pink"  },
            new BannerSlotKeys { bucket = BannerBucket.Black, prefix = "menu.banner.elim.black" },
            new BannerSlotKeys { bucket = BannerBucket.White, prefix = "menu.banner.elim.white" },
        };
        private static readonly BannerSlotKeys[] WinnerSlots =
        {
            new BannerSlotKeys { bucket = BannerBucket.Yellow, prefix = "menu.banner.win.yellow" },
            new BannerSlotKeys { bucket = BannerBucket.Orange, prefix = "menu.banner.win.orange" },
            new BannerSlotKeys { bucket = BannerBucket.White,  prefix = "menu.banner.win.white"  },
            new BannerSlotKeys { bucket = BannerBucket.BlackGrey, prefix = "menu.banner.win.black"  },
        };
        private static readonly BannerSlotKeys[] RoundOverSlots =
        {
            new BannerSlotKeys { bucket = BannerBucket.BlackGrey, prefix = "menu.banner.round.black" },
            new BannerSlotKeys { bucket = BannerBucket.Pink,  prefix = "menu.banner.round.pink"  },
            new BannerSlotKeys { bucket = BannerBucket.Cyan,  prefix = "menu.banner.round.blue"  },
            new BannerSlotKeys { bucket = BannerBucket.White, prefix = "menu.banner.round.white" },
        };
        // squad-elimination banner (EliminatedSquadScreenViewModel). broader palette than the solo
        // Eliminated one: orange + a black/grey split + pink/blue/yellow/white.
        private static readonly BannerSlotKeys[] SquadSlots =
        {
            new BannerSlotKeys { bucket = BannerBucket.Orange,    prefix = "menu.banner.squad.orange" },
            new BannerSlotKeys { bucket = BannerBucket.BlackGrey, prefix = "menu.banner.squad.black"  },
            new BannerSlotKeys { bucket = BannerBucket.Pink,      prefix = "menu.banner.squad.pink"   },
            new BannerSlotKeys { bucket = BannerBucket.Cyan,      prefix = "menu.banner.squad.blue"   },
            new BannerSlotKeys { bucket = BannerBucket.Yellow,    prefix = "menu.banner.squad.yellow" },
            new BannerSlotKeys { bucket = BannerBucket.White,     prefix = "menu.banner.squad.white"  },
        };

        public void ApplyQualifiedBannerColours(Component banner)  => ApplyBannerColours(banner, QualSlots,      "menu.banner.qual.highlight",  KEY_BANNER_QUAL_ENABLED);
        public void ApplyEliminatedBannerColours(Component banner) => ApplyBannerColours(banner, ElimSlots,      "menu.banner.elim.highlight",  KEY_BANNER_ELIM_ENABLED);
        public void ApplyWinnerBannerColours(Component banner)
        {
            ApplyBannerColours(banner, WinnerSlots, "menu.banner.win.highlight", KEY_BANNER_WIN_ENABLED);
            ApplyWinnerRoundOverWhiteOverride(banner);
        }
        public void ApplyRoundOverBannerColours(Component banner)  => ApplyBannerColours(banner, RoundOverSlots, "menu.banner.round.highlight", KEY_BANNER_ROUND_ENABLED);
        public void ApplySquadBannerColours(Component banner)      => ApplyBannerColours(banner, SquadSlots,     "menu.banner.squad.highlight", KEY_BANNER_SQUAD_ENABLED);

        // the Winner banner has a "round-over-white" image nested somewhere under it that the hue
        // matcher misses (its colour doesn't sit cleanly in the Yellow bucket). force-recolour it to
        // the Yellow replacement so the banner reads consistently when yellow customisation is on.
        private void ApplyWinnerRoundOverWhiteOverride(Component banner)
        {
            if (banner == null) return;
            if (SettingsService.Get(KEY_BANNER_WIN_ENABLED, "false") != "true") return;
            if (SettingsService.Get("menu.banner.win.yellow.on", "false") != "true") return;
            Color yellow = new Color(ParseF("menu.banner.win.yellow.r", 1f), ParseF("menu.banner.win.yellow.g", 0.85f), ParseF("menu.banner.win.yellow.b", 0f));
            foreach (var t in banner.transform.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.gameObject.name != "round-over-white") continue;
                var img = t.GetComponent<UnityEngine.UI.Image>();
                if (img != null) img.color = new Color(yellow.r, yellow.g, yellow.b, img.color.a);
            }
        }

        private void ApplyBannerColours(Component banner, BannerSlotKeys[] slotKeys, string highlightPrefix, string enabledKey)
        {
            if (banner == null) return;
            if (SettingsService.Get(enabledKey, "false") != "true") return;

            var slots = new System.Collections.Generic.List<BannerSlot>();
            foreach (var sk in slotKeys)
            {
                if (SettingsService.Get(sk.prefix + ".on", "false") != "true") continue;
                slots.Add(new BannerSlot
                {
                    bucket = sk.bucket,
                    target = new Color(ParseF(sk.prefix + ".r", 1f), ParseF(sk.prefix + ".g", 1f), ParseF(sk.prefix + ".b", 1f)),
                });
            }

            bool highlightOn = SettingsService.Get(highlightPrefix + ".on", "false") == "true";
            Color highlight = new Color(ParseF(highlightPrefix + ".r", 1f), ParseF(highlightPrefix + ".g", 1f), ParseF(highlightPrefix + ".b", 1f));

            ApplyBannerColours(banner, new BannerColours { slots = slots, highlightOn = highlightOn, highlight = highlight });
        }

        // banner colour replacement set + the HSV matcher, shared so the UI tab's live preview
        // recolours banners with the exact same rules as the real apply path. each channel is a
        // target colour + on flag; highlight is matched by component (ScrollUVs) not by colour.
        // the hue/value buckets a banner slot can map. each banner type exposes a different subset
        // (qual/elim: cyan/pink/black/white, winner: yellow/orange/white/black, roundover: black/pink/blue/white).
        public enum BannerBucket { Black, White, Cyan, Pink, Yellow, Orange, Blue, BlackGrey }

        public struct BannerSlot { public BannerBucket bucket; public Color target; }

        public struct BannerColours
        {
            public System.Collections.Generic.List<BannerSlot> slots;
            public bool highlightOn;
            public Color highlight;

            public bool AnyOn => highlightOn || (slots != null && slots.Count > 0);

            private static bool BucketMatches(BannerBucket b, float h, float s, float v)
            {
                switch (b)
                {
                    case BannerBucket.Black:  return v < 0.25f;
                    // dark + greys: low saturation up to (but not into) the white band, any value below it
                    case BannerBucket.BlackGrey: return s < 0.2f && v < 0.85f;
                    case BannerBucket.White:  return v > 0.85f && s < 0.15f;
                    case BannerBucket.Cyan:   return s > 0.3f && v > 0.3f && h >= 0.47f && h <= 0.58f;
                    case BannerBucket.Pink:   return s > 0.3f && v > 0.3f && (h >= 0.88f || h <= 0.05f);
                    case BannerBucket.Yellow: return s > 0.3f && v > 0.3f && h >= 0.13f && h <= 0.19f;
                    case BannerBucket.Orange: return s > 0.3f && v > 0.3f && h >= 0.05f && h <= 0.11f;
                    case BannerBucket.Blue:   return s > 0.3f && v > 0.3f && h >= 0.58f && h <= 0.72f;
                }
                return false;
            }

            public bool TryMatch(Color c, out Color target)
            {
                if (slots != null)
                {
                    Color.RGBToHSV(c, out float h, out float s, out float v);
                    for (int i = 0; i < slots.Count; i++)
                        if (BucketMatches(slots[i].bucket, h, s, v)) { target = slots[i].target; return true; }
                }
                target = default; return false;
            }

            public static bool IsHighlight(UnityEngine.UI.Image img) =>
                img.GetComponent<ScrollUVs>() != null || img.GetComponent<UI_ScrollUvs>() != null;
        }

        // colour-driven overload: same image/TMP recolour walk, but the caller hands us the
        // already-resolved replacement colours + on flags instead of settings keys. lets the UI
        // tab's live preview reuse the exact apply logic with unsaved slider values.
        public void ApplyBannerColours(Component banner, BannerColours set)
        {
            if (banner == null) return;
            var root = banner.transform;
            if (root == null) return;

            bool highlightOn = set.highlightOn;
            Color highlightTarget = set.highlight;
            if (!set.AnyOn) return;

            foreach (var img in root.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                if (img == null) continue;
                if (highlightOn && BannerColours.IsHighlight(img))
                {
                    img.color = new Color(highlightTarget.r, highlightTarget.g, highlightTarget.b, img.color.a);
                    continue;
                }
                if (set.TryMatch(img.color, out var t))
                    img.color = new Color(t.r, t.g, t.b, img.color.a);
            }

            foreach (var tmp in root.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                if (tmp == null) continue;
                if (set.TryMatch(tmp.color, out var tFill))
                    tmp.color = new Color(tFill.r, tFill.g, tFill.b, tmp.color.a);

                if (tmp.fontSharedMaterial == null) continue;
                var mat = tmp.fontMaterial;
                if (mat.HasProperty(TMPro.ShaderUtilities.ID_OutlineColor))
                {
                    var oc = mat.GetColor(TMPro.ShaderUtilities.ID_OutlineColor);
                    if (set.TryMatch(oc, out var tOut))
                        mat.SetColor(TMPro.ShaderUtilities.ID_OutlineColor, new Color(tOut.r, tOut.g, tOut.b, oc.a));
                }
                if (mat.HasProperty(TMPro.ShaderUtilities.ID_UnderlayColor))
                {
                    var uc = mat.GetColor(TMPro.ShaderUtilities.ID_UnderlayColor);
                    if (set.TryMatch(uc, out var tUn))
                        mat.SetColor(TMPro.ShaderUtilities.ID_UnderlayColor, new Color(tUn.r, tUn.g, tUn.b, uc.a));
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static string FindPrefabName(AssetBundle bundle)
        {
            foreach (var name in bundle.GetAllAssetNames())
                if (name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    return name;
            return null;
        }
    }
}
