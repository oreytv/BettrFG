#pragma warning disable CS8981
namespace BetterFG.Installer
{
    public static class installerstuff
    {
        public const string DisplayName = "BettrFG";
        public const string InstallerName = "BettrFG Installer";
        public const string RepoOwner = "oreytv";
        public const string RepoName = "BettrFG";
        public const string BepInExAssetName = "BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755.zip";
        public const string BepInExDownloadUrl = "https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755%2B3fab71a.zip";
        public const string PluginPayloadAssetName = "bettrfg_plugin.zip";
        public const string LegacyPluginPayloadAssetName = "betterfg_plugin.zip";
        public const string PluginFolderName = "BetterFG";
        public const string PluginDllName = "BetterFG.dll";

        // The mod's update window reads this to decide whether it can relaunch the installer
        // directly. We set it to this exe's own path every time the installer runs, so the mod
        // only ever launches an installer the user has already opened from a known location.
        public const string InstallerPathEnvVar = "BETTRFG_INSTALLER_PATH";

        // where the installer drops its own exe path so the in-game update window can relaunch it.
        // a file, not just the env var, because env vars don't reach a running/steam-launched game.
        public static string InstallerPathStampFile =>
            System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "BettrFG", "installer_path.txt");

        public static string LastFolderStampFile =>
            System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "BettrFG", "last_folder.txt");

        public static string LatestReleaseApiUrl => $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        public static string AllReleasesApiUrl => $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases";
        public static string ReleasePageUrl => $"https://github.com/{RepoOwner}/{RepoName}/releases/latest";
        public static string DottedPluginPayloadAssetName(string version) => $"{DisplayName}.{version}.zip";
    }
}
#pragma warning restore CS8981
