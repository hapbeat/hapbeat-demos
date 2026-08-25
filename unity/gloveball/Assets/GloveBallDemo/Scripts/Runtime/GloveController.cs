using GloveBallDemo.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GloveBallDemo.Runtime
{
    public enum GloveSide { Left, Right }

    /// <summary>Grip owns physical possession; trigger only charges a held ball.</summary>
    public sealed class GloveController : MonoBehaviour
    {
        [SerializeField] private GloveSide _side;
        [SerializeField] private InputActionProperty _gripInput;
        [SerializeField] private InputActionProperty _triggerInput;
        [SerializeField] private Transform _handRoot;
        [SerializeField] private Transform _gripAnchor;
        [SerializeField] private CatchVolume _catchVolume;
        [SerializeField] private Animator _handAnimator;
        [SerializeField] private AudioSource _chargeAudio;
        [SerializeField] private AudioClip _releaseClip;
        [Header("Held-ball aim ray")]
        [SerializeField] private LineRenderer _aimRay;
        [Min(0.1f)] [SerializeField] private float _aimRayMinimumLength = 0.75f;
        [Min(0.1f)] [SerializeField] private float _aimRayMaximumLength = 2.5f;
        [Min(0.001f)] [SerializeField] private float _aimRayWidth = 0.012f;
        [SerializeField] private Color _aimRayColor = new Color(0.55f, 0.9f, 1f, 0.85f);
        [Header("Grab acceptance")]
        [Tooltip("Seconds after a Grip press that an incoming ball may enter the CatchVolume and still be grabbed.")]
        [Min(0.05f)] [SerializeField] private float _grabWindowDuration = 0.3f;
        [SerializeField] private float _maximumTrackedSpeed = 12f;
        [SerializeField] private float _maximumAngularSpeed = 30f;

        private static readonly int GrabbedParam = Animator.StringToHash("Grabbed");
        private readonly ThrowCharge _charge = new ThrowCharge();
        private Ball _heldBall;
        private InputAction _gripAction;
        private InputAction _triggerAction;
        private bool _gripHeld;
        private bool _triggerHeld;
        private bool _isCharging;
        private bool _hasGrabbedParam;
        private float? _triggerOverride;
        private float? _gripOverride;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        private Vector3 _linearVelocity;
        private Vector3 _angularVelocity;
        private float _grabWindowRemaining;

        public bool IsHolding => _heldBall != null;
        public GloveSide Side => _side;
        public float Charge01 => _charge.Value;
        public Transform HandRoot => _handRoot != null ? _handRoot : transform;
        /// <summary>The authored palm point that owns a caught ball's local pose.</summary>
        public Transform GripAnchor => _gripAnchor != null ? _gripAnchor : HandRoot;
        public Vector3 TriggerThrowDirection => HandRoot.forward.normalized;

        public void SetTriggerOverride(float? value)
        {
            _triggerOverride = value;
            ApplyTrigger(!GameInputGate.IsBlocked && value.GetValueOrDefault() >= .5f);
        }
        public void ClearTriggerOverride() => SetTriggerOverride(null);
        public void SetGripOverride(float? value)
        {
            _gripOverride = value;
            ApplyGrip(!GameInputGate.IsBlocked && value.GetValueOrDefault() >= .5f);
        }
        public void ClearGripOverride() => SetGripOverride(null);

        private void Awake()
        {
            if (_handRoot == null) _handRoot = transform.parent != null ? transform.parent : transform;
            if (_catchVolume == null) _catchVolume = GetComponentInChildren<CatchVolume>();
            if (_catchVolume != null) _catchVolume.CandidateEntered += TryGrabCandidate;
            _previousPosition = HandRoot.position;
            _previousRotation = HandRoot.rotation;
            ConfigureAimRay();
            if (_handAnimator != null)
                foreach (var parameter in _handAnimator.parameters)
                    if (parameter.nameHash == GrabbedParam) { _hasGrabbedParam = true; break; }
            SetHandClosed(false);
        }

        private void OnEnable()
        {
            Subscribe(ref _gripAction, _gripInput.action, OnGripPerformed, OnGripCanceled);
            Subscribe(ref _triggerAction, _triggerInput.action, OnTriggerPerformed, OnTriggerCanceled);
            GameInputGate.BlockedChanged += OnInputGateChanged;
        }

        private void OnDisable()
        {
            Unsubscribe(ref _gripAction, OnGripPerformed, OnGripCanceled);
            Unsubscribe(ref _triggerAction, OnTriggerPerformed, OnTriggerCanceled);
            GameInputGate.BlockedChanged -= OnInputGateChanged;
            EndChargeFeedback(false);
            SetAimRayVisible(false);
        }

        private void OnDestroy()
        {
            if (_catchVolume != null) _catchVolume.CandidateEntered -= TryGrabCandidate;
            if (_heldBall != null) _heldBall.OwnershipInvalidated -= OnHeldBallOwnershipInvalidated;
        }

        private void Update()
        {
            SampleHandVelocity();
            TickGrabWindow(Time.deltaTime);
            if (_heldBall != null && _triggerHeld)
            {
                _charge.Tick(Time.deltaTime);
                HapticEventRelay.UpdateLoop(ChargeEvent, _heldBall.transform.position, .25f + .75f * _charge.Value);
            }
            RefreshAimRay();
        }

        private void OnGripPerformed(InputAction.CallbackContext _) { if (!_gripOverride.HasValue && !GameInputGate.IsBlocked) ApplyGrip(true); }
        private void OnGripCanceled(InputAction.CallbackContext _) { if (!_gripOverride.HasValue && !GameInputGate.IsBlocked) ApplyGrip(false); }
        private void OnTriggerPerformed(InputAction.CallbackContext _) { if (!_triggerOverride.HasValue && !GameInputGate.IsBlocked) ApplyTrigger(true); }
        private void OnTriggerCanceled(InputAction.CallbackContext _) { if (!_triggerOverride.HasValue && !GameInputGate.IsBlocked) ApplyTrigger(false); }

        private void ApplyGrip(bool pressed)
        {
            if (_gripHeld == pressed) return;
            _gripHeld = pressed;
            SetHandClosed(pressed);
            if (pressed)
            {
                _grabWindowRemaining = Mathf.Max(0f, _grabWindowDuration);
                TryGrabCandidate();
            }
            else
            {
                _grabWindowRemaining = 0f;
                if (_heldBall != null) ReleasePhysical();
            }
        }

        private void ApplyTrigger(bool pressed)
        {
            if (_triggerHeld == pressed) return;
            _triggerHeld = pressed;
            if (pressed && _heldBall != null) BeginCharge();
            else if (!pressed && _heldBall != null && _isCharging) ReleaseCharged();
        }

        private void TryGrabCandidate()
        {
            if (!_gripHeld || _grabWindowRemaining <= 0f || _heldBall != null || _catchVolume == null) return;
            var ball = _catchVolume.FindCatchable();
            if (ball == null || !ball.TryGrab(GripAnchor)) return;
            _heldBall = ball;
            _heldBall.OwnershipInvalidated += OnHeldBallOwnershipInvalidated;
            SetAimRayVisible(true);
            _charge.Reset();
            HapticEventRelay.Report(GrabEvent, ball.transform.position);
            DemoGameController.Instance?.OnBallCaught(this, ball);
            if (_triggerHeld) BeginCharge();
        }

        private void BeginCharge()
        {
            _charge.Reset();
            _isCharging = true;
            if (_chargeAudio != null) _chargeAudio.Play();
            HapticEventRelay.BeginLoop(ChargeEvent, _heldBall.transform.position, .25f);
        }

        private void ReleaseCharged()
        {
            Release(HandRoot.forward * _charge.Speed + _linearVelocity * .2f, _charge.Speed, true);
        }

        private void ReleasePhysical()
        {
            if (_isCharging)
            {
                ReleaseCharged();
                return;
            }

            var offset = _heldBall.transform.position - HandRoot.position;
            var velocity = _linearVelocity + Vector3.Cross(_angularVelocity, offset) + HandRoot.forward * 2f;
            Release(velocity, velocity.magnitude, false);
        }

        private void Release(Vector3 velocity, float gainSpeed, bool charged)
        {
            if (_heldBall == null) return;
            var ball = _heldBall;
            ball.OwnershipInvalidated -= OnHeldBallOwnershipInvalidated;
            _heldBall = null;
            SetAimRayVisible(false);
            _triggerHeld = false;
            EndChargeFeedback(charged);
            ball.Release(velocity);
            HapticEventRelay.Report(ReleaseEvent, ball.transform.position, Mathf.Clamp01(gainSpeed / ThrowCharge.MaximumSpeed));
            DemoGameController.Instance?.OnBallThrown(this, ball, gainSpeed);
            _charge.Reset();
        }

        private void OnInputGateChanged(bool blocked)
        {
            if (blocked)
            {
                NeutralizeForMenu();
            }
        }

        /// <summary>Returns a held ball without treating it as a player throw.</summary>
        public void NeutralizeForMenu()
        {
            var ball = _heldBall;
            if (ball != null)
            {
                ball.Kill("menu");
                if (_heldBall == ball)
                {
                    ClearExternalOwnership(ball);
                }
                return;
            }

            ClearInputState();
        }

        private void OnHeldBallOwnershipInvalidated(Ball ball)
        {
            if (_heldBall == ball)
            {
                ClearExternalOwnership(ball);
            }
        }

        private void ClearExternalOwnership(Ball ball)
        {
            ball.OwnershipInvalidated -= OnHeldBallOwnershipInvalidated;
            _heldBall = null;
            SetAimRayVisible(false);
            ClearInputState();
        }

        private void ClearInputState()
        {
            _gripHeld = false;
            _grabWindowRemaining = 0f;
            _triggerHeld = false;
            _charge.Reset();
            EndChargeFeedback(false);
            SetHandClosed(false);
            SetAimRayVisible(false);
        }

        private void SetHandClosed(bool closed)
        {
            if (_hasGrabbedParam)
            {
                // UGB authored Grabbed=true as its visually open, ball-holding pose. This demo
                // uses that pose at rest and the source bind pose as the closed Grip pose.
                _handAnimator.SetBool(GrabbedParam, !closed);
            }
        }

        private void EndChargeFeedback(bool playReleaseSound)
        {
            if (_chargeAudio != null)
            {
                _chargeAudio.Stop();
                if (playReleaseSound && _releaseClip != null) _chargeAudio.PlayOneShot(_releaseClip);
            }
            _isCharging = false;
            HapticEventRelay.EndLoop(ChargeEvent);
        }

        private void SampleHandVelocity()
        {
            var hand = HandRoot;
            var dt = Mathf.Max(Time.deltaTime, .0001f);
            _linearVelocity = Vector3.ClampMagnitude((hand.position - _previousPosition) / dt, _maximumTrackedSpeed);
            var delta = hand.rotation * Quaternion.Inverse(_previousRotation);
            delta.ToAngleAxis(out var angle, out var axis);
            if (angle > 180f) angle -= 360f;
            _angularVelocity = axis.sqrMagnitude > .0001f ? Vector3.ClampMagnitude(axis * (angle * Mathf.Deg2Rad / dt), _maximumAngularSpeed) : Vector3.zero;
            _previousPosition = hand.position;
            _previousRotation = hand.rotation;
        }

        private void ConfigureAimRay()
        {
            if (_aimRay == null) return;
            _aimRay.useWorldSpace = true;
            _aimRay.positionCount = 2;
            _aimRay.startWidth = _aimRayWidth;
            _aimRay.endWidth = _aimRayWidth;
            _aimRay.startColor = _aimRayColor;
            _aimRay.endColor = new Color(_aimRayColor.r, _aimRayColor.g, _aimRayColor.b, 0f);
            SetAimRayVisible(false);
        }

        private void RefreshAimRay()
        {
            if (_aimRay == null || !HasActualHeldBall())
            {
                SetAimRayVisible(false);
                return;
            }
            var start = GripAnchor.position;
            var length = Mathf.Lerp(
                Mathf.Min(_aimRayMinimumLength, _aimRayMaximumLength),
                Mathf.Max(_aimRayMinimumLength, _aimRayMaximumLength),
                Charge01);
            _aimRay.SetPosition(0, start);
            _aimRay.SetPosition(1, start + TriggerThrowDirection * length);
            _aimRay.enabled = true;
        }

        private bool HasActualHeldBall() =>
            _heldBall != null &&
            _heldBall.State == BallState.Held &&
            _heldBall.transform.IsChildOf(GripAnchor);

        /// <summary>Pure delta-time seam used by Update and deterministic EditMode tests.</summary>
        private void TickGrabWindow(float deltaTime)
        {
            if (_grabWindowRemaining > 0f)
                _grabWindowRemaining = Mathf.Max(0f, _grabWindowRemaining - Mathf.Max(0f, deltaTime));
        }

        private void SetAimRayVisible(bool visible)
        {
            if (_aimRay == null) return;
            if (visible) RefreshAimRay();
            else _aimRay.enabled = false;
        }

        private DemoHapticEvent GrabEvent => _side == GloveSide.Left ? DemoHapticEvent.LeftGrab : DemoHapticEvent.RightGrab;
        private DemoHapticEvent ReleaseEvent => _side == GloveSide.Left ? DemoHapticEvent.LeftRelease : DemoHapticEvent.RightRelease;
        private DemoHapticEvent ChargeEvent => _side == GloveSide.Left ? DemoHapticEvent.LeftChargeLoop : DemoHapticEvent.RightChargeLoop;

        private static void Subscribe(ref InputAction current, InputAction next, System.Action<InputAction.CallbackContext> performed, System.Action<InputAction.CallbackContext> canceled)
        { if (next == null || current == next) return; Unsubscribe(ref current, performed, canceled); current = next; current.performed += performed; current.canceled += canceled; if (!current.enabled) current.Enable(); }
        private static void Unsubscribe(ref InputAction current, System.Action<InputAction.CallbackContext> performed, System.Action<InputAction.CallbackContext> canceled)
        { if (current == null) return; current.performed -= performed; current.canceled -= canceled; current = null; }
    }
}
