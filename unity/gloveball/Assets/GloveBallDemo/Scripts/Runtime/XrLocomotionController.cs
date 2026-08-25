using UnityEngine;
using UnityEngine.InputSystem;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// Applies the original arena's Touch-stick locomotion to the generated XR Origin.
    /// </summary>
    public sealed class XrLocomotionController : MonoBehaviour
    {
        [SerializeField] private InputActionProperty _moveInput;
        [SerializeField] private InputActionProperty _snapTurnInput;
        [SerializeField] private Transform _head;

        [Header("Move")]
        [SerializeField] private float _moveSpeed = 2.5f;
        [SerializeField] private Vector2 _playAreaCenter;
        [SerializeField] private Vector2 _playAreaSize;
        [SerializeField] private float _boundaryPadding = 0.25f;

        [Header("Snap Turn")]
        [SerializeField] private float _snapTurnDegrees = 45f;
        [Range(0f, 1f)] [SerializeField] private float _snapTurnDeadzone = 0.7f;

        private bool _snapTurnReady = true;

        private void OnEnable()
        {
            EnableInput(_moveInput);
            EnableInput(_snapTurnInput);
        }

        private void Update()
        {
            if (GameInputGate.IsBlocked)
            {
                return;
            }

            Move();
            SnapTurn();
        }

        private void Move()
        {
            var move = ReadVector2(_moveInput);
            if (move.sqrMagnitude < 1e-4f)
            {
                return;
            }

            var forward = HorizontalDirection(_head != null ? _head.forward : transform.forward, transform.forward);
            var right = Vector3.Cross(Vector3.up, forward);
            var worldMove = (right * move.x) + (forward * move.y);
            MoveWithinPlayArea(Vector3.ClampMagnitude(worldMove, 1f) * (_moveSpeed * Time.deltaTime));
        }

        private void MoveWithinPlayArea(Vector3 requestedMove)
        {
            if (_playAreaSize.x <= 0f || _playAreaSize.y <= 0f)
            {
                transform.position += requestedMove;
                return;
            }

            var headPosition = _head != null ? _head.position : transform.position;
            var requestedHead = headPosition + requestedMove;
            var halfWidth = Mathf.Max(0f, _playAreaSize.x * 0.5f - _boundaryPadding);
            var halfDepth = Mathf.Max(0f, _playAreaSize.y * 0.5f - _boundaryPadding);
            var constrainedHead = requestedHead;
            constrainedHead.x = Mathf.Clamp(requestedHead.x, _playAreaCenter.x - halfWidth, _playAreaCenter.x + halfWidth);
            constrainedHead.z = Mathf.Clamp(requestedHead.z, _playAreaCenter.y - halfDepth, _playAreaCenter.y + halfDepth);
            transform.position += requestedMove + (constrainedHead - requestedHead);
        }

        private void SnapTurn()
        {
            var x = ReadVector2(_snapTurnInput).x;
            if (Mathf.Abs(x) < _snapTurnDeadzone)
            {
                _snapTurnReady = true;
                return;
            }

            if (!_snapTurnReady)
            {
                return;
            }

            var pivot = _head != null ? _head.position : transform.position;
            transform.RotateAround(pivot, Vector3.up, Mathf.Sign(x) * _snapTurnDegrees);
            _snapTurnReady = false;
        }

        private static void EnableInput(InputActionProperty input)
        {
            var action = input.action;
            if (action != null && !action.enabled)
            {
                action.Enable();
            }
        }

        private static Vector2 ReadVector2(InputActionProperty input)
        {
            var action = input.action;
            return action != null ? action.ReadValue<Vector2>() : Vector2.zero;
        }

        private static Vector3 HorizontalDirection(Vector3 direction, Vector3 fallback)
        {
            var horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (horizontal.sqrMagnitude < 1e-4f)
            {
                horizontal = Vector3.ProjectOnPlane(fallback, Vector3.up);
            }

            return horizontal.sqrMagnitude < 1e-4f ? Vector3.forward : horizontal.normalized;
        }
    }
}
