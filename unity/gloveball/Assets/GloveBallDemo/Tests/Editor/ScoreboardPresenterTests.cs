using System.Reflection;
using GloveBallDemo.Core;
using GloveBallDemo.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace GloveBallDemo.Tests
{
    public sealed class ScoreboardPresenterTests
    {
        [Test]
        public void EndlessModeNeverShowsWaveOrResultStateAcrossEveryWavePhase()
        {
            var root = new GameObject("Scoreboard");
            var presenter = root.AddComponent<ScoreboardPresenter>();
            var score = new ScoreModel();
            var wave = CreateText(root.transform, "Wave");
            var message = CreateText(root.transform, "Message");
            SetField(presenter, "_scoreText", CreateText(root.transform, "Score"));
            SetField(presenter, "_comboText", CreateText(root.transform, "Combo"));
            SetField(presenter, "_waveText", wave);
            SetField(presenter, "_messageText", message);

            try
            {
                var phase = new GamePhaseMachine(1);
                RefreshEndless(presenter, score, phase);
                RefreshEndless(presenter, score, phase);
                phase.Tick(GamePhaseMachine.CountdownDuration);
                RefreshEndless(presenter, score, phase);
                phase.Tick(WaveSchedule.Get(0).Duration);
                RefreshEndless(presenter, score, phase);
                phase.Tick(GamePhaseMachine.InterludeDuration);
                RefreshEndless(presenter, score, phase);

                Assert.That(phase.Phase, Is.EqualTo(GamePhase.Result));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void RefreshEndless(ScoreboardPresenter presenter, ScoreModel score, GamePhaseMachine phase)
        {
            presenter.Refresh(score, phase, DemoPlayMode.EndlessRandom);
            var wave = GetField<Text>(presenter, "_waveText");
            var message = GetField<Text>(presenter, "_messageText");
            Assert.That(wave.text, Is.EqualTo("ENDLESS RANDOM"));
            Assert.That(message.text, Does.Not.Contain("WAVE"));
            Assert.That(message.text, Does.Not.Contain("RESULT"));
        }

        private static Text CreateText(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<Text>();
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private static T GetField<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }
}
