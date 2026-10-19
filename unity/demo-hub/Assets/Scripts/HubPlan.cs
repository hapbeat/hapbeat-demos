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
        private static readonly System.Text.RegularExpressions.Regex OptionValuePattern =
            new System.Text.RegularExpressions.Regex(@"^[a-z0-9][a-z0-9._-]{0,31}\z");

        public const int SummaryMaxLength = 60;

        public List<HubPlanStep> Steps { get; } = new List<HubPlanStep>();
        /// <summary>Shown on the preset button; "" for none. Set by PRESET_SET, kept by the Hub's editor.</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Increased on every save of the preset (<see cref="HubPlanStore.SavePreset"/>), by the editor or PRESET_SET.</summary>
        public long Revision { get; set; }

        /// <summary>
        /// One-step plan for a step-less start (a demo tile, the `demo_id` external start): the session options of the per-demo
        /// settings <paramref name="demoSettings"/> (contracts "デモごとの設定"), retry offered.
        /// </summary>
        public static HubPlan Single(DemoSessionDescriptor descriptor, HubDemoSettings demoSettings)
        {
            var plan = new HubPlan();
            var saved = demoSettings.Values(descriptor);
            plan.Steps.Add(new HubPlanStep(descriptor.DemoId,
                descriptor.Options.Where(o => !o.IsDevice).ToDictionary(o => o.Id, o => saved[o.Id], StringComparer.Ordinal), true));
            return plan;
        }

        /// <summary>Appends a step with the descriptor defaults of its session options (a preset never holds `scope: device` ones).</summary>
        public bool Add(DemoSessionDescriptor descriptor)
        {
            if (Steps.Count >= MaxSteps) return false;
            var options = descriptor.Options.Where(o => !o.IsDevice).ToDictionary(o => o.Id, o => o.Default, StringComparer.Ordinal);
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

        /// <summary>Takes the step at <paramref name="from"/> out and inserts it so that it ends at <paramref name="to"/>.</summary>
        public bool Move(int from, int to)
        {
            if (from < 0 || from >= Steps.Count || to < 0 || to >= Steps.Count || from == to) return false;
            var step = Steps[from];
            Steps.RemoveAt(from);
            Steps.Insert(to, step);
            return true;
        }

        public bool Remove(int index)
        {
            if (index < 0 || index >= Steps.Count) return false;
            Steps.RemoveAt(index);
            return true;
        }

        public void ToggleRetry(int index) => Steps[index].Retry = !Steps[index].Retry;

        /// <summary>Advances an active session option to its next value, wrapping around (a preset holds no device option).</summary>
        public bool CycleOption(int index, DemoSessionDescriptor descriptor, string optionId)
        {
            var step = Steps[index];
            var option = descriptor.FindOption(optionId);
            if (option == null || option.IsDevice || !descriptor.IsActive(option, step.Options)) return false;
            var current = Value(step, option);
            var position = option.Values.ToList().FindIndex(v => v.Value == current);
            step.Options[option.Id] = option.Values[(position + 1) % option.Values.Count].Value;
            return true;
        }

        public static string Value(HubPlanStep step, DemoSessionOption option) =>
            step.Options.TryGetValue(option.Id, out var value) && option.HasValue(value) ? value : option.Default;

        /// <summary>
        /// Options shown as chips: the session options active under the step's current values. Inactive ones are hidden, and
        /// `scope: device` ones belong to the per-demo settings, not to a step.
        /// </summary>
        public static IReadOnlyList<DemoSessionOption> VisibleOptions(HubPlanStep step, DemoSessionDescriptor descriptor) =>
            descriptor.Options.Where(o => !o.IsDevice && descriptor.IsActive(o, step.Options)).ToList();

        /// <summary>
        /// "Volley ブロック 3点先取": the application name (<see cref="DemoSessionCatalogEntry.DisplayName"/>)
        /// plus the active value labels, at most 40 characters.
        /// </summary>
        public static string Title(HubPlanStep step, DemoSessionCatalogEntry entry)
        {
            var parts = new List<string> { entry.DisplayName };
            foreach (var option in VisibleOptions(step, entry.Descriptor))
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

        /// <summary>"Volley ブロック 3点先取 → T-Rex Encounter": installed steps only, at most 60 characters.</summary>
        public string Summary(IReadOnlyList<DemoSessionCatalogEntry> catalog)
        {
            var titles = new List<string>();
            foreach (var step in Steps)
            {
                var entry = Find(catalog, step.DemoId);
                if (entry != null) titles.Add(Title(step, entry));
            }
            return Truncate(string.Join(HubText.Arrow, titles), SummaryMaxLength);
        }

        public static DemoSessionCatalogEntry Find(IReadOnlyList<DemoSessionCatalogEntry> catalog, string demoId) =>
            catalog.FirstOrDefault(e => e.Descriptor.DemoId == demoId);

        /// <summary>
        /// Ticket at index 0 for the installed steps. Options contain exactly the active options (contract), resolved with
        /// <paramref name="demoSettings"/> (contracts "デモごとの設定"): a `scope: device` option always has the per-demo value (a
        /// step's own value is ignored with a warning); a session option has the step's value, else the descriptor default, so a
        /// preset or plan starts as its editor shows it (step-less starts fill the step first, <see cref="Single"/>).
        /// <paramref name="narrationVolume"/> null omits `narration_volume` (<see cref="HubSettings.TicketNarrationVolume"/>).
        /// Returns null when no step is installed.
        /// </summary>
        public DemoSessionTicket BuildTicket(IReadOnlyList<DemoSessionCatalogEntry> catalog, DemoSessionComponent finish, string sessionId, bool hapticsUi,
            DemoHandStyle handStyle, bool recenterUi, int? narrationVolume, HubDemoSettings demoSettings)
        {
            var steps = new List<DemoSessionStep>();
            foreach (var step in Steps)
            {
                var entry = Find(catalog, step.DemoId);
                if (entry == null) continue;
                var resolved = new HubPlanStep(step.DemoId, ResolveOptions(step, entry.Descriptor, demoSettings), step.Retry);
                var options = entry.Descriptor.Normalize(resolved.Options, null);
                steps.Add(new DemoSessionStep(step.DemoId, Title(resolved, entry), entry.PackageName, entry.ActivityName, options, step.Retry));
            }
            return steps.Count == 0 ? null : new DemoSessionTicket(sessionId, 0, hapticsUi, steps, finish, handStyle, recenterUi, narrationVolume);
        }

        /// <summary>
        /// Every option of <paramref name="descriptor"/> for one launch of <paramref name="step"/>: device options from the per-demo
        /// settings, session options from the step when it holds a valid value, else the descriptor default.
        /// </summary>
        public static Dictionary<string, string> ResolveOptions(HubPlanStep step, DemoSessionDescriptor descriptor, HubDemoSettings demoSettings)
        {
            var saved = demoSettings.Values(descriptor);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var option in descriptor.Options)
            {
                var held = step.Options.TryGetValue(option.Id, out var value);
                if (option.IsDevice)
                {
                    if (held) Debug.LogWarning("[Demo Hub] " + step.DemoId + ": " + option.Id + " is this headset's per-demo setting; the step's value " + value + " is ignored.");
                    result[option.Id] = saved[option.Id];
                }
                else result[option.Id] = held && option.HasValue(value) ? value : option.Default;
            }
            return result;
        }

        /// <summary>
        /// A preset written by PRESET_SET: the steps with their options as given (checked by <see cref="Check"/>) except the
        /// `scope: device` ones, which a preset never holds; the name; the revision is set by the caller.
        /// </summary>
        /// <summary>
        /// A preset saved by an older Hub may hold `scope: device` options of installed demos: they are dropped (warning, the
        /// value is not moved to the per-demo settings), so PRESET replies and launches never carry them. Not saved; true when
        /// something was dropped.
        /// </summary>
        public bool DropDeviceOptions(IReadOnlyList<DemoSessionCatalogEntry> catalog, int number)
        {
            var dropped = false;
            foreach (var step in Steps)
            {
                var descriptor = Find(catalog, step.DemoId)?.Descriptor;
                if (descriptor == null) continue;
                foreach (var id in step.Options.Keys.Where(id => descriptor.FindOption(id)?.IsDevice == true).ToList())
                {
                    Debug.LogWarning("[Demo Hub] Preset " + number + ": " + step.DemoId + " " + id + "=" + step.Options[id] +
                        " is a per-demo setting and is dropped from the preset.");
                    step.Options.Remove(id);
                    dropped = true;
                }
            }
            return dropped;
        }

        public static HubPlan FromPreset(string name, IReadOnlyList<DemoSwitchPresetStep> steps, IReadOnlyList<DemoSessionCatalogEntry> catalog)
        {
            var plan = new HubPlan { Name = name ?? string.Empty };
            foreach (var step in steps.Take(MaxSteps))
            {
                var descriptor = Find(catalog, step.DemoId)?.Descriptor;
                var options = step.Options.Where(p => descriptor?.FindOption(p.Key)?.IsDevice != true)
                    .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                if (options.Count < step.Options.Count)
                    Debug.LogWarning("[Demo Hub] PRESET_SET: per-demo settings of " + step.DemoId + " are not stored in a preset.");
                plan.Steps.Add(new HubPlanStep(step.DemoId, options, step.Retry));
            }
            return plan;
        }

        /// <summary>
        /// The stored steps for PRESET, including steps whose demo is not installed. Options that the wire format cannot
        /// carry (not an identifier / option value, more than 8) are left out.
        /// </summary>
        public IReadOnlyList<DemoSwitchPresetStep> PresetSteps() => Steps.Select(step => new DemoSwitchPresetStep(step.DemoId,
            step.Options.Where(p => IdentifierPattern.IsMatch(p.Key) && OptionValuePattern.IsMatch(p.Value ?? string.Empty))
                .OrderBy(p => p.Key, StringComparer.Ordinal).Take(DemoSwitchPresets.MaxStepOptions)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal), step.Retry)).ToList();

        /// <summary>
        /// PRESET_SET validation of the whole plan before anything is stored: every demo must be installed (a catalog entry)
        /// and every option key and value must exist in its descriptor. The first offending step decides the result, and
        /// <paramref name="demoId"/> is its demo.
        /// </summary>
        public static DemoSwitchPresetCheck Check(IReadOnlyList<DemoSwitchPresetStep> steps, IReadOnlyList<DemoSessionCatalogEntry> catalog, out string demoId)
        {
            foreach (var step in steps)
            {
                var entry = Find(catalog, step.DemoId);
                demoId = step.DemoId;
                if (entry == null) return DemoSwitchPresetCheck.NotInstalled;
                foreach (var pair in step.Options)
                {
                    var option = entry.Descriptor.FindOption(pair.Key);
                    if (option == null || !option.HasValue(pair.Value)) return DemoSwitchPresetCheck.UnknownOption;
                }
            }
            demoId = null;
            return DemoSwitchPresetCheck.Ok;
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
            return new JObject { ["version"] = 1, ["name"] = Name, ["revision"] = Revision, ["steps"] = steps }.ToString(Formatting.Indented);
        }

        public static bool TryFromJson(string json, out HubPlan plan)
        {
            plan = null;
            try
            {
                var root = JObject.Parse(json);
                if (root.Value<int?>("version") != 1 || !(root["steps"] is JArray steps)) return false;
                var result = new HubPlan();
                // Files from before name / revision load as unnamed, revision 0.
                if (root["name"] is JValue name && name.Type == JTokenType.String && DemoSwitchPresets.IsValidName(name.Value<string>()))
                    result.Name = name.Value<string>();
                if (root["revision"] is JValue revision && revision.Type == JTokenType.Integer
                    && long.TryParse(revision.ToString(Formatting.None), out var number) && number >= 0)
                    result.Revision = number;
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
        /// <summary>The per-demo settings (<see cref="HubDemoSettings"/>), next to the Hub settings.</summary>
        public const string DemoSettingsSlot = "demo-settings";
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

        /// <summary>Saves preset <paramref name="number"/> with its revision increased; a failed write keeps the old revision.</summary>
        public bool SavePreset(HubPlan plan, int number)
        {
            plan.Revision++;
            if (Save(plan, PresetSlot(number))) return true;
            plan.Revision--;
            return false;
        }

        public bool TryLoad(string slot, out HubPlan plan)
        {
            plan = null;
            return TryRead(slot, out var text) && HubPlan.TryFromJson(text, out plan);
        }

        /// <summary>Preset 1..3; a missing or unreadable preset is an empty plan.</summary>
        public HubPlan LoadPreset(int number) => TryLoad(PresetSlot(number), out var plan) ? plan : new HubPlan();

        /// <summary>Saves the settings with their revision increased; a failed write keeps the old revision.</summary>
        public bool SaveSettings(HubSettings settings)
        {
            settings.Revision++;
            if (Write(SettingsSlot, settings.ToJson())) return true;
            settings.Revision--;
            return false;
        }

        /// <summary>
        /// Saved settings, or defaults (nothing shown on the top screen). On the first run (no settings
        /// file) the legacy last plan moves to preset 1, which is then shown when it has steps.
        /// </summary>
        public HubSettings LoadSettings()
        {
            if (TryRead(SettingsSlot, out var text) && HubSettings.TryFromJson(text, out var settings))
            {
                // A file from before the hub_id: it gets one now and keeps it.
                if (settings.HubId != null) return settings;
                settings.HubId = DemoSwitchHub.NewHubId();
                Write(SettingsSlot, settings.ToJson());
                return settings;
            }
            settings = new HubSettings { HubId = DemoSwitchHub.NewHubId() };
            if (Exists(SettingsSlot)) return settings;
            if (TryLoad(LegacyLastSlot, out var last) && Save(last, PresetSlot(1)))
            {
                Delete(LegacyLastSlot);
                if (last.Steps.Count > 0) settings.VisiblePresets.Add(1);
            }
            SaveSettings(settings);
            return settings;
        }

        /// <summary>Saved per-demo settings, or none (every demo at its descriptor defaults).</summary>
        public HubDemoSettings LoadDemoSettings() =>
            TryRead(DemoSettingsSlot, out var text) && HubDemoSettings.TryFromJson(text, out var demoSettings) ? demoSettings : new HubDemoSettings();

        /// <summary>
        /// Saves the per-demo settings together with the Hub settings with their revision increased (the counter DEMO_SETTINGS
        /// and HUB_SETTINGS share): both are written to temporary files first and replace the old files only when both writes
        /// succeeded, so the two never disagree. False (nothing changed on disk, the revision kept) when either failed.
        /// </summary>
        public bool SaveDemoSettings(HubDemoSettings demoSettings, HubSettings settings)
        {
            settings.Revision++;
            if (WriteTogether(DemoSettingsSlot, demoSettings.ToJson(), SettingsSlot, settings.ToJson())) return true;
            settings.Revision--;
            return false;
        }

        public bool Exists(string slot) => File.Exists(PathFor(slot));

        /// <summary>
        /// Two files that must match: both temporary files are written, then both replace the old ones; a failure on the way
        /// deletes the temporary files and puts back what was replaced.
        /// </summary>
        private bool WriteTogether(string firstSlot, string firstText, string secondSlot, string secondText)
        {
            var slots = new[] { firstSlot, secondSlot };
            var texts = new[] { firstText, secondText };
            var temporaries = slots.Select(slot => PathFor(slot) + ".tmp").ToArray();
            var backups = slots.Select(slot => PathFor(slot) + ".bak").ToArray();
            var replaced = 0;
            try
            {
                Directory.CreateDirectory(_directory);
                for (var i = 0; i < 2; i++) File.WriteAllText(temporaries[i], texts[i], new UTF8Encoding(false));
                for (; replaced < 2; replaced++)
                {
                    var path = PathFor(slots[replaced]);
                    if (File.Exists(backups[replaced])) File.Delete(backups[replaced]);
                    if (File.Exists(path)) File.Move(path, backups[replaced]);
                    File.Move(temporaries[replaced], path);
                }
                foreach (var backup in backups) if (File.Exists(backup)) File.Delete(backup);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogWarning("[Demo Hub] Could not save '" + firstSlot + "' and '" + secondSlot + "': " + exception.Message);
                // Put back each file already replaced, or moved aside when the failure came, newest first.
                for (var i = Math.Min(replaced, 1); i >= 0; i--)
                {
                    try
                    {
                        var path = PathFor(slots[i]);
                        if (File.Exists(backups[i]) && (i < replaced || !File.Exists(path)))
                        {
                            if (File.Exists(path)) File.Delete(path);
                            File.Move(backups[i], path);
                        }
                    }
                    catch (Exception restore) when (restore is IOException || restore is UnauthorizedAccessException)
                    {
                        Debug.LogWarning("[Demo Hub] Could not restore '" + slots[i] + "': " + restore.Message);
                    }
                }
                foreach (var temporary in temporaries)
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    catch (Exception remove) when (remove is IOException || remove is UnauthorizedAccessException) { Debug.LogWarning("[Demo Hub] " + remove.Message); }
                }
                return false;
            }
        }

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
