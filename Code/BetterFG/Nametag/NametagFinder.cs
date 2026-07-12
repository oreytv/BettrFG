using BetterFG.Core;
using FGClient;
using PartyMenu;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterFG.Nametag
{
    public static class NametagFinder
    {
        private const string NAME_AND_CROWN = "NameAndCrownParent";
        private const string NAME_TEXT_PATH = "NameAndCrownParent/NameText";
        private const string NAME_WRAP_PATH = "NameAndCrownParent/BetterFG_NameWrapper/NameText";

        private static readonly string[] NAMETAG_NAMES =
        {
            "LocalPlayerNameTagSprite(Clone)",
            "LocalPlayerNameTag(Clone)",
        };

        // the local nametag lives under one of two HUD parents — resolve by direct path, NO FindObjectsOfType
        // (those scan the whole scene through interop and were the respawn hitch):
        //   3D:     <CAMERAS root>/LevelCameras_*/Main Camera Brain/PlayerInfoHUD/Parent
        //   canvas: UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/PersistentUnderlayUI/PB_InfoHUD/Parent
        // both parents can exist at once, so we don't pick "a parent" and stop — we look for the actual local
        // nametag child under each and return the first that has one (3D is the usual in-game surface).
        private const string CANVAS_HUD_PARENT_PATH =
            "UICanvas_Client_V2(Clone)/Default/InGameUiManager(Clone)/PersistentUnderlayUI/PB_InfoHUD/Parent";
        private const string CAMERAS_HUD_SUBPATH = "Main Camera Brain/PlayerInfoHUD/Parent";

        public static Transform FindLocalNameTagSprite()
        {
            // 3D HUD first. the CAMERAS root's leading dash count varies across scenes, and the camera rig child
            // is named per-mode (LevelCameras_LevelEditor in UGC editor, a different suffix in normal rounds) —
            // hardcoding LevelCameras_LevelEditor is why the tag only showed in editor rounds. match the root by
            // "CAMERAS", its child by "LevelCameras_" prefix, then walk the fixed sub-path. iterating roots is cheap.
            int sceneCount = SceneManager.sceneCount;
            for (int s = 0; s < sceneCount; s++)
            {
                var scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null || root.name.IndexOf("CAMERAS", System.StringComparison.Ordinal) < 0) continue;
                    var rt = root.transform;
                    for (int c = 0; c < rt.childCount; c++)
                    {
                        var rig = rt.GetChild(c);
                        if (!rig.name.StartsWith("LevelCameras_", System.StringComparison.Ordinal)) continue;
                        var parent = rig.Find(CAMERAS_HUD_SUBPATH);
                        if (parent == null) continue;
                        foreach (var name in NAMETAG_NAMES)
                        {
                            var t = parent.Find(name);
                            if (t != null) return t;
                        }
                    }
                }
            }

            // canvas HUD (PB_InfoHUD): fixed absolute path, GameObject.Find is a direct walk (no scan)
            var canvas = GameObject.Find(CANVAS_HUD_PARENT_PATH);
            if (canvas != null)
                foreach (var name in NAMETAG_NAMES)
                {
                    var t = canvas.transform.Find(name);
                    if (t != null) return t;
                }

            return null;
        }

        public static PlayerInfoDisplay FindLocalDisplay()
            => FindLocalNameTagSprite()?.GetComponent<PlayerInfoDisplay>();

        public static Transform FindNameAndCrownParent()
        {
            var tag = FindLocalNameTagSprite();
            return tag != null ? tag.Find(NAME_AND_CROWN) : null;
        }

        public static TMPro.TextMeshPro FindLocalNameText()
            => FindLocalNameTextAny()?.TryCast<TMPro.TextMeshPro>();

        public static TMP_Text FindLocalNameTextAny()
        {
            var tag = FindLocalNameTagSprite();
            if (tag == null) return null;

            var goDisplay = tag.GetComponent<PlayerInfoDisplayGameObject>();
            if (goDisplay != null && goDisplay._text != null) return goDisplay._text;

            var canvasDisplay = tag.GetComponent<PlayerInfoDisplayCanvas>();
            if (canvasDisplay != null && canvasDisplay._text != null) return canvasDisplay._text;

            var go = tag.Find(NAME_TEXT_PATH);
            if (go != null)
            {
                var tmp = go.GetComponent<TMP_Text>()
                       ?? go.GetComponentInChildren<TMP_Text>(true);
                if (tmp != null) return tmp;
            }

            var wrapped = tag.Find(NAME_WRAP_PATH);
            if (wrapped != null)
            {
                var tmp = wrapped.GetComponent<TMP_Text>()
                       ?? wrapped.GetComponentInChildren<TMP_Text>(true);
                if (tmp != null) return tmp;
            }

            var any = tag.GetComponentInChildren<TMP_Text>(true);
            if (any != null) return any;

            Debug.LogWarning($"[NametagFinder] NameText not found under '{tag.name}'");
            return null;
        }

        // ── Public reapply ────────────────────────────────────────────────────

        public static void ReapplyAllNameplates()
        {
            var vms = UnityEngine.Object.FindObjectsOfType<NameTagViewModel>(true);
            if (vms != null)
                for (int i = 0; i < vms.Length; i++)
                    NametagPatchHub.HandleViewModel(vms[i], null);

            var partyVms = UnityEngine.Object.FindObjectsOfType<ExpandedMemberItemViewModel>(true);
            if (partyVms != null)
                for (int i = 0; i < partyVms.Length; i++)
                    ApplyToExpandedMember(partyVms[i]);
        }

        // scoped variant for view switches — only the rebuilt view's nameplates, not a whole-scene
        // FindObjectsOfType. anything outside this view kept its nameplate through the switch.
        public static void ReapplyNameplatesInScope(UnityEngine.Transform scope)
        {
            if (scope == null) return;
            foreach (var vm in scope.GetComponentsInChildren<NameTagViewModel>(true))
                NametagPatchHub.HandleViewModel(vm, null);
            foreach (var vm in scope.GetComponentsInChildren<ExpandedMemberItemViewModel>(true))
                ApplyToExpandedMember(vm);
        }

        // ── Nameplate helpers ─────────────────────────────────────────────────

        internal static void ApplyToExpandedMember(ExpandedMemberItemViewModel vm)
        {
            if (vm == null) return;
            var tmp = vm._nameplate?.Username;
            if (tmp == null) return;

            string memberName = vm._item?.Username ?? "";
            string realName = LocalPlayerInfo.FGlocalplayerusername;

            // local player — use live local settings
            if (!string.IsNullOrEmpty(realName) && memberName == realName)
            {
                bool useDisplay = BetterFG.Services.SettingsService.Get("nametag.enabled", "false") == "true"
                                  || !string.IsNullOrEmpty(LocalPlayerInfo.CustomName);
                string name = useDisplay ? LocalPlayerInfo.DisplayName : realName;
                NametagIconApplicator.ApplyToNameplate(tmp, name, NametagIconApplicator.NameplateType.Party);
                NametagIconApplicator.ApplyLocalPartyBacking(vm.transform);
                NametagIconApplicator.ApplyLocalNickname(vm.transform, party: true);
                return;
            }

#if PROFILES
            // remote member — match to an enabled profile by clean name and apply its nametag
            BetterFG.Network.NetworkClient.PrimeProfilesForLobby();
            string clean = FallGuysLib.Players.PlayerUtils.CleanPlayerName(memberName);
            var rp = BetterFG.Customization.Profiles.ProfileService.GetRemoteProfileForName(clean);
            var nt = rp?.nametag;
            if (nt == null) return;

            NametagIconApplicator.ApplyRemoteToNameplate(tmp, memberName, nt);
            NametagIconApplicator.ApplyPartyBacking(vm.transform, nt.backingEnabled, nt.backingPath, nt.backingOffX, nt.backingOffY, nt.backingScale);
            NametagIconApplicator.ApplyNickname(vm.transform, party: true, enabled: !string.IsNullOrEmpty(nt.nickname), text: nt.nickname ?? "");
#endif
        }

        // ── Patches ───────────────────────────────────────────────────────────
        // NOTE: NameTagViewModel patches are in NametagPatchHub — don't add them here too

        [HarmonyLib.HarmonyPatch(typeof(PartyMenuMemberItem), "UpdateGraphics")]
        internal static class patch_PartyMember_UpdateGraphics
        {
            [HarmonyLib.HarmonyPostfix]
            public static void Postfix(PartyMenuMemberItem __instance) => ApplyToExpandedMember(__instance._expandedModel);
        }

        [HarmonyLib.HarmonyPatch(typeof(PartyMenuMemberItem), "Select", new[] { typeof(bool) })]
        internal static class patch_PartyMember_Select
        {
            [HarmonyLib.HarmonyPostfix]
            public static void Postfix(PartyMenuMemberItem __instance) => ApplyToExpandedMember(__instance._expandedModel);
        }
    }
}
