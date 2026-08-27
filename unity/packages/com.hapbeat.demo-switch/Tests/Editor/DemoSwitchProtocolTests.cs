using NUnit.Framework;

namespace Hapbeat.DemoSwitch.Tests
{
    public sealed class DemoSwitchProtocolTests
    {
        private const string Secret = "correct horse battery staple";

        [Test]
        public void ParserAcceptsVersionOneSwitchCommand()
        {
            const string json = "{\"version\":1,\"type\":\"SWITCH\",\"controller_id\":\"m5-main\",\"seq\":42,\"demo_id\":\"gloveball\"}";

            var result = DemoSwitchProtocol.ParseCommand(json);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.Command.ControllerId, Is.EqualTo("m5-main"));
            Assert.That(result.Command.Sequence, Is.EqualTo(42));
            Assert.That(result.Command.DemoId, Is.EqualTo("gloveball"));
        }

        [TestCase("{}")]
        [TestCase("{\"version\":2,\"type\":\"SWITCH\",\"controller_id\":\"m5-main\",\"seq\":42,\"demo_id\":\"gloveball\"}")]
        [TestCase("{\"version\":1,\"type\":\"SWITCH\",\"controller_id\":\"m5-main\",\"seq\":42,\"demo_id\":\"gloveball\",\"package\":\"evil.app\"}")]
        [TestCase("{\"version\":1,\"type\":\"SWITCH\",\"controller_id\":\"m5-main\",\"seq\":42,\"seq\":43,\"demo_id\":\"gloveball\"}")]
        [TestCase("{\"version\":1,\"type\":\"SWITCH\",\"controller_id\":\"m5-main\",\"seq\":999999999999999999999999999999999999,\"demo_id\":\"gloveball\"}")]
        public void ParserRejectsIncompleteUnsupportedOrExtendedCommands(string json)
        {
            Assert.That(DemoSwitchProtocol.ParseCommand(json).Success, Is.False);
        }

        [Test]
        public void CanonicalCommandAndHmacMatchWorkedFixture()
        {
            var command = new DemoSwitchCommand("m5-main", 42, "gloveball", string.Empty);

            Assert.That(DemoSwitchProtocol.Canonicalize(command), Is.EqualTo(
                "HAPBEAT-DEMO-SWITCH/1\nCOMMAND\n" +
                "version=1:1\ntype=6:SWITCH\ncontroller_id=7:m5-main\n" +
                "seq=2:42\ndemo_id=9:gloveball\n"));
            Assert.That(DemoSwitchProtocol.ComputeAuth(command, Secret), Is.EqualTo(
                "a1fae3d007d76e41694e2893c9444f886b9f0c6d2e1d0424913c5bbb0bd73d8e"));
        }

        [Test]
        public void AuthRejectsMissingAndInvalidMacWhenSecretConfigured()
        {
            var unsigned = new DemoSwitchCommand("m5-main", 42, "gloveball", string.Empty);
            var invalid = new DemoSwitchCommand("m5-main", 42, "gloveball", new string('0', 64));

            Assert.That(DemoSwitchProtocol.Authenticate(unsigned, Secret), Is.False);
            Assert.That(DemoSwitchProtocol.Authenticate(invalid, Secret), Is.False);
        }

        [Test]
        public void StatusSerializesRequiredFieldsAndValidAuthentication()
        {
            var status = new DemoSwitchStatus("READY", "m5-main", 42, "gloveball", "gloveball", "ok", string.Empty);

            var json = DemoSwitchProtocol.SerializeStatus(status, Secret);
            var parsed = DemoSwitchProtocol.ParseStatus(json);

            Assert.That(parsed.Success, Is.True, parsed.ErrorMessage);
            Assert.That(parsed.Status.Type, Is.EqualTo("READY"));
            Assert.That(parsed.Status.CurrentDemoId, Is.EqualTo("gloveball"));
            Assert.That(DemoSwitchProtocol.Authenticate(parsed.Status, Secret), Is.True);
        }

        [Test]
        public void StatusParserRejectsUnknownDuplicateAndInvalidCode()
        {
            const string invalidCode = "{\"version\":1,\"type\":\"FAILED\",\"controller_id\":\"m5-main\",\"seq\":42,\"demo_id\":\"gloveball\",\"current_demo_id\":\"gloveball\",\"code\":\"anything\",\"message\":\"\"}";
            const string duplicate = "{\"version\":1,\"type\":\"READY\",\"controller_id\":\"m5-main\",\"seq\":42,\"seq\":43,\"demo_id\":\"gloveball\",\"current_demo_id\":\"gloveball\",\"code\":\"ok\",\"message\":\"\"}";
            const string unknown = "{\"version\":1,\"type\":\"READY\",\"controller_id\":\"m5-main\",\"seq\":42,\"demo_id\":\"gloveball\",\"current_demo_id\":\"gloveball\",\"code\":\"ok\",\"message\":\"\",\"extra\":true}";

            Assert.That(DemoSwitchProtocol.ParseStatus(invalidCode).Success, Is.False);
            Assert.That(DemoSwitchProtocol.ParseStatus(duplicate).Success, Is.False);
            Assert.That(DemoSwitchProtocol.ParseStatus(unknown).Success, Is.False);
        }

