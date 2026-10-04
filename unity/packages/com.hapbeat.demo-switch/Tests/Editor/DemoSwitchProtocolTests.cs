using NUnit.Framework;

namespace Hapbeat.DemoSwitch.Tests
{
    public sealed class DemoSwitchProtocolTests
    {
        private const string Secret = "correct horse battery staple";

        [Test]
        public void ControlCanonicalAndAuthBindActionAndScene()
        {
            var command=new DemoSwitchCommand("m5-main",43,"volley","","scene","block");
            Assert.That(DemoSwitchProtocol.ComputeAuth(command,Secret),Is.EqualTo("861ece8d1d310cb1dc9be46ec660ca4f9a8ed5b2ccf1a7a96252cef691b39ae2"));
            var signed=new DemoSwitchCommand("m5-main",43,"volley",DemoSwitchProtocol.ComputeAuth(command,Secret),"scene","receive");
            Assert.That(DemoSwitchProtocol.Authenticate(signed,Secret),Is.False);
        }

        [TestCase("scene","block",true)]
        [TestCase("scene","../block",false)]
        [TestCase("scene","",false)]
        [TestCase("menu_open","",true)]
        [TestCase("menu_close","",true)]
        [TestCase("recenter","",true)]
        [TestCase("restart","",true)]
        [TestCase("toggle","",false)]
        [TestCase("menu_open","block",false)]
        public void ControlParserEnforcesActionAndLogicalScene(string action,string scene,bool valid)
        {
            string json="{\"version\":1,\"type\":\"CONTROL\",\"controller_id\":\"m5-main\",\"seq\":43,\"demo_id\":\"volley\",\"action\":\""+action+"\",\"scene_id\":\""+scene+"\"}";
            Assert.That(DemoSwitchProtocol.ParseCommand(json).Success,Is.EqualTo(valid));
            Assert.That(DemoSwitchProtocol.ParseCommand(json.Replace(",\"scene_id\":\""+scene+"\"","")).Success,Is.False);
            Assert.That(DemoSwitchProtocol.ParseCommand(json.Replace("CONTROL","SWITCH")).Success,Is.False);
            Assert.That(DemoSwitchProtocol.ParseCommand(json.Replace("\"type\":\"CONTROL\"","\"type\":{}")).Success,Is.False);
        }

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

        // hapbeat-contracts fixtures/sample-demo-switch-messages.json unsigned_query / unsigned_state (spec example).
        const string QueryExample = "{\"version\":1,\"type\":\"QUERY\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\"}";
        const string StateExample = "{\"version\":1,\"type\":\"STATE\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\",\"current_demo_id\":\"handdemo\",\"haptics_on\":true,\"haptics_ui\":false,\"recenter_ui\":false,\"paused\":false,\"step_index\":1,\"step_count\":3}";

        [Test]
        public void QueryAndStateMatchTheSpecExampleAndHmacVectors()
        {
            var query = DemoSwitchProtocol.ParseQuery(QueryExample);
            Assert.That(query.Success, Is.True, query.ErrorMessage);
            Assert.That(DemoSwitchProtocol.Canonicalize(query.Query), Is.EqualTo(
                "HAPBEAT-DEMO-SWITCH/1\nQUERY\nversion=1:1\ntype=5:QUERY\ncontroller_id=12:remote-pixel\nnonce=16:0123456789abcdef\n"));
            Assert.That(DemoSwitchProtocol.ComputeAuth(query.Query, Secret), Is.EqualTo("f64cd6fb617ef3fee7968a8580765b6c527467203174e221be5c7da4de1190c2"));

            var state = new DemoSwitchState("remote-pixel", "0123456789abcdef", "handdemo", true, false, false, false, 1, 3);
            Assert.That(DemoSwitchProtocol.SerializeState(state, string.Empty), Is.EqualTo(StateExample), "Field order and values as the example.");
            Assert.That(DemoSwitchProtocol.Canonicalize(state), Is.EqualTo(
                "HAPBEAT-DEMO-SWITCH/1\nSTATE\nversion=1:1\ntype=5:STATE\ncontroller_id=12:remote-pixel\nnonce=16:0123456789abcdef\n" +
                "current_demo_id=8:handdemo\nhaptics_on=4:true\nhaptics_ui=5:false\nrecenter_ui=5:false\npaused=5:false\nstep_index=1:1\nstep_count=1:3\n"));
            Assert.That(DemoSwitchProtocol.ComputeAuth(state, Secret), Is.EqualTo("1c4beefa4a7fbc8de87f2a7498e38e5821557dfae27fa88a7e1f0cda7c5672df"));

            var outside = new DemoSwitchState("remote-pixel", "0123456789abcdef", "demo_hub", false, true, true, true, -1, 0);
            Assert.That(DemoSwitchProtocol.Canonicalize(outside), Is.EqualTo(
                "HAPBEAT-DEMO-SWITCH/1\nSTATE\nversion=1:1\ntype=5:STATE\ncontroller_id=12:remote-pixel\nnonce=16:0123456789abcdef\n" +
                "current_demo_id=8:demo_hub\nhaptics_on=5:false\nhaptics_ui=4:true\nrecenter_ui=4:true\npaused=4:true\nstep_index=2:-1\nstep_count=1:0\n"));
            Assert.That(DemoSwitchProtocol.ComputeAuth(outside, Secret), Is.EqualTo("5db6ac7dca4a1f10b26ae03c7ae4e51e5b853f676e33cf053645a95d61817a38"));

            var signed = DemoSwitchProtocol.ParseState(DemoSwitchProtocol.SerializeState(outside, Secret));
            Assert.That(signed.Success, Is.True, signed.ErrorMessage);
            Assert.That(signed.State.StepIndex, Is.EqualTo(-1));
            Assert.That(signed.State.Paused, Is.True);
            Assert.That(DemoSwitchProtocol.Authenticate(signed.State, Secret), Is.True);
            var parsedExample = DemoSwitchProtocol.ParseState(StateExample);
            Assert.That(parsedExample.Success, Is.True, parsedExample.ErrorMessage);
            Assert.That(parsedExample.State.StepCount, Is.EqualTo(3));
        }

