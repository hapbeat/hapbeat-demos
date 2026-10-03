using System;
using UnityEngine;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.Hands;

namespace Hapbeat.DemoSwitch
{
    /// <summary>Hand input that opens the shared pause menu. The controller's menu button always works too.</summary>
    public enum DemoPauseGesture
    {
        /// <summary>Quest's left-hand system menu gesture (palm toward the face, pinch), read from Meta Hand Tracking Aim.</summary>
        SystemMenu = 0,
        /// <summary>Left palm toward the face with thumb and index pinched, held for 2 s (T-Rex's development pause).</summary>
        PalmPinchHold = 1
    }

    /// <summary>
    /// Shared pause for demos without their own menu (settings: Pause Menu). While paused the host's
    /// gameplay is paused, its haptics are off and Unity audio is paused; the panel offers
    /// "再開", "最初からやり直す" and, when the Hub is installed, "Hub に戻る". Works with or without a session.
    /// </summary>
    public static class DemoPause
    {
        private static DemoPausePanel _panel;
        private static bool _audioWasPaused;
        private static string _hubPackage;

        /// <summary>Fires with true when the pause panel opens and false when it closes.</summary>
        public static event Action<bool> PausedChanged;

        public static bool IsPaused => _panel != null;

        /// <summary>Replaces <see cref="DemoSwitch.ReturnToHub"/> in tests.</summary>
        internal static Func<bool> HubLauncher = DemoSwitch.ReturnToHub;

        /// <summary>Called by the bootstrap; null disables "Hub に戻る".</summary>
        internal static void Configure(string hubPackage) => _hubPackage = string.IsNullOrWhiteSpace(hubPackage) ? null : hubPackage;

        internal static bool CanReturnToHub => _hubPackage != null && DemoSession.Platform.IsPackageInstalled(_hubPackage);

        internal static DemoPausePanel Panel => _panel;

        internal static void ResetForTests(string hubPackage = null)
        {
            if (_panel != null) DestroyPanel();
            _hubPackage = hubPackage;
            _audioWasPaused = false;
            HubLauncher = DemoSwitch.ReturnToHub;
            PausedChanged = null;
        }

        /// <summary>The menu input: opens the pause, or resumes when already paused.</summary>
        public static void Toggle()
        {
            if (IsPaused) Resume();
            else Pause();
        }

        /// <summary>Opens the pause panel in front of the HMD. Refused while the completion panel is shown.</summary>
        public static bool Pause()
        {
            if (IsPaused) return true;
            if (DemoSession.IsCompletionShown) return false;
            var host = DemoSession.CurrentHost;
            host?.SetGameplayPaused(true);
            host?.SetHapticsEnabled(false);
            _audioWasPaused = AudioListener.pause;
            AudioListener.pause = true;
            _panel = DemoPausePanel.Create(CanReturnToHub);
            Debug.Log("[Demo Pause] Paused.");
            PausedChanged?.Invoke(true);
            return true;
        }

        /// <summary>Closes the panel and restores haptics (the session's ON/OFF state), audio and gameplay.</summary>
        public static void Resume()
        {
            if (!IsPaused) return;
            DestroyPanel();
            var host = DemoSession.CurrentHost;
            host?.SetHapticsEnabled(DemoSession.HapticsEnabled);
            host?.SetGameplayPaused(false);
            AudioListener.pause = _audioWasPaused;
            Debug.Log("[Demo Pause] Resumed.");
            PausedChanged?.Invoke(false);
        }

        internal static void RestartFromPause()
        {
            Resume();
            DemoSession.CurrentHost?.Restart();
        }

        /// <summary>Launches the Hub; on success this runtime stops haptics and sound and removes its task.</summary>
        internal static bool ReturnToHubFromPause()
        {
            if (!HubLauncher())
            {
                if (_panel != null) _panel.ShowError("Hub を起動できませんでした");
                return false;
            }
            DemoSession.CurrentHost?.SetHapticsEnabled(false);
            AudioListener.pause = true;
            DemoSession.Platform.FinishTask();
            return true;
        }