        [Test]
        public void StatusSerializationKeepsUtf8BoundaryAndTruncatesBeyondItBeforeSigning()
        {
            var boundary = new string('\u00e9', 128);
            var status = new DemoSwitchStatus("FAILED", "m5-main", 42, "gloveball", "gloveball", "launch_failed", boundary + "x");

            var json = DemoSwitchProtocol.SerializeStatus(status, Secret);
            var parsed = DemoSwitchProtocol.ParseStatus(json);

            Assert.That(System.Text.Encoding.UTF8.GetByteCount(json), Is.LessThanOrEqualTo(DemoSwitchProtocol.MaxPayloadBytes));
            Assert.That(parsed.Success, Is.True, parsed.ErrorMessage);
            Assert.That(parsed.Status.Message, Is.EqualTo(boundary));
            Assert.That(System.Text.Encoding.UTF8.GetByteCount(parsed.Status.Message), Is.EqualTo(256));
            Assert.That(DemoSwitchProtocol.Authenticate(parsed.Status, Secret), Is.True);
        }

        [Test]
        public void StatusParserRejectsMessageBeyondUtf8Limit()
        {
            var message = new string('\u00e9', 129);
            var json = "{\"version\":1,\"type\":\"FAILED\",\"controller_id\":\"m5-main\",\"seq\":42,\"demo_id\":\"gloveball\",\"current_demo_id\":\"gloveball\",\"code\":\"launch_failed\",\"message\":\"" + message + "\"}";

            Assert.That(DemoSwitchProtocol.ParseStatus(json).Success, Is.False);
        }

        [Test]
        public void StatusSerializationShrinksEscapedDiagnosticsToWholePayloadLimit()
        {
            var status = new DemoSwitchStatus("FAILED", "m5-main", 42, "gloveball", "gloveball", "launch_failed", new string('\0', 256));

            var json = DemoSwitchProtocol.SerializeStatus(status, Secret);
            var parsed = DemoSwitchProtocol.ParseStatus(json);

            Assert.That(System.Text.Encoding.UTF8.GetByteCount(json), Is.LessThanOrEqualTo(DemoSwitchProtocol.MaxPayloadBytes));
            Assert.That(parsed.Success, Is.True, parsed.ErrorMessage);
            Assert.That(parsed.Status.Message.Length, Is.LessThan(256));
            Assert.That(DemoSwitchProtocol.Authenticate(parsed.Status, Secret), Is.True);
        }

        [Test]
        public void DiscoveryParserAcceptsCanonicalRequestAndHereRoundTrip()
        {
            var unsigned = new DemoSwitchDiscover("m5-main", "0123456789abcdef", string.Empty);
            var auth = DemoSwitchProtocol.ComputeAuth(unsigned, Secret);
            var json = "{\"version\":1,\"type\":\"DISCOVER\",\"controller_id\":\"m5-main\",\"nonce\":\"0123456789abcdef\",\"auth\":\"" + auth + "\"}";

            var parsed = DemoSwitchProtocol.ParseDiscover(json);

            Assert.That(parsed.Success, Is.True, parsed.ErrorMessage);
            Assert.That(DemoSwitchProtocol.Authenticate(parsed.Discover, Secret), Is.True);
            Assert.That(DemoSwitchProtocol.Canonicalize(parsed.Discover), Is.EqualTo(
                "HAPBEAT-DEMO-SWITCH/1\nDISCOVER\nversion=1:1\ntype=8:DISCOVER\n" +
                "controller_id=7:m5-main\nnonce=16:0123456789abcdef\n"));

            var hereJson = DemoSwitchProtocol.SerializeHere(
                new DemoSwitchHere(parsed.Discover.ControllerId, parsed.Discover.Nonce, "gloveball"), Secret);
            var here = DemoSwitchProtocol.ParseHere(hereJson);
            Assert.That(here.Success, Is.True, here.ErrorMessage);
            Assert.That(here.Here.CurrentDemoId, Is.EqualTo("gloveball"));
            Assert.That(DemoSwitchProtocol.Authenticate(here.Here, Secret), Is.True);
            Assert.That(DemoSwitchProtocol.Canonicalize(here.Here), Is.EqualTo(
                "HAPBEAT-DEMO-SWITCH/1\nHERE\nversion=1:1\ntype=4:HERE\n" +
                "controller_id=7:m5-main\nnonce=16:0123456789abcdef\ncurrent_demo_id=9:gloveball\n"));
        }

