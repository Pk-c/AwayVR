using System.Collections.Generic;
using UnityEngine;

namespace AwayVR.Menu
{
    /// <summary>
    /// Settings menu, driven entirely by the stick: there is no mouse in VR, therefore no
    /// pointer. Everything applies live, with no need to close the menu.
    /// </summary>
    public class VrMenu : MonoBehaviour
    {
        private const int VisibleRows = 10;

        private readonly MenuUI _ui = new MenuUI();
        private readonly List<MenuItem> _items = new List<MenuItem>();
        private readonly List<RowData> _rows = new List<RowData>();

        private bool _visible;
        private int _selected;
        private int _scrollTop;
        private MenuPage _page = MenuPage.Main;
        /// <summary>Action currently being captured, -1 when none.</summary>
        private int _capture = -1;

        private float _lastToggle;
        private float _sauvegardeA = -1f;
        private bool _controlWasAllowed = true;

        private float _navCooldown;
        private int _navHeldDir;   // repetition verticale
        private bool _hHeld;       // front horizontal, etat distinct

        private const float NavFirstDelay = 0.32f;
        private const float NavRepeatDelay = 0.10f;

        // Sub-page back. Still a merged KeyCode: inside the menu the game's commands are
        // frozen, so no collision is possible.
        private const KeyCode RightStickClick = KeyCode.JoystickButton9;

        // ------------------------------------------------------------------

        private void Start()
        {
            BuildItems();
            _selected = FirstSelectable();
        }

        private void BuildItems()
        {
            _items.Clear();

            // Deliberately a SHORT list. Every setting still exists and is still read; what
            // was taken out of the menu is what gets set once and never touched again â€” the
            // weapon's position in the hand, the grenade's offsets, the arming thresholds.
            // Those live in the config file, where they belong: a menu you scroll through
            // hides the four or five settings that are genuinely worth changing.

            _items.Add(new SectionItem("Controls"));
            _items.Add(new EnumItem("Turn mode", Plugin.CfgTurnMode,
                new[] { "Snap", "Smooth" }));
            _items.Add(new FloatItem("Snap angle", Plugin.CfgSnapAngle,
                5f, 180f, 6f, 90f, 1f, "0", " deg"));
            _items.Add(new FloatItem("Turn speed", Plugin.CfgSmoothTurnSpeed,
                20f, 400f, 12f, 220f, 1f, "0", " deg/s"));

            _items.Add(new SectionItem("Weapon"));
            _items.Add(new FloatItem("Swing threshold", Plugin.CfgSwingThreshold,
                0.3f, 6f, 0.08f, 2.0f, 0.05f, "0.00", " m/s"));
            _items.Add(new BoolItem("Power from motion", Plugin.CfgGrenadePowerFromMotion));
            _items.Add(new FloatItem("Throw power min", Plugin.CfgGrenadePowerMin,
                0.05f, 1.00f, 0.02f, 0.5f, 0.01f, "0.00", "x"));
            _items.Add(new FloatItem("Throw power max", Plugin.CfgGrenadePowerMax,
                1.00f, 5.00f, 0.05f, 1.0f, 0.02f, "0.00", "x"));

            _items.Add(new SectionItem("Graphics"));
            _items.Add(new FloatItem("World scale", Plugin.CfgWorldScale,
                0.30f, 3.00f, 0.06f, 0.8f, 0.01f, "0.00", "x"));
            _items.Add(new BoolItem("Disable colour grading", Plugin.CfgDisableColorGrading));

            // Full-screen effects, one switch each, all live and all reversible. This is a
            // bisection instrument, not a taste panel: the point is to turn exactly one thing
            // off and see what changes, which is the only way left to name the culprit.
            _items.Add(new SectionItem("Effects"));
            _items.Add(new FloatItem("Render scale", Plugin.CfgResolutionScale,
                0.50f, 2.00f, 0.05f, 0.5f, 0.02f, "0.00", "x"));
            _items.Add(new BoolItem("No ambient occlusion", Plugin.CfgDisableOcclusion));
            _items.Add(new BoolItem("No depth of field", Plugin.CfgDisableDepthOfField));
            _items.Add(new BoolItem("No global fog", Plugin.CfgDisableGlobalFog));
            _items.Add(new BoolItem("No blink effect", Plugin.CfgDisableBlink));
            _items.Add(new BoolItem("No temporal AA", Plugin.CfgDisableTemporalAA));
            _items.Add(new BoolItem("Weapons camera off", Plugin.CfgWeaponsCameraOff));
            _items.Add(new BoolItem("Disable bloom", Plugin.CfgDisableBloom));
            _items.Add(new BoolItem("Force anisotropic", Plugin.CfgAnisotropic));
            _items.Add(new CascadeItem("Shadow cascades"));
            _items.Add(new FloatItem("Shadow distance", Plugin.CfgShadowDistance,
                10f, 200f, 2f, 40f, 1f, "0", " m"));
            _items.Add(new FloatItem("LOD bias", Plugin.CfgLodBias,
                0.50f, 10.00f, 0.10f, 1.0f, 0.05f, "0.0", "x"));
            _items.Add(new LayerBisectItem("Hide layer"));
            _items.Add(new RootBisectItem("Hide object"));

            _items.Add(new SectionItem("Interface"));
            _items.Add(new BoolItem("HUD always visible", Plugin.CfgHudAlwaysVisible));
            _items.Add(new FloatItem("HUD size", Plugin.CfgHudWidth,
                0.50f, 6.00f, 0.10f, 1.5f, 0.05f, "0.00", " m"));
            _items.Add(new FloatItem("HUD distance", Plugin.CfgHudDistance,
                0.60f, 6.00f, 0.10f, 1.5f, 0.05f, "0.00", " m"));

            _items.Add(new SectionItem("Player"));
            _items.Add(new FloatItem("Player height", Plugin.CfgHeightOffset,
                -1.20f, 1.20f, 0.05f, 0.6f, 0.01f, "0.00", " m"));

            _items.Add(new SectionItem("System"));
            _items.Add(new BoolItem("FPS counter", Plugin.CfgFpsCounter));
            _items.Add(new ActionItem("Reset all", "left/right", Reinitialiser));
        }

