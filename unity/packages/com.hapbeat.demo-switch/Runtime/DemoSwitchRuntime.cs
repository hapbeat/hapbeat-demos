using System;
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
        }

        public bool SwitchLocal(string demoId)
        {
            if (!_settings.TryResolveTarget(demoId, out var target))
            {
                Debug.LogError("[Demo Switch] Demo ID is not in the local allowlist: " + demoId);
                return false;
            }

            StopListener();
            DemoSwitch.NotifyBeforeSwitch(demoId);
            if (_launcher.TryLaunch(target, null, out var error)) return true;
            Debug.LogError("[Demo Switch] " + error);
            TryStartListener(out _);
            return false;
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
            StopListener();
            DemoSwitch.NotifyBeforeSwitch(command.DemoId);
            var context = new DemoSwitchLaunchContext(command.ControllerId, command.Sequence, command.DemoId,
                datagram.Source.Address.ToString(), datagram.Source.Port);
            if (_launcher.TryLaunch(target, context, out var error)) return;

            SendStatus(datagram.Source, new DemoSwitchStatus("FAILED", command.ControllerId, command.Sequence,
                command.DemoId, _settings.CurrentDemoId, "launch_failed", error));
            TryStartListener(out _);
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
            if (paused) StopListener(); else StartForegroundReceiver();
        }

        private void OnDestroy()
        {
            _transport?.Dispose();
            if (Instance == this) Instance = null;
        }
    }
}
