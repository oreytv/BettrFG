param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)

$ErrorActionPreference = "Stop"

$projectRoot = $PSScriptRoot
$repoRoot = Split-Path -Parent $projectRoot
$publishDir = Join-Path $projectRoot ("publish\" + $Configuration + "\" + $Runtime)
$propsPath = Join-Path $repoRoot "Directory.Build.props"

if (!(Test-Path $propsPath)) {
    throw "Directory.Build.props missing: $propsPath"
}

[xml]$propsXml = Get-Content $propsPath -Raw
$props = $propsXml.Project.PropertyGroup | Select-Object -First 1
$version = $props.BettrFGVersion
$downloadsDir = Join-Path ([Environment]::GetFolderPath("UserProfile")) "Downloads"
$installerExeName = "BettrFG.Installer.exe"
$downloadsExe = Join-Path $downloadsDir $installerExeName
$selfContainedFlag = if ($SelfContained) { "true" } else { "false" }
$singleFileArgs = @(
    "publish",
    (Join-Path $projectRoot "BetterFG.Installer.csproj"),
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContainedFlag,
    "-p:PublishSingleFile=true",
    "-p:DebugType=None",
    "-p:DebugSymbols=false",
    "-o", $publishDir
)

if ($SelfContained) {
    $singleFileArgs += "-p:IncludeNativeLibrariesForSelfExtract=true"
    $singleFileArgs += "-p:EnableCompressionInSingleFile=true"
}

dotnet @singleFileArgs

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (!(Test-Path $downloadsDir)) {
    New-Item -ItemType Directory -Path $downloadsDir | Out-Null
}

$publishedExe = Join-Path $publishDir $installerExeName
if (!(Test-Path $publishedExe)) {
    throw "published installer exe missing: $publishedExe"
}

$running = Get-Process -Name "BettrFG.Installer" -ErrorAction SilentlyContinue
if ($running) {
    try {
        Start-Process -FilePath "taskkill.exe" -ArgumentList "/F", "/IM", $installerExeName, "/T" -Verb RunAs -Wait -WindowStyle Hidden
        Start-Sleep -Milliseconds 500
    }
    catch {
        Write-Warning "couldnt close the running installer (UAC declined?): $($_.Exception.Message)"
    }
}

try {
    Copy-Item -LiteralPath $publishedExe -Destination $downloadsExe -Force
    Write-Host "installer copied to $downloadsExe"
}
catch {
    Write-Warning "couldnt copy to Downloads, installer still open. grab it from $publishDir"
}

Write-Host "installer published to $publishDir"
Write-Host "self-contained: $selfContainedFlag"
