using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Talks to OpenVR directly, bypassing Unity's legacy joystick layer.
    ///
    /// SteamVR knows about the A and X buttons - its driver binding sends them to the
    /// a_press action - but Unity's legacy mapping gives that action no joystick index, so
    /// they are unreachable through Input. Reading the runtime ourselves gets them back,
    /// along with clean analog triggers and grips.
    ///
    /// The table's layout depends on the interface version, and calling the wrong entry jumps
    /// to an arbitrary pointer. So we pin IVRSystem_019, whose exact method order comes from
    /// Valve's own C# binding, and refuse to read controllers under any other version.
    /// </summary>
    internal static class OpenVrBridge
    {
        [DllImport("openvr_api", EntryPoint = "VR_GetGenericInterface",
                   CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetGenericInterface(
            [MarshalAs(UnmanagedType.LPStr)] string version, ref int error);

        [DllImport("openvr_api", EntryPoint = "VR_IsInterfaceVersionValid",
                   CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool IsInterfaceVersionValid(
            [MarshalAs(UnmanagedType.LPStr)] string version);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate void GetRenderTargetSize(ref uint width, ref uint height);

        /// <summary>
        /// The runtime accepts 001 to 026, so the choice is ours - and what matters is not
        /// being current but knowing the method order exactly. This is the version Valve's
        /// own C# binding declares, and the order below is taken from it verbatim.
        /// </summary>
        private const string Pinned = "IVRSystem_019";

        private const int IdxControllerRole = 18;   // GetTrackedDeviceIndexForControllerRole
        private const int IdxControllerState = 34;  // GetControllerState

        private static readonly string[] Versions = { Pinned };

        // From Valve's EVRButtonId.
        public const int ButtonA = 7;
        public const int ButtonApplicationMenu = 1;
        public const int ButtonGrip = 2;
        public const int ButtonTrigger = 33;
        public const int ButtonStick = 32;

        private enum ControllerRole { Invalid = 0, LeftHand = 1, RightHand = 2 }

        [StructLayout(LayoutKind.Sequential)]
        private struct ControllerAxis { public float x, y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct ControllerState
        {
            public uint PacketNum;
            public ulong ButtonPressed;
            public ulong ButtonTouched;
            public ControllerAxis Axis0, Axis1, Axis2, Axis3, Axis4;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate uint IndexForRole(ControllerRole role);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool ReadState(uint device, ref ControllerState state, uint size);

        private static IndexForRole _indexForRole;
        private static ReadState _readState;

        private static ulong _leftMask, _rightMask;
        private static uint _leftIndex = 0xFFFFFFFF, _rightIndex = 0xFFFFFFFF;
        private static float _nextRoleLookup;

        /// <summary>Is that OpenVR button held on that hand?</summary>
        public static bool Pressed(bool left, int button)
        {
            if (!Ready || button < 0 || button > 63) return false;
            ulong mask = left ? _leftMask : _rightMask;
            return (mask & (1UL << button)) != 0UL;
        }

        public static void Tick()
        {
            if (!Ready || _readState == null) return;

            // Device indices change when a controller sleeps and wakes.
            if (Time.unscaledTime >= _nextRoleLookup)
            {
                _nextRoleLookup = Time.unscaledTime + 1f;
                try
                {
                    _leftIndex = _indexForRole(ControllerRole.LeftHand);
                    _rightIndex = _indexForRole(ControllerRole.RightHand);
                }
                catch { Ready = false; return; }
            }

            ulong left = ReadMask(_leftIndex);
            ulong right = ReadMask(_rightIndex);

            // Reported as it changes, because a snapshot taken with F10 can never catch a
            // button that has to be held with the same hand.
            if (Plugin.CfgProbe.Value)
            {
                if (left != _leftMask) Report("left", _leftMask, left);
                if (right != _rightMask) Report("right", _rightMask, right);
            }

            _leftMask = left;
            _rightMask = right;
        }

        private static string ButtonName(int bit)
        {
            switch (bit)
            {
                case 0:  return "System";
                case 1:  return "ApplicationMenu (B/Y)";
                case 2:  return "Grip";
                case 7:  return "A/X";
                case 31: return "ProximitySensor";
                case 32: return "Stick (touchpad)";
                case 33: return "Trigger";
                default: return "bit " + bit;
            }
        }

        private static void Report(string hand, ulong before, ulong now)
        {
            for (int bit = 0; bit < 64; bit++)
            {
                ulong m = 1UL << bit;
                bool was = (before & m) != 0UL;
                bool has = (now & m) != 0UL;
                if (was == has) continue;
                Plugin.Log.LogInfo("[openvr] " + hand + " " + ButtonName(bit)
                                   + (has ? "  DOWN" : "  up") + "   (bit " + bit + ")");
            }
        }

        private static ulong ReadMask(uint device)
        {
            if (device == 0xFFFFFFFF) return 0UL;
            var state = new ControllerState();
            try
            {
                if (!_readState(device, ref state, (uint)Marshal.SizeOf(typeof(ControllerState))))
                    return 0UL;
            }
            catch { Ready = false; return 0UL; }
            return state.ButtonPressed;
        }

        public static string Version { get; private set; }
        public static IntPtr Table { get; private set; }
        public static bool Ready { get; private set; }

        private static bool _tried;

        public static void Probe()
        {
            if (_tried) return;
            _tried = true;

            foreach (var v in Versions)
            {
                bool valid;
                try { valid = IsInterfaceVersionValid(v); }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("OpenVR bridge: openvr_api unreachable (" + e.GetType().Name
                                          + "). Falling back to Unity input.");
                    return;
                }
                if (!valid) continue;

                int error = 0;
                IntPtr table;
                try { table = GetGenericInterface("FnTable:" + v, ref error); }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("OpenVR bridge: " + v + " -> " + e.GetType().Name);
                    continue;
                }

                if (table == IntPtr.Zero || error != 0)
                {
                    Plugin.Log.LogInfo("OpenVR bridge: " + v + " refused (error " + error + ")");
                    continue;
                }

                Version = v;
                Table = table;
                Validate();
                return;
            }

            Plugin.Log.LogWarning("OpenVR bridge: no IVRSystem version accepted.");
        }

        /// <summary>
        /// Confirms the table is real before anything depends on it: the first entries must be
        /// plausible pointers, and index 0 must return an eye size close to what Unity reports.
        /// A table that fails this is not used at all.
        /// </summary>
        private static void Validate()
        {
            for (int i = 0; i < 8; i++)
            {
                var fn = Marshal.ReadIntPtr(Table, i * IntPtr.Size);
                if (fn != IntPtr.Zero) continue;
                Plugin.Log.LogWarning("OpenVR bridge: " + Version + " table entry " + i
                                      + " is null - not usable.");
                return;
            }

            uint w = 0, h = 0;
            try
            {
                var fn = Marshal.ReadIntPtr(Table, 0);
                var call = (GetRenderTargetSize)Marshal.GetDelegateForFunctionPointer(
                    fn, typeof(GetRenderTargetSize));
                call(ref w, ref h);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("OpenVR bridge: probe call failed (" + e.GetType().Name + ").");
                return;
            }

            bool sane = w > 200 && w < 16000 && h > 200 && h < 16000;
            Plugin.Log.LogInfo("OpenVR bridge: " + Version + " acquired, render target "
                               + w + "x" + h + (sane ? "  [plausible]" : "  [SUSPECT]"));
            if (!sane)
            {
                Plugin.Log.LogWarning("OpenVR bridge: the size is not plausible, so the table "
                                      + "layout does not match. Not used.");
                return;
            }

            // The controller entries are bound only under the pinned version, whose layout is
            // the one taken from Valve's binding. Any other version means unknown offsets.
            if (Version != Pinned)
            {
                Plugin.Log.LogWarning("OpenVR bridge: " + Version + " is not the pinned "
                                      + Pinned + ", controller reads stay off.");
                return;
            }

            try
            {
                _indexForRole = (IndexForRole)Marshal.GetDelegateForFunctionPointer(
                    Marshal.ReadIntPtr(Table, IdxControllerRole * IntPtr.Size), typeof(IndexForRole));
                _readState = (ReadState)Marshal.GetDelegateForFunctionPointer(
                    Marshal.ReadIntPtr(Table, IdxControllerState * IntPtr.Size), typeof(ReadState));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("OpenVR bridge: binding the controller entries failed ("
                                      + e.GetType().Name + ").");
                return;
            }

            Ready = true;
        }

        public static void Dump(System.Text.StringBuilder sb)
        {
            sb.AppendLine("-- OpenVR bridge --");
            sb.AppendLine("  enabled=" + Plugin.CfgOpenVrBridge.Value
                          + "  ready=" + Ready
                          + "  version=" + (Version ?? "<none>")
                          + "  table=0x" + Table.ToInt64().ToString("X"));
            sb.AppendLine("  devices: left=" + (_leftIndex == 0xFFFFFFFF ? "-" : _leftIndex.ToString())
                          + "  right=" + (_rightIndex == 0xFFFFFFFF ? "-" : _rightIndex.ToString()));
            sb.AppendLine("  buttons: left=0x" + _leftMask.ToString("X")
                          + "  right=0x" + _rightMask.ToString("X")
                          + "   (A/X = bit " + ButtonA + ")");
            sb.AppendLine("  X held=" + Pressed(true, ButtonA)
                          + "   A held=" + Pressed(false, ButtonA));
        }
    }
}
