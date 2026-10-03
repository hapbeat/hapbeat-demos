using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;

namespace Hapbeat.DemoSwitch
{
    /// <summary>One tapered capsule of the ghost hand: spheres at both ends joined by a tube.</summary>
    internal struct DemoGhostHandSegment
    {
        public Vector3 Start;
        public Vector3 End;
        public float StartRadius;
        public float EndRadius;
        /// <summary>The end is a fingertip: its cap sphere keeps the full radius and its outermost point is the tip joint.</summary>
        public bool Tip;
    }

    /// <summary>
    /// Procedural hand shape from XR Hands joint positions (index = <c>XRHandJointID.ToIndex()</c>).
    /// Finger chains and palm links become tapered capsules. A fingertip capsule ends its cap exactly
    /// at the tip joint, the point the panels poke with, so the drawn tip and the press point agree.
    /// </summary>
    internal static class DemoGhostHandShape
    {
        public const int JointCount = 26;

        private struct Bone
        {
            public readonly int From;
            public readonly int To;
            public readonly float FromRadius;
            public readonly float ToRadius;
            public readonly bool Tip;

            public Bone(XRHandJointID from, XRHandJointID to, float fromRadius, float toRadius, bool tip = false)
            {
                From = from.ToIndex();
                To = to.ToIndex();
                FromRadius = fromRadius;
                ToRadius = toRadius;
                Tip = tip;
            }
        }

        // Radii in metres, roughly an adult hand; the little finger is thinner.
        private static readonly Bone[] Bones =
        {
            new Bone(XRHandJointID.Wrist, XRHandJointID.ThumbMetacarpal, 0.016f, 0.013f),
            new Bone(XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal, 0.013f, 0.0105f),
            new Bone(XRHandJointID.ThumbProximal, XRHandJointID.ThumbDistal, 0.0105f, 0.009f),
            new Bone(XRHandJointID.ThumbDistal, XRHandJointID.ThumbTip, 0.009f, 0.0075f, true),

            new Bone(XRHandJointID.Wrist, XRHandJointID.IndexMetacarpal, 0.016f, 0.013f),
            new Bone(XRHandJointID.IndexMetacarpal, XRHandJointID.IndexProximal, 0.013f, 0.0095f),
            new Bone(XRHandJointID.IndexProximal, XRHandJointID.IndexIntermediate, 0.0095f, 0.0085f),
            new Bone(XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal, 0.0085f, 0.0075f),
            new Bone(XRHandJointID.IndexDistal, XRHandJointID.IndexTip, 0.0075f, 0.0065f, true),

            new Bone(XRHandJointID.Wrist, XRHandJointID.MiddleMetacarpal, 0.016f, 0.013f),
            new Bone(XRHandJointID.MiddleMetacarpal, XRHandJointID.MiddleProximal, 0.013f, 0.0095f),
            new Bone(XRHandJointID.MiddleProximal, XRHandJointID.MiddleIntermediate, 0.0095f, 0.0085f),
            new Bone(XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal, 0.0085f, 0.0075f),
            new Bone(XRHandJointID.MiddleDistal, XRHandJointID.MiddleTip, 0.0075f, 0.0065f, true),

            new Bone(XRHandJointID.Wrist, XRHandJointID.RingMetacarpal, 0.016f, 0.013f),
            new Bone(XRHandJointID.RingMetacarpal, XRHandJointID.RingProximal, 0.013f, 0.009f),
            new Bone(XRHandJointID.RingProximal, XRHandJointID.RingIntermediate, 0.009f, 0.008f),
            new Bone(XRHandJointID.RingIntermediate, XRHandJointID.RingDistal, 0.008f, 0.007f),
            new Bone(XRHandJointID.RingDistal, XRHandJointID.RingTip, 0.007f, 0.006f, true),

            new Bone(XRHandJointID.Wrist, XRHandJointID.LittleMetacarpal, 0.016f, 0.012f),
            new Bone(XRHandJointID.LittleMetacarpal, XRHandJointID.LittleProximal, 0.012f, 0.008f),
            new Bone(XRHandJointID.LittleProximal, XRHandJointID.LittleIntermediate, 0.008f, 0.007f),
            new Bone(XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal, 0.007f, 0.0062f),
            new Bone(XRHandJointID.LittleDistal, XRHandJointID.LittleTip, 0.0062f, 0.0055f, true),

            // Palm: knuckle row and the thumb web fill the gaps between the metacarpal spokes.
            new Bone(XRHandJointID.IndexProximal, XRHandJointID.MiddleProximal, 0.0105f, 0.0105f),
            new Bone(XRHandJointID.MiddleProximal, XRHandJointID.RingProximal, 0.0105f, 0.0105f),
            new Bone(XRHandJointID.RingProximal, XRHandJointID.LittleProximal, 0.0105f, 0.0095f),
            new Bone(XRHandJointID.IndexMetacarpal, XRHandJointID.LittleMetacarpal, 0.013f, 0.012f),
            new Bone(XRHandJointID.ThumbMetacarpal, XRHandJointID.IndexProximal, 0.011f, 0.0095f),
        };

