param(
    [string]$GameRoot = "C:\Program Files (x86)\Steam\steamapps\common\Fall Guys",
    [string]$BuildOutput = ""
)

$ErrorActionPreference = "Stop"

$installerRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $installerRoot
$payloadDir = Join-Path $installerRoot "payload"
$stageDir = Join-Path $installerRoot "payload_stage"
$pluginOnlyStageDir = Join-Path $installerRoot "plugin_only_stage"
$propsPath = Join-Path $repoRoot "Directory.Build.props"
$pluginStage = Join-Path $stageDir "BepInEx\plugins\BetterFG"
$pluginOnlyFolder = Join-Path $pluginOnlyStageDir "BetterFG"
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

$releaseZipName = "bettrfg_plugin.zip"
$legacyReleaseZipName = "betterfg_plugin.zip"
$dottedZipName = "$displayName.$modVersion.zip"
$bepinexZipName = "BepInEx.zip"
$zipPath = Join-Path $payloadDir $bepinexZipName
$pluginZipPath = Join-Path $payloadDir $releaseZipName
$legacyPluginZipPath = Join-Path $payloadDir $legacyReleaseZipName
$dottedPluginZipPath = Join-Path $payloadDir $dottedZipName
$downloadsZipPath = Join-Path ([Environment]::GetFolderPath("UserProfile")) ("Downloads\" + $bepinexZipName)
$downloadsPluginZipPath = Join-Path ([Environment]::GetFolderPath("UserProfile")) ("Downloads\" + $releaseZipName)
$downloadsLegacyPluginZipPath = Join-Path ([Environment]::GetFolderPath("UserProfile")) ("Downloads\" + $legacyReleaseZipName)
$downloadsDottedPluginZipPath = Join-Path ([Environment]::GetFolderPath("UserProfile")) ("Downloads\" + $dottedZipName)
$oldPayloadZipPath = Join-Path $payloadDir "betterfg_payload.zip"
$oldPrettyPluginZipPath = Join-Path $payloadDir "$displayName ($modVersion).zip"
$oldDownloadsPrettyPluginZipPath = Join-Path ([Environment]::GetFolderPath("UserProfile")) ("Downloads\$displayName ($modVersion).zip")

if (!(Test-Path $GameRoot)) {
    throw "game root missing: $GameRoot"
}

if (!(Test-Path $payloadDir)) {
    New-Item -ItemType Directory -Path $payloadDir | Out-Null
}

if (Test-Path $stageDir) {
    Remove-Item -LiteralPath $stageDir -Recurse -Force
}

if (Test-Path $pluginOnlyStageDir) {
    Remove-Item -LiteralPath $pluginOnlyStageDir -Recurse -Force
}

New-Item -ItemType Directory -Path $stageDir | Out-Null
New-Item -ItemType Directory -Path $pluginOnlyStageDir | Out-Null

function Copy-FromGame {
    param(
        [string]$RelativePath
    )

    $src = Join-Path $GameRoot $RelativePath
    $dst = Join-Path $stageDir $RelativePath

    if (!(Test-Path $src)) {
        throw "missing game file for payload: $RelativePath"
    }

    if ((Get-Item $src) -is [System.IO.DirectoryInfo]) {
        Copy-Item -LiteralPath $src -Destination $dst -Recurse -Force
    }
    else {
        $dstDir = Split-Path -Parent $dst
        if ($dstDir -and !(Test-Path $dstDir)) {
            New-Item -ItemType Directory -Path $dstDir | Out-Null
        }
        Copy-Item -LiteralPath $src -Destination $dst -Force
    }
}

Copy-FromGame "dotnet"
Copy-FromGame "doorstop_config.ini"
Copy-FromGame "INSTALL_CEP_CE.bat"
Copy-FromGame "TURN_OFF_CEP_CE.bat"
if (Test-Path (Join-Path $GameRoot "winhttp.dll")) {
    Copy-FromGame "winhttp.dll"
}
elseif (Test-Path (Join-Path $GameRoot "BatchData\Modded\winhttp.dll")) {
    $winhttpDst = Join-Path $stageDir "winhttp.dll"
    Copy-Item -LiteralPath (Join-Path $GameRoot "BatchData\Modded\winhttp.dll") -Destination $winhttpDst -Force
}
else {
    throw "missing game file for payload: winhttp.dll"
}
Copy-FromGame "BatchData"
Copy-FromGame "BepInEx\core"
Copy-FromGame "BepInEx\patchers"
Copy-FromGame "BepInEx\plugins\BetterFG"

$pluginJunk = @(
    "CachedRoundSplashScreens",
    "Settings",
    "woop",
    "debug_profiles.json"
)

foreach ($junk in $pluginJunk) {
    $junkPath = Join-Path $pluginStage $junk
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
            Copy-Item -LiteralPath $src -Destination (Join-Path $pluginStage $name) -Force
        }
    }

    $libsSource = Join-Path $BuildOutput "Libs"
    if (Test-Path $libsSource) {
        $libsDest = Join-Path $pluginStage "Libs"
        if (Test-Path $libsDest) {
            Remove-Item -LiteralPath $libsDest -Recurse -Force
        }
        Copy-Item -LiteralPath $libsSource -Destination $libsDest -Recurse -Force
    }
}

if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

if (Test-Path $pluginZipPath) {
    Remove-Item -LiteralPath $pluginZipPath -Force
}

if (Test-Path $legacyPluginZipPath) {
    Remove-Item -LiteralPath $legacyPluginZipPath -Force
}

if (Test-Path $dottedPluginZipPath) {
    Remove-Item -LiteralPath $dottedPluginZipPath -Force
}

if (Test-Path $oldPayloadZipPath) {
    Remove-Item -LiteralPath $oldPayloadZipPath -Force
}

if (Test-Path $oldPrettyPluginZipPath) {
    Remove-Item -LiteralPath $oldPrettyPluginZipPath -Force
}

if (Test-Path $oldDownloadsPrettyPluginZipPath) {
    Remove-Item -LiteralPath $oldDownloadsPrettyPluginZipPath -Force
}

Copy-Item -LiteralPath $pluginStage -Destination $pluginOnlyFolder -Recurse -Force
Remove-Item -LiteralPath $pluginStage -Recurse -Force

Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
Compress-Archive -Path (Join-Path $pluginOnlyStageDir "*") -DestinationPath $pluginZipPath -CompressionLevel Optimal
Copy-Item -LiteralPath $pluginZipPath -Destination $legacyPluginZipPath -Force
Copy-Item -LiteralPath $pluginZipPath -Destination $dottedPluginZipPath -Force
Copy-Item -LiteralPath $zipPath -Destination $downloadsZipPath -Force
Copy-Item -LiteralPath $pluginZipPath -Destination $downloadsPluginZipPath -Force
Copy-Item -LiteralPath $legacyPluginZipPath -Destination $downloadsLegacyPluginZipPath -Force
Copy-Item -LiteralPath $dottedPluginZipPath -Destination $downloadsDottedPluginZipPath -Force
Write-Host "payload zip rebuilt at $zipPath"
Write-Host "plugin zip rebuilt at $pluginZipPath"
Write-Host "legacy plugin zip copied to $legacyPluginZipPath"
Write-Host "plugin zip copied to $dottedPluginZipPath"
Write-Host "BepInEx zip copied to $downloadsZipPath"
Write-Host "$releaseZipName copied to $downloadsPluginZipPath"
Write-Host "$legacyReleaseZipName copied to $downloadsLegacyPluginZipPath"
Write-Host "$dottedZipName copied to $downloadsDottedPluginZipPath"
