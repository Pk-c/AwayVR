using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityStandardAssets.Characters.FirstPerson;

namespace AwayVR
{
    /// <summary>
    /// Measures the walk against what the game asked for, and watches for falling.
    ///
    /// Two questions, both answered on the F10 dump rather than guessed at:
    ///
    ///   is the character losing the ground? In a scene with nothing to fall off, any airborne
    ///   frame is a defect, and the game takes a different branch for those - so this counts
    ///   them and reports how long the longest one lasted;
    ///
    ///   does the speed match the stick? The game's own arithmetic is
    ///   speed = (m_IsWalking ? m_WalkSpeed : m_RunSpeed) * |m_Input|, with m_Input clamped to
    ///   length one, so the deflection is a direct multiplier. Recording the deflection and the
    ///   distance actually covered side by side shows whether the stick reads sensibly and
    ///   whether the character moves at the speed that follows from it.
    ///
    /// Counters restart with each dump, so one walk gives the figures for that walk alone.
    /// </summary>
    internal static class WalkProbe
    {
        /// <summary>Deflection above which the stick counts as pushed all the way.</summary>
        private const float FullPush = 0.90f;

        private static readonly FieldInfo FIsWalking =
            AccessTools.Field(typeof(FirstPersonController), "m_IsWalking");
        private static readonly FieldInfo FInput =
            AccessTools.Field(typeof(FirstPersonController), "m_Input");
        private static readonly FieldInfo FController =
            AccessTools.Field(typeof(FirstPersonController), "m_CharacterController");
        private static readonly FieldInfo FWalkSpeed =
            AccessTools.Field(typeof(FirstPersonController), "m_WalkSpeed");
        private static readonly FieldInfo FRunSpeed =
            AccessTools.Field(typeof(FirstPersonController), "m_RunSpeed");
        private static readonly FieldInfo FProfile =
            AccessTools.Field(typeof(FirstPersonController), "m_CurrentCharacterProfile");
        private static readonly FieldInfo FTimeInAir =
            AccessTools.Field(typeof(FirstPersonController), "m_timeInAir");

        private static FirstPersonController _fpc;
        private static Vector3 _lastPos;
        private static bool _has;

        // Histograms rather than running averages: a median and a first percentile cannot be
        // had from an average, and it is the worst frames that are being complained about.
        // 64 buckets of 1/32, so 0 to 2 with two decimals of resolution.
        private static readonly int[] PushHist = new int[64];   // deflection at full push
        private static readonly int[] RatioHist = new int[64];  // covered / expected

        // The throttle, followed through every layer it passes. Speed is speed * |m_Input|, so
        // whatever loses deflection loses pace - and there are three places it can go:
        //   Stick  - the dedicated LeftStickOnly axes, closest to the hardware;
        //   Game   - Horizontal/Vertical, which the input manager declares SEVERAL times over and
        //            Unity merges, so they need not equal the pair above;
        //   Input  - m_Input, after the game's clamp and our own gaze-relative rotation.
        private static readonly int[] StickHist = new int[64];
        private static readonly int[] GameHist = new int[64];
        private static readonly int[] InputHist = new int[64];
        private static float _stickMax, _gameMax, _inputMax;
        private static int _samples, _pushSamples, _runFrames;

        private static int _airFrames, _airEpisodes;
        private static float _airRun, _airWorst, _fallTotal, _timeInAirWorst;
        private static bool _wasAir;

        // Of the frames that lost most of their travel, what state were they in. This is
        // what separates a landing from a wall from something unaccounted for.
        private static int _deadAir, _deadWall, _deadRoom, _deadOther;
        private static int _overshoot;
        private static float _worstFallSpeed;

        private static float _worstHeadOffset;
        private static float _worstRatio = 1f, _worstExpected, _worstGot;
        private static string _worstWhere = "-";
        private static float _walkSpeed, _runSpeed;

        public static void Reset()
        {
            for (int i = 0; i < PushHist.Length; i++)
            {
                PushHist[i] = 0; RatioHist[i] = 0;
                StickHist[i] = 0; GameHist[i] = 0; InputHist[i] = 0;
            }
            _stickMax = _gameMax = _inputMax = 0f;
            _samples = _pushSamples = _runFrames = 0;
            _airFrames = _airEpisodes = 0;
            _deadAir = _deadWall = _deadRoom = _deadOther = 0;
            _overshoot = 0;
            _airRun = _airWorst = _fallTotal = _timeInAirWorst = _worstFallSpeed = 0f;
            _wasAir = false;
            _worstHeadOffset = 0f;
            _worstRatio = 1f;
            _worstExpected = _worstGot = 0f;
            _worstWhere = "-";
            _has = false;
        }