        private static void DestroyPanel()
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(_panel.gameObject);
            else UnityEngine.Object.DestroyImmediate(_panel.gameObject);
            _panel = null;
        }
    }

    /// <summary>
    /// Option B detector: the left palm faces the head (palm normal within 60° of the direction to the
    /// head) while thumb and index tips are pinched (closer than 1.5 cm, released past 3 cm once
    /// holding), continuously for <see cref="HoldSeconds"/>. Fires once per hold; releasing re-arms.
    /// Same thresholds as T-Rex's development pause.
    /// </summary>
    internal sealed class DemoPalmPinchHold
    {
        public const float HoldSeconds = 2f;
        public const float FacingDot = 0.5f;
        public const float PinchClose = 0.015f;
        public const float PinchRelease = 0.03f;
        private float _held;
        private bool _armed = true;

        public float Held => _held;

        /// <summary>Left hand only: forward x index side points out of the back of the hand, so the palm normal is its negative.</summary>
        public static bool PalmFacesHead(Vector3 wrist, Vector3 middleProximal, Vector3 indexProximal, Vector3 littleProximal, Vector3 palm, Vector3 head)
        {
            var normal = -Vector3.Cross(middleProximal - wrist, indexProximal - littleProximal).normalized;
            return Vector3.Dot(normal, (head - palm).normalized) > FacingDot;
        }

        public bool Update(bool facing, float pinchGap, float deltaTime)
        {
            var sign = facing && pinchGap < (_held > 0f ? PinchRelease : PinchClose);
            if (!sign)
            {
                _held = 0f;
                _armed = true;
                return false;
            }
            if (!_armed) return false;
            _held += deltaTime;
            if (_held < HoldSeconds) return false;
            _armed = false;
            _held = 0f;
            return true;
        }
    }

    /// <summary>
    /// The one place that decides "open/close the pause menu" each frame: the left controller's menu
    /// button, plus either Quest's left-hand system menu gesture (<see cref="MetaAimHand"/> aim flag
    /// <see cref="MetaAimFlags.MenuPressed"/>; needs the OpenXR feature Meta Hand Tracking Aim) or the
    /// palm-pinch hold, per <see cref="DemoPauseGesture"/>.
    /// </summary>
    internal sealed class DemoPauseInput : MonoBehaviour
    {
        private readonly DemoPalmPinchHold _palmPinch = new DemoPalmPinchHold();
        private bool _controllerMenuWas;
        private bool _systemMenuWas;

        public DemoPauseGesture Gesture { get; set; }

        private void Update()
        {
            if (Sample(Time.unscaledDeltaTime)) DemoPause.Toggle();
        }

        internal bool Sample(float deltaTime)
        {
            var controller = ControllerMenuHeld();
            var fired = controller && !_controllerMenuWas;
            _controllerMenuWas = controller;
            if (Gesture == DemoPauseGesture.SystemMenu)
            {
                var system = SystemMenuHeld();
                fired |= system && !_systemMenuWas;
                _systemMenuWas = system;
            }
            else fired |= SamplePalmPinch(deltaTime);
            // The completion panel owns input; never stack the pause on it.
            return fired && !DemoSession.IsCompletionShown;
        }

        private static bool ControllerMenuHeld()
        {
            var device = XRController.leftHand;
            if (device == null || !device.added) return false;
            var menu = device.TryGetChildControl<ButtonControl>("menu") ?? device.TryGetChildControl<ButtonControl>("menuButton");
            return menu != null && menu.isPressed;
        }

        private static bool SystemMenuHeld()
        {
            var hand = MetaAimHand.left;
            if (hand == null || !hand.added) return false;
            return ((MetaAimFlags)hand.aimFlags.ReadValue() & MetaAimFlags.MenuPressed) != 0;
        }

        private bool SamplePalmPinch(float deltaTime)
        {
            var camera = Camera.main;
            if (camera == null || !DemoSessionPointers.TryGetLeftHand(out var hand)) return _palmPinch.Update(false, float.MaxValue, deltaTime);
            var space = DemoSessionPointers.TrackingSpace();
            if (!TryJoint(hand, XRHandJointID.Wrist, space, out var wrist) || !TryJoint(hand, XRHandJointID.MiddleProximal, space, out var middle) ||
                !TryJoint(hand, XRHandJointID.IndexProximal, space, out var index) || !TryJoint(hand, XRHandJointID.LittleProximal, space, out var little) ||
                !TryJoint(hand, XRHandJointID.Palm, space, out var palm) || !TryJoint(hand, XRHandJointID.ThumbTip, space, out var thumbTip) ||
                !TryJoint(hand, XRHandJointID.IndexTip, space, out var indexTip))
                return _palmPinch.Update(false, float.MaxValue, deltaTime);
            var facing = DemoPalmPinchHold.PalmFacesHead(wrist, middle, index, little, palm, camera.transform.position);
            return _palmPinch.Update(facing, Vector3.Distance(thumbTip, indexTip), deltaTime);
        }

        private static bool TryJoint(XRHand hand, XRHandJointID id, Transform space, out Vector3 position)
        {
            position = default;
            if (!hand.GetJoint(id).TryGetPose(out var pose)) return false;
            position = DemoSessionPointers.ToWorld(space, pose.position);
            return true;
        }
    }

    /// <summary>"一時停止" panel: 0.55 m in front of the HMD when opened, then fixed in place.</summary>
    internal sealed class DemoPausePanel : MonoBehaviour
    {
        public const float InputDelaySeconds = 0.5f;
        public const float Distance = 0.55f;
        public const float Drop = 0.12f;
        private const float Width = 440f;
        private const float ButtonHeight = 68f;
        private const float ButtonGap = 14f;
        private DemoSessionPanel _panel;
        private Text _error;

        internal DemoSessionPanel Panel => _panel;
        internal DemoSessionButton ResumeButton { get; private set; }
        internal DemoSessionButton RestartButton { get; private set; }
        /// <summary>Null when the Hub is not installed.</summary>
        internal DemoSessionButton HubButton { get; private set; }
        internal string ErrorText => _error.text;

        public static DemoPausePanel Create(bool withHub)
        {
            var buttons = withHub ? 3 : 2;
            // Fixed slots: heading, buttons, and a reserved error line (no layout shift).
            var height = 28f + 46f + 18f + buttons * (ButtonHeight + ButtonGap) + 40f + 16f;
            var panel = DemoSessionPanel.Create("Hapbeat Demo Pause", new Vector2(Width, height));
            var pause = panel.gameObject.AddComponent<DemoPausePanel>();
            pause._panel = panel;
            var y = height * 0.5f - 28f - 23f;
            panel.AddText(new Vector2(0, y), new Vector2(Width - 40, 46), "一時停止", 34, Color.white);
            y -= 23f + 18f + ButtonHeight * 0.5f;
            var size = new Vector2(Width - 60, ButtonHeight);
            pause.ResumeButton = panel.AddButton(new Vector2(0, y), size, "再開", 24, DemoPause.Resume);
            y -= ButtonHeight + ButtonGap;
            pause.RestartButton = panel.AddButton(new Vector2(0, y), size, "最初からやり直す", 24, DemoPause.RestartFromPause);
            if (withHub)
            {
                y -= ButtonHeight + ButtonGap;
                pause.HubButton = panel.AddButton(new Vector2(0, y), size, "Hub に戻る", 24, () => DemoPause.ReturnToHubFromPause());
            }
            y -= ButtonHeight * 0.5f + ButtonGap + 20f;
            pause._error = panel.AddText(new Vector2(0, y), new Vector2(Width - 40, 40), string.Empty, 17, new Color(1f, 0.55f, 0.45f));
            var camera = Camera.main;
            if (camera != null) panel.PlaceInFront(camera.transform, Distance, Drop);
            panel.EnableInputAfter(InputDelaySeconds);
            return pause;
        }

        internal void ShowError(string message)
        {
            _error.text = message;
            _panel.EnableInputAfter(0.5f);
        }
    }
}
