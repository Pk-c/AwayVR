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
        private static readonly FieldInfo FIsWalking =
            AccessTools.Field(typeof(FirstPersonController), "m_IsWalking");

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
            if (!VrManager.VrActive || !FieldsResolved) return;

            var input = (Vector2)FInput.GetValue(__instance);
            if (input.sqrMagnitude < 1e-6f) return;

            input = Shape(input);

            if (Plugin.CfgHeadRelativeMove.Value)
            {
                var cam = VrManager.MainCamera;
                if (cam != null)
                {
                    // Yaw from the FLATTENED FORWARD, never from eulerAngles.y: Euler extraction
                    // is unstable once the transform carries pitch and roll, and a head always
                    // does. Look steeply up or down and the extracted yaw swings, which sent the
                    // character walking somewhere other than where the stick pointed.
                    var headYaw = FlatYaw(cam.transform);
                    var bodyYaw = Quaternion.Euler(0f, __instance.transform.eulerAngles.y, 0f);

                    var wanted = headYaw * new Vector3(input.x, 0f, input.y);
                    var local = Quaternion.Inverse(bodyYaw) * wanted;
                    input = new Vector2(local.x, local.z);
                }
            }

            FInput.SetValue(__instance, input);
        }

        /// <summary>
        /// Response curve on the stick: proportional up to a threshold, constant beyond it.
        ///
        /// The game's displacement is speed * |m_Input| with nothing in between, so deflection is
        /// the throttle. Measured over a walk where the stick was held hard forward throughout, it
        /// reported 0.53 to 1.00 with a median of 0.78 - a thumb simply does not hold the rim of a
        /// small VR stick, and on a flat screen nobody notices. Multiplied straight into the speed
        /// it becomes a pace that surges and sags under a hand that feels perfectly still.
        ///
        /// Saturating early leaves a deliberate half-push slower while making "pushed forward" one
        /// single speed. The dead zone is radial, unlike the game's own square one, so a diagonal
        /// is not truncated.
        ///
        /// Direction is untouched: only the length changes.
        /// </summary>
        private static Vector2 Shape(Vector2 input)
        {
            float mag = input.magnitude;
            if (mag < 1e-6f) return Vector2.zero;

            float dead = Plugin.CfgMoveDeadzone.Value;
            float full = Mathf.Max(dead + 0.05f, Plugin.CfgFullSpeedAt.Value);

            if (mag <= dead) return Vector2.zero;

            float t = Mathf.Clamp01((mag - dead) / (full - dead));
            return input / mag * t;
        }

        /// <summary>
        /// Horizontal heading of a transform, stable at any pitch. Looking straight up or down
        /// leaves no forward to flatten, so the up vector stands in - which is where the face
        /// points in that pose.
        /// </summary>
        private static Quaternion FlatYaw(Transform t)
        {
            var fwd = t.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f)
            {
                fwd = t.up;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-6f) return Quaternion.identity;
            }
            return Quaternion.LookRotation(fwd.normalized, Vector3.up);
        }

        // ------------------------------------------------------------------
        // Miscellaneous
        // ------------------------------------------------------------------

        /// <summary>
        /// The character always runs; the stick alone sets the pace.
        ///
        /// The game has TWO speed controls stacked on each other: a walk/run switch, and the
        /// stick's deflection, since the displacement is speed * |m_Input|. The deflection is
        /// already continuous, so the switch adds nothing a player would want - and it decided
        /// for itself when to flip. It runs only while its button is HELD, returns to walking
        /// speed the first frame it sees a release, and also on 0.15 s of neutral stick; bound to
        /// a stick CLICK that is pushed forward at the same time, the contact broke constantly
        /// and the pace changed mid-stride.
        ///
        /// So the switch is taken out of play, and the whole method with it - the timers no
        /// longer serve anything. Characters the game forbids from running keep their own
        /// behaviour: some scenes rely on it.
        /// </summary>
        [HarmonyPatch(typeof(FirstPersonController), "UpdateIsWalking")]
        [HarmonyPrefix]
        private static bool UpdateIsWalking_Prefix(FirstPersonController __instance)
        {
            if (!VrManager.VrActive || !Plugin.CfgAlwaysRun.Value) return true;
            if (FIsWalking == null || FProfile == null) return true;

            var profile = (FirstPersonController.CharacterProfile)FProfile.GetValue(__instance);
            if (profile != null && !profile.m_CanRun) return true;

            FIsWalking.SetValue(__instance, false);
            return false;
        }

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
