using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AwayVR
{
    /// <summary>
    /// In first person, the player mesh is placed outside the camera's frustum. In VR the
    /// headset moves the viewpoint and you end up seeing it from the inside: a dark
    /// silhouette stuck to your head.
    ///
    /// Switching it to ShadowsOnly takes it out of rendering while keeping its cast shadow,
    /// which is preferable to removing the layer from the culling mask.
    /// </summary>
    internal static class PlayerBody
    {
        private static readonly Dictionary<Renderer, ShadowCastingMode> Saved =
            new Dictionary<Renderer, ShadowCastingMode>();

        private static int _layer = -1;

        private static int Layer
        {
            get
            {
                if (_layer < 0)
                {
                    _layer = LayerMask.NameToLayer("Player");
                    if (_layer < 0) _layer = 31;
                }
                return _layer;
            }
        }

        private static float _nextScan;

        /// <summary>Called on scene setup with force, and periodically from the sweep.</summary>
        public static int Apply(Camera cam, bool log) { return Apply(cam, log, false); }

        public static int Apply(Camera cam, bool log, bool force)
        {
            if (cam == null) return 0;

            // Throttled hard. This walks every renderer in the scene, and the body it is
            // looking for only reappears when a character is swapped — twice a second was
            // paying a scene-wide scan for an event that happens once a minute.
            if (!force)
            {
                if (Time.unscaledTime < _nextScan) return 0;
                _nextScan = Time.unscaledTime + 2f;
            }

            int layer = Layer;
            int n = 0;
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || r.gameObject.layer != layer) continue;
                if (r.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;

                if (!Saved.ContainsKey(r)) Saved[r] = r.shadowCastingMode;
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                n++;
                if (log)
                    Plugin.Log.LogInfo("  body hidden (shadow kept): " + Hierarchy.Path(r.transform));
            }
            return n;
        }

        public static void ResetScanTimer() { _nextScan = 0f; }

        private static void RestoreRenderers()
        {
            foreach (var kv in Saved)
                if (kv.Key != null) kv.Key.shadowCastingMode = kv.Value;
            Saved.Clear();
        }

        public static int PlayerLayer { get { return Layer; } }

        public static void Forget()
        {
            Saved.Clear();
        }
    }
}
