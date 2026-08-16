using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace AwayVR
{
    /// <summary>
    /// A world-space panel showing a texture in front of the player.
    ///
    /// Used for both the dialogue box and the HUD: in either case all we handle is a texture
    /// Unity has already composed, which spares us from reproducing a layout we never
    /// managed to rebuild correctly by hand.
    /// </summary>
    internal class VrPanel
    {
        private Canvas _canvas;
        private RawImage _image;
        private RectTransform _rect;


        public void Ensure(string name, int sortingOrder)
        {
            if (_canvas != null) return;

            var go = new GameObject(name);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = sortingOrder;

            var imgGo = new GameObject("Image");
            imgGo.transform.SetParent(go.transform, false);
            _image = imgGo.AddComponent<RawImage>();
            _image.raycastTarget = false;

            // Draw OVER the scenery. The panel camera only clears depth and renders last, so
            // the depth test has to be neutralised: otherwise geometry already drawn — a
            // nearby wall or NPC — would hide the panel.
            var mat = new Material(Shader.Find("UI/Default"));
            mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            _image.material = mat;

            _rect = (RectTransform)imgGo.transform;
            _rect.anchorMin = Vector2.zero;
            _rect.anchorMax = Vector2.one;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;

            // Dedicated layer: this is what takes the panel out of the main camera's pass,
            // and therefore out of all its full-screen effects.
            PanelOverlay.Adopt(go);
        }

        public void Show(bool visible)
        {
            if (_canvas != null) _canvas.enabled = visible;
        }

        public void SetTexture(Texture tex, float alpha)
        {
            if (_image == null) return;
            _image.texture = tex;
            _image.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
        }

        /// <summary>
        /// Places the panel in front of the gaze, level, with a damped follow.
        ///
        /// The pose is written in WORLD space even though the panel is a child of the camera
        /// that renders it. Both matter, for different reasons:
        ///
        ///  - being a child of its own render camera means the framing is right whatever
        ///    pose Unity re-latches just before drawing, which is what used to shift the
        ///    panel on screen when we placed it relative to the main camera instead;
        ///  - writing a world rotation cancels the parent's pitch and roll. Inheriting the
        ///    full head pose made the panel tip over as soon as you leaned your head, and
        ///    a heads-up display has no business doing that.
        ///
        /// The yaw and its damping come from GazeFollow, shared with the game's virtual
        /// screens so the two can never drift apart.
        /// </summary>
        public void Place(float distance, float width, int pixelWidth, int pixelHeight)
        {
            var parent = PanelOverlay.Anchor;
            if (_canvas == null || parent == null) return;

            var panel = (RectTransform)_canvas.transform;
            if (panel.parent != parent) panel.SetParent(parent, false);


            panel.sizeDelta = new Vector2(Mathf.Max(pixelWidth, 1), Mathf.Max(pixelHeight, 1));
            float scale = width / Mathf.Max(pixelWidth, 1);
            panel.localScale = new Vector3(scale, scale, scale);

            panel.rotation = GazeFollow.Rotation;
            panel.position = GazeFollow.PointAt(distance);
        }

        /// <summary>Snap back into place as the panel appears, instead of sliding in.</summary>
        public void Recentre() { GazeFollow.Recentre(); }

        public void Destroy()
        {
            if (_canvas != null) Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _image = null;
            _rect = null;
        }
    }
}
