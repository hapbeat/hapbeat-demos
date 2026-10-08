using System;
using System.Collections.Generic;
using System.Linq;
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

        static string HubSettingsSet(long seq, string visible = "\"volley\"") =>
            "{\"version\":1,\"type\":\"HUB_SETTINGS_SET\",\"controller_id\":\"remote-pixel\",\"seq\":" + seq + ",\"demo_id\":\"demo_hub\",\"haptics_ui\":true,\"recenter_ui\":false,\"hand_style\":\"skin\",\"staff_waiting\":true,\"visible_demos\":[" + visible + "]}";

        static string HubStart(long seq, string demoId = "volley") =>
            "{\"version\":1,\"type\":\"HUB_START\",\"controller_id\":\"remote-pixel\",\"seq\":" + seq + ",\"demo_id\":\"demo_hub\",\"steps\":[{\"demo_id\":\"" + demoId + "\"},{\"demo_id\":\"fps\"}]}";

        static string Control(long seq, string action, string demoId = DemoSwitchSettings.HubDemoId) =>
            "{\"version\":1,\"type\":\"CONTROL\",\"controller_id\":\"remote-pixel\",\"seq\":" + seq + ",\"demo_id\":\"" + demoId + "\",\"action\":\"" + action + "\",\"scene_id\":\"\"}";

        sealed class FakeHost : IDemoSwitchHubHost
        {
            public bool Busy, Stores = true, Installed = true, Starts = true;
            public DemoSwitchPresetCheck Check = DemoSwitchPresetCheck.Ok;
            public int Stored, Started, SettingsStored, TopShown, Replayed;
            public string StoredName;
            public string Screen = DemoSwitchScreens.Main;
            public IReadOnlyList<string> StoredVisible;
            public DemoHandStyle? HandStyleSet;
            public Action<string> OnFailed;
            public bool IsBusy => Busy;
            public string CurrentScreen => Screen;
            public DemoSwitchHubSettings ReadSettings() => DemoSwitchHubProtocolTests.ExampleSettings();
            public DemoSwitchPresetCheck CheckVisibleDemos(IReadOnlyList<string> demoIds, out string demoId)
            {
                demoId = demoIds.FirstOrDefault(id => id != "volley" && id != "fps");
                return demoId == null ? DemoSwitchPresetCheck.Ok : DemoSwitchPresetCheck.NotInstalled;
            }
            public bool TryStoreSettings(bool hapticsUi, bool recenterUi, DemoHandStyle handStyle, bool staffWaiting, IReadOnlyList<string> visibleDemos, out string error)
            {
                error = Stores ? null : "disk full";
                if (Stores) { SettingsStored++; StoredVisible = visibleDemos; }
                return Stores;
            }
            public bool TrySetHandStyle(DemoHandStyle style, out string error)
            {
                error = Stores ? null : "disk full";
                if (Stores) HandStyleSet = style;
                return Stores;
            }
            public bool TryStartSteps(IReadOnlyList<DemoSwitchPresetStep> steps, Action<string> onFailed, out string error)
            {
                error = Starts ? null : "no such activity";
                if (!Starts) return false;
                Started++;
                DemoAppHandoff.Begin(steps[0].DemoId, onFailed, Time.realtimeSinceStartup);
                return true;
            }
            public void ShowTop()
            {
                TopShown++;
                Screen = DemoSwitchScreens.Main;
                Busy = false;
            }
            public bool TryReplay(Action<string> onFailed, out string error)
            {
                error = Starts ? null : "no such activity";
                if (!Starts) return false;
                Replayed++;
                DemoAppHandoff.Begin("volley", onFailed, Time.realtimeSinceStartup);
                return true;
            }
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
            public string LaunchedTicket;
            public bool TryTakeStringExtra(string name, out string value) { value = null; return false; }
            public bool TryLaunch(string packageName, string activityName, string ticketJson, out string error) { error = null; LaunchedTicket = ticketJson; return true; }
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
        GameObject _hands;
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
            DemoSwitchHub.UnregisterHost(DemoSwitchHub.Host);
            UnityEngine.Object.DestroyImmediate(_go);
            UnityEngine.Object.DestroyImmediate(_settings);
            _transport.Dispose();
            _receiver.Dispose();
            DemoSession.ResetForTests();
            AudioListener.pause = false;
            if (_hands != null) UnityEngine.Object.DestroyImmediate(_hands);
            typeof(DemoHands).GetProperty("Instance").SetValue(null, null);
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

        void ExpectStatus(string type, string code, string message = null, string currentDemoId = DemoSwitchSettings.HubDemoId,
            string demoId = DemoSwitchSettings.HubDemoId)
        {
            var status = ReceiveStatus();
            Assert.That(status.Type, Is.EqualTo(type));
            Assert.That(status.Code, Is.EqualTo(code));
            Assert.That(status.DemoId, Is.EqualTo(demoId));
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
            DemoSwitchHub.RegisterHost(host);
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
            DemoSwitchHub.RegisterHost(host);

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
            DemoSwitchHub.RegisterHost(host);

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
            DemoSwitchHub.RegisterHost(host);
            Send(PresetStart(1));
            ExpectStatus("ACK", "ok");
            LogAssert.Expect(LogType.Error, new Regex("did not come to the front"));
            DemoAppHandoff.Tick(Time.realtimeSinceStartup + DemoAppHandoff.TimeoutSeconds + 1f);
            ExpectStatus("FAILED", "launch_failed", DemoAppHandoff.NotInFrontError);
            DemoAppHandoff.OnBackgrounded();
            Assert.That(NothingReceived(), Is.True, "No READY after the failure.");
        }

        // ------------------------------------------------ Hub settings, Hub start and the Hub's CONTROL actions

        const string HubSettingsGet = "{\"version\":1,\"type\":\"HUB_SETTINGS_GET\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\",\"from\":0}";
        // hapbeat-contracts fixtures unsigned_hub_settings (FakeHost.ReadSettings returns its values).
        const string HubSettingsExample = "{\"version\":1,\"type\":\"HUB_SETTINGS\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\",\"revision\":3,\"haptics_ui\":false,\"recenter_ui\":true,\"hand_style\":\"ghost\",\"staff_waiting\":false,\"player\":1,\"group\":-1,\"demo_count\":2,\"from\":0,\"demos\":[{\"demo_id\":\"volley\",\"title\":\"Volley\",\"visible\":true},{\"demo_id\":\"fps\",\"title\":\"FPS\",\"visible\":false}]}";

        // Two steps (volley, then trex-encounter), the first one with もう一度.
        const string SessionTicket = "{\"version\":1,\"session_id\":\"0f3a9c2e7b1d4a56\",\"index\":0,\"haptics_ui\":false,\"steps\":[" +
            "{\"demo_id\":\"volley\",\"title\":\"Volley\",\"package\":\"jp.hapbeat.volley\",\"activity\":\"com.unity3d.player.UnityPlayerGameActivity\",\"options\":{},\"retry\":true}," +
            "{\"demo_id\":\"trex-encounter\",\"title\":\"T-Rex\",\"package\":\"com.hapbeat.trexencounter\",\"activity\":\"com.epicgames.unreal.GameActivity\",\"options\":{},\"retry\":false}]," +
            "\"finish\":{\"package\":\"jp.hapbeat.demohub\",\"activity\":\"com.unity3d.player.UnityPlayerGameActivity\"}}";

        sealed class SessionHost : IDemoSessionHost
        {
            public int Restarts;
            public void ApplyOptions(IReadOnlyDictionary<string, string> options) { }
            public void Restart() => Restarts++;
            public void SetHapticsEnabled(bool enabled) { }
            public void SetGameplayPaused(bool paused) { }
        }

        void SetCurrentDemo(string demoId) => typeof(DemoSwitchSettings).GetField("_currentDemoId", Private).SetValue(_settings, demoId);

        /// <summary>Shared hands registered as <see cref="DemoHands.Instance"/>; never resolved in EditMode, so only the ghost look.</summary>
        DemoHands AddHands()
        {
            _hands = new GameObject("shared hands");
            var hands = _hands.AddComponent<DemoHands>();
            typeof(DemoHands).GetProperty("Instance").SetValue(null, hands);
            return hands;
        }

        [Test]
        public void WithoutAHostHubMessagesAreDroppedAndHubControlsRefused()
        {
            foreach (var json in new[] { HubSettingsGet, HubSettingsSet(1), HubStart(2), HubSettingsExample })
            {
                LogAssert.Expect(LogType.Warning, new Regex("Rejected"));
                Send(json);
                Assert.That(NothingReceived(), Is.True, json);
            }
            Send(Control(3, "hub_top"));
            ExpectStatus("FAILED", "not_allowed");
            Send(Control(4, "hub_replay"));
            ExpectStatus("FAILED", "not_allowed");
        }

        [Test]
        public void OutOfForegroundHubSettingsGetIsAnsweredButSetAndStartAreRefused()
        {
            var host = new FakeHost();
            DemoSwitchHub.RegisterHost(host);
            typeof(DemoSwitchRuntime).GetField("_foreground", Private).SetValue(_runtime, false);

            Send(HubSettingsGet);
            Assert.That(Receive(), Is.EqualTo(HubSettingsExample));
            Send(HubSettingsSet(1));
            ExpectStatus("FAILED", "not_allowed", DemoSwitchRuntime.NotForegroundMessage);
            Send(HubStart(2));
            ExpectStatus("FAILED", "not_allowed", DemoSwitchRuntime.NotForegroundMessage);
            Assert.That(host.SettingsStored + host.Started, Is.Zero);
        }

        [Test]
        public void HubSettingsSetIsValidatedBeforeAckThenStoredAndReady()
        {
            var host = new FakeHost();
            DemoSwitchHub.RegisterHost(host);

            host.Busy = true;
            Send(HubSettingsSet(1));
            ExpectStatus("FAILED", "not_allowed", "The Hub's manage screen is open.");
            host.Busy = false;

            Send(HubSettingsSet(2, "\"volley\",\"trex-encounter\""));
            ExpectStatus("FAILED", "not_allowed", "trex-encounter");
            Assert.That(host.SettingsStored, Is.Zero, "Nothing is stored when a demo is not installed.");

            Send(HubSettingsSet(3, "\"fps\",\"volley\""));
            ExpectStatus("ACK", "ok");
            ExpectStatus("READY", "ok");
            Assert.That(host.StoredVisible, Is.EqualTo(new[] { "fps", "volley" }));
            Send(HubSettingsSet(3));
            ExpectStatus("FAILED", "replay");
            Assert.That(host.SettingsStored, Is.EqualTo(1));

            host.Stores = false;
            Send(HubSettingsSet(4));
            ExpectStatus("ACK", "ok");
            ExpectStatus("FAILED", "launch_failed", "disk full");
        }

        [Test]
        public void HubStartIsValidatedThenReadyNamesTheFirstDemoOnceTheHubHasLeft()
        {
            var host = new FakeHost();
            DemoSwitchHub.RegisterHost(host);

            host.Check = DemoSwitchPresetCheck.UnknownOption;
            Send(HubStart(1, "boxing"));
            ExpectStatus("FAILED", "invalid_payload", "boxing");
            host.Check = DemoSwitchPresetCheck.NotInstalled;
            Send(HubStart(2, "handdemo"));
            ExpectStatus("FAILED", "not_allowed", "handdemo");
            host.Check = DemoSwitchPresetCheck.Ok;

            host.Starts = false;
            Send(HubStart(3));
            ExpectStatus("ACK", "ok");
            ExpectStatus("FAILED", "launch_failed", "no such activity");
            host.Starts = true;

            Send(HubStart(4));
            ExpectStatus("ACK", "ok");
            Assert.That(NothingReceived(), Is.True, "READY waits for the hand-over.");
            DemoAppHandoff.OnBackgrounded();
            ExpectStatus("READY", "ok", currentDemoId: "volley");
            Assert.That(host.Started, Is.EqualTo(1));
        }

        [Test]
        public void HubTopIsAcceptedWhileTheManageScreenIsOpen()
        {
            var host = new FakeHost { Busy = true, Screen = DemoSwitchScreens.Manage };
            DemoSwitchHub.RegisterHost(host);

            Send(PresetSet(1));
            ExpectStatus("FAILED", "not_allowed", "The Hub's manage screen is open.");
            Send(Control(2, "hub_top"));
            ExpectStatus("ACK", "ok");
            Assert.That(host.TopShown, Is.EqualTo(1), "Executed on the application thread right after ACK.");
            Assert.That(host.CurrentScreen, Is.EqualTo(DemoSwitchScreens.Main));
        }

        [Test]
        public void HubReplayOnlyOnTheFinishScreenAndReadyNamesTheStartedDemo()
        {
            var host = new FakeHost();
            DemoSwitchHub.RegisterHost(host);

            Send(Control(1, "hub_replay"));
            ExpectStatus("FAILED", "not_allowed");
            Send(Control(2, "session_next"));
            ExpectStatus("FAILED", "not_allowed", null, DemoSwitchSettings.HubDemoId);

            host.Screen = DemoSwitchScreens.Completion;
            Send(Control(3, "hub_replay"));
            ExpectStatus("ACK", "ok");
            Assert.That(NothingReceived(), Is.True, "READY waits for the hand-over.");
            DemoAppHandoff.OnBackgrounded();
            ExpectStatus("READY", "ok", currentDemoId: "volley");
            Assert.That(host.Replayed, Is.EqualTo(1));
        }

        [Test]
        public void HandStyleNeedsTheSharedHandsAndOnTheHubChangesTheHubSetting()
        {
            var host = new FakeHost();
            DemoSwitchHub.RegisterHost(host);

            Send(Control(1, "hand_style_ghost"));
            ExpectStatus("FAILED", "not_allowed");

            AddHands();
            Send(Control(2, "hand_style_skin"));
            ExpectStatus("FAILED", "not_allowed", null, DemoSwitchSettings.HubDemoId);
            Assert.That(host.HandStyleSet, Is.Null, "Skin needs Meta's hand mesh.");
            Send(Control(3, "hand_style_ghost"));
            ExpectStatus("ACK", "ok");
            Assert.That(host.HandStyleSet, Is.EqualTo(DemoHandStyle.Ghost));
        }

        [Test]
        public void SessionNextLaunchesTheNextStepWithTheCurrentHandStyle()
        {
            SetCurrentDemo("volley");
            Send(Control(1, "session_next", "volley"));
            ExpectStatus("FAILED", "not_allowed", null, "volley", "volley");

            Assert.That(DemoSession.TryBegin(SessionTicket, "volley", null, out var error), Is.True, error);
            AddHands().SetStyle(DemoHandStyle.Skin);
            Send(Control(2, "session_next", "volley"));
            ExpectStatus("ACK", "ok", null, "volley", "volley");
            Assert.That(NothingReceived(), Is.True, "READY waits for the hand-over.");
            Assert.That(_platform.LaunchedTicket, Does.Contain("\"index\":1").And.Contain("\"hand_style\":\"skin\""));
            DemoAppHandoff.OnBackgrounded();
            ExpectStatus("READY", "ok", null, "trex-encounter", "volley");
            Assert.That(_platform.Finishes, Is.EqualTo(1));
        }

        [Test]
        public void SessionRetryOnlyWhileTheCompletionPanelOffersIt()
        {
            SetCurrentDemo("volley");
            Assert.That(DemoSession.TryBegin(SessionTicket, "volley", null, out var error), Is.True, error);
            var sessionHost = new SessionHost();
            DemoSession.RegisterHost(sessionHost);

            Send(Control(1, "session_retry", "volley"));
            ExpectStatus("FAILED", "not_allowed", null, "volley", "volley");

            DemoSession.ShowCompletion();
            Send(Control(2, "session_retry", "volley"));
            ExpectStatus("ACK", "ok", null, "volley", "volley");
            Assert.That(sessionHost.Restarts, Is.EqualTo(1));
            Assert.That(DemoSession.IsCompletionShown, Is.False);
        }

        [Test]
        public void StateReportsTheOptionalFields()
        {
            var query = new DemoSwitchQuery("remote-pixel", "0123456789abcdef", "");
            var state = DemoSwitchRuntime.BuildState(query, "volley", true);
            Assert.That(state.DeviceModel, Is.EqualTo(DemoSwitchProtocol.NormalizeDeviceModel(SystemInfo.deviceModel)));
            Assert.That(state.Editor, Is.True);
            Assert.That(state.Screen, Is.EqualTo(DemoSwitchScreens.Main));
            Assert.That(state.HandStyle, Is.Null, "No shared hands: omitted.");
            Assert.That(DemoSwitchProtocol.ParseState(DemoSwitchProtocol.SerializeState(state, "secret")).Success, Is.True);

            Assert.That(DemoSession.TryBegin(SessionTicket, "volley", null, out var error), Is.True, error);
            DemoSession.ShowCompletion();
            Assert.That(DemoSwitchRuntime.BuildState(query, "volley", true).Screen, Is.EqualTo(DemoSwitchScreens.Completion));

            AddHands().SetStyle(DemoHandStyle.Skin);
            Assert.That(DemoSwitchRuntime.BuildState(query, "volley", true).HandStyle, Is.EqualTo(DemoHandStyle.Skin));
            var hub = new FakeHost { Screen = DemoSwitchScreens.Manage };
            Assert.That(DemoSwitchRuntime.BuildState(query, DemoSwitchSettings.HubDemoId, true, hub).Screen, Is.EqualTo(DemoSwitchScreens.Manage),
                "The Hub reports its own screen.");
        }
    }
}
