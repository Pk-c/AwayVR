using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.XR;

namespace AwayVR
{
    /// <summary>
    /// Grenades held in, and thrown from, the left hand.
    ///
    /// The game spawns from weapons_secondary's own transform and applies its force along
    /// that transform's forward. So we point it at the hand for the duration of the call and
    /// put it back: count, cooldown, fuse and explosion stay the game's own.
    ///
    /// The held model copies the prefab's meshes rather than instantiating it - instantiating
    /// would run the projectile's Awake and arm a live grenade in your hand.
    /// </summary>
    internal static class Grenades
    {
        private const string HeldName = "AwayVR_HeldGrenade";

        private static Transform _held;
        private static weapons_secondary _secondary;
        private static float _nextSecondaryScan;
        private static GameObject _prefabUsed;

        private static FieldInfo _fCount;
        private static FieldInfo _fProjectile;
        private static FieldInfo _fPower;
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
            _powerSaved = false;
            if (!VrManager.VrActive) return;

            // The force scales even when the throw is not relocated to the hand: they are
            // separate settings, and how hard you threw is meaningful either way.
            ApplyPower(__instance);

            if (!Plugin.CfgGrenadeFromHand.Value) return;

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

            var dir = ThrowDirection(hand);
            // LookRotation gives up on a direction parallel to its up vector; straight up or
            // straight down is a perfectly ordinary way to throw a grenade.
            t.rotation = Mathf.Abs(dir.y) > 0.999f
                ? Quaternion.LookRotation(dir, Vector3.forward)
                : Quaternion.LookRotation(dir, Vector3.up);
            _moved = true;

            // Every throw is logged. Grenades reported as going off by themselves have two
            // possible causes that call for opposite fixes: the input firing when it should
            // not, or the game calling Attack for a reason of its own. Only the log tells
            // them apart.
            if (Plugin.CfgTraceInput.Value)
                Plugin.Log.LogInfo("[grenade] thrown from the left hand");
        }

        /// <summary>
        /// Scales the throw by how hard you threw. speedpower is an ordinary int field, so we
        /// set it for the duration of the call and put it back; the weapon's balance stays the
        /// game's own number, multiplied. Being an int, a small base value quantises the
        /// result - it is logged once under the input trace.
        /// </summary>
        private static void ApplyPower(weapons_secondary instance)
        {
            _powerSaved = false;
            if (!Plugin.CfgGrenadePowerFromMotion.Value) return;

            ResolveFields();
            if (_fPower == null) return;

            int basePower;
            try { basePower = (int)_fPower.GetValue(instance); }
            catch { return; }

            if (!_powerLogged)
            {
                _powerLogged = true;
                if (Plugin.CfgTraceInput.Value)
                    Plugin.Log.LogInfo("[grenade] base speedpower = " + basePower);
            }

            // Peak hand speed against a reference: throw at the reference speed and the force
            // is exactly the game's own. Clamped at both ends so a flick cannot send a grenade
            // across the level, and a grenade let go at rest still leaves the hand.
            float reference = Mathf.Max(Plugin.CfgGrenadeRefSpeed.Value, 0.1f);
            float fresh = Time.unscaledTime - _peakTime <= 0.35f ? _peakSpeed : 0f;
            float factor = Mathf.Clamp(fresh / reference,
                                       Plugin.CfgGrenadePowerMin.Value,
                                       Plugin.CfgGrenadePowerMax.Value);

            _powerOriginal = basePower;
            _powerSaved = true;
            try { _fPower.SetValue(instance, Mathf.RoundToInt(basePower * factor)); }
            catch { _powerSaved = false; return; }

            if (Plugin.CfgTraceInput.Value)
                Plugin.Log.LogInfo("[grenade] thrown at " + fresh.ToString("0.00")
                                   + " m/s, power x" + factor.ToString("0.00"));
        }

        private static int _powerOriginal;
        private static bool _powerSaved;
        private static bool _powerLogged;

