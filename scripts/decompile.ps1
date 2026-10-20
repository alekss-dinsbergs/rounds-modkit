# Windows counterpart of decompile.sh. Output stays local (gitignored).
param(
    [string]$RoundsDir = "C:\Program Files (x86)\Steam\steamapps\common\ROUNDS"
)
$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Managed = Join-Path $RoundsDir "ROUNDS_Data\Managed"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
# ilspycmd targets net6; the SDK installed for the launcher is net8.
$env:DOTNET_ROLL_FORWARD = "Major"

if (-not (Test-Path (Join-Path $Managed "Assembly-CSharp.dll"))) { throw "ROUNDS not found at $RoundsDir (pass -RoundsDir)" }

$Manifest = Join-Path (Split-Path -Parent (Split-Path -Parent $RoundsDir)) "appmanifest_1557740.acf"
$Build = "unknown"; $Branch = "public"
if (Test-Path $Manifest) {
    $text = Get-Content $Manifest -Raw
    if ($text -match '"buildid"\s+"(\d+)"') { $Build = $Matches[1] }
    if ($text -match '"BetaKey"\s+"([^"]+)"') { $Branch = $Matches[1] }
}
if ($Branch -ne "old-rounds-for-mods") { Write-Warning "installed branch is '$Branch', not old-rounds-for-mods" }

$Out = Join-Path $Root "decompiled\$Build"
New-Item -ItemType Directory -Force $Out, (Join-Path $Root "lib") | Out-Null

if (-not (Get-Command ilspycmd -ErrorAction SilentlyContinue)) { dotnet tool install -g ilspycmd --version 8.2.0.7535 }
if (-not (Get-Command assembly-publicizer -ErrorAction SilentlyContinue)) { dotnet tool install -g BepInEx.AssemblyPublicizer.Cli }

foreach ($asm in "Assembly-CSharp", "Assembly-CSharp-firstpass") {
    $dest = Join-Path $Out $asm
    if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
    ilspycmd -p -o $dest -r $Managed --nested-directories (Join-Path $Managed "$asm.dll")
    assembly-publicizer (Join-Path $Managed "$asm.dll") -o (Join-Path $Root "lib\$asm.dll") --overwrite
}
"$Build $Branch" | Set-Content (Join-Path $Out "SOURCE")
Write-Host "Done: $Out"
