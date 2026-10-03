using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// Aligns the first tracked HMD frame with a scene-authored X/Z and yaw anchor.
    /// A later alignment is requested only when Demo Switch reads a fresh launch context, or by 視線をリセット
    /// (<see cref="DemoRecenter.ResetView"/>) when the scene host has no <see cref="IDemoSessionRecenter"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class XrStartAlignment : MonoBehaviour
    {
        [SerializeField] private XROrigin _xrOrigin;
        [SerializeField] private Transform _anchor;

        private Coroutine _alignmentCoroutine;
        private int _requestedAlignment;
        private int _completedAlignment;
        private bool _initialAlignmentRequested;

        private void OnEnable()
        {
            DemoSwitch.LaunchContextDetected += RequestAlignment;
            if (_initialAlignmentRequested) return;
            _initialAlignmentRequested = true;
            RequestAlignment();
        }

        private void OnDisable()
        {
            DemoSwitch.LaunchContextDetected -= RequestAlignment;
            if (_alignmentCoroutine == null) return;
            StopCoroutine(_alignmentCoroutine);
            _alignmentCoroutine = null;
        }

        private void RequestAlignment()
        {
            _requestedAlignment++;
            if (_alignmentCoroutine == null && isActiveAndEnabled)
                _alignmentCoroutine = StartCoroutine(AlignWhenTracked());
        }

        private IEnumerator AlignWhenTracked()
        {
            while (_completedAlignment < _requestedAlignment)
            {
                if (!TryResolveSceneReferences() || !IsHeadTracked())
                {
                    yield return null;
                    continue;
                }

                // TrackedPoseDriver also updates before render. Waiting until the end of that
                // frame ensures the camera transform contains the tracked pose we are aligning.
                yield return new WaitForEndOfFrame();
                if (!TryResolveSceneReferences() || !IsHeadTracked()) continue;

                var requestToComplete = _requestedAlignment;
                Align(_xrOrigin, _anchor);
                _completedAlignment = requestToComplete;
            }

            _alignmentCoroutine = null;
        }

        /// <summary>視線をリセット (<see cref="DemoRecenter.ResetView"/>): the first active alignment aligns again; false when the scene has none.</summary>
        internal static bool TryRealignActive()
        {
            foreach (var alignment in FindObjectsByType<XrStartAlignment>(FindObjectsSortMode.InstanceID))
            {
                if (!alignment.isActiveAndEnabled) continue;
                alignment.RequestAlignment();
                return true;
            }
            return false;
        }

        private bool TryResolveSceneReferences()
        {
            if (_anchor == null) _anchor = transform;
            if (_xrOrigin == null) _xrOrigin = FindAnyObjectByType<XROrigin>();
            return _anchor != null && _xrOrigin != null && _xrOrigin.Camera != null;
        }

        private static bool IsHeadTracked()
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return head.isValid &&
                   head.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) &&
                   tracked;
        }

        internal static void Align(XROrigin xrOrigin, Transform anchor)
        {
            var camera = xrOrigin.Camera;
            var yaw = ComputeYaw(camera.transform.forward, anchor.forward, xrOrigin.Origin.transform.up);
            xrOrigin.RotateAroundCameraUsingOriginUp(yaw);

            // RotateAroundCameraUsingOriginUp moves the origin. Re-read the camera afterwards,
            // then translate only world X/Z; floor tracking owns Y and Camera Offset.
            camera = xrOrigin.Camera;
            xrOrigin.Origin.transform.position += ComputeHorizontalDelta(camera.transform.position, anchor.position);
        }

        internal static float ComputeYaw(Vector3 cameraForward, Vector3 anchorForward, Vector3 originUp)
        {
            var from = Vector3.ProjectOnPlane(cameraForward, originUp);
            var to = Vector3.ProjectOnPlane(anchorForward, originUp);
            if (from.sqrMagnitude < 0.000001f || to.sqrMagnitude < 0.000001f) return 0f;
            return Vector3.SignedAngle(from, to, originUp);
        }

        internal static Vector3 ComputeHorizontalDelta(Vector3 cameraPosition, Vector3 anchorPosition) =>
            new Vector3(anchorPosition.x - cameraPosition.x, 0f, anchorPosition.z - cameraPosition.z);
    }
}
