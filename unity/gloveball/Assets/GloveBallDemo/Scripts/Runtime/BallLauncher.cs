using GloveBallDemo.Core;
using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// Fires balls at a requested point. Every shot is telegraphed first (muzzle light + warning
    /// event) so the shot stays dodgeable; three return hits stun the launcher.
    /// </summary>
    public class BallLauncher : MonoBehaviour
    {
        [SerializeField] private Transform _muzzle;
        [SerializeField] private Light _warningLight;
        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField] private float _telegraphDuration = 0.6f;
        [Header("Ballistics")]
        [Tooltip("Elevation in degrees used for lob orders.")]
        [SerializeField] private float _lobElevation = 30f;
        [SerializeField] private int _hitsToStun = 3;
        [SerializeField] private float _stunDuration = 5f;
        [SerializeField] private Color _idleColor = new Color(0.6f, 0.15f, 0.2f);
        [SerializeField] private Color _stunColor = new Color(0.25f, 0.25f, 0.28f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private MaterialPropertyBlock _block;
        private BallPool _pool;
        private float _telegraphTimer;
        private float _stunTimer;
        private bool _shotPending;
        private Vector3 _pendingAimPoint;
        private float _pendingSpeed;
        private AimKind _pendingAim;
        private int _hitsTaken;
        private float _baseLightIntensity;

        public bool IsStunned => _stunTimer > 0f;

        /// <summary>Free to accept a new order (not stunned and not mid-telegraph).</summary>
        public bool CanFire => !IsStunned && !_shotPending;

        public Vector3 MuzzlePosition => _muzzle != null ? _muzzle.position : transform.position;

        public int ShotsFired { get; private set; }

        public void Bind(BallPool pool) => _pool = pool;

        private void Awake()
        {
            if (_muzzle == null)
            {
                _muzzle = transform;
            }

            if (_bodyRenderer == null)
            {
                _bodyRenderer = GetComponentInChildren<Renderer>();
            }

            _block = new MaterialPropertyBlock();
            if (_warningLight != null)
            {
                _baseLightIntensity = _warningLight.intensity;
                _warningLight.enabled = false;
            }

            ApplyColor(_idleColor);
        }

        /// <summary>Queues a telegraphed shot. Returns false when the launcher is busy or stunned.</summary>
        public bool RequestFire(Vector3 aimPoint, float speed, AimKind aim)
        {
            if (!CanFire)
            {
                return false;
            }

            _shotPending = true;
            _pendingAimPoint = aimPoint;
            _pendingSpeed = speed;
            _pendingAim = aim;
            _telegraphTimer = _telegraphDuration;

            if (_warningLight != null)
            {
                _warningLight.enabled = true;
            }

            HapticEventRelay.ReportHapticOnly(DemoHapticEvent.BallIncomingWarning, MuzzlePosition);
            return true;
        }

        /// <summary>Reports a return hit. Returns true when this hit caused the stun.</summary>
        public bool RegisterReturnHit()
        {
            if (IsStunned)
            {
                return false;
            }

            _hitsTaken++;
            if (_hitsTaken < _hitsToStun)
            {
                return false;
            }

            _hitsTaken = 0;
            _stunTimer = _stunDuration;
            CancelPendingShot();
            ApplyColor(_stunColor);
            return true;
        }

        public void ResetLauncher()
        {
            _hitsTaken = 0;
            _stunTimer = 0f;
            CancelPendingShot();
            ApplyColor(_idleColor);
        }

        private void Update()
        {
            if (_stunTimer > 0f)
            {
                _stunTimer -= Time.deltaTime;
                if (_stunTimer <= 0f)
                {
                    _stunTimer = 0f;
                    ApplyColor(_idleColor);
                }

                return;
            }

            if (!_shotPending)
            {
                return;
            }

            _telegraphTimer -= Time.deltaTime;
            if (_warningLight != null)
            {
                // Blink faster as the shot approaches.
                var phase = Mathf.PingPong(Time.time * 12f, 1f);
                _warningLight.intensity = _baseLightIntensity * (0.4f + 0.6f * phase);
            }

            if (_telegraphTimer <= 0f)
            {
                FireNow();
            }
        }

        private void FireNow()
        {
            _shotPending = false;
            if (_warningLight != null)
            {
                _warningLight.enabled = false;
                _warningLight.intensity = _baseLightIntensity;
            }

            if (_pool == null)
            {
                Debug.LogError($"[BallLauncher] {name} has no pool bound");
                return;
            }

            var origin = MuzzlePosition;
            var gravity = Mathf.Abs(Physics.gravity.y);
            Vector3 velocity;

            if (_pendingAim == AimKind.Lob)
            {
                if (!BallisticSolver.TrySolveWithElevation(origin, _pendingAimPoint, _lobElevation, gravity, out velocity) &&
                    !BallisticSolver.TrySolveWithSpeed(origin, _pendingAimPoint, _pendingSpeed, gravity, true, out velocity))
                {
                    Debug.LogWarning($"[BallLauncher] {name} could not solve a lob to {_pendingAimPoint}");
                    return;
                }
            }
            else
            {
                // Reaching the player is the wave schedule's job (its speed bands are derived from
                // the court length). The clamp below is a guard against a mis-tuned schedule, and
                // it is loud on purpose: quietly flying faster than the wave asked for is how a
                // difficulty curve stops meaning anything.
                if (!BallisticSolver.TrySolveWithSpeedClamped(
                        origin, _pendingAimPoint, _pendingSpeed, gravity, false, out velocity, out var usedSpeed))
                {
                    Debug.LogWarning($"[BallLauncher] {name} could not solve a shot to {_pendingAimPoint}");
                    return;
                }

                if (usedSpeed > _pendingSpeed + 1e-3f)
                {
                    Debug.LogWarning(
                        $"[BallLauncher] {name} was asked for {_pendingSpeed:0.0} m/s to {_pendingAimPoint}, " +
                        $"which cannot reach it; clamped to {usedSpeed:0.0} m/s. Raise the wave's MinSpeed.");
                }
            }

            var ball = _pool.Take();
            if (ball == null)
            {
                return;
            }

            // Keep the intended arrival point despite ball-specific air resistance.
            // Re-use the planned flight time; light balls depart faster and visibly slow down.
            if (ball.Body.linearDamping > 0f)
                velocity = CompensateAirResistance(origin, _pendingAimPoint, velocity, ball.Body.linearDamping);
            _muzzle.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
            ball.LaunchIncoming(origin, velocity);
            HapticEventRelay.PlayAudioOnly(DemoHapticEvent.BallIncomingWarning, origin);
            ShotsFired++;

            var controller = DemoGameController.Instance;
            if (controller != null)
            {
                controller.OnBallLaunched(this, ball, _pendingAim);
            }
        }

        private void CancelPendingShot()
        {
            _shotPending = false;
            _telegraphTimer = 0f;
            if (_warningLight != null)
            {
                _warningLight.enabled = false;
                _warningLight.intensity = _baseLightIntensity;
            }
        }

        public static Vector3 CompensateAirResistance(Vector3 origin, Vector3 target, Vector3 planned, float damping)
        {
            var horizontalSpeed = new Vector2(planned.x, planned.z).magnitude;
            if (horizontalSpeed < .01f || damping <= 0f) return planned;
            float time = new Vector2(target.x - origin.x, target.z - origin.z).magnitude / horizontalSpeed;
            float dt = Time.fixedDeltaTime;
            int steps = Mathf.Max(1, Mathf.RoundToInt(time / dt));
            float q = Mathf.Clamp01(1f - damping * dt);
            // PhysX discrete integration: v=(v+g*dt)*q, p+=v*dt.
            float velocityFactor = 1f, displacementFactor = 0f;
            Vector3 gravityVelocity = Vector3.zero, gravityDisplacement = Vector3.zero;
            for (int i = 0; i < steps; i++)
            {
                velocityFactor *= q;
                displacementFactor += velocityFactor * dt;
                gravityVelocity = (gravityVelocity + Physics.gravity * dt) * q;
                gravityDisplacement += gravityVelocity * dt;
            }
            return displacementFactor > .0001f ? (target - origin - gravityDisplacement) / displacementFactor : planned;
        }

        private void OnTriggerEnter(Collider other)
        {
            var ball = other.GetComponentInParent<Ball>();
            if (ball == null || ball.State != BallState.Thrown)
            {
                return;
            }

            var controller = DemoGameController.Instance;
            if (controller != null)
            {
                controller.OnLauncherHit(this, ball);
            }

            ball.Kill("launcher");
        }

        private void ApplyColor(Color color)
        {
            if (_bodyRenderer == null)
            {
                return;
            }

            _bodyRenderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            _block.SetColor(ColorId, color);
            _bodyRenderer.SetPropertyBlock(_block);
        }
    }
}
