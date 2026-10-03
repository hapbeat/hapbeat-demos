using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hapbeat.DemoSwitch
{
    /// <summary>Bilingual display label (`ja` required, `en` optional) from the Demo Session contract.</summary>
    public sealed class DemoSessionLabel
    {
        public DemoSessionLabel(string ja, string en = null)
        {
            Ja = ja;
            En = en;
        }

        public string Ja { get; }
        public string En { get; }
    }

    public sealed class DemoSessionOptionValue
    {
        public DemoSessionOptionValue(string value, DemoSessionLabel label)
        {
            Value = value;
            Label = label;
        }

        public string Value { get; }
        public DemoSessionLabel Label { get; }
    }

    public sealed class DemoSessionOption
    {
        public DemoSessionOption(string id, DemoSessionLabel label, IReadOnlyList<DemoSessionOptionValue> values,
            string defaultValue, string whenOptionId = null, IReadOnlyList<string> whenValues = null)
        {
            Id = id;
            Label = label;
            Values = values;
            Default = defaultValue;
            WhenOptionId = whenOptionId;
            WhenValues = whenValues ?? Array.Empty<string>();
        }

        public string Id { get; }
        public DemoSessionLabel Label { get; }
        public IReadOnlyList<DemoSessionOptionValue> Values { get; }
        public string Default { get; }
        /// <summary>Null when the option is always active.</summary>
        public string WhenOptionId { get; }
        public IReadOnlyList<string> WhenValues { get; }

        public bool HasValue(string value) => Values.Any(v => string.Equals(v.Value, value, StringComparison.Ordinal));
        public DemoSessionOptionValue Find(string value) => Values.FirstOrDefault(v => string.Equals(v.Value, value, StringComparison.Ordinal));
    }

    /// <summary>`hapbeat-demo-session.json`: how a demo APK describes itself to the Hub.</summary>
    public sealed class DemoSessionDescriptor
    {
        public const string FileName = "hapbeat-demo-session.json";

        public DemoSessionDescriptor(string demoId, DemoSessionLabel title, double? minutes, bool supportsHapticsToggle,
            IReadOnlyList<DemoSessionOption> options)
        {
            DemoId = demoId;
            Title = title;
            Minutes = minutes;
            SupportsHapticsToggle = supportsHapticsToggle;
            Options = options ?? Array.Empty<DemoSessionOption>();
        }

        public string DemoId { get; }
        public DemoSessionLabel Title { get; }
        public double? Minutes { get; }
        public bool SupportsHapticsToggle { get; }
        public IReadOnlyList<DemoSessionOption> Options { get; }

        public DemoSessionOption FindOption(string id) => Options.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.Ordinal));

        /// <summary>
        /// Whether <paramref name="option"/> is active for the given values. `when` refers to one other
        /// option; that option must itself be active and hold one of the listed values.
        /// </summary>
        public bool IsActive(DemoSessionOption option, IReadOnlyDictionary<string, string> values) => IsActive(option, values, 0);

        private bool IsActive(DemoSessionOption option, IReadOnlyDictionary<string, string> values, int depth)
        {
            if (option.WhenOptionId == null) return true;
            if (depth > Options.Count) return false; // A `when` cycle can never be satisfied.
            var reference = FindOption(option.WhenOptionId);
            if (reference == null || !IsActive(reference, values, depth + 1)) return false;
            var value = values != null && values.TryGetValue(reference.Id, out var held) && reference.HasValue(held) ? held : reference.Default;
            return option.WhenValues.Contains(value, StringComparer.Ordinal);
        }

        /// <summary>
        /// Returns exactly the active options: requested values when valid, otherwise the default.
        /// Unknown options, unknown values, missing active options and inactive options are reported
        /// in <paramref name="warnings"/>; the experience continues with defaults.
        /// </summary>
        public Dictionary<string, string> Normalize(IReadOnlyDictionary<string, string> requested, List<string> warnings)
        {
            requested = requested ?? new Dictionary<string, string>();
            var resolved = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var option in Options)
                resolved[option.Id] = requested.TryGetValue(option.Id, out var value) && option.HasValue(value) ? value : option.Default;

            var effective = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var option in Options)
            {
                var active = IsActive(option, resolved);
                var present = requested.TryGetValue(option.Id, out var value);
                if (!active)
                {
                    if (present) warnings?.Add("Option '" + option.Id + "' is inactive and was ignored.");
                    continue;
                }
                if (!present) warnings?.Add("Option '" + option.Id + "' is missing; using default '" + option.Default + "'.");
                else if (!option.HasValue(value)) warnings?.Add("Option '" + option.Id + "' has unknown value '" + value + "'; using default '" + option.Default + "'.");
                effective[option.Id] = resolved[option.Id];
            }
            foreach (var key in requested.Keys)
                if (FindOption(key) == null) warnings?.Add("Unknown option '" + key + "' was ignored.");
            return effective;
        }

        public static bool TryParse(string json, out DemoSessionDescriptor descriptor, out string error)
        {
            descriptor = null;
            if (!DemoSessionJson.TryLoad(json, out var root, out error)) return false;
            try
            {
                if (!DemoSessionJson.OnlyFields(root, "version", "demo_id", "title", "minutes", "supports", "options")) return Fail("Descriptor contains an unknown field.", out error);
                if (!DemoSessionJson.TryVersion(root)) return Fail("version must be 1.", out error);
                if (!DemoSessionJson.TryString(root, "demo_id", out var demoId) || !DemoSwitchProtocol.IsIdentifier(demoId)) return Fail("demo_id is invalid.", out error);
                if (!DemoSessionJson.TryLabel(root["title"], out var title)) return Fail("title is invalid.", out error);
                double? minutes = null;
                if (root.TryGetValue("minutes", StringComparison.Ordinal, out var minutesToken))
                {
                    if (minutesToken.Type != JTokenType.Integer && minutesToken.Type != JTokenType.Float) return Fail("minutes must be a number.", out error);
                    var m = minutesToken.Value<double>();
                    if (!(m > 0 && m <= 60)) return Fail("minutes must be in (0, 60].", out error);
                    minutes = m;
                }
                if (!(root["supports"] is JObject supports) || !DemoSessionJson.OnlyFields(supports, "haptics_toggle")
                    || !DemoSessionJson.TryBool(supports, "haptics_toggle", out var hapticsToggle)) return Fail("supports is invalid.", out error);
                if (!(root["options"] is JArray optionArray) || optionArray.Count > 8) return Fail("options must be an array of at most 8.", out error);
                var options = new List<DemoSessionOption>();
                foreach (var token in optionArray)
                {
                    if (!TryParseOption(token, out var option, out error)) return false;
                    if (options.Any(o => o.Id == option.Id)) return Fail("Duplicate option id '" + option.Id + "'.", out error);
                    options.Add(option);
                }
                foreach (var option in options)
                    if (option.WhenOptionId != null && (option.WhenOptionId == option.Id || options.All(o => o.Id != option.WhenOptionId)))
                        return Fail("Option '" + option.Id + "' has an unresolved when reference.", out error);
                descriptor = new DemoSessionDescriptor(demoId, title, minutes, hapticsToggle, options);
                error = null;
                return true;
            }
            catch (Exception exception) when (exception is JsonException || exception is FormatException || exception is InvalidCastException || exception is OverflowException)
            {
                return Fail(exception.Message, out error);
            }
        }

        private static bool TryParseOption(JToken token, out DemoSessionOption option, out string error)
        {
            option = null;
            if (!(token is JObject value) || !DemoSessionJson.OnlyFields(value, "id", "label", "values", "default", "when")) return Fail("Option is not an object with known fields.", out error);
            if (!DemoSessionJson.TryString(value, "id", out var id) || !DemoSwitchProtocol.IsIdentifier(id)) return Fail("Option id is invalid.", out error);
            if (!DemoSessionJson.TryLabel(value["label"], out var label)) return Fail("Option '" + id + "' label is invalid.", out error);
            if (!(value["values"] is JArray valueArray) || valueArray.Count < 1 || valueArray.Count > 8) return Fail("Option '" + id + "' values must have 1..8 items.", out error);
            var values = new List<DemoSessionOptionValue>();
            foreach (var item in valueArray)
            {
                if (!(item is JObject entry) || !DemoSessionJson.OnlyFields(entry, "value", "label")
                    || !DemoSessionJson.TryString(entry, "value", out var v) || !DemoSessionJson.IsValue(v)
                    || !DemoSessionJson.TryLabel(entry["label"], out var valueLabel)) return Fail("Option '" + id + "' has an invalid value.", out error);
                if (values.Any(x => x.Value == v)) return Fail("Option '" + id + "' has a duplicate value.", out error);
                values.Add(new DemoSessionOptionValue(v, valueLabel));
            }
            if (!DemoSessionJson.TryString(value, "default", out var defaultValue) || !DemoSessionJson.IsValue(defaultValue)) return Fail("Option '" + id + "' default is invalid.", out error);
            // Stricter than the schema: a default outside `values` could never be shown or cycled to.
            if (values.All(x => x.Value != defaultValue)) return Fail("Option '" + id + "' default is not one of its values.", out error);
            string whenId = null;
            List<string> whenValues = null;
            if (value.TryGetValue("when", StringComparison.Ordinal, out var whenToken))
            {
                if (!(whenToken is JObject when) || when.Count != 1) return Fail("Option '" + id + "' when must have exactly one property.", out error);
                var property = when.Properties().Single();
                if (!DemoSwitchProtocol.IsIdentifier(property.Name) || !(property.Value is JArray accepted) || accepted.Count < 1) return Fail("Option '" + id + "' when is invalid.", out error);
                whenValues = new List<string>();
                foreach (var item in accepted)
                {
                    if (item.Type != JTokenType.String || !DemoSessionJson.IsValue(item.Value<string>())) return Fail("Option '" + id + "' when value is invalid.", out error);
                    whenValues.Add(item.Value<string>());
                }
                whenId = property.Name;
            }
            option = new DemoSessionOption(id, label, values, defaultValue, whenId, whenValues);
            error = null;
            return true;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }

    public sealed class DemoSessionComponent
    {
        public DemoSessionComponent(string packageName, string activityName)
        {
            PackageName = packageName;
            ActivityName = activityName;
        }

        public string PackageName { get; }
        public string ActivityName { get; }
    }

    public sealed class DemoSessionStep
    {
        public DemoSessionStep(string demoId, string title, string packageName, string activityName,
            IReadOnlyDictionary<string, string> options, bool retry)
        {
            DemoId = demoId;
            Title = title;
            PackageName = packageName;
            ActivityName = activityName;
            Options = new Dictionary<string, string>(options ?? new Dictionary<string, string>(), StringComparer.Ordinal);
            Retry = retry;
        }

        public string DemoId { get; }
        public string Title { get; }
        public string PackageName { get; }
        public string ActivityName { get; }
        public IReadOnlyDictionary<string, string> Options { get; }
        public bool Retry { get; }
    }

    /// <summary>Look of the shared tracked hands (<see cref="DemoHands"/>); the ticket's optional `hand_style`.</summary>
    public enum DemoHandStyle { Ghost, Skin }

    public static class DemoHandStyles
    {
        public const string Ghost = "ghost";
        public const string Skin = "skin";

        public static string ToValue(DemoHandStyle style) => style == DemoHandStyle.Skin ? Skin : Ghost;

        public static bool TryParse(string value, out DemoHandStyle style)
        {
            style = DemoHandStyle.Ghost;
            if (string.Equals(value, Ghost, StringComparison.Ordinal)) return true;
            if (!string.Equals(value, Skin, StringComparison.Ordinal)) return false;
            style = DemoHandStyle.Skin;
            return true;
        }
    }

    /// <summary>Immutable session ticket carried between runtimes in the Intent extra.</summary>
    public sealed class DemoSessionTicket
    {
        public const int MaxBytes = 16384;
        public const int MaxSteps = 32;

        public DemoSessionTicket(string sessionId, int index, bool hapticsUi, IReadOnlyList<DemoSessionStep> steps, DemoSessionComponent finish,
            DemoHandStyle? handStyle = null)
        {
            SessionId = sessionId;
            Index = index;
            HapticsUi = hapticsUi;
            Steps = steps.ToArray();
            Finish = finish;
            HandStyle = handStyle;
        }

        public string SessionId { get; }
        public int Index { get; }
        public bool HapticsUi { get; }
        public IReadOnlyList<DemoSessionStep> Steps { get; }
        public DemoSessionComponent Finish { get; }
        /// <summary>`hand_style`; null when omitted (each runtime uses its own default).</summary>
        public DemoHandStyle? HandStyle { get; }

        /// <summary>`index == len(steps)`: every step completed; only the finish runtime receives it.</summary>
        public bool IsFinished => Index == Steps.Count;

        public DemoSessionTicket WithIndex(int index, bool hapticsUi) => new DemoSessionTicket(SessionId, index, hapticsUi, Steps, Finish, HandStyle);
        public DemoSessionTicket WithSession(string sessionId) => new DemoSessionTicket(sessionId, Index, HapticsUi, Steps, Finish, HandStyle);
        public DemoSessionTicket WithHandStyle(DemoHandStyle? handStyle) => new DemoSessionTicket(SessionId, Index, HapticsUi, Steps, Finish, handStyle);

        public static string NewSessionId()
        {
            var bytes = new byte[8];
            using (var random = System.Security.Cryptography.RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return string.Concat(bytes.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }

        public string ToJson()
        {
            var steps = new JArray();
            foreach (var step in Steps)
            {
                var options = new JObject();
                foreach (var pair in step.Options.OrderBy(p => p.Key, StringComparer.Ordinal)) options[pair.Key] = pair.Value;
                steps.Add(new JObject
                {
                    ["demo_id"] = step.DemoId,
                    ["title"] = step.Title,
                    ["package"] = step.PackageName,
                    ["activity"] = step.ActivityName,
                    ["options"] = options,
                    ["retry"] = step.Retry
                });
            }
            var root = new JObject
            {
                ["version"] = 1,
                ["session_id"] = SessionId,
                ["index"] = Index,
                ["haptics_ui"] = HapticsUi,
                ["steps"] = steps,
                ["finish"] = new JObject { ["package"] = Finish.PackageName, ["activity"] = Finish.ActivityName }
            };
            if (HandStyle.HasValue) root["hand_style"] = DemoHandStyles.ToValue(HandStyle.Value);
            return root.ToString(Formatting.None);
        }

        /// <summary>Schema-equivalent validation plus the 16384-byte limit and `index <= len(steps)`.</summary>
        public static bool TryParse(string json, out DemoSessionTicket ticket, out string error)
        {
            ticket = null;
            if (!DemoSessionJson.TryLoad(json, out var root, out error)) return false;
            try
            {
                if (!DemoSessionJson.OnlyFields(root, "version", "session_id", "index", "haptics_ui", "steps", "finish", "hand_style")) return Fail("Ticket contains an unknown field.", out error);
                if (!DemoSessionJson.TryVersion(root)) return Fail("version must be 1.", out error);
                if (!DemoSessionJson.TryString(root, "session_id", out var sessionId) || !DemoSessionJson.IsSessionId(sessionId)) return Fail("session_id is invalid.", out error);
                if (!root.TryGetValue("index", StringComparison.Ordinal, out var indexToken) || indexToken.Type != JTokenType.Integer) return Fail("index must be an integer.", out error);
                var index = indexToken.Value<long>();
                if (index < 0 || index > MaxSteps) return Fail("index is outside 0..32.", out error);
                if (!DemoSessionJson.TryBool(root, "haptics_ui", out var hapticsUi)) return Fail("haptics_ui must be a boolean.", out error);
                if (!(root["steps"] is JArray stepArray) || stepArray.Count < 1 || stepArray.Count > MaxSteps) return Fail("steps must have 1..32 items.", out error);
                var steps = new List<DemoSessionStep>();
                foreach (var token in stepArray)
                {
                    if (!TryParseStep(token, out var step, out error)) return false;
                    steps.Add(step);
                }
                if (!TryParseComponent(root["finish"], out var finish)) return Fail("finish is invalid.", out error);
                if (index > steps.Count) return Fail("index exceeds the number of steps.", out error);
                DemoHandStyle? handStyle = null;
                if (root.ContainsKey("hand_style"))
                {
                    if (!DemoSessionJson.TryString(root, "hand_style", out var handValue) || !DemoHandStyles.TryParse(handValue, out var parsedStyle))
                        return Fail("hand_style must be \"ghost\" or \"skin\".", out error);
                    handStyle = parsedStyle;
                }
                ticket = new DemoSessionTicket(sessionId, (int)index, hapticsUi, steps, finish, handStyle);
                error = null;
                return true;
            }
            catch (Exception exception) when (exception is JsonException || exception is FormatException || exception is InvalidCastException || exception is OverflowException)
            {
                return Fail(exception.Message, out error);
            }
        }

        private static bool TryParseStep(JToken token, out DemoSessionStep step, out string error)
        {
            step = null;
            if (!(token is JObject value) || !DemoSessionJson.OnlyFields(value, "demo_id", "title", "package", "activity", "options", "retry")) return Fail("Step is not an object with known fields.", out error);
            if (!DemoSessionJson.TryString(value, "demo_id", out var demoId) || !DemoSwitchProtocol.IsIdentifier(demoId)) return Fail("Step demo_id is invalid.", out error);
            if (!DemoSessionJson.TryString(value, "title", out var title) || !DemoSessionJson.LengthBetween(title, 1, 40)) return Fail("Step title must be 1..40 characters.", out error);
            if (!DemoSessionJson.TryString(value, "package", out var package) || !DemoSessionJson.IsJavaName(package)) return Fail("Step package is invalid.", out error);
            if (!DemoSessionJson.TryString(value, "activity", out var activity) || !DemoSessionJson.IsJavaName(activity)) return Fail("Step activity is invalid.", out error);
            if (!(value["options"] is JObject optionObject) || optionObject.Count > 8) return Fail("Step options must be an object of at most 8 entries.", out error);
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in optionObject.Properties())
            {
                if (!DemoSwitchProtocol.IsIdentifier(property.Name) || property.Value.Type != JTokenType.String || !DemoSessionJson.IsValue(property.Value.Value<string>()))
                    return Fail("Step option '" + property.Name + "' is invalid.", out error);
                options[property.Name] = property.Value.Value<string>();
            }
            if (!DemoSessionJson.TryBool(value, "retry", out var retry)) return Fail("Step retry must be a boolean.", out error);
            step = new DemoSessionStep(demoId, title, package, activity, options, retry);
            error = null;
            return true;
        }

        private static bool TryParseComponent(JToken token, out DemoSessionComponent component)
        {
            component = null;
            if (!(token is JObject value) || !DemoSessionJson.OnlyFields(value, "package", "activity")
                || !DemoSessionJson.TryString(value, "package", out var package) || !DemoSessionJson.IsJavaName(package)
                || !DemoSessionJson.TryString(value, "activity", out var activity) || !DemoSessionJson.IsJavaName(activity)) return false;
            component = new DemoSessionComponent(package, activity);
            return true;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }

    internal static class DemoSessionJson
    {
        private static readonly Regex ValuePattern = new Regex(@"^[a-z0-9][a-z0-9._-]{0,31}\z", RegexOptions.CultureInvariant);
        private static readonly Regex JavaNamePattern = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)+\z", RegexOptions.CultureInvariant);
        private static readonly Regex SessionIdPattern = new Regex(@"^[0-9a-f]{16}\z", RegexOptions.CultureInvariant);

        public static bool TryLoad(string json, out JObject root, out string error)
        {
            root = null;
            if (string.IsNullOrEmpty(json)) { error = "JSON is empty."; return false; }
            if (Encoding.UTF8.GetByteCount(json) > DemoSessionTicket.MaxBytes) { error = "JSON exceeds 16384 bytes."; return false; }
            try
            {
                root = DemoSwitchProtocol.ParseStrictObject(json);
                error = null;
                return true;
            }
            catch (Exception exception) when (exception is JsonException || exception is FormatException || exception is InvalidCastException)
            {
                error = exception.Message;
                return false;
            }
        }

        public static bool OnlyFields(JObject value, params string[] names) => value.Properties().All(p => names.Contains(p.Name, StringComparer.Ordinal));

        public static bool TryVersion(JObject value) => value.TryGetValue("version", StringComparison.Ordinal, out var token)
            && token.Type == JTokenType.Integer && token.Value<long>() == 1;

        public static bool TryString(JObject value, string name, out string result)
        {
            result = null;
            if (!value.TryGetValue(name, StringComparison.Ordinal, out var token) || token.Type != JTokenType.String) return false;
            result = token.Value<string>();
            return result != null;
        }

        public static bool TryBool(JObject value, string name, out bool result)
        {
            result = false;
            if (!value.TryGetValue(name, StringComparison.Ordinal, out var token) || token.Type != JTokenType.Boolean) return false;
            result = token.Value<bool>();
            return true;
        }

        public static bool TryLabel(JToken token, out DemoSessionLabel label)
        {
            label = null;
            if (!(token is JObject value) || !OnlyFields(value, "ja", "en")) return false;
            if (!TryString(value, "ja", out var ja) || !LengthBetween(ja, 1, 40)) return false;
            string en = null;
            if (value.ContainsKey("en") && (!TryString(value, "en", out en) || !LengthBetween(en, 1, 60))) return false;
            label = new DemoSessionLabel(ja, en);
            return true;
        }

        public static bool IsValue(string value) => value != null && ValuePattern.IsMatch(value);
        public static bool IsJavaName(string value) => value != null && value.Length <= 200 && JavaNamePattern.IsMatch(value);
        public static bool IsSessionId(string value) => value != null && SessionIdPattern.IsMatch(value);

        /// <summary>JSON Schema lengths count Unicode code points, not UTF-16 units.</summary>
        public static bool LengthBetween(string value, int minimum, int maximum)
        {
            var length = CodePointLength(value);
            return length >= minimum && length <= maximum;
        }

        public static int CodePointLength(string value)
        {
            var length = 0;
            for (var index = 0; index < value.Length; index++)
            {
                if (char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1])) index++;
                length++;
            }
            return length;
        }
    }
}
