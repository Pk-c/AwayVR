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
        private static float _retryUntil;

        /// <summary>Called on scene setup with force, and periodically from the sweep.</summary>
        public static int Apply(Camera cam, bool log) { return Apply(cam, log, false); }

        public static int Apply(Camera cam, bool log, bool force)
        {
            if (cam == null) return 0;

            // Event-driven. The body is put back by the game when a character is swapped and
            // at no other moment, so the walk happens then and once when the scene starts -
            // never on a timer.
            //
            // The retry window exists because the character's renderers are not always in
            // place on the frame the scene reports itself loaded; without it, a body that
            // arrives late would stay visible until the next swap.
            if (!force)
            {
                if (GameState.CharacterChanged) _retryUntil = Time.unscaledTime + 3f;
                if (Time.unscaledTime > _retryUntil) return 0;
                if (Time.unscaledTime < _nextScan) return 0;
                _nextScan = Time.unscaledTime + 0.5f;
            }
            else _retryUntil = Time.unscaledTime + 3f;

            int layer = Layer;
            int n = 0;

            // Narrowed to the player's own hierarchy. The body is a handful of renderers
            // under the character; sweeping the scene's fifteen hundred to find them meant
            // allocating an array of every renderer in the world twice a minute, and reading
            // a layer off each. The whole-scene sweep survives only as a fallback for the
            // frames before the player exists.
            var renderers = VrManager.PlayerRoot != null
                ? VrManager.PlayerRoot.GetComponentsInChildren<Renderer>(true)
                : Object.FindObjectsOfType<Renderer>();

            foreach (var r in renderers)
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
