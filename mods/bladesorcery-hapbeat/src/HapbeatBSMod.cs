using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Hapbeat.ModCore;
using ThunderRoad;
using UnityEngine;

namespace Hapbeat.BladeSorcery
{
    /// <summary>
    /// Blade &amp; Sorcery (PCVR 1.0) scripted mod that mirrors combat events onto Hapbeat
    /// haptic devices over Wi-Fi UDP.
    /// <para>
    /// The mod itself knows nothing about the wire protocol: it turns game events into
    /// <em>logical event names</em> and hands them to <see cref="HapbeatModClient"/>, which
    /// maps them to kit event ids and gains through <c>hapbeat_settings.json</c>. Retuning
    /// the feel therefore needs no rebuild.
    /// </para>
    /// <para>
    /// Every ThunderRoad API call is behind <see cref="ThunderRoadApi"/>. See that file
    /// (and the README) for the members that still need confirming against the real DLL.
    /// </para>
    /// </summary>
    public class HapbeatBSMod : ThunderScript
    {
        #region In-game mod options

        // Blade & Sorcery renders these automatically in its in-game mod settings menu and
        // persists them in the player's save. Only the coarse switches live here — the
        // per-event id/gain table stays in hapbeat_settings.json, which is where a
        // demo operator tunes the feel without a VR keyboard.

#if HAPBEAT_BS_MODOPTIONS
        [ModOptionCategory("Hapbeat", 0)]
        [ModOptionOrder(0)]
        [ModOptionTooltip("Turn all Hapbeat haptic output on or off.")]
        [ModOption]
#endif
        public static bool OptionEnabled = true;

        // No min/max/step attribute is applied here on purpose. BasSDK's documented
        // attribute set is ModOption / ModOptionCategory / ModOptionOrder /
        // ModOptionTooltip / ModOptionUI / ModOptionArrows / ModOptionButton /
        // ModOptionSlider; the range-carrying one is ModOptionUI, whose parameter list is
        // not documented anywhere we could confirm. Guessing it would trade a tuning
        // convenience for a build failure against the real ThunderRoad.dll, so the slider
        // ships without an explicit range and the value is clamped below instead. See the
        // README ILSpy checklist (#7) to add the range once the real signature is known.
#if HAPBEAT_BS_MODOPTIONS
        [ModOptionCategory("Hapbeat", 0)]
        [ModOptionOrder(1)]
        [ModOptionTooltip("Overall haptic strength multiplier applied on top of each event's own gain.")]
        [ModOptionSlider]
        [ModOption]
#endif
        public static float OptionMasterGain = 1.0f;

#if HAPBEAT_BS_MODOPTIONS
        [ModOptionCategory("Hapbeat", 0)]
        [ModOptionOrder(2)]
        [ModOptionTooltip("Pulse when several enemies close in around you.")]
        [ModOption]
#endif
        public static bool OptionSurroundedEnabled = true;

        #endregion

        #region Tuning constants

        /// <summary>Name shown on the device OLED (clipped to 16 chars on the wire).</summary>
        private const string AppName = "BladeSorcery";

        private const string SettingsFileName = "hapbeat_settings.json";

        /// <summary>How often the surrounded scan walks <c>Creature.allActive</c>. The scan
        /// is O(creatures) and runs off the main thread's Update, so it is throttled rather
        /// than run every frame.</summary>
        private const float SurroundScanIntervalSeconds = 0.4f;

        /// <summary>Radius (metres) counted as "closing in".</summary>
        private const float SurroundRadiusMeters = 3.0f;

        /// <summary>How many live non-player creatures inside the radius trigger the cue.</summary>
        private const int SurroundThreshold = 3;

        /// <summary>Minimum gap between two surrounded cues. Long on purpose: this is an
        /// ambient situational cue, not a per-hit one, and a melee brawl keeps the
        /// condition true for many seconds at a time.</summary>
        private const float SurroundCooldownSeconds = 5.0f;

        /// <summary>Health fraction at or below which the heartbeat loop starts.</summary>
        private const float LowHealthEnterFraction = 0.20f;

        /// <summary>Health fraction that must be regained before the loop stops. Higher than
        /// the entry threshold so healing that hovers around 20% does not restart the loop
        /// on every tick.</summary>
        private const float LowHealthExitFraction = 0.30f;

        /// <summary>Upper bound applied to the in-game Master gain slider. The slider ships
        /// without a declared range (see OptionMasterGain), so a host that hands over an
        /// unexpected scale cannot push the output past double strength.</summary>
        private const float MasterGainMax = 2.0f;

        #endregion

        #region Logical event names (keys in hapbeat_settings.json)

        private const string EvEnemyHit = "enemy_hit";
        private const string EvEnemyKill = "enemy_kill";
        private const string EvParry = "parry";
        private const string EvDeflect = "deflect";
        private const string EvPlayerHit = "player_hit";
        private const string EvLowHealth = "low_health_start";
        private const string EvSurrounded = "surrounded";

        #endregion

        private HapbeatModSettings _settings;
        private HapbeatModClient _client;
        private ThunderRoadApi _api;

