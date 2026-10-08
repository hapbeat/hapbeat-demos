using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hapbeat.DemoSwitch.Tests
{
    public sealed class DemoSwitchPresetDispatchTests
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        const string PresetGet = "{\"version\":1,\"type\":\"PRESET_GET\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\",\"preset\":1,\"from\":0}";

        static string PresetSet(long seq, string demoId = "volley") =>
            "{\"version\":1,\"type\":\"PRESET_SET\",\"controller_id\":\"remote-pixel\",\"seq\":" + seq + ",\"demo_id\":\"demo_hub\",\"preset\":2,\"name\":\"A\",\"visible\":true,\"steps\":[{\"demo_id\":\"" + demoId + "\"}]}";

        static string PresetStart(long seq) =>
            "{\"version\":1,\"type\":\"PRESET_START\",\"controller_id\":\"remote-pixel\",\"seq\":" + seq + ",\"demo_id\":\"demo_hub\",\"preset\":1}";

        sealed class FakeHost : IDemoSwitchPresetHost
        {
            public bool Busy, Stores = true, Installed = true, Starts = true;
            public DemoSwitchPresetCheck Check = DemoSwitchPresetCheck.Ok;
            public int Stored, Started;
            public string StoredName;
            public Action<string> OnFailed;
            public bool IsBusy => Busy;
            public DemoSwitchPreset ReadPreset(int number) =>
                new DemoSwitchPreset("Hub " + number, true, 3, new[] { new DemoSwitchPresetStep("volley", null, false) });
            public DemoSwitchPresetCheck CheckSteps(IReadOnlyList<DemoSwitchPresetStep> steps, out string demoId)
            {
                demoId = Check == DemoSwitchPresetCheck.Ok ? null : steps[0].DemoId;
                return Check;
            }
            public bool TryStorePreset(int number, string name, bool visible, IReadOnlyList<DemoSwitchPresetStep> steps, out string error)
            {
                error = Stores ? null : "disk full";
                if (Stores) { Stored++; StoredName = name; }
                return Stores;
            }
            public bool HasInstalledStep(int number) => Installed;
            public bool TryStartPreset(int number, Action<string> onFailed, out string error)
            {
                error = Starts ? null : "no such activity";
                if (!Starts) return false;
                Started++;
                OnFailed = onFailed;
                DemoAppHandoff.Begin("energy-duel", onFailed, Time.realtimeSinceStartup);
                return true;
            }
        }

        sealed class FakePlatform : IDemoSessionPlatform
        {
            public int Finishes;
            public bool TryTakeStringExtra(string name, out string value) { value = null; return false; }
            public bool TryLaunch(string packageName, string activityName, string ticketJson, out string error) { error = null; return true; }
            public void FinishTask() => Finishes++;
            public bool TryReadOwnAsset(string name, out string text, out string error) { text = null; error = "none"; return false; }
            public bool TryGetOwnComponent(out DemoSessionComponent component) { component = null; return false; }
            public IReadOnlyList<DemoSessionLauncherActivity> ListLauncherActivities() => new DemoSessionLauncherActivity[0];
            public bool TryReadPackageAsset(string packageName, string name, out string text, out string error) { text = null; error = "none"; return false; }
            public bool IsPackageInstalled(string packageName) => false;
        }

        sealed class MemorySequenceStore : IDemoSwitchSequenceStore
        {
            readonly Dictionary<string, long> _sequences = new Dictionary<string, long>();
            public bool TryGet(string controllerId, out long sequence) => _sequences.TryGetValue(controllerId, out sequence);
            public void Set(string controllerId, long sequence) => _sequences[controllerId] = sequence;
        }

        GameObject _go;
        DemoSwitchSettings _settings;
        DemoSwitchUdpTransport _transport;
        UdpClient _receiver;
        DemoSwitchRuntime _runtime;
        FakePlatform _platform;

        [SetUp]
        public void SetUp()
        {
            _platform = new FakePlatform();
            DemoSession.ResetForTests(_platform);
            _go = new GameObject("preset dispatch test");
            _settings = ScriptableObject.CreateInstance<DemoSwitchSettings>();
            _transport = new DemoSwitchUdpTransport();
            _receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            _receiver.Client.ReceiveTimeout = 1000;
            _runtime = _go.AddComponent<DemoSwitchRuntime>();
            typeof(DemoSwitchSettings).GetField("_currentDemoId", Private).SetValue(_settings, DemoSwitchSettings.HubDemoId);
            typeof(DemoSwitchSettings).GetField("_allowUnsignedOnIsolatedLan", Private).SetValue(_settings, true);
            typeof(DemoSwitchRuntime).GetField("_settings", Private).SetValue(_runtime, _settings);
            typeof(DemoSwitchRuntime).GetField("_transport", Private).SetValue(_runtime, _transport);
            typeof(DemoSwitchRuntime).GetField("_sequenceGuard", Private).SetValue(_runtime, new DemoSwitchSequenceGuard(new MemorySequenceStore()));
        }

        [TearDown]
        public void TearDown()
        {
            DemoSwitchPresets.UnregisterHost(DemoSwitchPresets.Host);
            UnityEngine.Object.DestroyImmediate(_go);
            UnityEngine.Object.DestroyImmediate(_settings);
            _transport.Dispose();
            _receiver.Dispose();
            DemoSession.ResetForTests();
            AudioListener.pause = false;
        }

        void Send(string json) =>
            typeof(DemoSwitchRuntime).GetMethod("Handle", Private).Invoke(_runtime,
                new object[] { new DemoSwitchDatagram(Encoding.UTF8.GetBytes(json), (IPEndPoint)_receiver.Client.LocalEndPoint) });

        string Receive()
        {
            IPEndPoint from = null;
            return Encoding.UTF8.GetString(_receiver.Receive(ref from));
        }

        DemoSwitchStatus ReceiveStatus()
        {
            var status = DemoSwitchProtocol.ParseStatus(Receive());
            Assert.That(status.Success, Is.True, status.ErrorMessage);
            return status.Status;
        }

        bool NothingReceived()
        {
            System.Threading.Thread.Sleep(100);
            return _receiver.Available == 0;
        }

        void ExpectStatus(string type, string code, string message = null, string currentDemoId = DemoSwitchSettings.HubDemoId)
        {
            var status = ReceiveStatus();
            Assert.That(status.Type, Is.EqualTo(type));
            Assert.That(status.Code, Is.EqualTo(code));
            Assert.That(status.DemoId, Is.EqualTo(DemoSwitchSettings.HubDemoId));
            Assert.That(status.CurrentDemoId, Is.EqualTo(currentDemoId));
            if (message != null) Assert.That(status.Message, Is.EqualTo(message));
        }

        [Test]
        public void WithoutAHostAllFourTypesAreDroppedWithoutAReply()
        {
            foreach (var json in new[] { PresetGet, PresetSet(1), PresetStart(2),
                         "{\"version\":1,\"type\":\"PRESET\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\",\"preset\":1,\"revision\":0,\"name\":\"\",\"visible\":true,\"step_count\":0,\"from\":0,\"steps\":[]}" })
            {
                LogAssert.Expect(LogType.Warning, new Regex("Rejected"));
                Send(json);
                Assert.That(NothingReceived(), Is.True, json);
            }
        }

        [Test]
        public void OutOfForegroundPresetGetIsAnsweredButSetAndStartAreRefused()
        {
            var host = new FakeHost();
            DemoSwitchPresets.RegisterHost(host);
            typeof(DemoSwitchRuntime).GetField("_foreground", Private).SetValue(_runtime, false);

            Send(PresetGet);
            var preset = JObject.Parse(Receive());
            Assert.That(preset.Value<string>("type"), Is.EqualTo("PRESET"));
            Assert.That(preset.Value<string>("name"), Is.EqualTo("Hub 1"));
            Assert.That(preset.Value<long>("revision"), Is.EqualTo(3));
            Assert.That(((JArray)preset["steps"]).Count, Is.EqualTo(1));

            Send(PresetSet(1));
            ExpectStatus("FAILED", "not_allowed", DemoSwitchRuntime.NotForegroundMessage);
            Send(PresetStart(2));
            ExpectStatus("FAILED", "not_allowed", DemoSwitchRuntime.NotForegroundMessage);
            Assert.That(host.Stored + host.Started, Is.Zero);
        }

        [Test]
        public void PresetSetIsValidatedBeforeAckThenStoredAndReady()
        {
            var host = new FakeHost();
            DemoSwitchPresets.RegisterHost(host);

            host.Busy = true;
            Send(PresetSet(1));
            ExpectStatus("FAILED", "not_allowed");
            host.Busy = false;

            host.Check = DemoSwitchPresetCheck.NotInstalled;
            Send(PresetSet(2, "handdemo"));
            ExpectStatus("FAILED", "not_allowed", "handdemo");
            host.Check = DemoSwitchPresetCheck.UnknownOption;
            Send(PresetSet(3, "boxing"));
            ExpectStatus("FAILED", "invalid_payload", "boxing");
            host.Check = DemoSwitchPresetCheck.Ok;
            Assert.That(host.Stored, Is.Zero, "Nothing is stored when validation fails.");

            Send(PresetSet(4));
            ExpectStatus("ACK", "ok");
            ExpectStatus("READY", "ok");
            Assert.That(host.Stored, Is.EqualTo(1));
            Assert.That(host.StoredName, Is.EqualTo("A"));

            Send(PresetSet(4));
            ExpectStatus("FAILED", "replay");
            Assert.That(host.Stored, Is.EqualTo(1), "A duplicate sequence never executes twice.");

            host.Stores = false;
            Send(PresetSet(5));
            ExpectStatus("ACK", "ok");
            ExpectStatus("FAILED", "launch_failed", "disk full");

            // Validation failures came before the sequence check, so 2 and 3 were never accepted; 5 was.
            Send(PresetSet(5));
            ExpectStatus("FAILED", "replay");
        }

        [Test]
        public void PresetStartSendsReadyWithTheStartedDemoOnceTheHubHasLeft()
        {
            var host = new FakeHost();
            DemoSwitchPresets.RegisterHost(host);

            host.Installed = false;
            Send(PresetStart(1));
            ExpectStatus("FAILED", "not_allowed");
            host.Installed = true;

            host.Starts = false;
            Send(PresetStart(2));
            ExpectStatus("ACK", "ok");
            ExpectStatus("FAILED", "launch_failed", "no such activity");
            host.Starts = true;

            Send(PresetStart(3));
            ExpectStatus("ACK", "ok");
            Assert.That(NothingReceived(), Is.True, "READY waits for the hand-over.");
            Send(PresetStart(4));
            ExpectStatus("FAILED", "not_allowed", "An operation is in progress.");
            DemoAppHandoff.OnBackgrounded();
            ExpectStatus("READY", "ok", currentDemoId: "energy-duel");
            Assert.That(_platform.Finishes, Is.EqualTo(1));
            Assert.That(host.Started, Is.EqualTo(1));
        }

        [Test]
        public void PresetStartThatStaysInFrontFails()
        {
            var host = new FakeHost();
            DemoSwitchPresets.RegisterHost(host);
            Send(PresetStart(1));
            ExpectStatus("ACK", "ok");
            LogAssert.Expect(LogType.Error, new Regex("did not come to the front"));
            DemoAppHandoff.Tick(Time.realtimeSinceStartup + DemoAppHandoff.TimeoutSeconds + 1f);
            ExpectStatus("FAILED", "launch_failed", DemoAppHandoff.NotInFrontError);
            DemoAppHandoff.OnBackgrounded();
            Assert.That(NothingReceived(), Is.True, "No READY after the failure.");
        }
    }
}
