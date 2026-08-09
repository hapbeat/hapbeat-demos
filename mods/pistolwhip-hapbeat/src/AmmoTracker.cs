using System;
using System.Collections.Concurrent;

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
