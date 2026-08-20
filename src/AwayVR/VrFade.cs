using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AwayVR
{
    /// <summary>
    /// Our own fade, for CHARACTER SWAPS only. Mirroring the game's plate was tried and
    /// cannot work: it never ramps, the game switches it on and off outright.
    ///
    /// The game's plate is silenced regardless - on the floating HUD panel it is just an ugly
    /// black rectangle.
    ///
    /// The surface is created once at start-up and kept at the root, not parented to the
    /// panel camera: parenting a DontDestroyOnLoad object to a scene object would destroy its
    /// persistence, and that camera is rebuilt with every scene. It is placed in world
    /// coordinates instead, so the cover is already standing when a scene comes up.
    /// </summary>
    internal static class VrFade
    {
        private const string PanelName = "AwayVR_FadePanel";

        private static Canvas _canvas;
        private static Image _image;

        /// <summary>Current opacity, 1 = fully covered.</summary>
        private static float _alpha;

        /// <summary>Held fully opaque until this moment, then it fades out.</summary>
        private static float _holdUntil = -999f;

        /// <summary>The game's own plates, silenced with the state we found them in.</summary>
        private static readonly Dictionary<Graphic, bool> Silenced = new Dictionary<Graphic, bool>();
        private static readonly List<Graphic> Found = new List<Graphic>();
        private static float _nextScan;

        /// <summary>
        /// Call when a scene has just come up: covers the view, then fades out.
        ///
        /// This is what removes the second of raw scene you used to see before the game's own
        /// plate appeared. We do not wait to discover anything - the cover goes up first, and
        /// the search for the game's plate happens behind it.
        /// </summary>
        private static bool _sceneCover;
        private static float _coverDeadline;

        public static void OnSceneLoaded()
        {
            // Cover the view the instant the scene comes up. The game's own BlinkEffect takes
            // over the transition, but it starts a fraction of a second late and the raw
            // scene shows through until it does.
            if (Plugin.CfgSceneCover.Value)
            {
                _alpha = 1f;
                _holdUntil = float.MaxValue;
                _duration = 0.2f;
                _sceneCover = true;
                _coverDeadline = Time.unscaledTime + Plugin.CfgSceneCoverMax.Value;
            }

            // The graphics are destroyed by the load, so every reference goes with them. No
            // fade is raised here: a scene load blanks the view on its own.
            Silenced.Clear();
            _nextScan = 0f;
        }

        /// <summary>
        /// The game's full-screen plates. Found only to be silenced: nothing here drives the
        /// fade any more, so a false positive can no longer darken the view.
        /// </summary>
        private static void SilenceGamePlates()
        {
            // Known plates are silenced EVERY frame. Only the search is throttled: the game
            // switches its plate back on for a single frame when it flashes one - on a
            // character swap, for instance - and a sweep every half second let exactly that
            // through as a black rectangle on the HUD.
            foreach (var kv in Silenced)
                if (kv.Key != null && kv.Key.enabled) kv.Key.enabled = false;

            // The search stops as soon as it succeeds. A scene has its fade plate or it has
            // none, and once found it is silenced every frame from the list above - there is
            // nothing left to look for.
            //
            // It is re-armed on a character swap because that is precisely when the game
            // flashes a plate, and a scene that had none until then may have built one.
            if (Silenced.Count > 0 && !GameState.CharacterChanged) return;
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 2f;

            float screenWidth = Mathf.Max(Screen.width, 1);
            Found.Clear();

            foreach (var c in Object.FindObjectsOfType<Canvas>())
            {
                if (c == null || !c.isRootCanvas) continue;
                if (c.name.StartsWith("AwayVR_")) continue;
                if (!CanvasTools.IsHandled(c)) continue;

                var canvasRect = c.transform as RectTransform;
                if (canvasRect == null) continue;

                // Full-screen canvas only. This is what rules out the minimap and the tinted
                // veil it carries, which a looser rule once mistook for a fade.
                if (canvasRect.rect.width < screenWidth * 0.8f) continue;

                float canvasArea = canvasRect.rect.width * canvasRect.rect.height;
                if (canvasArea <= 1f) continue;

                foreach (var g in c.GetComponentsInChildren<Graphic>(true))
                {
                    if (g == null || g is Text) continue;
                    if (g.name.IndexOf("fade", System.StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        var r0 = g.rectTransform.rect;
                        if (r0.width * r0.height < canvasArea * 0.75f) continue;
                    }
                    Found.Add(g);
                }
            }

            foreach (var g in Found)
            {
                if (g == null) continue;
                if (!Silenced.ContainsKey(g))
                {
                    Silenced[g] = g.enabled;
                    if (Plugin.CfgVerbose.Value)
                        Plugin.Log.LogInfo("Game fade plate silenced: '" + g.name + "' on "
                                           + Hierarchy.Path(g.transform));
                }
                if (g.enabled) g.enabled = false;
            }
        }

        /// <summary>
        /// Raises the cover and fades it out again. Used for scene loads and, more briefly,
        /// for character swaps.
        /// </summary>
        public static void Flash(float hold, float duration)
        {
            if (!Plugin.CfgVrFade.Value) return;
            _alpha = 1f;
            _holdUntil = Time.unscaledTime + hold;
            _duration = Mathf.Max(duration, 0.01f);
        }

        /// <summary>Duration of the fade out currently running.</summary>
        private static float _duration = 0.6f;

        /// <summary>
        /// Punctuates a character swap with a short fade.
        ///
        /// Polled rather than patched: the game writes Slots_Handler.active_char from half a
        /// dozen places, and hooking each of them would be far more fragile than watching the
        /// value everything else already agrees on.
        /// </summary>
        private static void WatchCharacterSwap()
        {
            if (!Plugin.CfgFadeOnCharacterSwap.Value) return;
            if (!GameState.CharacterChanged) return;
            Flash(0f, Plugin.CfgCharacterFadeDuration.Value);
        }

        /// <summary>
        /// Holds the cover until the game's blink has started, then hands over. The deadline
        /// exists because a scene may have no blink at all, and a cover with no end is worse
        /// than the half second it was hiding.
        /// </summary>
        private static void WatchSceneCover()
        {
            if (!_sceneCover) return;

            bool handOver = Time.unscaledTime >= _coverDeadline;

            if (!handOver && Time.unscaledTime >= _nextBlinkCheck)
            {
                _nextBlinkCheck = Time.unscaledTime + 0.1f;
                var blink = Object.FindObjectOfType<PostProcess.BlinkEffect>();
                handOver = blink != null && blink.enabled;
            }

            if (!handOver) return;
            _sceneCover = false;
            _holdUntil = Time.unscaledTime;
        }

        private static float _nextBlinkCheck;

        /// <summary>Creates the surface once, at start-up, ahead of any scene load.</summary>
        public static void Init()
        {
            Build();
            if (_canvas != null) _canvas.enabled = false;
        }

        private static void Build()
        {
            if (_canvas != null) return;

            var go = new GameObject(PanelName);
            Object.DontDestroyOnLoad(go);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            // Above every other panel: a fade covers the HUD and the dialogue too.
            _canvas.sortingOrder = 32760;

            var imgGo = new GameObject("Plate");
            imgGo.transform.SetParent(go.transform, false);
            _image = imgGo.AddComponent<Image>();
            _image.raycastTarget = false;

            var mat = new Material(Shader.Find("UI/Default"));
            mat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            _image.material = mat;

            // Transparent black from the outset. A UI Image defaults to opaque WHITE, and
            // this surface is created at start-up, before any camera exists: for the frames
            // between creation and the first placement it would otherwise be a white wall.
            _image.color = new Color(0f, 0f, 0f, 0f);

            var rt = (RectTransform)imgGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            PanelOverlay.Adopt(go);
        }

        public static void Tick()
        {
            if (!VrManager.VrActive || !Plugin.CfgVrFade.Value)
            {
                Restore();
                _alpha = 0f;
                if (_canvas != null) _canvas.enabled = false;
                return;
            }

            SilenceGamePlates();
            WatchCharacterSwap();
            WatchSceneCover();

            if (Time.unscaledTime >= _holdUntil && _alpha > 0f)
            {
                float duration = Mathf.Max(_duration, 0.01f);
                // Linear and framerate-independent: a fade has a stated duration, and an
                // exponential curve would leave a faint veil hanging around for ever.
                _alpha -= Time.unscaledDeltaTime / duration;
                if (_alpha < 0.002f) _alpha = 0f;
            }

            if (_canvas == null) Build();

            bool visible = _alpha > 0f;
            _canvas.enabled = visible;
            if (!visible) return;

            // Black, like the game's own transitions. Deliberately not a setting: BepInEx
            // has no guaranteed converter for Color, and an unsupported type makes the whole
            // plugin fail to load - far too high a price for an option nobody asked for.
            _image.color = new Color(0f, 0f, 0f, Mathf.Clamp01(_alpha));

            Place();
        }

        /// <summary>Placement alone, replayed just before the frame is drawn.</summary>
        public static void Place()
        {
            if (_canvas == null || !_canvas.enabled) return;

            // Follows whichever camera can draw it. The panel camera is preferred - it is
            // the one whose layer we live on - with the main camera as a fallback for the
            // frames just after a scene load, before that camera has been rebuilt.
            var eye = PanelOverlay.Anchor;
            if (eye == null && VrManager.MainCamera != null) eye = VrManager.MainCamera.transform;
            if (eye == null) return;

            // Close and generously oversized: it has to cover the whole field of view on any
            // headset, and it is locked to the head rather than following the gaze.
            float d = Plugin.CfgFadeDistance.Value;
            var rt = (RectTransform)_canvas.transform;
            rt.sizeDelta = new Vector2(1000f, 1000f);
            float scale = (d * 8f) / 1000f;
            rt.localScale = new Vector3(scale, scale, scale);
            rt.rotation = eye.rotation;
            rt.position = eye.position + eye.forward * d;
        }

        /// <summary>Hands every silenced plate back exactly as we found it.</summary>
        public static void Restore()
        {
            foreach (var kv in Silenced)
                if (kv.Key != null) kv.Key.enabled = kv.Value;
            Silenced.Clear();
        }
    }
}

