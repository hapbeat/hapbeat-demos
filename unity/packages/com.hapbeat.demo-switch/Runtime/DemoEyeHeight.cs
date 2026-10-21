using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// The participant's eye height is forced to the demo's authored value (<see cref="Target"/>, settings Eye Height, default
    /// <see cref="DefaultTargetEyeHeight"/>) instead of following the headset's floor (Quest's boundary setup) or the participant's
    /// height. Applied by 視線をリセット (<see cref="DemoRecenter.ResetView"/>), while a system recenter settles, and once per scene at
    /// the first tracked head frame (the scene's <see cref="XrStartAlignment"/> first alignment, else <see cref="DemoRecenterWatch"/>).
    /// The registered host's <see cref="IDemoSessionHeight"/> wins; otherwise the XR Origin is moved vertically (<see cref="ApplyToRig"/>).
    /// The Hub is not corrected.
    /// </summary>
    public static class DemoEyeHeight
    {
        /// <summary>Eye height above the floor, metres, when the settings do not override it.</summary>
        public const float DefaultTargetEyeHeight = 1.65f;

        /// <summary>The eye height this runtime applies (settings Eye Height).</summary>
        public static float Target { get; private set; } = DefaultTargetEyeHeight;

        /// <summary>
        /// The change of the vertical offset (rig Y, or a demo's own height correction) that puts a head now
        /// <paramref name="headHeightInOrigin"/> above the floor at <paramref name="target"/>.
        /// </summary>
        public static float Correction(float headHeightInOrigin, float target) => target - headHeightInOrigin;

        internal static void Configure(float target) => Target = target;

        internal static void ResetForTests() => Target = DefaultTargetEyeHeight;

        /// <summary>
        /// Puts the head at <see cref="Target"/> (always: the callers decide when; the start of a scene only with a tracked head).
        /// The host's <see cref="IDemoSessionHeight"/> wins; otherwise the XR Origin moves so the head is <see cref="Target"/> above the
        /// origin's height when the scene loaded (its authored floor).
        /// </summary>
        internal static void Apply()
        {
            if (DemoSession.CurrentDemoId == DemoSwitchSettings.HubDemoId) return;
            if (DemoSession.CurrentHost is IDemoSessionHeight own)
            {
                own.ApplyEyeHeight(Target);
                return;
            }
            var rig = DemoRecenter.FindRig(out var head);
            if (rig == null || head == null)
            {
                Debug.LogWarning("[Demo Session] Eye height: no XR Origin or head camera in the scene.");
                return;
            }
            ApplyToRig(rig, head, DemoRecenter.StartFloor(rig), Target);
        }

        /// <summary>
        /// Moves <paramref name="rig"/> vertically so <paramref name="head"/> is <paramref name="target"/> above <paramref name="floor"/>.
        /// The rig itself is moved, not the XR Origin's Camera Floor Offset Object, which the XR Origin sets again on every tracking
        /// origin change (0 in Floor mode, Camera Y Offset in Device mode); either mode keeps the result.
        /// </summary>
        internal static void ApplyToRig(Transform rig, Transform head, float floor, float target)
        {
            rig.position += Vector3.up * Correction(head.position.y - floor, target);
        }
    }
}
