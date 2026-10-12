using System.Collections.Generic;
using UnityEngine;

namespace Hapbeat.DemoSwitch.Editor
{
    /// <summary>
    /// Frame-to-frame smoothness of a posed hand: each joint's position and rotation relative to the hand's authored pose
    /// (its wrist joint and rotation, so the push and the turn of the whole hand drop out), one sample per frame. A
    /// joint jitters when its velocity (linear or angular, per frame) turns sharply from one frame to the next (more
    /// than <see cref="TurnDegrees"/>; a turn past 90 degrees is a reversal) while both frames move it more than a noise
    /// floor, and when that velocity jumps (its second difference). A smooth demonstration (DemoTutorialHand) has no sharp turns: its
    /// joints sweep along arcs (a few degrees of turn per frame), and its intended direction changes pass through a hold.
    /// </summary>
    public sealed class DemoHandJitter
    {
        /// <summary>Per-frame moves smaller than this (metres / degrees) are noise, never counted as a reversal.</summary>
        public const float PositionFloor = .0002f;
        public const float AngleFloor = .1f;
        /// <summary>A velocity turning more than this between two frames is a sharp turn (jitter).</summary>
        public const float TurnDegrees = 45f;

        public struct Joint
        {
            public string Name;
            /// <summary>Frames whose linear velocity turns more than <see cref="TurnDegrees"/> from the previous frame's.</summary>
            public int PositionTurns;
            /// <summary>Frames whose angular velocity turns more than <see cref="TurnDegrees"/> from the previous frame's.</summary>
            public int AngleTurns;
            /// <summary>Frames whose linear velocity points against the previous frame's.</summary>
            public int PositionReversals;
            /// <summary>Frames whose angular velocity points against the previous frame's.</summary>
            public int AngleReversals;
            /// <summary>Largest change of linear velocity between frames (millimetres per frame).</summary>
            public float MaxPositionJerkMm;
            /// <summary>Largest change of angular velocity between frames (degrees per frame).</summary>
            public float MaxAngleJerkDegrees;
            /// <summary>Largest move in one frame (millimetres).</summary>
            public float MaxStepMm;

            public int Reversals => PositionReversals + AngleReversals;
            /// <summary>The jitter count: sharp turns of either velocity (reversals included).</summary>
            public int Turns => PositionTurns + AngleTurns;
        }

        private readonly string[] _names;
        private readonly List<Vector3[]> _positions = new List<Vector3[]>();
        private readonly List<Quaternion[]> _rotations = new List<Quaternion[]>();

        public DemoHandJitter(IReadOnlyList<string> names)
        {
            _names = new string[names.Count];
            for (var i = 0; i < _names.Length; i++) _names[i] = names[i];
        }

        public int Frames => _positions.Count;

        /// <summary>One frame: <paramref name="joints"/> (null entries skipped) relative to the hand's <paramref name="wrist"/> and <paramref name="hand"/>.</summary>
        public void Add(IReadOnlyList<Transform> joints, Vector3 wrist, Quaternion hand)
        {
            var inverse = Quaternion.Inverse(hand);
            var positions = new Vector3[_names.Length];
            var rotations = new Quaternion[_names.Length];
            for (var i = 0; i < _names.Length; i++)
            {
                var joint = joints[i];
                if (joint == null)
                {
                    rotations[i] = Quaternion.identity;
                    continue;
                }

                positions[i] = inverse * (joint.position - wrist);
                rotations[i] = inverse * joint.rotation;
            }

            _positions.Add(positions);
            _rotations.Add(rotations);
        }

        /// <summary>The hand-relative position of joint <paramref name="joint"/> in frame <paramref name="frame"/>.</summary>
        public Vector3 Position(int frame, int joint) => _positions[frame][joint];

        public Joint[] Analyse()
        {
            var result = new Joint[_names.Length];
            for (var j = 0; j < _names.Length; j++)
            {
                var r = new Joint { Name = _names[j] };
                Vector3? lastStep = null;
                Vector3? lastSpin = null;
                for (var f = 1; f < _positions.Count; f++)
                {
                    var step = _positions[f][j] - _positions[f - 1][j];
                    var spin = Spin(_rotations[f - 1][j], _rotations[f][j]);
                    r.MaxStepMm = Mathf.Max(r.MaxStepMm, step.magnitude * 1000f);
                    if (lastStep.HasValue)
                    {
                        r.MaxPositionJerkMm = Mathf.Max(r.MaxPositionJerkMm, (step - lastStep.Value).magnitude * 1000f);
                        if (step.magnitude > PositionFloor && lastStep.Value.magnitude > PositionFloor)
                        {
                            if (Vector3.Angle(step, lastStep.Value) > TurnDegrees) r.PositionTurns++;
                            if (Vector3.Dot(step, lastStep.Value) < 0f) r.PositionReversals++;
                        }
                    }

                    if (lastSpin.HasValue)
                    {
                        r.MaxAngleJerkDegrees = Mathf.Max(r.MaxAngleJerkDegrees, (spin - lastSpin.Value).magnitude);
                        if (spin.magnitude > AngleFloor && lastSpin.Value.magnitude > AngleFloor)
                        {
                            if (Vector3.Angle(spin, lastSpin.Value) > TurnDegrees) r.AngleTurns++;
                            if (Vector3.Dot(spin, lastSpin.Value) < 0f) r.AngleReversals++;
                        }
                    }

                    lastStep = step;
                    lastSpin = spin;
                }

                result[j] = r;
            }

            return result;
        }

        /// <summary>The turn from <paramref name="from"/> to <paramref name="to"/> as an axis scaled by its angle (degrees, the short way).</summary>
        public static Vector3 Spin(Quaternion from, Quaternion to)
        {
            var delta = to * Quaternion.Inverse(from);
            if (delta.w < 0f) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            delta.ToAngleAxis(out var angle, out var axis);
            if (angle > 180f) angle -= 360f;
            return float.IsNaN(axis.x) || float.IsInfinity(axis.x) ? Vector3.zero : axis * angle;
        }
    }
}
