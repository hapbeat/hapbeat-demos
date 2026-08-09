using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Hapbeat.ModCore
{
    /// <summary>
    /// One logical event's binding: which kit event id it plays, at what relative gain,
    /// and whether it is on at all. Tuning these needs no rebuild of the mod.
    /// </summary>
    public class HapbeatEventSetting
    {
        /// <summary>Kit event id, e.g. "vr-shooter-kit.shot_recoil" (DEC-040:
        /// <c>&lt;kit-name&gt;.&lt;file-name&gt;</c>).</summary>
        public string EventId;

        /// <summary>Per-event gain, multiplied with <see cref="HapbeatModSettings.MasterGain"/>.</summary>
        public float Gain = 1.0f;

        /// <summary>Whether this logical event fires at all.</summary>
        public bool Enabled = true;

        public HapbeatEventSetting() { }

        public HapbeatEventSetting(string eventId, float gain, bool enabled)
        {
            EventId = eventId;
            Gain = gain;
            Enabled = enabled;
        }
    }

    /// <summary>
    /// JSON settings shared by all three VR mods (see dev-notes design doc). Loaded from
    /// a file next to the mod; written back with defaults when the file is missing so the
    /// user always has something to edit.
    /// <para>
    /// The JSON reader/writer is hand-rolled (see <see cref="MiniJson"/>) because the mod
    /// hosts differ: MelonLoader runs net6 where System.Text.Json exists, but the
    /// Blade &amp; Sorcery scripted mod runs on Unity's net472 mono where it does not.
    /// Depending on Newtonsoft would mean shipping a DLL alongside each mod. The schema
    /// is two levels deep, so a small reader is cheaper than either.
    /// </para>
    /// </summary>
    public class HapbeatModSettings
    {
        /// <summary>App name shown on the device OLED. Truncated to 16 chars on the wire
        /// (<see cref="HapbeatProtocol.MaxAppNameLength"/>, DEC-029).</summary>
        public string AppName = "HapbeatMod";

        /// <summary>Forced group number (1..99), or -1 to disable. Applied to every
        /// outgoing target via <c>ResolveTarget</c>.</summary>
        public int Group = -1;

        /// <summary>Forced player number (1..99), or -1 to disable.</summary>
        public int Player = -1;

        /// <summary>Gain applied to every event on top of its per-event gain.</summary>
        public float MasterGain = 1.0f;

        /// <summary>Minimum interval between two fires of the *same* logical event, in
        /// milliseconds. Suppresses packet storms from rapid-fire hooks. 0 disables.</summary>
        public int MinIntervalMs = 60;

        /// <summary>Logical event name ("shot", "hit", ...) to its binding.</summary>
        public readonly Dictionary<string, HapbeatEventSetting> Events =
            new Dictionary<string, HapbeatEventSetting>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Binding for <paramref name="logicalEvent"/>, or null if not configured.</summary>
        public HapbeatEventSetting GetEvent(string logicalEvent)
        {
            if (string.IsNullOrEmpty(logicalEvent))
                return null;
            HapbeatEventSetting setting;
            return Events.TryGetValue(logicalEvent, out setting) ? setting : null;
        }

        /// <summary>
        /// Default settings for a mod named <paramref name="appName"/>, bound to the
        /// standard vr-shooter-kit clips.
        /// </summary>
        public static HapbeatModSettings CreateDefault(string appName = "HapbeatMod")
        {
            var s = new HapbeatModSettings();
            s.AppName = appName;
            s.Events["shot"]      = new HapbeatEventSetting("vr-shooter-kit.shot_recoil", 1.0f, true);
            s.Events["hit"]       = new HapbeatEventSetting("vr-shooter-kit.hit_heavy", 1.0f, true);
            s.Events["hit_light"] = new HapbeatEventSetting("vr-shooter-kit.hit_light", 1.0f, true);
            s.Events["reload"]    = new HapbeatEventSetting("vr-shooter-kit.reload_click", 0.8f, true);
            s.Events["heartbeat"] = new HapbeatEventSetting("vr-shooter-kit.heartbeat", 0.9f, true);
            s.Events["beat"]      = new HapbeatEventSetting("vr-shooter-kit.beat_pulse", 0.5f, true);
            s.Events["kill"]      = new HapbeatEventSetting("vr-shooter-kit.kill_confirm", 1.0f, true);
            s.Events["slash"]     = new HapbeatEventSetting("vr-shooter-kit.slash", 1.0f, true);
            s.Events["block"]     = new HapbeatEventSetting("vr-shooter-kit.block_thud", 1.0f, true);
            return s;
        }

        /// <summary>
        /// Load settings from <paramref name="path"/>. If the file does not exist it is
        /// created with defaults. If it exists but cannot be read or parsed, defaults are
        /// returned and a warning goes to <paramref name="log"/> — the existing file is
        /// left untouched so the user can fix their typo instead of losing their tuning.
        /// </summary>
        public static HapbeatModSettings LoadOrCreate(string path, string appName = "HapbeatMod",
            Action<string> log = null)
        {
            var defaults = CreateDefault(appName);

            try
            {
                if (!File.Exists(path))
                {
                    defaults.Save(path);
                    if (log != null) log("[Hapbeat] Wrote default settings to " + path);
                    return defaults;
                }

                string text = File.ReadAllText(path, Encoding.UTF8);
                return Parse(text, defaults);
            }
            catch (Exception ex)
            {
                if (log != null)
                    log("[Hapbeat] Failed to load settings from " + path + " (" + ex.Message +
                        "); using defaults.");
                return CreateDefault(appName);
            }
        }

        /// <summary>
        /// Parse settings JSON. Fields absent from the document keep the value they have
        /// in <paramref name="defaults"/>; an <c>events</c> object *replaces* the default
        /// event map wholesale so a user can remove a binding.
        /// </summary>
        /// <exception cref="FormatException">Thrown on malformed JSON.</exception>
        public static HapbeatModSettings Parse(string json, HapbeatModSettings defaults = null)
        {
            var s = defaults ?? CreateDefault();
            var root = MiniJson.Parse(json);
            if (root == null || root.Type != JsonType.Object)
                throw new FormatException("Settings root must be a JSON object.");

            s.AppName = root.GetString("appName", s.AppName);
            s.Group = root.GetInt("group", s.Group);
            s.Player = root.GetInt("player", s.Player);
            s.MasterGain = root.GetFloat("masterGain", s.MasterGain);
            s.MinIntervalMs = root.GetInt("minIntervalMs", s.MinIntervalMs);

            JsonValue events = root.GetMember("events");
            if (events != null && events.Type == JsonType.Object)
            {
                s.Events.Clear();
                foreach (var kv in events.Members)
                {
                    JsonValue e = kv.Value;
                    if (e == null || e.Type != JsonType.Object)
                        continue;
                    s.Events[kv.Key] = new HapbeatEventSetting(
                        e.GetString("eventId", ""),
                        e.GetFloat("gain", 1.0f),
                        e.GetBool("enabled", true));
                }
            }

            return s;
        }

        /// <summary>Write these settings to <paramref name="path"/> (UTF-8, no BOM).</summary>
        public void Save(string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, ToJson(), new UTF8Encoding(false));
        }

        /// <summary>Serialize to the settings JSON schema.</summary>
        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"appName\": ").Append(MiniJson.Quote(AppName)).Append(",\n");
            sb.Append("  \"group\": ").Append(Group.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"player\": ").Append(Player.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"masterGain\": ").Append(MiniJson.Number(MasterGain)).Append(",\n");
            sb.Append("  \"minIntervalMs\": ").Append(MinIntervalMs.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"events\": {\n");

            int i = 0;
            foreach (var kv in Events)
            {
                HapbeatEventSetting e = kv.Value;
                sb.Append("    ").Append(MiniJson.Quote(kv.Key)).Append(": { \"eventId\": ")
                  .Append(MiniJson.Quote(e.EventId))
                  .Append(", \"gain\": ").Append(MiniJson.Number(e.Gain))
                  .Append(", \"enabled\": ").Append(e.Enabled ? "true" : "false")
                  .Append(" }");
                if (++i < Events.Count) sb.Append(',');
                sb.Append('\n');
            }

            sb.Append("  }\n");
            sb.Append("}\n");
            return sb.ToString();
        }
    }

    internal enum JsonType { Null, Bool, Number, String, Array, Object }

    /// <summary>Parsed JSON node. Only what the settings schema needs.</summary>
    internal class JsonValue
    {
        public JsonType Type;
        public bool Bool;
        public double Number;
        public string String;
        public List<JsonValue> Items;
        public Dictionary<string, JsonValue> Members;

        public JsonValue GetMember(string name)
        {
            JsonValue v;
            return (Members != null && Members.TryGetValue(name, out v)) ? v : null;
        }

        public string GetString(string name, string fallback)
        {
            JsonValue v = GetMember(name);
            return (v != null && v.Type == JsonType.String) ? v.String : fallback;
        }

        public float GetFloat(string name, float fallback)
        {
            JsonValue v = GetMember(name);
            return (v != null && v.Type == JsonType.Number) ? (float)v.Number : fallback;
        }

        public int GetInt(string name, int fallback)
        {
            JsonValue v = GetMember(name);
            return (v != null && v.Type == JsonType.Number) ? (int)Math.Round(v.Number) : fallback;
        }

        public bool GetBool(string name, bool fallback)
        {
            JsonValue v = GetMember(name);
            return (v != null && v.Type == JsonType.Bool) ? v.Bool : fallback;
        }
    }

    /// <summary>
    /// Dependency-free recursive-descent JSON reader/quoter. Deliberately strict enough
    /// to reject a truncated or malformed file (so <see cref="HapbeatModSettings.LoadOrCreate"/>
    /// can fall back to defaults with a warning) rather than silently reading garbage.
    /// </summary>
    internal static class MiniJson
    {
        public static JsonValue Parse(string text)
        {
            if (text == null)
                throw new FormatException("Empty JSON document.");

            int i = 0;
            JsonValue value = ParseValue(text, ref i);
            SkipWhitespace(text, ref i);
            if (i != text.Length)
                throw new FormatException("Trailing characters after JSON value at offset " + i + ".");
            return value;
        }

        private static JsonValue ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length)
                throw new FormatException("Unexpected end of JSON.");

            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return new JsonValue { Type = JsonType.String, String = ParseString(s, ref i) };
                case 't': Expect(s, ref i, "true"); return new JsonValue { Type = JsonType.Bool, Bool = true };
                case 'f': Expect(s, ref i, "false"); return new JsonValue { Type = JsonType.Bool, Bool = false };
                case 'n': Expect(s, ref i, "null"); return new JsonValue { Type = JsonType.Null };
                default: return ParseNumber(s, ref i);
            }
        }

        private static JsonValue ParseObject(string s, ref int i)
        {
            var value = new JsonValue
            {
                Type = JsonType.Object,
                Members = new Dictionary<string, JsonValue>(StringComparer.OrdinalIgnoreCase)
            };
            i++; // '{'
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return value; }

            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"')
                    throw new FormatException("Expected object key at offset " + i + ".");
                string key = ParseString(s, ref i);

                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    throw new FormatException("Expected ':' at offset " + i + ".");
                i++;

                value.Members[key] = ParseValue(s, ref i);

                SkipWhitespace(s, ref i);
                if (i >= s.Length)
                    throw new FormatException("Unterminated object.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return value; }
                throw new FormatException("Expected ',' or '}' at offset " + i + ".");
            }
        }

        private static JsonValue ParseArray(string s, ref int i)
        {
            var value = new JsonValue { Type = JsonType.Array, Items = new List<JsonValue>() };
            i++; // '['
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return value; }

            while (true)
            {
                value.Items.Add(ParseValue(s, ref i));
                SkipWhitespace(s, ref i);
                if (i >= s.Length)
                    throw new FormatException("Unterminated array.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return value; }
                throw new FormatException("Expected ',' or ']' at offset " + i + ".");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            i++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length)
                    throw new FormatException("Unterminated string.");
                char c = s[i++];
                if (c == '"')
                    return sb.ToString();

                if (c != '\\') { sb.Append(c); continue; }

                if (i >= s.Length)
                    throw new FormatException("Unterminated escape sequence.");
                char esc = s[i++];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length)
                            throw new FormatException("Truncated \\u escape.");
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default:
                        throw new FormatException("Invalid escape '\\" + esc + "'.");
                }
            }
        }

        private static JsonValue ParseNumber(string s, ref int i)
        {
            int start = i;
            if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' ||
                                    ((s[i] == '-' || s[i] == '+') && (s[i - 1] == 'e' || s[i - 1] == 'E'))))
                i++;

            string token = s.Substring(start, i - start);
            double parsed;
            if (token.Length == 0 ||
                !double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
                throw new FormatException("Invalid number '" + token + "' at offset " + start + ".");

            return new JsonValue { Type = JsonType.Number, Number = parsed };
        }

        private static void Expect(string s, ref int i, string literal)
        {
            if (i + literal.Length > s.Length || string.CompareOrdinal(s, i, literal, 0, literal.Length) != 0)
                throw new FormatException("Expected '" + literal + "' at offset " + i + ".");
            i += literal.Length;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n'))
                i++;
        }

        public static string Quote(string value)
        {
            var sb = new StringBuilder();
            sb.Append('"');
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>Culture-invariant number literal (never "1,0" under a European locale).</summary>
        public static string Number(float value)
        {
            return value.ToString("0.0###", CultureInfo.InvariantCulture);
        }
    }
}
