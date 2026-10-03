using System;
using System.Collections.Generic;
using System.Linq;
using Hapbeat.DemoSwitch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hapbeat.DemoHub
{
    /// <summary>
    /// Operator settings from the manage screen: which presets and demos the top screen offers, the
    /// initial `haptics_ui` of every launch, the M5 staff waiting mode and the shared hands' look
    /// (`hand_style` of every launch and the Hub's own hands). Stored as hub-settings.json.
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

        public string ToJson() => new JObject
        {
            ["version"] = 1,
            ["visible_presets"] = new JArray(VisiblePresets.OrderBy(n => n).Cast<object>().ToArray()),
            ["visible_demos"] = new JArray(VisibleDemos.OrderBy(d => d, StringComparer.Ordinal).Cast<object>().ToArray()),
            ["haptics_ui"] = HapticsUi,
            ["staff_waiting"] = StaffWaiting,
            ["hand_style"] = DemoHandStyles.ToValue(HandStyle)
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
                    HandStyle = DemoHandStyles.TryParse(root.Value<string>("hand_style"), out var handStyle) ? handStyle : DemoHandStyle.Ghost
                };
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

    /// <summary>Long press: fires once after <see cref="Seconds"/> of continuous holding; releasing resets.</summary>
    public sealed class HubHoldGesture
    {
        public const float Seconds = 2f;
        private float _since = -1f;
        private bool _fired;

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
            Progress = Mathf.Clamp01((now - _since) / Seconds);
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
    /// Where the panel is placed: once, when head tracking first becomes valid, and again only when the
    /// operator presses 手前に移動. It never follows the head.
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
