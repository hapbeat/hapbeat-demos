using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GloveBallDemo.Runtime
{
    /// <summary>Controller-only headset menu. It deliberately avoids XR ray and EventSystem setup.</summary>
    public sealed class QuestMenuController : MonoBehaviour
    {
        [SerializeField] private DemoGameController _game;
        [SerializeField] private GameObject _panel;
        [SerializeField] private Text _modeText;
        [SerializeField] private Text _restartText;
        [SerializeField] private Text _hintText;
        [Header("Input")]
        [SerializeField] private InputActionProperty _leftMenuInput;
        [SerializeField] private InputActionProperty _leftSecondaryInput;
        [SerializeField] private InputActionProperty _rightSecondaryInput;
        [SerializeField] private InputActionProperty _navigateInput;
        [SerializeField] private InputActionProperty _leftPrimaryInput;
        [SerializeField] private InputActionProperty _rightPrimaryInput;

        private InputAction _leftMenuAction;
        private InputAction _leftSecondaryAction;
        private InputAction _rightSecondaryAction;
        private InputAction _navigateAction;
        private InputAction _leftPrimaryAction;
        private InputAction _rightPrimaryAction;
        private int _selection;
        private bool _navigateReady = true;

        private const float NavigatePressThreshold = 0.60f;
        private const float NavigateReleaseThreshold = 0.25f;

        public bool IsOpen { get; private set; }
        public int SelectionIndex => _selection;

        private void Awake()
        {
            SetOpen(false);
        }

        private void OnEnable()
        {
            Subscribe(ref _leftMenuAction, _leftMenuInput.action, Toggle);
            Subscribe(ref _leftSecondaryAction, _leftSecondaryInput.action, Toggle);
            Subscribe(ref _rightSecondaryAction, _rightSecondaryInput.action, Toggle);
            SubscribeNavigate(_navigateInput.action);
            Subscribe(ref _leftPrimaryAction, _leftPrimaryInput.action, Confirm);
            Subscribe(ref _rightPrimaryAction, _rightPrimaryInput.action, Confirm);
        }

        private void OnDisable()
        {
            Unsubscribe(ref _leftMenuAction, Toggle);
            Unsubscribe(ref _leftSecondaryAction, Toggle);
            Unsubscribe(ref _rightSecondaryAction, Toggle);
            UnsubscribeNavigate();
            Unsubscribe(ref _leftPrimaryAction, Confirm);
            Unsubscribe(ref _rightPrimaryAction, Confirm);
            SetOpen(false);
        }

        private void Toggle(InputAction.CallbackContext _) => SetOpen(!IsOpen);

        private void Navigate(InputAction.CallbackContext context)
        {
            if (!IsOpen)
            {
                return;
            }

            var y = context.ReadValue<Vector2>().y;
            if (Mathf.Abs(y) <= NavigateReleaseThreshold)
            {
                _navigateReady = true;
                return;
            }

            if (!_navigateReady || Mathf.Abs(y) < NavigatePressThreshold)
            {
                return;
            }

            _navigateReady = false;
            _selection = (_selection + 1) % 2;
            Refresh();
        }

        private void NavigateCanceled(InputAction.CallbackContext _) => _navigateReady = true;

        private void Confirm(InputAction.CallbackContext _)
        {
            if (!IsOpen || _game == null)
            {
                return;
            }

            if (_selection == 0)
            {
                _game.SetPlayMode(_game.PlayMode == DemoPlayMode.Waves
                    ? DemoPlayMode.EndlessRandom
                    : DemoPlayMode.Waves);
            }
            else
            {
                _game.RestartRun();
            }

            Refresh();
        }

        private void SetOpen(bool open)
        {
            IsOpen = open;
            GameInputGate.SetBlocked(open);
            if (_panel != null)
            {
                _panel.SetActive(open);
            }

            if (open)
            {
                var currentY = _navigateAction != null ? _navigateAction.ReadValue<Vector2>().y : 0f;
                _navigateReady = Mathf.Abs(currentY) <= NavigateReleaseThreshold;
                Refresh();
            }
        }

        private void Refresh()
        {
            if (_game == null)
            {
                return;
            }

            SetText(_modeText, $"{(_selection == 0 ? "> " : "  ")}MODE  {_game.PlayMode}");
            SetText(_restartText, $"{(_selection == 1 ? "> " : "  ")}RESTART RUN");
            SetText(_hintText, "THUMBSTICK: SELECT   A / X: CONFIRM   MENU / B / Y: CLOSE");
        }

        private static void Subscribe(ref InputAction current, InputAction next, System.Action<InputAction.CallbackContext> handler)
        {
            if (next == null || current == next) return;
            current = next;
            current.performed += handler;
            if (!current.enabled) current.Enable();
        }

        private static void Unsubscribe(ref InputAction current, System.Action<InputAction.CallbackContext> handler)
        {
            if (current == null) return;
            current.performed -= handler;
            current = null;
        }

        private void SubscribeNavigate(InputAction next)
        {
            if (next == null || _navigateAction == next) return;
            UnsubscribeNavigate();
            _navigateAction = next;
            _navigateAction.performed += Navigate;
            _navigateAction.canceled += NavigateCanceled;
            if (!_navigateAction.enabled) _navigateAction.Enable();
        }

        private void UnsubscribeNavigate()
        {
            if (_navigateAction == null) return;
            _navigateAction.performed -= Navigate;
            _navigateAction.canceled -= NavigateCanceled;
            _navigateAction = null;
        }

        private static void SetText(Text text, string value)
        {
            if (text != null) text.text = value;
        }
    }
}
