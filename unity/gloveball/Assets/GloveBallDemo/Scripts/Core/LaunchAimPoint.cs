using UnityEngine;

namespace GloveBallDemo.Core
{
    /// <summary>Applies deterministic launcher offsets in the player's horizontal frame.</summary>
    public static class LaunchAimPoint
    {
        public static Vector3 Apply(Vector3 basePoint, Vector3 playerForward, Vector3 playerRight, float lateralOffset, float depthOffset)
        {
            playerForward.y = 0f;
            playerRight.y = 0f;
            var forward = playerForward.sqrMagnitude > 1e-4f ? playerForward.normalized : Vector3.forward;
            var right = playerRight.sqrMagnitude > 1e-4f ? playerRight.normalized : Vector3.right;
            return basePoint + right * lateralOffset + forward * depthOffset;
        }
    }
}
