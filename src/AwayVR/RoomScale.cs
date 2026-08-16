using UnityEngine;
using UnityEngine.XR;

namespace AwayVR
{
    /// <summary>
    /// Room-scale movement: physically walking moves the character.
    ///
    /// This is driven by the CHANGE in head pose, never by its absolute position. With the
    /// absolute value, moving the body and then compensating the rig left the measured
    /// offset unchanged on the next frame, and the character walked away forever.
    ///
    /// For each physical step dH: the CharacterController advances by dH (so with
    /// collisions), and the rig moves back by whatever distance was actually covered. The
    /// camera then sits at a CONSTANT offset from the capsule, which is exactly what we
    /// want: collisions stay underneath the head.
    ///
    /// The pose is read from InputTracking rather than from the camera, which keeps it
    /// independent of the compensation we have just applied.
    /// </summary>
    internal static class RoomScale
    {
        private static CharacterController _cc;
        private static Vector3 _lastHead;
        private static bool _hasLast;

        /// <summary>Accumulated compensation, expressed in the rig parent's space.</summary>
        public static Vector3 Offset { get; private set; }

        public static void Forget()
        {
            Offset = Vector3.zero;
            _cc = null;
            _hasLast = false;
        }

        public static void Tick()
        {
            if (!VrManager.VrActive || !Plugin.CfgRoomScaleMove.Value) { _hasLast = false; return; }

            var rig = VrManager.Rig;
            if (rig == null || rig.parent == null) { _hasLast = false; return; }

            var parent = rig.parent;
            if (_cc == null)
            {
                _cc = parent.GetComponent<CharacterController>();
                if (_cc == null) return;   // scene without a character: nothing to move
            }

            var head = InputTracking.GetLocalPosition(XRNode.Head);
            var flat = new Vector3(head.x, 0f, head.z);

            if (!_hasLast)
            {
                _lastHead = flat;
                _hasLast = true;
                return;
            }

            var step = flat - _lastHead;
            _lastHead = flat;

            // Applied every frame, with no accumulation. The previous version waited for the
            // total to exceed the threshold and then caught up all at once: those discrete
            // 2 cm jumps were what made the view shake whenever it was pushed back. The
            // threshold now only serves to ignore tracking noise.
            if (step.magnitude < Plugin.CfgRoomScaleDeadzone.Value) return;

            var world = rig.TransformVector(step);
            world.y = 0f;

            var before = _cc.transform.position;
            _cc.Move(world);
            var covered = _cc.transform.position - before;

            // Two policies when up against a wall:
            //
            //  - absorb the REQUESTED step: the view is blocked along with the body, so the
            //    camera never passes through anything and the capsule stays exactly under
            //    the head. The price is a break in 1:1 tracking while you lean into the
            //    wall, which is the usual trade-off;
            //  - absorb the COVERED step: tracking stays perfect but the head goes through
            //    walls and the body falls behind.
            var toAbsorb = Plugin.CfgBlockCameraOnWalls.Value ? world : covered;

            var local = parent.InverseTransformVector(toAbsorb);
            Offset -= new Vector3(local.x, 0f, local.z);
        }
    }
}
