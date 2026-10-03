using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    /// <summary>What the completion panel's forward button does.</summary>
    public readonly struct DemoSessionNext
    {
        public DemoSessionNext(DemoSessionStep step)
        {
            Step = step;
        }

        /// <summary>Null when the next launch is the finish runtime ("デモを終了").</summary>
        public DemoSessionStep Step { get; }
        public bool IsFinish => Step == null;
    }

    /// <summary>
    /// Demo Session runtime state (hapbeat-contracts specs/demo-session.md). Session mode is entered
    /// only from a valid ticket read at cold start and is never persisted.
    /// </summary>
    public static class DemoSession
    {
        public const string TicketExtra = "com.hapbeat.demo_session.ticket";

        private static IDemoSessionPlatform _platform = new SafeDemoSessionPlatform();
        private static string _currentDemoId = string.Empty;
        private static Dictionary<string, string> _options = new Dictionary<string, string>(StringComparer.Ordinal);
        private static IDemoSessionHost _host;
        private static DemoSessionCompletionPanel _completion;

        public static event Action CompletionShown;
        public static event Action Closed;
        public static event Action<bool> HapticsEnabledChanged;
        public static event Action<bool> HapticsUiVisibleChanged;

        /// <summary>Running a step (`index < len(steps)`) of a valid ticket for this demo.</summary>
        public static bool IsActive { get; private set; }
        /// <summary>Active ticket, or the all-complete ticket received by the finish runtime.</summary>
        public static DemoSessionTicket Ticket { get; private set; }
        public static bool IsFinishedSession => Ticket != null && Ticket.IsFinished;
        public static DemoSessionStep CurrentStep => IsActive ? Ticket.Steps[Ticket.Index] : null;
        /// <summary>This APK's descriptor, or null when absent/invalid/for another demo ID.</summary>
        public static DemoSessionDescriptor Descriptor { get; private set; }
        public static bool SupportsHapticsToggle => Descriptor != null && Descriptor.SupportsHapticsToggle;
        public static IReadOnlyDictionary<string, string> Options => _options;
        public static bool HapticsEnabled { get; private set; } = true;
        public static bool HapticsUiVisible { get; private set; }
        public static bool IsCompletionShown => _completion != null;

        public static DemoSessionNext Next
        {
            get
            {
                if (!IsActive) throw new InvalidOperationException("Demo Session is not active.");
                var next = Ticket.Index + 1;
                return new DemoSessionNext(next < Ticket.Steps.Count ? Ticket.Steps[next] : null);
            }
        }

        public static string GetOption(string id) => id != null && _options.TryGetValue(id, out var value) ? value : null;

        internal static IDemoAppControls HapticsControls { get; } = new DemoSessionHapticsControls();
        internal static string CurrentDemoId => _currentDemoId;
        internal static IDemoSessionPlatform Platform => _platform;

        /// <summary>Called once by the Demo Switch bootstrap after its receiver started.</summary>
        internal static void Initialize(string currentDemoId, IDemoSessionPlatform platform)
        {
            _platform = platform ?? new SafeDemoSessionPlatform();
            _currentDemoId = currentDemoId ?? string.Empty;
            Descriptor = LoadOwnDescriptor(_platform, _currentDemoId);
            if (_platform.TryTakeTicketExtra(out var json) && !TryBegin(json, _currentDemoId, Descriptor, out var error))
                Debug.LogWarning("[Demo Session] Ticket ignored; starting normally. " + error);
        }

        private static DemoSessionDescriptor LoadOwnDescriptor(IDemoSessionPlatform platform, string currentDemoId)
        {
            if (currentDemoId == DemoSwitchSettings.HubDemoId) return null; // The Hub has no descriptor.
            if (!platform.TryReadOwnAsset(DemoSessionDescriptor.FileName, out var text, out var error))
            {
                Debug.Log("[Demo Session] No descriptor in this application: " + error);
                return null;
            }
            if (!DemoSessionDescriptor.TryParse(text, out var descriptor, out error))
            {
                Debug.LogWarning("[Demo Session] Invalid " + DemoSessionDescriptor.FileName + ": " + error);
                return null;
            }
            if (descriptor.DemoId != currentDemoId)
            {
                Debug.LogWarning("[Demo Session] Descriptor demo_id '" + descriptor.DemoId + "' does not match current demo '" + currentDemoId + "'.");
                return null;
            }
            return descriptor;
        }

        /// <summary>Validates and enters session mode. Internal entry for tests and the bootstrap.</summary>
        internal static bool TryBegin(string json, string currentDemoId, DemoSessionDescriptor descriptor, out string error)
        {
            if (!DemoSessionTicket.TryParse(json, out var ticket, out error)) return false;
            if (ticket.IsFinished)
            {
                if (currentDemoId != DemoSwitchSettings.HubDemoId)
                {
                    error = "A completed ticket is accepted only by the finish runtime.";
                    return false;
                }
                Ticket = ticket;
                IsActive = false;
                HapticsUiVisible = ticket.HapticsUi;
                return true;
            }
            var step = ticket.Steps[ticket.Index];
            if (step.DemoId != currentDemoId)
            {
                error = "steps[index].demo_id '" + step.DemoId + "' does not match current demo '" + currentDemoId + "'.";
                return false;
            }

            var warnings = new List<string>();
            if (descriptor != null) _options = descriptor.Normalize(step.Options, warnings);
            else
            {
                _options = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var pair in step.Options) _options[pair.Key] = pair.Value;
                warnings.Add("No descriptor; options are applied unchecked.");
            }
            foreach (var warning in warnings) Debug.LogWarning("[Demo Session] " + warning);
            Ticket = ticket;
            IsActive = true;
            HapticsEnabled = true;
            HapticsUiVisible = ticket.HapticsUi;
            Debug.Log("[Demo Session] Step " + (ticket.Index + 1) + " / " + ticket.Steps.Count + " (" + step.DemoId + ") session " + ticket.SessionId);
            return true;
        }

        internal static void ResetForTests(IDemoSessionPlatform platform = null, string currentDemoId = "", DemoSessionDescriptor descriptor = null)
        {
            CloseCompletion(false);
            DemoPause.ResetForTests();
            DemoAppHandoff.ResetForTests();
            _platform = platform ?? new SafeDemoSessionPlatform();
            _currentDemoId = currentDemoId;
            Descriptor = descriptor;
            Ticket = null;
            IsActive = false;
            _options = new Dictionary<string, string>(StringComparer.Ordinal);
            _host = null;
            HapticsEnabled = true;
            HapticsUiVisible = false;
            CompletionShown = null;
            Closed = null;
            HapticsEnabledChanged = null;
            HapticsUiVisibleChanged = null;
        }

        public static void RegisterHost(IDemoSessionHost host)
        {
            if (host == null) return;
            if (_host != null && !ReferenceEquals(_host, host) && !(_host is UnityEngine.Object o && o == null))
                Debug.LogWarning("[Demo Session] A second session host replaced the previous one.");
            _host = host;
            if (IsActive) host.ApplyOptions(_options);
            host.SetHapticsEnabled(HapticsEnabled);
        }

        public static void UnregisterHost(IDemoSessionHost host)
        {
            if (ReferenceEquals(_host, host)) _host = null;
        }

        private static IDemoSessionHost Host => _host is UnityEngine.Object o && o == null ? null : _host;

        /// <summary>The registered scene host, or null (also used by <see cref="DemoPause"/>).</summary>
        internal static IDemoSessionHost CurrentHost => Host;

        public static void SetHapticsEnabled(bool enabled)
        {
            HapticsEnabled = enabled;
            // While paused the host stays silent; DemoPause.Resume applies this state.
            if (!DemoPause.IsPaused) Host?.SetHapticsEnabled(enabled);
            HapticsEnabledChanged?.Invoke(enabled);
        }

        public static void SetHapticsUiVisible(bool visible)
        {
            if (HapticsUiVisible == visible) return;
            HapticsUiVisible = visible;
            HapticsUiVisibleChanged?.Invoke(visible);
        }

        /// <summary>Shows the completion panel (scene anchor, else in front of the user). Session mode only; closes a shown pause first.</summary>
        public static bool ShowCompletion()
        {
            if (!IsActive)
            {
                Debug.Log("[Demo Session] ShowCompletion ignored outside session mode.");
                return false;
            }
            if (_completion != null) return true;
            DemoPause.Resume();
            _completion = DemoSessionCompletionPanel.Create(Ticket, Next);
            Host?.SetGameplayPaused(true);
            CompletionShown?.Invoke();
            return true;
        }

        internal static void CloseCompletion(bool notify = true)
        {
            if (_completion == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(_completion.gameObject);
            else UnityEngine.Object.DestroyImmediate(_completion.gameObject);
            _completion = null;
            if (!notify) return;
            Host?.SetGameplayPaused(false);
            Closed?.Invoke();
        }

        /// <summary>A system recenter: the shown completion panel goes in front of the HMD again.</summary>
        internal static void OnRecenter()
        {
            if (_completion != null) _completion.PlaceInFront();
        }

        internal static void RetryFromCompletion()
        {
            CloseCompletion();
            Host?.Restart();
        }

        /// <summary>
        /// Launches the next step, or the finish runtime after the last step. <paramref name="onFailed"/>
        /// receives the error when the started application does not come to the front (see <see cref="LaunchTicket"/>).
        /// </summary>
        public static bool LaunchNext(out string error, Action<string> onFailed = null)
        {
            if (!IsActive) { error = "Demo Session is not active."; return false; }
            return LaunchTicket(Ticket.WithIndex(Ticket.Index + 1, HapticsUiVisible), out error, onFailed);
        }

        /// <summary>Launches the finish runtime with the all-complete ticket.</summary>
        public static bool LaunchFinish(out string error, Action<string> onFailed = null)
        {
            if (Ticket == null) { error = "No Demo Session ticket."; return false; }
            return LaunchTicket(Ticket.WithIndex(Ticket.Steps.Count, HapticsUiVisible), out error, onFailed);
        }

        /// <summary>
        /// Starts the component named by <paramref name="ticket"/>'s index with that ticket attached. Once
        /// this runtime has gone to the background it stops its haptics, sound and 7710 listener and removes
        /// its task, exactly once. When it is still in front after <see cref="DemoAppHandoff.TimeoutSeconds"/>,
        /// it keeps running and <paramref name="onFailed"/> receives the error (null: only logged).
        /// </summary>
        public static bool LaunchTicket(DemoSessionTicket ticket, out string error, Action<string> onFailed = null)
        {
            if (DemoAppHandoff.IsPending || DemoAppHandoff.IsFinished) { error = "A launch is already in progress."; return false; }
            var json = ticket.ToJson();
            if (!DemoSessionTicket.TryParse(json, out _, out error)) return false;
            var finish = ticket.IsFinished;
            var package = finish ? ticket.Finish.PackageName : ticket.Steps[ticket.Index].PackageName;
            var activity = finish ? ticket.Finish.ActivityName : ticket.Steps[ticket.Index].ActivityName;
            var nextDemoId = finish ? DemoSwitchSettings.HubDemoId : ticket.Steps[ticket.Index].DemoId;
            if (!_platform.TryLaunch(package, activity, json, out error))
            {
                Debug.LogError("[Demo Session] Launch failed: " + error);
                return false;
            }
            DemoAppHandoff.Begin(nextDemoId, onFailed, Time.realtimeSinceStartup);
            return true;
        }

        internal static bool ApplyHapticsAction(string action)
        {
            switch (action)
            {
                case "haptics_on": SetHapticsEnabled(true); return true;
                case "haptics_off": SetHapticsEnabled(false); return true;
                case "haptics_ui_show": SetHapticsUiVisible(true); return true;
                case "haptics_ui_hide": SetHapticsUiVisible(false); return true;
                default: return false;
            }
        }

        /// <summary>CONTROL adapter for `haptics_*`; it bypasses the scene's IDemoAppControls.</summary>
        private sealed class DemoSessionHapticsControls : IDemoAppControls
        {
            public bool CanExecuteControl(string action, string sceneId) =>
                SupportsHapticsToggle && sceneId == string.Empty && DemoSwitchProtocol.IsHapticsAction(action);

            public IEnumerator ExecuteControl(string action, string sceneId)
            {
                if (!CanExecuteControl(action, sceneId) || !ApplyHapticsAction(action))
                    throw new InvalidOperationException("Unsupported haptics control.");
                yield break;
            }
        }
    }
}
