using System;

namespace GloveBallDemo.Core
{
    public enum GamePhase
    {
        Countdown,
        Wave,
        /// <summary>Short breather that shows the wave result before the next wave starts.</summary>
        Interlude,
        Result
    }

    /// <summary>
    /// Countdown -> Wave(0) -> Interlude -> Wave(1) ... -> Result -> Countdown.
    /// Pure logic: driven by <see cref="Tick"/>, never reads Time.
    /// </summary>
    public sealed class GamePhaseMachine
    {
        public const float CountdownDuration = 3f;
        public const float InterludeDuration = 3f;
        public const float ResultDuration = 10f;

        private readonly int _waveCount;
        private readonly Func<int, float> _waveDuration;
        private readonly float _interludeDuration;

        public GamePhaseMachine(int waveCount, Func<int, float> waveDuration = null, float interludeDuration = InterludeDuration)
        {
            if (waveCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(waveCount), "at least one wave is required");
            }

            _waveCount = waveCount;
            _waveDuration = waveDuration ?? (index => WaveSchedule.Get(index).Duration);
            _interludeDuration = interludeDuration;
            Phase = GamePhase.Countdown;
            PhaseDuration = CountdownDuration;
        }

        /// <summary>Raised after the phase field has been updated: (previous, current).</summary>
        public event Action<GamePhase, GamePhase> PhaseChanged;

        public GamePhase Phase { get; private set; }

        /// <summary>Zero-based index of the wave being played, or the last one played.</summary>
        public int WaveIndex { get; private set; }

        public float PhaseElapsed { get; private set; }

        public float PhaseDuration { get; private set; }

        public int WaveCount => _waveCount;

        public float PhaseRemaining => PhaseDuration - PhaseElapsed;

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            PhaseElapsed += deltaTime;
            // A single tick can be longer than a phase (editor hitches, long frames),
            // so keep advancing until the budget is consumed.
            while (PhaseElapsed >= PhaseDuration)
            {
                var overflow = PhaseElapsed - PhaseDuration;
                Advance();
                PhaseElapsed = overflow;
            }
        }

        private void Advance()
        {
            switch (Phase)
            {
                case GamePhase.Countdown:
                    Enter(GamePhase.Wave, _waveDuration(WaveIndex));
                    break;
                case GamePhase.Wave:
                    Enter(GamePhase.Interlude, _interludeDuration);
                    break;
                case GamePhase.Interlude:
                    if (WaveIndex + 1 < _waveCount)
                    {
                        WaveIndex++;
                        Enter(GamePhase.Wave, _waveDuration(WaveIndex));
                    }
                    else
                    {
                        Enter(GamePhase.Result, ResultDuration);
                    }

                    break;
                case GamePhase.Result:
                    WaveIndex = 0;
                    Enter(GamePhase.Countdown, CountdownDuration);
                    break;
            }
        }

        private void Enter(GamePhase next, float duration)
        {
            var previous = Phase;
            Phase = next;
            PhaseDuration = duration;
            PhaseElapsed = 0f;
            PhaseChanged?.Invoke(previous, next);
        }
    }
}
