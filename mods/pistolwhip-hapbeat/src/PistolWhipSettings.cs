using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Hapbeat.ModCore;

namespace Hapbeat.PistolWhip
{
    /// <summary>
    /// The shared settings schema plus the fields only this mod has (beat sync).
    /// <para>
    /// The shared core is read-only for this mod, so instead of subclassing
    /// <see cref="HapbeatModSettings"/> this wraps it: the base object is parsed by the
    /// core's own parser, and the extra keys are read from the same document with the
    /// core's <c>MiniJson</c> (internal, but the core is compiled into this assembly as
    /// source, so it is reachable). One file, one parser, no second dependency.
    /// </para>
    /// </summary>
    internal class PistolWhipSettings
    {
        /// <summary>Shared fields: appName / group / player / masterGain / minIntervalMs / events.</summary>
        public HapbeatModSettings Base;

        /// <summary>Whether to hook Koreographer and fire <c>beat</c> on music beats.</summary>
        public bool BeatEnabled = true;

        /// <summary>
        /// Koreography event ids to fire on. Empty (the default) means "every id found in
        /// the loaded song" — event ids are chosen per song by the audio designer, so
        /// there is no universal id to hard-code. Fill this in after reading the ids the
        /// mod logs on song load, if the automatic set turns out to be too noisy.
        /// </summary>
        public readonly List<string> BeatEventIds = new List<string>();

        /// <summary>Log every Koreography event id discovered when a song loads. This is
        /// how a user finds values for <see cref="BeatEventIds"/>.</summary>
        public bool LogDiscoveredBeatIds = true;

        /// <summary>
        /// How often (ms) to read the ammo count from the HUD, which is the mod's only
        /// per-frame hook. Lower is more accurate about dry trigger pulls; higher costs
        /// the frame less. <c>0</c> stops reading it altogether — nothing else breaks,
        /// but firing on an empty gun then produces a recoil haptic.
        /// See <see cref="AmmoTracker.ShouldSample"/> for why this is sampled at all.
        /// </summary>
        public int AmmoPollMs = 50;

        private const string AppName = "PistolWhip";

        /// <summary>Defaults for Pistol Whip, bound to the vr-shooter-kit clips.</summary>
        public static PistolWhipSettings CreateDefault()
        {
            var s = new PistolWhipSettings();
            s.Base = HapbeatModSettings.CreateDefault(AppName);

            // Replace the core's generic map with this game's logical events. The core's
            // defaults exist for mods that use its vocabulary as-is; Pistol Whip has its
            // own (melee vs slash, low-health start/stop, beat).
            s.Base.Events.Clear();
            Bind(s, LogicalEvents.Shot,           "vr-shooter-kit.shot_recoil",  1.0f);
            Bind(s, LogicalEvents.Melee,          "vr-shooter-kit.hit_light",    0.9f);
            Bind(s, LogicalEvents.Reload,         "vr-shooter-kit.reload_click", 0.8f);
            Bind(s, LogicalEvents.PlayerHit,      "vr-shooter-kit.hit_heavy",    1.0f);
            Bind(s, LogicalEvents.LowHealthStart, "vr-shooter-kit.heartbeat",    0.9f);
            Bind(s, LogicalEvents.LowHealthStop,  "vr-shooter-kit.heartbeat",    0.9f);
            Bind(s, LogicalEvents.Death,          "vr-shooter-kit.hit_heavy",    1.0f);
            Bind(s, LogicalEvents.Beat,           "vr-shooter-kit.beat_pulse",   0.5f);
            return s;
        }

        private static void Bind(PistolWhipSettings s, string logical, string eventId, float gain)
        {
            s.Base.Events[logical] = new HapbeatEventSetting(eventId, gain, true);
        }

        /// <summary>
        /// Load from <paramref name="path"/>, writing a default file when none exists.
        /// A malformed file leaves the user's file untouched and falls back to defaults
        /// with one warning, so a typo costs a warning rather than the tuning.
        /// </summary>
        public static PistolWhipSettings LoadOrCreate(string path, Action<string> log = null)
        {
            var defaults = CreateDefault();
            try
            {
                if (!File.Exists(path))
                {
                    defaults.Save(path);
                    if (log != null) log("Wrote default settings to " + path);
                    return defaults;
                }

                string text = File.ReadAllText(path, Encoding.UTF8);
                defaults.Base = HapbeatModSettings.Parse(text, defaults.Base);
                defaults.ParseExtras(text);
                if (log != null) log("Loaded settings from " + path);
                return defaults;
            }
            catch (Exception ex)
            {
                if (log != null)
                    log("Failed to load settings from " + path + " (" + ex.Message +
                        "); using defaults. The file was left as-is.");
                return CreateDefault();
            }
        }

        private void ParseExtras(string json)
        {
            JsonValue root = MiniJson.Parse(json);
            if (root == null || root.Type != JsonType.Object)
                return;

            BeatEnabled = root.GetBool("beatEnabled", BeatEnabled);
            LogDiscoveredBeatIds = root.GetBool("logDiscoveredBeatIds", LogDiscoveredBeatIds);
            AmmoPollMs = root.GetInt("ammoPollMs", AmmoPollMs);

            JsonValue ids = root.GetMember("beatEventIds");
            if (ids != null && ids.Type == JsonType.Array)
            {
                BeatEventIds.Clear();
                foreach (JsonValue item in ids.Items)
                {
                    if (item != null && item.Type == JsonType.String && !string.IsNullOrEmpty(item.String))
                        BeatEventIds.Add(item.String);
                }
            }
        }

        public void Save(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, ToJson(), new UTF8Encoding(false));
        }

        /// <summary>
        /// The core's document with this mod's extra keys spliced in before the closing
        /// brace. Splicing rather than re-emitting the whole schema keeps this from going
        /// stale if the shared schema gains a field.
        /// </summary>
        public string ToJson()
        {
            string baseJson = Base.ToJson();

            var extras = new StringBuilder();
            extras.Append("  \"beatEnabled\": ").Append(BeatEnabled ? "true" : "false").Append(",\n");
            extras.Append("  \"beatEventIds\": [");
            for (int i = 0; i < BeatEventIds.Count; i++)
            {
                if (i > 0) extras.Append(", ");
                extras.Append(MiniJson.Quote(BeatEventIds[i]));
            }
            extras.Append("],\n");
            extras.Append("  \"logDiscoveredBeatIds\": ").Append(LogDiscoveredBeatIds ? "true" : "false").Append(",\n");
            extras.Append("  \"ammoPollMs\": ").Append(AmmoPollMs.ToString(CultureInfo.InvariantCulture));

            int close = baseJson.LastIndexOf('}');
            if (close < 0)
                return baseJson; // unexpected shape — better to emit the core's document verbatim

            string head = baseJson.Substring(0, close).TrimEnd();
            return head + ",\n" + extras + "\n}\n";
        }
    }
}
