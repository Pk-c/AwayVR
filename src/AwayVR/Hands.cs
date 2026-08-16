using UnityEngine;
using UnityEngine.XR;

namespace AwayVR
{
    /// <summary>
    /// Follows an XR node. The pose is reapplied just before rendering as well as in Update:
    /// that is what TrackedPoseDriver does, and without it the hand trails one frame behind
    /// the head, which is immediately obvious in VR.
    /// </summary>
    public class HandTracker : MonoBehaviour
    {
        public XRNode Node = XRNode.RightHand;
        public bool Tracked { get; private set; }

        private void OnEnable() { Application.onBeforeRender += ApplyPose; }
        private void OnDisable() { Application.onBeforeRender -= ApplyPose; }
        private void Update() { ApplyPose(); }

        private void ApplyPose()
        {
            var pos = InputTracking.GetLocalPosition(Node);
            var rot = InputTracking.GetLocalRotation(Node);

            // An untracked node reports identity: rather than snapping the hand to the rig
            // origin, we would rather leave it where it was.
            Tracked = pos != Vector3.zero || rot != Quaternion.identity;
            if (!Tracked) return;

            transform.localPosition = pos;
            transform.localRotation = rot;
        }
    }

    internal static class Hands
    {
        public const string LeftName = "AwayVR_HandLeft";
        public const string RightName = "AwayVR_HandRight";

        public static Transform Left { get; private set; }
        public static Transform Right { get; private set; }

        public static void Ensure(Transform rig)
        {
            if (rig == null) return;
            Left = EnsureOne(rig, LeftName, XRNode.LeftHand);
            Right = EnsureOne(rig, RightName, XRNode.RightHand);
        }

        private static Transform EnsureOne(Transform rig, string name, XRNode node)
        {
            var t = rig.Find(name);
            if (t == null)
            {
                var go = new GameObject(name);
                t = go.transform;
                t.SetParent(rig, false);
            }

            var tracker = t.GetComponent<HandTracker>();
            if (tracker == null) tracker = t.gameObject.AddComponent<HandTracker>();
            tracker.Node = node;
            return t;
        }

        public static Transform Get(HandSide side)
        {
            return side == HandSide.Left ? Left : Right;
        }
    }

    public enum HandSide
    {
        Left,
        Right
    }

    public enum WeaponAnchorPoint
    {
        /// <summary>
        /// Centre of the renderer carrying the model's arm or hand. That is the point which
        /// actually corresponds to your hand, so the one that rotates naturally.
        /// </summary>
        Hand,
        /// <summary>Rear end of the model.</summary>
        Base,
        /// <summary>Front end of the model.</summary>
        Tip,
        /// <summary>Midpoint of all the renderers taken together.</summary>
        Centre,
        /// <summary>The model's raw pivot, uncorrected.</summary>
        Pivot
    }

    public enum WeaponAttachMode
    {
        /// <summary>Weapons stay attached to the camera, as in the flat game.</summary>
        Off,
        Right,
        Left
    }
}
