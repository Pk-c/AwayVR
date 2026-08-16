# Builds AwayVR and copies the plugin into the game installation.
param(
    [string]$GameDir = "H:\Steam\steamapps\common\AWAY",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$src = Join-Path $PSScriptRoot "src\AwayVR"

if (-not $SkipBuild) {
    Push-Location $src
    dotnet build -c Release -v minimal -p:GameDir=$GameDir
    if ($LASTEXITCODE -ne 0) { Pop-Location; throw "build failed" }
    Pop-Location
}

$plugins = Join-Path $GameDir "BepInEx\plugins"
if (-not (Test-Path $plugins)) { New-Item -ItemType Directory -Force $plugins | Out-Null }

$dll = Join-Path $src "bin\Release\AwayVR.dll"
Copy-Item $dll (Join-Path $plugins "AwayVR.dll") -Force
Write-Host "deployed -> $plugins\AwayVR.dll"
