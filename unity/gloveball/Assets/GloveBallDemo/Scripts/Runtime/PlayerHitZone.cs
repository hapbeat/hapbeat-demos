using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// The body volume that can be hit, tracking the headset. Deliberately excludes the hands:
    /// a ball touching a glove is a catch attempt, never a hit.
    /// </summary>
    [RequireComponent(typeof(CapsuleCollider))]
    public class PlayerHitZone : PlayerImpactSurface
    {
        [SerializeField] private Transform _head;
        /// <summary>Offset from the head down to the middle of the torso capsule.</summary>
        [SerializeField] private Vector3 _headOffset = new Vector3(0f, -0.45f, 0f);
        [SerializeField] private float _invulnerableAfterHit = 1.2f;

        private float _invulnerableTimer;

        public Transform Head => _head;

        /// <summary>Chest height point the launchers aim at.</summary>
        public Vector3 AimPoint => transform.position;

        public void Bind(Transform head) => _head = head;

        private void LateUpdate()
        {
            if (_invulnerableTimer > 0f)
            {
                _invulnerableTimer -= Time.deltaTime;
            }

            if (_head == null)
            {
                return;
            }

            transform.position = _head.position + _headOffset;
            // Yaw only: the capsule should not tip over when the player looks down.
            var forward = _head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 1e-4f)
            {
                transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            var ball = collision.collider.GetComponentInParent<Ball>();
            if (ball == null || ball.State != BallState.Incoming || ball.IsInCollisionGrace)
            {
                return;
            }

            if (_invulnerableTimer > 0f)
            {
                return;
            }

            _invulnerableTimer = _invulnerableAfterHit;
            var controller = DemoGameController.Instance;
            if (controller != null)
            {
                controller.OnPlayerHit(ball);
            }

        }
    }
}
