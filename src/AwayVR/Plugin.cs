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
        internal static ConfigEntry<bool> CfgRecenterOnLoad;
        internal static ConfigEntry<bool> CfgRoomScaleMove;
        internal static ConfigEntry<float> CfgRoomScaleDeadzone;
        internal static ConfigEntry<bool> CfgBlockCameraOnWalls;

        // --- camera ---
        internal static ConfigEntry<float> CfgHeightOffset;
        internal static ConfigEntry<float> CfgWorldScale;
        internal static ConfigEntry<float> CfgNearClip;
        internal static ConfigEntry<bool> CfgHmdDrivesPitch;
        internal static ConfigEntry<bool> CfgDisableHeadBob;
        internal static ConfigEntry<WeaponsCameraMode> CfgWeaponsCamera;

        // --- full-screen effects ---
        internal static ConfigEntry<string> CfgDisabledEffects;
        internal static ConfigEntry<bool> CfgDisableBloom;
        internal static ConfigEntry<bool> CfgDisableColorGrading;

        // --- locomotion ---
        internal static ConfigEntry<bool> CfgHeadRelativeMove;
        internal static ConfigEntry<Patches.TurnMode> CfgTurnMode;
        internal static ConfigEntry<float> CfgSnapAngle;
        internal static ConfigEntry<float> CfgSmoothTurnSpeed;
        internal static ConfigEntry<float> CfgTurnDeadzone;

        // --- player body ---
        internal static ConfigEntry<PlayerBodyMode> CfgPlayerBody;

        // --- layers ---
        internal static ConfigEntry<string> CfgHiddenLayers;

        // --- weapons ---
        internal static ConfigEntry<WeaponAttachMode> CfgWeaponAttach;
        internal static ConfigEntry<float> CfgWeaponScale;
        internal static ConfigEntry<WeaponAnchorPoint> CfgWeaponAnchor;
        internal static ConfigEntry<float> CfgWeaponOffX, CfgWeaponOffY, CfgWeaponOffZ;

        // --- swing to attack ---
        internal static ConfigEntry<bool> CfgSwingToAttack;
        internal static ConfigEntry<float> CfgSwingThreshold;
        internal static ConfigEntry<float> CfgSwingCooldown;

        // --- hidden canvases ---
        internal static ConfigEntry<string> CfgHiddenCanvases;

        // --- HUD ---
        internal static ConfigEntry<bool> CfgWorldLockCameraChildren;
        internal static ConfigEntry<float> CfgHudDistance;
        internal static ConfigEntry<float> CfgHudWidth;
        internal static ConfigEntry<float> CfgHudFollowSpeed;

        // --- menu ---
        internal static ConfigEntry<KeyCode> CfgMenuKey;
        internal static ConfigEntry<float> CfgMenuScale;
        internal static ConfigEntry<float> CfgMenuDistance;
        internal static ConfigEntry<float> CfgMenuWidth;
        internal static ConfigEntry<float> CfgMenuVOffset;

        // --- miscellaneous ---
        internal static ConfigEntry<KeyCode> CfgRecenterKey;
        internal static ConfigEntry<KeyCode> CfgDiagKey;
        internal static ConfigEntry<KeyCode> CfgToggleEffectsKey;
        internal static ConfigEntry<KeyCode> CfgStepLayerKey;
        internal static ConfigEntry<KeyCode> CfgResetLayerKey;
        internal static ConfigEntry<KeyCode> CfgStepCanvasKey;
        internal static ConfigEntry<bool> CfgVerbose;
        internal static ConfigEntry<bool> CfgProbe;
        internal static ConfigEntry<float> CfgAxisThreshold;
        internal static ConfigEntry<bool> CfgTraceInput;
        internal static ConfigEntry<bool> CfgDialogCapture;
        internal static ConfigEntry<float> CfgDialogDistance;
        internal static ConfigEntry<float> CfgDialogWidth;
        internal static ConfigEntry<float> CfgDialogFollowSpeed;
        internal static ConfigEntry<bool> CfgHudAlwaysVisible;
        internal static ConfigEntry<bool> CfgVrFade;
        internal static ConfigEntry<float> CfgFadeDistance;
        internal static ConfigEntry<float> CfgFadeDuration;
        internal static ConfigEntry<float> CfgFadeHold;
        internal static ConfigEntry<bool> CfgFadeOnCharacterSwap;
        internal static ConfigEntry<float> CfgCharacterFadeDuration;
        internal static ConfigEntry<bool> CfgGrenadeInHand;
        internal static ConfigEntry<bool> CfgGrenadeFromHand;
        internal static ConfigEntry<float> CfgGrenadeScale;
        internal static ConfigEntry<float> CfgGrenadeOffX, CfgGrenadeOffY, CfgGrenadeOffZ;
        internal static ConfigEntry<bool> CfgGrenadeGesture;
        internal static ConfigEntry<float> CfgGrenadeArmLevel, CfgGrenadeReleaseLevel;
        internal static ConfigEntry<float> CfgGrenadeArmScale;
        internal static ConfigEntry<bool> CfgBlackDefaultSky;
        internal static ConfigEntry<float> CfgHudSceneReminder;
        internal static ConfigEntry<float> CfgHudSceneDelay;
        internal static ConfigEntry<float> CfgHudFlashDuration;
        internal static ConfigEntry<float> CfgHudFadeSpeed;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            CfgEnabled = Config.Bind("01 - XR", "Enabled", true,
                "Enables VR. When false the game starts normally in 2D.");
            CfgDevice = Config.Bind("01 - XR", "Device", "OpenVR",
                "Name of the XR device to load. Must appear in globalgamemanagers' enabledVRDevices.");
            CfgResolutionScale = Config.Bind("01 - XR", "ResolutionScale", 1.0f,
                new ConfigDescription("Eye texture supersampling.", new AcceptableValueRange<float>(0.5f, 2.0f)));
            CfgRecenterOnLoad = Config.Bind("01 - XR", "RecenterOnSceneLoad", true,
                "Recentres the view on every scene load, so you start facing the character's "
                + "forward rather than wherever the headset happened to point at spawn.");

            CfgRoomScaleMove = Config.Bind("01 - XR", "RoomScaleMovement", true,
                "Physically walking moves the character, with collisions. Tracking stays centred "
                + "on the character's eye height: this game has a fixed height, and floor-based "
                + "tracking does not fit it.");
            CfgRoomScaleDeadzone = Config.Bind("01 - XR", "RoomScaleDeadzone", 0.0005f,
                new ConfigDescription("Smallest movement taken into account per frame, in metres. "
                    + "A plain noise floor: a large value produces visible jumps.",
                    new AcceptableValueRange<float>(0.0001f, 0.02f)));

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
            CfgNearClip = Config.Bind("02 - Camera", "NearClipPlane", 0.05f,
                "Near clip plane in VR. Too large and hands or weapons get cut off.");
            CfgHmdDrivesPitch = Config.Bind("02 - Camera", "HmdDrivesPitch", true,
                "The headset drives pitch and roll; the mouse only turns the body (yaw).");
            CfgDisableHeadBob = Config.Bind("02 - Camera", "DisableHeadBob", true,
                "Disables the walking head bob, which causes motion sickness and fights the tracking.");
            CfgWeaponsCamera = Config.Bind("02 - Camera", "WeaponsCameraMode", WeaponsCameraMode.Merge,
                "Keep = leave the weapons camera as an overlay. Merge = fold its layers into the main camera. Disable = hide the weapons.");

            CfgDisabledEffects = Config.Bind("03 - Effects", "DisabledEffects",
                "",
                "Comma-separated names of components to switch off on the cameras in VR. Empty by "
                + "default: the F11 test showed that none of this game's effects breaks stereo.");

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

            CfgPlayerBody = Config.Bind("032 - Player body", "PlayerBodyMode", PlayerBodyMode.ShadowsOnly,
                "The player mesh sits outside the frustum in 2D, but the headset moves the viewpoint "
                + "and you see it from inside. ShadowsOnly = invisible but still casts its "
                + "shadow. Hide = layer removed entirely. Keep = original state.");

            CfgHiddenLayers = Config.Bind("033 - Layers", "HiddenLayers", "",
                "Comma-separated layers (name or index) removed from the main camera's culling mask "
                + "in VR. Empty by default; an escape hatch for when stray geometry is found "
                + "through bisection (StepLayer key).");

            CfgWeaponAttach = Config.Bind("034 - Weapons", "AttachTo", WeaponAttachMode.Right,
                "Hand to attach the viewmodel to. Off = leaves the weapons on the camera.");
            CfgWeaponScale = Config.Bind("034 - Weapons", "Scale", 0.40f,
                new ConfigDescription("Viewmodel scale. A viewmodel is authored oversized: unnoticeable on a screen, "
                    + "glaring in VR.", new AcceptableValueRange<float>(0.05f, 2f)));
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

            CfgWorldLockCameraChildren = Config.Bind("035 - HUD", "WorldLockCameraChildren", true,
                "Detaches whatever the game parents to the camera (the menu video quad, panels) and "
                + "reattaches it to the rig. Those objects then stop following the head, which "
                + "is essential for comfort on the title screen.");


            CfgSwingToAttack = Config.Bind("034 - Weapons", "SwingToAttack", true,
                "Swinging your hand triggers the attack, for MELEE weapons only. Throwing weapons "
                + "keep the trigger: hurling a projectile by waving your hand would be "
                + "imprecise.");
            CfgSwingThreshold = Config.Bind("034 - Weapons", "SwingThreshold", 4.0f,
                new ConfigDescription("Hand speed that triggers a swing, in m/s. Lower is more sensitive, at the risk of "
                    + "unintended swings.",
                    new AcceptableValueRange<float>(0.3f, 6f)));
            CfgSwingCooldown = Config.Bind("034 - Weapons", "SwingCooldown", 0.45f,
                new ConfigDescription("Minimum delay between two swings, in seconds.",
                    new AcceptableValueRange<float>(0.05f, 2f)));

            CfgHiddenCanvases = Config.Bind("035 - HUD", "HiddenCanvases", "",
                "Comma-separated names of canvases to switch off. Managed from the in-game menu.");


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
            CfgMenuScale = Config.Bind("036 - Menu", "Scale", 1.6f,
                new ConfigDescription("Menu scale on the desktop mirror only.",
                    new AcceptableValueRange<float>(0.5f, 4f)));
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
            CfgStepLayerKey = Config.Bind("04 - Keys", "StepLayer", KeyCode.F7,
                "Bisection: hides one more layer on the main camera with each press, restoring the "
                + "previous one. Used to put a name on stray geometry.");
            CfgStepCanvasKey = Config.Bind("04 - Keys", "StepCanvas", KeyCode.F5,
                "Bisection: hides one more canvas with each press, restoring the previous one. Used "
                + "to identify a stray panel without going through the menu.");
            CfgResetLayerKey = Config.Bind("04 - Keys", "ResetLayers", KeyCode.F6,
                "Restores the original culling mask.");
            CfgToggleEffectsKey = Config.Bind("04 - Keys", "ToggleAllEffects", KeyCode.F11,
                "Toggles ALL camera effects, OnRenderImage and CommandBuffer alike. Used to isolate whichever one breaks the image.");
            CfgVerbose = Config.Bind("04 - Keys", "Verbose", true, "Verbose logging.");
            CfgProbe = Config.Bind("04 - Keys", "ControllerProbe", false,
                "Logs controller buttons, device by device.");
            CfgDialogCapture = Config.Bind("035 - HUD", "DialogCapture", true,
                "Captures the IMGUI drawing of the dialogues, invisible in VR, and shows it on a "
                + "panel in front of the player.");
            CfgDialogDistance = Config.Bind("035 - HUD", "DialogDistance", 1.60f,
                new ConfigDescription("Distance of the dialogue panel, in metres.",
                    new AcceptableValueRange<float>(0.5f, 6f)));
            CfgDialogWidth = Config.Bind("035 - HUD", "DialogWidth", 2.00f,
                new ConfigDescription("Width of the dialogue panel, in metres.",
                    new AcceptableValueRange<float>(0.5f, 8f)));
            CfgBlackDefaultSky = Config.Bind("02 - Camera", "BlackDefaultSkybox", true,
                "Replaces Unity's default skybox with a black background. Screens with no scenery, "
                + "such as the rewards screen, otherwise leave an empty blue sky in VR.");
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
            CfgFadeDistance = Config.Bind("035 - HUD", "FadeDistance", 0.30f,
                new ConfigDescription(
                    "Distance of the fade surface, in metres. Close enough to cover the view, "
                    + "far enough not to clip through the near plane.",
                    new AcceptableValueRange<float>(0.15f, 2f)));
            CfgFadeDuration = Config.Bind("035 - HUD", "FadeDuration", 0.60f,
                new ConfigDescription("How long the fade out into the scene takes, in seconds.",
                    new AcceptableValueRange<float>(0.05f, 5f)));
            CfgFadeHold = Config.Bind("035 - HUD", "FadeHold", 0.25f,
                new ConfigDescription(
                    "How long the view stays fully covered after a scene comes up, before "
                    + "the fade out begins. Covers the frames the game spends settling.",
                    new AcceptableValueRange<float>(0f, 5f)));
            CfgFadeOnCharacterSwap = Config.Bind("035 - HUD", "FadeOnCharacterSwap", true,
                "Punctuates a character swap with a short fade.");
            CfgCharacterFadeDuration = Config.Bind("035 - HUD", "CharacterFadeDuration", 0.35f,
                new ConfigDescription("Length of that fade, in seconds.",
                    new AcceptableValueRange<float>(0.05f, 2f)));
            CfgGrenadeInHand = Config.Bind("034 - Weapons", "GrenadeInHand", true,
                "Shows a grenade in the left hand whenever you have one left.");
            CfgGrenadeFromHand = Config.Bind("034 - Weapons", "GrenadeFromHand", true,
                "Throws the grenade from the left hand, in the direction it points, "
                + "instead of from the camera.");
            CfgGrenadeScale = Config.Bind("034 - Weapons", "GrenadeScale", 1.0f,
                new ConfigDescription("Scale of the grenade held in the hand.",
                    new AcceptableValueRange<float>(0.05f, 5f)));
            CfgGrenadeOffX = Config.Bind("034 - Weapons", "GrenadeOffsetX", 0f,
                new ConfigDescription("Grenade offset in the hand, sideways, in metres.",
                    new AcceptableValueRange<float>(-0.5f, 0.5f)));
            CfgGrenadeOffY = Config.Bind("034 - Weapons", "GrenadeOffsetY", 0f,
                new ConfigDescription("Grenade offset in the hand, vertical, in metres.",
                    new AcceptableValueRange<float>(-0.5f, 0.5f)));
            CfgGrenadeOffZ = Config.Bind("034 - Weapons", "GrenadeOffsetZ", 0f,
                new ConfigDescription("Grenade offset in the hand, depth, in metres.",
                    new AcceptableValueRange<float>(-0.5f, 0.5f)));
            CfgGrenadeGesture = Config.Bind("034 - Weapons", "GrenadeGesture", true,
                "Squeeze the left trigger fully to arm the grenade, release it fully to throw. "
                + "Off, the trigger throws on the press — which fires at the lightest touch.");
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
            CfgDialogFollowSpeed = Config.Bind("035 - HUD", "DialogFollowSpeed", 4.0f,
                new ConfigDescription(
                    "Follow speed of the dialogue panel. Low = heavily damped, catching up with the head "
                    + "slowly. 0 = frozen where it appeared.",
                    new AcceptableValueRange<float>(0f, 20f)));
            CfgTraceInput = Config.Bind("05 - VR bindings", "TraceInput", true,
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
            _harmony.PatchAll(typeof(Patches.FpcPatches));
            _harmony.PatchAll(typeof(Patches.InputPatches));
            _harmony.PatchAll(typeof(Patches.InputRedirect));
            _harmony.PatchAll(typeof(Grenades));
            Patches.InputRedirect.Apply(_harmony);
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

    public enum WeaponsCameraMode
    {
        Keep,
        Merge,
        Disable
    }
}
