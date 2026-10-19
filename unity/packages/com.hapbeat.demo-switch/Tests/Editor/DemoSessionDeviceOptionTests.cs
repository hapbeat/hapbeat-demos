using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hapbeat.DemoSwitch.Tests
{
    /// <summary>
    /// `scope: device` options (contracts demo-session.md "デモごとの設定"): a runtime keeps the values a ticket brought and
    /// uses them, else the descriptor default, when it starts without a ticket. Session options are never kept.
    /// </summary>
    public sealed class DemoSessionDeviceOptionTests
    {
        // hapbeat-contracts fixtures/sample-demo-session.json descriptor_boxing (labels shortened).
        const string BoxingDescriptor = @"{""version"":1,""demo_id"":""boxing"",""title"":{""ja"":""ボクシング""},""minutes"":2,""supports"":{""haptics_toggle"":true},
 ""options"":[
  {""id"":""round"",""label"":{""ja"":""ラウンド""},""default"":""55"",""values"":[{""value"":""30"",""label"":{""ja"":""30秒""}},{""value"":""55"",""label"":{""ja"":""55秒""}}]},
  {""id"":""haptics_db"",""label"":{""ja"":""触覚の強さ""},""default"":""2"",""scope"":""device"",
   ""values"":[{""value"":""0"",""label"":{""ja"":""0 dB""}},{""value"":""2"",""label"":{""ja"":""-2 dB""}},{""value"":""6"",""label"":{""ja"":""-6 dB""}}]}]}";

        static string Ticket(string options) => @"{""version"":1,""session_id"":""0f3a9c2e7b1d4a56"",""index"":0,""haptics_ui"":false,""steps"":[" +
            @"{""demo_id"":""boxing"",""title"":""Boxing"",""package"":""com.hapbeat.boxing"",""activity"":""com.unity3d.player.UnityPlayerGameActivity"",""options"":" + options + @",""retry"":true}]," +
            @"""finish"":{""package"":""jp.hapbeat.demohub"",""activity"":""com.unity3d.player.UnityPlayerGameActivity""}}";

        sealed class LaunchPlatform : IDemoSessionPlatform
        {
            public string Extra;
            public string Descriptor = BoxingDescriptor;
            public bool TryTakeStringExtra(string name, out string value)
            {
                value = name == DemoSession.TicketExtra ? Extra : null;
                Extra = null;
                return value != null;
            }
            public bool TryLaunch(string packageName, string activityName, string ticketJson, out string error) { error = "none"; return false; }
            public void FinishTask() { }
            public bool TryReadOwnAsset(string name, out string text, out string error)
            {
                text = Descriptor;
                error = text == null ? "none" : null;
                return text != null;
            }
            public bool TryGetOwnComponent(out DemoSessionComponent component) { component = null; return false; }
            public IReadOnlyList<DemoSessionLauncherActivity> ListLauncherActivities() => new DemoSessionLauncherActivity[0];
            public bool TryReadPackageAsset(string packageName, string name, out string text, out string error) { text = null; error = "none"; return false; }
            public bool IsPackageInstalled(string packageName) => false;
        }

        MemoryDemoDeviceOptionStore _store;
        LaunchPlatform _platform;

        [SetUp]
        public void SetUp()
        {
            _store = new MemoryDemoDeviceOptionStore();
            _platform = new LaunchPlatform();
            DemoSession.ResetForTests(_platform, deviceOptions: _store);
        }

        [TearDown]
        public void TearDown() => DemoSession.ResetForTests();

        /// <summary>A cold start: the bootstrap reads the descriptor and the ticket (null: none) like a launch.</summary>
        void Launch(string ticket)
        {
            DemoSession.ResetForTests(_platform, deviceOptions: _store);
            _platform.Extra = ticket;
            DemoSession.Initialize("boxing", _platform);
        }

        [Test]
        public void ScopeIsParsedAndDefaultsToSession()
        {
            Assert.That(DemoSessionDescriptor.TryParse(BoxingDescriptor, out var descriptor, out var error), Is.True, error);
            Assert.That(descriptor.FindOption("round").Scope, Is.EqualTo(DemoSessionOptionScope.Session));
            Assert.That(descriptor.FindOption("haptics_db").IsDevice, Is.True);
            var session = BoxingDescriptor.Replace(@"""scope"":""device""", @"""scope"":""session""");
            Assert.That(DemoSessionDescriptor.TryParse(session, out descriptor, out error), Is.True, error);
            Assert.That(descriptor.FindOption("haptics_db").IsDevice, Is.False);
            foreach (var scope in new[] { @"""Device""", @"""hub""", "1", "null" })
                Assert.That(DemoSessionDescriptor.TryParse(BoxingDescriptor.Replace(@"""device""", scope), out _, out _), Is.False, scope);
        }

        [Test]
        public void WithoutAnyTicketTheDeviceOptionIsTheDescriptorDefault()
        {
            Launch(null);
            Assert.That(DemoSession.IsActive, Is.False);
            Assert.That(DemoSession.Options, Is.EquivalentTo(new Dictionary<string, string> { ["haptics_db"] = "2" }), "Only device options outside a session.");
            Assert.That(DemoSession.GetOption("round"), Is.Null);
        }

        [Test]
        public void ATicketKeepsItsDeviceValueForLaunchesWithoutATicket()
        {
            Launch(Ticket(@"{""round"":""30"",""haptics_db"":""6""}"));
            Assert.That(DemoSession.IsActive, Is.True);
            Assert.That(DemoSession.GetOption("haptics_db"), Is.EqualTo("6"));
            Assert.That(DemoSession.GetOption("round"), Is.EqualTo("30"));
            Assert.That(_store.TryGet("boxing", "round", out _), Is.False, "Session options are never kept.");

            Launch(null);
            Assert.That(DemoSession.IsActive, Is.False);
            Assert.That(DemoSession.GetOption("haptics_db"), Is.EqualTo("6"));
            Assert.That(DemoSession.GetOption("round"), Is.Null);

            // A ticket without the option (or with an unknown value) keeps the stored value; the step uses the default.
            LogAssert.Expect(LogType.Warning, new Regex("haptics_db"));
            Launch(Ticket(@"{""round"":""55"",""haptics_db"":""9""}"));
            Assert.That(DemoSession.GetOption("haptics_db"), Is.EqualTo("2"));
            Launch(null);
            Assert.That(DemoSession.GetOption("haptics_db"), Is.EqualTo("6"));
        }

        [Test]
        public void AStoredValueTheDescriptorNoLongerHasFallsBackToTheDefault()
        {
            _store.Set("boxing", "haptics_db", "12");
            LogAssert.Expect(LogType.Warning, new Regex("haptics_db=12 is not one of its values; using the default 2"));
            Launch(null);
            Assert.That(DemoSession.GetOption("haptics_db"), Is.EqualTo("2"));
        }

        [Test]
        public void ATicketForAnotherDemoStillLoadsTheDeviceOptions()
        {
            _store.Set("boxing", "haptics_db", "0");
            LogAssert.Expect(LogType.Warning, new Regex("Ticket ignored"));
            Launch(Ticket("{}").Replace(@"""demo_id"":""boxing""", @"""demo_id"":""volley"""));
            Assert.That(DemoSession.IsActive, Is.False);
            Assert.That(DemoSession.GetOption("haptics_db"), Is.EqualTo("0"));
        }

        [Test]
        public void PlayerPrefsKeysSeparateTheDemoAndTheOption() =>
            Assert.That(PlayerPrefsDemoDeviceOptionStore.Key("a.b", "c"), Is.Not.EqualTo(PlayerPrefsDemoDeviceOptionStore.Key("a", "b.c")));
    }
}
