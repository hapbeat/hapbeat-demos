using NUnit.Framework;
using UnityEngine;

namespace Hapbeat.DemoSwitch.Tests
{
    public sealed class XrStartAlignmentTests
    {
        [Test]
        public void ComputeYaw_UsesHorizontalCameraToAnchorAngle()
        {
            var yaw = XrStartAlignment.ComputeYaw(
                new Vector3(0f, 0.5f, 1f),
                new Vector3(1f, -0.25f, 0f),
                Vector3.up);

            Assert.That(yaw, Is.EqualTo(90f).Within(0.001f));
        }

        [Test]
        public void ComputeHorizontalDelta_AlignsXzWithoutChangingY()
        {
            var delta = XrStartAlignment.ComputeHorizontalDelta(
                new Vector3(2f, 1.7f, -4f),
                new Vector3(-1f, 9f, 3f));

            Assert.That(delta, Is.EqualTo(new Vector3(-3f, 0f, 7f)));
        }
    }
}
