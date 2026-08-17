using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace AwayVR
{
    /// <summary>Scene report written to the log, to spot whatever breaks in stereo.</summary>
    internal static class Diagnostics
    {
        public static void DumpScene()
        {
            var sb = new StringBuilder();
            sb.AppendLine("================ AwayVR: scene report ================");
            sb.AppendLine("XR active=" + VrManager.VrActive
                          + " device='" + XRSettings.loadedDeviceName + "'"
                          + " eye=" + XRSettings.eyeTextureWidth + "x" + XRSettings.eyeTextureHeight);

            DumpScale(sb);
            DumpFond(sb);
            DumpCameras(sb);
            DumpSettings(sb);
            RootBisect.Dump(sb);
            Refraction.Dump(sb);
            LayerTools.Dump(sb, VrManager.MainCamera);
            Weapons.Dump(sb);
            Trails.Dump(sb);
            Weapons.DumpArmesOrphelines(sb);
            DumpNodes(sb);
            DumpEtatJeu(sb);
            DumpCanvases(sb);

            sb.AppendLine("==========================================================");
            Plugin.Log.LogInfo(sb.ToString());
        }

        /// <summary>
        /// World scale is judged by the interpupillary distance IN WORLD METRES: that is
        /// what makes the scenery feel larger or smaller. If it does not move when you
        /// change the scale, the setting is doing nothing but shifting the eye.
        /// </summary>
        private static void DumpScale(StringBuilder sb)
        {
            var cam = VrManager.MainCamera;
            if (cam == null || !XRSettings.isDeviceActive) return;

            var l = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.GetColumn(3);
            var r = cam.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.GetColumn(3);
            float ipd = Vector3.Distance(l, r);

            string rig = VrManager.Rig != null ? VrManager.Rig.localScale.x.ToString("0.00") : "?";
            sb.AppendLine("-- Scale --");
            sb.AppendLine("  WorldScale=" + Plugin.CfgWorldScale.Value.ToString("0.00")
                          + "   rig scale=" + rig
                          + "   world IPD=" + ipd.ToString("0.0000") + " m");
        }

        /// <summary>
        /// What the image background is made of. Once no renderer is responsible, a flat
        /// field can only come from the skybox or the camera's clear colour.
        /// </summary>
        private static void DumpFond(StringBuilder sb)
        {
            var cam = VrManager.MainCamera;
            sb.AppendLine("-- Background --");
            sb.AppendLine("  scene skybox  : "
                          + (RenderSettings.skybox != null
                              ? RenderSettings.skybox.name + " (shader "
                                + (RenderSettings.skybox.shader != null
                                    ? RenderSettings.skybox.shader.name : "?") + ")"
                              : "<none>"));
            if (cam == null) return;
            sb.AppendLine("  camera clear  : " + cam.clearFlags
                          + "   colour=" + cam.backgroundColor);
            sb.AppendLine("  camera skybox : "
                          + (cam.GetComponent<Skybox>() != null ? "component present" : "none"));
        }

        private static void DumpCameras(StringBuilder sb)
        {
            var cams = Object.FindObjectsOfType<Camera>();
            sb.AppendLine("-- Cameras (" + cams.Length + ") --");
            foreach (var c in cams)
            {
                sb.AppendLine(string.Format(
                    "  [{0}] {1}\n      enabled={2} depth={3} clear={4} mask=0x{5:X} fov={6:0.#} near={7:0.###} far={8:0.#} stereo={9} target={10}",
                    c.enabled ? "on " : "off",
                    Hierarchy.Path(c.transform),
                    c.enabled, c.depth, c.clearFlags, c.cullingMask,
                    c.fieldOfView, c.nearClipPlane, c.farClipPlane,
                    c.stereoTargetEye,
                    c.targetTexture == null ? "screen" : c.targetTexture.name));

                // We list everything: this is where the effect that breaks stereo shows up.
                var effets = new List<string>();
                var autres = new List<string>();
                foreach (var m in c.GetComponents<MonoBehaviour>())
                {
                    if (m == null) continue;
                    var t = m.GetType();
                    var n = t.Name + (m.enabled ? "" : "(off)");
                    if (CameraEffects.UsesCommandBuffers(t)) effets.Add(n + "[cb]");
                    else if (CameraEffects.UsesRenderImage(t)) effets.Add(n + "[img]");
                    else autres.Add(n);
                }
                if (effets.Count > 0)
                    sb.AppendLine("      camera effects  : " + string.Join(", ", effets.ToArray()));
                if (autres.Count > 0)
                    sb.AppendLine("      other components: " + string.Join(", ", autres.ToArray()));

                // FxPro's switches are set per scene, so the component being present says
                // nothing: only its parameters tell one world apart from another.
                foreach (var m in c.GetComponents<MonoBehaviour>())
                {
                    if (m == null || m.GetType().Name != "FxPro") continue;
                    sb.AppendLine("      FxPro           : " + CameraEffects.DescribeFxPro(m));
                }
            }
        }

        /// <summary>
        /// The switches that decide what the frame looks like, printed with the frame.
        ///
        /// Added after a dump was read against the wrong assumptions: the setting under test
        /// had been toggled from the in-game menu, which saves on close, so the report showed
        /// a state nobody had intended. A dump that does not say how it was configured cannot
        /// be trusted, and every minute spent theorising over one is wasted.
        /// </summary>
        private static void DumpSettings(StringBuilder sb)
        {
            sb.AppendLine("-- AwayVR effect settings --");
            sb.AppendLine("  renderScale=" + Plugin.CfgResolutionScale.Value.ToString("0.00")
                          + "  (live " + UnityEngine.XR.XRSettings.eyeTextureResolutionScale.ToString("0.00") + ")");
            sb.AppendLine("  weaponsCameraOff=" + Plugin.CfgWeaponsCameraOff.Value
                          + "  effectsMoved=" + WeaponEffects.Installed);
            sb.AppendLine("  noOcclusion=" + Plugin.CfgDisableOcclusion.Value
                          + "  noDepthOfField=" + Plugin.CfgDisableDepthOfField.Value
                          + "  noGlobalFog=" + Plugin.CfgDisableGlobalFog.Value);
            sb.AppendLine("  noBlink=" + Plugin.CfgDisableBlink.Value
                          + "  noTemporalAA=" + Plugin.CfgDisableTemporalAA.Value
                          + "  charEffects=" + Plugin.CfgCharacterEffects.Value
                          + "  noBloom=" + Plugin.CfgDisableBloom.Value
                          + "  noColorGrading=" + Plugin.CfgDisableColorGrading.Value);
            sb.AppendLine("  layerBisect=" + (LayerBisect.Current < 0 ? "none"
                          : LayerBisect.Current + " " + LayerTools.LayerName(LayerBisect.Current)));
            sb.AppendLine("  aniso=" + UnityEngine.QualitySettings.anisotropicFiltering
                          + "  cascades=" + UnityEngine.QualitySettings.shadowCascades
                          + "  shadowRes=" + UnityEngine.QualitySettings.shadowResolution
                          + "  shadowDist=" + UnityEngine.QualitySettings.shadowDistance.ToString("0")
                          + "  lodBias=" + UnityEngine.QualitySettings.lodBias.ToString("0.#"));
        }

        private static void DumpNodes(StringBuilder sb)
        {
            var states = new List<XRNodeState>();
            InputTracking.GetNodeStates(states);
            sb.AppendLine("-- XR nodes (" + states.Count + ") --");
            foreach (var s in states)
            {
                Vector3 pos;
                sb.AppendLine(string.Format("  {0,-16} tracked={1} pos={2}",
                    s.nodeType, s.tracked,
                    s.TryGetPosition(out pos) ? pos.ToString("0.00") : "n/a"));
            }
        }

        /// <summary>
        /// State of the game locks that can swallow a command.
        ///
        /// ShowPanels.Update only opens the pause menu if NONE of these flags blocks it, and
        /// it does not run at all when the component is absent from the scene. Without these
        /// values, a stick click with no effect is indistinguishable from a mute input.
        /// </summary>
        private static void DumpEtatJeu(StringBuilder sb)
        {
            sb.AppendLine("-- Game locks --");

            try { sb.AppendLine("  hide_weapons        : " + basics.hide_weapons); }
            catch { sb.AppendLine("  hide_weapons        : <unreadable>"); }

            try
            {
                sb.AppendLine("  combat allowed      : " + SeavenTools.AreCombatActionsAllowed());
            }
            catch (System.Exception e)
            {
                sb.AppendLine("  combat allowed      : <unreadable> " + e.GetType().Name);
            }

            int swords = 0, swordsOn = 0;
            foreach (var w in Object.FindObjectsOfType<weapons_sword>())
            {
                if (w == null) continue;
                swords++;
                if (w.enabled) swordsOn++;
            }
            sb.AppendLine("  swing               : enabled=" + Plugin.CfgSwingToAttack.Value
                          + "  melee=" + Swing.MeleeDetected
                          + "  settled=" + Swing.MeleeSettled
                          + "  weapons_sword=" + swordsOn + "/" + swords
                          + "  speed=" + Swing.Speed.ToString("0.00")
                          + "  threshold=" + Plugin.CfgSwingThreshold.Value.ToString("0.00"));
            // A legacy animation and an Invoke both follow timeScale: at zero "DiaryShow"
            // does not play and the book stays parked off to the right.
            sb.AppendLine("  Time.timeScale      : " + Time.timeScale);

            var tShowPanels = HarmonyLib.AccessTools.TypeByName("ShowPanels");
            if (tShowPanels == null)
            {
                sb.AppendLine("  ShowPanels type not found");
            }
            else
            {
                var inst = HarmonyLib.AccessTools.Field(tShowPanels, "instance");
                var dial = HarmonyLib.AccessTools.Field(tShowPanels, "dialog_active");
                object v = inst != null ? inst.GetValue(null) : null;
                sb.AppendLine("  ShowPanels.instance : "
                              + (v as Object != null ? "present in the scene" : "ABSENT"));
                if (dial != null)
                    sb.AppendLine("  dialog_active       : " + dial.GetValue(null));
            }

            var tCanceler = HarmonyLib.AccessTools.TypeByName("InputCanceler");
            if (tCanceler != null)
            {
                var p = HarmonyLib.AccessTools.Property(tCanceler, "Instance")
                        ?? HarmonyLib.AccessTools.Property(tCanceler.BaseType, "Instance");
                object inst = p != null ? p.GetValue(null, null) : null;
                if (inst as Object != null)
                {
                    var pc = HarmonyLib.AccessTools.Property(tCanceler, "AreInputCancelled");
                    sb.AppendLine("  AreInputCancelled   : "
                                  + (pc != null ? pc.GetValue(inst, null) : "?"));
                }
                else sb.AppendLine("  InputCanceler       : absent");
            }

            // The diary's internal state: tells us whether the game CONSIDERS it open.
            // Without it there is no telling a diary that refuses to open apart from an open
            // diary whose panel is simply misplaced.
            var tDiary = HarmonyLib.AccessTools.TypeByName("JourneyDiary");
            if (tDiary != null)
            {
                var diary = Object.FindObjectOfType(tDiary);
                if (diary != null)
                {
                    sb.AppendLine("  JourneyDiary : "
                        + string.Join("  ", new[] { "_IsActive", "_InTransition", "_HasFocus" })
                          .Replace("_", ""));
                    foreach (var nom in new[] { "_IsActive", "_InTransition", "_HasFocus", "_CurrentPage" })
                    {
                        var f = HarmonyLib.AccessTools.Field(tDiary, nom);
                        if (f != null) sb.AppendLine("      " + nom + " = " + f.GetValue(diary));
                    }

                    // Read by reflection: the Animation type lives in a Unity module the
                    // project does not reference, and pulling it in just for a diagnostic
                    // would bloat the shipped plugin.
                    var fa = HarmonyLib.AccessTools.Field(tDiary, "_AppearAnimation");
                    object anim = fa != null ? fa.GetValue(diary) : null;
                    if (anim as Object == null)
                    {
                        sb.AppendLine("      _AppearAnimation = NULL, the animation cannot play");
                    }
                    else
                    {
                        var pj = HarmonyLib.AccessTools.Property(anim.GetType(), "isPlaying");
                        sb.AppendLine("      _AppearAnimation = " + ((Object)anim).name
                            + "  isPlaying=" + (pj != null ? pj.GetValue(anim, null) : "?"));
                    }
                }
                else sb.AppendLine("  JourneyDiary : absent from the scene");
            }

            DumpGrenade(sb);
            DumpVideo(sb);
            DumpDialogue(sb);

            sb.AppendLine("-- VR inputs --");
            foreach (VrBindings.Action a in System.Enum.GetValues(typeof(VrBindings.Action)))
                sb.AppendLine(string.Format("  {0,-14} {1,-26} tenu={2}",
                    a, VrBindings.Text(a), VrBindings.Held(a)));
        }

        /// <summary>
        /// Canvases present in the scene but disabled. FindObjectsOfType ignores them, yet
        /// that is exactly the state of a closed map or dialogue box: without this list we
        /// would not even know they exist, nor under what name to look for them.
        /// </summary>
        private static void DumpCanvasesInactifs(StringBuilder sb, Canvas[] actifs)
        {
            var vus = new System.Collections.Generic.HashSet<Canvas>(actifs);
            var lignes = new System.Collections.Generic.List<string>();

            foreach (var c in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                if (c == null || vus.Contains(c)) continue;
                // Skip assets and uninstantiated prefabs: only the scene is of interest.
                if (!c.gameObject.scene.IsValid()) continue;
                if (c.name.StartsWith("AwayVR_")) continue;

                lignes.Add("  [inactive] " + Hierarchy.Path(c.transform)
                           + "  mode=" + c.renderMode
                           + "  layer=" + c.gameObject.layer
                           + "  root=" + c.isRootCanvas);
            }

            if (lignes.Count == 0) return;
            sb.AppendLine("-- Inactive canvases (" + lignes.Count + ") --");
            foreach (var l in lignes) sb.AppendLine(l);
        }

        /// <summary>
        /// When a panel's content sits far from its centre, walks up the parent chain of
        /// the first offending element, reporting each local position. The offset is carried
        /// by ONE specific node: that is what needs dealing with, not the canvas.
        /// </summary>
        private static void DumpChaineHorsCadre(StringBuilder sb, Canvas c, RectTransform rt,
                                                Vector3 centreRect)
        {
            if (rt == null) return;
            float seuil = Mathf.Max(rt.rect.width, rt.rect.height) * rt.lossyScale.x * 0.6f;

            foreach (var g in c.GetComponentsInChildren<Graphic>(false))
            {
                if (g == null || !g.enabled || g.color.a < 0.05f) continue;
                if ((g.rectTransform.position - centreRect).magnitude < seuil) continue;

                sb.AppendLine("      chain of '" + g.name + "' (out of frame):");
                var t = g.transform;
                while (t != null && t != c.transform)
                {
                    var r = t as RectTransform;
                    sb.AppendLine("        " + t.name
                                  + "  local=" + t.localPosition.ToString("0")
                                  + (r != null
                                      ? "  anchored=" + r.anchoredPosition.ToString("0")
                                        + "  anchors=" + r.anchorMin + ".." + r.anchorMax
                                      : "")
                                  + "  active=" + t.gameObject.activeSelf);
                    t = t.parent;
                }
                return;   // one example is enough: the chain is the same for the block
            }
        }

        /// <summary>
        /// Why the held grenade is or is not showing: four things have to line up.
        /// </summary>
        private static void DumpGrenade(StringBuilder sb)
        {
            sb.AppendLine("-- Grenade --");

            var secondary = Object.FindObjectOfType<weapons_secondary>();
            sb.AppendLine("  weapons_secondary : "
                          + (secondary != null ? Hierarchy.Path(secondary.transform) : "ABSENT"));

            if (secondary != null)
            {
                var f = HarmonyLib.AccessTools.Field(typeof(weapons_secondary), "projectile");
                var prefab = f != null ? f.GetValue(secondary) as GameObject : null;
                sb.AppendLine("  projectile prefab : " + (prefab != null ? prefab.name : "NULL"));
                if (prefab != null)
                {
                    int meshes = 0;
                    foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                        if (mf != null && mf.sharedMesh != null) meshes++;
                    sb.AppendLine("  meshes in prefab  : " + meshes
                                  + (meshes == 0 ? "   <- nothing to copy" : ""));
                }
            }

            var tBasics = HarmonyLib.AccessTools.TypeByName("basics");
            var fCount = tBasics != null ? HarmonyLib.AccessTools.Field(tBasics, "grenades") : null;
            sb.AppendLine("  basics.grenades   : "
                          + (fCount != null ? fCount.GetValue(null).ToString() : "field not found"));

            var hand = Hands.Get(HandSide.Left);
            sb.AppendLine("  left hand         : "
                          + (hand != null ? Hierarchy.Path(hand) : "ABSENT"));

            if (hand != null)
            {
                var held = hand.Find("AwayVR_HeldGrenade");
                if (held == null) sb.AppendLine("  held model        : not built");
                else
                {
                    var cam = VrManager.MainCamera;
                    sb.AppendLine("  held model        : built, active="
                                  + held.gameObject.activeInHierarchy
                                  + ", scale=" + held.localScale.x.ToString("0.00"));

                    // Where it actually ENDS UP. "Built and active" says nothing about
                    // whether it is anywhere you could see it: a mesh nested in the prefab
                    // can land metres away, which looks exactly like it was never created.
                    foreach (var r in held.GetComponentsInChildren<Renderer>(true))
                    {
                        var d = cam != null
                            ? (r.bounds.center - cam.transform.position).magnitude : 0f;
                        float angle = cam != null
                            ? Vector3.Angle(cam.transform.forward,
                                            r.bounds.center - cam.transform.position) : 0f;
                        sb.AppendLine("      " + r.name
                                      + "  size=" + r.bounds.size.ToString("0.00")
                                      + "  distance=" + d.ToString("0.00") + " m"
                                      + "  angle=" + angle.ToString("0") + " deg"
                                      + "  visible=" + r.isVisible
                                      + "  layer=" + r.gameObject.layer);
                    }
                }
            }
        }

        private static void DumpVideo(StringBuilder sb)
        {
            sb.AppendLine("-- Video --");
            int n = 0;

            var tVideo = HarmonyLib.AccessTools.TypeByName("UnityEngine.Video.VideoPlayer");
            if (tVideo != null)
            {
                foreach (var o in Object.FindObjectsOfType(tVideo))
                {
                    var c = o as Component;
                    if (c == null) continue;
                    n++;
                    sb.AppendLine("  VideoPlayer on " + Hierarchy.Path(c.transform));
                    foreach (var nom in new[] { "isPlaying", "renderMode", "targetCamera",
                                                "targetTexture", "targetMaterialRenderer", "url" })
                    {
                        var p = HarmonyLib.AccessTools.Property(tVideo, nom);
                        if (p == null) continue;
                        object v = null;
                        try { v = p.GetValue(o, null); } catch { }
                        sb.AppendLine("      " + nom + " = " + (v == null ? "<null>" : v.ToString()));
                    }
                }
            }
            else sb.AppendLine("  (VideoPlayer module absent from the build)");

            // Old-API MovieTextures are placed on a material or on a RawImage.
            foreach (var r in Resources.FindObjectsOfTypeAll<UnityEngine.UI.RawImage>())
            {
                if (r == null || !r.gameObject.scene.IsValid()) continue;
                if (r.texture == null) continue;
                var t = r.texture.GetType().Name;
                if (t.IndexOf("Movie", System.StringComparison.OrdinalIgnoreCase) < 0
                    && t.IndexOf("Render", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                n++;
                sb.AppendLine("  RawImage " + Hierarchy.Path(r.transform)
                              + "   texture=" + r.texture.name + " (" + t + ")"
                              + "  active=" + r.gameObject.activeInHierarchy);
            }

            // UI_play_video places a MovieTexture on a RawImage in its Start(). While the
            // object is inactive that Start has not run, the texture is null, and the sweep
            // above misses it: so we query the component itself.
            var tPlay = HarmonyLib.AccessTools.TypeByName("UI_play_video");
            if (tPlay != null)
            {
                var champ = HarmonyLib.AccessTools.Field(tPlay, "movie");
                foreach (var o in Resources.FindObjectsOfTypeAll(tPlay))
                {
                    var c = o as Component;
                    if (c == null || !c.gameObject.scene.IsValid()) continue;
                    n++;

                    object mv = champ != null ? champ.GetValue(o) : null;
                    sb.AppendLine("  UI_play_video on " + Hierarchy.Path(c.transform)
                                  + "   actif=" + c.gameObject.activeInHierarchy);
                    sb.AppendLine("      movie = " + (mv as Object == null
                        ? "<null> - no video assigned"
                        : ((Object)mv).name));
                }
            }

            // The menu's "background video" is not one: Menu_Background picks one of three
            // sets at random and activates it. So it is a scene object rendered by the main
            // camera, and its absence comes down to its layer, its position or the camera
            // mask - not to our UI capture.
            var tBg = HarmonyLib.AccessTools.TypeByName("Menu_Background");
            if (tBg == null)
            {
                sb.AppendLine("  Menu_Background type not found in the assemblies");
            }
            else
            {
                var cam = VrManager.MainCamera;
                int fonds = 0;
                foreach (var o in Resources.FindObjectsOfTypeAll(tBg))
                {
                    fonds++;
                    var c = o as Component;
                    if (c == null || !c.gameObject.scene.IsValid()) continue;
                    n++;
                    sb.AppendLine("  Menu_Background on " + Hierarchy.Path(c.transform));

                    foreach (var nom in new[] { "Background_1", "Background_2", "Background_3" })
                    {
                        var f = HarmonyLib.AccessTools.Field(tBg, nom);
                        var go = f != null ? f.GetValue(o) as GameObject : null;
                        if (go == null) { sb.AppendLine("      " + nom + " = <null>"); continue; }

                        sb.AppendLine("      " + nom + " : " + Hierarchy.Path(go.transform)
                                      + "  active=" + go.activeInHierarchy);
                        if (!go.activeInHierarchy) continue;

                        int seen = 0, total = 0;
                        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                        {
                            if (r == null) continue;
                            total++;
                            int l = r.gameObject.layer;
                            bool inMask = cam != null && (cam.cullingMask & (1 << l)) != 0;
                            if (r.enabled && r.gameObject.activeInHierarchy && inMask) seen++;
                            if (total <= 6)
                                sb.AppendLine("          " + r.name + "  layer=" + l
                                              + " '" + LayerMask.LayerToName(l) + "'"
                                              + "  mask=" + (inMask ? "YES" : "NO")
                                              + "  visible=" + r.isVisible
                                              + "  dist=" + (cam != null
                                                  ? (r.bounds.center - cam.transform.position)
                                                        .magnitude.ToString("0.0") + " m" : "?"));
                        }
                        sb.AppendLine("          -> " + seen + "/" + total + " renderers rendered");
                    }
                }
                if (fonds == 0)
                    sb.AppendLine("  no Menu_Background in this scene");
            }

            if (n == 0) sb.AppendLine("  (no player found)");
        }

        private static void DumpDialogue(StringBuilder sb)
        {
            var cam = VrManager.MainCamera;
            var cibles = new List<Transform>();

            foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
            {
                if (t == null || !t.gameObject.scene.IsValid()) continue;
                if (t.name.IndexOf("Dialog", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (t.name.StartsWith("AwayVR_")) continue;
                cibles.Add(t);
                if (cibles.Count >= 8) break;
            }

            sb.AppendLine("-- 'Dialog' objects (" + cibles.Count + ") --");
            foreach (var t in cibles)
            {
                int layer = t.gameObject.layer;
                bool rendered = cam != null && (cam.cullingMask & (1 << layer)) != 0;

                sb.AppendLine("  " + Hierarchy.Path(t));
                sb.AppendLine("      actif=" + t.gameObject.activeInHierarchy
                              + "  layer=" + layer + " '" + LayerMask.LayerToName(layer) + "'"
                              + "  rendered by the camera: " + (rendered ? "YES" : "NO"));

                if (cam != null)
                {
                    var vers = t.position - cam.transform.position;
                    sb.AppendLine("      distance=" + vers.magnitude.ToString("0.00") + " m"
                                  + "  angle from gaze="
                                  + Vector3.Angle(cam.transform.forward, vers).ToString("0") + " deg"
                                  + "  scale=" + t.lossyScale.ToString("0.000"));
                }

                var comps = new List<string>();
                foreach (var comp in t.GetComponents<Component>())
                    if (comp != null && !(comp is Transform)) comps.Add(comp.GetType().Name);
                if (comps.Count > 0)
                    sb.AppendLine("      components: " + string.Join(", ", comps.ToArray()));

                foreach (var cv in t.GetComponentsInChildren<Canvas>(true))
                    sb.AppendLine("      canvas: " + Hierarchy.Path(cv.transform)
                                  + "  mode=" + cv.renderMode + "  root=" + cv.isRootCanvas
                                  + "  active=" + cv.gameObject.activeInHierarchy);

                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                    sb.AppendLine("      renderer: " + r.name + "  type=" + r.GetType().Name
                                  + "  visible=" + r.isVisible + "  layer=" + r.gameObject.layer);
            }
        }
        private static void DumpCanvases(StringBuilder sb)
        {
            var canvases = Object.FindObjectsOfType<Canvas>();
            int overlay = 0;
            sb.AppendLine("-- Canvas (" + canvases.Length + ") --");
            sb.AppendLine("  window = " + Screen.width + "x" + Screen.height);
            DumpCanvasesInactifs(sb, canvases);
            foreach (var c in canvases)
            {
                if (!c.isRootCanvas) continue;
                if (c.renderMode == RenderMode.ScreenSpaceOverlay) overlay++;

                var rt = c.transform as RectTransform;
                string taille = "?";
                if (rt != null)
                {
                    // Real physical size: that is what gives away an oversized panel.
                    var m = rt.sizeDelta;
                    var s = rt.lossyScale;
                    taille = m.x.ToString("0") + "x" + m.y.ToString("0") + " px"
                             + "  ->  " + (m.x * s.x).ToString("0.00") + "x"
                             + (m.y * s.y).ToString("0.00") + " m";
                }

                sb.AppendLine("  " + Hierarchy.Path(c.transform) + "  mode=" + c.renderMode
                              + " enabled=" + c.enabled + "  " + taille);

                // Two distinct causes make a panel invisible with no way to tell them
                // apart by eye: either it is not handled (a render mode other than
                // ScreenSpaceOverlay, so ignored by CanvasTools), or it is handled but its
                // layer is missing from the camera mask. We settle it here.
                var camC = VrManager.MainCamera;
                int layer = c.gameObject.layer;
                string nomLayer = LayerMask.LayerToName(layer);
                bool rendered = camC != null && (camC.cullingMask & (1 << layer)) != 0;

                sb.AppendLine("      layer=" + layer
                              + (string.IsNullOrEmpty(nomLayer) ? "" : " '" + nomLayer + "'")
                              + "  in camera mask: " + (rendered ? "YES" : "NO")
                              + "  sortingOrder=" + c.sortingOrder
                              + "  worldCamera=" + (c.worldCamera != null ? c.worldCamera.name : "-")
                              + "  handled=" + (CanvasTools.IsHandled(c) ? "yes" : "no"));

                // The PIVOT changes everything: measuring an offset from transform.position
                // only makes sense if the pivot is centred. A corner pivot yields enormous
                // offsets for content that is nevertheless perfectly framed. So we report
                // the pivot, and measure everything from the rect's real centre.
                var camDiag = VrManager.MainCamera;
                Vector3 centreRect = c.transform.position;
                if (rt != null)
                {
                    centreRect = rt.TransformPoint(rt.rect.center);
                    sb.AppendLine("      pivot=" + rt.pivot
                                  + "  rect=(" + rt.rect.x.ToString("0") + "," + rt.rect.y.ToString("0")
                                  + " " + rt.rect.width.ToString("0") + "x" + rt.rect.height.ToString("0") + ")"
                                  + "  pivot->centre offset="
                                  + (centreRect - c.transform.position).magnitude.ToString("0.00") + " m");
                }

                if (camDiag != null && c.renderMode == RenderMode.WorldSpace)
                {
                    var vers = centreRect - camDiag.transform.position;
                    sb.AppendLine("      panel centre: distance=" + vers.magnitude.ToString("0.00") + " m"
                                  + "  angle from gaze=" + Vector3.Angle(camDiag.transform.forward, vers).ToString("0")
                                  + " deg  (fov " + camDiag.fieldOfView.ToString("0") + ")");
                }

                // Visible graphics. The list used to stop at 6, which hid precisely the
                // elements we were looking for: we raise the limit. We also give the
                // physical size and the offset from the panel centre - an oversized element,
                // or one pushed out of frame, stands out immediately.
                int shown = 0;
                foreach (var g in c.GetComponentsInChildren<Graphic>(false))
                {
                    if (g == null || !g.enabled || g.color.a < 0.05f) continue;
                    var img = g as Image;

                    string geo = "";
                    var grt = g.rectTransform;
                    if (grt != null)
                    {
                        var s = grt.lossyScale;
                        var r = grt.rect;
                        // Offset from the panel CENTRE, the only reference that tells you
                        // whether an element is inside the frame or beside it.
                        var d = grt.position - centreRect;
                        geo = "  " + (r.width * s.x).ToString("0.00") + "x"
                              + (r.height * s.y).ToString("0.00") + " m"
                              + "  centre_offset=" + d.magnitude.ToString("0.00") + " m";
                    }

                    sb.AppendLine("      " + g.GetType().Name + " '" + g.name + "'"
                                  + geo
                                  + (img != null
                                      ? "  sprite=" + (img.sprite != null ? img.sprite.name : "<aucun>")
                                      : ""));
                    if (++shown >= 40) { sb.AppendLine("      ... (truncated)"); break; }
                }

                DumpChaineHorsCadre(sb, c, rt, centreRect);
            }
            if (overlay > 0)
                sb.AppendLine("  /!\\ " + overlay + " canvases in ScreenSpaceOverlay: invisible in VR.");
        }
    }
}

