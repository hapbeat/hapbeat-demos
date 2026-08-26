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
        public DemoSwitchCommand(string controllerId, long sequence, string demoId, string auth)
        {
            ControllerId = controllerId;
            Sequence = sequence;
            DemoId = demoId;
            Auth = auth ?? string.Empty;
        }

        public string ControllerId { get; }
        public long Sequence { get; }
        public string DemoId { get; }
        public string Auth { get; }
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

    internal static class DemoSwitchProtocol
    {
        public const int MaxPayloadBytes = 1024;
        public const long MaxSequence = 9007199254740991L;
        private static readonly string[] CommandFields = { "version", "type", "controller_id", "seq", "demo_id", "auth" };
        private static readonly string[] StatusFields = { "version", "type", "controller_id", "seq", "demo_id", "current_demo_id", "code", "message", "auth" };
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
                var value = JObject.Parse(json, StrictLoadSettings);
                if (value.Properties().Any(p => !CommandFields.Contains(p.Name, StringComparer.Ordinal)))
                    return Error("invalid_payload", "Command contains an unknown or duplicate field.");

                if (!TryInteger(value, "version", out var version) || version != 1)
                    return Error("unsupported_version", "Only protocol version 1 is supported.");
                if (!TryString(value, "type", out var type) || type != "SWITCH")
                    return Error("invalid_payload", "type must be SWITCH.");
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

                return new CommandParseResult(new DemoSwitchCommand(controllerId, sequence, demoId, auth), null, null);
            }
            catch (Exception exception) when (exception is JsonException || exception is OverflowException || exception is FormatException)
            {
                return Error("invalid_payload", exception.Message);
            }
        }

        public static StatusParseResult ParseStatus(string json)
        {
            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > MaxPayloadBytes)
                return new StatusParseResult(null, "Payload is empty or exceeds 1024 bytes.");

            try
            {
                var value = JObject.Parse(json, StrictLoadSettings);
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

        public static string Canonicalize(DemoSwitchCommand command) =>
            "HAPBEAT-DEMO-SWITCH/1\nCOMMAND\n" +
            Field("version", "1") + Field("type", "SWITCH") +
            Field("controller_id", command.ControllerId) + Field("seq", command.Sequence.ToString(CultureInfo.InvariantCulture)) +
            Field("demo_id", command.DemoId);

        public static string Canonicalize(DemoSwitchStatus status) =>
            "HAPBEAT-DEMO-SWITCH/1\nSTATUS\n" +
            Field("version", "1") + Field("type", status.Type) + Field("controller_id", status.ControllerId) +
            Field("seq", status.Sequence.ToString(CultureInfo.InvariantCulture)) + Field("demo_id", status.DemoId) +
            Field("current_demo_id", status.CurrentDemoId) + Field("code", status.Code) + Field("message", status.Message);

        public static string ComputeAuth(DemoSwitchCommand command, string secret) => ComputeMac(Canonicalize(command), secret);
        public static string ComputeAuth(DemoSwitchStatus status, string secret) => ComputeMac(Canonicalize(status), secret);
        public static bool Authenticate(DemoSwitchCommand command, string secret) => ConstantTimeMacEquals(command.Auth, ComputeAuth(command, secret));
        public static bool Authenticate(DemoSwitchStatus status, string secret) => ConstantTimeMacEquals(status.Auth, ComputeAuth(status, secret));

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
        private static string Field(string name, string value) => name + "=" + Encoding.UTF8.GetByteCount(value).ToString(CultureInfo.InvariantCulture) + ":" + value + "\n";

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
}
