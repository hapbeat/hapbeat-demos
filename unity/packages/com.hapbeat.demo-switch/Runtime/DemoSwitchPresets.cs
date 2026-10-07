using System;
using System.Collections.Generic;
using System.Linq;

namespace Hapbeat.DemoSwitch
{
    /// <summary>One step of a Hub preset as PRESET / PRESET_SET carry it (contracts demo-switch-control.md "Hub presets").</summary>
    public sealed class DemoSwitchPresetStep
    {
        public DemoSwitchPresetStep(string demoId, IReadOnlyDictionary<string, string> options, bool retry)
        {
            DemoId = demoId;
            Options = options == null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : options.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            Retry = retry;
        }

        public string DemoId { get; }
        public IReadOnlyDictionary<string, string> Options { get; }
        /// <summary>Offer "もう一度" after the step (default true).</summary>
        public bool Retry { get; }
    }

    /// <summary>A Hub preset (1..3): name ("" for none), top-screen visibility, the Hub's save counter and its steps.</summary>
    public sealed class DemoSwitchPreset
    {
        public DemoSwitchPreset(string name, bool visible, long revision, IReadOnlyList<DemoSwitchPresetStep> steps)
        {
            Name = name ?? string.Empty;
            Visible = visible;
            Revision = revision;
            Steps = steps ?? Array.Empty<DemoSwitchPresetStep>();
        }

        public string Name { get; }
        public bool Visible { get; }
        /// <summary>Increased by the Hub whenever the preset is saved (its own editor or PRESET_SET).</summary>
        public long Revision { get; }
        public IReadOnlyList<DemoSwitchPresetStep> Steps { get; }
    }

    /// <summary>Result of <see cref="IDemoSwitchPresetHost.CheckSteps"/>: the first offending step decides it.</summary>
    public enum DemoSwitchPresetCheck
    {
        Ok,
        /// <summary>The demo is not a Hub catalog entry: FAILED/not_allowed.</summary>
        NotInstalled,
        /// <summary>An option key or value is not in the demo's descriptor: FAILED/invalid_payload.</summary>
        UnknownOption
    }

    /// <summary>
    /// The Hub's presets for PRESET_GET / PRESET_SET / PRESET_START. Only the Hub registers one
    /// (<see cref="DemoSwitchPresets.RegisterHost"/>); without a host the receiver drops these messages without a reply.
    /// The receiver owns authentication, foreground, sequence and status; the host only reads, checks, stores and starts.
    /// </summary>
    public interface IDemoSwitchPresetHost
    {
        /// <summary>True while staff edit the presets (manage screen): PRESET_SET / PRESET_START are refused.</summary>
        bool IsBusy { get; }

        /// <summary>The stored preset <paramref name="number"/> (1..3), including steps whose demo is not installed.</summary>
        DemoSwitchPreset ReadPreset(int number);

        /// <summary>Validates a whole PRESET_SET before anything is stored; <paramref name="demoId"/> is the first offending step's demo.</summary>
        DemoSwitchPresetCheck CheckSteps(IReadOnlyList<DemoSwitchPresetStep> steps, out string demoId);

        /// <summary>Stores the preset (increasing its revision) and refreshes the screens; false with the storage error.</summary>
        bool TryStorePreset(int number, string name, bool visible, IReadOnlyList<DemoSwitchPresetStep> steps, out string error);

        /// <summary>Whether preset <paramref name="number"/> has at least one installed step.</summary>
        bool HasInstalledStep(int number);

        /// <summary>
        /// Starts the preset like the top screen does (Hub-wide launch settings apply). False with the error when it could
        /// not start; <paramref name="onFailed"/> receives the error when the started application does not come to the front.
        /// </summary>
        bool TryStartPreset(int number, Action<string> onFailed, out string error);
    }

    public static class DemoSwitchPresets
    {
        public const int Count = 3;
        public const int MaxSteps = 32;
        public const int NameMaxLength = 40;
        /// <summary>Schema bound of a step's options.</summary>
        public const int MaxStepOptions = 8;

        internal static IDemoSwitchPresetHost Host { get; private set; }

        public static void RegisterHost(IDemoSwitchPresetHost host) => Host = host;

        public static void UnregisterHost(IDemoSwitchPresetHost host)
        {
            if (ReferenceEquals(Host, host)) Host = null;
        }

        /// <summary>
        /// The empty string (no name), or 1-40 code points with at least one non-space character and no C0/C1 control
        /// character or U+2028 / U+2029 (demo-remote-preset name rules).
        /// </summary>
        public static bool IsValidName(string name)
        {
            if (name == null) return false;
            if (name.Length == 0) return true;
            var length = DemoSessionJson.CodePointLength(name);
            if (length > NameMaxLength) return false;
            var visible = false;
            foreach (var character in name)
            {
                if (character <= '\u001f' || (character >= '\u007f' && character <= '\u009f') || character == '\u2028' || character == '\u2029')
                    return false;
                if (!char.IsWhiteSpace(character)) visible = true;
            }
            return visible;
        }
    }
}
