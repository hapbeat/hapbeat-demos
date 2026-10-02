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
            Assert.That(HubPlan.Title(step, _volley), Is.EqualTo("バレーボール レシーブ 10球"));
            Assert.That(plan.CycleOption(0, _volley, "scene"), Is.True);
            Assert.That(HubPlan.Title(step, _volley), Is.EqualTo("バレーボール ブロック 3点先取"), "Hidden choices are remembered.");
            plan.ToggleRetry(0);
            Assert.That(step.Retry, Is.False);
            Assert.That(HubPlan.Truncate(new string('あ', 50), 40).Length, Is.EqualTo(40));
        }

        [Test]
        public void PresetsAndLastPlanRoundTrip()
        {
            var store = new HubPlanStore(_directory);
            Assert.That(store.TryLoad(HubPlanStore.LastSlot, out _), Is.False);
            var plan = new HubPlan { HapticsUi = true };
            plan.Add(_volley);
            plan.CycleOption(0, _volley, "scene");
            plan.ToggleRetry(0);
            plan.Add(_trex);
            for (var number = 1; number <= HubPlanStore.PresetCount; number++)
                Assert.That(store.Save(plan, HubPlanStore.PresetSlot(number)), Is.True);
            Assert.That(store.Save(new HubPlan(), HubPlanStore.LastSlot), Is.True);
            Assert.That(store.TryLoad(HubPlanStore.PresetSlot(2), out var loaded), Is.True);
            Assert.That(loaded.HapticsUi, Is.True);
            Assert.That(loaded.Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "volley", "trex-encounter" }));
            Assert.That(loaded.Steps[0].Options["scene"], Is.EqualTo("receive"));
            Assert.That(loaded.Steps[0].Retry, Is.False);
            Assert.That(store.TryLoad(HubPlanStore.LastSlot, out var last) && last.Steps.Count == 0, Is.True);
            File.WriteAllText(Path.Combine(_directory, "preset-3.json"), "{broken");
            Assert.That(store.TryLoad(HubPlanStore.PresetSlot(3), out _), Is.False);
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
            var ticket = plan.BuildTicket(_catalog, finish, DemoSessionTicket.NewSessionId());
            Assert.That(DemoSessionTicket.TryParse(ticket.ToJson(), out var parsed, out var error), Is.True, error);
            Assert.That(parsed.Index, Is.Zero);
            Assert.That(parsed.HapticsUi, Is.False);
            Assert.That(parsed.Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "volley", "boxing", "trex-encounter" }));
            Assert.That(parsed.Steps[0].Options, Is.EquivalentTo(new Dictionary<string, string> { ["scene"] = "block", ["points"] = "3" }), "Only active options.");
            Assert.That(parsed.Steps[0].Title, Is.EqualTo("バレーボール ブロック 3点先取"));
            Assert.That(parsed.Steps[0].PackageName, Is.EqualTo("jp.hapbeat.volley"));
            Assert.That(parsed.Steps[1].Retry, Is.False);
            Assert.That(parsed.Steps[2].ActivityName, Is.EqualTo("com.epicgames.unreal.GameActivity"));
            Assert.That(parsed.Steps[2].Options, Is.Empty);
            Assert.That(parsed.Finish.PackageName, Is.EqualTo("jp.hapbeat.demohub"));
            Assert.That(parsed.SessionId, Does.Match("^[0-9a-f]{16}$"));
            Assert.That(plan.Minutes(_catalog), Is.EqualTo(8).Within(1e-6));

            var empty = new HubPlan();
            empty.Steps.Add(new HubPlanStep("not-installed", null, true));
            Assert.That(empty.BuildTicket(_catalog, finish, DemoSessionTicket.NewSessionId()), Is.Null);
        }

        [Test]
        public void ControllerScreensBuildAndStartFailsSafelyInTheEditor()
        {
            var go = new GameObject("hub test");
            try
            {
                var controller = go.AddComponent<DemoHubController>();
                controller.Initialize(_catalog, new HubPlanStore(_directory));
                controller.Plan.Add(_volley);
                controller.Show(DemoHubController.HubScreen.Edit);
                controller.Show(DemoHubController.HubScreen.Finished);
                controller.Show(DemoHubController.HubScreen.Top);
                LogAssert.Expect(LogType.Warning, new Regex("Application launch is supported only"));
                LogAssert.Expect(LogType.Error, new Regex(@"\[Demo Session\] Launch failed"));
                controller.StartSession();
                Assert.That(controller.Screen, Is.EqualTo(DemoHubController.HubScreen.Top));
                var texts = controller.Panel.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text).ToList();
                Assert.That(texts.Any(t => t.StartsWith(HubText.LaunchFailed)), Is.True);
                controller.SavePreset(1);
                controller.Plan.Remove(0);
                controller.LoadPreset(1);
                Assert.That(controller.Plan.Steps.Single().DemoId, Is.EqualTo("volley"));
            }
            finally { Object.DestroyImmediate(go); }
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
    }
}
