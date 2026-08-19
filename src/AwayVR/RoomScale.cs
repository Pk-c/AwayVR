using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Keeps the collision capsule under the head, horizontally, every frame.
    ///
    /// Driven by the ABSOLUTE error between the two, not by the change in head pose. A loop on the
    /// change can only hold the gap it started with: any initial offset, any drift, any teleport is
    /// preserved forever, and the head ends up standing beside the body - hitting geometry it
    /// cannot see. On the error, the gap is driven to zero and the loop repairs itself whatever
    /// happens to either end.
    ///
    /// Each frame the capsule is advanced by that error through CharacterController.Move, so walls
    /// still apply, and the rig is pulled back by the same amount. Moving the capsule alone would
    /// achieve nothing: the rig is its child, so the head travels with it and the error does not
    /// change. It is the rig that closes the gap; the Move is what makes the body actually travel.
    ///
    /// The vertical axis is never touched. Height belongs to the game's own gravity and to the
    /// player's real height, and a capsule chasing the head vertically would crouch and jump on its
    /// own.
    /// </summary>
    internal static class RoomScale
    {
        /// <summary>Downward bias kept on every move, in metres, to preserve ground contact.</summary>
        private const float StickToGround = 0.01f;

        /// <summary>
        /// A jump no walk can produce. Past it the player was teleported, respawned or moved by a
        /// cutscene, and the compensation gathered against the old position is meaningless.
        /// </summary>
        private const float TeleportJump = 2f;

        private static CharacterController _cc;
        private static Vector3 _lastBody;
        private static bool _hasBody;
        private static bool _wasEnabled;

        /// <summary>Compensation applied to the rig, in its parent's space.</summary>
        public static Vector3 Offset { get; private set; }

        /// <summary>Head-to-capsule distance left after the last correction. Diagnostic.</summary>
        public static float LastError { get; private set; }
        public static int Moves { get; private set; }

        public static void Forget()
        {
            Offset = Vector3.zero;
            _cc = null;
            _hasBody = false;
        }

        public static void Tick()
        {
            if (!VrManager.VrActive || !Plugin.CfgRoomScaleMove.Value)
            {
                // Switched off, the compensation gathered so far would stay applied and leave the
                // head standing beside the capsule. Off means the head back over the body.
                if (_wasEnabled) { Offset = Vector3.zero; _wasEnabled = false; }
                _hasBody = false;
                return;
            }
            _wasEnabled = true;

            var rig = VrManager.Rig;
            var cam = VrManager.MainCamera;
            if (rig == null || rig.parent == null || cam == null) { _hasBody = false; return; }

            var parent = rig.parent;
            if (_cc == null)
            {
                _cc = parent.GetComponent<CharacterController>();
                if (_cc == null) return;   // scene without a character: nothing to move
            }

            var body = _cc.transform.position;

            // A respawn, a teleport or a cutscene move: everything gathered against the old
            // position is void, and correcting towards it would drag the player back.
            if (_hasBody && (body - _lastBody).magnitude > TeleportJump)
            {
                Offset = Vector3.zero;
                _lastBody = body;
                if (VrManager.Instance != null) VrManager.Instance.RequestCentre();
                return;
            }
            _lastBody = body;
            _hasBody = true;

            var error = cam.transform.position - body;
            error.y = 0f;
            LastError = error.magnitude;

            // Below the headset's own noise there is nothing to correct, and calling the game's
            // CharacterController every frame for jitter competes with its own movement.
            if (LastError < Plugin.CfgRoomScaleDeadzone.Value) return;
            Moves++;

            // A purely HORIZONTAL move lifts the capsule off its contact: the controller resolves
            // the sweep with no downward component, isGrounded drops for that frame and the game's
            // own move then takes its airborne branch on the next one. The game keeps its contact
            // with m_StickToGroundForce on every move; we borrow the idea. Small enough not to
            // fight stepOffset on stairs.
            var before = body;
            _cc.Move(new Vector3(error.x, -StickToGround, error.z));
            var covered = _cc.transform.position - before;

            // Two policies against a wall:
            //
            //  - absorb the REQUESTED error: the head stays centred whatever happens, and leaning
            //    into geometry pushes the view instead of letting it through;
            //  - absorb the COVERED distance: tracking stays perfectly 1:1 and the head passes
            //    through walls while the body falls behind.
            var absorb = Plugin.CfgBlockCameraOnWalls.Value ? error : covered;
            absorb.y = 0f;   // the downward bias is ours, never given back to the rig

            var local = parent.InverseTransformVector(absorb);
            Offset -= new Vector3(local.x, 0f, local.z);
        }
    }
}
