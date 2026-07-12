#pragma warning disable CS8981
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BetterFG.Installer;

public sealed class installerform : Form
{
    private static readonly HttpClient http = new HttpClient();

    private readonly bfgpanel shellPanel;
    private readonly TextBox gamePathBox;
    private readonly TextBox logBox;
    private readonly bfgbutton installButton;
    private readonly bfgbutton uninstallButton;
    private readonly bfgbutton turnOnModsButton;
    private readonly bfgbutton turnOffModsButton;
    private readonly Label statusLabel;
    private readonly Label releaseLabel;
    private readonly Label installedLabel;
    private readonly ProgressBar progressBar;

    private releaseinfo? latestRelease;
    private bool releaseCheckRunning;
    private string releaseStatusText = "latest: checking github release...";

    private static readonly Bitmap buttonShineTex = LoadBitmap("assets.button_shine_pinkbg.png");
    private static readonly Bitmap windowTex = LoadBitmap("assets.ui.windows.generalbg.png");
    private static readonly Bitmap windowHoverTex = LoadBitmap("assets.ui.windows.generalbg_hover.png");
    private static readonly Bitmap logoTex = LoadBitmap("assets.ui.betterfglogo.png");
    private static readonly Icon appIcon = LoadIcon("assets.betterfg.ico");

    public installerform()
    {
        if (!http.DefaultRequestHeaders.UserAgent.Any())
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BettrFG.Installer");
        http.Timeout = Timeout.InfiniteTimeSpan;

        var stampedPath = RecordInstallerPath();

        Text = installerstuff.InstallerName;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 500);
        MinimumSize = new Size(760, 500);
        BackColor = Color.Black;
        ForeColor = Color.White;
        Font = new Font("Arial", 8.5f, FontStyle.Regular);
        Icon = appIcon;
        DoubleBuffered = true;

        shellPanel = new bfgpanel
        {
            Location = new Point(4, 4),
            Size = new Size(ClientSize.Width - 8, ClientSize.Height - 8),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            BackTexture = windowTex,
            HoverTexture = windowHoverTex,
            Padding = new Padding(0)
        };
        Controls.Add(shellPanel);

        var logoH = 50;
        var logoW = (int)(logoTex.Width * (logoH / (float)logoTex.Height));
        var logo = new PictureBox
        {
            Image = logoTex,
            SizeMode = PictureBoxSizeMode.StretchImage,
            BackColor = Color.Transparent,
            Location = new Point(9, 6),
            Size = new Size(logoW, logoH)
        };

        var pathLabel = new Label
        {
            Text = "Fall Guys folder",
            Font = new Font("Arial", 8.25f, FontStyle.Bold),
            AutoSize = true,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Location = new Point(9, 58)
        };

