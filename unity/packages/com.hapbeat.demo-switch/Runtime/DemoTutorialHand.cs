using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Hands;

namespace Hapbeat.DemoSwitch
{
    /// <summary>Which mesh a <see cref="DemoTutorialHand"/> shows: the host's public placeholder or Meta's mesh of the shared hands.</summary>
    public enum DemoTutorialHandModel { Placeholder, Meta }

    /// <summary>
    /// A posed demonstration hand with its forearm, for tutorials (opt-in: a demo adds it where it wants one; the player's
    /// own hands, <see cref="DemoHands"/>, are not changed). It is the hand the player sees: the shared hands' Meta mesh
    /// with their current look (Ghost / Skin, following <see cref="DemoHands.StyleChanged"/>) when they draw it, posed on
    /// its own skeleton as a tracked hand is (<see cref="PoseMetaHand"/>), else the host's public placeholder hand
    /// (<see cref="PlaceholderSkeleton"/>, whose joints follow the XR Hands axes). Each pose (<see cref="ApplyPose"/>) sets
    /// the wrist joint, the hand rotation and the hand shape (<see cref="HandShapes"/>: a relaxed open hand and a fist,
    /// blended joint by joint), and lays the forearm from the wrist toward the elbow: a flattened elliptic section running on
    /// from the hand's cuff (<see cref="ForearmMesh"/>), fading out toward the elbow into a closed, rounded end, in the
    /// hand's look. Just past the wrist the hand fades out and the forearm takes over (no seam, no rim): every pixel shows
    /// one surface, the nearest of the hand and the forearm. This transform's scale is <see cref="Scale"/>. Visual only: no
    /// colliders, no haptics, no sound. Each pose depends on its inputs alone (every joint's rest pose is restored first),
    /// so a frame never carries the previous frame's bend.
    /// </summary>
    public sealed class DemoTutorialHand : MonoBehaviour
    {
        public const string GhostShaderPath = "HapbeatDemoHands/TutorialHandGhost";
        public const string SkinShaderPath = "HapbeatDemoHands/TutorialHandSkin";
        public const string OutlineShaderPath = "HapbeatDemoHands/TutorialHandOutline";
        /// <summary>The outline's shader name (e.g. to draw a capture without it).</summary>
        public const string OutlineShaderName = "Hidden/Hapbeat/DemoTutorialHandOutline";
        /// <summary>The child that draws the forearm.</summary>
        public const string ForearmName = "Demo Forearm";

        private static readonly int WristPositionId = Shader.PropertyToID("_WristPosition");
        private static readonly int ArmDirectionId = Shader.PropertyToID("_ArmDirection");
        private static readonly int FadeStartId = Shader.PropertyToID("_FadeStart");
        private static readonly int FadeLengthId = Shader.PropertyToID("_FadeLength");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int ColorMaskId = Shader.PropertyToID("_ColorMask");
        private static readonly int DepthClipId = Shader.PropertyToID("_DepthClip");
        private static readonly int CullId = Shader.PropertyToID("_Cull");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");
        private static readonly int InStartId = Shader.PropertyToID("_InStart");
        private static readonly int InLengthId = Shader.PropertyToID("_InLength");
        private static readonly int JointCount = XRHandJointID.EndMarker.ToIndex();

        [Tooltip("The left hand (else the right).")]
        public bool Left;

        [Header("Placeholder (public; its joints follow the XR Hands axes, named *_<XRHandJointID>)")]
        [Tooltip("The posable skeleton until Meta's mesh is chosen, and the hand shown without it (public clone, shared hands off).")]
        public GameObject PlaceholderSkeleton;
        [Tooltip("The placeholder's look, used when the shared hands draw no Meta mesh.")]
        public Material[] PlaceholderMaterials = Array.Empty<Material>();

        [Header("Size")]
        [Tooltip("Hand and forearm relative to real size (this transform's scale, set when the hand is built).")]
        [Range(.5f, 1.2f)] public float Scale = 1f;

