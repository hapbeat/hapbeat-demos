using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Hapbeat.DemoSwitch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hapbeat.DemoHub
{
    /// <summary>
    /// Operator settings from the manage screen: which presets and demos the top screen offers, the
    /// initial `haptics_ui` of every launch, the M5 staff waiting mode, the shared hands' look
    /// (`hand_style` of every launch and the Hub's own hands) and the narration volume of every launch.
    /// Stored as hub-settings.json.
    /// </summary>
    public sealed class HubSettings
    {
        public HashSet<int> VisiblePresets { get; } = new HashSet<int>();
        public HashSet<string> VisibleDemos { get; } = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Initial `haptics_ui` for every launch from the Hub (default hidden).</summary>
        public bool HapticsUi { get; set; }
        /// <summary>Head-locked "staff will start" labels instead of the launcher (M5 operation).</summary>
        public bool StaffWaiting { get; set; }
        /// <summary>Look of the shared hands: the Hub's own and every ticket's `hand_style` (default ghost).</summary>
        public DemoHandStyle HandStyle { get; set; } = DemoHandStyle.Ghost;
        /// <summary>Initial `recenter_ui` of every launch and the Hub's own 視線をリセット button (default hidden).</summary>
        public bool RecenterUi { get; set; }
        /// <summary>`narration_volume` of every launch: 0..100 in steps of 10 (default 50, <see cref="DemoNarration"/>).</summary>
        public int NarrationVolume { get; set; } = DemoSwitchSettings.DefaultNarrationVolume;
        /// <summary>Increased on every save of the settings (<see cref="HubPlanStore.SaveSettings"/>): manage screen, HUB_SETTINGS_SET, a preset's `visible`.</summary>
        public long Revision { get; set; }
        /// <summary>
        /// DEMO_SETTINGS `hub_id`: 16 lowercase hex made once at the Hub's first start (<see cref="HubPlanStore.LoadSettings"/>) and
        /// kept here; null only before that.
        /// </summary>
        public string HubId { get; set; }

        /// <summary>
        /// The ticket's `narration_volume`: omitted at the default 50, because a demo built before the narration volume
        /// (package before 0.1.0-d22) rejects a ticket with an unknown field; any other value is sent.
        /// </summary>
        public int? TicketNarrationVolume => NarrationVolume == DemoSwitchSettings.DefaultNarrationVolume ? (int?)null : NarrationVolume;

        public string ToJson() => new JObject
        {
            ["version"] = 1,
            ["revision"] = Revision,
            ["visible_presets"] = new JArray(VisiblePresets.OrderBy(n => n).Cast<object>().ToArray()),
            ["visible_demos"] = new JArray(VisibleDemos.OrderBy(d => d, StringComparer.Ordinal).Cast<object>().ToArray()),
            ["haptics_ui"] = HapticsUi,
            ["staff_waiting"] = StaffWaiting,
            ["hand_style"] = DemoHandStyles.ToValue(HandStyle),
            ["recenter_ui"] = RecenterUi,
            ["narration_volume"] = NarrationVolume,
            ["hub_id"] = HubId
        }.ToString(Formatting.Indented);

        public static bool TryFromJson(string json, out HubSettings settings)
        {
            settings = null;
            try
            {
                var root = JObject.Parse(json);
                if (root.Value<int?>("version") != 1) return false;
                var result = new HubSettings
                {
                    HapticsUi = root.Value<bool?>("haptics_ui") ?? false,
                    StaffWaiting = root.Value<bool?>("staff_waiting") ?? false,
                    HandStyle = DemoHandStyles.TryParse(root.Value<string>("hand_style"), out var handStyle) ? handStyle : DemoHandStyle.Ghost,
                    RecenterUi = root.Value<bool?>("recenter_ui") ?? false,
                    // Files from before the narration volume, or an invalid value: the default.
                    NarrationVolume = root["narration_volume"] is JValue volume && volume.Type == JTokenType.Integer
                        && DemoNarration.IsValidVolume(volume.Value<long>()) ? volume.Value<int>() : DemoSwitchSettings.DefaultNarrationVolume
                };
                if (root["hub_id"] is JValue hubId && hubId.Type == JTokenType.String && DemoSwitchHub.IsHubId(hubId.Value<string>()))
                    result.HubId = hubId.Value<string>();
                // Files from before the revision load as revision 0.
                if (root["revision"] is JValue revision && revision.Type == JTokenType.Integer
                    && long.TryParse(revision.ToString(Formatting.None), out var number) && number >= 0)
                    result.Revision = number;
                if (root["visible_presets"] is JArray presets)
                    foreach (var token in presets)
                        if (token.Type == JTokenType.Integer && token.Value<int>() >= 1 && token.Value<int>() <= HubPlanStore.PresetCount)
                            result.VisiblePresets.Add(token.Value<int>());
                if (root["visible_demos"] is JArray demos)
                    foreach (var token in demos)
                        if (token.Type == JTokenType.String) result.VisibleDemos.Add(token.Value<string>());
                settings = result;
                return true;
            }
            catch (Exception exception) when (exception is JsonException || exception is InvalidCastException || exception is FormatException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// The per-demo settings (contracts demo-session.md "デモごとの設定"): for each demo, the value of every descriptor option
    /// (both scopes), used by every launch of that demo from this Hub. Changed on the manage screen's デモのタイル tab and with
    /// DEMO_SETTINGS_SET; stored as demo-settings.json. A value the descriptor no longer has is the default again, with a
    /// warning; a demo whose DEMO_SETTINGS could exceed 1024 bytes (<see cref="DemoSwitchHub.FitsDemoSettings"/>) is kept out
    /// and always launches with its descriptor defaults. Values of demos that are not installed are kept.
    /// </summary>
    public sealed class HubDemoSettings
    {
        private readonly Dictionary<string, Dictionary<string, string>> _values =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        /// <summary>
        /// Every option of <paramref name="descriptor"/>: the stored value when it is one of the option's values, else the
        /// default (only defaults for a descriptor kept out of the per-demo settings).
        /// </summary>
        public Dictionary<string, string> Values(DemoSessionDescriptor descriptor)
        {
            var stored = DemoSwitchHub.FitsDemoSettings(descriptor) && _values.TryGetValue(descriptor.DemoId, out var held) ? held : null;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var option in descriptor.Options)
                result[option.Id] = stored != null && stored.TryGetValue(option.Id, out var value) && option.HasValue(value) ? value : option.Default;
            return result;
        }

        /// <summary>Stores the given values of one demo; its other options keep theirs.</summary>
        public void Set(string demoId, IReadOnlyDictionary<string, string> values)
        {
            if (!_values.TryGetValue(demoId, out var stored)) _values[demoId] = stored = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in values) stored[pair.Key] = pair.Value;
        }

        /// <summary>A copy, to restore when a save fails.</summary>
        public HubDemoSettings Clone()
        {
            var copy = new HubDemoSettings();
            foreach (var pair in _values) copy.Set(pair.Key, pair.Value);
            return copy;
        }

        /// <summary>
        /// Once the catalog is known: logs and drops stored values that the installed descriptors no longer have (an unknown
        /// option or value is the default again), and warns about every descriptor kept out of the per-demo settings.
        /// </summary>
        public void Check(IReadOnlyList<DemoSessionCatalogEntry> catalog)
        {
            foreach (var entry in catalog)
            {
                var descriptor = entry.Descriptor;
                if (!DemoSwitchHub.FitsDemoSettings(descriptor))
                {
                    Debug.LogWarning("[Demo Hub] " + descriptor.DemoId + ": its DEMO_SETTINGS could exceed 1024 bytes (long option IDs or values); " +
                        "it has no per-demo settings and launches with its descriptor defaults.");
                    continue;
                }
                if (!_values.TryGetValue(descriptor.DemoId, out var stored)) continue;
                foreach (var pair in stored.ToList())
                {
                    var option = descriptor.FindOption(pair.Key);
                    if (option != null && option.HasValue(pair.Value)) continue;
                    Debug.LogWarning("[Demo Hub] " + descriptor.DemoId + ": the saved setting " + pair.Key + "=" + pair.Value +
                        " is not in its descriptor; " + (option == null ? "it is dropped." : "the default " + option.Default + " is used."));
                    stored.Remove(pair.Key);
                }
            }
        }

        private static bool IsIdentifier(string value) =>
            value != null && System.Text.RegularExpressions.Regex.IsMatch(value, @"^[a-z0-9][a-z0-9._-]{0,63}\z");

        public string ToJson()
        {
            var demos = new JObject();
            foreach (var demo in _values.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var options = new JObject();
                foreach (var pair in demo.Value.OrderBy(p => p.Key, StringComparer.Ordinal)) options[pair.Key] = pair.Value;
                demos[demo.Key] = options;
            }
            return new JObject { ["version"] = 1, ["demos"] = demos }.ToString(Formatting.Indented);
        }

        public static bool TryFromJson(string json, out HubDemoSettings demoSettings)
        {
            demoSettings = null;
            try
            {
                var root = JObject.Parse(json);
                if (root.Value<int?>("version") != 1 || !(root["demos"] is JObject demos)) return false;
                var result = new HubDemoSettings();
                foreach (var demo in demos.Properties())
                {
                    if (!IsIdentifier(demo.Name) || !(demo.Value is JObject options)) continue;
                    var values = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var option in options.Properties())
                        if (IsIdentifier(option.Name) && option.Value.Type == JTokenType.String) values[option.Name] = option.Value.Value<string>();
                    result.Set(demo.Name, values);
                }
                demoSettings = result;
                return true;
            }
            catch (Exception exception) when (exception is JsonException || exception is InvalidCastException || exception is FormatException)
            {
                return false;
            }
        }
    }

    /// <summary>What the top screen offers: shown presets (1-based numbers) and demo tiles, in display order.</summary>
    public sealed class HubTopItems
    {
        private HubTopItems(IReadOnlyList<int> presets, IReadOnlyList<DemoSessionCatalogEntry> demos)
        {
            Presets = presets;
            Demos = demos;
        }

        public IReadOnlyList<int> Presets { get; }
        public IReadOnlyList<DemoSessionCatalogEntry> Demos { get; }
        public bool IsEmpty => Presets.Count == 0 && Demos.Count == 0;

        /// <summary>
        /// A preset is shown when it is marked visible and has at least one installed step; a demo
        /// tile when it is marked visible and installed (in the catalog). Tiles keep catalog order.
        /// </summary>
        /// <param name="presets">Preset plans; index 0 is preset 1.</param>
        public static HubTopItems Decide(HubSettings settings, IReadOnlyList<HubPlan> presets, IReadOnlyList<DemoSessionCatalogEntry> catalog)
        {
            var shownPresets = new List<int>();
            for (var number = 1; number <= presets.Count; number++)
                if (settings.VisiblePresets.Contains(number) && presets[number - 1].Steps.Any(s => HubPlan.Find(catalog, s.DemoId) != null))
                    shownPresets.Add(number);
            var demos = catalog.Where(e => settings.VisibleDemos.Contains(e.Descriptor.DemoId)).ToList();
            return new HubTopItems(shownPresets, demos);
        }
    }

    /// <summary>Long press: fires once after <see cref="Duration"/> (default <see cref="Seconds"/>) of continuous holding; releasing resets.</summary>
    public sealed class HubHoldGesture
    {
        public const float Seconds = 1f;
        private float _since = -1f;
        private bool _fired;

        public HubHoldGesture(float duration = Seconds)
        {
            Duration = duration;
        }

        public float Duration { get; }

        /// <summary>0..1 while held, 0 when released.</summary>
        public float Progress { get; private set; }

        public bool Update(bool held, float now)
        {
            if (!held)
            {
                Reset();
                return false;
            }
            if (_since < 0f) _since = now;
            Progress = Mathf.Clamp01((now - _since) / Duration);
            if (_fired || Progress < 1f) return false;
            _fired = true;
            return true;
        }

        public void Reset()
        {
            _since = -1f;
            _fired = false;
            Progress = 0f;
        }
    }

    /// <summary>
    /// External start (contracts demo-session.md "Hub を外部から起動してセッションを始める"): the launch Intent's String
    /// extra <see cref="Extra"/>, one JSON object of at most <see cref="MaxBytes"/> UTF-8 bytes: <c>{"version":1,"preset":1}</c>
    /// (preset 1..3), <c>{"version":1,"demo_id":"handdemo","options":{...}}</c> (options optional, string values) or
    /// <c>{"version":1,"steps":[{"demo_id":"handdemo","options":{...},"retry":false}, ...]}</c> (an external plan of 1..32
    /// steps; each step has demo_id, optional options and optional retry, default true). Other fields, and anything but
    /// exactly one of preset / demo_id / steps, are invalid. An external plan is never saved.
    /// </summary>
    public sealed class HubStartRequest
    {
        public const string Extra = "com.hapbeat.demo_hub.start";
        public const int MaxBytes = 4096;
        private static readonly string[] Fields = { "version", "preset", "demo_id", "options", "steps" };
        private static readonly string[] StepFields = { "demo_id", "options", "retry" };

        private HubStartRequest(int preset, string demoId, IReadOnlyDictionary<string, string> options, IReadOnlyList<HubPlanStep> steps)
        {
            Preset = preset;
            DemoId = demoId;
            Options = options;
            Steps = steps;
        }

        /// <summary>1..3, or 0 for a single demo or an external plan.</summary>
        public int Preset { get; }
        /// <summary>The single demo, or null for a preset or an external plan.</summary>
        public string DemoId { get; }
        public IReadOnlyDictionary<string, string> Options { get; }
        /// <summary>The external plan's steps as given (options not yet checked against the descriptors), or null.</summary>
        public IReadOnlyList<HubPlanStep> Steps { get; }

        public static bool TryParse(string json, out HubStartRequest request, out string error)
        {
            request = null;
            if (string.IsNullOrEmpty(json) || Encoding.UTF8.GetByteCount(json) > MaxBytes) { error = "empty or over 4096 bytes"; return false; }
            try
            {
                JObject root;
                using (var reader = new JsonTextReader(new System.IO.StringReader(json)) { DateParseHandling = DateParseHandling.None })
                {
                    root = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) { error = "trailing content"; return false; }
                }
                var unknown = root.Properties().FirstOrDefault(p => !Fields.Contains(p.Name, StringComparer.Ordinal));
                if (unknown != null) { error = "unknown field " + unknown.Name; return false; }
                if (!(root["version"] is JValue version) || version.Type != JTokenType.Integer || version.Value<long>() != 1) { error = "version must be 1"; return false; }
                var hasPreset = root["preset"] != null;
                var hasDemo = root["demo_id"] != null;
                var hasSteps = root["steps"] != null;
                if ((hasPreset ? 1 : 0) + (hasDemo ? 1 : 0) + (hasSteps ? 1 : 0) != 1) { error = "give exactly one of preset, demo_id or steps"; return false; }
                if (hasPreset)
                {
                    if (root["options"] != null) { error = "options belong to demo_id"; return false; }
                    var preset = root["preset"];
                    if (preset.Type != JTokenType.Integer || preset.Value<long>() < 1 || preset.Value<long>() > HubPlanStore.PresetCount)
                    { error = "preset must be 1.." + HubPlanStore.PresetCount; return false; }
                    request = new HubStartRequest(preset.Value<int>(), null, new Dictionary<string, string>(StringComparer.Ordinal), null);
                    error = null;
                    return true;
                }
                if (hasSteps)
                {
                    if (root["options"] != null) { error = "options belong to each step"; return false; }
                    if (!(root["steps"] is JArray stepArray) || stepArray.Count < 1 || stepArray.Count > HubPlan.MaxSteps)
                    { error = "steps must be an array of 1.." + HubPlan.MaxSteps; return false; }
                    var steps = new List<HubPlanStep>();
                    for (var index = 0; index < stepArray.Count; index++)
                    {
                        if (!TryParseStep(stepArray[index], out var step, out error))
                        {
                            error = StepPrefix(index) + error;
                            return false;
                        }
                        steps.Add(step);
                    }
                    request = new HubStartRequest(0, null, new Dictionary<string, string>(StringComparer.Ordinal), steps);
                    error = null;
                    return true;
                }
                if (!TryParseDemo(root["demo_id"], root["options"], out var demoId, out var options, out error)) return false;
                request = new HubStartRequest(0, demoId, options, null);
                error = null;
                return true;
            }
            catch (Exception exception) when (exception is JsonException || exception is InvalidCastException || exception is FormatException || exception is OverflowException)
            {
                error = "not a JSON object";
                return false;
            }
        }

        /// <summary>
        /// The external plan of a Demo Switch HUB_START (steps already parsed and checked by the receiver), started like the
        /// start extra `steps`.
        /// </summary>
        public static HubStartRequest FromSteps(IReadOnlyList<DemoSwitchPresetStep> steps) =>
            new HubStartRequest(0, null, new Dictionary<string, string>(StringComparer.Ordinal), steps.Select(step => new HubPlanStep(step.DemoId,
                step.Options.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal), step.Retry)).ToList());

        /// <summary>One external plan step: only demo_id, options (optional) and retry (optional boolean, default true).</summary>
        private static bool TryParseStep(JToken token, out HubPlanStep step, out string error)
        {
            step = null;
            if (!(token is JObject stepObject)) { error = "step must be an object"; return false; }
            var unknown = stepObject.Properties().FirstOrDefault(p => !StepFields.Contains(p.Name, StringComparer.Ordinal));
            if (unknown != null) { error = "unknown field " + unknown.Name; return false; }
            if (stepObject["demo_id"] == null) { error = "demo_id is missing"; return false; }
            if (!TryParseDemo(stepObject["demo_id"], stepObject["options"], out var demoId, out var options, out error)) return false;
            var retry = true;
            if (stepObject["retry"] != null)
            {
                if (stepObject["retry"].Type != JTokenType.Boolean) { error = "retry must be true or false"; return false; }
                retry = stepObject["retry"].Value<bool>();
            }
            step = new HubPlanStep(demoId, options, retry);
            error = null;
            return true;
        }

        /// <summary>A demo_id (identifier) and its optional options object of string values.</summary>
        private static bool TryParseDemo(JToken demoIdToken, JToken optionsToken, out string demoId, out Dictionary<string, string> options, out string error)
        {
            demoId = null;
            options = null;
            if (demoIdToken.Type != JTokenType.String || !IsIdentifier(demoIdToken.Value<string>())) { error = "demo_id is invalid"; return false; }
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (optionsToken != null)
            {
                if (!(optionsToken is JObject optionObject)) { error = "options must be an object"; return false; }
                foreach (var property in optionObject.Properties())
                {
                    if (property.Value.Type != JTokenType.String) { error = "option " + property.Name + " must be a string"; return false; }
                    result[property.Name] = property.Value.Value<string>();
                }
            }
            demoId = demoIdToken.Value<string>();
            options = result;
            error = null;
            return true;
        }

        /// <summary>"ステップ 2: " for the step at <paramref name="index"/> (0-based), so the status line names the step.</summary>
        private static string StepPrefix(int index) => string.Format(CultureInfo.InvariantCulture, HubText.StartStep, index + 1);

        /// <summary>The Demo Switch identifier rule (<c>[a-z0-9][a-z0-9._-]{0,63}</c>).</summary>
        private static bool IsIdentifier(string value) =>
            value != null && System.Text.RegularExpressions.Regex.IsMatch(value, @"^[a-z0-9][a-z0-9._-]{0,63}\z");

        /// <summary>
        /// The session's ticket, as the top screen would start it: a preset's installed steps, one step of
        /// <see cref="DemoId"/> with the given options and retry on, or the external plan's <see cref="Steps"/> with their
        /// options and retry (unknown options and values are left out with a warning; an option a step does not give is the
        /// descriptor default, while for <see cref="DemoId"/> it comes from the per-demo settings <paramref name="demoSettings"/>,
        /// which also decide every `scope: device` option); finish = this Hub,
        /// and the manage screen's haptics UI, 視線をリセット, hand style and narration volume. Null with <paramref name="error"/> (shown on the
        /// top screen) when nothing installed can start, or when any external plan step is not installed.
        /// </summary>
        public DemoSessionTicket BuildTicket(IReadOnlyList<HubPlan> presets, IReadOnlyList<DemoSessionCatalogEntry> catalog,
            DemoSessionComponent finish, string sessionId, HubSettings settings, HubDemoSettings demoSettings, out string error)
        {
            HubPlan plan;
            if (Steps != null)
            {
                plan = new HubPlan();
                for (var index = 0; index < Steps.Count; index++)
                {
                    var given = Steps[index];
                    var entry = HubPlan.Find(catalog, given.DemoId);
                    if (entry == null)
                    {
                        error = StepPrefix(index) + HubText.StartNotInstalled + given.DemoId;
                        return null;
                    }
                    var step = new HubPlanStep(given.DemoId, null, given.Retry);
                    plan.Steps.Add(step);
                    ApplyOptions(step, entry.Descriptor, given.Options);
                }
                error = null;
            }
            else if (DemoId == null)
            {
                plan = presets[Preset - 1];
                error = string.Format(CultureInfo.InvariantCulture, HubText.StartPresetEmpty, Preset);
            }
            else
            {
                var entry = HubPlan.Find(catalog, DemoId);
                if (entry == null)
                {
                    error = HubText.StartNotInstalled + DemoId;
                    return null;
                }
                plan = HubPlan.Single(entry.Descriptor, demoSettings);
                ApplyOptions(plan.Steps[0], entry.Descriptor, Options);
                error = HubText.StartNotInstalled + DemoId;
            }
            var ticket = plan.BuildTicket(catalog, finish, sessionId, settings.HapticsUi, settings.HandStyle, settings.RecenterUi,
                settings.TicketNarrationVolume, demoSettings);
            if (ticket != null) error = null;
            return ticket;
        }

        /// <summary>Sets the given options on <paramref name="step"/>; an unknown option or value keeps what the step had, with a warning.</summary>
        private static void ApplyOptions(HubPlanStep step, DemoSessionDescriptor descriptor, IReadOnlyDictionary<string, string> options)
        {
            foreach (var pair in options)
            {
                var option = descriptor.FindOption(pair.Key);
                if (option == null || !option.HasValue(pair.Value))
                {
                    Debug.LogWarning("[Demo Hub] External start: option " + pair.Key + "=" + pair.Value + " is unknown for " + step.DemoId + "; it is ignored.");
                    continue;
                }
                step.Options[pair.Key] = pair.Value;
            }
        }
    }

    /// <summary>
    /// Where the panel is placed: once, when head tracking first becomes valid, and again only when the
    /// 視線をリセット is pressed (or CONTROL `recenter`) or after a system recenter. It never follows the head.
    /// </summary>
    public static class HubPanelPlacement
    {
        public const float Distance = 0.6f;
        /// <summary>Panel centre below eye height, metres.</summary>
        public const float Drop = 0.18f;

        /// <summary>In front of the head's heading (pitch and roll ignored), facing the head.</summary>
        public static Pose Target(Vector3 headPosition, Vector3 headForward, Vector3 headUp)
        {
            var heading = Vector3.ProjectOnPlane(headForward, Vector3.up);
            // Looking straight down: the top of the head points the way the user faces.
            if (heading.sqrMagnitude < 0.0001f) heading = Vector3.ProjectOnPlane(headUp, Vector3.up);
            if (heading.sqrMagnitude < 0.0001f) heading = Vector3.forward;
            var position = headPosition + heading.normalized * Distance + Vector3.down * Drop;
            return new Pose(position, Quaternion.LookRotation(position - headPosition, Vector3.up));
        }
    }
}
