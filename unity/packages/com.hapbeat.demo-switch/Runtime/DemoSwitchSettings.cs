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

        public bool ReceiverEnabled => _receiverEnabled;
        public int Port => _port;
        public string CurrentDemoId => _currentDemoId;
        public string SharedSecret => _sharedSecret;
        public bool AllowUnsignedOnIsolatedLan => _allowUnsignedOnIsolatedLan;

        public bool TryResolveTarget(string demoId, out DemoSwitchTarget target)
        {
            target = null;
            if (!DemoSwitchProtocol.IsIdentifier(demoId)) return false;

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
