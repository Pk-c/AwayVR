using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AwayVR
{
    public enum PlayerBodyMode
    {
        /// <summary>Body invisible but still casting its shadow. Recommended.</summary>
        ShadowsOnly,
        /// <summary>Player layer removed from rendering entirely: no body, no shadow.</summary>
        Hide,
        /// <summary>Original state.</summary>
        Keep
    }

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

        public static int Apply(PlayerBodyMode mode, Camera cam, bool log)
        {
            if (cam == null) return 0;

            int layer = Layer;

            // The culling mask is now recomputed continuously by VrManager: we no longer
            // touch it here, otherwise the two would fight each other.
            if (mode == PlayerBodyMode.Hide)
            {
                RestoreRenderers();
                return 1;
            }

            if (mode == PlayerBodyMode.Keep)
            {
                RestoreRenderers();
                return 0;
            }

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
