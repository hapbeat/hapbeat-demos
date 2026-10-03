using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hapbeat.DemoSwitch.Tests
{
    /// <summary>
    /// Applies the device address to a real HapbeatManager created in Edit Mode. Its Awake does not run
    /// there, so no client is created and nothing is sent; SetAddressOverride only updates the routing state.
    /// </summary>
    public sealed class DemoDeviceAddressApplierTests
    {
        GameObject _host;
        HapbeatConfig _config;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            if (_config != null) Object.DestroyImmediate(_config);
        }

        HapbeatManager CreateManager(int buildPlayer = -1, int buildGroup = -1)
        {
            _host = new GameObject("HapbeatManager (test)");
            var manager = _host.AddComponent<HapbeatManager>();
            _config = ScriptableObject.CreateInstance<HapbeatConfig>();
            _config.enableLogging = false;
            _config.buildOverridePlayer = buildPlayer;
            _config.buildOverrideGroup = buildGroup;
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("_config").objectReferenceValue = _config;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return manager;
        }

        [Test]
        public void WaitsWhileHapbeatManagerIsMissing()
        {
            Assert.IsFalse(DemoDeviceAddressApplier.TryApply(new DemoDeviceAddress(1, 2), null));
        }

        [Test]
        public void AppliesBothAxesAndLogsEffectiveValues()
        {
            var manager = CreateManager();
            LogAssert.Expect(LogType.Log, "HAPBEAT_DEVICE_ADDRESS player=1 group=2 source=/data/hapbeat-device.json");
            Assert.IsTrue(DemoDeviceAddressApplier.TryApply(new DemoDeviceAddress(1, 2, "/data/hapbeat-device.json"), manager));
            Assert.AreEqual(1, manager.OverridePlayer);
            Assert.AreEqual(2, manager.OverrideGroup);
        }

        [Test]
        public void UnspecifiedAxisKeepsTheCurrentOverride()
        {
            var manager = CreateManager();
            manager.SetAddressOverride(5, 6, persist: false); // e.g. restored from PlayerPrefs
            LogAssert.Expect(LogType.Log, "HAPBEAT_DEVICE_ADDRESS player=5 group=2 source=(none)");
            Assert.IsTrue(DemoDeviceAddressApplier.TryApply(new DemoDeviceAddress(-1, 2), manager));
            Assert.AreEqual(5, manager.OverridePlayer);
            Assert.AreEqual(2, manager.OverrideGroup);
        }

        [Test]
        public void BothUnspecifiedChangesNothing()
        {
            var manager = CreateManager();
            manager.SetAddressOverride(-1, 7, persist: false);
            LogAssert.Expect(LogType.Log, "HAPBEAT_DEVICE_ADDRESS player=-1 group=7 source=(none)");
            Assert.IsTrue(DemoDeviceAddressApplier.TryApply(new DemoDeviceAddress(-1, -1), manager));
            Assert.AreEqual(-1, manager.OverridePlayer);
            Assert.AreEqual(7, manager.OverrideGroup);
        }

        [Test]
        public void BuildForcedAxisWins()
        {
            var manager = CreateManager(buildGroup: 9);
            LogAssert.Expect(LogType.Log, "HAPBEAT_DEVICE_ADDRESS player=3 group=9 source=(none)");
            Assert.IsTrue(DemoDeviceAddressApplier.TryApply(new DemoDeviceAddress(3, 4), manager));
            Assert.AreEqual(3, manager.OverridePlayer);
            Assert.AreEqual(9, manager.OverrideGroup);
        }
    }
}
