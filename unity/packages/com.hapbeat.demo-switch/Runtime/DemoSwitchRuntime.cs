using System;
using System.Collections;
using System.Net;
using System.Text;
using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// Keeps UDP 7710 bound while the process lives (contracts: Transport and lifecycle). Out of the foreground
    /// (<see cref="OnApplicationFocus"/> false or <see cref="OnApplicationPause"/> true) it still answers DISCOVER
    /// and QUERY (STATE `foreground: false`) but refuses SWITCH / CONTROL; only a hand-over stops the listener.
    /// </summary>
    internal sealed class DemoSwitchRuntime : MonoBehaviour
    {
        /// <summary>A failed bind (e.g. the previous application's socket still open) is retried every 0.25 s for 5 s.</summary>
        internal const float BindRetryIntervalSeconds = 0.25f;
        internal const int BindRetryAttempts = 20;
        internal const string NotForegroundMessage = "not in foreground";
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private DemoSwitchSettings _settings;
        private DemoSwitchUdpTransport _transport;
        private DemoSwitchSequenceGuard _sequenceGuard;
        private IDemoSwitchLaunchAdapter _launcher;
        private readonly DemoSwitchMulticastLock _multicastLock = new DemoSwitchMulticastLock();
        private bool _foreground = true;
        private bool _listenerStarted;
        private Coroutine _bindRetry;
        private Coroutine _control;
        private DemoSwitchLaunchContext? _pendingLaunchContext;
        private float _nextDropLogTime;
        private bool _controlBusy;

        public static DemoSwitchRuntime Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var settings = Resources.Load<DemoSwitchSettings>(DemoSwitchSettings.ResourceName);
            if (settings == null) return;
            var host = new GameObject("Hapbeat Demo Switch");
            DontDestroyOnLoad(host);
            var runtime = host.AddComponent<DemoSwitchRuntime>();
            runtime.Initialize(settings);
        }

        private void Initialize(DemoSwitchSettings settings)
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            _settings = settings;
            _transport = new DemoSwitchUdpTransport();
            _sequenceGuard = new DemoSwitchSequenceGuard(new PlayerPrefsSequenceStore());
            _launcher = DemoSwitchLaunchAdapter.Create();
            StartReceiver();
            DemoSession.Initialize(settings.CurrentDemoId, DemoSessionPlatform.Create());
            gameObject.AddComponent<DemoSessionHapticsButton>();
            gameObject.AddComponent<DemoSessionRecenterButton>();
            gameObject.AddComponent<DemoRecenterWatch>();
            if (settings.Hands) gameObject.AddComponent<DemoHands>().SetStyle(DemoHands.ResolveStyle(DemoSession.Ticket, settings.HandStyle));
            if (settings.PauseMenu && settings.CurrentDemoId != DemoSwitchSettings.HubDemoId)
            {
                DemoPause.Configure(settings.TryResolveTarget(DemoSwitchSettings.HubDemoId, out var hub) ? hub.PackageName : null);
                gameObject.AddComponent<DemoPauseInput>().Gesture = settings.PauseGesture;
            }
        }

        /// <summary>The started application is in front (<see cref="DemoAppHandoff"/>): release 7710 and run the usual switch cleanup.</summary>
        internal void StopForHandoff(string nextDemoId)
        {
            StopListener();
            DemoSwitch.NotifyBeforeSwitch(nextDemoId);
        }

        /// <summary>
        /// Starts an allowlisted application; this one finishes once it has gone to the background
        /// (<see cref="DemoAppHandoff"/>). <paramref name="onFailed"/> receives the error when it is still in front after the timeout.
        /// </summary>
        public bool SwitchLocal(string demoId, Action<string> onFailed = null)
        {
            if (_controlBusy || DemoAppHandoff.IsPending || DemoAppHandoff.IsFinished) return false;
            if (!_settings.TryResolveTarget(demoId, out var target))
            {
                Debug.LogError("[Demo Switch] Demo ID is not in the local allowlist: " + demoId);
                return false;
            }

            if (!_launcher.TryLaunch(target, null, out var error))
            {
                Debug.LogError("[Demo Switch] " + error);
                return false;
            }
            DemoAppHandoff.Begin(demoId, onFailed, Time.realtimeSinceStartup);
            return true;
        }

        private void Update()
        {
            if (_transport == null) return;
            DemoSwitchFrameDrain.Drain(_transport.Inbox, 8, Handle);
            if (Time.realtimeSinceStartup >= _nextDropLogTime)
            {
                var dropped = _transport.Inbox.TakeDroppedCount();
                if (dropped > 0)
                    Debug.LogWarning("[Demo Switch] Dropped " + dropped + " UDP datagrams because the 64-message inbox was full.");
                _nextDropLogTime = Time.realtimeSinceStartup + 5f;
            }
        }

        private void Handle(DemoSwitchDatagram datagram)
        {
            string json;
            try
            {
                json = StrictUtf8.GetString(datagram.Payload);
            }
            catch (DecoderFallbackException)
            {
                Debug.LogWarning("[Demo Switch] Rejected payload: invalid UTF-8.");
                return;
            }

            if (DemoSwitchProtocol.TryGetMessageType(json, out var messageType) && messageType == "DISCOVER")
            {
                HandleDiscover(json, datagram.Source);
                return;
            }
            if (messageType == "QUERY")
            {
                HandleQuery(json, datagram.Source);
                return;
            }

            var parsed = DemoSwitchProtocol.ParseCommand(json);
            if (!parsed.Success)
            {
                Debug.LogWarning("[Demo Switch] Rejected payload: " + parsed.ErrorCode);
                return;
            }

            var command = parsed.Command;
            if (!string.IsNullOrEmpty(_settings.SharedSecret) && !DemoSwitchProtocol.Authenticate(command, _settings.SharedSecret))
            {
                SendFailure(datagram.Source, command, "invalid_auth", "Command authentication failed.");
                return;
            }
            if (string.IsNullOrEmpty(_settings.SharedSecret) && !_settings.AllowUnsignedOnIsolatedLan)
            {
                SendFailure(datagram.Source, command, "unsigned_disabled", "Unsigned command mode is disabled.");
                return;
            }
            if (!_foreground)
            {
                SendFailure(datagram.Source, command, "not_allowed", NotForegroundMessage);
                return;
            }
            if (_controlBusy || DemoAppHandoff.IsPending || DemoAppHandoff.IsFinished)
            {
                SendFailure(datagram.Source, command, "not_allowed", "An operation is in progress.");
                return;
            }
            if (command.IsControl)
            {
                HandleControl(command, datagram.Source);
                return;
            }
            if (!_settings.TryResolveTarget(command.DemoId, out var target))
            {
                SendFailure(datagram.Source, command, "not_allowed", "demo_id is not in the local allowlist.");
                return;
            }
            if (!_sequenceGuard.TryAccept(command.ControllerId, command.Sequence))
            {
                SendFailure(datagram.Source, command, "replay", "seq was already accepted or is older.");
                return;
            }

            SendStatus(datagram.Source, new DemoSwitchStatus("ACK", command.ControllerId, command.Sequence,
                command.DemoId, _settings.CurrentDemoId, "ok", string.Empty));
            var context = new DemoSwitchLaunchContext(command.ControllerId, command.Sequence, command.DemoId,
                datagram.Source.Address.ToString(), datagram.Source.Port);
            var source = datagram.Source;
            if (_launcher.TryLaunch(target, context, out var error))
            {
                // The listener and BeforeSwitch stop once this application is in the background; still in front: FAILED.
                DemoAppHandoff.Begin(command.DemoId, failure => SendFailure(source, command, "launch_failed", failure), Time.realtimeSinceStartup);
                return;
            }

            SendStatus(datagram.Source, new DemoSwitchStatus("FAILED", command.ControllerId, command.Sequence,
                command.DemoId, _settings.CurrentDemoId, "launch_failed", error));
        }

        private void HandleDiscover(string json, IPEndPoint source)
        {
            var result = DemoSwitchDiscoveryHandler.Handle(json, _settings.CurrentDemoId, _settings.SharedSecret,
                _settings.AllowUnsignedOnIsolatedLan);
            if (!result.ShouldReply)
            {
                Debug.LogWarning("[Demo Switch] Rejected DISCOVER payload: " + result.ErrorCode);
                return;
            }

            try
            {
                _transport.Send(result.ResponseJson, source);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Demo Switch] HERE send failed: " + exception.Message);
            }
        }

        private void HandleQuery(string json, IPEndPoint source)
        {
            var result = DemoSwitchQueryHandler.Handle(json, query => BuildState(query, _settings.CurrentDemoId, _foreground), _settings.SharedSecret,
                _settings.AllowUnsignedOnIsolatedLan);
            if (!result.ShouldReply)
            {
                Debug.LogWarning("[Demo Switch] Rejected QUERY payload: " + result.ErrorCode);
                return;
            }

            try
            {
                _transport.Send(result.ResponseJson, source);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Demo Switch] STATE send failed: " + exception.Message);
            }
        }

        /// <summary>
        /// STATE values: whether this runtime is in the foreground, haptics output (<see cref="DemoSession.HapticsEnabled"/>),
        /// whether the haptics button is shown (the descriptor supports the toggle and haptics UI is on), the 視線をリセット
        /// button, the shared pause or a scene's <see cref="IDemoAppMenuState"/>, and the active session's step (-1 / 0 outside a session).
        /// </summary>
        internal static DemoSwitchState BuildState(DemoSwitchQuery query, string currentDemoId, bool foreground)
        {
            var ticket = DemoSession.IsActive ? DemoSession.Ticket : null;
            return new DemoSwitchState(query.ControllerId, query.Nonce, currentDemoId, foreground, DemoSession.HapticsEnabled,
                DemoSession.SupportsHapticsToggle && DemoSession.HapticsUiVisible, DemoSession.RecenterUiVisible,
                DemoPause.IsPaused || IsAppMenuOpen(), ticket != null ? ticket.Index : -1, ticket != null ? ticket.Steps.Count : 0);
        }

        private static bool IsAppMenuOpen()
        {
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                if (behaviour.isActiveAndEnabled && behaviour is IDemoAppMenuState menu && menu.IsMenuOpen) return true;
            return false;
        }

        /// <summary>
        /// The adapter for a CONTROL action. haptics_*, recenter / recenter_ui_* and tutorial_start belong to the shared
        /// Demo Session layer. Other actions go to the scene's adapter when it accepts them; otherwise menu_open,
        /// menu_close and restart fall back to the shared pause (<see cref="DemoPause.SharedControls"/>, which accepts
        /// them only where the shared pause is on). The caller rejects the action when the result cannot execute it.
        /// </summary>
        internal static IDemoAppControls ResolveControls(string action, string sceneId, IDemoAppControls sceneControls)
        {
            if (DemoSwitchProtocol.IsHapticsAction(action)) return DemoSession.HapticsControls;
            if (DemoSwitchProtocol.IsRecenterAction(action)) return DemoSession.RecenterControls;
            if (action == "tutorial_start") return DemoSession.TutorialControls;
            if (sceneControls != null && sceneControls.CanExecuteControl(action, sceneId)) return sceneControls;
            return DemoSwitchProtocol.IsSharedPauseAction(action) ? DemoPause.SharedControls : sceneControls;
        }

        private static bool IsSharedLayerAction(string action) => DemoSwitchProtocol.IsHapticsAction(action)
            || DemoSwitchProtocol.IsRecenterAction(action) || action == "tutorial_start";

        private void HandleControl(DemoSwitchCommand command, IPEndPoint source)
        {
            IDemoAppControls sceneControls = null;
            if (!IsSharedLayerAction(command.Action))
                foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                {
                    if (!behaviour.isActiveAndEnabled || !(behaviour is IDemoAppControls candidate)) continue;
                    if (sceneControls != null) { SendFailure(source,command,"not_allowed","Multiple app control adapters."); return; }
                    sceneControls = candidate;
                }
            var adapter = ResolveControls(command.Action, command.SceneId, sceneControls);
            if (command.DemoId != _settings.CurrentDemoId || adapter == null
                || !adapter.CanExecuteControl(command.Action,command.SceneId))
            { SendFailure(source,command,"not_allowed","Current demo or action is not supported."); return; }
            if (!_sequenceGuard.TryAccept(command.ControllerId,command.Sequence))
            { SendFailure(source,command,"replay","seq was already accepted or is older."); return; }
            _controlBusy = true;
            SendStatus(source,new DemoSwitchStatus("ACK",command.ControllerId,command.Sequence,command.DemoId,_settings.CurrentDemoId,"ok",""));
            _control = StartCoroutine(ExecuteControl(adapter,command,source));
        }

        private IEnumerator ExecuteControl(IDemoAppControls adapter, DemoSwitchCommand command, IPEndPoint source)
        {
            IEnumerator operation = null;
            string error = null;
            try { operation = adapter.ExecuteControl(command.Action,command.SceneId); }
            catch (Exception exception) { error = exception.GetType().Name; }
            bool complete = false;
            while (error == null && !complete)
            {
                object next = null;
                try { complete = operation == null || !operation.MoveNext(); if (!complete) next = operation.Current; }
                catch (Exception exception) { error = exception.GetType().Name; }
                if (!complete && error == null) yield return next;
                if (!_foreground) { error = "Application left foreground."; break; }
            }
            try { (operation as IDisposable)?.Dispose(); }
            catch (Exception exception) { error = exception.GetType().Name; }
            // Give newly loaded scene components a frame to initialize before READY.
            yield return null;
            _controlBusy = false;
            if (!_foreground) yield break;
            if (error != null) SendFailure(source,command,"launch_failed",error);
            else SendStatus(source,new DemoSwitchStatus("READY",command.ControllerId,command.Sequence,command.DemoId,_settings.CurrentDemoId,"ok",""));
        }

        private void SendFailure(IPEndPoint endpoint, DemoSwitchCommand command, string code, string message) =>
            SendStatus(endpoint, new DemoSwitchStatus("FAILED", command.ControllerId, command.Sequence,
                command.DemoId, _settings.CurrentDemoId, code, message));

        private bool SendStatus(IPEndPoint endpoint, DemoSwitchStatus status)
        {
            try
            {
                _transport.Send(DemoSwitchProtocol.SerializeStatus(status, _settings.SharedSecret), endpoint);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Demo Switch] Status send failed: " + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Reads a launch context (at start, and a new Intent each time this application comes to the front) and binds
        /// 7710 unless it is bound. A failed bind is retried (<see cref="RetryBind"/>); READY or FAILED/listener_failed
        /// for a launch context follows the final result. Nothing starts again after a hand-over.
        /// </summary>
        private void StartReceiver()
        {
            if (DemoAppHandoff.IsFinished) return;
            CaptureLaunchContext();
            if (_listenerStarted) { ReportReady(); return; }
            if (_bindRetry != null) return;
            if (!TryValidateReceiver(out var error))
            {
                ReportListenerFailure(error);
                return;
            }
            error = TryBind();
            if (error == null) { OnListenerStarted(); return; }
            Debug.LogWarning("[Demo Switch] Could not bind UDP " + _settings.Port + " (" + error + "); retrying every " +
                BindRetryIntervalSeconds + " s, " + BindRetryAttempts + " times.");
            _bindRetry = StartCoroutine(RetryBind(TryBind, OnBindRetryFinished));
        }

        /// <summary>
        /// Calls <paramref name="bind"/> (null = bound, otherwise the error) every <see cref="BindRetryIntervalSeconds"/>,
        /// at most <see cref="BindRetryAttempts"/> times, and reports the result with the number of retries made.
        /// </summary>
        internal static IEnumerator RetryBind(Func<string> bind, Action<string, int> onFinished)
        {
            string error = null;
            for (var attempt = 1; attempt <= BindRetryAttempts; attempt++)
            {
                yield return new WaitForSecondsRealtime(BindRetryIntervalSeconds);
                error = bind();
                if (error == null) { onFinished(null, attempt); yield break; }
            }
            onFinished(error, BindRetryAttempts);
        }

        private void OnBindRetryFinished(string error, int attempts)
        {
            _bindRetry = null;
            if (error == null)
            {
                Debug.Log("[Demo Switch] Bound UDP " + _settings.Port + " after " + attempts + " retries.");
                OnListenerStarted();
                return;
            }
            Debug.LogError("[Demo Switch] Could not bind UDP " + _settings.Port + " after " + attempts + " retries: " + error);
            ReportListenerFailure(error);
        }

        private void OnListenerStarted()
        {
            _listenerStarted = true;
            _multicastLock.Acquire();
            ReportReady();
        }

        private void CaptureLaunchContext()
        {
            if (_launcher == null || !_launcher.TryReadLaunchContext(out var context)) return;
            _pendingLaunchContext = context;
            _sequenceGuard.AdvanceTo(context.ControllerId, context.Sequence);
            DemoSwitch.NotifyLaunchContextDetected();
        }

        private void ReportReady()
        {
            if (!_listenerStarted || !_pendingLaunchContext.HasValue) return;
            var context = _pendingLaunchContext.Value;
            if (SendStatus(context.ControllerEndpoint,
                    new DemoSwitchStatus("READY", context.ControllerId, context.Sequence, context.DemoId,
                        _settings.CurrentDemoId, "ok", string.Empty)))
                _pendingLaunchContext = null;
        }

        private void ReportListenerFailure(string error)
        {
            if (!_pendingLaunchContext.HasValue) return;
            var context = _pendingLaunchContext.Value;
            SendStatus(context.ControllerEndpoint,
                new DemoSwitchStatus("FAILED", context.ControllerId, context.Sequence, context.DemoId,
                    DemoSwitchProtocol.IsIdentifier(_settings.CurrentDemoId) ? _settings.CurrentDemoId : context.DemoId,
                    "listener_failed", error));
        }

        private bool TryValidateReceiver(out string error)
        {
            error = null;
            if (_settings == null) { error = "Settings were not loaded."; return false; }
            if (!_settings.ReceiverEnabled) { error = "Receiver is disabled in settings."; return false; }
            if (!DemoSwitchProtocol.IsIdentifier(_settings.CurrentDemoId))
            {
                Debug.LogError("[Demo Switch] Receiver disabled because Current Demo ID is invalid.");
                error = "Current Demo ID is invalid.";
                return false;
            }
            if (string.IsNullOrEmpty(_settings.SharedSecret))
            {
                if (!_settings.AllowUnsignedOnIsolatedLan)
                {
                    Debug.LogWarning("[Demo Switch] Receiver disabled: set a shared secret or explicitly enable isolated-LAN unsigned mode.");
                    error = "Set a shared secret or explicitly enable isolated-LAN unsigned mode.";
                    return false;
                }
                Debug.LogWarning("[Demo Switch] Unsigned mode is enabled. Use only on an isolated demo LAN.");
            }
            return true;
        }

        /// <summary>Null when 7710 is bound, otherwise the socket error.</summary>
        private string TryBind()
        {
            try
            {
                _transport.Start(_settings.Port);
                return null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }

        /// <summary>Hand-over or teardown: stops a bind retry, releases 7710 and the multicast lock.</summary>
        private void StopListener()
        {
            if (_bindRetry != null) { StopCoroutine(_bindRetry); _bindRetry = null; }
            _multicastLock.Release();
            if (!_listenerStarted) return;
            _transport.Stop();
            _listenerStarted = false;
        }

        // Focus and pause only set the foreground state; the listener keeps running (DISCOVER / QUERY are answered).
        private void OnApplicationFocus(bool hasFocus)
        {
            _foreground = hasFocus;
            if (hasFocus) StartReceiver();
        }

        private void OnApplicationPause(bool paused)
        {
            _foreground = !paused;
            if (paused)
            {
                if (_control != null) StopCoroutine(_control);
                _control = null;
                _controlBusy = false;
            }
            else StartReceiver();
        }

        private void OnDestroy()
        {
            StopListener();
            _transport?.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}
