using System.Collections.Generic;
using PigeonCoopToolkit.Effects.Trails;
using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Drives the weapon's trail from the real swing.
    ///
    /// The game uses PigeonCoopToolkit trails, whose Emit flag was raised by the attack
    /// ANIMATION. In VR the weapon follows the hand instead, so that animation no longer
    /// carries the blade and the trail never fires. We raise Emit from the measured hand
    /// speed, which is what the gesture actually is.
    ///
    /// It fires on the SWING ITSELF, the same event that triggers the attack, rather than on
    /// a threshold of its own - two independent numbers drift apart, and a lower one drew a
    /// trail on every small movement. It then sustains while the hand keeps moving, so a long
    /// swing keeps its trail to the end.
    /// </summary>
    internal static class Trails
    {
        private static readonly List<TrailRenderer_Base> Found = new List<TrailRenderer_Base>();
        private static Transform _scanned;
        private static float _emitUntil;

        public static int Count { get { return Found.Count; } }
        public static bool Emitting { get { return Time.unscaledTime < _emitUntil; } }

        public static void Forget()
        {
            Found.Clear();
            _scanned = null;
            _emitUntil = 0f;
        }

        private static void Rescan(Transform root)
        {
            Found.Clear();
            _scanned = root;
            if (root == null) return;
            foreach (var t in root.GetComponentsInChildren<TrailRenderer_Base>(true))
                if (t != null) Found.Add(t);

            if (Found.Count > 0)
                Plugin.Log.LogInfo("Weapon trails found: " + Found.Count);
        }

        public static void Tick()
        {
            if (!VrManager.VrActive || !Plugin.CfgWeaponTrail.Value)
            {
                Silence();
                return;
            }

            var root = Weapons.Root;
            if (root == null) { Silence(); return; }
            if (root != _scanned) Rescan(root);
            if (Found.Count == 0) return;

            float now = Time.unscaledTime;

            // Started only by a real swing.
            if (Swing.IsSwinging) _emitUntil = now + Plugin.CfgTrailHold.Value;

            // Sustained while the hand is still moving with intent, so the trail follows the
            // whole gesture instead of stopping a fraction of a second in.
            else if (now < _emitUntil
                     && Swing.Speed >= Plugin.CfgSwingThreshold.Value * 0.5f)
                _emitUntil = now + Plugin.CfgTrailHold.Value;

            bool emit = Emitting;
            for (int i = Found.Count - 1; i >= 0; i--)
            {
                var t = Found[i];
                if (t == null) { Found.RemoveAt(i); continue; }

                // The trail object is switched off between the game's own attacks; it has to
                // be active for the component to build anything.
                if (emit && !t.gameObject.activeSelf) t.gameObject.SetActive(true);
                if (t.Emit != emit) t.Emit = emit;
            }
        }

        private static void Silence()
        {
            for (int i = Found.Count - 1; i >= 0; i--)
            {
                var t = Found[i];
                if (t == null) { Found.RemoveAt(i); continue; }
                if (t.Emit) t.Emit = false;
            }
            _emitUntil = 0f;
        }

        public static void Dump(System.Text.StringBuilder sb)
        {
            sb.AppendLine("-- Weapon trails --");
            sb.AppendLine("  enabled=" + Plugin.CfgWeaponTrail.Value
                          + "  found=" + Found.Count
                          + "  emitting=" + Emitting
                          + "  speed=" + Swing.Speed.ToString("0.00")
                          + "  swingThreshold=" + Plugin.CfgSwingThreshold.Value.ToString("0.00"));

            foreach (var t in Found)
            {
                if (t == null) continue;
                sb.AppendLine("  " + t.GetType().Name + "  " + Hierarchy.Path(t.transform)
                              + "  active=" + t.gameObject.activeInHierarchy
                              + "  Emit=" + t.Emit);
            }

            // Everything in the scene, so a trail sitting outside the viewmodel still shows.
            int others = 0;
            foreach (var t in Object.FindObjectsOfType<TrailRenderer_Base>())
            {
                if (t == null || Found.Contains(t)) continue;
                if (others == 0) sb.AppendLine("  -- elsewhere in the scene --");
                sb.AppendLine("  " + t.GetType().Name + "  " + Hierarchy.Path(t.transform)
                              + "  Emit=" + t.Emit);
                if (++others >= 12) { sb.AppendLine("  ..."); break; }
            }
        }
    }
}
