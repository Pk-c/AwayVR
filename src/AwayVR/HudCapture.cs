using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Renders the game's canvases into a texture shown on a VR panel. The canvases stay in
    /// screen mode, driven end to end by Unity, but attached to a camera of ours that draws
    /// into a texture: nothing to recompute, and the game's off-screen parking is clipped by
    /// the camera frame exactly as the screen used to clip it.
    ///
    /// The camera sits far from any scenery, so it sees only what is attached to it.
    /// </summary>
    internal static class HudCapture
    {
        private const string CameraName = "AwayVR_UiCamera";
        private static readonly Vector3 FarAway = new Vector3(0f, 100000f, 0f);

        private static Camera _cam;
        private static RenderTexture _rt;
        private static readonly VrPanel _panel = new VrPanel();

        public static bool Active { get; private set; }

        /// <summary>Current opacity, smoothed so the HUD fades in and out.</summary>
        private static float _alpha;
        /// <summary>Display requested until the game resumes its clock.</summary>
        private static bool _untilTimeResumes;

        /// <summary>Current reason for showing the HUD, logged whenever it changes.</summary>
        private static string _reason = "";

        /// <summary>Target opacity, according to what the player and the game ask for.</summary>
        private static float Target()
        {
            string r = Reason();
            if (r != _reason)
            {
                _reason = r;
                if (Plugin.CfgVerbose.Value)
                    Plugin.Log.LogInfo("HUD: " + (r.Length == 0 ? "hidden" : "visible - " + r));
            }
            return r.Length == 0 ? 0f : 1f;
        }

        private static string Reason()
        {
            // Menu and start-up screens: there the HUD IS the interface, so it must stay
            // visible. The player's chosen behaviour only applies once the first gameplay
            // scene has loaded.
            if (!VrManager.InGame) return "outside gameplay";

            // Arrival reminder: nothing at all for a moment, then the HUD for a couple of
            // seconds. The delay matters - a scene comes up mid-load with the view still
            // settling, and showing the HUD immediately means it is gone before there is
            // anything to read.
            float now = Time.unscaledTime;
            if (now >= _reminderFrom && now < _reminderUntil) return "scene arrival";

            if (Plugin.CfgHudAlwaysVisible.Value) return "always-visible option";

            // Diary and pause menu: the game stops time. Rather than guessing a duration,
            // we stay visible for as long as the clock is stopped.
            if (VrBindings.Down(VrBindings.Action.Map) || VrBindings.Down(VrBindings.Action.GameMenu))
            {
                _untilTimeResumes = true;
                // The game plays a whole animation before suspending time: without this
                // grace period we would conclude it had resumed before it ever stopped.
                _stopGrace = Time.unscaledTime + Plugin.CfgHudFlashDuration.Value;
            }

            if (_untilTimeResumes)
            {
                if (Time.timeScale <= 0.0001f) return "time stopped";
                if (Time.unscaledTime < _stopGrace) return "waiting for time to stop";
                _untilTimeResumes = false;
            }

            // One input, held, shows the HUD and releasing it hides it again. It used to be
            // either grip, which put it on guard as well - and guard is held for long stretches
            // of a fight, so the panel sat in front of you throughout.
            if (VrBindings.Held(VrBindings.Action.ShowHud)) return "hud button";
            return "";
        }

        private static float _stopGrace = -999f;
        private static float _reminderUntil = -999f;
        private static float _reminderFrom = -999f;
        private static bool _firstLoadSeen;

        /// <summary>Call on entering each gameplay scene: briefly shows the HUD.</summary>
        public static void OnSceneLoaded()
        {
            // No reminder for the very first load. That one is leaving the menu for the hub,
            // where the game hands you a quiet arrival - flashing the HUD across it is the
            // one place it reads as an intrusion rather than a courtesy. Every later load is
            // a transition mid-play, where knowing your health and ammo is worth having.
            if (!_firstLoadSeen)
            {
                _firstLoadSeen = true;
                _reminderFrom = -999f;
                _reminderUntil = -999f;
                return;
            }

            _reminderFrom = Time.unscaledTime + Plugin.CfgHudSceneDelay.Value;
            _reminderUntil = _reminderFrom + Plugin.CfgHudSceneReminder.Value;
        }

        /// <summary>Camera to attach canvases to, or null if capture is unavailable.</summary>
        public static Camera UiCamera()
        {
            Prepare();
            return _cam;
        }

        private static void Prepare()
        {
            int w = Mathf.Max(Screen.width, 16);
            int h = Mathf.Max(Screen.height, 16);

            if (_rt != null && (_rt.width != w || _rt.height != h))
            {
                if (_cam != null) _cam.targetTexture = null;
                Object.Destroy(_rt);
                _rt = null;
            }
            if (_rt == null)
            {
                // 24 bits, NOT 16: that is the only format carrying the 8 stencil bits
                // Unity's UI uses for masking. Without them every Mask component fails
                // silently - the compass rendered square instead of round, and the HUD
                // background, which is meant to be clipped, filled the whole frame.
                _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                _rt.name = "AwayVR_HudRT";
                _rt.Create();
                if (_cam != null) _cam.targetTexture = _rt;
            }

            if (_cam != null) return;

            var go = new GameObject(CameraName);
            Object.DontDestroyOnLoad(go);
            go.transform.position = FarAway;
            go.transform.rotation = Quaternion.identity;

            _cam = go.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.orthographic = false;
            _cam.fieldOfView = 60f;
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 1000f;
            _cam.depth = -100f;              // renders before the main camera
            _cam.targetTexture = _rt;
            _cam.stereoTargetEye = StereoTargetEyeMask.None;
            _cam.allowHDR = false;

            Plugin.Log.LogInfo("HUD capture: UI camera created, texture " + w + "x" + h + ".");
        }

        public static void Tick()
        {
            if (!VrManager.VrActive)
            {
                if (Active)
                {
                    Active = false;
                    _panel.Show(false);
                    _alpha = 0f;
                    if (_cam != null) _cam.enabled = false;
                }
                return;
            }

            Prepare();
            if (_cam == null || _rt == null) return;

            if (!Active)
            {
                Active = true;
                _cam.enabled = true;
                _panel.Recentre();
            }

            _panel.Ensure("AwayVR_HudPanel", 31000);

            float target = Target();
            // Recentre as it appears, otherwise the panel slides in from wherever the head
            // was pointing the last time it was visible.
            if (target > 0f && _alpha <= 0.001f) _panel.Recentre();

            // Framerate-independent fade: the HUD must not appear faster just because the
            // scene happens to be cheap to render.
            float k = 1f - Mathf.Exp(-Plugin.CfgHudFadeSpeed.Value
                                     * Mathf.Max(Time.unscaledDeltaTime, 0.0001f));
            _alpha = Mathf.Lerp(_alpha, target, k);
            if (_alpha < 0.004f) _alpha = 0f;

            // The UI camera renders a full 1920x1080 pass into its texture. It used to do
            // so on every frame of the game, whether or not the HUD was on screen - a whole
            // camera's culling and draw submission for an image nobody was looking at. It is
            // switched on from the moment the HUD is asked for, which is before the fade
            // reaches visibility, so the texture is always ready in time.
            bool wanted = target > 0f || _alpha > 0f;
            if (_cam.enabled != wanted) _cam.enabled = wanted;

            bool visible = _alpha > 0f;
            _panel.Show(visible);
            if (!visible) return;

            _panel.SetTexture(_rt, _alpha);

            Place();
        }

        /// <summary>
        /// Placement alone, replayed just before the frame is drawn. See
        /// VrManager.PlaceBeforeRender: doing this in LateUpdate only meant working from a
        /// head pose one frame old, which is what made the panel shimmer when nodding.
        /// </summary>
        public static void Place()
        {
            if (!Active || _alpha <= 0f || _rt == null) return;
            _panel.Place(Plugin.CfgHudDistance.Value,
                         Plugin.CfgHudWidth.Value,
                         _rt.width, _rt.height);
        }

        public static void Forget()
        {
            _panel.Destroy();
            Active = false;
        }
    }
}
