using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Layer bisection on the main camera. When an artefact is neither a camera effect nor a
    /// canvas, it is geometry: we hide the layers one at a time until it disappears, which
    /// names it instead of leaving us to guess.
    /// </summary>
    internal static class LayerTools
    {
        private static int _originalMask;
        private static bool _captured;
        private static int _step = -1;
        private static List<int> _candidates;

        /// <summary>True during a bisection: continuous reapplication must stand aside.</summary>
        public static bool BisectionActive { get { return _step >= 0; } }

        private static void Capture(Camera cam)
        {
            if (_captured) return;
            _originalMask = cam.cullingMask;
            _captured = true;
        }

        /// <summary>Layers present in the mask AND carrying at least one active renderer.</summary>
        private static List<int> Candidates(Camera cam)
        {
            var counts = RendererCountsByLayer();
            var list = new List<int>();
            for (int i = 0; i < 32; i++)
            {
                if ((_originalMask & (1 << i)) == 0) continue;
                if (counts[i] == 0) continue;
                list.Add(i);
            }
            return list;
        }

        public static int[] RendererCountsByLayer()
        {
            var counts = new int[32];
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || !r.enabled) continue;
                var l = r.gameObject.layer;
                if (l >= 0 && l < 32) counts[l]++;
            }
            return counts;
        }

        /// <summary>Hides the next layer, restoring the previous one.</summary>
        public static void Step(Camera cam)
        {
            if (cam == null) { Plugin.Log.LogWarning("No main camera."); return; }
            Capture(cam);

            if (_step < 0)
                _candidates = Candidates(cam);

            cam.cullingMask = _originalMask;
            _step++;

            if (_candidates == null || _step >= _candidates.Count)
            {
                _step = -1;
                _candidates = null;
                Plugin.Log.LogInfo("Bisection finished: every layer restored. If the artefact "
                                   + "never went away, it is not rendered by the main camera.");
                return;
            }

            int layer = _candidates[_step];
            cam.cullingMask = _originalMask & ~(1 << layer);
            Plugin.Log.LogInfo(string.Format("Layer hidden: {0} '{1}'   ({2}/{3})",
                layer, LayerName(layer), _step + 1, _candidates.Count));
        }

        /// <summary>Removes the layers named in the configuration from the culling mask.</summary>
        public static int ApplyHidden(Camera cam, string csv, bool log)
        {
            if (cam == null || string.IsNullOrEmpty(csv)) return 0;

            int mask = cam.cullingMask;
            int n = 0;
            foreach (var raw in csv.Split(','))
            {
                var token = raw.Trim();
                if (token.Length == 0) continue;

                int layer;
                if (!int.TryParse(token, out layer))
                    layer = LayerMask.NameToLayer(token);

                if (layer < 0 || layer > 31)
                {
                    Plugin.Log.LogWarning("Unknown layer in HiddenLayers: '" + token + "'");
                    continue;
                }
                if ((mask & (1 << layer)) == 0) continue;

                mask &= ~(1 << layer);
                n++;
                if (log)
                    Plugin.Log.LogInfo("  layer hidden: " + layer + " '" + LayerName(layer) + "'");
            }

            cam.cullingMask = mask;
            // Bisection must start from the effective state, hidden layers included.
            _originalMask = mask;
            _captured = true;
            return n;
        }

        public static void Reset(Camera cam)
        {
            if (cam == null || !_captured) return;
            cam.cullingMask = _originalMask;
            _step = -1;
            _candidates = null;
            Plugin.Log.LogInfo("Culling mask restored.");
        }

        public static string LayerName(int layer)
        {
            var n = LayerMask.LayerToName(layer);
            return string.IsNullOrEmpty(n) ? "<unnamed>" : n;
        }

        public static void Dump(StringBuilder sb, Camera cam)
        {
            var counts = RendererCountsByLayer();
            sb.AppendLine("-- Active renderers by layer --");
            for (int i = 0; i < 32; i++)
            {
                if (counts[i] == 0) continue;
                bool seen = cam != null && (cam.cullingMask & (1 << i)) != 0;
                sb.AppendLine(string.Format("  {0,2} {1,-18} {2,5} renderers   {3}",
                    i, LayerName(i), counts[i], seen ? "rendered by the camera" : "outside mask"));
            }

            if (cam == null) return;

            // Anything parented under the camera or the rig follows the head by construction.
            sb.AppendLine("-- Renderers under the rig / camera --");
            var root = VrManager.Rig != null ? VrManager.Rig : cam.transform;
            int found = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                sb.AppendLine(string.Format("  [{0}] {1}  layer={2} '{3}'  mat={4}",
                    r.enabled ? "on " : "off",
                    Hierarchy.Path(r.transform),
                    r.gameObject.layer, LayerName(r.gameObject.layer),
                    r.sharedMaterial != null ? r.sharedMaterial.name : "<null>"));
                found++;
                if (found > 40) { sb.AppendLine("  ... (truncated)"); break; }
            }
            if (found == 0) sb.AppendLine("  (none)");

            var projectors = Object.FindObjectsOfType<Projector>();
            if (projectors.Length > 0)
            {
                sb.AppendLine("-- Projectors (" + projectors.Length + ") --");
                foreach (var p in projectors)
                    sb.AppendLine(string.Format("  [{0}] {1}  mask=0x{2:X}  mat={3}",
                        p.enabled ? "on " : "off", Hierarchy.Path(p.transform),
                        p.ignoreLayers, p.material != null ? p.material.name : "<null>"));
            }
        }
    }
}