        [Header("Forearm (real size: from the wrist toward the elbow; it ends, closed and rounded, just past where its fade ends)")]
        [Tooltip("Draw the forearm (off: the hand alone, still ending just past the wrist).")]
        public bool ShowForearm = true;
        [Tooltip("Radii at the wrist joint: across the arm (thumb side to little-finger side), through it (back to palm). The cuff of Meta's mesh (DemoTutorialHandCuff in the editor).")]
        public Vector2 ForearmWristRadii = new Vector2(.026f, .018f);
        [Tooltip("Radii where the hand has faded out (CuffEnd past the wrist).")]
        public Vector2 ForearmCuffRadii = new Vector2(.026f, .017f);
        [Tooltip("Radii toward the elbow (the forearm's muscle, rounder than the flattened wrist), before the rounded end closes it.")]
        public Vector2 ForearmFarRadii = new Vector2(.034f, .027f);
        [Tooltip("The forearm fades out over this length (smoothstep) from the fade start given with each pose, fully transparent before its rounded end.")]
        [Min(.01f)] public float ForearmFadeLength = .08f;
        [Tooltip("Just past the wrist joint the hand fades out and the forearm, drawn after it, takes over (its outline fading in as the hand's fades out; no seam, no rim): from this far past the wrist ...")]
        public float HandFadeStart = .002f;
        [Tooltip("... over this length (to CuffEnd).")]
        [Min(.005f)] public float HandFadeLength = .008f;
        [Tooltip("Skin look: the place on the hand's skin texture the forearm takes its colour from (the mesh at the wrist).")]
        public Vector2 SkinUv = new Vector2(.5f, .5f);

        /// <summary>
        /// The two hand shapes as joint rotations (degrees, Euler) from the rest pose, the right hand's, in each joint's own
        /// XR Hands axes (+Z toward the fingertip, +Y out of the back of the hand): x flexes the joint toward the palm; y
        /// turns it sideways and z rolls it about the bone, both mirrored for the left hand (whose X runs the other way across
        /// the hand; flexion is the same). Open: relaxed, each finger a little curled (more toward the little finger), the
        /// thumb relaxed alongside the index finger. Fist: the fingers curled into the palm (knuckle, middle and end joints),
        /// the ring and little metacarpals cupped, the thumb brought across the palm and bent over the middle phalanges of the
        /// index and middle fingers. Joints not listed keep their rest pose.
        /// </summary>
        public static readonly (XRHandJointID joint, Vector3 open, Vector3 fist)[] HandShapes =
        {
            (XRHandJointID.ThumbMetacarpal, new Vector3(5f, 15f, 10f), new Vector3(15f, -30f, 20f)),
            (XRHandJointID.ThumbProximal, new Vector3(25f, 0f, 0f), new Vector3(60f, 0f, 0f)),
            (XRHandJointID.ThumbDistal, new Vector3(20f, 0f, 0f), new Vector3(40f, 0f, 0f)),
            (XRHandJointID.IndexProximal, new Vector3(8f, 0f, 0f), new Vector3(88f, 0f, 0f)),
            (XRHandJointID.IndexIntermediate, new Vector3(10f, 0f, 0f), new Vector3(100f, 0f, 0f)),
            (XRHandJointID.IndexDistal, new Vector3(5f, 0f, 0f), new Vector3(70f, 0f, 0f)),
            (XRHandJointID.MiddleProximal, new Vector3(10f, 0f, 0f), new Vector3(90f, 0f, 0f)),
            (XRHandJointID.MiddleIntermediate, new Vector3(13f, 0f, 0f), new Vector3(100f, 0f, 0f)),
            (XRHandJointID.MiddleDistal, new Vector3(6f, 0f, 0f), new Vector3(68f, 0f, 0f)),
            (XRHandJointID.RingMetacarpal, new Vector3(1f, 0f, 0f), new Vector3(5f, 0f, 0f)),
            (XRHandJointID.RingProximal, new Vector3(12f, 0f, 0f), new Vector3(93f, 0f, 0f)),
            (XRHandJointID.RingIntermediate, new Vector3(16f, 0f, 0f), new Vector3(98f, 0f, 0f)),
            (XRHandJointID.RingDistal, new Vector3(7f, 0f, 0f), new Vector3(65f, 0f, 0f)),
            (XRHandJointID.LittleMetacarpal, new Vector3(2f, 0f, 0f), new Vector3(10f, 0f, 0f)),
            (XRHandJointID.LittleProximal, new Vector3(14f, 0f, 0f), new Vector3(95f, 0f, 0f)),
            (XRHandJointID.LittleIntermediate, new Vector3(18f, 0f, 0f), new Vector3(95f, 0f, 0f)),
            (XRHandJointID.LittleDistal, new Vector3(8f, 0f, 0f), new Vector3(60f, 0f, 0f)),
        };

        /// <summary>
        /// The rotation of <see cref="HandShapes"/> entry <paramref name="shape"/> at <paramref name="curl"/> (0 open .. 1 fist), in
        /// the joint's XR Hands axes: the open and fist rotations slerped joint by joint (no twist through the palm).
        /// </summary>
        public static Quaternion ShapeRotation(int shape, float curl, bool left)
        {
            var (_, open, fist) = HandShapes[shape];
            return Quaternion.Slerp(Mirror(open, left), Mirror(fist, left), Mathf.Clamp01(curl));
        }

