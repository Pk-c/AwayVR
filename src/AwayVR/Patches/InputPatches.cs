using HarmonyLib;

namespace AwayVR.Patches
{
    /// <summary>
    /// VR bindings for the actions going through InputM.GetAction, the single funnel for
    /// attacks, guarding and navigation - hooking it remaps all of them and inherits the
    /// game's animations, trails and hit boxes for free.
    ///
    /// The prefix REPLACES the original read: the left trigger becomes the grenade, so it
    /// must stop triggering guard.
    /// </summary>
    internal static class InputPatches
    {
        [HarmonyPatch(typeof(InputM), "GetAction")]
        [HarmonyPrefix]
        private static bool GetAction_Prefix(InputAction id, ref bool __result)
        {
            if (!VrManager.VrActive) return true;

            switch (id)
            {
                case InputAction.Guard:
                    __result = VrBindings.Held(VrBindings.Action.Guard);
                    return false;

                case InputAction.ToggleGameMenu:
                    __result = VrBindings.Down(VrBindings.Action.GameMenu);
                    return false;

                case InputAction.NextTab:
                    // A single key that cycles: there is no "previous" to assign.
                    __result = VrBindings.Down(VrBindings.Action.NextTab);
                    return false;

                case InputAction.PreviousTab:
                    __result = false;
                    return false;

                default:
                    return true;   // attacks: original behaviour, extended below
            }
        }

        /// <summary>
        /// Swing-to-attack, added on top of the button-triggered attacks. The right trigger
        /// keeps working alongside it.
        /// </summary>
        [HarmonyPatch(typeof(InputM), "GetAction")]
        [HarmonyPostfix]
        private static void GetAction_Postfix(InputAction id, ref bool __result)
        {
            if (__result) return;
            if (!VrManager.VrActive || !Plugin.CfgSwingToAttack.Value) return;

            // ChannelAttack is a held charge: a brief swing makes no sense for it.
            if (id != InputAction.Attack && id != InputAction.AttackCanCharge) return;

            if (Swing.IsSwinging) __result = true;
        }
    }
}
