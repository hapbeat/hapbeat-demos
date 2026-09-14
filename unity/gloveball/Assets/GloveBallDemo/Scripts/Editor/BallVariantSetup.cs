using System;
using System.Collections.Generic;
using System.IO;
using GloveBallDemo.Runtime;
using Hapbeat;
using UnityEditor;
using UnityEngine;

namespace GloveBallDemo.Editor
{
    /// <summary>Additive asset setup only. Never opens, saves or rebuilds a scene.</summary>
    [InitializeOnLoad]
    public static class BallVariantSetup
    {
        private const string PreviewKey = "GloveBall.BallVariantPreview";
        private static int _previewTicks;
        static BallVariantSetup() { EditorApplication.update += PreviewTick; }
        public static void PreviewInPlayMode()
        {
            BatchOps.MuteEditorAudio();
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
            new GameObject("Pipeline initialization camera").AddComponent<Camera>();
            SessionState.SetBool(PreviewKey, true);
            EditorApplication.isPlaying = true;
        }
        private static void PreviewTick()
        {
            if (!SessionState.GetBool(PreviewKey, false) || !EditorApplication.isPlaying || ++_previewTicks < 20) return;
            SessionState.SetBool(PreviewKey, false);
            AudioListener.volume = 0;
            try { VerifyAndRender(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }
        private const string Art = "Assets/GloveBallDemo/Art/Balls";
        private const string MapPath = "Assets/GloveBallDemo/Haptics/BallImpactEventMap.asset";
        private static readonly string[] Names = { "Bowling", "Volleyball", "Foam", "Basketball", "Perforated" };

        public static void VerifyAndRender()
        {
            BatchOps.MuteEditorAudio();
            var map = AssetDatabase.LoadAssetAtPath<HapbeatEventMap>(MapPath);
            if (map == null || map.entries.Count != 15) throw new InvalidOperationException("Expected 15 impact entries");
            var ids = new HashSet<string>();
            foreach (var entry in map.entries)
                if (entry.streamClip == null || !ids.Add(entry.id)) throw new InvalidOperationException("Invalid impact clip or duplicate id");
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var container = new GameObject("Ball verification (temporary)");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(container, scene);
            RenderTexture texture = null;
            Texture2D image = null;
            var asyncCompilation = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            var usePipeline = Unsupported.useScriptableRenderPipeline;
            Unsupported.useScriptableRenderPipeline = true;
            try
            {
                var relay = container.AddComponent<HapticEventRelay>();
                typeof(HapticEventRelay).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(relay, null);
                for (var e = 16; e < 31; e++)
                    if (relay.GetHapbeatTrigger((DemoHapticEvent)e)?.ResolveEntry() == null) throw new InvalidOperationException("Unwired event " + e);
                for (var i = 0; i < 5; i++)
                {
                    var ball = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<Ball>(DemoAssetPaths.BallPrefab), container.transform);
                    ball.GetComponent<Rigidbody>().isKinematic = true;
                    ball.SelectVariant(i);
                    ball.transform.position = new Vector3((i - 2) * .30f, 0, 0);
                    ball.transform.rotation = Quaternion.Euler(90, 0, 0);
                    var bounds = ball.GetComponent<SphereCollider>().bounds;
                    foreach (var renderer in ball.GetComponentsInChildren<Renderer>())
                    {
                        if (renderer.bounds.size.magnitude > bounds.size.magnitude * 1.2f) throw new InvalidOperationException("Oversized visual");
                    }
                }
                var cameraObject = new GameObject("Preview camera");
                cameraObject.transform.SetParent(container.transform);
                var camera = cameraObject.AddComponent<Camera>();
                camera.cameraType = CameraType.Preview;
                camera.scene = scene;
                camera.transform.position = new Vector3(0, .38f, -1.5f);
                camera.transform.LookAt(Vector3.zero);
                camera.orthographic = true;
                camera.orthographicSize = .42f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.08f, .1f, .13f);
                var lightObject = new GameObject("Preview light");
                lightObject.transform.SetParent(container.transform);
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 2f;
                light.transform.rotation = Quaternion.Euler(35, -25, 0);
                texture = new RenderTexture(1200, 600, 24);
                camera.targetTexture = texture;
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,
                    new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = texture });
                RenderTexture.active = texture;
                image = new Texture2D(1200, 600, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1200, 600), 0, 0);
                image.Apply();
                File.WriteAllBytes("../../art-source/gloveball/balls/generated/unity-comparison.png", image.EncodeToPNG());
                Debug.Log("[BallVariants] Verified all 15 runtime trigger bindings and rendered all 5 imported visuals.");
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncCompilation;
                Unsupported.useScriptableRenderPipeline = usePipeline;
                RenderTexture.active = null;
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                if (texture != null) { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
                UnityEngine.Object.DestroyImmediate(container);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [MenuItem("GloveBall Demo/Install Ball Variants (no scene changes)")]
        public static void Install()
        {
            Directory.CreateDirectory(Art);
            var source = Path.GetFullPath("../../art-source/gloveball/balls/generated/runtime");
            foreach (var name in Names)
                foreach (var suffix in new[] { ".fbx", "_Normal.png" })
                    File.Copy(Path.Combine(source, name + suffix), Art + "/" + name + suffix, true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            foreach (var name in Names)
            {
                var modelImporter = (ModelImporter)AssetImporter.GetAtPath(Art + "/" + name + ".fbx");
                modelImporter.bakeAxisConversion = true;
                modelImporter.SaveAndReimport();
                var importer = (TextureImporter)AssetImporter.GetAtPath(Art + "/" + name + "_Normal.png");
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
            InstallVisuals();
            InstallImpacts();
            BallImpactKit.RegisterManifest();
            AssetDatabase.SaveAssets();
            Debug.Log("[BallVariants] Installed 5 visuals and 15 independent impact events. Scenes untouched.");
        }

        private static void InstallVisuals()
        {
            var root = PrefabUtility.LoadPrefabContents(DemoAssetPaths.BallPrefab);
            try
            {
                var ball = root.GetComponent<Ball>();
                var so = new SerializedObject(ball);
                var variants = so.FindProperty("_visualVariants");
                if (variants.arraySize != 0) return; // Preserve hand-tuned installed prefab.
                var diameter = root.GetComponent<SphereCollider>().radius * 2;
                foreach (Transform child in root.transform) child.gameObject.SetActive(false);
                variants.arraySize = Names.Length;
                for (var i = 0; i < Names.Length; i++)
                {
                    var name = Names[i];
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "/" + name + ".fbx");
                    if (model == null) throw new InvalidOperationException("Missing model " + name);
                    var visual = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                    visual.name = name;
                    visual.transform.localPosition = Vector3.zero;
                    // Keep the FBX importer's axis conversion.
                    var renderers = visual.GetComponentsInChildren<Renderer>();
                    var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    var max = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
                    visual.transform.localScale *= diameter / max;
                    visual.transform.localPosition -= bounds.center * diameter / max;
                    foreach (var renderer in renderers)
                    {
                        var materials = renderer.sharedMaterials;
                        for (var m = 0; m < materials.Length; m++)
                            materials[m] = MakeMaterial(name, materials[m], m);
                        renderer.sharedMaterials = materials;
                    }
                    visual.SetActive(i == 0);
                    variants.GetArrayElementAtIndex(i).objectReferenceValue = visual;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, DemoAssetPaths.BallPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static Material MakeMaterial(string kind, Material source, int index)
        {
            var path = $"{Art}/{kind}_{index}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var label = source != null ? source.name.ToLowerInvariant() : "";
            var color = kind == "Bowling" ? new Color(.018f,.035f,.12f) :
                kind == "Foam" ? new Color(.95f,.21f,.035f) :
                kind == "Basketball" ? new Color(.58f,.12f,.018f) : new Color(.55f,.8f,.025f);
            if (kind == "Volleyball") color = label.Contains("blue") ? new Color(.015f,.16f,.65f) :
                label.Contains("yellow") ? new Color(.95f,.64f,.025f) : new Color(.9f,.88f,.77f);
            if (label.Contains("seam")) color = new Color(.025f,.03f,.045f);
            material.SetColor("_BaseColor", color.gamma);
            material.SetFloat("_Smoothness", kind == "Bowling" ? .82f : kind == "Perforated" ? .64f : .2f);
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Art}/{kind}_Normal.png"));
            material.EnableKeyword("_NORMALMAP");
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void InstallImpacts()
        {
            var map = AssetDatabase.LoadAssetAtPath<HapbeatEventMap>(MapPath);
            if (map == null) { map = ScriptableObject.CreateInstance<HapbeatEventMap>(); AssetDatabase.CreateAsset(map, MapPath); }
            var existing = AssetDatabase.LoadAssetAtPath<HapbeatEventMap>(DemoAssetPaths.EventMap);
            var surfaces = new[] { "l_arm_collide", "r_arm_collide", "body_collide" };
            var go = new GameObject("BallImpactBindings");
            try
            {
                var bindings = go.AddComponent<BallImpactBindings>();
                var list = new List<HapbeatEventBinding>();
                for (var kind = 0; kind < Names.Length; kind++)
                    for (var side = 0; side < 3; side++)
                    {
                        var eventName = Names[kind].ToLowerInvariant() + "_" + surfaces[side];
                        var entry = map.entries.Find(e => e.eventName == eventName);
                        if (entry == null)
                        {
                            var template = existing.entries.Find(e => e.eventName == surfaces[side]);
                            if (template == null || template.streamClip == null) throw new InvalidOperationException("Missing impact template");
                            var clipPath = BallImpactKit.ClipPath(Names[kind]);
                            if (AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath) == null)
                            {
                                if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(template.streamClip), clipPath))
                                    throw new InvalidOperationException("Could not copy placeholder clip");
                            }
                            entry = new HapbeatEventEntry {
                                mode = HapticMode.StreamClip, category = "gloveball-ball-impacts", eventName = eventName,
                                displayName = Names[kind] + " / " + surfaces[side],
                                streamClip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath),
                                target = template.target, gain = template.gain, loop = false,
                                notes = "Kit clip shared by this ball's L/R/body events. Replace WAV in place, preserving its meta GUID."
                            };
                            entry.SetCachedManifestIntensity(1f);
                            map.entries.Add(entry);
                        }
                        var trigger = go.AddComponent<HapbeatUnityEventTrigger>();
                        trigger.EditorSetupEntry(map, entry.id);
                        list.Add(new HapbeatEventBinding { Event = (DemoHapticEvent)(16 + kind * 3 + side), Trigger = trigger });
                    }
                bindings.Bindings = list.ToArray();
                EditorUtility.SetDirty(map);
                const string prefabPath = "Assets/Resources/BallImpactBindings.prefab";
                if (!File.Exists(prefabPath)) PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