        private static Quaternion Mirror(Vector3 euler, bool left) => Quaternion.Euler(euler.x, left ? -euler.y : euler.y, left ? -euler.z : euler.z);

        /// <summary>
        /// Poses Meta's hand mesh on its own skeleton (its bones' lengths, as the shared hands draw it when tracked):
        /// <paramref name="joints"/> (Meta's XRHand_* bones by <c>XRHandJointID.ToIndex()</c>) back to their rest local poses
        /// <paramref name="rest"/> (position and rotation), each <see cref="HandShapes"/> joint turned by its
        /// <see cref="ShapeRotation"/>, the wrist joint on <paramref name="wristPosition"/> turned <paramref name="hand"/>
        /// (world, XR Hands axes: +Z toward the fingers, +Y the back of the hand). The wrist bone, with the mesh's cuff, turns
        /// with the palm, as on a tracked hand: the mesh is never bent between its wrist bone and the palm, which its skin
        /// weights do not follow (bent back, the palm's heel came out through the back of the hand, more so with a build's
        /// two bones per vertex). A bent wrist is drawn between the hand, cut just past the wrist, and the forearm. Restoring
        /// positions as well as rotations makes the pose depend on its inputs alone (a bend is never applied twice).
        /// </summary>
        public static void PoseMetaHand(IReadOnlyList<Transform> joints, IReadOnlyList<Pose> rest, bool left, float curl,
            Vector3 wristPosition, Quaternion hand)
        {
            for (var i = 0; i < joints.Count; i++)
                if (joints[i] != null) joints[i].SetLocalPositionAndRotation(rest[i].position, rest[i].rotation);
            for (var s = 0; s < HandShapes.Length; s++)
            {
                var joint = joints[HandShapes[s].joint.ToIndex()];
                // Meta's bones face the other way along the bone (DemoHands.JointRotationOffset, a half turn about Y): the
                // same turn in the joint's XR Hands axes is that offset's conjugate.
                if (joint != null) joint.localRotation *= DemoHands.JointRotationOffset * ShapeRotation(s, curl, left) * DemoHands.JointRotationOffset;
            }

            joints[XRHandJointID.Wrist.ToIndex()].SetPositionAndRotation(wristPosition, hand * DemoHands.JointRotationOffset);
        }

        /// <summary>From a hand's rotation to its forearm's with the wrist straight (+Z toward the elbow), see <see cref="StraightArm"/>.</summary>
        private static readonly Quaternion HandToArm = Quaternion.Euler(0f, 180f, 0f);

        /// <summary>The forearm of a straight wrist with the hand turned <paramref name="hand"/>: +Z toward the elbow, +Y the back of the arm.</summary>
        public static Quaternion StraightArm(Quaternion hand) => hand * HandToArm;

        /// <summary>
        /// The forearm's passes are queued this far from the hand's same passes: its depth pre-pass before the hand's (the
        /// hand's fading cuff and its inside, behind the forearm's surface, stay hidden), its fill and outline after the
        /// hand's outline (they show where the cuff fades).
        /// </summary>
        public const int ForearmQueueOffset = 3;

        /// <summary>
        /// The hand is drawn only where it is opaque: its depth pre-pass, fill and outline all stop at this fade, just past
        /// the wrist, where the forearm (its depth pre-pass written before the hand's) takes over. Past it the hand's
        /// fading cuff draws nothing: drawn over the forearm without depth, it showed its far side (the palm heel, the thumb's
        /// base) and the forearm through itself, a second layer. So every pixel shows one surface, the nearest of the hand
        /// and the forearm. The forearm's pre-pass covers all of its visible surface (only its fully faded tip writes none,
        /// so it hides nothing): its fill, front faces only, then blends the nearest surface alone and never shows the
        /// arm's far side through the fade. Its outline fades out by the same mark.
        /// </summary>
        private const float HandDepthClip = .99f;
        private const float ForearmDepthClip = .02f;

