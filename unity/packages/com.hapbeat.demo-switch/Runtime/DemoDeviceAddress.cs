using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// Per-device Hapbeat address file `hapbeat-device.json` (hapbeat-contracts specs/demo-session.md,
    /// "device address file"). Written at install time by the operator PC; read once at start.
    /// Reading and validation are SDK-independent; the Hapbeat SDK applier lives in the optional
    /// `Hapbeat.DemoSwitch.HapbeatSdk` assembly.
    /// </summary>
    public sealed class DemoDeviceAddress
    {
        public const string FileName = "hapbeat-device.json";
        public const int MaxBytes = 1024;
        /// <summary>Axis value meaning "leave this axis unchanged".</summary>
        public const int Unspecified = -1;
        /// <summary>Editor only: absolute path of a file to read instead of none.</summary>
        public const string PathEnvironmentVariable = "HAPBEAT_DEVICE_ADDRESS_FILE";
        public const string LogTag = "HAPBEAT_DEVICE_ADDRESS";

        public DemoDeviceAddress(int player, int group, string source = null)
        {
            Player = player;
            Group = group;
            Source = source;
        }

        /// <summary>1..99, or <see cref="Unspecified"/>.</summary>
        public int Player { get; }
        /// <summary>1..99, or <see cref="Unspecified"/>.</summary>
        public int Group { get; }
        /// <summary>Path the value was read from, or null when parsed from text.</summary>
        public string Source { get; }

        /// <summary>Test entry: replaces the platform path (Editor and players).</summary>
        internal static string PathOverride { get; set; }

        /// <summary>
        /// File read on this runtime: `Application.persistentDataPath` (Android `getExternalFilesDir(null)`)
        /// in players; in the Editor only <see cref="PathEnvironmentVariable"/>, otherwise null (nothing to read).
        /// </summary>
        public static string ResolvePath()
        {
            if (!string.IsNullOrEmpty(PathOverride)) return PathOverride;
#if UNITY_EDITOR
            var path = Environment.GetEnvironmentVariable(PathEnvironmentVariable);
            return string.IsNullOrEmpty(path) ? null : path;
#else
            return Path.Combine(Application.persistentDataPath, FileName);
#endif
        }

        /// <summary>
        /// Reads this device's file. Returns null when there is no file, or when it is invalid
        /// (a warning is logged; the caller then does nothing).
        /// </summary>
        public static DemoDeviceAddress LoadForThisDevice()
        {
            var path = ResolvePath();
            if (path == null) return null;
            if (!TryLoad(path, out var address, out var error))
            {
                if (error != null) Debug.LogWarning("[Demo Device Address] Ignored " + path + ": " + error);
                return null;
            }
            return address;
        }

        /// <summary>False with a null <paramref name="error"/> when the file does not exist.</summary>
        internal static bool TryLoad(string path, out DemoDeviceAddress address, out string error)
        {
            address = null;
            string text;
            try
            {
                if (!File.Exists(path)) { error = null; return false; }
                if (new FileInfo(path).Length > MaxBytes) { error = "File exceeds " + MaxBytes + " bytes."; return false; }
                text = File.ReadAllText(path, new UTF8Encoding(false, true));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is DecoderFallbackException)
            {
                error = exception.Message;
                return false;
            }
            if (!TryParse(text, out var parsed, out error)) return false;
            address = new DemoDeviceAddress(parsed.Player, parsed.Group, path);
            return true;
        }

        /// <summary>Schema-equivalent validation (`demo-device-address.schema.json`) plus the 1024-byte limit.</summary>
        public static bool TryParse(string json, out DemoDeviceAddress address, out string error)
        {
            address = null;
            if (string.IsNullOrEmpty(json)) { error = "JSON is empty."; return false; }
            if (Encoding.UTF8.GetByteCount(json) > MaxBytes) { error = "JSON exceeds " + MaxBytes + " bytes."; return false; }
            try
            {
                var root = DemoSwitchProtocol.ParseStrictObject(json);
                if (!DemoSessionJson.OnlyFields(root, "version", "player", "group")) { error = "Unknown field."; return false; }
                if (!DemoSessionJson.TryVersion(root)) { error = "version must be 1."; return false; }
                if (!TryAxis(root, "player", out var player)) { error = "player must be -1 or 1..99."; return false; }
                if (!TryAxis(root, "group", out var group)) { error = "group must be -1 or 1..99."; return false; }
                address = new DemoDeviceAddress(player, group);
                error = null;
                return true;
            }
            catch (Exception exception) when (exception is JsonException || exception is FormatException || exception is InvalidCastException || exception is OverflowException)
            {
                error = exception.Message;
                return false;
            }
        }

        private static bool TryAxis(JObject root, string name, out int value)
        {
            value = Unspecified;
            if (!root.TryGetValue(name, StringComparison.Ordinal, out var token) || token.Type != JTokenType.Integer) return false;
            var number = token.Value<long>();
            if (number != Unspecified && (number < 1 || number > 99)) return false;
            value = (int)number;
            return true;
        }

        /// <summary>
        /// Values to pass to the SDK's address override. The SDK treats -1 as "disable this axis",
        /// so an <see cref="Unspecified"/> axis passes the current effective value to keep it unchanged.
        /// </summary>
        public void Resolve(int currentPlayer, int currentGroup, out int player, out int group)
        {
            player = Player == Unspecified ? currentPlayer : Player;
            group = Group == Unspecified ? currentGroup : Group;
        }

        /// <summary>`HAPBEAT_DEVICE_ADDRESS player=&lt;n&gt; group=&lt;n&gt; source=&lt;path&gt;` with the effective values.</summary>
        public static string FormatLog(int player, int group, string source) =>
            LogTag + " player=" + player.ToString(CultureInfo.InvariantCulture) + " group=" + group.ToString(CultureInfo.InvariantCulture)
            + " source=" + (source ?? "(none)");
    }
}
