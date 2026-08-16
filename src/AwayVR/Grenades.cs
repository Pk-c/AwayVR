using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

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
            // From the MODEL, not from the hand. Once the grenade is offset in the grip the
            // two are centimetres apart, and a throw that starts somewhere other than where
            // you can see the grenade is exactly the kind of mismatch a headset makes
            // obvious.
            t.position = (_held != null && _held.gameObject.activeInHierarchy)
                ? _held.position : hand.position;
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

                // Placement relative to the PREFAB ROOT, not to the mesh's immediate parent.
                // Copying localPosition straight across only works for a mesh sitting at the
                // top level; nested one level down it inherits an offset that is never
                // applied, and the model ends up metres from the hand.
                var rel = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                piece.transform.localPosition = rel.GetColumn(3);
                var forward = (Vector3)rel.GetColumn(2);
                var up = (Vector3)rel.GetColumn(1);
                if (forward.sqrMagnitude > 1e-8f && up.sqrMagnitude > 1e-8f)
                    piece.transform.localRotation = Quaternion.LookRotation(forward, up);
                piece.transform.localScale = new Vector3(
                    ((Vector3)rel.GetColumn(0)).magnitude,
                    ((Vector3)rel.GetColumn(1)).magnitude,
                    ((Vector3)rel.GetColumn(2)).magnitude);

                piece.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                piece.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
                piece.layer = hand.gameObject.layer;
            }
            return rootT;
        }

        // ------------------------------------------------------------------
        // Throw gesture
        // ------------------------------------------------------------------

        /// <summary>
        /// The game's own left trigger axis, already declared in its InputManager on joystick
        /// axis 8. We read the analog value rather than the button because the button fires
        /// the moment the trigger moves at all — which is what made grenades feel like they
        /// were going off on their own.
        /// </summary>
        private const string TriggerAxis = "LeftTrigg_sensibility_Attack";

        private static bool _armed;
        private static bool _throwPending;
        private static float _pendingSince;

        /// <summary>True while the trigger is squeezed and the grenade is ready to leave.</summary>
        public static bool Armed { get { return _armed; } }

        /// <summary>
        /// Reads the gesture: squeeze the trigger all the way to arm, let it go all the way to
        /// throw. Two widely separated thresholds, so no amount of trembling on the trigger
        /// can cross both.
        /// </summary>
        private static void UpdateGesture()
        {
            if (!VrManager.VrActive || !Plugin.CfgGrenadeGesture.Value)
            {
                _armed = false;
                _throwPending = false;
                return;
            }

            float v;
            try { v = Mathf.Abs(Input.GetAxisRaw(TriggerAxis)); }
            catch { return; }

            if (!_armed)
            {
                if (v >= Plugin.CfgGrenadeArmLevel.Value && Count() >= 1) _armed = true;
            }
            else if (v <= Plugin.CfgGrenadeReleaseLevel.Value)
            {
                _armed = false;
                _throwPending = true;
                _pendingSince = Time.unscaledTime;
            }
        }

        /// <summary>
        /// Claimed by the input redirect in place of the raw button press.
        ///
        /// A flag rather than a single-frame pulse: the gesture is read in LateUpdate and the
        /// game reads its button in Update, and nothing fixes the order between our scripts
        /// and its own. It does go stale after a moment, so a throw that finds no reader —
        /// during a load, say — cannot surface much later as a grenade nobody asked for.
        /// </summary>
        public static bool ConsumeThrow()
        {
            if (!_throwPending) return false;
            if (Time.unscaledTime - _pendingSince > 0.5f) { _throwPending = false; return false; }
            _throwPending = false;
            return true;
        }

        public static void Tick()
        {
            UpdateGesture();

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

            // Swells while armed: the only feedback there is that the throw is charged, and
            // without it you cannot tell an armed grenade from an idle one.
            float s = Plugin.CfgGrenadeScale.Value;
            if (_armed) s *= Plugin.CfgGrenadeArmScale.Value;
            _held.localScale = new Vector3(s, s, s);

            Place();
        }

        /// <summary>
        /// Places the held model. Called just before the frame is drawn, never from
        /// LateUpdate: the hand pose is re-latched afterwards, and compensating for a
        /// rotation one frame old is what made the grenade tremble in the hand.
        /// </summary>
        public static void Place()
        {
            if (_held == null || !_held.gameObject.activeSelf) return;

            // The offset is expressed in RIG space, not hand space — the same lesson the
            // weapon taught us. An offset written in hand space rotates with the wrist and
            // becomes a lever arm, so moving the grenade inevitably moves its centre of
            // rotation too. Cancelling the hand's rotation on the offset alone leaves the
            // held point where the controller is, and the offset merely translates it.
            //
            // The rotation is read from the tracking rather than from the parent transform.
            // Both end up the same, but the parent's copy is only written by its own
            // before-render callback, and nothing orders that against ours — reading the
            // source directly removes the question entirely.
            var rot = InputTracking.GetLocalRotation(XRNode.LeftHand);
            var offset = new Vector3(Plugin.CfgGrenadeOffX.Value,
                                     Plugin.CfgGrenadeOffY.Value,
                                     Plugin.CfgGrenadeOffZ.Value);
            _held.localPosition = Quaternion.Inverse(rot) * offset;
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