        [TestCase("{\"version\":1,\"type\":\"DISCOVER\",\"controller_id\":\"m5-main\",\"nonce\":\"0123456789abcdef\",\"extra\":true}")]
        [TestCase("{\"version\":1,\"type\":\"DISCOVER\",\"controller_id\":\"m5-main\",\"nonce\":\"0123456789abcdef\",\"nonce\":\"fedcba9876543210\"}")]
        [TestCase("{\"version\":1,\"type\":\"DISCOVER\",\"controller_id\":\"m5-main\",\"nonce\":\"0123456789ABCDEF\"}")]
        [TestCase("{\"version\":1,\"type\":\"DISCOVER\",\"controller_id\":\"m5-main\",\"nonce\":\"0123456789abcdef\"}garbage")]
        [TestCase("{\"version\":1,/* comment */\"type\":\"DISCOVER\",\"controller_id\":\"m5-main\",\"nonce\":\"0123456789abcdef\"}")]
        public void DiscoveryParserRejectsUnknownDuplicateInvalidNonceAndTrailingContent(string json)
        {
            Assert.That(DemoSwitchProtocol.ParseDiscover(json).Success, Is.False);
        }

        [Test]
        public void DiscoveryParserRejectsPayloadBeyondDatagramLimit()
        {
            var json = "{\"version\":1,\"type\":\"DISCOVER\",\"controller_id\":\"m5-main\",\"nonce\":\"0123456789abcdef\",\"padding\":\"" +
                       new string('x', DemoSwitchProtocol.MaxPayloadBytes) + "\"}";

            Assert.That(System.Text.Encoding.UTF8.GetByteCount(json), Is.GreaterThan(DemoSwitchProtocol.MaxPayloadBytes));
            Assert.That(DemoSwitchProtocol.ParseDiscover(json).Success, Is.False);
        }

        [TestCase("{\"version\":1,\"type\":\"HERE\",\"controller_id\":\"m5-main\",\"nonce\":\"0123456789abcdef\",\"current_demo_id\":\"gloveball\",\"extra\":true}")]
        [TestCase("{\"version\":1,\"type\":\"HERE\",\"controller_id\":\"m5-main\",\"nonce\":\"0123456789abcdef\",\"nonce\":\"fedcba9876543210\",\"current_demo_id\":\"gloveball\"}")]
        [TestCase("{\"version\":1,\"type\":\"HERE\",\"controller_id\":\"m5-main\",\"nonce\":\"0123456789abcdef\",\"current_demo_id\":\"gloveball\"}garbage")]
        public void HereParserRejectsUnknownDuplicateAndTrailingContent(string json)
        {
            Assert.That(DemoSwitchProtocol.ParseHere(json).Success, Is.False);
        }

        [Test]
        public void DiscoveryHandlerRequiresConfiguredAuthenticationPolicyAndBuildsHere()
        {
            var unsignedJson = "{\"version\":1,\"type\":\"DISCOVER\",\"controller_id\":\"m5-main\",\"nonce\":\"0123456789abcdef\"}";
            var unsignedRejected = DemoSwitchDiscoveryHandler.Handle(unsignedJson, "gloveball", string.Empty, false);
            var unsignedAllowed = DemoSwitchDiscoveryHandler.Handle(unsignedJson, "gloveball", string.Empty, true);
            var signedTemplate = new DemoSwitchDiscover("m5-main", "0123456789abcdef", string.Empty);
            var signedJson = "{\"version\":1,\"type\":\"DISCOVER\",\"controller_id\":\"m5-main\",\"nonce\":\"0123456789abcdef\",\"auth\":\"" +
                             DemoSwitchProtocol.ComputeAuth(signedTemplate, Secret) + "\"}";
            var signed = DemoSwitchDiscoveryHandler.Handle(signedJson, "gloveball", Secret, false);

            Assert.That(unsignedRejected.ShouldReply, Is.False);
            Assert.That(unsignedRejected.ErrorCode, Is.EqualTo("unsigned_disabled"));
            Assert.That(unsignedAllowed.ShouldReply, Is.True);
            Assert.That(DemoSwitchProtocol.ParseHere(unsignedAllowed.ResponseJson).Success, Is.True);
            Assert.That(signed.ShouldReply, Is.True);
            var parsedSignedHere = DemoSwitchProtocol.ParseHere(signed.ResponseJson);
            Assert.That(parsedSignedHere.Success, Is.True, parsedSignedHere.ErrorMessage);
            Assert.That(DemoSwitchProtocol.Authenticate(parsedSignedHere.Here, Secret), Is.True);
        }

        [Test]
        public void DiscoveryHandlerRejectsMissingOrInvalidMacWhenSecretConfigured()
        {
            const string unsignedJson = "{\"version\":1,\"type\":\"DISCOVER\",\"controller_id\":\"m5-main\",\"nonce\":\"0123456789abcdef\"}";
            var invalidJson = unsignedJson.Substring(0, unsignedJson.Length - 1) + ",\"auth\":\"" + new string('0', 64) + "\"}";

            Assert.That(DemoSwitchDiscoveryHandler.Handle(unsignedJson, "gloveball", Secret, true).ErrorCode,
                Is.EqualTo("invalid_auth"));
            Assert.That(DemoSwitchDiscoveryHandler.Handle(invalidJson, "gloveball", Secret, true).ErrorCode,
                Is.EqualTo("invalid_auth"));
        }
    }
}
