$name = "BettrFG.Installer"
$exe = "$name.exe"

if (-not (Get-Process -Name $name -ErrorAction SilentlyContinue)) { return }

cmd.exe /c "taskkill /F /IM $exe /T >nul 2>&1"
Start-Sleep -Milliseconds 300

if (Get-Process -Name $name -ErrorAction SilentlyContinue) {
    try {
        Start-Process -FilePath "taskkill.exe" -ArgumentList "/F", "/IM", $exe, "/T" -Verb RunAs -Wait -WindowStyle Hidden
        Start-Sleep -Milliseconds 500
    } catch { }
}