        public static int BoneCount => Bones.Length;

        /// <summary>Appends one segment per bone whose two joints are both valid.</summary>
        public static void Build(IReadOnlyList<Vector3> positions, IReadOnlyList<bool> valid, List<DemoGhostHandSegment> result)
        {
            result.Clear();
            foreach (var bone in Bones)
            {
                if (!valid[bone.From] || !valid[bone.To]) continue;
                var start = positions[bone.From];
                var end = positions[bone.To];
                var endRadius = bone.ToRadius;
                if (bone.Tip)
                {
                    // Pull the end cap's centre back so its outermost point is the tip joint itself.
                    var offset = end - start;
                    var length = offset.magnitude;
                    if (length > 1e-5f)
                    {
                        endRadius = Mathf.Min(endRadius, length);
                        end -= offset / length * endRadius;
                    }
                }
                result.Add(new DemoGhostHandSegment
                {
                    Start = start,
                    End = end,
                    StartRadius = bone.FromRadius,
                    EndRadius = endRadius,
                    Tip = bone.Tip
                });
            }
        }
    }

    /// <summary>Writes ghost hand segments into one mesh: low-poly spheres at both ends and a tapered tube.</summary>
    internal static class DemoGhostHandMesh
    {
        private const int Sides = 10;
        private const int Rings = 6;
        // Joint spheres sit inside the tubes (they only fill the gaps at bends) and the fingertip tube ends inside
        // its cap, so two surfaces never nearly coincide and depth-fight at the joints.
        private const float InnerScale = 0.9f;
        private static readonly List<Vector3> Vertices = new List<Vector3>();
        private static readonly List<Vector3> Normals = new List<Vector3>();
        private static readonly List<int> Triangles = new List<int>();

        /// <summary>
        /// <paramref name="space"/> maps world positions into the renderer's local space. Both submeshes hold
        /// the same triangles: submesh 0 for the depth pre-pass material, submesh 1 for the colour material.
        /// </summary>
        public static void Write(IReadOnlyList<DemoGhostHandSegment> segments, Transform space, Mesh mesh)
        {
            Vertices.Clear();
            Normals.Clear();
            Triangles.Clear();
            foreach (var segment in segments)
            {
                var start = space.InverseTransformPoint(segment.Start);
                var end = space.InverseTransformPoint(segment.End);
                AddSphere(start, segment.StartRadius * InnerScale);
                AddSphere(end, segment.Tip ? segment.EndRadius : segment.EndRadius * InnerScale);
                AddTube(start, end, segment.StartRadius, segment.Tip ? segment.EndRadius * InnerScale : segment.EndRadius);
            }
            mesh.Clear();
            mesh.SetVertices(Vertices);
            mesh.SetNormals(Normals);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(Triangles, 0, false);
            mesh.SetTriangles(Triangles, 1, false);
            mesh.RecalculateBounds();
        }

        private static void AddSphere(Vector3 centre, float radius)
        {
            var first = Vertices.Count;
            for (var ring = 0; ring <= Rings; ring++)
            {
                var polar = Mathf.PI * ring / Rings;
                for (var side = 0; side <= Sides; side++)
                {
                    var azimuth = 2f * Mathf.PI * side / Sides;
                    var normal = new Vector3(Mathf.Sin(polar) * Mathf.Cos(azimuth), Mathf.Cos(polar), Mathf.Sin(polar) * Mathf.Sin(azimuth));
                    Vertices.Add(centre + normal * radius);
                    Normals.Add(normal);
                }
            }
            for (var ring = 0; ring < Rings; ring++)
                for (var side = 0; side < Sides; side++)
                {
                    var a = first + ring * (Sides + 1) + side;
                    var b = a + Sides + 1;
                    Triangles.Add(a); Triangles.Add(a + 1); Triangles.Add(b);
                    Triangles.Add(b); Triangles.Add(a + 1); Triangles.Add(b + 1);
                }
        }

