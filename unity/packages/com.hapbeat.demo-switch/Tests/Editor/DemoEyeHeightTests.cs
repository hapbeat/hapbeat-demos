using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hapbeat.DemoSwitch.Tests
{
    public sealed class DemoEyeHeightTests
    {
        sealed class HeightHost : IDemoSessionHost, IDemoSessionRecenter, IDemoSessionHeight
        {
            public readonly List<string> Calls = new List<string>();
            public readonly List<float> Targets = new List<float>();
            public void ApplyOptions(IReadOnlyDictionary<string, string> options) { }
            public void Restart() { }
            public void SetHapticsEnabled(bool enabled) { }
            public void SetGameplayPaused(bool paused) { }
            public void RecenterToStart() => Calls.Add("recenter");
            public void ApplyEyeHeight(float target)
            {
                Calls.Add("height");
                Targets.Add(target);
            }
        }

        [SetUp]
        public void SetUp() => DemoSession.ResetForTests(null, "volley");

        [TearDown]
        public void TearDown() => DemoSession.ResetForTests();

        [Test]
        public void CorrectionIsTheChangeThatPutsTheHeadAtTheTarget()
        {
            Assert.That(DemoEyeHeight.DefaultTargetEyeHeight, Is.EqualTo(1.65f));
            Assert.That(DemoEyeHeight.Correction(1.40f, 1.65f), Is.EqualTo(0.25f).Within(1e-5f), "A short participant (or a low floor) is lifted.");
            Assert.That(DemoEyeHeight.Correction(1.90f, 1.65f), Is.EqualTo(-0.25f).Within(1e-5f), "A tall participant (or a high floor) is lowered.");
            Assert.That(DemoEyeHeight.Correction(1.65f, 1.65f), Is.EqualTo(0f).Within(1e-5f));
            Assert.That(DemoEyeHeight.Correction(0.05f, 1.65f), Is.EqualTo(1.60f).Within(1e-5f), "Device tracking: the head near the origin.");
        }

        [TestCase(0f, 1.82f, TestName = "ApplyToRigInFloorTracking")]
        [TestCase(1.1176f, 0.02f, TestName = "ApplyToRigInDeviceTracking")]
        public void ApplyToRigPutsTheHeadAtTheTargetAboveTheFloor(float offsetY, float trackedY)
        {
            var rig = new GameObject("test rig");
            var offset = new GameObject("test camera offset");
            var head = new GameObject("test head");
            try
            {
                offset.transform.SetParent(rig.transform, false);
                head.transform.SetParent(offset.transform, false);
                rig.transform.SetPositionAndRotation(new Vector3(1f, 0.3f, -2f), Quaternion.Euler(0, 40, 0));
                offset.transform.localPosition = new Vector3(0f, offsetY, 0f);
                head.transform.localPosition = new Vector3(0.2f, trackedY, 0.1f);
                var horizontal = new Vector2(head.transform.position.x, head.transform.position.z);

                DemoEyeHeight.ApplyToRig(rig.transform, head.transform, 0.3f, 1.65f);
                Assert.That(head.transform.position.y, Is.EqualTo(0.3f + 1.65f).Within(1e-4f));
                Assert.That(new Vector2(head.transform.position.x, head.transform.position.z), Is.EqualTo(horizontal), "Height only.");
                Assert.That(offset.transform.localPosition.y, Is.EqualTo(offsetY), "The XR Origin's offset (set again on a tracking origin change) is not used.");

                var rigHeight = rig.transform.position.y;
                DemoEyeHeight.ApplyToRig(rig.transform, head.transform, 0.3f, 1.65f);
                Assert.That(rig.transform.position.y, Is.EqualTo(rigHeight).Within(1e-5f), "Applied again, nothing accumulates.");
            }
            finally
            {
                Object.DestroyImmediate(head);
                Object.DestroyImmediate(offset);
                Object.DestroyImmediate(rig);
            }
        }

        [Test]
        public void ResetViewAppliesTheHostsEyeHeightAfterItsAlignment()
        {
            var host = new HeightHost();
            DemoSession.RegisterHost(host);
            DemoRecenter.ResetView();
            Assert.That(host.Calls, Is.EqualTo(new[] { "recenter", "height" }));
            Assert.That(host.Targets, Is.EqualTo(new[] { DemoEyeHeight.DefaultTargetEyeHeight }));

            DemoEyeHeight.Configure(1.5f);
            DemoRecenter.ResetView();
            Assert.That(host.Targets[1], Is.EqualTo(1.5f), "The settings' Eye Height.");

            DemoSession.ResetForTests(null, DemoSwitchSettings.HubDemoId);
            host = new HeightHost();
            DemoSession.RegisterHost(host);
            DemoRecenter.ResetView();
            Assert.That(host.Calls, Is.Empty, "The Hub is not corrected.");
        }

        [Test]
        public void ResetViewWithoutAHeightHostMovesTheRig()
        {
            // The open scene's own camera and XR Origin are hidden for the test (FindRig must find the test rig).
            var hidden = new List<GameObject>();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (!root.activeSelf) continue;
                root.SetActive(false);
                hidden.Add(root);
            }
            var rig = new GameObject("test rig");
            var head = new GameObject("test head", typeof(Camera)) { tag = "MainCamera" };
            try
            {
                head.transform.SetParent(rig.transform, false);
                rig.transform.position = new Vector3(0f, 0.2f, 0f);
                head.transform.localPosition = new Vector3(0.3f, 1.85f, 0.4f);
                Assert.That(Camera.main, Is.EqualTo(head.GetComponent<Camera>()));

                DemoRecenter.ResetView();
                Assert.That(head.transform.position.y, Is.EqualTo(0.2f + DemoEyeHeight.DefaultTargetEyeHeight).Within(1e-4f),
                    "The head is the target above the rig's height when the scene loaded.");
                Assert.That(head.transform.position.x, Is.EqualTo(0f).Within(1e-4f), "Then also at the start position.");

                head.transform.localPosition += Vector3.down * 0.3f;
                DemoRecenter.ResetView();
                Assert.That(head.transform.position.y, Is.EqualTo(0.2f + DemoEyeHeight.DefaultTargetEyeHeight).Within(1e-4f),
                    "Measured from the start floor, not from the moved rig.");
            }
            finally
            {
                Object.DestroyImmediate(head);
                Object.DestroyImmediate(rig);
                foreach (var root in hidden) root.SetActive(true);
            }
        }
    }
}
