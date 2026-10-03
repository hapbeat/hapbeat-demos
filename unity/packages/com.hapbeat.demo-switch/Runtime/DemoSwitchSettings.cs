using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    [Serializable]
    public sealed class DemoSwitchTarget
    {
        [SerializeField] private string _demoId = string.Empty;
        [SerializeField] private string _packageName = string.Empty;
        [SerializeField] private string _activityName = string.Empty;

        public DemoSwitchTarget(string demoId, string packageName, string activityName)
        {
            _demoId = demoId;
            _packageName = packageName;
            _activityName = activityName;
        }

        public string DemoId => _demoId;
        public string PackageName => _packageName;
        public string ActivityName => _activityName;
    }

    [CreateAssetMenu(fileName = ResourceName, menuName = "Hapbeat/Demo Switch Settings")]
    public sealed class DemoSwitchSettings : ScriptableObject
    {
        public const string ResourceName = "HapbeatDemoSwitchSettings";
        public const string HubDemoId = "demo_hub";

        [Header("Shared hub")]
        [Tooltip("Common return destination. Updating this package adds hub return without changing game scenes.")]
        [SerializeField] private DemoSwitchTarget _hub = new DemoSwitchTarget(
            HubDemoId, "jp.hapbeat.demohub", "com.unity3d.player.UnityPlayerGameActivity");

        [Header("Receiver")]
        [SerializeField] private bool _receiverEnabled;
        [SerializeField, Min(1)] private int _port = 7710;
        [SerializeField] private string _currentDemoId = string.Empty;

        [Header("Authentication")]
        [Tooltip("Leave empty only on an isolated demo LAN. Never commit a production secret.")]
        [SerializeField] private string _sharedSecret = string.Empty;
        [Tooltip("Allows unsigned commands when Shared Secret is empty. A warning is logged at startup.")]
        [SerializeField] private bool _allowUnsignedOnIsolatedLan;

        [Header("Local launch allowlist")]
        [SerializeField] private List<DemoSwitchTarget> _targets = new List<DemoSwitchTarget>();

        [Header("Presentation")]
        [Tooltip("Draws the shared tracked hands (DemoHands): Meta's hand mesh from the private assets, else procedural ghost hands. Leave off in demos that render their own hands.")]
        [SerializeField] private bool _hands;
        [Tooltip("Look of the shared hands when the session ticket has no hand_style.")]
        [SerializeField] private DemoHandStyle _handStyle = DemoHandStyle.Ghost;

        public bool ReceiverEnabled => _receiverEnabled;
        public int Port => _port;
        public string CurrentDemoId => _currentDemoId;
        public string SharedSecret => _sharedSecret;
        public bool AllowUnsignedOnIsolatedLan => _allowUnsignedOnIsolatedLan;
        public bool Hands => _hands;
        public DemoHandStyle HandStyle => _handStyle;

        public bool TryResolveTarget(string demoId, out DemoSwitchTarget target)
        {
            target = null;
            if (!DemoSwitchProtocol.IsIdentifier(demoId)) return false;

            // Resolve only the trusted local destination, never a package/activity from the network.
            if (demoId == HubDemoId && _hub != null && _hub.DemoId == HubDemoId)
            {
                target = _hub;
                return !string.IsNullOrWhiteSpace(target.PackageName) &&
                       !string.IsNullOrWhiteSpace(target.ActivityName);
            }

            foreach (var candidate in _targets)
            {
                if (candidate != null && string.Equals(candidate.DemoId, demoId, StringComparison.Ordinal))
                {
                    target = candidate;
                    return !string.IsNullOrWhiteSpace(candidate.PackageName) &&
                           !string.IsNullOrWhiteSpace(candidate.ActivityName);
                }
            }

            return false;
        }

        internal void SetTargetsForTests(List<DemoSwitchTarget> targets) => _targets = targets;
    }
}
