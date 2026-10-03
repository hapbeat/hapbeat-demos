using System;
using System.Collections;
using System.Net;
using System.Text;
using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    internal sealed class DemoSwitchRuntime : MonoBehaviour
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private DemoSwitchSettings _settings;
        private DemoSwitchUdpTransport _transport;
        private DemoSwitchSequenceGuard _sequenceGuard;
        private IDemoSwitchLaunchAdapter _launcher;
        private bool _foreground = true;
        private bool _listenerStarted;
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
            StartForegroundReceiver();
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

        private void HandleControl(DemoSwitchCommand command, IPEndPoint source)
        {
            IDemoAppControls adapter = null;
            // haptics_* and recenter / recenter_ui_* belong to the shared Demo Session layer, never to the scene's control adapter.
            if (DemoSwitchProtocol.IsHapticsAction(command.Action)) adapter = DemoSession.HapticsControls;
            else if (DemoSwitchProtocol.IsRecenterAction(command.Action)) adapter = DemoSession.RecenterControls;
            else foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (!behaviour.isActiveAndEnabled || !(behaviour is IDemoAppControls candidate)) continue;
                if (adapter != null) { SendFailure(source,command,"not_allowed","Multiple app control adapters."); return; }
                adapter = candidate;
            }
            if (command.DemoId != _settings.CurrentDemoId || adapter == null
                || !adapter.CanExecuteControl(command.Action,command.SceneId))
            { SendFailure(source,command,"not_allowed","Current demo or action is not supported."); return; }
            if (!_sequenceGuard.TryAccept(command.ControllerId,command.Sequence))
            { SendFailure(source,command,"replay","seq was already accepted or is older."); return; }
            _controlBusy = true;
            SendStatus(source,new DemoSwitchStatus("ACK",command.ControllerId,command.Sequence,command.DemoId,_settings.CurrentDemoId,"ok",""));
            StartCoroutine(ExecuteControl(adapter,command,source));
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

        private void StartForegroundReceiver()
        {
            CaptureLaunchContext();
            if (!TryStartListener(out var error))
            {
                ReportListenerFailure(error);
                return;
            }
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

        private bool TryStartListener(out string error)
        {
            error = null;
            if (!_foreground) { error = "Application is not in the foreground."; return false; }
            if (_listenerStarted) return true;
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

            try
            {
                _transport.Start(_settings.Port);
                _listenerStarted = true;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError("[Demo Switch] Could not bind UDP " + _settings.Port + ": " + exception.Message);
                error = exception.Message;
                return false;
            }
        }

        private void StopListener()
        {
            if (!_listenerStarted) return;
            _transport.Stop();
            _listenerStarted = false;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            _foreground = hasFocus;
            if (hasFocus) StartForegroundReceiver(); else StopListener();
        }

        private void OnApplicationPause(bool paused)
        {
            _foreground = !paused;
            if (paused) { StopAllCoroutines(); _controlBusy = false; StopListener(); }
            else StartForegroundReceiver();
        }

        private void OnDestroy()
        {
            _transport?.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}
