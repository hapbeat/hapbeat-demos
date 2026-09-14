using System;
using UnityEngine;

namespace GloveBallDemo.Runtime
{
    [Serializable]
    public sealed class BallFeel
    {
        public BallKind Kind;
        [Min(.01f)] public float Mass = .25f;
        [Range(0f, 2f)] public float AirResistance;
        public PhysicsMaterial BounceMaterial;
        public AudioClip ImpactClip;
        [Range(0f, 1f)] public float ImpactVolume = .7f;
    }

    [CreateAssetMenu(menuName = "GloveBall/Ball Feel Settings")]
    public sealed class BallFeelSettings : ScriptableObject
    {
        [Range(.5f, 1.5f)] public float FirstRoundSpeedMultiplier = .85f;
        [Range(.5f, 1.5f)] public float FinalRoundSpeedMultiplier = .95f;
        [Tooltip("Gameplay tuning, not a real-world mass simulation. One entry per ball kind.")]
        public BallFeel[] Balls = Array.Empty<BallFeel>();
        public BallFeel Find(BallKind kind)
        {
            foreach (var item in Balls) if (item != null && item.Kind == kind) return item;
            return null;
        }
    }
}
