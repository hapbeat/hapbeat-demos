using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// A system recenter (Quest: hold the Meta button) moves the tracking origin, so world-fixed panels can
    /// end up beside or behind the user. Detected from <see cref="XRInputSubsystem.trackingOriginUpdated"/>
    /// and, because that event is raised by the native XR provider (not confirmed for Quest's long press),
    /// also from a jump of the head pose in tracking space within one ordinary frame. Listeners are
    /// notified every frame for <see cref="SettleSeconds"/> so that the origin change and a demo's own
    /// re-alignment are taken in. The open pause and completion panels go in front of the HMD again.
    /// </summary>
    public static class DemoRecenter
    {
        public const float SettleSeconds = 0.3f;
        /// <summary>Heading change within one frame that only a recenter (or tracking recovery) produces.</summary>
        public const float JumpYawDegrees = 30f;
        public const float JumpMetres = 0.25f;
        /// <summary>Longer frames (loading, returning from the background) are not compared.</summary>
        public const float MaxFrameSeconds = 0.05f;

        /// <summary>Raised every frame while a recenter settles (also by tracking recovery jumps).</summary>
        public static event Action Recentered;

        internal static void Notify()
        {
            DemoPause.OnRecenter();
            DemoSession.OnRecenter();
            var handlers = Recentered;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        /// <summary>Whether two consecutive head poses in tracking space are a recenter rather than head motion.</summary>
        internal static bool IsJump(Vector3 previousPosition, Quaternion previousRotation, Vector3 position, Quaternion rotation, float deltaTime)
        {
            if (deltaTime <= 0f || deltaTime > MaxFrameSeconds) return false;
            var moved = Vector3.ProjectOnPlane(position - previousPosition, Vector3.up).magnitude;
            var before = Vector3.ProjectOnPlane(previousRotation * Vector3.forward, Vector3.up);
            var after = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
            var turned = before.sqrMagnitude > 0.0001f && after.sqrMagnitude > 0.0001f ? Vector3.Angle(before, after) : 0f;
            return moved > JumpMetres || turned > JumpYawDegrees;
        }
    }

    /// <summary>Watches for recenters (added by the Demo Switch bootstrap) and drives <see cref="DemoRecenter"/>.</summary>
    internal sealed class DemoRecenterWatch : MonoBehaviour
    {
        private readonly List<XRInputSubsystem> _found = new List<XRInputSubsystem>();
        private readonly List<XRInputSubsystem> _subscribed = new List<XRInputSubsystem>();
        private bool _originUpdated;
        private float _settleUntil = -1f;
        private float _nextScan;
        private bool _hasHead;
        private Vector3 _headPosition;
        private Quaternion _headRotation;

        private void Update()
        {
            if (Time.realtimeSinceStartup < _nextScan) return;
            _nextScan = Time.realtimeSinceStartup + 1f;
            SubsystemManager.GetSubsystems(_found);
            foreach (var subsystem in _found)
            {
                if (_subscribed.Contains(subsystem)) continue;
                subsystem.trackingOriginUpdated += OnTrackingOriginUpdated;
                _subscribed.Add(subsystem);
            }
        }

        private void OnTrackingOriginUpdated(XRInputSubsystem subsystem) => _originUpdated = true;

        private void LateUpdate()
        {
            var source = _originUpdated ? "tracking origin updated" : null;
            _originUpdated = false;
            if (TryGetHead(out var position, out var rotation))
            {
                if (source == null && _hasHead && DemoRecenter.IsJump(_headPosition, _headRotation, position, rotation, Time.unscaledDeltaTime))
                    source = "head pose jump";
                _hasHead = true;
                _headPosition = position;
                _headRotation = rotation;
            }
            else _hasHead = false;

            var now = Time.realtimeSinceStartup;
            if (source != null)
            {
                Debug.Log("[Demo Session] DEMO_SESSION_RECENTER (" + source + ")");
                _settleUntil = now + DemoRecenter.SettleSeconds;
            }
            if (now <= _settleUntil) DemoRecenter.Notify();
        }

        /// <summary>The HMD pose in tracking space.</summary>
        private static bool TryGetHead(out Vector3 position, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            position = default;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid) return false;
            if (head.TryGetFeatureValue(CommonUsages.isTracked, out var tracked) && !tracked) return false;
            return (head.TryGetFeatureValue(CommonUsages.centerEyePosition, out position) || head.TryGetFeatureValue(CommonUsages.devicePosition, out position))
                && (head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out rotation) || head.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation));
        }

        private void OnDestroy()
        {
            foreach (var subsystem in _subscribed) subsystem.trackingOriginUpdated -= OnTrackingOriginUpdated;
            _subscribed.Clear();
        }
    }
}
