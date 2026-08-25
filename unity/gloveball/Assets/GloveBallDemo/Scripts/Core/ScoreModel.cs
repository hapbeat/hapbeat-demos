using System;

namespace GloveBallDemo.Core
{
    /// <summary>Score and combo. Pure logic so the rules can be unit tested.</summary>
    public sealed class ScoreModel
    {
        public const int CatchPoints = 50;
        public const int TargetPoints = 100;
        public const int LauncherPoints = 150;
        public const int MaxMultiplier = 4;

        /// <summary>Successful actions needed to gain one multiplier step.</summary>
        private const int ComboStep = 3;

        public ScoreModel()
        {
            Reset();
        }

        /// <summary>Raised whenever score or combo changed.</summary>
        public event Action Changed;

        public int Score { get; private set; }

        public int Combo { get; private set; }

        public int BestCombo { get; private set; }

        /// <summary>1x .. 4x, one step per <see cref="ComboStep"/> consecutive successes.</summary>
        public int Multiplier
        {
            get
            {
                var m = 1 + Combo / ComboStep;
                return m > MaxMultiplier ? MaxMultiplier : m;
            }
        }

        public void Reset()
        {
            Score = 0;
            Combo = 0;
            BestCombo = 0;
            Changed?.Invoke();
        }

        public int RegisterCatch() => AddScoring(CatchPoints);

        public int RegisterTargetHit() => AddScoring(TargetPoints);

        public int RegisterLauncherHit() => AddScoring(LauncherPoints);

        /// <summary>Player took a hit: the hit haptic remains gameplay feedback and the combo resets.</summary>
        public void RegisterPlayerHit()
        {
            Combo = 0;
            Changed?.Invoke();
        }

        /// <summary>Applies the current multiplier, then grows the combo. Returns the points awarded.</summary>
        private int AddScoring(int basePoints)
        {
            var awarded = basePoints * Multiplier;
            Score += awarded;
            Combo++;
            if (Combo > BestCombo)
            {
                BestCombo = Combo;
            }

            Changed?.Invoke();
            return awarded;
        }
    }
}
