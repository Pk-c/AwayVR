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

        private static readonly Dictionary<string, FieldInfo> FxProFields =
            new Dictionary<string, FieldInfo>();

        /// <summary>
        /// Takes every render-texture camera out of stereo.
        ///
        /// A camera that draws into a RenderTexture has nothing to do with the headset's
        /// eyes, yet the game leaves several of them at stereoTargetEye = Both. The minimap
        /// is the one that bites: it renders the world from above into its own texture, with
        /// clearFlags = Nothing, and being a stereo camera it is handed the EYE texture as
        /// well. Nothing clears it, so its top-down view of the scenery is left lying in the
        /// eye buffer — a semi-transparent slab of geometry, offset from what you are looking
        /// at, exactly as reported.
        ///
        /// The correlation is what proves it: UI_hide_map shows the minimap outdoors and
        /// hides it in caves. Desert glitches, dungeon does not, and no rendering setting
        /// changes either — because none of them touch a camera that is not part of the
        /// scene's effect chain at all. This is the same defect the first world had, in a new
        /// guise: back then the minimap's RenderTexture reached the eyes through a
        /// screen-space canvas, and that route is now handled; this is the camera itself.
        ///
        /// Applied to every render-texture camera rather than to the minimap by name, because
        /// the rule is general: off-screen rendering is monoscopic by definition.
        /// </summary>
        public static void KeepRenderTextureCamerasMono()
        {
            foreach (var cam in UnityEngine.Object.FindObjectsOfType<Camera>())
            {
                if (cam == null || cam.targetTexture == null) continue;
                if (cam.stereoTargetEye == StereoTargetEyeMask.None) continue;

                cam.stereoTargetEye = StereoTargetEyeMask.None;
                Plugin.Log.LogInfo("Render-texture camera taken out of stereo: "
                                   + Hierarchy.Path(cam.transform)
                                   + " (" + cam.targetTexture.name + ")");
            }
        }

        /// <summary>
        /// The two full-screen passes that reconstruct the scene from the camera's MONO
        /// matrices, and therefore cannot line up in stereo.
        ///
        /// Both take the finished depth buffer and rebuild world positions from it, using
        /// frustum corners or a view matrix belonging to "the camera" — of which there is
        /// only one, while there are two eyes. What they compute lands beside the geometry
        /// it belongs to, offset by roughly the interpupillary distance, and the eye reads a
        /// dark copy of the world sitting next to the world. That is the ghosting, and it is
        /// on the GEOMETRY, which is what rules out the panels and FxPro.
        ///
        /// It has been there from the start. What changed is that it became visible
        /// everywhere: at a supersampled eye texture the offset edges are sharp instead of
        /// mushy, so a defect that read as softness now reads as a double image.
        ///
        /// Two separate switches on purpose. Occlusion is the likelier culprit — it draws
        /// dark contours around every object, which is exactly what a ghost of the geometry
        /// looks like — while the fog is part of the art direction and worth keeping if it
        /// is innocent. Turning both off at once would tell us nothing.
        /// </summary>
        public static void ApplyStereoBroken(bool disableOcclusion, bool disableFog)
        {
            foreach (var cam in UnityEngine.Object.FindObjectsOfType<Camera>())
            {
                foreach (var c in cam.GetComponents<MonoBehaviour>())
                {
                    if (c == null) continue;
                    var n = c.GetType().Name;

                    bool occlusion = n.IndexOf("Occlusion", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool fog = n.IndexOf("GlobalFog", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!occlusion && !fog) continue;

                    // SYMMETRIC: the switch has to work both ways or it cannot be used to
                    // bisect anything. A one-way switch means every test needs a restart,
                    // and you can never tell a fix from a coincidence.
                    bool want = !(occlusion ? disableOcclusion : disableFog);
                    if (c.enabled == want) continue;
                    c.enabled = want;
                    if (Plugin.CfgVerbose.Value)
                        Plugin.Log.LogInfo((want ? "Re-enabled: " : "Disabled: ") + n + " on "
                                           + Hierarchy.Path(c.transform));
                }
            }
        }

        /// <summary>
        /// FxPro's lens simulations, and the eyelid blink. Both are separate from the
        /// occlusion, and the DEPTH OF FIELD is the one that matters.
        ///
        /// It blurs everything away from its focus distance, working from the depth buffer
        /// and a single camera's matrices — so in stereo the blur lands offset from the
        /// geometry it belongs to, which is the ghosting that survived switching the
        /// occlusion off. Its switch is set per scene, which is why one world ghosts and
        /// another does not: the desert has it on, the first world does not.
        ///
        /// It also blurs the viewmodel, since the weapon sits far from the focus plane. That
        /// is the sharpening noticed when this was first switched off — an independent
        /// confirmation that the depth of field is what is running, and the reason to be
        /// confident this time.
        ///
        /// Chromatic aberration and lens curvature go with it: both simulate a lens you are
        /// already looking through, around a centre that is right for neither eye.
        ///
        /// Bloom, colour tinting and film grain are NOT touched. They are what gives each
        /// world its look, and none of them reconstructs anything from depth.
        /// </summary>
        private static readonly string[] LensFields =
        {
            "DOFEnabled", "ChromaticAberration", "ChromaticAberrationPrecise",
            "LensCurvatureEnabled", "LensCurvaturePrecise"
        };

        /// <summary>
        /// The game's own values, remembered the first time we see a component, so the
        /// switch can put them back. Without this, turning the setting off would leave the
        /// effect off until the scene reloaded — and a test you cannot undo is not a test.
        /// </summary>
        private static readonly Dictionary<MonoBehaviour, bool[]> LensOriginals =
            new Dictionary<MonoBehaviour, bool[]>();

        public static void ApplyLensEffects(bool disableDepthOfField, bool disableBlink)
        {
            foreach (var cam in UnityEngine.Object.FindObjectsOfType<Camera>())
            {
                foreach (var c in cam.GetComponents<MonoBehaviour>())
                {
                    if (c == null) continue;
                    var t = c.GetType();

                    if (t.Name == "BlinkEffect")
                    {
                        if (c.enabled == !disableBlink) continue;
                        c.enabled = !disableBlink;
                        continue;
                    }

                    if (t.Name != "FxPro") continue;

                    bool[] original;
                    if (!LensOriginals.TryGetValue(c, out original))
                    {
                        original = new bool[LensFields.Length];
                        for (int i = 0; i < LensFields.Length; i++)
                        {
                            var fi = FxProField(t, LensFields[i]);
                            original[i] = fi != null && fi.FieldType == typeof(bool)
                                          && (bool)fi.GetValue(c);
                        }
                        LensOriginals[c] = original;
                    }

                    for (int i = 0; i < LensFields.Length; i++)
                    {
                        var f = FxProField(t, LensFields[i]);
                        if (f == null || f.FieldType != typeof(bool)) continue;
                        bool want = disableDepthOfField ? false : original[i];
                        if ((bool)f.GetValue(c) == want) continue;
                        f.SetValue(c, want);
                        if (Plugin.CfgVerbose.Value)
                            Plugin.Log.LogInfo("FxPro." + LensFields[i] + " = " + want
                                               + " on " + Hierarchy.Path(c.transform));
                    }
                }
            }
        }

        /// <summary>Dropped on a scene load: the components are gone with it.</summary>
        public static void ForgetOriginals() { LensOriginals.Clear(); }

        private static FieldInfo FxProField(Type t, string name)
        {
            var key = t.FullName + "." + name;
            FieldInfo f;
            if (FxProFields.TryGetValue(key, out f)) return f;
            f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public
                                 | BindingFlags.NonPublic);
            FxProFields[key] = f;
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
                var f = FxProField(t, name);
                if (f == null) continue;
                object v;
                try { v = f.GetValue(c); } catch { continue; }
                parts.Add(name + "=" + v);
            }
            return string.Join(" ", parts.ToArray());
        }

        /// <summary>
        /// Switches off the game's temporal anti-aliasing, and it has to go in VR.
        ///
        /// This is the ghosting. TemporalReprojection is Playdead's TAA — FrustumJitter
        /// offsets the projection every frame, VelocityBuffer records the motion, and the
        /// result is blended with the PREVIOUS frame kept in a history buffer.
        ///
        /// There is exactly one history buffer, held per camera. In multi-pass stereo the
        /// same camera renders twice per frame with two different view matrices, so the left
        /// eye's image becomes the history the right eye blends against, and the other way
        /// round on the next pass. Every eye is permanently mixed with the other one, offset
        /// by the interpupillary distance. That is the double image, and it follows the head
        /// because the offset is the head's own stereo separation.
        ///
        /// No setting fixes this: it would take one history buffer per eye, which means
        /// rewriting the effect. So the effect goes. All three components go together — the
        /// jitter left running without the reprojection to resolve it turns clean edges into
        /// a permanent shimmer, which is worse than what we set out to cure.
        ///
        /// The anti-aliasing it provided is not really lost either: the eye texture is
        /// supersampled well above 1, which is what actually smooths edges here.
        /// </summary>
        public static void ApplyTemporalAA(bool disabled)
        {
            foreach (var cam in UnityEngine.Object.FindObjectsOfType<Camera>())
            {
                foreach (var c in cam.GetComponents<MonoBehaviour>())
                {
                    if (c == null) continue;
                    var n = c.GetType().Name;
                    if (n != "TemporalReprojection" && n != "FrustumJitter"
                        && n != "VelocityBuffer") continue;
                    if (c.enabled == !disabled) continue;
                    c.enabled = !disabled;
                    if (disabled && Plugin.CfgVerbose.Value)
                        Plugin.Log.LogInfo("Temporal AA disabled: " + n + " on "
                                           + Hierarchy.Path(c.transform));
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
