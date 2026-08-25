using System.Collections.Generic;
using GloveBallDemo.Core;
using NUnit.Framework;
using UnityEngine;

namespace GloveBallDemo.Tests
{
    public class LaunchPlannerTests
    {
        private const float Dt = 1f / 60f;

        private static List<LaunchOrder> RunWave(int seed, WaveDefinition wave, int launchers, float seconds, int activeBalls = 0)
        {
            var planner = new LaunchPlanner(seed);
            planner.BeginWave(wave, launchers);

            var orders = new List<LaunchOrder>();
            var steps = (int)(seconds / Dt);
            for (var i = 0; i < steps; i++)
            {
                if (planner.Tick(Dt, activeBalls, out var order))
                {
                    orders.Add(order);
                }
            }

            return orders;
        }

        [Test]
        public void SameSeedProducesTheSameSequence()
        {
            var wave = WaveSchedule.Get(1);
            var a = RunWave(1234, wave, 3, wave.Duration);
            var b = RunWave(1234, wave, 3, wave.Duration);

            Assert.AreEqual(a.Count, b.Count);
            for (var i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].LauncherIndex, b[i].LauncherIndex, $"launcher #{i}");
                Assert.AreEqual(a[i].Speed, b[i].Speed, 1e-6f, $"speed #{i}");
                Assert.AreEqual(a[i].Aim, b[i].Aim, $"aim #{i}");
                Assert.AreEqual(a[i].LateralOffset, b[i].LateralOffset, 1e-6f, $"lateral #{i}");
                Assert.AreEqual(a[i].DepthOffset, b[i].DepthOffset, 1e-6f, $"depth #{i}");
            }
        }

        [Test]
        public void DifferentSeedsDiverge()
        {
            var wave = WaveSchedule.Get(2);
            var a = RunWave(1, wave, 3, wave.Duration);
            var b = RunWave(2, wave, 3, wave.Duration);

            var identical = a.Count == b.Count;
            if (identical)
            {
                for (var i = 0; i < a.Count; i++)
                {
                    if (a[i].LauncherIndex != b[i].LauncherIndex ||
                        !UnityEngine.Mathf.Approximately(a[i].Speed, b[i].Speed))
                    {
                        identical = false;
                        break;
                    }
                }
            }

            Assert.IsFalse(identical, "two seeds produced an identical wave");
        }

        [Test]
        public void SpeedsStayInsideTheWaveRange()
        {
            foreach (var wave in WaveSchedule.Waves)
            {
                var orders = RunWave(77, wave, 3, wave.Duration);
                foreach (var order in orders)
                {
                    Assert.GreaterOrEqual(order.Speed, wave.MinSpeed - 1e-4f);
                    Assert.LessOrEqual(order.Speed, wave.MaxSpeed + 1e-4f);
                }
            }
        }

        [Test]
        public void LauncherIndexStaysInsideTheWaveBudget()
        {
            var wave = WaveSchedule.Get(0); // 2 launchers even though 3 exist in the scene
            var orders = RunWave(5, wave, 3, wave.Duration);
            CollectionAssert.IsNotEmpty(orders);
            foreach (var order in orders)
            {
                Assert.GreaterOrEqual(order.LauncherIndex, 0);
                Assert.Less(order.LauncherIndex, wave.LauncherCount);
            }
        }

        [Test]
        public void LauncherIndexNeverRepeatsBackToBack()
        {
            var wave = WaveSchedule.Get(2);
            var orders = RunWave(31, wave, 3, wave.Duration);
            for (var i = 1; i < orders.Count; i++)
            {
                Assert.AreNotEqual(orders[i - 1].LauncherIndex, orders[i].LauncherIndex, $"repeat at #{i}");
            }
        }

        [Test]
        public void ShotCountMatchesTheIntervalBudget()
        {
            foreach (var wave in WaveSchedule.Waves)
            {
                var orders = RunWave(9, wave, 3, wave.Duration);
                var maxShots = (int)(wave.Duration / wave.MinInterval) + 1;
                var minShots = (int)(wave.Duration / wave.MaxInterval) - 1;
                Assert.LessOrEqual(orders.Count, maxShots, "fired faster than MinInterval allows");
                Assert.GreaterOrEqual(orders.Count, minShots, "fired slower than MaxInterval allows");
            }
        }

        [Test]
        public void ConcurrencyCapSuppressesShots()
        {
            var wave = WaveSchedule.Get(2);
            var saturated = RunWave(9, wave, 3, wave.Duration, wave.MaxActiveBalls);
            CollectionAssert.IsEmpty(saturated, "planner fired while the ball cap was already reached");
        }

        [Test]
        public void SuppressedShotFiresImmediatelyOnceSpaceFreesUp()
        {
            var wave = WaveSchedule.Get(0);
            var planner = new LaunchPlanner(3);
            planner.BeginWave(wave, 3);

            // Run past the longest possible interval with the cap held closed.
            var steps = (int)((wave.MaxInterval + 1f) / Dt);
            for (var i = 0; i < steps; i++)
            {
                Assert.IsFalse(planner.Tick(Dt, wave.MaxActiveBalls, out _));
            }

            Assert.IsTrue(planner.Tick(Dt, 0, out _), "shot did not resume when the cap cleared");
        }

        [Test]
        public void EveryOrderHasBoundedPlayerLocalAimOffsets()
        {
            var wave = WaveSchedule.Get(2);
            var orders = RunWave(4242, wave, 3, wave.Duration * 4f);
            foreach (var order in orders)
            {
                Assert.That(order.LateralOffset, Is.InRange(-LaunchPlanner.LateralOffsetLimit, LaunchPlanner.LateralOffsetLimit));
                Assert.That(order.DepthOffset, Is.InRange(-LaunchPlanner.DepthOffsetLimit, LaunchPlanner.DepthOffsetLimit));
            }
        }

        [Test]
        public void PlayerLocalAimOffsetsRotateWithPlayerYaw()
        {
            var local = LaunchAimPoint.Apply(Vector3.zero, Vector3.forward, Vector3.right, 0.85f, -0.75f);
            var turned = LaunchAimPoint.Apply(Vector3.zero, Vector3.right, Vector3.back, 0.85f, -0.75f);

            Assert.That(local, Is.EqualTo(new Vector3(0.85f, 0f, -0.75f)));
            Assert.That(turned, Is.EqualTo(new Vector3(-0.75f, 0f, -0.85f)));
        }

        [Test]
        public void EveryWaveOpensWithADirectShot()
        {
            for (var waveIndex = 0; waveIndex < WaveSchedule.WaveCount; waveIndex++)
            {
                var wave = WaveSchedule.Get(waveIndex);
                for (var seed = 0; seed < 50; seed++)
                {
                    var orders = RunWave(seed, wave, 3, wave.Duration);
                    CollectionAssert.IsNotEmpty(orders, $"wave {waveIndex + 1} seed {seed} fired nothing");
                    Assert.AreEqual(
                        AimKind.Direct,
                        orders[0].Aim,
                        $"wave {waveIndex + 1} seed {seed} opened with {orders[0].Aim}");
                }
            }
        }

        [Test]
        public void OpeningWaveAimsAtThePlayerAtLeastFortyPercentOfTheTime()
        {
            var share = MeasureDirectShare(WaveSchedule.Get(0), out var total);
            Assert.Greater(total, 500, "not enough shots sampled to judge the ratio");
            Assert.GreaterOrEqual(
                share,
                WaveSchedule.Wave1MinimumDirectWeight,
                $"only {share:P0} of opening-wave shots were aimed at the player");
        }

        [Test]
        public void ShotsFollowTheWavesDirectWeight()
        {
            // Checked on the last wave as well as the first, so a regression that pins every wave
            // to one weight cannot hide behind the opening wave's floor.
            var wave = WaveSchedule.Get(WaveSchedule.WaveCount - 1);
            var share = MeasureDirectShare(wave, out _);
            Assert.That(share, Is.EqualTo(wave.DirectWeight).Within(0.06f), "direct share drifted from the wave weight");
        }

        /// <summary>
        /// Share of Direct shots over many seeds. A single wave is a dozen shots - far too few
        /// for its own ratio to say anything about the configured weight.
        /// </summary>
        private static float MeasureDirectShare(WaveDefinition wave, out int total)
        {
            var direct = 0;
            total = 0;

            for (var seed = 0; seed < 200; seed++)
            {
                foreach (var order in RunWave(seed, wave, 3, wave.Duration))
                {
                    total++;
                    if (order.Aim == AimKind.Direct)
                    {
                        direct++;
                    }
                }
            }

            return total == 0 ? 0f : (float)direct / total;
        }

        [Test]
        public void BeginWaveResetsTheLaunchCount()
        {
            var wave = WaveSchedule.Get(0);
            var planner = new LaunchPlanner(11);
            planner.BeginWave(wave, 3);
            var steps = (int)(wave.Duration / Dt);
            for (var i = 0; i < steps; i++)
            {
                planner.Tick(Dt, 0, out _);
            }

            Assert.Greater(planner.LaunchCount, 0);
            planner.BeginWave(wave, 3);
            Assert.AreEqual(0, planner.LaunchCount);
        }

        [Test]
        public void EndlessPlannerIsSeededAndKeepsOrdersWithinItsConfiguredRange()
        {
            var a = new EndlessLaunchPlanner(73, 0.45f, 1.25f, 13f, 19f);
            var b = new EndlessLaunchPlanner(73, 0.45f, 1.25f, 13f, 19f);
            a.Begin(3);
            b.Begin(3);
            var seen = 0;
            for (var i = 0; i < 3000; i++)
            {
                var dueA = a.Tick(Dt, 0, 16, out var orderA);
                var dueB = b.Tick(Dt, 0, 16, out var orderB);
                Assert.That(dueA, Is.EqualTo(dueB));
                if (!dueA) continue;
                seen++;
                Assert.That(orderA.LauncherIndex, Is.InRange(0, 2));
                Assert.That(orderA.Speed, Is.InRange(13f, 19f));
                Assert.That(orderA.Aim, Is.EqualTo(AimKind.Direct).Or.EqualTo(AimKind.Lob));
                Assert.That(orderA.LauncherIndex, Is.EqualTo(orderB.LauncherIndex));
                Assert.That(orderA.Speed, Is.EqualTo(orderB.Speed));
                Assert.That(orderA.Aim, Is.EqualTo(orderB.Aim));
                a.Commit();
                b.Commit();
            }

            Assert.That(seen, Is.GreaterThan(20));
        }

        [Test]
        public void EndlessPlannerKeepsItsDueOrderUntilALauncherAcceptsIt()
        {
            var planner = new EndlessLaunchPlanner(55, 0.01f, 0.01f, 13f, 19f);
            planner.Begin(3);
            Assert.That(planner.Tick(0.02f, 0, 16, out var due), Is.True);

            Assert.That(planner.Tick(10f, 0, 16, out var retry), Is.True);
            Assert.That(retry.LauncherIndex, Is.EqualTo(due.LauncherIndex));
            Assert.That(retry.Speed, Is.EqualTo(due.Speed));
            Assert.That(retry.Aim, Is.EqualTo(due.Aim));

            planner.Commit();
            Assert.That(planner.Tick(0f, 0, 16, out _), Is.False);
        }
    }
}
