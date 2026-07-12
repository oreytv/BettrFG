@echo off
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"

:again
cls
echo BetterFG skin grouping
echo.
set /a count=0
for %%C in (Costumes Accessories Items Plinths) do (
  for /d %%D in ("%%C\*") do (
    if exist "%%~fD\info.json" (
      set /a count+=1
      set "skin[!count!]=%%~fD\info.json"
      echo !count!. %%C / %%~nxD
    )
  )
)

if !count! EQU 0 (
  echo No skins found. Put this bat in the skins repo root.
  pause
  exit /b 1
)

echo.
set "pick="
set "info="
set /p "pick=Skin number: "
for %%N in (!pick!) do set "info=!skin[%%N]!"
if not defined info goto invalid

set "group="
set /p "group=Group name (blank = Unsorted): "
set "INFO=!info!"
set "GROUP=!group!"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; $path=$env:INFO; $group=[string]$env:GROUP; $group=$group.Trim(); $json=Get-Content -LiteralPath $path -Raw | ConvertFrom-Json; if ([string]::IsNullOrWhiteSpace($group)) { if ($json.PSObject.Properties.Name -contains 'group') { $json.PSObject.Properties.Remove('group') } } else { $json | Add-Member -NotePropertyName group -NotePropertyValue $group -Force }; $json | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -Encoding utf8"
if errorlevel 1 (
  echo Failed to update !info!
  pause
  exit /b 1
)

if exist "generate_catalog.bat" call "generate_catalog.bat"
echo.
echo Updated.
set "again="
set /p "again=Group another skin? [y/N]: "
if /i "!again!"=="y" goto again
exit /b 0

:invalid
echo Invalid skin number.
pause
goto again
