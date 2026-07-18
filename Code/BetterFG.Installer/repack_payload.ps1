param(
    [string]$GameRoot = "C:\Program Files (x86)\Steam\steamapps\common\Fall Guys",
    [string]$BuildOutput = ""
)

$ErrorActionPreference = "Stop"

$installerRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $installerRoot
$payloadDir = Join-Path $installerRoot "payload"
$pluginOnlyStageDir = Join-Path $installerRoot "plugin_only_stage"
$propsPath = Join-Path $repoRoot "Directory.Build.props"
$pluginOnlyFolder = Join-Path $pluginOnlyStageDir "BetterFG"
$gamePluginFolder = Join-Path $GameRoot "BepInEx\plugins\BetterFG"
$BuildOutput = $BuildOutput.Trim('"')

if (!(Test-Path $propsPath)) {
    throw "Directory.Build.props missing: $propsPath"
}

[xml]$propsXml = Get-Content $propsPath -Raw
$props = $propsXml.Project.PropertyGroup | Select-Object -First 1
$modVersion = $props.BettrFGVersion
$displayName = $props.BettrFGDisplayName

if ([string]::IsNullOrWhiteSpace($modVersion)) {
    throw "couldnt read BettrFGVersion from Directory.Build.props"
}

if ([string]::IsNullOrWhiteSpace($displayName)) {
    $displayName = "BettrFG"
}

if (!(Test-Path $gamePluginFolder)) {
    throw "game plugin folder missing: $gamePluginFolder"
}

$releaseZipName = "bettrfg_plugin.zip"
$legacyReleaseZipName = "betterfg_plugin.zip"
$dottedZipName = "$displayName.$modVersion.zip"

$pluginZipPath = Join-Path $payloadDir $releaseZipName
$legacyPluginZipPath = Join-Path $payloadDir $legacyReleaseZipName
$dottedPluginZipPath = Join-Path $payloadDir $dottedZipName

$downloadsDir = Join-Path ([Environment]::GetFolderPath("UserProfile")) "Downloads"
$downloadsPluginZipPath = Join-Path $downloadsDir $releaseZipName
$downloadsLegacyPluginZipPath = Join-Path $downloadsDir $legacyReleaseZipName
$downloadsDottedPluginZipPath = Join-Path $downloadsDir $dottedZipName

$staleBepInEx = @(
    (Join-Path $payloadDir "BepInEx.zip"),
    (Join-Path $downloadsDir "BepInEx.zip")
)
foreach ($stale in $staleBepInEx) {
    if (Test-Path $stale) {
        Remove-Item -LiteralPath $stale -Force
    }
}

if (!(Test-Path $payloadDir)) {
    New-Item -ItemType Directory -Path $payloadDir | Out-Null
}

if (Test-Path $pluginOnlyStageDir) {
    Remove-Item -LiteralPath $pluginOnlyStageDir -Recurse -Force
}

New-Item -ItemType Directory -Path $pluginOnlyStageDir | Out-Null
Copy-Item -LiteralPath $gamePluginFolder -Destination $pluginOnlyFolder -Recurse -Force

$pluginJunk = @(
    "CachedRoundSplashScreens",
    "Settings",
    "woop",
    "debug_profiles.json"
)

foreach ($junk in $pluginJunk) {
    $junkPath = Join-Path $pluginOnlyFolder $junk
    if (Test-Path $junkPath) {
        Remove-Item -LiteralPath $junkPath -Recurse -Force
    }
}

if ($BuildOutput -and (Test-Path $BuildOutput)) {
    $filesToReplace = @(
        "BetterFG.dll",
        "BetterFG.pdb",
        "FallGuysLib.dll"
    )

    foreach ($name in $filesToReplace) {
        $src = Join-Path $BuildOutput $name
        if (Test-Path $src) {
            Copy-Item -LiteralPath $src -Destination (Join-Path $pluginOnlyFolder $name) -Force
        }
    }

    $libsSource = Join-Path $BuildOutput "Libs"
    if (Test-Path $libsSource) {
        $libsDest = Join-Path $pluginOnlyFolder "Libs"
        if (Test-Path $libsDest) {
            Remove-Item -LiteralPath $libsDest -Recurse -Force
        }
        Copy-Item -LiteralPath $libsSource -Destination $libsDest -Recurse -Force
    }
}

foreach ($old in @($pluginZipPath, $legacyPluginZipPath, $dottedPluginZipPath)) {
    if (Test-Path $old) {
        Remove-Item -LiteralPath $old -Force
    }
}

Compress-Archive -Path (Join-Path $pluginOnlyStageDir "*") -DestinationPath $pluginZipPath -CompressionLevel Optimal
Copy-Item -LiteralPath $pluginZipPath -Destination $legacyPluginZipPath -Force
Copy-Item -LiteralPath $pluginZipPath -Destination $dottedPluginZipPath -Force

if (!(Test-Path $downloadsDir)) {
    New-Item -ItemType Directory -Path $downloadsDir | Out-Null
}

Copy-Item -LiteralPath $pluginZipPath -Destination $downloadsPluginZipPath -Force
Copy-Item -LiteralPath $pluginZipPath -Destination $downloadsLegacyPluginZipPath -Force
Copy-Item -LiteralPath $pluginZipPath -Destination $downloadsDottedPluginZipPath -Force

Write-Host "plugin zip rebuilt at $pluginZipPath"
Write-Host "plugin zip copied to $legacyPluginZipPath and $dottedPluginZipPath"
Write-Host "plugin zips copied to $downloadsDir"
