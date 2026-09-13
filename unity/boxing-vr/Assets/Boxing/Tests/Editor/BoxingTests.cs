using Hapbeat.Boxing.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingRulesTests
    {
        private BoxingTuning tuning;
        [SetUp] public void Setup() => tuning = ScriptableObject.CreateInstance<BoxingTuning>();
        [TearDown] public void Cleanup() => Object.DestroyImmediate(tuning);
        [Test] public void RelativeVelocityIsDifferenceNotSum()
        {
            Assert.That(BoxingCollision.RelativeSpeed(Vector3.zero, Vector3.right, Vector3.zero, Vector3.right, 1), Is.Zero);
            Assert.That(BoxingCollision.RelativeSpeed(Vector3.zero, Vector3.right, Vector3.zero, Vector3.left, 1), Is.EqualTo(2));
        }
        [Test] public void FastCrossingBetweenFramesStillHits()
        {
            Assert.That(BoxingCollision.Sweep(Vector3.back, Vector3.forward, 0.1f, Vector3.zero, Vector3.zero, 0.1f, out float time), Is.True);
            Assert.That(time, Is.EqualTo(0.4f).Within(0.00001));
        }
        [Test] public void ParallelMotionDoesNotCreateAnImpact()
        {
            Assert.That(BoxingCollision.Sweep(Vector3.zero, Vector3.forward, 0.1f, Vector3.right, Vector3.right + Vector3.forward, 0.1f, out _), Is.False);
        }
        [Test] public void GainIsMonotonicBoundedAndRejectsInvalidSpeed()
        {
            Assert.That(tuning.Gain(0), Is.Zero); Assert.That(tuning.Gain(float.NaN), Is.Zero);
            float last = 0;
            for (float speed = 0; speed <= 20; speed += 0.1f)
            { float gain = tuning.Gain(speed); Assert.That(gain, Is.GreaterThanOrEqualTo(last)); Assert.That(gain, Is.LessThanOrEqualTo(tuning.maximumGain)); last = gain; }
            Assert.That(tuning.Gain(5), Is.GreaterThan(tuning.Gain(0.6f) * 2));
        }
        [Test] public void WeakHardThresholdAndContinuousModeAreIndependentOfGain()
        {
            tuning.hardHitSpeed = 2.5f;
            Assert.That(tuning.IsHard(2.49f), Is.False); Assert.That(tuning.IsHard(2.5f), Is.True);
            float gain = tuning.Gain(4); tuning.impactMode = ImpactMode.Continuous;
            Assert.That(tuning.IsHard(4), Is.False); Assert.That(tuning.Gain(4), Is.EqualTo(gain));
        }
        [Test] public void CountdownPauseAndRoundHaveExactLifecycle()
        {
            var round = new BoxingRound(); round.Start(90); round.Tick(2, false);
            Assert.That(round.Phase, Is.EqualTo(BoxingPhase.Countdown)); round.Tick(30, true);
            Assert.That(round.Countdown, Is.EqualTo(1)); round.Tick(1, false);
            Assert.That(round.Phase, Is.EqualTo(BoxingPhase.Fighting)); Assert.That(round.TimeLeft, Is.EqualTo(90));
            round.Tick(90, false); Assert.That(round.Phase, Is.EqualTo(BoxingPhase.Results));
        }
        [Test] public void EnemyUsesBothHandsAndMovesWithinOneSmallStep()
        {
            var opponent = new BoxingOpponent(tuning); int left = 0, right = 0, id = 0;
            for (int i = 0; i < 9000; i++)
            {
                opponent.Tick(0.01f, Vector3.up * 1.65f, true);
                Assert.That(Mathf.Abs(opponent.Root.x), Is.LessThanOrEqualTo(0.161f));
                Assert.That(opponent.Root.z, Is.InRange(0.97f, 1.19f));
                if (opponent.AttackId != id) { id = opponent.AttackId; if (opponent.AttackLeft) left++; else right++; }
            }
            Assert.That(left, Is.GreaterThan(5)); Assert.That(right, Is.GreaterThan(5));
        }
    }

    public sealed class BoxingSceneTests
    {
        private BoxingGame game;
        private BoxingTuning originalTuning;
        [SetUp] public void Setup()
        {
            EditorSceneManager.OpenScene(BoxingProject.ScenePath);
            game = Object.FindAnyObjectByType<BoxingGame>();
            originalTuning = game.tuning; game.tuning = Object.Instantiate(originalTuning);
            game.feedback.forceSilent = true; game.feedback.sdkRoot.SetActive(false);
            game.Initialize(); game.Round.Start(90);
        }
        [TearDown] public void Cleanup()
        {
            var temp = game.tuning; game.tuning = originalTuning; Object.DestroyImmediate(temp);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        }
        public static BoxerPose Pose(bool guard = false) => new BoxerPose {
            valid = true, head = new Vector3(0, 1.65f, 0), left = new Vector3(guard ? -0.12f : -0.45f, guard ? 1.65f : 1.1f, 0.3f),
            right = new Vector3(guard ? 0.12f : 0.45f, guard ? 1.65f : 1.1f, 0.3f),
            headRotation = Quaternion.identity, leftRotation = Quaternion.identity, rightRotation = Quaternion.identity, leftClosed = true, rightClosed = true };
        private void Advance(float seconds, BoxerPose pose) { for (int i = 0; i < seconds * 90; i++) game.Simulate(1f / 90, pose); }
        [Test] public void UnguardedHeadGetsHitAcrossACompleteRoundWithoutLiveSending()
        {
            Advance(95, Pose());
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Results));
            Assert.That(game.Round.Taken, Is.GreaterThan(10)); Assert.That(game.feedback.Sends, Is.Zero);
        }
        [Test] public void GlovesInFrontOfHeadBlockAndNoAttackHitsTwice()
        {
            Advance(95, Pose(true));
            Assert.That(game.Round.Blocks, Is.GreaterThan(10));
            Assert.That(game.Round.Blocks + game.Round.Taken, Is.LessThanOrEqualTo(game.Opponent.AttackId));
            Assert.That(game.Round.Taken, Is.LessThan(game.Round.Blocks));
        }
        [Test] public void MenuFreezesTimeAndOpponentThenRearms()
        {
            Advance(6, Pose()); game.menu.Open(); float time = game.Round.TimeLeft; Vector3 enemy = game.Opponent.Left;
            Advance(10, Pose()); Assert.That(game.Round.TimeLeft, Is.EqualTo(time)); Assert.That(game.Opponent.Left, Is.EqualTo(enemy));
            game.menu.Close(); Advance(1, Pose()); Assert.That(game.Round.TimeLeft, Is.LessThan(time));
        }
        [Test] public void LostTrackingAndRestoredDistantFistsNeverCreateGhostHits()
        {
            Advance(4, Pose()); int hits = game.feedback.Reports; float time = game.Round.TimeLeft;
            var missing = Pose(); missing.valid = false; Advance(4, missing);
            Assert.That(game.Round.TimeLeft, Is.EqualTo(time)); Assert.That(game.feedback.Reports, Is.EqualTo(hits));
            var restored = Pose(); restored.left = game.Opponent.Head; restored.right = game.Opponent.Head;
            Advance(0.3f, restored); Assert.That(game.Round.Hits, Is.Zero);
        }
        [Test] public void AllZonesHaveTwoEditablePcmHapticBindings()
        {
            Assert.That(game.feedback.impactTriggers.Length, Is.EqualTo(6));
            foreach (var trigger in game.feedback.impactTriggers)
            {
                var entry = trigger.ResolveEntry(); Assert.That(entry, Is.Not.Null); Assert.That(entry.streamClip, Is.Not.Null);
                Assert.That(entry.mode, Is.EqualTo(Hapbeat.HapticMode.StreamClip)); Assert.That(entry.streamClip.frequency, Is.EqualTo(16000));
            }
        }
        [Test] public void GradualPunchDoesNotLatchBeforeActualContact()
        {
            game.tuning.enemyInterval = 100;
            var pose = Pose(); Advance(4, pose);
            pose.left = game.Opponent.Head + Vector3.back * 0.5f;
            Advance(0.4f, pose);
            int before = game.Round.Hits;
            for (int i = 0; i < 35; i++)
            {
                pose.left = game.Opponent.Head + Vector3.back * (0.5f - i * 0.008f);
                game.Simulate(1f / 90, pose);
            }
            Assert.That(game.Round.Hits - before, Is.EqualTo(1), "The 3cm release margin must not claim contact before a real hit.");
            Advance(0.4f, pose);
            Assert.That(game.Round.Hits - before, Is.EqualTo(1), "Holding contact must not repeat the impact.");
        }
        [Test] public void TargetingAndSwitchAllowlistArePresent()
        {
            var settings = Resources.Load<Hapbeat.DemoSwitch.DemoSwitchSettings>("HapbeatDemoSwitchSettings");
            Assert.That(settings.CurrentDemoId, Is.EqualTo("boxing"));
            Assert.That(settings.TryResolveTarget("gloveball", out var target), Is.True);
            Assert.That(target.PackageName, Is.EqualTo("jp.hapbeat.gloveballdemo"));
            string[] targets = { "*/pos_l_wrist", "*/pos_r_wrist", "*/pos_neck" };
            for (int i = 0; i < 6; i++) Assert.That(game.feedback.impactTriggers[i].ResolveEntry().target, Is.EqualTo(targets[i / 2]));
            Assert.That(settings.TryResolveTarget("handdemo", out target), Is.True);
            Assert.That(target.PackageName, Is.EqualTo("com.Hapbeat.HapticHandDemo_G2"));
        }
        [Test] public void ExternalTransitionCanResumeIfAppLaunchFails()
        {
            Advance(6, Pose()); game.PauseForExternalTransition();
            float time = game.Round.TimeLeft; Advance(2, Pose());
            Assert.That(game.menu.IsOpen, Is.True); Assert.That(game.Round.TimeLeft, Is.EqualTo(time));
            Assert.That(game.menu.dwellBar.enabled, Is.False, "No completed gaze indicator before dwelling.");
            game.menu.Activate(0); Advance(1, Pose());
            Assert.That(game.menu.IsOpen, Is.False); Assert.That(game.Round.TimeLeft, Is.LessThan(time));
        }
        [Test] public void LeanAfterTelegraphDodgesTheCommittedPunch()
        {
            var pose = Pose();
            for (int i = 0; i < 600 && !game.Opponent.Telegraphing; i++) game.Simulate(1f / 90, pose);
            Assert.That(game.Opponent.Telegraphing, Is.True);
            for (int i = 0; i < 10; i++)
            {
                pose.head.x -= 0.05f; pose.left.x -= 0.05f; pose.right.x -= 0.05f;
                game.Simulate(1f / 90, pose);
            }
            Advance(1.2f, pose);
            Assert.That(game.Round.Taken, Is.Zero); Assert.That(game.Round.Dodges, Is.GreaterThanOrEqualTo(1));
        }
        [Test] public void TrackedControllersDoNotHaveGazeOverrideTheirMenuSelection()
        {
            game.input.mode = BoxingInputMode.Controllers; game.input.SetTestPose(Pose());
            Assert.That(game.menu.UsesGaze, Is.False);
            game.input.mode = BoxingInputMode.Hands; Assert.That(game.menu.UsesGaze, Is.True);
        }
        [Test] public void GlovesBehindHeadCannotRetroactivelyBlock()
        {
            var pose = Pose(true); pose.left.z = pose.right.z = -0.3f;
            Advance(95, pose);
            Assert.That(game.Round.Taken, Is.GreaterThan(10)); Assert.That(game.Round.Blocks, Is.Zero);
        }
        [Test] public void FeedbackRetainsRelativeSpeedGainAndWaveformAcrossAllZones()
        {
            foreach (ImpactZone zone in System.Enum.GetValues(typeof(ImpactZone)))
            {
                game.feedback.Impact(new BoxingImpact(zone, 0.6f, false, Vector3.zero, game.tuning));
                float weak = game.feedback.LastImpact.gain; Assert.That(game.feedback.LastImpact.hard, Is.False);
                game.feedback.Impact(new BoxingImpact(zone, 5, false, Vector3.zero, game.tuning));
                Assert.That(game.feedback.LastImpact.gain, Is.GreaterThan(weak)); Assert.That(game.feedback.LastImpact.hard, Is.True);
                Assert.That(game.feedback.LastImpact.zone, Is.EqualTo(zone));
            }
            Assert.That(game.feedback.Sends, Is.Zero);
        }
    }
}