        private bool _built;
        private GameObject _skeleton;
        /// <summary>The posed joints by XR Hands joint: the placeholder's, or once Meta's mesh is chosen, its bones.</summary>
        private Transform[] _joints;
        private Quaternion[] _rest;
        private Renderer _placeholder;
        /// <summary>Meta's mesh bones by XR Hands joint (null: the placeholder is shown); once set, also <see cref="_joints"/>.</summary>
        private Transform[] _metaJoints;
        /// <summary>Their rest (bind) local poses.</summary>
        private Pose[] _metaRest;
        private Renderer _mesh;
        private Texture _skinTexture;
        private bool _lookResolved;
        /// <summary>The forearm has the hand's look (it is drawn when <see cref="ShowForearm"/>).</summary>
        private bool _forearmDressed;
        private Transform _forearm;
        private MeshRenderer _forearmRenderer;
        private Mesh _forearmMesh;
        /// <summary>The length <see cref="_forearmMesh"/> was built for (it ends where the current fade ends).</summary>
        private float _forearmMeshLength;
        private Vector3 _forearmWrist;
        private Vector3 _forearmDirection;
        private float _forearmFadeStart;
        private MaterialPropertyBlock _block;
        private DemoHands _shared;
        private Shader _ghostShader;
        private Shader _skinShader;
        private Shader _outlineShader;
        /// <summary>This hand's copies of the shared hands' materials, for the hand and for the forearm (their shaders, the same values).</summary>
        private readonly Dictionary<(Material source, bool forearm), Material> _looks = new Dictionary<(Material, bool), Material>();

        /// <summary>Which mesh is shown (diagnostics; the placeholder until Meta's mesh is chosen).</summary>
        public DemoTutorialHandModel Model { get; private set; }
        /// <summary>True once the look is chosen (the shared hands resolved, or there are none).</summary>
        public bool IsLookResolved => _lookResolved;
        /// <summary>The forearm is drawn (diagnostics).</summary>
        public bool ForearmShown => _forearmRenderer != null && _forearmRenderer.enabled && gameObject.activeInHierarchy;
        /// <summary>Where the hand has faded out past the wrist (real size): the forearm alone from there.</summary>
        public float CuffEnd => HandFadeStart + HandFadeLength;

        /// <summary>A posed joint (world pose after <see cref="ApplyPose"/>): Meta's bone, or the placeholder's joint. Null before the hand is built.</summary>
        public Transform Joint(XRHandJointID id) => _joints != null ? _joints[id.ToIndex()] : null;

        /// <summary>
        /// The forearm as last placed (diagnostics, world): from the wrist joint toward the elbow, opaque up to
        /// <paramref name="fadeStart"/> past the wrist, gone at <paramref name="fadeEnd"/> (metres as drawn).
        /// </summary>
        public bool TryGetForearm(out Vector3 wrist, out Vector3 towardElbow, out float fadeStart, out float fadeEnd)
        {
            wrist = _forearmWrist;
            towardElbow = _forearmDirection;
            fadeStart = _forearmFadeStart * Scale;
            fadeEnd = (_forearmFadeStart + ForearmFadeLength) * Scale;
            return ForearmShown;
        }

        /// <summary>
        /// Poses the hand (world): the wrist joint on <paramref name="wristPosition"/>, the hand turned <paramref name="hand"/>
        /// (XR Hands axes: +Z toward the fingers, +Y the back of the hand), the fingers curled by <paramref name="curl"/>
        /// (0 open .. 1 fist), and the forearm from the wrist along <paramref name="arm"/> (+Z toward the elbow, +Y the back;
        /// <see cref="StraightArm"/> for a straight wrist), fading out from <paramref name="forearmFadeStart"/> (real size)
        /// past the wrist over <see cref="ForearmFadeLength"/>. Builds the hand on first use and picks its look once the
        /// shared hands have picked theirs (nothing is drawn before that). Returns false when the hand cannot be built (no
        /// placeholder skeleton).
        /// </summary>
        public bool ApplyPose(Vector3 wristPosition, Quaternion hand, float curl, Quaternion arm, float forearmFadeStart)
        {
            if (!EnsureBuilt()) return false;
            ResolveLook();
            var wristJoint = _joints[XRHandJointID.Wrist.ToIndex()];
            if (_metaJoints != null)
            {
                // Meta's mesh on its own skeleton, as the player's tracked hand is drawn, the cuff turning with the palm. When
                // the hand bends back at the wrist the cuff, past where the hand is cut, lies inside the forearm.
                PoseMetaHand(_metaJoints, _metaRest, Left, curl, wristPosition, hand);
            }
            else
            {
                for (var i = 0; i < _joints.Length; i++)
                    if (_joints[i] != null) _joints[i].localRotation = _rest[i];
                for (var s = 0; s < HandShapes.Length; s++)
                {
                    var t = _joints[HandShapes[s].joint.ToIndex()];
                    if (t != null) t.localRotation *= ShapeRotation(s, curl, Left);
                }

                // Move the whole skeleton so its wrist joint lands on the pose.
                var skeleton = _skeleton.transform;
                skeleton.rotation = hand * Quaternion.Inverse(wristJoint.rotation) * skeleton.rotation;
                skeleton.position += wristPosition - wristJoint.position;
            }

            if (_metaJoints != null)
            {
                var metaWrist = _metaJoints[XRHandJointID.Wrist.ToIndex()];
                _mesh.GetPropertyBlock(_block);
                // The mesh wrist's +Z points away from the fingers: toward the elbow (the look fades past it).
                _block.SetVector(WristPositionId, metaWrist.position);
                _block.SetVector(ArmDirectionId, metaWrist.forward);
                // Opaque up to where the mesh ends, where the forearm takes over.
                _block.SetFloat(FadeStartId, HandFadeStart * Scale);
                _block.SetFloat(FadeLengthId, HandFadeLength * Scale);
                // Its outline from the fingertips on (the forearm's fades in past the wrist).
                _block.SetFloat(InStartId, -1f);
                if (_skinTexture != null) _block.SetTexture(MainTexId, _skinTexture);
                _mesh.SetPropertyBlock(_block);
            }

            PlaceForearm(wristJoint.position, arm, forearmFadeStart);
            return true;
        }

