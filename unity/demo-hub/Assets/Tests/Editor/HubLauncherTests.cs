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
    public sealed class HubLauncherTests
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
        public void TopShowsOnlyVisibleInstalledItems()
        {
            var presets = new[] { new HubPlan(), new HubPlan(), new HubPlan() };
            presets[0].Add(_volley);
            presets[1].Steps.Add(new HubPlanStep("not-installed", null, true));
            presets[2].Add(_trex);
            var settings = new HubSettings();
            var empty = HubTopItems.Decide(settings, presets, _catalog);
            Assert.That(empty.IsEmpty, Is.True, "Nothing is shown until the operator chooses.");

            settings.VisiblePresets.UnionWith(new[] { 1, 2 });
            settings.VisibleDemos.UnionWith(new[] { "trex-encounter", "volley", "not-installed" });
            var items = HubTopItems.Decide(settings, presets, _catalog);
            Assert.That(items.Presets, Is.EqualTo(new[] { 1 }), "Preset 2 has no installed step; preset 3 is hidden.");
            Assert.That(items.Demos.Select(e => e.Descriptor.DemoId), Is.EqualTo(new[] { "volley", "trex-encounter" }), "Installed only, catalog order.");
            Assert.That(items.IsEmpty, Is.False);

            settings.VisiblePresets.Clear();
            settings.VisibleDemos.Clear();
            settings.VisibleDemos.Add("not-installed");
            Assert.That(HubTopItems.Decide(settings, presets, _catalog).IsEmpty, Is.True);
        }

        [Test]
        public void TileStartsAOneStepSessionWithDescriptorDefaults()
        {
            var finish = new DemoSessionComponent(HubIdentity.PackageName, HubIdentity.ActivityName);
            var ticket = HubPlan.Single(_volley).BuildTicket(_catalog, finish, DemoSessionTicket.NewSessionId(), true);
            Assert.That(DemoSessionTicket.TryParse(ticket.ToJson(), out var parsed, out var error), Is.True, error);
            Assert.That(parsed.Index, Is.Zero);
            Assert.That(parsed.HapticsUi, Is.True);
            Assert.That(parsed.Steps.Count, Is.EqualTo(1));
            Assert.That(parsed.Steps[0].DemoId, Is.EqualTo("volley"));
            Assert.That(parsed.Steps[0].Options, Is.EquivalentTo(new Dictionary<string, string> { ["scene"] = "block", ["points"] = "7" }), "Descriptor defaults, active only.");
            Assert.That(parsed.Steps[0].Retry, Is.True);
            Assert.That(parsed.Finish.PackageName, Is.EqualTo(HubIdentity.PackageName));
            Assert.That(parsed.Finish.ActivityName, Is.EqualTo(HubIdentity.ActivityName));
        }

        [Test]
        public void FinishedTileReturnsToTopAndFinishedPlanShowsFinishScreen()
        {
            var finish = new DemoSessionComponent(HubIdentity.PackageName, HubIdentity.ActivityName);
            var single = HubPlan.Single(_boxing).BuildTicket(_catalog, finish, DemoSessionTicket.NewSessionId(), false);
            var plan = new HubPlan();
            plan.Add(_volley);
            plan.Add(_boxing);
            var multi = plan.BuildTicket(_catalog, finish, DemoSessionTicket.NewSessionId(), false);
            Assert.That(DemoHubController.InitialScreen(null), Is.EqualTo(DemoHubController.HubScreen.Top));
            Assert.That(DemoHubController.InitialScreen(single.WithIndex(1, false)), Is.EqualTo(DemoHubController.HubScreen.Top));
            Assert.That(DemoHubController.InitialScreen(multi.WithIndex(2, false)), Is.EqualTo(DemoHubController.HubScreen.Finished));
            Assert.That(DemoHubController.InitialScreen(multi), Is.EqualTo(DemoHubController.HubScreen.Top), "Not finished.");
        }

        [Test]
        public void LongPressFiresOnceAfterTwoSeconds()
        {
            var hold = new HubHoldGesture();
            Assert.That(hold.Update(false, 0f), Is.False);
            Assert.That(hold.Update(true, 10f), Is.False);
            Assert.That(hold.Update(true, 11.9f), Is.False);
            Assert.That(hold.Progress, Is.EqualTo(0.95f).Within(1e-4));
            Assert.That(hold.Update(true, 12f), Is.True);
            Assert.That(hold.Update(true, 13f), Is.False, "Once per press.");
            Assert.That(hold.Update(false, 13.1f), Is.False);
            Assert.That(hold.Progress, Is.Zero);
            Assert.That(hold.Update(true, 14f), Is.False);
            Assert.That(hold.Update(false, 15f), Is.False, "Released early.");
            Assert.That(hold.Update(true, 16f), Is.False);
            Assert.That(hold.Update(true, 18f), Is.True);
            Assert.That(DemoHubController.HoldLabel(0f), Is.EqualTo(HubText.Manage));
            Assert.That(DemoHubController.HoldLabel(0.4f), Is.EqualTo("■■□□□"));
            Assert.That(DemoHubController.HoldLabel(1f), Is.EqualTo("■■■■■"));
        }

        [Test]
        public void SettingsRoundTripAndLegacyPlanMovesToPresetOne()
        {
            var legacy = new HubPlan();
            legacy.Add(_trex);
            var store = new HubPlanStore(_directory);
            Assert.That(store.Save(legacy, HubPlanStore.LegacyLastSlot), Is.True);
            var first = store.LoadSettings();
            Assert.That(first.VisiblePresets, Is.EquivalentTo(new[] { 1 }), "The migrated plan is shown.");
            Assert.That(store.LoadPreset(1).Steps.Single().DemoId, Is.EqualTo("trex-encounter"));
            Assert.That(store.Exists(HubPlanStore.LegacyLastSlot), Is.False);
            Assert.That(first.HapticsUi || first.StaffWaiting || first.VisibleDemos.Count > 0, Is.False, "Defaults are off.");

            first.VisiblePresets.Add(3);
            first.VisibleDemos.Add("volley");
            first.HapticsUi = true;
            first.StaffWaiting = true;
            Assert.That(store.SaveSettings(first), Is.True);
            // A later legacy file is not migrated again.
            store.Save(new HubPlan(), HubPlanStore.LegacyLastSlot);
            var loaded = store.LoadSettings();
            Assert.That(loaded.VisiblePresets, Is.EquivalentTo(new[] { 1, 3 }));
            Assert.That(loaded.VisibleDemos, Is.EquivalentTo(new[] { "volley" }));
            Assert.That(loaded.HapticsUi && loaded.StaffWaiting, Is.True);
            Assert.That(store.LoadPreset(1).Steps.Count, Is.EqualTo(1));

            var fresh = new HubPlanStore(Path.Combine(_directory, "fresh"));
            Assert.That(fresh.LoadSettings().VisiblePresets, Is.Empty, "No legacy plan: nothing shown.");
            Assert.That(HubSettings.TryFromJson("{\"version\":2}", out _), Is.False);
        }

        [Test]
        public void PanelFollowsOnlyAfterThirtyFiveDegrees()
        {
            var head = new Vector3(0, 1.6f, 0);
            var target = HubPanelFollow.Target(head, Vector3.forward, Vector3.up);
            Assert.That(Vector3.Distance(target.position, new Vector3(0, 1.6f - HubPanelFollow.Drop, HubPanelFollow.Distance)), Is.LessThan(1e-4f));
            var pitched = HubPanelFollow.Target(head, Quaternion.Euler(-40, 0, 0) * Vector3.forward, Quaternion.Euler(-40, 0, 0) * Vector3.up);
            Assert.That(Vector3.Distance(pitched.position, target.position), Is.LessThan(1e-4f), "Yaw only.");
            Assert.That(HubPanelFollow.OutOfPlace(head, Quaternion.Euler(0, 34, 0) * Vector3.forward, Vector3.up, target.position), Is.False);
            Assert.That(HubPanelFollow.OutOfPlace(head, Quaternion.Euler(0, -36, 0) * Vector3.forward, Vector3.up, target.position), Is.True);
            Assert.That(HubPanelFollow.OutOfPlace(head + Vector3.back * 0.4f, Vector3.forward, Vector3.up, target.position), Is.True, "Stepped back.");

            var follow = new HubPanelFollow();
            var pose = target;
            pose = follow.Step(pose, head, Quaternion.Euler(0, 20, 0) * Vector3.forward, Vector3.up, 0.1f);
            Assert.That(follow.Moving, Is.False, "Within 35°: stays put.");
            Assert.That(pose.position, Is.EqualTo(target.position));
            var turned = Quaternion.Euler(0, 90, 0) * Vector3.forward;
            for (var frame = 0; frame < 5; frame++) pose = follow.Step(pose, head, turned, Vector3.up, 0.09f);
            Assert.That(follow.Moving, Is.True, "Still easing at 0.45 s.");
            pose = follow.Step(pose, head, turned, Vector3.up, 0.09f);
            Assert.That(follow.Moving, Is.False);
            Assert.That(Vector3.Distance(pose.position, HubPanelFollow.Target(head, turned, Vector3.up).position), Is.LessThan(1e-4f));
        }

        [Test]
        public void ControllerScreensBuildAndStartFailsSafelyInTheEditor()
        {
            var go = new GameObject("hub test");
            try
            {
                var controller = go.AddComponent<DemoHubController>();
                controller.Initialize(_catalog, new HubPlanStore(_directory));
                controller.Show(DemoHubController.HubScreen.Top);
                Assert.That(Texts(controller), Has.Member(HubText.NothingToShow));
                Assert.That(controller.ManageButton, Is.Not.Null);

                controller.Show(DemoHubController.HubScreen.Manage);
                controller.EditedPlan.Add(_volley);
                controller.Settings.VisiblePresets.Add(1);
                controller.Settings.VisibleDemos.Add("boxing");
                controller.SelectTab(DemoHubController.TilesTab);
                Assert.That(Texts(controller), Has.Member(HubText.TilesHeading));
                controller.SelectTab(0);
                controller.Show(DemoHubController.HubScreen.Finished);
                controller.Show(DemoHubController.HubScreen.Top);
                Assert.That(Texts(controller).Any(t => t.StartsWith(HubText.Preset + " 1")), Is.True);
                Assert.That(Texts(controller).Any(t => t.StartsWith("ボクシング")), Is.True);

                LogAssert.Expect(LogType.Warning, new Regex("Application launch is supported only"));
                LogAssert.Expect(LogType.Error, new Regex(@"\[Demo Session\] Launch failed"));
                controller.StartDemo(_catalog.Single(e => e.Descriptor.DemoId == "boxing"));
                Assert.That(controller.Screen, Is.EqualTo(DemoHubController.HubScreen.Top));
                Assert.That(Texts(controller).Any(t => t.StartsWith(HubText.LaunchFailed)), Is.True);

                controller.Settings.StaffWaiting = true;
                controller.Show(DemoHubController.HubScreen.Top);
                Assert.That(Texts(controller), Has.Member(HubText.StaffWaitingNote));
                Assert.That(Texts(controller).Any(t => t.StartsWith(HubText.Preset)), Is.False, "Staff waiting hides the launcher.");
                Assert.That(controller.ManageButton, Is.Not.Null);
            }
            finally { Object.DestroyImmediate(go); }
        }

        static List<string> Texts(DemoHubController controller) =>
            controller.Panel.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text).ToList();
    }
}
