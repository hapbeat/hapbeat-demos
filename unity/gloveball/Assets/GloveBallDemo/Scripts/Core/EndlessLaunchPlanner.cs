using System;

namespace GloveBallDemo.Core
{
    /// <summary>Deterministic endless launch orders with independently tuneable random cadence.</summary>
    public sealed class EndlessLaunchPlanner
    {
        private readonly Random _random;
        private readonly float _minInterval;
        private readonly float _maxInterval;
        private readonly float _minSpeed;
        private readonly float _maxSpeed;
        private int _launcherCount;
        private float _timer;
        private float _nextInterval;
        private LaunchOrder _pendingOrder;
        private bool _hasPendingOrder;

        public EndlessLaunchPlanner(int seed, float minInterval, float maxInterval, float minSpeed, float maxSpeed)
        {
            _random = new Random(seed);
            _minInterval = minInterval;
            _maxInterval = maxInterval;
            _minSpeed = minSpeed;
            _maxSpeed = maxSpeed;
        }

        public void Begin(int launcherCount)
        {
            _launcherCount = Math.Max(0, launcherCount);
            _timer = 0f;
            _nextInterval = NextInterval();
            _hasPendingOrder = false;
        }

        public bool Tick(float deltaTime, int activeBalls, int maxActiveBalls, out LaunchOrder order)
        {
            order = _pendingOrder;
            if (_launcherCount == 0 || activeBalls >= maxActiveBalls)
            {
                return false;
            }

            if (_hasPendingOrder)
            {
                return true;
            }

            _timer += deltaTime;
            if (_timer < _nextInterval)
            {
                return false;
            }

            _pendingOrder = new LaunchOrder(
                _random.Next(_launcherCount),
                Lerp(_minSpeed, _maxSpeed, (float)_random.NextDouble()),
                _random.Next(2) == 0 ? AimKind.Direct : AimKind.Lob,
                Lerp(-LaunchPlanner.LateralOffsetLimit, LaunchPlanner.LateralOffsetLimit, (float)_random.NextDouble()),
                Lerp(-LaunchPlanner.DepthOffsetLimit, LaunchPlanner.DepthOffsetLimit, (float)_random.NextDouble()));
            _hasPendingOrder = true;
            order = _pendingOrder;
            return true;
        }

        /// <summary>Confirms that the current due order was accepted by a launcher.</summary>
        public void Commit()
        {
            if (!_hasPendingOrder)
            {
                return;
            }

            _hasPendingOrder = false;
            _timer = 0f;
            _nextInterval = NextInterval();
        }

        private float NextInterval() => Lerp(_minInterval, _maxInterval, (float)_random.NextDouble());
        private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
    }
}
