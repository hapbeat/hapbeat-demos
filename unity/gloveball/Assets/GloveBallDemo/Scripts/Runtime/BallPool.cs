using System.Collections.Generic;
using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>Fixed-size ball pool. Nothing is instantiated once the wave is running.</summary>
    public class BallPool : MonoBehaviour
    {
        [SerializeField] private Ball _prefab;
        [SerializeField] private int _size = 16;
        [SerializeField] private bool _verboseLog;

        private readonly Queue<Ball> _free = new Queue<Ball>();
        private readonly List<Ball> _all = new List<Ball>();

        /// <summary>Balls handed out and not yet returned.</summary>
        public int ActiveCount => _all.Count - _free.Count;

        public int TotalTaken { get; private set; }

        public int TotalReturned { get; private set; }

        private void Awake()
        {
            if (_prefab == null)
            {
                Debug.LogError("[BallPool] no ball prefab assigned");
                return;
            }

            for (var i = 0; i < _size; i++)
            {
                var ball = Instantiate(_prefab, transform);
                ball.name = $"Ball_{i:00}";
                ball.BindPool(this);
                ball.ResetToIdle();
                IgnoreOtherBalls(ball);
                _all.Add(ball);
                _free.Enqueue(ball);
            }
        }

        private void IgnoreOtherBalls(Ball ball)
        {
            var addedColliders = ball.GetComponentsInChildren<Collider>(true);
            foreach (var existing in _all)
            {
                var existingColliders = existing.GetComponentsInChildren<Collider>(true);
                foreach (var addedCollider in addedColliders)
                    foreach (var existingCollider in existingColliders)
                        Physics.IgnoreCollision(addedCollider, existingCollider, true);
            }
        }

        /// <summary>Returns a parked ball, or null when the pool is exhausted.</summary>
        public Ball Take()
        {
            if (_free.Count == 0)
            {
                Debug.LogWarning($"[BallPool] exhausted ({_all.Count} balls in play)");
                return null;
            }

            var ball = _free.Dequeue();
            TotalTaken++;
            return ball;
        }

        public void Return(Ball ball, string reason)
        {
            if (ball == null || _free.Contains(ball))
            {
                return;
            }

            ball.transform.SetParent(transform, false);
            ball.ResetToIdle();
            _free.Enqueue(ball);
            TotalReturned++;

            if (_verboseLog)
            {
                Debug.Log($"[BallPool] returned {ball.name} ({reason}); active={ActiveCount}");
            }
        }

        /// <summary>Clears the court, e.g. when a run ends.</summary>
        public void ReturnAll(string reason)
        {
            foreach (var ball in _all)
            {
                if (ball.State != BallState.Idle)
                {
                    ball.Kill(reason);
                }
            }
        }
    }
}
