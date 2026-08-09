namespace Hapbeat.PistolWhip
{
    /// <summary>
    /// Logical event names this mod fires. The mod never names a kit event id or a
    /// gain — those live in <c>hapbeat_settings.json</c> so they can be retuned without
    /// rebuilding (see <see cref="PistolWhipSettings"/>).
    /// </summary>
    internal static class LogicalEvents
    {
        /// <summary>Gun fired (with ammo).</summary>
        public const string Shot = "shot";

        /// <summary>Melee/punch hit landed.</summary>
        public const string Melee = "melee";

        /// <summary>Reload completed.</summary>
        public const string Reload = "reload";

        /// <summary>Player took a hit.</summary>
        public const string PlayerHit = "player_hit";

        /// <summary>Armor lost — starts the looping low-health heartbeat.</summary>
        public const string LowHealthStart = "low_health_start";

        /// <summary>
        /// Stops the low-health heartbeat. Bound to the same kit event id as
        /// <see cref="LowHealthStart"/>, because STOP addresses the clip that is playing.
        /// If a user rebinds one and not the other, the fallback in
        /// <see cref="PistolWhipHapbeatMod.StopLowHealth"/> stops the start binding instead.
        /// </summary>
        public const string LowHealthStop = "low_health_stop";

        /// <summary>Player died.</summary>
        public const string Death = "death";

        /// <summary>One music beat (Koreographer OneOff event).</summary>
        public const string Beat = "beat";
    }
}
