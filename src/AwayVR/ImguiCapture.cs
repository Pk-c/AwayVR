using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Captures the IMGUI drawing of the dialogues and shows it in VR.
    ///
    /// This game's dialogues are not canvas UI: DialogOnGUI draws them with Unity's
    /// immediate-mode IMGUI, which the VR compositor never folds into the eye textures. No
    /// canvas setting could ever have made them appear.
    ///
    /// The idea: IMGUI draws into whichever render target is ACTIVE when it runs. So we wrap
    /// OnGUI to redirect that target to a texture of ours, then show the texture on a panel
    /// in front of the player. The game keeps drawing exactly as before, and we never have
    /// to reimplement its dialogue system.
    ///
    /// Two precautions decide whether this works at all:
    ///  - only step in on the Repaint event, the only one that actually draws;
    ///  - always restore the previous target on the way out, otherwise the game's entire
    ///    rendering ends up in our texture.
    /// </summary>
    internal static class ImguiCapture
    {
        private static RenderTexture _rt;
        private static RenderTexture _previous;
        private static bool _redirecting;

        /// <summary>The dialogue drew something during the last frame.</summary>
        private static bool _didDraw;

        private static readonly VrPanel _panel = new VrPanel();
        private static bool _wasVisible;

        private static FieldInfo _duiField;
        private static FieldInfo _alphaField;
        private static bool _fieldsResolved;

        // ------------------------------------------------------------------
        // Patch
        // ------------------------------------------------------------------

        public static void Apply(Harmony harmony)
        {
            var type = AccessTools.TypeByName("DialogOnGUI");
            if (type == null)
            {
                Plugin.Log.LogWarning("Dialogue capture: type DialogOnGUI not found.");
                return;
            }

            var method = AccessTools.Method(type, "OnGUI");
            if (method == null)
            {
                Plugin.Log.LogWarning("Dialogue capture: DialogOnGUI.OnGUI not found.");
                return;
            }

            harmony.Patch(method,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(ImguiCapture), "Before")),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(ImguiCapture), "After")));
            Plugin.Log.LogInfo("  dialogue capture: DialogOnGUI.OnGUI redirected.");
        }

        private static void Before()
        {
            _redirecting = false;
            if (!VrManager.VrActive || !Plugin.CfgDialogCapture.Value) return;

            // Only Repaint draws; on the other events IMGUI merely computes layout, and
            // hijacking the target would achieve nothing.
            if (Event.current == null || Event.current.type != EventType.Repaint) return;

            var rt = Texture();
            if (rt == null) return;

            _previous = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
            _redirecting = true;
        }

        private static void After()
        {
            if (!_redirecting) return;
            _redirecting = false;

            RenderTexture.active = _previous;
            _previous = null;
            _didDraw = true;
        }

        // ------------------------------------------------------------------
        // Texture and panel
        // ------------------------------------------------------------------

        private static RenderTexture Texture()
        {
            int w = Mathf.Max(Screen.width, 16);
            int h = Mathf.Max(Screen.height, 16);

            // IMGUI lays out in screen pixels: the texture must have exactly those
            // dimensions, or the dialogue would be framed crookedly.
            if (_rt != null && (_rt.width != w || _rt.height != h))
            {
                Object.Destroy(_rt);
                _rt = null;
            }
            if (_rt == null)
            {
                _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
                _rt.name = "AwayVR_DialogRT";
                _rt.Create();
            }
            return _rt;
        }

        /// <summary>Current dialogue opacity, read from DialogUI.dui.alpha.</summary>
        private static float Alpha()
        {
            if (!_fieldsResolved)
            {
                _fieldsResolved = true;
                var t = AccessTools.TypeByName("DialogUI");
                if (t != null)
                {
                    _duiField = AccessTools.Field(t, "dui");
                    _alphaField = AccessTools.Field(t, "alpha");
                }
            }
            if (_duiField == null || _alphaField == null) return 0f;

            var dui = _duiField.GetValue(null);
            if (dui == null) return 0f;
            return (float)_alphaField.GetValue(dui);
        }

        public static void Tick()
        {
            if (!VrManager.VrActive || !Plugin.CfgDialogCapture.Value)
            {
                _panel.Show(false);
                _wasVisible = false;
                return;
            }

            _panel.Ensure("AwayVR_DialogPanel", 32000);

            float a = Alpha();
            bool visible = _didDraw && a > 0.01f;
            _didDraw = false;

            // Recentre as it appears: otherwise the panel would slide in from wherever the
            // head was pointing when the previous dialogue ended.
            if (visible && !_wasVisible) _panel.Recentre();
            _wasVisible = visible;

            _panel.Show(visible);
            if (!visible) return;

            if (_rt == null) return;

            _panel.SetTexture(_rt, a);
            _panel.Place(Plugin.CfgDialogDistance.Value,
                         Plugin.CfgDialogWidth.Value,
                         _rt.width, _rt.height);
        }

        public static void Forget()
        {
            _panel.Destroy();
            _wasVisible = false;
        }
    }
}
