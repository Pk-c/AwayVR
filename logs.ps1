# Pulls the useful parts out of the logs after a test run.
param(
    [string]$GameDir = "H:\Steam\steamapps\common\AWAY",
    [switch]$Full
)

$bep = Join-Path $GameDir "BepInEx\LogOutput.log"

if (-not (Test-Path $bep)) {
    Write-Host "No $bep : BepInEx did not load at all." -ForegroundColor Red
    Write-Host "Check that winhttp.dll and doorstop_config.ini sit in the game root."
    return
}

if ($Full) {
    Get-Content $bep
    return
}

Write-Host "=== BepInEx / plugin loading ===" -ForegroundColor Cyan
Select-String -Path $bep -Pattern "BepInEx (\d)|Loading \[|Chainloader|plugin" | ForEach-Object { $_.Line }

Write-Host ""
Write-Host "=== AwayVR ===" -ForegroundColor Cyan
Select-String -Path $bep -Pattern "AwayVR" | ForEach-Object { $_.Line }

Write-Host ""
Write-Host "=== Errors / exceptions ===" -ForegroundColor Yellow
Select-String -Path $bep -Pattern "Error|Exception|Fatal|failed|XR:" | ForEach-Object { $_.Line }
