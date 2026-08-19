using HarmonyLib;
using UnityEngine;

namespace AwayVR.Patches
{
    /// <summary>
    /// Drives a struck enemy AWAY from the player, which is what the game's own direction means
    /// - it just says it as "ahead of the body", true on a screen where you face what you hit.
    ///
    /// Both classes push their victim along player_teleport.Position_actuelle_du_player.forward,
    /// and animals_hit also copies that rotation onto the debris it spawns. The body's yaw only
    /// moves on a snap turn, so after turning on the spot physically it no longer agrees with
    /// anything you see: the enemy leaves at an angle unrelated to the blow, sometimes towards
    /// you. Each patched call is given the victim, so the push is measured from it.
    ///
    /// The substitution lasts exactly the length of these calls: see PlayerFacing.
    /// </summary>
    [HarmonyPatch]
    internal static class KnockbackPatches
    {
        [HarmonyPatch(typeof(enemies_navigation), "Update")]
        [HarmonyPrefix]
        private static void EnemyUpdate_Prefix(enemies_navigation __instance, ref Transform __state)
        {
            __state = PlayerFacing.Borrow(__instance != null ? __instance.transform : null);
        }

        [HarmonyPatch(typeof(enemies_navigation), "Update")]
        [HarmonyPostfix]
        private static void EnemyUpdate_Postfix(Transform __state)
        {
            PlayerFacing.Return(__state);
        }

        [HarmonyPatch(typeof(animals_hit), "OnTriggerEnter")]
        [HarmonyPrefix]
        private static void AnimalTrigger_Prefix(animals_hit __instance, ref Transform __state)
        {
            __state = PlayerFacing.Borrow(__instance != null ? __instance.transform : null);
        }

        [HarmonyPatch(typeof(animals_hit), "OnTriggerEnter")]
        [HarmonyPostfix]
        private static void AnimalTrigger_Postfix(Transform __state)
        {
            PlayerFacing.Return(__state);
        }

        [HarmonyPatch(typeof(animals_hit), "OnCollisionEnter")]
        [HarmonyPrefix]
        private static void AnimalCollision_Prefix(animals_hit __instance, ref Transform __state)
        {
            __state = PlayerFacing.Borrow(__instance != null ? __instance.transform : null);
        }

        [HarmonyPatch(typeof(animals_hit), "OnCollisionEnter")]
        [HarmonyPostfix]
        private static void AnimalCollision_Postfix(Transform __state)
        {
            PlayerFacing.Return(__state);
        }

        /// <summary>
        /// COSMETIC, and the only thing the player's facing does for a bomb: the rotation is
        /// captured when it is dropped and later worn by the explosion's particle effect. No
        /// force is involved - a bomb's damage volume is placed on the bomb itself.
        /// </summary>
        [HarmonyPatch(typeof(bombs), "Start")]
        [HarmonyPrefix]
        private static void BombStart_Prefix(bombs __instance, ref Transform __state)
        {
            __state = PlayerFacing.Borrow(__instance != null ? __instance.transform : null);
        }

        [HarmonyPatch(typeof(bombs), "Start")]
        [HarmonyPostfix]
        private static void BombStart_Postfix(Transform __state)
        {
            PlayerFacing.Return(__state);
        }
    }
}
