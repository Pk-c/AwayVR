using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Grenades held in, and thrown from, the left hand.
    ///
    /// The game spawns its grenade from weapons_secondary's own transform — position,
    /// rotation and throw direction all come from it:
    ///
    ///     Instantiate(projectile, base.transform.position, Quaternion.identity);
    ///     g.GetComponent&lt;Rigidbody&gt;().AddForce(base.transform.forward * speedpower);
    ///
    /// So we do not reimplement the throw at all. We point that transform at the left hand
    /// for the duration of the call and put it back straight after: the grenade leaves the
    /// hand, flies where the hand points, and every bit of the game's own behaviour —
    /// count, cooldown, fuse, explosion — is untouched.
    ///
    /// The held model is built from the prefab's meshes rather than instantiated from it.
    /// Instantiating would run the projectile's own Awake and Start, arming a live grenade
    /// in your hand; copying the meshes gives a purely visual object that cannot do
    /// anything.
    /// </summary>
    internal static class Grenades
    {
        private const string HeldName = "AwayVR_HeldGrenade";

        private static Transform _held;
        private static GameObject _prefabUsed;

        private static FieldInfo _fCount;
        private static FieldInfo _fProjectile;
        private static bool _fieldsResolved;

        // ------------------------------------------------------------------
        // Throwing from the hand
        // ------------------------------------------------------------------

        private static Vector3 _savedPos;
        private static Quaternion _savedRot;
        private static bool _moved;

        [HarmonyPatch(typeof(weapons_secondary), "Attack")]
        [HarmonyPrefix]
        private static void Attack_Prefix(weapons_secondary __instance)
        {
            _moved = false;
            if (!VrManager.VrActive || !Plugin.CfgGrenadeFromHand.Value) return;

            var hand = Hands.Get(HandSide.Left);
            if (hand == null) return;

            var t = __instance.transform;
            _savedPos = t.position;
            _savedRot = t.rotation;
            t.position = hand.position;
            t.rotation = hand.rotation;
            _moved = true;

            // Every throw is logged. Grenades reported as going off by themselves have two
            // possible causes that call for opposite fixes: the input firing when it should
            // not, or the game calling Attack for a reason of its own. Only the log tells
            // them apart.
            if (Plugin.CfgTraceInput.Value)
                Plugin.Log.LogInfo("[grenade] thrown from the left hand");
        }

        /// <summary>
        /// Puts the transform back immediately. The move has to last exactly one call: this
        /// component sits in the player hierarchy, and leaving it displaced would drag
        /// whatever else reads it out of place.
        /// </summary>
        [HarmonyPatch(typeof(weapons_secondary), "Attack")]
        [HarmonyPostfix]
        private static void Attack_Postfix(weapons_secondary __instance)
        {
            if (!_moved) return;
            _moved = false;
            __instance.transform.position = _savedPos;
            __instance.transform.rotation = _savedRot;
        }

        // ------------------------------------------------------------------
        // Model held in the hand
        // ------------------------------------------------------------------

        private static void ResolveFields()
        {
            if (_fieldsResolved) return;
            _fieldsResolved = true;

            var tBasics = AccessTools.TypeByName("basics");
            if (tBasics != null) _fCount = AccessTools.Field(tBasics, "grenades");
            _fProjectile = AccessTools.Field(typeof(weapons_secondary), "projectile");
        }

        private static int Count()
        {
            ResolveFields();
            if (_fCount == null) return 0;
            try { return (int)_fCount.GetValue(null); }
            catch { return 0; }
        }

        /// <summary>
        /// Builds a visual copy: every mesh of the prefab, with its materials and its local
        /// placement, and nothing else. No Rigidbody, no collider, no script.
        /// </summary>
        private static Transform BuildVisual(GameObject prefab, Transform hand)
        {
            var root = new GameObject(HeldName);
            var rootT = root.transform;
            rootT.SetParent(hand, false);

            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null) continue;

                var piece = new GameObject(mf.name);
                piece.transform.SetParent(rootT, false);

                // Local placement relative to the prefab root, so a multi-part model keeps
                // its shape.
                piece.transform.localPosition = mf.transform.localPosition;
                piece.transform.localRotation = mf.transform.localRotation;
                piece.transform.localScale = mf.transform.localScale;

                piece.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                piece.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
                piece.layer = hand.gameObject.layer;
            }
            return rootT;
        }

        public static void Tick()
        {
            if (!VrManager.VrActive || !Plugin.CfgGrenadeInHand.Value)
            {
                if (_held != null) _held.gameObject.SetActive(false);
                return;
            }

            var hand = Hands.Get(HandSide.Left);
            if (hand == null) return;

            // Rebuilt when the hand is recreated by a scene load, or when the game swaps the
            // projectile for a different one.
            ResolveFields();
            GameObject prefab = null;
            var secondary = Object.FindObjectOfType<weapons_secondary>();
            if (secondary != null && _fProjectile != null)
                prefab = _fProjectile.GetValue(secondary) as GameObject;

            if (prefab == null)
            {
                if (_held != null) _held.gameObject.SetActive(false);
                return;
            }

            if (_held == null || _held.parent != hand || _prefabUsed != prefab)
            {
                if (_held != null) Object.Destroy(_held.gameObject);
                _held = BuildVisual(prefab, hand);
                _prefabUsed = prefab;
            }

            bool show = Count() >= 1;
            if (_held.gameObject.activeSelf != show) _held.gameObject.SetActive(show);
            if (!show) return;

            float s = Plugin.CfgGrenadeScale.Value;
            _held.localScale = new Vector3(s, s, s);
            _held.localPosition = new Vector3(Plugin.CfgGrenadeOffX.Value,
                                              Plugin.CfgGrenadeOffY.Value,
                                              Plugin.CfgGrenadeOffZ.Value);
            _held.localRotation = Quaternion.identity;
        }

        public static void Forget()
        {
            if (_held != null) Object.Destroy(_held.gameObject);
            _held = null;
            _prefabUsed = null;
        }
    }
}
