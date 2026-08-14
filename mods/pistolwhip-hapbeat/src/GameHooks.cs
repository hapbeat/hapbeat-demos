using System;

namespace Hapbeat.PistolWhip
{
    /// <summary>
    /// Harmony postfix bodies for the game's combat and health events. Each one turns a
    /// game event into a logical Hapbeat event and nothing else — no protocol, no event
    /// ids, no gains (those are settings-side).
    /// <para>
    /// Every body is wrapped so a reflection surprise inside an IL2CPP postfix cannot
    /// propagate into game code.
    /// </para>
    /// </summary>
    internal static class GameHooks
    {
        /// <summary>Last reload method reported by <c>Reloader.SetReloadMethod</c>
        /// ("Gesture" / "Trigger" / ...). Diagnostics only — the haptic is the same
        /// either way; it is logged so a user can tell which path they are on when a
        /// reload produces no feedback.</summary>
        private static string _reloadMethod;

        /// <summary><c>Gun.Fire</c> — recoil, suppressed on a dry trigger pull.</summary>
        public static void AfterGunFire(object __instance)
        {
            try
            {
                string hand = GameReflection.GetChildName(__instance, "hand");
                if (!AmmoTracker.HasAmmo(hand))
                    return;
                PistolWhipHapbeatMod.Fire(LogicalEvents.Shot);
            }
            catch (Exception ex) { PistolWhipHapbeatMod.HookFailed("Gun.Fire", ex); }
        }

        /// <summary><c>GunAmmoDisplay.Update</c> — per-frame ammo observation, no haptic.</summary>
        public static void AfterAmmoDisplayUpdate(object __instance)
        {
            // Runs every frame at VR refresh rates. Everything below this line costs
            // il2cpp interop and a marshalled string, so it is sampled rather than run
            // per frame — see AmmoTracker.ShouldSample.
            if (!AmmoTracker.ShouldSample())
                return;

            try
            {
                object gun = GameReflection.GetMember(__instance, "gun");
                if (gun == null)
                    return;
                string hand = GameReflection.GetChildName(gun, "hand");
                int bullets = GameReflection.GetInt(__instance, "currentBulletCount", -1);
                if (bullets >= 0)
                    AmmoTracker.Report(hand, bullets);
            }
            catch (Exception ex) { PistolWhipHapbeatMod.HookFailed("GunAmmoDisplay.Update", ex); }
        }

        /// <summary><c>MeleeWeapon.ProcessHit</c> — melee/punch connected.</summary>
        public static void AfterMeleeHit()
        {
            try { PistolWhipHapbeatMod.Fire(LogicalEvents.Melee); }
            catch (Exception ex) { PistolWhipHapbeatMod.HookFailed("MeleeWeapon.ProcessHit", ex); }
        }

        /// <summary>
        /// <c>Gun.Reload(bool triggeredByMelee)</c> — a melee-triggered reload is skipped
        /// because the melee hit already produced its own haptic in the same frame.
        /// </summary>
        public static void AfterGunReload(object __instance, object[] __args)
        {
            try
            {
                if (__args != null && __args.Length > 0 && __args[0] is bool && (bool)__args[0])
                    return; // reload came from a melee hit

                // Fail open when the field is absent: better a spurious reload buzz than
                // silence on every reload.
                if (!GameReflection.GetBool(__instance, "reloadTriggered", true))
                    return;

                PistolWhipHapbeatMod.Fire(LogicalEvents.Reload);
            }
            catch (Exception ex) { PistolWhipHapbeatMod.HookFailed("Gun.Reload", ex); }
        }

        /// <summary><c>Reloader.SetReloadMethod</c> — records which reload scheme is active.</summary>
        public static void AfterSetReloadMethod(object[] __args)
        {
            try
            {
                if (__args == null || __args.Length == 0 || __args[0] == null)
                    return;
                string method = __args[0].ToString();
                if (method == _reloadMethod)
                    return;
                _reloadMethod = method;
                PistolWhipHapbeatMod.LogInfo("Reload method is now '" + method + "'.");
            }
            catch (Exception ex) { PistolWhipHapbeatMod.HookFailed("Reloader.SetReloadMethod", ex); }
        }

        /// <summary><c>Projectile.ShowPlayerHitEffects</c> — the player was hit.</summary>
        public static void AfterPlayerHit()
        {
            try { PistolWhipHapbeatMod.Fire(LogicalEvents.PlayerHit); }
            catch (Exception ex) { PistolWhipHapbeatMod.HookFailed("Projectile.ShowPlayerHitEffects", ex); }
        }

        /// <summary><c>PlayerHUD.OnArmorLost</c> — armor gone, start the low-health loop.</summary>
        public static void AfterArmorLost(object __instance)
        {
            try
            {
                // Fires on every armor change; only the transition to "no armor left"
                // is the low-health state.
                if (GameReflection.GetBool(__instance, "hasArmor", false))
                    return;
                PistolWhipHapbeatMod.StartLowHealth();
            }
            catch (Exception ex) { PistolWhipHapbeatMod.HookFailed("PlayerHUD.OnArmorLost", ex); }
        }

        /// <summary><c>PlayerHUD.playArmorGainedEffect</c> — healed, stop the loop.</summary>
        public static void AfterArmorGained()
        {
            try { PistolWhipHapbeatMod.StopLowHealth(); }
            catch (Exception ex) { PistolWhipHapbeatMod.HookFailed("PlayerHUD.playArmorGainedEffect", ex); }
        }

        /// <summary><c>Player.ProcessKillerHit</c> — killed by an enemy.</summary>
        public static void AfterKillerHit()
        {
            try { PistolWhipHapbeatMod.OnDeath(); }
            catch (Exception ex) { PistolWhipHapbeatMod.HookFailed("Player.ProcessKillerHit", ex); }
        }

        /// <summary><c>PlayerHUD.OnPlayerDeath</c> — death screen. Usually the same death
        /// as <see cref="AfterKillerHit"/>; the per-event rate limit collapses the pair.</summary>
        public static void AfterPlayerDeath()
        {
            try { PistolWhipHapbeatMod.OnDeath(); }
            catch (Exception ex) { PistolWhipHapbeatMod.HookFailed("PlayerHUD.OnPlayerDeath", ex); }
        }

        /// <summary>Reset per-session state (mod shutdown).</summary>
        public static void Reset()
        {
            _reloadMethod = null;
            AmmoTracker.Clear();
        }
    }
}
