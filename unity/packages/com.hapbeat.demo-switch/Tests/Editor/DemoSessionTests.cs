using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hapbeat.DemoSwitch.Tests
{
    public sealed class DemoSessionTests
    {
        // Equivalent to hapbeat-contracts fixtures/sample-demo-session.json (descriptor_volley, ticket).
        const string VolleyDescriptor = @"{""version"":1,""demo_id"":""volley"",""title"":{""ja"":""バレーボール"",""en"":""Volleyball""},""minutes"":3,
 ""supports"":{""haptics_toggle"":true},
 ""options"":[
  {""id"":""scene"",""label"":{""ja"":""モード"",""en"":""Mode""},""default"":""block"",
   ""values"":[{""value"":""block"",""label"":{""ja"":""ブロック"",""en"":""Block""}},{""value"":""receive"",""label"":{""ja"":""レシーブ"",""en"":""Receive""}}]},
  {""id"":""points"",""label"":{""ja"":""点数"",""en"":""Points""},""default"":""7"",""when"":{""scene"":[""block""]},
   ""values"":[{""value"":""3"",""label"":{""ja"":""3点先取""}},{""value"":""5"",""label"":{""ja"":""5点先取""}},{""value"":""7"",""label"":{""ja"":""7点先取""}}]},
  {""id"":""balls"",""label"":{""ja"":""球数"",""en"":""Balls""},""default"":""10"",""when"":{""scene"":[""receive""]},
   ""values"":[{""value"":""10"",""label"":{""ja"":""10球""}},{""value"":""20"",""label"":{""ja"":""20球""}}]}]}";

        const string Ticket = @"{""version"":1,""session_id"":""0f3a9c2e7b1d4a56"",""index"":1,""haptics_ui"":false,
 ""steps"":[
  {""demo_id"":""volley"",""title"":""バレー ブロック 3点"",""package"":""jp.hapbeat.volley"",""activity"":""com.unity3d.player.UnityPlayerGameActivity"",""options"":{""scene"":""block"",""points"":""3""},""retry"":true},
  {""demo_id"":""trex-encounter"",""title"":""T-Rex"",""package"":""com.hapbeat.trexencounter"",""activity"":""com.epicgames.unreal.GameActivity"",""options"":{},""retry"":false}],
 ""finish"":{""package"":""jp.hapbeat.demohub"",""activity"":""com.unity3d.player.UnityPlayerGameActivity""}}";

        static string AtIndex(int index) => Ticket.Replace(@"""index"":1", @"""index"":" + index);

        sealed class FakePlatform : IDemoSessionPlatform
        {
            public bool LaunchSucceeds = true;
            public string LaunchedPackage, LaunchedActivity, LaunchedTicket;
            public int Finishes;
            public bool TryTakeTicketExtra(out string json) { json = null; return false; }
            public bool TryLaunch(string packageName, string activityName, string ticketJson, out string error)
            {
                error = LaunchSucceeds ? null : "no such activity";
                if (!LaunchSucceeds) return false;
                LaunchedPackage = packageName; LaunchedActivity = activityName; LaunchedTicket = ticketJson;
                return true;
            }
            public void FinishTask() => Finishes++;
            public bool TryReadOwnAsset(string name, out string text, out string error) { text = null; error = "none"; return false; }
            public bool TryGetOwnComponent(out DemoSessionComponent component) { component = null; return false; }
            public IReadOnlyList<DemoSessionLauncherActivity> ListLauncherActivities() => new DemoSessionLauncherActivity[0];
            public bool TryReadPackageAsset(string packageName, string name, out string text, out string error) { text = null; error = "none"; return false; }
            public bool IsPackageInstalled(string packageName) => packageName == "jp.hapbeat.demohub";
        }

        sealed class Host : IDemoSessionHost
        {
            public IReadOnlyDictionary<string, string> Options;
            public int Restarts;
            public bool? Haptics, Paused;
            public void ApplyOptions(IReadOnlyDictionary<string, string> options) => Options = options;
            public void Restart() => Restarts++;
            public void SetHapticsEnabled(bool enabled) => Haptics = enabled;
            public void SetGameplayPaused(bool paused) => Paused = paused;
        }

        FakePlatform _platform;

        static DemoSessionDescriptor Volley()
        {
            Assert.That(DemoSessionDescriptor.TryParse(VolleyDescriptor, out var descriptor, out var error), Is.True, error);
            return descriptor;
        }

        [SetUp]
        public void SetUp()
        {
            _platform = new FakePlatform();
            DemoSession.ResetForTests(_platform);
        }

        [TearDown]
        public void TearDown()
        {
            DemoSession.ResetForTests();
            AudioListener.pause = false;
        }

        [Test]
        public void FixtureTicketEntersSessionOnlyForTheIndexedDemo()
        {
            Assert.That(DemoSession.TryBegin(Ticket, "trex-encounter", null, out var error), Is.True, error);
            Assert.That(DemoSession.IsActive, Is.True);
            Assert.That(DemoSession.CurrentStep.Title, Is.EqualTo("T-Rex"));
            Assert.That(DemoSession.Next.IsFinish, Is.True, "Last step leads to finish.");

            DemoSession.ResetForTests(_platform);
            Assert.That(DemoSession.TryBegin(Ticket, "volley", Volley(), out error), Is.False);
            Assert.That(error, Does.Contain("does not match"));
            Assert.That(DemoSession.IsActive, Is.False);

            Assert.That(DemoSession.TryBegin(AtIndex(0), "volley", Volley(), out error), Is.True, error);
            Assert.That(DemoSession.Next.IsFinish, Is.False);
            Assert.That(DemoSession.Next.Step.DemoId, Is.EqualTo("trex-encounter"));
            Assert.That(DemoSession.GetOption("points"), Is.EqualTo("3"));
        }

        [Test]
        public void CompletedTicketIsAcceptedOnlyByTheHub()
        {
            Assert.That(DemoSession.TryBegin(AtIndex(2), "volley", Volley(), out _), Is.False);
            Assert.That(DemoSession.TryBegin(AtIndex(2), DemoSwitchSettings.HubDemoId, null, out var error), Is.True, error);
            Assert.That(DemoSession.IsActive, Is.False);
            Assert.That(DemoSession.IsFinishedSession, Is.True);
        }

        [TestCase("not json")]
        [TestCase("[]")]
        [TestCase(@"{""version"":1}")]
        public void MalformedTicketsAreRejected(string json)
        {
            Assert.That(DemoSessionTicket.TryParse(json, out _, out _), Is.False);
            Assert.That(DemoSession.TryBegin(json, "volley", Volley(), out _), Is.False);
            Assert.That(DemoSession.IsActive, Is.False);
        }

        [TestCase(@"""version"":1,", @"""version"":2,")]
        [TestCase(@"""index"":1", @"""index"":3")]
        [TestCase(@"""index"":1", @"""index"":-1")]
        [TestCase(@"""index"":1", @"""index"":1.5")]
        [TestCase(@"""session_id"":""0f3a9c2e7b1d4a56""", @"""session_id"":""0F3A9C2E7B1D4A56""")]
        [TestCase(@"""haptics_ui"":false", @"""haptics_ui"":0")]
        [TestCase(@"""haptics_ui"":false", @"""haptics_ui"":false,""extra"":1")]
        [TestCase(@"""retry"":false", @"""retry"":false,""x"":1")]
        [TestCase(@"""title"":""T-Rex""", @"""title"":""""")]
        [TestCase(@"""title"":""T-Rex""", @"""title"":""12345678901234567890123456789012345678901""")]
        [TestCase(@"""package"":""jp.hapbeat.volley""", @"""package"":""volley""")]
        [TestCase(@"""activity"":""com.epicgames.unreal.GameActivity""", @"""activity"":""com.epicgames/GameActivity""")]
        [TestCase(@"""points"":""3""", @"""points"":3")]
        [TestCase(@"""points"":""3""", @"""Points"":""3""")]
        [TestCase(@"""demo_id"":""volley""", @"""demo_id"":""Volley""")]
        [TestCase(@"""finish"":{", @"""finish"":{""x"":1,")]
        [TestCase(@"""index"":1,", @"""index"":1,""index"":0,")]
        [TestCase(@"""steps"":[", @"""steps"":/*c*/[")]
        public void SchemaViolationsAreRejected(string from, string to)
        {
            var json = Ticket.Replace(from, to);
            Assert.That(json, Is.Not.EqualTo(Ticket));
            Assert.That(DemoSessionTicket.TryParse(json, out _, out _), Is.False, to);
        }

        [Test]
        public void TitleLengthCountsCodePoints()
        {
            var forty = string.Concat(Enumerable.Repeat("体", 40));
            Assert.That(DemoSessionTicket.TryParse(Ticket.Replace(@"""title"":""T-Rex""", @"""title"":""" + forty + @""""), out _, out var error), Is.True, error);
            Assert.That(DemoSessionTicket.TryParse(Ticket.Replace(@"""title"":""T-Rex""", @"""title"":""" + forty + @"体"""), out _, out _), Is.False);
        }

        [Test]
        public void TicketsOver16KiBAreRejectedEvenIfOtherwiseValid()
        {
            var padded = Ticket.Replace(@"""version"":1,", @"""version"":1," + new string(' ', DemoSessionTicket.MaxBytes));
            Assert.That(System.Text.Encoding.UTF8.GetByteCount(padded), Is.GreaterThan(DemoSessionTicket.MaxBytes));
            Assert.That(DemoSessionTicket.TryParse(padded, out _, out var error), Is.False);
            Assert.That(error, Does.Contain("16384"));
            Assert.That(DemoSession.TryBegin(padded, "trex-encounter", null, out _), Is.False);
        }

        [Test]
        public void TicketRoundTripsThroughJson()
        {
            Assert.That(DemoSessionTicket.TryParse(Ticket, out var ticket, out var error), Is.True, error);
            Assert.That(DemoSessionTicket.TryParse(ticket.ToJson(), out var again, out error), Is.True, error);
            Assert.That(again.Steps[0].Options["points"], Is.EqualTo("3"));
            Assert.That(again.Finish.PackageName, Is.EqualTo("jp.hapbeat.demohub"));
            Assert.That(DemoSessionJson.IsSessionId(DemoSessionTicket.NewSessionId()), Is.True);
        }

        [Test]
        public void HandStyleIsOptionalAndCarriedToTheNextStep()
        {
            Assert.That(DemoSessionTicket.TryParse(Ticket, out var plain, out var error), Is.True, error);
            Assert.That(plain.HandStyle, Is.Null);
            Assert.That(plain.ToJson(), Does.Not.Contain("hand_style"), "Omitted when not chosen.");

            var skin = Ticket.Replace(@"""haptics_ui"":false", @"""haptics_ui"":false,""hand_style"":""skin""");
            Assert.That(skin, Does.Contain("hand_style"));
            Assert.That(DemoSessionTicket.TryParse(skin, out var ticket, out error), Is.True, error);
            Assert.That(ticket.HandStyle, Is.EqualTo(DemoHandStyle.Skin));
            Assert.That(ticket.WithIndex(2, true).HandStyle, Is.EqualTo(DemoHandStyle.Skin));
            Assert.That(ticket.WithSession(DemoSessionTicket.NewSessionId()).HandStyle, Is.EqualTo(DemoHandStyle.Skin));
            Assert.That(DemoSessionTicket.TryParse(ticket.WithHandStyle(DemoHandStyle.Ghost).ToJson(), out var ghost, out error), Is.True, error);
            Assert.That(ghost.HandStyle, Is.EqualTo(DemoHandStyle.Ghost));
            Assert.That(DemoSessionTicket.TryParse(skin.Replace(@"""skin""", @"""glove"""), out _, out error), Is.False);
            Assert.That(error, Does.Contain("hand_style"));

            Assert.That(DemoHands.ResolveStyle(ticket, DemoHandStyle.Ghost), Is.EqualTo(DemoHandStyle.Skin), "The ticket wins.");
            Assert.That(DemoHands.ResolveStyle(plain, DemoHandStyle.Skin), Is.EqualTo(DemoHandStyle.Skin), "Omitted: the settings default.");
            Assert.That(DemoHands.ResolveStyle(null, DemoHandStyle.Ghost), Is.EqualTo(DemoHandStyle.Ghost));

            Assert.That(DemoSession.TryBegin(skin.Replace(@"""index"":1", @"""index"":0"), "volley", Volley(), out error), Is.True, error);
            Assert.That(DemoSession.LaunchNext(out error), Is.True, error);
            Assert.That(DemoSessionTicket.TryParse(_platform.LaunchedTicket, out var sent, out error), Is.True, error);
            Assert.That(sent.HandStyle, Is.EqualTo(DemoHandStyle.Skin));
        }

        [Test]
        public void OptionsAreNormalizedToDescriptorDefaultsWithWarnings()
        {
            var descriptor = Volley();
            var warnings = new List<string>();
            var options = descriptor.Normalize(new Dictionary<string, string> { ["scene"] = "block", ["points"] = "9", ["speed"] = "fast" }, warnings);
            Assert.That(options, Is.EquivalentTo(new Dictionary<string, string> { ["scene"] = "block", ["points"] = "7" }));
            Assert.That(warnings.Count, Is.EqualTo(2));

            warnings.Clear();
            options = descriptor.Normalize(new Dictionary<string, string> { ["scene"] = "dance" }, warnings);
            Assert.That(options, Is.EquivalentTo(new Dictionary<string, string> { ["scene"] = "block", ["points"] = "7" }));
            Assert.That(warnings.Count, Is.EqualTo(2), "Unknown value and missing active option.");
        }

        [Test]
        public void WhenControlsWhichOptionsAreActive()
        {
            var descriptor = Volley();
            var warnings = new List<string>();
            var receive = descriptor.Normalize(new Dictionary<string, string> { ["scene"] = "receive", ["balls"] = "20", ["points"] = "3" }, warnings);
            Assert.That(receive, Is.EquivalentTo(new Dictionary<string, string> { ["scene"] = "receive", ["balls"] = "20" }));
            Assert.That(warnings.Single(), Does.Contain("inactive"));
            var values = new Dictionary<string, string> { ["scene"] = "receive" };
            Assert.That(descriptor.IsActive(descriptor.FindOption("points"), values), Is.False);
            Assert.That(descriptor.IsActive(descriptor.FindOption("balls"), values), Is.True);
            Assert.That(descriptor.IsActive(descriptor.FindOption("points"), new Dictionary<string, string>()), Is.True, "Default scene is block.");

            Assert.That(DemoSession.TryBegin(AtIndex(0).Replace(@"""scene"":""block"",""points"":""3""", @"""scene"":""receive"",""points"":""3"""), "volley", descriptor, out var error), Is.True, error);
            Assert.That(DemoSession.GetOption("balls"), Is.EqualTo("10"));
            Assert.That(DemoSession.GetOption("points"), Is.Null);
        }

        [TestCase(@"""default"":""block""", @"""default"":""spike""")]
        [TestCase(@"""when"":{""scene"":[""block""]}", @"""when"":{""mode"":[""block""]}")]
        [TestCase(@"""when"":{""scene"":[""block""]}", @"""when"":{}")]
        [TestCase(@"""haptics_toggle"":true", @"""haptics_toggle"":""yes""")]
        [TestCase(@"""minutes"":3", @"""minutes"":0")]
        [TestCase(@"""ja"":""バレーボール""", @"""ja"":""""")]
        public void InvalidDescriptorsAreRejected(string from, string to)
        {
            var json = VolleyDescriptor.Replace(from, to);
            Assert.That(json, Is.Not.EqualTo(VolleyDescriptor));
            Assert.That(DemoSessionDescriptor.TryParse(json, out _, out _), Is.False, to);
        }

        [Test]
        public void HostReceivesOptionsAndHapticsOnRegistration()
        {
            Assert.That(DemoSession.TryBegin(AtIndex(0), "volley", Volley(), out var error), Is.True, error);
            var host = new Host();
            DemoSession.RegisterHost(host);
            Assert.That(host.Options["points"], Is.EqualTo("3"));
            Assert.That(host.Haptics, Is.True, "Every step starts with haptics on.");

            DemoSession.ResetForTests(_platform);
            var normal = new Host();
            DemoSession.RegisterHost(normal);
            Assert.That(normal.Options, Is.Null, "Normal launch never applies session options.");
            Assert.That(normal.Haptics, Is.True);
        }

        [Test]
        public void HapticsControlRequiresTheDescriptorToggle()
        {
            foreach (var action in new[] { "haptics_on", "haptics_off", "haptics_ui_show", "haptics_ui_hide" })
            {
                var json = "{\"version\":1,\"type\":\"CONTROL\",\"controller_id\":\"m5-main\",\"seq\":5,\"demo_id\":\"volley\",\"action\":\"" + action + "\",\"scene_id\":\"\"}";
                Assert.That(DemoSwitchProtocol.ParseCommand(json).Success, Is.True, action);
                Assert.That(DemoSwitchProtocol.ParseCommand(json.Replace("\"scene_id\":\"\"", "\"scene_id\":\"block\"")).Success, Is.False, action);
                Assert.That(DemoSession.HapticsControls.CanExecuteControl(action, ""), Is.False, "No descriptor: not_allowed.");
            }

            var withoutToggle = VolleyDescriptor.Replace(@"""haptics_toggle"":true", @"""haptics_toggle"":false");
            Assert.That(DemoSessionDescriptor.TryParse(withoutToggle, out var noToggle, out _), Is.True);
            DemoSession.ResetForTests(_platform, "volley", noToggle);
            Assert.That(DemoSession.HapticsControls.CanExecuteControl("haptics_off", ""), Is.False);

            DemoSession.ResetForTests(_platform, "volley", Volley());
            var host = new Host();
            DemoSession.RegisterHost(host);
            Assert.That(DemoSession.HapticsControls.CanExecuteControl("haptics_off", ""), Is.True, "Accepted without a session.");
            Assert.That(DemoSession.HapticsControls.CanExecuteControl("menu_open", ""), Is.False);
            Run(DemoSession.HapticsControls.ExecuteControl("haptics_off", ""));
            Assert.That(DemoSession.HapticsEnabled, Is.False);
            Assert.That(host.Haptics, Is.False);
            Run(DemoSession.HapticsControls.ExecuteControl("haptics_ui_show", ""));
            Assert.That(DemoSession.HapticsUiVisible, Is.True);
            Run(DemoSession.HapticsControls.ExecuteControl("haptics_ui_hide", ""));
            Run(DemoSession.HapticsControls.ExecuteControl("haptics_on", ""));
            Assert.That(DemoSession.HapticsUiVisible, Is.False);
            Assert.That(host.Haptics, Is.True);
        }

        static void Run(IEnumerator operation)
        {
            while (operation.MoveNext()) { }
        }

        [Test]
        public void CompletionButtonsWaitOneSecondAndRetryRestartsTheStep()
        {
            Assert.That(DemoSession.TryBegin(AtIndex(0), "volley", Volley(), out var error), Is.True, error);
            var host = new Host();
            DemoSession.RegisterHost(host);
            int shown = 0, closed = 0;
            DemoSession.CompletionShown += () => shown++;
            DemoSession.Closed += () => closed++;
            Assert.That(DemoSession.ShowCompletion(), Is.True);
            var panel = Object.FindAnyObjectByType<DemoSessionCompletionPanel>();
            Assert.That(panel, Is.Not.Null);
            Assert.That(shown, Is.EqualTo(1));
            Assert.That(host.Paused, Is.True);
            var now = Time.realtimeSinceStartup;
            Assert.That(panel.Panel.AcceptsInputAt(now + 0.9f), Is.False);
            Assert.That(panel.Panel.AcceptsInputAt(now + DemoSessionCompletionPanel.InputDelaySeconds + 0.01f), Is.True);
            Assert.That(panel.RetryButton, Is.Not.Null);
            Assert.That(panel.ForwardButton.Label, Is.EqualTo("次へ：T-Rex"));

            panel.RetryButton.Press();
            Assert.That(host.Restarts, Is.EqualTo(1));
            Assert.That(host.Paused, Is.False);
            Assert.That(closed, Is.EqualTo(1));
            Assert.That(DemoSession.IsCompletionShown, Is.False);
        }

        [Test]
        public void LastStepOffersFinishWithoutRetry()
        {
            Assert.That(DemoSession.TryBegin(Ticket, "trex-encounter", null, out var error), Is.True, error);
            DemoSession.ShowCompletion();
            var panel = Object.FindAnyObjectByType<DemoSessionCompletionPanel>();
            Assert.That(panel.RetryButton, Is.Null);
            Assert.That(panel.ForwardButton.Label, Is.EqualTo("デモを終了"));
        }

        [Test]
        public void PokeFiresOnlyAfterArmingInFront()
        {
            var tracker = new DemoSessionPokeTracker();
            Assert.That(tracker.Update(0.01f, true), Is.False, "A tip already inside never fires.");
            Assert.That(tracker.Update(-0.03f, true), Is.False);
            Assert.That(tracker.Update(-0.01f, true), Is.False, "Hovering in front.");
            Assert.That(tracker.Update(0.001f, true), Is.True);
            Assert.That(tracker.Update(0.01f, true), Is.False, "One press per approach.");
            Assert.That(tracker.Update(-0.01f, true), Is.False, "Hysteresis: not re-armed yet.");
            Assert.That(tracker.Update(0.001f, true), Is.False);
            tracker.Update(-0.03f, true);
            Assert.That(tracker.Update(0.001f, false), Is.False, "Crossing outside a button consumes the approach.");
            Assert.That(tracker.Update(0.001f, true), Is.False);
        }

        [Test]
        public void PanelPokeAndRayActivateButtonsAfterTheDelay()
        {
            var panel = DemoSessionPanel.Create("test panel", new Vector2(300, 200));
            try
            {
                int presses = 0;
                panel.AddButton(Vector2.zero, new Vector2(120, 60), "OK", 20, () => presses++);
                panel.EnableInputAfter(1f);
                var now = Time.realtimeSinceStartup;
                var front = panel.transform.position - panel.transform.forward * 0.03f;
                var through = panel.transform.position + panel.transform.forward * 0.002f;
                panel.ProcessPointers(new[] { new DemoSessionPointer { Id = 1, IsPoke = true, Position = front } }, now);
                panel.ProcessPointers(new[] { new DemoSessionPointer { Id = 1, IsPoke = true, Position = through } }, now);
                Assert.That(presses, Is.Zero, "Disabled during the delay.");
                panel.ProcessPointers(new[] { new DemoSessionPointer { Id = 1, IsPoke = true, Position = front } }, now + 1.1f);
                panel.ProcessPointers(new[] { new DemoSessionPointer { Id = 1, IsPoke = true, Position = through } }, now + 1.1f);
                Assert.That(presses, Is.EqualTo(1));

                var origin = panel.transform.position - panel.transform.forward * 0.5f;
                var ray = new DemoSessionPointer { Id = 3, Position = origin, Direction = panel.transform.forward };
                panel.ProcessPointers(new[] { ray }, now + 2f);
                ray.TriggerHeld = true;
                panel.ProcessPointers(new[] { ray }, now + 2f);
                panel.ProcessPointers(new[] { ray }, now + 2f);
                Assert.That(presses, Is.EqualTo(2), "Trigger edge fires once.");
                var miss = new DemoSessionPointer { Id = 2, Position = origin + panel.transform.right * 0.2f, Direction = panel.transform.forward };
                panel.ProcessPointers(new[] { miss }, now + 3f);
                miss.TriggerHeld = true;
                panel.ProcessPointers(new[] { miss }, now + 3f);
                Assert.That(presses, Is.EqualTo(2), "Outside the button.");
            }
            finally { Object.DestroyImmediate(panel.gameObject); }
        }

        [Test]
        public void PressedButtonReportsHeldUntilReleased()
        {
            var panel = DemoSessionPanel.Create("test panel", new Vector2(300, 200));
            try
            {
                var button = panel.AddButton(Vector2.zero, new Vector2(120, 60), "Hold", 20, null);
                panel.EnableInputAfter(0f);
                var now = Time.realtimeSinceStartup + 1f;
                var front = panel.transform.position - panel.transform.forward * 0.03f;
                var through = panel.transform.position + panel.transform.forward * 0.002f;
                panel.ProcessPointers(new[] { new DemoSessionPointer { Id = 1, IsPoke = true, Position = through } }, now);
                Assert.That(button.Held, Is.False, "A tip that came from behind does not hold.");
                panel.ProcessPointers(new[] { new DemoSessionPointer { Id = 1, IsPoke = true, Position = front } }, now);
                panel.ProcessPointers(new[] { new DemoSessionPointer { Id = 1, IsPoke = true, Position = through } }, now);
                Assert.That(button.Held, Is.True);
                panel.ProcessPointers(new[] { new DemoSessionPointer { Id = 1, IsPoke = true, Position = through } }, now);
                Assert.That(button.Held, Is.True, "Still pressed.");
                panel.ProcessPointers(new[] { new DemoSessionPointer { Id = 1, IsPoke = true, Position = front } }, now);
                Assert.That(button.Held, Is.False, "Released.");

                var ray = new DemoSessionPointer { Id = 3, Position = panel.transform.position - panel.transform.forward * 0.5f, Direction = panel.transform.forward, TriggerHeld = true };
                panel.ProcessPointers(new[] { ray }, now);
                Assert.That(button.Held, Is.True);
                ray.TriggerHeld = false;
                panel.ProcessPointers(new[] { ray }, now);
                Assert.That(button.Held, Is.False);
            }
            finally { Object.DestroyImmediate(panel.gameObject); }
        }

        [Test]
        public void LaunchNextAdvancesIndexAndCarriesHapticsUi()
        {
            Assert.That(DemoSession.TryBegin(AtIndex(0), "volley", Volley(), out var error), Is.True, error);
            DemoSession.SetHapticsUiVisible(true);
            Assert.That(DemoSession.LaunchNext(out error), Is.True, error);
            Assert.That(_platform.LaunchedPackage, Is.EqualTo("com.hapbeat.trexencounter"));
            Assert.That(_platform.LaunchedActivity, Is.EqualTo("com.epicgames.unreal.GameActivity"));
            Assert.That(DemoSessionTicket.TryParse(_platform.LaunchedTicket, out var sent, out error), Is.True, error);
            Assert.That(sent.Index, Is.EqualTo(1));
            Assert.That(sent.HapticsUi, Is.True);
            Assert.That(sent.SessionId, Is.EqualTo("0f3a9c2e7b1d4a56"));
            Assert.That(_platform.Finishes, Is.Zero, "Finishes only after leaving the foreground.");
            DemoAppHandoff.OnBackgrounded();
            Assert.That(_platform.Finishes, Is.EqualTo(1));
        }

        [Test]
        public void HandOverStopsAndFinishesOnceAfterLeavingTheForeground()
        {
            Assert.That(DemoSession.TryBegin(AtIndex(0), "volley", Volley(), out var error), Is.True, error);
            var host = new Host();
            DemoSession.RegisterHost(host);
            var switches = new List<string>();
            System.Action<string> onSwitch = id => switches.Add(id);
            DemoSwitch.BeforeSwitch += onSwitch;
            try
            {
                Assert.That(DemoSession.LaunchNext(out error), Is.True, error);
                Assert.That(DemoAppHandoff.IsPending, Is.True);
                Assert.That(_platform.Finishes, Is.Zero, "Not before this application is in the background.");
                Assert.That(host.Haptics, Is.True, "Nothing stops at the start call.");
                Assert.That(AudioListener.pause, Is.False);
                Assert.That(switches, Is.Empty);
                Assert.That(DemoSession.LaunchNext(out error), Is.False, "One hand-over at a time.");

                DemoAppHandoff.OnBackgrounded();
                DemoAppHandoff.OnBackgrounded();
                DemoAppHandoff.Tick(Time.realtimeSinceStartup + DemoAppHandoff.TimeoutSeconds + 1f);
                Assert.That(_platform.Finishes, Is.EqualTo(1), "Focus loss and pause finish exactly once.");
                Assert.That(host.Haptics, Is.False);
                Assert.That(AudioListener.pause, Is.True);
                Assert.That(switches, Is.EqualTo(new[] { "trex-encounter" }));
                Assert.That(DemoSession.LaunchNext(out _), Is.False, "Nothing starts after finishing.");
            }
            finally { DemoSwitch.BeforeSwitch -= onSwitch; }
        }

        [Test]
        public void HandOverThatStaysInFrontShowsTheErrorAndKeepsRunning()
        {
            Assert.That(DemoSession.TryBegin(AtIndex(0), "volley", Volley(), out var error), Is.True, error);
            var host = new Host();
            DemoSession.RegisterHost(host);
            DemoSession.ShowCompletion();
            var panel = Object.FindAnyObjectByType<DemoSessionCompletionPanel>();
            var start = Time.realtimeSinceStartup;
            panel.ForwardButton.Press();
            Assert.That(DemoAppHandoff.IsPending, Is.True);
            Assert.That(panel.ErrorText, Is.Empty);

            // No panel input while leaving.
            var ray = new DemoSessionPointer { Id = 3, Position = panel.RetryButton.Rect.position - panel.transform.forward * 0.5f, Direction = panel.transform.forward };
            panel.Panel.ProcessPointers(new[] { ray }, start + 10f);
            ray.TriggerHeld = true;
            panel.Panel.ProcessPointers(new[] { ray }, start + 10f);
            Assert.That(host.Restarts, Is.Zero);
            Assert.That(DemoSession.IsCompletionShown, Is.True);

            DemoAppHandoff.Tick(start + DemoAppHandoff.TimeoutSeconds - 0.1f);
            Assert.That(DemoAppHandoff.IsPending, Is.True);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("did not come to the front"));
            DemoAppHandoff.Tick(start + DemoAppHandoff.TimeoutSeconds + 0.1f);
            Assert.That(DemoAppHandoff.IsPending, Is.False);
            Assert.That(panel.ErrorText, Does.Contain(DemoAppHandoff.NotInFrontError));
            Assert.That(DemoSession.IsCompletionShown, Is.True);
            DemoAppHandoff.OnBackgrounded();
            Assert.That(_platform.Finishes, Is.Zero, "A later focus loss does not finish.");
            Assert.That(host.Haptics, Is.True);
            Assert.That(AudioListener.pause, Is.False);
            Assert.That(DemoSession.LaunchNext(out error), Is.True, "Can try again.");
        }

        [Test]
        public void LastStepLaunchesFinishAndFailureKeepsRunning()
        {
            Assert.That(DemoSession.TryBegin(Ticket, "trex-encounter", null, out var error), Is.True, error);
            _platform.LaunchSucceeds = false;
            LogAssert.Expect(LogType.Error, "[Demo Session] Launch failed: no such activity");
            Assert.That(DemoSession.LaunchNext(out error), Is.False);
            Assert.That(error, Is.EqualTo("no such activity"));
            Assert.That(_platform.Finishes, Is.Zero);
            Assert.That(AudioListener.pause, Is.False);

            _platform.LaunchSucceeds = true;
            Assert.That(DemoSession.LaunchNext(out error), Is.True, error);
            Assert.That(_platform.LaunchedPackage, Is.EqualTo("jp.hapbeat.demohub"));
            Assert.That(DemoSessionTicket.TryParse(_platform.LaunchedTicket, out var sent, out _), Is.True);
            Assert.That(sent.IsFinished, Is.True);
            Assert.That(sent.Index, Is.EqualTo(2));
        }

        [Test]
        public void CompletionErrorIsShownWhenLaunchFails()
        {
            Assert.That(DemoSession.TryBegin(AtIndex(0), "volley", Volley(), out var error), Is.True, error);
            _platform.LaunchSucceeds = false;
            DemoSession.ShowCompletion();
            var panel = Object.FindAnyObjectByType<DemoSessionCompletionPanel>();
            LogAssert.Expect(LogType.Error, "[Demo Session] Launch failed: no such activity");
            panel.ForwardButton.Press();
            Assert.That(panel.ErrorText, Does.Contain("no such activity"));
            Assert.That(DemoSession.IsCompletionShown, Is.True);
        }

        [Test]
        public void HapticsButtonSitsLowLeftAndKeepsAFixedLabelWidth()
        {
            var pose = DemoSessionHapticsButton.TargetPose(Vector3.up * 1.6f, Vector3.forward);
            var offset = pose.position - Vector3.up * 1.6f;
            Assert.That(offset.magnitude, Is.EqualTo(DemoSessionHapticsButton.Distance).Within(1e-4f));
            Assert.That(offset.x, Is.LessThan(0f), "Left");
            Assert.That(offset.y, Is.LessThan(0f), "Below");
            Assert.That(Mathf.Asin(-offset.y / offset.magnitude) * Mathf.Rad2Deg, Is.EqualTo(35f).Within(0.1f));
            var lookingDown = DemoSessionHapticsButton.TargetPose(Vector3.up * 1.6f, new Vector3(0, -1, 1));
            Assert.That(Vector3.Distance(lookingDown.position, pose.position), Is.LessThan(1e-4f), "Pitch does not move the button.");
            Assert.That(DemoSessionHapticsButton.Label(true), Is.EqualTo("触覚 ON"));
            Assert.That(DemoSessionHapticsButton.Label(false), Is.EqualTo("触覚 OFF"));
        }

        static GameObject MainCamera(Vector3 position, Vector3 forward)
        {
            var go = new GameObject("test camera", typeof(Camera)) { tag = "MainCamera" };
            go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));
            return go;
        }

        /// <summary>Asserts the HMD-front placement relative to whichever camera Camera.main returns (an open scene may have its own).</summary>
        static void AssertInFrontOfHead(Transform panel, float distance, float drop, string message)
        {
            var head = Camera.main.transform;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            var expected = head.position + forward * distance + Vector3.down * drop;
            Assert.That(Vector3.Distance(panel.position, expected), Is.LessThan(1e-3f), message);
        }

        [Test]
        public void CompletionPanelUsesTheSceneAnchorElseTheHmdFront()
        {
            var camera = MainCamera(new Vector3(0, 1.6f, 0), Vector3.forward);
            var anchorObject = new GameObject("test anchor");
            try
            {
                Assert.That(DemoSession.TryBegin(AtIndex(0), "volley", Volley(), out var error), Is.True, error);
                DemoSession.ShowCompletion();
                var front = Object.FindAnyObjectByType<DemoSessionCompletionPanel>().transform;
                AssertInFrontOfHead(front, DemoSessionCompletionPanel.Distance, DemoSessionCompletionPanel.Drop, "No anchor: in front of the HMD.");
                DemoSession.CloseCompletion();

                anchorObject.transform.SetPositionAndRotation(new Vector3(1f, 1.2f, 1f), Quaternion.Euler(0, 180, 0));
                var anchor = anchorObject.AddComponent<DemoSessionPanelAnchor>();
                Assert.That(anchor.FaceUser, Is.True, "Default: yaw toward the participant.");
                DemoSession.ShowCompletion();
                var placed = Object.FindAnyObjectByType<DemoSessionCompletionPanel>().transform;
                Assert.That(Vector3.Distance(placed.position, anchorObject.transform.position), Is.LessThan(1e-4f));
                var expected = Vector3.ProjectOnPlane(anchorObject.transform.position - Camera.main.transform.position, Vector3.up).normalized;
                Assert.That(Vector3.Dot(placed.forward, expected), Is.GreaterThan(0.9999f), "+Z points away from the viewer, level.");
                DemoSession.CloseCompletion();

                anchor.FaceUser = false;
                DemoSession.ShowCompletion();
                placed = Object.FindAnyObjectByType<DemoSessionCompletionPanel>().transform;
                Assert.That(Quaternion.Angle(placed.rotation, anchorObject.transform.rotation), Is.LessThan(0.01f), "The anchor's own rotation.");
                DemoSession.CloseCompletion();

                anchor.enabled = false;
                DemoSession.ShowCompletion();
                AssertInFrontOfHead(Object.FindAnyObjectByType<DemoSessionCompletionPanel>().transform,
                    DemoSessionCompletionPanel.Distance, DemoSessionCompletionPanel.Drop, "A disabled anchor is ignored.");
            }
            finally
            {
                Object.DestroyImmediate(anchorObject);
                Object.DestroyImmediate(camera);
            }
        }

        [Test]
        public void PauseStopsGameplayHapticsAndAudioAndResumeRestoresThem()
        {
            var camera = MainCamera(new Vector3(0, 1.6f, 0), Vector3.right);
            try
            {
                var host = new Host();
                DemoSession.RegisterHost(host);
                var changes = new List<bool>();
                DemoPause.PausedChanged += paused => changes.Add(paused);
                Assert.That(DemoPause.Pause(), Is.True, "Works without a session.");
                Assert.That(DemoPause.IsPaused, Is.True);
                Assert.That(host.Paused, Is.True);
                Assert.That(host.Haptics, Is.False);
                Assert.That(AudioListener.pause, Is.True);
                var panel = DemoPause.Panel;
                AssertInFrontOfHead(panel.transform, DemoPausePanel.Distance, DemoPausePanel.Drop, "In front of the HMD.");
                Assert.That(panel.Panel.AcceptsInputAt(Time.realtimeSinceStartup + DemoPausePanel.InputDelaySeconds + 0.01f), Is.True);

                host.Haptics = null;
                DemoSession.SetHapticsEnabled(true);
                Assert.That(host.Haptics, Is.Null, "The host stays silent while paused.");
                DemoSession.SetHapticsEnabled(false);
                Assert.That(host.Haptics, Is.Null);

                panel.ResumeButton.Press();
                Assert.That(DemoPause.IsPaused, Is.False);
                Assert.That(host.Paused, Is.False);
                Assert.That(host.Haptics, Is.False, "Resume applies the session's haptics switch.");
                Assert.That(AudioListener.pause, Is.False);
                Assert.That(changes, Is.EqualTo(new[] { true, false }));

                DemoSession.SetHapticsEnabled(true);
                DemoPause.Toggle();
                Assert.That(DemoPause.IsPaused, Is.True);
                DemoPause.Toggle();
                Assert.That(DemoPause.IsPaused, Is.False, "The menu input toggles.");
                Assert.That(host.Haptics, Is.True);
            }
            finally { Object.DestroyImmediate(camera); }
        }

        [Test]
        public void PauseRestartAndHubButtons()
        {
            var host = new Host();
            DemoSession.RegisterHost(host);
            DemoPause.Pause();
            Assert.That(DemoPause.Panel.HubButton, Is.Null, "No Hub configured.");
            DemoPause.Panel.RestartButton.Press();
            Assert.That(host.Restarts, Is.EqualTo(1));
            Assert.That(DemoPause.IsPaused, Is.False);
            Assert.That(host.Paused, Is.False);

            DemoPause.ResetForTests("com.example.missinghub");
            DemoPause.Pause();
            Assert.That(DemoPause.Panel.HubButton, Is.Null, "Hub not installed: no button.");
            DemoPause.Resume();

            DemoPause.ResetForTests("jp.hapbeat.demohub");
            var launches = 0;
            DemoPause.HubLauncher = _ => { launches++; return false; };
            DemoPause.Pause();
            Assert.That(DemoPause.Panel.HubButton.Label, Is.EqualTo("Hub に戻る"));
            Assert.That(DemoPause.Panel.NextButton, Is.Null, "No session: no next step.");
            DemoPause.Panel.HubButton.Press();
            Assert.That(launches, Is.EqualTo(1));
            Assert.That(DemoPause.Panel.ErrorText, Is.EqualTo(DemoPause.HubFailed), "A failed launch keeps the panel with an error.");
            Assert.That(_platform.Finishes, Is.Zero);

            // The Hub started but this application stays in front: error, no finish.
            DemoPause.HubLauncher = onFailed => { launches++; DemoAppHandoff.Begin(DemoSwitchSettings.HubDemoId, onFailed, 0f); return true; };
            DemoPause.Panel.ShowError(string.Empty);
            DemoPause.Panel.HubButton.Press();
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("did not come to the front"));
            DemoAppHandoff.Tick(DemoAppHandoff.TimeoutSeconds + 0.1f);
            Assert.That(DemoPause.Panel.ErrorText, Is.EqualTo(DemoPause.HubFailed));
            Assert.That(_platform.Finishes, Is.Zero);

            DemoPause.Panel.HubButton.Press();
            Assert.That(_platform.Finishes, Is.Zero, "Not before the Hub is in front.");
            DemoAppHandoff.OnBackgrounded();
            Assert.That(_platform.Finishes, Is.EqualTo(1), "Hub in front: this runtime finishes once.");
            Assert.That(host.Haptics, Is.False);
            Assert.That(AudioListener.pause, Is.True);
        }

        [Test]
        public void PauseOffersTheNextStepInASession()
        {
            Assert.That(DemoSession.TryBegin(AtIndex(0), "volley", Volley(), out var error), Is.True, error);
            DemoPause.ResetForTests("jp.hapbeat.demohub");
            DemoPause.HubLauncher = _ => false;
            DemoPause.Pause();
            var panel = DemoPause.Panel;
            Assert.That(panel.NextButton.Label, Is.EqualTo("次へ：T-Rex"));
            var order = new[] { panel.ResumeButton, panel.RestartButton, panel.NextButton, panel.HubButton }.Select(b => b.Rect.anchoredPosition.y).ToArray();
            Assert.That(order, Is.Ordered.Descending, "再開 / 最初からやり直す / 次へ / Hub に戻る");

            _platform.LaunchSucceeds = false;
            LogAssert.Expect(LogType.Error, "[Demo Session] Launch failed: no such activity");
            panel.NextButton.Press();
            Assert.That(panel.ErrorText, Does.Contain("no such activity"));
            _platform.LaunchSucceeds = true;
            panel.NextButton.Press();
            Assert.That(_platform.LaunchedPackage, Is.EqualTo("com.hapbeat.trexencounter"));
            Assert.That(DemoSessionTicket.TryParse(_platform.LaunchedTicket, out var sent, out error), Is.True, error);
            Assert.That(sent.Index, Is.EqualTo(1));
            DemoAppHandoff.OnBackgrounded();
            Assert.That(_platform.Finishes, Is.EqualTo(1));

            DemoSession.ResetForTests(_platform);
            Assert.That(DemoSession.TryBegin(Ticket, "trex-encounter", null, out error), Is.True, error);
            DemoPause.Pause();
            Assert.That(DemoPause.Panel.NextButton.Label, Is.EqualTo("デモを終了"), "Last step: finish.");
            Assert.That(DemoPause.Panel.HubButton, Is.Null);
        }

        [Test]
        public void RecenterPlacesOpenPanelsInFrontOfTheHeadAgain()
        {
            var camera = MainCamera(new Vector3(0, 1.6f, 0), Vector3.forward);
            var anchorObject = new GameObject("test anchor");
            try
            {
                DemoPause.Pause();
                camera.transform.SetPositionAndRotation(new Vector3(0.4f, 1.5f, -0.2f), Quaternion.LookRotation(Vector3.back));
                DemoRecenter.Notify();
                AssertInFrontOfHead(DemoPause.Panel.transform, DemoPausePanel.Distance, DemoPausePanel.Drop, "Pause panel after a recenter.");
                DemoPause.Resume();

                anchorObject.transform.position = new Vector3(1f, 1.2f, 1f);
                anchorObject.AddComponent<DemoSessionPanelAnchor>();
                Assert.That(DemoSession.TryBegin(AtIndex(0), "volley", Volley(), out var error), Is.True, error);
                DemoSession.ShowCompletion();
                var completion = Object.FindAnyObjectByType<DemoSessionCompletionPanel>();
                var inputFrom = completion.Panel.AcceptsInputAt(Time.realtimeSinceStartup + 0.5f);
                camera.transform.rotation = Quaternion.LookRotation(Vector3.left);
                DemoRecenter.Notify();
                AssertInFrontOfHead(completion.transform, DemoSessionCompletionPanel.Distance, DemoSessionCompletionPanel.Drop, "Also from a scene anchor.");
                Assert.That(completion.Panel.AcceptsInputAt(Time.realtimeSinceStartup + 0.5f), Is.EqualTo(inputFrom), "The input delay does not restart.");
            }
            finally
            {
                Object.DestroyImmediate(anchorObject);
                Object.DestroyImmediate(camera);
            }
        }

        [Test]
        public void HeadPoseJumpWithinOneFrameIsARecenter()
        {
            var head = new Vector3(0.1f, 1.6f, 0.2f);
            var facing = Quaternion.Euler(0, 20, 0);
            Assert.That(DemoRecenter.IsJump(head, facing, head, Quaternion.Euler(0, 95, 0), 0.014f), Is.True, "Heading reset");
            Assert.That(DemoRecenter.IsJump(head, facing, Vector3.up * 1.6f + Vector3.right * 0.5f, facing, 0.014f), Is.True, "Position reset");
            Assert.That(DemoRecenter.IsJump(head, facing, head + Vector3.right * 0.02f, Quaternion.Euler(0, 28, 0), 0.014f), Is.False, "A fast head turn (570°/s)");
            Assert.That(DemoRecenter.IsJump(head, facing, head, Quaternion.Euler(50, 20, 0), 0.014f), Is.False, "Looking down");
            Assert.That(DemoRecenter.IsJump(head, facing, head, Quaternion.Euler(0, 140, 0), 0.5f), Is.False, "Long frames are not compared");
        }

        [Test]
        public void CapturedButtonFollowsItsPointerOffTheButton()
        {
            var panel = DemoSessionPanel.Create("test panel", new Vector2(300, 200));
            try
            {
                var button = panel.AddButton(new Vector2(0, 50), new Vector2(100, 40), "Grip", 20, null);
                panel.EnableInputAfter(0f);
                var now = Time.realtimeSinceStartup + 1f;
                Vector3 At(float x, float y, float depth) => panel.transform.TransformPoint(new Vector3(x, y, 0)) + panel.transform.forward * depth;
                panel.ProcessPointers(new[] { new DemoSessionPointer { Id = 1, IsPoke = true, Position = At(0, 50, -0.03f) } }, now);
                panel.ProcessPointers(new[] { new DemoSessionPointer { Id = 1, IsPoke = true, Position = At(0, 50, 0.002f) } }, now);
                Assert.That(button.Held && button.Captured, Is.True);
                panel.ProcessPointers(new[] { new DemoSessionPointer { Id = 1, IsPoke = true, Position = At(10, -60, 0.01f) } }, now);
                Assert.That(button.Held, Is.False, "Off the button.");
                Assert.That(button.Captured, Is.True, "Still pressed into the panel.");
                Assert.That(Vector2.Distance(button.CapturePoint, new Vector2(10, -60)), Is.LessThan(0.01f));
                panel.ProcessPointers(new[] { new DemoSessionPointer { Id = 1, IsPoke = true, Position = At(10, -60, -0.01f) } }, now);
                Assert.That(button.Captured, Is.False, "Released.");

                var ray = new DemoSessionPointer { Id = 3, Position = At(0, 50, -0.5f), Direction = panel.transform.forward };
                panel.ProcessPointers(new[] { ray }, now);
                ray.TriggerHeld = true;
                panel.ProcessPointers(new[] { ray }, now);
                ray.Position = At(-40, -30, -0.5f);
                panel.ProcessPointers(new[] { ray }, now);
                Assert.That(button.Captured, Is.True);
                Assert.That(Vector2.Distance(button.CapturePoint, new Vector2(-40, -30)), Is.LessThan(0.01f));
                ray.TriggerHeld = false;
                panel.ProcessPointers(new[] { ray }, now);
                Assert.That(button.Captured, Is.False);
            }
            finally { Object.DestroyImmediate(panel.gameObject); }
        }

        [Test]
        public void PauseAndCompletionNeverStack()
        {
            Assert.That(DemoSession.TryBegin(AtIndex(0), "volley", Volley(), out var error), Is.True, error);
            var host = new Host();
            DemoSession.RegisterHost(host);
            DemoPause.Pause();
            DemoSession.ShowCompletion();
            Assert.That(DemoPause.IsPaused, Is.False, "Completion closes the pause first.");
            Assert.That(DemoSession.IsCompletionShown, Is.True);
            Assert.That(host.Paused, Is.True);
            Assert.That(AudioListener.pause, Is.False);
            Assert.That(DemoPause.Pause(), Is.False, "No pause over the completion panel.");
            Assert.That(DemoPause.IsPaused, Is.False);
        }

        // Left hand, fingers up, palm toward -Z (thumb/index side at -X).
        static readonly Vector3 Wrist = new Vector3(0, 1.1f, 0.3f), MiddleProximal = new Vector3(0, 1.19f, 0.3f),
            IndexProximal = new Vector3(-0.02f, 1.18f, 0.3f), LittleProximal = new Vector3(0.03f, 1.17f, 0.3f), Palm = new Vector3(0, 1.15f, 0.3f);

        [Test]
        public void PalmPinchHoldNeedsTheLeftPalmTowardTheFaceForTwoSeconds()
        {
            Assert.That(DemoPalmPinchHold.PalmFacesHead(Wrist, MiddleProximal, IndexProximal, LittleProximal, Palm, new Vector3(0, 1.3f, 0)), Is.True);
            Assert.That(DemoPalmPinchHold.PalmFacesHead(Wrist, MiddleProximal, IndexProximal, LittleProximal, Palm, new Vector3(0, 1.3f, 0.6f)), Is.False, "Back of the hand toward the face.");

            var hold = new DemoPalmPinchHold();
            var fired = 0;
            for (var frame = 0; frame < 18; frame++) fired += hold.Update(true, 0.01f, 0.1f) ? 1 : 0;
            Assert.That(fired, Is.Zero, "1.8 s");
            Assert.That(hold.Update(true, 0.025f, 0.1f), Is.False, "Holding tolerates up to 3 cm.");
            Assert.That(hold.Update(true, 0.01f, 0.1001f), Is.True, "2 s");
            for (var frame = 0; frame < 30; frame++) Assert.That(hold.Update(true, 0.01f, 0.1f), Is.False, "Once per hold.");
            hold.Update(true, 0.05f, 0.1f);
            Assert.That(hold.Update(true, 0.02f, 0.1f), Is.False, "Starting needs a pinch under 1.5 cm.");
            Assert.That(hold.Held, Is.Zero);
            for (var frame = 0; frame < 25; frame++) Assert.That(hold.Update(false, 0.01f, 0.1f), Is.False, "Not facing.");
            for (var frame = 0; frame < 19; frame++) hold.Update(true, 0.01f, 0.1f);
            hold.Update(false, 0.01f, 0.1f);
            Assert.That(hold.Update(true, 0.01f, 0.1f), Is.False, "Breaking the sign restarts the 2 s.");
        }

        [Test]
        public void PauseInputWithoutDevicesNeverFires()
        {
            var go = new GameObject("pause input");
            try
            {
                var input = go.AddComponent<DemoPauseInput>();
                input.Gesture = DemoPauseGesture.PalmPinchHold;
                Assert.That(input.Sample(0.1f), Is.False, "No controller or hand in the Editor.");
            }
            finally { Object.DestroyImmediate(go); }
        }

#if UNITY_ANDROID
        [Test]
        public void ManifestQueryForTheHubIsAddedOnce()
        {
            const string manifest = "<manifest>\n  <application android:label=\"x\" />\n</manifest>";
            var once = Editor.DemoSwitchAndroidManifest.AddPackageQuery(manifest, "jp.hapbeat.demohub");
            Assert.That(once, Does.Contain("<queries><package android:name=\"jp.hapbeat.demohub\" /></queries>"));
            Assert.That(once.IndexOf("<queries>"), Is.LessThan(once.IndexOf("<application")));
            Assert.That(Editor.DemoSwitchAndroidManifest.AddPackageQuery(once, "jp.hapbeat.demohub"), Is.EqualTo(once));
        }
#endif

        [Test]
        public void BundledFontHasEveryFixedGlyph()
        {
            var font = DemoSessionFont.Get();
            Assert.That(font.name, Does.Contain("Noto"));
            foreach (var character in "体験完了もう一度次へ：デモを終了触覚ONOFF起動できませんでした/0123456789一時停止再開最初からやり直すHubに戻る")
                Assert.That(font.HasCharacter(character), Is.True, character.ToString());
        }
    }
}
