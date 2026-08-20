using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace AwayVR.Patches
{
    /// <summary>
    /// Gives the robot back its lock-on, and with it its only means of firing.
    ///
    /// WeaponMissile fires nothing unless TargetLockSystem holds at least one target. That system
    /// acquires by raycasting from the camera, then drops any target whose HUD marker has left
    /// the screen - and it asks the marker where it is:
    ///
    ///     HUDTargets_[i].transform.position = main.WorldToScreenPoint(centre);   // LateUpdate
    ///     if (!new Rect(-m, -m, Screen.width + m, Screen.height + m)
    ///             .Contains(HUDTargets_[i].transform.position)) RemoveTarget(i); // Update
    ///
    /// That round trip only holds for an OVERLAY canvas, where a transform position is a pixel
    /// coordinate. The mod hands every screen canvas to a UI camera parked a hundred kilometres
    /// up, so the marker's position is a world point with a Y around 100000 - outside any screen
    /// rect ever built. Every target was therefore dropped the frame after it was acquired, the
    /// count never left zero, and the trigger did nothing at all.
    ///
    /// VR breaks the same test a second way, on its own: WorldToScreenPoint measures in EYE
    /// TEXTURE pixels (2683x2870 here) while the rect is built from the desktop window, so even
    /// the centre of your view falls outside it.
    ///
    /// So the test itself is replaced, in place, by one asked in a space that still exists: is
    /// the target inside the CAMERA's view? Nothing else in Update changes - obstruction, range,
    /// destruction and the maximum count all cull exactly as before - and the release when you
    /// look away is done again in LateUpdate, where both the target and the camera are at hand.
    ///
    /// Moving the markers before Update was not enough: a target acquired in that same pass has
    /// a marker created after the move, still carrying its prefab's position, and it was dropped
    /// on the spot. Acquisition and rejection inside one frame, forever - targets never left zero.
    ///
    /// LateUpdate then puts each marker where it must actually be drawn: on the enemy itself, in
    /// the world, since the HUD it belongs to is only raised on demand here. See TargetMarkers.
    /// </summary>
    [HarmonyPatch]
    internal static class TargetLockPatches
    {
        private static readonly FieldInfo FTargets =
            AccessTools.Field(typeof(TargetLockSystem), "targets_");
        private static readonly FieldInfo FMarkers =
            AccessTools.Field(typeof(TargetLockSystem), "HUDTargets_");

        private static readonly MethodInfo MRemove =
            AccessTools.Method(typeof(TargetLockSystem), "RemoveTarget", new[] { typeof(int) });

        internal static bool Resolved { get { return FTargets != null && FMarkers != null; } }

        /// <summary>True once the screen test has been replaced; the prefix stands in until then.</summary>
        internal static bool Transpiled { get; private set; }

        /// <summary>
        /// Stands in for Rect.Contains inside Update. In VR the rect is meaningless - it is built
        /// from the desktop window while the marker lives on an off-scene canvas - so the answer
        /// it wants, "is this target still in view", is given in LateUpdate instead, and nothing
        /// is dropped here.
        /// </summary>
        public static bool OnScreen(ref Rect r, Vector3 p)
        {
            if (VrManager.VrActive && Plugin.CfgTargetLockFix.Value) return true;
            return r.Contains(p);
        }

        public static bool OnScreen2(ref Rect r, Vector2 p)
        {
            if (VrManager.VrActive && Plugin.CfgTargetLockFix.Value) return true;
            return r.Contains(p);
        }

        [HarmonyPatch(typeof(TargetLockSystem), "Update")]
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Update_Transpiler(IEnumerable<CodeInstruction> code)
        {
            var contains3 = AccessTools.Method(typeof(Rect), "Contains", new[] { typeof(Vector3) });
            var contains2 = AccessTools.Method(typeof(Rect), "Contains", new[] { typeof(Vector2) });
            var notre3 = AccessTools.Method(typeof(TargetLockPatches), "OnScreen");
            var notre2 = AccessTools.Method(typeof(TargetLockPatches), "OnScreen2");

            int n = 0;
            foreach (var ins in code)
            {
                if ((ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt)
                    && ins.operand is MethodInfo)
                {
                    var m = (MethodInfo)ins.operand;
                    if (m == contains3 && notre3 != null) { ins.operand = notre3; n++; }
                    else if (m == contains2 && notre2 != null) { ins.operand = notre2; n++; }
                }
                yield return ins;
            }

            Transpiled = n > 0;
            if (n == 0)
                Plugin.Log.LogWarning("Target lock: the screen test was not found; "
                                      + "falling back to moving the markers.");
        }

        /// <summary>Targets held right now, for the scene report.</summary>
        internal static int Count(TargetLockSystem sys)
        {
            if (sys == null || FTargets == null) return -1;
            var l = FTargets.GetValue(sys) as List<GameObject>;
            return l != null ? l.Count : -1;
        }

        [HarmonyPatch(typeof(TargetLockSystem), "Update")]
        [HarmonyPrefix]
        private static void Update_Prefix(TargetLockSystem __instance)
        {
            if (!VrManager.VrActive || !Plugin.CfgTargetLockFix.Value || !Resolved) return;
            // The test itself is gone: nothing left to lie to.
            if (Transpiled) return;

            var markers = FMarkers.GetValue(__instance) as List<GameObject>;
            if (markers == null) return;

            // z stays positive: the rect ignores it, but a marker behind the camera is the
            // game's own business and it settles that in LateUpdate.
            var milieu = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 1f);
            for (int i = 0; i < markers.Count; i++)
                if (markers[i] != null) markers[i].transform.position = milieu;
        }

        [HarmonyPatch(typeof(TargetLockSystem), "LateUpdate")]
        [HarmonyPostfix]
        private static void LateUpdate_Postfix(TargetLockSystem __instance)
        {
            if (!VrManager.VrActive || !Plugin.CfgTargetLockFix.Value || !Resolved) return;

            var cam = VrManager.MainCamera;
            if (cam == null) return;

            var targets = FTargets.GetValue(__instance) as List<GameObject>;
            var markers = FMarkers.GetValue(__instance) as List<GameObject>;
            if (targets == null || markers == null) return;

            int n = Mathf.Min(targets.Count, markers.Count);
            for (int i = 0; i < n; i++)
            {
                var marker = markers[i];
                var target = targets[i];
                if (marker == null || target == null || !marker.activeSelf) continue;

                var col = target.GetComponent<Collider>();
                if (col == null) continue;

                var centre = col.bounds.center;

                if (Plugin.CfgTargetMarkersInWorld.Value)
                {
                    TargetMarkers.Place(marker, centre, cam);
                    continue;
                }

                var ecran = cam.WorldToScreenPoint(centre);
                if (ecran.z <= 0f) continue;

                // Eye texture to canvas: the two pixel spaces differ, and only the ratio
                // carries over.
                var canvas = marker.GetComponentInParent<Canvas>();
                var uiCam = canvas != null ? canvas.worldCamera : null;
                if (uiCam == null) continue;

                float u = ecran.x / Mathf.Max(1, cam.pixelWidth);
                float v = ecran.y / Mathf.Max(1, cam.pixelHeight);

                marker.transform.position = uiCam.ScreenToWorldPoint(
                    new Vector3(u * uiCam.pixelWidth, v * uiCam.pixelHeight, canvas.planeDistance));
            }

            RelacherHorsChamp(__instance, targets, cam);
        }

        /// <summary>
        /// The release the game means by its screen test: a target you have turned away from stops
        /// being locked. Asked of the CAMERA rather than of a marker on a canvas, which is the only
        /// place the question still has an answer. Its own margin is kept, read as a fraction of
        /// the eye texture.
        /// </summary>
        private static void RelacherHorsChamp(TargetLockSystem sys, List<GameObject> targets, Camera cam)
        {
            if (MRemove == null || !Plugin.CfgTargetLockFix.Value) return;

            float marge = sys.outOfScreenMargin / Mathf.Max(1f, cam.pixelHeight);

            for (int i = targets.Count - 1; i >= 0; i--)
            {
                var t = targets[i];
                if (t == null) continue;          // the game clears destroyed ones itself
                var col = t.GetComponent<Collider>();
                if (col == null) continue;

                var vue = cam.WorldToViewportPoint(col.bounds.center);
                bool dedans = vue.z > 0f
                              && vue.x >= -marge && vue.x <= 1f + marge
                              && vue.y >= -marge && vue.y <= 1f + marge;
                if (!dedans) MRemove.Invoke(sys, new object[] { i });
            }
        }
    }
}