        /// <summary>
        /// The forearm from the wrist joint toward the elbow, fading out from <paramref name="fadeStart"/> past the wrist over
        /// <see cref="ForearmFadeLength"/>, its rounded end just past where the fade ends, in the hand's look.
        /// </summary>
        private void PlaceForearm(Vector3 wristPosition, Quaternion arm, float fadeStart)
        {
            if (_forearmDressed && _forearmRenderer.enabled != ShowForearm) _forearmRenderer.enabled = ShowForearm;
            var length = fadeStart + ForearmFadeLength;
            if (!Mathf.Approximately(_forearmMeshLength, length))
            {
                Release(_forearmMesh);
                _forearmMesh = ForearmMesh(length, ForearmWristRadii, ForearmCuffRadii, CuffEnd, ForearmFarRadii, SkinUv);
                _forearmMeshLength = length;
                _forearm.GetComponent<MeshFilter>().sharedMesh = _forearmMesh;
            }

            _forearm.SetPositionAndRotation(wristPosition, arm);
            _forearmWrist = wristPosition;
            _forearmDirection = _forearm.forward;
            _forearmFadeStart = fadeStart;
            _forearmRenderer.GetPropertyBlock(_block);
            _block.SetVector(WristPositionId, wristPosition);
            _block.SetVector(ArmDirectionId, _forearm.forward);
            _block.SetFloat(FadeStartId, fadeStart * Scale);
            _block.SetFloat(FadeLengthId, ForearmFadeLength * Scale);
            // Its outline is in by where the hand's goes (the hand's outline stops where its cuff starts to fade); before
            // that it lies under the opaque hand.
            _block.SetFloat(InStartId, (HandFadeStart - HandFadeLength) * Scale);
            _block.SetFloat(InLengthId, HandFadeLength * Scale);
            if (_skinTexture != null) _block.SetTexture(MainTexId, _skinTexture);
            _forearmRenderer.SetPropertyBlock(_block);
        }

