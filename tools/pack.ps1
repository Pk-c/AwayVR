<#
    Assembles the AwayVR installation archive.

    Design constraint: installing must come down to unzipping into the game folder and
    launching. No patch step, no script to run.

    That forces us to ship globalgamemanagers already patched. The engine reads it before
    the mono runtime starts, and therefore before any of the mod's code could run: no
    plugin can modify it in time for the launch under way. The file holds only build
    settings — no assets, no game code.

    The original ships alongside it as .orig, which makes uninstalling clean and offline,
    without depending on a Steam integrity check.
#>
param(
    [string]$GameDir = "H:\Steam\steamapps\common\AWAY",
    [string]$OutDir  = "$PSScriptRoot\..\dist",
    [string]$Version = "0.1.0"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\.."

if (-not (Test-Path $GameDir)) { throw "Game folder not found: $GameDir" }

# --- build ---
Write-Host "Building the plugin..."
dotnet build "$root\src\AwayVR\AwayVR.csproj" -c Release /p:GameDir="$GameDir" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$dll = "$root\src\AwayVR\bin\Release\AwayVR.dll"
if (-not (Test-Path $dll)) { throw "AwayVR.dll not found after the build" }

# --- check that globalgamemanagers really is patched ---
# Without this check we could ship the untouched file, and the player would get a game
# starting in 2D with an error message that means nothing to them.
$ggm  = Join-Path $GameDir "Away_Data\globalgamemanagers"
$orig = Join-Path $GameDir "Away_Data\globalgamemanagers.orig"
foreach ($f in @($ggm, $orig)) {
    if (-not (Test-Path $f)) { throw "Expected file missing: $f" }
}
# Binary search: this file is a Unity container, not text.
$bytes = [System.IO.File]::ReadAllBytes($ggm)
$needle = [System.Text.Encoding]::ASCII.GetBytes("OpenVR")
$found = $false
for ($i = 0; $i -le $bytes.Length - $needle.Length -and -not $found; $i++) {
    $ok = $true
    for ($j = 0; $j -lt $needle.Length; $j++) {
        if ($bytes[$i + $j] -ne $needle[$j]) { $ok = $false; break }
    }
    if ($ok) { $found = $true }
}
if (-not $found) { throw "globalgamemanagers does not look patched: 'OpenVR' is absent." }

# --- staging ---
$stage = Join-Path $env:TEMP "AwayVR_pack"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

function Copy-Relative($relative) {
    $src = Join-Path $GameDir $relative
    if (-not (Test-Path $src)) { throw "Not found in the game: $relative" }
    $dst = Join-Path $stage $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
    Copy-Item $src $dst -Recurse -Force
}

# Doorstop: loaded by the Windows loader as the process starts, and it is what boots
# BepInEx.
Copy-Relative "winhttp.dll"
Copy-Relative "doorstop_config.ini"
Copy-Relative ".doorstop_version"
Copy-Relative "BepInEx\core"

# OpenVR runtime, redistributable under Valve's BSD-3 licence. Taken from the repository
# rather than from the game folder so that a fresh clone can build the archive. Its licence
# travels with it: BSD-3 requires the notice to accompany a binary redistribution.
$vendored = "$root\packaging\third_party\openvr_api.dll"
$openvrDst = Join-Path $stage "Away_Data\Plugins\openvr_api.dll"
New-Item -ItemType Directory -Force -Path (Split-Path $openvrDst) | Out-Null
if (Test-Path $vendored) {
    Copy-Item $vendored $openvrDst -Force
} else {
    Write-Host "  packaging\third_party\openvr_api.dll missing, falling back to the game folder"
    Copy-Relative "Away_Data\Plugins\openvr_api.dll"
}

Copy-Relative "Away_Data\globalgamemanagers"
Copy-Relative "Away_Data\globalgamemanagers.orig"

New-Item -ItemType Directory -Force -Path (Join-Path $stage "BepInEx\plugins") | Out-Null
Copy-Item $dll (Join-Path $stage "BepInEx\plugins\AwayVR.dll") -Force

# The configuration is NOT shipped: BepInEx regenerates it on first launch using the
# defaults from the code. Shipping one would freeze settings that have changed often, and
# would stop players picking up new defaults when the mod is updated.

Copy-Item "$root\packaging\uninstall VR.bat" (Join-Path $stage "uninstall VR.bat") -Force
Copy-Item "$root\packaging\README.txt" (Join-Path $stage "README.txt") -Force

# Licence notices, required by the licences themselves rather than as a courtesy.
$lic = Join-Path $stage "licenses"
New-Item -ItemType Directory -Force -Path $lic | Out-Null
Copy-Item "$root\packaging\third_party\openvr-LICENSE.txt" (Join-Path $lic "openvr-LICENSE.txt") -Force
Copy-Item "$root\LICENSE" (Join-Path $lic "AwayVR-LICENSE.txt") -Force

# BepInEx ships its own licence in the release archive; carry it if it is there.
$bepLic = Join-Path $GameDir "BepInEx\LICENSE"
if (Test-Path $bepLic) { Copy-Item $bepLic (Join-Path $lic "BepInEx-LICENSE.txt") -Force }

# --- archive ---
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$zip = Join-Path $OutDir "AwayVR-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip

$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ""
Write-Host "Archive: $zip  ($mb MB)"
Get-ChildItem $stage -Recurse -File |
    ForEach-Object { "  " + $_.FullName.Substring($stage.Length + 1) } |
    Sort-Object
