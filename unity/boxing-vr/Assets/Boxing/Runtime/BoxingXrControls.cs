using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace Hapbeat.Boxing
{
    // One input path for OpenXR devices and XRI's Input System simulated devices.
    public sealed class BoxingXrControls : IDisposable
    {
        public struct Frame
        {
            public Pose head, left, right;
            public bool headTracked, leftTracked, rightTracked, menu, confirm;
            public float navigate;
        }

        private readonly InputActionMap map = new InputActionMap("Boxing XR");
        private readonly TrackedPose head, left, right;
        private readonly InputAction menu, confirm, leftNavigate, rightNavigate;

        public BoxingXrControls()
        {
            head = new TrackedPose(map, "Head", "<XRHMD>", "centerEye");
            left = new TrackedPose(map, "Left", "<XRController>{LeftHand}", "device");
            right = new TrackedPose(map, "Right", "<XRController>{RightHand}", "device");
            menu = map.AddAction("Menu", InputActionType.Button);
            menu.AddBinding("<XRController>{LeftHand}/menuButton");
            menu.AddBinding("<XRController>{LeftHand}/secondaryButton");
            menu.AddBinding("<XRController>{RightHand}/secondaryButton");
            confirm = map.AddAction("Confirm", InputActionType.Button);
            confirm.AddBinding("<XRController>{LeftHand}/primaryButton");
            confirm.AddBinding("<XRController>{RightHand}/primaryButton");
            leftNavigate = map.AddAction("Left Navigate", InputActionType.Value, "<XRController>{LeftHand}/primary2DAxis");
            rightNavigate = map.AddAction("Right Navigate", InputActionType.Value, "<XRController>{RightHand}/primary2DAxis");
            map.Enable();
        }

        public Frame Read()
        {
            var frame = new Frame();
            frame.headTracked = head.Read(out frame.head);
            frame.leftTracked = left.Read(out frame.left);
            frame.rightTracked = right.Read(out frame.right);
            frame.menu = menu.IsPressed(); frame.confirm = confirm.IsPressed();
            float leftY = leftNavigate.ReadValue<Vector2>().y, rightY = rightNavigate.ReadValue<Vector2>().y;
            // Compare vertical intent so a horizontal stick cannot suppress the other hand.
            frame.navigate = Mathf.Abs(leftY) >= Mathf.Abs(rightY) ? leftY : rightY;
            return frame;
        }

        public void Dispose() => map.Dispose();

        private sealed class TrackedPose
        {
            private readonly InputAction position, rotation, tracked, state;
            public TrackedPose(InputActionMap map, string name, string device, string prefix)
            {
                position = map.AddAction(name + " Position", InputActionType.Value, device + "/" + prefix + "Position");
                rotation = map.AddAction(name + " Rotation", InputActionType.Value, device + "/" + prefix + "Rotation");
                tracked = map.AddAction(name + " Tracked", InputActionType.Button, device + "/isTracked");
                state = map.AddAction(name + " State", InputActionType.Value, device + "/trackingState");
            }
            public bool Read(out Pose pose)
            {
                pose = new Pose(position.ReadValue<Vector3>(), rotation.ReadValue<Quaternion>());
                const int required = (int)(InputTrackingState.Position | InputTrackingState.Rotation);
                return tracked.IsPressed() && (state.ReadValue<int>() & required) == required &&
                    position.controls.Count > 0 && rotation.controls.Count > 0;
            }
        }
    }
}
