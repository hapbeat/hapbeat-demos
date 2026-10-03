using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;

namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// The shared tracked hands: Meta's OpenXR hand mesh driven by XR Hands joints, in the Ghost or Skin
    /// look of Safety Mill VR / Energy Duel. The mesh and its baked skin textures are private assets
    /// (Oculus SDK License) loaded from <c>Resources/HapbeatPrivate/MetaHands</c>; a project without them
    /// (a public clone) gets the procedural <see cref="DemoGhostHands"/> instead.
    ///
    /// Joints are sampled the same way as the panels' fingertip poke (<see cref="DemoSessionPointers"/>:
    /// the first running <see cref="XRHandSubsystem"/>, converted through the same tracking space), and
    /// every mesh joint is placed on its XR Hands joint, with the index distal joint aligned so that the drawn
    /// index fingertip ends at the press point (see <see cref="IndexDistalToTip"/>). An untracked hand is
    /// hidden; controllers draw nothing.
    /// Ported from Energy Duel's EnergyHandModelResolver / EnergyMetaHand; enabled per application by the
    /// settings' Hands flag (off by default).
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class DemoHands : MonoBehaviour
    {
        public const string MetaLeftModelPath = "HapbeatPrivate/MetaHands/OpenXRLeftHand";
        public const string MetaRightModelPath = "HapbeatPrivate/MetaHands/OpenXRRightHand";
        public const string MetaLeftSkinPath = "HapbeatPrivate/MetaHands/T_MetaHand_L";
        public const string MetaRightSkinPath = "HapbeatPrivate/MetaHands/T_MetaHand_R";
        public const string GhostShaderPath = "HapbeatDemoHands/HandGhost";
        public const string SkinShaderPath = "HapbeatDemoHands/HandSkin";
        public const string OutlineShaderPath = "HapbeatDemoHands/HandOutline";
        public const string JointPrefix = "XRHand_";

        /// <summary>
        /// Metres from Meta's XRHand_IndexDistal joint to the end of the index fingertip surface (measured from
        /// both meshes by the EditMode test). The mesh's own IndexTip bone sits about halfway along the last
        /// phalanx and is not the main influence of any vertex, so the drawn fingertip moves with the distal joint: it ends where the
        /// tracked IndexTip (the poke point) is only when the distal joint is drawn this far behind that tip.
        /// </summary>
        public const float IndexDistalToTip = 0.0227f;

        /// <summary>Meta's OpenXR joints face -Z toward the fingertips where XR Hands joints face +Z: a half turn about Y.</summary>
        public static readonly Quaternion JointRotationOffset = Quaternion.Euler(0f, 180f, 0f);

        private static readonly int WristPositionId = Shader.PropertyToID("_WristPosition");
        private static readonly int ArmDirectionId = Shader.PropertyToID("_ArmDirection");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly List<XRHandSubsystem> Subsystems = new List<XRHandSubsystem>();
        private static readonly int JointCount = XRHandJointID.EndMarker.ToIndex();

        private enum Mode { Pending, Meta, Procedural }

        private sealed class MetaHand
        {
            public GameObject Model;
            public SkinnedMeshRenderer Renderer;
            public Transform[] Joints;
            public Texture Skin;
        }

        private readonly MetaHand[] _hands = new MetaHand[2];
        private readonly Pose[] _poses = new Pose[JointCount];
        private readonly bool[] _valid = new bool[JointCount];
        private Mode _mode = Mode.Pending;
        private Material[] _ghostMaterials;
        private Material[] _skinMaterials;
        private MaterialPropertyBlock _block;
        /// <summary>Tests point these at a missing path to exercise the procedural fallback.</summary>
        internal string LeftModelPath = MetaLeftModelPath;
        internal string RightModelPath = MetaRightModelPath;

        /// <summary>The hands added by the Demo Switch bootstrap (null when the settings' Hands flag is off).</summary>
        public static DemoHands Instance { get; private set; }

        public DemoHandStyle Style { get; private set; } = DemoHandStyle.Ghost;
        /// <summary>True when Meta's mesh is drawn; false before the first frame or with the procedural fallback.</summary>
        public bool UsesMetaModel => _mode == Mode.Meta;

        /// <summary>The ticket's `hand_style` when present, otherwise <paramref name="fallback"/> (the settings' default).</summary>
        public static DemoHandStyle ResolveStyle(DemoSessionTicket ticket, DemoHandStyle fallback) =>
            ticket != null && ticket.HandStyle.HasValue ? ticket.HandStyle.Value : fallback;

        /// <summary>Switches the look; the procedural fallback has only the ghost look.</summary>
        public void SetStyle(DemoHandStyle style)
        {
            Style = style;
            if (_mode == Mode.Meta) ApplyStyle();
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
        }

        private void OnEnable() => Application.onBeforeRender += OnBeforeRender;

        private void OnDisable() => Application.onBeforeRender -= OnBeforeRender;

        private void LateUpdate()
        {
            if (EnsureCreated() == Mode.Meta) Sample();
        }

        // The hand subsystem refreshes the joints again just before rendering; follow it.
        private void OnBeforeRender()
        {
            if (_mode == Mode.Meta) Sample();
        }

        private void Sample()
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
                for (var index = 0; index < JointCount; index++)
                {
                    _valid[index] = hand.GetJoint(XRHandJointIDUtility.FromIndex(index)).TryGetPose(out var pose);
                    _poses[index] = _valid[index]
                        ? new Pose(DemoSessionPointers.ToWorld(space, pose.position), space != null ? space.rotation * pose.rotation : pose.rotation)
                        : default;
                }
            ApplyHand(side, tracked, _poses, _valid);
        }

        /// <summary>Poses hand <paramref name="side"/> (0 left, 1 right) from world joint poses, or hides it when untracked.</summary>
        internal void ApplyHand(int side, bool tracked, IReadOnlyList<Pose> poses, IReadOnlyList<bool> valid)
        {
            if (EnsureCreated() != Mode.Meta) return;
            var hand = _hands[side];
            tracked = tracked && valid[XRHandJointID.Wrist.ToIndex()];
            if (hand.Renderer.enabled != tracked) hand.Renderer.enabled = tracked;
            if (!tracked) return;
            PoseJoints(hand.Joints, poses, valid);
            var wrist = hand.Joints[XRHandJointID.Wrist.ToIndex()];
            _block ??= new MaterialPropertyBlock();
            hand.Renderer.GetPropertyBlock(_block);
            // The mesh wrist's +Z points away from the fingers: toward the elbow.
            _block.SetVector(WristPositionId, wrist.position);
            _block.SetVector(ArmDirectionId, wrist.forward);
            if (hand.Skin != null) _block.SetTexture(MainTexId, hand.Skin);
            hand.Renderer.SetPropertyBlock(_block);
        }

        /// <summary>
        /// Places each mesh joint on its XR Hands joint (index = <c>XRHandJointID.ToIndex()</c>). The ID order
        /// puts every parent before its children, so a child set later is not moved again by its parent.
        /// A joint without a valid pose keeps following its parent. The index distal joint alone is drawn
        /// <see cref="IndexDistalToTip"/> behind the tracked IndexTip along the last phalanx, so the drawn
        /// index fingertip ends at the poke point whatever the user's finger length (the poke itself is unchanged).
        /// </summary>
        internal static void PoseJoints(IReadOnlyList<Transform> joints, IReadOnlyList<Pose> poses, IReadOnlyList<bool> valid)
        {
            var tip = XRHandJointID.IndexTip.ToIndex();
            var distal = XRHandJointID.IndexDistal.ToIndex();
            for (var index = 0; index < joints.Count; index++)
            {
                var joint = joints[index];
                if (joint == null || !valid[index]) continue;
                var position = poses[index].position;
                if (index == distal && valid[tip])
                {
                    var along = poses[tip].position - position;
                    if (along.sqrMagnitude > 1e-10f) position = poses[tip].position - along.normalized * IndexDistalToTip;
                }
                joint.SetPositionAndRotation(position, poses[index].rotation * JointRotationOffset);
            }
        }

        internal bool IsHandVisible(int side) => _mode == Mode.Meta && _hands[side].Renderer.enabled;
        internal SkinnedMeshRenderer HandRenderer(int side) => _mode == Mode.Meta ? _hands[side].Renderer : null;
        internal Transform Joint(int side, XRHandJointID id) => _mode == Mode.Meta ? _hands[side].Joints[id.ToIndex()] : null;

        /// <summary>Loads Meta's mesh once; without it (or its shaders) adds the procedural ghost hands instead.</summary>
        private Mode EnsureCreated()
        {
            if (_mode != Mode.Pending) return _mode;
            if (TryCreateMeta()) _mode = Mode.Meta;
            else
            {
                _mode = Mode.Procedural;
                if (GetComponent<DemoGhostHands>() == null) gameObject.AddComponent<DemoGhostHands>();
            }
            return _mode;
        }

        private bool TryCreateMeta()
        {
            var left = Resources.Load<GameObject>(LeftModelPath);
            var right = Resources.Load<GameObject>(RightModelPath);
            if (left == null || right == null)
            {
                Debug.Log("[Demo Hands] Meta hand meshes are not in this project (private assets not linked); using the procedural ghost hands.");
                return false;
            }
            var ghost = Resources.Load<Shader>(GhostShaderPath);
            var skin = Resources.Load<Shader>(SkinShaderPath);
            var outline = Resources.Load<Shader>(OutlineShaderPath);
            if (!Supported(ghost) || !Supported(skin) || !Supported(outline))
            {
                Debug.LogWarning("[Demo Hands] Hand shaders under Resources/HapbeatDemoHands are missing or unsupported; using the procedural ghost hands.");
                return false;
            }
            _hands[0] = CreateHand(left, Resources.Load<Texture>(MetaLeftSkinPath));
            _hands[1] = _hands[0] != null ? CreateHand(right, Resources.Load<Texture>(MetaRightSkinPath)) : null;
            if (_hands[0] == null || _hands[1] == null)
            {
                foreach (var hand in _hands)
                    if (hand != null) Release(hand.Model);
                _hands[0] = _hands[1] = null;
                return false;
            }
            CreateMaterials(ghost, skin, outline);
            ApplyStyle();
            return true;
        }

        private static bool Supported(Shader shader) => shader != null && shader.isSupported;

        /// <summary>One model instance; null when it lacks a single-submesh skinned mesh or any XRHand_&lt;joint&gt;.</summary>
        private MetaHand CreateHand(GameObject prefab, Texture skin)
        {
            var model = Instantiate(prefab, transform, false);
            model.name = prefab.name;
            var renderer = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
            var byName = new Dictionary<string, Transform>();
            foreach (var child in model.GetComponentsInChildren<Transform>(true))
                if (!byName.ContainsKey(child.name)) byName.Add(child.name, child);
            var joints = new Transform[JointCount];
            var missing = new List<string>();
            for (var index = 0; index < JointCount; index++)
            {
                var id = XRHandJointIDUtility.FromIndex(index);
                if (!byName.TryGetValue(JointPrefix + id, out joints[index])) missing.Add(id.ToString());
            }
            if (renderer == null || renderer.sharedMesh == null || renderer.sharedMesh.subMeshCount != 1 || missing.Count != 0)
            {
                Debug.LogWarning("[Demo Hands] " + prefab.name + " is not usable (skinned mesh with one submesh: "
                    + (renderer != null && renderer.sharedMesh != null && renderer.sharedMesh.subMeshCount == 1 ? "ok" : "missing")
                    + ", missing joints: " + string.Join(",", missing) + ").");
                Release(model);
                return null;
            }
            renderer.updateWhenOffscreen = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.skinnedMotionVectors = false;
            // After the panels (whose depth layer then hides a hand behind them), see DemoSessionPanel.
            renderer.sortingOrder = DemoSessionPanel.HandSortingOrder;
            renderer.enabled = false;
            return new MetaHand { Model = model, Renderer = renderer, Joints = joints, Skin = skin };
        }

        /// <summary>
        /// One submesh drawn three times by queue: a depth pre-pass (2999), the fill (3000) and the outline
        /// (3001), all after the panels by the renderer's sorting order (<see cref="DemoSessionPanel.HandSortingOrder"/>).
        /// Values follow Energy Duel's hand materials (Safety Mill VR's Ghost / Skin looks).
        /// </summary>
        private void CreateMaterials(Shader ghost, Shader skin, Shader outline)
        {
            var depth = new Material(ghost) { name = "Demo Hand Depth", renderQueue = (int)RenderQueue.Transparent - 1 };
            depth.SetFloat("_ColorMask", 0f);
            depth.SetFloat("_ZWrite", 1f);
            var ghostFill = new Material(ghost) { name = "Demo Hand Ghost", renderQueue = (int)RenderQueue.Transparent };
            var ghostOutline = new Material(outline) { name = "Demo Hand Ghost Outline", renderQueue = (int)RenderQueue.Transparent + 1 };
            var skinFill = new Material(skin) { name = "Demo Hand Skin", renderQueue = (int)RenderQueue.Transparent };
            var skinOutline = new Material(outline) { name = "Demo Hand Skin Outline", renderQueue = (int)RenderQueue.Transparent + 1 };
            skinOutline.SetColor("_OutlineColor", new Color(0.58f, 0.44f, 0.37f, 1f));
            skinOutline.SetFloat("_OutlineWidth", 0.0007f);
            skinOutline.SetFloat("_OutlineOpacity", 1f);
            skinOutline.SetFloat("_FadeStart", -0.04f);
            skinOutline.SetFloat("_FadeLength", 0.03f);
            _ghostMaterials = new[] { depth, ghostFill, ghostOutline };
            _skinMaterials = new[] { depth, skinFill, skinOutline };
        }

        private void ApplyStyle()
        {
            var materials = Style == DemoHandStyle.Skin ? _skinMaterials : _ghostMaterials;
            foreach (var hand in _hands) hand.Renderer.sharedMaterials = materials;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            foreach (var hand in _hands)
                if (hand != null) Release(hand.Model);
            var released = new HashSet<Material>();
            foreach (var set in new[] { _ghostMaterials, _skinMaterials })
                if (set != null)
                    foreach (var material in set)
                        if (released.Add(material)) Release(material);
        }

        private static void Release(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
