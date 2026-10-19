using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hapbeat.DemoSwitch.Tests
{
    /// <summary>DEMO_SETTINGS_GET / DEMO_SETTINGS / DEMO_SETTINGS_SET (contracts demo-switch-control.md "Demo settings").</summary>
    public sealed class DemoSwitchDemoSettingsProtocolTests
    {
        private const string Secret = "test-secret";

        // hapbeat-contracts fixtures/sample-demo-switch-messages.json unsigned_demo_settings_get / _settings / _settings_set.
        private const string DemoSettingsGet = "{\"version\":1,\"type\":\"DEMO_SETTINGS_GET\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\",\"target\":\"boxing\"}";
        private const string DemoSettings = "{\"version\":1,\"type\":\"DEMO_SETTINGS\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\",\"revision\":4,\"target\":\"boxing\",\"installed\":true,\"settings\":{\"haptics_db\":\"2\",\"round\":\"55\"}}";
        private const string DemoSettingsSet = "{\"version\":1,\"type\":\"DEMO_SETTINGS_SET\",\"controller_id\":\"remote-pixel\",\"seq\":48,\"demo_id\":\"demo_hub\",\"target\":\"boxing\",\"settings\":{\"haptics_db\":\"4\"}}";

        internal static DemoSwitchDemoSettings ExampleSettings() =>
            new DemoSwitchDemoSettings(4, true, new Dictionary<string, string> { ["round"] = "55", ["haptics_db"] = "2" });

        private static string WithAuth(string json, string auth) => json.Substring(0, json.Length - 1) + ",\"auth\":\"" + auth + "\"}";

        [Test]
        public void GetAndReplyMatchTheFixturesAndHmacVectors()
        {
            var get = DemoSwitchProtocol.ParseDemoSettingsGet(DemoSettingsGet);
            Assert.That(get.Success, Is.True, get.ErrorMessage);
            Assert.That(get.Request.Target, Is.EqualTo("boxing"));
            Assert.That(DemoSwitchProtocol.ComputeAuth(get.Request, Secret), Is.EqualTo("bbd39a11d9b607e9ce63b1541d102768c294f93b8d64b6e192147f0810d00c40"));
            Assert.That(DemoSwitchProtocol.Authenticate(DemoSwitchProtocol.ParseDemoSettingsGet(WithAuth(DemoSettingsGet, "bbd39a11d9b607e9ce63b1541d102768c294f93b8d64b6e192147f0810d00c40")).Request, Secret), Is.True);

            Assert.That(DemoSwitchProtocol.SerializeDemoSettings(get.Request, ExampleSettings(), string.Empty), Is.EqualTo(DemoSettings), "Settings sorted by option ID.");
            var reply = new DemoSwitchDemoSettingsReply("remote-pixel", "0123456789abcdef", "boxing", ExampleSettings());
            Assert.That(DemoSwitchProtocol.Canonicalize(reply), Does.EndWith("target=6:boxing\ninstalled=4:true\nsettings=21:haptics_db=2|round=55\n"));
            Assert.That(JObject.Parse(DemoSwitchProtocol.SerializeDemoSettings(get.Request, ExampleSettings(), Secret)).Value<string>("auth"), Is.EqualTo("bcbd4b9a0a15ede79e48bedf1ab30fdc7c636c81c3bf2735d0af67354ae0a8a5"));
        }

        [Test]
        public void ADemoThatIsNotInstalledHasNoSettings()
        {
            var get = DemoSwitchProtocol.ParseDemoSettingsGet(DemoSettingsGet).Request;
            var none = JObject.Parse(DemoSwitchProtocol.SerializeDemoSettings(get, new DemoSwitchDemoSettings(4, false, null), string.Empty));
            Assert.That(none.Value<bool>("installed"), Is.False);
            Assert.That(none["settings"], Is.Empty);
            Assert.That(DemoSwitchProtocol.Canonicalize(new DemoSwitchDemoSettingsReply("remote-pixel", "0123456789abcdef", "boxing",
                new DemoSwitchDemoSettings(4, false, null))), Does.EndWith("installed=5:false\nsettings=0:\n"));
            Assert.Throws<ArgumentException>(() => DemoSwitchProtocol.SerializeDemoSettings(get,
                new DemoSwitchDemoSettings(4, false, new Dictionary<string, string> { ["round"] = "55" }), string.Empty));
        }

        [Test]
        public void SetMatchesTheFixtureAndHmacVector()
        {
            var set = DemoSwitchProtocol.ParseDemoSettingsSet(DemoSettingsSet);
            Assert.That(set.Success, Is.True, set.ErrorMessage);
            Assert.That(set.Command.Target, Is.EqualTo("boxing"));
            Assert.That(set.Command.Settings, Is.EquivalentTo(new Dictionary<string, string> { ["haptics_db"] = "4" }));
            Assert.That(DemoSwitchProtocol.Canonicalize(set.Command), Does.StartWith("HAPBEAT-DEMO-SWITCH/1\nCOMMAND\n"));
            Assert.That(DemoSwitchProtocol.Canonicalize(set.Command), Does.EndWith("target=6:boxing\nsettings=12:haptics_db=4\n"));
            Assert.That(DemoSwitchProtocol.ComputeAuth(set.Command, Secret), Is.EqualTo("0e18d85624c98c0f63b39291176219c48a1e560e452a5398ddc28b05c3f3571e"));
            var tampered = DemoSwitchProtocol.ParseDemoSettingsSet(WithAuth(DemoSettingsSet.Replace("\"4\"", "\"6\""), "0e18d85624c98c0f63b39291176219c48a1e560e452a5398ddc28b05c3f3571e"));
            Assert.That(DemoSwitchProtocol.Authenticate(tampered.Command, Secret), Is.False, "settings are signed.");
            var eight = DemoSettingsSet.Replace("{\"haptics_db\":\"4\"}",
                "{" + string.Join(",", Enumerable.Range(0, 8).Select(i => "\"o" + i + "\":\"v\"")) + "}");
            Assert.That(DemoSwitchProtocol.ParseDemoSettingsSet(eight).Success, Is.True);
        }

        [TestCase("\"target\":\"boxing\"", "\"target\":\"Boxing\"")]
        [TestCase("\"target\":\"boxing\"", "\"target\":\"boxing\",\"from\":0")]
        [TestCase(",\"target\":\"boxing\"", "")]
        [TestCase("\"nonce\":\"0123456789abcdef\"", "\"nonce\":\"xyz\"")]
        [TestCase("\"type\":\"DEMO_SETTINGS_GET\"", "\"type\":\"HUB_SETTINGS_GET\"")]
        public void GetParserRejectsSchemaViolations(string from, string to)
        {
            Assert.That(DemoSettingsGet.Contains(from), Is.True, from);
            Assert.That(DemoSwitchProtocol.ParseDemoSettingsGet(DemoSettingsGet.Replace(from, to)).Success, Is.False, to);
        }

        [TestCase("{\"haptics_db\":\"4\"}", "{}")]
        [TestCase("{\"haptics_db\":\"4\"}", "{\"o0\":\"v\",\"o1\":\"v\",\"o2\":\"v\",\"o3\":\"v\",\"o4\":\"v\",\"o5\":\"v\",\"o6\":\"v\",\"o7\":\"v\",\"o8\":\"v\"}")]
        [TestCase("{\"haptics_db\":\"4\"}", "{\"haptics_db\":4}")]
        [TestCase("{\"haptics_db\":\"4\"}", "{\"haptics_db\":\"4|5\"}")]
        [TestCase("{\"haptics_db\":\"4\"}", "{\"Haptics\":\"4\"}")]
        [TestCase("\"demo_id\":\"demo_hub\"", "\"demo_id\":\"boxing\"")]
        [TestCase("\"seq\":48", "\"seq\":0")]
        [TestCase("\"target\":\"boxing\"", "\"target\":\"../x\"")]
        [TestCase("\"seq\":48", "\"nonce\":\"0123456789abcdef\"")]
        [TestCase("\"settings\":{\"haptics_db\":\"4\"}", "\"settings\":{\"haptics_db\":\"4\"},\"steps\":[]")]
        public void SetParserRejectsSchemaViolations(string from, string to)
        {
            Assert.That(DemoSettingsSet.Contains(from), Is.True, from);
            Assert.That(DemoSwitchProtocol.ParseDemoSettingsSet(DemoSettingsSet.Replace(from, to)).Success, Is.False, to);
        }

        [Test]
        public void GetHandlerUsesTheQueryAuthenticationPolicy()
        {
            Func<string, DemoSwitchDemoSettings> read = target => ExampleSettings();
            Assert.That(DemoSwitchDemoSettingsGetHandler.Handle(DemoSettingsGet, read, string.Empty, false).ErrorCode, Is.EqualTo("unsigned_disabled"));
            Assert.That(DemoSwitchDemoSettingsGetHandler.Handle(DemoSettingsGet, read, string.Empty, true).ResponseJson, Is.EqualTo(DemoSettings));
            Assert.That(DemoSwitchDemoSettingsGetHandler.Handle(DemoSettingsGet, read, Secret, true).ErrorCode, Is.EqualTo("invalid_auth"));
            var signed = DemoSwitchDemoSettingsGetHandler.Handle(WithAuth(DemoSettingsGet, "bbd39a11d9b607e9ce63b1541d102768c294f93b8d64b6e192147f0810d00c40"), read, Secret, false);
            Assert.That(JObject.Parse(signed.ResponseJson).Value<string>("auth"), Is.EqualTo("bcbd4b9a0a15ede79e48bedf1ab30fdc7c636c81c3bf2735d0af67354ae0a8a5"));
        }

        [Test]
        public void ADescriptorWhoseSettingsCouldExceedTheDatagramDoesNotFit()
        {
            string Descriptor(int idLength, int valueLength) =>
                "{\"version\":1,\"demo_id\":\"" + new string('d', 64) + "\",\"title\":{\"ja\":\"x\"},\"supports\":{\"haptics_toggle\":false},\"options\":[" +
                string.Join(",", Enumerable.Range(0, 8).Select(i => "{\"id\":\"" + i + new string('o', idLength - 1) + "\",\"label\":{\"ja\":\"x\"},\"default\":\"a\"," +
                    "\"values\":[{\"value\":\"a\",\"label\":{\"ja\":\"a\"}},{\"value\":\"" + new string('v', valueLength) + "\",\"label\":{\"ja\":\"b\"}}]}")) + "]}";
            Assert.That(DemoSessionDescriptor.TryParse(Descriptor(10, 10), out var small, out var error), Is.True, error);
            Assert.That(DemoSwitchHub.FitsDemoSettings(small), Is.True);
            Assert.That(DemoSessionDescriptor.TryParse(Descriptor(64, 32), out var large, out error), Is.True, error);
            Assert.That(DemoSwitchHub.FitsDemoSettings(large), Is.False, "8 × (64 + 32) bytes of settings alone exceed what is left.");
        }
    }
}
