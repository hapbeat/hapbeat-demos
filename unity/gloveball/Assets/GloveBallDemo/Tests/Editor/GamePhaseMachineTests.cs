using System.Collections.Generic;
using GloveBallDemo.Core;
using NUnit.Framework;

namespace GloveBallDemo.Tests
{
    public class GamePhaseMachineTests
    {
        private static GamePhaseMachine NewMachine(out List<GamePhase> log)
        {
            var machine = new GamePhaseMachine(WaveSchedule.WaveCount);
            var captured = new List<GamePhase>();
            machine.PhaseChanged += (_, next) => captured.Add(next);
            log = captured;
            return machine;
        }

        [Test]
        public void StartsCountdownImmediately()
        {
            var machine = new GamePhaseMachine(3);
            Assert.AreEqual(GamePhase.Countdown, machine.Phase);
            Assert.AreEqual(GamePhaseMachine.CountdownDuration, machine.PhaseRemaining);
            Assert.AreEqual(0, machine.WaveIndex);
        }

        [Test]
        public void FullRunVisitsEveryWaveThenResultThenRestartsAtCountdown()
        {
            var machine = NewMachine(out var log);

            // 0.5 s steps for 200 s covers countdown + 3 waves + interludes + result + idle.
            for (var i = 0; i < 400; i++)
            {
                machine.Tick(0.5f);
            }

            var expected = new List<GamePhase>();
            for (var w = 0; w < WaveSchedule.WaveCount; w++)
            {
                expected.Add(GamePhase.Wave);
                expected.Add(GamePhase.Interlude);
            }

            expected.Add(GamePhase.Result);
            expected.Add(GamePhase.Countdown);

            // The loop runs long enough to start a second cycle; only assert the first one.
            CollectionAssert.AreEqual(expected, log.GetRange(0, expected.Count));
        }

        [Test]
        public void WaveIndexAdvancesOncePerWave()
        {
            var machine = new GamePhaseMachine(WaveSchedule.WaveCount);
            var seen = new List<int>();
            machine.PhaseChanged += (_, next) =>
            {
                if (next == GamePhase.Wave)
                {
                    seen.Add(machine.WaveIndex);
                }
            };

            for (var i = 0; i < 400; i++)
            {
                machine.Tick(0.5f);
            }

            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, seen.GetRange(0, WaveSchedule.WaveCount));
        }

        [Test]
        public void WavePhaseUsesScheduledDuration()
        {
            var machine = new GamePhaseMachine(WaveSchedule.WaveCount);
            machine.Tick(GamePhaseMachine.CountdownDuration);
            Assert.AreEqual(GamePhase.Wave, machine.Phase);
            Assert.AreEqual(WaveSchedule.Get(0).Duration, machine.PhaseDuration, 1e-3f);
        }

        [Test]
        public void CustomRoundDurationAndIntermissionAreUsed()
        {
            var machine = new GamePhaseMachine(3, _ => 30f, 3f);
            machine.Tick(GamePhaseMachine.CountdownDuration);
            Assert.AreEqual(GamePhase.Wave, machine.Phase);
            Assert.AreEqual(30f, machine.PhaseDuration);
            machine.Tick(30f);
            Assert.AreEqual(GamePhase.Interlude, machine.Phase);
            Assert.AreEqual(3f, machine.PhaseDuration);
        }

        [Test]
        public void OversizedTickAdvancesMultiplePhases()
        {
            var machine = NewMachine(out var log);
            // Longer than countdown + wave 0 combined.
            machine.Tick(GamePhaseMachine.CountdownDuration + WaveSchedule.Get(0).Duration + 0.1f);
            Assert.AreEqual(GamePhase.Interlude, machine.Phase);
            CollectionAssert.AreEqual(
                new[] { GamePhase.Wave, GamePhase.Interlude }, log);
        }

        [Test]
        public void NonPositiveTickIsANoOp()
        {
            var machine = new GamePhaseMachine(3);
            machine.Tick(0f);
            machine.Tick(-5f);
            Assert.AreEqual(GamePhase.Countdown, machine.Phase);
            Assert.AreEqual(0f, machine.PhaseElapsed, 1e-6f);
        }

        [Test]
        public void PhaseRemainingCountsDown()
        {
            var machine = new GamePhaseMachine(3);
            machine.Tick(1f);
            Assert.AreEqual(GamePhaseMachine.CountdownDuration - 1f, machine.PhaseRemaining, 1e-3f);
        }
    }
}
