using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace AwayVR.Menu
{
    /// <summary>One row of the menu. The renderer knows only these four properties.</summary>
    internal abstract class MenuItem
    {
        public string Label;

        public abstract string ValueText { get; }

        /// <summary>Draws a gauge; otherwise only the textual value is shown.</summary>
        public virtual bool HasSlider { get { return false; } }

        /// <summary>Gauge position, 0..1.</summary>
        public virtual float Normalized { get { return 0f; } }

        /// <summary>Continuous setting: the stick drives a rate, not discrete steps.</summary>
        public virtual bool IsAnalog { get { return false; } }

        /// <summary>dir is -1 or +1. Used for discrete values.</summary>
        public virtual void Step(int dir) { }

        /// <summary>deflection ranges from -1 to 1, dt in seconds.</summary>
        public virtual void Analog(float deflection, float dt) { }

        /// <summary>Page to open, or MenuPage.None when the row opens none.</summary>
        public virtual MenuPage Activate() { return MenuPage.None; }
    }

    internal class SectionItem : MenuItem
    {
        public SectionItem(string title) { Label = title; }
        public override string ValueText { get { return ""; } }
    }

    /// <summary>A choice among an enum's values, presented as a switch.</summary>
    internal class EnumItem : MenuItem
    {
        private readonly ConfigEntryBase _entry;
        private readonly Array _values;
        private readonly string[] _names;

        public EnumItem(string label, ConfigEntryBase entry, string[] names)
        {
            Label = label;
            _entry = entry;
            _values = Enum.GetValues(entry.SettingType);
            _names = names;
        }

        private int Index
        {
            get
            {
                for (int i = 0; i < _values.Length; i++)
                    if (_values.GetValue(i).Equals(_entry.BoxedValue)) return i;
                return 0;
            }
        }

        public override string ValueText
        {
            get
            {
                int idx = Index;
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < _values.Length; i++)
                {
                    string n = _names != null && i < _names.Length ? _names[i] : _values.GetValue(i).ToString();
                    if (i > 0) sb.Append("   ");
                    if (i == idx) sb.Append("<b>").Append(n).Append("</b>");
                    else sb.Append("<color=#5d6b80>").Append(n).Append("</color>");
                }
                return sb.ToString();
            }
        }

        public override void Step(int dir)
        {
            int i = (Index + dir + _values.Length) % _values.Length;
            _entry.BoxedValue = _values.GetValue(i);
        }
    }

    /// <summary>
    /// A continuous value. The rate of change follows how far the stick is pushed: barely
    /// tilted it advances by the finest step, fully pushed it sweeps the range quickly.
    /// </summary>
    internal class FloatItem : MenuItem
    {
        private readonly ConfigEntry<float> _entry;
        private readonly float _min, _max;
        private readonly float _slowRate, _fastRate;
        private readonly float _quantum;
        private readonly string _format;
        private readonly string _suffix;

        /// <summary>
        /// UNROUNDED accumulator. Adding rate*dt to the already-rounded value lost every
        /// increment smaller than half a step: at 90 Hz a slow setting never advanced at
        /// all, and some would not budge even with the stick fully pushed.
        /// </summary>
        private float _accum;
        private bool _accumValide;

        /// <summary>
        /// The game's axes already carry a 0.30 dead zone in the InputManager: piling a
        /// large one on top removed the entire fine-adjustment range.
        /// </summary>
        private const float Deadzone = 0.02f;

        public FloatItem(string label, ConfigEntry<float> entry, float min, float max,
                         float slowRate, float fastRate, float quantum,
                         string format, string suffix)
        {
            Label = label;
            _entry = entry;
            _min = min; _max = max;
            _slowRate = slowRate; _fastRate = fastRate;
            _quantum = quantum;
            _format = format;
            _suffix = suffix;
        }

        public override string ValueText { get { return _entry.Value.ToString(_format) + _suffix; } }
        public override bool HasSlider { get { return true; } }
        public override float Normalized { get { return Mathf.InverseLerp(_min, _max, _entry.Value); } }
        public override bool IsAnalog { get { return true; } }

        public override void Analog(float deflection, float dt)
        {
            float a = Mathf.Abs(deflection);
            if (a < Deadzone) { _accumValide = false; return; }

            // Resynchronise if the value changed elsewhere (another setting, a reload).
            if (!_accumValide || Mathf.Abs(_accum - _entry.Value) > _quantum * 2f)
            {
                _accum = _entry.Value;
                _accumValide = true;
            }

            float t = (a - Deadzone) / (1f - Deadzone);
            // Cubic curve: the lower half of the stick's travel becomes genuinely usable
            // for fine adjustment, and only the far end sweeps the range quickly.
            float rate = Mathf.Lerp(_slowRate, _fastRate, t * t * t);

            _accum = Mathf.Clamp(_accum + Mathf.Sign(deflection) * rate * dt, _min, _max);
            _entry.Value = Mathf.Round(_accum / _quantum) * _quantum;
        }

        public override void Step(int dir)
        {
            float v = Mathf.Clamp(_entry.Value + dir * _quantum, _min, _max);
            _entry.Value = Mathf.Round(v / _quantum) * _quantum;
            _accumValide = false;
        }
    }

    /// <summary>A yes/no switch.</summary>
    internal class BoolItem : MenuItem
    {
        private readonly ConfigEntry<bool> _entry;

        public BoolItem(string label, ConfigEntry<bool> entry)
        {
            Label = label;
            _entry = entry;
        }

        public override string ValueText
        {
            get
            {
                return _entry.Value
                    ? "<b>yes</b>   <color=#5d6b80>no</color>"
                    : "<color=#5d6b80>yes</color>   <b>no</b>";
            }
        }

        public override void Step(int dir) { _entry.Value = !_entry.Value; }
    }

    /// <summary>A row that triggers an action instead of carrying a value.</summary>
    internal class ActionItem : MenuItem
    {
        private readonly System.Action _action;
        private readonly string _texte;

        public ActionItem(string label, string texte, System.Action action)
        {
            Label = label;
            _texte = texte;
            _action = action;
        }

        public override string ValueText { get { return _texte; } }

        public override void Step(int dir)
        {
            if (_action != null) _action();
        }
    }

    internal enum MenuPage { None, Main, Layers, Canvases, Bindings }

    /// <summary>Opens the input assignment page.</summary>
    internal class BindingsItem : MenuItem
    {
        public BindingsItem(string label) { Label = label; }
        public override string ValueText { get { return "›"; } }
        public override MenuPage Activate() { return MenuPage.Bindings; }
    }

    /// <summary>Opens the sub-page for selecting hidden layers.</summary>
    internal class LayerComboItem : MenuItem
    {
        public LayerComboItem(string label) { Label = label; }

        public override string ValueText
        {
            get
            {
                int n = HiddenLayers.Current.Count;
                if (n == 0) return "<color=#5d6b80>none</color>   ›";
                return n + (n > 1 ? " layers" : " layer") + "   ›";
            }
        }

        public override MenuPage Activate() { return MenuPage.Layers; }
    }

    /// <summary>Opens the sub-page for selecting hidden canvases.</summary>
    internal class CanvasComboItem : MenuItem
    {
        public CanvasComboItem(string label) { Label = label; }

        public override string ValueText
        {
            get
            {
                int n = HiddenCanvases.Count;
                if (n == 0) return "<color=#5d6b80>none</color>   ›";
                return n + " hidden   ›";
            }
        }

        public override MenuPage Activate() { return MenuPage.Canvases; }
    }

    /// <summary>
    /// Manually hidden canvases. An escape hatch: when a stray canvas cannot be identified
    /// automatically, you point at it from inside the game.
    /// </summary>
    internal static class HiddenCanvases
    {
        private static readonly HashSet<string> _set = new HashSet<string>();
        private static string _last;

        private static void Sync()
        {
            var s = Plugin.CfgHiddenCanvases.Value ?? "";
            if (s == _last) return;
            _last = s;
            _set.Clear();
            foreach (var raw in s.Split(','))
            {
                var t = raw.Trim();
                if (t.Length > 0) _set.Add(t);
            }
        }

        public static int Count { get { Sync(); return _set.Count; } }

        public static bool IsHidden(string name) { Sync(); return _set.Contains(name); }

        public static void Toggle(string name)
        {
            Sync();
            if (!_set.Remove(name)) _set.Add(name);

            var liste = new List<string>(_set);
            liste.Sort(System.StringComparer.OrdinalIgnoreCase);
            _last = string.Join(",", liste.ToArray());
            Plugin.CfgHiddenCanvases.Value = _last;
        }
    }

    /// <summary>Shared state of the hidden-layer list, backed by the text config.</summary>
    internal static class HiddenLayers
    {
        private static readonly HashSet<int> _set = new HashSet<int>();
        private static string _lastSerialized;

        public static ICollection<int> Current
        {
            get { Sync(); return _set; }
        }

        private static void Sync()
        {
            var s = Plugin.CfgHiddenLayers.Value ?? "";
            if (s == _lastSerialized) return;
            _lastSerialized = s;
            _set.Clear();
            foreach (var raw in s.Split(','))
            {
                var tok = raw.Trim();
                if (tok.Length == 0) continue;
                int layer;
                if (!int.TryParse(tok, out layer)) layer = LayerMask.NameToLayer(tok);
                if (layer >= 0 && layer < 32) _set.Add(layer);
            }
        }

        public static bool IsHidden(int layer) { Sync(); return _set.Contains(layer); }

        public static void Toggle(int layer)
        {
            Sync();
            if (!_set.Remove(layer)) _set.Add(layer);
            Serialize();
        }

        private static void Serialize()
        {
            var names = new List<string>();
            for (int i = 0; i < 32; i++)
            {
                if (!_set.Contains(i)) continue;
                var n = LayerMask.LayerToName(i);
                names.Add(string.IsNullOrEmpty(n) ? i.ToString() : n);
            }
            _lastSerialized = string.Join(",", names.ToArray());
            Plugin.CfgHiddenLayers.Value = _lastSerialized;
        }

        /// <summary>Named layers, the only ones worth presenting in the list.</summary>
        public static List<int> Named()
        {
            var list = new List<int>();
            for (int i = 0; i < 32; i++)
                if (!string.IsNullOrEmpty(LayerMask.LayerToName(i))) list.Add(i);
            return list;
        }
    }
}
