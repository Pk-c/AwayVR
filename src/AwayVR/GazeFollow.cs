using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Shared damped gaze follow, computed once per frame for the HUD, the dialogue box and
    /// the virtual screens - separate damping let them drift apart.
    ///
    /// YAW only. A panel that tips over with your head is disorienting.
    /// </summary>
    internal static class GazeFollow
    {
        /// <summary>World yaw the panels should face, in degrees.</summary>
        public static float Yaw { get; private set; }

        /// <summary>World position the panels should orbit around.</summary>
        public static Vector3 Origin { get; private set; }

        private static float _lag;
        private static float _previousYaw;
        private static bool _initialised;

        /// <summary>Snap back into place: used when a panel appears after being hidden.</summary>
        public static void Recentre() { _initialised = false; }

        /// <summary>
        /// Call every frame in LateUpdate, once the head pose has been written.
        ///
        /// The reference is the camera that RENDERS the panels, not the game's main camera:
        /// reading the latter and rendering with the former is exactly what used to shift
        /// everything on screen whenever the two poses disagreed.
        /// </summary>
        public static void Update(Transform reference, float speed)
        {
            if (reference == null) return;

            Origin = reference.position;

            float yaw = reference.eulerAngles.y;
            if (!_initialised)
            {
                _previousYaw = yaw;
                _lag = 0f;
                _initialised = true;
            }

            // Lag built up by head rotation, then absorbed. Mathf.DeltaAngle handles the wrap
            // through 360 degrees, which would otherwise cause a jump.
            _lag -= Mathf.DeltaAngle(_previousYaw, yaw);
            _previousYaw = yaw;

            if (speed <= 0f)
            {
                _lag = 0f;
            }
            else
            {
                float k = 1f - Mathf.Exp(-speed * Mathf.Max(Time.unscaledDeltaTime, 0.0001f));
                _lag *= 1f - k;
                _lag = Mathf.Clamp(_lag, -90f, 90f);
            }

            Yaw = yaw + _lag;
        }

        /// <summary>
        /// Re-reads the head POSITION just before rendering, where Unity has re-latched the
        /// pose. Not the yaw: the lag was measured against the LateUpdate value, and adding
        /// it to a newer one defeats the damping and makes the panel shake when turning.
        /// </summary>
        public static void Refresh(Transform reference)
        {
            if (reference == null) return;
            Origin = reference.position;
        }

        /// <summary>Level rotation the panels should adopt, in world space.</summary>
        public static Quaternion Rotation { get { return Quaternion.Euler(0f, Yaw, 0f); } }

        /// <summary>Point at the given distance in front of the gaze, at eye height.</summary>
        public static Vector3 PointAt(float distance)
        {
            return Origin + Rotation * Vector3.forward * distance;
        }
    }
}
