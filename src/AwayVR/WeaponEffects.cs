using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Moves the per-character full-screen effects off Weapons_Camera and onto the main one,
    /// so that camera can finally be switched off.
    ///
    /// The whole difficulty of this mod's camera handling comes from one conflict. The
    /// character effects — the mechanic's red wash, the magician's cracked glasses — are
    /// components on Weapons_Camera and on nothing else, so the camera has to keep running
    /// for them to process the frame. But it renders NOTHING (its layers were merged into
    /// the main camera to cure the doubled arm) and it clears depth only, so its colour
    /// buffer holds whatever happened to be in that render target before. Its image-effect
    /// chain then takes that content and composites it onto the screen.
    ///
    /// That is the stale half-transparent frame. And it explains why removing effects made
    /// things WORSE rather than better: each one that was removed was a pass that rewrote the
    /// whole target and so hid the garbage underneath. Take away the last one and there is
    /// nothing left writing the image at all — a black screen.
    ///
    /// So we copy the effects onto the main camera, where the buffer holds the actual scene,
    /// and let the empty camera be disabled.
    ///
    /// The copies are SLAVED to the originals. The game switches its character effects on and
    /// off by toggling those components, and it goes on doing so on the disabled camera; if
    /// the copies did not follow, the effects would never appear.
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
        /// Creates the copies. Only inspector data is carried over — the public fields, which
        /// is where the shaders, materials and LUTs live. Private fields are deliberately NOT
        /// copied: they hold cached references, above all to the camera the effect was
        /// originally attached to, and copying those would point the new component straight
        /// back at the camera we are trying to retire.
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
