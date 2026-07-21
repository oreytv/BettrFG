using System.Collections;
using BetterFG.Core;
using BetterFG.Network;
using BetterFG.Services;
using BetterFG.Utilities;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using FG.Common;
using FG.Common.Character;
using FG.Common.Character.MotorSystem;
using FGClient;
using HarmonyLib;
using UnityEngine;
using FGClient.UI.PrivateLobby;
using BetterFG.Customization.Menu;

namespace BetterFG.Nametag
{
    public static class NametagPatchHub
    {
        [HarmonyPatch(typeof(NameTagViewModel), "UpdateDisplay",
            new[] { typeof(string), typeof(string), typeof(CustomisationSelections) })]
        internal static class patch_UpdateDisplay_Full
        {
            [HarmonyPostfix]
            public static void Postfix(NameTagViewModel __instance, string playerKey)
                => HandleViewModel(__instance, playerKey);
        }

        [HarmonyPatch(typeof(NameTagViewModel), "UpdateDisplayWithLocalPlayer")]
        internal static class patch_UpdateDisplayWithLocalPlayer
        {
            [HarmonyPostfix]
            public static void Postfix(NameTagViewModel __instance)
                => HandleViewModel(__instance, null);
        }

        // private lobby nametags get reused as data changes, so re-run our handler off the
        // lobby vm's _nameTagVM whenever the lobby data updates.
        [HarmonyPatch(typeof(PrivateLobbyShowInfoViewModel), "RespondToDataChange")]
        internal static class patch_PrivateLobbyRespondToDataChange
        {
            [HarmonyPostfix]
            public static void Postfix(PrivateLobbyShowInfoViewModel __instance)
                => HandleViewModel(__instance?._nameTagVM, null);
        }

        // the ugc loading screen nameplate lives on CreatorIDViewModel._nametagViewModel. the text
        // isn't set yet on RequestComplete so we apply a beat later.
        [HarmonyPatch(typeof(FGClient.Fraggle.CreatorIDViewModel), "RequestComplete")]
        internal static class patch_CreatorIDRequestComplete
        {
            [HarmonyPostfix]
            public static void Postfix(FGClient.Fraggle.CreatorIDViewModel __instance)
            {
                var vm = __instance?._nametagViewModel;
                if (vm != null)
                    BeanMonitorService.Instance?.StartCoroutine(ApplyNextTick(vm).WrapToIl2Cpp());
            }
        }

        // loading screen nameplate text isn't set yet on RequestComplete, give it a beat. the game's
        // creator fill also clobbers our swapped round description, so re-assert that here too.
        private static IEnumerator ApplyNextTick(NameTagViewModel vm)
        {
            yield return new WaitForSeconds(0.1f);
            HandleViewModel(vm, null);
            BetterFG.Features.UnityRound.BetterFGUnityRounds.ApplyDescriptionNow();
        }

        // foreground recolor doesn't fire on first open of this screen, so kick it scoped to the
        // screen's own children once it opens. nothing to do with nametags - separate thing.
        [HarmonyPatch(typeof(PrivateLobbyScreenViewModel), "OnOpened")]
        internal static class patch_PrivateLobbyScreenOnOpened
        {
            [HarmonyPostfix]
            public static void Postfix(PrivateLobbyScreenViewModel __instance)
            {
                var root = __instance?.transform;
                if (root == null) return;
                BeanMonitorService.Instance?.StartCoroutine(
                    MenuCustomizationApplication.ReapplyForegroundFromSettingsCoroutine(root).WrapToIl2Cpp());
            }
        }

        [HarmonyPatch(typeof(PlayerInfoDisplayGameObject), "SetTextAlpha")]
        internal static class patch_SetTextAlpha
        {
            [HarmonyPrefix]
            public static bool Prefix(PlayerInfoDisplayGameObject __instance, float alpha)
            {
                if (__instance._prevTextAlpha == alpha) return true;
                NametagIconApplicator.SetIconAlphaForDisplay(__instance, alpha);
                return true;
            }
        }

        [HarmonyPatch(typeof(PlayerInfoDisplayCanvas), "SetTextAlpha")]
        internal static class patch_SetTextAlpha_Canvas
        {
            [HarmonyPrefix]
            public static bool Prefix(PlayerInfoDisplayCanvas __instance, float alpha)
            {
                NametagIconApplicator.SetIconAlphaForDisplay(__instance, alpha);
                return true;
            }
        }

