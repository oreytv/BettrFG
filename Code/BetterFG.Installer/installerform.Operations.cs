#pragma warning disable CS8981
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BetterFG.Installer;

public sealed partial class installerform
{
    private void LaunchGame()
    {
        try
        {
            var gameFolder = TryGetGameFolderQuiet(gamePathBox.Text);
            if (gameFolder == null)
                return;

            var launcher = Path.Combine(gameFolder, "FallGuys_client.exe");
            if (!File.Exists(launcher))
            {
                Log("no FallGuys_client.exe to launch");
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = launcher, UseShellExecute = true, WorkingDirectory = gameFolder });
            Log("launched via FallGuys_client.exe");
        }
        catch (Exception ex)
        {
            Log("launch failed... " + ex.Message);
        }
    }

    private async Task InstallBetterfgAsync()
    {
        try
        {
            SetBusy(true, "status: installing");

            var gameFolder = NormalizeGameFolder(gamePathBox.Text);
            if (gameFolder == null)
                return;

            var release = (currentOp == bfgop.Modify ? selectedRelease : latestRelease) ?? await GetLatestReleaseAsync();
            var hasFramework = HasBepInExInstall(gameFolder);

            await InstallReleaseAsync(gameFolder, release, hasFramework);
            Log("install finished");
            statusLabel.Text = "status: installed";
            stepTitleLabel.Text = $"Installed {release.version}";
            RefreshInstallState();

            if (!hasFramework)
                MessageBox.Show(
                    this,
                    "BettrFG should now be installed, along with BepInEx. Your game will take a few minutes to start when you launch the game for the first time. Same thing may occur after client updates. You may start the game",
                    installerstuff.DisplayName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("install failed... " + ex.Message);
            statusLabel.Text = "status: failed";
            stepTitleLabel.Text = "Install failed";
            MessageBox.Show(this, ex.Message, $"{installerstuff.DisplayName} install failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, statusLabel.Text);
        }
    }

    private void StartUninstall()
    {
        var gameFolder = TryGetGameFolderQuiet(gamePathBox.Text);
        if (gameFolder == null || GetInstalledVersion(gameFolder) == null)
        {
            MessageBox.Show(this, $"{installerstuff.DisplayName} isn't installed in that folder.", installerstuff.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = MessageBox.Show(this, $"Remove {installerstuff.DisplayName} from this Fall Guys install?", "Uninstall", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes)
            return;

        currentOp = bfgop.Uninstall;
        BeginFade(3);
    }

    private void UninstallBetterfg()
    {
        try
        {
            SetBusy(true, "status: uninstalling");

            var gameFolder = NormalizeGameFolder(gamePathBox.Text);
            if (gameFolder == null)
                return;

            var pluginFolder = Path.Combine(gameFolder, "BepInEx", "plugins", installerstuff.PluginFolderName);
            if (!Directory.Exists(pluginFolder))
            {
                Log("plugin folder wasnt there");
                statusLabel.Text = "status: nothing to remove";
                stepTitleLabel.Text = "Nothing to uninstall";
                RefreshInstallState();
                return;
            }

            var fullPluginFolder = Path.GetFullPath(pluginFolder);
            var expectedTail = Path.Combine("BepInEx", "plugins", installerstuff.PluginFolderName);
            if (!fullPluginFolder.Contains(expectedTail, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("refused to remove weird path");

            Directory.Delete(fullPluginFolder, true);
            Log("removed " + fullPluginFolder);

            var pluginsDir = Path.Combine(gameFolder, "BepInEx", "plugins");
            var othersExist = Directory.Exists(pluginsDir) && Directory.EnumerateFileSystemEntries(pluginsDir).Any();
            if (!othersExist)
            {
                DisableMods(gameFolder);
                RemoveBepInExFramework(gameFolder);
                Log("no other BepInEx plugins here, fully reverted to vanilla (removed BepInEx, dotnet, BatchData, winhttp, doorstop)");
            }
            else
            {
                Log("other BepInEx plugins present, leaving the loader on for them");
            }

            statusLabel.Text = "status: removed";
            stepTitleLabel.Text = "Uninstalled " + installerstuff.DisplayName;
            RefreshInstallState();
        }
        catch (Exception ex)
        {
            Log("uninstall failed... " + ex.Message);
            statusLabel.Text = "status: failed";
            stepTitleLabel.Text = "Uninstall failed";
            MessageBox.Show(this, ex.Message, $"{installerstuff.DisplayName} uninstall failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, statusLabel.Text);
        }
    }

    private void OpenPluginsFolder()
    {
        try
        {
            var gameFolder = NormalizeGameFolder(gamePathBox.Text);
            if (gameFolder == null)
                return;

            var pluginFolder = Path.Combine(gameFolder, "BepInEx", "plugins");
            Directory.CreateDirectory(pluginFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = pluginFolder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log("open folder failed... " + ex.Message);
        }
    }

    private async Task InstallReleaseAsync(string gameFolder, releaseinfo release, bool hasFramework)
    {
        if (!hasFramework)
        {
            Log("no bepinex here, pulling " + release.basePayloadName + " straight from bepinex.dev");
            await DownloadAndInstallZipAsync(gameFolder, release.basePayloadName, release.basePayloadUrl, release.basePayloadDigest);
        }
        else
        {
            Log("bepinex already present, just refreshing " + installerstuff.PluginFolderName);
        }

        var pluginsFolder = Path.Combine(gameFolder, "BepInEx", "plugins");
        Directory.CreateDirectory(pluginsFolder);
        await DownloadAndInstallZipAsync(pluginsFolder, release.pluginPayloadName, release.pluginPayloadUrl, release.pluginPayloadDigest);

        EnableMods(gameFolder);
    }

    private async Task DownloadAndInstallZipAsync(string targetRoot, string payloadName, string payloadUrl, string payloadDigest)
    {
        Log("downloading " + payloadName + " from " + payloadUrl);
        SetProgress(0, "status: downloading " + payloadName);
        using var response = await http.GetAsync(payloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var tempZip = Path.Combine(Path.GetTempPath(), "bettrfg_payload_" + Guid.NewGuid().ToString("N") + ".zip");
        var totalBytes = response.Content.Headers.ContentLength;
        await using (var fs = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None))
        await using (var stream = await response.Content.ReadAsStreamAsync())
        {
            var buffer = new byte[1024 * 128];
            long done = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fs.WriteAsync(buffer, 0, read);
                done += read;
                if (totalBytes.HasValue && totalBytes.Value > 0)
                    SetProgress((int)Math.Min(100, done * 100 / totalBytes.Value), $"status: downloading {payloadName} {done / 1024 / 1024}mb/{totalBytes.Value / 1024 / 1024}mb");
            }
        }

        try
        {
            SetProgress(100, "status: verifying " + payloadName);
            VerifyDownloadedPayload(tempZip, payloadDigest);
            SetProgress(0, "status: extracting " + payloadName);
            InstallPayloadFromZip(tempZip, targetRoot);
            SetProgress(100, "status: extracted " + payloadName);
        }
        finally
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }
    }

    private static bool HasBepInExInstall(string gameFolder)
    {
        if (!Directory.Exists(Path.Combine(gameFolder, "BepInEx", "core")))
            return false;

        return File.Exists(Path.Combine(gameFolder, "winhttp.dll"))
            || File.Exists(Path.Combine(gameFolder, "BatchData", "Modded", "winhttp.dll"));
    }

    private void InstallPayloadFromZip(string zipPath, string targetRoot)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var fullTargetRoot = Path.GetFullPath(targetRoot);
        var entries = zip.Entries.ToArray();
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var relativePath = entry.FullName
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(relativePath))
                continue;

            var finalPath = Path.GetFullPath(Path.Combine(targetRoot, relativePath));
            if (!finalPath.StartsWith(fullTargetRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("payload tried to write outside the game folder");

            if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\") || string.IsNullOrWhiteSpace(entry.Name))
            {
                Directory.CreateDirectory(finalPath);
                Log("made dir " + relativePath);
                continue;
            }

            var parentDir = Path.GetDirectoryName(finalPath);
            if (string.IsNullOrWhiteSpace(parentDir))
                throw new InvalidOperationException("payload file had no parent dir: " + relativePath);

            Directory.CreateDirectory(parentDir);
            entry.ExtractToFile(finalPath, true);
            Log("wrote " + relativePath);
            if (entries.Length > 0)
                SetProgress((i + 1) * 100 / entries.Length, "status: extracting");
        }
    }

    private static void VerifyDownloadedPayload(string zipPath, string digest)
    {
        if (string.IsNullOrWhiteSpace(digest))
            return;

        if (!digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            return;

        var expected = digest.Substring("sha256:".Length).Trim().ToLowerInvariant();
        using var stream = File.OpenRead(zipPath);
        using var sha = SHA256.Create();
        var actual = Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("downloaded payload hash did not match github release digest");
    }
}
#pragma warning restore CS8981
