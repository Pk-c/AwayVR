using System.Text;
using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Logs every controller input as it changes, PER DEVICE.
    ///
    /// The merged reads Unity offers (KeyCode.JoystickButtonN) mix the two hands and hide
    /// which one fired - that is what produced two contradictory conclusions about the face
    /// buttons. This walks every joystick separately, every button and every axis, and prints
    /// what actually moved.
    /// </summary>
    internal static class ControllerProbe
    {
        private const int MaxJoysticks = 8;
        private const int MaxButtons = 20;
        private const int MaxAxes = 20;

        private static readonly bool[,] Buttons = new bool[MaxJoysticks + 1, MaxButtons];
        private static readonly float[,] Axes = new float[MaxJoysticks + 1, MaxAxes + 1];
        private static bool _wasOn;

        public static void Tick()
        {
            if (!Plugin.CfgProbe.Value)
            {
                if (_wasOn) { _wasOn = false; Plugin.Log.LogInfo("[probe] off"); }
                return;
            }

            if (!_wasOn)
            {
                _wasOn = true;
                var names = Input.GetJoystickNames();
                var sb = new StringBuilder("[probe] on. Joysticks:");
                for (int i = 0; i < names.Length; i++)
                    sb.Append("\n   ").Append(i + 1).Append(" = '").Append(names[i]).Append('\'');
                Plugin.Log.LogInfo(sb.ToString());
            }

            for (int joy = 1; joy <= MaxJoysticks; joy++)
            {
                for (int b = 0; b < MaxButtons; b++)
                {
                    var code = KeyCode.Joystick1Button0 + (joy - 1) * MaxButtons + b;
                    bool now;
                    try { now = Input.GetKey(code); }
                    catch { continue; }

                    if (now == Buttons[joy, b]) continue;
                    Buttons[joy, b] = now;
                    Plugin.Log.LogInfo("[probe] joystick " + joy + " button " + b
                                       + (now ? "  DOWN" : "  up"));
                }
            }

            // Axes are global rather than per device, so they are read once.
            for (int a = 1; a <= MaxAxes; a++)
            {
                float v;
                try { v = Input.GetAxisRaw("AwayVR_Probe" + a); }
                catch { continue; }
                if (Mathf.Abs(v - Axes[0, a]) < 0.15f) continue;
                Axes[0, a] = v;
                Plugin.Log.LogInfo("[probe] axis " + a + " = " + v.ToString("0.00"));
            }
        }
    }
}
