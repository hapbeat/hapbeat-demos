using System;

namespace GloveBallDemo.Runtime
{
    /// <summary>Blocks gameplay controls while the in-headset menu owns the controller buttons.</summary>
    public static class GameInputGate
    {
        public static bool IsBlocked { get; private set; }

        public static event Action<bool> BlockedChanged;

        public static void SetBlocked(bool blocked)
        {
            if (IsBlocked == blocked)
            {
                return;
            }

            IsBlocked = blocked;
            BlockedChanged?.Invoke(blocked);
        }
    }
}
