using System.Collections.Generic;
using UnityEngine;

namespace AwayVR.Menu
{
    /// <summary>
    /// Settings menu, driven entirely by the stick: there is no mouse in VR, therefore no
    /// pointer. Three pages: the settings, the layer selection and the canvas selection.
    /// Everything applies live, with no need to close the menu.
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

            _items.Add(new SectionItem("Controls"));
            _items.Add(new EnumItem("Turn mode", Plugin.CfgTurnMode,
                new[] { "Snap", "Smooth" }));
            _items.Add(new FloatItem("Snap angle", Plugin.CfgSnapAngle,
                5f, 180f, 6f, 90f, 1f, "0", " deg"));
            _items.Add(new FloatItem("Turn speed", Plugin.CfgSmoothTurnSpeed,
                20f, 400f, 12f, 220f, 1f, "0", " deg/s"));

            _items.Add(new BoolItem("Room-scale walking", Plugin.CfgRoomScaleMove));
            _items.Add(new BoolItem("Block view at walls", Plugin.CfgBlockCameraOnWalls));

            _items.Add(new SectionItem("Weapon"));
            _items.Add(new EnumItem("Hand", Plugin.CfgWeaponAttach,
                new[] { "None", "Right", "Left" }));
            _items.Add(new EnumItem("Grip point", Plugin.CfgWeaponAnchor,
                new[] { "Hand", "Base", "Tip", "Center", "Pivot" }));
            _items.Add(new FloatItem("Scale", Plugin.CfgWeaponScale,
                0.05f, 2f, 0.02f, 0.6f, 0.01f, "0.00", "x"));
            _items.Add(new FloatItem("Position X (side)", Plugin.CfgWeaponOffX,
                -2.5f, 2.5f, 0.02f, 1.2f, 0.005f, "0.000", " m"));
            _items.Add(new FloatItem("Position Y (height)", Plugin.CfgWeaponOffY,
                -2.5f, 2.5f, 0.02f, 1.2f, 0.005f, "0.000", " m"));
            _items.Add(new FloatItem("Position Z (depth)", Plugin.CfgWeaponOffZ,
                -2.5f, 2.5f, 0.02f, 1.2f, 0.005f, "0.000", " m"));
            _items.Add(new BoolItem("Swing to attack", Plugin.CfgSwingToAttack));
            _items.Add(new BoolItem("Grenade in hand", Plugin.CfgGrenadeInHand));
            _items.Add(new BoolItem("Throw from hand", Plugin.CfgGrenadeFromHand));
            _items.Add(new BoolItem("Throw by release", Plugin.CfgGrenadeGesture));
            _items.Add(new FloatItem("Arm at", Plugin.CfgGrenadeArmLevel,
                0.40f, 1.00f, 0.02f, 0.5f, 0.01f, "0.00", ""));
            _items.Add(new FloatItem("Release at", Plugin.CfgGrenadeReleaseLevel,
                0.00f, 0.50f, 0.02f, 0.5f, 0.01f, "0.00", ""));
            _items.Add(new FloatItem("Armed scale", Plugin.CfgGrenadeArmScale,
                1.00f, 2.00f, 0.02f, 0.5f, 0.01f, "0.00", "x"));
            _items.Add(new FloatItem("Grenade scale", Plugin.CfgGrenadeScale,
                0.05f, 3.00f, 0.02f, 0.8f, 0.01f, "0.00", "x"));
            _items.Add(new FloatItem("Grenade X", Plugin.CfgGrenadeOffX,
                -0.50f, 0.50f, 0.01f, 0.3f, 0.005f, "0.000", " m"));
            _items.Add(new FloatItem("Grenade Y", Plugin.CfgGrenadeOffY,
                -0.50f, 0.50f, 0.01f, 0.3f, 0.005f, "0.000", " m"));
            _items.Add(new FloatItem("Grenade Z", Plugin.CfgGrenadeOffZ,
                -0.50f, 0.50f, 0.01f, 0.3f, 0.005f, "0.000", " m"));
            _items.Add(new FloatItem("Swing threshold", Plugin.CfgSwingThreshold,
                0.3f, 6f, 0.08f, 2.0f, 0.05f, "0.00", " m/s"));

