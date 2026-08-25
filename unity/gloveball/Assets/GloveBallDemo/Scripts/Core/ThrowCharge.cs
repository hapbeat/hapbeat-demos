namespace GloveBallDemo.Core
{
    /// <summary>Deterministic trigger charge used for every player throw.</summary>
    public sealed class ThrowCharge
    {
        public const float FullChargeSeconds = 1.5f;
        public const float MinimumSpeed = 5f;
        public const float MaximumSpeed = 20f;

        public float Value { get; private set; }

        public float Speed => MinimumSpeed + ((MaximumSpeed - MinimumSpeed) * Value);

        public void Reset() => Value = 0f;

        public void Tick(float deltaTime)
        {
            if (deltaTime > 0f)
            {
                Value = System.Math.Min(1f, Value + (deltaTime / FullChargeSeconds));
            }
        }
    }
}
