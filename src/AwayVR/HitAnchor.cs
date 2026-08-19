using UnityEngine;

namespace AwayVR
{
    public enum HitboxPlacement
    {
        /// <summary>The game's own: pinned to the anchor, so it rides on the headset.</summary>
        Game,
        /// <summary>
        /// Placed by the mod, in front of the character: same geometry the game authored, but
        /// measured from the body's fixed eye height instead of the headset's.
        /// </summary>
        Ahead
    }

    /// <summary>
    /// Keeps the game's damage anchor on the head.
    ///
    /// Every melee hit box is a sword_hit_zone, and its Update pins it to
    /// repere_feedback_position.Position_actuelle_du_repere_feedback - authored as a child of
    /// the camera, two metres straight ahead. Reparent that object anywhere else and the whole
    /// of melee combat moves with it: on the rig it faces where the BODY faces, and since the
    /// body only turns on a snap, turning on the spot physically leaves every blow landing
    /// beside the enemy while the weapon passes visibly through it.
    ///
    /// Nothing in the game moves it. The mod could, so the mod checks - and puts it back.
    /// </summary>
    internal static class HitAnchor
    {
        private static Vector3 _localPos;
        private static Quaternion _localRot;
        private static bool _captured;

        /// <summary>Authored pose is per scene: each level carries its own copy.</summary>
        public static void Forget() { _captured = false; }

        /// <summary>Where the anchor sits, for the scene report.</summary>
        public static Transform Current
        {
            get { return repere_feedback_position.Position_actuelle_du_repere_feedback; }
        }

        /// <summary>True when the anchor hangs under the VR camera, as authored.</summary>
        public static bool OnHead
        {
            get
            {
                var t = Current;
                var cam = VrManager.MainCamera;
                return t != null && cam != null && t.parent == cam.transform;
            }
        }

        /// <summary>
        /// Where the mod puts the damage volume: the game's own geometry - two metres ahead,
        /// slightly below the eye line - but measured from the CHARACTER rather than from the
        /// headset.
        ///
        /// The game built that geometry for an eye welded at a fixed height above the body. In
        /// VR that height is whatever your posture says: crouch fifty centimetres and the whole
        /// volume follows you down, and the mod re-zeroes the tracking origin on every scene
        /// load, so the reference is whatever pose you happened to be in as the level appeared.
        /// Measured from the body instead, none of that reaches the fight.
        ///
        /// Direction still comes from the head - you hit where you look, and turning on the spot
        /// physically is carried - but the pitch is bounded: at two metres out, a glance down
        /// you never meant as aiming buries the volume in the floor.
        /// </summary>
        public static bool Ahead(out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;

            var cam = VrManager.MainCamera;
            if (cam == null || !_captured) return false;

            var body = VrManager.PlayerRoot;
            if (body == null && VrManager.Rig != null) body = VrManager.Rig.parent;
            if (body == null) return false;

            var fwd = cam.transform.forward;
            var cap = new Vector3(fwd.x, 0f, fwd.z);
            if (cap.sqrMagnitude < 1e-6f)
            {
                // Straight up or down leaves no heading to flatten; the top of the head points
                // where the face does in that pose.
                var up = cam.transform.up;
                cap = new Vector3(up.x, 0f, up.z);
                if (cap.sqrMagnitude < 1e-6f) return false;
            }

            float limite = Plugin.CfgHitboxPitch.Value;
            float tangage = Mathf.Clamp(
                -Mathf.Asin(Mathf.Clamp(fwd.y, -1f, 1f)) * Mathf.Rad2Deg, -limite, limite);

            rot = Quaternion.LookRotation(cap.normalized, Vector3.up)
                  * Quaternion.Euler(tangage, 0f, 0f);

            // World scale stretches the play area, and with it the reach the anchor would have
            // had as a child of the scaled rig.
            float echelle = VrManager.Rig != null ? VrManager.Rig.localScale.x : 1f;

            pos = body.position
                  + Vector3.up * VrManager.AuthoredEyeHeight
                  + rot * (_localPos * echelle);
            return true;
        }

        public static void Tick()
        {
            var t = Current;
            var cam = VrManager.MainCamera;
            if (t == null || cam == null) return;

            if (t.parent == cam.transform)
            {
                // The reference pose, taken while it is still where the game put it.
                if (!_captured)
                {
                    _localPos = t.localPosition;
                    _localRot = t.localRotation;
                    _captured = true;
                }
                return;
            }

            // Without a captured pose we would put it back at an invented place, which is
            // worse than leaving it: the position is the whole reach of the weapon.
            if (!_captured) return;

            t.SetParent(cam.transform, false);
            t.localPosition = _localPos;
            t.localRotation = _localRot;
            Plugin.Log.LogWarning("Damage anchor had left the camera: put back at "
                                  + _localPos.ToString("0.000")
                                  + ". Melee hits were following the body, not the head.");
        }
    }
}
