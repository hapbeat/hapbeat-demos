using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// Applies this device's `hapbeat-device.json` to the Hapbeat SDK once, as soon as
    /// <see cref="HapbeatManager.Instance"/> exists. Compiled only when `com.hapbeat.sdk` is installed;
    /// no scene component or demo code is required.
    /// </summary>
    internal sealed class DemoDeviceAddressApplier : MonoBehaviour
    {
        private DemoDeviceAddress _address;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var address = DemoDeviceAddress.LoadForThisDevice();
            if (address == null) return;
            var host = new GameObject("Hapbeat Device Address");
            DontDestroyOnLoad(host);
            host.AddComponent<DemoDeviceAddressApplier>()._address = address;
        }

        // HapbeatManager resolves its build-forced and PlayerPrefs overrides in Awake, so the
        // first Update after it exists applies the file on top of them.
        private void Update()
        {
            if (TryApply(_address, HapbeatManager.Instance)) Destroy(gameObject);
        }

        /// <summary>
        /// Sets the file's axes as a non-persistent override. An unspecified (-1) axis passes the
        /// manager's current effective value, because the SDK reads -1 as "disable this axis".
        /// Build-forced axes are kept by the SDK itself.
        /// </summary>
        internal static bool TryApply(DemoDeviceAddress address, HapbeatManager manager)
        {
            if (address == null || manager == null) return false;
            address.Resolve(manager.OverridePlayer, manager.OverrideGroup, out var player, out var group);
            manager.SetAddressOverride(player, group, persist: false);
            Debug.Log(DemoDeviceAddress.FormatLog(manager.OverridePlayer, manager.OverrideGroup, address.Source));
            return true;
        }
    }
}