        // Logical events actually present in the settings file. Checked before firing so a
        // user who deleted a binding gets one startup warning instead of a log line per hit.
        private HashSet<string> _boundEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private bool _started;
        private bool _heartbeatActive;
        private float _nextSurroundScanTime;
        private float _lastSurroundFireTime = float.NegativeInfinity;
        private float _appliedMasterGain = float.NaN;

        #region ThunderScript lifecycle

        public override void ScriptLoaded(ModManager.ModData modData)
        {
            base.ScriptLoaded(modData);

            try
            {
                Startup();
            }
            catch (Exception ex)
            {
                // Never let this mod break the game's mod loading for the user's other mods.
                LogError("Startup failed: " + ex);
                Shutdown();
            }
        }

        public override void ScriptUpdate()
        {
            base.ScriptUpdate();

            if (!_started || _client == null)
                return;

            try
            {
                ApplyMasterGainOption();

                if (!_api.IsPlayerReady)
                    return;

                UpdateLowHealth();

                if (OptionSurroundedEnabled)
                    UpdateSurrounded();
            }
            catch (Exception ex)
            {
                LogError("Update failed: " + ex);
            }
        }

        public override void ScriptDisable()
        {
            base.ScriptDisable();
            Shutdown();
        }

        public override void ScriptUnload()
        {
            base.ScriptUnload();
            Shutdown();
        }

        #endregion

        #region Startup / shutdown

        private void Startup()
        {
            string path = Path.Combine(GetModDirectory(), SettingsFileName);

            _settings = LoadSettings(path);
            _boundEvents = new HashSet<string>(_settings.Events.Keys, StringComparer.OrdinalIgnoreCase);
            WarnAboutMissingBindings();

            _client = new HapbeatModClient(_settings);
            _client.Log = LogInfo;
            _client.OpenBroadcast();

            _api = new ThunderRoadApi();
            _api.Log = LogInfo;
            _api.PlayerSpawned += OnPlayerSpawned;
            _api.EnemyHit += () => Send(EvEnemyHit);
            _api.EnemyKill += () => Send(EvEnemyKill);
#if HAPBEAT_BS_PARRY
            _api.Parry += () => Send(EvParry);
#endif
#if HAPBEAT_BS_DEFLECT
            _api.Deflect += () => Send(EvDeflect);
#endif
            _api.PlayerHit += () => Send(EvPlayerHit);
            _api.PlayerKilled += OnPlayerKilled;
            _api.SubscribeSpawn();

            _started = true;
            LogInfo("Loaded. Settings: " + path);
        }

        private void Shutdown()
        {
            if (!_started && _client == null && _api == null)
                return;

            _started = false;

            try
            {
                if (_api != null)
                    _api.UnsubscribeAll();
            }
            catch (Exception ex)
            {
                LogError("Failed to unsubscribe: " + ex.Message);
            }
            _api = null;

            try
            {
                if (_client != null)
                {
                    // Leaving the heartbeat looping on the device after the mod goes away
                    // would need a power cycle to stop.
                    if (_heartbeatActive)
                        _client.FireStop(EvLowHealth);
                    _client.Close();
                }
            }
            catch (Exception ex)
            {
                LogError("Failed to close client: " + ex.Message);
            }

            _client = null;
            _heartbeatActive = false;
        }

        private void OnPlayerSpawned()
        {
            // A fresh player creature means the previous per-instance subscriptions are
            // dead and any heartbeat state belongs to the previous life.
            StopHeartbeat();
            _lastSurroundFireTime = float.NegativeInfinity;

            try
            {
                _api.SubscribeWorld();
            }
            catch (Exception ex)
            {
                LogError("Failed to subscribe to gameplay events: " + ex);
            }
        }

        private void OnPlayerKilled()
        {
            StopHeartbeat();
            Send(EvPlayerHit);
        }

        #endregion

        #region Per-frame logic

        private void UpdateLowHealth()
        {
            float fraction;
            if (!_api.TryGetPlayerHealthFraction(out fraction))
                return;

            if (!_heartbeatActive && fraction <= LowHealthEnterFraction)
            {
                if (Send(EvLowHealth))
                    _heartbeatActive = true;
            }
            else if (_heartbeatActive && fraction >= LowHealthExitFraction)
            {
                StopHeartbeat();
            }
        }

        private void StopHeartbeat()
        {
            if (!_heartbeatActive || _client == null)
            {
                _heartbeatActive = false;
                return;
            }

            _heartbeatActive = false;
            _client.FireStop(EvLowHealth); // stops are never rate limited
        }

        private void UpdateSurrounded()
        {
            float now = Time.time;
            if (now < _nextSurroundScanTime)
                return;
            _nextSurroundScanTime = now + SurroundScanIntervalSeconds;

            if (now - _lastSurroundFireTime < SurroundCooldownSeconds)
                return;

            int nearby = _api.CountNearbyCreatures(SurroundRadiusMeters);
            if (nearby < SurroundThreshold) // also covers the -1 "player unavailable" case
                return;

            if (Send(EvSurrounded))
                _lastSurroundFireTime = now;
        }

