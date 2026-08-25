// Editor-only automation belongs in the runtime assembly because Unity cannot AddComponent an Editor script.
#if UNITY_EDITOR
using GloveBallDemo.Core;
using UnityEngine;
using UnityEngine.InputSystem.XR;

namespace GloveBallDemo.Runtime
{
    /// <summary>Drives the grip-hold and trigger-charge loop during batch smoke runs.</summary>
    public sealed class ScriptedHandDriver : MonoBehaviour
    {
        private const float HoldSeconds = 0.35f;
        private const float TrackSpeed = 12f;
        private const float ReturnSpeed = 4f;
        private const float ReachRadius = 0.95f;
        private const float InterceptOffset = 0.45f;
        private const float MaxLeadTime = 3f;

        private enum Mode { Seek, Hold, Charge }

        private GloveController _glove;
        private Transform _hand;
        private Transform _head;
        private Ball[] _balls;
        private TargetPanel[] _panels;
        private Vector3 _homePosition;
        private Mode _mode;
        private float _timer;
        private TargetPanel _target;

        public int Catches { get; private set; }
        public int Throws { get; private set; }

        private void Start()
        {
            foreach (var glove in FindObjectsByType<GloveController>(FindObjectsSortMode.None))
            {
                if (glove.transform.parent != null && glove.transform.parent.name == "RightHand")
                {
                    _glove = glove;
                    break;
                }
            }
            if (_glove == null)
            {
                Debug.LogError("[ScriptedHands] no RightHand glove found");
                enabled = false;
                return;
            }

            _hand = _glove.transform.parent;
            var pose = _hand.GetComponent<TrackedPoseDriver>();
            if (pose != null) pose.enabled = false;
            _head = Camera.main != null ? Camera.main.transform : _hand.parent;
            _homePosition = _hand.position;
            _balls = FindObjectsByType<Ball>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            _panels = FindObjectsByType<TargetPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        }

        private void Update()
        {
            if (_glove == null) return;
            switch (_mode)
            {
                case Mode.Seek: TickSeek(); break;
                case Mode.Hold: TickHold(); break;
                case Mode.Charge: TickCharge(); break;
            }
        }

        private void TickSeek()
        {
            _glove.SetTriggerOverride(0f);
            _glove.SetGripOverride(1f);
            if (_glove.IsHolding)
            {
                Catches++;
                _timer = 0f;
                _mode = Mode.Hold;
                return;
            }

            var ball = FindInterceptable(out var intercept);
            if (ball == null)
            {
                MoveHandTo(_homePosition, ReturnSpeed);
                return;
            }
            MoveHandTo(intercept, TrackSpeed);
        }

        private void TickHold()
        {
            if (!_glove.IsHolding)
            {
                _glove.SetGripOverride(0f);
                _mode = Mode.Seek;
                return;
            }
            _timer += Time.deltaTime;
            MoveHandTo(_homePosition + new Vector3(0f, 0.1f, -0.25f), ReturnSpeed);
            if (_timer < HoldSeconds) return;

            _target = FindThrowTarget();
            if (_target == null || !AimAtTarget(_target))
            {
                _mode = Mode.Seek;
                return;
            }
            _timer = 0f;
            _glove.SetTriggerOverride(1f);
            _mode = Mode.Charge;
        }

        private void TickCharge()
        {
            if (!_glove.IsHolding)
            {
                _glove.SetTriggerOverride(0f);
                _mode = Mode.Seek;
                return;
            }
            if (_target != null) AimAtTarget(_target);
            _timer += Time.deltaTime;
            if (_timer < ThrowCharge.FullChargeSeconds) return;

            _glove.SetTriggerOverride(0f);
            _glove.SetGripOverride(0f);
            Throws++;
            _mode = Mode.Seek;
        }

        private bool AimAtTarget(TargetPanel target)
        {
            var anchor = _glove.HandRoot.position;
            if (!BallisticSolver.TrySolveWithSpeedClamped(anchor, target.transform.position, ThrowCharge.MaximumSpeed,
                    Mathf.Abs(Physics.gravity.y), false, out var velocity, out _))
            {
                return false;
            }
            _hand.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
            return true;
        }

        private Ball FindInterceptable(out Vector3 interceptPoint)
        {
            interceptPoint = _homePosition;
            Ball best = null;
            var bestLead = float.MaxValue;
            var plane = _head.position.z + InterceptOffset;
            foreach (var ball in _balls)
            {
                if (ball == null || ball.State != BallState.Incoming || ball.Body.linearVelocity.z > -0.1f) continue;
                var lead = (plane - ball.transform.position.z) / ball.Body.linearVelocity.z;
                if (lead <= 0f || lead > MaxLeadTime || lead >= bestLead) continue;
                best = ball;
                bestLead = lead;
                interceptPoint = BallisticSolver.Predict(ball.transform.position, ball.Body.linearVelocity, lead, Mathf.Abs(Physics.gravity.y));
            }
            if (best != null) interceptPoint = ClampToReach(interceptPoint);
            return best;
        }

        private Vector3 ClampToReach(Vector3 point)
        {
            var shoulder = _head.position + new Vector3(0f, -0.25f, 0f);
            var offset = point - shoulder;
            return offset.magnitude > ReachRadius ? shoulder + (offset.normalized * ReachRadius) : point;
        }

        private TargetPanel FindThrowTarget()
        {
            TargetPanel best = null;
            var nearest = float.MaxValue;
            foreach (var panel in _panels)
            {
                if (panel == null || !panel.isActiveAndEnabled) continue;
                var distance = (panel.transform.position - _hand.position).sqrMagnitude;
                if (distance < nearest) { best = panel; nearest = distance; }
            }
            return best;
        }

        private void MoveHandTo(Vector3 target, float speed) =>
            _hand.position = Vector3.MoveTowards(_hand.position, target, speed * Time.deltaTime);

        private void OnDisable()
        {
            if (_glove != null) _glove.ClearTriggerOverride();
            if (_glove != null) _glove.ClearGripOverride();
        }
    }
}
#endif
