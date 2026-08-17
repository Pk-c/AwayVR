using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace AwayVR
{
    /// <summary>
    /// Frame rate readout in a corner of the view. Shows the smoothed rate and the worst
    /// frame of the last few seconds: an average sits at the refresh rate while a stutter
    /// every two seconds ruins the experience. Judged against the headset's own refresh rate.
    /// </summary>
    internal static class FpsCounter
    {
        private const string RootName = "AwayVR_Fps";

        private static Canvas _canvas;
        private static Text _text;

        private static float _smoothed;
        private static float _worst = float.MaxValue;
        private static float _worstUntil;
        private static float _nextRefresh;

        public static void Forget()
        {
            if (_canvas != null) Object.Destroy(_canvas.gameObject);
            _canvas = null;
            _text = null;
        }

        private static void Build()
        {
            if (_canvas != null) return;

            var go = new GameObject(RootName);
            Object.DontDestroyOnLoad(go);

            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            // Above the HUD and the dialogue, below the fade: a fade is meant to hide
            // everything, and a frame counter shining through it would be absurd.
            _canvas.sortingOrder = 32500;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(420f, 90f);

            var textGo = new GameObject("Value");
            textGo.transform.SetParent(go.transform, false);
            _text = textGo.AddComponent<Text>();
            _text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _text.fontSize = 34;
            _text.alignment = TextAnchor.MiddleLeft;
            _text.supportRichText = true;
            _text.raycastTarget = false;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.text = "";

            var trt = (RectTransform)textGo.transform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
        }

        /// <summary>Target frame rate, from the headset itself.</summary>
        private static float Target
        {
            get
            {
                float r = XRDevice.refreshRate;
                return r > 1f ? r : 90f;
            }
        }

        public static void Tick()
        {
            if (!VrManager.VrActive || !Plugin.CfgFpsCounter.Value)
            {
                if (_canvas != null) _canvas.enabled = false;
                return;
            }

            Build();
            PanelOverlay.Adopt(_canvas.gameObject);
            _canvas.enabled = true;

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.00001f);
            float instant = 1f / dt;

            // Framerate-independent smoothing, so the readout settles at the same speed
            // whatever the rate it is measuring.
            float k = 1f - Mathf.Exp(-4f * dt);
            _smoothed = _smoothed <= 0f ? instant : Mathf.Lerp(_smoothed, instant, k);

            // Rolling worst over a three-second window. Reset by expiry rather than by a
            // ring buffer: the value only has to be honest about the recent past, and a
            // window that never forgets would show one bad load frame for ever.
            float now = Time.unscaledTime;
            if (now >= _worstUntil)
            {
                _worst = instant;
                _worstUntil = now + 3f;
            }
            else if (instant < _worst) _worst = instant;

            // Redrawn four times a second: at frame rate the digits are unreadable.
            if (now < _nextRefresh) return;
            _nextRefresh = now + 0.25f;

            float target = Target;
            string colour = _smoothed >= target * 0.9f ? "#7ddc7d"
                          : _smoothed >= target * 0.75f ? "#e3c15a" : "#e07070";

            _text.text = string.Format("<color={0}>{1:0}</color> fps   <color=#8794a8>min {2:0}</color>",
                                       colour, _smoothed, _worst);
        }

        /// <summary>
        /// Placed just before the frame is drawn, like every other surface we own: the head
        /// pose is re-latched after LateUpdate, and placing there makes it shimmer.
        /// </summary>
        public static void Place()
        {
            if (_canvas == null || !_canvas.enabled) return;

            var eye = PanelOverlay.Anchor;
            if (eye == null && VrManager.MainCamera != null) eye = VrManager.MainCamera.transform;
            if (eye == null) return;

            const float distance = 1.2f;
            var rt = (RectTransform)_canvas.transform;

            // Low and to the left, out of the way of anything you are actually looking at.
            rt.position = eye.position + eye.rotation * new Vector3(-0.36f, -0.30f, distance);
            rt.rotation = eye.rotation;
            rt.localScale = Vector3.one * (0.30f / rt.sizeDelta.x);
        }
    }
}
