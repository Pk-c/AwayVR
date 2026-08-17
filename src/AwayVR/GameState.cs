using System.Reflection;
using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// The two game-side events the mod reacts to, polled once per frame and shared so the
    /// fade, the melee test, the player body and the weapon holder check need no scans.
    ///
    /// CharacterChanged watches Slots_Handler.active_char; ControlRegained is the moment
    /// InputM hands control back, which is the end of a cutscene. Both are true for one frame.
    /// </summary>
    internal static class GameState
    {
        /// <summary>Active character name, or null before the game has one.</summary>
        public static string Character { get; private set; }

        /// <summary>True for one frame after the character changes. Not on the first read.</summary>
        public static bool CharacterChanged { get; private set; }

        /// <summary>True for one frame after player control is handed back.</summary>
        public static bool ControlRegained { get; private set; }

        private static FieldInfo _fCharacter;
        private static bool _resolved;
        private static bool _controlAllowed = true;

        public static void OnSceneLoaded()
        {
            Character = null;
            CharacterChanged = false;
            ControlRegained = false;
            _controlAllowed = true;
        }

        public static void Tick()
        {
            CharacterChanged = false;
            ControlRegained = false;

            if (!_resolved)
            {
                _resolved = true;
                var t = HarmonyLib.AccessTools.TypeByName("Slots_Handler");
                if (t != null) _fCharacter = HarmonyLib.AccessTools.Field(t, "active_char");
            }

            if (_fCharacter != null)
            {
                string now = null;
                try { now = _fCharacter.GetValue(null) as string; }
                catch { }

                if (now != Character)
                {
                    // The first read is start-up, not a swap: reporting it would flash a fade
                    // and re-run every scan the moment a scene comes up, which the scene load
                    // has already done.
                    bool first = Character == null;
                    Character = now;
                    CharacterChanged = !first;
                }
            }

            bool allowed = true;
            try { allowed = InputM.IsPlayerControlAllowed(); }
            catch { }

            if (allowed && !_controlAllowed) ControlRegained = true;
            _controlAllowed = allowed;
        }
    }
}
