using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AwayVR
{
    /// <summary>
    /// Attaches the game's screen-space canvases to the UI camera, which draws them into a
    /// texture displayed on a VR panel.
    ///
    /// A ScreenSpaceOverlay canvas is invisible in VR: Unity draws it into the backbuffer
    /// after the cameras have rendered, so it lands in the mirror window and never in the
    /// eye textures. It has to go through a camera.
    ///
    /// We spent a long time trying to convert these canvases to WorldSpace, recomputing the
    /// rect, scale, anchor and parenting ourselves. Every attempt failed on a different
    /// detail, and for one underlying reason: this game hides its UI by SLIDING it off
    /// screen — the diary is parked at +1114 px, the menu panels at -921 — which a screen
    /// frame clips naturally but a world-space panel leaves floating in your field of view.
    /// Keeping the canvases in screen mode under a camera of our own means Unity retains
    /// full control of the layout, clipping included, and we compute no geometry at all.
    /// </summary>
    internal static class CanvasTools
    {
        private class Original
        {
            public RenderMode Mode;
            public Camera WorldCamera;
            public Transform Parent;
            public Vector3 LocalPos;
            public Quaternion LocalRot;
            public Vector3 LocalScale;
            public Vector2 SizeDelta;
            public bool Enabled;
            public CanvasScaler Scaler;
            public bool ScalerEnabled;
        }

        private static readonly Dictionary<Canvas, Original> Saved = new Dictionary<Canvas, Original>();

        /// <summary>True if this canvas has passed through our hands.</summary>
        public static bool IsHandled(Canvas c) { return c != null && Saved.ContainsKey(c); }

        /// <summary>Render modes tied to the screen, hence invisible in VR.</summary>
        private static bool IsScreenSpace(RenderMode m)
        {
            return m == RenderMode.ScreenSpaceOverlay || m == RenderMode.ScreenSpaceCamera;
        }

        /// <summary>
        /// Sweeps the scene's canvases. Call this periodically: dialogues are instantiated
        /// from a prefab when an NPC speaks, and scenes create their own canvases while the
        /// game is running.
        /// </summary>
        public static int Apply(bool log)
        {
            Prune();

            int n = 0;
            foreach (var canvas in Object.FindObjectsOfType<Canvas>())
            {
                if (canvas == null || !canvas.isRootCanvas) continue;

                if (!Saved.ContainsKey(canvas))
                {
                    if (!IsScreenSpace(canvas.renderMode)) continue;
                    Saved[canvas] = Capture(canvas);
                    if (log)
                        Plugin.Log.LogInfo("  canvas taken over: "
                                           + Hierarchy.Path(canvas.transform));
                }

                var orig = Saved[canvas];

                // Explicit hiding: the manual escape hatch for when a stray canvas cannot be
                // identified automatically. Takes priority over everything else.
                if (Menu.HiddenCanvases.IsHidden(canvas.name))
                {
                    Restore(canvas, orig);
                    canvas.enabled = false;
                    continue;
                }

                ToTexture(canvas, orig);
                n++;
            }
            return n;
        }

        private static Original Capture(Canvas c)
        {
            var rt = c.transform as RectTransform;
            var scaler = c.GetComponent<CanvasScaler>();
            return new Original
            {
                Mode = c.renderMode,
                WorldCamera = c.worldCamera,
                Parent = c.transform.parent,
                LocalPos = c.transform.localPosition,
                LocalRot = c.transform.localRotation,
                LocalScale = c.transform.localScale,
                SizeDelta = rt != null ? rt.sizeDelta : Vector2.zero,
                Enabled = c.enabled,
                Scaler = scaler,
                ScalerEnabled = scaler != null && scaler.enabled
            };
        }

        /// <summary>
        /// Hands the canvas over to the off-scene UI camera. It STAYS in screen mode: Unity
        /// keeps sizing it, laying it out and clipping whatever overflows, exactly as it
        /// would on screen. No geometry is computed here.
        /// </summary>
        private static void ToTexture(Canvas c, Original o)
        {
            var cam = HudCapture.UiCamera();
            if (cam == null) return;

            c.enabled = true;
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = cam;
            c.planeDistance = 10f;

            // The CanvasScaler stays in charge: it is what gives the canvas the size the
            // game expects, and taking that away from it was the source of all our trouble.
            if (o.Scaler != null) o.Scaler.enabled = o.ScalerEnabled;
        }

        private static void Restore(Canvas c, Original o)
        {
            if (o.Scaler != null) o.Scaler.enabled = o.ScalerEnabled;
            if (c.transform.parent != o.Parent)
                c.transform.SetParent(o.Parent, false);

            c.renderMode = o.Mode;
            c.worldCamera = o.WorldCamera;
            c.transform.localPosition = o.LocalPos;
            c.transform.localRotation = o.LocalRot;
            c.transform.localScale = o.LocalScale;

            var rt = c.transform as RectTransform;
            if (rt != null && o.SizeDelta != Vector2.zero) rt.sizeDelta = o.SizeDelta;
            c.enabled = o.Enabled;
        }

        // ------------------------------------------------------------------
        // Manual selection of canvases to hide
        // ------------------------------------------------------------------

        private static int _step = -1;
        private static List<string> _stepNames;

        /// <summary>
        /// Hides one more canvas on each call, restoring the previous one. Faster than
        /// navigating the menu when hunting down a stray panel.
        /// </summary>
        public static void StepHide()
        {
            if (_step < 0) _stepNames = RootCanvasNames();

            _step++;
            if (_stepNames == null || _step >= _stepNames.Count)
            {
                _step = -1;
                _stepNames = null;
                Plugin.CfgHiddenCanvases.Value = "";
                Plugin.Log.LogInfo("Canvas bisection finished: all restored.");
                return;
            }

            Plugin.CfgHiddenCanvases.Value = _stepNames[_step];
            Plugin.Log.LogInfo(string.Format("Canvas hidden: '{0}'   ({1}/{2})",
                _stepNames[_step], _step + 1, _stepNames.Count));
        }

        /// <summary>Names of the scene's root canvases, for the selection page.</summary>
        public static List<string> RootCanvasNames()
        {
            var names = new List<string>();
            foreach (var c in Object.FindObjectsOfType<Canvas>())
            {
                if (c == null || !c.isRootCanvas) continue;
                if (c.name.StartsWith("AwayVR_")) continue;
                if (!names.Contains(c.name)) names.Add(c.name);
            }
            names.Sort(System.StringComparer.OrdinalIgnoreCase);
            return names;
        }

        /// <summary>
        /// Drops canvases destroyed by a scene change. We never clear everything: some
        /// canvases survive scene loads, and recapturing one after conversion would record
        /// the converted state as if it were the original.
        /// </summary>
        private static void Prune()
        {
            List<Canvas> dead = null;
            foreach (var kv in Saved)
            {
                if (kv.Key == null)
                {
                    if (dead == null) dead = new List<Canvas>();
                    dead.Add(kv.Key);
                }
            }
            if (dead == null) return;
            foreach (var m in dead) Saved.Remove(m);
        }
    }
}
