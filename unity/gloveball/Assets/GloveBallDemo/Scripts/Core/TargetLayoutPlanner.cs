using System;
using System.Collections.Generic;
using UnityEngine;

namespace GloveBallDemo.Core
{
    public readonly struct TargetLayoutSlot
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public TargetLayoutSlot(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    /// <summary>Seeded three-dimensional target placement constrained to the playable court volume.</summary>
    public sealed class TargetLayoutPlanner
    {
        public const int MaximumTargetCount = 4;
        private const int RandomAttemptBudget = 512;
        private readonly System.Random _random;

        public TargetLayoutPlanner(int seed) => _random = new System.Random(seed);

        public TargetLayoutSlot[] Plan(Vector3 playerPosition, Bounds placementBounds, float minimumDistance, int targetCount = MaximumTargetCount)
        {
            if (targetCount < 1 || targetCount > MaximumTargetCount)
                throw new ArgumentOutOfRangeException(nameof(targetCount));

            minimumDistance = Mathf.Max(0f, minimumDistance);
            var positions = new List<Vector3>(targetCount);
            for (var attempt = 0; attempt < RandomAttemptBudget && positions.Count < targetCount; attempt++)
            {
                var candidate = RandomPoint(placementBounds);
                if (MinimumDistanceTo(candidate, positions) >= minimumDistance) positions.Add(candidate);
            }

            // Rejection sampling can miss a valid arrangement in a tight volume. A fixed grid
            // searches all three axes deterministically before an impossible setup is relaxed.
            if (positions.Count < targetCount)
            {
                var candidates = BuildGrid(placementBounds);
                while (positions.Count < targetCount)
                {
                    var bestIndex = FindBest(candidates, positions, minimumDistance);
                    if (bestIndex < 0) bestIndex = FindBest(candidates, positions, 0f);
                    if (bestIndex < 0) bestIndex = 0;
                    positions.Add(candidates[bestIndex]);
                    candidates.RemoveAt(bestIndex);
                }
            }

            var slots = new TargetLayoutSlot[targetCount];
            for (var i = 0; i < slots.Length; i++)
            {
                var toPlayer = playerPosition - positions[i];
                if (toPlayer.sqrMagnitude < 1e-6f) toPlayer = Vector3.back;
                slots[i] = new TargetLayoutSlot(positions[i], Quaternion.LookRotation(toPlayer.normalized, Vector3.up));
            }
            return slots;
        }

        private Vector3 RandomPoint(Bounds bounds) => new Vector3(
            Lerp(bounds.min.x, bounds.max.x, Next01()),
            Lerp(bounds.min.y, bounds.max.y, Next01()),
            Lerp(bounds.min.z, bounds.max.z, Next01()));

        private static List<Vector3> BuildGrid(Bounds bounds)
        {
            var result = new List<Vector3>(125);
            for (var x = 0; x < 5; x++)
                for (var y = 0; y < 5; y++)
                    for (var z = 0; z < 5; z++)
                        result.Add(new Vector3(
                            Lerp(bounds.min.x, bounds.max.x, x / 4f),
                            Lerp(bounds.min.y, bounds.max.y, y / 4f),
                            Lerp(bounds.min.z, bounds.max.z, z / 4f)));
            return result;
        }

        private static int FindBest(List<Vector3> candidates, List<Vector3> positions, float threshold)
        {
            var bestIndex = -1;
            var bestDistance = -1f;
            for (var i = 0; i < candidates.Count; i++)
            {
                var distance = MinimumDistanceTo(candidates[i], positions);
                if (distance >= threshold && distance > bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        private static float MinimumDistanceTo(Vector3 candidate, List<Vector3> positions)
        {
            if (positions.Count == 0) return float.PositiveInfinity;
            var minimum = float.PositiveInfinity;
            foreach (var position in positions) minimum = Mathf.Min(minimum, Vector3.Distance(candidate, position));
            return minimum;
        }

        private float Next01() => (float)_random.NextDouble();
        private static float Lerp(float a, float b, float t) => a + ((b - a) * t);
    }
}
