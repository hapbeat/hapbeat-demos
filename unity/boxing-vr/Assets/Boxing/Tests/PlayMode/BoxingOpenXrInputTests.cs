using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.XR.OpenXR.Features.Interactions;
using System.Collections;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace Hapbeat.Boxing.Tests
{
    // Use the installed OpenXR provider's actual layouts, not XRI simulated layouts.
    public sealed class BoxingOpenXrInputTests : InputTestFixture
    {
        public override void Setup()
        {
            base.Setup();
            InputSystem.RegisterLayout<UnityEngine.XR.OpenXR.Input.HapticControl>();
        }
        [TestCase(false)]
        [TestCase(true)]
        public void NativeTouchLayoutFeedsTrackedPoseAndLeftStick(bool touchPlus)
        {
            InputSystem.RegisterLayout<OculusTouchControllerProfile.OculusTouchController>();
            InputSystem.RegisterLayout<MetaQuestTouchPlusControllerProfile.QuestTouchPlusController>();
            var left = touchPlus ? (InputDevice)InputSystem.AddDevice<MetaQuestTouchPlusControllerProfile.QuestTouchPlusController>()
                : InputSystem.AddDevice<OculusTouchControllerProfile.OculusTouchController>();
            InputSystem.SetDeviceUsage(left, new InternedString("LeftHand"));
            using var controls = new BoxingXrControls();
            Set(left.GetChildControl<ButtonControl>("isTracked"), 1);
            Set(left.GetChildControl<IntegerControl>("trackingState"), 3);
            Set(left.GetChildControl<Vector3Control>("devicePosition"), new Vector3(-0.2f, 1.3f, 0.4f));
            Set(left.GetChildControl<QuaternionControl>("deviceRotation"), Quaternion.identity);
            Set(left.GetChildControl<Vector2Control>("thumbstick"), Vector2.down);
            Set(left.GetChildControl<ButtonControl>("primaryButton"), 1);
            var frame = controls.Read();
            Assert.That(frame.confirm, Is.True, "Native A/X must reach the game.");
            Assert.That(frame.leftTracked, Is.True, "Native tracked controller must not freeze the round.");
            Assert.That(frame.left.position, Is.EqualTo(new Vector3(-0.2f, 1.3f, 0.4f)));
            Assert.That(frame.navigate, Is.LessThan(-0.6f), "Native left stick must move the menu.");
        }

        [Test]
        public void NativeRightStickMovesMenuWithoutLeftStick()
        {
            InputSystem.RegisterLayout<OculusTouchControllerProfile.OculusTouchController>();
            var right = InputSystem.AddDevice<OculusTouchControllerProfile.OculusTouchController>();
            InputSystem.SetDeviceUsage(right, new InternedString("RightHand"));
            using var controls = new BoxingXrControls();
            Set(right.thumbstick, Vector2.down);
            Assert.That(controls.Read().navigate, Is.LessThan(-0.6f));
        }

        [UnityTest]
        public IEnumerator FirstTrackedPoseAndRecenterUseSceneStartMarker()
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            yield return SceneManager.LoadSceneAsync("Boxing");
            var game = Object.FindAnyObjectByType<BoxingGame>();
            InputSystem.RegisterLayout<XRSimulatedHMD>();
            var head = InputSystem.AddDevice<XRSimulatedHMD>();
            InputSystem.QueueStateEvent(head, new XRSimulatedHMDState { isTracked = true, trackingState = 3, centerEyePosition = new Vector3(0.8f, 1.65f, -0.6f), centerEyeRotation = Quaternion.Euler(0, 120, 0), deviceRotation = Quaternion.Euler(0, 120, 0) });
            InputSystem.Update(); yield return null; yield return null;
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(game.input.headCamera.transform.eulerAngles.y, 0)), Is.LessThan(0.1f));
            Assert.That(new Vector2(game.input.headCamera.transform.position.x, game.input.headCamera.transform.position.z).magnitude, Is.LessThan(0.001f));
            Assert.That(game.input.startPoint, Is.Not.Null);
            game.input.startPoint.position = new Vector3(0.4f, 0.003f, -0.2f);
            game.input.startPoint.rotation = Quaternion.Euler(0, 35, 0);
            game.input.Recenter(); yield return null;
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(game.input.headCamera.transform.eulerAngles.y, 35)), Is.LessThan(0.1f));
            Assert.That(game.input.headCamera.transform.position.x, Is.EqualTo(0.4f).Within(0.001f));
            Assert.That(game.input.headCamera.transform.position.z, Is.EqualTo(-0.2f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator StartupTrackingDelayMustNotSwitchControllerModeByGaze()
        {
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            yield return SceneManager.LoadSceneAsync("Boxing");
            var game = Object.FindAnyObjectByType<BoxingGame>();
            Assert.That(game.input.mode, Is.EqualTo(BoxingInputMode.Controllers), "Authoritative scene must start in Controllers.");
            game.menu.Open();
            game.input.headCamera.transform.LookAt(game.menu.rows[2].transform.position);
            // No tracked device yet: same delay as waiting for Air Link controllers.
            yield return new WaitForSecondsRealtime(2.6f);
            Assert.That(game.input.mode, Is.EqualTo(BoxingInputMode.Controllers), "Looking through the menu while tracking starts must not silently select Hands.");
            Assert.That(game.menu.Selection, Is.Zero, "Gaze must not override the controller menu selection.");

            InputSystem.RegisterLayout<XRSimulatedHMD>();
            InputSystem.RegisterLayout<OculusTouchControllerProfile.OculusTouchController>();
            var head = InputSystem.AddDevice<XRSimulatedHMD>();
            var left = InputSystem.AddDevice<OculusTouchControllerProfile.OculusTouchController>();
            var right = InputSystem.AddDevice<OculusTouchControllerProfile.OculusTouchController>();
            InputSystem.SetDeviceUsage(left, new InternedString("LeftHand"));
            InputSystem.SetDeviceUsage(right, new InternedString("RightHand"));
            InputSystem.QueueStateEvent(head, new XRSimulatedHMDState { isTracked = true, trackingState = 3, centerEyePosition = Vector3.up * 1.65f, centerEyeRotation = Quaternion.identity, deviceRotation = Quaternion.identity });
            Set(left.isTracked, 1); Set(left.trackingState, 3);
            Set(right.isTracked, 1); Set(right.trackingState, 3);
            Set(left.devicePosition, new Vector3(-0.2f, 1.3f, 0.35f)); Set(left.deviceRotation, Quaternion.identity);
            Set(right.devicePosition, new Vector3(0.2f, 1.3f, 0.35f)); Set(right.deviceRotation, Quaternion.identity);
            // Model a focused host after the batch-only unfocused start; tracking is not overridden.
            game.SendMessage("FocusChanged", true, SendMessageOptions.RequireReceiver);
            game.SendMessage("OnApplicationPause", false, SendMessageOptions.RequireReceiver);
            yield return null;
            Assert.That(game.input.HasTracking, Is.True);
            Set(right.thumbstick, Vector2.down); yield return null;
            Assert.That(game.menu.Selection, Is.EqualTo(1), "Right stick must move the actual menu.");
            Set(right.thumbstick, Vector2.zero); yield return null;
            Set(left.thumbstick, Vector2.up); yield return null;
            Assert.That(game.menu.Selection, Is.Zero, "Left stick must also move the actual menu.");
            Set(left.thumbstick, Vector2.zero); Set(right.primaryButton, 1); yield return null;
            Set(right.primaryButton, 0);
            Assert.That(game.menu.IsOpen, Is.False, "A must start the round.");
            Set(left.devicePosition, new Vector3(-0.25f, 1.4f, 0.4f));
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.That(game.presentation.leftGlove.gameObject.activeSelf, Is.True);
            Assert.That(Vector3.Distance(game.presentation.leftGlove.position, game.input.Current.left), Is.LessThan(0.001f));
            Assert.That(game.Round.Countdown, Is.LessThan(3), "Countdown must advance when controller tracking arrives.");
            yield return new WaitForSecondsRealtime(3.2f);
            Assert.That(game.Round.Phase, Is.EqualTo(BoxingPhase.Fighting));
            Assert.That(game.input.HasOverride, Is.False);
            Assert.That(game.feedback.Sends, Is.Zero);
        }
    }
}