        /// <summary>
        /// A forearm along +Z from a little inside the hand (z below 0) to <paramref name="length"/>, closed at both ends.
        /// Its section is an ellipse (x across the arm, y through it): <paramref name="wristRadii"/> at the wrist joint
        /// (inside the hand's cuff), the cuff's <paramref name="cuffRadii"/> where the hand's mesh ends
        /// (<paramref name="cuffEnd"/>), then easing into the rounder <paramref name="farRadii"/>; past <paramref name="length"/>
        /// (where the fade ends) an elliptical cap closes it to a point (no cut end). The cap lies wholly where the fills are
        /// fully transparent: inside the fade the Skin fill (alpha = the fade, no silhouette falloff) would still show it as a
        /// darker rounded stub over a dark background. Every vertex has the uv <paramref name="uv"/> (the skin colour at the
        /// wrist). Front faces outside.
        /// </summary>
        public static Mesh ForearmMesh(float length, Vector2 wristRadii, Vector2 cuffRadii, float cuffEnd, Vector2 farRadii, Vector2 uv)
        {
            const int around = 20;
            const int bodyRings = 14;
            const int capRings = 7;
            const float into = .012f;
            cuffEnd = Mathf.Clamp(cuffEnd, .005f, length * .4f);
            // The rounded end past the fade's end, fully transparent (it closes the mesh, no cut end).
            var cap = Mathf.Min(.025f, (length - cuffEnd) * .45f);
            var capFrom = length;

            Vector2 Radii(float z)
            {
                if (z <= 0f) return wristRadii;
                if (z <= cuffEnd) return Vector2.Lerp(wristRadii, cuffRadii, Ease(z / cuffEnd));
                return Vector2.Lerp(cuffRadii, farRadii, Ease(Mathf.Clamp01((z - cuffEnd) / (length - cuffEnd))));
            }

            // Ring stations along the arm: the body evenly, then the cap by angle (denser toward its tip).
            var stations = new List<(float z, float scale)>();
            for (var r = 0; r <= bodyRings; r++) stations.Add((Mathf.Lerp(-into, capFrom, r / (float)bodyRings), 1f));
            for (var r = 1; r < capRings; r++)
            {
                var angle = r / (float)capRings * (Mathf.PI * .5f);
                stations.Add((capFrom + (cap * Mathf.Sin(angle)), Mathf.Cos(angle)));
            }

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            foreach (var (z, scale) in stations)
            {
                var radii = Radii(z) * scale;
                for (var a = 0; a < around; a++)
                {
                    var angle = a * (2f * Mathf.PI / around);
                    vertices.Add(new Vector3(Mathf.Cos(angle) * radii.x, Mathf.Sin(angle) * radii.y, z));
                }
            }

            var rings = stations.Count;
            for (var r = 0; r < rings - 1; r++)
            {
                for (var a = 0; a < around; a++)
                {
                    var i0 = (r * around) + a;
                    var i1 = (r * around) + ((a + 1) % around);
                    var j0 = i0 + around;
                    var j1 = i1 + around;
                    // Outward-facing (clockwise seen from outside): the fills draw front faces only, the outline the silhouette.
                    triangles.AddRange(new[] { i0, i1, j0, i1, j1, j0 });
                }
            }

            // The rounded end's tip.
            var tip = vertices.Count;
            vertices.Add(new Vector3(0f, 0f, capFrom + cap));
            var last = (rings - 1) * around;
            for (var a = 0; a < around; a++) triangles.AddRange(new[] { last + a, last + ((a + 1) % around), tip });

            // The wrist cap, inside the hand, on vertices of its own: shared with the body, its normals would tilt the body's
            // first ring toward the hand, lighting it as a pale band just where the hand hands over to the forearm.
            var rim = vertices.Count;
            for (var a = 0; a < around; a++) vertices.Add(vertices[a]);
            var centre = vertices.Count;
            vertices.Add(new Vector3(0f, 0f, -into));
            for (var a = 0; a < around; a++) triangles.AddRange(new[] { centre, rim + ((a + 1) % around), rim + a });

            var mesh = new Mesh { name = "Demo Forearm" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, Enumerable.Repeat(uv, vertices.Count).ToList());
            mesh.SetTriangles(triangles, 0);
            // Smooth normals across the body and the rounded end (the ring seam shares its vertices).
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float Ease(float x) => x * x * (3f - (2f * x));

        // ------------------------------------------------------------ building

        /// <summary>
        /// Builds the hand once (also done by <see cref="ApplyPose"/>): the placeholder skeleton under this transform (scaled
        /// to <see cref="Scale"/>) as the posable hand, hidden until the look is decided (<see cref="ResolveLook"/>), and the
        /// forearm (its mesh in real size, built when it is placed). Returns false when the hand cannot be posed (no
        /// placeholder skeleton, or one without a wrist or palm joint).
        /// </summary>
        public bool EnsureBuilt()
        {
            if (_built) return _joints != null;
            _built = true;
            _block = new MaterialPropertyBlock();
            if (PlaceholderSkeleton == null)
            {
                Debug.LogWarning($"[Demo Tutorial Hand] {name}: no placeholder skeleton prefab; the hand is not shown.", this);
                return false;
            }

            transform.localScale = Vector3.one * Scale;
            _ghostShader = Resources.Load<Shader>(GhostShaderPath);
            _skinShader = Resources.Load<Shader>(SkinShaderPath);
            _outlineShader = Resources.Load<Shader>(OutlineShaderPath);
            var skeleton = Instantiate(PlaceholderSkeleton, transform, false);
            skeleton.name = PlaceholderSkeleton.name;
            Quiet(skeleton);
            var all = skeleton.GetComponentsInChildren<Transform>(true);
            var joints = new Transform[JointCount];
            var rest = new Quaternion[JointCount];
            for (var i = 0; i < JointCount; i++)
            {
                var suffix = "_" + XRHandJointIDUtility.FromIndex(i);
                joints[i] = all.FirstOrDefault(t => t.name.EndsWith(suffix, StringComparison.Ordinal));
                rest[i] = joints[i] != null ? joints[i].localRotation : Quaternion.identity;
            }

            if (joints[XRHandJointID.Wrist.ToIndex()] == null || joints[XRHandJointID.Palm.ToIndex()] == null)
            {
                Debug.LogError($"[Demo Tutorial Hand] {name}: {PlaceholderSkeleton.name} has no *_Wrist / *_Palm joint.", this);
                Release(skeleton);
                return false;
            }

            var placeholderMesh = skeleton.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (placeholderMesh != null)
            {
                if (PlaceholderMaterials != null && PlaceholderMaterials.Length > 0) placeholderMesh.sharedMaterials = PlaceholderMaterials;
                placeholderMesh.updateWhenOffscreen = true;
                placeholderMesh.shadowCastingMode = ShadowCastingMode.Off;
                // Hidden until the look is decided (the shared hands decide on their first frame).
                placeholderMesh.enabled = false;
            }

            var forearm = new GameObject(ForearmName, typeof(MeshFilter), typeof(MeshRenderer));
            forearm.transform.SetParent(transform, false);
            var forearmRenderer = forearm.GetComponent<MeshRenderer>();
            forearmRenderer.shadowCastingMode = ShadowCastingMode.Off;
            forearmRenderer.receiveShadows = false;
            forearmRenderer.lightProbeUsage = LightProbeUsage.Off;
            forearmRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            forearmRenderer.enabled = false;

            _skeleton = skeleton;
            _joints = joints;
            _rest = rest;
            _placeholder = placeholderMesh;
            _mesh = placeholderMesh;
            _forearm = forearm.transform;
            _forearmRenderer = forearmRenderer;
            return true;
        }

        /// <summary>
        /// The same mesh and look as the player's own hands: with the shared hands drawing Meta's mesh
        /// (<see cref="DemoHands.CurrentMaterials"/> non-null), a copy of that mesh with their looks (<see cref="Looks"/>),
        /// following a style change (<see cref="DemoHands.StyleChanged"/>); with their procedural fallback, or without
        /// shared hands, the placeholder. While running shared hands have not resolved yet, nothing is shown. The forearm
        /// takes the same looks.
        /// </summary>
        private void ResolveLook()
        {
            if (_lookResolved) return;
            var shared = DemoHands.Instance;
            if (shared != null && shared.isActiveAndEnabled && !shared.IsResolved) return;

            var materials = shared != null ? shared.CurrentMaterials : null;
            if (materials != null && TryMeta(shared, materials))
            {
                DressForearm(materials);
                return;
            }

            _lookResolved = true;
            if (_placeholder != null)
            {
                _placeholder.enabled = true;
                DressForearm(_placeholder.sharedMaterials);
            }

            Model = DemoTutorialHandModel.Placeholder;
        }

        private void DressForearm(IReadOnlyList<Material> materials)
        {
            _forearmRenderer.sharedMaterials = Looks(materials, true);
            _forearmRenderer.sortingOrder = _mesh != null ? _mesh.sortingOrder : 0;
            _forearmRenderer.enabled = ShowForearm;
            _forearmDressed = true;
        }

        private bool TryMeta(DemoHands shared, IReadOnlyList<Material> materials)
        {
            var prefab = Resources.Load<GameObject>(Left ? DemoHands.MetaLeftModelPath : DemoHands.MetaRightModelPath);
            if (prefab == null) return false;
            var meta = Instantiate(prefab, transform, false);
            meta.name = prefab.name;
            Quiet(meta);
            var byName = meta.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var joints = new Transform[JointCount];
            for (var i = 0; i < joints.Length; i++) byName.TryGetValue(DemoHands.JointPrefix + XRHandJointIDUtility.FromIndex(i), out joints[i]);
            var mesh = meta.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (mesh == null || joints[XRHandJointID.Wrist.ToIndex()] == null)
            {
                Release(meta);
                return false;
            }

            mesh.sharedMaterials = Looks(materials, false);
            mesh.sortingOrder = DemoSessionPanel.HandSortingOrder;
            // Posed on its own skeleton from here on: the placeholder skeleton is no longer used.
            _skeleton.SetActive(false);
            _joints = joints;
            _metaRest = joints.Select(j => j != null ? new Pose(j.localPosition, j.localRotation) : Pose.identity).ToArray();
            mesh.updateWhenOffscreen = true;
            mesh.shadowCastingMode = ShadowCastingMode.Off;
            mesh.receiveShadows = false;
            _metaJoints = joints;
            _mesh = mesh;
            _skinTexture = Resources.Load<Texture>(Left ? DemoHands.MetaLeftSkinPath : DemoHands.MetaRightSkinPath);
            _lookResolved = true;
            Model = DemoTutorialHandModel.Meta;
            if (_shared != shared)
            {
                if (_shared != null) _shared.StyleChanged -= OnSharedStyleChanged;
                _shared = shared;
                _shared.StyleChanged += OnSharedStyleChanged;
            }

            return true;
        }

        /// <summary>The shared hands switched between Ghost and Skin: the hand (and its forearm) follows.</summary>
        private void OnSharedStyleChanged(DemoHandStyle style)
        {
            var materials = _shared != null ? _shared.CurrentMaterials : null;
            if (materials == null || _metaJoints == null) return;
            var copy = materials.ToArray();
            _mesh.sharedMaterials = Looks(copy, false);
            _forearmRenderer.sharedMaterials = Looks(copy, true);
        }

        /// <summary>
        /// This hand's materials for the shared hands' <paramref name="materials"/>. For the hand: each look (depth
        /// pre-pass, fill, outline) as this hand's copy with the shared values (the look of the player's hands),
        /// culling off, all three drawn only where the hand is opaque (<see cref="HandDepthClip"/>): one surface per pixel,
        /// the forearm past the wrist, never the cuff's far side or the forearm through a fading cuff. For the
        /// <paramref name="forearm"/>: each DemoHands look (depth pre-pass, Ghost or Skin fill, outline) as the same look on
        /// the tutorial shader with the shared material's values: back faces culled, the depth pre-pass over all of
        /// its visible surface, queued after the hand. A material of another shader (the placeholder's) is used as it is.
        /// </summary>
        private Material[] Looks(IReadOnlyList<Material> materials, bool forearm)
        {
            var result = new Material[materials.Count];
            for (var i = 0; i < result.Length; i++)
            {
                var source = materials[i];
                if (source != null && _looks.TryGetValue((source, forearm), out var cached))
                {
                    result[i] = cached;
                    continue;
                }

                result[i] = Look(source, forearm);
                if (source != null && result[i] != source) _looks[(source, forearm)] = result[i];
            }

            return result;
        }

        private Material Look(Material source, bool forearm)
        {
            if (source == null || source.shader == null) return source;
            var shader = source.shader.name switch
            {
                "Hidden/Hapbeat/DemoHandGhost" => _ghostShader,
                "Hidden/Hapbeat/DemoHandSkin" => _skinShader,
                "Hidden/Hapbeat/DemoHandOutline" => _outlineShader,
                _ => null,
            };
            if (shader == null) return source;
            // The depth pre-pass: no colour, depth written (DemoHands sets both on that material only).
            var depthOnly = source.HasProperty(ColorMaskId) && source.GetFloat(ColorMaskId) == 0f && source.GetFloat(ZWriteId) == 1f;
            var outline = shader == _outlineShader;
            var material = new Material(shader) { name = source.name + (forearm ? " (Forearm)" : " (Demo)") };
            material.CopyPropertiesFromMaterial(source);
            material.renderQueue = source.renderQueue + (!forearm ? 0 : depthOnly ? -ForearmQueueOffset : ForearmQueueOffset);
            // Set on every copy: a copied material holds only the source's values, so these shader-only properties are not
            // yet in it (the forearm's fills ignore the clip mark, the outline the culling).
            material.SetFloat(CullId, (float)(forearm ? CullMode.Back : CullMode.Off));
            material.SetFloat(DepthClipId, !forearm ? HandDepthClip : !depthOnly && !outline ? 0f : ForearmDepthClip);
            return material;
        }

        /// <summary>A display copy: no colliders, no scripts of its own.</summary>
        private static void Quiet(GameObject model)
        {
            foreach (var collider in model.GetComponentsInChildren<Collider>(true)) Release(collider);
            foreach (var behaviour in model.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
            foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
        }

        private void OnDestroy()
        {
            if (_shared != null) _shared.StyleChanged -= OnSharedStyleChanged;
            _shared = null;
            // The forearm mesh and the look copies are not assets: destroyed with the hand.
            Release(_forearmMesh);
            foreach (var material in _looks.Values) Release(material);
            _looks.Clear();
        }

        private static void Release(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}
