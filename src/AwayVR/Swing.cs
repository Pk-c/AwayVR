using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Swing-to-attack detection, for melee weapons only.
    ///
    /// The game already fires everything an attack needs: animation, trail and hit box. So
    /// we reimplement none of it — we simply make InputM believe the attack button was
    /// pressed at the moment the hand accelerates.
    ///
    /// Throwing weapons keep the button: hurling a projectile by waving your hand would be
    /// imprecise and tiring.
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
        private static void RefreshMelee()
        {
            if (Time.unscaledTime < _nextMeleeCheck) return;
            _nextMeleeCheck = Time.unscaledTime + 0.6f;

            // Scene-wide search: depending on the weapon, weapons_sword is not always under
            // the viewmodel root, and a narrower search never detected melee at all.
            // FindObjectsOfType only returns active objects.
            foreach (var s in Object.FindObjectsOfType<weapons_sword>())
            {
                if (s != null && s.enabled) { _melee = true; return; }
            }
            _melee = false;
        }
    }
}
