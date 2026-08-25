namespace GloveBallDemo.Core
{
    /// <summary>Static tuning data for a single wave. Values are initial guesses; tune on device.</summary>
    public readonly struct WaveDefinition
    {
        public readonly float Duration;
        public readonly float MinInterval;
        public readonly float MaxInterval;
        public readonly float MinSpeed;
        public readonly float MaxSpeed;
        public readonly int MaxActiveBalls;
        public readonly int LauncherCount;
        /// <summary>
        /// Share of the shots that are aimed straight at the player. Early waves lean on it:
        /// a shot passing to the side teaches nothing, the player needs balls to catch.
        /// </summary>
        public readonly float DirectWeight;

        public WaveDefinition(
            float duration,
            float minInterval,
            float maxInterval,
            float minSpeed,
            float maxSpeed,
            int maxActiveBalls,
            int launcherCount,
            float directWeight)
        {
            Duration = duration;
            MinInterval = minInterval;
            MaxInterval = maxInterval;
            MinSpeed = minSpeed;
            MaxSpeed = maxSpeed;
            MaxActiveBalls = maxActiveBalls;
            LauncherCount = launcherCount;
            DirectWeight = directWeight;
        }
    }

    public static class WaveSchedule
    {
        /// <summary>Minimum share of Direct shots the opening wave must keep. Tests assert it.</summary>
        public const float Wave1MinimumDirectWeight = 0.70f;

        /// <summary>
        /// Horizontal distance from a launcher muzzle to the player, in metres, as laid out by
        /// DemoSceneBuilder. The speed bands below are derived from it, and the tests check them
        /// against it; keep the two in step if the court layout moves.
        /// </summary>
        public const float LauncherToPlayerDistance = 14.2f;

        /// <summary>
        /// Wave 1 eases the player in; wave 3 is the pressure peak.
        ///
        /// The speed bands come from the court geometry rather than from feel alone: over
        /// <see cref="LauncherToPlayerDistance"/> a drag-free shot needs at least sqrt(g*d) =
        /// 12.0 m/s just to arrive, so anything below that cannot reach the player at any angle.
        /// The bands span 13-21 m/s, a flight time of roughly 1.2 s down to 0.75 s: the reaction
        /// window narrows wave by wave and never goes unreachable.
        /// </summary>
        public static readonly WaveDefinition[] Waves =
        {
            new WaveDefinition(30f, 1.25f, 1.65f, 13f, 15f, 5, 2, 0.70f),
            new WaveDefinition(30f, 0.85f, 1.15f, 15f, 18f, 7, 3, 0.65f),
            new WaveDefinition(30f, 0.55f, 0.85f, 17f, 21f, 9, 3, 0.60f)
        };

        public static int WaveCount => Waves.Length;

        public static WaveDefinition Get(int waveIndex)
        {
            if (waveIndex < 0)
            {
                waveIndex = 0;
            }

            if (waveIndex >= Waves.Length)
            {
                waveIndex = Waves.Length - 1;
            }

            return Waves[waveIndex];
        }
    }
}
