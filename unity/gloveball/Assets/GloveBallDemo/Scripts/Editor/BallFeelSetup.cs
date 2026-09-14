using System.IO;
using GloveBallDemo.Runtime;
using UnityEditor;
using UnityEngine;

namespace GloveBallDemo.Editor
{
    /// <summary>Adds ball tuning assets. Never opens or saves any scene or EventMap.</summary>
    public static class BallFeelSetup
    {
        private const string Audio = "Assets/GloveBallDemo/Audio/BallImpacts";
        private const string Art = "Assets/GloveBallDemo/Art/Balls";
        [MenuItem("Hapbeat/Development/Install Ball Feel Assets")]
        public static void Install()
        {
            EditorUtility.audioMasterMute = true;
            AssetDatabase.Refresh();
            const string path = "Assets/Resources/BallFeelSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<BallFeelSettings>(path);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<BallFeelSettings>();
                var masses = new[] { 1.2f, .27f, .06f, .62f, .04f };
                var damping = new[] { .02f, .08f, .65f, .04f, .4f };
                var bounce = new[] { .25f, .65f, .22f, .8f, .48f };
                settings.Balls = new BallFeel[5];
                for (int i = 0; i < 5; i++)
                {
                    var kind = (BallKind)i;
                    var materialPath = $"{Art}/{kind}Physics.physicMaterial";
                    var physics = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(materialPath);
                    if (physics == null)
                    {
                        physics = new PhysicsMaterial(kind + "Physics") { bounciness = bounce[i], dynamicFriction = .4f, staticFriction = .4f, bounceCombine = PhysicsMaterialCombine.Average };
                        AssetDatabase.CreateAsset(physics, materialPath);
                    }
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{Audio}/{kind}Impact.wav");
                    if (clip == null) throw new System.InvalidOperationException($"Missing impact audio: {kind}");
                    settings.Balls[i] = new BallFeel { Kind = kind, Mass = masses[i], AirResistance = damping[i], BounceMaterial = physics, ImpactClip = clip, ImpactVolume = i == 2 ? .55f : .7f };
                }
                AssetDatabase.CreateAsset(settings, path);
            }
            CreateFoamSurface();
            AssetDatabase.SaveAssets();
            Debug.Log("[BallFeelSetup] Five ball profiles and porous foam material installed; no scene/EventMap changes.");
        }

        private static float Surface(float u, float v)
        {
            // Tiled cellular pores with softer fine grain. Visible at hand-held size in a headset.
            float x = u * 54, y = v * 27;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            float nearest = 2;
            for (int oy = -1; oy <= 1; oy++) for (int ox = -1; ox <= 1; ox++)
            {
                int ix = (cx + ox + 54) % 54, iy = (cy + oy + 27) % 27;
                float rx = Mathf.Repeat(Mathf.Sin(ix * 127.1f + iy * 311.7f) * 43758.5453f, 1);
                float ry = Mathf.Repeat(Mathf.Sin(ix * 269.5f + iy * 183.3f) * 43758.5453f, 1);
                nearest = Mathf.Min(nearest, Vector2.Distance(new Vector2(x,y), new Vector2(cx+ox+rx,cy+oy+ry)));
            }
            return Mathf.SmoothStep(.12f, 1f, Mathf.Clamp01(nearest / .34f));
        }

        private static void CreateFoamSurface()
        {
            const int size = 512;
            var color = new Texture2D(size,size,TextureFormat.RGB24,false);
            var normal = new Texture2D(size,size,TextureFormat.RGB24,false,true);
            try
            {
                for (int y=0;y<size;y++) for(int x=0;x<size;x++)
                {
                    float u=(float)x/size,v=(float)y/size;
                    float h=Surface(u,v);
                    color.SetPixel(x,y,Color.Lerp(new Color(.46f,.24f,.1f), new Color(1f,.85f,.62f),h));
                    float dx=Surface(u+1f/size,v)-Surface(u-1f/size,v);
                    float dy=Surface(u,v+1f/size)-Surface(u,v-1f/size);
                    var n=new Vector3(-dx*1.7f,-dy*1.7f,1).normalized;
                    normal.SetPixel(x,y,new Color(n.x*.5f+.5f,n.y*.5f+.5f,n.z*.5f+.5f));
                }
                color.Apply(); normal.Apply();
                File.WriteAllBytes(Art+"/FoamPoresColor.png",color.EncodeToPNG());
                File.WriteAllBytes(Art+"/FoamPoresNormal.png",normal.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(color); Object.DestroyImmediate(normal); }
            AssetDatabase.ImportAsset(Art+"/FoamPoresColor.png");
            AssetDatabase.ImportAsset(Art+"/FoamPoresNormal.png");
            var importer=(TextureImporter)AssetImporter.GetAtPath(Art+"/FoamPoresNormal.png");
            importer.textureType=TextureImporterType.NormalMap;
            importer.SaveAndReimport();
            var material=AssetDatabase.LoadAssetAtPath<Material>(Art+"/Foam_0.mat");
            if (material == null) throw new System.InvalidOperationException("Foam material missing");
            material.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/FoamPoresColor.png"));
            material.SetTexture("_BumpMap",AssetDatabase.LoadAssetAtPath<Texture2D>(Art+"/FoamPoresNormal.png"));
            material.SetColor("_BaseColor",new Color(1f,.58f,.22f));
            material.SetFloat("_Smoothness",.025f);
            material.SetFloat("_BumpScale",1.25f);
            material.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
        }
    }
}
