using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Maps the game's actions onto the VR controller inputs.
    ///
    /// The game only ever reads "any joystick", which MERGES the two hands: a left-hand
    /// button and its right-hand counterpart report the same index, so acting with one hand
    /// triggered the action bound to the other. We therefore read the PER-DEVICE key codes,
    /// and above all we REPLACE the original read rather than adding to it — otherwise the
    /// confusion remains.
    ///
    /// Device indices are not fixed — plugging in an Xbox pad takes the first slot — so we
    /// resolve them by the NAME Unity reports.
    /// </summary>
    internal static class VrBindings
    {
        /// <summary>One input: which hand, which button index.</summary>
        internal struct Key
        {
            public bool Left;
            public int Button;

            /// <summary>
            /// Axis name, for ANALOG inputs. The Touch grips are among them: Unity does not
            /// expose them as buttons, so no button read could ever see them.
            /// </summary>
            public string Axis;

            /// <summary>
            /// Which way the axis has to move: 0 for either (a grip, a trigger — travel with
            /// no direction to it), +1 or -1 for a stick, where up and down are two separate
            /// inputs on one axis.
            /// </summary>
            public int Sign;

            public Key(bool left, int button)
            {
                Left = left; Button = button; Axis = null; Sign = 0;
            }

            public Key(string axis) { Left = false; Button = -1; Axis = axis; Sign = 0; }

            public Key(string axis, int sign)
            {
                Left = false; Button = -1; Axis = axis; Sign = sign;
            }

            /// <summary>Cache key: the same axis read two ways must not share an entry.</summary>
            public string AxisKey
            {
                get { return (Sign > 0 ? "+" : Sign < 0 ? "-" : "") + Axis; }
            }

            public bool Valid { get { return Button >= 0 || !string.IsNullOrEmpty(Axis); } }

            public override string ToString()
            {
                if (!string.IsNullOrEmpty(Axis))
                    return (Sign > 0 ? "AX+:" : Sign < 0 ? "AX-:" : "AX:") + Axis;
                return Button < 0 ? "-" : (Left ? "L:" : "R:") + Button;
            }

            public static Key Parse(string s)
            {
                if (string.IsNullOrEmpty(s)) return new Key(false, -1);
                var t = s.Trim();
                int i = t.IndexOf(':');
                if (i <= 0) return new Key(false, -1);

                var prefix = t.Substring(0, i);
                var rest = t.Substring(i + 1);

                if (prefix.Equals("AX", System.StringComparison.OrdinalIgnoreCase))
                    return new Key(rest);
                if (prefix.Equals("AX+", System.StringComparison.OrdinalIgnoreCase))
                    return new Key(rest, 1);
                if (prefix.Equals("AX-", System.StringComparison.OrdinalIgnoreCase))
                    return new Key(rest, -1);

                int b;
                if (!int.TryParse(rest, out b)) return new Key(false, -1);
                return new Key(prefix[0] == 'L' || prefix[0] == 'l', b);
            }
        }

        /// <summary>Rebindable actions.</summary>
        internal enum Action
        {
            Attack, Guard, Jump, Run, SwitchWeapon,
            Grenade, Map, GameMenu, NextTab, Cancel,
            VrSettings, ShowHud, PrevWeapon
        }

        internal static readonly string[] Labels =
        {
            "Attack (right trigger)", "Guard (right grip)", "Jump / confirm (B)",
            "Run (left stick click)",
            "Next weapon (right stick down)", "Grenade (left grip)", "Diary (Y)",
            "Pause menu (right stick click)", "Next tab (right trigger)",
            "Cancel (right stick click)",
            "VR settings (both stick clicks)", "Show HUD (left trigger)",
            "Previous weapon (right stick up)"
        };

        /// <summary>
        /// Indices measured with the controller probe. Only eight physical inputs respond.
        ///
        /// Face buttons: B and Y are the ones that report, at indices 0 and 2 — not A and X.
        /// Unity's OpenVR mapping was designed around the Vive wand, which has no A button
        /// but does have a menu button: Unity therefore exposes ApplicationMenu_Press, where
        /// the Touch binding wires B and Y, and leaves A_Press — where A and X are wired —
        /// with no slot at all.
        ///
        /// Grips: SteamVR declares them in "trigger" mode, so they are analog, read through
        /// axes 10 and 11. We first wrote them as "button OR axis" out of caution — wrongly:
        /// buttons 16 and 17 are the capacitive TOUCH, not the click. Merely resting a
        /// finger on the grip fired them, short-circuiting the axis threshold entirely.
        /// </summary>
        private static readonly string[] Defaults =
        {
            "R:15",             // Attack        right trigger
            "AX:AwayVR_GripR",  // Guard         right grip
            "R:0",              // Jump/confirm  right face button
            "L:8",              // Run           left stick click
            "AX-:RightStickOnlyY",  // SwitchWeapon  right stick down -> next weapon
            "AX:AwayVR_GripL",  // Grenade       left grip
            "L:2",              // Map           left face button -> "MAP"
            "R:9",              // GameMenu      right stick click -> "Cancel", in-game pause
            "R:15",             // NextTab       right trigger (menus only)
            "R:9",              // Cancel        same command as GameMenu (see Remap)
            "L:8+R:9",          // VrSettings    both stick clicks

            // The HUD is on the left TRIGGER, and the right grip no longer raises it: guard
            // is held for long stretches of a fight, and a panel that hangs in front of you
            // the whole time you are blocking is worse than no panel at all.
            "AX:LeftTrigg_sensibility_Attack",  // ShowHud

            // Both weapon directions share one stick axis, told apart by sign. The stick
            // rests near zero and only a deliberate push reaches nine tenths of travel, so
            // neither direction can be brushed by accident.
            "AX+:RightStickOnlyY"   // PrevWeapon    right stick up
        };

        private static BepInEx.Configuration.ConfigEntry<string>[] _entries;

        public static void Init(BepInEx.Configuration.ConfigFile config)
        {
            var n = Defaults.Length;
            _entries = new BepInEx.Configuration.ConfigEntry<string>[n];
            for (int i = 0; i < n; i++)
                _entries[i] = config.Bind("05 - VR bindings", ((Action)i).ToString(), Defaults[i],
                    "VR input. Format L:<button> or R:<button>. Set from the in-game menu.");
        }

        public static void Set(Action a, Key t) { _entries[(int)a].Value = t.ToString(); }
        public static string Text(Action a) { return _entries[(int)a].Value; }

        /// <summary>
        /// A binding reads on two levels: variants separated by '|', of which only ONE needs
        /// to be satisfied, each made of keys joined by '+' that must all be held.
        ///
        /// The AND form is for when no free input is left: the VR settings take both stick
        /// clicks, which are already assigned individually to run and to the game menu.
        ///
        /// The OR form is for inputs whose exact shape on the Unity side is unknown. Grips
        /// are analog yet also expose a click at the end of their travel: writing them as
        /// "R:17|AX:AwayVR_GripR" makes them respond either way, without depending on a
        /// guess about the mapping table.
        /// </summary>
        public static Key[][] Variants(Action a)
        {
            var raw = _entries[(int)a].Value ?? "";
            var res = new System.Collections.Generic.List<Key[]>();

            foreach (var group in raw.Split('|'))
            {
                var list = new System.Collections.Generic.List<Key>();
                foreach (var p in group.Split('+'))
                {
                    var t = Key.Parse(p);
                    if (t.Valid) list.Add(t);
                }
                if (list.Count > 0) res.Add(list.ToArray());
            }
            return res.ToArray();
        }

        /// <summary>First variant, for display and capture.</summary>
        public static Key[] Get(Action a)
        {
            var v = Variants(a);
            return v.Length > 0 ? v[0] : new Key[0];
        }

        // ------------------------------------------------------------------
        // Device resolution
        // ------------------------------------------------------------------

        private static int _joyLeft = -1;
        private static int _joyRight = -1;
        private static float _nextResolve;

        private static void Resolve()
        {
            if (Time.unscaledTime < _nextResolve) return;
            _nextResolve = Time.unscaledTime + 2f;

            int left = -1, right = -1;
            var names = Input.GetJoystickNames();
            for (int i = 0; i < names.Length; i++)
            {
                var n = names[i];
                if (string.IsNullOrEmpty(n)) continue;
                if (n.IndexOf("OpenVR", System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                if (n.IndexOf("Left", System.StringComparison.OrdinalIgnoreCase) >= 0) left = i + 1;
                else if (n.IndexOf("Right", System.StringComparison.OrdinalIgnoreCase) >= 0) right = i + 1;
            }

            if (left != _joyLeft || right != _joyRight)
            {
                _joyLeft = left;
                _joyRight = right;
                Plugin.Log.LogInfo("VR controllers: left = joystick " + left
                                   + ", right = joystick " + right);
            }
        }

        /// <summary>Joystick1Button0 is 350, then one block of 20 codes per device.</summary>
        public static KeyCode Code(bool left, int button)
        {
            int joy = left ? _joyLeft : _joyRight;
            if (joy < 1 || joy > 8 || button < 0 || button > 19) return KeyCode.None;
            return KeyCode.Joystick1Button0 + (joy - 1) * 20 + button;
        }

        /// <summary>Adjustable: analog grips vary in travel from one controller to the next,
        /// and a fixed threshold leaves them either inert or sticky.</summary>
        private static float AxisThreshold
        {
            get { return Plugin.CfgAxisThreshold != null ? Plugin.CfgAxisThreshold.Value : 0.55f; }
        }

        /// <summary>
        /// Sticks want a far higher threshold than grips. A stick is pushed in a direction on
        /// purpose and returns to centre on its own; a grip is squeezed progressively. Sharing
        /// one threshold would either make the sticks trip on a glance or the grips unusable.
        /// </summary>
        private static float StickThreshold
        {
            get { return Plugin.CfgStickThreshold != null ? Plugin.CfgStickThreshold.Value : 0.9f; }
        }

        private static readonly System.Collections.Generic.Dictionary<string, bool> AxisState =
            new System.Collections.Generic.Dictionary<string, bool>();
        private static readonly System.Collections.Generic.Dictionary<string, bool> AxisPressed =
            new System.Collections.Generic.Dictionary<string, bool>();
        private static readonly System.Collections.Generic.Dictionary<string, bool> AxisReleased =
            new System.Collections.Generic.Dictionary<string, bool>();
        private static readonly System.Collections.Generic.Dictionary<string, int> AxisFrame =
            new System.Collections.Generic.Dictionary<string, int>();

        /// <summary>
        /// Boolean state of an axis, remembering the edge. We only update once per frame:
        /// several of the game's scripts poll the same action, and without this the first
        /// call would consume the edge on behalf of all the others.
        /// </summary>
        private static void UpdateAxis(string name, int sign, string key,
                                       out bool active, out bool pressed, out bool released)
        {
            bool before;
            if (!AxisState.TryGetValue(key, out before)) before = false;

            int last;
            if (AxisFrame.TryGetValue(key, out last) && last == Time.frameCount)
            {
                // Already evaluated this frame: report the SAME edge to everyone. Consuming
                // it on the first call would deny the event to the scripts that follow,
                // several of which read the same action.
                active = before;
                AxisPressed.TryGetValue(key, out pressed);
                AxisReleased.TryGetValue(key, out released);
                return;
            }

            float v;
            try { v = Input.GetAxisRaw(name); }
            catch { active = false; pressed = false; released = false; return; }

            if (sign > 0) active = v >= StickThreshold;
            else if (sign < 0) active = v <= -StickThreshold;
            else active = Mathf.Abs(v) >= AxisThreshold;

            pressed = active && !before;
            released = !active && before;

            AxisState[key] = active;
            AxisPressed[key] = pressed;
            AxisReleased[key] = released;
            AxisFrame[key] = Time.frameCount;
        }

        public static bool Held(Key t)
        {
            if (!string.IsNullOrEmpty(t.Axis))
            {
                bool active, pressed, released;
                UpdateAxis(t.Axis, t.Sign, t.AxisKey, out active, out pressed, out released);
                return active;
            }
            Resolve();
            var c = Code(t.Left, t.Button);
            return c != KeyCode.None && Input.GetKey(c);
        }

        public static bool Down(Key t)
        {
            if (!string.IsNullOrEmpty(t.Axis))
            {
                bool active, pressed, released;
                UpdateAxis(t.Axis, t.Sign, t.AxisKey, out active, out pressed, out released);
                return pressed;
            }
            Resolve();
            var c = Code(t.Left, t.Button);
            return c != KeyCode.None && Input.GetKeyDown(c);
        }

        public static bool Up(Key t)
        {
            if (!string.IsNullOrEmpty(t.Axis))
            {
                // This used to return false unconditionally, so an axis binding could NEVER
                // report a release. Slots_Handler reads "Skip_Up" with GetButtonUp, which
                // left weapon switching on the grip completely mute.
                bool active, pressed, released;
                UpdateAxis(t.Axis, t.Sign, t.AxisKey, out active, out pressed, out released);
                return released;
            }
            Resolve();
            var c = Code(t.Left, t.Button);
            return c != KeyCode.None && Input.GetKeyUp(c);
        }

        private static readonly System.Collections.Generic.Dictionary<string, bool> ComboState =
            new System.Collections.Generic.Dictionary<string, bool>();
        private static readonly System.Collections.Generic.Dictionary<string, bool> ComboPressed =
            new System.Collections.Generic.Dictionary<string, bool>();
        private static readonly System.Collections.Generic.Dictionary<string, int> ComboFrame =
            new System.Collections.Generic.Dictionary<string, int>();

        private static bool AllHeld(Key[] b)
        {
            if (b == null || b.Length == 0) return false;
            for (int i = 0; i < b.Length; i++)
                if (!Held(b[i])) return false;
            return true;
        }

        /// <summary>True as soon as one variant of the binding is fully held.</summary>
        private static bool AnyVariantHeld(Action a)
        {
            var v = Variants(a);
            for (int i = 0; i < v.Length; i++)
                if (AllHeld(v[i])) return true;
            return false;
        }

        /// <summary>Edge of the whole binding, evaluated once per frame.</summary>
        private static bool Pressed(Action a)
        {
            var key = a.ToString();
            int last;
            if (ComboFrame.TryGetValue(key, out last) && last == Time.frameCount)
            {
                bool f;
                ComboPressed.TryGetValue(key, out f);
                return f;
            }

            bool before;
            if (!ComboState.TryGetValue(key, out before)) before = false;

            bool held = AnyVariantHeld(a);
            bool pressed = held && !before;

            ComboState[key] = held;
            ComboPressed[key] = pressed;
            ComboFrame[key] = Time.frameCount;
            Trace(a, held);
            return pressed;
        }

        /// <summary>
        /// True if a combo currently held contains this binding. Without this, clicking both
        /// sticks to open the settings would also trigger run and the game menu, each of
        /// which is assigned to a single click.
        /// </summary>
        private static bool Eclipsed(Action a)
        {
            var mine = Variants(a);

            for (int i = 0; i < _entries.Length; i++)
            {
                if (i == (int)a) continue;
                foreach (var other in Variants((Action)i))
                {
                    // Only a combo can eclipse, and only while fully held.
                    if (other.Length < 2 || !AllHeld(other)) continue;

                    foreach (var own in mine)
                    {
                        if (own.Length != 1) continue;
                        for (int j = 0; j < other.Length; j++)
                            if (Same(other[j], own[0])) return true;
                    }
                }
            }
            return false;
        }

        private static bool Same(Key a, Key b)
        {
            if (!string.IsNullOrEmpty(a.Axis) || !string.IsNullOrEmpty(b.Axis))
                return a.Axis == b.Axis;
            return a.Left == b.Left && a.Button == b.Button;
        }

        private static readonly System.Collections.Generic.Dictionary<string, bool> TraceState =
            new System.Collections.Generic.Dictionary<string, bool>();

        /// <summary>
        /// Logs an action's state changes. Without this there is no telling whether a mute
        /// command comes from an input that never reports, or from the game ignoring the
        /// action — two causes calling for opposite fixes.
        /// </summary>
        private static void Trace(Action a, bool held)
        {
            if (Plugin.CfgTraceInput == null || !Plugin.CfgTraceInput.Value) return;

            var key = a.ToString();
            bool before;
            if (TraceState.TryGetValue(key, out before) && before == held) return;
            TraceState[key] = held;
            if (held) Plugin.Log.LogInfo("[input] " + a + "   binding=" + Text(a));
        }

        public static bool Held(Action a)
        {
            if (Eclipsed(a)) return false;
            bool held = AnyVariantHeld(a);
            Trace(a, held);
            return held;
        }

        public static bool Down(Action a)
        {
            if (Eclipsed(a)) return false;
            return Pressed(a);
        }

        public static bool Up(Action a)
        {
            foreach (var v in Variants(a))
                if (v.Length == 1 && Up(v[0])) return true;
            return false;
        }

        /// <summary>Sweeps every device: used by the guided capture.</summary>
        public static bool FirstKeyPressed(out Key t)
        {
            Resolve();
            for (int side = 0; side < 2; side++)
            {
                bool left = side == 0;
                for (int b = 0; b < 20; b++)
                {
                    var c = Code(left, b);
                    if (c != KeyCode.None && Input.GetKeyDown(c))
                    {
                        t = new Key(left, b);
                        return true;
                    }
                }
            }
            // The grips and triggers are analog: without this axis sweep they could not be
            // captured, and would stay unassignable.
            foreach (var axisName in new[] { "AwayVR_GripL", "AwayVR_GripR",
                                             "LeftTrigg_sensibility_Attack",
                                             "RightTrigg_sensibility_Attack" })
            {
                float v;
                try { v = Input.GetAxisRaw(axisName); }
                catch { continue; }
                if (Mathf.Abs(v) >= AxisThreshold)
                {
                    t = new Key(axisName);
                    return true;
                }
            }

            // Sticks, captured with their direction: one axis, two bindings.
            foreach (var axisName in new[] { "RightStickOnlyY", "RightStickOnlyX" })
            {
                float v;
                try { v = Input.GetAxisRaw(axisName); }
                catch { continue; }
                if (v >= StickThreshold) { t = new Key(axisName, 1); return true; }
                if (v <= -StickThreshold) { t = new Key(axisName, -1); return true; }
            }

            t = new Key(false, -1);
            return false;
        }

        // ------------------------------------------------------------------
        // Mapping onto the game's named InputManager buttons
        // ------------------------------------------------------------------

        /// <summary>
        /// VR action bound to one of the game's named buttons. Returns false when the name
        /// is not remapped, in which case the original read is left untouched.
        /// </summary>
        public static bool Remap(string buttonName, out Action a)
        {
            switch (buttonName)
            {
                case "Grenades": a = Action.Grenade; return true;
                case "MAP": a = Action.Map; return true;
                case "Skip_Up": a = Action.SwitchWeapon; return true;
                case "Skip_Down": a = Action.PrevWeapon; return true;
                case "Fire3": a = Action.Run; return true;
                case "Jump": a = Action.Jump; return true;
                case "Submit": a = Action.Jump; return true;

                // ShowPanels, which tells Escape (open) apart from Cancel (close), exists
                // ONLY in the menu scenes: measured, its instance is absent during play.
                // While playing, the one and only pause command is Cancel, read by the Pause
                // class. So Cancel is what carries "the menu", and cancelling is the same
                // command — the game does not distinguish them.
                case "Cancel": a = Action.GameMenu; return true;

                // Map tabs. InGameMenu does NOT go through InputAction.NextTab, which only
                // serves QuickMenu: it reads these two named buttons. Without this line,
                // changing page once the map is open is impossible.
                case "NavigationNext": a = Action.NextTab; return true;

                default: a = Action.Attack; return false;
            }
        }
    }
}
