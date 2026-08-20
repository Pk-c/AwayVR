using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Writes down what a weapon is actually doing, so a "nothing happens" can be read instead
    /// of guessed at.
    ///
    /// A trigger that fires nothing has three possible causes and they look identical in a
    /// headset: the press never reaches the game, the weapon script is not the one you think is
    /// running, or the weapon has a condition you cannot see - the robot fires only with a
    /// locked target. This prints all three, plus what the aiming ray meets, and only when the
    /// answer changes, so the log stays readable.
    /// </summary>
    internal static class CombatProbe
    {
        private static readonly FieldInfo FTracking =
            AccessTools.Field(typeof(TargetLockSystem), "tracking_");

        private static float _next;
        private static string _dernier;

        public static void Tick()
        {
            if (!VrManager.VrActive || !Plugin.CfgVerbose.Value) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.5f;

            string cle;
            var ligne = Describe(out cle);
            // Compared on the STABLE half only: a distance and an age change on every sample,
            // and comparing the whole line would print one every half second forever.
            if (ligne == null || cle == _dernier) return;
            _dernier = cle;
            Plugin.Log.LogInfo(ligne);
        }

        /// <summary>Same text, for the scene report.</summary>
        public static void Dump(StringBuilder sb)
        {
            string cle;
            var ligne = Describe(out cle);
            if (ligne == null) return;
            sb.AppendLine("-- Combat probe --");
            sb.AppendLine("  " + ligne.Substring("combat: ".Length));
        }

        /// <summary>
        /// Builds the line, and alongside it the key that decides whether it is worth printing:
        /// the same facts minus everything that drifts on its own.
        /// </summary>
        private static string Describe(out string cle)
        {
            cle = null;
            var cam = VrManager.MainCamera;
            if (cam == null) return null;

            var sb = new StringBuilder("combat: ");
            var k = new StringBuilder();

            string perso = "?";
            try { perso = Slots_Handler.active_char; }
            catch { }
            sb.Append("char=").Append(perso);
            k.Append(perso);

            var armes = ArmesActives();
            sb.Append("  weapons=[").Append(armes).Append(']');
            k.Append('|').Append(armes);

            // Does the press reach the game at all? The action is an EDGE, so it is true for a
            // single frame and a probe would practically never land on it: InputPatches notes
            // the moment instead.
            float depuis = Patches.InputPatches.SinceAttack;
            sb.Append("  attack=").Append(depuis < 0f ? "never" : depuis.ToString("0.0") + "s ago");
            // Only "has it ever fired" is stable; the age itself moves every sample.
            k.Append('|').Append(depuis < 0f ? "noattack" : "attack");

            var sys = TargetLockSystem.Get();
            if (sys == null)
            {
                sb.Append("  lock=<no system in scene>");
                k.Append("|nolock");
                cle = k.ToString();
                return sb.ToString();
            }

            bool tracking = FTracking != null && (bool)FTracking.GetValue(sys);
            sb.Append("  lock: tracking=").Append(tracking)
              .Append(" targets=").Append(Patches.TargetLockPatches.Count(sys))
              .Append(" range=").Append(sys.range.ToString("0"))
              .Append(" cible=").Append(Masque(sys.targetLayers.value))
              .Append(" obstacle=").Append(Masque(sys.obstructorLayers.value));
            k.Append('|').Append(tracking).Append('|').Append(Patches.TargetLockPatches.Count(sys));

            // The acquisition ray, repeated here exactly as the game casts it.
            var origine = cam.transform.position;
            var sens = cam.transform.forward;
            RaycastHit hit;

            sb.Append("  ray=");
            if (Physics.Raycast(origine, sens, out hit, sys.range,
                                sys.targetLayers.value | sys.obstructorLayers.value,
                                QueryTriggerInteraction.Ignore))
            {
                sb.Append(Touche(hit));
                k.Append('|').Append(hit.collider.name).Append(hit.collider.gameObject.layer);
            }
            else
            {
                sb.Append("<nothing>");
                k.Append("|noray");
            }

            // And with no mask at all: what is in front of you, whatever its layer. A target
            // that shows up here but not above is on a layer the system does not accept.
            sb.Append("  free=");
            if (Physics.Raycast(origine, sens, out hit, sys.range, ~0, QueryTriggerInteraction.Ignore))
            {
                sb.Append(Touche(hit));
                k.Append('|').Append(hit.collider.name).Append(hit.collider.gameObject.layer);
            }
            else
            {
                sb.Append("<nothing>");
                k.Append("|nofree");
            }

            cle = k.ToString();
            return sb.ToString();
        }

        private static string Touche(RaycastHit hit)
        {
            int l = hit.collider.gameObject.layer;
            return hit.collider.name + " layer=" + l + " '" + LayerMask.LayerToName(l) + "'"
                   + " d=" + hit.distance.ToString("0.0");
        }

        private static string Masque(int masque)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 32; i++)
            {
                if ((masque & (1 << i)) == 0) continue;
                if (sb.Length > 0) sb.Append('|');
                var n = LayerMask.LayerToName(i);
                sb.Append(string.IsNullOrEmpty(n) ? i.ToString() : n);
            }
            return sb.Length > 0 ? sb.ToString() : "<none>";
        }

        /// <summary>
        /// Weapon behaviours alive under the viewmodel. Which one is running decides everything
        /// else, and it is not always the one the character's name suggests.
        /// </summary>
        private static string ArmesActives()
        {
            var root = Weapons.Root;
            if (root == null) return "<no viewmodel>";

            var sb = new StringBuilder();
            foreach (var m in root.GetComponentsInChildren<MonoBehaviour>(false))
            {
                if (m == null || !m.enabled) continue;
                var n = m.GetType().Name;
                if (n.IndexOf("eapon", System.StringComparison.Ordinal) < 0
                    && n.IndexOf("issile", System.StringComparison.Ordinal) < 0) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append(n);
            }
            return sb.Length > 0 ? sb.ToString() : "<none active>";
        }
    }
}
