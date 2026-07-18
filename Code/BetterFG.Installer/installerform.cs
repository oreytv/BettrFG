#pragma warning disable CS8981
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace BetterFG.Installer;

public sealed partial class installerform : Form
{
    private static readonly HttpClient http = new HttpClient();

    private readonly bfgpanel shellPanel;
    private readonly TextBox gamePathBox;
    private readonly TextBox logBox;
    private readonly bfgbutton installButton;
    private readonly bfgbutton modifyButton;
    private readonly bfgbutton uninstallButton;
    private readonly bfgbutton opInstallButton;
    private readonly Label statusLabel;
    private readonly Label hubStatusLabel;
    private readonly Label hubFolderLabel;
    private readonly ProgressBar progressBar;
    private readonly ToolTip installTip = new ToolTip();

    private readonly Label stepTitleLabel;
    private readonly bfgbutton steamButton;
    private readonly bfgbutton epicButton;
    private readonly bfgbutton browseButton;
    private readonly bfgbutton openFolderButton;
    private readonly bfgbutton launchButton;
    private readonly bfgbutton backButton;
    private readonly ComboBox versionCombo;
    private string? steamPath;
    private string? epicPath;
    private string? lastBrowsedFolder;
    private readonly List<Control> step1Controls = new();
    private readonly List<Control> step2Controls = new();
    private readonly List<Control> step3Controls = new();
    private readonly List<Control> logControls = new();
    private int currentStep = 1;
    private bfgop currentOp;

    private readonly Form fadeOverlay;
    private readonly System.Windows.Forms.Timer fadeTimer;
    private readonly Stopwatch fadeClock = new();
    private int fadePendingStep;
    private bool fadePhase2;
    private bool fadeSwap;
    private bool fadeFull;
    private const int FadeMs = 1000;

    private releaseinfo? latestRelease;
    private releaseinfo? selectedRelease;
    private List<releaseinfo> availableReleases = new();
    private bool releaseCheckRunning;

    private static readonly Bitmap buttonShineTex = LoadBitmap("assets.button_shine_pinkbg.png");
    private static readonly Bitmap blueButtonShineTex = LoadBitmap("assets.button_shine_bluebg.png");
    private static readonly Bitmap yellowButtonShineTex = LoadBitmap("assets.button_shine_yellowbg.png");
    private static readonly Bitmap steamIconTex = LoadBitmap("assets.icon_logo_steam.png");
    private static readonly Bitmap epicIconTex = LoadBitmap("assets.icon_logo_epic.png");
    private static readonly Bitmap windowTex = LoadBitmap("assets.ui.windows.generalbg.png");
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
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        ClientSize = new Size(760, 500);
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
        shellPanel.Controls.Add(logo);

        stepTitleLabel = new Label
        {
            Text = "Step 1 of 3 / Choose your Fall Guys",
            Font = new Font("Arial", 11f, FontStyle.Bold),
            AutoSize = true,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Location = new Point(9, 62)
        };
        shellPanel.Controls.Add(stepTitleLabel);

        backButton = new bfgbutton
        {
            Text = "Back",
            Location = new Point(654, 28),
            Size = new Size(90, 30)
        };
        backButton.Click += (_, _) => { if (currentStep > 1) BeginFade(currentStep - 1); };
        shellPanel.Controls.Add(backButton);

        var installs = GetFallGuysInstalls();
        steamPath = installs.FirstOrDefault(x => x.Store == "Steam").Path;
        epicPath = installs.FirstOrDefault(x => x.Store == "Epic").Path;
        lastBrowsedFolder = ReadLastBrowsedFolder();

        gamePathBox = new TextBox { Visible = false };
        gamePathBox.TextChanged += (_, _) => RefreshInstallState();
        shellPanel.Controls.Add(gamePathBox);

        steamButton = new bfgbutton
        {
            Text = "Steam",
            TextAlign = ContentAlignment.MiddleLeft,
            LeftIcon = steamIconTex,
            Location = new Point(226, 178),
            Size = new Size(300, 50)
        };
        steamButton.Click += (_, _) => ChooseAndAdvance(steamPath);
        shellPanel.Controls.Add(steamButton);
        step1Controls.Add(steamButton);

