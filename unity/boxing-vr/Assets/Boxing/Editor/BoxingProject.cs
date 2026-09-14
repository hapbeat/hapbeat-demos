using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Hapbeat;
using Hapbeat.DemoSwitch;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using Unity.XR.CoreUtils;

namespace Hapbeat.Boxing.Editor
{
    public static class BoxingProject
    {
        public const string ScenePath = "Assets/Boxing/Scenes/Boxing.unity";
        private const string Root = "Assets/Boxing";
        private static Material navy, mat, red, cyan, white, dark, skin, gold;

        [MenuItem("Hapbeat Boxing/Create Initial Scene")]
        public static void Create()
        {
            Configure();
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); Validate(); return; }
            Directory.CreateDirectory(Root + "/Scenes"); Directory.CreateDirectory(Root + "/Art"); Directory.CreateDirectory(Root + "/Haptics");
            Directory.CreateDirectory("Assets/Resources");
            AssetDatabase.Refresh();
            navy = Material("Navy", new Color(0.024f, 0.042f, 0.075f));
            mat = Material("Canvas", new Color(0.10f, 0.19f, 0.23f));
            red = Material("RedLeather", new Color(0.78f, 0.07f, 0.035f), 0.48f);
            cyan = Material("CyanLeather", new Color(0.04f, 0.53f, 0.65f), 0.48f);
            white = Material("Ivory", new Color(0.86f, 0.88f, 0.85f));
            dark = Material("Rubber", new Color(0.028f, 0.036f, 0.045f));
            skin = Material("Opponent", new Color(0.48f, 0.36f, 0.29f), 0.25f);
            gold = Material("Amber", new Color(1, 0.54f, 0.08f));
            // Initial scaffold only. Subsequent calls retain the checked-in scene and tuning.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.36f, 0.42f, 0.49f);
            RenderSettings.fog = true; RenderSettings.fogColor = new Color(0.025f, 0.038f, 0.055f); RenderSettings.fogDensity = 0.035f;
            Light("Key", new Vector3(50, -25, 0), Color.white, 1.4f, true);
            Light("Rim", new Vector3(25, 155, 0), new Color(0.35f, 0.75f, 1), 0.65f, false);
            var arena = new GameObject("Arena - original procedural assets").transform;
            Box("Floor", arena, new Vector3(0, -0.15f, 0), new Vector3(24, 0.2f, 24), navy);
            Box("Ring", arena, new Vector3(0, -0.055f, 0.4f), new Vector3(5.4f, 0.11f, 5.4f), mat);
            for (int i = -1; i <= 1; i += 2)
            for (int j = -1; j <= 1; j += 2)
            {
                Vector3 corner = new Vector3(i * 2.55f, 0.75f, j * 2.55f + 0.4f);
                Box("Corner pad", arena, corner, new Vector3(0.18f, 1.5f, 0.18f), i == j ? cyan : red);
            }
            for (int level = 0; level < 3; level++)
            {
                float y = 0.55f + level * 0.33f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Capsule("Rope", arena, new Vector3(side * 2.55f, y, -2.15f), new Vector3(side * 2.55f, y, 2.95f), 0.018f, level == 1 ? white : cyan);
                    Capsule("Rope", arena, new Vector3(-2.55f, y, side * 2.55f + 0.4f), new Vector3(2.55f, y, side * 2.55f + 0.4f), 0.018f, level == 1 ? white : red);
                }
            }
            for (int i = -2; i <= 2; i++)
            {
                Box("Wall pier", arena, new Vector3(i * 3.5f, 2, 7), new Vector3(0.28f, 4, 0.45f), dark);
                Box("Window", arena, new Vector3(i * 3.5f, 2.7f, 7.05f), new Vector3(2.4f, 0.7f, 0.1f), mat);
                Box("Ceiling strip", arena, new Vector3(i * 2.5f, 4.5f, 0), new Vector3(0.12f, 0.06f, 10), white);
            }
            Box("Rear wall", arena, new Vector3(0, 2, 7.2f), new Vector3(20, 4, 0.2f), navy);
            var floorMark = Box("Start position", arena, new Vector3(0, 0.003f, 0), new Vector3(0.5f, 0.006f, 0.05f), gold);
            var rig = new GameObject("XR Origin (Boxing)");
            var origin = rig.AddComponent<XROrigin>();
            var offset = new GameObject("Camera Offset"); offset.transform.SetParent(rig.transform, false);
            var cameraObject = new GameObject("Main Camera"); cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(offset.transform, false); cameraObject.transform.localPosition = Vector3.up * 1.65f;
            var camera = cameraObject.AddComponent<Camera>(); camera.nearClipPlane = 0.045f; camera.farClipPlane = 50; camera.backgroundColor = RenderSettings.fogColor; camera.clearFlags = CameraClearFlags.SolidColor;
            cameraObject.AddComponent<AudioListener>();
            origin.Camera = camera; origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            var input = rig.AddComponent<BoxingInput>(); input.origin = origin; input.headCamera = camera; input.startPoint = floorMark;
            SetupHeadDriver(input);
            var host = new GameObject("Boxing Match");
            var game = host.AddComponent<BoxingGame>();
            var tuning = ScriptableObject.CreateInstance<BoxingTuning>(); AssetDatabase.CreateAsset(tuning, Root + "/BoxingTuning.asset");
            game.tuning = tuning; game.input = input;
            var view = host.AddComponent<BoxingPresentation>(); game.presentation = view;
            view.leftGlove = Glove("Player Left", null, cyan, true);
            view.rightGlove = Glove("Player Right", null, red, false);
            var enemy = new GameObject("Opponent - sparring partner").transform;
            view.enemyHead = Sphere("Head", enemy, new Vector3(0, 1.65f, 1.08f), new Vector3(0.31f, 0.38f, 0.31f), skin);
            // Face points toward the player (-Z), with headguard and inset face details.
            Sphere("Headguard", view.enemyHead, new Vector3(0, 0.07f, 0.01f), new Vector3(0.34f, 0.15f, 0.32f), red, true);
            for (int s = -1; s <= 1; s += 2)
            {
                Sphere("Eye", view.enemyHead, new Vector3(s * 0.060f, 0.014f, -0.145f), new Vector3(0.044f, 0.024f, 0.018f), dark, true);
                Sphere("Cheek guard", view.enemyHead, new Vector3(s * 0.139f, -0.03f, -0.02f), new Vector3(0.07f, 0.17f, 0.25f), red, true);
            }
            view.enemyTorso = Sphere("Torso", enemy, new Vector3(0, 1.22f, 1.08f), new Vector3(0.52f, 0.66f, 0.30f), skin);
            view.enemyHip = Sphere("Shorts", enemy, new Vector3(0, 0.86f, 1.08f), new Vector3(0.43f, 0.29f, 0.30f), navy);
            Sphere("Waistband", view.enemyHip, new Vector3(0, 0.08f, 0), new Vector3(0.44f, 0.065f, 0.30f), white, true);
            view.enemyLeftGlove = Glove("Enemy Left Glove", enemy, red, true);
            view.enemyRightGlove = Glove("Enemy Right Glove", enemy, red, false);
            view.enemyArms = Enumerable.Range(0, 4).Select(i => Primitive("Arm " + i, PrimitiveType.Capsule, enemy, skin)).ToArray();
            view.enemyLegs = new Transform[6];
            for (int i = 0; i < 2; i++)
            {
                view.enemyLegs[i * 3] = Primitive("Thigh", PrimitiveType.Capsule, enemy, skin);
                view.enemyLegs[i * 3 + 1] = Primitive("Shin", PrimitiveType.Capsule, enemy, skin);
                view.enemyLegs[i * 3 + 2] = Sphere("Boot", enemy, Vector3.zero, new Vector3(0.15f, 0.16f, 0.29f), dark);
            }
            var hud = Canvas("Ring scoreboard", new Vector3(0, 2.4f, 3.05f), Vector3.zero, new Vector2(1200, 260), 0.003f);
            view.timerText = Text("Timer", hud, new Vector2(0, 65), new Vector2(1100, 85), 60, "90s");
            view.scoreText = Text("Score", hud, new Vector2(0, -8), new Vector2(1150, 45), 27, "SCORE 0");
            view.cueText = Text("Cue", hud, new Vector2(0, -58), new Vector2(1150, 45), 28, "GLOVES UP");
            view.statusText = Text("Status", hud, new Vector2(0, -106), new Vector2(1150, 35), 17, "");
            var flashCanvas = Canvas("Impact feedback", Vector3.zero, Vector3.zero, new Vector2(600, 600), 0.001f);
            flashCanvas.SetParent(camera.transform, false); flashCanvas.localPosition = new Vector3(0, 0, 0.55f);
            view.impactText = Text("Impact", flashCanvas, new Vector2(0, -150), new Vector2(620, 50), 18, "");
            var burst = new GameObject("Contact burst").transform;
            for (int i = 0; i < 8; i++)
            {
                var spark = Box("Spark", burst, Quaternion.Euler(0, 0, i * 45) * Vector3.up * 0.4f, new Vector3(0.035f, 0.22f, 0.03f), gold, true);
                spark.localRotation = Quaternion.Euler(0, 0, -i * 45);
            }
            view.hitBurst = burst; burst.gameObject.SetActive(false);
            var menu = host.AddComponent<BoxingMenu>(); game.menu = menu; menu.game = game; menu.input = input;
            var panel = Canvas("Boxing menu", new Vector3(0, 1.6f, 1.25f), Vector3.zero, new Vector2(780, 900), 0.0012f);
            menu.panel = panel;
            var background = new GameObject("Panel background", typeof(RectTransform), typeof(Image)); background.transform.SetParent(panel, false);
            background.GetComponent<RectTransform>().sizeDelta = new Vector2(780, 900); background.GetComponent<Image>().color = new Color(0.015f, 0.027f, 0.045f, 1);
            menu.title = Text("Title", panel, new Vector2(0, 325), new Vector2(700, 160), 60, "HAPBEAT\nBOXING");
            menu.rows = new Text[7];
            for (int i = 0; i < 7; i++) menu.rows[i] = Text("Option " + i, panel, new Vector2(0, 185 - i * 65), new Vector2(680, 58), 30, "");
            menu.hint = Text("Controls", panel, new Vector2(0, -340), new Vector2(730, 125), 20, "");
            var dwell = new GameObject("Look to select progress", typeof(RectTransform), typeof(Image)); dwell.transform.SetParent(panel, false);
            var dr = dwell.GetComponent<RectTransform>(); dr.anchoredPosition = new Vector2(0, -255); dr.sizeDelta = new Vector2(600, 6);
            menu.dwellBar = dwell.GetComponent<Image>(); menu.dwellBar.color = Color.cyan; menu.dwellBar.type = Image.Type.Filled; menu.dwellBar.fillMethod = Image.FillMethod.Horizontal;
            BuildHaptics(game);
            BoxingContent.ConfigureModels(game);
            BoxingContent.ConfigureImpactVisuals(game.presentation);
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            ConfigureSwitch(); AssetDatabase.SaveAssets(); Validate();
        }

        public static void Configure()
        {
            PlayerSettings.companyName = "Hapbeat"; PlayerSettings.productName = "Hapbeat Boxing"; PlayerSettings.bundleVersion = "0.1.0-d1";
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, "com.hapbeat.boxing");
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            QualitySettings.vSyncCount = 0;
            Directory.CreateDirectory(Root + "/Art"); AssetDatabase.Refresh();
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Root + "/Art/BoxingRenderer.asset");
            if (renderer == null) { renderer = ScriptableObject.CreateInstance<UniversalRendererData>(); AssetDatabase.CreateAsset(renderer, Root + "/Art/BoxingRenderer.asset"); }
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(Root + "/Art/BoxingPipeline.asset");
            if (pipeline == null)
            {
                pipeline = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
                AssetDatabase.CreateAsset(pipeline, Root + "/Art/BoxingPipeline.asset");
                var so = new SerializedObject(pipeline); var list = so.FindProperty("m_RendererDataList"); list.arraySize = 1; list.GetArrayElementAtIndex(0).objectReferenceValue = renderer; so.ApplyModifiedPropertiesWithoutUndo();
                pipeline.msaaSampleCount = 4; pipeline.renderScale = 1f; pipeline.supportsHDR = false;
            }
            GraphicsSettings.defaultRenderPipeline = pipeline; QualitySettings.renderPipeline = pipeline;
            foreach (BuildTargetGroup group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                var settings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
                if (settings == null || settings.Manager == null) throw new InvalidOperationException("Template XR settings missing for " + group);
                settings.InitManagerOnStart = true;
                if (!XRPackageMetadataStore.AssignLoader(settings.Manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group))
                    throw new InvalidOperationException("Cannot assign OpenXR loader " + group);
                FeatureHelpers.RefreshFeatures(group);
                var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                foreach (var feature in xr.GetFeatures())
                {
                    string type = feature.GetType().Name;
                    feature.enabled = type == "OculusTouchControllerProfile" || type == "MetaQuestTouchPlusControllerProfile" ||
                        type == "HandTracking" || type == "HandInteractionProfile" || type == "MetaHandTrackingAim" ||
                        type == "HandCommonPosesInteraction" || (group == BuildTargetGroup.Android && type == "MetaQuestFeature");
                    EditorUtility.SetDirty(feature);
                }
                EditorUtility.SetDirty(xr); EditorUtility.SetDirty(settings);
            }
            foreach (var key in new[] { "Unity.XR.Oculus.Settings", "Unity.XR.WindowsMR.Settings", "UnityEditor.XR.ARCore.ARCoreSettings", "UnityEditor.XR.ARKit.ARKitSettings", "com.unity.xr.arfoundation.simulation_settings", "com.unity.input.settings.actions" }) EditorBuildSettings.RemoveConfigObject(key);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Hapbeat Boxing/Upgrade Input And Template Settings")]
        public static void Upgrade()
        {
            // Remove only serialized feature objects belonging to packages excluded from the VR seed.
            const string xrPath = "Assets/XR/Settings/OpenXR Package Settings.asset";
            string original = File.ReadAllText(xrPath), cleaned = original;
            foreach (Match block in Regex.Matches(original, @"(?ms)^--- !u!\d+ &(-?\d+)\r?\n.*?(?=^--- !u!|\z)"))
            {
                var script = Regex.Match(block.Value, @"m_Script: \{fileID: 11500000, guid: ([a-f0-9]+), type: 3\}");
                if (!script.Success || !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(script.Groups[1].Value))) continue;
                cleaned = cleaned.Replace(block.Value, "");
                cleaned = Regex.Replace(cleaned, @"(?m)^\s*- \{fileID: " + block.Groups[1].Value + @"\}\r?\n", "");
            }
            if (cleaned != original) { File.WriteAllText(xrPath, cleaned); AssetDatabase.ImportAsset(xrPath, ImportAssetOptions.ForceUpdate); }
            Configure();
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var input = UnityEngine.Object.FindAnyObjectByType<BoxingInput>();
            SetupHeadDriver(input);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets(); Validate();
        }
        private static void SetupHeadDriver(BoxingInput input)
        {
            var driver = input.headCamera.GetComponent<TrackedPoseDriver>();
            if (driver == null)
            {
                driver = input.headCamera.gameObject.AddComponent<TrackedPoseDriver>();
                driver.positionInput = new InputActionProperty(new InputAction("Head Position", InputActionType.Value, "<XRHMD>/centerEyePosition"));
                driver.rotationInput = new InputActionProperty(new InputAction("Head Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation"));
                driver.trackingStateInput = new InputActionProperty(new InputAction("Head Tracking", InputActionType.Value, "<XRHMD>/trackingState"));
                driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            }
            input.headDriver = driver;
        }

        [MenuItem("Hapbeat Boxing/Polish Existing Protective Gear")]
        public static void Polish()
        {
            Upgrade();
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var primitive = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var sphere = primitive.GetComponent<MeshFilter>().sharedMesh;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var mesh in root.GetComponentsInChildren<MeshFilter>(true))
                if (new[] { "Headguard", "Cheek guard", "Waistband", "Cuff", "Wrist strap" }.Contains(mesh.name)) mesh.sharedMesh = sphere;
            UnityEngine.Object.DestroyImmediate(primitive);
            var game = UnityEngine.Object.FindAnyObjectByType<BoxingGame>();
            game.menu.panel.Find("Panel background").GetComponent<Image>().color = new Color(0.015f, 0.027f, 0.045f, 1);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }

        private static void BuildHaptics(BoxingGame game)
        {
            var feedback = game.gameObject.AddComponent<BoxingFeedback>(); game.feedback = feedback;
            var sdk = new GameObject("Hapbeat SDK"); sdk.AddComponent<HapbeatManager>(); feedback.sdkRoot = sdk;
            var config = ScriptableObject.CreateInstance<HapbeatConfig>(); config.appName = "Boxing <g>"; config.enableLogging = false;
            AssetDatabase.CreateAsset(config, "Assets/Resources/HapbeatConfig.asset");
            BoxingContent.ConfigureFeedback(game);
        }
        private static void ConfigureSwitch()
        {
            var settings = ScriptableObject.CreateInstance<DemoSwitchSettings>();
            var so = new SerializedObject(settings); so.FindProperty("_receiverEnabled").boolValue = true;
            so.FindProperty("_currentDemoId").stringValue = "boxing"; so.FindProperty("_allowUnsignedOnIsolatedLan").boolValue = true;
            var targets = so.FindProperty("_targets"); targets.arraySize = 2;
            for (int i = 0; i < 2; i++)
            {
                var item = targets.GetArrayElementAtIndex(i); item.FindPropertyRelative("_demoId").stringValue = i == 0 ? "gloveball" : "handdemo";
                item.FindPropertyRelative("_packageName").stringValue = i == 0 ? "jp.hapbeat.gloveballdemo" : "com.Hapbeat.HapticHandDemo_G2";
                item.FindPropertyRelative("_activityName").stringValue = "com.unity3d.player.UnityPlayerGameActivity";
            }
            so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.CreateAsset(settings, "Assets/Resources/HapbeatDemoSwitchSettings.asset");
        }

        public static void Validate()
        {
            ValidateLayerSettings("ProjectSettings/TagManager.asset", "layers");
            ValidateLayerSettings("Assets/XRI/Settings/Resources/InteractionLayerSettings.asset", "m_LayerNames");
            var scene = EditorSceneManager.OpenScene(ScenePath);
            var game = UnityEngine.Object.FindAnyObjectByType<BoxingGame>();
            if (game == null || game.input == null || game.tuning == null || game.feedback == null || game.menu == null) throw new InvalidOperationException("Incomplete boxing scene");
            if (game.input.headDriver == null || game.input.headDriver.GetComponent<Camera>() != game.input.headCamera) throw new InvalidOperationException("Head tracked pose driver missing");
            if (game.input.startPoint == null) throw new InvalidOperationException("Scene start position marker missing");
            foreach (var root in scene.GetRootGameObjects())
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0) throw new InvalidOperationException("Missing script: " + transform.name);
            foreach (var trigger in game.feedback.impactTriggers)
                if (trigger == null || trigger.ResolveEntry() == null || trigger.ResolveEntry().streamClip == null) throw new InvalidOperationException("Unwired haptic trigger");
            if (game.feedback.impactTriggers.Length != 12 || game.feedback.contactSounds.Length != 4 || game.feedback.contactSounds.Any(c => c == null) || game.feedback.bell == null || game.feedback.bellSource == null)
                throw new InvalidOperationException("Incomplete surface feedback assets");
            Debug.Log("BOXING_SCENE_VALID: XR rig, menu, opponent, twelve surface haptic bindings, no missing scripts");
        }
        private static void ValidateLayerSettings(string path, string property)
        {
            if (path.StartsWith("Assets/")) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (assets.Length == 0) throw new InvalidOperationException("Cannot parse layer settings: " + path);
            var table = new SerializedObject(assets[0]).FindProperty(property);
            if (table == null || table.arraySize != 32 || table.GetArrayElementAtIndex(0).stringValue != "Default")
                throw new InvalidOperationException("Invalid layer table: " + path);
        }
        public static void BuildWindows()
        {
            Configure(); Validate(); Directory.CreateDirectory("Builds/Windows");
            var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, "Builds/Windows/HapbeatBoxing.exe", BuildTarget.StandaloneWindows64, BuildOptions.DetailedBuildReport);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Windows build failed");
            BoxingSimulator.AssertExcludedFromBuild(report);
        }
        public static void BuildAndroid()
        {
            Configure(); Validate(); Directory.CreateDirectory("Builds");
            var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, "Builds/HapbeatBoxing.apk", BuildTarget.Android, BuildOptions.DetailedBuildReport);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) throw new Exception("Android build failed");
            BoxingSimulator.AssertExcludedFromBuild(report);
        }
        private static Material Material(string name, Color color, float smoothness = 0.15f)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.name = name;
            material.SetColor("_BaseColor", color); material.SetFloat("_Smoothness", smoothness); AssetDatabase.CreateAsset(material, Root + "/Art/" + name + ".mat"); return material;
        }
        private static Transform Primitive(string name, PrimitiveType type, Transform parent, Material material)
        {
            var go = GameObject.CreatePrimitive(type); go.name = name; go.transform.SetParent(parent, false);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); go.GetComponent<Renderer>().sharedMaterial = material; return go.transform;
        }
        private static Transform Box(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool local = false) => Shape(name, PrimitiveType.Cube, parent, position, scale, material, local);
        private static Transform Sphere(string name, Transform parent, Vector3 position, Vector3 scale, Material material, bool local = false)
        {
            // Identity root keeps facial details / glove parts in metres rather than inheriting non-uniform scale.
            var root = new GameObject(name).transform; root.SetParent(parent, false); if (local) root.localPosition = position; else root.position = position;
            Shape(name + " mesh", PrimitiveType.Sphere, root, Vector3.zero, scale, material, true); return root;
        }
        private static Transform Shape(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material, bool local)
        {
            var t = Primitive(name, type, parent, material); if (local) t.localPosition = position; else t.position = position; t.localScale = scale; return t;
        }
        private static Transform Capsule(string name, Transform parent, Vector3 a, Vector3 b, float radius, Material material)
        { var t = Primitive(name, PrimitiveType.Capsule, parent, material); BoxingPresentation.Segment(t, a, b, radius); return t; }
        private static Transform Glove(string name, Transform parent, Material leather, bool left)
        {
            var root = new GameObject(name).transform; root.SetParent(parent, false);
            Sphere("Padded knuckles", root, new Vector3(0, 0.005f, 0.01f), new Vector3(0.205f, 0.185f, 0.22f), leather, true);
            Sphere("Palm", root, new Vector3(0, -0.045f, -0.018f), new Vector3(0.16f, 0.10f, 0.19f), dark, true);
            var thumb = Sphere("Thumb", root, new Vector3(left ? 0.078f : -0.078f, -0.055f, -0.004f), new Vector3(0.075f, 0.085f, 0.14f), leather, true);
            thumb.localRotation = Quaternion.Euler(0, left ? -24 : 24, 0);
            Sphere("Cuff", root, new Vector3(0, -0.01f, -0.11f), new Vector3(0.16f, 0.135f, 0.075f), leather, true);
            Sphere("Wrist strap", root, new Vector3(0, 0.025f, -0.115f), new Vector3(0.165f, 0.055f, 0.062f), white, true);
            Box("Mark", root, new Vector3(0, 0.052f, -0.116f), new Vector3(0.07f, 0.006f, 0.042f), dark, true);
            return root;
        }
        private static void Light(string name, Vector3 angles, Color color, float strength, bool shadows)
        { var go = new GameObject(name); go.transform.eulerAngles = angles; var light = go.AddComponent<Light>(); light.type = LightType.Directional; light.color = color; light.intensity = strength; light.shadows = shadows ? LightShadows.Soft : LightShadows.None; }
        private static RectTransform Canvas(string name, Vector3 position, Vector3 rotation, Vector2 size, float scale)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas)); var rect = go.GetComponent<RectTransform>(); rect.position = position; rect.eulerAngles = rotation; rect.sizeDelta = size; rect.localScale = Vector3.one * scale;
            var canvas = go.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; return rect;
        }
        private static Text Text(string name, Transform parent, Vector2 position, Vector2 size, int fontSize, string content)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchoredPosition = position; rect.sizeDelta = size;
            var text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = fontSize; text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white; text.text = content; text.raycastTarget = false; return text;
        }
    }
}
