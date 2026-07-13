using System;
using System.Collections.Generic;
using System.IO;
using BetterFG.Customization.Menu;
using BetterFG.Customization.Player;
using BetterFG.Nametag;
using BetterFG.Patches;
using BetterFG.Services;
using UnityEngine;

namespace BetterFG.Customization.Presets
{
    // a preset is a named snapshot of every settings key under these prefixes — skin/plinth (plinth
    // skins live in the multi list), the player name + nametag, the all-cosmetics / custom-texture
    // stuff, ALL menu customization (bg image, fg colours, cam, ambient/sun, plinth colour), and the
    // per-screen backgrounds (screen.* — fallforce/loading/finalround/explore gradient + pattern).
    // save grabs the current values, load wipes those keys and writes them back then reapplies
    // everything live. one file per preset in Settings/presets/<name>.txt, same key=value lines as
    // last.txt so there's nothing to parse.
    public static class PresetService
    {
        private static readonly string[] PREFIXES =
            { "skin.", "skintex.", "allcosmetics.", "nametag.", "menu.", "screen." };

        private static readonly string Dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BettrFG", "Settings", "presets");

        public static List<string> List()
        {
            var names = new List<string>();
            if (!Directory.Exists(Dir)) return names;
            foreach (var f in Directory.GetFiles(Dir, "*.txt"))
                names.Add(Path.GetFileNameWithoutExtension(f));
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public static void Save(string name)
        {
            name = Sanitize(name);
            if (string.IsNullOrEmpty(name)) return;
            try
            {
                if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir);
                var lines = new List<string>();
                foreach (var kv in SettingsService.Snapshot(PREFIXES))
                    lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(Path.Combine(Dir, name + ".txt"), lines);
                Plugin.Log.LogInfo("Presets: saved " + name);
            }
            catch (Exception ex) { Plugin.Log.LogError("Presets: save failed: " + ex.Message); }
        }

        public static void Load(string name)
        {
            string path = Path.Combine(Dir, Sanitize(name) + ".txt");
            if (!File.Exists(path)) return;

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (var line in File.ReadAllLines(path))
                {
                    int eq = line.IndexOf('=');
                    if (eq < 1) continue;
                    values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception ex) { Plugin.Log.LogError("Presets: load failed: " + ex.Message); return; }

            SettingsService.ReplacePrefixed(PREFIXES, values);

            // diff against what's equipped, don't nuke: RemoveOneSkinByFile is the tab's own targeted
            // unequip (no GEO churn for items, proper cosmetic re-composite when a body skin comes
            // off), and RestoreFromSettings skips files that still have an active slot — so switching
            // presets only ever touches the difference. game cosmetics sync themselves from settings
            // (RestoreSavedGameCosmetics removes unwanted + adds missing).
            var app = CustomizationServices.ApplicationService;
            var catalog = CustomizationServices.CatalogService;
            var loader = CustomizationServices.LoaderService;
            var plinth = CustomizationServices.PlinthApp;
            if (app != null && catalog != null && loader != null && plinth != null)
            {
                var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in SettingsService.Get("skin.multi.files", "").Split(','))
                    if (!string.IsNullOrWhiteSpace(f)) wanted.Add(f.Trim());

                foreach (var slot in app.GetActiveSlots())
                    if (slot?.skinInfo != null && !wanted.Contains(slot.skinInfo.file))
                        app.RemoveOneSkinByFile(slot.skinInfo.file);

                app.RestoreFromSettings(catalog.AvailableSkins, loader, plinth, force: true);
                app.RestoreSavedGameCosmetics();
            }
            NametagFinder.ReapplyAllNameplates();

            // menu bg/fg/cam — SpawnMenuBg is the menu-enter restore-everything path (gradient,
            // image bg, ambient/sun, plinth colour, circles pattern). fg gets a revert first since
            // ReapplyForegroundFromSettings doesn't undo old tints when the new preset has fewer
            // colours on; same idea for cam (reset, then apply only if the preset has cam keys)
            // and the pattern (restore original if the new preset has none).
            var menuApp = MenuCustomizationApplication.Instance;
            if (menuApp != null)
            {
                menuApp.SpawnMenuBg();
                if (SettingsService.Get(MenuCustomizationApplication.KEY_PATTERN_PATH, "") == "")
                    menuApp.RestorePattern();
                menuApp.RevertForeground();
                menuApp.ReapplyForegroundFromSettings();
                menuApp.ResetCam();
                MenuCustomizationApplication.AutoApplyCamFromSettings();
            }

            // per-screen banners: SpawnMenuBg already handled the FallForce menu path via its
            // Apply call. if a loading screen is live right now, repaint it so the new screen.*
            // values take effect without waiting for the next fade-in.
            LoadingScreenBg.ReapplyActive();

            Plugin.Log.LogInfo("Presets: loaded " + name);
        }

        public static void Delete(string name)
        {
            try
            {
                string path = Path.Combine(Dir, Sanitize(name) + ".txt");
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex) { Plugin.Log.LogError("Presets: delete failed: " + ex.Message); }
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }
    }
}
