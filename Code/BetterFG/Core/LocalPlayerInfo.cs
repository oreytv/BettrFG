using BetterFG.Services;

namespace BetterFG.Core
{
    public static class LocalPlayerInfo
    {
        private const string KEY_CUSTOM_NAME = "nametag.customname";

        // the real FG username — set once from GlobalGameStateClient on main menu enter
        public static string FGlocalplayerusername { get; set; } = "";

        public static string CustomName
        {
            get => SettingsService.Get(KEY_CUSTOM_NAME, "");
            set => SettingsService.Set(KEY_CUSTOM_NAME, value ?? "");
        }

        // whichever name should actually display
        public static string DisplayName =>
            string.IsNullOrEmpty(CustomName) ? FGlocalplayerusername : CustomName;
    }
}