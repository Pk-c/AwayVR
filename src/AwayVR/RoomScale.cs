using UnityEngine;
using UnityEngine.XR;

namespace AwayVR
{
    /// <summary>
    /// Room-scale movement: physically walking moves the character.
    ///
    /// Driven by the CHANGE in head pose, never its absolute position - with the absolute
    /// value, compensating the rig left the measured offset unchanged and the character
    /// walked away forever.
    ///
    /// Each step advances the CharacterController (so with collisions) and moves the rig back
    /// by the distance actually covered, keeping the camera at a constant offset from the
    /// capsule. The pose comes from InputTracking, independent of that compensation.
    /// </summary>
    internal static class RoomScale
    {
        private static CharacterController _cc;
        private static Vector3 _lastHead;
        private static bool _hasLast;

        /// <summary>Accumulated compensation, expressed in the rig parent's space.</summary>
        public static Vector3 Offset { get; private set; }

        /// <summary>Last measured head step, and how many crossed the deadzone. Diagnostic.</summary>
        public static float LastStep { get; private set; }
        public static int Moves { get; private set; }

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

            // Applied every frame with no accumulation: waiting for a total and catching up
            // all at once produced 2 cm jumps that shook the view.
            //
            // The deadzone matters more than it looks. Every step past it calls the game's own
            // CharacterController.Move, in the same frame as the game's own movement - so a
            // value below real tracking noise means calling it constantly for jitter, which
            // recomputes grounding and can eat part of the walk. Half a millimetre was such a
            // value; it is a setting again because the right figure depends on the headset.
            LastStep = step.magnitude;
            if (LastStep < Plugin.CfgRoomScaleDeadzone.Value) return;
            Moves++;

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
