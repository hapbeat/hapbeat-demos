using System;

namespace GloveBallDemo.Core
{
    public enum AimKind
    {
        /// <summary>Straight at the player: must be caught or dodged.</summary>
        Direct,
        /// <summary>High arc that drops in; slow enough to line up a return throw.</summary>
        Lob
    }

    public readonly struct LaunchOrder
    {
        public readonly int LauncherIndex;
        public readonly float Speed;
        public readonly AimKind Aim;
        /// <summary>Signed metres to offset the aim point sideways from the player.</summary>
        public readonly float LateralOffset;
        /// <summary>Signed metres to offset the aim point forward/backward from the player.</summary>
        public readonly float DepthOffset;

        public LaunchOrder(int launcherIndex, float speed, AimKind aim, float lateralOffset, float depthOffset)
        {
            LauncherIndex = launcherIndex;
            Speed = speed;
            Aim = aim;
            LateralOffset = lateralOffset;
            DepthOffset = depthOffset;
        }
    }

    /// <summary>
    /// Decides which launcher fires, when, and where it aims. Deterministic for a given seed
    /// so the wave rhythm can be regression tested.
    /// </summary>
    public sealed class LaunchPlanner
    {
        /// <summary>Maximum absolute player-local sideways aim displacement, in metres.</summary>
        public const float LateralOffsetLimit = 0.85f;
        /// <summary>Maximum absolute player-local forward/backward aim displacement, in metres.</summary>
        public const float DepthOffsetLimit = 0.75f;

        private readonly Random _random;
        private WaveDefinition _wave;
        private int _launcherCount;
        private float _timer;
        private float _nextInterval;
        private int _lastLauncherIndex = -1;
        private bool _forceDirect;

        public LaunchPlanner(int seed)
        {
            _random = new Random(seed);
        }

        public int LaunchCount { get; private set; }

        /// <summary>Starts a wave. The first shot is delayed by a normal interval.</summary>
        public void BeginWave(WaveDefinition wave, int availableLaunchers)
        {
            _wave = wave;
            _launcherCount = Math.Min(wave.LauncherCount, Math.Max(1, availableLaunchers));
            _timer = 0f;
            _nextInterval = NextInterval();
            _lastLauncherIndex = -1;
            // The wave has to open with a ball the player can actually catch: a wave that starts
            // with a shot flying past their shoulder reads as the machine ignoring them.
            _forceDirect = true;
            LaunchCount = 0;
        }

        /// <summary>
        /// Advances the wave clock. Returns true when a shot is due; the caller passes the number
        /// of balls currently in flight so the planner can respect the concurrency cap.
        /// </summary>
        public bool Tick(float deltaTime, int activeBalls, out LaunchOrder order)
        {
            order = default;
            if (_launcherCount <= 0)
            {
                return false;
            }

            _timer += deltaTime;
            if (_timer < _nextInterval)
            {
                return false;
            }

            if (activeBalls >= _wave.MaxActiveBalls)
            {
                // Hold the shot but do not bank up time; retry on the next tick.
                _timer = _nextInterval;
                return false;
            }

            _timer = 0f;
            _nextInterval = NextInterval();
            order = BuildOrder();
            LaunchCount++;
            return true;
        }

        private LaunchOrder BuildOrder()
        {
            var index = PickLauncher();
            var speed = Lerp(_wave.MinSpeed, _wave.MaxSpeed, (float)_random.NextDouble());
            var aim = PickAim();
            var lateral = Lerp(-LateralOffsetLimit, LateralOffsetLimit, (float)_random.NextDouble());
            var depth = Lerp(-DepthOffsetLimit, DepthOffsetLimit, (float)_random.NextDouble());

            return new LaunchOrder(index, speed, aim, lateral, depth);
        }

        private int PickLauncher()
        {
            if (_launcherCount == 1)
            {
                _lastLauncherIndex = 0;
                return 0;
            }

            // Avoid firing the same barrel twice in a row: repeats read as a stuck machine.
            int index;
            do
            {
                index = _random.Next(_launcherCount);
            }
            while (index == _lastLauncherIndex);

            _lastLauncherIndex = index;
            return index;
        }

        /// <summary>
        /// Direct takes the wave's own share; the remainder is lobbed. Spatial variation is
        /// deliberately independent of this flight profile and lives in LaunchOrder's offsets.
        /// </summary>
        private AimKind PickAim()
        {
            // The roll is drawn even when the shot is forced, so the random stream - and with it
            // the determinism of the whole wave - does not depend on which shot is the first one.
            var roll = _random.NextDouble();
            if (_forceDirect)
            {
                _forceDirect = false;
                return AimKind.Direct;
            }

            var direct = _wave.DirectWeight;
            if (roll < direct)
            {
                return AimKind.Direct;
            }

            return AimKind.Lob;
        }

        private float NextInterval()
        {
            return Lerp(_wave.MinInterval, _wave.MaxInterval, (float)_random.NextDouble());
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
    }
}
