using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Puts the lock-on markers ON the enemies, in the world.
    ///
    /// They belong to a screen canvas, which this mod captures into a texture shown on a panel
    /// you raise on demand - so a marker only existed while you were holding the HUD up, which
    /// is exactly when you are not fighting. A targeting reticle you have to open a menu to
    /// consult tells you nothing.
    ///
    /// So each marker is lifted out of the HUD and hung in space on its target, on the panel
    /// layer: drawn by the overlay camera, which shares the main camera's pose and clears depth
    /// only, so a lock stays legible against any background. The game keeps owning them - it
    /// creates them, hides them behind you and destroys them with the lock - we only decide
    /// where they are and how big.
    /// </summary>
    internal static class TargetMarkers
    {
        private const string CanvasName = "AwayVR_TargetCanvas";

        private static Canvas _canvas;

        public static void Forget() { _canvas = null; }

        /// <summary>World-space canvas the markers are moved onto, built on first use.</summary>
        private static Canvas Ensure()
        {
            if (_canvas != null) return _canvas;

            var go = GameObject.Find(CanvasName);
            if (go == null)
            {
                go = new GameObject(CanvasName);
                var c = go.AddComponent<Canvas>();
                c.renderMode = RenderMode.WorldSpace;
                _canvas = c;
            }
            else
            {
                _canvas = go.GetComponent<Canvas>();
                if (_canvas == null) _canvas = go.AddComponent<Canvas>();
            }

            // Scale 1: a canvas unit is a metre here, and every marker is then sized on its
            // own from its distance. Nothing else hangs off this canvas.
            go.transform.SetParent(null, true);
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            // No free layer means no overlay pass: the default layer still draws, simply
            // subject to the scene's depth and to the camera's effects.
            go.layer = PanelOverlay.Layer >= 0 ? PanelOverlay.Layer : 0;
            return _canvas;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        /// <summary>
        /// Hangs one marker on its target. Apparent size is held constant by scaling with the
        /// distance: a lock across the room has to read the same as one at arm's length, and
        /// the game's own scaling did the opposite, being meant for a flat screen.
        /// </summary>
        public static void Place(GameObject marker, Vector3 cible, Camera cam)
        {
            if (marker == null || cam == null) return;

            var canvas = Ensure();
            if (canvas == null) return;

            int layer = canvas.gameObject.layer;
            if (marker.transform.parent != canvas.transform)
            {
                marker.transform.SetParent(canvas.transform, false);
                SetLayer(marker.transform, layer);
            }
            else if (marker.layer != layer)
            {
                SetLayer(marker.transform, layer);
            }

            var vue = cam.transform;
            float distance = Vector3.Distance(cible, vue.position);

            marker.transform.position = cible;
            // Facing AWAY from the eye: a canvas shows its front to whatever its +Z leaves.
            marker.transform.rotation = Quaternion.LookRotation(cible - vue.position, vue.up);

            var rect = marker.transform as RectTransform;
            float px = rect != null && rect.sizeDelta.y > 1f ? rect.sizeDelta.y : 64f;
            float voulu = Mathf.Max(0.01f, Plugin.CfgTargetMarkerSize.Value) * Mathf.Max(0.5f, distance);
            float echelle = voulu / px;

            marker.transform.localScale = new Vector3(echelle, echelle, echelle);
        }
    }
}
