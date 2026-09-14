using Hapbeat.Boxing.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingRulesTests
    {
        [TestCase("ProjectSettings/TagManager.asset", "layers")]
        [TestCase("Assets/XRI/Settings/Resources/InteractionLayerSettings.asset", "m_LayerNames")]
        public void SerializedLayerTablesLoadWithAll32Slots(string path, string property)
        {
            if (path.StartsWith("Assets/")) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            Assert.That(assets, Is.Not.Empty, "Unity must parse the actual serialized settings, not use a fallback.");
            var table = new SerializedObject(assets[0]).FindProperty(property);
            Assert.That(table, Is.Not.Null);
            Assert.That(table.arraySize, Is.EqualTo(32));
            Assert.That(table.GetArrayElementAtIndex(0).stringValue, Is.EqualTo("Default"));
            Assert.That(table.GetArrayElementAtIndex(31).stringValue, Is.Empty);
        }
        private BoxingTuning tuning;
        [Test] public void SimulatorAssetsResolveWithoutMissingReferences() => BoxingSimulator.ValidateAssets();
        [Test] public void SimulatorAssetsAreNotSceneOrResourcesDependencies()
        {
            foreach (var path in AssetDatabase.GetDependencies("Assets/Boxing/Scenes/Boxing.unity", true))
                Assert.That(path, Does.Not.StartWith("Assets/Boxing/Editor/Simulator/"));
            var config = AssetDatabase.LoadAllAssetsAtPath("Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset")[0];
            var properties = new SerializedObject(config);
            Assert.That(properties.FindProperty("m_AutomaticallyInstantiateSimulatorPrefab").boolValue, Is.False);
            Assert.That(properties.FindProperty("m_SimulatorPrefab").objectReferenceValue, Is.Null);
        }
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
        [Test] public void HealthDamageGuardKnockoutAndRestart()
        {
            var round = new BoxingRound(); round.Start(90); round.Tick(3, false);
            round.Report(new BoxingImpact(ImpactZone.LeftGlove, 1, false, Vector3.zero, tuning, ImpactSurface.Glove));
            Assert.That(round.PlayerHealth, Is.EqualTo(100)); Assert.That(round.Blocks, Is.EqualTo(1));
            round.Report(new BoxingImpact(ImpactZone.Head, 1, false, Vector3.zero, tuning, ImpactSurface.Body));
            Assert.That(round.PlayerHealth, Is.EqualTo(90)); Assert.That(round.EnemyHealth, Is.EqualTo(100));
            for (int i = 0; i < 5; i++) round.Report(new BoxingImpact(ImpactZone.LeftGlove, 4, true, Vector3.zero, tuning, ImpactSurface.Body));
            Assert.That(round.EnemyHealth, Is.Zero); Assert.That(round.Phase, Is.EqualTo(BoxingPhase.Results));
            round.Report(new BoxingImpact(ImpactZone.Head, 4, false, Vector3.zero, tuning, ImpactSurface.Body));
            Assert.That(round.PlayerHealth, Is.EqualTo(90), "No damage after KO.");
            round.Start(90); Assert.That(round.PlayerHealth, Is.EqualTo(100)); Assert.That(round.EnemyHealth, Is.EqualTo(100));
            round.Tick(3, false);
            for (int i = 0; i < 5; i++) round.Report(new BoxingImpact(ImpactZone.Head, 4, false, Vector3.zero, tuning, ImpactSurface.Body));
            Assert.That(round.PlayerHealth, Is.Zero); Assert.That(round.Phase, Is.EqualTo(BoxingPhase.Results));
        }
        [TestCase(true)] [TestCase(false)]
        public void GloveSurfaceNeverDamagesEitherBoxer(bool attack)
        {
            var round = new BoxingRound(); round.Start(90); round.Tick(3, false);
            round.Report(new BoxingImpact(ImpactZone.LeftGlove, 5, attack, Vector3.zero, tuning, ImpactSurface.Glove));
            Assert.That(round.PlayerHealth, Is.EqualTo(100)); Assert.That(round.EnemyHealth, Is.EqualTo(100));
            Assert.That(attack ? round.EnemyBlocks : round.Blocks, Is.EqualTo(1)); Assert.That(round.Hits, Is.Zero);
        }
        [Test] public void OpponentSometimesRaisesAndLowersGuardWithoutReadingPlayerPunches()
        {
            var enemy = new BoxingOpponent(tuning); int guarded = 0, transitions = 0; bool old = false;
            for (int i = 0; i < 6000; i++)
            {
                enemy.Tick(0.01f, Vector3.up * 1.65f, true);
                if (enemy.Guarding && !old) transitions++;
                if (enemy.GuardWeight > 0.9f)
                {
                    guarded++; Assert.That(enemy.Striking, Is.False);
                    Assert.That(Mathf.Abs(enemy.Left.x - enemy.Head.x), Is.LessThan(0.13f));
                    Assert.That(enemy.Left.y, Is.GreaterThan(enemy.Head.y - 0.06f));
                }
                old = enemy.Guarding;
            }
            Assert.That(guarded, Is.GreaterThan(100)); Assert.That(transitions, Is.GreaterThan(2)); Assert.That(enemy.CompletedAttacks, Is.GreaterThan(5));
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
            game.Initialize(); game.Round.Start(90, 10000); // Long collision tests must not stop early at KO.
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
        private void FocusEvent(string method, bool value) => typeof(BoxingGame).GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(game, new object[] { value });
        [Test] public void FocusedHeadsetContinuesWhenEditorWindowLosesFocus()
        {
            FocusEvent("DisplayFocus", true);
            FocusEvent("FocusChanged", false);
            game.StartRound(); Advance(4, Pose());
            Assert.That(game.Paused, Is.False, game.PauseReason);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Fighting));
        }
        [Test] public void ActualHeadsetFocusLossStillStopsAndRequiresResume()
        {
            FocusEvent("DisplayFocus", true);
            game.StartRound(); Advance(4, Pose());
            FocusEvent("DisplayFocus", false);
            float time = game.Round.TimeLeft; Advance(1, Pose());
            Assert.That(game.Paused, Is.True); Assert.That(game.Round.TimeLeft, Is.EqualTo(time));
            FocusEvent("DisplayFocus", true);
            Advance(1, Pose()); Assert.That(game.menu.IsOpen, Is.True);
            game.menu.Close(); Advance(1, Pose()); Assert.That(game.Paused, Is.False);
        }
        [Test] public void ApplicationPauseStillStopsFocusedHeadset()
        {
            FocusEvent("DisplayFocus", true); game.StartRound(); Advance(4, Pose());
            FocusEvent("OnApplicationPause", true); game.menu.Close(); Advance(1, Pose());
            Assert.That(game.Paused, Is.True); Assert.That(game.PauseReason, Is.EqualTo("APPLICATION PAUSED"));
        }
        [Test] public void WithoutHeadsetWindowFocusStillPauses()
        {
            FocusEvent("FocusChanged", false); game.StartRound(); Advance(1, Pose());
            Assert.That(game.Paused, Is.True); Assert.That(game.PauseReason, Is.EqualTo("GAME WINDOW NOT FOCUSED"));
        }
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
        [Test] public void GuardContactStopsEnemyStrikeAndRetractsInsteadOfPassingThrough()
        {
            var pose = Pose(true);
            for (int i = 0; i < 900 && game.Round.Blocks == 0; i++) game.Simulate(1f / 90, pose);
            Assert.That(game.Round.Blocks, Is.EqualTo(1));
            Assert.That(game.Opponent.Striking, Is.False, "A counted block must stop the visible strike.");
            bool left = game.Opponent.AttackLeft;
            Vector3 contact = left ? game.Opponent.Left : game.Opponent.Right;
            game.Simulate(1f / 90, pose);
            Assert.That((left ? game.Opponent.Left : game.Opponent.Right).z, Is.GreaterThanOrEqualTo(contact.z));
            Assert.That(game.Round.Taken, Is.Zero);
        }
        [Test] public void HealthBarsReplaceCueAndReflectBothHealthValues()
        {
            game.Round.Start(90); game.Round.Tick(3, false);
            game.Round.Report(new BoxingImpact(ImpactZone.Head, 1, false, Vector3.zero, game.tuning, ImpactSurface.Body));
            game.Round.Report(new BoxingImpact(ImpactZone.LeftGlove, 4, true, Vector3.zero, game.tuning, ImpactSurface.Body));
            game.presentation.RenderHealth(game.Round);
            Assert.That(game.presentation.cueText.enabled, Is.False);
            Assert.That(game.presentation.PlayerHealthBar.rectTransform.anchorMax.x, Is.EqualTo(0.9f).Within(0.001f));
            Assert.That(game.presentation.EnemyHealthBar.rectTransform.anchorMax.x, Is.EqualTo(0.8f).Within(0.001f));
        }
        [Test] public void SceneAndNewInputUseCorrectedControllerPitch()
        {
            Assert.That(game.input.controllerRotation, Is.EqualTo(new Vector3(75, 0, 0)));
        }
        [Test] public void ImpactFeedbackMustNotCoverTheViewWithARectangularPanel()
        {
            Assert.That(game.input.headCamera.transform.Find("Impact feedback/Damage tint"), Is.Null);
            var ring = game.presentation.hitBurst.GetComponent<LineRenderer>();
            Assert.That(ring, Is.Not.Null); Assert.That(ring.useWorldSpace, Is.False);
            Assert.That(ring.sharedMaterial.GetFloat("_Surface"), Is.EqualTo(1));
        }
        [Test] public void CompactGlovesMatchTheSmallerCollisionEnvelope()
        {
            var size = BoxingContent.GloveSize(game.presentation.leftGlove);
            Assert.That(size.x, Is.InRange(0.16f, 0.18f)); Assert.That(size.z, Is.InRange(0.21f, 0.24f));
            Assert.That(game.tuning.gloveRadius, Is.EqualTo(0.09f));
        }
        [Test] public void GuardInterceptionAndBodyHitSelectDifferentOutcomes()
        {
            game.Round.Tick(3, false);
            for (int i = 0; i < 2000 && game.Opponent.GuardWeight < 0.99f; i++) game.Opponent.Tick(0.01f, Pose().head, true);
            Assert.That(game.Opponent.Guarding, Is.True);
            typeof(BoxingGame).GetMethod("SaveHistory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(game, new object[] { Pose() });
            var resolve = typeof(BoxingGame).GetMethod("ResolvePlayer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Vector3 from = game.Opponent.Left + Vector3.back * 0.5f, to = game.Opponent.Head;
            var args = new object[] { to, from, true, ImpactZone.LeftGlove, false, 0f, 0.1f };
            resolve.Invoke(game, args);
            Assert.That(game.Round.EnemyBlocks, Is.EqualTo(1)); Assert.That(game.Round.Hits, Is.Zero);
            Assert.That(game.feedback.LastImpact.surface, Is.EqualTo(ImpactSurface.Glove));
            Assert.That(game.Round.EnemyHealth, Is.EqualTo(10000));
            resolve.Invoke(game, args); Assert.That(game.Round.EnemyBlocks, Is.EqualTo(1), "Held overlap must not repeatedly fire.");
            resolve.Invoke(game, new object[] { game.Opponent.Body, game.Opponent.Body + Vector3.back * 0.6f, true, ImpactZone.RightGlove, false, 0f, 0.1f });
            Assert.That(game.Round.Hits, Is.EqualTo(1)); Assert.That(game.Round.EnemyHealth, Is.LessThan(10000));
            Assert.That(game.feedback.LastImpact.surface, Is.EqualTo(ImpactSurface.Body));
        }
        [TestCase(true)] [TestCase(false)]
        public void SurfaceSelectsDistinctAudioAndTactileClipsForBothDirections(bool attack)
        {
            foreach (ImpactZone zone in System.Enum.GetValues(typeof(ImpactZone)))
            foreach (float speed in new[] { 1f, 5f })
            {
                var glove = new BoxingImpact(zone, speed, attack, Vector3.zero, game.tuning, ImpactSurface.Glove);
                var body = new BoxingImpact(zone, speed, attack, Vector3.zero, game.tuning, ImpactSurface.Body);
                Assert.That(game.feedback.contactSounds[BoxingFeedback.SoundIndex(glove)], Is.Not.SameAs(game.feedback.contactSounds[BoxingFeedback.SoundIndex(body)]));
                var g = game.feedback.impactTriggers[BoxingFeedback.TriggerIndex(glove)].ResolveEntry();
                var b = game.feedback.impactTriggers[BoxingFeedback.TriggerIndex(body)].ResolveEntry();
                Assert.That(g.streamClip, Is.Not.SameAs(b.streamClip)); Assert.That(g.target, Is.EqualTo(b.target));
                Assert.That(g.streamClip.length, Is.LessThan(b.streamClip.length));
            }
            Assert.That(game.feedback.Sends, Is.Zero);
        }
        [Test] public void StartAndEndRingOnceAndResultsRemainStable()
        {
            Advance(4, Pose()); Assert.That(game.feedback.Rings, Is.EqualTo(1));
            Advance(95, Pose()); Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Results));
            Assert.That(game.feedback.Rings, Is.EqualTo(2)); Advance(1, Pose()); Assert.That(game.feedback.Rings, Is.EqualTo(2));
            Assert.That(game.feedback.bellSource, Is.Not.SameAs(game.feedback.audioSource));
            Assert.That(game.feedback.bell.length, Is.GreaterThan(2));
        }
        [Test] public void RecordedGongHasFourSeparatedAttacksAndALongFinalTail()
        {
            var clip = game.feedback.bell; var samples = new float[clip.samples];
            Assert.That(clip.channels, Is.EqualTo(1)); Assert.That(clip.GetData(samples, 0), Is.True);
            float Rms(float time)
            {
                int start = (int)(time * clip.frequency), count = (int)(0.015f * clip.frequency); double sum = 0;
                for (int i = 0; i < count; i++) sum += samples[start + i] * samples[start + i];
                return (float)System.Math.Sqrt(sum / count);
            }
            foreach (float at in new[] { 0f, 0.24f, 0.48f, 0.88f })
            {
                Assert.That(Rms(at + 0.015f), Is.GreaterThan(0.05f));
                if (at > 0) Assert.That(Rms(at - 0.02f), Is.LessThan(0.005f));
            }
            Assert.That(Rms(1.5f), Is.GreaterThan(0.005f));
        }
        [Test] public void KnockoutDoesNotEmitAnotherImpactInTheSameFrame()
        {
            game.Round.Start(90, 10); game.Round.Tick(3, false);
            var report = typeof(BoxingGame).GetMethod("Report", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var impact = new BoxingImpact(ImpactZone.LeftGlove, 1, true, Vector3.zero, game.tuning, ImpactSurface.Body);
            report.Invoke(game, new object[] { impact }); report.Invoke(game, new object[] { impact });
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Results));
            Assert.That(game.feedback.Reports, Is.EqualTo(1));
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
        [Test] public void AllZonesHaveFourSurfaceAndStrengthPcmHapticBindings()
        {
            Assert.That(game.feedback.impactTriggers.Length, Is.EqualTo(12));
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
            for (int i = 0; i < 12; i++) Assert.That(game.feedback.impactTriggers[i].ResolveEntry().target, Is.EqualTo(targets[i / 4]));
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
        [Test] public void LostControllerTrackingDoesNotEnableGazeMenu()
        {
            game.input.mode = BoxingInputMode.Controllers; var missing = Pose(); missing.valid = false; game.input.SetTestPose(missing);
            Assert.That(game.menu.UsesGaze, Is.False);
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
                game.feedback.Impact(new BoxingImpact(zone, 0.6f, false, Vector3.zero, game.tuning, ImpactSurface.Body));
                float weak = game.feedback.LastImpact.gain; Assert.That(game.feedback.LastImpact.hard, Is.False);
                game.feedback.Impact(new BoxingImpact(zone, 5, false, Vector3.zero, game.tuning, ImpactSurface.Body));
                Assert.That(game.feedback.LastImpact.gain, Is.GreaterThan(weak)); Assert.That(game.feedback.LastImpact.hard, Is.True);
                Assert.That(game.feedback.LastImpact.zone, Is.EqualTo(zone));
            }
            Assert.That(game.feedback.Sends, Is.Zero);
        }
    }
}