        // a respawn teleport completing is the reliable "player is back" signal — the alpha->1 path only
        // fires cleanly when you manually unhide the nametag, so on a normal checkpoint respawn the crown
        // never got re-pinned. End() runs when the teleport finishes (our respawn button AND the game's own
        // checkpoint respawns), so re-assert the crown here. ApplyLocal re-pins now and next frame.
        [HarmonyPatch(typeof(MotorFunctionTeleportStateActive), nameof(MotorFunctionTeleportStateActive.End))]
        internal static class patch_TeleportEnd
        {
            [HarmonyPostfix]
            public static void Postfix()
            {
                // just re-pin — the badge's rank/text/recolour already survived the respawn, only its
                // position got stomped by the re-layout. no need to re-run the full apply here.
                if (CrownRankService.Enabled) CrownRankService.RepinCrown();
            }
        }

        // the game re-centres the name+crown pair on its own schedule (respawn, recolour, any VM refresh), and
        // that's the only place the crown's side is actually decided. racing it from a coroutine was random —
        // so enforce our crown-side swap right here, immediately after every centre, deterministically.
        [HarmonyPatch(typeof(CrownRankPlayerTagLayoutHelper), nameof(CrownRankPlayerTagLayoutHelper.CenterNameAndCrownRank))]
        internal static class patch_CenterNameAndCrownRank
        {
            [HarmonyPostfix]
            public static void Postfix(CrownRankPlayerTagLayoutHelper __instance)
            {
                if (CrownRankService.Enabled) CrownRankService.EnforceCrownSide(__instance);
            }
        }

        // a name change routes through CenterName (name-only re-centre), NOT CenterNameAndCrownRank — so our
        // swap-enforcing postfix above never fired on a name edit and the crown snapped back to the game's
        // default side. same helper, same enforcement, so hook this too.
        [HarmonyPatch(typeof(CrownRankPlayerTagLayoutHelper), nameof(CrownRankPlayerTagLayoutHelper.CenterName))]
        internal static class patch_CenterName
        {
            [HarmonyPostfix]
            public static void Postfix(CrownRankPlayerTagLayoutHelper __instance)
            {
                if (CrownRankService.Enabled) CrownRankService.EnforceCrownSide(__instance);
            }
        }

        // a new in-game nametag just spawned. this fires for EVERY nametag the game creates, whenever it
        // creates it (local, remote, late joiners) — so it's the one place we need to (re)apply the full
        // nametag and never miss a creation site, no polling. a bean that spawns AFTER our round-start
        // nametag pass would otherwise get a fresh default tag that nothing re-styles — this is the fix for
        // "the custom nametag isn't applied to beans that spawn later". so we run the complete refresh
        // (style + profile + icons + font) over every current row, not just the font, and NOT gated on
        // font replacement being on — nametag styling is independent of the font feature.
        //
        // the spawned row's VM/text often isn't fully populated on the same frame as SpawnPlayerTag, so we
        // refresh now AND a couple beats later to catch the late-bound state (same reason WaitForNametag
        // re-applies the local tag several times).
        [HarmonyPatch(typeof(PlayerInfoHUDBase), "SpawnPlayerTag")]
        internal static class patch_SpawnPlayerTag
        {
            [HarmonyPostfix]
            public static void Postfix(PlayerInfoHUDBase __instance)
            {
                // a spectate switch respawns EVERY row at once, so this fires in a burst. running a
                // whole-scene RefreshRemoteNametags per tag (each a FindObjectsOfType<NameTagViewModel>
                // scan) is what froze the game on every switch. collapse the burst into one queued sweep.
                if (__instance == null || _refreshQueued) return;
                var host = BeanMonitorService.Instance;
                if (host == null) { RefreshRemoteNametags(); return; }
                _refreshQueued = true;
                host.StartCoroutine(CoalescedRefresh().WrapToIl2Cpp());
            }
        }

        private static bool _refreshQueued;

        private static IEnumerator CoalescedRefresh()
        {
            yield return null; // let the rest of this frame's spawn burst land first
            RefreshRemoteNametags();
            yield return new WaitForSeconds(0.5f); // second pass catches late-bound VM text
            RefreshRemoteNametags();
            _refreshQueued = false;
        }

        // the game just set fame/famepass (gold) visuals on this nametag — i.e. it assigned the gold
        // EndFamePass material bound to the ORIGINAL atlas. apply our font NOW, right after, so the font
        // re-derives that gold material onto our atlas. this is the exact moment the gold material lands,
        // so famepass nametags get the custom font on the FIRST round load, no waiting for a later patch.
        [HarmonyPatch(typeof(PlayerInfoDisplayCanvas), nameof(PlayerInfoDisplayCanvas.SetNameVisualsDependingOnFame))]
        internal static class patch_SetNameVisualsFame
        {
            [HarmonyPostfix]
            public static void Postfix(PlayerInfoDisplayCanvas __instance)
            {
                if (!FontReplacementService.MasterOn || __instance == null) return;
                try
                {
                    var tmp = NametagIconApplicator.TryGetNameText(__instance);
                    if (tmp != null) FontReplacementService.ApplyToNametag(tmp);
                }
                catch (System.Exception ex) { Plugin.Log.LogWarning("BFGFont: SetNameVisualsFame: " + ex.Message); }
            }
        }



