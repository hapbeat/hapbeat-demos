using System;
using UnityEngine;

namespace Hapbeat.Boxing
{
    public enum BoxingPhase { Ready, Countdown, Fighting, Results }
    public enum BoxingInputMode { Controllers, Hands, Desktop }
    public enum ImpactMode { Continuous, WeakHard }
    public enum ImpactZone { LeftGlove, RightGlove, Head }

    [CreateAssetMenu(menuName = "Hapbeat Boxing/Tuning")]
    public sealed class BoxingTuning : ScriptableObject
    {
        [Header("Round")]
        [Min(10)] public float roundSeconds = 90;
        [Min(1)] public float maximumHealth = 100;
        [Min(0.1f)] public float enemyInterval = 1.6f;
        [Min(0.1f)] public float telegraphSeconds = 0.42f;
        [Min(0.08f)] public float strikeSeconds = 0.22f;
        [Min(0.1f)] public float recoverSeconds = 0.55f;
        [Min(0.5f)] public float playRadius = 1.0f;
        [Header("Collision (metres / seconds)")]
        [Min(0.01f)] public float gloveRadius = 0.115f;
        [Min(0.01f)] public float headRadius = 0.14f;
        [Min(0.01f)] public float enemyHeadRadius = 0.17f;
        [Min(0.01f)] public float enemyBodyRadius = 0.25f;
        [Min(0)] public float minimumImpactSpeed = 0.25f;
        [Min(0.01f)] public float hitCooldown = 0.22f;
        [Header("Impact response")]
        public ImpactMode impactMode = ImpactMode.WeakHard;
        [Min(0.1f)] public float hardHitSpeed = 2.5f;
        [Min(0.1f)] public float fullGainSpeed = 6f;
        [Range(0, 1)] public float minimumGain = 0.12f;
        [Range(0, 2)] public float maximumGain = 1f;
        [Range(0.1f, 3)] public float gainExponent = 0.85f;

        public float Gain(float relativeSpeed)
        {
            if (!float.IsFinite(relativeSpeed) || relativeSpeed < minimumImpactSpeed) return 0;
            float t = Mathf.InverseLerp(minimumImpactSpeed, Mathf.Max(minimumImpactSpeed + 0.01f, fullGainSpeed), relativeSpeed);
            return Mathf.Lerp(minimumGain, maximumGain, Mathf.Pow(t, gainExponent));
        }
        public bool IsHard(float speed) => impactMode == ImpactMode.WeakHard && speed >= hardHitSpeed;
    }

    public struct BoxerPose
    {
        public Vector3 head, left, right;
        public Quaternion headRotation, leftRotation, rightRotation;
        public bool valid, leftClosed, rightClosed;
        public double timestamp;
    }

    public readonly struct BoxingImpact
    {
        public readonly ImpactZone zone;
        public readonly float relativeSpeed, gain;
        public readonly bool hard, attack;
        public readonly Vector3 point;
        public BoxingImpact(ImpactZone zone, float speed, bool attack, Vector3 point, BoxingTuning tuning)
        {
            this.zone = zone; relativeSpeed = speed; gain = tuning.Gain(speed);
            hard = tuning.IsHard(speed); this.attack = attack; this.point = point;
        }
    }

    public static class BoxingCollision
    {
        // Sweep in relative space: both participants can move during the sample.
        public static bool Sweep(Vector3 a0, Vector3 a1, float ar, Vector3 b0, Vector3 b1, float br, out float t)
        {
            Vector3 p = a0 - b0, d = (a1 - a0) - (b1 - b0);
            float r = ar + br, c = p.sqrMagnitude - r * r;
            if (c <= 0) { t = 0; return true; }
            float a = d.sqrMagnitude, b = Vector3.Dot(p, d), discriminant = b * b - a * c;
            if (a < 1e-10f || discriminant < 0) { t = 0; return false; }
            t = (-b - Mathf.Sqrt(discriminant)) / a;
            return t >= 0 && t <= 1;
        }
        public static float RelativeSpeed(Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, float dt) =>
            dt > 0 ? ((a1 - a0) - (b1 - b0)).magnitude / dt : 0;
    }

    public sealed class BoxingRound
    {
        public BoxingPhase Phase { get; private set; } = BoxingPhase.Ready;
        public float TimeLeft { get; private set; }
        public float Countdown { get; private set; }
        public int Hits { get; private set; }
        public int Blocks { get; private set; }
        public int Taken { get; private set; }
        public int Dodges { get; private set; }
        public int Score { get; private set; }
        public float MaximumHealth { get; private set; } = 100;
        public float PlayerHealth { get; private set; } = 100;
        public float EnemyHealth { get; private set; } = 100;
        public void Start(float duration, float maximumHealth = 100)
        {
            MaximumHealth = Mathf.Max(1, maximumHealth);
            PlayerHealth = EnemyHealth = MaximumHealth;
            TimeLeft = duration; Countdown = 3; Hits = Blocks = Taken = Dodges = Score = 0; Phase = BoxingPhase.Countdown;
        }
        public void Tick(float dt, bool paused)
        {
            if (paused || dt <= 0) return;
            if (Phase == BoxingPhase.Countdown)
            {
                Countdown = Mathf.Max(0, Countdown - dt);
                if (Countdown <= 0) Phase = BoxingPhase.Fighting;
            }
            else if (Phase == BoxingPhase.Fighting)
            {
                TimeLeft = Mathf.Max(0, TimeLeft - dt);
                if (TimeLeft <= 0) Phase = BoxingPhase.Results;
            }
        }
        public void Report(BoxingImpact impact)
        {
            if (Phase != BoxingPhase.Fighting) return;
            float damage = impact.hard ? 20 : 10;
            if (impact.attack) { Hits++; Score += Mathf.RoundToInt(20 + 80 * Mathf.Clamp01(impact.gain)); EnemyHealth = Mathf.Max(0, EnemyHealth - damage); }
            else if (impact.zone == ImpactZone.Head) { Taken++; PlayerHealth = Mathf.Max(0, PlayerHealth - damage); }
            else { Blocks++; Score += 15; }
            if (PlayerHealth <= 0 || EnemyHealth <= 0) Phase = BoxingPhase.Results;
        }
        public void Dodge() { if (Phase == BoxingPhase.Fighting) { Dodges++; Score += 10; } }
    }
}
