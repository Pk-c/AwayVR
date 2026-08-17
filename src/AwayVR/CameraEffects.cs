using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace AwayVR
{
    /// <summary>
    /// Everything the mod does to the game's cameras, in one pass over the scene - it was six
    /// separate scans, twice a second.
    ///
    /// Two families have to be covered: effects implementing OnRenderImage, and those
    /// injecting themselves through CommandBuffers, which never go through it at all.
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
        /// Does the type handle CommandBuffers? Detected from its fields: such effects always
        /// keep a reference around so they can unregister them later.
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

        // ------------------------------------------------------------------
        // The single sweep
        // ------------------------------------------------------------------

        /// <summary>
        /// Amounts, not switches. Zeroing them removes the effect while its full-screen pass
        /// keeps running - and that pass is what rewrites the whole target. Switching the
        /// effect off instead left the buffer unwritten, which is the stale frame that used
        /// to bleed through.
        /// </summary>
        private static readonly string[] LensAmounts =
        {
            "ChromaticAberrationOffset", "LensCurvaturePower"
        };

        /// <summary>
        /// The game's own lens values, remembered the first time each component is seen, so
        /// the switch can put them back. Without it, turning the setting off would leave the
        /// effect off until the scene reloaded - and a test you cannot undo is not a test.
        /// </summary>
        private static readonly Dictionary<MonoBehaviour, float[]> LensOriginals =
            new Dictionary<MonoBehaviour, float[]>();

        private static readonly Dictionary<string, FieldInfo> FieldCache =
            new Dictionary<string, FieldInfo>();

        public static void ForgetOriginals() { LensOriginals.Clear(); }

        public static void Sweep(bool noBloom, bool noGrading, bool noTemporalAA,
                                 bool noOcclusion, bool noFog, bool noDepthOfField,
                                 bool noBlink, bool noCharacterEffects)
        {
            foreach (var cam in UnityEngine.Object.FindObjectsOfType<Camera>())
            {
                if (cam == null) continue;

                // Off-screen rendering is monoscopic by definition, yet the game leaves some
                // of these cameras set to draw to both eyes - the minimap among them, which
                // then leaves its top-down view of the scenery lying in the eye buffer.
                if (cam.targetTexture != null
                    && cam.stereoTargetEye != StereoTargetEyeMask.None)
                {
                    cam.stereoTargetEye = StereoTargetEyeMask.None;
                    Plugin.Log.LogInfo("Render-texture camera taken out of stereo: "
                                       + Hierarchy.Path(cam.transform)
                                       + " (" + cam.targetTexture.name + ")");
                }

                foreach (var c in cam.GetComponents<MonoBehaviour>())
                {
                    if (c == null) continue;
                    var t = c.GetType();
                    var n = t.Name;

                    if (n == "FxPro") { ApplyLens(c, t, noDepthOfField); continue; }

                    bool want;
                    // The per-character full-screen washes - the mechanic's red, the
                    // magician's cracked glasses. Tested first so it wins over the colour
                    // grading rule, which would otherwise claim WeaponCameraColorFilters.
                    if (noCharacterEffects && n.StartsWith("WeaponCamera", StringComparison.Ordinal))
                        want = false;
                    else if (n.IndexOf("Bloom", StringComparison.OrdinalIgnoreCase) >= 0)
                        want = !noBloom;
                    else if (n.IndexOf("ColorFilters", StringComparison.OrdinalIgnoreCase) >= 0
                             || n.IndexOf("Lut", StringComparison.OrdinalIgnoreCase) >= 0)
                        want = !noGrading;
                    else if (n == "TemporalReprojection" || n == "FrustumJitter"
                             || n == "VelocityBuffer")
                        want = !noTemporalAA;
                    else if (n.IndexOf("Occlusion", StringComparison.OrdinalIgnoreCase) >= 0)
                        want = !noOcclusion;
                    else if (n.IndexOf("GlobalFog", StringComparison.OrdinalIgnoreCase) >= 0)
                        want = !noFog;
                    else if (n == "BlinkEffect")
                        want = !noBlink;
                    else continue;

                    if (c.enabled == want) continue;
                    c.enabled = want;
                    if (Plugin.CfgVerbose.Value)
                        Plugin.Log.LogInfo((want ? "Re-enabled: " : "Disabled: ") + n
                                           + " on " + Hierarchy.Path(c.transform));
                }
            }
        }

        /// <summary>
        /// FxPro's depth of field, chromatic aberration and lens curvature. All three warp or
        /// blur the finished image around a single centre computed for ONE camera, so with
        /// two eyes the result lands beside the geometry it belongs to. Bloom, colour tinting
        /// and film grain are deliberately left alone: they give each world its look and none
        /// of them reconstructs anything from depth.
        /// </summary>
        private static void ApplyLens(MonoBehaviour c, Type t, bool disabled)
        {
            var blur = BlurSizeField(c, t);

            float[] original;
            if (!LensOriginals.TryGetValue(c, out original))
            {
                original = new float[LensAmounts.Length + 1];
                for (int i = 0; i < LensAmounts.Length; i++)
                {
                    var fi = Field(t, LensAmounts[i]);
                    original[i] = fi != null && fi.FieldType == typeof(float)
                                  ? (float)fi.GetValue(c) : 0f;
                }
                original[LensAmounts.Length] = blur.Key != null
                    ? (float)blur.Key.GetValue(blur.Value) : 0f;
                LensOriginals[c] = original;
            }

            for (int i = 0; i < LensAmounts.Length; i++)
            {
                var f = Field(t, LensAmounts[i]);
                if (f == null || f.FieldType != typeof(float)) continue;
                float want = disabled ? 0f : original[i];
                if (Mathf.Approximately((float)f.GetValue(c), want)) continue;
                f.SetValue(c, want);
            }

            if (blur.Key != null)
            {
                float want = disabled ? 0f : original[LensAmounts.Length];
                if (!Mathf.Approximately((float)blur.Key.GetValue(blur.Value), want))
                    blur.Key.SetValue(blur.Value, want);
            }
        }

        /// <summary>DOFParams.DOFBlurSize, with the params object that owns it.</summary>
        private static KeyValuePair<FieldInfo, object> BlurSizeField(MonoBehaviour c, Type t)
        {
            var pf = Field(t, "DOFParams");
            if (pf == null) return new KeyValuePair<FieldInfo, object>(null, null);

            object dof;
            try { dof = pf.GetValue(c); }
            catch { return new KeyValuePair<FieldInfo, object>(null, null); }
            if (dof == null) return new KeyValuePair<FieldInfo, object>(null, null);

            var bf = Field(dof.GetType(), "DOFBlurSize");
            if (bf == null || bf.FieldType != typeof(float))
                return new KeyValuePair<FieldInfo, object>(null, null);
            return new KeyValuePair<FieldInfo, object>(bf, dof);
        }

        private static FieldInfo Field(Type t, string name)
        {
            var key = t.FullName + "." + name;
            FieldInfo f;
            if (FieldCache.TryGetValue(key, out f)) return f;
            f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public
                                 | BindingFlags.NonPublic);
            FieldCache[key] = f;
            return f;
        }

        /// <summary>Reads FxPro's switches back, for the diagnostic dump.</summary>
        public static string DescribeFxPro(MonoBehaviour c)
        {
            var t = c.GetType();
            var parts = new List<string>();
            foreach (var name in new[] { "BloomEnabled", "DOFEnabled", "ChromaticAberration",
                                         "LensCurvatureEnabled", "HalfResolution",
                                         "FilmGrainIntensity", "VignettingIntensity",
                                         "ColorEffectsEnabled" })
            {
                var f = Field(t, name);
                if (f == null) continue;
                object v;
                try { v = f.GetValue(c); } catch { continue; }
                parts.Add(name + "=" + v);
            }
            return string.Join(" ", parts.ToArray());
        }
    }
}
