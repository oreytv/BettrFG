#pragma warning disable CS8981
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace BetterFG.Installer;

public sealed partial class installerform
{
    private async Task RefreshReleaseStateAsync()
    {
        if (releaseCheckRunning)
            return;

        releaseCheckRunning = true;
        try
        {
            statusLabel.Text = "status: checking github releases";
            availableReleases = await GetAllReleasesAsync();
            if (availableReleases.Count == 0)
                throw new InvalidOperationException("no installable releases found");

            latestRelease = availableReleases[0];
            selectedRelease ??= availableReleases[0];

            versionCombo.Items.Clear();
            for (int i = 0; i < availableReleases.Count; i++)
                versionCombo.Items.Add(i == 0 ? availableReleases[i].version + " (latest)" : availableReleases[i].version);
            var sel = availableReleases.IndexOf(selectedRelease);
            versionCombo.SelectedIndex = sel >= 0 ? sel : 0;

            statusLabel.Text = "status: waiting";
        }
        catch (Exception ex)
        {
            latestRelease = null;
            statusLabel.Text = "status: release check failed";
            Log("release check failed... " + ex.Message);
        }
        finally
        {
            releaseCheckRunning = false;
            RefreshInstallState();
        }
    }

    private async Task<releaseinfo> GetLatestReleaseAsync()
    {
        Log("checking " + installerstuff.LatestReleaseApiUrl);
        using var response = await http.GetAsync(installerstuff.LatestReleaseApiUrl);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        var release = await JsonSerializer.DeserializeAsync<githublatestrelease>(stream);
        if (release == null || string.IsNullOrWhiteSpace(release.tag_name))
            throw new InvalidOperationException("github latest release response was invalid");

        var version = NormalizeReleaseVersion(release.tag_name);
        var pluginAsset = PickPluginPayloadAsset(release.assets, version);
        if (pluginAsset == null || string.IsNullOrWhiteSpace(pluginAsset.browser_download_url))
            throw new InvalidOperationException("latest github release " + release.tag_name + " did not contain " + installerstuff.PluginPayloadAssetName + ". assets: " + AssetNames(release.assets));

        var displayName = string.IsNullOrWhiteSpace(release.name) ? release.tag_name : release.name.Trim();
        Log("latest release: " + displayName + " -> plugin " + pluginAsset.name);
        return new releaseinfo
        {
            version = version,
            displayName = displayName,
            pluginPayloadName = pluginAsset.name,
            pluginPayloadUrl = pluginAsset.browser_download_url,
            pluginPayloadDigest = pluginAsset.digest ?? "",
            basePayloadName = installerstuff.BepInExAssetName,
            basePayloadUrl = installerstuff.BepInExDownloadUrl,
            basePayloadDigest = ""
        };
    }

    private async Task<List<releaseinfo>> GetAllReleasesAsync()
    {
        Log("checking " + installerstuff.AllReleasesApiUrl);
        using var response = await http.GetAsync(installerstuff.AllReleasesApiUrl);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync();
        var releases = await JsonSerializer.DeserializeAsync<githublatestrelease[]>(stream);

        var result = new List<releaseinfo>();
        if (releases == null)
            return result;

        foreach (var r in releases)
        {
            if (string.IsNullOrWhiteSpace(r.tag_name))
                continue;

            var version = NormalizeReleaseVersion(r.tag_name);
            var pluginAsset = PickPluginPayloadAsset(r.assets, version);
            if (pluginAsset == null || string.IsNullOrWhiteSpace(pluginAsset.browser_download_url))
                continue;

            var displayName = string.IsNullOrWhiteSpace(r.name) ? r.tag_name : r.name.Trim();
            result.Add(new releaseinfo
            {
                version = version,
                displayName = displayName,
                pluginPayloadName = pluginAsset.name,
                pluginPayloadUrl = pluginAsset.browser_download_url,
                pluginPayloadDigest = pluginAsset.digest ?? "",
                basePayloadName = installerstuff.BepInExAssetName,
                basePayloadUrl = installerstuff.BepInExDownloadUrl,
                basePayloadDigest = ""
            });
        }

        Log($"found {result.Count} installable releases");
        return result;
    }

    private static githubreleaseasset? PickAsset(githubreleaseasset[] assets, string exactName)
    {
        return assets.FirstOrDefault(x => string.Equals(x.name, exactName, StringComparison.OrdinalIgnoreCase));
    }

    private static string AssetNames(githubreleaseasset[] assets)
    {
        if (assets == null || assets.Length == 0) return "(none)";
        return string.Join(", ", assets.Select(x => string.IsNullOrWhiteSpace(x.name) ? "(blank)" : x.name));
    }

    private static githubreleaseasset? PickPluginPayloadAsset(githubreleaseasset[] assets, string version)
    {
        var dottedName = installerstuff.DottedPluginPayloadAssetName(version);
        return PickAsset(assets, installerstuff.PluginPayloadAssetName)
            ?? PickAsset(assets, installerstuff.LegacyPluginPayloadAssetName)
            ?? PickAsset(assets, dottedName)
            ?? assets.FirstOrDefault(x =>
                x.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                && x.name.StartsWith(installerstuff.DisplayName + ".", StringComparison.OrdinalIgnoreCase));
    }

    private static Version? GetInstalledVersion(string gameFolder)
    {
        var dllPath = Path.Combine(gameFolder, "BepInEx", "plugins", installerstuff.PluginFolderName, installerstuff.PluginDllName);
        if (!File.Exists(dllPath))
            return null;

        try
        {
            var asmVersion = AssemblyName.GetAssemblyName(dllPath).Version;
            if (asmVersion != null)
                return asmVersion;
        }
        catch
        {
        }

        try
        {
            var fileVersion = FileVersionInfo.GetVersionInfo(dllPath).FileVersion;
            if (!string.IsNullOrWhiteSpace(fileVersion) && Version.TryParse(fileVersion, out var parsed))
                return parsed;
        }
        catch
        {
        }

        return null;
    }

    private static string NormalizeReleaseVersion(string tag)
    {
        var clean = tag.Trim();
        if (clean.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            clean = clean.Substring(1);
        return clean;
    }

    private static Version NormalizeVersion(Version version)
    {
        return new Version(
            Math.Max(0, version.Major),
            Math.Max(0, version.Minor),
            Math.Max(0, version.Build));
    }

    private static string VersionText(Version version)
    {
        var clean = NormalizeVersion(version);
        return clean.Major + "." + clean.Minor + "." + clean.Build;
    }

    private sealed class githubreleaseasset
    {
        public string name { get; set; } = "";
        public string browser_download_url { get; set; } = "";
        public string digest { get; set; } = "";
    }

    private sealed class githublatestrelease
    {
        public string name { get; set; } = "";
        public string tag_name { get; set; } = "";
        public githubreleaseasset[] assets { get; set; } = Array.Empty<githubreleaseasset>();
    }

    private sealed class releaseinfo
    {
        public string version { get; set; } = "";
        public string displayName { get; set; } = "";
        public string basePayloadName { get; set; } = "";
        public string basePayloadUrl { get; set; } = "";
        public string basePayloadDigest { get; set; } = "";
        public string pluginPayloadName { get; set; } = "";
        public string pluginPayloadUrl { get; set; } = "";
        public string pluginPayloadDigest { get; set; } = "";
    }
}
#pragma warning restore CS8981