        internal static void HandleViewModel(NameTagViewModel vm, string playerKey)
        {
            if (vm == null) return;

            var txt = vm._playerNameText;
            if (txt != null && txt.fontMaterial != null)
                txt.fontMaterial.renderQueue = 4000;

            string key = !string.IsNullOrEmpty(playerKey) ? playerKey : vm._playerName;

            string localName = LocalPlayerInfo.FGlocalplayerusername;
            bool isLocal = vm._enableWithTheLocalPlayerData || (!string.IsNullOrEmpty(localName) && key == localName);

            if (isLocal)
                ApplyLocal(vm);
            else
                ApplyRemote(vm, key);

            // font replacement is independent of nametag profiles: ApplyLocal/ApplyRemote bail early for
            // players with no BetterFG nametag (empty key, no profile), so they'd never get the custom
            // font. apply it here at the single chokepoint every nametag VM passes through, so EVERY
            // in-game nametag (local + remote, profile or not) gets the font with its outline intact.
            if (txt != null)
                FontReplacementService.ApplyToNametag(txt);
        }

        public static void RefreshRemoteNametags()
        {
            try
            {
                var vms = Object.FindObjectsOfType<NameTagViewModel>(true);
                if (vms != null)
                    for (int i = 0; i < vms.Length; i++)
                        HandleViewModel(vms[i], null);

                // the local tag resolves the in-round HUD mode-agnostically (CAMERAS root + LevelCameras_
                // prefix, or the canvas PB_InfoHUD), so walk up from it to the HUD that holds every bean row
                // instead of re-hardcoding paths that only matched the UGC editor rig.
                var hudBase = NametagFinder.FindLocalNameTagSprite()?.GetComponentInParent<PlayerInfoHUDBase>();
                var spawned = hudBase?._spawnedInfoObjects;
                if (spawned == null) return;

                for (int i = 0; i < spawned.Count; i++)
                {
                    var row = spawned[i];
                    var display = row?.playerInfo;
                    if (display == null) continue;

                    string key = row.fgcc != null ? BeanNetworkUtil.TryGetPlayerKeyForBean(row.fgcc.gameObject) : "";
                    var profile = RemoteProfileStore.TryGet(key);
                    var tmp = NametagIconApplicator.TryGetNameText(display);
                    if (profile?.nametag == null && tmp != null)
                    {
                        profile = RemoteProfileStore.TryGet(tmp.text);
                        if (profile?.nametag == null)
                            profile = RemoteProfileStore.TryGet(System.Text.RegularExpressions.Regex.Replace(tmp.text ?? "", "<[^>]*>", "").Trim());
                    }
                    // font replacement is independent of profiles — a profile-less player still gets the
                    // custom font. apply it here too so HUD rows that never hit a NameTagViewModel (3D
                    // world tags) aren't left on the game font.
                    if (profile?.nametag == null)
                    {
                        if (tmp != null) FontReplacementService.ApplyToNametag(tmp);
                        continue;
                    }

                    var info = profile.nametag;
                    NametagIconApplicator.ApplyRemoteToDisplay(display, tmp != null ? tmp.text : "", info);
                    NametagIconApplicator.ApplyBacking(display.transform, !string.IsNullOrEmpty(info.backingPath), info.backingPath,
                        info.backingOffX, info.backingOffY, info.backingScale <= 0f ? 1f : info.backingScale);
                    NametagIconApplicator.ApplyNickname(display.transform, party: false, !string.IsNullOrEmpty(info.nickname), info.nickname);
                    if (info.platformHide == "true" || !string.IsNullOrEmpty(info.platformCustom))
                        NametagIconApplicator.ApplyPlatformIcon(display.gameObject, info.platformHide == "true", info.platformCustom ?? "");
                }
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("NametagPatchHub: refresh " + ex.Message);
            }
        }