        private static void AddTube(Vector3 start, Vector3 end, float startRadius, float endRadius)
        {
            var axis = end - start;
            if (axis.sqrMagnitude < 1e-10f) return;
            var direction = axis.normalized;
            var side0 = Vector3.Cross(direction, Mathf.Abs(direction.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            // side0 → side1 turns so that (start, end, next start) winds clockwise seen from outside.
            var side1 = Vector3.Cross(side0, direction);
            var first = Vertices.Count;
            for (var side = 0; side <= Sides; side++)
            {
                var angle = 2f * Mathf.PI * side / Sides;
                var normal = side0 * Mathf.Cos(angle) + side1 * Mathf.Sin(angle);
                Vertices.Add(start + normal * startRadius);
                Normals.Add(normal);
                Vertices.Add(end + normal * endRadius);
                Normals.Add(normal);
            }
            for (var side = 0; side < Sides; side++)
            {
                var a = first + side * 2;
                Triangles.Add(a); Triangles.Add(a + 1); Triangles.Add(a + 2);
                Triangles.Add(a + 2); Triangles.Add(a + 1); Triangles.Add(a + 3);
            }
        }
    }

    /// <summary>
    /// Translucent, rim-lit hands drawn procedurally from XR Hands joints (no third-party hand model).
    /// Joints are sampled the same way as the panels' fingertip poke (<see cref="DemoSessionPointers"/>:
    /// the first running <see cref="XRHandSubsystem"/>, converted through the same tracking space), so the
    /// drawn index tip is the press point. An untracked hand is hidden; controllers draw nothing here.
    /// Enabled per application by the settings' Ghost Hands flag (off by default).
    /// </summary>
    public sealed class DemoGhostHands : MonoBehaviour
    {
        public const string ShaderResourcePath = "HapbeatDemoGhostHands/GhostHand";
        private static readonly List<XRHandSubsystem> Subsystems = new List<XRHandSubsystem>();
        private readonly Vector3[] _positions = new Vector3[DemoGhostHandShape.JointCount];
        private readonly bool[] _valid = new bool[DemoGhostHandShape.JointCount];
        private readonly List<DemoGhostHandSegment> _segments = new List<DemoGhostHandSegment>();
        private readonly MeshRenderer[] _renderers = new MeshRenderer[2];
        private readonly Mesh[] _meshes = new Mesh[2];
        private Material _depthMaterial;
        private Material _colorMaterial;
        private bool _created;

        private void LateUpdate()
        {
            SubsystemManager.GetSubsystems(Subsystems);
            XRHandSubsystem running = null;
            foreach (var subsystem in Subsystems)
                if (subsystem.running) { running = subsystem; break; }
            var space = DemoSessionPointers.TrackingSpace();
            UpdateHand(0, running != null ? running.leftHand : default, running != null, space);
            UpdateHand(1, running != null ? running.rightHand : default, running != null, space);
        }

        private void UpdateHand(int side, XRHand hand, bool available, Transform space)
        {
            var tracked = available && hand.isTracked;
            if (tracked)
                for (var index = 0; index < DemoGhostHandShape.JointCount; index++)
                {
                    _valid[index] = hand.GetJoint(XRHandJointIDUtility.FromIndex(index)).TryGetPose(out var pose);
                    _positions[index] = _valid[index] ? DemoSessionPointers.ToWorld(space, pose.position) : Vector3.zero;
                }
            ApplyHand(side, tracked, _positions, _valid);
        }

        /// <summary>Shows hand <paramref name="side"/> (0 left, 1 right) from world joint positions, or hides it when untracked.</summary>
        internal void ApplyHand(int side, bool tracked, IReadOnlyList<Vector3> positions, IReadOnlyList<bool> valid)
        {
            if (!EnsureCreated()) return;
            var renderer = _renderers[side];
            if (tracked)
            {
                DemoGhostHandShape.Build(positions, valid, _segments);
                tracked = _segments.Count > 0;
            }
            if (tracked) DemoGhostHandMesh.Write(_segments, renderer.transform, _meshes[side]);
            if (renderer.gameObject.activeSelf != tracked) renderer.gameObject.SetActive(tracked);
        }

        internal bool IsHandVisible(int side) => _created && _renderers[side].gameObject.activeSelf;
        internal Mesh HandMesh(int side) => _created ? _meshes[side] : null;
        internal MeshRenderer HandRenderer(int side) => _created ? _renderers[side] : null;

        private bool EnsureCreated()
        {
            if (_created) return true;
            var shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null || !shader.isSupported)
            {
                Debug.LogWarning("[Demo Ghost Hands] Shader " + ShaderResourcePath + " is missing or unsupported; ghost hands are disabled.");
                enabled = false;
                return false;
            }
            _depthMaterial = new Material(shader) { name = "Ghost Hand Depth", renderQueue = (int)RenderQueue.Transparent - 1 };
            _depthMaterial.SetFloat("_ColorMask", 0f);
            _depthMaterial.SetFloat("_ZWrite", 1f);
            _colorMaterial = new Material(shader) { name = "Ghost Hand", renderQueue = (int)RenderQueue.Transparent };
            for (var side = 0; side < _renderers.Length; side++)
            {
                var go = new GameObject(side == 0 ? "Ghost Hand Left" : "Ghost Hand Right", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                var mesh = new Mesh { name = go.name };
                mesh.MarkDynamic();
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] { _depthMaterial, _colorMaterial };
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                go.SetActive(false);
                _renderers[side] = renderer;
                _meshes[side] = mesh;
            }
            _created = true;
            return true;
        }

        private void OnDestroy()
        {
            if (!_created) return;
            foreach (var renderer in _renderers)
                if (renderer != null) Release(renderer.gameObject);
            foreach (var mesh in _meshes) Release(mesh);
            Release(_depthMaterial);
            Release(_colorMaterial);
        }

        private static void Release(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
