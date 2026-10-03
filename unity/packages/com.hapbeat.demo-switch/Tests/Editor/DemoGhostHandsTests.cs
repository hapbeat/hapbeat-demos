using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace Hapbeat.DemoSwitch.Tests
{
    public sealed class DemoGhostHandsTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        /// <summary>A flat open right hand: fingers along +z from a wrist at the origin, 2 cm apart along x.</summary>
        internal static (Vector3[] positions, bool[] valid) OpenHand()
        {
            var positions = new Vector3[DemoGhostHandShape.JointCount];
            var valid = new bool[DemoGhostHandShape.JointCount];
            positions[XRHandJointID.Wrist.ToIndex()] = Vector3.zero;
            positions[XRHandJointID.Palm.ToIndex()] = new Vector3(0f, 0f, 0.05f);
            Chain(positions, new Vector3(-0.035f, 0f, 0.02f), Vector3.forward + Vector3.left * 0.5f,
                XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip);
            Chain(positions, new Vector3(-0.02f, 0f, 0.03f), Vector3.forward,
                XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip);
            Chain(positions, new Vector3(0f, 0f, 0.03f), Vector3.forward,
                XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip);
            Chain(positions, new Vector3(0.02f, 0f, 0.03f), Vector3.forward,
                XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip);
            Chain(positions, new Vector3(0.04f, 0f, 0.03f), Vector3.forward,
                XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip);
            for (var index = 0; index < valid.Length; index++) valid[index] = true;
            return (positions, valid);
        }

        private static void Chain(Vector3[] positions, Vector3 start, Vector3 direction, params XRHandJointID[] joints)
        {
            var step = direction.normalized * 0.03f;
            for (var index = 0; index < joints.Length; index++) positions[joints[index].ToIndex()] = start + step * index;
        }

        [Test]
        public void Shape_FullHandProducesEveryBone()
        {
            var (positions, valid) = OpenHand();
            var segments = new List<DemoGhostHandSegment>();

            DemoGhostHandShape.Build(positions, valid, segments);

            Assert.That(segments.Count, Is.EqualTo(DemoGhostHandShape.BoneCount));
            foreach (var segment in segments)
            {
                Assert.That(segment.StartRadius, Is.GreaterThan(0f));
                Assert.That(segment.EndRadius, Is.GreaterThan(0f));
            }
        }

        [Test]
        public void Shape_IndexFingertipSurfaceEndsAtTheJointUsedForPoke()
        {
            var (positions, valid) = OpenHand();
            var tip = positions[XRHandJointID.IndexTip.ToIndex()];
            var distal = positions[XRHandJointID.IndexDistal.ToIndex()];
            var segments = new List<DemoGhostHandSegment>();

            DemoGhostHandShape.Build(positions, valid, segments);

            var found = false;
            foreach (var segment in segments)
            {
                if ((segment.Start - distal).sqrMagnitude > 1e-10f) continue;
                if (Vector3.Dot(segment.End - segment.Start, Vector3.forward) <= 0f) continue;
                var outermost = segment.End + (tip - distal).normalized * segment.EndRadius;
                Assert.That(Vector3.Distance(outermost, tip), Is.LessThan(1e-5f));
                found = true;
            }
            Assert.That(found, Is.True);
        }

        [Test]
        public void Shape_SkipsBonesWithAnUntrackedJoint()
        {
            var (positions, valid) = OpenHand();
            valid[XRHandJointID.IndexTip.ToIndex()] = false;
            var segments = new List<DemoGhostHandSegment>();

            DemoGhostHandShape.Build(positions, valid, segments);

            Assert.That(segments.Count, Is.EqualTo(DemoGhostHandShape.BoneCount - 1));
        }

        [Test]
        public void Hands_TrackedHandIsDrawnAndHiddenWhenTrackingIsLost()
        {
            _host = new GameObject("Ghost hands test");
            var hands = _host.AddComponent<DemoGhostHands>();
            var (positions, valid) = OpenHand();
            var tip = positions[XRHandJointID.IndexTip.ToIndex()];

            hands.ApplyHand(1, true, positions, valid);

            Assert.That(hands.IsHandVisible(1), Is.True);
            Assert.That(hands.IsHandVisible(0), Is.False);
            var mesh = hands.HandMesh(1);
            Assert.That(mesh.vertexCount, Is.GreaterThan(0));
            Assert.That(mesh.subMeshCount, Is.EqualTo(2));
            Assert.That(hands.HandRenderer(1).sharedMaterials.Length, Is.EqualTo(2));
            Assert.That(hands.HandRenderer(1).sortingOrder, Is.EqualTo(DemoSessionPanel.HandSortingOrder), "Drawn after the panels.");
            var farthest = float.MinValue;
            foreach (var vertex in mesh.vertices) farthest = Mathf.Max(farthest, vertex.z);
            // The low-poly cap reaches the tip joint within a millimetre and never past it.
            Assert.That(farthest, Is.LessThanOrEqualTo(tip.z + 1e-5f));
            Assert.That(farthest, Is.GreaterThan(tip.z - 0.001f));

            hands.ApplyHand(1, false, positions, valid);

            Assert.That(hands.IsHandVisible(1), Is.False);
        }

        [Test]
        public void Hands_HandWithoutAnyCompleteBoneIsHidden()
        {
            _host = new GameObject("Ghost hands test");
            var hands = _host.AddComponent<DemoGhostHands>();
            var (positions, valid) = OpenHand();
            hands.ApplyHand(0, true, positions, valid);
            for (var index = 0; index < valid.Length; index++) valid[index] = false;

            hands.ApplyHand(0, true, positions, valid);

            Assert.That(hands.IsHandVisible(0), Is.False);
        }

        [Test]
        public void Hands_ShaderShipsInResources()
        {
            var shader = Resources.Load<Shader>(DemoGhostHands.ShaderResourcePath);

            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.isSupported, Is.True);
        }
    }
}
