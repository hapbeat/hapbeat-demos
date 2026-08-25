using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>Solid player-arm surface: reflects incoming balls, preserves the rally, and reports its hand.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class ArmImpactSurface : PlayerImpactSurface
    {
        [SerializeField] private GloveSide _side;

        private void OnCollisionEnter(Collision collision)
        {
            var ball = collision.collider.GetComponentInParent<Ball>();
            if (ball == null || ball.State != BallState.Incoming || ball.IsInCollisionGrace) return;
            HapticEventRelay.Report(_side == GloveSide.Left ? DemoHapticEvent.LeftArmCollide : DemoHapticEvent.RightArmCollide, collision.GetContact(0).point);
        }
    }
}