        epicButton = new bfgbutton
        {
            Text = "Epic",
            TextAlign = ContentAlignment.MiddleLeft,
            LeftIcon = epicIconTex,
            Location = new Point(226, 242),
            Size = new Size(300, 50)
        };
        epicButton.Click += (_, _) => ChooseAndAdvance(epicPath);
        shellPanel.Controls.Add(epicButton);
        step1Controls.Add(epicButton);

        browseButton = new bfgbutton
        {
            Text = "Browse",
            Location = new Point(226, 306),
            Size = new Size(300, 50)
        };
        browseButton.Click += (_, _) => BrowseGameFolder();
        shellPanel.Controls.Add(browseButton);
        step1Controls.Add(browseButton);

        hubFolderLabel = new Label
        {
            Text = "",
            Font = new Font("Arial", 8.25f, FontStyle.Regular),
            AutoSize = true,
            ForeColor = Color.FromArgb(190, 190, 190),
            BackColor = Color.Transparent,
            Location = new Point(10, 100)
        };

        hubStatusLabel = new Label
        {
            Text = "checking install...",
            Font = new Font("Arial", 12f, FontStyle.Bold),
            AutoSize = true,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Location = new Point(10, 122)
        };

        installButton = new bfgbutton
        {
            Text = "Install",
            Style = bfgstyle.Blue,
            Location = new Point(226, 196),
            Size = new Size(300, 46)
        };
        installButton.Click += (_, _) => { currentOp = bfgop.Install; BeginFade(3); };

        modifyButton = new bfgbutton
        {
            Text = "Install a specific version",
            Location = new Point(226, 250),
            Size = new Size(300, 40)
        };
        modifyButton.Click += (_, _) => { currentOp = bfgop.Modify; BeginFade(3); };

        uninstallButton = new bfgbutton
        {
            Text = "Uninstall",
            Location = new Point(226, 298),
            Size = new Size(300, 40)
        };
        uninstallButton.Click += (_, _) => StartUninstall();

        step2Controls.Add(hubFolderLabel);
        step2Controls.Add(hubStatusLabel);
        step2Controls.Add(installButton);
        step2Controls.Add(modifyButton);
        step2Controls.Add(uninstallButton);

        versionCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Arial", 8.5f, FontStyle.Bold),
            BackColor = Color.Black,
            ForeColor = Color.White,
            Location = new Point(10, 124),
            Size = new Size(180, 24)
        };
        versionCombo.SelectedIndexChanged += (_, _) =>
        {
            if (versionCombo.SelectedIndex >= 0 && versionCombo.SelectedIndex < availableReleases.Count)
                selectedRelease = availableReleases[versionCombo.SelectedIndex];
        };

        opInstallButton = new bfgbutton
        {
            Text = "Install this version",
            Style = bfgstyle.Blue,
            Location = new Point(200, 122),
            Size = new Size(170, 28)
        };
        opInstallButton.Click += async (_, _) => await InstallBetterfgAsync();

        openFolderButton = new bfgbutton
        {
            Text = "Open plugins folder",
            Location = new Point(10, 160),
            Size = new Size(165, 28)
        };
        openFolderButton.Click += (_, _) => OpenPluginsFolder();

        launchButton = new bfgbutton
        {
            Text = "Launch Fall Guys",
            Style = bfgstyle.Blue,
            Location = new Point(181, 160),
            Size = new Size(160, 28)
        };
        launchButton.Click += (_, _) => LaunchGame();

        step3Controls.Add(versionCombo);
        step3Controls.Add(opInstallButton);
        step3Controls.Add(openFolderButton);
        step3Controls.Add(launchButton);

        statusLabel = new Label
        {
            Text = "status: waiting",
            Font = new Font("Arial", 8.25f, FontStyle.Regular),
            ForeColor = Color.FromArgb(170, 170, 170),
            AutoSize = true,
            BackColor = Color.Transparent,
            Location = new Point(10, 196)
        };

        logBox = new TextBox
        {
            Location = new Point(9, 214),
            Size = new Size(735, 244),
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
            Location = new Point(9, 463),
            Size = new Size(735, 15),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous
        };

        logControls.Add(statusLabel);
        logControls.Add(logBox);
        logControls.Add(progressBar);

        shellPanel.Controls.Add(hubFolderLabel);
        shellPanel.Controls.Add(hubStatusLabel);
        shellPanel.Controls.Add(installButton);
        shellPanel.Controls.Add(modifyButton);
        shellPanel.Controls.Add(uninstallButton);
        shellPanel.Controls.Add(versionCombo);
        shellPanel.Controls.Add(opInstallButton);
        shellPanel.Controls.Add(openFolderButton);
        shellPanel.Controls.Add(launchButton);
        shellPanel.Controls.Add(statusLabel);
        shellPanel.Controls.Add(logBox);
        shellPanel.Controls.Add(progressBar);

        Log($"{installerstuff.DisplayName} installer booted");
        if (stampedPath != null)
            Log("stamped installer path into " + installerstuff.InstallerPathStampFile + " so the in-game updater can find me");
        else
            Log("couldnt stamp installer path (running from a weird spot?), in-game updater will fall back to the release page");
        Log("release page: " + installerstuff.ReleasePageUrl);
        Log($"found {installs.Count} Fall Guys install(s): steam={(steamPath != null)}, epic={(epicPath != null)}");

        fadeOverlay = new fadeform
        {
            FormBorderStyle = FormBorderStyle.None,
            BackColor = Color.Black,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Opacity = 0
        };
        fadeTimer = new System.Windows.Forms.Timer { Interval = 15 };
        fadeTimer.Tick += FadeTick;

        ShowStep(1);
        Shown += (_, _) => BeginFadeIn();
        Shown += async (_, _) => await RefreshReleaseStateAsync();
    }

    private void BeginFade(int nextStep)
    {
        if (fadeTimer.Enabled)
        {
            ShowStep(nextStep);
            return;
        }

        fadePendingStep = nextStep;
        fadeSwap = true;
        fadeFull = false;
        fadePhase2 = false;
        fadeOverlay.Owner = this;
        fadeOverlay.Bounds = RectangleToScreen(new Rectangle(0, 88, ClientSize.Width, ClientSize.Height - 88));
        fadeOverlay.Opacity = 0;
        fadeOverlay.Show();
        fadeClock.Restart();
        fadeTimer.Start();
    }

    private void BeginFadeIn()
    {
        fadeSwap = false;
        fadeFull = true;
        fadePhase2 = true;
        fadeOverlay.Owner = this;
        fadeOverlay.Bounds = RectangleToScreen(ClientRectangle);
        fadeOverlay.Opacity = 1;
        fadeOverlay.Show();
        fadeClock.Restart();
        fadeTimer.Start();
    }

    private void FadeTick(object? sender, EventArgs e)
    {
        fadeOverlay.Bounds = RectangleToScreen(fadeFull ? ClientRectangle : new Rectangle(0, 88, ClientSize.Width, ClientSize.Height - 88));
        var t = fadeClock.ElapsedMilliseconds / (double)FadeMs;

        if (!fadePhase2)
        {
            fadeOverlay.Opacity = Math.Min(1.0, t);
            if (t >= 1.0)
            {
                if (fadeSwap)
                    ShowStep(fadePendingStep);
                fadePhase2 = true;
                fadeClock.Restart();
            }
        }
        else
        {
            fadeOverlay.Opacity = Math.Max(0.0, 1.0 - t);
            if (t >= 1.0)
            {
                fadeTimer.Stop();
                fadeClock.Reset();
                fadeOverlay.Hide();
            }
        }
    }

    private void ShowStep(int step)
    {
        currentStep = step;
        foreach (var c in step1Controls) c.Visible = step == 1;
        foreach (var c in step2Controls) c.Visible = step == 2;
        foreach (var c in step3Controls) c.Visible = step == 3;
        foreach (var c in logControls) c.Visible = step == 3;

        stepTitleLabel.Visible = step > 1;
        if (step == 2)
            stepTitleLabel.Text = "Manage " + installerstuff.DisplayName;

        if (step == 1)
            RefreshLauncherButtons();

        RefreshInstallState();

        if (step == 3)
            EnterOperation();
    }

    private void EnterOperation()
    {
        var modify = currentOp == bfgop.Modify;
        versionCombo.Visible = modify;
        opInstallButton.Visible = modify;

        if (currentOp == bfgop.Install)
        {
            stepTitleLabel.Text = "Installing";
            _ = InstallBetterfgAsync();
        }
        else if (currentOp == bfgop.Uninstall)
        {
            stepTitleLabel.Text = "Uninstalling";
            UninstallBetterfg();
        }
        else
        {
            stepTitleLabel.Text = "Install a specific version";
        }
    }

    private void ChooseAndAdvance(string? path)
    {
        if (path == null)
            return;
        gamePathBox.Text = path;
        BeginFade(2);
    }

    private void BrowseGameFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Pick your Fall Guys folder"
        };

        var start = lastBrowsedFolder ?? steamPath ?? epicPath;
        if (!string.IsNullOrWhiteSpace(start) && Directory.Exists(start))
            dialog.SelectedPath = start;

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var chosen = dialog.SelectedPath;
        if (!File.Exists(Path.Combine(chosen, "FallGuys_client_game.exe")))
        {
            MessageBox.Show(this, "That folder doesn't have FallGuys_client_game.exe in it. Pick the actual Fall Guys folder.", "Wrong folder", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        lastBrowsedFolder = chosen;
        WriteLastBrowsedFolder(chosen);
        gamePathBox.Text = chosen;
        BeginFade(2);
    }

    private void RefreshLauncherButtons()
    {
        ConfigLauncher(steamButton, "Steam", steamPath, false);
        ConfigLauncher(epicButton, "Epic", epicPath, false);
        ConfigLauncher(browseButton, "Browse", lastBrowsedFolder, true);
    }

    private void ConfigLauncher(bfgbutton btn, string label, string? path, bool isBrowse)
    {
        var valid = path != null && File.Exists(Path.Combine(path, "FallGuys_client_game.exe"));
        installTip.SetToolTip(btn, valid ? path! : "");

        if (!valid)
        {
            btn.Text = isBrowse ? "Browse for your Fall Guys folder" : label + "  -  Fall Guys not found here";
            btn.Enabled = isBrowse;
            btn.Style = bfgstyle.Pink;
            return;
        }

        btn.Enabled = true;
        var installed = GetInstalledVersion(path!);
        if (installed != null)
        {
            btn.Text = $"{label}  -  {installerstuff.DisplayName} {VersionText(installed)} installed";
            btn.Style = bfgstyle.Yellow;
        }
        else
        {
            btn.Text = $"{label}  -  mods not installed";
            btn.Style = bfgstyle.Pink;
        }
    }

    private void RefreshInstallState()
    {
        var gameFolder = TryGetGameFolderQuiet(gamePathBox.Text);
        backButton.Visible = currentStep > 1;
        backButton.Enabled = currentStep > 1;

        hubFolderLabel.Text = gameFolder == null ? "no Fall Guys folder picked" : "folder: " + gameFolder;

        var installedVersion = gameFolder == null ? null : GetInstalledVersion(gameFolder);
        var latestText = latestRelease?.version;

        if (installedVersion == null)
        {
            hubStatusLabel.Text = latestRelease == null
                ? $"{installerstuff.DisplayName} is not installed here"
                : $"{installerstuff.DisplayName} is not installed  -  latest is {latestText}";
            installButton.Text = latestRelease == null ? "Install" : $"Install {latestText}";
        }
        else
        {
            var iv = VersionText(installedVersion);
            if (latestRelease != null && Version.TryParse(latestRelease.version, out var lv) && NormalizeVersion(installedVersion) < NormalizeVersion(lv))
            {
                hubStatusLabel.Text = $"{installerstuff.DisplayName} {iv} installed  -  update to {latestText} available";
                installButton.Text = $"Update to {latestText}";
            }
            else if (latestRelease != null)
            {
                hubStatusLabel.Text = $"{installerstuff.DisplayName} {iv} installed  -  up to date";
                installButton.Text = $"Reinstall {latestText}";
            }
            else
            {
                hubStatusLabel.Text = $"{installerstuff.DisplayName} {iv} installed";
                installButton.Text = "Reinstall";
            }
        }

        installButton.Enabled = gameFolder != null && latestRelease != null;
        modifyButton.Enabled = gameFolder != null && availableReleases.Count > 0;
        uninstallButton.Visible = currentStep == 2 && installedVersion != null;

        var isSteam = gameFolder != null && gameFolder.IndexOf("steamapps", StringComparison.OrdinalIgnoreCase) >= 0;
        launchButton.Visible = currentStep == 3 && installedVersion != null && isSteam;
    }

    private void Log(string text)
    {
        logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
    }

    private void SetBusy(bool busy, string statusText)
    {
        installButton.Enabled = !busy;
        modifyButton.Enabled = !busy;
        uninstallButton.Enabled = !busy;
        opInstallButton.Enabled = !busy;
        backButton.Enabled = !busy && currentStep > 1;
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
}
#pragma warning restore CS8981
