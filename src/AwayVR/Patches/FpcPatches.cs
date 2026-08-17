using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;

// The game also ships a MouseLook at the global namespace root, which shadows the Standard
// Assets one: we disambiguate explicitly.
using FpsMouseLook = UnityStandardAssets.Characters.FirstPerson.MouseLook;

namespace AwayVR.Patches
{
    public enum TurnMode
    {
        /// <summary>Stepped rotation. Far more comfortable, the VR default.</summary>
        Snap,
        /// <summary>Continuous rotation, at a constant speed.</summary>
        Smooth
    }

    /// <summary>
    /// Patches on FirstPersonController: the headset owns head orientation (the game must
    /// write neither pitch nor camera local position), yaw moves to a snap/smooth turn, and
    /// movement becomes relative to the GAZE rather than the body - walking somewhere other
    /// than where you look is the single biggest source of discomfort.
    /// </summary>
    internal static class FpcPatches
    {
        private static readonly FieldInfo FMouseLook =
            AccessTools.Field(typeof(FirstPersonController), "m_MouseLook");
        private static readonly FieldInfo FCamera =
            AccessTools.Field(typeof(FirstPersonController), "m_Camera");
        private static readonly FieldInfo FYaw =
            AccessTools.Field(typeof(FirstPersonController), "_Yaw");
        private static readonly FieldInfo FPitch =
            AccessTools.Field(typeof(FirstPersonController), "_Pitch");
        private static readonly FieldInfo FProfile =
            AccessTools.Field(typeof(FirstPersonController), "m_CurrentCharacterProfile");
        private static readonly FieldInfo FInput =
            AccessTools.Field(typeof(FirstPersonController), "m_Input");

        internal static bool FieldsResolved
        {
            get
            {
                return FMouseLook != null && FCamera != null && FYaw != null
                       && FPitch != null && FProfile != null && FInput != null;
            }
        }

        /// <summary>Arms the snap: stops it turning continuously while the stick is held.</summary>
        private static bool _snapArmed;

        // ------------------------------------------------------------------
        // Yaw: snap / smooth turn
        // ------------------------------------------------------------------

        [HarmonyPatch(typeof(FirstPersonController), "RotateView")]
        [HarmonyPrefix]
        private static bool RotateView_Prefix(FirstPersonController __instance)
        {
            if (!VrManager.VrActive)
                return true;
            if (!FieldsResolved)
                return true;

            var camera = (Camera)FCamera.GetValue(__instance);
            if (camera == null)
                return true;

            var profile = (FirstPersonController.CharacterProfile)FProfile.GetValue(__instance);
            if (profile != null && profile.m_LookAt != null)
            {
                // "Look at this" cutscene: we do not force the player's head (nauseating, and
                // pointless since the headset takes over anyway). We turn the body instead.
                var dir = profile.m_LookAt.position - camera.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 1e-6f)
                {
                    var target = Quaternion.LookRotation(dir);
                    __instance.transform.rotation =
                        Quaternion.Slerp(__instance.transform.rotation, target, Time.deltaTime * 8f);
                }
                return false;
            }

            // We keep _Yaw / _Pitch up to date so the game's state does not drift, but
            // neither is applied: the headset provides the pitch, and yaw now comes from the
            // turn logic below.
            var mouseLook = (FpsMouseLook)FMouseLook.GetValue(__instance);
            if (mouseLook != null)
            {
                var yaw = (Quaternion)FYaw.GetValue(__instance);
                var pitch = (Quaternion)FPitch.GetValue(__instance);
                mouseLook.LookRotation(ref yaw, ref pitch);
                FYaw.SetValue(__instance, yaw);
                FPitch.SetValue(__instance, pitch);
            }

            ApplyTurn(__instance.transform);
            return false;
        }

        private static void ApplyTurn(Transform body)
        {
            float turn = InputM.GetLook().x;
            float delta = 0f;

            if (Plugin.CfgTurnMode.Value == TurnMode.Snap)
            {
                float threshold = Plugin.CfgTurnDeadzone.Value;
                if (Mathf.Abs(turn) >= threshold)
                {
                    if (!_snapArmed)
                    {
                        delta = Mathf.Sign(turn) * Plugin.CfgSnapAngle.Value;
                        _snapArmed = true;
                    }
                }
                // Hysteresis: the stick has to be released properly before snapping again.
                else if (Mathf.Abs(turn) < threshold * 0.6f)
                {
                    _snapArmed = false;
                }
            }
            else
            {
                delta = turn * Plugin.CfgSmoothTurnSpeed.Value * Time.deltaTime;
            }

            if (delta == 0f) return;

            // Rotate around the HEAD, not the body origin. As soon as the camera is offset
            // from the capsule - room-scale, or simply a physical side step - turning around
            // the body flings the player sideways.
            var cam = VrManager.MainCamera;
            if (cam == null)
            {
                body.Rotate(0f, delta, 0f, Space.World);
                return;
            }

            var before = cam.transform.position;
            body.Rotate(0f, delta, 0f, Space.World);

            // Unity transforms update immediately: the camera position read here already
            // accounts for the parent's rotation.
            var correction = before - cam.transform.position;
            correction.y = 0f;
            body.position += correction;
        }

        // ------------------------------------------------------------------
        // Gaze-relative movement
        // ------------------------------------------------------------------

        /// <summary>
        /// The game computes its direction from transform.forward/right, so relative to the
        /// BODY. Rather than rewriting Update, we express the input in the gaze frame: a
        /// single rotation of m_Input is enough, and the rest of the controller follows.
        /// </summary>
        [HarmonyPatch(typeof(FirstPersonController), "GetInput")]
        [HarmonyPostfix]
        private static void GetInput_Postfix(FirstPersonController __instance)
        {
            if (!VrManager.VrActive || !Plugin.CfgHeadRelativeMove.Value) return;
            if (!FieldsResolved) return;

            var cam = VrManager.MainCamera;
            if (cam == null) return;

            var input = (Vector2)FInput.GetValue(__instance);
            if (input.sqrMagnitude < 1e-6f) return;

            var headYaw = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);
            var bodyYaw = Quaternion.Euler(0f, __instance.transform.eulerAngles.y, 0f);

            var wanted = headYaw * new Vector3(input.x, 0f, input.y);
            var local = Quaternion.Inverse(bodyYaw) * wanted;

            FInput.SetValue(__instance, new Vector2(local.x, local.z));
        }

        // ------------------------------------------------------------------
        // Miscellaneous
        // ------------------------------------------------------------------

        /// <summary>Kills the head bob, which writes the camera's local position.</summary>
        [HarmonyPatch(typeof(FirstPersonController), "UpdateCameraPosition")]
        [HarmonyPrefix]
        private static bool UpdateCameraPosition_Prefix()
        {
            return !VrManager.VrActive;
        }

        /// <summary>
        /// player_teleport orients the player through SetRotation, which goes via MouseLook.
        /// Since yaw no longer comes from there, we apply the orientation to the body.
        /// </summary>
        [HarmonyPatch(typeof(FirstPersonController), "SetRotation")]
        [HarmonyPrefix]
        private static void SetRotation_Prefix(FirstPersonController __instance, Transform other)
        {
            if (!VrManager.VrActive || other == null) return;
            __instance.transform.rotation = Quaternion.Euler(0f, other.rotation.eulerAngles.y, 0f);
            _snapArmed = false;
        }
    }
}
