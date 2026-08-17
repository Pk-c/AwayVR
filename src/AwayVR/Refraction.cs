using System;
using System.Collections.Generic;
using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Hides geometry drawn with a screen-space or grab-pass shader. Both reconstruct the
    /// frame from a single camera's matrices, so with two eyes the result lands beside the
    /// geometry it belongs to. The desert's cloud shadows are the case that matters.
    /// </summary>
    internal static class Refraction
    {
        private static readonly Dictionary<Renderer, bool> Silenced =
            new Dictionary<Renderer, bool>();

        /// <summary>
        /// Matched on shader NAME: Unity offers no way to ask whether a shader works in
        /// screen space or grabs the frame. Frozen rather than configurable - the answer does
        /// not vary. Every renderer hidden is logged with its path and shader.
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
                // every access, and this loop runs over every renderer in the scene - fifteen
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
