using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AwayVR.Patches
{
    public enum DropAim
    {
        /// <summary>The game's own: a direction frozen when the weapon was created.</summary>
        Game,
        /// <summary>Where the weapon points, so where your hand points.</summary>
        Weapon,
        /// <summary>Where you look.</summary>
        Gaze
    }

    /// <summary>
    /// Aims the tree's throw at where you point, instead of at a direction decided long ago.
    ///
    /// weapons_bombs_droper measures its firing direction ONCE, in Start, and keeps it for the
    /// whole life of the weapon:
    ///
    ///     cameraSpawnDirection = main.transform.InverseTransformDirection(transform.forward);
    ///     ...
    ///     AddForce(main.transform.TransformDirection(cameraSpawnDirection) * speedpower);
    ///
    /// Held under the camera that is exact and free - the weapon cannot move relative to the head,
    /// so one measurement stands for all of them. Held in a HAND it is a snapshot of wherever your
    /// arm happened to be at the instant the weapon appeared, kept forever: the throw leaves at a
    /// fixed angle from your head that has nothing to do with your aim, and no amount of pointing
    /// changes it.
    ///
    /// Refreshing the field just before the throw is enough - the game then computes back exactly
    /// the direction we put in, and its own code is left untouched.
    /// </summary>
    [HarmonyPatch]
    internal static class ThrowAimPatches
    {
        private static readonly FieldInfo FDirection =
            AccessTools.Field(typeof(weapons_bombs_droper), "cameraSpawnDirection");

        [HarmonyPatch(typeof(weapons_bombs_droper), "drop")]
        [HarmonyPrefix]
        private static void Drop_Prefix(weapons_bombs_droper __instance)
        {
            if (!VrManager.VrActive || FDirection == null || __instance == null) return;

            var mode = Plugin.CfgDropAim.Value;
            if (mode == DropAim.Game) return;

            var cam = VrManager.MainCamera;
            if (cam == null) return;

            var direction = mode == DropAim.Gaze
                ? cam.transform.forward
                : __instance.transform.forward;

            // Stored the way the game reads it back: in the camera's frame.
            FDirection.SetValue(__instance, cam.transform.InverseTransformDirection(direction));
        }
    }
}
