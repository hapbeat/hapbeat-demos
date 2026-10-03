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
            var ticket = HubPlan.Single(_volley).BuildTicket(_catalog, finish, DemoSessionTicket.NewSessionId(), true, DemoHandStyle.Ghost);
            Assert.That(DemoSessionTicket.TryParse(ticket.ToJson(), out var parsed, out var error), Is.True, error);
            Assert.That(parsed.Index, Is.Zero);
            Assert.That(parsed.HapticsUi, Is.True);
            Assert.That(parsed.Steps.Count, Is.EqualTo(1));
            Assert.That(parsed.Steps[0].DemoId, Is.EqualTo("volley"));
            Assert.That(parsed.Steps[0].Options, Is.EquivalentTo(new Dictionary<string, string> { ["scene"] = "block", ["points"] = "7" }), "Descriptor defaults, active only.");
            Assert.That(parsed.Steps[0].Retry, Is.True);
            Assert.That(parsed.Finish.PackageName, Is.EqualTo(HubIdentity.PackageName));
            Assert.That(parsed.Finish.ActivityName, Is.EqualTo(HubIdentity.ActivityName));
            Assert.That(parsed.Steps[0].Title, Is.EqualTo("Volley ブロック 7点先取"));
            Assert.That(parsed.HandStyle, Is.EqualTo(DemoHandStyle.Ghost));
        }

        [Test]
        public void FinishedTileReturnsToTopAndFinishedPlanShowsFinishScreen()
        {
            var finish = new DemoSessionComponent(HubIdentity.PackageName, HubIdentity.ActivityName);
            var single = HubPlan.Single(_boxing).BuildTicket(_catalog, finish, DemoSessionTicket.NewSessionId(), false, DemoHandStyle.Ghost);
            var plan = new HubPlan();
            plan.Add(_volley);
            plan.Add(_boxing);
            var multi = plan.BuildTicket(_catalog, finish, DemoSessionTicket.NewSessionId(), false, DemoHandStyle.Ghost);
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
            Assert.That(first.HandStyle, Is.EqualTo(DemoHandStyle.Ghost), "Ghost hands by default.");

            first.VisiblePresets.Add(3);
            first.VisibleDemos.Add("volley");
            first.HapticsUi = true;
            first.StaffWaiting = true;
            first.HandStyle = DemoHandStyle.Skin;
            Assert.That(store.SaveSettings(first), Is.True);
            // A later legacy file is not migrated again.
            store.Save(new HubPlan(), HubPlanStore.LegacyLastSlot);
            var loaded = store.LoadSettings();
            Assert.That(loaded.VisiblePresets, Is.EquivalentTo(new[] { 1, 3 }));
            Assert.That(loaded.VisibleDemos, Is.EquivalentTo(new[] { "volley" }));
            Assert.That(loaded.HapticsUi && loaded.StaffWaiting, Is.True);
            Assert.That(loaded.HandStyle, Is.EqualTo(DemoHandStyle.Skin));
            Assert.That(HubSettings.TryFromJson("{\"version\":1,\"hand_style\":\"glove\"}", out var unknown), Is.True);
            Assert.That(unknown.HandStyle, Is.EqualTo(DemoHandStyle.Ghost), "Unknown look: the default.");
            Assert.That(store.LoadPreset(1).Steps.Count, Is.EqualTo(1));

            var fresh = new HubPlanStore(Path.Combine(_directory, "fresh"));
            Assert.That(fresh.LoadSettings().VisiblePresets, Is.Empty, "No legacy plan: nothing shown.");
            Assert.That(HubSettings.TryFromJson("{\"version\":2}", out _), Is.False);
        }

        [Test]
        public void PanelIsPlacedOnceAndMovesOnlyOnRecenter()
        {
            var go = new GameObject("hub test");
            var head = new GameObject("head").transform;
            try
            {
                var controller = go.AddComponent<DemoHubController>();
                controller.Initialize(_catalog, new HubPlanStore(_directory));
                controller.Show(DemoHubController.HubScreen.Top);
                var panel = controller.Panel.transform;

                head.SetPositionAndRotation(new Vector3(0, 1.6f, 0), Quaternion.Euler(-30, 0, 0));
                controller.TickPlacement(head, false);
                Assert.That(controller.Placed, Is.False, "Before tracking: provisional.");
                head.SetPositionAndRotation(new Vector3(0.2f, 1.5f, 0), Quaternion.Euler(-30, 40, 0));
                controller.TickPlacement(head, true);
                Assert.That(controller.Placed, Is.True);
                var expected = HubPanelPlacement.Target(head.position, head.forward, head.up);
                Assert.That(Vector3.Distance(panel.position, expected.position), Is.LessThan(1e-4f));
                Assert.That(panel.position.y, Is.EqualTo(1.5f - HubPanelPlacement.Drop).Within(1e-4f), "Pitch ignored: below eye height.");
                Assert.That(Vector3.ProjectOnPlane(panel.position - head.position, Vector3.up).magnitude, Is.EqualTo(HubPanelPlacement.Distance).Within(1e-4f));

                // Turning, walking and switching screens never move it.
                head.SetPositionAndRotation(new Vector3(-0.5f, 1.2f, 0.3f), Quaternion.Euler(10, -120, 0));
                controller.TickPlacement(head, true);
                controller.Show(DemoHubController.HubScreen.Manage);
                controller.Show(DemoHubController.HubScreen.Finished);
                controller.Show(DemoHubController.HubScreen.Top);
                Assert.That(Vector3.Distance(panel.position, expected.position), Is.LessThan(1e-4f));
                Assert.That(Quaternion.Angle(panel.rotation, expected.rotation), Is.LessThan(0.01f));

                // 手前に移動 places it in front of the head again.
                Assert.That(Buttons(controller).Count(b => b.Label == HubText.Recenter), Is.EqualTo(1));
                controller.PlaceInFrontOf(head);
                Assert.That(Vector3.Distance(panel.position, HubPanelPlacement.Target(head.position, head.forward, head.up).position), Is.LessThan(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(head.gameObject);
            }
        }

        [Test]
        public void ManageScreenShowsAll32StepsWithoutPages()
        {
            var go = new GameObject("hub test");
            try
            {
                var controller = go.AddComponent<DemoHubController>();
                controller.Initialize(_catalog, new HubPlanStore(_directory));
                controller.Show(DemoHubController.HubScreen.Manage);
                var size = controller.Panel.Size;
                for (var i = 0; i < HubPlan.MaxSteps; i++) controller.EditedPlan.Add(i % 2 == 0 ? _volley : _trex);
                controller.SelectStep(0);
                Assert.That(controller.Panel.Size, Is.EqualTo(size), "The panel keeps its size as the plan grows.");

                var buttons = Buttons(controller);
                for (var i = 1; i <= HubPlan.MaxSteps; i++)
                    Assert.That(buttons.Count(b => b.Label.StartsWith(i + ". ")), Is.EqualTo(1), "Row " + i);
                Assert.That(buttons.Count(b => b.Label == HubText.Up), Is.EqualTo(HubPlan.MaxSteps));
                Assert.That(buttons.Count(b => b.Label == HubText.Down), Is.EqualTo(HubPlan.MaxSteps));
                Assert.That(buttons.Count(b => b.Label == HubText.Remove), Is.EqualTo(HubPlan.MaxSteps));
                Assert.That(buttons.Any(b => b.Label == "▲" || b.Label == "▼"), Is.False, "No page buttons.");
                Assert.That(buttons.Count(b => b.Label.StartsWith("モード：")), Is.EqualTo(1), "Options of the selected step.");
                AssertInsideWithoutOverlap(controller);

                controller.SelectTab(DemoHubController.TilesTab);
                Assert.That(controller.Panel.Size, Is.EqualTo(size), "Same size on every tab.");
                AssertInsideWithoutOverlap(controller);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ManyDemosFitWithoutPages()
        {
            var catalog = new List<DemoSessionCatalogEntry>();
            for (var i = 0; i < 20; i++)
            {
                var json = "{\"version\":1,\"demo_id\":\"demo-" + i + "\",\"title\":{\"ja\":\"デモ " + i + "\"},\"minutes\":2,\"supports\":{\"haptics_toggle\":false},\"options\":[]}";
                Assert.That(DemoSessionDescriptor.TryParse(json, out var descriptor, out var error), Is.True, error);
                catalog.Add(new DemoSessionCatalogEntry(descriptor, "jp.hapbeat.demo" + i, "com.unity3d.player.UnityPlayerGameActivity", "Demo " + i));
            }
            var go = new GameObject("hub test");
            try
            {
                var controller = go.AddComponent<DemoHubController>();
                controller.Initialize(catalog, new HubPlanStore(_directory));
                foreach (var entry in catalog) controller.Settings.VisibleDemos.Add(entry.Descriptor.DemoId);
                controller.Settings.VisiblePresets.Add(1);
                controller.Preset(1).Add(catalog[0].Descriptor);
                controller.Show(DemoHubController.HubScreen.Top);
                Assert.That(Buttons(controller).Count(b => b.Label.StartsWith("Demo ")), Is.EqualTo(catalog.Count), "Every tile at once.");
                AssertInsideWithoutOverlap(controller);

                controller.Show(DemoHubController.HubScreen.Manage);
                Assert.That(Buttons(controller).Count(b => b.Label.StartsWith("Demo ")), Is.EqualTo(catalog.Count), "Whole catalog.");
                AssertInsideWithoutOverlap(controller);
                controller.SelectTab(DemoHubController.TilesTab);
                Assert.That(Buttons(controller).Count(b => b.Label == HubText.TileOn), Is.EqualTo(catalog.Count));
                AssertInsideWithoutOverlap(controller);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void MovingARowKeepsItSelectedAndFlashesIt()
        {
            var go = new GameObject("hub test");
            try
            {
                var controller = go.AddComponent<DemoHubController>();
                controller.Initialize(_catalog, new HubPlanStore(_directory));
                controller.Show(DemoHubController.HubScreen.Manage);
                controller.EditedPlan.Add(_volley);
                controller.EditedPlan.Add(_boxing);
                controller.EditedPlan.Add(_trex);
                controller.SelectStep(2);
                controller.Move(2, 1);
                Assert.That(controller.EditedPlan.Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "volley", "trex-encounter", "boxing" }));
                Assert.That(controller.SelectedStep, Is.EqualTo(1), "The selection follows the moved step.");
                var moved = Buttons(controller).Single(b => b.Label.StartsWith("2. "));
                Assert.That(moved.Label, Is.EqualTo("2. T-Rex Encounter"));
                Assert.That(moved.IsFlashing && moved.Highlighted, Is.True);
                Assert.That(Buttons(controller).Single(b => b.Label.StartsWith("3. ")).IsFlashing, Is.False);
                controller.RemoveStep(0);
                Assert.That(controller.SelectedStep, Is.Zero);
                Assert.That(controller.EditedPlan.Steps.Count, Is.EqualTo(2));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void HandStyleChoiceIsSavedAndSentInTickets()
        {
            var go = new GameObject("hub test");
            try
            {
                var store = new HubPlanStore(_directory);
                var controller = go.AddComponent<DemoHubController>();
                controller.Initialize(_catalog, store);
                controller.Show(DemoHubController.HubScreen.Manage);
                var toggle = Buttons(controller).Single(b => b.Label == HubText.HandStyleGhost);
                var width = toggle.Rect.sizeDelta.x;
                Press(toggle);
                Assert.That(controller.Settings.HandStyle, Is.EqualTo(DemoHandStyle.Skin));
                var skin = Buttons(controller).Single(b => b.Label == HubText.HandStyleSkin);
                Assert.That(skin.Rect.sizeDelta.x, Is.EqualTo(width), "Fixed width: no layout shift.");
                Assert.That(store.LoadSettings().HandStyle, Is.EqualTo(DemoHandStyle.Skin), "Saved at once.");

                var finish = new DemoSessionComponent(HubIdentity.PackageName, HubIdentity.ActivityName);
                var ticket = HubPlan.Single(_trex).BuildTicket(_catalog, finish, DemoSessionTicket.NewSessionId(), false, controller.Settings.HandStyle);
                Assert.That(ticket.ToJson(), Does.Contain("\"hand_style\":\"skin\""));
            }
            finally { Object.DestroyImmediate(go); }
        }

        static IReadOnlyList<DemoSessionButton> Buttons(DemoHubController controller) => controller.Panel.Buttons;

        /// <summary>The panel's own press path (internal to the package), as a poke or trigger would call it.</summary>
        static void Press(DemoSessionButton button) =>
            typeof(DemoSessionButton).GetMethod("Press", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(button, null);

        /// <summary>Every button lies inside the panel, none overlap, and each is at least 40 mm tall (pokable).</summary>
        static void AssertInsideWithoutOverlap(DemoHubController controller)
        {
            var half = controller.Panel.Size * 0.5f;
            var rects = Buttons(controller).Select(b => (b.Label, rect: new Rect(b.Rect.anchoredPosition - b.Rect.sizeDelta * 0.5f, b.Rect.sizeDelta))).ToList();
            foreach (var (label, rect) in rects)
            {
                Assert.That(rect.xMin >= -half.x && rect.xMax <= half.x && rect.yMin >= -half.y && rect.yMax <= half.y, Is.True, label + " inside the panel " + rect);
                Assert.That(rect.height, Is.GreaterThanOrEqualTo(DemoHubController.RowHeight - 1e-3f), label + " pokable height");
            }
            for (var a = 0; a < rects.Count; a++)
                for (var b = a + 1; b < rects.Count; b++)
                    Assert.That(rects[a].rect.Overlaps(rects[b].rect), Is.False, rects[a].Label + " overlaps " + rects[b].Label);
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
                Assert.That(Texts(controller).Any(t => t.StartsWith("Boxing")), Is.True, "Tiles use the application name.");

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