        gamePathBox = new TextBox
        {
            Font = new Font("Arial", 9f, FontStyle.Regular),
            Location = new Point(9, 77),
            Size = new Size(603, 24),
            BackColor = Color.Black,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        gamePathBox.TextChanged += (_, _) => RefreshInstallState();

        var browseButton = new bfgbutton
        {
            Text = "Browse",
            Location = new Point(617, 76),
            Size = new Size(127, 24)
        };
        browseButton.Click += (_, _) => BrowseGameFolder();

        installButton = new bfgbutton
        {
            Text = "Install",
            Location = new Point(9, 110),
            Size = new Size(148, 26)
        };
        installButton.Click += async (_, _) => await InstallBetterfgAsync();

        uninstallButton = new bfgbutton
        {
            Text = "Uninstall",
            Location = new Point(160, 110),
            Size = new Size(98, 26)
        };
        uninstallButton.Click += (_, _) => UninstallBetterfg();

        var openFolderButton = new bfgbutton
        {
            Text = "Open plugins folder",
            Location = new Point(261, 110),
            Size = new Size(153, 26)
        };
        openFolderButton.Click += (_, _) => OpenPluginsFolder();

        turnOnModsButton = new bfgbutton
        {
            Text = "Turn on mods",
            Location = new Point(417, 110),
            Size = new Size(115, 26)
        };
        turnOnModsButton.Click += (_, _) => TurnOnMods();

        turnOffModsButton = new bfgbutton
        {
            Text = "Turn off mods",
            Location = new Point(535, 110),
            Size = new Size(124, 26)
        };
        turnOffModsButton.Click += (_, _) => TurnOffMods();

        statusLabel = new Label
        {
            Text = "status: waiting",
            Font = new Font("Arial", 8.25f, FontStyle.Regular),
            ForeColor = Color.FromArgb(170, 170, 170),
            AutoSize = true,
            BackColor = Color.Transparent,
            Location = new Point(10, 143)
        };

        releaseLabel = new Label
        {
            Text = releaseStatusText,
            Font = new Font("Arial", 8.25f, FontStyle.Regular),
            ForeColor = Color.White,
            AutoSize = true,
            BackColor = Color.Transparent,
            Location = new Point(10, 160)
        };

        installedLabel = new Label
        {
            Text = "installed: checking folder...",
            Font = new Font("Arial", 8.25f, FontStyle.Regular),
            ForeColor = Color.FromArgb(210, 210, 210),
            AutoSize = true,
            BackColor = Color.Transparent,
            Location = new Point(10, 177)
        };

        logBox = new TextBox
        {
            Location = new Point(9, 200),
            Size = new Size(735, 266),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Font = new Font("Arial", 8.25f),
            BackColor = Color.Black,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        progressBar = new ProgressBar
        {
            Location = new Point(9, 473),
            Size = new Size(735, 13),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous
        };

        shellPanel.Controls.Add(logo);
        shellPanel.Controls.Add(pathLabel);
        shellPanel.Controls.Add(gamePathBox);
        shellPanel.Controls.Add(browseButton);
        shellPanel.Controls.Add(installButton);
        shellPanel.Controls.Add(uninstallButton);
        shellPanel.Controls.Add(openFolderButton);
        shellPanel.Controls.Add(turnOnModsButton);
        shellPanel.Controls.Add(turnOffModsButton);
        shellPanel.Controls.Add(statusLabel);
        shellPanel.Controls.Add(releaseLabel);
        shellPanel.Controls.Add(installedLabel);
        shellPanel.Controls.Add(logBox);
        shellPanel.Controls.Add(progressBar);

        gamePathBox.Text = FindFallGuysFolder() ?? string.Empty;
        Log($"{installerstuff.DisplayName} installer booted");
        if (stampedPath != null)
            Log("stamped installer path into " + installerstuff.InstallerPathStampFile + " so the in-game updater can find me");
        else
            Log("couldnt stamp installer path (running from a weird spot?) — in-game updater will fall back to the release page");
        Log("release page: " + installerstuff.ReleasePageUrl);
        if (string.IsNullOrWhiteSpace(gamePathBox.Text))
            Log("couldnt auto-find Fall Guys... pick the game folder manually");
        else
            Log("found Fall Guys at " + gamePathBox.Text);

        RefreshInstallState();
        Shown += async (_, _) => await RefreshReleaseStateAsync();
    }

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

    private void BrowseGameFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Pick your Fall Guys folder"
        };

        if (!string.IsNullOrWhiteSpace(gamePathBox.Text) && Directory.Exists(gamePathBox.Text))
            dialog.SelectedPath = gamePathBox.Text;

