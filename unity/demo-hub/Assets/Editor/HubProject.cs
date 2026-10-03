using System;
using System.IO;
using System.Linq;
using Hapbeat.DemoSwitch;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace Hapbeat.DemoHub.Editor
{
    public static class HubProject
    {
        public const string ScenePath = "Assets/Scenes/DemoHub.unity";
        public const string PackageId = "jp.hapbeat.demohub";
        public const string FontPath = "Packages/com.hapbeat.demo-switch/Runtime/Resources/HapbeatDemoSession/NotoSansCJKjp-Regular.otf";

        // Initial authoring only: never replace an existing, hand-edited scene or settings.
        [MenuItem("Hapbeat Demo Hub/Create Initial Scene (Once)")]
        public static void CreateInitialScene()
        {
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Hub scene already exists; edit it directly.");
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory("Assets/Resources");
            AssetDatabase.Refresh();
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) throw new InvalidOperationException("Install the bundled Japanese font first.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var camera = new GameObject("Head", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.038f, 0.06f);
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 10;
            var pose = camera.gameObject.AddComponent<TrackedPoseDriver>();
            pose.positionInput = new InputActionProperty(new InputAction("Head Position", binding: "<XRHMD>/centerEyePosition"));
            pose.rotationInput = new InputActionProperty(new InputAction("Head Rotation", binding: "<XRHMD>/centerEyeRotation"));
            pose.trackingStateInput = new InputActionProperty(new InputAction("Head Tracking", binding: "<XRHMD>/trackingState"));
            var canvas = new GameObject("Waiting Message", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            canvas.transform.SetParent(camera.transform, false);
            canvas.transform.localPosition = new Vector3(0, 0, 2);
            canvas.transform.localScale = Vector3.one * 0.0015f;
            ((RectTransform)canvas.transform).sizeDelta = new Vector2(1200, 600);
            Label(canvas.transform, font, "HAPBEAT", 140, 30, new Color(0.35f, 0.8f, 0.92f));
            Label(canvas.transform, font, "正面を向いてお待ちください", 35, 48, Color.white);
            Label(canvas.transform, font, "Please face forward and wait.", -45, 34, new Color(0.8f, 0.86f, 0.94f));
            Label(canvas.transform, font, "スタッフがデモを開始します\nThe staff will start your demo.", -170, 26, new Color(0.58f, 0.68f, 0.78f));
            EditorSceneManager.SaveScene(scene, ScenePath);
            var settingsPath = "Assets/Resources/" + DemoSwitchSettings.ResourceName + ".asset";
            if (!File.Exists(settingsPath))
            {
                var settings = ScriptableObject.CreateInstance<DemoSwitchSettings>();
                var so = new SerializedObject(settings);
                so.FindProperty("_receiverEnabled").boolValue = true;
                so.FindProperty("_currentDemoId").stringValue = "demo_hub";
                so.FindProperty("_allowUnsignedOnIsolatedLan").boolValue = true;
                var targets = so.FindProperty("_targets");
                var ids = new[] { "gloveball", "handdemo", "gloveball_v2", "boxing" };
                var packages = new[] { "jp.hapbeat.gloveballdemo", "com.Hapbeat.HapticHandDemo_G2", "jp.hapbeat.gloveballdemo.v2", "com.hapbeat.boxing" };
                targets.arraySize = ids.Length;
                for (var i = 0; i < ids.Length; i++)
                {
                    var entry = targets.GetArrayElementAtIndex(i);
                    entry.FindPropertyRelative("_demoId").stringValue = ids[i];
                    entry.FindPropertyRelative("_packageName").stringValue = packages[i];
                    entry.FindPropertyRelative("_activityName").stringValue = "com.unity3d.player.UnityPlayerGameActivity";
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.CreateAsset(settings, settingsPath);
            }
            Configure();
            Debug.Log("[Hub] Created waiting scene without modifying any other project.");
        }

        static void Label(Transform parent, Font font, string message, float y, int size, Color color)
        {
            var text = new GameObject(message.Split('\n')[0], typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(parent, false);
            text.rectTransform.sizeDelta = new Vector2(1200, 100);
            text.rectTransform.anchoredPosition = new Vector2(0, y);
            text.font = font;
            text.fontSize = size;
            text.text = message;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
        }

        // Adds the Demo Session front end to the existing hand-edited scene. Idempotent; nothing else changes.
        [MenuItem("Hapbeat Demo Hub/Add Demo Session Controller")]
        public static void AddSessionController()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath);
            if (UnityEngine.Object.FindAnyObjectByType<Hapbeat.DemoHub.DemoHubController>() != null)
            {
                Debug.Log("[Hub] Demo Session controller already present.");
                return;
            }
            var waiting = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Canvas>(true))
                .FirstOrDefault(x => x.name == "Waiting Message");
            if (waiting == null) throw new InvalidOperationException("Waiting Message canvas not found.");
            var controller = new GameObject("Demo Session Hub").AddComponent<Hapbeat.DemoHub.DemoHubController>();
            var so = new SerializedObject(controller);
            so.FindProperty("_waitingMessage").objectReferenceValue = waiting.gameObject;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Hub] Added Demo Session controller.");
        }

        [MenuItem("Hapbeat Demo Hub/Configure XR and Player")]
        public static void Configure()
        {
            PlayerSettings.companyName = "Hapbeat";
            PlayerSettings.productName = "Demo Hub";
            PlayerSettings.bundleVersion = "0.1.0-d5";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.bundleVersionCode = 5;
            PlayerSettings.runInBackground = false;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            var ps = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            ps.FindProperty("activeInputHandler").intValue = 1;
            ps.ApplyModifiedPropertiesWithoutUndo();
            Directory.CreateDirectory("Assets/XR");
            AssetDatabase.Refresh();
            if (!EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(XRGeneralSettings.k_SettingsKey, out var general))
            {
                general = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(general, "Assets/XR/XRGeneralSettings.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, general, true);
            }
            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                if (!general.HasSettingsForBuildTarget(group)) general.CreateDefaultSettingsForBuildTarget(group);
                if (!general.HasManagerSettingsForBuildTarget(group)) general.CreateDefaultManagerSettingsForBuildTarget(group);
                general.SettingsForBuildTarget(group).InitManagerOnStart = true;
                var manager = general.ManagerSettingsForBuildTarget(group);
                if (!XRPackageMetadataStore.AssignLoader(manager, "UnityEngine.XR.OpenXR.OpenXRLoader", group))
                    throw new InvalidOperationException("OpenXR loader assignment failed: " + group);
                FeatureHelpers.RefreshFeatures(group);
                var xr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                xr.GetFeature<OculusTouchControllerProfile>().enabled = true;
                // Declares hands-supported mode to Quest even though the waiting room has no input UI.
                var hands = xr.GetFeatures().Single(x => x.GetType().Name == "HandTracking");
                hands.enabled = true;
                EditorUtility.SetDirty(hands);
                if (group == BuildTargetGroup.Android)
                {
                    var quest = xr.GetFeature<MetaQuestFeature>();
                    quest.enabled = true;
                    quest.ForceRemoveInternetPermission = false;
                    var q = new SerializedObject(quest);
                    var devices = q.FindProperty("targetDevices");
                    for (var i = 0; i < devices.arraySize; i++)
                    {
                        var device = devices.GetArrayElementAtIndex(i);
                        var name = device.FindPropertyRelative("manifestName").stringValue;
                        device.FindPropertyRelative("enabled").boolValue = name == "quest2" || name == "eureka" || name == "quest3s";
                    }
                    q.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(quest);
                }
                EditorUtility.SetDirty(xr);
                EditorUtility.SetDirty(manager);
                EditorUtility.SetDirty(general.SettingsForBuildTarget(group));
            }
            EditorUtility.SetDirty(general);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Hapbeat Demo Hub/Build Quest APK")]
        public static void BuildApk()
        {
            Configure();
            HubValidation.Validate();
            if (!File.Exists(ScenePath)) throw new InvalidOperationException("Create the hub scene first.");
            Directory.CreateDirectory("Builds");
            EditorUserBuildSettings.buildAppBundle = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { ScenePath }, target = BuildTarget.Android,
                locationPathName = "Builds/hapbeat-demo-hub.apk", options = BuildOptions.None
            });
            Debug.Log($"[Hub] Build: {report.summary.result}, errors={report.summary.totalErrors}, warnings={report.summary.totalWarnings}");
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Hub APK build failed.");
        }
    }
}
