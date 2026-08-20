# AwayVR

A VR mod for **AWAY: Journey to the Unexpected**

<img width="1672" height="941" alt="d4d85224-1acc-4861-833c-94d8671a53b7" src="https://github.com/user-attachments/assets/ac74c3ab-ff57-4a3e-b515-3e7eb8f08435" />

I tried to make this mod feel like native : 6Dof, snap/smooth turn mode, tracked hands, swing-to-attack, grenades thrown with your hands, and the game's flat interface
rebuilt as panels you can actually look at.

Not affiliated with the publisher. No game assets or game code are redistributed.

If you like my work you can follow me on Patreon ( free membership ), I try to make like native mode for beautiful games!

https://patreon.com/ChromaticMod

<a href="https://patreon.com/ChromaticMod">
  <img width="200" height="105" alt="imakevrmodforgames-preview" src="https://github.com/user-attachments/assets/0517352b-e120-47bc-b062-b85fc333f814" />
</a>

OR

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/A0Y524C5N8)


---

## Installation

Download the latest release <a href="https://github.com/Pk-c/AwayVR/releases/tag/V1.0">Here</a>

Unzip the release archive into the game folder, next to `Away.exe`, then launch the game.
That is all

```
...\Steam\steamapps\common\AWAY\
```

SteamVR must be running and the headset on before you start the game.

To uninstall, double-click `uninstall VR.bat` in the game folder. It restores the original
configuration file, removes every file the mod added, and deletes itself.

## Controls

| Input | Action |
|---|---|
| Right trigger | Attack |
| Swing your arm | Attack, with melee weapons |
| Right grip | Guard |
| Left grip | Grenade: squeeze to arm, release to throw |
| Left trigger | Show the HUD while held |
| A | Jump, confirm, advance dialogue |
| B | Next character, advance dialogue  |
| X | Open/close the diary |
| Y | Pause menu, cancel |
| Right stick down / up | Next / previous character |
| Both stick clicks | The mod's VR settings |

No SteamVR configuration is required. **A and X are read from OpenVR directly**: Unity's
legacy input layer gives those two buttons no joystick index at all, which is why most
Unity VR mods cannot use them.

The grenade leaves your hand, in the direction of the throw, and as hard as you threw it.

## Settings Menu

The in-game menu opens by clicking both sticks, and everything applies live:

| Section | What it holds |
|---|---|
| Controls | walking speed and dead zone, snap or smooth turn, turn angle and speed |
| Weapon | weapon size, swing threshold, trail, damage volume placement, knockback, throwing power |
| Graphics | world scale, colour grading |
| Effects | render scale, one switch per full-screen effect, shadows, level of detail |
| Interface | HUD always visible, HUD size and distance |
| Player | head centring, room-scale walking, camera blocked by walls, player height |
| System | frame-rate counter, reset everything |

Two of them are worth knowing about, because they undo assumptions the game makes that only
hold on a flat screen. **Hit box** decides where a melee blow's damage volume goes: the game
pins it two metres ahead of the *headset*, so it rides on your posture - crouch and it sinks
with you. **Knockback** decides which way a struck enemy is thrown: the game pushes it along
the *body's* forward, which is also "away from you" only because, on a screen, you always
face what you hit.

The robot is a lock-on weapon: look at an enemy to lock it, then fire.

Everything is stored in `BepInEx/config/fr.awayvr.plugin.cfg`, which also holds the finer
options that are not exposed in the menu - grenade offsets, swing threshold, fade timings,
and the input bindings.

## How it works

A few things about this game turned out to shape the whole design:

- **`globalgamemanagers` ships already patched.** The engine reads it before the mono
  runtime exists, so no plugin can enable the OpenVR device in time for the launch under
  way. The file holds build settings only - no assets, no code - and the original travels
  with it as `.orig` for uninstalling.
- **Unity 2017's built-in VR requires Direct3D 11.** Under D3D12 it fails silently, so the
  patch also puts D3D11 back at the head of the graphics API list.
- **The interface is captured, not rebuilt.** Screen-space canvases are attached to a
  camera of our own that draws into a texture shown on a panel, so Unity keeps full control
  of the layout - including the game's habit of hiding UI by sliding it off screen.
  Dialogues are IMGUI, so they are captured by redirecting the render target around
  `OnGUI`.
- **Several full-screen effects cannot work in stereo.** Anything that reconstructs the
  scene from the depth buffer uses one camera's matrices while there are two eyes: the
  result lands beside the geometry it belongs to. Cloud shadows, depth of field, lens
  curvature and grab-pass shaders are neutralised; I also make the choice to slightly change the color grading since things are much darker in vr

## Troubleshooting

`BepInEx/LogOutput.log` says what happened.

| Message | Meaning |
|---|---|
| `is not in enabledVRDevices` | `globalgamemanagers` was not copied, or a game update replaced it |
| `Unity 2017 VR requires Direct3D 11` | Add `-force-d3d11` to the Steam launch options |
| `XR activation failed` | SteamVR is not running, or the headset is not detected |

If input misbehaves, set `OpenVrBridge` to `false` in the config with the game closed: the
mod falls back to Unity's own input and loses only A and X.


## Building the mod

Requirements: .NET SDK, and an install of the game with BepInEx 5 in place - the project
references `BepInEx.dll`, `0Harmony.dll` and the game's own assemblies from there.

```powershell
dotnet build src/AwayVR/AwayVR.csproj -c Release -p:GameDir="H:\Steam\steamapps\common\AWAY"
./deploy.ps1        # build and copy the plugin into the game
./tools/pack.ps1    # assemble dist/AwayVR-<version>.zip
```

`tools/patch_ggm.py` produces the patched `globalgamemanagers` (needs Python and UnityPy).

## Licences

| Component | Licence |
|---|---|
| AwayVR | MIT, see [LICENSE](LICENSE) |
| [BepInEx](https://github.com/BepInEx/BepInEx) | LGPL-2.1 |
| [OpenVR](https://github.com/ValveSoftware/openvr) | BSD-3-Clause, see `packaging/third_party/` |

`openvr_api.dll` is redistributed unmodified, with its licence text alongside it as that
licence requires.