        if (dialog.ShowDialog(this) == DialogResult.OK)
            gamePathBox.Text = dialog.SelectedPath;
    }

    private async Task InstallBetterfgAsync()
    {
        try
        {
            SetBusy(true, "status: installing");

            var gameFolder = NormalizeGameFolder(gamePathBox.Text);
            if (gameFolder == null)
                return;

            var release = latestRelease ?? await GetLatestReleaseAsync();
            latestRelease = release;
            var hasFramework = HasBepInExInstall(gameFolder);

            var installedVersion = GetInstalledVersion(gameFolder);
            if (installedVersion != null && Version.TryParse(release.version, out var latestVersion) && NormalizeVersion(installedVersion) >= NormalizeVersion(latestVersion))
            {
                var sameResult = MessageBox.Show(
                    this,
                    $"Installed {installerstuff.DisplayName} is already {VersionText(installedVersion)}. Reinstall anyway?",
                    installerstuff.DisplayName,
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (sameResult != DialogResult.Yes)
                {
                    statusLabel.Text = "status: up to date";
                    RefreshInstallState();
                    return;
                }
            }

            await InstallReleaseAsync(gameFolder, release, hasFramework);
            Log("install finished");
            statusLabel.Text = "status: installed";
            MessageBox.Show(this, $"{installerstuff.DisplayName} installed.", installerstuff.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);

            if (!hasFramework)
                TurnOnMods();
            RefreshInstallState();
        }
        catch (Exception ex)
        {
            Log("install failed... " + ex.Message);
            statusLabel.Text = "status: failed";
            MessageBox.Show(this, ex.Message, $"{installerstuff.DisplayName} install failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, statusLabel.Text);
        }
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
                RefreshInstallState();
                return;
            }

            var fullPluginFolder = Path.GetFullPath(pluginFolder);
            var expectedTail = Path.Combine("BepInEx", "plugins", installerstuff.PluginFolderName);
            if (!fullPluginFolder.Contains(expectedTail, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("refused to remove weird path");

            Directory.Delete(fullPluginFolder, true);
            Log("removed " + fullPluginFolder);

            var turnOffFile = Path.Combine(gameFolder, "TURN_OFF_CEP_CE.bat");
            if (File.Exists(turnOffFile))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = turnOffFile,
                    UseShellExecute = true,
                    WorkingDirectory = gameFolder
                });
                Log("started TURN_OFF_CEP_CE.bat");
            }
            else
            {
                Log("TURN_OFF_CEP_CE.bat not found... skipped");
            }

            statusLabel.Text = "status: removed";
            MessageBox.Show(this, $"{installerstuff.DisplayName} removed.", installerstuff.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshInstallState();
        }
        catch (Exception ex)
        {
            Log("uninstall failed... " + ex.Message);
            statusLabel.Text = "status: failed";
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

    private void TurnOnMods()
    {
        try
        {
            SetBusy(true, "status: turning on mods");
            var gameFolder = NormalizeGameFolder(gamePathBox.Text);
            if (gameFolder == null)
                return;

            var batchFile = Path.Combine(gameFolder, "INSTALL_CEP_CE.bat");
            if (!File.Exists(batchFile))
            {
                Log("INSTALL_CEP_CE.bat not found in game folder");
                MessageBox.Show(this, "INSTALL_CEP_CE.bat not found in the game folder.", "File not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = batchFile,
                UseShellExecute = true,
                WorkingDirectory = gameFolder
            });

            Log("started INSTALL_CEP_CE.bat");
            statusLabel.Text = "status: running turn on mods";
        }
        catch (Exception ex)
        {
            Log("turn on mods failed... " + ex.Message);
            statusLabel.Text = "status: failed";
            MessageBox.Show(this, ex.Message, "Turn on mods failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, statusLabel.Text);
        }
    }

    private void TurnOffMods()
    {
        try
        {
            SetBusy(true, "status: turning off mods");
            var gameFolder = NormalizeGameFolder(gamePathBox.Text);
            if (gameFolder == null)
                return;

            var batchFile = Path.Combine(gameFolder, "TURN_OFF_CEP_CE.bat");
            if (!File.Exists(batchFile))
            {
                Log("TURN_OFF_CEP_CE.bat not found in game folder");
                MessageBox.Show(this, "TURN_OFF_CEP_CE.bat not found in the game folder.", "File not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = batchFile,
                UseShellExecute = true,
                WorkingDirectory = gameFolder
            });

            Log("started TURN_OFF_CEP_CE.bat");
            statusLabel.Text = "status: running turn off mods";
        }
        catch (Exception ex)
        {
            Log("turn off mods failed... " + ex.Message);
            statusLabel.Text = "status: failed";
            MessageBox.Show(this, ex.Message, "Turn off mods failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false, statusLabel.Text);
        }
    }

    private void RefreshModToggleButtons()
    {
        var gameFolder = TryGetGameFolderQuiet(gamePathBox.Text);
        turnOnModsButton.Visible = gameFolder != null && File.Exists(Path.Combine(gameFolder, "INSTALL_CEP_CE.bat"));
        turnOffModsButton.Visible = gameFolder != null && File.Exists(Path.Combine(gameFolder, "TURN_OFF_CEP_CE.bat"));
    }

    private void RefreshInstallState()
    {
        RefreshModToggleButtons();

        releaseLabel.Text = latestRelease == null
            ? releaseStatusText
            : $"latest: {latestRelease.displayName} ({latestRelease.version})";

        var gameFolder = TryGetGameFolderQuiet(gamePathBox.Text);
        if (gameFolder == null)
        {
            installedLabel.Text = "installed: game folder not ready";
            installButton.Text = latestRelease == null ? "Install" : $"Install {latestRelease.version}";
            return;
        }

        var installedVersion = GetInstalledVersion(gameFolder);
        if (installedVersion == null)
        {
            installedLabel.Text = $"installed: no {installerstuff.DisplayName} found";
            installButton.Text = latestRelease == null ? "Install" : $"Install {latestRelease.version}";
            return;
        }

        if (latestRelease == null || !Version.TryParse(latestRelease.version, out var latestVersion))
        {
            var installedText = VersionText(installedVersion);
            installedLabel.Text = $"installed: {installedText}";
            installButton.Text = $"Reinstall {installedText}";
            return;
        }

        var installedNorm = NormalizeVersion(installedVersion);
        var latestNorm = NormalizeVersion(latestVersion);
        var installedVersionText = VersionText(installedNorm);

        if (installedNorm < latestNorm)
        {
            installedLabel.Text = $"installed: {installedVersionText}... update available";
            installButton.Text = $"Update to {latestRelease.version}";
            return;
        }

        if (installedNorm > latestNorm)
        {
            installedLabel.Text = $"installed: {installedVersionText}... newer than github {latestRelease.version}";
            installButton.Text = $"Reinstall {installedVersionText}";
            return;
        }

        installedLabel.Text = $"installed: {installedVersionText}... up to date";
        installButton.Text = $"Reinstall {installedVersionText}";
    }

    private async Task RefreshReleaseStateAsync()
    {
        if (releaseCheckRunning)
            return;

        releaseCheckRunning = true;
        try
        {
            statusLabel.Text = "status: checking github release";
            latestRelease = await GetLatestReleaseAsync();
            releaseStatusText = $"latest: {latestRelease.displayName} ({latestRelease.version})";
            statusLabel.Text = "status: waiting";
        }
        catch (Exception ex)
        {
            latestRelease = null;
            releaseStatusText = "latest: couldnt check github release";
            statusLabel.Text = "status: release check failed";
            Log("release check failed... " + ex.Message);
        }
        finally
        {
            releaseCheckRunning = false;
            RefreshInstallState();
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

        var baseAsset = PickBasePayloadAsset(release.assets) ?? await GetBasePayloadAssetAsync();

        var displayName = string.IsNullOrWhiteSpace(release.name) ? release.tag_name : release.name.Trim();
        Log("latest release: " + displayName + " -> plugin " + pluginAsset.name + (baseAsset != null ? ", base " + baseAsset.name : ""));
        return new releaseinfo
        {
            version = version,
            displayName = displayName,
            pluginPayloadName = pluginAsset.name,
            pluginPayloadUrl = pluginAsset.browser_download_url,
            pluginPayloadDigest = pluginAsset.digest ?? "",
            basePayloadName = baseAsset?.name ?? "",
            basePayloadUrl = baseAsset?.browser_download_url ?? "",
            basePayloadDigest = baseAsset?.digest ?? ""
        };
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
                && x.name.StartsWith(installerstuff.DisplayName + ".", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(x.name, installerstuff.BasePayloadAssetName, StringComparison.OrdinalIgnoreCase));
    }

    private static githubreleaseasset? PickBasePayloadAsset(githubreleaseasset[] assets)
    {
        return PickAsset(assets, installerstuff.BasePayloadAssetName)
            ?? PickAsset(assets, installerstuff.LegacyBasePayloadAssetName);
    }

    private async Task<githubreleaseasset?> GetBasePayloadAssetAsync()
    {
        try
        {
            Log("checking bootstrap payload " + installerstuff.BasePayloadReleaseApiUrl);
            using var response = await http.GetAsync(installerstuff.BasePayloadReleaseApiUrl);
            if (!response.IsSuccessStatusCode)
            {
                Log("bootstrap payload release not found");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            var release = await JsonSerializer.DeserializeAsync<githublatestrelease>(stream);
            var asset = release == null ? null : PickBasePayloadAsset(release.assets);
            if (asset == null || string.IsNullOrWhiteSpace(asset.browser_download_url))
            {
                Log("bootstrap release did not contain " + installerstuff.BasePayloadAssetName);
                return null;
            }

            return asset;
        }
        catch (Exception ex)
        {
            Log("bootstrap payload check failed... " + ex.Message);
            return null;
        }
    }

    private async Task InstallReleaseAsync(string gameFolder, releaseinfo release, bool hasFramework)
    {
        if (!hasFramework)
        {
            if (string.IsNullOrWhiteSpace(release.basePayloadUrl))
                throw new InvalidOperationException("latest github release did not contain " + installerstuff.BasePayloadAssetName);

            Log("no bepinex install found... doing full bootstrap");
            await DownloadAndInstallZipAsync(gameFolder, release.basePayloadName, release.basePayloadUrl, release.basePayloadDigest);
        }
        else
        {
            Log("found bepinex install... only updating " + installerstuff.PluginFolderName);
        }

        var pluginsFolder = Path.Combine(gameFolder, "BepInEx", "plugins");
        Directory.CreateDirectory(pluginsFolder);
        await DownloadAndInstallZipAsync(pluginsFolder, release.pluginPayloadName, release.pluginPayloadUrl, release.pluginPayloadDigest);
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
        return File.Exists(Path.Combine(gameFolder, "INSTALL_CEP_CE.bat"));
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

    private string? FindFallGuysFolder()
    {
        foreach (var path in GetSteamLibraryRoots())
        {
            var gamePath = Path.Combine(path, "steamapps", "common", "Fall Guys");
            if (Directory.Exists(gamePath))
                return gamePath;
        }

        return null;
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

    private void Log(string text)
    {
        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
    }

    private void SetBusy(bool busy, string statusText)
    {
        installButton.Enabled = !busy;
        uninstallButton.Enabled = !busy;
        statusLabel.Text = statusText;
        UseWaitCursor = busy;
    }

    private void SetProgress(int percent, string statusText)
    {
        if (percent < progressBar.Minimum) percent = progressBar.Minimum;
        if (percent > progressBar.Maximum) percent = progressBar.Maximum;
        progressBar.Value = percent;
        statusLabel.Text = statusText;
        progressBar.Refresh();
        statusLabel.Refresh();
    }

    private static Bitmap LoadBitmap(string endsWithName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(x => x.EndsWith(endsWithName, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
            throw new InvalidOperationException("missing installer texture " + endsWithName);

        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("failed opening installer texture " + endsWithName);
        return new Bitmap(stream);
    }

    private static Icon LoadIcon(string endsWithName)
    {
        var asm = Assembly.GetExecutingAssembly();
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(x => x.EndsWith(endsWithName, StringComparison.OrdinalIgnoreCase));

        if (resourceName == null)
            throw new InvalidOperationException("missing installer icon " + endsWithName);

        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("failed opening installer icon " + endsWithName);
        return new Icon(stream);
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

    private static void DrawSliced(Graphics g, Rectangle rect, Image image, int slice)
    {
        if (image == null || rect.Width <= 0 || rect.Height <= 0)
            return;

        g.InterpolationMode = InterpolationMode.HighQualityBilinear;

        int srcW = image.Width;
        int srcH = image.Height;
        int left = Math.Min(slice, srcW / 2);
        int right = left;
        int top = Math.Min(slice, srcH / 2);
        int bottom = top;

        int midSrcW = Math.Max(1, srcW - left - right);
        int midSrcH = Math.Max(1, srcH - top - bottom);
        int midDstW = Math.Max(1, rect.Width - left - right);
        int midDstH = Math.Max(1, rect.Height - top - bottom);

        DrawPart(g, image, new Rectangle(rect.Left, rect.Top, left, top), new Rectangle(0, 0, left, top));
        DrawPart(g, image, new Rectangle(rect.Left + left, rect.Top, midDstW, top), new Rectangle(left, 0, midSrcW, top));
        DrawPart(g, image, new Rectangle(rect.Right - right, rect.Top, right, top), new Rectangle(srcW - right, 0, right, top));

        DrawPart(g, image, new Rectangle(rect.Left, rect.Top + top, left, midDstH), new Rectangle(0, top, left, midSrcH));
        DrawPart(g, image, new Rectangle(rect.Left + left, rect.Top + top, midDstW, midDstH), new Rectangle(left, top, midSrcW, midSrcH));
        DrawPart(g, image, new Rectangle(rect.Right - right, rect.Top + top, right, midDstH), new Rectangle(srcW - right, top, right, midSrcH));

        DrawPart(g, image, new Rectangle(rect.Left, rect.Bottom - bottom, left, bottom), new Rectangle(0, srcH - bottom, left, bottom));
        DrawPart(g, image, new Rectangle(rect.Left + left, rect.Bottom - bottom, midDstW, bottom), new Rectangle(left, srcH - bottom, midSrcW, bottom));
        DrawPart(g, image, new Rectangle(rect.Right - right, rect.Bottom - bottom, right, bottom), new Rectangle(srcW - right, srcH - bottom, right, bottom));
    }

    private void InitializeComponent()
    {
    }

    private static void DrawPart(Graphics g, Image image, Rectangle dest, Rectangle src)
    {
        if (dest.Width <= 0 || dest.Height <= 0 || src.Width <= 0 || src.Height <= 0)
            return;
        g.DrawImage(image, dest, src, GraphicsUnit.Pixel);
    }

    private sealed class bfgpanel : Panel
    {
        public Image? BackTexture { get; set; }
        public Image? HoverTexture { get; set; }
        private bool isHovering;

        public bfgpanel()
        {
            DoubleBuffered = true;
            BackColor = Color.Black;
            MouseEnter += (_, _) => { isHovering = true; Invalidate(); };
            MouseLeave += (_, _) => { isHovering = false; Invalidate(); };
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.Black);
            var tex = isHovering && HoverTexture != null ? HoverTexture : BackTexture;
            if (tex != null)
                DrawSliced(e.Graphics, ClientRectangle, tex, 6);
        }
    }

    private sealed class bfgbutton : Button
    {
        private bool hovering;
        private bool pressing;

        public bfgbutton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Black;
            ForeColor = Color.White;
            Font = new Font("Arial", 7.5f, FontStyle.Bold);
            Padding = new Padding(0);
            TabStop = false;
            MouseEnter += (_, _) => { hovering = true; Invalidate(); };
            MouseLeave += (_, _) => { hovering = false; pressing = false; Invalidate(); };
            MouseDown += (_, _) => { pressing = true; Invalidate(); };
            MouseUp += (_, _) => { pressing = false; Invalidate(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.Black);

            if (buttonShineTex != null)
            {
                var oldInterpolation = g.InterpolationMode;
                var oldPixelOffset = g.PixelOffsetMode;
                var oldCompositing = g.CompositingQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                var dest = new Rectangle(0, 0, Width, Height);
                var src = new Rectangle(0, 0, buttonShineTex.Width, buttonShineTex.Height);
                g.DrawImage(buttonShineTex, dest, src, GraphicsUnit.Pixel);

                if (!hovering)
                {
                    using var darkBrush = new SolidBrush(Color.FromArgb(26, 0, 0, 0));
                    g.FillRectangle(darkBrush, dest);
                }

                g.InterpolationMode = oldInterpolation;
                g.PixelOffsetMode = oldPixelOffset;
                g.CompositingQuality = oldCompositing;
            }

            using var textBrush = new SolidBrush(Enabled ? Color.White : Color.FromArgb(120, 120, 120));
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            var textRect = new RectangleF(0, pressing ? 1 : 0, Width - 1, Height - 1);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            g.DrawString(Text, Font, textBrush, textRect, sf);
        }
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
