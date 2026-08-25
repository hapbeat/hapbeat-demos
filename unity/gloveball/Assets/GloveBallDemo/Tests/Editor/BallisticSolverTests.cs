using GloveBallDemo.Core;
using NUnit.Framework;
using UnityEngine;

namespace GloveBallDemo.Tests
{
    public class BallisticSolverTests
    {
        private const float G = BallisticSolver.DefaultGravity;
        private const float Tolerance = 0.1f;

        private static readonly Vector3[] Origins =
        {
            new Vector3(-3f, 1.2f, 9f),
            new Vector3(0f, 1.2f, 10.5f),
            new Vector3(3f, 1.2f, 12f)
        };

        private static readonly Vector3[] Targets =
        {
            new Vector3(0f, 1.4f, -3.5f),
            new Vector3(-0.85f, 1.1f, -3.5f),
            new Vector3(0.85f, 1.7f, -4f),
            new Vector3(2f, 0.2f, 0f)
        };

        [Test]
        public void SpeedSolutionLandsOnTarget([Values(false, true)] bool highArc)
        {
            foreach (var origin in Origins)
            {
                foreach (var target in Targets)
                {
                    Assert.IsTrue(
                        BallisticSolver.TrySolveWithSpeed(origin, target, 16f, G, highArc, out var v),
                        $"16 m/s should reach {target} from {origin}");

                    var t = BallisticSolver.TimeOfFlight(origin, target, v);
                    var landing = BallisticSolver.Predict(origin, v, t, G);
                    Assert.Less(
                        Vector3.Distance(landing, target),
                        Tolerance,
                        $"origin={origin} target={target} highArc={highArc} landing={landing}");
                }
            }
        }

        [Test]
        public void SpeedSolutionPreservesTheRequestedMuzzleSpeed()
        {
            Assert.IsTrue(BallisticSolver.TrySolveWithSpeed(Origins[0], Targets[0], 14f, G, false, out var v));
            Assert.AreEqual(14f, v.magnitude, 1e-3f);
        }

        [Test]
        public void HighArcClimbsMoreThanLowArc()
        {
            Assert.IsTrue(BallisticSolver.TrySolveWithSpeed(Origins[1], Targets[0], 14f, G, false, out var low));
            Assert.IsTrue(BallisticSolver.TrySolveWithSpeed(Origins[1], Targets[0], 14f, G, true, out var high));
            Assert.Greater(high.y, low.y);
        }

        [Test]
        public void SpeedTooLowIsReportedAsUnreachable()
        {
            Assert.IsFalse(
                BallisticSolver.TrySolveWithSpeed(Origins[1], Targets[0], 1f, G, false, out _),
                "1 m/s cannot cross a 14 m court");
        }

        [Test]
        public void MinimumSpeedIsTheThresholdOfReachability()
        {
            foreach (var origin in Origins)
            {
                foreach (var target in Targets)
                {
                    var minimum = BallisticSolver.MinimumSpeedToReach(origin, target, G);
                    Assert.IsTrue(
                        BallisticSolver.TrySolveWithSpeed(origin, target, minimum * 1.01f, G, false, out _),
                        $"just above the minimum should reach {target} from {origin}");
                    Assert.IsFalse(
                        BallisticSolver.TrySolveWithSpeed(origin, target, minimum * 0.99f, G, false, out _),
                        $"just below the minimum should not reach {target} from {origin}");
                }
            }
        }

        [Test]
        public void ClampedSolutionRaisesAnUnreachableSpeedAndSaysSo()
        {
            var origin = Origins[1];
            var target = Targets[0];
            var minimum = BallisticSolver.MinimumSpeedToReach(origin, target, G);

            Assert.IsTrue(
                BallisticSolver.TrySolveWithSpeedClamped(origin, target, 2f, G, false, out var v, out var usedSpeed),
                "the clamp should rescue a shot that is merely too slow");
            Assert.GreaterOrEqual(usedSpeed, minimum, "clamped speed still cannot reach");
            Assert.AreEqual(usedSpeed, v.magnitude, 1e-3f);

            var t = BallisticSolver.TimeOfFlight(origin, target, v);
            Assert.Less(Vector3.Distance(BallisticSolver.Predict(origin, v, t, G), target), Tolerance);
        }

        [Test]
        public void ClampedSolutionLeavesAReachableSpeedAlone()
        {
            Assert.IsTrue(
                BallisticSolver.TrySolveWithSpeedClamped(Origins[1], Targets[0], 16f, G, false, out var v, out var usedSpeed));
            Assert.AreEqual(16f, usedSpeed, 1e-3f);
            Assert.AreEqual(16f, v.magnitude, 1e-3f);
        }

        [Test]
        public void ElevationSolutionLandsOnTarget([Values(25f, 35f, 50f)] float elevation)
        {
            foreach (var origin in Origins)
            {
                foreach (var target in Targets)
                {
                    if (!BallisticSolver.TrySolveWithElevation(origin, target, elevation, G, out var v))
                    {
                        // A flat angle cannot gain height over a long run; that is a valid refusal.
                        continue;
                    }

                    var t = BallisticSolver.TimeOfFlight(origin, target, v);
                    var landing = BallisticSolver.Predict(origin, v, t, G);
                    Assert.Less(
                        Vector3.Distance(landing, target),
                        Tolerance,
                        $"origin={origin} target={target} elevation={elevation} landing={landing}");
                }
            }
        }

        [Test]
        public void ElevationSolutionMatchesTheRequestedAngle()
        {
            Assert.IsTrue(BallisticSolver.TrySolveWithElevation(Origins[0], Targets[0], 40f, G, out var v));
            var angle = Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg;
            Assert.AreEqual(40f, angle, 1e-2f);
        }

        [Test]
        public void ElevationRefusesImpossibleAngles()
        {
            Assert.IsFalse(BallisticSolver.TrySolveWithElevation(Origins[0], Targets[0], 0f, G, out _));
            Assert.IsFalse(BallisticSolver.TrySolveWithElevation(Origins[0], Targets[0], 90f, G, out _));
            // A 5 degree launch cannot climb 9 m over a 1 m run, whatever the speed.
            Assert.IsFalse(
                BallisticSolver.TrySolveWithElevation(new Vector3(0f, 1f, 0f), new Vector3(0f, 10f, 1f), 5f, G, out _));
        }

        [Test]
        public void PredictFollowsSimpleFreeFall()
        {
            var p = BallisticSolver.Predict(Vector3.zero, Vector3.zero, 2f, G);
            Assert.AreEqual(-0.5f * G * 4f, p.y, 1e-4f);
        }
    }
}
