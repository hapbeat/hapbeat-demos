using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Hapbeat.DemoSwitch;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hapbeat.DemoHub.Tests
{
    public sealed class HubPlanTests
    {
        IReadOnlyList<DemoSessionCatalogEntry> _catalog;
        DemoSessionDescriptor _volley, _boxing, _trex;
        string _directory;

        [SetUp]
        public void SetUp()
        {
            _catalog = HubCatalog.EditorDummy();
            _volley = _catalog.Single(e => e.Descriptor.DemoId == "volley").Descriptor;
            _boxing = _catalog.Single(e => e.Descriptor.DemoId == "boxing").Descriptor;
            _trex = _catalog.Single(e => e.Descriptor.DemoId == "trex-encounter").Descriptor;
            _directory = Path.Combine(Path.GetTempPath(), "hapbeat-hub-tests-" + System.Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        [Test]
        public void AddMoveAndRemoveKeepOrder()
        {
            var plan = new HubPlan();
            Assert.That(plan.Add(_volley) && plan.Add(_boxing) && plan.Add(_trex), Is.True);
            Assert.That(plan.Steps[0].Options, Is.EquivalentTo(new Dictionary<string, string> { ["scene"] = "block", ["points"] = "7", ["balls"] = "10" }));
            Assert.That(plan.Steps.All(s => s.Retry), Is.True, "New steps offer retry by default.");
            Assert.That(plan.MoveUp(0), Is.False);
            Assert.That(plan.MoveDown(2), Is.False);
            Assert.That(plan.MoveDown(0), Is.True);
            Assert.That(plan.Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "boxing", "volley", "trex-encounter" }));
            Assert.That(plan.MoveUp(2), Is.True);
            Assert.That(plan.Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "boxing", "trex-encounter", "volley" }));
            Assert.That(plan.Move(0, 2), Is.True, "Insert: the others close up.");
            Assert.That(plan.Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "trex-encounter", "volley", "boxing" }));
            Assert.That(plan.Move(2, 0), Is.True);
            Assert.That(plan.Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "boxing", "trex-encounter", "volley" }));
            Assert.That(plan.Move(1, 1) || plan.Move(0, 3) || plan.Move(-1, 0), Is.False);
            Assert.That(plan.Remove(1), Is.True);
            Assert.That(plan.Remove(5), Is.False);
            Assert.That(plan.Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "boxing", "volley" }));
            for (var i = plan.Steps.Count; i < HubPlan.MaxSteps; i++) plan.Add(_trex);
            Assert.That(plan.Add(_trex), Is.False, "32 steps maximum.");
        }

        [Test]
        public void OptionChipsCycleAndFollowWhen()
        {
            var plan = new HubPlan();
            plan.Add(_volley);
            var step = plan.Steps[0];
            Assert.That(HubPlan.VisibleOptions(step, _volley).Select(o => o.Id), Is.EqualTo(new[] { "scene", "points" }));
            Assert.That(plan.CycleOption(0, _volley, "points"), Is.True);
            Assert.That(step.Options["points"], Is.EqualTo("3"), "7 wraps to 3.");
            Assert.That(plan.CycleOption(0, _volley, "balls"), Is.False, "Inactive option cannot be cycled.");
            Assert.That(plan.CycleOption(0, _volley, "scene"), Is.True);
            Assert.That(HubPlan.VisibleOptions(step, _volley).Select(o => o.Id), Is.EqualTo(new[] { "scene", "balls" }));
            var volley = _catalog.Single(e => e.Descriptor.DemoId == "volley");
            Assert.That(HubPlan.Title(step, volley), Is.EqualTo("Volley レシーブ 10球"));
            Assert.That(plan.CycleOption(0, _volley, "scene"), Is.True);
            Assert.That(HubPlan.Title(step, volley), Is.EqualTo("Volley ブロック 3点先取"), "Hidden choices are remembered.");
            plan.ToggleRetry(0);
            Assert.That(step.Retry, Is.False);
            Assert.That(HubPlan.Truncate(new string('あ', 50), 40).Length, Is.EqualTo(40));
        }

        [Test]
        public void PresetsRoundTrip()
        {
            var store = new HubPlanStore(_directory);
            Assert.That(store.TryLoad(HubPlanStore.PresetSlot(1), out _), Is.False);
            Assert.That(store.LoadPreset(1).Steps, Is.Empty);
            var plan = new HubPlan();
            plan.Add(_volley);
            plan.CycleOption(0, _volley, "scene");
            plan.ToggleRetry(0);
            plan.Add(_trex);
            for (var number = 1; number <= HubPlanStore.PresetCount; number++)
                Assert.That(store.Save(plan, HubPlanStore.PresetSlot(number)), Is.True);
            Assert.That(store.TryLoad(HubPlanStore.PresetSlot(2), out var loaded), Is.True);
            Assert.That(loaded.Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "volley", "trex-encounter" }));
            Assert.That(loaded.Steps[0].Options["scene"], Is.EqualTo("receive"));
            Assert.That(loaded.Steps[0].Retry, Is.False);
            File.WriteAllText(Path.Combine(_directory, "preset-3.json"), "{broken");
            Assert.That(store.TryLoad(HubPlanStore.PresetSlot(3), out _), Is.False);
            Assert.That(store.LoadPreset(3).Steps, Is.Empty, "A broken preset is empty.");
        }

        [Test]
        public void NameAndRevisionRoundTripAndOlderFilesLoad()
        {
            var store = new HubPlanStore(_directory);
            var plan = new HubPlan { Name = "XR Kaigi A", Revision = 5 };
            plan.Add(_volley);
            Assert.That(store.Save(plan, HubPlanStore.PresetSlot(1)), Is.True);
            Assert.That(store.TryLoad(HubPlanStore.PresetSlot(1), out var loaded), Is.True);
            Assert.That(loaded.Name, Is.EqualTo("XR Kaigi A"));
            Assert.That(loaded.Revision, Is.EqualTo(5));

            File.WriteAllText(Path.Combine(_directory, "preset-2.json"), "{\"version\":1,\"steps\":[{\"demo_id\":\"volley\",\"options\":{},\"retry\":true}]}");
            Assert.That(store.TryLoad(HubPlanStore.PresetSlot(2), out var old), Is.True, "Files from before name / revision still load.");
            Assert.That(old.Name, Is.EqualTo(""));
            Assert.That(old.Revision, Is.Zero);
            Assert.That(old.Steps.Single().DemoId, Is.EqualTo("volley"));

            File.WriteAllText(Path.Combine(_directory, "preset-3.json"), "{\"version\":1,\"name\":\" \",\"revision\":-4,\"steps\":[]}");
            Assert.That(store.TryLoad(HubPlanStore.PresetSlot(3), out var invalid), Is.True);
            Assert.That(invalid.Name, Is.EqualTo(""), "A name outside the rules is dropped.");
            Assert.That(invalid.Revision, Is.Zero);
        }

        [Test]
        public void EverySavedPresetIncreasesItsRevision()
        {
            var store = new HubPlanStore(_directory);
            var plan = new HubPlan();
            plan.Add(_boxing);
            Assert.That(store.SavePreset(plan, 1), Is.True);
            Assert.That(store.SavePreset(plan, 1), Is.True);
            Assert.That(plan.Revision, Is.EqualTo(2));
            Assert.That(store.LoadPreset(1).Revision, Is.EqualTo(2));

            // A file where the directory should be: the write fails and the revision stays.
            var blocked = new HubPlanStore(Path.Combine(_directory, "preset-1.json"));
            Assert.That(blocked.SavePreset(plan, 1), Is.False);
            Assert.That(plan.Revision, Is.EqualTo(2));
        }

        static DemoSwitchPresetStep Step(string demoId, params (string key, string value)[] options) =>
            new DemoSwitchPresetStep(demoId, options.ToDictionary(o => o.key, o => o.value), true);

        [Test]
        public void PresetCheckReportsTheFirstOffendingDemo()
        {
            Assert.That(HubPlan.Check(new[] { Step("volley", ("scene", "receive"), ("balls", "20")), Step("trex-encounter") }, _catalog, out var demoId),
                Is.EqualTo(DemoSwitchPresetCheck.Ok));
            Assert.That(demoId, Is.Null);
            Assert.That(HubPlan.Check(new DemoSwitchPresetStep[0], _catalog, out _), Is.EqualTo(DemoSwitchPresetCheck.Ok), "An empty list clears the preset.");
            Assert.That(HubPlan.Check(new[] { Step("volley"), Step("handdemo"), Step("boxing", ("round", "30")) }, _catalog, out demoId),
                Is.EqualTo(DemoSwitchPresetCheck.NotInstalled));
            Assert.That(demoId, Is.EqualTo("handdemo"));
            Assert.That(HubPlan.Check(new[] { Step("volley"), Step("boxing", ("round", "30")), Step("handdemo") }, _catalog, out demoId),
                Is.EqualTo(DemoSwitchPresetCheck.UnknownOption));
            Assert.That(demoId, Is.EqualTo("boxing"));
            Assert.That(HubPlan.Check(new[] { Step("trex-encounter", ("tutorial", "on")) }, _catalog, out demoId), Is.EqualTo(DemoSwitchPresetCheck.UnknownOption));
            Assert.That(demoId, Is.EqualTo("trex-encounter"));
        }

        [Test]
        public void PresetStepsKeepTheStoredOptionsIncludingUninstalledDemos()
        {
            var plan = HubPlan.FromPreset("A", new[] { Step("handdemo", ("tutorial", "on")), new DemoSwitchPresetStep("volley", null, false) });
            plan.Steps[1].Options["Bad Key"] = "x";
            var steps = plan.PresetSteps();
            Assert.That(plan.Name, Is.EqualTo("A"));
            Assert.That(steps.Select(s => s.DemoId), Is.EqualTo(new[] { "handdemo", "volley" }));
            Assert.That(steps[0].Options["tutorial"], Is.EqualTo("on"));
            Assert.That(steps[1].Retry, Is.False);
            Assert.That(steps[1].Options, Is.Empty, "Options the wire format cannot carry are left out.");
        }

        [Test]
        public void TicketSatisfiesTheContractAndSkipsUninstalledDemos()
        {
            var plan = new HubPlan();
            plan.Add(_volley);
            plan.CycleOption(0, _volley, "points");
            plan.Steps.Add(new HubPlanStep("not-installed", null, true));
            plan.Add(_boxing);
            plan.ToggleRetry(2);
            plan.Add(_trex);
            var finish = new DemoSessionComponent(HubIdentity.PackageName, HubIdentity.ActivityName);
            var ticket = plan.BuildTicket(_catalog, finish, DemoSessionTicket.NewSessionId(), false, DemoHandStyle.Skin, false);
            Assert.That(DemoSessionTicket.TryParse(ticket.ToJson(), out var parsed, out var error), Is.True, error);
            Assert.That(parsed.Index, Is.Zero);
            Assert.That(parsed.HapticsUi, Is.False);
            Assert.That(parsed.Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "volley", "boxing", "trex-encounter" }));
            Assert.That(parsed.Steps[0].Options, Is.EquivalentTo(new Dictionary<string, string> { ["scene"] = "block", ["points"] = "3" }), "Only active options.");
            Assert.That(parsed.Steps[0].Title, Is.EqualTo("Volley ブロック 3点先取"), "Application name, then option values.");
            Assert.That(parsed.Steps[2].Title, Is.EqualTo("T-Rex Encounter"));
            Assert.That(parsed.HandStyle, Is.EqualTo(DemoHandStyle.Skin));
            Assert.That(parsed.Steps[0].PackageName, Is.EqualTo("jp.hapbeat.volley"));
            Assert.That(parsed.Steps[1].Retry, Is.False);
            Assert.That(parsed.Steps[2].ActivityName, Is.EqualTo("com.epicgames.unreal.GameActivity"));
            Assert.That(parsed.Steps[2].Options, Is.Empty);
            Assert.That(parsed.Finish.PackageName, Is.EqualTo("jp.hapbeat.demohub"));
            Assert.That(parsed.SessionId, Does.Match("^[0-9a-f]{16}$"));

            var empty = new HubPlan();
            empty.Steps.Add(new HubPlanStep("not-installed", null, true));
            Assert.That(empty.BuildTicket(_catalog, finish, DemoSessionTicket.NewSessionId(), false, DemoHandStyle.Ghost, false), Is.Null);
        }

        [Test]
        public void NamesComeFromTheApplicationLabel()
        {
            var volley = _catalog.Single(e => e.Descriptor.DemoId == "volley");
            Assert.That(volley.DisplayName, Is.EqualTo("Volley"), "PackageManager label, no Hapbeat prefix.");
            var unlabeled = new DemoSessionCatalogEntry(_volley, "jp.hapbeat.volley", "com.unity3d.player.UnityPlayerGameActivity", "  ");
            Assert.That(unlabeled.DisplayName, Is.EqualTo("バレーボール"), "Without a label: the descriptor title.");
            var longName = new DemoSessionCatalogEntry(_volley, "jp.hapbeat.volley", "com.unity3d.player.UnityPlayerGameActivity", new string('x', 45));
            var plan = new HubPlan();
            plan.Add(_volley);
            Assert.That(HubPlan.Title(plan.Steps[0], longName).Length, Is.EqualTo(HubPlan.TitleMaxLength), "Step titles keep the 40-character limit.");
            Assert.That(plan.Summary(_catalog), Is.EqualTo("Volley ブロック 7点先取"));
        }

        [Test]
        public void HubTextGlyphsExistInTheBundledFont()
        {
            var font = Resources.Load<Font>("HapbeatDemoSession/NotoSansCJKjp-Regular");
            Assert.That(font, Is.Not.Null);
            foreach (var value in HubText.All)
                foreach (var character in value.Where(c => !char.IsWhiteSpace(c) && c != '{' && c != '}'))
                    Assert.That(font.HasCharacter(character), Is.True, value + ": " + character);
        }

        [Test]
        public void DeviceAddressLineShowsUnspecifiedAxes()
        {
            Assert.That(DemoHubController.DeviceAddressLine(new DemoDeviceAddress(1, 2)), Is.EqualTo("この端末: プレイヤー 1 / グループ 2"));
            Assert.That(DemoHubController.DeviceAddressLine(new DemoDeviceAddress(-1, 99)), Is.EqualTo("この端末: プレイヤー 指定なし / グループ 99"));
            Assert.That(DemoHubController.DeviceAddressLine(null), Is.EqualTo("この端末: プレイヤー 指定なし / グループ 指定なし"));
        }
    }
}
