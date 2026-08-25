using UnityEngine;

namespace GloveBallDemo.Core
{
    /// <summary>
    /// Closed-form projectile solutions used by the launchers. Pure functions, no scene access,
    /// so the accuracy of a shot can be asserted in EditMode tests.
    /// </summary>
    public static class BallisticSolver
    {
        public const float DefaultGravity = 9.81f;

        /// <summary>
        /// Launch velocity that hits <paramref name="target"/> with a given muzzle speed.
        /// Two arcs exist for a reachable target; <paramref name="highArc"/> picks the lofted one.
        /// Returns false when the speed is too low to reach the target at all.
        /// </summary>
        public static bool TrySolveWithSpeed(
            Vector3 origin,
            Vector3 target,
            float speed,
            float gravity,
            bool highArc,
            out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (speed <= 0f || gravity <= 0f)
            {
                return false;
            }

            var delta = target - origin;
            var flat = new Vector3(delta.x, 0f, delta.z);
            var d = flat.magnitude;
            var h = delta.y;

            if (d < 1e-4f)
            {
                // Straight up/down: no horizontal component to solve for.
                velocity = new Vector3(0f, h >= 0f ? speed : -speed, 0f);
                return true;
            }

            var v2 = speed * speed;
            var discriminant = v2 * v2 - gravity * (gravity * d * d + 2f * h * v2);
            if (discriminant < 0f)
            {
                return false;
            }

            var root = Mathf.Sqrt(discriminant);
            var tanTheta = (v2 + (highArc ? root : -root)) / (gravity * d);
            var theta = Mathf.Atan(tanTheta);

            var horizontalDir = flat / d;
            velocity = horizontalDir * (speed * Mathf.Cos(theta)) + Vector3.up * (speed * Mathf.Sin(theta));
            return true;
        }

        /// <summary>
        /// Slowest muzzle speed that can still reach <paramref name="target"/> (the speed of the
        /// solution that bisects the line to the target). Below this no angle arrives.
        /// </summary>
        public static float MinimumSpeedToReach(Vector3 origin, Vector3 target, float gravity)
        {
            if (gravity <= 0f)
            {
                return 0f;
            }

            var delta = target - origin;
            var d = new Vector3(delta.x, 0f, delta.z).magnitude;
            var h = delta.y;
            return Mathf.Sqrt(gravity * (h + Mathf.Sqrt(d * d + h * h)));
        }

        /// <summary>
        /// Same as <see cref="TrySolveWithSpeed"/>, except a speed that cannot reach the target is
        /// raised to the slowest one that can, rather than failing. <paramref name="usedSpeed"/>
        /// reports the speed actually flown so the caller can flag an out-of-range request.
        /// Returns false only when the geometry itself is degenerate.
        /// </summary>
        public static bool TrySolveWithSpeedClamped(
            Vector3 origin,
            Vector3 target,
            float speed,
            float gravity,
            bool highArc,
            out Vector3 velocity,
            out float usedSpeed)
        {
            usedSpeed = speed;
            var minimum = MinimumSpeedToReach(origin, target, gravity);
            if (speed < minimum)
            {
                // A hair above the minimum: exactly at it the discriminant is zero, and rounding
                // can still take it negative.
                usedSpeed = minimum * 1.001f;
            }

            return TrySolveWithSpeed(origin, target, usedSpeed, gravity, highArc, out velocity);
        }

        /// <summary>
        /// Launch velocity that hits <paramref name="target"/> from a fixed elevation angle,
        /// deriving the speed. Used for lobs, where the shape of the arc matters more than the speed.
        /// Returns false when the angle cannot reach the target (too flat for the height gain).
        /// </summary>
        public static bool TrySolveWithElevation(
            Vector3 origin,
            Vector3 target,
            float elevationDegrees,
            float gravity,
            out Vector3 velocity)
        {
            velocity = Vector3.zero;
            if (gravity <= 0f || elevationDegrees <= 0f || elevationDegrees >= 90f)
            {
                return false;
            }

            var delta = target - origin;
            var flat = new Vector3(delta.x, 0f, delta.z);
            var d = flat.magnitude;
            var h = delta.y;
            if (d < 1e-4f)
            {
                return false;
            }

            var theta = elevationDegrees * Mathf.Deg2Rad;
            var cos = Mathf.Cos(theta);
            var denominator = 2f * cos * cos * (d * Mathf.Tan(theta) - h);
            if (denominator <= 1e-6f)
            {
                return false;
            }

            var speed = Mathf.Sqrt(gravity * d * d / denominator);
            if (float.IsNaN(speed) || float.IsInfinity(speed))
            {
                return false;
            }

            var horizontalDir = flat / d;
            velocity = horizontalDir * (speed * cos) + Vector3.up * (speed * Mathf.Sin(theta));
            return true;
        }

        /// <summary>Position of a drag-free projectile after <paramref name="time"/> seconds.</summary>
        public static Vector3 Predict(Vector3 origin, Vector3 velocity, float time, float gravity)
        {
            return origin + velocity * time + Vector3.down * (0.5f * gravity * time * time);
        }

        /// <summary>Seconds until the projectile covers the horizontal distance to the target.</summary>
        public static float TimeOfFlight(Vector3 origin, Vector3 target, Vector3 velocity)
        {
            var flat = new Vector3(target.x - origin.x, 0f, target.z - origin.z);
            var horizontalSpeed = new Vector2(velocity.x, velocity.z).magnitude;
            return horizontalSpeed < 1e-4f ? 0f : flat.magnitude / horizontalSpeed;
        }
    }
}
