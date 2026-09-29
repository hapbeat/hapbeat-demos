using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hapbeat.DemoSwitch
{
    internal sealed class DemoSwitchCommand
    {
        public DemoSwitchCommand(string controllerId, long sequence, string demoId, string auth, string action = null, string sceneId = "")
        {
            ControllerId = controllerId;
            Sequence = sequence;
            DemoId = demoId;
            Auth = auth ?? string.Empty;
            Action = action;
            SceneId = sceneId;
        }

        public string ControllerId { get; }
        public long Sequence { get; }
        public string DemoId { get; }
        public string Auth { get; }
        public string Action { get; }
        public string SceneId { get; }
        public bool IsControl => Action != null;
    }

    internal sealed class DemoSwitchStatus
    {
        public DemoSwitchStatus(string type, string controllerId, long sequence, string demoId,
            string currentDemoId, string code, string message, string auth = "")
        {
            Type = type;
            ControllerId = controllerId;
            Sequence = sequence;
            DemoId = demoId;
            CurrentDemoId = currentDemoId;
            Code = code;
            Message = message ?? string.Empty;
            Auth = auth ?? string.Empty;
        }

        public string Type { get; }
        public string ControllerId { get; }
        public long Sequence { get; }
        public string DemoId { get; }
        public string CurrentDemoId { get; }
        public string Code { get; }
        public string Message { get; }
        public string Auth { get; }
    }

    internal sealed class DemoSwitchDiscover
    {
        public DemoSwitchDiscover(string controllerId, string nonce, string auth)
        {
            ControllerId = controllerId;
            Nonce = nonce;
            Auth = auth ?? string.Empty;
        }

        public string ControllerId { get; }
        public string Nonce { get; }
        public string Auth { get; }
    }

    internal sealed class DemoSwitchHere
    {
        public DemoSwitchHere(string controllerId, string nonce, string currentDemoId, string auth = "")
        {
            ControllerId = controllerId;
            Nonce = nonce;
            CurrentDemoId = currentDemoId;
            Auth = auth ?? string.Empty;
        }

        public string ControllerId { get; }
        public string Nonce { get; }
        public string CurrentDemoId { get; }
        public string Auth { get; }
    }

    internal readonly struct CommandParseResult
    {
        public CommandParseResult(DemoSwitchCommand command, string errorCode, string errorMessage)
        {
            Command = command;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public bool Success => Command != null;
        public DemoSwitchCommand Command { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
    }

    internal readonly struct StatusParseResult
    {
        public StatusParseResult(DemoSwitchStatus status, string errorMessage)
        {
            Status = status;
            ErrorMessage = errorMessage;
        }

        public bool Success => Status != null;
        public DemoSwitchStatus Status { get; }
        public string ErrorMessage { get; }
    }

    internal readonly struct DiscoveryParseResult
    {
        public DiscoveryParseResult(DemoSwitchDiscover discover, string errorMessage)
        {
            Discover = discover;
            ErrorMessage = errorMessage;
        }

        public bool Success => Discover != null;
        public DemoSwitchDiscover Discover { get; }
        public string ErrorMessage { get; }
    }

    internal readonly struct HereParseResult
    {
        public HereParseResult(DemoSwitchHere here, string errorMessage)
        {
            Here = here;
            ErrorMessage = errorMessage;
        }

        public bool Success => Here != null;
        public DemoSwitchHere Here { get; }
        public string ErrorMessage { get; }
    }

    internal static class DemoSwitchProtocol
    {
        public const int MaxPayloadBytes = 1024;
        public const long MaxSequence = 9007199254740991L;
        private static readonly string[] CommandFields = { "version", "type", "controller_id", "seq", "demo_id", "auth" };
        private static readonly string[] ControlFields = { "version", "type", "controller_id", "seq", "demo_id", "auth", "action", "scene_id" };
        private static readonly string[] StatusFields = { "version", "type", "controller_id", "seq", "demo_id", "current_demo_id", "code", "message", "auth" };
        private static readonly string[] DiscoverFields = { "version", "type", "controller_id", "nonce", "auth" };
        private static readonly string[] HereFields = { "version", "type", "controller_id", "nonce", "current_demo_id", "auth" };
        private static readonly HashSet<string> StatusTypes = new HashSet<string>(StringComparer.Ordinal) { "ACK", "READY", "FAILED" };
        private static readonly HashSet<string> StatusCodes = new HashSet<string>(StringComparer.Ordinal)
        {
            "ok", "invalid_payload", "unsupported_version", "invalid_auth", "unsigned_disabled",
            "not_allowed", "replay", "launch_failed", "listener_failed"
        };
        private static readonly JsonLoadSettings StrictLoadSettings = new JsonLoadSettings
        {
            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
        };

        public static CommandParseResult ParseCommand(string json)
        {
            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > MaxPayloadBytes)
                return Error("invalid_payload", "Payload is empty or exceeds 1024 bytes.");

            try
            {
                var value = ParseStrictObject(json);
                bool control = TryString(value,"type",out var messageType) && messageType == "CONTROL";
                if (value.Properties().Any(p => !(control ? ControlFields : CommandFields).Contains(p.Name, StringComparer.Ordinal)))
                    return Error("invalid_payload", "Command contains an unknown or duplicate field.");

                if (!TryInteger(value, "version", out var version) || version != 1)
                    return Error("unsupported_version", "Only protocol version 1 is supported.");
                if (!TryString(value, "type", out var type) || (type != "SWITCH" && type != "CONTROL"))
                    return Error("invalid_payload", "type must be SWITCH or CONTROL.");
                if (!TryString(value, "controller_id", out var controllerId) || !IsIdentifier(controllerId))
                    return Error("invalid_payload", "controller_id is invalid.");
                if (!TryInteger(value, "seq", out var sequence) || sequence < 1 || sequence > MaxSequence)
                    return Error("invalid_payload", "seq is outside the supported range.");
                if (!TryString(value, "demo_id", out var demoId) || !IsIdentifier(demoId))
                    return Error("invalid_payload", "demo_id is invalid.");

                var auth = string.Empty;
                if (value.TryGetValue("auth", StringComparison.Ordinal, out var authToken))
                {
                    if (authToken.Type != JTokenType.String) return Error("invalid_payload", "auth must be a string.");
                    auth = authToken.Value<string>();
                    if (!IsLowerHexMac(auth)) return Error("invalid_payload", "auth must be 64 lowercase hex characters.");
                }

                string action = null, sceneId = "";
                if (control && (!TryString(value,"action",out action) || !TryString(value,"scene_id",out sceneId)
                    || !IsControlAction(action) || (action == "scene" ? !IsIdentifier(sceneId) : sceneId != "")))
                    return Error("invalid_payload", "Invalid control action or scene_id.");
                return new CommandParseResult(new DemoSwitchCommand(controllerId, sequence, demoId, auth, action, sceneId), null, null);
            }
            catch (Exception exception) when (exception is JsonException || exception is OverflowException || exception is FormatException)
            {
                return Error("invalid_payload", exception.Message);
            }
        }

        public static bool TryGetMessageType(string json, out string type)
        {
            type = null;
            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > MaxPayloadBytes) return false;
            try
            {
                var value = ParseStrictObject(json);
                return TryString(value, "type", out type);
            }
            catch (Exception exception) when (exception is JsonException || exception is OverflowException || exception is FormatException)
            {
                return false;
            }
        }

        public static StatusParseResult ParseStatus(string json)
        {
            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > MaxPayloadBytes)
                return new StatusParseResult(null, "Payload is empty or exceeds 1024 bytes.");

            try
            {
                var value = ParseStrictObject(json);
                if (value.Properties().Any(p => !StatusFields.Contains(p.Name, StringComparer.Ordinal)))
                    return new StatusParseResult(null, "Status contains an unknown field.");
                if (!TryInteger(value, "version", out var version) || version != 1 ||
                    !TryString(value, "type", out var type) || !StatusTypes.Contains(type) ||
                    !TryString(value, "controller_id", out var controllerId) || !IsIdentifier(controllerId) ||
                    !TryInteger(value, "seq", out var sequence) || sequence < 1 || sequence > MaxSequence ||
                    !TryString(value, "demo_id", out var demoId) || !IsIdentifier(demoId) ||
                    !TryString(value, "current_demo_id", out var currentDemoId) || !IsIdentifier(currentDemoId) ||
                    !TryString(value, "code", out var code) || !StatusCodes.Contains(code) ||
                    !TryString(value, "message", out var message) || Encoding.UTF8.GetByteCount(message) > 256)
                    return new StatusParseResult(null, "Status is incomplete or invalid.");

                var auth = string.Empty;
                if (value.TryGetValue("auth", StringComparison.Ordinal, out var token))
                {
                    if (token.Type != JTokenType.String) return new StatusParseResult(null, "Status auth must be a string.");
                    auth = token.Value<string>();
                }
                if (auth.Length > 0 && !IsLowerHexMac(auth)) return new StatusParseResult(null, "Status auth is invalid.");
                return new StatusParseResult(new DemoSwitchStatus(type, controllerId, sequence, demoId, currentDemoId, code, message, auth), null);
            }
            catch (Exception exception) when (exception is JsonException || exception is OverflowException || exception is FormatException)
            {
                return new StatusParseResult(null, exception.Message);
            }
        }

        public static string SerializeStatus(DemoSwitchStatus status, string secret)
        {
            if (status == null || !StatusTypes.Contains(status.Type) || !StatusCodes.Contains(status.Code) ||
                !IsIdentifier(status.ControllerId) || !IsIdentifier(status.DemoId) || !IsIdentifier(status.CurrentDemoId) ||
                status.Sequence < 1 || status.Sequence > MaxSequence)
                throw new ArgumentException("Status fields do not satisfy the Demo Switch contract.", nameof(status));

            var message = TruncateUtf8(status.Message ?? string.Empty, 256);
            while (true)
            {
                var normalized = new DemoSwitchStatus(status.Type, status.ControllerId, status.Sequence, status.DemoId,
                    status.CurrentDemoId, status.Code, message);
                var auth = string.IsNullOrEmpty(secret) ? string.Empty : ComputeAuth(normalized, secret);
                var value = new JObject
                {
                    ["version"] = 1,
                    ["type"] = normalized.Type,
                    ["controller_id"] = normalized.ControllerId,
                    ["seq"] = normalized.Sequence,
                    ["demo_id"] = normalized.DemoId,
                    ["current_demo_id"] = normalized.CurrentDemoId,
                    ["code"] = normalized.Code,
                    ["message"] = normalized.Message
                };
                if (auth.Length > 0) value["auth"] = auth;
                var json = value.ToString(Formatting.None);
                if (Encoding.UTF8.GetByteCount(json) <= MaxPayloadBytes) return json;
                if (message.Length == 0)
                    throw new InvalidOperationException("Status fields exceed the 1024-byte payload limit.");
                message = RemoveLastScalar(message);
            }
        }

        public static DiscoveryParseResult ParseDiscover(string json)
        {
            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > MaxPayloadBytes)
                return new DiscoveryParseResult(null, "Payload is empty or exceeds 1024 bytes.");

            try
            {
                var value = ParseStrictObject(json);
                if (value.Properties().Any(p => !DiscoverFields.Contains(p.Name, StringComparer.Ordinal)))
                    return new DiscoveryParseResult(null, "DISCOVER contains an unknown field.");
                if (!TryInteger(value, "version", out var version) || version != 1 ||
                    !TryString(value, "type", out var type) || type != "DISCOVER" ||
                    !TryString(value, "controller_id", out var controllerId) || !IsIdentifier(controllerId) ||
                    !TryString(value, "nonce", out var nonce) || !IsNonce(nonce))
                    return new DiscoveryParseResult(null, "DISCOVER is incomplete or invalid.");

                var auth = string.Empty;
                if (value.TryGetValue("auth", StringComparison.Ordinal, out var token))
                {
                    if (token.Type != JTokenType.String)
                        return new DiscoveryParseResult(null, "DISCOVER auth must be a string.");
                    auth = token.Value<string>();
                    if (!IsLowerHexMac(auth))
                        return new DiscoveryParseResult(null, "DISCOVER auth is invalid.");
                }
                return new DiscoveryParseResult(new DemoSwitchDiscover(controllerId, nonce, auth), null);
            }
            catch (Exception exception) when (exception is JsonException || exception is OverflowException || exception is FormatException)
            {
                return new DiscoveryParseResult(null, exception.Message);
            }
        }

        public static HereParseResult ParseHere(string json)
        {
            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > MaxPayloadBytes)
                return new HereParseResult(null, "Payload is empty or exceeds 1024 bytes.");

            try
            {
                var value = ParseStrictObject(json);
                if (value.Properties().Any(p => !HereFields.Contains(p.Name, StringComparer.Ordinal)))
                    return new HereParseResult(null, "HERE contains an unknown field.");
                if (!TryInteger(value, "version", out var version) || version != 1 ||
                    !TryString(value, "type", out var type) || type != "HERE" ||
                    !TryString(value, "controller_id", out var controllerId) || !IsIdentifier(controllerId) ||
                    !TryString(value, "nonce", out var nonce) || !IsNonce(nonce) ||
                    !TryString(value, "current_demo_id", out var currentDemoId) || !IsIdentifier(currentDemoId))
                    return new HereParseResult(null, "HERE is incomplete or invalid.");

                var auth = string.Empty;
                if (value.TryGetValue("auth", StringComparison.Ordinal, out var token))
                {
                    if (token.Type != JTokenType.String)
                        return new HereParseResult(null, "HERE auth must be a string.");
                    auth = token.Value<string>();
                    if (!IsLowerHexMac(auth)) return new HereParseResult(null, "HERE auth is invalid.");
                }
                return new HereParseResult(new DemoSwitchHere(controllerId, nonce, currentDemoId, auth), null);
            }
            catch (Exception exception) when (exception is JsonException || exception is OverflowException || exception is FormatException)
            {
                return new HereParseResult(null, exception.Message);
            }
        }

        public static string SerializeHere(DemoSwitchHere here, string secret)
        {
            if (here == null || !IsIdentifier(here.ControllerId) || !IsNonce(here.Nonce) || !IsIdentifier(here.CurrentDemoId))
                throw new ArgumentException("HERE fields do not satisfy the Demo Switch contract.", nameof(here));

            var auth = string.IsNullOrEmpty(secret) ? string.Empty : ComputeAuth(here, secret);
            var value = new JObject
            {
                ["version"] = 1,
                ["type"] = "HERE",
                ["controller_id"] = here.ControllerId,
                ["nonce"] = here.Nonce,
                ["current_demo_id"] = here.CurrentDemoId
            };
            if (auth.Length > 0) value["auth"] = auth;
            var json = value.ToString(Formatting.None);
            if (Encoding.UTF8.GetByteCount(json) > MaxPayloadBytes)
                throw new InvalidOperationException("HERE fields exceed the 1024-byte payload limit.");
            return json;
        }

        public static bool IsControlAction(string action) => action == "menu_open" || action == "menu_close"
            || action == "recenter" || action == "restart" || action == "scene";

        public static string Canonicalize(DemoSwitchCommand command) =>
            "HAPBEAT-DEMO-SWITCH/1\nCOMMAND\n" +
            Field("version", "1") + Field("type", command.IsControl ? "CONTROL" : "SWITCH") +
            Field("controller_id", command.ControllerId) + Field("seq", command.Sequence.ToString(CultureInfo.InvariantCulture)) +
            Field("demo_id", command.DemoId) + (command.IsControl ? Field("action",command.Action) + Field("scene_id",command.SceneId) : "");

        public static string Canonicalize(DemoSwitchStatus status) =>
            "HAPBEAT-DEMO-SWITCH/1\nSTATUS\n" +
            Field("version", "1") + Field("type", status.Type) + Field("controller_id", status.ControllerId) +
            Field("seq", status.Sequence.ToString(CultureInfo.InvariantCulture)) + Field("demo_id", status.DemoId) +
            Field("current_demo_id", status.CurrentDemoId) + Field("code", status.Code) + Field("message", status.Message);

        public static string Canonicalize(DemoSwitchDiscover discover) =>
            "HAPBEAT-DEMO-SWITCH/1\nDISCOVER\n" +
            Field("version", "1") + Field("type", "DISCOVER") + Field("controller_id", discover.ControllerId) +
            Field("nonce", discover.Nonce);

        public static string Canonicalize(DemoSwitchHere here) =>
            "HAPBEAT-DEMO-SWITCH/1\nHERE\n" +
            Field("version", "1") + Field("type", "HERE") + Field("controller_id", here.ControllerId) +
            Field("nonce", here.Nonce) + Field("current_demo_id", here.CurrentDemoId);

        public static string ComputeAuth(DemoSwitchCommand command, string secret) => ComputeMac(Canonicalize(command), secret);
        public static string ComputeAuth(DemoSwitchStatus status, string secret) => ComputeMac(Canonicalize(status), secret);
        public static string ComputeAuth(DemoSwitchDiscover discover, string secret) => ComputeMac(Canonicalize(discover), secret);
        public static string ComputeAuth(DemoSwitchHere here, string secret) => ComputeMac(Canonicalize(here), secret);
        public static bool Authenticate(DemoSwitchCommand command, string secret) => ConstantTimeMacEquals(command.Auth, ComputeAuth(command, secret));
        public static bool Authenticate(DemoSwitchStatus status, string secret) => ConstantTimeMacEquals(status.Auth, ComputeAuth(status, secret));
        public static bool Authenticate(DemoSwitchDiscover discover, string secret) => ConstantTimeMacEquals(discover.Auth, ComputeAuth(discover, secret));
        public static bool Authenticate(DemoSwitchHere here, string secret) => ConstantTimeMacEquals(here.Auth, ComputeAuth(here, secret));

        public static bool IsIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64 || !IsLowerIdentifierFirst(value[0])) return false;
            for (var index = 1; index < value.Length; index++)
            {
                var character = value[index];
                if (!IsLowerIdentifierFirst(character) && character != '.' && character != '_' && character != '-') return false;
            }
            return true;
        }

        private static bool IsLowerIdentifierFirst(char value) => (value >= 'a' && value <= 'z') || (value >= '0' && value <= '9');
        private static bool IsLowerHexMac(string value) => value != null && value.Length == 64 && value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        private static bool IsNonce(string value) => value != null && value.Length == 16 && value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        private static string Field(string name, string value) => name + "=" + Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture) + ":" + value + "\n";

        private static JObject ParseStrictObject(string json)
        {
            RejectJsonComments(json);
            using (var textReader = new System.IO.StringReader(json))
            using (var reader = new JsonTextReader(textReader) { DateParseHandling = DateParseHandling.None })
            {
                var value = JObject.Load(reader, StrictLoadSettings);
                if (reader.Read()) throw new JsonReaderException("Trailing content is not allowed.");
                return value;
            }
        }

        private static void RejectJsonComments(string json)
        {
            var inString = false;
            var escaped = false;
            foreach (var character in json)
            {
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (character == '\\') escaped = true;
                    else if (character == '"') inString = false;
                }
                else if (character == '"') inString = true;
                else if (character == '/') throw new JsonReaderException("JSON comments are not allowed.");
            }
        }

        private static string ComputeMac(string canonical, string secret)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
                return string.Concat(hmac.ComputeHash(Encoding.UTF8.GetBytes(canonical)).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        private static bool ConstantTimeMacEquals(string supplied, string expected)
        {
            if (!IsLowerHexMac(supplied) || expected == null || expected.Length != 64) return false;
            var difference = 0;
            for (var index = 0; index < 64; index++) difference |= supplied[index] ^ expected[index];
            return difference == 0;
        }

        private static bool TryInteger(JObject value, string name, out long result)
        {
            result = 0;
            if (!value.TryGetValue(name, StringComparison.Ordinal, out var token) || token.Type != JTokenType.Integer) return false;
            return long.TryParse(token.ToString(Formatting.None), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private static bool TryString(JObject value, string name, out string result)
        {
            result = null;
            if (!value.TryGetValue(name, StringComparison.Ordinal, out var token) || token.Type != JTokenType.String) return false;
            result = token.Value<string>();
            return result != null;
        }

        private static CommandParseResult Error(string code, string message) => new CommandParseResult(null, code, message);

        private static string TruncateUtf8(string value, int maximumBytes)
        {
            var index = 0;
            var byteCount = 0;
            while (index < value.Length)
            {
                var characterCount = char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]) ? 2 : 1;
                var nextBytes = Encoding.UTF8.GetByteCount(value.Substring(index, characterCount));
                if (byteCount + nextBytes > maximumBytes) break;
                byteCount += nextBytes;
                index += characterCount;
            }
            return index == value.Length ? value : value.Substring(0, index);
        }

        private static string RemoveLastScalar(string value)
        {
            var length = value.Length - 1;
            if (length > 0 && char.IsLowSurrogate(value[length]) && char.IsHighSurrogate(value[length - 1])) length--;
            return value.Substring(0, length);
        }
    }

    internal readonly struct DiscoveryHandleResult
    {
        public DiscoveryHandleResult(string responseJson, string errorCode)
        {
            ResponseJson = responseJson;
            ErrorCode = errorCode;
        }

        public bool ShouldReply => ResponseJson != null;
        public string ResponseJson { get; }
        public string ErrorCode { get; }
    }

    internal static class DemoSwitchDiscoveryHandler
    {
        public static DiscoveryHandleResult Handle(string json, string currentDemoId, string sharedSecret,
            bool allowUnsignedOnIsolatedLan)
        {
            var parsed = DemoSwitchProtocol.ParseDiscover(json);
            if (!parsed.Success) return new DiscoveryHandleResult(null, "invalid_payload");

            var discover = parsed.Discover;
            if (!string.IsNullOrEmpty(sharedSecret))
            {
                if (!DemoSwitchProtocol.Authenticate(discover, sharedSecret))
                    return new DiscoveryHandleResult(null, "invalid_auth");
            }
            else if (!allowUnsignedOnIsolatedLan)
            {
                return new DiscoveryHandleResult(null, "unsigned_disabled");
            }

            if (!DemoSwitchProtocol.IsIdentifier(currentDemoId))
                return new DiscoveryHandleResult(null, "invalid_payload");

            var here = new DemoSwitchHere(discover.ControllerId, discover.Nonce, currentDemoId);
            return new DiscoveryHandleResult(DemoSwitchProtocol.SerializeHere(here, sharedSecret), null);
        }
    }
}
