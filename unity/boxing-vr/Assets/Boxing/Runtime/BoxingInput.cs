using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using Unity.XR.CoreUtils;

namespace Hapbeat.Boxing
{
    [DefaultExecutionOrder(-200)]
    public sealed class BoxingInput : MonoBehaviour
    {
        public XROrigin origin;
        public Camera headCamera;
        public TrackedPoseDriver headDriver;
        public BoxingInputMode mode = BoxingInputMode.Controllers;
        public Vector3 controllerOffset = new Vector3(0, -0.015f, 0.08f);
        public Vector3 controllerRotation = new Vector3(-15, 0, 0);
        public bool HasTracking { get; private set; }
        public bool MenuPressed { get; private set; }
        public bool ConfirmPressed { get; private set; }
        public float Navigate { get; private set; }
        public BoxerPose Current { get; private set; }
        public bool HasOverride { get; private set; }
        private BoxerPose testPose;
        private XRHandSubsystem hands;
        private readonly List<XRHandSubsystem> handSystems = new List<XRHandSubsystem>();
        private readonly List<XRInputSubsystem> inputSystems = new List<XRInputSubsystem>();
        private bool oldMenu, oldConfirm, handMenuLatched, aligned;
        private float openHandsTime, leftPunch, rightPunch;
        private float desktopYaw, desktopPitch;

