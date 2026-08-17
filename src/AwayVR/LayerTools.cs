using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Layer inventory, for the diagnostic dump. Which layers actually carry renderers, and
    /// which of those the camera is drawing - the two questions that tell a hidden object
    /// apart from an object that is drawn but invisible.
    /// </summary>
    internal static class LayerTools
    {
        public static string LayerName(int layer)
        {
            var n = LayerMask.LayerToName(layer);
            return string.IsNullOrEmpty(n) ? "<unnamed>" : n;
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
