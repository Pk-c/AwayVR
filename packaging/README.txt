AwayVR — a VR mod for AWAY: Journey to the Unexpected
=====================================================

INSTALLATION
------------
Unzip the contents of this archive into the game folder, next to Away.exe, then
launch the game. That is all.

  ...\Steam\steamapps\common\AWAY\

SteamVR must be running and the headset on before you start the game.

UNINSTALLING
------------
Double-click uninstall.bat in the game folder. It restores the original
configuration file and removes every file that was added.


WHAT THE ARCHIVE CONTAINS
-------------------------
  winhttp.dll, doorstop_config.ini, .doorstop_version
        The BepInEx bootstrapper. Loaded by Windows when the game starts.

  BepInEx\core\, BepInEx\plugins\AwayVR.dll
        BepInEx itself and the mod.

  Away_Data\Plugins\openvr_api.dll
        Valve's OpenVR runtime.

  Away_Data\globalgamemanagers
        The game's Unity configuration file, changed in two respects: the OpenVR
        device is enabled, and Direct3D 11 is placed ahead of Direct3D 12. Unity
        2017's built-in VR only works on D3D11; under D3D12 it fails with no
        error message at all.

        This file contains no game assets and no game code, only build settings.
        It ships already modified because the engine reads it before starting the
        runtime a mod could ever execute in: no code can patch it in time for the
        launch under way.

  Away_Data\globalgamemanagers.orig
        The original, kept for uninstalling.


CONTROLS
--------
  Right trigger .................. attack (swinging your arm also works)
  Right grip ..................... guard
  Left grip ...................... switch character
  Left trigger ................... grenade
  Right face button .............. jump, confirm, advance dialogue
  Left face button ............... open/close the diary
  Left stick click ............... run
  Right stick click .............. menu, cancel
  Right trigger (in a menu) ...... next tab
  Both grips ..................... show the HUD
  Both stick clicks .............. the mod's VR settings

No SteamVR configuration is required.


SETTINGS
--------
The VR settings open in game by clicking both sticks: turning, world scale,
player height, weapon placement, HUD.

They are stored in BepInEx\config\fr.awayvr.plugin.cfg, which also holds finer
options that are not exposed in the menu.


TROUBLESHOOTING
---------------
The BepInEx\LogOutput.log file says what happened.

  "is not in enabledVRDevices"
        globalgamemanagers was not copied, or the game was updated and replaced
        it. Reinstall the mod.

  "Unity 2017 VR requires Direct3D 11"
        Add -force-d3d11 to the Steam launch options.

  "XR activation failed"
        SteamVR is not running, or the headset is not being detected.


LICENCES
--------
BepInEx    LGPL-2.1        https://github.com/BepInEx/BepInEx
openvr_api BSD-3-Clause    https://github.com/ValveSoftware/openvr
AwayVR     see LICENSE     https://github.com/Pk-c/AwayVR
