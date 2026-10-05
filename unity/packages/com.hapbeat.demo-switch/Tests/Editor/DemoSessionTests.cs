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
            public bool TryTakeStringExtra(string name, out string value) { value = null; return false; }
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

        sealed class RecenterHost : IDemoSessionHost, IDemoSessionRecenter
        {
            public int Recenters;
            public void ApplyOptions(IReadOnlyDictionary<string, string> options) { }
            public void Restart() { }
            public void SetHapticsEnabled(bool enabled) { }
            public void SetGameplayPaused(bool paused) { }
            public void RecenterToStart() => Recenters++;
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

        sealed class TutorialHost : IDemoSessionHost, IDemoSessionTutorial
        {
            public int Tutorials, Restarts;
            public bool? Paused;
            public void ApplyOptions(IReadOnlyDictionary<string, string> options) { }
            public void Restart() => Restarts++;
            public void SetHapticsEnabled(bool enabled) { }
            public void SetGameplayPaused(bool paused) => Paused = paused;
            public void StartTutorial() => Tutorials++;
        }

        sealed class SceneMenu : IDemoAppControls
        {
            public bool Handles;
            public bool CanExecuteControl(string action, string sceneId) => Handles && action == "menu_open";
            public IEnumerator ExecuteControl(string action, string sceneId) { yield break; }
        }

        [Test]
        public void MenuAndRestartFallBackToTheSharedPauseOnlyWhereItIsOn()
        {
            foreach (var action in new[] { "menu_open", "menu_close", "restart" })
            {
                var adapter = DemoSwitchRuntime.ResolveControls(action, "", null);
                Assert.That(adapter, Is.SameAs(DemoPause.SharedControls), action);
                Assert.That(adapter.CanExecuteControl(action, ""), Is.False, action + ": no shared pause (Hub, own-menu demos): not_allowed.");
            }
            Assert.That(DemoSwitchRuntime.ResolveControls("scene", "block", null), Is.Null, "Scenes need the app's adapter.");

            DemoPause.Configure(null);
            var shared = DemoPause.SharedControls;
            Assert.That(shared.CanExecuteControl("menu_open", ""), Is.True);
            Assert.That(shared.CanExecuteControl("menu_close", ""), Is.True);
            Assert.That(shared.CanExecuteControl("restart", ""), Is.False, "Nothing to restart without a scene host.");
            Assert.That(shared.CanExecuteControl("scene", "block"), Is.False);
            var host = new Host();
            DemoSession.RegisterHost(host);
            Assert.That(shared.CanExecuteControl("restart", ""), Is.True);

            Run(shared.ExecuteControl("menu_open", ""));
            Assert.That(DemoPause.IsPaused, Is.True);
            Assert.That(host.Paused, Is.True);
            Run(shared.ExecuteControl("menu_open", ""));
            Assert.That(DemoPause.IsPaused, Is.True, "Explicit open, not a toggle.");
            Run(shared.ExecuteControl("menu_close", ""));
            Assert.That(DemoPause.IsPaused, Is.False);
            Run(shared.ExecuteControl("menu_close", ""));
            Assert.That(DemoPause.IsPaused, Is.False, "Explicit close, not a toggle.");
            Run(shared.ExecuteControl("menu_open", ""));
            Run(shared.ExecuteControl("restart", ""));
            Assert.That(host.Restarts, Is.EqualTo(1), "Same as the panel's restart button.");
            Assert.That(DemoPause.IsPaused, Is.False);

            // The scene's adapter wins when it handles the action; the rest still falls back.
            var menu = new SceneMenu { Handles = true };
            Assert.That(DemoSwitchRuntime.ResolveControls("menu_open", "", menu), Is.SameAs(menu));
            Assert.That(DemoSwitchRuntime.ResolveControls("restart", "", menu), Is.SameAs(DemoPause.SharedControls));
            menu.Handles = false;
            Assert.That(DemoSwitchRuntime.ResolveControls("menu_open", "", menu), Is.SameAs(DemoPause.SharedControls));
            Assert.That(DemoSwitchRuntime.ResolveControls("haptics_on", "", menu), Is.SameAs(DemoSession.HapticsControls));
            Assert.That(DemoSwitchRuntime.ResolveControls("recenter", "", menu), Is.SameAs(DemoSession.RecenterControls));
            Assert.That(DemoSwitchRuntime.ResolveControls("tutorial_start", "", menu), Is.SameAs(DemoSession.TutorialControls));

            // The completion panel owns input: neither the pause nor a restart from outside.
            DemoSession.ResetForTests(_platform, "volley", Volley());
            DemoPause.Configure(null);
            Assert.That(DemoSession.TryBegin(AtIndex(0), "volley", Volley(), out var error), Is.True, error);
            DemoSession.RegisterHost(host);
            DemoSession.ShowCompletion();
            Assert.That(DemoPause.SharedControls.CanExecuteControl("menu_open", ""), Is.False);
            Assert.That(DemoPause.SharedControls.CanExecuteControl("restart", ""), Is.False);
            Assert.That(DemoPause.SharedControls.CanExecuteControl("menu_close", ""), Is.True);
        }

        [Test]
        public void TutorialStartNeedsAHostWithATutorialAndClosesPanelsFirst()
        {
            var controls = DemoSession.TutorialControls;
            Assert.That(controls.CanExecuteControl("tutorial_start", ""), Is.False, "No host: not_allowed.");
            DemoSession.RegisterHost(new Host());
            Assert.That(controls.CanExecuteControl("tutorial_start", ""), Is.False, "No tutorial: not_allowed.");

            DemoSession.ResetForTests(_platform, "volley", Volley());
            Assert.That(DemoSession.TryBegin(AtIndex(0), "volley", Volley(), out var error), Is.True, error);
            var host = new TutorialHost();
            DemoSession.RegisterHost(host);
            Assert.That(controls.CanExecuteControl("tutorial_start", ""), Is.True);
            Assert.That(controls.CanExecuteControl("tutorial_start", "intro"), Is.False);
            Assert.That(controls.CanExecuteControl("restart", ""), Is.False);
            DemoSession.ShowCompletion();
            Run(controls.ExecuteControl("tutorial_start", ""));
            Assert.That(host.Tutorials, Is.EqualTo(1));
            Assert.That(DemoSession.IsCompletionShown, Is.False);
            Assert.That(host.Paused, Is.False, "Gameplay resumes.");
            DemoPause.Pause();
            Run(controls.ExecuteControl("tutorial_start", ""));
            Assert.That(host.Tutorials, Is.EqualTo(2));
            Assert.That(DemoPause.IsPaused, Is.False);
            Assert.That(host.Restarts, Is.Zero);
        }

        sealed class OwnMenu : MonoBehaviour, IDemoAppMenuState
        {
            public bool Open;
            public bool IsMenuOpen => Open;
        }

        [Test]
        public void StateReportsHapticsButtonsPauseAndStep()
        {
            var query = new DemoSwitchQuery("remote-pixel", "0123456789abcdef", "");
            var state = DemoSwitchRuntime.BuildState(query, "volley");
            Assert.That(state.CurrentDemoId, Is.EqualTo("volley"));
            Assert.That(state.HapticsOn, Is.True);
            Assert.That(state.HapticsUi, Is.False);
            Assert.That(state.RecenterUi, Is.False);
            Assert.That(state.Paused, Is.False);
            Assert.That(state.StepIndex, Is.EqualTo(-1), "Outside a session.");
            Assert.That(state.StepCount, Is.Zero);

            DemoSession.SetHapticsUiVisible(true);
            Assert.That(DemoSwitchRuntime.BuildState(query, "volley").HapticsUi, Is.False, "No haptics toggle: the button is not shown.");

            DemoSession.ResetForTests(_platform, "volley", Volley());
            Assert.That(DemoSession.TryBegin(AtIndex(0), "volley", Volley(), out var error), Is.True, error);
            DemoSession.SetHapticsUiVisible(true);
            DemoSession.SetRecenterUiVisible(true);
            DemoSession.SetHapticsEnabled(false);
            DemoPause.Pause();
            state = DemoSwitchRuntime.BuildState(query, "volley");
            Assert.That(state.HapticsOn, Is.False);
            Assert.That(state.HapticsUi, Is.True);
            Assert.That(state.RecenterUi, Is.True);
            Assert.That(state.Paused, Is.True);
            Assert.That(state.StepIndex, Is.Zero);
            Assert.That(state.StepCount, Is.EqualTo(2));
            DemoPause.Resume();
            Assert.That(DemoSwitchRuntime.BuildState(query, "volley").Paused, Is.False);

            var go = new GameObject("own menu");
            try
            {
                var menu = go.AddComponent<OwnMenu>();
                Assert.That(DemoSwitchRuntime.BuildState(query, "volley").Paused, Is.False);
                menu.Open = true;
                Assert.That(DemoSwitchRuntime.BuildState(query, "volley").Paused, Is.True, "The app's own menu pause.");
            }
            finally { Object.DestroyImmediate(go); }
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
        public void PanelDepthLayerStaysLastAndHandsDrawAfterPanels()
        {
            var panel = DemoSessionPanel.Create("test panel", new Vector2(300, 200));
            try
            {
                Assert.That(DemoSessionPanel.HandSortingOrder, Is.GreaterThan(DemoSessionPanel.SortingOrder));
                Assert.That(panel.GetComponent<Canvas>().sortingOrder, Is.EqualTo(DemoSessionPanel.SortingOrder));
                var depth = panel.DepthLayer;
                Assert.That(depth, Is.Not.Null);
                Assert.That(depth.material.shader.name, Is.EqualTo("Hidden/Hapbeat/DemoPanelDepth"));
                panel.AddButton(Vector2.zero, new Vector2(120, 60), "OK", 20, null);
                panel.AddText(new Vector2(0, 60), new Vector2(200, 30), "text", 16, Color.white);
                panel.AddRect(new Vector2(0, -60), new Vector2(200, 4), Color.white);
                Assert.That(depth.rectTransform.GetSiblingIndex(), Is.EqualTo(panel.Root.childCount - 1), "Depth after every colour.");
                Assert.That(depth.rectTransform.rect.size, Is.EqualTo(panel.Size), "Covers the whole panel.");
                panel.UpdateDepthLayer();
                Assert.That(depth.enabled, Is.True);
                panel.GetComponent<UnityEngine.UI.Image>().color = Color.clear;
                panel.UpdateDepthLayer();
                Assert.That(depth.enabled, Is.False, "A see-through panel hides nothing.");
            }
            finally { Object.DestroyImmediate(panel.gameObject); }
        }

        [Test]
        public void ClickSoundIsShortQuietAndEndsSilent()
        {
            var rate = DemoSessionClickSound.SampleRate;
            var samples = DemoSessionClickSound.Samples(rate);
            Assert.That(samples.Length, Is.EqualTo(Mathf.RoundToInt(0.03f * rate)), "30 ms.");
            var peak = samples.Max(Mathf.Abs);
            Assert.That(peak, Is.GreaterThan(0.3f).And.LessThanOrEqualTo(DemoSessionClickSound.Peak));
            Assert.That(peak * DemoSessionClickSound.Volume, Is.LessThanOrEqualTo(0.2f), "About -14 dBFS at most.");
            Assert.That(samples[0], Is.EqualTo(0f));
            Assert.That(samples[samples.Length - 1], Is.EqualTo(0f).Within(1e-6f), "No step at the end.");
            var lastMs = samples.Skip(samples.Length - rate / 1000).Max(Mathf.Abs);
            Assert.That(lastMs, Is.LessThan(peak * 0.01f), "Decayed below 1 % before the end.");
            // Most of the energy is in the first 10 ms: a click, not a tone.
            var early = samples.Take(rate / 100).Sum(v => v * v);
            Assert.That(early / samples.Sum(v => v * v), Is.GreaterThan(0.95f));
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
        public void HapticsButtonSitsLowNearTheCentreAndKeepsAFixedLabelWidth()
        {
            var pose = DemoSessionHapticsButton.TargetPose(Vector3.up * 1.6f, Vector3.forward);
            var offset = pose.position - Vector3.up * 1.6f;
            Assert.That(offset.magnitude, Is.EqualTo(DemoSessionHapticsButton.Distance).Within(1e-4f));
            Assert.That(offset.x, Is.LessThan(0f), "Left");
            Assert.That(offset.y, Is.LessThan(0f), "Below");
            Assert.That(Mathf.Asin(-offset.y / offset.magnitude) * Mathf.Rad2Deg, Is.EqualTo(30f).Within(0.1f));
            Assert.That(Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg, Is.EqualTo(-5f).Within(0.1f));
            var lookingDown = DemoSessionHapticsButton.TargetPose(Vector3.up * 1.6f, new Vector3(0, -1, 1));
            Assert.That(Vector3.Distance(lookingDown.position, pose.position), Is.LessThan(1e-4f), "Pitch does not move the button.");
            Assert.That(DemoSessionHapticsButton.Label(true), Is.EqualTo("Haptics\nON"));
            Assert.That(DemoSessionHapticsButton.Label(false), Is.EqualTo("Haptics\nOFF"));

            DemoSession.ResetForTests(_platform, "volley", Volley());
            DemoSession.SetHapticsUiVisible(true);
            var go = new GameObject("haptics button");
            var camera = MainCamera(new Vector3(0, 1.6f, 0), Vector3.forward);
            try
            {
                var button = go.AddComponent<DemoSessionHapticsButton>();
                var update = typeof(DemoSessionHapticsButton).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                update.Invoke(button, null);
                var rect = button.Panel.Buttons.Single().Rect;
                var size = rect.sizeDelta;
                DemoSession.SetHapticsEnabled(false);
                update.Invoke(button, null);
                Assert.That(button.Panel.Buttons.Single().Label, Is.EqualTo("Haptics\nOFF"));
                Assert.That(rect.sizeDelta, Is.EqualTo(size), "The label never resizes the button.");
                Assert.That(button.Panel.Size, Is.EqualTo(DemoSessionCornerButtons.PanelSize));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(camera);
            }
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

        [Test]
        public void RecenterUiIsOptionalAndCarriedToTheNextStep()
        {
            Assert.That(DemoSessionTicket.TryParse(Ticket, out var plain, out var error), Is.True, error);
            Assert.That(plain.RecenterUi, Is.False, "Omitted: hidden.");
            Assert.That(plain.ToJson(), Does.Not.Contain("recenter_ui"));
            var shown = Ticket.Replace(@"""haptics_ui"":false", @"""haptics_ui"":false,""recenter_ui"":true");
            Assert.That(DemoSessionTicket.TryParse(shown, out var ticket, out error), Is.True, error);
            Assert.That(ticket.RecenterUi, Is.True);
            Assert.That(ticket.WithIndex(2, false).RecenterUi, Is.True);
            Assert.That(ticket.WithSession(DemoSessionTicket.NewSessionId()).WithHandStyle(DemoHandStyle.Skin).RecenterUi, Is.True);
            Assert.That(DemoSessionTicket.TryParse(ticket.WithRecenterUi(false).ToJson(), out var hidden, out error), Is.True, error);
            Assert.That(hidden.RecenterUi, Is.False);
            Assert.That(DemoSessionTicket.TryParse(shown.Replace(@"""recenter_ui"":true", @"""recenter_ui"":""yes"""), out _, out error), Is.False);
            Assert.That(error, Does.Contain("recenter_ui"));

            Assert.That(DemoSession.TryBegin(shown.Replace(@"""index"":1", @"""index"":0"), "volley", Volley(), out error), Is.True, error);
            Assert.That(DemoSession.RecenterUiVisible, Is.True, "The ticket sets the button.");
            DemoSession.SetRecenterUiVisible(false);
            Assert.That(DemoSession.LaunchNext(out error), Is.True, error);
            Assert.That(DemoSessionTicket.TryParse(_platform.LaunchedTicket, out var sent, out error), Is.True, error);
            Assert.That(sent.RecenterUi, Is.False, "The current state goes to the next step.");

            DemoSession.ResetForTests(_platform);
            Assert.That(DemoSession.TryBegin(AtIndex(2).Replace(@"""haptics_ui"":false", @"""haptics_ui"":false,""recenter_ui"":true"), DemoSwitchSettings.HubDemoId, null, out error), Is.True, error);
            Assert.That(DemoSession.RecenterUiVisible, Is.True, "The finish runtime too.");
        }

        [Test]
        public void RecenterControlsWorkInEveryRuntimeWithoutAdapter()
        {
            foreach (var action in new[] { "recenter", "recenter_ui_show", "recenter_ui_hide" })
            {
                var json = "{\"version\":1,\"type\":\"CONTROL\",\"controller_id\":\"m5-main\",\"seq\":5,\"demo_id\":\"volley\",\"action\":\"" + action + "\",\"scene_id\":\"\"}";
                Assert.That(DemoSwitchProtocol.ParseCommand(json).Success, Is.True, action);
                Assert.That(DemoSwitchProtocol.ParseCommand(json.Replace("\"scene_id\":\"\"", "\"scene_id\":\"block\"")).Success, Is.False, action);
                Assert.That(DemoSwitchProtocol.IsRecenterAction(action), Is.True);
                Assert.That(DemoSwitchProtocol.IsHapticsAction(action), Is.False);
                Assert.That(DemoSession.RecenterControls.CanExecuteControl(action, ""), Is.True, "No descriptor or session needed.");
                Assert.That(DemoSession.RecenterControls.CanExecuteControl(action, "block"), Is.False);
            }
            Assert.That(DemoSession.RecenterControls.CanExecuteControl("haptics_on", ""), Is.False);
            Assert.That(DemoSession.RecenterControls.CanExecuteControl("menu_open", ""), Is.False);

            var changes = new List<bool>();
            DemoSession.RecenterUiVisibleChanged += visible => changes.Add(visible);
            Run(DemoSession.RecenterControls.ExecuteControl("recenter_ui_show", ""));
            Assert.That(DemoSession.RecenterUiVisible, Is.True);
            Run(DemoSession.RecenterControls.ExecuteControl("recenter_ui_hide", ""));
            Assert.That(DemoSession.RecenterUiVisible, Is.False);
            Assert.That(changes, Is.EqualTo(new[] { true, false }));

            DemoSession.ResetForTests(_platform, "volley");
            var host = new RecenterHost();
            DemoSession.RegisterHost(host);
            Run(DemoSession.RecenterControls.ExecuteControl("recenter", ""));
            Assert.That(host.Recenters, Is.EqualTo(1), "CONTROL recenter runs the demo's own start alignment.");
        }

        [Test]
        public void ResetViewUsesTheHostAndPlacesPanelsInFront()
        {
            var camera = MainCamera(new Vector3(0, 1.6f, 0), Vector3.forward);
            var notified = 0;
            System.Action onRecentered = () => notified++;
            DemoRecenter.Recentered += onRecentered;
            try
            {
                DemoSession.ResetForTests(_platform, "volley");
                var host = new RecenterHost();
                DemoSession.RegisterHost(host);
                DemoPause.Pause();
                Camera.main.transform.SetPositionAndRotation(new Vector3(0.6f, 1.5f, 0.3f), Quaternion.LookRotation(Vector3.left));
                DemoRecenter.ResetView();
                Assert.That(host.Recenters, Is.EqualTo(1));
                Assert.That(notified, Is.EqualTo(1), "Listeners move their panels too (the Hub).");
                Assert.That(DemoRecenter.SettleUntil, Is.GreaterThan(Time.realtimeSinceStartup), "The panels follow the alignment for a moment.");
                AssertInFrontOfHead(DemoPause.Panel.transform, DemoPausePanel.Distance, DemoPausePanel.Drop, "The open pause panel goes in front.");
                DemoPause.Resume();

                DemoSession.ResetForTests(_platform, DemoSwitchSettings.HubDemoId);
                host = new RecenterHost();
                DemoSession.RegisterHost(host);
                DemoRecenter.ResetView();
                Assert.That(host.Recenters, Is.Zero, "The Hub only moves its panel.");
                Assert.That(notified, Is.EqualTo(2));
            }
            finally
            {
                DemoRecenter.Recentered -= onRecentered;
                Object.DestroyImmediate(camera);
            }
        }

        [Test]
        public void DefaultAlignmentPutsTheHeadAtTheStartPoseKeepingHeight()
        {
            var rig = new GameObject("test rig");
            var head = new GameObject("test head");
            try
            {
                head.transform.SetParent(rig.transform, false);
                rig.transform.SetPositionAndRotation(new Vector3(2f, 0.1f, -1f), Quaternion.Euler(0, 70, 0));
                head.transform.localPosition = new Vector3(0.4f, 1.6f, -0.3f);
                head.transform.localRotation = Quaternion.Euler(20, -35, 5);
                var start = new Vector3(-1f, 0f, 3f);
                var front = Quaternion.Euler(0, 200, 0) * Vector3.forward;
                var height = head.transform.position.y;
                var rigHeight = rig.transform.position.y;
                DemoRecenter.AlignRig(rig.transform, head.transform, start, front);
                Assert.That(head.transform.position.x, Is.EqualTo(start.x).Within(1e-4f));
                Assert.That(head.transform.position.z, Is.EqualTo(start.z).Within(1e-4f));
                Assert.That(head.transform.position.y, Is.EqualTo(height).Within(1e-4f), "Floor height unchanged.");
                Assert.That(rig.transform.position.y, Is.EqualTo(rigHeight).Within(1e-4f));
                var heading = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
                Assert.That(Vector3.Angle(heading, front), Is.LessThan(0.01f), "The head faces the start front.");
                Assert.That(Vector3.Angle(rig.transform.up, Vector3.up), Is.LessThan(0.01f), "Yaw only.");
            }
            finally
            {
                Object.DestroyImmediate(head);
                Object.DestroyImmediate(rig);
            }
        }

        [Test]
        public void RecenterButtonSitsLeftOfTheHapticsButtonAndIsHiddenByDefault()
        {
            var head = Vector3.up * 1.6f;
            var pose = DemoSessionRecenterButton.TargetPose(head, Vector3.forward);
            var offset = pose.position - head;
            Assert.That(offset.magnitude, Is.EqualTo(DemoSessionRecenterButton.Distance).Within(1e-4f));
            Assert.That(Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg, Is.EqualTo(-19.5f).Within(0.1f));
            Assert.That(Mathf.Asin(-offset.y / offset.magnitude) * Mathf.Rad2Deg, Is.EqualTo(30f).Within(0.1f));
            // Same height; the panels' facing edges (88 mm wide) stay apart.
            var haptics = DemoSessionHapticsButton.TargetPose(head, Vector3.forward);
            Assert.That(haptics.position.y, Is.EqualTo(pose.position.y).Within(1e-4f));
            var halfWidth = DemoSessionCornerButtons.PanelSize.x * 0.0005f;
            var recenterRight = pose.position + pose.rotation * Vector3.right * halfWidth;
            var hapticsLeft = haptics.position - haptics.rotation * Vector3.right * halfWidth;
            Assert.That(hapticsLeft.x - recenterRight.x, Is.GreaterThan(0.005f), "At least 5 mm between the buttons.");
            Assert.That(DemoSessionRecenterButton.Label, Is.EqualTo("Reset\nView"));

            var go = new GameObject("recenter button");
            var camera = MainCamera(new Vector3(0, 1.6f, 0), Vector3.forward);
            try
            {
                var button = go.AddComponent<DemoSessionRecenterButton>();
                var update = typeof(DemoSessionRecenterButton).GetMethod("Update", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                update.Invoke(button, null);
                Assert.That(button.Panel, Is.Null, "Hidden by default.");
                DemoSession.SetRecenterUiVisible(true);
                update.Invoke(button, null);
                Assert.That(button.Panel.gameObject.activeSelf, Is.True);
                Assert.That(button.Panel.Buttons.Single().Label, Is.EqualTo("Reset\nView"));
                Assert.That(button.Panel.Size, Is.EqualTo(DemoSessionCornerButtons.PanelSize), "Same size as the haptics button.");
                DemoSession.SetRecenterUiVisible(false);
                update.Invoke(button, null);
                Assert.That(button.Panel.gameObject.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(camera);
            }
        }

        [Test]
        public void PanelsDrawOnTopOfTheScene()
        {
            var panel = DemoSessionPanel.Create("test panel", new Vector2(300, 200));
            try
            {
                var button = panel.AddButton(Vector2.zero, new Vector2(120, 60), "OK", 20, null);
                var text = panel.AddText(new Vector2(0, 60), new Vector2(200, 30), "text", 16, Color.white);
                var rect = panel.AddRect(new Vector2(0, -60), new Vector2(200, 4), Color.white);
                var graphics = panel.GetComponentsInChildren<UnityEngine.UI.Graphic>(true).Where(g => g != panel.DepthLayer).ToArray();
                Assert.That(graphics.Length, Is.GreaterThanOrEqualTo(6), "Background, cursors, button, label, text, rect.");
                foreach (var graphic in graphics)
                    Assert.That(graphic.material.shader.name, Is.EqualTo("Hidden/Hapbeat/DemoPanelUi"), graphic.name);
                Assert.That(DemoSessionPanel.OnTopMaterial.shader.isSupported, Is.True);
                Assert.That(button.Text.material, Is.SameAs(DemoSessionPanel.OnTopMaterial));
                Assert.That(text.material, Is.SameAs(DemoSessionPanel.OnTopMaterial));
                Assert.That(rect.material, Is.SameAs(DemoSessionPanel.OnTopMaterial));
                Assert.That(panel.DepthLayer.material.shader.name, Is.EqualTo("Hidden/Hapbeat/DemoPanelDepth"));
            }
            finally { Object.DestroyImmediate(panel.gameObject); }
        }

        [Test]
        public void PausePanelAcceptsInputWhileTimeIsStopped()
        {
            var camera = MainCamera(new Vector3(0, 1.6f, 0), Vector3.forward);
            var previous = Time.timeScale;
            try
            {
                var host = new Host();
                DemoSession.RegisterHost(host);
                // A demo's own pause (e.g. Volley's menu) stops time; the shared panels and hands use real time.
                Time.timeScale = 0f;
                DemoPause.Pause();
                var panel = DemoPause.Panel;
                var resume = panel.ResumeButton.Rect;
                var now = Time.realtimeSinceStartup + DemoPausePanel.InputDelaySeconds + 0.1f;
                var front = resume.position - panel.transform.forward * 0.03f;
                var through = resume.position + panel.transform.forward * 0.002f;
                panel.Panel.ProcessPointers(new[] { new DemoSessionPointer { Id = 1, IsPoke = true, Position = front } }, now);
                panel.Panel.ProcessPointers(new[] { new DemoSessionPointer { Id = 1, IsPoke = true, Position = through } }, now);
                Assert.That(DemoPause.IsPaused, Is.False, "再開 pressed by a fingertip with time stopped.");
                Assert.That(host.Paused, Is.False);
            }
            finally
            {
                Time.timeScale = previous;
                Object.DestroyImmediate(camera);
            }
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
            foreach (var character in "体験完了もう一度次へ：デモを終了触覚ONOFF起動できませんでした/0123456789一時停止再開最初からやり直すHubに戻る視線をリセット")
                Assert.That(font.HasCharacter(character), Is.True, character.ToString());
        }

        /// <summary>
        /// Text boxes truncate vertically, so a box shorter than one line of its font size shows nothing (the
        /// 34-size headings in 46 mm boxes were blank). Every box must hold its explicit lines at its font size
        /// (a best-fit button label at its smallest size).
        /// </summary>
        internal static void AssertTextsFitTheirLines(DemoSessionPanel panel, string what)
        {
            var texts = panel.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            Assert.That(texts, Is.Not.Empty, what);
            foreach (var text in texts)
            {
                var size = text.resizeTextForBestFit ? text.resizeTextMinSize : text.fontSize;
                var lines = Mathf.Max(1, text.text.Split('\n').Length);
                Assert.That(text.rectTransform.rect.height, Is.GreaterThanOrEqualTo(lines * panel.LineHeight(size)),
                    what + ": \"" + text.text + "\" (size " + size + ", " + lines + " line(s))");
            }
        }

        [Test]
        public void EveryPanelTextBoxIsAtLeastItsLinesTall()
        {
            Assert.That(DemoSessionTicket.TryParse(AtIndex(0), out var ticket, out var error), Is.True, error);
            var next = new DemoSessionNext(ticket.Steps[1]);
            var panels = new List<DemoSessionPanel>();
            try
            {
                var heading = DemoSessionPanel.Create("test panel", Vector2.one);
                panels.Add(heading);
                Assert.That(heading.LineHeight(34), Is.GreaterThan(46f), "One line at size 34 is taller than the old 46 mm heading box.");

                panels.Add(DemoPausePanel.Create(true, next).Panel);
                panels.Add(DemoPausePanel.Create(true, new DemoSessionNext(null)).Panel);
                panels.Add(DemoSessionCompletionPanel.Create(ticket, next).Panel);
                foreach (var label in new[] { DemoSessionHapticsButton.Label(true), DemoSessionHapticsButton.Label(false), DemoSessionRecenterButton.Label })
                {
                    var corner = DemoSessionPanel.Create("corner", DemoSessionCornerButtons.PanelSize);
                    panels.Add(corner);
                    corner.AddButton(Vector2.zero, DemoSessionCornerButtons.ButtonSize, label, DemoSessionCornerButtons.FontSize, null);
                }
                foreach (var panel in panels.Skip(1)) AssertTextsFitTheirLines(panel, panel.name);
            }
            finally
            {
                foreach (var panel in panels)
                    if (panel != null) Object.DestroyImmediate(panel.gameObject);
            }
        }
    }
}
