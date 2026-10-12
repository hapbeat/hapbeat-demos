using System.Linq;
using Hapbeat.DemoSwitch.Editor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace Hapbeat.DemoSwitch.Tests
{
    public sealed class DemoTutorialHandTests
    {
        private GameObject _host;
        private GameObject _skeleton;
        private Material _material;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
            if (_skeleton != null) Object.DestroyImmediate(_skeleton);
            if (_material != null) Object.DestroyImmediate(_material);
        }

        [Test]
        public void HandShapesMirrorSidewaysTurnsAndRollsForTheLeftHand()
        {
            for (var s = 0; s < DemoTutorialHand.HandShapes.Length; s++)
            {
                var (joint, open, fist) = DemoTutorialHand.HandShapes[s];
                Assert.That(Quaternion.Angle(DemoTutorialHand.ShapeRotation(s, 0f, false), Quaternion.Euler(open)), Is.LessThan(.01f), $"{joint}: open");
                Assert.That(Quaternion.Angle(DemoTutorialHand.ShapeRotation(s, 1f, false), Quaternion.Euler(fist)), Is.LessThan(.01f), $"{joint}: fist");
                Assert.That(Quaternion.Angle(DemoTutorialHand.ShapeRotation(s, 2f, false), Quaternion.Euler(fist)), Is.LessThan(.01f), $"{joint}: curl clamped");
                Assert.That(Quaternion.Angle(DemoTutorialHand.ShapeRotation(s, 1f, true), Quaternion.Euler(fist.x, -fist.y, -fist.z)), Is.LessThan(.01f), $"{joint}: left mirrored");
            }
        }

        /// <summary>
        /// The forearm runs from just inside the hand to its rounded end past the fade, its section the wrist radii at the
        /// joint, and every face looks outward (the fills draw front faces only; an inward face would show the arm's inside).
        /// </summary>
        [Test]
        public void ForearmMeshIsAClosedTubeFacingOutward()
        {
            const float length = .15f;
            var wrist = new Vector2(.026f, .018f);
            var mesh = DemoTutorialHand.ForearmMesh(length, wrist, new Vector2(.026f, .017f), .01f, new Vector2(.034f, .027f), new Vector2(.3f, .4f));
            try
            {
                var vertices = mesh.vertices;
                Assert.That(vertices.Min(v => v.z), Is.EqualTo(-.012f).Within(1e-5f), "starts inside the hand");
                Assert.That(vertices.Max(v => v.z), Is.InRange(length, length + .026f), "ends in a rounded cap just past the fade");
                var atWrist = vertices.Where(v => Mathf.Abs(v.z + .012f) < 1e-5f && v.sqrMagnitude > .0002f).ToArray();
                Assert.That(atWrist.Max(v => v.x), Is.EqualTo(wrist.x).Within(1e-4f));
                Assert.That(atWrist.Max(v => v.y), Is.EqualTo(wrist.y).Within(1e-3f));
                Assert.That(mesh.uv.Distinct().ToArray(), Is.EqualTo(new[] { new Vector2(.3f, .4f) }), "one skin colour");
                var triangles = mesh.triangles;
                for (var t = 0; t < triangles.Length; t += 3)
                {
                    var (a, b, c) = (vertices[triangles[t]], vertices[triangles[t + 1]], vertices[triangles[t + 2]]);
                    var normal = Vector3.Cross(b - a, c - a);
                    if (normal.sqrMagnitude < 1e-14f) continue;
                    var centroid = (a + b + c) / 3f;
                    var axis = new Vector3(0f, 0f, Mathf.Clamp(centroid.z, 0f, length));
                    Assert.That(Vector3.Dot(normal, centroid - axis), Is.GreaterThan(0f), $"triangle {t / 3} faces outward");
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        /// <summary>
        /// Without Meta's mesh in use (no shared hands here) the hand is the host's placeholder: its wrist joint lands on the
        /// pose, the fingers curl, and the forearm is drawn from the wrist joint along the arm.
        /// </summary>
        [Test]
        public void PlaceholderHandTakesThePoseWithItsForearm()
        {
            _material = new Material(Shader.Find("Hidden/Internal-Colored"));
            _skeleton = PlaceholderSkeleton(_material);
            _host = new GameObject("Demo Right Hand");
            var hand = _host.AddComponent<DemoTutorialHand>();
            hand.PlaceholderSkeleton = _skeleton;
            hand.Scale = .9f;
            var wrist = new Vector3(.1f, 1.2f, .4f);
            var rotation = Quaternion.Euler(10f, 30f, -20f);

            Assert.That(hand.ApplyPose(wrist, rotation, 1f, DemoTutorialHand.StraightArm(rotation), .07f), Is.True);

            Assert.That(hand.Model, Is.EqualTo(DemoTutorialHandModel.Placeholder));
            Assert.That(hand.IsLookResolved, Is.True);
            Assert.That(_host.transform.localScale.x, Is.EqualTo(.9f).Within(1e-6f));
            Assert.That(Vector3.Distance(hand.Joint(XRHandJointID.Wrist).position, wrist), Is.LessThan(1e-5f));
            Assert.That(Quaternion.Angle(hand.Joint(XRHandJointID.Wrist).rotation, rotation), Is.LessThan(.01f));
            var proximal = hand.Joint(XRHandJointID.IndexProximal);
            var fist = DemoTutorialHand.HandShapes.First(s => s.joint == XRHandJointID.IndexProximal).fist;
            Assert.That(Quaternion.Angle(proximal.localRotation, Quaternion.Euler(fist)), Is.LessThan(.01f), "the fist's knuckle bend");
            Assert.That(hand.ForearmShown, Is.True);
            Assert.That(hand.TryGetForearm(out var forearmWrist, out var towardElbow, out var fadeStart, out var fadeEnd), Is.True);
            Assert.That(Vector3.Distance(forearmWrist, wrist), Is.LessThan(1e-5f));
            Assert.That(Vector3.Angle(towardElbow, rotation * Vector3.back), Is.LessThan(.01f), "a straight wrist: the arm runs back from the fingers");
            Assert.That(fadeStart, Is.EqualTo(.07f * .9f).Within(1e-6f));
            Assert.That(fadeEnd, Is.EqualTo((.07f + hand.ForearmFadeLength) * .9f).Within(1e-6f));

            hand.ShowForearm = false;
            hand.ApplyPose(wrist, rotation, 0f, DemoTutorialHand.StraightArm(rotation), .07f);
            Assert.That(hand.ForearmShown, Is.False, "the forearm switched off");
            Assert.That(Quaternion.Angle(proximal.localRotation, Quaternion.Euler(DemoTutorialHand.HandShapes.First(s => s.joint == XRHandJointID.IndexProximal).open)), Is.LessThan(.01f), "open again: the pose depends on its inputs alone");
        }

        [Test]
        public void WithoutAPlaceholderSkeletonNothingIsShown()
        {
            _host = new GameObject("Demo Left Hand");
            var hand = _host.AddComponent<DemoTutorialHand>();
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no placeholder skeleton"));
            Assert.That(hand.ApplyPose(Vector3.zero, Quaternion.identity, 0f, Quaternion.identity, .07f), Is.False);
            Assert.That(hand.ForearmShown, Is.False);
            Assert.That(hand.Joint(XRHandJointID.Wrist), Is.Null);
        }

        /// <summary>
        /// A shot-like motion (fist, push, open while the wrist bends back, back) played frame by frame on Meta's mesh posed
        /// by <see cref="DemoTutorialHand.PoseMetaHand"/>: no joint turns sharply from one frame to the next
        /// (<see cref="DemoHandJitter"/>), and each frame's pose depends on its inputs alone (a fresh copy posed with the last
        /// frame's inputs gives the same hand). The bent wrist once spun the palm and fingers about the wrist every frame.
        /// </summary>
        [TestCase(72, false)]
        [TestCase(90, false)]
        [TestCase(72, true)]
        [TestCase(90, true)]
        public void MetaHandMovesSmoothlyFrameToFrame(int fps, bool left)
        {
            if (!DemoTutorialHandCuff.MetaPresent) Assert.Ignore("Meta hand mesh is private and not linked into this project.");
            var root = new GameObject("hand root").transform;
            root.localScale = Vector3.one * .9f;
            var fresh = new GameObject("fresh root").transform;
            fresh.localScale = Vector3.one * .9f;
            try
            {
                var (joints, rest) = MetaHand(left, root);
                var (freshJoints, freshRest) = MetaHand(left, fresh);
                var jitter = new DemoHandJitter(Enumerable.Range(0, joints.Length).Select(i => XRHandJointIDUtility.FromIndex(i).ToString()).ToArray());
                var frames = Mathf.CeilToInt(2f * MotionSeconds * fps);
                (Vector3 wrist, Quaternion hand, float curl) last = default;
                for (var f = 1; f <= frames; f++)
                {
                    last = Motion(Mathf.Repeat(f / (float)fps, MotionSeconds), left);
                    DemoTutorialHand.PoseMetaHand(joints, rest, left, last.curl, last.wrist, last.hand);
                    jitter.Add(joints, last.wrist, last.hand);
                }

                var sharp = jitter.Analyse().Where(j => j.Turns > 0).Select(j => $"{j.Name}: {j.Turns} sharp turns, max {j.MaxStepMm:0.0} mm/frame").ToArray();
                Assert.That(sharp, Is.Empty, $"{fps} fps: joints turning more than {DemoHandJitter.TurnDegrees} degrees between frames");

                DemoTutorialHand.PoseMetaHand(freshJoints, freshRest, left, last.curl, last.wrist, last.hand);
                for (var i = 0; i < joints.Length; i++)
                {
                    if (joints[i] == null) continue;
                    Assert.That(Vector3.Distance(joints[i].position, freshJoints[i].position), Is.LessThan(1e-4f), $"{XRHandJointIDUtility.FromIndex(i)}: the same inputs pose the same hand");
                }
            }
            finally
            {
                Object.DestroyImmediate(fresh.gameObject);
                Object.DestroyImmediate(root.gameObject);
            }
        }

        /// <summary>
        /// Through the motion (its opening bends the wrist back) Meta's mesh is posed as a tracked hand is: the wrist bone turns
        /// with the hand and the palm keeps its rest pose on it, so the mesh is never bent between its wrist bone and the palm
        /// (its skin weights do not follow that bend: bent back, the palm's heel came out through the back of the hand).
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public void MetaHandIsNeverBentAtItsWristBone(bool left)
        {
            if (!DemoTutorialHandCuff.MetaPresent) Assert.Ignore("Meta hand mesh is private and not linked into this project.");
            var root = new GameObject("hand root").transform;
            root.localScale = Vector3.one * .9f;
            try
            {
                var (joints, rest) = MetaHand(left, root);
                var wrist = joints[XRHandJointID.Wrist.ToIndex()];
                var palm = XRHandJointID.Palm.ToIndex();
                var bent = 0f;
                for (var t = 0f; t < MotionSeconds; t += 1f / 72f)
                {
                    var (position, hand, curl) = Motion(t, left);
                    bent = Mathf.Max(bent, Quaternion.Angle(hand, Fist(left)));
                    DemoTutorialHand.PoseMetaHand(joints, rest, left, curl, position, hand);
                    Assert.That(Quaternion.Angle(wrist.rotation, hand * DemoHands.JointRotationOffset), Is.LessThan(.01f), $"t={t:0.00}: the wrist bone turns with the hand");
                    Assert.That(Quaternion.Angle(joints[palm].localRotation, rest[palm].rotation), Is.LessThan(.01f), $"t={t:0.00}: the palm at rest on the wrist bone");
                }

                Assert.That(bent, Is.GreaterThan(30f), "the opening bends the wrist back (else this checks nothing)");
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        [Test]
        public void TheCuffOfMetasMeshIsMeasuredAtTheWrist()
        {
            if (!DemoTutorialHandCuff.MetaPresent) Assert.Ignore("Meta hand mesh is private and not linked into this project.");
            foreach (var left in new[] { false, true })
            {
                var (uv, radii, _, end) = DemoTutorialHandCuff.Measure(left, .002f, .01f);
                Assert.That(radii.x, Is.InRange(.015f, .04f), "half width across the wrist");
                Assert.That(radii.y, Is.InRange(.01f, .035f), "half thickness through the wrist");
                Assert.That(radii.x, Is.GreaterThan(radii.y), "a flattened wrist");
                Assert.That(end, Is.GreaterThan(.01f), "the mesh runs past the band");
                Assert.That(uv.x, Is.InRange(0f, 1f));
                Assert.That(uv.y, Is.InRange(0f, 1f));
            }
        }

        private const float MotionSeconds = 3f;

        /// <summary>The fist's rotation along a push forward and a little up, rolled thumb side up (mirrored for the left hand).</summary>
        private static Quaternion Fist(bool left) =>
            Quaternion.LookRotation(Quaternion.Euler(-8f, left ? -20f : 20f, 0f) * Vector3.forward, Vector3.up) * Quaternion.Euler(0f, 0f, left ? 20f : -20f);

        /// <summary>
        /// A shot-like loop <paramref name="t"/> seconds in (a function of the time alone): open -> fist -> push 28 cm ->
        /// open while still pushing, the wrist bending back 60 degrees -> back.
        /// </summary>
        private static (Vector3 wrist, Quaternion hand, float curl) Motion(float t, bool left)
        {
            float Ramp(float from, float to) => Mathf.Clamp01((t - from) / (to - from));
            float Ease(float x) => x * x * (3f - (2f * x));
            const float pushAt = 1f;
            const float openAt = pushAt + .3f;
            const float returnAt = pushAt + 1f;
            var curl = Ramp(.35f, .55f) * (1f - Ramp(openAt, openAt + .1f));
            var push = Ease(Ramp(pushAt, pushAt + .45f)) * (1f - Ease(Ramp(returnAt, returnAt + .7f)));
            var extend = Ease(Ramp(openAt, openAt + .15f)) * (1f - Ease(Ramp(returnAt, returnAt + .4f)));
            var fist = Fist(left);
            var start = new Vector3(left ? .04f : -.04f, 1.1f, .3f);
            return (start + (fist * Vector3.forward * (.28f * push)), fist * Quaternion.Euler(-60f * extend, 0f, 0f), curl);
        }

        private static (Transform[] joints, Pose[] rest) MetaHand(bool left, Transform parent)
        {
            var meta = Object.Instantiate(Resources.Load<GameObject>(left ? DemoHands.MetaLeftModelPath : DemoHands.MetaRightModelPath), parent, false);
            var byName = meta.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var joints = new Transform[XRHandJointID.EndMarker.ToIndex()];
            for (var i = 0; i < joints.Length; i++) byName.TryGetValue(DemoHands.JointPrefix + XRHandJointIDUtility.FromIndex(i), out joints[i]);
            var rest = joints.Select(j => j != null ? new Pose(j.localPosition, j.localRotation) : Pose.identity).ToArray();
            return (joints, rest);
        }

        /// <summary>
        /// A placeholder hand in XR Hands axes (joints named R_&lt;joint&gt;, +Z toward the fingertips): the wrist at the root, each
        /// finger a chain of 3 cm bones along +Z, with an empty skinned renderer in <paramref name="material"/> (the placeholder's look).
        /// </summary>
        private static GameObject PlaceholderSkeleton(Material material)
        {
            var root = new GameObject("Placeholder");
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMaterials = new[] { material };
            var wrist = new GameObject("R_" + XRHandJointID.Wrist).transform;
            wrist.SetParent(root.transform, false);
            var palm = new GameObject("R_" + XRHandJointID.Palm).transform;
            palm.SetParent(wrist, false);
            palm.localPosition = new Vector3(0f, 0f, .04f);
            var fingers = new[]
            {
                new[] { XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip },
                new[] { XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, XRHandJointID.IndexTip },
                new[] { XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip },
                new[] { XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, XRHandJointID.RingTip },
                new[] { XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, XRHandJointID.LittleTip },
            };
            for (var f = 0; f < fingers.Length; f++)
            {
                var parent = wrist;
                for (var j = 0; j < fingers[f].Length; j++)
                {
                    var joint = new GameObject("R_" + fingers[f][j]).transform;
                    joint.SetParent(parent, false);
                    joint.localPosition = j == 0 ? new Vector3((f - 2) * -.02f, 0f, .01f) : new Vector3(0f, 0f, .03f);
                    parent = joint;
                }
            }

            return root;
        }
    }
}
