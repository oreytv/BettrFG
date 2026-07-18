#pragma warning disable CS8981
using System;
using System.IO;

namespace BetterFG.Installer;

public sealed partial class installerform
{
    private const string ModdedClientIni = "TargetApplicationPath=FallGuys_client_game.exe\r\nWorkingDirectory=\r\nWaitForExit=0\r\nSkipEOS=0\r\n";
    private const string VanillaClientIni = "TargetApplicationPath=start_protected_game.exe\r\nWorkingDirectory=\r\nWaitForExit=0\r\nSkipEOS=0\r\n";

    // EAC lives in different spots per store: Epic keeps it at EasyAntiCheat\, Steam under the game data Plugins folder
    private static string EacHome(string gameFolder)
    {
        return gameFolder.IndexOf("steamapps", StringComparison.OrdinalIgnoreCase) >= 0
            ? Path.Combine(gameFolder, "FallGuys_client_game_Data", "Plugins", "x86_64", "EasyAntiCheat.dll")
            : Path.Combine(gameFolder, "EasyAntiCheat", "EasyAntiCheat.dll");
    }

    private void EnableMods(string gameFolder)
    {
        var batchModded = Path.Combine(gameFolder, "BatchData", "Modded");
        var batchUnchanged = Path.Combine(gameFolder, "BatchData", "Unchanged");
        Directory.CreateDirectory(batchModded);
        Directory.CreateDirectory(batchUnchanged);

        var moddedWinhttp = Path.Combine(batchModded, "winhttp.dll");
        var moddedIni = Path.Combine(batchModded, "FallGuys_client.ini");
        var vanillaIni = Path.Combine(batchUnchanged, "FallGuys_client.ini");
        var rootWinhttp = Path.Combine(gameFolder, "winhttp.dll");
        var rootIni = Path.Combine(gameFolder, "FallGuys_client.ini");
        var eacLive = EacHome(gameFolder);
        var eacBackup = Path.Combine(batchUnchanged, "EasyAntiCheat.dll");

        if (!File.Exists(moddedIni))
            File.WriteAllText(moddedIni, ModdedClientIni);
        if (!File.Exists(vanillaIni))
            File.WriteAllText(vanillaIni, VanillaClientIni);

        if (!File.Exists(moddedWinhttp))
        {
            if (!File.Exists(rootWinhttp))
                throw new InvalidOperationException("no winhttp.dll anywhere to enable mods with - reinstall to pull BepInEx again");
            File.Copy(rootWinhttp, moddedWinhttp);
            Log("stashed winhttp into BatchData for later");
        }

        File.Copy(moddedWinhttp, rootWinhttp, true);
        File.WriteAllText(rootIni, ModdedClientIni);

        if (File.Exists(eacLive))
        {
            if (!File.Exists(eacBackup))
            {
                File.Move(eacLive, eacBackup);
                Log("moved EasyAntiCheat out to BatchData");
            }
            else
            {
                File.Delete(eacLive);
                Log("EAC backup already there, just dropped the live one");
            }
        }

        Log("mods on - winhttp in place, launch pointed at the unprotected exe, EAC out of the way");
    }

    private void DisableMods(string gameFolder)
    {
        var batchUnchanged = Path.Combine(gameFolder, "BatchData", "Unchanged");
        var rootWinhttp = Path.Combine(gameFolder, "winhttp.dll");
        var rootIni = Path.Combine(gameFolder, "FallGuys_client.ini");
        var eacLive = EacHome(gameFolder);
        var eacBackup = Path.Combine(batchUnchanged, "EasyAntiCheat.dll");

        if (File.Exists(rootWinhttp))
            File.Delete(rootWinhttp);
        File.WriteAllText(rootIni, VanillaClientIni);

        if (File.Exists(eacBackup) && !File.Exists(eacLive))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(eacLive)!);
            File.Move(eacBackup, eacLive);
            Log("put EasyAntiCheat back");
        }

        Log("mods off - back on the protected launcher");
    }

    private static void RemoveBepInExFramework(string gameFolder)
    {
        var targets = new[]
        {
            Path.Combine(gameFolder, "BepInEx"),
            Path.Combine(gameFolder, "dotnet"),
            Path.Combine(gameFolder, "BatchData"),
            Path.Combine(gameFolder, "winhttp.dll"),
            Path.Combine(gameFolder, "doorstop_config.ini"),
            Path.Combine(gameFolder, ".doorstop_version"),
            Path.Combine(gameFolder, "changelog.txt"),
        };

        foreach (var t in targets)
        {
            try
            {
                if (Directory.Exists(t))
                    Directory.Delete(t, true);
                else if (File.Exists(t))
                    File.Delete(t);
            }
            catch
            {
            }
        }
    }
}
#pragma warning restore CS8981
