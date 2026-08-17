using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Keeps the weapon's shield hidden until you actually guard.
    ///
    /// The game relied on the idle animation to hold it out of sight; with the weapon on the
    /// hand that animation no longer plays, so the shield sat there permanently. It follows
    /// weapons_sword.IsGuarding instead, which is the state the game itself uses.
    ///
    /// Bound from the sword's own OnEnable rather than searched for: only the main character
    /// carries a shield, so looking for one on every other character would be a scene sweep
    /// that can never succeed.
    /// </summary>
    internal static class Shield
    {
        private static weapons_sword _sword;
        private static GameObject _plate;

        public static void Forget()
        {
            _sword = null;
            _plate = null;
        }

        /// <summary>Called when a sword becomes active. No shield on it means nothing to do.</summary>
        public static void Bind(weapons_sword sword)
        {
            if (sword == null) return;
            _sword = sword;
            _plate = sword.Shield_Sprite;
        }

        public static void Unbind(weapons_sword sword)
        {
            if (sword != _sword) return;
            _sword = null;
            _plate = null;
        }

        public static void Tick()
        {
            if (!VrManager.VrActive || _sword == null || _plate == null) return;

            bool guarding = _sword.IsGuarding;
            if (_plate.activeSelf != guarding) _plate.SetActive(guarding);
        }
    }
}