        private static void ApplyLocal(NameTagViewModel vm)
        {
            var txt = vm._playerNameText;
            if (txt == null) return;

            // first menu entry: the NameTagViewModel postfix fires before OnMainMenuEntered sets
            // FGlocalplayerusername, so the styled nametag landed with an empty name and the game's
            // own UpdateDisplay then overwrote our styling with the real name. fetch on demand.
            if (string.IsNullOrEmpty(LocalPlayerInfo.FGlocalplayerusername))
            {
                try
                {
                    var fetched = FGClient.GlobalGameStateClient.Instance?.GetLocalPlayerName();
                    if (!string.IsNullOrEmpty(fetched)) LocalPlayerInfo.FGlocalplayerusername = fetched;
                }
                catch { }
            }

            // even when the full nametag feature is off, honour a custom name on its own — it's
            // the only useful effect of CustomName outside the 3D nametag system.
            bool useDisplay = SettingsService.Get("nametag.enabled", "false") == "true"
                              || !string.IsNullOrEmpty(LocalPlayerInfo.CustomName);
            string name = useDisplay ? LocalPlayerInfo.DisplayName : LocalPlayerInfo.FGlocalplayerusername;
            NametagIconApplicator.ApplyToNameplate(txt, name, NametagIconApplicator.NameplateType.Regular);
            NametagIconApplicator.ApplyLocalBacking(vm.transform);
            NametagIconApplicator.ApplyLocalNickname(vm.transform, party: false);
        }

        private static void ApplyRemote(NameTagViewModel vm, string playerKey)
        {
            if (string.IsNullOrEmpty(playerKey)) return;
            var txt = vm._playerNameText;
            if (txt == null) return;

            // _playerName in the private lobby carries rich-text/decoration the store isn't keyed on,
            // so try the raw value, then a stripped version, then whatever the actual TMP shows.
            string stripped = System.Text.RegularExpressions.Regex.Replace(playerKey, "<[^>]*>", "").Trim();
            string strippedTxt = System.Text.RegularExpressions.Regex.Replace(txt.text ?? "", "<[^>]*>", "").Trim();
            var profile = RemoteProfileStore.TryGet(playerKey)
                       ?? RemoteProfileStore.TryGet(stripped)
                       ?? RemoteProfileStore.TryGet(strippedTxt);

#if PROFILES
            // the private lobby never feeds RemoteProfileStore (only the in-round pipeline does) — the
            // lobby profiles live in ProfileService, so fall back to those by clean name.
            if (profile?.nametag == null)
            {
                NetworkClient.PrimeProfilesForLobby();
                profile = BetterFG.Customization.Profiles.ProfileService.GetRemoteProfileForName(stripped)
                       ?? BetterFG.Customization.Profiles.ProfileService.GetRemoteProfileForName(strippedTxt);
            }
#endif
            if (profile?.nametag == null) { NametagIconApplicator.RevertRemote(vm); return; }

            NametagIconApplicator.ApplyRemoteToNameplate(txt, vm._playerName, profile.nametag);

            var info = profile.nametag;
            NametagIconApplicator.ApplyBacking(vm.transform, !string.IsNullOrEmpty(info.backingPath), info.backingPath,
                info.backingOffX, info.backingOffY, info.backingScale <= 0f ? 1f : info.backingScale);
            NametagIconApplicator.ApplyNickname(vm.transform, party: false, !string.IsNullOrEmpty(info.nickname), info.nickname);

            bool hide = info.platformHide == "true";
            string customSprite = info.platformCustom ?? "";

            if (!hide && string.IsNullOrEmpty(customSprite)) return;

            BeanMonitorService.Instance?.StartCoroutine(PollAndApplyPlatformIcon(vm, info).WrapToIl2Cpp());
        }

        private static IEnumerator PollAndApplyPlatformIcon(NameTagViewModel vm, RemoteNametagInfo info)
        {
            float elapsed = 0f;
            while (elapsed < 5f)
            {
                if (TryApplyPlatformIcon(vm, info))
                    yield break;

                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
            }
            Plugin.Log.LogWarning($"NametagPatchHub: timed out waiting for HUD row for '{vm._playerName}'");
        }

        private static bool TryApplyPlatformIcon(NameTagViewModel vm, RemoteNametagInfo info)
        {
            bool hide = info.platformHide == "true";
            string customSprite = info.platformCustom ?? "";

            var huds = Object.FindObjectsOfType<PlayerInfoHUDBase>(true);
            if (huds == null) return false;

            for (int h = 0; h < huds.Length; h++)
            {
                var spawned = huds[h]?._spawnedInfoObjects;
                if (spawned == null) continue;

                for (int i = 0; i < spawned.Count; i++)
                {
                    var row = spawned[i];
                    if (row == null) continue;

                    if (!NametagIconApplicator.MatchesText(row.playerInfo, vm._playerNameText)) continue;
                    NametagIconApplicator.ApplyPlatformIcon(row.playerInfo?.gameObject, hide, customSprite);
                    return true;
                }
            }

            return false;
        }
    }
}
