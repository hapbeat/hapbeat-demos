using System;
using System.Collections.Generic;

namespace Hapbeat.DemoSwitch
{
    /// <summary>One installed demo as HUB_SETTINGS carries it (contracts demo-switch-control.md "Hub settings").</summary>
    public sealed class DemoSwitchHubDemo
    {
        public DemoSwitchHubDemo(string demoId, string title, bool visible)
        {
            DemoId = demoId;
            Title = title;
            Visible = visible;
        }

        public string DemoId { get; }
        /// <summary>The Hub's display name (<see cref="DemoSwitchHub.IsValidTitle"/>).</summary>
        public string Title { get; }
        /// <summary>Shown as a tile on the Hub's top screen.</summary>
        public bool Visible { get; }
    }

    /// <summary>The Hub-wide settings of the manage screen, this headset's device address and the installed demos.</summary>
    public sealed class DemoSwitchHubSettings
    {
        public DemoSwitchHubSettings(long revision, bool hapticsUi, bool recenterUi, DemoHandStyle handStyle, bool staffWaiting,
            int player, int group, IReadOnlyList<DemoSwitchHubDemo> demos)
        {
            Revision = revision;
            HapticsUi = hapticsUi;
            RecenterUi = recenterUi;
            HandStyle = handStyle;
            StaffWaiting = staffWaiting;
            Player = player;
            Group = group;
            Demos = demos ?? Array.Empty<DemoSwitchHubDemo>();
        }

        /// <summary>Increased by the Hub whenever it saves its settings (manage screen, HUB_SETTINGS_SET, a preset's `visible`).</summary>
        public long Revision { get; }
        public bool HapticsUi { get; }
        public bool RecenterUi { get; }
        public DemoHandStyle HandStyle { get; }
        public bool StaffWaiting { get; }
        /// <summary>1..99, or -1 when not specified (read only).</summary>
        public int Player { get; }
        /// <summary>1..99, or -1 when not specified (read only).</summary>
        public int Group { get; }
        /// <summary>The installed demos (Hub catalog entries) in catalog order.</summary>
        public IReadOnlyList<DemoSwitchHubDemo> Demos { get; }
    }

    /// <summary>
    /// The Hub's side of the Hub presets, Hub settings, Hub start and the Hub's CONTROL actions (`hub_top`, `hub_replay`,
    /// `hand_style_*`). Only the Hub registers one (<see cref="DemoSwitchHub.RegisterHost"/>); without a host the receiver drops
    /// PRESET_* / HUB_* messages without a reply and refuses `hub_top` / `hub_replay`. The receiver owns authentication,
    /// foreground, sequence and status; the host only reads, checks, stores and starts.
    /// </summary>
    public interface IDemoSwitchHubHost
    {
        /// <summary>True while staff edit on the manage screen: PRESET_SET / PRESET_START / HUB_SETTINGS_SET / HUB_START are refused.</summary>
        bool IsBusy { get; }

        /// <summary>STATE `screen`: <see cref="DemoSwitchScreens.Main"/>, `completion` (finish screen) or `manage`.</summary>
        string CurrentScreen { get; }

        /// <summary>The stored preset <paramref name="number"/> (1..3), including steps whose demo is not installed.</summary>
        DemoSwitchPreset ReadPreset(int number);

        /// <summary>Validates steps (PRESET_SET, HUB_START) as a whole; <paramref name="demoId"/> is the first offending step's demo.</summary>
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

        /// <summary>The Hub-wide settings, the device address and every installed demo.</summary>
        DemoSwitchHubSettings ReadSettings();

        /// <summary>HUB_SETTINGS_SET validation: <see cref="DemoSwitchPresetCheck.NotInstalled"/> with the first demo that is not installed.</summary>
        DemoSwitchPresetCheck CheckVisibleDemos(IReadOnlyList<string> demoIds, out string demoId);

        /// <summary>
        /// Stores the four values and the tiles (the listed installed demos shown, the other installed ones hidden, the choice of
        /// demos that are not installed kept), increasing the revision, and applies them at once; false with the storage error.
        /// </summary>
        bool TryStoreSettings(bool hapticsUi, bool recenterUi, DemoHandStyle handStyle, bool staffWaiting, IReadOnlyList<string> visibleDemos,
            out string error);

        /// <summary>CONTROL `hand_style_*` on the Hub: changes, applies and stores the Hub-wide hand style; false with the storage error.</summary>
        bool TrySetHandStyle(DemoHandStyle style, out string error);

        /// <summary>
        /// HUB_START: a session from checked steps, like the start extra `steps` (Hub-wide launch settings apply, nothing stored).
        /// False with the error when it could not start; <paramref name="onFailed"/> as in <see cref="TryStartPreset"/>.
        /// </summary>
        bool TryStartSteps(IReadOnlyList<DemoSwitchPresetStep> steps, Action<string> onFailed, out string error);

        /// <summary>CONTROL `hub_top`: the top screen (closes the manage or finish screen; nothing changes on the top screen).</summary>
        void ShowTop();

        /// <summary>
        /// CONTROL `hub_replay` (finish screen only): the finished plan again from its first step with the Hub-wide launch
        /// settings. False with the error when it could not start; <paramref name="onFailed"/> as in <see cref="TryStartPreset"/>.
        /// </summary>
        bool TryReplay(Action<string> onFailed, out string error);
    }

    /// <summary>STATE `screen` values (contracts: State query).</summary>
    public static class DemoSwitchScreens
    {
        public const string Main = "main";
        /// <summary>The completion panel, or the Hub's finish screen.</summary>
        public const string Completion = "completion";
        /// <summary>The Hub's manage screen.</summary>
        public const string Manage = "manage";
    }

    public static class DemoSwitchHub
    {
        /// <summary>Schema bound of `demo_count` / `demos` / `visible_demos` (`from` is below it).</summary>
        public const int MaxDemos = 64;

        internal static IDemoSwitchHubHost Host { get; private set; }

        public static void RegisterHost(IDemoSwitchHubHost host) => Host = host;

        public static void UnregisterHost(IDemoSwitchHubHost host)
        {
            if (ReferenceEquals(Host, host)) Host = null;
        }

        /// <summary>A HUB_SETTINGS title: the preset name rules without the empty name (1-40 code points, a non-space character).</summary>
        public static bool IsValidTitle(string title) => !string.IsNullOrEmpty(title) && DemoSwitchPresets.IsValidName(title);
    }
}
