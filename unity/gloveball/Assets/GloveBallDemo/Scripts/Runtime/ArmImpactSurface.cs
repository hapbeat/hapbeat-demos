using System.Collections.Generic;
using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>Solid player-arm surface: reflects incoming balls, preserves the rally, and reports its hand.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class ArmImpactSurface : PlayerImpactSurface
    {
        [SerializeField] private GloveSide _side;
        private readonly Dictionary<Ball, Vector3> _pendingImpacts = new Dictionary<Ball, Vector3>();

        private void OnCollisionEnter(Collision collision)
        {
            var ball = collision.collider.GetComponentInParent<Ball>();
            HandleImpact(ball, collision.GetContact(0).point);
        }

        private void HandleImpact(Ball ball, Vector3 point)
        {
            if (ball == null || ball.State != BallState.Incoming || ball.IsInCollisionGrace) return;
            if (!_pendingImpacts.ContainsKey(ball)) _pendingImpacts.Add(ball, point);
        }

        private void FixedUpdate()
        {
            if (_pendingImpacts.Count == 0) return;

            foreach (var impact in _pendingImpacts)
            {
                var ball = impact.Key;
                if (ball == null || ball.State != BallState.Incoming) continue;
                HapticEventRelay.Report(
                    _side == GloveSide.Left ? DemoHapticEvent.LeftArmCollide : DemoHapticEvent.RightArmCollide,
                    impact.Value);
            }

            _pendingImpacts.Clear();
        }

        private void OnDisable() => _pendingImpacts.Clear();
    }
}
