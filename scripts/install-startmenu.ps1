# Installs the launcher for the current user and adds "ROUNDS Modkit" to the Start Menu (and so
# to Windows search). Re-run to update; -Uninstall removes it. Needs the .NET 8 SDK.
#   powershell -ExecutionPolicy Bypass -File scripts\install-startmenu.ps1
param([switch]$Uninstall)
$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$Dest = Join-Path $env:LOCALAPPDATA "Programs\RoundsModkit"
$Exe = Join-Path $Dest "RoundsModkit.exe"
$Shortcut = Join-Path ([Environment]::GetFolderPath("Programs")) "ROUNDS Modkit.lnk"

if (Get-Process -Name RoundsModkit -ErrorAction SilentlyContinue) {
    throw "ROUNDS Modkit is running. Close it and run this script again."
}

if ($Uninstall) {
    Remove-Item -Force -ErrorAction SilentlyContinue $Shortcut
    Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $Dest
    Write-Host "Removed."
    exit 0
}

$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"
dotnet publish (Join-Path $Root "src\RoundsModkit.Launcher") -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none `
    -o (Join-Path $Root "artifacts\win-x64") | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

New-Item -ItemType Directory -Force $Dest | Out-Null
Copy-Item -Force (Join-Path $Root "artifacts\win-x64\RoundsModkit.exe") $Exe

# Borrow the game's own icon when ROUNDS is in the main Steam library; the launcher has none yet.
$Icon = "$Exe,0"
$SteamPath = (Get-ItemProperty -Path "HKCU:\Software\Valve\Steam" -Name SteamPath -ErrorAction SilentlyContinue).SteamPath
if ($SteamPath) {
    $RoundsExe = Join-Path ($SteamPath -replace "/", "\") "steamapps\common\ROUNDS\Rounds.exe"
    if (Test-Path $RoundsExe) { $Icon = "$RoundsExe,0" }
}

$Shell = New-Object -ComObject WScript.Shell
$Link = $Shell.CreateShortcut($Shortcut)
$Link.TargetPath = $Exe
$Link.WorkingDirectory = $Dest
$Link.IconLocation = $Icon
$Link.Description = "Manage BepInEx mods and settings for ROUNDS"
$Link.Save()

Write-Host "Installed: $Exe"
Write-Host "Start Menu: $Shortcut"
