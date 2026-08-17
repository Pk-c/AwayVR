using HarmonyLib;
using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Swing-to-attack for melee weapons. The game already fires animation, trail and hit
    /// box, so we only make InputM believe the button was pressed when the hand accelerates.
    ///
    /// Throwing weapons keep the button: hurling a projectile by waving would be imprecise.
    /// </summary>
    internal static class Swing
    {
        private static Vector3 _lastLocal;
        private static bool _hasLast;
        private static float _lastSwingTime;
        private static int _activeFrame = -1;

        private static float _nextMeleeCheck;
        private static bool _melee;

        /// <summary>Hand speed measured on the last frame, in m/s.</summary>
        public static float Speed { get; private set; }

        /// <summary>True when the active weapon is a melee weapon.</summary>
        public static bool MeleeEquipped { get { return _melee; } }

        /// <summary>
        /// True for exactly one frame after a swing. Detection happens in LateUpdate and
        /// arms the NEXT frame: that way every script in the game sees it, whatever their
        /// execution order, and sees it exactly once.
        /// </summary>
        public static bool IsSwinging { get { return Time.frameCount == _activeFrame; } }

        public static void Tick()
        {
            if (!VrManager.VrActive || !Plugin.CfgSwingToAttack.Value)
            {
                _hasLast = false;
                _melee = false;
                // The question reopens with it: clearing the answer while leaving it marked
                // as settled is what left swing detection dead for the rest of the scene.
                _meleeKnown = false;
                return;
            }

            RefreshMelee();

            var hand = Hands.Get(Plugin.CfgWeaponAttach.Value == WeaponAttachMode.Left
                ? HandSide.Left : HandSide.Right);
            if (hand == null) { _hasLast = false; return; }

            // Measured in rig space: walking or turning must not count as a swing.
            var local = hand.localPosition;
            float dt = Time.unscaledDeltaTime;

            if (_hasLast && dt > 0f)
                Speed = (local - _lastLocal).magnitude / dt;
            else
                Speed = 0f;

            _lastLocal = local;
            _hasLast = true;

            if (!_melee) return;

            if (Speed >= Plugin.CfgSwingThreshold.Value
                && Time.unscaledTime - _lastSwingTime >= Plugin.CfgSwingCooldown.Value)
            {
                _lastSwingTime = Time.unscaledTime;
                _activeFrame = Time.frameCount + 1;
                if (Plugin.CfgVerbose.Value)
                    Plugin.Log.LogInfo("Swing detected at " + Speed.ToString("0.00") + " m/s");
            }
        }

        /// <summary>
        /// The game's melee weapon is the weapons_sword component: it carries the hit box
        /// and the attack states. Launchers (fireballs, grenades, boomerang, bombs) do not
        /// have it.
        /// </summary>
        /// <summary>Forces the next Tick to look again - scene load, or a weapon swap.</summary>
        public static void Invalidate() { _meleeKnown = false; }

        // The weapon is not in your hands when a scene starts - in the hub it is handed over
        // much later, on crossing a trigger. So the component announces itself instead of us
        // asking: weapons_sword raises OnEnable when the game activates it.

        [HarmonyPatch(typeof(weapons_sword), "OnEnable")]
        [HarmonyPostfix]
        private static void SwordEnabled()
        {
            _melee = true;
            _meleeKnown = true;
        }

        /// <summary>Another sword may still be active, so this only reopens the question.</summary>
        [HarmonyPatch(typeof(weapons_sword), "OnDisable")]
        [HarmonyPostfix]
        private static void SwordDisabled() { _meleeKnown = false; }

        public static void OnSceneLoaded() { Invalidate(); }

        public static bool MeleeDetected { get { return _melee; } }
        public static bool MeleeSettled { get { return _meleeKnown; } }

        private static bool _meleeKnown;

        private static void RefreshMelee()
        {
            // Event-driven: the answer only moves when the character or the weapon changes.
            if (GameState.CharacterChanged) Invalidate();
            if (_meleeKnown) return;

            if (Time.unscaledTime < _nextMeleeCheck) return;
            _nextMeleeCheck = Time.unscaledTime + 0.5f;

            // Scene-wide: weapons_sword is not always under the viewmodel root, and a
            // narrower search never detected melee at all.
            foreach (var s in Object.FindObjectsOfType<weapons_sword>())
            {
                if (s == null || !s.enabled) continue;
                _melee = true;
                _meleeKnown = true;
                return;
            }

            // Settling on "no" is safe: OnEnable corrects it the moment a sword appears.
            _melee = false;
            _meleeKnown = true;
        }
    }
}
