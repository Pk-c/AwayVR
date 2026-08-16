using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace AwayVR
{
    /// <summary>
    /// Effects attached to cameras. Two families, and both have to be covered:
    ///
    ///  - classic full-screen effects, which implement OnRenderImage;
    ///  - those injecting themselves through CommandBuffers (AmplifyOcclusion, the
    ///    PostProcessing stack). Those never go through OnRenderImage, so switching them off
    ///    "like an image effect" does not touch them at all.
    ///
    /// Both kinds often rebuild the scene from the camera's monoscopic parameters
    /// (fieldOfView, worldToCameraMatrix) where stereo would require the per-eye matrices —
    /// hence offset dark silhouettes that follow the head around.
    /// </summary>
    internal static class CameraEffects
    {
        private static readonly Dictionary<Type, bool> RenderImageCache = new Dictionary<Type, bool>();
        private static readonly Dictionary<Type, bool> CommandBufferCache = new Dictionary<Type, bool>();

        private const BindingFlags Decl =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>Does the type declare OnRenderImage anywhere in its hierarchy?</summary>
        public static bool UsesRenderImage(Type t)
        {
            bool cached;
            if (RenderImageCache.TryGetValue(t, out cached)) return cached;

            bool found = false;
            for (var cur = t; cur != null && cur != typeof(MonoBehaviour); cur = cur.BaseType)
            {
                if (cur.GetMethod("OnRenderImage", Decl) != null) { found = true; break; }
            }
            RenderImageCache[t] = found;
            return found;
        }

        /// <summary>
        /// Does the type handle CommandBuffers? We detect it from its fields: such effects
        /// always keep a reference around so they can unregister them later.
        /// </summary>
        public static bool UsesCommandBuffers(Type t)
        {
            bool cached;
            if (CommandBufferCache.TryGetValue(t, out cached)) return cached;

            bool found = false;
            for (var cur = t; cur != null && cur != typeof(MonoBehaviour) && !found; cur = cur.BaseType)
            {
                foreach (var f in cur.GetFields(Decl))
                {
                    if (Involves(f.FieldType, typeof(CommandBuffer))) { found = true; break; }
                }
            }
            CommandBufferCache[t] = found;
            return found;
        }

        private static bool Involves(Type t, Type target)
        {
            if (t == null) return false;
            if (t == target) return true;
            if (t.IsArray) return Involves(t.GetElementType(), target);
            if (t.IsGenericType)
            {
                foreach (var arg in t.GetGenericArguments())
                    if (Involves(arg, target)) return true;
            }
            return false;
        }

        /// <summary>
        /// Switches the game's bloom off, wherever it lives.
        ///
        /// AmplifyBloomEffect sits on Weapons_Camera, alongside the colour grading — and
        /// nowhere else. While that camera was disabled to cure the double arm, the game had
        /// no bloom and no grading at all, which is the flatter, brighter image the mod has
        /// shown until now. Letting the camera render again restores the intended look, but
        /// bloom is markedly harsher in a headset than on a monitor, so it gets its own
        /// switch rather than an all-or-nothing choice between correct colours and no glare.
        /// </summary>
        public static void ApplyBloom(bool disabled)
        {
            foreach (var cam in UnityEngine.Object.FindObjectsOfType<Camera>())
            {
                foreach (var c in cam.GetComponents<MonoBehaviour>())
                {
                    if (c == null) continue;
                    if (c.GetType().Name.IndexOf("Bloom", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    if (c.enabled == !disabled) continue;
                    c.enabled = !disabled;
                }
            }
        }

        /// <summary>
        /// Switches the game's colour grading off.
        ///
        /// WeaponCameraColorFilters carries some thirty LUTs and rides on the weapons camera
        /// next to the bloom. Restoring that camera brought the grading back with it — and
        /// what reads as pleasing contrast on a monitor reads as crushed and harsh in a
        /// headset, where the image fills your whole field of view. Off by default for that
        /// reason, and a setting rather than a decision because it is a matter of taste.
        /// </summary>
        public static void ApplyColorGrading(bool disabled)
        {
            foreach (var cam in UnityEngine.Object.FindObjectsOfType<Camera>())
            {
                foreach (var c in cam.GetComponents<MonoBehaviour>())
                {
                    if (c == null) continue;
                    var n = c.GetType().Name;
                    if (n.IndexOf("ColorFilters", StringComparison.OrdinalIgnoreCase) < 0
                        && n.IndexOf("Lut", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (c.enabled == !disabled) continue;
                    c.enabled = !disabled;
                }
            }
        }


    }
}
