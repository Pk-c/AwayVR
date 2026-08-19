using UnityEngine;

namespace AwayVR
{
    public enum KnockbackDirection
    {
        /// <summary>The game's own: along the body's forward, which a snap turn alone moves.</summary>
        Game,
        /// <summary>Along your gaze, flattened.</summary>
        Gaze,
        /// <summary>Straight away from you, whatever you are looking at.</summary>
        Away
    }

    /// <summary>
    /// A stand-in for the player transform, turned the way the moment actually means.
    ///
    /// The game reads player_teleport.Position_actuelle_du_player.forward whenever it needs to
    /// know which way the player faces - which way to hurl a struck enemy, above all. On a screen
    /// that single direction says three things at once: where the body points, where you look,
    /// and which way is "away from the player", because you face what you hit. VR pulls the three
    /// apart, and the body - which only turns on a snap - is the one that keeps the field.
    ///
    /// Rather than rotate the player - the whole rig hangs off it, the view would spin - we lend
    /// the game a root object placed exactly on the player and turned the right way, and hand it
    /// over only for the calls that ask the question. Everything else keeps reading the real one.
    /// </summary>
    internal static class PlayerFacing
    {
        private const string ProxyName = "AwayVR_PlayerFacing";

        private static Transform _proxy;
        private static Transform _real;

        /// <summary>Depth of the swap: enemies nest their calls through collision callbacks.</summary>
        private static int _borrowed;

        public static void Forget()
        {
            // The proxy belongs to the scene that is going away; the real transform with it.
            _proxy = null;
            _real = null;
            _borrowed = 0;
        }

        /// <summary>
        /// Hands the proxy over and returns what was there, or null when the substitution cannot
        /// be made - in which case the caller does nothing and the game keeps its own answer.
        ///
        /// The target is what is about to be struck: in Away mode the push is measured from it,
        /// so it is driven off along the line that separates you, recomputed while the force
        /// lasts. That is what the game means by its own direction - on a screen you face what
        /// you hit, so "ahead of the body" and "away from me" are the same sentence.
        /// </summary>
        public static Transform Borrow(Transform cible)
        {
            if (!VrManager.VrActive) return null;

            var mode = Plugin.CfgKnockback.Value;
            if (mode == KnockbackDirection.Game) return null;

            var cam = VrManager.MainCamera;
            var reel = player_teleport.Position_actuelle_du_player;
            if (cam == null || reel == null) return null;

            var proxy = Ensure();
            if (proxy == null) return null;

            Vector3 cap = Vector3.zero;

            if (mode == KnockbackDirection.Away && cible != null)
            {
                var ecart = cible.position - reel.position;
                ecart.y = 0f;
                // Standing inside you there is no line to push along: the gaze answers instead.
                if (ecart.sqrMagnitude > 1e-4f) cap = ecart.normalized;
            }

            if (cap.sqrMagnitude < 1e-6f)
            {
                var fwd = cam.transform.forward;
                cap = new Vector3(fwd.x, 0f, fwd.z);
                if (cap.sqrMagnitude < 1e-6f)
                {
                    var up = cam.transform.up;
                    cap = new Vector3(up.x, 0f, up.z);
                }
                if (cap.sqrMagnitude < 1e-6f) return null;
                cap.Normalize();
            }

            proxy.position = reel.position;
            proxy.rotation = Quaternion.LookRotation(cap, Vector3.up);

            _real = reel;
            _borrowed++;
            player_teleport.Position_actuelle_du_player = proxy;
            return reel;
        }

        /// <summary>Gives the real transform back. Silent when nothing was borrowed.</summary>
        public static void Return(Transform reel)
        {
            if (reel == null) return;
            player_teleport.Position_actuelle_du_player = reel;
            if (_borrowed > 0) _borrowed--;
        }

        /// <summary>
        /// Watchdog. A patched method that throws never reaches its postfix, and the game would
        /// then read the proxy for the rest of the session - including its POSITION, one frame
        /// stale. Cheap to check, and it puts everything straight.
        /// </summary>
        public static void Tick()
        {
            if (_borrowed == 0) return;
            if (player_teleport.Position_actuelle_du_player != _proxy) { _borrowed = 0; return; }

            if (_real != null) player_teleport.Position_actuelle_du_player = _real;
            _borrowed = 0;
            Plugin.Log.LogWarning("Player facing proxy was left installed: handed back.");
        }

        private static Transform Ensure()
        {
            if (_proxy != null) return _proxy;

            // A ROOT object: the game reads localRotation in places and treats it as a world
            // one, which only holds because the player itself is a root. Parenting ours
            // anywhere would quietly change what those reads mean.
            var go = GameObject.Find(ProxyName);
            if (go == null) go = new GameObject(ProxyName);
            _proxy = go.transform;
            _proxy.SetParent(null, true);
            return _proxy;
        }
    }
}
