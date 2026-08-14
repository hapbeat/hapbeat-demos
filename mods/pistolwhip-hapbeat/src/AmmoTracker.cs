using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Hapbeat.PistolWhip
{
    /// <summary>
    /// Remembers how many rounds each gun has left, keyed by the name of the hand that
    /// holds it.
    /// <para>
    /// <c>Gun.Fire</c> also runs on a dry trigger pull, which would fire a recoil haptic
    /// for a shot that never happened. The ammo count is only observable from the HUD
    /// component that refreshes it every frame, so that is where it is read from — the
    /// same seam the existing Pistol Whip haptics mods use.
    /// </para>
    /// Keyed by hand name rather than by object reference because il2cpp proxy objects
    /// are not reference-stable across calls.
    /// </summary>
    internal static class AmmoTracker
    {
        private static readonly ConcurrentDictionary<string, int> _bulletsByHand =
            new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        private static readonly Stopwatch _clock = Stopwatch.StartNew();
        private static long _lastSampleMs = long.MinValue;

        /// <summary>
        /// Whether the per-frame HUD hook should actually read the ammo count this time.
        /// <para>
        /// <c>GunAmmoDisplay.Update</c> is the only hook on a per-frame path, and reading
        /// through it is not cheap: each pass crosses into il2cpp for a member lookup and
        /// marshals a Unity <c>name</c> string back. At 90–120 Hz that allocates steadily
        /// and shows up as hitching. Ammo cannot change faster than the trigger, so
        /// sampling on a short interval keeps the dry-fire guard while taking the cost
        /// off the frame. <c>ammoPollMs</c> = 0 turns the sampling off entirely (dry
        /// trigger pulls then produce a recoil).
        /// </para>
        /// </summary>
        public static bool ShouldSample()
        {
            int intervalMs = PistolWhipHapbeatMod.Settings.AmmoPollMs;
            if (intervalMs <= 0)
                return false;

            long nowMs = _clock.ElapsedMilliseconds;
            // Not locked: a duplicated sample on a race costs one extra read, and a
            // missed one is picked up on the next frame. Neither is worth contention on
            // a hot path.
            if (nowMs - _lastSampleMs < intervalMs)
                return false;

            _lastSampleMs = nowMs;
            return true;
        }

        /// <summary>Record the current round count for one gun.</summary>
        public static void Report(string handName, int bulletCount)
        {
            _bulletsByHand[handName ?? string.Empty] = bulletCount;
        }

        /// <summary>
        /// Whether the gun in <paramref name="handName"/> has ammo. Fails open: a hand we
        /// have never seen a count for counts as loaded, so a changed HUD field silences
        /// the guard rather than every shot.
        /// </summary>
        public static bool HasAmmo(string handName)
        {
            int bullets;
            if (!_bulletsByHand.TryGetValue(handName ?? string.Empty, out bullets))
                return true;
            return bullets > 0;
        }

        /// <summary>Drop all counts (mod shutdown).</summary>
        public static void Clear()
        {
            _bulletsByHand.Clear();
        }
    }
}
