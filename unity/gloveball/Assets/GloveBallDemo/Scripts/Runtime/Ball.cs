using System;
using UnityEngine;

namespace GloveBallDemo.Runtime
{
    public enum BallState
    {
        /// <summary>Parked in the pool.</summary>
        Idle,
        /// <summary>In flight from a launcher; can hit the player or be caught.</summary>
        Incoming,
        Held,
        /// <summary>Thrown by the player; can score on targets and launchers.</summary>
        Thrown,
        Dead
    }

    /// <summary>
    /// One ball. Owns its own lifetime rules; arena collisions stay physical and scoring
    /// triggers explicitly return the ball through the pool.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Ball : MonoBehaviour
    {
        [Header("Lifetime")]
        [Tooltip("Seconds after launch before an unscored ball is returned to the pool.")]
        [SerializeField] private float _maxLifetime = 5f;
        [Tooltip("World-space Y below which an escaped ball is returned to the pool.")]
        [SerializeField] private float _floorHeight = -3f;
        /// <summary>Collisions are ignored briefly after launch so the barrel cannot kill the shot.</summary>
        [SerializeField] private float _collisionGrace = 0.08f;

        private Rigidbody _rigidbody;
        private BallPool _pool;
        private Transform _holdAnchor;
        private float _stateAge;

        public BallState State { get; private set; } = BallState.Idle;

        public Rigidbody Body => _rigidbody;
        public bool IsInCollisionGrace => _stateAge <= _collisionGrace;

        /// <summary>Raised when pool recovery invalidates an external holder's ownership.</summary>
        public event Action<Ball> OwnershipInvalidated;

        /// <summary>Set once when the pool creates the instance.</summary>
        public void BindPool(BallPool pool)
        {
            _pool = pool;
            EnsureBody();
        }

        private void Awake()
        {
            EnsureBody();
        }

        private void EnsureBody()
        {
            if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();
        }

        public void ResetToIdle()
        {
            _holdAnchor = null;
            OwnershipInvalidated?.Invoke(this);
            SetState(BallState.Idle);
            transform.SetParent(null, true);
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.isKinematic = true;
            gameObject.SetActive(false);
        }

        public void LaunchIncoming(Vector3 position, Vector3 velocity)
        {
            gameObject.SetActive(true);
            transform.SetParent(null, true);
            _holdAnchor = null;
            transform.position = position;
            _rigidbody.isKinematic = false;
            _rigidbody.position = position;
            _rigidbody.linearVelocity = velocity;
            _rigidbody.angularVelocity = UnityEngine.Random.insideUnitSphere * 4f;
            SetState(BallState.Incoming);
        }

        /// <summary>Snaps the ball into a glove. Returns false if it is no longer catchable.</summary>
        public bool TryGrab(Transform gripAnchor)
        {
            if (gripAnchor == null || (State != BallState.Incoming && State != BallState.Thrown))
            {
                return false;
            }

            _holdAnchor = gripAnchor;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            _rigidbody.isKinematic = true;
            transform.SetParent(gripAnchor, false);
            transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            SyncToHoldAnchor();
            SetState(BallState.Held);
            return true;
        }

        public void Release(Vector3 velocity)
        {
            if (State != BallState.Held)
            {
                return;
            }

            transform.SetParent(null, true);
            _holdAnchor = null;
            _rigidbody.isKinematic = false;
            _rigidbody.linearVelocity = velocity;
            _rigidbody.angularVelocity = UnityEngine.Random.insideUnitSphere * 6f;
            SetState(BallState.Thrown);
        }

        /// <summary>Ends the ball's life and hands it back to the pool.</summary>
        public void Kill(string reason)
        {
            if (State == BallState.Dead || State == BallState.Idle)
            {
                return;
            }

            SetState(BallState.Dead);
            transform.SetParent(null, true);
            _holdAnchor = null;
            if (_pool != null)
            {
                _pool.Return(this, reason);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            _stateAge += Time.deltaTime;

            switch (State)
            {
                case BallState.Held:
                    return;
                case BallState.Incoming:
                case BallState.Thrown:
                    if (_stateAge > _maxLifetime)
                    {
                        Kill("lifetime");
                        return;
                    }

                    if (_stateAge > _collisionGrace && transform.position.y <= _floorHeight)
                    {
                        Kill("floor");
                    }

                    break;
            }
        }

        private void LateUpdate()
        {
            if (State != BallState.Held || _holdAnchor == null)
            {
                return;
            }

            SyncToHoldAnchor();
        }

        private void SyncToHoldAnchor()
        {
            _rigidbody.isKinematic = true;
            if (transform.parent != _holdAnchor)
            {
                transform.SetParent(_holdAnchor, false);
            }

            var position = _holdAnchor.position;
            var rotation = _holdAnchor.rotation;
            transform.SetPositionAndRotation(position, rotation);
            _rigidbody.position = position;
            _rigidbody.rotation = rotation;
        }

        private void SetState(BallState state)
        {
            State = state;
            _stateAge = 0f;
        }
    }
}
