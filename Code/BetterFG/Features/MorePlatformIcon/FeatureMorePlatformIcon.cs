using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using BetterFG.Core;
using BetterFG.Customization.Menu;
using BetterFG.Nametag;
using BetterFG.Utilities;
using FGClient;
using FGClient.UI.PrivateLobby;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace BetterFG.Features.MorePlatformIcon
{
    internal static class FeatureMorePlatformIcon
    {
        public static readonly bfgfeature feature = new bfgfeature("moreplatformicon", "More platform icons", true, new List<featuresetting>
        {
            new featuresetting { id = "privatelobby", label = "Private lobby player list", defaultOn = true },
        }, onClosed: NametagIconApplicator.RestoreKnownPlatformIcons);

        const string Res = "BetterFG.assets.ui.feature.moreplatformicon.featuremoreplatformicon_platformicons.png";
        const string ResOutline = "BetterFG.assets.ui.feature.moreplatformicon.featuremoreplatformicon_platformicons_outline.png";
        const int Cell = 134;
        const float IconScale = 1.8f;
        const float Drop = 0.2f;

        static TMP_SpriteAsset _asset;
        static TMP_SpriteAsset _assetOutline;
        static Sprite[] _sprites;
        static Sprite[] _spritesOutline;
        static PrivateLobbyPlayerListViewModel _queuedVm;
        static bool _queued;
        static readonly string[] _names =
        {
            "pc_egs", "pc_steam", "xb1", "xsx",
            "xbs", "ps4", "ps5", "android_ega",
            "ios_ega", "switch", "win", "linux"
        };

        public static bool On(string setting) => featureRegistry.IsOn("moreplatformicon", setting);
        public static bool Enabled => feature.enabled;
        public static string[] PlatformIconIds() => (string[])_names.Clone();

        public static Sprite SpriteForName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            SpriteAsset();
            if (_sprites == null) return null;

            for (int i = 0; i < _names.Length; i++)
                if (string.Equals(_names[i], name, StringComparison.OrdinalIgnoreCase))
                    return i < _sprites.Length ? _sprites[i] : null;

            return null;
        }

        public static string SpriteNameForPlayerKey(string playerKey)
        {
            if (!Enabled) return "";
            if (string.IsNullOrEmpty(playerKey)) return "";

            for (int i = 0; i < _names.Length; i++)
            {
                string n = _names[i];
                if (playerKey.StartsWith(n + "_", StringComparison.OrdinalIgnoreCase))
                    return n;
            }

            return "";
        }

        public static Sprite SpriteForPlayerKey(string playerKey)
        {
            if (!Enabled) return null;
            string name = SpriteNameForPlayerKey(playerKey);
            if (string.IsNullOrEmpty(name)) return null;

            SpriteAsset();
            if (_sprites == null) return null;

            for (int i = 0; i < _names.Length; i++)
                if (_names[i] == name)
                    return i < _sprites.Length ? _sprites[i] : null;

            return null;
        }

        public static TMP_SpriteAsset SpriteAsset()
        {
            if (_asset != null) return _asset;
            _asset = BuildAsset(Res, "BetterFGMorePlatformIcons", "betterfg_moreplatformicons", ref _sprites);
            return _asset;
        }

        public static TMP_SpriteAsset SpriteAssetOutline()
        {
            if (_assetOutline != null) return _assetOutline;
            _assetOutline = BuildAsset(ResOutline, "BetterFGMorePlatformIconsOutline", "betterfg_moreplatformicons_outline", ref _spritesOutline);
            return _assetOutline;
        }

        static TMP_SpriteAsset BuildAsset(string res, string assetName, string texName, ref Sprite[] spriteStore)
        {
            var tex = EmbeddedResourceandUnity.LoadTexture(res);
            if (tex == null) return null;

            tex.name = texName;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            asset.name = assetName;
            asset.version = "1.1.0";
            asset.spriteSheet = tex;
            asset.hashCode = TMP_TextUtilities.GetSimpleHashCode(asset.name);

            var mat = new Material(Shader.Find("TextMeshPro/Sprite"));
            mat.name = assetName + "_Mat";
            mat.mainTexture = tex;
            asset.material = mat;
            asset.materialHashCode = TMP_TextUtilities.GetSimpleHashCode(mat.name);

            var glyphs = new Il2CppSystem.Collections.Generic.List<TMP_SpriteGlyph>();
            var chars = new Il2CppSystem.Collections.Generic.List<TMP_SpriteCharacter>();
            spriteStore = new Sprite[_names.Length];
            asset.spriteInfoList = new Il2CppSystem.Collections.Generic.List<TMP_Sprite>();
            asset.fallbackSpriteAssets = new Il2CppSystem.Collections.Generic.List<TMP_SpriteAsset>();

            for (int i = 0; i < _names.Length; i++)
            {
                int col = i % 4;
                int row = i / 4;
                int x = col * Cell;
                int y = tex.height - ((row + 1) * Cell);

                var rect = new Rect(x, y, Cell, Cell);
                var sprite = Sprite.Create(tex, rect, new Vector2(0.5f, 0.5f), Cell);
                sprite.name = _names[i];
                sprite = Sprite.Create(tex, rect, new Vector2(0.7f, 0.7f), Cell * 2f);
                sprite.name = _names[i];
                spriteStore[i] = sprite;

                float bearingY = Cell * (1f - Drop);
                uint unicode = (uint)(0xE700 + i);
                var glyph = new TMP_SpriteGlyph(
                    (uint)i,
                    new GlyphMetrics(Cell, Cell, 0f, bearingY, Cell),
                    new GlyphRect(x, y, Cell, Cell),
                    IconScale,
                    0,
                    sprite
                );
                glyphs.Add(glyph);

                var ch = new TMP_SpriteCharacter(unicode, glyph);
                ch.name = _names[i];
                ch.glyphIndex = (uint)i;
                chars.Add(ch);
            }

            asset.spriteGlyphTable = glyphs;
            asset.spriteCharacterTable = chars;
            asset.UpdateLookupTables();
            return asset;
        }

        public static void QueuePrivateLobbyApply(PrivateLobbyPlayerListViewModel vm)
        {
            var app = MenuCustomizationApplication.Instance;
            if (vm == null || app == null) return;

            _queuedVm = vm;
            if (_queued) return;

            _queued = true;
            app.StartCoroutine(ApplyPrivateLobbyAfterRefresh().WrapToIl2Cpp());
        }

        public static IEnumerator ApplyPrivateLobbyAfterOpen(PrivateLobbyPlayerListViewModel vm)
        {
            yield return null;
            ApplyPrivateLobby(vm);
            yield return new WaitForSeconds(0.15f);
            ApplyPrivateLobby(vm);
        }

        static IEnumerator ApplyPrivateLobbyAfterRefresh()
        {
            yield return null;
            yield return null;
            ApplyPrivateLobby(_queuedVm);
            yield return new WaitForSeconds(0.05f);
            ApplyPrivateLobby(_queuedVm);
            yield return new WaitForSeconds(0.2f);
            ApplyPrivateLobby(_queuedVm);
            yield return new WaitForSeconds(0.5f);
            ApplyPrivateLobby(_queuedVm);
            _queued = false;
        }

        public static void ApplyPrivateLobby(PrivateLobbyPlayerListViewModel vm)
        {
            try
            {
                if (vm == null || vm.PlayerList == null || vm._cachedPlayersGameObjectList == null) return;
                string localKey = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? "";

                int count = Math.Min(vm.PlayerList.Count, vm._cachedPlayersGameObjectList.Count);
                for (int i = 0; i < count; i++)
                {
                    var data = vm.PlayerList[i];
                    var row = vm._cachedPlayersGameObjectList[i];
                    if (data == null || row == null) continue;

                    ApplyPrivateLobbyPlatform(row.transform, data.PlayerKey);
                    ApplyPrivateLobbyCustomName(row.transform, data.PlayerKey);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PrivateLobbyName] list " + ex.Message);
            }
        }

        // scene-based driver (foreground timing): each row's name is what it currently shows, so match
        // on that. used on first open where the VM cache isn't filled yet.
        public static void ApplyPrivateLobbyNamesFromScene(Transform listRoot)
        {
            if (listRoot == null) return;
            foreach (var text in listRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (text == null || text.transform.parent == null) continue;
                if (text.gameObject.name != "Text" || text.transform.parent.name != "Content") continue;

                string visible = NametagIconApplicator.BuildInlinePlatformName(text.text ?? "", "", true);
                ApplyPrivateLobbyCustomName(text.transform.parent, BetterFG.Tweaks.StripSizeTagsTweak.Strip(visible).Trim());
            }
        }

        // one per-row pass: set the row's name + full style. local uses live settings, remote uses
        // the matched profile. keeps the inline platform sprite the platform pass set. row may be the
        // VM's row transform OR the Content node; we resolve the Text under it either way.
        public static void ApplyPrivateLobbyCustomName(Transform row, string playerKey)
        {
            try
            {
                if (row == null || string.IsNullOrEmpty(playerKey)) return;
                // row may be the VM row (Panel/Content/Text under it) or the Content node directly
                var text = (row.Find("Panel/Content/Text") ?? row.Find("Text"))?.GetComponent<TextMeshProUGUI>();
                if (text == null) return;

                string localKey = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? "";
                string cleanKey = FallGuysLib.Players.PlayerUtils.CleanPlayerName(playerKey);
                // match local by real name, real key, OR the custom name we may have already set the row to
                bool isLocal = !string.IsNullOrEmpty(localKey) &&
                    (string.Equals(cleanKey, FallGuysLib.Players.PlayerUtils.CleanPlayerName(localKey), StringComparison.OrdinalIgnoreCase)
                     || string.Equals(cleanKey, FallGuysLib.Players.PlayerUtils.CleanPlayerName(LocalPlayerInfo.FGlocalplayerusername), StringComparison.OrdinalIgnoreCase)
                     || (!string.IsNullOrEmpty(LocalPlayerInfo.CustomName) && string.Equals(playerKey, LocalPlayerInfo.CustomName, StringComparison.OrdinalIgnoreCase)));

                var info = isLocal
                    ? LocalNametagInfo()
#if PROFILES
                    : BetterFG.Customization.Profiles.ProfileService.GetRemoteProfileForName(
                        FallGuysLib.Players.PlayerUtils.CleanPlayerName(playerKey))?.nametag;
#else
                    : null;
#endif
                if (info == null) return;

                // keep the inline platform sprite tag the platform pass prepended, restyle the rest
                string cur = text.text ?? "";
                string visibleName = NametagIconApplicator.BuildInlinePlatformName(cur, "", true);
                string platformTag = cur.EndsWith(visibleName, StringComparison.Ordinal)
                    ? cur.Substring(0, cur.Length - visibleName.Length)
                    : "";
                NametagIconApplicator.ApplyRemoteToNameplate(text, visibleName, info);
                if (!string.IsNullOrEmpty(platformTag))
                    text.text = platformTag + text.text;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PrivateLobbyName] customname " + ex.Message);
            }
        }

        // build a remoteNametagInfo from your own live nametag settings so the local row gets the
        // same name+colour+style path as everyone else.
        private static BetterFG.Network.remoteNametagInfo LocalNametagInfo()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            float F(string k, float d) => float.TryParse(BetterFG.Services.SettingsService.Get(k, ""), System.Globalization.NumberStyles.Float, ci, out float v) ? v : d;
            return new BetterFG.Network.remoteNametagInfo
            {
                customName = LocalPlayerInfo.CustomName,
                r = F("nametag.color.r", 1f), g = F("nametag.color.g", 1f), b = F("nametag.color.b", 1f),
                bold = BetterFG.Services.SettingsService.Get("nametag.bold", "false") == "true",
                italic = BetterFG.Services.SettingsService.Get("nametag.italic", "false") == "true",
                nameStyle = BetterFG.Services.SettingsService.Get("nametag.namestyle", "default"),
                iconMode = "none",
            };
        }

        public static void ApplyPrivateLobbyPlatform(Transform row, string playerKey)
        {
            try
            {
                if (row == null) return;
                var text = row.Find("Panel/Content/Text")?.GetComponent<TextMeshProUGUI>();
                if (text == null) return;

                if (!On("privatelobby"))
                {
                    text.text = NametagIconApplicator.BuildInlinePlatformName(text.text ?? "", "", true);
                    return;
                }

                string spriteName = SpriteNameForPlayerKey(playerKey);
                if (string.IsNullOrEmpty(spriteName))
                {
                    text.text = NametagIconApplicator.BuildInlinePlatformName(text.text ?? "", "", true);
                    return;
                }

                if (!NametagIconApplicator.ApplyInlinePlatformAsset(text)) return;
                NametagIconApplicator.ApplyInlinePlatform(text, spriteName, 0.7f, 0.08f);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PrivateLobbyName] platform " + ex.Message);
            }
        }

        public static void ApplyPrivateLobbyLocalName(Transform row, string playerKey = "", string matchText = "")
        {
            try
            {
                string localName = GlobalGameStateClient.Instance?.GetLocalPlayerName() ?? "";
                string localKey = GlobalGameStateClient.Instance?.GetLocalPlayerKey() ?? "";
                string displayName = BetterFG.Tweaks.StripSizeTagsTweak.Strip(LocalPlayerInfo.DisplayName);
                if (row == null || string.IsNullOrEmpty(localName) || string.IsNullOrEmpty(displayName)) return;

                var text = row.Find("Panel/Content/Text")?.GetComponent<TextMeshProUGUI>();
                if (text == null) return;

                string visible = text.text ?? "";
                string m = matchText ?? "";
                if (!string.IsNullOrEmpty(playerKey))
                {
                    if (string.IsNullOrEmpty(localKey) ||
                        !string.Equals(playerKey, localKey, StringComparison.OrdinalIgnoreCase))
                        return;
                }
                else
                {
                    string cleanVisible = BetterFG.Tweaks.StripSizeTagsTweak.Strip(
                        NametagIconApplicator.BuildInlinePlatformName(visible, "", true)).Trim();
                    string cleanMatch = BetterFG.Tweaks.StripSizeTagsTweak.Strip(
                        NametagIconApplicator.BuildInlinePlatformName(m, "", true)).Trim();
                    string cleanLocal = BetterFG.Tweaks.StripSizeTagsTweak.Strip(localName).Trim();
                    if (!string.Equals(cleanMatch, cleanLocal, StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(cleanVisible, cleanLocal, StringComparison.OrdinalIgnoreCase))
                        return;
                }

                string spriteName = "";
                if (On("privatelobby"))
                {
                    spriteName = SpriteNameForPlayerKey(playerKey);
                    if (!string.IsNullOrEmpty(spriteName))
                        NametagIconApplicator.ApplyInlinePlatformAsset(text);
                }
                if (string.IsNullOrEmpty(spriteName))
                    text.text = NametagIconApplicator.BuildInlinePlatformName(text.text ?? "", "", true);

                NametagIconApplicator.ApplyLocalInlineName(text, displayName, spriteName, 0.7f, -0.2f);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PrivateLobbyName] row " + ex.Message);
            }
        }
    }
}