            _items.Add(new SectionItem("Graphics"));
            _items.Add(new FloatItem("World scale", Plugin.CfgWorldScale,
                0.30f, 3.00f, 0.06f, 0.8f, 0.01f, "0.00", "x"));
            _items.Add(new BoolItem("Disable bloom", Plugin.CfgDisableBloom));
            _items.Add(new BoolItem("Disable colour grading", Plugin.CfgDisableColorGrading));
            _items.Add(new LayerComboItem("Hidden layers"));

            _items.Add(new SectionItem("Interface"));
            _items.Add(new BoolItem("HUD always visible", Plugin.CfgHudAlwaysVisible));
            _items.Add(new BoolItem("VR fade", Plugin.CfgVrFade));
            _items.Add(new BoolItem("Fade on character swap", Plugin.CfgFadeOnCharacterSwap));
            _items.Add(new FloatItem("HUD size", Plugin.CfgHudWidth,
                0.50f, 6.00f, 0.10f, 1.5f, 0.05f, "0.00", " m"));
            _items.Add(new FloatItem("HUD distance", Plugin.CfgHudDistance,
                0.60f, 6.00f, 0.10f, 1.5f, 0.05f, "0.00", " m"));
            _items.Add(new BoolItem("Unlock from head", Plugin.CfgWorldLockCameraChildren));
            _items.Add(new CanvasComboItem("Hidden canvases"));

            _items.Add(new SectionItem("Player"));
            _items.Add(new FloatItem("Player height", Plugin.CfgHeightOffset,
                -1.20f, 1.20f, 0.05f, 0.6f, 0.01f, "0.00", " m"));

            _items.Add(new SectionItem("System"));
            _items.Add(new BindingsItem("Controller bindings"));
            _items.Add(new BoolItem("Controller probe", Plugin.CfgProbe));
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
                case MenuPage.Layers: return HiddenLayers.Named().Count;
                case MenuPage.Canvases: return CanvasTools.RootCanvasNames().Count;
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
            if (_page == MenuPage.Layers)
            {
                var named = HiddenLayers.Named();
                if (_selected >= 0 && _selected < named.Count)
                    HiddenLayers.Toggle(named[_selected]);
            }
            else if (_page == MenuPage.Bindings)
            {
                // Arm the capture; the next input pressed will be recorded.
                _capture = _selected;
            }
            else if (_page == MenuPage.Canvases)
            {
                var noms = CanvasTools.RootCanvasNames();
                if (_selected >= 0 && _selected < noms.Count)
                {
                    HiddenCanvases.Toggle(noms[_selected]);
                    var mgr = VrManager.Instance;
                    if (mgr != null) mgr.ReapplyScene();
                }
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
                case MenuPage.Layers:
                    foreach (var layer in HiddenLayers.Named())
                        _rows.Add(new RowData
                        {
                            Label = LayerMask.LayerToName(layer),
                            Value = HiddenLayers.IsHidden(layer)
                                ? "<b>hidden</b>" : "<color=#5d6b80>visible</color>"
                        });
                    _ui.SetHeader("Hidden layers",
                        "left/right  toggle       right stick click  back");
                    break;

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

                case MenuPage.Canvases:
                    foreach (var nom in CanvasTools.RootCanvasNames())
                        _rows.Add(new RowData
                        {
                            Label = nom,
                            Value = HiddenCanvases.IsHidden(nom)
                                ? "<b>hidden</b>" : "<color=#5d6b80>visible</color>"
                        });
                    _ui.SetHeader("Scene canvases",
                        "left/right  toggle       right stick click  back");
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