        [TestCase("{\"version\":1,\"type\":\"QUERY\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\",\"extra\":true}")]
        [TestCase("{\"version\":1,\"type\":\"QUERY\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\",\"nonce\":\"fedcba9876543210\"}")]
        [TestCase("{\"version\":1,\"type\":\"QUERY\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789ABCDEF\"}")]
        [TestCase("{\"version\":1,\"type\":\"QUERY\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\",\"seq\":1}")]
        [TestCase("{\"version\":1,\"type\":\"DISCOVER\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\"}")]
        [TestCase("{\"version\":1,\"type\":\"QUERY\",\"controller_id\":\"remote-pixel\",\"nonce\":\"0123456789abcdef\"}garbage")]
        public void QueryParserRejectsUnknownDuplicateInvalidAndTrailingContent(string json)
        {
            Assert.That(DemoSwitchProtocol.ParseQuery(json).Success, Is.False);
        }

        [Test]
        public void StateParserRejectsUnknownFieldsWrongTypesAndOutOfRangeSteps()
        {
            Assert.That(DemoSwitchProtocol.ParseState(StateExample.Replace("}", ",\"extra\":1}")).Success, Is.False);
            Assert.That(DemoSwitchProtocol.ParseState(StateExample.Replace("\"paused\":false", "\"paused\":0")).Success, Is.False);
            Assert.That(DemoSwitchProtocol.ParseState(StateExample.Replace("\"step_index\":1", "\"step_index\":-2")).Success, Is.False);
            Assert.That(DemoSwitchProtocol.ParseState(StateExample.Replace("\"step_count\":3", "\"step_count\":33")).Success, Is.False);
            Assert.That(DemoSwitchProtocol.ParseState(StateExample.Replace(",\"haptics_ui\":false", "")).Success, Is.False);
        }

        [Test]
        public void QueryHandlerUsesTheDiscoveryAuthenticationPolicy()
        {
            DemoSwitchState Answer(DemoSwitchQuery q) => new DemoSwitchState(q.ControllerId, q.Nonce, "handdemo", true, false, false, false, 1, 3);
            var signedJson = QueryExample.Substring(0, QueryExample.Length - 1) + ",\"auth\":\"f64cd6fb617ef3fee7968a8580765b6c527467203174e221be5c7da4de1190c2\"}";
            var invalidJson = QueryExample.Substring(0, QueryExample.Length - 1) + ",\"auth\":\"" + new string('0', 64) + "\"}";

            Assert.That(DemoSwitchQueryHandler.Handle(QueryExample, Answer, string.Empty, false).ErrorCode, Is.EqualTo("unsigned_disabled"));
            var unsigned = DemoSwitchQueryHandler.Handle(QueryExample, Answer, string.Empty, true);
            Assert.That(unsigned.ResponseJson, Is.EqualTo(StateExample));
            Assert.That(DemoSwitchQueryHandler.Handle(QueryExample, Answer, Secret, true).ErrorCode, Is.EqualTo("invalid_auth"));
            Assert.That(DemoSwitchQueryHandler.Handle(invalidJson, Answer, Secret, true).ErrorCode, Is.EqualTo("invalid_auth"));
            Assert.That(DemoSwitchQueryHandler.Handle("{\"version\":1,\"type\":\"QUERY\"}", Answer, Secret, true).ErrorCode, Is.EqualTo("invalid_payload"));
            var signed = DemoSwitchQueryHandler.Handle(signedJson, Answer, Secret, false);
            Assert.That(signed.ShouldReply, Is.True);
            var state = DemoSwitchProtocol.ParseState(signed.ResponseJson);
            Assert.That(state.Success, Is.True, state.ErrorMessage);
            Assert.That(state.State.Auth, Is.EqualTo("1c4beefa4a7fbc8de87f2a7498e38e5821557dfae27fa88a7e1f0cda7c5672df"));
        }

        [TestCase("tutorial_start","",true)]
        [TestCase("tutorial_start","intro",false)]
        public void TutorialStartIsAControlAction(string action,string scene,bool valid)
        {
            string json="{\"version\":1,\"type\":\"CONTROL\",\"controller_id\":\"m5-main\",\"seq\":43,\"demo_id\":\"handdemo\",\"action\":\""+action+"\",\"scene_id\":\""+scene+"\"}";
            Assert.That(DemoSwitchProtocol.ParseCommand(json).Success,Is.EqualTo(valid));
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
