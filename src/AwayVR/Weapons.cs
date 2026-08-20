using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Attaches the viewmodel to the tracked hand. Two traps specific to this game:
    ///
    ///  - the models live under "Hide_W_y_n", not under Weapons_Camera;
    ///  - an Animator animates Hide_W_y_n's local POSITION to holster the weapon, so it
    ///    overwrites any position offset every frame while rotation works fine.
    ///
    /// Hence the intermediate anchor: we write onto it, the game keeps the weapon's own.
    /// </summary>
    internal static class Weapons
    {
        private const string AnchorName = "AwayVR_WeaponAnchor";

        private static Transform _root;
        private static Transform _anchor;

        private static Transform _origParent;
        private static Vector3 _origPos;
        private static Quaternion _origRot;
        private static Vector3 _origScale;
        private static bool _captured;

        /// <summary>
        /// The holder's scale as it was the FIRST time we ever saw one, kept for the whole
        /// session. _origScale cannot serve: Forget() runs on every scene load and re-captures it
        /// from the new scene's holder - which is the very value that differs.
        /// </summary>
        private static Vector3 _referenceScale = Vector3.one;
        private static bool _hasReference;

        /// <summary>Model point to place in the hand, in the anchor's local space.</summary>
        private static Vector3 _gripLocal;
        private static string _signature;

        private static float _nextCheck;

        public static Transform Root { get { return _root; } }

        // ------------------------------------------------------------------

        /// <summary>
        /// Detects a brand-new weapon holder and releases the old one. The game rebuilds that
        /// node without destroying the previous one - the lever cutscenes do it on return -
        /// leaving ours on the hand while the new one draws in its normal place: two arms.
        /// </summary>
        private static bool _holderChecked;

        private static void ReleaseStaleHolder(bool log)
        {
            if (_root == null || _anchor == null) return;

            // A new holder is built when a cutscene or a dialogue hands control back, and at
            // no other time. InputM is the game's own notion of "the player is not driving".
            // Scanning every Transform on a timer cost ten thousand string allocations a
            // second, since Transform.name allocates on every read.
            if (_holderChecked && !GameState.ControlRegained) return;
            _holderChecked = true;

            foreach (var c in Object.FindObjectsOfType<weapon_position>())
            {
                if (c == null) continue;
                if (ReleaseIfReplaced(c.transform, log)) return;
            }
            foreach (var c in Object.FindObjectsOfType<weapons_sway>())
            {
                if (c == null) continue;
                if (ReleaseIfReplaced(c.transform, log)) return;
            }
        }

        /// <summary>
        /// Walks up from a weapon script to its 'Hide_W_y_n' holder. True when that holder is
        /// a NEW one - neither ours nor anything under our anchor - in which case ours is
        /// handed back before the two draw at once.
        /// </summary>
        private static bool ReleaseIfReplaced(Transform from, bool log)
        {
            Transform holder = null;
            for (var p = from; p != null; p = p.parent)
            {
                if (p == _root) return false;          // ours, nothing to do
                if (p == _anchor) return false;        // under our anchor, likewise
                if (p.name == "Hide_W_y_n") { holder = p; break; }
            }
            if (holder == null || holder == _root) return false;
            if (IsUnder(holder, _anchor)) return false;

            if (log || Plugin.CfgVerbose.Value)
                Plugin.Log.LogInfo("New weapon holder detected: releasing the old one.");
            Restore(false);
            Forget();
            return true;
        }

        private static bool IsUnder(Transform t, Transform parent)
        {
            for (var p = t; p != null; p = p.parent)
                if (p == parent) return true;
            return false;
        }

        private static Transform FindRoot()
        {
            if (_root != null) return _root;

            var go = GameObject.Find("Hide_W_y_n");
            if (go == null) go = GameObject.Find("Weapons_Camera");
            if (go == null) return null;

            _root = go.transform;
            if (!_captured)
            {
                _origParent = _root.parent;
                _origPos = _root.localPosition;
                _origRot = _root.localRotation;
                _origScale = _root.localScale;
                _captured = true;

                if (!_hasReference)
                {
                    _referenceScale = _root.localScale;
                    _hasReference = true;
                    Plugin.Log.LogInfo("Weapon holder reference scale: "
                                       + _referenceScale.ToString("0.000"));
                }
            }
            return _root;
        }

        private static Transform EnsureAnchor(Transform hand)
        {
            if (_anchor != null && _anchor.parent == hand) return _anchor;

            var existing = hand.Find(AnchorName);
            if (existing != null) { _anchor = existing; return _anchor; }

            var go = new GameObject(AnchorName);
            _anchor = go.transform;
            _anchor.SetParent(hand, false);
            return _anchor;
        }

        // ------------------------------------------------------------------

        public static void Apply(WeaponAttachMode mode, bool log)
        {
            ReleaseStaleHolder(log);

            var root = FindRoot();
            if (root == null) return;

            if (mode == WeaponAttachMode.Off)
            {
                Restore(log);
                return;
            }

            // A weapons camera still active would become a controller-driven camera, so we
            // insist it be merged or switched off.
            var wcam = root.GetComponent<Camera>();
            if (wcam != null && wcam.enabled)
            {
                if (log)
                    Plugin.Log.LogWarning("WeaponsCameraMode=Keep is incompatible with attaching to the hand: "
                                          + "switch to Merge.");
                return;
            }

            var hand = Hands.Get(mode == WeaponAttachMode.Left ? HandSide.Left : HandSide.Right);
            if (hand == null) return;

            var anchor = EnsureAnchor(hand);
            NeutralizeTransformScripts(root, log);

            if (root.parent != anchor)
            {
                root.SetParent(anchor, false);
                _signature = null;
                if (log) Plugin.Log.LogInfo("Viewmodel attached to " + hand.name);
            }

            // Model pose zeroed inside the anchor. The holstering Animator writes a
            // translation that differs depending on the state at the moment we attach it: on
            // respawn, on a map change or after a death, the model arrived in a different
            // pose and the measured grip point no longer matched. Pinning it makes the
            // settings valid once and for all.
            Fixer(root);

            RefreshAutoOffset(hand, log);
            Pose();
        }

        /// <summary>
        /// Cancels the translation the Animator writes on the viewmodel root. Call this in
        /// LateUpdate: the animation is evaluated between Update and LateUpdate, so that is
        /// the only moment when we run after it.
        /// </summary>
        public static void Fixer()
        {
            if (_root != null && _root.parent == _anchor) Fixer(_root);
        }

        /// <summary>
        /// Undoes what the Animator writes onto the weapon holder every frame.
        ///
        /// SCALE belongs here as much as position and rotation, and leaving it out was a bug: it
        /// multiplies straight into the final size, so the same weapon came back smaller after a
        /// scene change - a boomstick carried into a dungeon shrank, and a weapon too small stops
        /// reaching anything. The reference is the scale the holder had the FIRST time we ever saw
        /// it, not the current scene's, or each scene would pin its own value and the size would
        /// still move.
        /// </summary>
        private static void Fixer(Transform root)
        {
            if (root.localPosition != Vector3.zero) root.localPosition = Vector3.zero;
            if (root.localRotation != Quaternion.identity) root.localRotation = Quaternion.identity;

            if (_hasReference && root.localScale != _referenceScale)
            {
                if (Plugin.CfgVerbose.Value)
                    Plugin.Log.LogInfo("Weapon holder scale drifted: " + root.localScale.ToString("0.000")
                                       + " -> " + _referenceScale.ToString("0.000"));
                root.localScale = _referenceScale;
            }
        }

        /// <summary>
        /// Anchor pose. Called every frame: the menu settings are visible as you move them,
        /// and the game's Animator can no longer overwrite them.
        /// </summary>
        public static void Pose()
        {
            if (_anchor == null) return;

            // The game holsters weapons through basics.hide_weapons (chests, dialogues,
            // mini-games). It relies on an Animator sliding the weapon out of the camera's
            // frustum; attached to the hand, that translation no longer takes it out of
            // sight. So we disable the anchor, which also suspends weapons_sword and stops
            // you attacking with an empty hand.
            bool cacher = basics.hide_weapons;
            if (_anchor.gameObject.activeSelf == cacher)
                _anchor.gameObject.SetActive(!cacher);
            if (cacher) return;

            float scale = Plugin.CfgWeaponScale.Value;
            _anchor.localScale = new Vector3(scale, scale, scale);
            _anchor.localRotation = Quaternion.identity;

            var position = new Vector3(
                Plugin.CfgWeaponOffX.Value, Plugin.CfgWeaponOffY.Value, Plugin.CfgWeaponOffZ.Value);

            // The offset is expressed in the hand's YAW frame, not in hand space and not in rig
            // space. Hand space rotates it with the wrist and turns it into a lever arm, so
            // moving the weapon moves its centre of rotation too. Rig space fixes it to the
            // play area instead of to you: turn on the spot physically and the offset stays
            // pointing where the room points, so the weapon drifts to the wrong side of your
            // own hand - the one case where the body does not turn with you. Yaw only keeps
            // the wrist out of it while following you around.
            var hand = _anchor.parent;
            var decalageMain = hand != null
                ? Quaternion.Inverse(hand.localRotation) * (FlatYaw(hand.localRotation) * position)
                : position;

            _anchor.localPosition = decalageMain - _gripLocal * scale;
        }

        /// <summary>
        /// Horizontal heading of a rotation, stable at any pitch. Never through eulerAngles.y:
        /// the extraction swings once the transform carries pitch and roll, and a hand held out
        /// in front of you carries both. Pointing straight up or down leaves no forward to
        /// flatten, so the up vector stands in - which is where the back of the hand faces then.
        /// </summary>
        private static Quaternion FlatYaw(Quaternion q)
        {
            var fwd = q * Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f)
            {
                fwd = q * Vector3.up;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 1e-6f) return Quaternion.identity;
            }
            return Quaternion.LookRotation(fwd.normalized, Vector3.up);
        }

        /// <summary>
        /// Fragments to ignore when picking the reference model. The viewmodel mixes the
        /// weapon with full-screen effects, a shield and decorative elements that switch on
        /// and off during play; including them gave a nonsensical grip point, and above all
        /// an unstable one.
        /// </summary>
        private static readonly string[] Decorations =
        {
            "fx", "blink", "sphere", "torus", "shield", "sprite", "particle",
            "glow", "light", "trail", "smoke", "fire", "ember", "flame"
        };

        /// <summary>The largest non-parasitic renderer: that is the weapon model.</summary>
        private static Renderer MainRenderer()
        {
            if (_root == null) return null;

            Renderer best = null;
            float meilleurVolume = -1f;

            foreach (var r in _root.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.enabled) continue;
                if (r is ParticleSystemRenderer) continue;

                var n = r.name.ToLowerInvariant();
                bool parasite = false;
                foreach (var f in Decorations)
                    if (n.IndexOf(f) >= 0) { parasite = true; break; }
                if (parasite) continue;

                var t = r.bounds.size;
                float vol = t.x * t.y * t.z;
                if (vol <= 0f) continue;
                if (vol > meilleurVolume) { meilleurVolume = vol; best = r; }
            }
            return best;
        }

        private static void RefreshAutoOffset(Transform hand, bool log)
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.1f;

            var principal = MainRenderer();
            if (principal == null) return;

            // The signature no longer depends on the NUMBER of active renderers. Effects
            // and decorations lighting up during play kept changing it, so the grip point
            // was recomputed and the weapon jumped mid-game.
            string sig = principal.name + "|" + Plugin.CfgWeaponAnchor.Value;
            if (sig == _signature) return;

            Bounds local;
            if (!TryRendererBounds(principal, out local)) return;
            _signature = sig;
            // The melee test depends on which weapon is in hand, and this is the one place
            // that knows it has changed.
            Swing.Invalidate();

            switch (Plugin.CfgWeaponAnchor.Value)
            {
                case WeaponAnchorPoint.Pivot:
                    _gripLocal = Vector3.zero;
                    break;

                case WeaponAnchorPoint.Centre:
                    _gripLocal = local.center;
                    break;

                case WeaponAnchorPoint.Base:
                    _gripLocal = SurAxeDominant(local, false);
                    break;

                case WeaponAnchorPoint.Tip:
                    _gripLocal = SurAxeDominant(local, true);
                    break;

                default:
                    // Hand: the grip sits at one end of the model, never in the middle. We
                    // take the end of the longest axis, the only axis that makes sense here:
                    // this game's model extends along X rather than Z, so looking for the
                    // ends along Z produced nothing usable.
                    _gripLocal = SurAxeDominant(local, false);
                    break;
            }

            if (log)
                Plugin.Log.LogInfo("  grip " + Plugin.CfgWeaponAnchor.Value
                                   + " on '" + principal.name + "'"
                                   + ": centre=" + local.center.ToString("0.00")
                                   + " size=" + local.size.ToString("0.00")
                                   + " -> grip=" + _gripLocal.ToString("0.000"));
        }

        /// <summary>
        /// End of the model along its longest axis. The other axes keep the centre value,
        /// so we stay in the middle of the cross-section.
        /// </summary>
        private static Vector3 SurAxeDominant(Bounds b, bool versLeMax)
        {
            var t = b.size;
            var p = b.center;

            if (t.x >= t.y && t.x >= t.z) p.x = versLeMax ? b.max.x : b.min.x;
            else if (t.y >= t.z) p.y = versLeMax ? b.max.y : b.min.y;
            else p.z = versLeMax ? b.max.z : b.min.z;

            return p;
        }

        /// <summary>Bounds of a single renderer, expressed in anchor space.</summary>
        private static bool TryRendererBounds(Renderer r, out Bounds local)
        {
            local = new Bounds();
            if (r == null || _anchor == null) return false;
            return Encapsuler(r, ref local, true);
        }

        /// <summary>Adds the 8 corners of a renderer's world bounds, in anchor space.</summary>
        private static bool Encapsuler(Renderer r, ref Bounds local, bool premier)
        {
            var b = r.bounds;
            var c = b.center;
            var e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                var coin = new Vector3(
                    c.x + ((i & 1) == 0 ? -e.x : e.x),
                    c.y + ((i & 2) == 0 ? -e.y : e.y),
                    c.z + ((i & 4) == 0 ? -e.z : e.z));
                var p = _anchor.InverseTransformPoint(coin);
                if (premier && i == 0) local = new Bounds(p, Vector3.zero);
                else local.Encapsulate(p);
            }
            return true;
        }

        /// <summary>Current held point, shown in the menu so you can tune without guessing.</summary>
        public static string GripInfo()
        {
            return _gripLocal.ToString("0.00");
        }

        /// <summary>Structure of the active viewmodel, to work out where the hand is.</summary>
        public static void Dump(System.Text.StringBuilder sb)
        {
            sb.AppendLine("-- Viewmodel --");
            if (_root == null) { sb.AppendLine("  (none)"); return; }
            sb.AppendLine("  root   : " + Hierarchy.Path(_root));
            sb.AppendLine("  anchor : " + (_anchor != null ? _anchor.name : "<none>")
                          + "  grip=" + _gripLocal.ToString("0.000")
                          + "  mode=" + Plugin.CfgWeaponAnchor.Value);

            foreach (var r in _root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                var b = r.bounds;
                Vector3 enAncre = _anchor != null
                    ? _anchor.InverseTransformPoint(b.center) : b.center;
                // VISIBLE = renderer enabled AND object active in the hierarchy. The dump
                // only reported the component's state, which made holstered weapons look as
                // though they were on screen.
                bool visible = r.enabled && r.gameObject.activeInHierarchy;
                sb.AppendLine(string.Format("  [{0}] {1,-28} size={2}  centre(anchor)={3}",
                    visible ? "SEEN" : "   ", r.name, b.size.ToString("0.00"),
                    enAncre.ToString("0.00")));
            }
        }

        /// <summary>
        /// Everything visible on the player-weapons layer WITHOUT going through our anchor.
        /// The second arm reported by the player is not under our weapon holder - only one
        /// model is visible there - and is not called Hide_W_y_n, or the duplicate detection
        /// would have caught it. So we look for it by what it is, not by its name.
        /// </summary>
        public static void DumpArmesOrphelines(System.Text.StringBuilder sb)
        {
            sb.AppendLine("-- Weapons visible outside our anchor --");
            int n = 0;

            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;

                int layer = r.gameObject.layer;
                string layerName = LayerMask.LayerToName(layer);
                bool isWeapon = layerName == "player_weapons"
                            || Hierarchy.Path(r.transform).IndexOf("Hide_W_y_n",
                                   System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isWeapon) continue;
                if (_anchor != null && IsUnder(r.transform, _anchor)) continue;

                sb.AppendLine("  " + Hierarchy.Path(r.transform)
                              + "   layer=" + layer + " '" + layerName + "'"
                              + "  size=" + r.bounds.size.ToString("0.00"));
                if (++n >= 25) { sb.AppendLine("  ... (truncated)"); break; }
            }

            if (n == 0) sb.AppendLine("  (none)");
        }

        /// <summary>
        /// weapon_position and weapon_reinit_position reapply a WORLD position captured at
        /// start-up, and weapons_sway swings the weapon with the mouse. All three undo the
        /// attachment to the hand.
        /// </summary>
        private static void NeutralizeTransformScripts(Transform root, bool log)
        {
            int n = 0;
            foreach (var c in root.GetComponentsInChildren<weapon_position>(true))
                if (c.enabled) { c.enabled = false; n++; }
            foreach (var c in root.GetComponentsInChildren<weapon_reinit_position>(true))
                if (c.enabled) { c.enabled = false; n++; }
            foreach (var c in root.GetComponentsInChildren<weapons_sway>(true))
                if (c.enabled) { c.enabled = false; n++; }

            if (n > 0 && log)
                Plugin.Log.LogInfo("  " + n + " weapon repositioning script(s) neutralised.");
        }

        public static void Restore(bool log)
        {
            if (!_captured || _root == null) return;
            if (_root.parent != _origParent)
                _root.SetParent(_origParent, false);
            _root.localPosition = _origPos;
            _root.localRotation = _origRot;
            _root.localScale = _origScale;
            _signature = null;
            if (log) Plugin.Log.LogInfo("Viewmodel handed back to the camera.");
        }

        /// <summary>Called on a scene load: everything below has to be rediscovered.</summary>
        public static void OnSceneLoaded()
        {
            _holderChecked = false;
        }

        public static void Forget()
        {
            _root = null;
            _anchor = null;
            _origParent = null;
            _captured = false;
            _signature = null;
            _gripLocal = Vector3.zero;
        }
    }
}

