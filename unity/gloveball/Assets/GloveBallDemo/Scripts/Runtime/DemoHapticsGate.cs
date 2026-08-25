using System;
using UnityEngine;

namespace GloveBallDemo.Runtime
{
    /// <summary>
    /// Decides whether this session may put haptics on the network, and enforces the answer.
    ///
    /// Batch runs are what this exists for. SmokePlay and VerifySimulator drive real gameplay and
    /// the SDK broadcasts over the LAN, so an unattended verification run buzzes whatever Hapbeat
    /// happens to be switched on at the developer's desk. A batch editor therefore sends nothing
    /// unless the run asks for it with <see cref="LiveArgument"/> on the command line. Interactive
    /// Editor Play and the player build always send - those are the paths a person is actually
    /// trying the demo on, headset or simulator alike.
    ///
    /// Enforcement is at both ends:
    ///  - this component deactivates the HapbeatManager GameObject before its Awake can run
    ///    (hence the execution order), which stops playback, the periodic PING and the
    ///    CONNECT_STATUS announcement alike. Disabling the component instead would not: Unity
    ///    still calls Awake on a disabled component.
    ///  - HapticEventRelay skips Fire()/Stop(), so no trigger is handed to a dead SDK.
    ///
    /// Counting, logging and the EventReported stream are untouched - those are what the batch
    /// assertions read, and they say nothing about the network.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class DemoHapticsGate : MonoBehaviour
    {
        /// <summary>Command-line switch that opts a batch run back into live sending.</summary>
        public const string LiveArgument = "-gbHapticsLive";

        [Tooltip("The HapbeatManager object, switched off when this session must not send.")]
        [SerializeField] private GameObject _sdkRoot;

        private static bool _resolved;
        private static bool _liveSend;

        /// <summary>
        /// Whether haptics may reach the network in this session. Resolved once per play session.
        /// </summary>
        public static bool LiveSendEnabled
        {
            get
            {
                if (!_resolved)
                {
                    _resolved = true;
                    _liveSend = Resolve();
                }

                return _liveSend;
            }
        }

        private static bool Resolve()
        {
            // A player build and an interactive editor both report false here, so both send.
            if (!Application.isBatchMode)
            {
                return true;
            }

            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg == LiveArgument)
                {
                    return true;
                }
            }

            return false;
        }

        private void Awake()
        {
            if (LiveSendEnabled)
            {
                return;
            }

            if (_sdkRoot != null)
            {
                _sdkRoot.SetActive(false);
            }

            Debug.Log(
                $"[Haptics] live send: OFF (batch run without {LiveArgument}); " +
                "the SDK is switched off and no packets leave this machine");
        }
    }
}
