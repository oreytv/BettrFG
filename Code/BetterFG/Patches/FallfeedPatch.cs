using System;
using BetterFG.Core;
using BetterFG.Features.MorePlatformIcon;
using BetterFG.Nametag;
using BetterFG.Network;
using BetterFG.Services;
using BetterFG.Tweaks;
using FGClient.FallFeed;
using FGClient;
using HarmonyLib;

namespace BetterFG.Patches
{
    [HarmonyPatch(typeof(FallFeedNotificationViewModel), "PlayAppearTween")]
    internal static class FallFeedNamePatch
    {
        [HarmonyPrefix]
        public static void Prefix(FallFeedNotificationViewModel __instance)
        {
            var sst = StripSizeTagsTweak.Instance;
            if (sst != null && sst.IsEnabled)
            {
                var names2 = __instance._playerNames;
                if (names2 != null)
                    for (int i = 0; i < names2.Length; i++)
                        names2[i] = StripSizeTagsTweak.Strip(names2[i]);
            }
        }

        [HarmonyPostfix]
        public static void Postfix(FallFeedNotificationViewModel __instance)
        {
            try
            {
                var msgData = __instance._messageData;
                var keys = msgData?.PlayerKeys;
                var names = __instance._playerNames;


                if (keys == null || names == null || keys.Length == 0 || names.Length != keys.Length)
                    return;

                string localKey = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? "";
                string localName = LocalPlayerInfo.FGlocalplayerusername ?? "";
                string localCustomName = LocalPlayerInfo.CustomName ?? "";

                bool changed = false;
                for (int i = 0; i < keys.Length; i++)
                {
                    string fullKey = keys[i] ?? "";
                    string original = names[i] ?? "";
                    string next = original;


                    var profile = RemoteProfileStore.TryGet(fullKey);
                    var info = profile?.nametag;

                    bool hasProfile = profile != null;
                    bool hasInfo = info != null;
                    bool hasCustomName = hasInfo && !string.IsNullOrEmpty(info.customName);
                    bool hasStyle = hasInfo && HasNameStyle(info);
                    bool isLocal = IsLocalKey(fullKey);


                    if (hasInfo && (hasCustomName || hasStyle))
                    {
                        string display = hasCustomName
                            ? info.customName
                            : FallGuysLib.Players.PlayerUtils.CleanPlayerName(fullKey);

                        next = NametagIconApplicator.BuildInlineStyledName(
                            next,
                            display,
                            info.r, info.g, info.b,
                            info.bold, info.italic,
                            info.platformCustom ?? "",
                            info.platformHide == "true",
                            "",
                            fullKey,
                            0.6f);

                    }
                    else if (isLocal && !string.IsNullOrEmpty(localCustomName))
                    {
                        var ci = System.Globalization.CultureInfo.InvariantCulture;
                        float P(string key, string def) =>
                            float.TryParse(SettingsService.Get(key, def), System.Globalization.NumberStyles.Float, ci, out var v) ? v : float.Parse(def, ci);

                        float r = P("nametag.color.r", "1"), g = P("nametag.color.g", "1"), b = P("nametag.color.b", "1");
                        bool bold = SettingsService.Get("nametag.bold", "false") == "true";
                        bool italic = SettingsService.Get("nametag.italic", "false") == "true";
                        string displayName = LocalPlayerInfo.DisplayName;


                        next = NametagIconApplicator.BuildInlineStyledName(
                            next,
                            displayName,
                            r, g, b,
                            bold, italic,
                            "",
                            false,
                            "",
                            fullKey,
                            0.6f);

                    }
                    else
                    {
                        string platformSprite = FeatureMorePlatformIcon.SpriteNameForPlayerKey(fullKey);

                        if (!string.IsNullOrEmpty(platformSprite))
                        {
                            next = NametagIconApplicator.BuildInlinePlatformName(next, fullKey, false, "", 0.6f);
                        }
                        else
                        {
                        }
                    }

                    if (next == original)
                    {
                        continue;
                    }

                    names[i] = next;
                    changed = true;
                }

                if (!changed)
                {
                    return;
                }

                NametagIconApplicator.ApplyInlinePlatformAssetOutline(__instance._text);
                __instance.BuildMessageWithPlayerNames();
                NametagIconApplicator.ApplyInlinePlatformAssetOutline(__instance._text);
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("Fallfeedpatch.cs" + ex.Message);
            }

            FallFeedQualTimeTweak.Instance?.Apply(__instance);
        }

        private static bool HasNameStyle(remoteNametagInfo info)
        {
            if (info == null) return false;
            return info.bold || info.italic || !string.IsNullOrEmpty(info.nameStyle) ||
                   Math.Abs(info.r - 1f) > 0.001f ||
                   Math.Abs(info.g - 1f) > 0.001f ||
                   Math.Abs(info.b - 1f) > 0.001f;
        }

        private static bool IsLocalKey(string fullKey)
        {
            string localKey = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? "";
            if (!string.IsNullOrEmpty(localKey) && fullKey.Equals(localKey, StringComparison.OrdinalIgnoreCase))
                return true;

            // Fall back to matching by cleaned name. Short names (e.g. "e") are safe to
            // match only when the local key's own cleaned name is equally short — confirming
            // the local player actually has that name and it's not a substring false positive.
            string localName = LocalPlayerInfo.FGlocalplayerusername;
            if (string.IsNullOrEmpty(localName)) return false;
            string cleanedKey = FallGuysLib.Players.PlayerUtils.CleanPlayerName(fullKey);
            if (!cleanedKey.Equals(localName, StringComparison.OrdinalIgnoreCase)) return false;
            if (localName.Length < 4)
            {
                string cleanedLocalKey = FallGuysLib.Players.PlayerUtils.CleanPlayerName(localKey);
                if (!cleanedLocalKey.Equals(localName, StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }
    }
}