        private void ApplyMasterGainOption()
        {
            // The in-game slider writes the static field directly, so the value is polled
            // rather than pushed. Comparison is exact on purpose: the slider only ever
            // moves in discrete steps, and this avoids re-assigning every frame.
            if (OptionMasterGain == _appliedMasterGain)
                return;

            _appliedMasterGain = OptionMasterGain;

            float gain = OptionMasterGain;
            if (float.IsNaN(gain) || gain < 0f) gain = 0f;
            else if (gain > MasterGainMax) gain = MasterGainMax;

            _settings.MasterGain = gain;
        }

        #endregion

        #region Sending

        /// <summary>
        /// Fire a logical event if the mod is enabled and the event is bound in the
        /// settings file. Returns whether a command actually went out (false also covers
        /// the core's per-event rate limit).
        /// </summary>
        private bool Send(string logicalEvent)
        {
            if (!OptionEnabled || _client == null)
                return false;
            if (!_boundEvents.Contains(logicalEvent))
                return false;

            return _client.Fire(logicalEvent);
        }

        #endregion

        #region Settings

        /// <summary>
        /// Read the settings file, seeding it on first run and falling back to this mod's
        /// own defaults on any failure.
        /// <para>
        /// The core's <c>LoadOrCreate</c> is deliberately not used: its fallback is the
        /// shared shooter-mod event map (shot / hit / reload / ...), none of whose keys this
        /// mod ever fires. A single typo in the JSON would therefore silence all seven
        /// combat events for the session instead of restoring working defaults.
        /// </para>
        /// </summary>
        private HapbeatModSettings LoadSettings(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    HapbeatModSettings seeded = CreateDefaultSettings();
                    seeded.Save(path);
                    LogInfo("Wrote default settings to " + path);
                    return seeded;
                }

                string text = File.ReadAllText(path, Encoding.UTF8);

                // Fields the file omits keep the value they have in the defaults passed in,
                // so a partial file still lands on the Blade & Sorcery event map.
                return HapbeatModSettings.Parse(text, CreateDefaultSettings());
            }
            catch (Exception ex)
            {
                // The broken file is left on disk on purpose: the user can fix their typo
                // instead of losing their tuning.
                LogError("Failed to load settings from " + path + " (" + ex.Message +
                         "); using built-in defaults for this session.");
                return CreateDefaultSettings();
            }
        }

        /// <summary>
        /// The event map written on first run. Kit event ids come from
        /// <c>kits/vr-shooter-kit/manifest.json</c>.
        /// </summary>
        private static HapbeatModSettings CreateDefaultSettings()
        {
            var s = new HapbeatModSettings();
            s.AppName = AppName;
            s.MinIntervalMs = 60;

            s.Events[EvEnemyHit] = new HapbeatEventSetting("vr-shooter-kit.slash", 0.9f, true);
            s.Events[EvEnemyKill] = new HapbeatEventSetting("vr-shooter-kit.kill_confirm", 0.9f, true);
            s.Events[EvParry] = new HapbeatEventSetting("vr-shooter-kit.block_thud", 1.0f, true);
            s.Events[EvDeflect] = new HapbeatEventSetting("vr-shooter-kit.block_thud", 0.8f, true);
            s.Events[EvPlayerHit] = new HapbeatEventSetting("vr-shooter-kit.hit_heavy", 1.0f, true);
            s.Events[EvLowHealth] = new HapbeatEventSetting("vr-shooter-kit.heartbeat", 0.9f, true);
            s.Events[EvSurrounded] = new HapbeatEventSetting("vr-shooter-kit.beat_pulse", 0.6f, true);
            return s;
        }

        private void WarnAboutMissingBindings()
        {
            string[] required =
            {
                EvEnemyHit, EvEnemyKill, EvParry, EvDeflect, EvPlayerHit, EvLowHealth, EvSurrounded
            };

            var missing = new List<string>();
            foreach (string name in required)
            {
                if (!_boundEvents.Contains(name))
                    missing.Add(name);
            }

            if (missing.Count > 0)
                LogInfo("Not bound in settings (these events will stay silent): " +
                        string.Join(", ", missing.ToArray()));
        }

        /// <summary>
        /// Directory the mod DLL was loaded from — the same folder the game scanned, so the
        /// settings file sits next to the mod where the user expects it.
        /// </summary>
        private static string GetModDirectory()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(location))
                {
                    string dir = Path.GetDirectoryName(location);
                    if (!string.IsNullOrEmpty(dir))
                        return dir;
                }
            }
            catch (Exception)
            {
                // Some hosts return an empty Location for dynamically loaded assemblies.
            }

            // Fall back to the process working directory rather than throwing: a settings
            // file in an odd place is recoverable, a failed load is not.
            return Directory.GetCurrentDirectory();
        }

        #endregion

        #region Logging

        private static void LogInfo(string message)
        {
            Debug.Log("[HapbeatBS] " + message);
        }

        private static void LogError(string message)
        {
            Debug.LogError("[HapbeatBS] " + message);
        }

        #endregion
    }
}
