using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Hapbeat.DemoSwitch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hapbeat.DemoHub
{
    public sealed class HubPlanStep
    {
        public HubPlanStep(string demoId, Dictionary<string, string> options, bool retry)
        {
            DemoId = demoId;
            Options = options ?? new Dictionary<string, string>(StringComparer.Ordinal);
            Retry = retry;
        }

        public string DemoId { get; }
        /// <summary>Every option value chosen so far, including currently inactive ones (kept for when they reappear).</summary>
        public Dictionary<string, string> Options { get; }
        public bool Retry { get; set; }
    }

    /// <summary>
    /// A preset's editable plan. The stored format is internal to the Hub (contract: plan format is
    /// implementation-defined); only <see cref="BuildTicket"/> produces contract data.
    /// </summary>
    public sealed class HubPlan
    {
        public const int MaxSteps = DemoSessionTicket.MaxSteps;
        public const int TitleMaxLength = 40;
        private static readonly System.Text.RegularExpressions.Regex IdentifierPattern =
            new System.Text.RegularExpressions.Regex(@"^[a-z0-9][a-z0-9._-]{0,63}\z");

        public const int SummaryMaxLength = 60;

        public List<HubPlanStep> Steps { get; } = new List<HubPlanStep>();

        /// <summary>One-step plan for a demo tile: descriptor default options, retry offered.</summary>
        public static HubPlan Single(DemoSessionDescriptor descriptor)
        {
            var plan = new HubPlan();
            plan.Add(descriptor);
            return plan;
        }

        public bool Add(DemoSessionDescriptor descriptor)
        {
            if (Steps.Count >= MaxSteps) return false;
            var options = descriptor.Options.ToDictionary(o => o.Id, o => o.Default, StringComparer.Ordinal);
            Steps.Add(new HubPlanStep(descriptor.DemoId, options, true));
            return true;
        }

        public bool MoveUp(int index)
        {
            if (index <= 0 || index >= Steps.Count) return false;
            (Steps[index - 1], Steps[index]) = (Steps[index], Steps[index - 1]);
            return true;
        }

        public bool MoveDown(int index)
        {
            if (index < 0 || index >= Steps.Count - 1) return false;
            (Steps[index + 1], Steps[index]) = (Steps[index], Steps[index + 1]);
            return true;
        }

        public bool Remove(int index)
        {
            if (index < 0 || index >= Steps.Count) return false;
            Steps.RemoveAt(index);
            return true;
        }

        public void ToggleRetry(int index) => Steps[index].Retry = !Steps[index].Retry;

        /// <summary>Advances an active option to its next value, wrapping around.</summary>
        public bool CycleOption(int index, DemoSessionDescriptor descriptor, string optionId)
        {
            var step = Steps[index];
            var option = descriptor.FindOption(optionId);
            if (option == null || !descriptor.IsActive(option, step.Options)) return false;
            var current = Value(step, option);
            var position = option.Values.ToList().FindIndex(v => v.Value == current);
            step.Options[option.Id] = option.Values[(position + 1) % option.Values.Count].Value;
            return true;
        }

        public static string Value(HubPlanStep step, DemoSessionOption option) =>
            step.Options.TryGetValue(option.Id, out var value) && option.HasValue(value) ? value : option.Default;

        /// <summary>Options shown as chips: active under the step's current values. Inactive ones are hidden.</summary>
        public static IReadOnlyList<DemoSessionOption> VisibleOptions(HubPlanStep step, DemoSessionDescriptor descriptor) =>
            descriptor.Options.Where(o => descriptor.IsActive(o, step.Options)).ToList();

        /// <summary>"バレーボール ブロック 3点先取": descriptor title plus active value labels, at most 40 characters.</summary>
        public static string Title(HubPlanStep step, DemoSessionDescriptor descriptor)
        {
            var parts = new List<string> { descriptor.Title.Ja };
            foreach (var option in VisibleOptions(step, descriptor))
                parts.Add(option.Find(Value(step, option)).Label.Ja);
            return Truncate(string.Join(" ", parts), TitleMaxLength);
        }

        public static string Truncate(string value, int maxCodePoints)
        {
            var builder = new StringBuilder();
            var count = 0;
            for (var index = 0; index < value.Length && count < maxCodePoints; index++, count++)
            {
                builder.Append(value[index]);
                if (char.IsHighSurrogate(value[index]) && index + 1 < value.Length) builder.Append(value[++index]);
            }
            return builder.ToString();
        }

        /// <summary>Estimated minutes of installed steps (descriptor `minutes`).</summary>
        public double Minutes(IReadOnlyList<DemoSessionCatalogEntry> catalog) =>
            Steps.Select(s => Find(catalog, s.DemoId)).Where(e => e != null).Sum(e => e.Descriptor.Minutes ?? 0);

        /// <summary>"バレーボール ブロック 3点先取 → T-Rex エンカウンター": installed steps only, at most 60 characters.</summary>
        public string Summary(IReadOnlyList<DemoSessionCatalogEntry> catalog)
        {
            var titles = new List<string>();
            foreach (var step in Steps)
            {
                var entry = Find(catalog, step.DemoId);
                if (entry != null) titles.Add(Title(step, entry.Descriptor));
            }
            return Truncate(string.Join(HubText.Arrow, titles), SummaryMaxLength);
        }

        public static DemoSessionCatalogEntry Find(IReadOnlyList<DemoSessionCatalogEntry> catalog, string demoId) =>
            catalog.FirstOrDefault(e => e.Descriptor.DemoId == demoId);

        /// <summary>
        /// Ticket at index 0 for the installed steps. Options contain exactly the active options
        /// (contract). Returns null when no step is installed.
        /// </summary>
        public DemoSessionTicket BuildTicket(IReadOnlyList<DemoSessionCatalogEntry> catalog, DemoSessionComponent finish, string sessionId, bool hapticsUi)
        {
            var steps = new List<DemoSessionStep>();
            foreach (var step in Steps)
            {
                var entry = Find(catalog, step.DemoId);
                if (entry == null) continue;
                var options = entry.Descriptor.Normalize(step.Options.Where(p => entry.Descriptor.FindOption(p.Key) != null)
                    .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal), null);
                steps.Add(new DemoSessionStep(step.DemoId, Title(step, entry.Descriptor), entry.PackageName, entry.ActivityName, options, step.Retry));
            }
            return steps.Count == 0 ? null : new DemoSessionTicket(sessionId, 0, hapticsUi, steps, finish);
        }

        public string ToJson()
        {
            var steps = new JArray();
            foreach (var step in Steps)
            {
                var options = new JObject();
                foreach (var pair in step.Options.OrderBy(p => p.Key, StringComparer.Ordinal)) options[pair.Key] = pair.Value;
                steps.Add(new JObject { ["demo_id"] = step.DemoId, ["options"] = options, ["retry"] = step.Retry });
            }
            return new JObject { ["version"] = 1, ["steps"] = steps }.ToString(Formatting.Indented);
        }

        public static bool TryFromJson(string json, out HubPlan plan)
        {
            plan = null;
            try
            {
                var root = JObject.Parse(json);
                if (root.Value<int?>("version") != 1 || !(root["steps"] is JArray steps)) return false;
                var result = new HubPlan();
                foreach (var token in steps.Take(MaxSteps))
                {
                    var demoId = token.Value<string>("demo_id");
                    if (demoId == null || !IdentifierPattern.IsMatch(demoId)) continue;
                    var options = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (token["options"] is JObject optionObject)
                        foreach (var property in optionObject.Properties())
                            if (property.Value.Type == JTokenType.String) options[property.Name] = property.Value.Value<string>();
                    result.Steps.Add(new HubPlanStep(demoId, options, token.Value<bool?>("retry") ?? true));
                }
                plan = result;
                return true;
            }
            catch (Exception exception) when (exception is JsonException || exception is InvalidCastException || exception is FormatException)
            {
                return false;
            }
        }
    }

    /// <summary>Three preset plans and the launcher settings under persistentDataPath/demo-session.</summary>
    public sealed class HubPlanStore
    {
        public const int PresetCount = 3;
        public const string SettingsSlot = "hub-settings";
        /// <summary>The planner Hub's auto-saved plan; moved to preset 1 once, when the launcher first runs.</summary>
        public const string LegacyLastSlot = "last";
        private readonly string _directory;

        public HubPlanStore(string directory)
        {
            _directory = directory;
        }

        public static HubPlanStore Default => new HubPlanStore(Path.Combine(Application.persistentDataPath, "demo-session"));
        public static string PresetSlot(int number) => "preset-" + number;

        public bool Save(HubPlan plan, string slot) => Write(slot, plan.ToJson());

        public bool TryLoad(string slot, out HubPlan plan)
        {
            plan = null;
            return TryRead(slot, out var text) && HubPlan.TryFromJson(text, out plan);
        }

        /// <summary>Preset 1..3; a missing or unreadable preset is an empty plan.</summary>
        public HubPlan LoadPreset(int number) => TryLoad(PresetSlot(number), out var plan) ? plan : new HubPlan();

        public bool SaveSettings(HubSettings settings) => Write(SettingsSlot, settings.ToJson());

        /// <summary>
        /// Saved settings, or defaults (nothing shown on the top screen). On the first run (no settings
        /// file) the legacy last plan moves to preset 1, which is then shown when it has steps.
        /// </summary>
        public HubSettings LoadSettings()
        {
            if (TryRead(SettingsSlot, out var text) && HubSettings.TryFromJson(text, out var settings)) return settings;
            settings = new HubSettings();
            if (Exists(SettingsSlot)) return settings;
            if (TryLoad(LegacyLastSlot, out var last) && Save(last, PresetSlot(1)))
            {
                Delete(LegacyLastSlot);
                if (last.Steps.Count > 0) settings.VisiblePresets.Add(1);
            }
            SaveSettings(settings);
            return settings;
        }

        public bool Exists(string slot) => File.Exists(PathFor(slot));

        private bool Write(string slot, string text)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var path = PathFor(slot);
                var temporary = path + ".tmp";
                File.WriteAllText(temporary, text, new UTF8Encoding(false));
                if (File.Exists(path)) File.Delete(path);
                File.Move(temporary, path);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("[Demo Hub] Could not save '" + slot + "': " + exception.Message);
                return false;
            }
        }

        private bool TryRead(string slot, out string text)
        {
            text = null;
            try
            {
                var path = PathFor(slot);
                if (!File.Exists(path)) return false;
                text = File.ReadAllText(path, Encoding.UTF8);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("[Demo Hub] Could not load '" + slot + "': " + exception.Message);
                return false;
            }
        }

        private void Delete(string slot)
        {
            try
            {
                File.Delete(PathFor(slot));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("[Demo Hub] Could not remove '" + slot + "': " + exception.Message);
            }
        }

        private string PathFor(string slot) => Path.Combine(_directory, slot + ".json");
    }
}
