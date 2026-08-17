AwayVR - a VR mod for AWAY: Journey to the Unexpected
=====================================================

INSTALLATION
------------
Unzip the contents of this archive into the game folder, next to Away.exe, then
launch the game. That is all.

  ...\Steam\steamapps\common\AWAY\

SteamVR must be running and the headset on before you start the game.

UNINSTALLING
------------
Double-click "uninstall VR.bat" in the game folder. It restores the original
configuration file, removes every file the mod added, and finally deletes
itself - nothing of the mod is left behind.


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
  Right trigger .................. attack
  Swing your arm ................. attack, with melee weapons
  Right grip ..................... guard
  Left grip ...................... grenade: squeeze to arm, release to throw
  Left trigger ................... show the HUD while held

  A .............................. jump, confirm, advance dialogue
  B .............................. next character
  X .............................. open/close the diary
  Y .............................. pause menu, cancel

  Left stick click ............... run
  Right stick down / up .......... next / previous character
  Right trigger (in a menu) ...... next tab
  Both stick clicks .............. the mod's VR settings

No SteamVR configuration is required. A and X are read from OpenVR directly:
Unity's legacy input layer gives those two buttons no index at all, which is why
most Unity VR mods cannot use them.

The grenade is thrown from your hand, in the direction of the throw, and as hard
as you actually threw it.


SETTINGS
--------
The VR settings open in game by clicking both sticks: turning, world scale,
player height, weapon placement, HUD size and distance, shadow distance, render
scale, and a frame-rate counter.

  Character effects
        The per-character full-screen washes - the mechanic's red, the magician's
        cracked glasses. Strong in a headset; switch them off if they bother you.

  Render scale
        Supersampling. The game renders deferred, where MSAA does not exist, so
        this is the only anti-aliasing available. 1.3 by default; lower it if the
        frame rate suffers.

Everything is stored in BepInEx\config\fr.awayvr.plugin.cfg, which also holds
finer options that are not exposed in the menu - grenade offsets, swing
threshold, fade timings, and the input bindings.


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

  Input behaving oddly
        Set OpenVrBridge to false in the config file, with the game closed. The
        mod then falls back to Unity's own input and loses only A and X.


LICENCES
--------
Full texts are in the licenses\ folder of this archive.

BepInEx    LGPL-2.1        https://github.com/BepInEx/BepInEx
openvr_api BSD-3-Clause    https://github.com/ValveSoftware/openvr
AwayVR     see LICENSE     https://github.com/Pk-c/AwayVR

openvr_api.dll is redistributed unmodified under the BSD-3-Clause licence, whose
text accompanies it as that licence requires.
