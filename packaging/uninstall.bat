@echo off
REM ---------------------------------------------------------------------------
REM  AwayVR uninstaller.
REM  Leave this in the game folder: the script works in its OWN directory, never
REM  in the current one, so that a double-click from somewhere else cannot end up
REM  deleting the wrong files.
REM ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0"

if not exist "Away.exe" (
    echo ERROR: this script must sit in the game folder, next to Away.exe.
    pause
    exit /b 1
)

echo Uninstalling AwayVR...

REM The original Unity configuration file is restored first: it is what decides
REM whether the game starts at all.
if exist "Away_Data\globalgamemanagers.orig" (
    copy /y "Away_Data\globalgamemanagers.orig" "Away_Data\globalgamemanagers" >nul
    del /q "Away_Data\globalgamemanagers.orig"
    echo   globalgamemanagers restored.
) else (
    echo   WARNING: globalgamemanagers.orig is missing, nothing to restore.
    echo   If needed, use "Verify integrity of game files" in Steam.
)

REM Our DLL only: this folder also holds libraries belonging to the game.
if exist "Away_Data\Plugins\openvr_api.dll" del /q "Away_Data\Plugins\openvr_api.dll"

if exist "winhttp.dll"          del /q "winhttp.dll"
if exist "doorstop_config.ini"  del /q "doorstop_config.ini"
if exist ".doorstop_version"    del /q ".doorstop_version"
if exist "BepInEx"              rd /s /q "BepInEx"

echo   mod files removed.
echo.
echo AwayVR is uninstalled. The game will start normally in 2D again.
pause
