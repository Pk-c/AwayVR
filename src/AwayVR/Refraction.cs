using System;
using System.Collections.Generic;
using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Geometry whose shader works in SCREEN space rather than in the world — the family the
    /// object walk finally named, and the one no camera-effect switch could ever reach.
    ///
    /// The desert's ghost is '_System/Cloud_System', carrying 'Hidden/ScreenSpaceCloudShadow'
    /// on a mesh spanning the whole world. A screen-space shadow pass reconstructs each
    /// pixel's world position from the depth buffer using the camera's matrices — of which
    /// there is one, while there are two eyes. The cloud pattern therefore lands on different
    /// world positions in each eye: a semi-transparent shadow layer, shaped like the terrain
    /// and offset from it, sliding as the head moves.
    ///
    /// It explains everything that resisted. It is geometry, so it lives on Default and the
    /// layer walk could not separate it. It is not a post-process, so no effect switch
    /// touched it. And a desert has a sky full of moving cloud shadows where a dungeon has
    /// none — the whole outdoor-indoor correlation, without needing performance or the
    /// minimap to explain it.
    ///
    /// A shader with a grab pass captures the frame as rendered SO FAR and samples it back to
    /// fake refraction — heat haze over sand, glass, water. There is one such capture per
    /// frame. In multi-pass stereo the same geometry is drawn twice from two viewpoints while
    /// the grab texture is shared, so what the second eye refracts is the first eye's picture:
    /// a semi-transparent copy of the scenery, offset by the eye separation, sitting over the
    /// real one.
    ///
    /// It matches every observation at once. It is geometry, so it lives on the Default layer
    /// and the layer walk cannot separate it from the rest. It is not a post-process, so no
    /// effect switch touches it. It is transparent, so it reads as a semi-transparent slab.
    /// And a desert has heat haze where a dungeon has none, which is the whole outdoor-indoor
    /// correlation without needing the minimap to explain it.
    ///
    /// Matched on shader NAME rather than by inspecting passes: Unity exposes no way to ask
    /// whether a shader has a grab pass, so the list of fragments is a setting and every
    /// renderer it hides is logged by name — if it takes something it should not, the log
    /// says exactly what to remove from the list.
    /// </summary>
    internal static class Refraction
    {
        private static readonly Dictionary<Renderer, bool> Silenced =
            new Dictionary<Renderer, bool>();

        /// <summary>
        /// Shader-name fragments, frozen rather than configurable. Matched on the NAME because
        /// Unity offers no way to ask a shader whether it works in screen space or grabs the
        /// frame — but the answer never varies from one run to the next, so there is nothing
        /// here for a player to decide. Every renderer hidden is logged with its path and its
        /// shader, which is what a wrong match would need anyway.
        ///
        /// 'screenspacecloud' is the one that mattered: the desert's cloud shadows. The rest
        /// are the grab-pass family — heat haze, glass, water — kept because they break in
        /// exactly the same way for exactly the same reason.
        /// </summary>
        private static readonly string[] Fragments =
        {
            "screenspacecloud", "screenspaceshadow",
            "distort", "refract", "grab", "heat", "glass", "water", "mirage", "haze"
        };

        private static float _nextScan;
        private static int _scans;

        public static void Forget()
        {
            foreach (var kv in Silenced)
                if (kv.Key != null) kv.Key.enabled = kv.Value;
            Silenced.Clear();
            _nextScan = 0f;
            _scans = 0;
        }

        private static bool Matches(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName)) return false;
            var lower = shaderName.ToLowerInvariant();
            for (int i = 0; i < Fragments.Length; i++)
                if (lower.Contains(Fragments[i])) return true;
            return false;
        }

        public static void Tick()
        {
            // Known offenders are re-hidden every frame; only the SEARCH is throttled, since
            // sweeping every renderer in a scene is far too costly at frame rate.
            foreach (var kv in Silenced)
                if (kv.Key != null && kv.Key.enabled) kv.Key.enabled = false;

            if (Time.unscaledTime < _nextScan) return;
            // The offending geometry is placed by the scene and does not come and go, so
            // this is not a patrol: it is a handful of passes after a load to catch anything
            // that spawns late, and then nothing at all. A scene load resets the counter.
            _scans++;
            _nextScan = _scans >= 4 ? float.MaxValue : Time.unscaledTime + 2f;

            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                if (Silenced.ContainsKey(r)) continue;

                // sharedMaterial, NOT sharedMaterials: the plural allocates a fresh array on
                // every access, and this loop runs over every renderer in the scene — fifteen
                // hundred of them in the desert. That allocation was the mod's largest source
                // of garbage, and garbage collection is exactly what a one-in-ten-frames stall
                // looks like.
                var mat = r.sharedMaterial;
                if (mat == null || mat.shader == null) continue;
                if (!Matches(mat.shader.name)) continue;
                string which = mat.shader.name;

                Silenced[r] = r.enabled;
                r.enabled = false;
                Plugin.Log.LogInfo("Screen-space renderer hidden: " + Hierarchy.Path(r.transform)
                                   + "  shader=" + which);
            }
        }

        /// <summary>
        /// Lists the transparent geometry in the scene with its shaders, for the dump. This
        /// is what names the culprit when the keyword list misses it: the ghost is one of
        /// these, and its shader tells us what to add.
        /// </summary>
        public static void Dump(System.Text.StringBuilder sb)
        {
            sb.AppendLine("-- Transparent renderers (queue >= 2500) --");

            var cam = VrManager.MainCamera;
            var eye = cam != null ? cam.transform.position : Vector3.zero;
            var found = new List<KeyValuePair<float, string>>();

            foreach (var r in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                var mats = r.sharedMaterials;
                if (mats == null) continue;

                foreach (var m in mats)
                {
                    if (m == null || m.shader == null) continue;
                    if (m.renderQueue < 2500) continue;

                    float d = Vector3.Distance(eye, r.bounds.center);
                    found.Add(new KeyValuePair<float, string>(d, string.Format(
                        "  {0,6:0.0} m  q={1,4}  {2}   shader='{3}'",
                        d, m.renderQueue, Hierarchy.Path(r.transform), m.shader.name)));
                    break;
                }
            }

            found.Sort((a, b) => a.Key.CompareTo(b.Key));
            int n = 0;
            foreach (var f in found)
            {
                sb.AppendLine(f.Value);
                if (++n >= 30) { sb.AppendLine("  ... (" + found.Count + " in total)"); break; }
            }
            if (found.Count == 0) sb.AppendLine("  (none)");
        }
    }
}
