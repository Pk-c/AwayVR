using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Copies the per-character full-screen effects from Weapons_Camera onto the main camera
    /// so that camera can be disabled. Left running it renders nothing and clears depth only,
    /// so its colour buffer holds a stale frame that its effect chain composites onto screen.
    ///
    /// Copies are slaved to the originals: the game keeps toggling those components.
    /// </summary>
    internal static class WeaponEffects
    {
        private class Pair
        {
            public MonoBehaviour Original;
            public MonoBehaviour Copy;
        }

        private static readonly List<Pair> Pairs = new List<Pair>();
        private static Camera _source;
        private static Camera _target;

        public static bool Installed { get { return Pairs.Count > 0; } }

        public static void Forget()
        {
            foreach (var p in Pairs)
                if (p.Copy != null) UnityEngine.Object.Destroy(p.Copy);
            Pairs.Clear();
            _source = null;
            _target = null;
        }

        /// <summary>
        /// Only PUBLIC fields are copied - shaders, materials, LUTs. Private ones hold cached
        /// references, above all to the camera we are retiring.
        /// </summary>
        public static void Install(Camera weapons, Camera main)
        {
            if (weapons == null || main == null) return;
            if (_source == weapons && _target == main && Pairs.Count > 0) return;

            Forget();
            _source = weapons;
            _target = main;

            foreach (var c in weapons.GetComponents<MonoBehaviour>())
            {
                if (c == null) continue;
                var t = c.GetType();
                if (!CameraEffects.UsesRenderImage(t) && !CameraEffects.UsesCommandBuffers(t))
                    continue;

                MonoBehaviour copy;
                try { copy = main.gameObject.AddComponent(t) as MonoBehaviour; }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Could not move " + t.Name + ": " + e.Message);
                    continue;
                }
                if (copy == null) continue;

                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public))
                {
                    try { f.SetValue(copy, f.GetValue(c)); }
                    catch { }
                }

                copy.enabled = c.enabled;
                Pairs.Add(new Pair { Original = c, Copy = copy });
            }

            Plugin.Log.LogInfo("Weapons_Camera effects moved to the main camera ("
                               + Pairs.Count + ").");
        }

        /// <summary>Mirrors the game's own on/off decisions onto the copies.</summary>
        public static void Sync()
        {
            for (int i = Pairs.Count - 1; i >= 0; i--)
            {
                var p = Pairs[i];
                if (p.Original == null || p.Copy == null) { Pairs.RemoveAt(i); continue; }
                if (p.Copy.enabled != p.Original.enabled) p.Copy.enabled = p.Original.enabled;
            }
        }
    }
}
