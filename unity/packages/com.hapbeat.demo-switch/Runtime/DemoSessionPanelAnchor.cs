using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// Scene placement of the Demo Session completion panel. When an active one exists in the loaded
    /// scenes (the first by instance ID if several), the panel appears at this transform's position
    /// instead of in front of the HMD, and stays there. Facing: with <see cref="FaceUser"/> the panel
    /// turns about the vertical axis toward the HMD when it appears; otherwise it takes this transform's
    /// rotation, whose +Z points away from the viewer like a world-space Canvas.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DemoSessionPanelAnchor : MonoBehaviour
    {
        [Tooltip("Turn the panel toward the participant (yaw only) when it appears. Off: use this transform's rotation (+Z away from the viewer).")]
        [SerializeField] private bool _faceUser = true;

        public bool FaceUser
        {
            get => _faceUser;
            set => _faceUser = value;
        }

        /// <summary>The first active and enabled anchor, or null (the panel then goes in front of the HMD).</summary>
        public static DemoSessionPanelAnchor FindActive()
        {
            foreach (var anchor in FindObjectsByType<DemoSessionPanelAnchor>(FindObjectsSortMode.InstanceID))
                if (anchor.isActiveAndEnabled) return anchor;
            return null;
        }

        /// <summary>Panel pose for a viewer at <paramref name="headPosition"/>.</summary>
        public Pose ResolvePose(Vector3 headPosition)
        {
            var position = transform.position;
            if (!_faceUser) return new Pose(position, transform.rotation);
            var away = Vector3.ProjectOnPlane(position - headPosition, Vector3.up);
            if (away.sqrMagnitude < 0.0001f) away = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (away.sqrMagnitude < 0.0001f) away = Vector3.forward;
            return new Pose(position, Quaternion.LookRotation(away.normalized, Vector3.up));
        }

        private void OnDrawGizmos()
        {
            // Completion panel footprint (about 0.44 m x 0.35 m) and the viewing direction.
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.8f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(0.44f, 0.35f, 0.005f));
            Gizmos.DrawLine(Vector3.zero, Vector3.back * 0.15f);
        }
    }
}
