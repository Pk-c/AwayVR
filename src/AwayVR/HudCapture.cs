using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Renders the game's canvases into a texture, shown on a VR panel.
    ///
    /// Same contract as the dialogue capture, and for the same reason: we no longer touch
    /// the layout. The canvases stay in screen mode, driven end to end by Unity — rect,
    /// CanvasScaler, anchors — but attached to a camera of OUR OWN that draws into a
    /// texture instead of the screen.
    ///
    /// This answers the three failures of the world-space approach, where we took that
    /// computation over ourselves:
    ///  - no geometry left to recompute, so no more content flung out of frame;
    ///  - this game hides its UI by SLIDING it off screen (the diary is parked at
    ///    +1114 px); the camera frame clips that overflow naturally, just as the screen
    ///    used to;
    ///  - size is adjusted by changing the panel, never the canvas.
    ///
    /// The camera sits very far from any scenery, so it sees nothing but the canvases
    /// attached to it — no need to meddle with layers that belong to the game.
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
                    Plugin.Log.LogInfo("HUD: " + (r.Length == 0 ? "hidden" : "visible — " + r));
            }
            return r.Length == 0 ? 0f : 1f;
        }

        private static string Reason()
        {
            // Menu and start-up screens: there the HUD IS the interface, so it must stay
            // visible. The player's chosen behaviour only applies once the first gameplay
            // scene has loaded.
            if (!VrManager.InGame) return "outside gameplay";

            // Short reminder on arriving in a scene: you get to see your ammo, health and
            // characters without having to ask for the HUD.
            if (Time.unscaledTime < _reminderUntil) return "scene arrival";

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

            // Holding a grip shows the HUD; releasing it hides the HUD again.
            if (VrBindings.Held(VrBindings.Action.SwitchWeapon)) return "left grip";
            if (VrBindings.Held(VrBindings.Action.Guard)) return "right grip";
            return "";
        }

        private static float _stopGrace = -999f;
        private static float _reminderUntil = -999f;

        /// <summary>Call on entering each gameplay scene: briefly shows the HUD.</summary>
        public static void OnSceneLoaded()
        {
            _reminderUntil = Time.unscaledTime + Plugin.CfgHudSceneReminder.Value;
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
                // silently — the compass rendered square instead of round, and the HUD
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

            bool visible = _alpha > 0f;
            _panel.Show(visible);
            if (!visible) return;

            _panel.SetTexture(_rt, _alpha);

            var view = VrManager.MainCamera;
            if (view == null) return;

            _panel.Place(view,
                         Plugin.CfgHudDistance.Value,
                         Plugin.CfgHudWidth.Value,
                         Plugin.CfgHudFollowSpeed.Value,
                         _rt.width, _rt.height);
        }

        public static void Forget()
        {
            _panel.Destroy();
            Active = false;
        }
    }
}
