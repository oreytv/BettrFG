#pragma warning disable CS8981
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;

namespace BetterFG.Installer;

public sealed partial class installerform
{
    private string? RecordInstallerPath()
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return null;

            // env var too, harmless, but the file is what the mod actually reads — env vars don't
            // reach an already-running / steam-launched game process.
            Environment.SetEnvironmentVariable(installerstuff.InstallerPathEnvVar, exePath, EnvironmentVariableTarget.User);

            var stampPath = installerstuff.InstallerPathStampFile;
            Directory.CreateDirectory(Path.GetDirectoryName(stampPath)!);
            File.WriteAllText(stampPath, exePath);
            return exePath;
        }
        catch
        {
            // best-effort; the mod just falls back to opening the release page if this never lands
            return null;
        }
    }

    private static string? ReadLastBrowsedFolder()
    {
        try
        {
            var f = installerstuff.LastFolderStampFile;
            if (File.Exists(f))
            {
                var p = File.ReadAllText(f).Trim();
                if (!string.IsNullOrWhiteSpace(p))
                    return p;
            }
        }
        catch
        {
        }

        return null;
    }

    private static void WriteLastBrowsedFolder(string path)
    {
        try
        {
            var f = installerstuff.LastFolderStampFile;
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllText(f, path);
        }
        catch
        {
        }
    }

    private string? NormalizeGameFolder(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            throw new InvalidOperationException("pick the Fall Guys folder first");

        var fullPath = Path.GetFullPath(rawPath.Trim());
        if (!Directory.Exists(fullPath))
            throw new InvalidOperationException("that folder doesnt exist");

        var exePath = Path.Combine(fullPath, "FallGuys_client_game.exe");
        if (!File.Exists(exePath))
        {
            Log("warning: FallGuys_client_game.exe not found in that folder. pick the correct Fall Guys folder.");
            MessageBox.Show(this, "FallGuys_client_game.exe wasn't found in that folder.\nMake sure you picked the actual Fall Guys game folder.", "Wrong folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        gamePathBox.Text = fullPath;
        return fullPath;
    }

    private static string? TryGetGameFolderQuiet(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(rawPath.Trim());
            if (!Directory.Exists(fullPath))
                return null;

            return File.Exists(Path.Combine(fullPath, "FallGuys_client_game.exe"))
                ? fullPath
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static List<(string Store, string Path)> GetFallGuysInstalls()
    {
        var found = new List<(string Store, string Path)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void tryAdd(string store, string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !File.Exists(Path.Combine(root, "FallGuys_client_game.exe")))
                return;
            var full = Path.GetFullPath(root);
            if (seen.Add(full))
                found.Add((store, full));
        }

        foreach (var lib in GetSteamLibraryRoots())
            tryAdd("Steam", Path.Combine(lib, "steamapps", "common", "Fall Guys"));
        foreach (var loc in GetEpicInstallLocations())
            tryAdd("Epic", loc);

        return found;
    }

    private static List<string> GetEpicInstallLocations()
    {
        var locations = new List<string>();
        var epicData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic");

        var manifestDir = Path.Combine(epicData, "EpicGamesLauncher", "Data", "Manifests");
        if (Directory.Exists(manifestDir))
        {
            foreach (var item in Directory.EnumerateFiles(manifestDir, "*.item"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(item));
                    if (doc.RootElement.TryGetProperty("InstallLocation", out var loc) && loc.GetString() is { Length: > 0 } path)
                        locations.Add(path);
                }
                catch
                {
                }
            }
        }

        var launcherDat = Path.Combine(epicData, "UnrealEngineLauncher", "LauncherInstalled.dat");
        if (File.Exists(launcherDat))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(launcherDat));
                if (doc.RootElement.TryGetProperty("InstallationList", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in list.EnumerateArray())
                        if (entry.TryGetProperty("InstallLocation", out var loc) && loc.GetString() is { Length: > 0 } path)
                            locations.Add(path);
                }
            }
            catch
            {
            }
        }

        return locations;
    }

    private static string[] GetSteamLibraryRoots()
    {
        var paths = new System.Collections.Generic.List<string>();
        using var steamKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam")
            ?? Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");

        var steamPath = steamKey?.GetValue("InstallPath") as string;
        if (!string.IsNullOrWhiteSpace(steamPath))
            paths.Add(steamPath);

        if (!string.IsNullOrWhiteSpace(steamPath))
        {
            var libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(libraryFile))
            {
                foreach (var rawLine in File.ReadAllLines(libraryFile))
                {
                    var clean = rawLine.Trim();
                    if (!clean.StartsWith("\""))
                        continue;

                    var parts = clean.Split('"', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                        continue;

                    if (!int.TryParse(parts[0].Trim(), out _))
                        continue;

                    var libraryPath = parts[1].Replace(@"\\", @"\");
                    if (!string.IsNullOrWhiteSpace(libraryPath))
                        paths.Add(libraryPath);
                }
            }
        }

        return paths
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
#pragma warning restore CS8981
