using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace Hapbeat.DemoSwitch.Tests
{
    public sealed class DemoHandsTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown()
        {
            if (_host != null) Object.DestroyImmediate(_host);
        }

        private static bool MetaModelsPresent =>
            Resources.Load<GameObject>(DemoHands.MetaLeftModelPath) != null && Resources.Load<GameObject>(DemoHands.MetaRightModelPath) != null;

        /// <summary>The ghost-hands test hand with XR Hands joint rotations: +Z along each bone, +Y up (back of the hand).</summary>
        private static (Pose[] poses, bool[] valid) OpenHandPoses()
        {
            var (positions, valid) = DemoGhostHandsTests.OpenHand();
            var poses = new Pose[positions.Length];
            for (var index = 0; index < positions.Length; index++)
                poses[index] = new Pose(positions[index], Quaternion.LookRotation(Vector3.forward, Vector3.up));
            return (poses, valid);
        }

        [Test]
        public void MissingPrivateModelsFallBackToTheProceduralGhostHands()
        {
            _host = new GameObject("Demo hands test");
            var hands = _host.AddComponent<DemoHands>();
            hands.LeftModelPath = hands.RightModelPath = "HapbeatPrivate/Missing/NoHand";
            var (poses, valid) = OpenHandPoses();

            hands.ApplyHand(1, true, poses, valid);

            Assert.That(hands.UsesMetaModel, Is.False);
            Assert.That(hands.HandRenderer(1), Is.Null);
            var ghost = _host.GetComponent<DemoGhostHands>();
            Assert.That(ghost, Is.Not.Null, "The procedural ghost hands take over.");
            ghost.ApplyHand(1, true, poses.Select(p => p.position).ToArray(), valid);
            Assert.That(ghost.IsHandVisible(1), Is.True);
        }

        [Test]
        public void MetaHandFollowsTheJointsAndSwitchesLook()
        {
            if (!MetaModelsPresent)
            {
                // A public clone: only the fallback exists, covered above; nothing of Meta's to check.
                Assert.That(Resources.Load<Shader>(DemoGhostHands.ShaderResourcePath), Is.Not.Null);
                return;
            }
            _host = new GameObject("Demo hands test");
            var hands = _host.AddComponent<DemoHands>();
            var (poses, valid) = OpenHandPoses();

            hands.ApplyHand(1, true, poses, valid);

            Assert.That(hands.UsesMetaModel, Is.True);
            Assert.That(hands.IsHandVisible(1), Is.True);
            Assert.That(hands.IsHandVisible(0), Is.False, "Untouched hand stays hidden.");
            Assert.That(_host.GetComponent<DemoGhostHands>(), Is.Null);
            foreach (var id in new[] { XRHandJointID.Wrist, XRHandJointID.IndexIntermediate, XRHandJointID.IndexTip, XRHandJointID.LittleTip })
                Assert.That(Vector3.Distance(hands.Joint(1, id).position, poses[id.ToIndex()].position), Is.LessThan(1e-5f), id.ToString());
            // The index distal joint is drawn IndexDistalToTip behind the tracked tip (here 30 mm from the tracked distal).
            var tip = poses[XRHandJointID.IndexTip.ToIndex()].position;
            Assert.That(Vector3.Distance(hands.Joint(1, XRHandJointID.IndexDistal).position, tip + Vector3.back * DemoHands.IndexDistalToTip), Is.LessThan(1e-5f));
            // Meta's joints face -Z toward the fingertips.
            Assert.That(Vector3.Dot(hands.Joint(1, XRHandJointID.IndexDistal).forward, Vector3.forward), Is.LessThan(-0.99f));

            var renderer = hands.HandRenderer(1);
            Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(3));
            Assert.That(renderer.sharedMaterials.Select(m => m.renderQueue), Is.EqualTo(new[] { 2999, 3000, 3001 }));
            Assert.That(renderer.sharedMaterials[1].shader.name, Is.EqualTo("Hidden/Hapbeat/DemoHandGhost"));
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            Assert.That(Vector3.Distance(block.GetVector("_WristPosition"), poses[XRHandJointID.Wrist.ToIndex()].position), Is.LessThan(1e-5f));
            Assert.That(Vector3.Dot(block.GetVector("_ArmDirection"), Vector3.back), Is.GreaterThan(0.99f), "Toward the elbow.");

            hands.SetStyle(DemoHandStyle.Skin);
            Assert.That(renderer.sharedMaterials[1].shader.name, Is.EqualTo("Hidden/Hapbeat/DemoHandSkin"));
            hands.ApplyHand(1, true, poses, valid);
            renderer.GetPropertyBlock(block);
            Assert.That(block.GetTexture("_MainTex"), Is.Not.Null, "Baked right-hand skin.");

            hands.ApplyHand(1, false, poses, valid);
            Assert.That(hands.IsHandVisible(1), Is.False);
        }

        /// <summary>
        /// Skins Meta's mesh on the CPU. Its last index phalanx is rigid on the distal joint and ends
        /// <see cref="DemoHands.IndexDistalToTip"/> past it; posed with tracked fingers of different lengths, the
        /// drawn fingertip must end at the tracked IndexTip (the poke point).
        /// </summary>
        [Test]
        public void MetaIndexFingertipSurfaceEndsAtThePokePoint()
        {
            if (!MetaModelsPresent)
            {
                Assert.That(Resources.Load<Shader>(DemoGhostHands.ShaderResourcePath), Is.Not.Null);
                return;
            }
            foreach (var path in new[] { DemoHands.MetaLeftModelPath, DemoHands.MetaRightModelPath })
            {
                var model = Object.Instantiate(Resources.Load<GameObject>(path));
                try
                {
                    var renderer = model.GetComponentInChildren<SkinnedMeshRenderer>();
                    var byName = model.GetComponentsInChildren<Transform>().GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                    var count = XRHandJointID.EndMarker.ToIndex();
                    var joints = new Transform[count];
                    var poses = new Pose[count];
                    var valid = new bool[count];
                    for (var index = 0; index < count; index++)
                    {
                        joints[index] = byName[DemoHands.JointPrefix + XRHandJointIDUtility.FromIndex(index)];
                        poses[index] = new Pose(joints[index].position, joints[index].rotation * Quaternion.Inverse(DemoHands.JointRotationOffset));
                        valid[index] = true;
                    }
                    var tipIndex = XRHandJointID.IndexTip.ToIndex();
                    var distal = poses[XRHandJointID.IndexDistal.ToIndex()].position;
                    var along = (poses[tipIndex].position - distal).normalized;
                    var fingertip = FingertipVertices(renderer, joints);
                    Assert.That(fingertip.Count, Is.GreaterThan(0), "Index distal/tip bones skin the mesh.");

                    var length = fingertip.Max(v => Vector3.Dot(Skin(renderer, v) - distal, along));
                    Debug.Log("[Demo Hands test] " + path + ": index fingertip surface ends " + (length * 1000f).ToString("0.00") + " mm past XRHand_IndexDistal.");
                    Assert.That(length, Is.EqualTo(DemoHands.IndexDistalToTip).Within(0.0005f), path + ": IndexDistalToTip matches the mesh.");

                    // Tracked fingers shorter and longer than Meta's: the drawn tip still ends at the poke point.
                    foreach (var tracked in new[] { 0.018f, 0.028f })
                    {
                        poses[tipIndex] = new Pose(distal + along * tracked, poses[tipIndex].rotation);
                        DemoHands.PoseJoints(joints, poses, valid);
                        var reach = fingertip.Max(v => Vector3.Dot(Skin(renderer, v) - poses[tipIndex].position, along));
                        Assert.That(Mathf.Abs(reach), Is.LessThan(0.001f), path + ": drawn index tip within 1 mm of the poke point (finger " + tracked + " m).");
                    }
                }
                finally { Object.DestroyImmediate(model); }
            }
        }

        /// <summary>Vertices mainly skinned by the index distal or tip joint.</summary>
        private static List<int> FingertipVertices(SkinnedMeshRenderer renderer, Transform[] joints)
        {
            var bones = renderer.bones;
            var weights = renderer.sharedMesh.boneWeights;
            var result = new List<int>();
            for (var vertex = 0; vertex < weights.Length; vertex++)
            {
                var bone = bones[weights[vertex].boneIndex0];
                if (bone == joints[XRHandJointID.IndexDistal.ToIndex()] || bone == joints[XRHandJointID.IndexTip.ToIndex()]) result.Add(vertex);
            }
            return result;
        }

        /// <summary>Linear blend skinning of one vertex with the bones' current transforms, as the GPU draws it.</summary>
        private static Vector3 Skin(SkinnedMeshRenderer renderer, int vertex)
        {
            var mesh = renderer.sharedMesh;
            return Skin(mesh.vertices[vertex], mesh.boneWeights[vertex], renderer.bones, mesh.bindposes);
        }

        private static Vector3 Skin(Vector3 vertex, BoneWeight weight, Transform[] bones, Matrix4x4[] bindposes)
        {
            Vector3 Part(int bone, float w) => w <= 0f ? Vector3.zero : (bones[bone].localToWorldMatrix * bindposes[bone]).MultiplyPoint3x4(vertex) * w;
            return Part(weight.boneIndex0, weight.weight0) + Part(weight.boneIndex1, weight.weight1)
                + Part(weight.boneIndex2, weight.weight2) + Part(weight.boneIndex3, weight.weight3);
        }

        [Test]
        public void HandShadersShipInResources()
        {
            foreach (var path in new[] { DemoHands.GhostShaderPath, DemoHands.SkinShaderPath, DemoHands.OutlineShaderPath })
            {
                var shader = Resources.Load<Shader>(path);
                Assert.That(shader, Is.Not.Null, path);
                Assert.That(shader.isSupported, Is.True, path);
            }
        }

        [Test]
        public void Settings_HandsAreOffByDefaultWithTheGhostLook()
        {
            var settings = ScriptableObject.CreateInstance<DemoSwitchSettings>();
            try
            {
                Assert.That(settings.Hands, Is.False);
                Assert.That(settings.HandStyle, Is.EqualTo(DemoHandStyle.Ghost));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
