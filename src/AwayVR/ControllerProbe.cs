using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Controller probe, per-device edition.
    ///
    /// The first version only read KeyCode.JoystickButtonN, which means "any joystick" and
    /// therefore MERGES every detected device. Since SteamVR presents each controller as a
    /// separate joystick, two inputs sharing an index on different hands collapse into one —
    /// exactly what we were seeing: four face buttons reporting only two indices.
    ///
    /// So we also sweep the per-device codes, Joystick1ButtonN through Joystick4ButtonN.
    /// </summary>
    internal static class ControllerProbe
    {
        private const int ButtonCount = 20;
        private const int DeviceCount = 4;

        /// <summary>A deliberate press exceeds this hold time; a capacitive sensor does not.</summary>
        private const float MinHold = 0.12f;

        // [0] = "any joystick", [1..4] = joysticks 1 to 4.
        private static readonly bool[,] State = new bool[DeviceCount + 1, ButtonCount];
        private static readonly float[,] Since = new float[DeviceCount + 1, ButtonCount];
        private static readonly bool[,] Logged = new bool[DeviceCount + 1, ButtonCount];
        private static int _counter;

        /// <summary>
        /// Axes under watch. The grips are analog: a sweep of indices 10 through 19 showed
        /// they are the only ones that respond, across their full travel.
        /// </summary>
        private static readonly string[,] Axes =
        {
            { "LeftTrigg_sensibility_Attack", "axis 8  left trigger" },
            { "RightTrigg_sensibility_Attack", "axis 9  right trigger" },
            { "AwayVR_GripL", "axis 10  left grip" },
            { "AwayVR_GripR", "axis 11  right grip" }
        };

        /// <summary>Low threshold: a barely touched analog input must still show up.</summary>
        private const float AxisThreshold = 0.15f;

        private static readonly float[] Last = new float[4];
        private static readonly float[] Peak = new float[4];
        private static bool _wasActive;

        /// <summary>
        /// KeyCode.JoystickButton0 is 330, and each per-device block follows every 20 codes:
        /// Joystick1Button0 = 350, Joystick2Button0 = 370, and so on.
        /// </summary>
        private static KeyCode Code(int device, int button)
        {
            return device == 0
                ? KeyCode.JoystickButton0 + button
                : KeyCode.Joystick1Button0 + (device - 1) * 20 + button;
        }

        private static string Name(int device)
        {
            return device == 0 ? "any" : "joystick " + device;
        }

        public static void Tick()
        {
            bool active = Plugin.CfgProbe.Value;
            if (active != _wasActive)
            {
                _wasActive = active;
                if (active) Start();
                else Plugin.Log.LogInfo("=== CONTROLLER PROBE STOPPED ===");
            }
            if (!active) return;

            float t = Time.unscaledTime;

            for (int m = 0; m <= DeviceCount; m++)
            {
                for (int b = 0; b < ButtonCount; b++)
                {
                    bool p = Input.GetKey(Code(m, b));

                    if (p && !State[m, b])
                    {
                        Since[m, b] = t;
                        Logged[m, b] = false;
                    }
                    State[m, b] = p;

                    if (p && !Logged[m, b] && t - Since[m, b] >= MinHold)
                    {
                        Logged[m, b] = true;
                        // Only the per-device detail matters now; the merged entry is kept
                        // purely as a landmark.
                        if (m == 0) _counter++;
                        Plugin.Log.LogInfo("  " + (m == 0 ? "#" + _counter + " " : "     ")
                                           + " BUTTON " + b + "   [" + Name(m) + "]");
                    }
                }
            }

            for (int i = 0; i < Axes.GetLength(0); i++)
            {
                float v;
                try { v = Input.GetAxisRaw(Axes[i, 0]); }
                catch { continue; }

                float a = Mathf.Abs(v);
                if (a > AxisThreshold && Mathf.Abs(Last[i]) <= AxisThreshold)
                    Plugin.Log.LogInfo("  AXIS " + Axes[i, 1] + " active");

                // We keep the peak amplitude: that is what tells a genuinely driven axis
                // apart from one merely jittering around zero.
                if (a > Peak[i])
                {
                    Peak[i] = a;
                    if (a > AxisThreshold)
                        Plugin.Log.LogInfo("  AXIS " + Axes[i, 1] + " peak = " + v.ToString("0.00"));
                }
                Last[i] = v;
            }
        }

        private static void Start()
        {
            Plugin.Log.LogInfo("=== CONTROLLER PROBE ACTIVE ===");

            var names = Input.GetJoystickNames();
            Plugin.Log.LogInfo("  devices detected: " + names.Length);
            for (int i = 0; i < names.Length; i++)
                Plugin.Log.LogInfo("    joystick " + (i + 1) + ": '" + names[i] + "'");

            Plugin.Log.LogInfo("  Press each input and HOLD it for half a second.");

            _counter = 0;
            for (int m = 0; m <= DeviceCount; m++)
                for (int b = 0; b < ButtonCount; b++)
                {
                    State[m, b] = false;
                    Logged[m, b] = false;
                    Since[m, b] = 0f;
                }
            for (int i = 0; i < Last.Length; i++) { Last[i] = 0f; Peak[i] = 0f; }
        }
    }
}
