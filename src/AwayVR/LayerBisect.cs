using System.Collections.Generic;
using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Hides one render layer at a time, to name geometry that no post-process affects.
    /// Only layers carrying visible renderers are offered, so the walk is a few steps.
    /// </summary>
    internal static class LayerBisect
    {
        private static readonly List<int> Candidates = new List<int>();
        private static int _index = -1;

        /// <summary>Layer currently hidden, or -1 when nothing is.</summary>
        public static int Current
        {
            get
            {
                if (_index < 0 || _index >= Candidates.Count) return -1;
                return Candidates[_index];
            }
        }

        public static string Label
        {
            get
            {
                int layer = Current;
                if (layer < 0) return "<color=#5d6b80>all visible</color>";
                return layer + " " + LayerTools.LayerName(layer)
                       + "   <color=#5d6b80>(" + (_index + 1) + "/" + Candidates.Count + ")</color>";
            }
        }

        public static void Reset()
        {
            _index = -1;
            Candidates.Clear();
        }

        private static void Rebuild()
        {
            Candidates.Clear();
            var counts = LayerTools.RendererCountsByLayer();
            var cam = VrManager.MainCamera;
            for (int i = 0; i < 32; i++)
            {
                if (counts[i] == 0) continue;
                // Only what the camera actually draws: hiding a layer it never renders
                // proves nothing and wastes a step.
                if (cam != null && (cam.cullingMask & (1 << i)) == 0) continue;
                if (i == PanelOverlay.Layer) continue;
                Candidates.Add(i);
            }
        }

        /// <summary>Moves one step through the list. -1 goes back, +1 goes forward.</summary>
        public static void Step(int direction)
        {
            if (Candidates.Count == 0 || _index < 0) Rebuild();
            if (Candidates.Count == 0) return;

            _index += direction >= 0 ? 1 : -1;

            // Past either end, everything comes back: the walk is a loop through "all
            // visible" so you can always get out of it without leaving the menu.
            if (_index >= Candidates.Count || _index < -1) _index = -1;
            if (_index == -1) Candidates.Clear();

            if (Current >= 0)
                Plugin.Log.LogInfo("Layer hidden: " + Current + " "
                                   + LayerTools.LayerName(Current));
        }
    }
}