        public void SetTestPose(BoxerPose pose) { HasOverride = true; testPose = Current = pose; HasTracking = pose.valid; }
        public void ClearTestPose() => HasOverride = false;
        public void SelectMode(BoxingInputMode next)
        {
            mode = next; HasTracking = false; openHandsTime = 0; handMenuLatched = false;
        }
        public void Recenter()
        {
            if (headCamera == null || origin == null) return;
            origin.RotateAroundCameraUsingOriginUp(-headCamera.transform.eulerAngles.y);
            var p = headCamera.transform.position;
            origin.transform.position -= new Vector3(p.x, 0, p.z);
            aligned = true;
        }
        private void Update()
        {
            MenuPressed = ConfirmPressed = false; Navigate = 0;
            if (headDriver != null) headDriver.enabled = mode != BoxingInputMode.Desktop && !HasOverride;
            if (HasOverride) { Current = testPose; HasTracking = testPose.valid; return; }
            BoxerPose frame = new BoxerPose { timestamp = Time.realtimeSinceStartupAsDouble };
            if (mode == BoxingInputMode.Desktop) ReadDesktop(ref frame);
            else
            {
                bool headValid = TryNode(XRNode.Head, out var hp);
                // Head transform is driven by the template's Input System TrackedPoseDriver (Update + BeforeRender).
                if (headValid && !aligned)
                {
                    SubsystemManager.GetSubsystems(inputSystems);
                    foreach (var system in inputSystems) system.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                    Recenter();
                }
                frame.head = headCamera.transform.position; frame.headRotation = headCamera.transform.rotation;
                if (mode == BoxingInputMode.Controllers)
                {
                    bool l = TryNode(XRNode.LeftHand, out var lp), r = TryNode(XRNode.RightHand, out var rp);
                    var space = headCamera.transform.parent;
                    frame.left = space.TransformPoint(lp.position + lp.rotation * controllerOffset);
                    frame.right = space.TransformPoint(rp.position + rp.rotation * controllerOffset);
                    frame.leftRotation = space.rotation * lp.rotation * Quaternion.Euler(controllerRotation);
                    frame.rightRotation = space.rotation * rp.rotation * Quaternion.Euler(controllerRotation);
                    frame.valid = headValid && l && r; frame.leftClosed = frame.rightClosed = true;
                }
                else
                {
                    FindHands();
                    bool l = ReadHand(true, out frame.left, out frame.leftRotation, out frame.leftClosed);
                    bool r = ReadHand(false, out frame.right, out frame.rightRotation, out frame.rightClosed);
                    frame.valid = headValid && l && r;
                }
                ReadButtons();
            }
            if (Keyboard.current != null)
            {
                MenuPressed |= Keyboard.current.escapeKey.wasPressedThisFrame;
                ConfirmPressed |= Keyboard.current.enterKey.wasPressedThisFrame;
                if (Keyboard.current.upArrowKey.wasPressedThisFrame) Navigate = 1;
                if (Keyboard.current.downArrowKey.wasPressedThisFrame) Navigate = -1;
            }
            if (mode == BoxingInputMode.Hands)
            {
                bool open = frame.valid && !frame.leftClosed && !frame.rightClosed;
                openHandsTime = open ? openHandsTime + Time.unscaledDeltaTime : 0;
                if (!open) handMenuLatched = false;
                if (openHandsTime > 1.2f && !handMenuLatched) { MenuPressed = true; handMenuLatched = true; }
            }
            Current = frame; HasTracking = frame.valid;
        }
        private static bool TryNode(XRNode node, out Pose pose)
        {
            pose = Pose.identity;
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid || !device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked) || !tracked) return false;
            bool p = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out var position);
            bool r = device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out var rotation);
            pose = new Pose(position, rotation); return p && r;
        }
        private void FindHands()
        {
            if (hands != null && hands.running) return;
            SubsystemManager.GetSubsystems(handSystems);
            hands = handSystems.Find(s => s.running);
        }
        private bool ReadHand(bool left, out Vector3 position, out Quaternion rotation, out bool closed)
        {
            position = Vector3.zero; rotation = Quaternion.identity; closed = false;
            if (hands == null || !hands.running) return false;
            XRHand hand = left ? hands.leftHand : hands.rightHand;
            if (!hand.isTracked || !hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm) ||
                !hand.GetJoint(XRHandJointID.MiddleTip).TryGetPose(out var tip) ||
                !hand.GetJoint(XRHandJointID.MiddleProximal).TryGetPose(out var knuckle) ||
                !hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist)) return false;
            var space = headCamera.transform.parent;
            position = space.TransformPoint(knuckle.position);
            Vector3 forward = knuckle.position - wrist.position;
            if (forward.sqrMagnitude <= 0.00001f) return false;
            rotation = space.rotation * Quaternion.LookRotation(forward.normalized, palm.rotation * Vector3.up);
            // Scale against this hand's measured palm length, not a fixed hand size.
            closed = Vector3.Distance(tip.position, palm.position) < Vector3.Distance(knuckle.position, wrist.position) * 0.95f;
            return true;
        }
        private void ReadButtons()
        {
            var l = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var r = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            l.TryGetFeatureValue(UnityEngine.XR.CommonUsages.menuButton, out bool menu);
            l.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool y);
            r.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool b);
            l.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool x);
            r.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool a);
            l.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 axis);
            bool m = menu || y || b, confirm = x || a;
            MenuPressed = m && !oldMenu; ConfirmPressed = confirm && !oldConfirm;
            oldMenu = m; oldConfirm = confirm; Navigate = axis.y;
        }
        private void ReadDesktop(ref BoxerPose frame)
        {
            var k = Keyboard.current; var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.isPressed)
            {
                desktopYaw += mouse.delta.ReadValue().x * 0.08f;
                desktopPitch = Mathf.Clamp(desktopPitch - mouse.delta.ReadValue().y * 0.08f, -45, 45);
            }
            float lean = k == null ? 0 : (k.dKey.isPressed ? 0.35f : 0) - (k.aKey.isPressed ? 0.35f : 0);
            float height = k != null && k.sKey.isPressed ? 1.2f : 1.65f;
            frame.head = new Vector3(lean, height, 0); frame.headRotation = Quaternion.Euler(desktopPitch, desktopYaw, 0);
            headCamera.transform.SetPositionAndRotation(frame.head, frame.headRotation);
            if (k != null && k.qKey.wasPressedThisFrame) leftPunch = 1;
            if (k != null && k.eKey.wasPressedThisFrame) rightPunch = 1;
            leftPunch = Mathf.Max(0, leftPunch - Time.unscaledDeltaTime * 3.5f);
            rightPunch = Mathf.Max(0, rightPunch - Time.unscaledDeltaTime * 3.5f);
            float l = Mathf.Sin(leftPunch * Mathf.PI) * 0.62f, r = Mathf.Sin(rightPunch * Mathf.PI) * 0.62f;
            bool guard = k != null && k.spaceKey.isPressed;
            frame.left = frame.head + new Vector3(guard ? -0.12f : -0.23f, guard ? -0.03f : -0.25f, 0.32f + l);
            frame.right = frame.head + new Vector3(guard ? 0.12f : 0.23f, guard ? -0.03f : -0.25f, 0.32f + r);
            frame.leftRotation = frame.rightRotation = Quaternion.identity;
            frame.leftClosed = frame.rightClosed = frame.valid = true;
        }
    }
}
