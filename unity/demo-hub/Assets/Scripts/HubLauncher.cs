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
    /// initial `haptics_ui` of every launch, and the M5 staff waiting mode. Stored as hub-settings.json.
    /// </summary>
    public sealed class HubSettings
    {
        public HashSet<int> VisiblePresets { get; } = new HashSet<int>();
        public HashSet<string> VisibleDemos { get; } = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Initial `haptics_ui` for every launch from the Hub (default hidden).</summary>
        public bool HapticsUi { get; set; }
        /// <summary>Head-locked "staff will start" labels instead of the launcher (M5 operation).</summary>
        public bool StaffWaiting { get; set; }

        public string ToJson() => new JObject
        {
            ["version"] = 1,
            ["visible_presets"] = new JArray(VisiblePresets.OrderBy(n => n).Cast<object>().ToArray()),
            ["visible_demos"] = new JArray(VisibleDemos.OrderBy(d => d, StringComparer.Ordinal).Cast<object>().ToArray()),
            ["haptics_ui"] = HapticsUi,
            ["staff_waiting"] = StaffWaiting
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
                    StaffWaiting = root.Value<bool?>("staff_waiting") ?? false
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
    /// Lazy yaw-only follow: the panel stays put while it is within 35° of the head's heading (and
    /// near its distance and height), otherwise it eases back in front over 0.5 s.
    /// </summary>
    public sealed class HubPanelFollow
    {
        public const float Distance = 0.6f;
        /// <summary>Panel centre below eye height, metres.</summary>
        public const float Drop = 0.18f;
        public const float RecenterDegrees = 35f;
        public const float RecenterSeconds = 0.5f;
        /// <summary>Distance or height error that also recentres (refitting, sitting down).</summary>
        public const float MaxOffset = 0.25f;
        private Pose _from;
        private float _elapsed = -1f;

        public bool Moving => _elapsed >= 0f;

        /// <summary>In front of the head's heading (pitch and roll ignored), facing the head.</summary>
        public static Pose Target(Vector3 headPosition, Vector3 headForward, Vector3 headUp)
        {
            var heading = Heading(headForward, headUp);
            var position = headPosition + heading * Distance + Vector3.down * Drop;
            return new Pose(position, Quaternion.LookRotation(position - headPosition, Vector3.up));
        }

        public static bool OutOfPlace(Vector3 headPosition, Vector3 headForward, Vector3 headUp, Vector3 panelPosition)
        {
            var offset = panelPosition - headPosition;
            var flat = Vector3.ProjectOnPlane(offset, Vector3.up);
            if (flat.sqrMagnitude < 0.0001f) return true;
            if (Vector3.Angle(Heading(headForward, headUp), flat) >= RecenterDegrees) return true;
            return Mathf.Abs(flat.magnitude - Distance) > MaxOffset || Mathf.Abs(offset.y + Drop) > MaxOffset;
        }

        /// <summary>Next panel pose: starts a recentre when out of place, then eases to the moving target.</summary>
        public Pose Step(Pose current, Vector3 headPosition, Vector3 headForward, Vector3 headUp, float deltaTime)
        {
            if (!Moving && OutOfPlace(headPosition, headForward, headUp, current.position))
            {
                _from = current;
                _elapsed = 0f;
            }
            if (!Moving) return current;
            _elapsed += deltaTime;
            var t = Mathf.Clamp01(_elapsed / RecenterSeconds);
            if (t >= 1f) _elapsed = -1f;
            var eased = t * t * (3f - 2f * t);
            var target = Target(headPosition, headForward, headUp);
            return new Pose(Vector3.Lerp(_from.position, target.position, eased), Quaternion.Slerp(_from.rotation, target.rotation, eased));
        }

        private static Vector3 Heading(Vector3 headForward, Vector3 headUp)
        {
            var heading = Vector3.ProjectOnPlane(headForward, Vector3.up);
            // Looking straight down: the top of the head points the way the user faces.
            if (heading.sqrMagnitude < 0.0001f) heading = Vector3.ProjectOnPlane(headUp, Vector3.up);
            if (heading.sqrMagnitude < 0.0001f) heading = Vector3.forward;
            return heading.normalized;
        }
    }
}