        private int FirstSelectable()
        {
            for (int i = 0; i < _items.Count; i++)
                if (!(_items[i] is SectionItem)) return i;
            return 0;
        }

        // ------------------------------------------------------------------

        private void Update()
        {
            _ui.Ensure(VrManager.Rig);
            // The panel is rebuilt after a scene change: reapply the state.
            _ui.Show(_visible);

            HandleToggle();
            if (!_visible) return;

            // Repositioned every frame: the rig may only appear after the menu is opened,
            // and without this the panel would stay at its fallback scale.
            _ui.PlaceInFront(VrManager.MainCamera, VrManager.Rig,
                             Plugin.CfgMenuDistance.Value,
                             Plugin.CfgMenuWidth.Value,
                             Plugin.CfgMenuVOffset.Value);

            HandleNavigation();
            SauvegarderSiNecessaire();
            Refresh();
        }

        private void HandleToggle()
        {
            // Read PER DEVICE, through an action rather than merged KeyCodes: those mean
            // "any joystick", so a plugged-in Xbox pad would open the menu. More
            // importantly, going through an action lets Eclipsed neutralise Run and the
            // game menu, each assigned to one of the two clicks on its own: without it,
            // opening the settings would also trigger the game menu.
            bool edge = VrBindings.Down(VrBindings.Action.VrSettings);

            bool key = Input.GetKeyDown(Plugin.CfgMenuKey.Value);
            if (!edge && !key) return;
            if (Time.unscaledTime - _lastToggle < 0.4f) return;

            _lastToggle = Time.unscaledTime;
            SetVisible(!_visible);
        }

