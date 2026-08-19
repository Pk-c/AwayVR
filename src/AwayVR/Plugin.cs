using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace AwayVR
{
    [BepInPlugin(Guid, "Away VR", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "fr.awayvr.plugin";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        // --- XR ---
        internal static ConfigEntry<bool> CfgEnabled;
        internal static ConfigEntry<string> CfgDevice;
        internal static ConfigEntry<float> CfgResolutionScale;
        internal static ConfigEntry<bool> CfgAnisotropic;
        internal static ConfigEntry<int> CfgShadowCascades;
        internal static ConfigEntry<ShadowResolution> CfgShadowResolution;
        internal static ConfigEntry<float> CfgLodBias;
        internal static ConfigEntry<float> CfgShadowDistance;
        internal static ConfigEntry<bool> CfgDisableTemporalAA;
        internal static ConfigEntry<bool> CfgDisableOcclusion;
        internal static ConfigEntry<bool> CfgDisableGlobalFog;
        internal static ConfigEntry<bool> CfgDisableDepthOfField;
        internal static ConfigEntry<bool> CfgDisableBlink;
        internal static ConfigEntry<bool> CfgCharacterEffects;
        internal static ConfigEntry<bool> CfgWeaponsCameraOff;
        internal static ConfigEntry<bool> CfgFpsCounter;
        internal static ConfigEntry<bool> CfgRecenterOnLoad;
        internal static ConfigEntry<bool> CfgRoomScaleMove;
        internal static ConfigEntry<float> CfgRoomScaleDeadzone;
        internal static ConfigEntry<bool> CfgBlockCameraOnWalls;

        // --- camera ---
        internal static ConfigEntry<float> CfgHeightOffset;
        internal static ConfigEntry<float> CfgWorldScale;

        // --- full-screen effects ---
        internal static ConfigEntry<bool> CfgDisableBloom;
        internal static ConfigEntry<bool> CfgDisableColorGrading;

        // --- locomotion ---
        internal static ConfigEntry<bool> CfgHeadRelativeMove;
        internal static ConfigEntry<bool> CfgAlwaysRun;
        internal static ConfigEntry<float> CfgFullSpeedAt;
        internal static ConfigEntry<float> CfgMoveDeadzone;
        internal static ConfigEntry<bool> CfgCentreOnBody;
        internal static ConfigEntry<Patches.TurnMode> CfgTurnMode;
        internal static ConfigEntry<float> CfgSnapAngle;
        internal static ConfigEntry<float> CfgSmoothTurnSpeed;
        internal static ConfigEntry<float> CfgTurnDeadzone;

        // --- player body ---

        // --- layers ---

        // --- weapons ---
        internal static ConfigEntry<WeaponAttachMode> CfgWeaponAttach;
        internal static ConfigEntry<float> CfgWeaponScale;
        internal static ConfigEntry<WeaponAnchorPoint> CfgWeaponAnchor;
        internal static ConfigEntry<bool> CfgOrientHitbox;
        internal static ConfigEntry<float> CfgHitboxScale;
        internal static ConfigEntry<float> CfgWeaponOffX, CfgWeaponOffY, CfgWeaponOffZ;

        // --- swing to attack ---
        internal static ConfigEntry<bool> CfgSwingToAttack;
        internal static ConfigEntry<bool> CfgWeaponTrail;
        internal static ConfigEntry<float> CfgTrailHold;
        internal static ConfigEntry<float> CfgSwingThreshold;
        internal static ConfigEntry<float> CfgSwingCooldown;

        // --- hidden canvases ---

        // --- HUD ---
        internal static ConfigEntry<float> CfgHudDistance;
        internal static ConfigEntry<float> CfgHudWidth;
        internal static ConfigEntry<float> CfgHudFollowSpeed;

        // --- menu ---
        internal static ConfigEntry<KeyCode> CfgMenuKey;
        internal static ConfigEntry<float> CfgMenuDistance;
        internal static ConfigEntry<float> CfgMenuWidth;
        internal static ConfigEntry<float> CfgMenuVOffset;

        // --- miscellaneous ---
        internal static ConfigEntry<KeyCode> CfgRecenterKey;
        internal static ConfigEntry<KeyCode> CfgDiagKey;
        internal static ConfigEntry<bool> CfgVerbose;
        internal static ConfigEntry<bool> CfgProbe;
        internal static ConfigEntry<bool> CfgOpenVrBridge;
        internal static ConfigEntry<float> CfgAxisThreshold;
        internal static ConfigEntry<bool> CfgTraceInput;
        internal static ConfigEntry<bool> CfgDialogCapture;
        internal static ConfigEntry<float> CfgDialogDistance;
        internal static ConfigEntry<float> CfgDialogWidth;
        internal static ConfigEntry<bool> CfgHudAlwaysVisible;
        internal static ConfigEntry<bool> CfgVrFade;
        internal static ConfigEntry<bool> CfgSceneCover;
        internal static ConfigEntry<float> CfgSceneCoverMax;
        internal static ConfigEntry<float> CfgFadeDistance;
        internal static ConfigEntry<bool> CfgFadeOnCharacterSwap;
        internal static ConfigEntry<float> CfgCharacterFadeDuration;
        internal static ConfigEntry<bool> CfgGrenadeInHand;
        internal static ConfigEntry<bool> CfgGrenadeFromHand;
        internal static ConfigEntry<float> CfgGrenadeScale;
        internal static ConfigEntry<float> CfgGrenadeOffX, CfgGrenadeOffY, CfgGrenadeOffZ;
        internal static ConfigEntry<bool> CfgGrenadeGesture;
        internal static ConfigEntry<bool> CfgGrenadeAimFromMotion;
        internal static ConfigEntry<float> CfgGrenadeMotionMin;
        internal static ConfigEntry<float> CfgGrenadeThrowPitch;
        internal static ConfigEntry<bool> CfgGrenadePowerFromMotion;
        internal static ConfigEntry<float> CfgGrenadeRefSpeed;
        internal static ConfigEntry<float> CfgGrenadePowerMin, CfgGrenadePowerMax;
        internal static ConfigEntry<float> CfgStickThreshold;
        internal static ConfigEntry<float> CfgGrenadeArmLevel, CfgGrenadeReleaseLevel;
        internal static ConfigEntry<float> CfgGrenadeArmScale;
        internal static ConfigEntry<float> CfgHudSceneReminder;
        internal static ConfigEntry<float> CfgHudSceneDelay;
        internal static ConfigEntry<float> CfgHudFlashDuration;
        internal static ConfigEntry<float> CfgHudFadeSpeed;

        private Harmony _harmony;

        /// <summary>Applies one patch class, reporting a failure instead of propagating it.</summary>
        private void PatchSafely(System.Type type)
        {
            try { _harmony.PatchAll(type); }
            catch (System.Exception e)
            {
                Log.LogError("Patches in " + type.Name + " were not applied: " + e.Message);
            }
        }

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            CfgEnabled = Config.Bind("01 - XR", "Enabled", true,
                "Enables VR. When false the game starts normally in 2D.");
            CfgDevice = Config.Bind("01 - XR", "Device", "OpenVR",
                "Name of the XR device to load. Must appear in globalgamemanagers' enabledVRDevices.");
            CfgResolutionScale = Config.Bind("01 - XR", "ResolutionScale", 1.3f,
                new ConfigDescription(
                    "Eye texture supersampling. The game renders deferred, where MSAA does "
                    + "not exist, so this is the ONLY anti-aliasing available - hence a "
                    + "default well above 1. Lower it if the frame rate suffers.",
                    new AcceptableValueRange<float>(0.5f, 2.0f)));
            CfgAnisotropic = Config.Bind("06 - Visuals", "ForceAnisotropic", true,
                "Forces anisotropic filtering on every texture. The game's preset only enables "
                + "it per texture, and its textures mostly do not ask for it, so floors and "
                + "walls blur at a grazing angle. Close to free.");
            CfgShadowCascades = Config.Bind("06 - Visuals", "ShadowCascades", 2,
                new ConfigDescription("Directional shadow cascades. More cascades over the "
                    + "same distance means sharper shadows near the player.",
                    new AcceptableValueList<int>(1, 2, 4)));
            CfgShadowResolution = Config.Bind("06 - Visuals", "ShadowResolution",
                ShadowResolution.VeryHigh,
                "Shadow map resolution. The game's preset stops at High.");
            CfgShadowDistance = Config.Bind("06 - Visuals", "ShadowDistance", 100f,
                new ConfigDescription("How far shadows are cast, in metres. The game ships "
                    + "100. The heaviest single lever in an open area - a desert casts "
                    + "shadows to the horizon where a dungeon has almost none, which is why "
                    + "one drops below the headset's refresh rate and the other does not.",
                    new AcceptableValueRange<float>(10f, 200f)));
            CfgLodBias = Config.Bind("06 - Visuals", "LodBias", 5.0f,
                new ConfigDescription("Keeps high-detail models in use further away. The game "
                    + "ships 3.0.", new AcceptableValueRange<float>(0.5f, 10f)));

            CfgDisableTemporalAA = Config.Bind("06 - Visuals", "DisableTemporalAA", false,
                "Switches off the game's temporal anti-aliasing. It keeps a single frame of "
                + "history per camera, which in stereo means each eye is blended with the "
                + "other one - the ghosting. Leave it on unless you want to see the effect.");
            CfgDisableOcclusion = Config.Bind("06 - Visuals", "DisableAmbientOcclusion", false,
                "Switches off Amplify Occlusion. It rebuilds world positions from the depth "
                + "buffer using the camera's single set of matrices, so in stereo its dark "
                + "contours land beside the geometry rather than on it - a shadow copy of the "
                + "world, offset by the eye separation.");
            CfgDisableGlobalFog = Config.Bind("06 - Visuals", "DisableGlobalFog", false,
                "Switches off GlobalFog, which reconstructs the scene the same monoscopic way. "
                + "Off by default because the fog is part of the art direction: try it only if "
                + "the ghosting survives with occlusion already disabled.");
            CfgDisableDepthOfField = Config.Bind("06 - Visuals", "DisableDepthOfField", true,
                "Zeroes FxPro's depth of field, chromatic aberration and lens curvature. The "
                + "amounts are set to zero rather than the effects switched off, so their "
                + "full-screen pass keeps rewriting the target - switching them off left it "
                + "unwritten, which is the stale frame that used to bleed through.");
            CfgDisableBlink = Config.Bind("06 - Visuals", "DisableBlinkEffect", false,
                "Switches off the eyelid-blink transition, which bends the whole screen "
                + "through a curvature term and only stops when its fade completes.");
            CfgCharacterEffects = Config.Bind("06 - Visuals", "CharacterEffects", true,
                "The per-character full-screen washes - the mechanic's red, the magician's "
                + "cracked glasses. Strong enough in a headset that some players will want "
                + "them off. Off leaves the effects' host camera alone; only the components "
                + "are switched off, and the game re-enables them on a swap so the sweep "
                + "keeps forcing them.");
            CfgWeaponsCameraOff = Config.Bind("06 - Visuals", "WeaponsCameraOff", false,
                "Disables Weapons_Camera instead of leaving it running blind, moving its "
                + "per-character effects onto the main camera first so nothing is lost. The "
                + "camera renders nothing and clears depth only, so its colour buffer holds "
                + "whatever was there before - and its effect chain composites that onto the "
                + "screen. That is the stale half-transparent frame.");
            CfgFpsCounter = Config.Bind("06 - Visuals", "FpsCounter", false,
                "Shows the frame rate, and the worst frame of the last few seconds, in the "
                + "corner of the view.");

            CfgRecenterOnLoad = Config.Bind("01 - XR", "RecenterOnSceneLoad", true,
                "Recentres the view on every scene load, so you start facing the character's "
                + "forward rather than wherever the headset happened to point at spawn.");

            CfgRoomScaleMove = Config.Bind("01 - XR", "RoomScaleMovement", true,
                "Physically walking moves the character, with collisions. Tracking stays centred "
                + "on the character's eye height: this game has a fixed height, and floor-based "
                + "tracking does not fit it.");

            CfgRoomScaleDeadzone = Config.Bind("01 - XR", "RoomScaleDeadzone", 0.002f,
                new ConfigDescription("How far the head must move before the character is\n"
                    + "advanced, in metres. Below this the mod calls the game's "
                    + "CharacterController every frame for tracking noise, which "
                    + "competes with the game's own movement. Raise it if walking "
                    + "feels held back; lower it for tighter one-to-one stepping.",
                    new AcceptableValueRange<float>(0.0002f, 0.05f)));
            CfgBlockCameraOnWalls = Config.Bind("01 - XR", "BlockCameraOnWalls", true,
                "The view is blocked by walls along with the body, and the collision capsule stays "
                + "exactly under the head. When false, head tracking stays perfect but the head "
                + "can pass through walls.");

            CfgHeightOffset = Config.Bind("02 - Camera", "HeightOffset", -0.7f,
                new ConfigDescription("Vertical eye offset, in metres.", new AcceptableValueRange<float>(-1.5f, 1.5f)));
            CfgWorldScale = Config.Bind("02 - Camera", "WorldScale", 1.0f,
                new ConfigDescription("World scale, achieved by scaling the rig: that also stretches the interpupillary "
                    + "distance, which is precisely what makes you feel taller or shorter. "
                    + ">1 = you feel taller.",
                    new AcceptableValueRange<float>(0.3f, 3f)));


            CfgDisableBloom = Config.Bind("03 - Effects", "DisableBloom", false,
                "Switches off the game's bloom. It lives on the weapons camera together "
                + "with the colour grading, and is far harsher in a headset than on a "
                + "monitor. The grading is kept either way.");
            CfgDisableColorGrading = Config.Bind("03 - Effects", "DisableColorGrading", true,
                "Switches off the game's colour grading. It rides on the weapons camera and "
                + "reads as far too contrasted in a headset, where the image fills your whole "
                + "field of view. Off by default; set to false for the flat game's look.");
            CfgHeadRelativeMove = Config.Bind("025 - Locomotion", "HeadRelativeMovement", true,
                "Movement follows the GAZE direction rather than the body orientation. Forward goes "
                + "where you look, and strafing is relative to the head. false = original "
                + "behaviour, relative to the body.");
            CfgCentreOnBody = Config.Bind("025 - Locomotion", "CentreHeadOnBody", true,
                "Puts the camera over the collision capsule. The headset reports the head's "
                + "position relative to the CENTRE OF THE ROOM, so standing anywhere else "
                + "leaves the capsule metres away from where you are looking - it then scrapes "
                + "geometry you cannot see and your walk is held back at random. Recentring "
                + "through the headset does not fix this: OpenVR ignores it in room-scale "
                + "space, so the offset is cancelled here instead.");
            CfgFullSpeedAt = Config.Bind("025 - Locomotion", "FullSpeedAt", 0.55f,
                new ConfigDescription(
                    "Deflection at which the character reaches full speed. Beyond it the "
                    + "pace no longer changes. The game multiplies its speed by the stick's "
                    + "deflection with nothing in between, and a thumb on a small VR stick "
                    + "wanders between 0.5 and 1.0 while it feels held still - which turned "
                    + "straight into a wandering speed. Raise it for a wider analogue range, "
                    + "lower it for a steadier pace.",
                    new AcceptableValueRange<float>(0.2f, 1f)));
            CfgMoveDeadzone = Config.Bind("025 - Locomotion", "MoveDeadzone", 0.15f,
                new ConfigDescription(
                    "Deflection below which the character does not move. Applied RADIALLY, "
                    + "unlike the game's own 0.30 per axis, which cuts a square out of a "
                    + "round stick and truncates the diagonals.",
                    new AcceptableValueRange<float>(0.02f, 0.5f)));
            CfgAlwaysRun = Config.Bind("025 - Locomotion", "AlwaysRun", true,
                "The character always runs, and you slow down by pushing the stick less far. "
                + "Speed is speed x |m_Input| in the game's own arithmetic, so the deflection "
                + "is already a continuous throttle - which makes the walk/run switch a second, "
                + "redundant speed control that flipped on its own and changed your pace "
                + "mid-stride. Characters the game forbids from running are left alone.");
            CfgTurnMode = Config.Bind("025 - Locomotion", "TurnMode", Patches.TurnMode.Snap,
                "Snap = stepped rotation (recommended). Smooth = continuous rotation.");
            CfgSnapAngle = Config.Bind("025 - Locomotion", "SnapAngle", 45f,
                new ConfigDescription("Angle of one snap turn step, in degrees.",
                    new AcceptableValueRange<float>(5f, 180f)));
            CfgSmoothTurnSpeed = Config.Bind("025 - Locomotion", "SmoothTurnSpeed", 120f,
                new ConfigDescription("Smooth turn speed, in degrees per second.",
                    new AcceptableValueRange<float>(10f, 540f)));
            CfgTurnDeadzone = Config.Bind("025 - Locomotion", "TurnDeadzone", 0.6f,
                new ConfigDescription("Stick deflection needed to trigger one snap step. You have to come back down to 60% "
                    + "of that threshold before another can fire.",
                    new AcceptableValueRange<float>(0.1f, 0.95f)));



            CfgWeaponAttach = Config.Bind("034 - Weapons", "AttachTo", WeaponAttachMode.Right,
                "Hand to attach the viewmodel to. Off = leaves the weapons on the camera.");
            CfgWeaponScale = Config.Bind("034 - Weapons", "Scale", 0.45f,
                new ConfigDescription("Viewmodel scale. A viewmodel is authored oversized: "
                    + "unnoticeable on a screen, glaring in VR.",
                    new AcceptableValueRange<float>(0.05f, 2f)));
            CfgOrientHitbox = Config.Bind("034 - Weapons", "OrientHitbox", true,
                "Turns the damage volume with you. The game spawns it with an identity rotation "
                + "and never sets one afterwards, so a non-spherical collider stays aligned to "
                + "the WORLD axes and no longer matches where you face once you turn. Its "
                + "position is left to the game.");
            CfgHitboxScale = Config.Bind("034 - Weapons", "HitboxScale", 1.35f,
                new ConfigDescription(
                    "Enlarges the damage volume. Its size was chosen for a mouse pointing "
                    + "the whole view; a hand swinging in the air is less precise and the "
                    + "weapon is drawn where you swing it, not where the game aims.",
                    new AcceptableValueRange<float>(1f, 3f)));
            CfgWeaponAnchor = Config.Bind("034 - Weapons", "AnchorPoint", WeaponAnchorPoint.Centre,
                "Which point of the model sits in your hand. Base = the rear end, which suits weapons "
                + "modelled as an outstretched arm holding an object. Centre = the midpoint of "
                + "the whole thing. Pivot = the model's raw pivot, uncorrected.");
            CfgWeaponOffX = Config.Bind("034 - Weapons", "PositionX", 0.075f,
                new ConfigDescription("Moves the arm on screen by choosing which point of the model is held. The centre of "
                    + "rotation stays on the controller whatever the value.",
                    new AcceptableValueRange<float>(-2.5f, 2.5f)));
            CfgWeaponOffY = Config.Bind("034 - Weapons", "PositionY", 0.405f, new ConfigDescription("", new AcceptableValueRange<float>(-2.5f, 2.5f)));
            CfgWeaponOffZ = Config.Bind("034 - Weapons", "PositionZ", -0.025f, new ConfigDescription("", new AcceptableValueRange<float>(-2.5f, 2.5f)));



            CfgSwingToAttack = Config.Bind("034 - Weapons", "SwingToAttack", true,
                "Swinging your hand triggers the attack, for MELEE weapons only. Throwing weapons "
                + "keep the trigger: hurling a projectile by waving your hand would be "
                + "imprecise.");
            CfgWeaponTrail = Config.Bind("034 - Weapons", "WeaponTrail", true,
                "Draws the weapon's trail when you swing. The game raised it from the attack "
                + "animation, which no longer carries the blade now that the weapon follows "
                + "the hand.");
            CfgTrailHold = Config.Bind("034 - Weapons", "TrailHold", 0.25f,
                new ConfigDescription("How long the trail keeps emitting after the hand slows.",
                    new AcceptableValueRange<float>(0.05f, 1f)));
            CfgSwingThreshold = Config.Bind("034 - Weapons", "SwingThreshold", 3.0f,
                new ConfigDescription("Hand speed that triggers a swing, in m/s. Lower is more sensitive, at the risk of "
                    + "unintended swings.",
                    new AcceptableValueRange<float>(0.3f, 6f)));
            CfgSwingCooldown = Config.Bind("034 - Weapons", "SwingCooldown", 0.45f,
                new ConfigDescription("Minimum delay between two swings, in seconds.",
                    new AcceptableValueRange<float>(0.05f, 2f)));



            CfgHudDistance = Config.Bind("035 - HUD", "Distance", 2.0f,
                new ConfigDescription("HUD distance, in metres.",
                    new AcceptableValueRange<float>(0.5f, 10f)));
            CfgHudWidth = Config.Bind("035 - HUD", "Width", 1.8f,
                new ConfigDescription("Physical HUD width, in metres.",
                    new AcceptableValueRange<float>(0.5f, 12f)));

            CfgHudFollowSpeed = Config.Bind("035 - HUD", "FollowSpeed", 3f,
                new ConfigDescription(
                    "How quickly the HUD catches up with your gaze. 0 LOCKS it to the head with no lag; "
                    + "a high value catches up fast, a low one lets it trail behind.",
                    new AcceptableValueRange<float>(0f, 20f)));


            CfgMenuKey = Config.Bind("036 - Menu", "MenuKey", KeyCode.F1,
                "Opens/closes the menu from the keyboard. In VR: click both sticks at once.");
            CfgMenuDistance = Config.Bind("036 - Menu", "Distance", 1.4f,
                new ConfigDescription("Distance of the menu panel in VR, in metres.",
                    new AcceptableValueRange<float>(0.4f, 5f)));
            CfgMenuWidth = Config.Bind("036 - Menu", "Width", 1.1f,
                new ConfigDescription("Physical width of the menu panel, in metres.",
                    new AcceptableValueRange<float>(0.4f, 6f)));
            CfgMenuVOffset = Config.Bind("036 - Menu", "VerticalOffset", -0.1f,
                new ConfigDescription("Vertical offset of the menu panel, in metres.",
                    new AcceptableValueRange<float>(-2f, 2f)));

            CfgRecenterKey = Config.Bind("04 - Keys", "Recenter", KeyCode.F9, "Recentres the view.");
            CfgDiagKey = Config.Bind("04 - Keys", "Diagnostics", KeyCode.F10, "Writes a scene report to the log.");
            CfgVerbose = Config.Bind("04 - Keys", "Verbose", false, "Verbose logging.");
            CfgDialogCapture = Config.Bind("035 - HUD", "DialogCapture", true,
                "Captures the IMGUI drawing of the dialogues, invisible in VR, and shows it on a "
                + "panel in front of the player.");
            CfgDialogDistance = Config.Bind("035 - HUD", "DialogDistance", 1.60f,
                new ConfigDescription("Distance of the dialogue panel, in metres.",
                    new AcceptableValueRange<float>(0.5f, 6f)));
            CfgDialogWidth = Config.Bind("035 - HUD", "DialogWidth", 2.00f,
                new ConfigDescription("Width of the dialogue panel, in metres.",
                    new AcceptableValueRange<float>(0.5f, 8f)));
            CfgHudSceneDelay = Config.Bind("035 - HUD", "HudSceneDelay", 2.0f,
                new ConfigDescription(
                    "How long to wait after a scene comes up before showing the HUD. The view "
                    + "is still settling during a load, and showing it at once means it is gone "
                    + "before there is anything to read.",
                    new AcceptableValueRange<float>(0f, 15f)));
            CfgHudSceneReminder = Config.Bind("035 - HUD", "HudSceneReminder", 2.0f,
                new ConfigDescription(
                    "How long the HUD stays up on arriving in a gameplay scene, before returning to "
                    + "on-demand display. 0 disables it.",
                    new AcceptableValueRange<float>(0f, 15f)));
            CfgVrFade = Config.Bind("035 - HUD", "VrFade", true,
                "Reproduces the game's screen fades across the whole field of view. Without "
                + "it they only darken the floating HUD panel and the world stays visible.");
            CfgSceneCover = Config.Bind("035 - HUD", "SceneCover", true,
                "Blacks the view the instant a scene comes up, until the game's own blink "
                + "transition takes over. Without it the raw scene shows for about half a "
                + "second before the blink starts.");
            CfgSceneCoverMax = Config.Bind("035 - HUD", "SceneCoverMax", 3.0f,
                new ConfigDescription("How long the cover may wait for that blink. A scene "
                    + "may have none, and a cover with no end is worse than what it hides.",
                    new AcceptableValueRange<float>(0.2f, 10f)));
            CfgFadeDistance = Config.Bind("035 - HUD", "FadeDistance", 0.30f,
                new ConfigDescription(
                    "Distance of the fade surface, in metres. Close enough to cover the view, "
                    + "far enough not to clip through the near plane.",
                    new AcceptableValueRange<float>(0.15f, 2f)));
            CfgFadeOnCharacterSwap = Config.Bind("035 - HUD", "FadeOnCharacterSwap", true,
                "Punctuates a character swap with a short fade.");
            CfgCharacterFadeDuration = Config.Bind("035 - HUD", "CharacterFadeDuration", 0.7f,
                new ConfigDescription("Length of that fade, in seconds.",
                    new AcceptableValueRange<float>(0.05f, 2f)));
            CfgStickThreshold = Config.Bind("05 - VR bindings", "StickThreshold", 0.9f,
                new ConfigDescription("How far a stick must be pushed for a directional "
                    + "binding to fire. High on purpose: the stick is also used for other "
                    + "things, and a low threshold would trip on the way past.",
                    new AcceptableValueRange<float>(0.3f, 1f)));
            CfgGrenadeInHand = Config.Bind("034 - Weapons", "GrenadeInHand", true,
                "Shows a grenade in the left hand whenever you have one left.");
            CfgGrenadeFromHand = Config.Bind("034 - Weapons", "GrenadeFromHand", true,
                "Throws the grenade from the left hand, in the direction it points, "
                + "instead of from the camera.");
            CfgGrenadeScale = Config.Bind("034 - Weapons", "GrenadeScale", 1.3f,
                new ConfigDescription("Scale of the grenade held in the hand.",
                    new AcceptableValueRange<float>(0.05f, 5f)));
            CfgGrenadeOffX = Config.Bind("034 - Weapons", "GrenadeOffsetX", -0.055f,
                new ConfigDescription("Grenade offset in the hand, sideways, in metres.",
                    new AcceptableValueRange<float>(-0.5f, 0.5f)));
            CfgGrenadeOffY = Config.Bind("034 - Weapons", "GrenadeOffsetY", 0.5f,
                new ConfigDescription("Grenade offset in the hand, vertical, in metres.",
                    new AcceptableValueRange<float>(-0.5f, 0.5f)));
            CfgGrenadeOffZ = Config.Bind("034 - Weapons", "GrenadeOffsetZ", 0.14f,
                new ConfigDescription("Grenade offset in the hand, depth, in metres.",
                    new AcceptableValueRange<float>(-0.5f, 0.5f)));
            CfgGrenadeGesture = Config.Bind("034 - Weapons", "GrenadeGesture", true,
                "Squeeze the left trigger fully to arm the grenade, release it fully to throw. "
                + "Off, the trigger throws on the press - which fires at the lightest touch.");
            CfgGrenadeAimFromMotion = Config.Bind("034 - Weapons", "GrenadeAimFromMotion", true,
                "Throw where the hand is actually moving. Below the speed threshold the "
                + "grenade goes where the hand points instead, so a still release still aims.");
            CfgGrenadeMotionMin = Config.Bind("034 - Weapons", "GrenadeMotionMin", 1.5f,
                new ConfigDescription("How fast the hand must move for the motion to aim the "
                    + "throw, in metres per second.",
                    new AcceptableValueRange<float>(0.2f, 6f)));
            CfgGrenadePowerFromMotion = Config.Bind("034 - Weapons", "GrenadePowerFromMotion",
                true,
                "Throw as hard as you actually threw. The game's own force is kept as the "
                + "reference and multiplied, so the weapon's balance is unchanged.");
            CfgGrenadeRefSpeed = Config.Bind("034 - Weapons", "GrenadeRefSpeed", 6.0f,
                new ConfigDescription("Hand speed that reproduces the game's original throw, "
                    + "in metres per second. Lower it if grenades feel heavy.",
                    new AcceptableValueRange<float>(0.5f, 10f)));
            CfgGrenadePowerMin = Config.Bind("034 - Weapons", "GrenadePowerMin", 0.25f,
                new ConfigDescription("Weakest throw, as a fraction of the game's force. Never "
                    + "zero: a grenade let go at rest must still leave the hand.",
                    new AcceptableValueRange<float>(0.05f, 1f)));
            CfgGrenadePowerMax = Config.Bind("034 - Weapons", "GrenadePowerMax", 1.4f,
                new ConfigDescription("Hardest throw, as a fraction of the game's force.",
                    new AcceptableValueRange<float>(1f, 5f)));
            CfgGrenadeThrowPitch = Config.Bind("034 - Weapons", "GrenadeThrowPitch", 20f,
                new ConfigDescription("Upward tilt applied when aiming by pointing, in degrees. "
                    + "Level along the controller the grenade drops almost at once.",
                    new AcceptableValueRange<float>(0f, 60f)));
            CfgGrenadeArmLevel = Config.Bind("034 - Weapons", "GrenadeArmLevel", 0.9f,
                new ConfigDescription("How far the trigger must travel to arm the grenade.",
                    new AcceptableValueRange<float>(0.4f, 1f)));
            CfgGrenadeReleaseLevel = Config.Bind("034 - Weapons", "GrenadeReleaseLevel", 0.1f,
                new ConfigDescription("How far back the trigger must come to let it go. Well "
                    + "below the arming level, so no amount of trembling crosses both.",
                    new AcceptableValueRange<float>(0f, 0.5f)));
            CfgGrenadeArmScale = Config.Bind("034 - Weapons", "GrenadeArmScale", 1.2f,
                new ConfigDescription("How much the grenade swells once armed.",
                    new AcceptableValueRange<float>(1f, 2f)));
            CfgHudAlwaysVisible = Config.Bind("035 - HUD", "HudAlwaysVisible", false,
                "Shows the HUD permanently instead of only on demand.");
            CfgHudFlashDuration = Config.Bind("035 - HUD", "HudFlashDuration", 2.0f,
                new ConfigDescription(
                    "Grace period given to the game to stop time after the diary or the menu opens, "
                    + "before we conclude that play has resumed.",
                    new AcceptableValueRange<float>(0.2f, 10f)));
            CfgHudFadeSpeed = Config.Bind("035 - HUD", "HudFadeSpeed", 6.0f,
                new ConfigDescription("Fade-in and fade-out speed.",
                    new AcceptableValueRange<float>(1f, 30f)));
            CfgOpenVrBridge = Config.Bind("05 - VR bindings", "OpenVrBridge", true,
                "Reads the controllers from OpenVR directly instead of through Unity's "
                + "legacy joystick layer, which gives the A and X buttons no index at all. "
                + "Set to false if anything about input misbehaves - the mod falls back to "
                + "Unity, and this file can be edited with the game closed.");
            CfgProbe = Config.Bind("04 - Keys", "ControllerProbe", false,
                "Logs every controller button and axis as it changes, per device. The "
                + "merged reads Unity offers mix the two hands and hide which one fired.");
            CfgTraceInput = Config.Bind("05 - VR bindings", "TraceInput", false,
                "Logs every VR action that changes state, with its binding. Used to tell an input "
                + "that never reports apart from an action the game ignores.");
            CfgAxisThreshold = Config.Bind("05 - VR bindings", "AxisThreshold", 0.55f,
                new ConfigDescription(
                    "How hard a grip must be squeezed to count as pressed. The grips are analog: too low "
                    + "and they fire on their own, too high and you have to crush them.",
                    new AcceptableValueRange<float>(0.05f, 0.95f)));

            if (!CfgEnabled.Value)
            {
                Log.LogWarning("AwayVR disabled through the configuration.");
                return;
            }

            VrBindings.Init(Config);

            _harmony = new Harmony(Guid);

            // One failing patch must not take the mod down with it. A method that does not
            // exist in this Unity version makes PatchAll throw, and that exception used to
            // abort the whole start-up - the game then launched flat, with no VR at all.
            PatchSafely(typeof(Patches.FpcPatches));
            PatchSafely(typeof(Patches.InputPatches));
            PatchSafely(typeof(Patches.InputRedirect));
            PatchSafely(typeof(Patches.HitZone));
            PatchSafely(typeof(Grenades));
            PatchSafely(typeof(Swing));
            try { Patches.InputRedirect.Apply(_harmony); }
            catch (System.Exception e) { Log.LogError("Input redirect failed: " + e.Message); }
            ImguiCapture.Apply(_harmony);
            int n = 0;
            foreach (var m in _harmony.GetPatchedMethods()) n++;
            Log.LogInfo("Patches Harmony appliques : " + n + " methode(s).");

            var host = new GameObject("AwayVR_Manager");
            DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            VrFade.Init();
            host.AddComponent<VrManager>();
            host.AddComponent<Menu.VrMenu>();

            Log.LogInfo("AwayVR 0.1.0 initialise.");
        }
    }
}

