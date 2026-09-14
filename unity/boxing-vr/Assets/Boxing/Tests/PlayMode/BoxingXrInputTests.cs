using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace Hapbeat.Boxing.Tests
{
    public sealed class BoxingXrInputTests : InputTestFixture
    {
        private XRSimulatedHMD head;
        private XRSimulatedController left, right;
        private BoxingXrControls controls;
        public override void Setup()
        {
            base.Setup();
            InputSystem.RegisterLayout<XRSimulatedHMD>(); InputSystem.RegisterLayout<XRSimulatedController>();
            head = InputSystem.AddDevice<XRSimulatedHMD>();
            left = InputSystem.AddDevice<XRSimulatedController>(); right = InputSystem.AddDevice<XRSimulatedController>();
            InputSystem.SetDeviceUsage(left, new InternedString("LeftHand"));
            InputSystem.SetDeviceUsage(right, new InternedString("RightHand"));
            controls = new BoxingXrControls();
        }
        public override void TearDown()
        {
            controls?.Dispose();
            if (head != null && head.added) InputSystem.RemoveDevice(head);
            if (left != null && left.added) InputSystem.RemoveDevice(left);
            if (right != null && right.added) InputSystem.RemoveDevice(right);
            base.TearDown();
        }
        private void Send(int rightTracking = 3, bool rightTracked = true, ushort buttons = 0)
        {
            InputSystem.QueueStateEvent(head, new XRSimulatedHMDState { centerEyePosition = new Vector3(0, 1.6f, 0), centerEyeRotation = Quaternion.identity, deviceRotation = Quaternion.identity, isTracked = true, trackingState = 3 });
            InputSystem.QueueStateEvent(left, new XRSimulatedControllerState { devicePosition = new Vector3(-0.2f, 1.3f, 0.4f), deviceRotation = Quaternion.identity, isTracked = true, trackingState = 3, primary2DAxis = Vector2.up });
            InputSystem.QueueStateEvent(right, new XRSimulatedControllerState { devicePosition = new Vector3(0.3f, 1.4f, 0.6f), deviceRotation = Quaternion.identity, isTracked = rightTracked, trackingState = rightTracking, buttons = buttons });
            InputSystem.Update();
        }
        [Test] public void SimulatedDevicesDriveTheSameActionsAsRealXr()
        {
            Send(buttons: (ushort)(1 << (int)ControllerButton.PrimaryButton));
            var frame = controls.Read();
            Assert.That(frame.headTracked && frame.leftTracked && frame.rightTracked, Is.True);
            Assert.That(frame.head.position.y, Is.EqualTo(1.6f));
            Assert.That(frame.left.position.x, Is.EqualTo(-0.2f)); Assert.That(frame.right.position.z, Is.EqualTo(0.6f));
            Assert.That(frame.confirm, Is.True); Assert.That(frame.navigate, Is.EqualTo(1));
            Send(buttons: (ushort)(1 << (int)ControllerButton.SecondaryButton));
            Assert.That(controls.Read().menu, Is.True); Assert.That(controls.Read().confirm, Is.False);
        }
        [Test] public void PartialOrLostTrackingCannotBecomeAPunch()
        {
            Send(rightTracking: 1); Assert.That(controls.Read().rightTracked, Is.False);
            Send(rightTracked: false); Assert.That(controls.Read().rightTracked, Is.False);
            Send(); Assert.That(controls.Read().rightTracked, Is.True);
            InputSystem.RemoveDevice(right); InputSystem.Update(); Assert.That(controls.Read().rightTracked, Is.False);
        }
        [Test] public void ControllerAtOriginIsStillTracked()
        {
            Send();
            InputSystem.QueueStateEvent(left, new XRSimulatedControllerState { devicePosition = Vector3.zero, deviceRotation = Quaternion.identity, isTracked = true, trackingState = 3 });
            InputSystem.Update(); Assert.That(controls.Read().leftTracked, Is.True);
        }
    }
}
