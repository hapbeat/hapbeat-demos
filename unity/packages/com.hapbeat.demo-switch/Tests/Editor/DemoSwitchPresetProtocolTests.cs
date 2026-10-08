using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hapbeat.DemoSwitch.Tests
{
    public sealed class DemoSwitchPresetProtocolTests
    {
        private const string Secret = "correct horse battery staple";

        // hapbeat-contracts fixtures/sample-demo-switch-messages.json unsigned_preset_get / unsigned_preset / unsigned_preset_set / unsigned_preset_start.
        private const string PresetGetExample = "{\"version\":1,\"type\":\"PRESET_GET\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\",\"preset\":1,\"from\":0}";
        private const string PresetExample = "{\"version\":1,\"type\":\"PRESET\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\",\"preset\":1,\"revision\":7,\"name\":\"XR Kaigi A\",\"visible\":true,\"step_count\":2,\"from\":0,\"steps\":[{\"demo_id\":\"energy-duel\",\"options\":{\"tutorial\":\"on\"}},{\"demo_id\":\"volley\",\"options\":{\"scene\":\"match\"},\"retry\":false}]}";
        private const string PresetSetExample = "{\"version\":1,\"type\":\"PRESET_SET\",\"controller_id\":\"remote-pixel\",\"seq\":44,\"demo_id\":\"demo_hub\",\"preset\":1,\"name\":\"XR Kaigi A\",\"visible\":true,\"steps\":[{\"demo_id\":\"energy-duel\",\"options\":{\"tutorial\":\"on\"}},{\"demo_id\":\"volley\",\"options\":{\"scene\":\"match\"},\"retry\":false}]}";
        private const string PresetStartExample = "{\"version\":1,\"type\":\"PRESET_START\",\"controller_id\":\"remote-pixel\",\"seq\":45,\"demo_id\":\"demo_hub\",\"preset\":1}";
        // HMAC-SHA256 of the PRESET_GET fixture with the test secret above.
        private const string PresetGetVector = "6e1ee8be274d4dd262f88078ec289a4742585ea11b482dc39de2a951f3816814";
        private const string ExampleSteps = "energy-duel;tutorial=on;1|volley;scene=match;0";

        private static string WithAuth(string json, string auth) => json.Substring(0, json.Length - 1) + ",\"auth\":\"" + auth + "\"}";

        private static DemoSwitchPreset ExamplePreset() => new DemoSwitchPreset("XR Kaigi A", true, 7, new[]
        {
            new DemoSwitchPresetStep("energy-duel", new Dictionary<string, string> { ["tutorial"] = "on" }, true),
            new DemoSwitchPresetStep("volley", new Dictionary<string, string> { ["scene"] = "match" }, false)
        });

        // HMAC vectors computed independently from the spec's canonical rules (Python hmac / hashlib).
        [Test]
        public void PresetGetMatchesTheFixtureAndHmacVector()
        {
            var parsed = DemoSwitchProtocol.ParsePresetGet(PresetGetExample);
            Assert.That(parsed.Success, Is.True, parsed.ErrorMessage);
            Assert.That(parsed.Request.Preset, Is.EqualTo(1));
            Assert.That(parsed.Request.From, Is.EqualTo(0));
            Assert.That(DemoSwitchProtocol.Canonicalize(parsed.Request), Is.EqualTo(
                "HAPBEAT-DEMO-SWITCH/1\nPRESET_GET\nversion=1:1\ntype=10:PRESET_GET\ncontroller_id=12:remote-pixel\nnonce=16:0123456789abcdef\npreset=1:1\nfrom=1:0\n"));
            Assert.That(DemoSwitchProtocol.ComputeAuth(parsed.Request, Secret), Is.EqualTo(PresetGetVector));
            var signed = DemoSwitchProtocol.ParsePresetGet(WithAuth(PresetGetExample, PresetGetVector));
            Assert.That(signed.Success, Is.True, signed.ErrorMessage);
            Assert.That(DemoSwitchProtocol.Authenticate(signed.Request, Secret), Is.True);
            Assert.That(DemoSwitchProtocol.Authenticate(DemoSwitchProtocol.ParsePresetGet(WithAuth(PresetGetExample.Replace("\"from\":0", "\"from\":1"), PresetGetVector)).Request, Secret), Is.False);
        }

        [Test]
        public void PresetSetAndStartMatchTheFixturesAndHmacVectors()
        {
            var set = DemoSwitchProtocol.ParsePresetCommand(PresetSetExample);
            Assert.That(set.Success, Is.True, set.ErrorMessage);
            Assert.That(set.Command.IsStart, Is.False);
            Assert.That(set.Command.Sequence, Is.EqualTo(44));
            Assert.That(set.Command.DemoId, Is.EqualTo("demo_hub"));
            Assert.That(set.Command.Name, Is.EqualTo("XR Kaigi A"));
            Assert.That(set.Command.Visible, Is.True);
            Assert.That(set.Command.Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "energy-duel", "volley" }));
            Assert.That(set.Command.Steps[0].Retry, Is.True, "retry defaults to true.");
            Assert.That(set.Command.Steps[1].Retry, Is.False);
            Assert.That(set.Command.Steps[1].Options["scene"], Is.EqualTo("match"));
            Assert.That(DemoSwitchProtocol.Canonicalize(set.Command), Is.EqualTo(
                "HAPBEAT-DEMO-SWITCH/1\nCOMMAND\nversion=1:1\ntype=10:PRESET_SET\ncontroller_id=12:remote-pixel\nseq=2:44\ndemo_id=8:demo_hub\npreset=1:1\n" +
                "name=10:XR Kaigi A\nvisible=4:true\nsteps=46:" + ExampleSteps + "\n"));
            Assert.That(DemoSwitchProtocol.ComputeAuth(set.Command, Secret), Is.EqualTo("1b89a5c12ee854a17a41bbd151d0baacdb5606a2d39da2d1ac481dcfb6bfc4ac"));
            var signedSet = DemoSwitchProtocol.ParsePresetCommand(WithAuth(PresetSetExample, "1b89a5c12ee854a17a41bbd151d0baacdb5606a2d39da2d1ac481dcfb6bfc4ac"));
            Assert.That(DemoSwitchProtocol.Authenticate(signedSet.Command, Secret), Is.True);
            var tampered = DemoSwitchProtocol.ParsePresetCommand(WithAuth(PresetSetExample.Replace("\"retry\":false", "\"retry\":true"),
                "1b89a5c12ee854a17a41bbd151d0baacdb5606a2d39da2d1ac481dcfb6bfc4ac"));
            Assert.That(DemoSwitchProtocol.Authenticate(tampered.Command, Secret), Is.False, "steps are signed.");

            var start = DemoSwitchProtocol.ParsePresetCommand(PresetStartExample);
            Assert.That(start.Success, Is.True, start.ErrorMessage);
            Assert.That(start.Command.IsStart, Is.True);
            Assert.That(DemoSwitchProtocol.Canonicalize(start.Command), Is.EqualTo(
                "HAPBEAT-DEMO-SWITCH/1\nCOMMAND\nversion=1:1\ntype=12:PRESET_START\ncontroller_id=12:remote-pixel\nseq=2:45\ndemo_id=8:demo_hub\npreset=1:1\n"));
            Assert.That(DemoSwitchProtocol.ComputeAuth(start.Command, Secret), Is.EqualTo("cd8cef991beffc6f3d5066f264192c21d96df7eaa13f04a2a7d0789b221395e7"));
        }

        [Test]
        public void EmptyNameAndClearedStepsAreSignedAsEmptyValues()
        {
            var json = PresetSetExample.Replace("\"preset\":1", "\"preset\":3").Replace("\"XR Kaigi A\"", "\"XR会議\"").Replace("\"visible\":true", "\"visible\":false");
            json = json.Substring(0, json.IndexOf("\"steps\":")) + "\"steps\":[]}";
            var parsed = DemoSwitchProtocol.ParsePresetCommand(json);
            Assert.That(parsed.Success, Is.True, parsed.ErrorMessage);
            Assert.That(parsed.Command.Steps, Is.Empty);
            Assert.That(DemoSwitchProtocol.Canonicalize(parsed.Command), Does.EndWith("name=8:XR会議\nvisible=5:false\nsteps=0:\n"), "Byte lengths count UTF-8.");
            Assert.That(DemoSwitchProtocol.ComputeAuth(parsed.Command, Secret), Is.EqualTo("6271119321ad2b9902c282c0036f8a57fd435121a5a5a6dc8f76ee8790fefb1e"));
            Assert.That(DemoSwitchProtocol.ParsePresetCommand(json.Replace("\"XR会議\"", "\"\"")).Success, Is.True, "No name.");
        }

        [Test]
        public void CanonicalStepsSortOptionsAndDefaultRetryToOne()
        {
            var set = DemoSwitchProtocol.ParsePresetCommand(PresetSetExample);
            Assert.That(DemoSwitchProtocol.CanonicalSteps(set.Command.Steps), Is.EqualTo(ExampleSteps));
            Assert.That(Encoding.UTF8.GetByteCount(ExampleSteps), Is.EqualTo(46));
            Assert.That(DemoSwitchProtocol.CanonicalSteps(new DemoSwitchPresetStep[0]), Is.EqualTo(""));
            Assert.That(DemoSwitchProtocol.CanonicalSteps(new[]
            {
                new DemoSwitchPresetStep("boxing", new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" }, true),
                new DemoSwitchPresetStep("trex", null, false)
            }), Is.EqualTo("boxing;a=1,b=2;1|trex;;0"));
        }

        [Test]
        public void PresetSerializesTheFixtureAndItsHmacVector()
        {
            var request = DemoSwitchProtocol.ParsePresetGet(PresetGetExample).Request;
            Assert.That(DemoSwitchProtocol.SerializePreset(request, ExamplePreset(), string.Empty), Is.EqualTo(PresetExample), "Field order and values as the fixture.");
            var signed = JObject.Parse(DemoSwitchProtocol.SerializePreset(request, ExamplePreset(), Secret));
            Assert.That(signed.Value<string>("auth"), Is.EqualTo("7d0271b957113a10877f911928df1142d5755bc04eb3611d68b1dca9495f8e26"));
        }

        [TestCase("\"preset\":1", "\"preset\":0")]
        [TestCase("\"preset\":1", "\"preset\":4")]
        [TestCase("\"preset\":1", "\"preset\":\"1\"")]
        [TestCase("\"from\":0", "\"from\":-1")]
        [TestCase("\"from\":0", "\"from\":32")]
        [TestCase("\"from\":0", "\"from\":0,\"seq\":1")]
        [TestCase("\"from\":0", "\"from\":0,\"from\":1")]
        [TestCase("\"nonce\":\"0123456789abcdef\"", "\"nonce\":\"xyz\"")]
        [TestCase("\"version\":1", "\"version\":2")]
        [TestCase("\"type\":\"PRESET_GET\"", "\"type\":\"QUERY\"")]
        [TestCase(",\"preset\":1", "")]
        [TestCase(",\"from\":0", "")]
        [TestCase(",\"controller_id\":\"remote-pixel\"", "")]
        [TestCase("\"from\":0}", "\"from\":0}garbage")]
        public void PresetGetParserRejectsSchemaViolations(string from, string to)
        {
            Assert.That(DemoSwitchProtocol.ParsePresetGet(PresetGetExample.Replace(from, to)).Success, Is.False, to);
        }

        [TestCase("\"demo_id\":\"demo_hub\"", "\"demo_id\":\"volley\"")]
        [TestCase("\"seq\":44", "\"seq\":0")]
        [TestCase("\"preset\":1", "\"preset\":4")]
        [TestCase("\"XR Kaigi A\"", "\"a\\u2028\"")]
        [TestCase("\"XR Kaigi A\"", "\" \"")]
        [TestCase("\"XR Kaigi A\"", "\"a\\n\"")]
        [TestCase("\"XR Kaigi A\"", "\"a\\u0085\"")]
        [TestCase("\"XR Kaigi A\"", "\"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\"")]
        [TestCase("\"XR Kaigi A\"", "null")]
        [TestCase("\"visible\":true", "\"visible\":\"yes\"")]
        [TestCase("\"demo_id\":\"volley\"", "\"demo_id\":\"Volley\"")]
        [TestCase("\"demo_id\":\"volley\"", "\"demo_id\":\"volley\",\"path\":\"x\"")]
        [TestCase("\"scene\":\"match\"", "\"scene\":\"a;b\"")]
        [TestCase("\"scene\":\"match\"", "\"scene\":1")]
        [TestCase("\"scene\":\"match\"", "\"Scene\":\"match\"")]
        [TestCase("\"scene\":\"match\"", "\"scene\":\"match\",\"scene\":\"block\"")]
        [TestCase("\"retry\":false", "\"retry\":\"no\"")]
        [TestCase("\"preset\":1", "\"preset\":1,\"nonce\":\"0123456789abcdef\"")]
        [TestCase(",\"name\":\"XR Kaigi A\"", "")]
        [TestCase(",\"visible\":true", "")]
        [TestCase("\"steps\":[", "\"steps_\":[")]
        public void PresetSetParserRejectsSchemaViolations(string from, string to)
        {
            Assert.That(PresetSetExample.Contains(from), Is.True, from);
            Assert.That(DemoSwitchProtocol.ParsePresetCommand(PresetSetExample.Replace(from, to)).Success, Is.False, to);
        }

        [Test]
        public void PresetSetAllowsAtMost32StepsAnd8Options()
        {
            string Steps(int count) => string.Join(",", Enumerable.Repeat("{\"demo_id\":\"volley\"}", count));
            string Set(string steps) => "{\"version\":1,\"type\":\"PRESET_SET\",\"controller_id\":\"remote-pixel\",\"seq\":44,\"demo_id\":\"demo_hub\",\"preset\":1,\"name\":\"\",\"visible\":true,\"steps\":[" + steps + "]}";
            Assert.That(DemoSwitchProtocol.ParsePresetCommand(Set(Steps(32))).Success, Is.True);
            Assert.That(DemoSwitchProtocol.ParsePresetCommand(Set(Steps(33))).Success, Is.False);
            string Options(int count) => "{\"demo_id\":\"volley\",\"options\":{" + string.Join(",", Enumerable.Range(0, count).Select(i => "\"o" + i + "\":\"v\"")) + "}}";
            Assert.That(DemoSwitchProtocol.ParsePresetCommand(Set(Options(8))).Success, Is.True);
            Assert.That(DemoSwitchProtocol.ParsePresetCommand(Set(Options(9))).Success, Is.False);
        }

        [TestCase("\"demo_id\":\"demo_hub\"", "\"demo_id\":\"volley\"")]
        [TestCase("\"preset\":1", "\"preset\":0")]
        [TestCase("\"preset\":1", "\"preset\":1,\"steps\":[]")]
        [TestCase("\"preset\":1", "\"preset\":1,\"name\":\"\"")]
        [TestCase("\"seq\":45", "\"seq\":0")]
        [TestCase(",\"preset\":1", "")]
        [TestCase("\"preset\":1}", "\"preset\":1,\"auth\":\"ABC\"}")]
        public void PresetStartParserRejectsSchemaViolations(string from, string to)
        {
            Assert.That(DemoSwitchProtocol.ParsePresetCommand(PresetStartExample.Replace(from, to)).Success, Is.False, to);
        }

        [TestCase("", true)]
        [TestCase("XR Kaigi A", true)]
        [TestCase("展示 A", true)]
        [TestCase(" ", false)]
        [TestCase("　", false)]
        [TestCase("a\u0000", false)]
        [TestCase("a\u007f", false)]
        [TestCase("a\u009f", false)]
        [TestCase("a\u2029", false)]
        public void PresetNamesFollowTheRemotePresetRules(string name, bool valid)
        {
            Assert.That(DemoSwitchPresets.IsValidName(name), Is.EqualTo(valid));
        }

        [Test]
        public void PresetNamesCountCodePoints()
        {
            Assert.That(DemoSwitchPresets.IsValidName(string.Concat(Enumerable.Repeat("\U0001F600", 40))), Is.True, "40 code points (80 UTF-16 units).");
            Assert.That(DemoSwitchPresets.IsValidName(string.Concat(Enumerable.Repeat("\U0001F600", 41))), Is.False);
            Assert.That(DemoSwitchPresets.IsValidName(null), Is.False);
        }

        private static DemoSwitchPresetStep LongStep(int index) => new DemoSwitchPresetStep("demo-" + index.ToString("00") + new string('x', 40),
            Enumerable.Range(0, 3).ToDictionary(i => "option-" + i, i => "value-" + i + new string('v', 20)), index % 2 == 0);

        [TestCase("")]
        [TestCase(Secret)]
        public void PresetPagesFitTheDatagramAndCoverEveryStep(string secret)
        {
            var steps = Enumerable.Range(0, DemoSwitchPresets.MaxSteps).Select(LongStep).ToList();
            var preset = new DemoSwitchPreset("展示 A", false, 12, steps);
            var received = new List<string>();
            var from = 0;
            var pages = 0;
            while (from < steps.Count)
            {
                var json = DemoSwitchProtocol.SerializePreset(new DemoSwitchPresetGet("remote-pixel", "0123456789abcdef", 2, from, ""), preset, secret);
                Assert.That(Encoding.UTF8.GetByteCount(json), Is.LessThanOrEqualTo(DemoSwitchProtocol.MaxPayloadBytes));
                var value = JObject.Parse(json);
                Assert.That(value.Value<int>("step_count"), Is.EqualTo(32));
                Assert.That(value.Value<int>("from"), Is.EqualTo(from));
                var page = ((JArray)value["steps"]).Select(s => s.Value<string>("demo_id")).ToList();
                Assert.That(page, Is.Not.Empty);
                if (from + page.Count < steps.Count)
                {
                    // One more step would not fit.
                    var bigger = JObject.Parse(json);
                    var next = steps[from + page.Count];
                    var stepValue = new JObject { ["demo_id"] = next.DemoId, ["options"] = JObject.FromObject(next.Options.OrderBy(p => p.Key).ToDictionary(p => p.Key, p => p.Value)) };
                    if (!next.Retry) stepValue["retry"] = false;
                    ((JArray)bigger["steps"]).Add(stepValue);
                    Assert.That(Encoding.UTF8.GetByteCount(bigger.ToString(Newtonsoft.Json.Formatting.None)), Is.GreaterThan(DemoSwitchProtocol.MaxPayloadBytes));
                }
                if (secret.Length > 0)
                {
                    var unsigned = new DemoSwitchPresetPage("remote-pixel", "0123456789abcdef", 2, 12, "展示 A", false, 32, from, steps.Skip(from).Take(page.Count).ToList());
                    Assert.That(value.Value<string>("auth"), Is.EqualTo(DemoSwitchProtocol.ComputeAuth(unsigned, secret)));
                }
                else Assert.That(value.ContainsKey("auth"), Is.False);
                received.AddRange(page);
                from += page.Count;
                pages++;
            }
            Assert.That(received, Is.EqualTo(steps.Select(s => s.DemoId)));
            Assert.That(pages, Is.GreaterThan(1));
        }

        [Test]
        public void PresetPageIsEmptyPastTheEndOrWhenTheStepAloneDoesNotFit()
        {
            var past = JObject.Parse(DemoSwitchProtocol.SerializePreset(new DemoSwitchPresetGet("remote-pixel", "0123456789abcdef", 1, 2, ""), ExamplePreset(), Secret));
            Assert.That(past["steps"], Is.Empty);
            Assert.That(past.Value<int>("step_count"), Is.EqualTo(2));

            var huge = new DemoSwitchPresetStep(new string('h', 64), Enumerable.Range(0, 8).ToDictionary(i => "o" + i + new string('k', 60), i => new string('v', 32)), true);
            var preset = new DemoSwitchPreset("", true, 0, new[] { new DemoSwitchPresetStep("volley", null, true), huge });
            var first = JObject.Parse(DemoSwitchProtocol.SerializePreset(new DemoSwitchPresetGet("remote-pixel", "0123456789abcdef", 1, 0, ""), preset, Secret));
            Assert.That(((JArray)first["steps"]).Count, Is.EqualTo(1));
            var json = DemoSwitchProtocol.SerializePreset(new DemoSwitchPresetGet("remote-pixel", "0123456789abcdef", 1, 1, ""), preset, Secret);
            Assert.That(JObject.Parse(json)["steps"], Is.Empty, "The step at from alone does not fit.");
            Assert.That(Encoding.UTF8.GetByteCount(json), Is.LessThanOrEqualTo(DemoSwitchProtocol.MaxPayloadBytes));
        }

        [Test]
        public void PresetGetHandlerUsesTheQueryAuthenticationPolicy()
        {
            DemoSwitchPreset Read(int number) => number == 1 ? ExamplePreset() : null;
            Assert.That(DemoSwitchPresetGetHandler.Handle(PresetGetExample, Read, string.Empty, false).ErrorCode, Is.EqualTo("unsigned_disabled"));
            Assert.That(DemoSwitchPresetGetHandler.Handle(PresetGetExample, Read, string.Empty, true).ResponseJson, Is.EqualTo(PresetExample));
            Assert.That(DemoSwitchPresetGetHandler.Handle(PresetGetExample, Read, Secret, true).ErrorCode, Is.EqualTo("invalid_auth"));
            Assert.That(DemoSwitchPresetGetHandler.Handle(WithAuth(PresetGetExample, new string('0', 64)), Read, Secret, true).ErrorCode, Is.EqualTo("invalid_auth"));
            Assert.That(DemoSwitchPresetGetHandler.Handle("{\"version\":1,\"type\":\"PRESET_GET\"}", Read, Secret, true).ErrorCode, Is.EqualTo("invalid_payload"));
            var signed = DemoSwitchPresetGetHandler.Handle(WithAuth(PresetGetExample, "6e1ee8be274d4dd262f88078ec289a4742585ea11b482dc39de2a951f3816814"), Read, Secret, false);
            Assert.That(signed.ShouldReply, Is.True);
            Assert.That(JObject.Parse(signed.ResponseJson).Value<string>("auth"), Is.EqualTo("7d0271b957113a10877f911928df1142d5755bc04eb3611d68b1dca9495f8e26"));
        }
    }
}
