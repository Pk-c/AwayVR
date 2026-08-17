using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AwayVR
{
    /// <summary>
    /// Hides the scene one root object at a time.
    ///
    /// The layer walk proved the ghost is ordinary geometry but could not go further: the
    /// desert keeps everything on Default, so hiding that layer hides the scene. This is the
    /// same method one level finer — the scene's roots are `_CliffAreas`, `_Particles`,
    /// `_OpenAreaEntrance`, `_PNJs` and a dozen more, and the ghost belongs to exactly one of
    /// them.
    ///
    /// RENDERERS are switched off, never the GameObject. Deactivating a root would stop its
    /// scripts, its colliders and its triggers, and a scene that has stopped working tells
    /// you nothing about what it was drawing. Only the drawing is removed.
    ///
    /// Deliberately no filtering by name or by guess. Every previous attempt at this bug
    /// started from a theory about which component was at fault and each one was wrong; this
    /// walks the whole scene and lets the answer fall out.
    /// </summary>
    internal static class RootBisect
    {
        private static readonly List<GameObject> Roots = new List<GameObject>();
        private static readonly List<Renderer> Hidden = new List<Renderer>();
        private static int _index = -1;

        public static string Label
        {
            get
            {
                if (_index < 0 || _index >= Roots.Count) return "<color=#5d6b80>all visible</color>";
                var go = Roots[_index];
                return (go != null ? go.name : "?")
                       + "   <color=#5d6b80>(" + (_index + 1) + "/" + Roots.Count + ")</color>";
            }
        }

        /// <summary>The root currently hidden, or null.</summary>
        public static GameObject Selected
        {
            get { return _index >= 0 && _index < Roots.Count ? Roots[_index] : null; }
        }

        public static void Reset()
        {
            Restore();
            Roots.Clear();
            _index = -1;
        }

        private static void Restore()
        {
            foreach (var r in Hidden)
                if (r != null) r.enabled = true;
            Hidden.Clear();
        }

        private static void Rebuild()
        {
            Roots.Clear();
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid()) return;

            foreach (var go in scene.GetRootGameObjects())
            {
                if (go == null || !go.activeInHierarchy) continue;
                if (go.name.StartsWith("AwayVR_")) continue;

                // Only roots that actually draw something: the rest would be empty steps.
                bool draws = false;
                foreach (var r in go.GetComponentsInChildren<Renderer>(false))
                {
                    if (r == null || !r.enabled) continue;
                    draws = true;
                    break;
                }
                if (draws) Roots.Add(go);
            }
            Roots.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        public static void Step(int direction)
        {
            if (Roots.Count == 0) Rebuild();
            if (Roots.Count == 0) return;

            Restore();

            _index += direction >= 0 ? 1 : -1;
            if (_index >= Roots.Count || _index < -1) _index = -1;
            if (_index < 0) return;

            var target = Roots[_index];
            if (target == null) return;

            foreach (var r in target.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled) continue;
                r.enabled = false;
                Hidden.Add(r);
            }

            Plugin.Log.LogInfo("Root hidden: " + target.name + "  (" + Hidden.Count
                               + " renderers)");
        }

        /// <summary>
        /// Full inventory of the selected root: every renderer with its shader, plus the
        /// component types that draw something without being a renderer at all — projectors,
        /// reflection probes, cameras, line and trail renderers. Once the walk has named the
        /// root, this is what names the object inside it.
        /// </summary>
        public static void Dump(System.Text.StringBuilder sb)
        {
            var root = Selected;
            if (root == null)
            {
                sb.AppendLine("-- Root bisect: nothing selected --");
                return;
            }

            sb.AppendLine("-- Root '" + root.name + "' contents --");

            int n = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var mat = r.sharedMaterial;
                sb.AppendLine(string.Format(
                    "  [{0}] {1}  type={2}  layer={3} '{4}'  size={5}  shader='{6}'  queue={7}",
                    r.enabled ? "on " : "off",
                    Hierarchy.Path(r.transform),
                    r.GetType().Name,
                    r.gameObject.layer, LayerTools.LayerName(r.gameObject.layer),
                    r.bounds.size.ToString("0.0"),
                    mat != null && mat.shader != null ? mat.shader.name : "<none>",
                    mat != null ? mat.renderQueue : -1));
                if (++n >= 40) { sb.AppendLine("  ... (truncated)"); break; }
            }
            if (n == 0) sb.AppendLine("  (no renderers)");

            sb.AppendLine("  -- other drawing components --");
            int m = 0;
            foreach (var c in root.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                var t = c.GetType().Name;
                if (t != "Projector" && t != "ReflectionProbe" && t != "Camera"
                    && t != "Light" && t != "LensFlare" && t != "Halo") continue;
                sb.AppendLine("  " + t + "  " + Hierarchy.Path(c.transform));
                if (++m >= 20) { sb.AppendLine("  ... (truncated)"); break; }
            }
            if (m == 0) sb.AppendLine("  (none)");
        }
    }
}
