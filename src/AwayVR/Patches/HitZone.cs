using HarmonyLib;
using UnityEngine;

namespace AwayVR.Patches
{
    /// <summary>
    /// Gives the damage volume an orientation. Its position is the game's own.
    ///
    /// sword_hit_zone is instantiated with Quaternion.identity and its Update only ever writes a
    /// POSITION, so the collider keeps a world-fixed orientation for its whole life: turn around
    /// and it no longer lines up with where you face. Anything but a sphere therefore covers the
    /// wrong ground, and only by accident the right one.
    ///
    /// Only the rotation is set, and from the game's own reference so it stays consistent with the
    /// position that reference dictates. Moving the volume onto the weapon was tried and was a
    /// mistake: the game's placement is generous on purpose, and putting it on the tip meant a hit
    /// only landed when the weapon was exactly on the target.
    /// </summary>
    [HarmonyPatch(typeof(sword_hit_zone))]
    internal static class HitZone
    {
        /// <summary>
        /// Applied once on Start, before the collider is read, and never touched again: Update
        /// only ever writes a position, so the scale survives on its own.
        /// </summary>
        [HarmonyPatch("Start")]
        [HarmonyPostfix]
        private static void Start_Postfix(sword_hit_zone __instance)
        {
            if (!VrManager.VrActive) return;

            float k = Plugin.CfgHitboxScale.Value;
            if (k <= 1.001f) return;
            __instance.transform.localScale *= k;
        }

        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        private static void Update_Postfix(sword_hit_zone __instance)
        {
            if (!VrManager.VrActive || !Plugin.CfgOrientHitbox.Value) return;

            var reference = repere_feedback_position.Position_actuelle_du_repere_feedback;
            if (reference == null) return;

            __instance.transform.rotation = reference.rotation;
        }
    }
}
