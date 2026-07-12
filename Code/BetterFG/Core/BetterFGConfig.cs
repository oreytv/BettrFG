using System.IO;
using System.Reflection;
using BepInEx.Configuration;

namespace BetterFG.Core
{
    /// <summary>
    /// All BetterFG config entries live here.
    /// File lands at  &lt;DLL folder&gt;/Settings/base.cfg
    /// Add new entries as static ConfigEntry fields — no other file needs changing.
    /// </summary>
    public static class BetterFGConfig
    {
        private static ConfigFile _file;

        public static ConfigEntry<bool> AutoFetchOnStartup { get; private set; }

        public static void Init()
        {
            string dllDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            string settingsDir = Path.Combine(dllDir, "Settings");
            if (!Directory.Exists(settingsDir))
                Directory.CreateDirectory(settingsDir);

            _file = new ConfigFile(Path.Combine(settingsDir, "base.cfg"), saveOnInit: true);

            AutoFetchOnStartup = _file.Bind(
                "General",
                "AutoFetchOnStartup",
                true,
                "Automatically fetch the skin list from the repository when the game starts."
            );
        }
    }
}