        public static void Tick()
        {
            if (!VrManager.VrActive) return;
            if (FIsWalking == null || FInput == null || FController == null) return;

            var rig = VrManager.Rig;
            if (rig == null || rig.parent == null) return;

            if (_fpc == null) _fpc = rig.parent.GetComponentInParent<FirstPersonController>();
            if (_fpc == null) return;

            var cc = FController.GetValue(_fpc) as CharacterController;
            if (cc == null) return;

            float dt = Time.deltaTime;
            var pos = cc.transform.position;
            if (!_has || dt <= 0f) { _lastPos = pos; _has = true; return; }

            var delta = pos - _lastPos;
            _lastPos = pos;

            bool walking = (bool)FIsWalking.GetValue(_fpc);
            var input = (Vector2)FInput.GetValue(_fpc);
            float push = Mathf.Min(1f, input.magnitude);

            // The game intends to move only when the input has length; anything else would
            // average in standing still, which is not what is being measured.
            if (push <= 0.01f || !InputM.IsPlayerControlAllowed() || !_fpc.CanMove) return;

            _samples++;
            if (!walking) _runFrames++;

            float stick = Magnitude("LeftStickOnlyX", "LeftStickOnlyY");
            float game = Magnitude("Horizontal", "Vertical");
            StickHist[Bucket(stick)]++;
            GameHist[Bucket(game)]++;
            InputHist[Bucket(push)]++;
            if (stick > _stickMax) _stickMax = stick;
            if (game > _gameMax) _gameMax = game;
            if (push > _inputMax) _inputMax = push;

            // How far the camera sits from the capsule it is meant to be riding. Anything
            // above a few centimetres means the body scrapes geometry out of view.
            float off = VrManager.HeadOffset;
            if (off > _worstHeadOffset) _worstHeadOffset = off;

            // --- falling ---
            bool air = !cc.isGrounded;
            if (air)
            {
                if (!_wasAir) _airEpisodes++;
                _airFrames++;
                _airRun += dt;
                if (_airRun > _airWorst) _airWorst = _airRun;
                if (delta.y < 0f) _fallTotal -= delta.y;

                // Vertical speed while airborne: this is what the landing frame has to spend
                // before any of its budget can go forward.
                float fall = -delta.y / dt;
                if (fall > _worstFallSpeed) _worstFallSpeed = fall;
            }
            else _airRun = 0f;
            _wasAir = air;

            if (FTimeInAir != null)
            {
                float t = (float)FTimeInAir.GetValue(_fpc);
                if (t > _timeInAirWorst) _timeInAirWorst = t;
            }

            // --- speed against the stick ---
            ReadSpeeds();
            float expected = (walking ? _walkSpeed : _runSpeed) * push;
            var flat = new Vector3(delta.x, 0f, delta.z);
            float got = flat.magnitude / dt;

            if (push >= FullPush)
            {
                _pushSamples++;
                PushHist[Bucket(push)]++;
            }

            if (expected > 0.5f)
            {
                float ratio = got / expected;
                RatioHist[Bucket(ratio)]++;

                // Nothing in the game's arithmetic can exceed speed * |m_Input|, so a frame that
                // covers MORE than asked means something else moved the capsule.
                bool room = RoomScale.MovedOnFrame == Time.frameCount;
                if (ratio > 1.2f) _overshoot++;

                // A frame that covers less than half of what was asked, attributed. Ordered by
                // how well each explains itself: a landing, then a wall, then our own move.
                if (ratio < 0.5f)
                {
                    if (air) _deadAir++;
                    else if ((cc.collisionFlags & CollisionFlags.Sides) != 0) _deadWall++;
                    else if (room) _deadRoom++;
                    else _deadOther++;
                }

                if (ratio < _worstRatio)
                {
                    _worstRatio = ratio;
                    _worstExpected = expected;
                    _worstGot = got;
                    _worstWhere = "flags=" + cc.collisionFlags
                                  + " grounded=" + cc.isGrounded
                                  + " push=" + push.ToString("0.00")
                                  + " running=" + (!walking);
                }
            }
        }

        private static float Magnitude(string xName, string yName)
        {
            try
            {
                float x = Input.GetAxisRaw(xName), y = Input.GetAxisRaw(yName);
                return Mathf.Sqrt(x * x + y * y);
            }
            catch { return 0f; }
        }

        private static int Bucket(float v)
        {
            return Mathf.Clamp((int)(v * 32f), 0, 63);
        }

        /// <summary>Bucket holding the given share of the samples, back as a value.</summary>
        private static float Percentile(int[] hist, int total, float share)
        {
            if (total <= 0) return 0f;
            int want = (int)(total * share), seen = 0;
            for (int i = 0; i < hist.Length; i++)
            {
                seen += hist[i];
                if (seen >= want) return i / 32f;
            }
            return 2f;
        }

