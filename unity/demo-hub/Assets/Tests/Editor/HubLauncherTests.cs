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
            var ticket = HubPlan.Single(_volley).BuildTicket(_catalog, finish, DemoSessionTicket.NewSessionId(), true, DemoHandStyle.Ghost, false);
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
            Assert.That(parsed.RecenterUi, Is.False);
            var shown = HubPlan.Single(_volley).BuildTicket(_catalog, finish, DemoSessionTicket.NewSessionId(), false, DemoHandStyle.Ghost, true);
            Assert.That(DemoSessionTicket.TryParse(shown.ToJson(), out parsed, out error), Is.True, error);
            Assert.That(parsed.RecenterUi, Is.True);
        }

        [Test]
        public void FinishedTileReturnsToTopAndFinishedPlanShowsFinishScreen()
        {
            var finish = new DemoSessionComponent(HubIdentity.PackageName, HubIdentity.ActivityName);
            var single = HubPlan.Single(_boxing).BuildTicket(_catalog, finish, DemoSessionTicket.NewSessionId(), false, DemoHandStyle.Ghost, false);
            var plan = new HubPlan();
            plan.Add(_volley);
            plan.Add(_boxing);
            var multi = plan.BuildTicket(_catalog, finish, DemoSessionTicket.NewSessionId(), false, DemoHandStyle.Ghost, false);
            Assert.That(DemoHubController.InitialScreen(null), Is.EqualTo(DemoHubController.HubScreen.Top));
            Assert.That(DemoHubController.InitialScreen(single.WithIndex(1, false)), Is.EqualTo(DemoHubController.HubScreen.Top));
            Assert.That(DemoHubController.InitialScreen(multi.WithIndex(2, false)), Is.EqualTo(DemoHubController.HubScreen.Finished));
            Assert.That(DemoHubController.InitialScreen(multi), Is.EqualTo(DemoHubController.HubScreen.Top), "Not finished.");
        }

        [Test]
        public void LongPressFiresOnceAfterOneSecond()
        {
            var hold = new HubHoldGesture();
            Assert.That(hold.Update(false, 0f), Is.False);
            Assert.That(hold.Update(true, 10f), Is.False);
            Assert.That(hold.Update(true, 10.95f), Is.False);
            Assert.That(hold.Progress, Is.EqualTo(0.95f).Within(1e-4));
            Assert.That(hold.Update(true, 11f), Is.True);
            Assert.That(hold.Update(true, 12f), Is.False, "Once per press.");
            Assert.That(hold.Update(false, 12.1f), Is.False);
            Assert.That(hold.Progress, Is.Zero);
            Assert.That(hold.Update(true, 14f), Is.False);
            Assert.That(hold.Update(false, 14.5f), Is.False, "Released early.");
            Assert.That(hold.Update(true, 16f), Is.False);
            Assert.That(hold.Update(true, 17f), Is.True);
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
            Assert.That(first.HapticsUi || first.StaffWaiting || first.RecenterUi || first.VisibleDemos.Count > 0, Is.False, "Defaults are off.");
            Assert.That(first.HandStyle, Is.EqualTo(DemoHandStyle.Ghost), "Ghost hands by default.");

            first.VisiblePresets.Add(3);
            first.VisibleDemos.Add("volley");
            first.HapticsUi = true;
            first.StaffWaiting = true;
            first.RecenterUi = true;
            first.HandStyle = DemoHandStyle.Skin;
            Assert.That(store.SaveSettings(first), Is.True);
            // A later legacy file is not migrated again.
            store.Save(new HubPlan(), HubPlanStore.LegacyLastSlot);
            var loaded = store.LoadSettings();
            Assert.That(loaded.VisiblePresets, Is.EquivalentTo(new[] { 1, 3 }));
            Assert.That(loaded.VisibleDemos, Is.EquivalentTo(new[] { "volley" }));
            Assert.That(loaded.HapticsUi && loaded.StaffWaiting && loaded.RecenterUi, Is.True);
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
            var head = new GameObject("head", typeof(Camera)) { tag = "MainCamera" }.transform;
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

                // 視線をリセット (the package's button, DemoRecenter.Recentered) places the panel in front again.
                typeof(DemoHubController).GetMethod("Recenter", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(controller, null);
                var main = Camera.main.transform; // The test head, or an open scene's own main camera.
                Assert.That(Vector3.Distance(panel.position, HubPanelPlacement.Target(main.position, main.forward, main.up).position), Is.LessThan(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(head.gameObject);
            }
        }

        /// <summary>Text boxes truncate vertically: each must hold its explicit lines at its font size (best-fit labels at their smallest size).</summary>
        static void AssertTextsFitTheirLines(DemoHubController controller, string what)
        {
            var panel = controller.Panel;
            var texts = panel.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            Assert.That(texts, Is.Not.Empty, what);
            foreach (var text in texts)
            {
                var size = text.resizeTextForBestFit ? text.resizeTextMinSize : text.fontSize;
                var lines = Mathf.Max(1, text.text.Split('\n').Length);
                Assert.That(text.rectTransform.rect.height, Is.GreaterThanOrEqualTo(lines * panel.LineHeight(size)),
                    what + ": \"" + text.text + "\" (size " + size + ", " + lines + " line(s))");
            }
        }

        [Test]
        public void EveryHubTextBoxIsAtLeastItsLinesTall()
        {
            var go = new GameObject("hub test");
            try
            {
                var controller = go.AddComponent<DemoHubController>();
                controller.Initialize(_catalog, new HubPlanStore(_directory));
                controller.Show(DemoHubController.HubScreen.Top);
                AssertTextsFitTheirLines(controller, "top, nothing to show");

                controller.Preset(1).Add(_volley);
                controller.Settings.VisiblePresets.Add(1);
                controller.Settings.VisibleDemos.UnionWith(new[] { "volley", "boxing", "trex-encounter" });
                controller.Show(DemoHubController.HubScreen.Top);
                AssertTextsFitTheirLines(controller, "top");

                controller.Settings.StaffWaiting = true;
                controller.Show(DemoHubController.HubScreen.Top);
                AssertTextsFitTheirLines(controller, "staff waiting");
                controller.Settings.StaffWaiting = false;

                controller.Show(DemoHubController.HubScreen.Manage);
                controller.SelectTab(1);
                AssertTextsFitTheirLines(controller, "manage, empty plan");
                controller.SelectTab(0);
                AssertTextsFitTheirLines(controller, "manage, step hint");
                controller.SelectStep(0);
                AssertTextsFitTheirLines(controller, "manage, step editor");
                controller.SelectTab(DemoHubController.TilesTab);
                AssertTextsFitTheirLines(controller, "manage, tiles");

                controller.Show(DemoHubController.HubScreen.Finished);
                AssertTextsFitTheirLines(controller, "finished");
            }
            finally { Object.DestroyImmediate(go); }
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
                for (var i = 0; i < HubPlan.MaxSteps; i++) controller.EditedPlan.Add(i % 2 == 0 ? _volley : _trex);
                controller.SelectStep(0);
                Assert.That(controller.GridRows, Is.EqualTo(DemoHubController.RowsPerColumn), "Two columns of 16.");
                var size = controller.Panel.Size;

                var buttons = Buttons(controller);
                for (var i = 1; i <= HubPlan.MaxSteps; i++)
                    Assert.That(buttons.Count(b => b.Label.StartsWith(i + ". ")), Is.EqualTo(1), "Row " + i);
                Assert.That(buttons.Count(b => b.Label == HubText.Grip), Is.EqualTo(HubPlan.MaxSteps));
                Assert.That(buttons.Count(b => b.Label == HubText.Up), Is.EqualTo(HubPlan.MaxSteps));
                Assert.That(buttons.Count(b => b.Label == HubText.Down), Is.EqualTo(HubPlan.MaxSteps));
                Assert.That(buttons.Count(b => b.Label == HubText.Remove), Is.EqualTo(HubPlan.MaxSteps));
                Assert.That(buttons.Count(b => b.Label.StartsWith("モード：")), Is.EqualTo(1), "Options of the selected step.");
                AssertInsideWithoutOverlap(controller);

                controller.SelectTab(DemoHubController.TilesTab);
                Assert.That(controller.Panel.Size.x, Is.EqualTo(size.x), "Same width on every tab.");
                Assert.That(controller.GridRows, Is.EqualTo(_catalog.Count), "Tiles: one row per installed demo.");
                AssertInsideWithoutOverlap(controller);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ManageHeightFollowsTheLongerColumnAndKeepsTheTopInPlace()
        {
            var go = new GameObject("hub test");
            try
            {
                var controller = go.AddComponent<DemoHubController>();
                controller.Initialize(_catalog, new HubPlanStore(_directory));
                controller.Show(DemoHubController.HubScreen.Manage);
                Assert.That(controller.GridRows, Is.EqualTo(3), "Catalog 3 rows (and the step hint).");
                var catalogPosition = Buttons(controller).First(b => b.Label == "Volley").Rect.anchoredPosition;
                Vector3 TopLeft() => controller.Panel.transform.TransformPoint(new Vector3(-controller.Panel.Size.x * 0.5f, controller.Panel.Size.y * 0.5f, 0));
                var topLeft = TopLeft();
                var height = controller.Panel.Size.y;
                for (var i = 0; i < 10; i++) Press(Buttons(controller).First(b => b.Label == "Volley"));
                Assert.That(controller.EditedPlan.Steps.Count, Is.EqualTo(10));
                Assert.That(controller.GridRows, Is.EqualTo(10), "Plan 10 rows.");
                Assert.That(controller.Panel.Size.y - height, Is.EqualTo(7 * 43f).Within(1e-3f), "Seven more rows of 40 mm + 3 mm gap.");
                Assert.That(Vector3.Distance(TopLeft(), topLeft), Is.LessThan(1e-5f), "Tabs and catalog stay where they were.");
                Assert.That(Buttons(controller).First(b => b.Label == "Volley").Rect.anchoredPosition - new Vector2(-controller.Panel.Size.x * 0.5f, controller.Panel.Size.y * 0.5f),
                    Is.EqualTo(catalogPosition - new Vector2(-controller.Panel.Size.x * 0.5f, height * 0.5f)));
                AssertInsideWithoutOverlap(controller);

                controller.SelectTab(1);
                Assert.That(controller.GridRows, Is.EqualTo(3), "Each preset tab fits its own plan.");
                controller.SelectTab(0);
                controller.SelectStep(9);
                Assert.That(controller.GridRows, Is.EqualTo(10));

                // Nothing between the grid and the footer, and 完了 right after 手の見た目.
                var buttons = Buttons(controller);
                var lastRow = buttons.Where(b => b.Label.StartsWith("10. ")).Single();
                var hand = buttons.Single(b => b.Label == HubText.HandStyleGhost);
                var done = buttons.Single(b => b.Label == HubText.Done);
                Assert.That(Bottom(lastRow) - Top(hand), Is.LessThan(10f), "No empty rows above the footer.");
                Assert.That(Left(done) - Right(hand), Is.EqualTo(8f).Within(1e-3f));
                Assert.That(controller.Panel.Size.x * 0.5f - Right(done), Is.LessThan(40f), "No wide blank on the right of the footer.");
            }
            finally { Object.DestroyImmediate(go); }
        }

        static float Top(DemoSessionButton b) => b.Rect.anchoredPosition.y + b.Rect.sizeDelta.y * 0.5f;
        static float Bottom(DemoSessionButton b) => b.Rect.anchoredPosition.y - b.Rect.sizeDelta.y * 0.5f;
        static float Left(DemoSessionButton b) => b.Rect.anchoredPosition.x - b.Rect.sizeDelta.x * 0.5f;
        static float Right(DemoSessionButton b) => b.Rect.anchoredPosition.x + b.Rect.sizeDelta.x * 0.5f;

        static void SetHold(DemoSessionButton button, bool held, bool captured, Vector2 point)
        {
            var type = typeof(DemoSessionButton);
            type.GetProperty(nameof(DemoSessionButton.Held)).SetValue(button, held);
            type.GetProperty(nameof(DemoSessionButton.Captured)).SetValue(button, captured);
            type.GetProperty(nameof(DemoSessionButton.CapturePoint)).SetValue(button, point);
        }

        [Test]
        public void LongPressedGripDragsTheRowToTheInsertionLine()
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
                controller.SelectStep(0);
                var grip = controller.Grips[0];
                var start = grip.Rect.anchoredPosition;
                SetHold(grip, true, true, start);
                controller.UpdateDrag(10f);
                controller.UpdateDrag(10f + DemoHubController.DragHoldSeconds - 0.05f);
                Assert.That(controller.DragFrom, Is.EqualTo(-1), "Not before the long press.");
                controller.UpdateDrag(10f + DemoHubController.DragHoldSeconds + 0.01f);
                Assert.That(controller.DragFrom, Is.Zero);

                // Finger slides down past the last row (off the grip: no longer Held, still Captured).
                var below = new Vector2(start.x + 30f, controller.Grips[2].Rect.anchoredPosition.y - 25f);
                SetHold(grip, false, true, below);
                controller.UpdateDrag(11f);
                Assert.That(controller.DropSlot, Is.EqualTo(3), "After the last step.");
                var line = controller.Panel.GetComponentsInChildren<UnityEngine.UI.Image>().Single(i => i.gameObject.name == "Rect");
                Assert.That(line.gameObject.activeSelf, Is.True, "Insertion line shown.");
                Assert.That(line.rectTransform.anchoredPosition.y, Is.LessThan(Bottom(controller.Grips[2])).And.GreaterThan(Bottom(controller.Grips[2]) - 5f));

                SetHold(grip, false, false, below);
                controller.UpdateDrag(11.1f);
                Assert.That(controller.EditedPlan.Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "boxing", "trex-encounter", "volley" }), "Inserted, not swapped.");
                Assert.That(controller.SelectedStep, Is.EqualTo(2), "The selection follows.");
                Assert.That(Buttons(controller).Single(b => b.Label.StartsWith("3. ")).IsFlashing, Is.True);

                // Upward, between rows 1 and 2.
                grip = controller.Grips[2];
                SetHold(grip, true, true, grip.Rect.anchoredPosition);
                controller.UpdateDrag(20f);
                controller.UpdateDrag(21f);
                var between = new Vector2(grip.Rect.anchoredPosition.x, (Bottom(controller.Grips[0]) + Top(controller.Grips[1])) * 0.5f);
                SetHold(grip, false, true, between);
                controller.UpdateDrag(21.1f);
                Assert.That(controller.DropSlot, Is.EqualTo(1));
                SetHold(grip, false, false, between);
                controller.UpdateDrag(21.2f);
                Assert.That(controller.EditedPlan.Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "boxing", "volley", "trex-encounter" }));
                Assert.That(controller.SelectedStep, Is.EqualTo(1));

                // Released where it was: nothing changes.
                grip = controller.Grips[0];
                SetHold(grip, true, true, grip.Rect.anchoredPosition);
                controller.UpdateDrag(30f);
                controller.UpdateDrag(31f);
                SetHold(grip, false, false, grip.Rect.anchoredPosition);
                controller.UpdateDrag(31.1f);
                Assert.That(controller.EditedPlan.Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "boxing", "volley", "trex-encounter" }));
                Assert.That(controller.DragFrom, Is.EqualTo(-1));
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
                var ticket = HubPlan.Single(_trex).BuildTicket(_catalog, finish, DemoSessionTicket.NewSessionId(), false, controller.Settings.HandStyle, false);
                Assert.That(ticket.ToJson(), Does.Contain("\"hand_style\":\"skin\""));
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void RecenterButtonChoiceShowsTheHubsButtonAndIsSentInTickets()
        {
            var go = new GameObject("hub test");
            try
            {
                DemoSession.SetRecenterUiVisible(false);
                var store = new HubPlanStore(_directory);
                var controller = go.AddComponent<DemoHubController>();
                controller.Initialize(_catalog, store);
                Assert.That(DemoSession.RecenterUiVisible, Is.False, "Hidden by default.");
                controller.Show(DemoHubController.HubScreen.Manage);
                var toggle = Buttons(controller).Single(b => b.Label == HubText.RecenterUiOff);
                var width = toggle.Rect.sizeDelta.x;
                Press(toggle);
                Assert.That(controller.Settings.RecenterUi, Is.True);
                Assert.That(DemoSession.RecenterUiVisible, Is.True, "The Hub's own button follows the choice.");
                var shown = Buttons(controller).Single(b => b.Label == HubText.RecenterUiOn);
                Assert.That(shown.Rect.sizeDelta.x, Is.EqualTo(width), "Fixed width: no layout shift.");
                Assert.That(shown.Highlighted, Is.True);
                Assert.That(store.LoadSettings().RecenterUi, Is.True, "Saved at once.");
                AssertInsideWithoutOverlap(controller);

                DemoSession.SetRecenterUiVisible(false);
                var again = new GameObject("hub test 2");
                try
                {
                    again.AddComponent<DemoHubController>().Initialize(_catalog, store);
                    Assert.That(DemoSession.RecenterUiVisible, Is.True, "Applied from the saved settings at start.");
                }
                finally { Object.DestroyImmediate(again); }
            }
            finally
            {
                Object.DestroyImmediate(go);
                DemoSession.SetRecenterUiVisible(false);
            }
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

        [TestCase("{\"version\":1,\"preset\":1}", 1, null)]
        [TestCase("{\"version\":1,\"preset\":3}", 3, null)]
        [TestCase("{\"version\":1,\"demo_id\":\"handdemo\",\"options\":{\"tutorial\":\"on\"}}", 0, "handdemo")]
        [TestCase("{\"version\":1,\"demo_id\":\"volley\"}", 0, "volley")]
        public void ExternalStartAcceptsAPresetOrOneDemo(string json, int preset, string demoId)
        {
            Assert.That(HubStartRequest.TryParse(json, out var request, out var error), Is.True, error);
            Assert.That(request.Preset, Is.EqualTo(preset));
            Assert.That(request.DemoId, Is.EqualTo(demoId));
        }

        [TestCase("")]
        [TestCase("not json")]
        [TestCase("[]")]
        [TestCase("{\"version\":2,\"preset\":1}")]
        [TestCase("{\"version\":1}")]
        [TestCase("{\"version\":1,\"preset\":0}")]
        [TestCase("{\"version\":1,\"preset\":4}")]
        [TestCase("{\"version\":1,\"preset\":\"1\"}")]
        [TestCase("{\"version\":1,\"preset\":1,\"demo_id\":\"volley\"}")]
        [TestCase("{\"version\":1,\"preset\":1,\"options\":{}}")]
        [TestCase("{\"version\":1,\"demo_id\":\"Volley\"}")]
        [TestCase("{\"version\":1,\"demo_id\":\"volley\",\"package\":\"evil.app\"}")]
        [TestCase("{\"version\":1,\"demo_id\":\"volley\",\"options\":{\"points\":3}}")]
        [TestCase("{\"version\":1,\"demo_id\":\"volley\",\"demo_id\":\"boxing\"}")]
        [TestCase("{\"version\":1,\"preset\":1} {}")]
        [TestCase("{\"version\":1,\"steps\":[]}")]
        [TestCase("{\"version\":1,\"steps\":null}")]
        [TestCase("{\"version\":1,\"steps\":{\"demo_id\":\"volley\"}}")]
        [TestCase("{\"version\":1,\"steps\":[\"volley\"]}")]
        [TestCase("{\"version\":1,\"steps\":[{\"options\":{}}]}")]
        [TestCase("{\"version\":1,\"steps\":[{\"demo_id\":\"Volley\"}]}")]
        [TestCase("{\"version\":1,\"steps\":[{\"demo_id\":\"volley\",\"title\":\"x\"}]}")]
        [TestCase("{\"version\":1,\"steps\":[{\"demo_id\":\"volley\",\"retry\":\"false\"}]}")]
        [TestCase("{\"version\":1,\"steps\":[{\"demo_id\":\"volley\",\"options\":{\"points\":3}}]}")]
        [TestCase("{\"version\":1,\"steps\":[{\"demo_id\":\"volley\",\"options\":[]}]}")]
        [TestCase("{\"version\":1,\"steps\":[{\"demo_id\":\"volley\",\"demo_id\":\"boxing\"}]}")]
        [TestCase("{\"version\":1,\"steps\":[{\"demo_id\":\"volley\"}],\"options\":{}}")]
        [TestCase("{\"version\":1,\"steps\":[{\"demo_id\":\"volley\"}],\"finish\":\"jp.hapbeat.demohub\"}")]
        [TestCase("{\"version\":1,\"steps\":[{\"demo_id\":\"volley\"}],\"preset\":1}")]
        [TestCase("{\"version\":1,\"steps\":[{\"demo_id\":\"volley\"}],\"demo_id\":\"volley\"}")]
        [TestCase("{\"version\":1,\"steps\":[{\"demo_id\":\"volley\"}],\"preset\":1,\"demo_id\":\"volley\"}")]
        public void ExternalStartRejectsInvalidRequests(string json)
        {
            Assert.That(HubStartRequest.TryParse(json, out var request, out var error), Is.False);
            Assert.That(request, Is.Null);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void ExternalStartRejectsOver4096Bytes()
        {
            var json = "{\"version\":1,\"demo_id\":\"volley\",\"options\":{\"scene\":\"" + new string('x', HubStartRequest.MaxBytes) + "\"}}";
            Assert.That(HubStartRequest.TryParse(json, out _, out _), Is.False);
        }

        [Test]
        public void ExternalStartAcceptsAnExternalPlan()
        {
            Assert.That(HubStartRequest.TryParse("{\"version\":1,\"steps\":[{\"demo_id\":\"volley\",\"options\":{\"scene\":\"receive\"}},{\"demo_id\":\"trex-encounter\",\"retry\":false}]}",
                out var request, out var error), Is.True, error);
            Assert.That(request.Preset, Is.Zero);
            Assert.That(request.DemoId, Is.Null);
            Assert.That(request.Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "volley", "trex-encounter" }));
            Assert.That(request.Steps[0].Options, Is.EquivalentTo(new Dictionary<string, string> { ["scene"] = "receive" }));
            Assert.That(request.Steps[0].Retry, Is.True, "retry defaults to true.");
            Assert.That(request.Steps[1].Options, Is.Empty);
            Assert.That(request.Steps[1].Retry, Is.False);
        }

        [Test]
        public void ExternalPlanAllowsAtMost32Steps()
        {
            string Plan(int count) => "{\"version\":1,\"steps\":[" + string.Join(",", Enumerable.Repeat("{\"demo_id\":\"volley\"}", count)) + "]}";
            Assert.That(HubStartRequest.TryParse(Plan(HubPlan.MaxSteps), out var request, out var error), Is.True, error);
            Assert.That(request.Steps.Count, Is.EqualTo(32));
            Assert.That(HubStartRequest.TryParse(Plan(HubPlan.MaxSteps + 1), out request, out error), Is.False);
            Assert.That(request, Is.Null);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void ExternalPlanErrorsNameTheStep()
        {
            Assert.That(HubStartRequest.TryParse("{\"version\":1,\"steps\":[{\"demo_id\":\"volley\"},{\"demo_id\":\"boxing\",\"retry\":1}]}", out _, out var error), Is.False);
            Assert.That(error, Does.StartWith(string.Format(HubText.StartStep, 2)));
            Assert.That(HubStartRequest.TryParse("{\"version\":1,\"steps\":[{\"demo_id\":\"volley\",\"package\":\"evil.app\"}]}", out _, out error), Is.False);
            Assert.That(error, Does.StartWith(string.Format(HubText.StartStep, 1)));
        }

        [Test]
        public void ExternalPlanBuildsTheTicketAndLeavesPresetsAlone()
        {
            var finish = new DemoSessionComponent(HubIdentity.PackageName, HubIdentity.ActivityName);
            var settings = new HubSettings { HapticsUi = true, RecenterUi = false, HandStyle = DemoHandStyle.Skin };
            var store = new HubPlanStore(_directory);
            var presets = new[] { new HubPlan(), new HubPlan(), new HubPlan() };
            presets[0].Add(_boxing);
            store.Save(presets[0], HubPlanStore.PresetSlot(1));
            var saved = File.ReadAllText(Path.Combine(_directory, HubPlanStore.PresetSlot(1) + ".json"));

            Assert.That(HubStartRequest.TryParse("{\"version\":1,\"steps\":[{\"demo_id\":\"volley\",\"options\":{\"scene\":\"receive\",\"speed\":\"fast\"}},"
                + "{\"demo_id\":\"trex-encounter\",\"retry\":false},{\"demo_id\":\"volley\"}]}", out var request, out var error), Is.True, error);
            LogAssert.Expect(LogType.Warning, new Regex("speed=fast"));
            var ticket = request.BuildTicket(presets, _catalog, finish, DemoSessionTicket.NewSessionId(), settings, out error);
            Assert.That(ticket, Is.Not.Null, error);
            Assert.That(DemoSessionTicket.TryParse(ticket.ToJson(), out var parsed, out error), Is.True, error);
            Assert.That(parsed.Index, Is.Zero);
            Assert.That(parsed.Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "volley", "trex-encounter", "volley" }));
            Assert.That(parsed.Steps[0].Options, Is.EquivalentTo(new Dictionary<string, string> { ["scene"] = "receive", ["balls"] = "10" }),
                "Given value kept, unknown option dropped, defaults for the rest, active options only.");
            Assert.That(parsed.Steps[2].Options, Is.EquivalentTo(new Dictionary<string, string> { ["scene"] = "block", ["points"] = "7" }));
            Assert.That(parsed.Steps.Select(s => s.Retry), Is.EqualTo(new[] { true, false, true }));
            Assert.That(parsed.Finish.PackageName, Is.EqualTo(HubIdentity.PackageName));
            Assert.That(parsed.HapticsUi, Is.True);
            Assert.That(parsed.RecenterUi, Is.False);
            Assert.That(parsed.HandStyle, Is.EqualTo(DemoHandStyle.Skin));
            Assert.That(presets[0].Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "boxing" }), "The external plan is not a preset.");
            Assert.That(presets[1].Steps, Is.Empty);
            Assert.That(File.ReadAllText(Path.Combine(_directory, HubPlanStore.PresetSlot(1) + ".json")), Is.EqualTo(saved));
            Assert.That(Directory.GetFiles(_directory).Length, Is.EqualTo(1), "Nothing else is saved.");
        }

        [Test]
        public void ExternalPlanWithAnUninstalledStepStartsNothing()
        {
            var finish = new DemoSessionComponent(HubIdentity.PackageName, HubIdentity.ActivityName);
            var presets = new[] { new HubPlan(), new HubPlan(), new HubPlan() };
            Assert.That(HubStartRequest.TryParse("{\"version\":1,\"steps\":[{\"demo_id\":\"volley\"},{\"demo_id\":\"handdemo\"},{\"demo_id\":\"boxing\"}]}",
                out var request, out var error), Is.True, error);
            Assert.That(request.BuildTicket(presets, _catalog, finish, DemoSessionTicket.NewSessionId(), new HubSettings(), out error), Is.Null);
            Assert.That(error, Is.EqualTo(string.Format(HubText.StartStep, 2) + HubText.StartNotInstalled + "handdemo"));
        }

        [Test]
        public void ExternalStartBuildsTheTicketWithManageSettings()
        {
            var finish = new DemoSessionComponent(HubIdentity.PackageName, HubIdentity.ActivityName);
            var settings = new HubSettings { HapticsUi = true, RecenterUi = true, HandStyle = DemoHandStyle.Skin };
            var presets = new[] { new HubPlan(), new HubPlan(), new HubPlan() };
            presets[0].Add(_volley);
            presets[0].Add(_trex);
            presets[1].Steps.Add(new HubPlanStep("not-installed", null, true));

            Assert.That(HubStartRequest.TryParse("{\"version\":1,\"preset\":1}", out var request, out var error), Is.True, error);
            var ticket = request.BuildTicket(presets, _catalog, finish, DemoSessionTicket.NewSessionId(), settings, out error);
            Assert.That(ticket, Is.Not.Null, error);
            Assert.That(ticket.Steps.Select(s => s.DemoId), Is.EqualTo(new[] { "volley", "trex-encounter" }));
            Assert.That(ticket.HapticsUi && ticket.RecenterUi, Is.True);
            Assert.That(ticket.HandStyle, Is.EqualTo(DemoHandStyle.Skin));

            Assert.That(HubStartRequest.TryParse("{\"version\":1,\"preset\":2}", out request, out _), Is.True);
            Assert.That(request.BuildTicket(presets, _catalog, finish, DemoSessionTicket.NewSessionId(), settings, out error), Is.Null);
            Assert.That(error, Is.EqualTo(string.Format(HubText.StartPresetEmpty, 2)));

            Assert.That(HubStartRequest.TryParse("{\"version\":1,\"demo_id\":\"handdemo\"}", out request, out _), Is.True);
            Assert.That(request.BuildTicket(presets, _catalog, finish, DemoSessionTicket.NewSessionId(), settings, out error), Is.Null);
            Assert.That(error, Is.EqualTo(HubText.StartNotInstalled + "handdemo"));

            Assert.That(HubStartRequest.TryParse("{\"version\":1,\"demo_id\":\"volley\",\"options\":{\"scene\":\"receive\",\"balls\":\"99\",\"speed\":\"fast\"}}", out request, out _), Is.True);
            LogAssert.Expect(LogType.Warning, new Regex("balls=99"));
            LogAssert.Expect(LogType.Warning, new Regex("speed=fast"));
            ticket = request.BuildTicket(presets, _catalog, finish, DemoSessionTicket.NewSessionId(), settings, out error);
            Assert.That(DemoSessionTicket.TryParse(ticket.ToJson(), out var parsed, out error), Is.True, error);
            Assert.That(parsed.Steps.Count, Is.EqualTo(1));
            Assert.That(parsed.Steps[0].Options, Is.EquivalentTo(new Dictionary<string, string> { ["scene"] = "receive", ["balls"] = "10" }),
                "Given value kept, unknown value and option replaced by the defaults, inactive options left out.");
            Assert.That(parsed.Steps[0].Retry, Is.True);
            Assert.That(parsed.Finish.PackageName, Is.EqualTo(HubIdentity.PackageName));
            Assert.That(parsed.HapticsUi, Is.True);
            Assert.That(parsed.RecenterUi, Is.True);
            Assert.That(parsed.HandStyle, Is.EqualTo(DemoHandStyle.Skin));
        }

        [Test]
        public void RejectedOrFailedExternalStartOpensTheTopWithTheReason()
        {
            var go = new GameObject("hub test");
            try
            {
                var controller = go.AddComponent<DemoHubController>();
                controller.Initialize(_catalog, new HubPlanStore(_directory));
                LogAssert.Expect(LogType.Warning, new Regex("External start rejected"));
                controller.StartExternal("{\"version\":1,\"demo_id\":\"handdemo\"}");
                Assert.That(controller.Screen, Is.EqualTo(DemoHubController.HubScreen.Top));
                Assert.That(Texts(controller), Has.Member(HubText.StartRejected + HubText.StartNotInstalled + "handdemo"));

                LogAssert.Expect(LogType.Warning, new Regex("External start rejected"));
                controller.StartExternal("{\"version\":1,\"steps\":[{\"demo_id\":\"volley\"},{\"demo_id\":\"handdemo\"}]}");
                Assert.That(controller.Screen, Is.EqualTo(DemoHubController.HubScreen.Top));
                Assert.That(Texts(controller), Has.Member(HubText.StartRejected + string.Format(HubText.StartStep, 2) + HubText.StartNotInstalled + "handdemo"));

                controller.Show(DemoHubController.HubScreen.Manage);
                LogAssert.Expect(LogType.Warning, new Regex("External start rejected"));
                controller.StartExternal("{\"version\":1,\"preset\":9}");
                Assert.That(controller.Screen, Is.EqualTo(DemoHubController.HubScreen.Top));
                Assert.That(Texts(controller).Any(t => t.StartsWith(HubText.StartRejected)), Is.True);

                // Accepted, but the Editor cannot start applications: the top screen shows the launch error.
                Object.DestroyImmediate(go);
                go = new GameObject("hub test");
                controller = go.AddComponent<DemoHubController>();
                controller.Initialize(_catalog, new HubPlanStore(_directory));
                LogAssert.Expect(LogType.Warning, new Regex("Application launch is supported only"));
                LogAssert.Expect(LogType.Error, new Regex(@"\[Demo Session\] Launch failed"));
                controller.StartExternal("{\"version\":1,\"demo_id\":\"boxing\"}");
                Assert.That(controller.Panel, Is.Not.Null);
                Assert.That(controller.Screen, Is.EqualTo(DemoHubController.HubScreen.Top));
                Assert.That(Texts(controller).Any(t => t.StartsWith(HubText.LaunchFailed)), Is.True);
            }
            finally { Object.DestroyImmediate(go); }
        }

        static List<string> Texts(DemoHubController controller) =>
            controller.Panel.GetComponentsInChildren<UnityEngine.UI.Text>().Select(t => t.text).ToList();
    }
}
