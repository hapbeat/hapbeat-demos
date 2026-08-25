using GloveBallDemo.Core;
using NUnit.Framework;

namespace GloveBallDemo.Tests
{
    public class ScoreModelTests
    {
        [Test]
        public void StartsEmptyOnScoreAndCombo()
        {
            var model = new ScoreModel();
            Assert.AreEqual(0, model.Score);
            Assert.AreEqual(0, model.Combo);
            Assert.AreEqual(1, model.Multiplier);
        }

        [Test]
        public void FirstCatchScoresBaseValue()
        {
            var model = new ScoreModel();
            Assert.AreEqual(ScoreModel.CatchPoints, model.RegisterCatch());
            Assert.AreEqual(ScoreModel.CatchPoints, model.Score);
            Assert.AreEqual(1, model.Combo);
        }

        [Test]
        public void MultiplierStepsUpEveryThreeSuccesses()
        {
            var model = new ScoreModel();
            Assert.AreEqual(1, model.Multiplier);

            model.RegisterCatch();
            model.RegisterCatch();
            Assert.AreEqual(1, model.Multiplier, "combo 2 is still 1x");

            model.RegisterCatch();
            Assert.AreEqual(2, model.Multiplier, "combo 3 reaches 2x");

            model.RegisterCatch();
            model.RegisterCatch();
            model.RegisterCatch();
            Assert.AreEqual(3, model.Multiplier, "combo 6 reaches 3x");
        }

        [Test]
        public void MultiplierIsCappedAtFour()
        {
            var model = new ScoreModel();
            for (var i = 0; i < 50; i++)
            {
                model.RegisterCatch();
            }

            Assert.AreEqual(ScoreModel.MaxMultiplier, model.Multiplier);
        }

        [Test]
        public void ScoringUsesTheMultiplierInEffectBeforeTheIncrement()
        {
            var model = new ScoreModel();
            model.RegisterCatch();
            model.RegisterCatch();
            model.RegisterCatch(); // combo -> 3, still awarded at 1x
            Assert.AreEqual(ScoreModel.CatchPoints * 3, model.Score);

            // Fourth catch is the first one at 2x.
            Assert.AreEqual(ScoreModel.CatchPoints * 2, model.RegisterCatch());
        }

        [Test]
        public void TargetAndLauncherHitsUseTheirOwnBaseValues()
        {
            var model = new ScoreModel();
            Assert.AreEqual(ScoreModel.TargetPoints, model.RegisterTargetHit());
            var launcher = model.RegisterLauncherHit();
            Assert.AreEqual(ScoreModel.LauncherPoints, launcher);
        }

        [Test]
        public void PlayerHitResetsComboWithoutEndingTheRound()
        {
            var model = new ScoreModel();
            model.RegisterCatch();
            model.RegisterCatch();
            model.RegisterCatch();
            Assert.AreEqual(2, model.Multiplier);

            model.RegisterPlayerHit();
            Assert.AreEqual(0, model.Combo);
            Assert.AreEqual(1, model.Multiplier);
            Assert.AreEqual(3, model.BestCombo, "best combo survives the reset");
        }

        [Test]
        public void PlayerHitDoesNotScore()
        {
            var model = new ScoreModel();
            model.RegisterCatch();
            var before = model.Score;
            model.RegisterPlayerHit();
            Assert.AreEqual(before, model.Score);
        }

        [Test]
        public void ResetRestoresTheInitialState()
        {
            var model = new ScoreModel();
            model.RegisterLauncherHit();
            model.RegisterPlayerHit();
            model.Reset();

            Assert.AreEqual(0, model.Score);
            Assert.AreEqual(0, model.Combo);
            Assert.AreEqual(0, model.BestCombo);
        }

        [Test]
        public void ChangedFiresOnEveryMutation()
        {
            var model = new ScoreModel();
            var count = 0;
            model.Changed += () => count++;

            model.RegisterCatch();
            model.RegisterTargetHit();
            model.RegisterPlayerHit();
            model.Reset();

            Assert.AreEqual(4, count);
        }
    }
}