        private void SetVisible(bool visible)
        {
            if (visible == _visible) return;
            _visible = visible;

            if (visible)
            {
                _page = MenuPage.Main;
                _selected = FirstSelectable();
                _scrollTop = 0;

                // Freeze the controls: otherwise the stick also walks the player around.
                // We remember the current state, which a cutscene may already have locked.
                _controlWasAllowed = InputM.IsPlayerControlAllowed();
                InputM.AllowPlayerControl(false);

                _ui.Show(true);
                _ui.PlaceInFront(VrManager.MainCamera, VrManager.Rig,
                                 Plugin.CfgMenuDistance.Value,
                                 Plugin.CfgMenuWidth.Value,
                                 Plugin.CfgMenuVOffset.Value);
                Refresh();
            }
            else
            {
                _ui.Show(false);
                InputM.AllowPlayerControl(_controlWasAllowed);
                Plugin.Instance.Config.Save();

                var mgr = VrManager.Instance;
                if (mgr != null) mgr.ReapplyScene();
            }
        }

        // ------------------------------------------------------------------
        // Navigation
        // ------------------------------------------------------------------

        private int PageCount()
        {
            switch (_page)
            {
                case MenuPage.Bindings: return VrBindings.Labels.Length;
                default: return _items.Count;
            }
        }

        private void HandleNavigation()
        {
            // During a capture every pressed input is recorded, so navigation has to be
            // suspended: otherwise the stick would serve two purposes at once.
            if (_capture >= 0)
            {
                VrBindings.Key t;
                if (VrBindings.FirstKeyPressed(out t))
                {
                    VrBindings.Set((VrBindings.Action)_capture, t);
                    Plugin.Log.LogInfo("Binding " + (VrBindings.Action)_capture + " = " + t);
                    _capture = -1;
                    MarquerModifie();
                }
                return;
            }

            float v = Input.GetAxisRaw("Vertical");
            float h = Input.GetAxisRaw("Horizontal");

            if (Input.GetKey(KeyCode.UpArrow)) v = 1f;
            if (Input.GetKey(KeyCode.DownArrow)) v = -1f;
            if (Input.GetKey(KeyCode.LeftArrow)) h = -1f;
            if (Input.GetKey(KeyCode.RightArrow)) h = 1f;

            if (PageCount() == 0) return;

            // Vertical movement stays stepped: you pick a row, you do not dial it in.
            // The vertical dead zone is deliberately large: while adjusting a value with
            // the stick it is easy to drift up or down, which used to change row in the
            // middle of an adjustment.
            int vdir = 0;
            if (Mathf.Abs(v) >= 0.75f) vdir = v > 0f ? -1 : 1;

            if (vdir != 0)
            {
                _hHeld = false;
                if (vdir != _navHeldDir)
                {
                    _navHeldDir = vdir;
                    _navCooldown = NavFirstDelay;
                    MoveSelection(vdir);
                }
                else
                {
                    _navCooldown -= Time.unscaledDeltaTime;
                    if (_navCooldown <= 0f)
                    {
                        _navCooldown = NavRepeatDelay;
                        MoveSelection(vdir);
                    }
                }
                return;
            }
            _navHeldDir = 0;

            HandleHorizontal(h);
        }

        private void HandleHorizontal(float h)
        {
            if (_page != MenuPage.Main)
            {
                if (Mathf.Abs(h) >= 0.5f)
                {
                    if (!_hHeld)
                    {
                        _hHeld = true;
                        ToggleOnSubPage();
                    }
                }
                else _hHeld = false;

                if (Input.GetKeyDown(RightStickClick) || Input.GetKeyDown(KeyCode.Escape))
                {
                    _page = MenuPage.Main;
                    _selected = FirstSelectable();
                    _scrollTop = 0;
                }
                return;
            }

            var item = _items[Mathf.Clamp(_selected, 0, _items.Count - 1)];
            if (item is SectionItem) return;

            if (item.IsAnalog)
            {
                // Continuous setting: deflection drives a rate, not a step.
                item.Analog(h, Time.unscaledDeltaTime);
                if (Mathf.Abs(h) >= 0.02f) MarquerModifie();
                return;
            }

            if (Mathf.Abs(h) >= 0.5f)
            {
                if (!_hHeld)
                {
                    _hHeld = true;
                    var page = item.Activate();
                    if (page != MenuPage.None)
                    {
                        _page = page;
                        _selected = 0;
                        _scrollTop = 0;
                    }
                    else { item.Step(h > 0f ? 1 : -1); MarquerModifie(); }
                }
            }
            else _hHeld = false;
        }