        private static void ReadSpeeds()
        {
            if (FWalkSpeed == null || FRunSpeed == null) return;

            _walkSpeed = (float)FWalkSpeed.GetValue(_fpc);
            _runSpeed = (float)FRunSpeed.GetValue(_fpc);

            var profile = FProfile != null
                ? FProfile.GetValue(_fpc) as FirstPersonController.CharacterProfile : null;
            if (profile == null) return;

            if (profile.m_WalkSpeedOverride != 0f) _walkSpeed = profile.m_WalkSpeedOverride;
            if (profile.m_RunSpeedOverride != 0f) _runSpeed = profile.m_RunSpeedOverride;
        }

        private static string Spread(int[] hist, float max)
        {
            return Percentile(hist, _samples, 0.05f).ToString("0.00")
                   + " / " + Percentile(hist, _samples, 0.25f).ToString("0.00")
                   + " / " + Percentile(hist, _samples, 0.50f).ToString("0.00")
                   + " / " + Percentile(hist, _samples, 0.95f).ToString("0.00")
                   + " / " + max.ToString("0.00");
        }

        public static void Dump(System.Text.StringBuilder sb)
        {
            sb.AppendLine("-- Walk probe --");

            if (_samples < 30)
            {
                sb.AppendLine("  not enough walking sampled (" + _samples + " frames)");
                Reset();
                return;
            }

            sb.AppendLine("  frames moving=" + _samples
                          + "  running=" + (100f * _runFrames / _samples).ToString("0") + "%"
                          + "  walkSpeed=" + _walkSpeed.ToString("0.00")
                          + "  runSpeed=" + _runSpeed.ToString("0.00"));

            sb.AppendLine("  ground lost: frames=" + _airFrames
                          + " (" + (100f * _airFrames / _samples).ToString("0.0") + "%)"
                          + "  episodes=" + _airEpisodes
                          + "  longest=" + _airWorst.ToString("0.000") + "s"
                          + "  fallen=" + _fallTotal.ToString("0.000") + "m");
            sb.AppendLine("  worst fall speed=" + _worstFallSpeed.ToString("0.00") + " m/s"
                          + "  game's timeInAir worst=" + _timeInAirWorst.ToString("0.000") + "s");
            sb.AppendLine("  camera to capsule: worst=" + _worstHeadOffset.ToString("0.000")
                          + "m  centring=" + (Plugin.CfgCentreOnBody != null
                                              && Plugin.CfgCentreOnBody.Value));
            sb.AppendLine("  frames that lost over half their travel: landing=" + _deadAir
                          + "  wall=" + _deadWall
                          + "  room-scale=" + _deadRoom
                          + "  UNACCOUNTED=" + _deadOther);
            sb.AppendLine("  frames that covered MORE than asked=" + _overshoot
                          + "   room-scale: enabled=" + Plugin.CfgRoomScaleMove.Value
                          + " moves=" + RoomScale.Moves
                          + " distance=" + RoomScale.MovedTotal.ToString("0.00") + "m");

            if (_pushSamples == 0)
                sb.AppendLine("  stick never reached " + FullPush.ToString("0.00"));
            else
                sb.AppendLine("  stick at full push: frames=" + _pushSamples
                    + " (" + (100f * _pushSamples / _samples).ToString("0") + "% of moving)"
                    + "  min=" + Percentile(PushHist, _pushSamples, 0.01f).ToString("0.00")
                    + "  median=" + Percentile(PushHist, _pushSamples, 0.5f).ToString("0.00")
                    + "  max=" + Percentile(PushHist, _pushSamples, 0.99f).ToString("0.00"));

            sb.AppendLine("  throttle, hardware to speed (p5 / p25 / median / p95 / max):");
            sb.AppendLine("    Stick  LeftStickOnly  " + Spread(StickHist, _stickMax));
            sb.AppendLine("    Game   Horiz/Vert     " + Spread(GameHist, _gameMax));
            sb.AppendLine("    Input  m_Input        " + Spread(InputHist, _inputMax));

            sb.AppendLine("  distance covered vs expected:  p1="
                + Percentile(RatioHist, _samples, 0.01f).ToString("0.00")
                + "  p5=" + Percentile(RatioHist, _samples, 0.05f).ToString("0.00")
                + "  median=" + Percentile(RatioHist, _samples, 0.5f).ToString("0.00")
                + "  p99=" + Percentile(RatioHist, _samples, 0.99f).ToString("0.00"));

            sb.AppendLine("  worst frame: expected=" + _worstExpected.ToString("0.00")
                          + " got " + _worstGot.ToString("0.00")
                          + " (" + (_worstRatio * 100f).ToString("0") + "%)  " + _worstWhere);

            Reset();
        }
    }
}
