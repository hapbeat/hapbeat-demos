using System;
using System.IO;
using System.Reflection;
using Hapbeat.ModCore;
using MelonLoader;

[assembly: MelonInfo(typeof(Hapbeat.PistolWhip.PistolWhipHapbeatMod), "PistolWhipHapbeat", "0.1.0", "Hapbeat")]
[assembly: MelonGame("Cloudhead Games, Ltd.", "Pistol Whip")]

namespace Hapbeat.PistolWhip
{
    /// <summary>
    /// MelonLoader entry point. Owns the settings, the UDP client and the lifetime of the
    /// hooks; the hooks themselves live in <see cref="GameHooks"/> and <see cref="BeatSync"/>.
    /// </summary>
    public class PistolWhipHapbeatMod : MelonMod
    {
        private static HapbeatModClient _client;
        private static PistolWhipSettings _settings;
        private static bool _lowHealthActive;
        private static bool _shutdown;
        private static readonly object _stateLock = new object();

        internal static PistolWhipSettings Settings
        {
            get { return _settings ?? (_settings = PistolWhipSettings.CreateDefault()); }
        }

        public override void OnInitializeMelon()
        {
            try
            {
                string path = ResolveSettingsPath();
                _settings = PistolWhipSettings.LoadOrCreate(path, LogInfo);

                _client = new HapbeatModClient(_settings.Base);
                _client.Log = LogInfo;
                _client.OpenBroadcast();
                LogInfo("UDP client open (appName='" + _settings.Base.AppName + "'). " +
                        "Edit " + path + " to retune events, then restart the game.");

                HookInstaller.InstallAll(HarmonyInstance);
                BeatSync.Install(HarmonyInstance);
            }
            catch (Exception ex)
            {
                LogError("Initialization failed: " + ex);
            }
        }

        public override void OnApplicationQuit()
        {
            Shutdown();
        }

        public override void OnDeinitializeMelon()
        {
            Shutdown();
        }

        private static void Shutdown()
        {
            lock (_stateLock)
            {
                if (_shutdown)
                    return;
                _shutdown = true;
            }

            try { BeatSync.Shutdown(); } catch (Exception) { /* teardown race */ }

            HapbeatModClient client = _client;
            _client = null;
            if (client != null)
            {
                // Leave nothing looping on the device: the low-health heartbeat would
                // otherwise keep playing after the game is gone.
                try { client.SendStopAll(); } catch (Exception) { }
                try { client.Close(); } catch (Exception) { }
            }

            _lowHealthActive = false;
            GameHooks.Reset();
            LogInfo("Shut down.");
        }

        #region Event entry points used by the hooks

        /// <summary>Fire a logical event if the client is up. Safe from any thread.</summary>
        internal static void Fire(string logicalEvent)
        {
            HapbeatModClient client = _client;
            if (client != null)
                client.Fire(logicalEvent);
        }

        /// <summary>Start the looping low-health heartbeat, once per low-health episode.</summary>
        internal static void StartLowHealth()
        {
            lock (_stateLock)
            {
                if (_lowHealthActive)
                    return;
                _lowHealthActive = true;
            }
            Fire(LogicalEvents.LowHealthStart);
        }

        /// <summary>Stop the low-health heartbeat if it is running.</summary>
        internal static void StopLowHealth()
        {
            lock (_stateLock)
            {
                if (!_lowHealthActive)
                    return;
                _lowHealthActive = false;
            }

            HapbeatModClient client = _client;
            if (client == null)
                return;

            // The stop binding normally points at the same kit event id as the start
            // binding. If a user rebound one and not the other, stop what we started.
            if (!client.FireStop(LogicalEvents.LowHealthStop))
                client.FireStop(LogicalEvents.LowHealthStart);
        }

        /// <summary>Player died: end the heartbeat loop, then the death hit.</summary>
        internal static void OnDeath()
        {
            StopLowHealth();
            Fire(LogicalEvents.Death);
        }

        #endregion

        #region Logging

        internal static void LogInfo(string message)
        {
            MelonLogger.Msg("[Hapbeat] " + message);
        }

        internal static void LogWarning(string message)
        {
            MelonLogger.Warning("[Hapbeat] " + message);
        }

        internal static void LogError(string message)
        {
            MelonLogger.Error("[Hapbeat] " + message);
        }

        /// <summary>
        /// A hook body threw. Logged once per hook so a changed game field does not spam
        /// a per-frame hook into an unreadable log.
        /// </summary>
        internal static void HookFailed(string hookName, Exception ex)
        {
            lock (_reportedHooks)
            {
                if (_reportedHooks.Contains(hookName))
                    return;
                _reportedHooks.Add(hookName);
            }
            LogWarning("Hook '" + hookName + "' raised " + ex.GetType().Name + ": " + ex.Message +
                       " (reported once).");
        }

        private static readonly System.Collections.Generic.HashSet<string> _reportedHooks =
            new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        #endregion

        /// <summary>
        /// Where <c>hapbeat_settings.json</c> lives: the game's <c>UserData</c> folder when
        /// it can be found (MelonLoader's convention for mod configuration, and it survives
        /// replacing the mod DLL), otherwise next to the DLL itself.
        /// <para>
        /// Derived from the assembly location rather than a MelonLoader path API so the
        /// mod does not break on a loader version that moved or renamed that API.
        /// </para>
        /// </summary>
        private static string ResolveSettingsPath()
        {
            const string FileName = "hapbeat_settings.json";

            string modDir = null;
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(location))
                    modDir = Path.GetDirectoryName(location);
            }
            catch (Exception) { /* fall through to the game directory */ }

            string gameDir = null;
            if (!string.IsNullOrEmpty(modDir))
                gameDir = Path.GetDirectoryName(modDir); // Mods\ -> game root
            if (string.IsNullOrEmpty(gameDir))
                gameDir = AppDomain.CurrentDomain.BaseDirectory;

            if (!string.IsNullOrEmpty(gameDir))
            {
                string userData = Path.Combine(gameDir, "UserData");
                if (Directory.Exists(userData))
                    return Path.Combine(userData, FileName);
            }

            if (!string.IsNullOrEmpty(modDir))
                return Path.Combine(modDir, FileName);

            return Path.GetFullPath(FileName);
        }
    }
}
