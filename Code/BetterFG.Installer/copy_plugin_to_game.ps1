param(
    [Parameter(Mandatory = $true)][ValidateSet("Steam", "Epic")][string]$Store,
    [Parameter(Mandatory = $true)][string]$BuildOutput
)

$ErrorActionPreference = "Stop"
$BuildOutput = $BuildOutput.Trim('"')

if (!(Test-Path $BuildOutput)) {
    throw "build output missing: $BuildOutput"
}

function Get-SteamFallGuysFolders {
    $roots = New-Object System.Collections.Generic.List[string]

    $steamPath = $null
    foreach ($k in @("HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKCU:\Software\Valve\Steam")) {
        try {
            $v = (Get-ItemProperty -Path $k -Name InstallPath -ErrorAction Stop).InstallPath
            if ($v) { $steamPath = $v; break }
        } catch { }
    }
    if (-not $steamPath) { return @() }

    $roots.Add($steamPath)
    $vdf = Join-Path $steamPath "steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
        # only the library "path" lines matter. matching them by name dodges the appid entries in the
        # "apps" sub-block, which also look like "<digits>" "<value>" and used to trip the parse up.
        foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s*"([^"]+)"')) {
            $roots.Add(($m.Groups[1].Value -replace '\\\\', '\'))
        }
    }

    $roots |
        Where-Object { $_ } |
        ForEach-Object { Join-Path $_ "steamapps\common\Fall Guys" } |
        Where-Object { Test-Path (Join-Path $_ "FallGuys_client_game.exe") } |
        Select-Object -Unique
}

function Get-EpicFallGuysFolders {
    $locations = New-Object System.Collections.Generic.List[string]
    $epicData = Join-Path ([Environment]::GetFolderPath("CommonApplicationData")) "Epic"

    $manifestDir = Join-Path $epicData "EpicGamesLauncher\Data\Manifests"
    if (Test-Path $manifestDir) {
        foreach ($item in Get-ChildItem -Path $manifestDir -Filter *.item -File) {
            try {
                $j = Get-Content $item.FullName -Raw | ConvertFrom-Json
                if ($j.InstallLocation) { $locations.Add($j.InstallLocation) }
            } catch { }
        }
    }

    $launcherDat = Join-Path $epicData "UnrealEngineLauncher\LauncherInstalled.dat"
    if (Test-Path $launcherDat) {
        try {
            $j = Get-Content $launcherDat -Raw | ConvertFrom-Json
            foreach ($entry in $j.InstallationList) {
                if ($entry.InstallLocation) { $locations.Add($entry.InstallLocation) }
            }
        } catch { }
    }

    $locations |
        Where-Object { $_ -and (Test-Path (Join-Path $_ "FallGuys_client_game.exe")) } |
        Select-Object -Unique
}

$folders = if ($Store -eq "Steam") { Get-SteamFallGuysFolders } else { Get-EpicFallGuysFolders }

if (-not $folders -or $folders.Count -eq 0) {
    Write-Host "no $Store Fall Guys install found, nothing copied"
    return
}

$files = @("BetterFG.dll", "BetterFG.pdb", "FallGuysLib.dll")

foreach ($game in $folders) {
    $pluginDir = Join-Path $game "BepInEx\plugins\BetterFG"
    New-Item -ItemType Directory -Path $pluginDir -Force | Out-Null
    foreach ($name in $files) {
        $src = Join-Path $BuildOutput $name
        if (Test-Path $src) {
            Copy-Item -LiteralPath $src -Destination (Join-Path $pluginDir $name) -Force
        }
    }
    Write-Host "$Store -> plugin copied to $pluginDir"
}
