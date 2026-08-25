using GloveBallDemo.Core;
using NUnit.Framework;
using UnityEngine;

namespace GloveBallDemo.Tests
{
    /// <summary>
    /// Guards the wave tuning against the two ways it can silently stop working: speeds that
    /// cannot physically reach the player (the launcher would clamp them and the difficulty
    /// curve would flatten out), and a curve that stops going up.
    /// </summary>
    public class WaveScheduleTests
    {
        /// <summary>Muzzle and chest are at much the same height, so the shot is effectively flat.</summary>
        private static Vector3 PlayerAimPoint =>
            new Vector3(0f, 0f, -WaveSchedule.LauncherToPlayerDistance);

        [Test]
        public void EveryWaveSpeedCanReachThePlayer()
        {
            var minimum = BallisticSolver.MinimumSpeedToReach(
                Vector3.zero, PlayerAimPoint, BallisticSolver.DefaultGravity);

            for (var i = 0; i < WaveSchedule.WaveCount; i++)
            {
                var wave = WaveSchedule.Get(i);
                Assert.Greater(
                    wave.MinSpeed,
                    minimum,
                    $"wave {i + 1} can ask for {wave.MinSpeed} m/s, below the {minimum:0.00} m/s needed to arrive");
            }
        }

        [Test]
        public void SlowestShotStillArrivesWithinTheReactionWindow()
        {
            var wave = WaveSchedule.Get(0);
            Assert.IsTrue(BallisticSolver.TrySolveWithSpeed(
                Vector3.zero, PlayerAimPoint, wave.MinSpeed, BallisticSolver.DefaultGravity, false, out var velocity));

            var flight = BallisticSolver.TimeOfFlight(Vector3.zero, PlayerAimPoint, velocity);
            // Slower than this and the wave drags; faster and the opening wave is unreadable.
            Assert.That(flight, Is.InRange(0.9f, 1.6f), "opening wave flight time");
        }

        [Test]
        public void FastestShotIsStillReactable()
        {
            var wave = WaveSchedule.Get(WaveSchedule.WaveCount - 1);
            Assert.IsTrue(BallisticSolver.TrySolveWithSpeed(
                Vector3.zero, PlayerAimPoint, wave.MaxSpeed, BallisticSolver.DefaultGravity, false, out var velocity));

            var flight = BallisticSolver.TimeOfFlight(Vector3.zero, PlayerAimPoint, velocity);
            Assert.Greater(flight, 0.6f, "final wave leaves no time to react at all");
        }

        [Test]
        public void SpeedBandsRiseWaveOverWave()
        {
            for (var i = 1; i < WaveSchedule.WaveCount; i++)
            {
                var previous = WaveSchedule.Get(i - 1);
                var current = WaveSchedule.Get(i);
                Assert.Greater(current.MinSpeed, previous.MinSpeed, $"wave {i + 1} min speed");
                Assert.Greater(current.MaxSpeed, previous.MaxSpeed, $"wave {i + 1} max speed");
            }
        }

        [Test]
        public void SpeedBandsAreWellFormed()
        {
            for (var i = 0; i < WaveSchedule.WaveCount; i++)
            {
                var wave = WaveSchedule.Get(i);
                Assert.LessOrEqual(wave.MinSpeed, wave.MaxSpeed, $"wave {i + 1} speed band");
                Assert.LessOrEqual(wave.MinInterval, wave.MaxInterval, $"wave {i + 1} interval band");
                Assert.That(wave.DirectWeight, Is.InRange(0f, 1f), $"wave {i + 1} direct weight");
            }
        }

        [Test]
        public void OpeningWaveFavoursDirectShots()
        {
            Assert.GreaterOrEqual(WaveSchedule.Get(0).DirectWeight, WaveSchedule.Wave1MinimumDirectWeight);
        }

        [TestCase(0, 1.25f, 1.65f, 13f, 15f, 5, 2, 0.70f)]
        [TestCase(1, 0.85f, 1.15f, 15f, 18f, 7, 3, 0.65f)]
        [TestCase(2, 0.55f, 0.85f, 17f, 21f, 9, 3, 0.60f)]
        public void WavesMatchSoloCombatTuning(
            int index, float minInterval, float maxInterval, float minSpeed, float maxSpeed,
            int maxActive, int launcherCount, float directWeight)
        {
            var wave = WaveSchedule.Get(index);
            Assert.That(wave.MinInterval, Is.EqualTo(minInterval));
            Assert.That(wave.MaxInterval, Is.EqualTo(maxInterval));
            Assert.That(wave.MinSpeed, Is.EqualTo(minSpeed));
            Assert.That(wave.MaxSpeed, Is.EqualTo(maxSpeed));
            Assert.That(wave.MaxActiveBalls, Is.EqualTo(maxActive));
            Assert.That(wave.LauncherCount, Is.EqualTo(launcherCount));
            Assert.That(wave.DirectWeight, Is.EqualTo(directWeight));
        }
    }
}