        private void ToggleOnSubPage()
        {
            if (_page == MenuPage.Bindings)
            {
                // Arm the capture; the next input pressed will be recorded.
                _capture = _selected;
            }
        }

        /// <summary>
        /// Deferred save. Waiting for the menu to close lost everything if the game was
        /// quit with it still open; saving on every step would write the file dozens of
        /// times a second while adjusting a value with the stick.
        /// </summary>
        /// <summary>Resets every setting to its default, then saves.</summary>
        private static void Reinitialiser()
        {
            int n = 0;
            foreach (var kv in Plugin.Instance.Config)
            {
                kv.Value.BoxedValue = kv.Value.DefaultValue;
                n++;
            }
            Plugin.Instance.Config.Save();
            Plugin.Log.LogInfo("Settings reset (" + n + " entries).");

            var mgr = VrManager.Instance;
            if (mgr != null) mgr.ReapplyScene();
        }

        private void MarquerModifie()
        {
            _sauvegardeA = Time.unscaledTime + 1.5f;
        }

        private void SauvegarderSiNecessaire()
        {
            if (_sauvegardeA < 0f || Time.unscaledTime < _sauvegardeA) return;
            _sauvegardeA = -1f;
            Plugin.Instance.Config.Save();
            Plugin.Log.LogInfo("Settings saved.");
        }

        private void MoveSelection(int dir)
        {
            int count = PageCount();

            for (int guard = 0; guard < count; guard++)
            {
                _selected += dir;
                if (_selected < 0) _selected = count - 1;
                if (_selected >= count) _selected = 0;
                if (_page != MenuPage.Main || !(_items[_selected] is SectionItem)) break;
            }

            if (_selected < _scrollTop + 1) _scrollTop = _selected - 1;
            if (_selected > _scrollTop + VisibleRows - 2) _scrollTop = _selected - VisibleRows + 2;
            _scrollTop = Mathf.Clamp(_scrollTop, 0, Mathf.Max(0, count - VisibleRows));
        }

        // ------------------------------------------------------------------
        // Rendering
        // ------------------------------------------------------------------

        private void Refresh()
        {
            _rows.Clear();

            switch (_page)
            {
                case MenuPage.Bindings:
                    for (int i = 0; i < VrBindings.Labels.Length; i++)
                        _rows.Add(new RowData
                        {
                            Label = VrBindings.Labels[i],
                            Value = _capture == i
                                ? "<b>press a button...</b>"
                                : VrBindings.Text((VrBindings.Action)i)
                        });
                    _ui.SetHeader("Controller bindings",
                        _capture >= 0
                            ? "press the button you want for this action"
                            : "left/right  assign       right stick click  back");
                    break;

                default:
                    foreach (var item in _items)
                        _rows.Add(new RowData
                        {
                            Label = item.Label,
                            Value = item.ValueText,
                            IsSection = item is SectionItem,
                            HasSlider = item.HasSlider,
                            Normalized = item.Normalized
                        });
                    _ui.SetHeader("Away VR", Hint());
                    break;
            }

            _ui.SetRows(_rows, _selected, _scrollTop);
        }

        /// <summary>Swing state: saves guessing why a swing is not registering.</summary>
        private static string Hint()
        {
            string arme = Swing.MeleeEquipped ? "melee (swing active)" : "ranged (trigger)";
            return "weapon: " + arme + "     hand: " + Swing.Speed.ToString("0.0") + " m/s"
                   + "     grip: " + Weapons.GripInfo() + "\n"
                   + "up/down  select       left/right  adjust       click both sticks  close";
        }
    }
}

