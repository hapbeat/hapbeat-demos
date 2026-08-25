using System.Collections.Generic;
using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// Trigger sphere in front of the glove that tracks catchable balls. Kept separate from
    /// <see cref="GloveController"/> so the assist radius can be tuned without touching input code.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class CatchVolume : MonoBehaviour
    {
        private readonly List<Ball> _candidates = new List<Ball>();

        public int CandidateCount => _candidates.Count;
        public event System.Action CandidateEntered;

        /// <summary>Closest ball that is currently in a catchable state, or null.</summary>
        public Ball FindCatchable()
        {
            Ball best = null;
            var bestDistance = float.MaxValue;

            for (var i = _candidates.Count - 1; i >= 0; i--)
            {
                var ball = _candidates[i];
                // This volume is exclusively the receiver for launcher shots. A player-released
                // ball can remain overlapped until physics sends OnTriggerExit, so accepting
                // Thrown here would immediately undo trigger release.
                if (ball == null || ball.State != BallState.Incoming)
                {
                    _candidates.RemoveAt(i);
                    continue;
                }

                var distance = (ball.transform.position - transform.position).sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                best = ball;
                bestDistance = distance;
            }

            return best;
        }

        private void OnTriggerEnter(Collider other)
        {
            var ball = other.GetComponentInParent<Ball>();
            if (ball != null && !_candidates.Contains(ball))
            {
                _candidates.Add(ball);
                CandidateEntered?.Invoke();
            }
        }

        private void OnTriggerExit(Collider other)
        {
            var ball = other.GetComponentInParent<Ball>();
            if (ball != null)
            {
                _candidates.Remove(ball);
            }
        }
    }
}
