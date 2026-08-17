using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace AwayVR.Patches
{
    /// <summary>
    /// Redirects the game's button reads onto our VR bindings.
    ///
    /// UnityEngine.Input.GetButton* is an extern method and cannot be patched directly. So
    /// we rewrite the GAME METHODS that call it, swapping the call for ours: the original
    /// behaviour is preserved and the VR binding takes its place. Nothing breaks when no
    /// input is assigned.
    /// </summary>
    internal static class InputRedirect
    {
        // --- substitutes called in place of UnityEngine.Input ---

        // All three substitutes REPLACE the original read as soon as the action is remapped.
        // An additive version would leave the game still reading "any joystick", where a
        // left-hand button and its right-hand counterpart share an index: pressing one would
        // still fire the action bound to the other.

        public static bool GetButton(string name)
        {
            VrBindings.Action a;
            if (VrManager.VrActive && VrBindings.Remap(name, out a)) return VrBindings.Held(a);
            return Input.GetButton(name);
        }

        public static bool GetButtonDown(string name)
        {
            // Grenades go through the gesture, never through a press. The trigger reports a
            // press almost as soon as it is touched, so bound directly it threw a grenade at
            // the lightest contact.
            if (VrManager.VrActive && name == "Grenades" && Plugin.CfgGrenadeGesture.Value)
                return Grenades.ConsumeThrow();

            VrBindings.Action a;
            if (VrManager.VrActive && VrBindings.Remap(name, out a)) return VrBindings.Down(a);
            return Input.GetButtonDown(name);
        }

        public static bool GetButtonUp(string name)
        {
            VrBindings.Action a;
            if (VrManager.VrActive && VrBindings.Remap(name, out a)) return VrBindings.Up(a);
            return Input.GetButtonUp(name);
        }

        /// <summary>
        /// Pause menu. ShowPanels does NOT read it through a named button but through
        /// Input.GetKeyDown(KeyCode.Escape), which the transpiler used to let through: the
        /// menu was therefore unreachable from a VR controller. JoystickButton7 is the other
        /// key the game accepts - absent from Touch - so we redirect it too.
        /// </summary>
        public static bool GetKeyDown(KeyCode k)
        {
            if (VrManager.VrActive && (k == KeyCode.Escape || k == KeyCode.JoystickButton7))
            {
                bool d = VrBindings.Down(VrBindings.Action.GameMenu);
                if (d && Plugin.CfgTraceInput != null && Plugin.CfgTraceInput.Value)
                    Plugin.Log.LogInfo("[input] Escape simulated for the pause menu (" + k + ")");
                return d;
            }
            return Input.GetKeyDown(k);
        }

        // ------------------------------------------------------------------

        private static readonly MethodInfo OrigGet =
            AccessTools.Method(typeof(Input), "GetButton", new[] { typeof(string) });
        private static readonly MethodInfo OrigDown =
            AccessTools.Method(typeof(Input), "GetButtonDown", new[] { typeof(string) });
        private static readonly MethodInfo OrigUp =
            AccessTools.Method(typeof(Input), "GetButtonUp", new[] { typeof(string) });
        private static readonly MethodInfo OrigKeyDown =
            AccessTools.Method(typeof(Input), "GetKeyDown", new[] { typeof(KeyCode) });

        private static readonly MethodInfo OursGet =
            AccessTools.Method(typeof(InputRedirect), "GetButton");
        private static readonly MethodInfo OursDown =
            AccessTools.Method(typeof(InputRedirect), "GetButtonDown");
        private static readonly MethodInfo OursUp =
            AccessTools.Method(typeof(InputRedirect), "GetButtonUp");
        private static readonly MethodInfo OursKeyDown =
            AccessTools.Method(typeof(InputRedirect), "GetKeyDown");

        /// <summary>Generic transpiler: substitutes our reads for Unity's.</summary>
        public static IEnumerable<CodeInstruction> Redirect(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var ins in instructions)
            {
                if (ins.opcode == OpCodes.Call)
                {
                    if (ins.operand as MethodInfo == OrigGet) { yield return new CodeInstruction(OpCodes.Call, OursGet); continue; }
                    if (ins.operand as MethodInfo == OrigDown) { yield return new CodeInstruction(OpCodes.Call, OursDown); continue; }
                    if (ins.operand as MethodInfo == OrigUp) { yield return new CodeInstruction(OpCodes.Call, OursUp); continue; }
                    if (ins.operand as MethodInfo == OrigKeyDown) { yield return new CodeInstruction(OpCodes.Call, OursKeyDown); continue; }
                }
                yield return ins;
            }
        }

        /// <summary>
        /// Game methods to rewrite. Each was identified as the sole reader of the matching
        /// action, which spares us from patching blindly.
        /// </summary>
        private static readonly string[,] Targets =
        {
            { "weapons_secondary", "Update" },   // grenade
            { "Slots_Handler", "Update" },       // weapon / character switching
            { "JourneyDiary", "Update" },        // diary, reads "MAP"
            { "ShowPanels", "Update" },          // title menus, reads "Cancel" and Escape
            { "Pause", "Update" },               // IN-GAME pause, reads "Cancel"
            { "story_next", "Update" },          // advancing a dialogue, reads "Submit"
            { "XP_scenario", "Update" },         // same on the XP screens
            { "UnityStandardAssets.Characters.FirstPerson.FirstPersonController",
              "UpdateIsWalking" },               // running

            // The game replaces Unity's input module with its own, and those call
            // Input.GetButtonDown directly rather than through BaseInput - so patching
            // BaseInput does nothing for them. This is what validates dialogues and menus.
            { "MultiPlatformInputModule", "SendSubmitEventToSelectedObject" },
            { "MultiPlatformInputModule", "ShouldActivateModule" },
            { "GamePadInputModule", "SendSubmitEventToSelectedObject" },
            { "GamePadInputModule", "ShouldActivateModule" },
            { "NoTouchpadInputModule", "HandleSelection" },
            { "AimerInputModule", "Process" }
        };

        /// <summary>
        /// Jump goes through CrossPlatformInputManager rather than Input: that method is
        /// managed code, so it can be patched directly.
        /// </summary>
        [HarmonyPatch(typeof(UnityStandardAssets.CrossPlatformInput.CrossPlatformInputManager),
                      "GetButtonDown")]
        [HarmonyPrefix]
        private static bool Cpim_GetButtonDown(string name, ref bool __result)
        {
            VrBindings.Action a;
            if (!VrManager.VrActive || !VrBindings.Remap(name, out a)) return true;
            __result = VrBindings.Down(a);
            return false;
        }

        [HarmonyPatch(typeof(UnityStandardAssets.CrossPlatformInput.CrossPlatformInputManager),
                      "GetButton")]
        [HarmonyPrefix]
        private static bool Cpim_GetButton(string name, ref bool __result)
        {
            VrBindings.Action a;
            if (!VrManager.VrActive || !VrBindings.Remap(name, out a)) return true;
            __result = VrBindings.Held(a);
            return false;
        }

        /// <summary>
        /// Menu validation. Unity's EventSystem reads "Submit" from inside UnityEngine.UI,
        /// which the transpiler cannot reach - it only rewrites the game's own methods. This
        /// one prefix covers every input module at once.
        /// </summary>
        [HarmonyPatch(typeof(UnityEngine.EventSystems.BaseInput), "GetButtonDown")]
        [HarmonyPrefix]
        private static bool BaseInput_GetButtonDown(string buttonName, ref bool __result)
        {
            VrBindings.Action a;
            if (!VrManager.VrActive || !VrBindings.Remap(buttonName, out a)) return true;
            __result = VrBindings.Down(a);
            return false;
        }

        // BaseInput has GetButtonDown but no GetButton in this Unity version: patching one
        // that does not exist aborts PatchAll, and with it the whole plugin.

        public static void Apply(Harmony harmony)
        {
            var transpiler = new HarmonyMethod(
                AccessTools.Method(typeof(InputRedirect), "Redirect"));

            for (int i = 0; i < Targets.GetLength(0); i++)
            {
                var type = AccessTools.TypeByName(Targets[i, 0]);
                if (type == null)
                {
                    Plugin.Log.LogWarning("Input redirect: type not found " + Targets[i, 0]);
                    continue;
                }

                var method = AccessTools.Method(type, Targets[i, 1]);
                if (method == null)
                {
                    Plugin.Log.LogWarning("Input redirect: method not found "
                                          + Targets[i, 0] + "." + Targets[i, 1]);
                    continue;
                }

                try
                {
                    harmony.Patch(method, transpiler: transpiler);
                    Plugin.Log.LogInfo("  input redirected: " + Targets[i, 0] + "." + Targets[i, 1]);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning("Cannot redirect input on "
                                          + Targets[i, 0] + "." + Targets[i, 1] + ": " + e.Message);
                }
            }
        }
    }
}
