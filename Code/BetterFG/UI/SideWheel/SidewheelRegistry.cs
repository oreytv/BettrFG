using BetterFG.UI.SideWheel;
using BetterFG.UI.Windows;
using UnityEngine;

namespace BetterFG.UI.SideWheel
{
    public static class SidewheelRegistry
    {
        public static void RegisterAll(SideWheelManager wheel)
        {
            Register<OptionsWindow>(wheel,
                "BettrFG Options",
                "BetterFG.assets.ui.side.keybindset.png",
                "BetterFG_OptionsWindow");

            Register<AudioSettingsWindow>(wheel,
                "Audio Settings",
                "BetterFG.assets.ui.side.audioset.png",
                "BetterFG_AudioSettingsWindow");

            Register<MenuMusicWindow>(wheel,
                "Menu Music",
                "BetterFG.assets.ui.side.menumusicset.png",
                "BetterFG_MenuMusicWindow");

            Register<PlayerdetailsWindow>(wheel,
                "Player Details",
                "BetterFG.assets.ui.side.nameset.png",
                "BetterFG_PlayerdetailsWindow");

            Register<PlayerScaleWindow>(wheel,
                "Player Scale",
                "BetterFG.assets.ui.side.scaleset.png",
                "BetterFG_PlayerScaleWindow");

            Register<TweaksWindow>(wheel,
                "Tweaks",
                "BetterFG.assets.ui.side.tweakset.png",
                "BetterFG_TweaksWindow");

            Register<PresetsWindow>(wheel,
                "Presets",
                "BetterFG.assets.ui.side.presetset.png",
                "BetterFG_PresetsWindow");

#if PROFILES
            Register<ProfilesWindow>(wheel,
                "Profiles",
                "BetterFG.assets.ui.side.profileset.png",
                "BetterFG_ProfilesWindow");
#endif
        }

        private static void Register<T>(SideWheelManager wheel, string label, string iconResource, string goName)
            where T : BetterFGWindow
        {
            var icon = SideWheelManager.LoadEmbedded(iconResource);
            wheel.RegisterEntry(label, icon, () =>
            {
                var go = new GameObject(goName);
                Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                return go.AddComponent<T>();
            });
        }
    }
}