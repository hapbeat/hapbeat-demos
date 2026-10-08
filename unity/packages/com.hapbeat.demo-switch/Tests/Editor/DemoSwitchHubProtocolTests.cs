using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hapbeat.DemoSwitch.Tests
{
    /// <summary>STATE optional fields, the new CONTROL actions and HUB_SETTINGS_GET / HUB_SETTINGS / HUB_SETTINGS_SET / HUB_START.</summary>
    public sealed class DemoSwitchHubProtocolTests
    {
        // HMAC vectors of the remote-hub-parity instructions §1 (shared by the Unity, Android and contracts implementations).
        private const string Secret = "test-secret";

        // hapbeat-contracts fixtures/sample-demo-switch-messages.json unsigned_state_extended ... unsigned_hub_start.
        private const string StateExtended = "{\"version\":1,\"type\":\"STATE\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\",\"current_demo_id\":\"handdemo\",\"foreground\":true,\"haptics_on\":true,\"haptics_ui\":false,\"recenter_ui\":false,\"paused\":false,\"step_index\":1,\"step_count\":3,\"device_model\":\"Oculus Quest 3\",\"editor\":false,\"screen\":\"main\",\"hand_style\":\"ghost\"}";
        private const string StatePartial = "{\"version\":1,\"type\":\"STATE\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\",\"current_demo_id\":\"handdemo\",\"foreground\":true,\"haptics_on\":true,\"haptics_ui\":false,\"recenter_ui\":false,\"paused\":false,\"step_index\":1,\"step_count\":3,\"screen\":\"completion\"}";
        private const string ControlHandStyleSkin = "{\"version\":1,\"type\":\"CONTROL\",\"controller_id\":\"remote-pixel\",\"seq\":48,\"demo_id\":\"handdemo\",\"action\":\"hand_style_skin\",\"scene_id\":\"\"}";
        private const string HubSettingsGet = "{\"version\":1,\"type\":\"HUB_SETTINGS_GET\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\",\"from\":0}";
        private const string HubSettings = "{\"version\":1,\"type\":\"HUB_SETTINGS\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\",\"revision\":3,\"haptics_ui\":false,\"recenter_ui\":true,\"hand_style\":\"ghost\",\"staff_waiting\":false,\"player\":1,\"group\":-1,\"demo_count\":2,\"from\":0,\"demos\":[{\"demo_id\":\"volley\",\"title\":\"Volley\",\"visible\":true},{\"demo_id\":\"fps\",\"title\":\"FPS\",\"visible\":false}]}";
        private const string HubSettingsSet = "{\"version\":1,\"type\":\"HUB_SETTINGS_SET\",\"controller_id\":\"remote-pixel\",\"seq\":46,\"demo_id\":\"demo_hub\",\"haptics_ui\":false,\"recenter_ui\":true,\"hand_style\":\"skin\",\"staff_waiting\":false,\"visible_demos\":[\"volley\"]}";
        private const string HubStart = "{\"version\":1,\"type\":\"HUB_START\",\"controller_id\":\"remote-pixel\",\"seq\":47,\"demo_id\":\"demo_hub\",\"steps\":[{\"demo_id\":\"volley\",\"options\":{\"scene\":\"match\"}}]}";

        internal static DemoSwitchHubSettings ExampleSettings() => new DemoSwitchHubSettings(3, false, true, DemoHandStyle.Ghost, false, 1, -1,
            new[] { new DemoSwitchHubDemo("volley", "Volley", true), new DemoSwitchHubDemo("fps", "FPS", false) });

        private static string WithAuth(string json, string auth) => json.Substring(0, json.Length - 1) + ",\"auth\":\"" + auth + "\"}";

        [Test]
        public void StateOptionalFieldsAreSignedInOrderWhenPresent()
        {
            var extended = new DemoSwitchState("remote-pixel", "0123456789abcdef", "handdemo", true, true, false, false, false, 1, 3,
                "Oculus Quest 3", false, DemoSwitchScreens.Main, DemoHandStyle.Ghost);
            Assert.That(DemoSwitchProtocol.SerializeState(extended, string.Empty), Is.EqualTo(StateExtended), "Field order and values as the fixture.");
            Assert.That(DemoSwitchProtocol.Canonicalize(extended), Does.EndWith(
                "step_count=1:3\ndevice_model=14:Oculus Quest 3\neditor=5:false\nscreen=4:main\nhand_style=5:ghost\n"));
            Assert.That(DemoSwitchProtocol.ComputeAuth(extended, Secret), Is.EqualTo("4722f50769469f635991a711f24fe04dd847136b02d1e6a2e241840d48b85cf1"));

            var partial = new DemoSwitchState("remote-pixel", "0123456789abcdef", "handdemo", true, true, false, false, false, 1, 3,
                screen: DemoSwitchScreens.Completion);
            Assert.That(DemoSwitchProtocol.SerializeState(partial, string.Empty), Is.EqualTo(StatePartial));
            Assert.That(DemoSwitchProtocol.ComputeAuth(partial, Secret), Is.EqualTo("c1529021a52273598596f8cefde5c5da6d58ee912bee05219f29c4e1569b1a74"));

            var parsed = DemoSwitchProtocol.ParseState(WithAuth(StateExtended, "4722f50769469f635991a711f24fe04dd847136b02d1e6a2e241840d48b85cf1"));
            Assert.That(parsed.Success, Is.True, parsed.ErrorMessage);
            Assert.That(parsed.State.DeviceModel, Is.EqualTo("Oculus Quest 3"));
            Assert.That(parsed.State.Editor, Is.False);
            Assert.That(parsed.State.Screen, Is.EqualTo("main"));
            Assert.That(parsed.State.HandStyle, Is.EqualTo(DemoHandStyle.Ghost));
            Assert.That(DemoSwitchProtocol.Authenticate(parsed.State, Secret), Is.True);

            var partialParsed = DemoSwitchProtocol.ParseState(StatePartial);
            Assert.That(partialParsed.Success, Is.True, partialParsed.ErrorMessage);
            Assert.That(partialParsed.State.DeviceModel, Is.Null, "Absent: unknown.");
            Assert.That(partialParsed.State.Editor, Is.Null);
            Assert.That(partialParsed.State.HandStyle, Is.Null);
        }

        [TestCase("\"device_model\":\"Oculus Quest 3\"", "\"device_model\":\"\"")]
        [TestCase("\"device_model\":\"Oculus Quest 3\"", "\"device_model\":\"a\\u0007\"")]
        [TestCase("\"device_model\":\"Oculus Quest 3\"", "\"device_model\":\"a\\u0085\"")]
        [TestCase("\"device_model\":\"Oculus Quest 3\"", "\"device_model\":\"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\"")]
        [TestCase("\"device_model\":\"Oculus Quest 3\"", "\"device_model\":3")]
        [TestCase("\"editor\":false", "\"editor\":\"no\"")]
        [TestCase("\"screen\":\"main\"", "\"screen\":\"pause\"")]
        [TestCase("\"hand_style\":\"ghost\"", "\"hand_style\":\"metal\"")]
        [TestCase("\"hand_style\":\"ghost\"", "\"hand_style\":\"ghost\",\"hand\":\"ghost\"")]
        public void StateParserRejectsInvalidOptionalFields(string from, string to)
        {
            Assert.That(StateExtended.Contains(from), Is.True, from);
            Assert.That(DemoSwitchProtocol.ParseState(StateExtended.Replace(from, to)).Success, Is.False, to);
        }

        [Test]
        public void DeviceModelIsNormalizedToTheSchema()
        {
            Assert.That(DemoSwitchProtocol.NormalizeDeviceModel("Oculus\u0000 Quest\n 3"), Is.EqualTo("Oculus Quest 3"));
            Assert.That(DemoSwitchProtocol.NormalizeDeviceModel("\u0001\u009f"), Is.Null, "Nothing left: omitted.");
            Assert.That(DemoSwitchProtocol.NormalizeDeviceModel(null), Is.Null);
            var emoji = DemoSwitchProtocol.NormalizeDeviceModel(string.Concat(Enumerable.Repeat("\U0001F600", 70)));
            Assert.That(emoji, Is.EqualTo(string.Concat(Enumerable.Repeat("\U0001F600", 64))), "64 code points, no split surrogate pair.");
            Assert.That(DemoSwitchProtocol.IsDeviceModel(emoji), Is.True);
        }

        [Test]
        public void HandStyleControlMatchesTheHmacVector()
        {
            var parsed = DemoSwitchProtocol.ParseCommand(ControlHandStyleSkin);
            Assert.That(parsed.Success, Is.True, parsed.ErrorMessage);
            Assert.That(DemoSwitchProtocol.ComputeAuth(parsed.Command, Secret), Is.EqualTo("19428bbb3df765bcb2606f517c3a41328574b5acd23537c306768ea4478eee80"));
        }

        [TestCase("hand_style_ghost")]
        [TestCase("hand_style_skin")]
        [TestCase("session_next")]
        [TestCase("session_retry")]
        [TestCase("hub_top")]
        [TestCase("hub_replay")]
        public void NewActionsAreControlActionsWithAnEmptyScene(string action)
        {
            string json = "{\"version\":1,\"type\":\"CONTROL\",\"controller_id\":\"remote-pixel\",\"seq\":48,\"demo_id\":\"handdemo\",\"action\":\"" + action + "\",\"scene_id\":\"\"}";
            Assert.That(DemoSwitchProtocol.ParseCommand(json).Success, Is.True);
            Assert.That(DemoSwitchProtocol.ParseCommand(json.Replace("\"scene_id\":\"\"", "\"scene_id\":\"block\"")).Success, Is.False);
        }

        [Test]
        public void HubSettingsGetMatchesTheFixtureAndHmacVector()
        {
            var parsed = DemoSwitchProtocol.ParseHubSettingsGet(HubSettingsGet);
            Assert.That(parsed.Success, Is.True, parsed.ErrorMessage);
            Assert.That(DemoSwitchProtocol.Canonicalize(parsed.Request), Is.EqualTo(
                "HAPBEAT-DEMO-SWITCH/1\nHUB_SETTINGS_GET\nversion=1:1\ntype=16:HUB_SETTINGS_GET\ncontroller_id=12:remote-pixel\nnonce=16:0123456789abcdef\nfrom=1:0\n"));
            Assert.That(DemoSwitchProtocol.ComputeAuth(parsed.Request, Secret), Is.EqualTo("de07246f7208dac8a116de59f8b2bf5f5e82135e5e276084a4b99664dc3d197a"));
            var signed = DemoSwitchProtocol.ParseHubSettingsGet(WithAuth(HubSettingsGet, "de07246f7208dac8a116de59f8b2bf5f5e82135e5e276084a4b99664dc3d197a"));
            Assert.That(DemoSwitchProtocol.Authenticate(signed.Request, Secret), Is.True);
        }

        [Test]
        public void HubSettingsSerializesTheFixtureAndItsHmacVector()
        {
            var request = DemoSwitchProtocol.ParseHubSettingsGet(HubSettingsGet).Request;
            Assert.That(DemoSwitchProtocol.SerializeHubSettings(request, ExampleSettings(), string.Empty), Is.EqualTo(HubSettings));
            Assert.That(DemoSwitchProtocol.CanonicalDemos(ExampleSettings().Demos), Is.EqualTo("volley;1;6:Volley|fps;0;3:FPS"));
            var signed = JObject.Parse(DemoSwitchProtocol.SerializeHubSettings(request, ExampleSettings(), Secret));
            Assert.That(signed.Value<string>("auth"), Is.EqualTo("33d8ca3fbb0e14efae9e8e505992d3f97446bcdcfed7774953b367485a4d1a49"));
        }

        [Test]
        public void HubSettingsSetAndStartMatchTheFixturesAndHmacVectors()
        {
            var set = DemoSwitchProtocol.ParseHubSettingsSet(HubSettingsSet);
            Assert.That(set.Success, Is.True, set.ErrorMessage);
            Assert.That(set.Command.RecenterUi, Is.True);
            Assert.That(set.Command.HandStyle, Is.EqualTo(DemoHandStyle.Skin));
            Assert.That(set.Command.VisibleDemos, Is.EqualTo(new[] { "volley" }));
            Assert.That(DemoSwitchProtocol.Canonicalize(set.Command), Does.EndWith("staff_waiting=5:false\nvisible_demos=6:volley\n"));
            Assert.That(DemoSwitchProtocol.ComputeAuth(set.Command, Secret), Is.EqualTo("dd5dc8984242f56413c51d6b17ddfee52dd40e3dc663bfbfbd13533f8155039b"));
            var none = DemoSwitchProtocol.ParseHubSettingsSet(HubSettingsSet.Replace("[\"volley\"]", "[]"));
            Assert.That(none.Success, Is.True, none.ErrorMessage);
            Assert.That(DemoSwitchProtocol.Canonicalize(none.Command), Does.EndWith("visible_demos=0:\n"));

            var start = DemoSwitchProtocol.ParseHubStart(HubStart);
            Assert.That(start.Success, Is.True, start.ErrorMessage);
            Assert.That(start.Command.Steps.Single().Options["scene"], Is.EqualTo("match"));
            Assert.That(DemoSwitchProtocol.Canonicalize(start.Command), Does.EndWith("steps=20:volley;scene=match;1\n"));
            Assert.That(DemoSwitchProtocol.ComputeAuth(start.Command, Secret), Is.EqualTo("8cc476ef9a64e5b376aad77225b57429736f116063bdf4149601e111bec1e7a2"));
            var signed = DemoSwitchProtocol.ParseHubStart(WithAuth(HubStart, "8cc476ef9a64e5b376aad77225b57429736f116063bdf4149601e111bec1e7a2"));
            Assert.That(DemoSwitchProtocol.Authenticate(signed.Command, Secret), Is.True);
            var tampered = DemoSwitchProtocol.ParseHubStart(WithAuth(HubStart.Replace("match", "block"), "8cc476ef9a64e5b376aad77225b57429736f116063bdf4149601e111bec1e7a2"));
            Assert.That(DemoSwitchProtocol.Authenticate(tampered.Command, Secret), Is.False, "steps are signed.");
        }

        [TestCase("\"from\":0", "\"from\":-1")]
        [TestCase("\"from\":0", "\"from\":64")]
        [TestCase("\"from\":0", "\"from\":\"0\"")]
        [TestCase("\"from\":0", "\"from\":0,\"preset\":1")]
        [TestCase(",\"from\":0", "")]
        [TestCase("\"type\":\"HUB_SETTINGS_GET\"", "\"type\":\"PRESET_GET\"")]
        public void HubSettingsGetParserRejectsSchemaViolations(string from, string to)
        {
            Assert.That(DemoSwitchProtocol.ParseHubSettingsGet(HubSettingsGet.Replace(from, to)).Success, Is.False, to);
        }

        [Test]
        public void HubSettingsGetAcceptsTheLastDemo()
        {
            Assert.That(DemoSwitchProtocol.ParseHubSettingsGet(HubSettingsGet.Replace("\"from\":0", "\"from\":63")).Success, Is.True);
        }

        [TestCase("\"demo_id\":\"demo_hub\"", "\"demo_id\":\"volley\"")]
        [TestCase("\"seq\":46", "\"seq\":0")]
        [TestCase("\"hand_style\":\"skin\"", "\"hand_style\":\"metal\"")]
        [TestCase("\"recenter_ui\":true", "\"recenter_ui\":1")]
        [TestCase(",\"staff_waiting\":false", "")]
        [TestCase("[\"volley\"]", "[\"volley\",\"volley\"]")]
        [TestCase("[\"volley\"]", "[\"Volley\"]")]
        [TestCase("[\"volley\"]", "[1]")]
        [TestCase("[\"volley\"]", "\"volley\"")]
        [TestCase("\"visible_demos\"", "\"visible\"")]
        public void HubSettingsSetParserRejectsSchemaViolations(string from, string to)
        {
            Assert.That(HubSettingsSet.Contains(from), Is.True, from);
            Assert.That(DemoSwitchProtocol.ParseHubSettingsSet(HubSettingsSet.Replace(from, to)).Success, Is.False, to);
        }

        [Test]
        public void HubSettingsSetAllowsAtMost64VisibleDemos()
        {
            string Set(int count) => HubSettingsSet.Replace("[\"volley\"]", "[" + string.Join(",", Enumerable.Range(0, count).Select(i => "\"d" + i + "\"")) + "]");
            Assert.That(DemoSwitchProtocol.ParseHubSettingsSet(Set(64)).Success, Is.True);
            Assert.That(DemoSwitchProtocol.ParseHubSettingsSet(Set(65)).Success, Is.False);
        }

        [TestCase("\"demo_id\":\"demo_hub\"", "\"demo_id\":\"volley\"")]
        [TestCase("\"steps\":[{\"demo_id\":\"volley\",\"options\":{\"scene\":\"match\"}}]", "\"steps\":[]")]
        [TestCase("\"scene\":\"match\"", "\"scene\":\"a;b\"")]
        [TestCase("\"seq\":47", "\"seq\":47,\"preset\":1")]
        public void HubStartParserRejectsSchemaViolations(string from, string to)
        {
            Assert.That(HubStart.Contains(from), Is.True, from);
            Assert.That(DemoSwitchProtocol.ParseHubStart(HubStart.Replace(from, to)).Success, Is.False, to);
        }

        [Test]
        public void HubStartAllowsAtMost32Steps()
        {
            string Start(int count) => "{\"version\":1,\"type\":\"HUB_START\",\"controller_id\":\"remote-pixel\",\"seq\":47,\"demo_id\":\"demo_hub\",\"steps\":["
                + string.Join(",", Enumerable.Repeat("{\"demo_id\":\"volley\"}", count)) + "]}";
            Assert.That(DemoSwitchProtocol.ParseHubStart(Start(32)).Success, Is.True);
            Assert.That(DemoSwitchProtocol.ParseHubStart(Start(33)).Success, Is.False);
        }

        [TestCase("Volley", true)]
        [TestCase("展示 A", true)]
        [TestCase("", false)]
        [TestCase("  ", false)]
        [TestCase("a\u0007", false)]
        [TestCase("a\u2028", false)]
        public void HubTitlesFollowThePresetNameRulesWithoutTheEmptyName(string title, bool valid)
        {
            Assert.That(DemoSwitchHub.IsValidTitle(title), Is.EqualTo(valid));
        }

        [TestCase("")]
        [TestCase(Secret)]
        public void HubSettingsPagesFitTheDatagramAndCoverEveryDemo(string secret)
        {
            var demos = Enumerable.Range(0, DemoSwitchHub.MaxDemos)
                .Select(i => new DemoSwitchHubDemo("demo-" + i.ToString("00") + new string('x', 20), "デモ " + i + new string('名', 30), i % 2 == 0)).ToList();
            var settings = new DemoSwitchHubSettings(9, true, false, DemoHandStyle.Skin, true, 12, 99, demos);
            var received = new List<string>();
            var from = 0;
            var pages = 0;
            while (from < demos.Count)
            {
                var json = DemoSwitchProtocol.SerializeHubSettings(new DemoSwitchHubSettingsGet("remote-pixel", "0123456789abcdef", from, ""), settings, secret);
                Assert.That(Encoding.UTF8.GetByteCount(json), Is.LessThanOrEqualTo(DemoSwitchProtocol.MaxPayloadBytes));
                var value = JObject.Parse(json);
                Assert.That(value.Value<int>("demo_count"), Is.EqualTo(64));
                Assert.That(value.Value<int>("from"), Is.EqualTo(from));
                var page = ((JArray)value["demos"]).Select(d => d.Value<string>("demo_id")).ToList();
                Assert.That(page, Is.Not.Empty);
                if (secret.Length > 0)
                {
                    var unsigned = new DemoSwitchHubSettingsPage("remote-pixel", "0123456789abcdef", settings, from, demos.Skip(from).Take(page.Count).ToList());
                    Assert.That(value.Value<string>("auth"), Is.EqualTo(DemoSwitchProtocol.ComputeAuth(unsigned, secret)));
                }
                received.AddRange(page);
                from += page.Count;
                pages++;
            }
            Assert.That(received, Is.EqualTo(demos.Select(d => d.DemoId)));
            Assert.That(pages, Is.GreaterThan(1));
            var past = JObject.Parse(DemoSwitchProtocol.SerializeHubSettings(new DemoSwitchHubSettingsGet("remote-pixel", "0123456789abcdef", 63, ""),
                new DemoSwitchHubSettings(0, false, false, DemoHandStyle.Ghost, false, -1, -1, demos.Take(2).ToList()), secret));
            Assert.That(past["demos"], Is.Empty, "from past the last demo.");
        }

        [Test]
        public void HubSettingsGetHandlerUsesTheQueryAuthenticationPolicy()
        {
            Assert.That(DemoSwitchHubSettingsGetHandler.Handle(HubSettingsGet, ExampleSettings, string.Empty, false).ErrorCode, Is.EqualTo("unsigned_disabled"));
            Assert.That(DemoSwitchHubSettingsGetHandler.Handle(HubSettingsGet, ExampleSettings, string.Empty, true).ResponseJson, Is.EqualTo(HubSettings));
            Assert.That(DemoSwitchHubSettingsGetHandler.Handle(HubSettingsGet, ExampleSettings, Secret, true).ErrorCode, Is.EqualTo("invalid_auth"));
            var signed = DemoSwitchHubSettingsGetHandler.Handle(WithAuth(HubSettingsGet, "de07246f7208dac8a116de59f8b2bf5f5e82135e5e276084a4b99664dc3d197a"),
                ExampleSettings, Secret, false);
            Assert.That(JObject.Parse(signed.ResponseJson).Value<string>("auth"), Is.EqualTo("33d8ca3fbb0e14efae9e8e505992d3f97446bcdcfed7774953b367485a4d1a49"));
        }
    }
}
