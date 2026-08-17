@echo off
REM ---------------------------------------------------------------------------
REM  AwayVR uninstaller. Removes everything the mod added, itself included.
REM
REM  It works in its OWN directory, never in the current one, so a double-click
REM  from elsewhere cannot delete the wrong files. It also refuses to run unless
REM  Away.exe sits next to it.
REM ---------------------------------------------------------------------------
setlocal
cd /d "%~dp0"

if not exist "Away.exe" (
    echo ERROR: this script must sit in the game folder, next to Away.exe.
    pause
    exit /b 1
)

echo Uninstalling AwayVR...
echo.

REM The Unity configuration file comes first: it is what decides whether the game
REM starts at all, and in which mode.
if exist "Away_Data\globalgamemanagers.orig" (
    copy /y "Away_Data\globalgamemanagers.orig" "Away_Data\globalgamemanagers" >nul
    del /q "Away_Data\globalgamemanagers.orig"
    echo   globalgamemanagers restored from the original.
) else (
    echo   WARNING: globalgamemanagers.orig is missing, nothing to restore.
    echo   Use "Verify integrity of game files" in Steam if the game misbehaves.
)

REM Our DLL only. This folder also holds libraries belonging to the game, so it is
REM never removed wholesale.
if exist "Away_Data\Plugins\openvr_api.dll" del /q "Away_Data\Plugins\openvr_api.dll"

REM The BepInEx bootstrapper.
if exist "winhttp.dll"         del /q "winhttp.dll"
if exist "doorstop_config.ini" del /q "doorstop_config.ini"
if exist ".doorstop_version"   del /q ".doorstop_version"

REM BepInEx itself, with the mod, its configuration and its logs.
if exist "BepInEx" rd /s /q "BepInEx"

REM Documentation shipped in the archive. changelog.txt is NOT ours and stays.
if exist "README.txt"    del /q "README.txt"
if exist "licenses"      rd /s /q "licenses"

REM An uninstaller from an earlier version of the mod, if one is still around.
if exist "uninstall.bat" del /q "uninstall.bat"

echo   mod files removed.
echo.
echo AwayVR is uninstalled. The game will start normally in 2D again.
echo.
pause

REM Self-deletion, last of all. (goto) makes cmd release the batch file so the
REM del that follows can remove it; nothing after this line would ever run.
(goto) 2>nul & del /q "%~f0"
