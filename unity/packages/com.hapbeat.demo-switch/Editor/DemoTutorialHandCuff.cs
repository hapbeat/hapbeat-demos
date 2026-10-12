using System.Linq;
using UnityEngine;
using UnityEngine.XR.Hands;

namespace Hapbeat.DemoSwitch.Editor
{
    /// <summary>
    /// Meta's private OpenXR hand mesh posed and measured without a headset, for a <see cref="DemoTutorialHand"/>'s forearm:
    /// the hand posed as the tutorial hand poses it (<see cref="PoseMeta"/>) and the cuff of the mesh past the wrist
    /// (<see cref="Measure"/>), where the forearm takes over. Read in the editor, where the mesh data is available (not
    /// readable at runtime); a host's scene setup copies the result into the component's serialized fields.
    /// </summary>
    public static class DemoTutorialHandCuff
    {
        /// <summary>Meta's hand meshes are in this project (the private assets are linked).</summary>
        public static bool MetaPresent =>
            Resources.Load<GameObject>(DemoHands.MetaLeftModelPath) != null && Resources.Load<GameObject>(DemoHands.MetaRightModelPath) != null;

        /// <summary>
        /// Meta's hand posed as the tutorial hand poses it (<see cref="DemoTutorialHand.PoseMetaHand"/>: on its own skeleton,
        /// the hand shape at <paramref name="curl"/>, 0 open .. 1 fist), the wrist joint at the origin in XR Hands axes
        /// (+Z toward the fingers, +Y out of the back of the hand), the wrist straight. Returns the instance (the caller
        /// destroys it), its renderer and its bones by <c>XRHandJointID.ToIndex()</c>.
        /// </summary>
        public static (GameObject instance, SkinnedMeshRenderer mesh, Transform[] joints) PoseMeta(bool left, float curl)
        {
            var meta = Object.Instantiate(Resources.Load<GameObject>(left ? DemoHands.MetaLeftModelPath : DemoHands.MetaRightModelPath));
            var byName = meta.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var joints = new Transform[XRHandJointID.EndMarker.ToIndex()];
            for (var i = 0; i < joints.Length; i++) byName.TryGetValue(DemoHands.JointPrefix + XRHandJointIDUtility.FromIndex(i), out joints[i]);
            var rest = joints.Select(j => j != null ? new Pose(j.localPosition, j.localRotation) : Pose.identity).ToArray();
            DemoTutorialHand.PoseMetaHand(joints, rest, left, curl, Vector3.zero, Quaternion.identity);
            return (meta, meta.GetComponentInChildren<SkinnedMeshRenderer>(true), joints);
        }

        /// <summary>
        /// The cuff of Meta's mesh past the wrist (the band <paramref name="from"/>..<paramref name="to"/> m past the wrist
        /// joint toward the elbow, the open hand posed as the tutorial hand poses it), where the forearm takes over:
        /// <c>uv</c>, the skin texture coordinate of the band vertex whose colour is nearest the band's mean colour (the
        /// forearm's Skin-look colour, <see cref="DemoTutorialHand.SkinUv"/>; the farthest vertex if the skin texture is
        /// missing); <c>radii</c>, the band's half extents across the arm (x) and through it (y) in the wrist joint's frame;
        /// <c>centre</c>, the band's middle in that frame (the forearm is centred on the joint); <c>end</c>, how far past the
        /// wrist joint the mesh ends.
        /// </summary>
        public static (Vector2 uv, Vector2 radii, Vector2 centre, float end) Measure(bool left, float from, float to)
        {
            var (instance, mesh, joints) = PoseMeta(left, 0f);
            var wrist = joints[XRHandJointID.Wrist.ToIndex()];
            var baked = new Mesh();
            Texture2D readable = null;
            try
            {
                mesh.BakeMesh(baked, true);
                var vertices = baked.vertices;
                var uvs = baked.uv;
                if (uvs.Length != vertices.Length || vertices.Length == 0) return (new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, 0f);
                // The mesh wrist's +Z points toward the elbow, +Y out of the back of the hand.
                var local = vertices.Select(v => wrist.InverseTransformDirection(mesh.transform.TransformPoint(v) - wrist.position)).ToArray();
                var farthest = local.Max(v => v.z);
                var cuff = Enumerable.Range(0, local.Length).Where(i => local[i].z >= from && local[i].z <= to).ToArray();
                if (cuff.Length == 0) return (new Vector2(.5f, .5f), Vector2.zero, Vector2.zero, farthest);
                var min = new Vector2(cuff.Min(i => local[i].x), cuff.Min(i => local[i].y));
                var max = new Vector2(cuff.Max(i => local[i].x), cuff.Max(i => local[i].y));
                var last = cuff.OrderByDescending(i => local[i].z).First();

                var skin = Resources.Load<Texture>(left ? DemoHands.MetaLeftSkinPath : DemoHands.MetaRightSkinPath);
                var uv = uvs[last];
                if (skin != null)
                {
                    readable = Readable(skin);
                    var colors = cuff.Select(i => readable.GetPixelBilinear(uvs[i].x, uvs[i].y)).ToArray();
                    var mean = new Color(colors.Average(c => c.r), colors.Average(c => c.g), colors.Average(c => c.b));
                    var nearest = Enumerable.Range(0, cuff.Length).OrderBy(k => Distance(colors[k], mean)).First();
                    uv = uvs[cuff[nearest]];
                }

                return (uv, (max - min) * .5f, (max + min) * .5f, farthest);
            }
            finally
            {
                Object.DestroyImmediate(baked);
                Object.DestroyImmediate(instance);
                if (readable != null) Object.DestroyImmediate(readable);
            }
        }

        private static float Distance(Color a, Color b) => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b);

        /// <summary>A readable copy of <paramref name="texture"/> (read through a render texture); the caller destroys it.</summary>
        private static Texture2D Readable(Texture texture)
        {
            var rt = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(texture, rt);
                RenderTexture.active = rt;
                var readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                readable.Apply();
                return readable;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
