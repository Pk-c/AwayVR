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

        /// <summary>How far the panel currently lags behind the gaze, in degrees.</summary>
        private float _lag;
        private float _previousYaw;
        private bool _initialised;

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
        /// Places the panel in front of the gaze, with a damped follow.
        ///
        /// Everything happens in LOCAL coordinates, under the camera that renders the panel.
        /// Placing it in world coordinates relative to the main camera required the two
        /// poses to match exactly — Unity re-latches the head pose just before rendering,
        /// and the slightest discrepancy shifted the panel on screen. As a child of its own
        /// camera it is framed correctly whatever that pose turns out to be.
        ///
        /// The damping therefore acts on a simple lag angle: the head turns, the panel falls
        /// behind, then catches back up to centre. Only yaw is followed; pitch and roll
        /// would tip the panel over with the head.
        /// </summary>
        public void Place(Camera cam, float distance, float width, float speed,
                          int pixelWidth, int pixelHeight)
        {
            var parent = PanelOverlay.Anchor;
            if (_canvas == null || parent == null) return;

            var panel = (RectTransform)_canvas.transform;
            if (panel.parent != parent) panel.SetParent(parent, false);

            float yaw = parent.eulerAngles.y;
            if (!_initialised)
            {
                _previousYaw = yaw;
                _lag = 0f;
                _initialised = true;
            }

            // Lag built up by head rotation, then absorbed. Mathf.DeltaAngle handles the
            // wrap through 360 degrees, which would otherwise cause a jump.
            _lag -= Mathf.DeltaAngle(_previousYaw, yaw);
            _previousYaw = yaw;

            // Zero speed means the panel is LOCKED to the head, with no lag at all.
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

            panel.sizeDelta = new Vector2(Mathf.Max(pixelWidth, 1), Mathf.Max(pixelHeight, 1));
            float scale = width / Mathf.Max(pixelWidth, 1);
            panel.localScale = new Vector3(scale, scale, scale);

            var rot = Quaternion.Euler(0f, _lag, 0f);
            panel.localRotation = rot;
            panel.localPosition = rot * new Vector3(0f, 0f, distance);
        }

        /// <summary>Snap back into place as the panel appears, instead of sliding in.</summary>
        public void Recentre() { _initialised = false; }

        public void Destroy()
        {
            if (_canvas != null) Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _image = null;
            _rect = null;
            _initialised = false;
        }
    }
}