        /// <summary>
        /// Puts the transform back immediately. The move has to last exactly one call: this
        /// component sits in the player hierarchy, and leaving it displaced would drag
        /// whatever else reads it out of place.
        /// </summary>
        [HarmonyPatch(typeof(weapons_secondary), "Attack")]
        [HarmonyPostfix]
        private static void Attack_Postfix(weapons_secondary __instance)
        {
            if (_powerSaved)
            {
                _powerSaved = false;
                try { _fPower.SetValue(__instance, _powerOriginal); }
                catch { }
            }

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
            _fPower = AccessTools.Field(typeof(weapons_secondary), "speedpower");
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
        /// Analog axis the gesture reads. We read a value rather than a button because the
        /// button fires the moment the input moves at all - which is what made grenades feel
        /// like they were going off on their own.
        /// </summary>
        private const string GestureAxis = "AwayVR_GripL";

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
            try { v = Mathf.Abs(Input.GetAxisRaw(GestureAxis)); }
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
        /// and its own. It does go stale after a moment, so a throw that finds no reader -
        /// during a load, say - cannot surface much later as a grenade nobody asked for.
        /// </summary>
        public static bool ConsumeThrow()
        {
            if (!_throwPending) return false;
            if (Time.unscaledTime - _pendingSince > 0.5f) { _throwPending = false; return false; }
            _throwPending = false;
            return true;
        }

        // ------------------------------------------------------------------
        // Hand motion, for the throw direction
        // ------------------------------------------------------------------

        private static Vector3 _lastLocal;
        private static bool _haveLast;
        private static Vector3 _peakDir;     // rig space, normalised
        private static float _peakSpeed;
        private static float _peakTime = -999f;

        /// <summary>
        /// Hand speed and direction, in RIG space so a running player's locomotion does not
        /// end up in the throw. It is the PEAK over the last fraction of a second that
        /// counts: the hand is already slowing when the grip opens.
        /// </summary>
        private static void TrackMotion(Transform hand)
        {
            var local = hand.localPosition;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);

            if (_haveLast)
            {
                var v = (local - _lastLocal) / dt;
                float speed = v.magnitude;
                bool stale = Time.unscaledTime - _peakTime > 0.3f;
                if (speed > _peakSpeed || stale)
                {
                    _peakSpeed = speed;
                    _peakDir = speed > 0.0001f ? v / speed : Vector3.zero;
                    _peakTime = Time.unscaledTime;
                }
            }

            _lastLocal = local;
            _haveLast = true;
        }

        /// <summary>
        /// Which way the grenade leaves. The game applies a fixed force along the transform's
        /// forward, so direction is the whole of what we control - the strength of the throw
        /// is the game's own, and stays that way.
        /// </summary>
        private static Vector3 ThrowDirection(Transform hand)
        {
            if (Plugin.CfgGrenadeAimFromMotion.Value
                && _peakSpeed >= Plugin.CfgGrenadeMotionMin.Value
                && Time.unscaledTime - _peakTime <= 0.35f
                && _peakDir.sqrMagnitude > 0.5f)
            {
                // Thrown with an actual arm movement: the gesture already carries its own arc,
                // so it is taken as it is and given no tilt of its own.
                var rig = hand.parent;
                return (rig != null ? rig.rotation * _peakDir : _peakDir).normalized;
            }

            // Released without a throw: aimed where the hand points, tilted up. Straight along
            // the controller the grenade leaves flat and drops almost at once, which is the
            // trajectory that felt wrong - nothing thrown by hand travels level.
            var dir = hand.forward;
            var axis = Vector3.Cross(Vector3.up, dir);
            if (axis.sqrMagnitude > 1e-6f)
                dir = Quaternion.AngleAxis(-Plugin.CfgGrenadeThrowPitch.Value,
                                           axis.normalized) * dir;
            return dir.normalized;
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

            TrackMotion(hand);

            // Rebuilt when the hand is recreated by a scene load, or when the game swaps the
            // projectile for a different one.
            ResolveFields();

            // Cached. This ran FindObjectOfType EVERY FRAME, which walks every loaded object
            // in the scene - by far the most expensive thing the mod did, and for a component
            // that changes at most once per scene. The reference nulls itself when the object
            // is destroyed, so a periodic retry is all that is needed to pick up the next one.
            if (_secondary == null && Time.unscaledTime >= _nextSecondaryScan)
            {
                _nextSecondaryScan = Time.unscaledTime + 0.5f;
                _secondary = Object.FindObjectOfType<weapons_secondary>();
            }

            GameObject prefab = null;
            if (_secondary != null && _fProjectile != null)
                prefab = _fProjectile.GetValue(_secondary) as GameObject;

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

            // Offset in RIG space, not hand space: in hand space it becomes a lever arm and
            // moving the grenade moves its centre of rotation too. The rotation comes from
            // the tracking rather than the parent, whose copy is written by its own
            // before-render callback with no ordering against ours.
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
            _secondary = null;
            _nextSecondaryScan = 0f;
        }
    }
}
