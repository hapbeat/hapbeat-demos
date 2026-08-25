// Android / Quest build configuration. Like everything else in this project the settings are
// applied from script, never through the Project Settings GUI, so a fresh clone reaches the
// same state with one batch call.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace GloveBallDemo.Editor
{
    /// <summary>
    /// Puts the project into the Quest sideload configuration (OpenXR loader + Meta Quest
    /// feature + Android player settings) and produces the APK.
    /// </summary>
    public static class DemoAndroidBuilder
    {
        public const string ProductName = "Hapbeat GloveBall Demo";
        public const string ApplicationIdentifier = "jp.hapbeat.gloveballdemo";

        /// <summary>Where the general XR settings asset is created when the project has none yet.</summary>
        private const string XrSettingsDir = "Assets/XR";

        private const string OpenXrLoaderTypeName = "UnityEngine.XR.OpenXR.OpenXRLoader";

        /// <summary>
        /// OpenXR's own validation requires API 24. Quest 2 / 3 / 3S all run Android 12L, and Meta
        /// requires API 32 for anything shipped to their store, so 32 is the floor used here.
        /// </summary>
        private const AndroidSdkVersions MinSdk = AndroidSdkVersions.AndroidApiLevel32;

        // ------------------------------------------------------------------ XR

        /// <summary>
        /// Enables the OpenXR loader for Android and turns on the features a Quest build needs.
        /// Safe to re-run: every step is idempotent.
        /// </summary>
        public static void ConfigureAndroidXr()
        {
            var perBuildTarget = GetOrCreateGeneralSettings();

            if (!perBuildTarget.HasSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                perBuildTarget.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Android);
                Debug.Log("[Android] created XRGeneralSettings for Android");
            }

            if (!perBuildTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
                Debug.Log("[Android] created XRManagerSettings for Android");
            }

            var generalSettings = perBuildTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            generalSettings.InitManagerOnStart = true;

            var manager = perBuildTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!XRPackageMetadataStore.IsLoaderAssigned(OpenXrLoaderTypeName, BuildTargetGroup.Android))
            {
                if (!XRPackageMetadataStore.AssignLoader(manager, OpenXrLoaderTypeName, BuildTargetGroup.Android))
                {
                    Debug.LogError("[Android] failed to assign the OpenXR loader for Android");
                    EditorApplication.Exit(20);
                    return;
                }
            }

            Debug.Log("[Android] active loaders: " +
                      string.Join(", ", manager.activeLoaders.Select(l => l == null ? "<null>" : l.name)));

            EditorUtility.SetDirty(perBuildTarget);
            EditorUtility.SetDirty(generalSettings);
            EditorUtility.SetDirty(manager);

            ConfigureOpenXrFeatures();

            AssetDatabase.SaveAssets();
        }

        private static XRGeneralSettingsPerBuildTarget GetOrCreateGeneralSettings()
        {
            // XRGeneralSettingsPerBuildTarget.GetOrCreate() is internal to the package, so the
            // same three steps (config object -> asset search -> create) are done here.
            EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(
                XRGeneralSettings.k_SettingsKey, out var settings);

            if (settings == null)
            {
                var guid = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget").FirstOrDefault();
                if (!string.IsNullOrEmpty(guid))
                {
                    settings = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(
                        AssetDatabase.GUIDToAssetPath(guid));
                }
            }

            if (settings == null)
            {
                Directory.CreateDirectory(XrSettingsDir);
                AssetDatabase.Refresh();

                settings = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(settings, XrSettingsDir + "/XRGeneralSettingsPerBuildTarget.asset");
                AssetDatabase.SaveAssets();
                Debug.Log("[Android] created XRGeneralSettingsPerBuildTarget");
            }

            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            return settings;
        }

        private static void ConfigureOpenXrFeatures()
        {
            // Makes sure a feature instance exists for every feature type the packages declare,
            // otherwise a freshly added package has nothing to enable.
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);

            var openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (openXr == null)
            {
                Debug.LogError("[Android] no OpenXRSettings for Android");
                EditorApplication.Exit(21);
                return;
            }

            // Meta Quest Support is what injects the Quest entries into the AndroidManifest.
            // The three controller profiles are what the runtime binds the Input System to; the
            // Meta Quest feature's own validation fails unless at least one of them is on.
            var wanted = new[]
            {
                typeof(MetaQuestFeature),
                typeof(OculusTouchControllerProfile),
                typeof(MetaQuestTouchProControllerProfile),
                typeof(MetaQuestTouchPlusControllerProfile),
            };

            foreach (var type in wanted)
            {
                var feature = openXr.GetFeature(type);
                if (feature == null)
                {
                    Debug.LogError($"[Android] OpenXR feature not found: {type.Name}");
                    EditorApplication.Exit(22);
                    return;
                }

                feature.enabled = true;
                EditorUtility.SetDirty(feature);
                Debug.Log($"[Android] OpenXR feature enabled: {feature.name}");
            }

            var quest = (MetaQuestFeature)openXr.GetFeature(typeof(MetaQuestFeature));
            // The demo talks to Hapbeat devices over UDP, so the manifest must keep INTERNET.
            quest.ForceRemoveInternetPermission = false;
            EditorUtility.SetDirty(quest);

            ConfigureQuestTargetDevices(quest);

            Debug.Log("[Android] enabled OpenXR features: " +
                      string.Join(", ", openXr.GetFeatures().Where(f => f.enabled).Select(f => f.name)));

            EditorUtility.SetDirty(openXr);
        }

        /// <summary>
        /// Picks which Quest models land in the manifest's com.oculus.supportedDevices list.
        ///
        /// Quest Pro ("cambria") must stay off: ModifyAndroidManifestMeta reacts to it by adding
        /// &lt;uses-feature oculus.software.eye_tracking required="true"/&gt;, which makes the APK
        /// refuse to install on every headset without eye tracking - Quest 2, 3 and 3S included.
        /// The demo runs on a Quest 3S, so Quest Pro support is not worth that trade.
        ///
        /// MetaQuestFeature.targetDevices is internal to the package, so it is edited through
        /// SerializedObject rather than reflection.
        /// </summary>
        private static void ConfigureQuestTargetDevices(MetaQuestFeature quest)
        {
            var enabledDevices = new HashSet<string> { "quest", "quest2", "eureka", "quest3s" };

            var so = new SerializedObject(quest);
            var devices = so.FindProperty("targetDevices");
            if (devices == null || !devices.isArray)
            {
                Debug.LogError("[Android] MetaQuestFeature.targetDevices not found");
                EditorApplication.Exit(26);
                return;
            }

            var summary = new List<string>();
            for (var i = 0; i < devices.arraySize; i++)
            {
                var device = devices.GetArrayElementAtIndex(i);
                var manifestName = device.FindPropertyRelative("manifestName").stringValue;
                var enabled = device.FindPropertyRelative("enabled");

                enabled.boolValue = enabledDevices.Contains(manifestName);
                summary.Add($"{manifestName}={(enabled.boolValue ? "on" : "off")}");
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[Android] Quest target devices: {string.Join(" ", summary)}");
        }

        // -------------------------------------------------------------- player

        /// <summary>Android player settings for a Quest sideload build. Idempotent.</summary>
        public static void ConfigureAndroidPlayer()
        {
            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = "Hapbeat";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationIdentifier);

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = MinSdk;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;

            // The Hapbeat SDK sends UDP from the headset; without this the manifest can end up
            // without INTERNET when the build strips unused permissions.
            PlayerSettings.Android.forceInternetPermission = true;

            // Quest is a Vulkan-first device and URP's Android path is tuned for it.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.MTRendering = true;
            PlayerSettings.SetMobileMTRendering(NamedBuildTarget.Android, true);

            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;

            // Exhibition build: no store upload, so a plain APK signed with the debug key.
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.Android.useCustomKeystore = false;

            Debug.Log($"[Android] player configured: id={ApplicationIdentifier} " +
                      $"backend=IL2CPP arch={PlayerSettings.Android.targetArchitectures} " +
                      $"minSdk={PlayerSettings.Android.minSdkVersion} " +
                      $"gfx={string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android))} " +
                      $"internet={PlayerSettings.Android.forceInternetPermission}");
        }

        // --------------------------------------------------------------- build

        /// <summary>
        /// Applies the XR + player configuration, then builds Demo.unity into tools/artifacts.
        /// </summary>
        public static void BuildApk(string outputPath)
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("[Android] could not switch the active build target to Android");
                EditorApplication.Exit(23);
                return;
            }

            ConfigureAndroidXr();
            ConfigureAndroidPlayer();
            AssetDatabase.SaveAssets();

            // Demo.unity is the only scene in the player; ArenaEnv is loaded additively at runtime
            // and therefore has to be listed too.
            var scenes = new[] { DemoAssetPaths.DemoScene, DemoAssetPaths.ArenaEnvScene }
                .Where(p => AssetDatabase.LoadAssetAtPath<SceneAsset>(p) != null)
                .ToArray();

            if (scenes.Length == 0)
            {
                Debug.LogError("[Android] no scenes to build");
                EditorApplication.Exit(24);
                return;
            }

            EditorBuildSettings.scenes = scenes
                .Select(p => new EditorBuildSettingsScene(p, true))
                .ToArray();

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");

            LogValidationIssues();

            Debug.Log($"[Android] building {outputPath} from: {string.Join(", ", scenes)}");

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };

            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;

            Debug.Log($"[Android] BuildReport: result={summary.result} " +
                      $"totalTime={summary.totalTime} totalSize={summary.totalSize} bytes " +
                      $"errors={summary.totalErrors} warnings={summary.totalWarnings} " +
                      $"output={summary.outputPath}");

            if (summary.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                {
                    foreach (var msg in step.messages.Where(m => m.type == LogType.Error || m.type == LogType.Exception))
                    {
                        Debug.LogError($"[Android] {step.name}: {msg.content}");
                    }
                }

                EditorApplication.Exit(25);
                return;
            }

            var info = new FileInfo(outputPath);
            Debug.Log($"[Android] APK written: {info.FullName} ({info.Length / (1024f * 1024f):F1} MB)");
        }

        /// <summary>
        /// Reports every OpenXR validation rule that currently fails, so build-blocking issues
        /// show up in the batch log instead of only in the Project Validation window.
        /// </summary>
        private static void LogValidationIssues()
        {
            var issues = new List<OpenXRFeature.ValidationRule>();
            OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Android);

            if (issues.Count == 0)
            {
                Debug.Log("[Android] OpenXR validation: no issues");
                return;
            }

            foreach (var issue in issues)
            {
                var line = $"[Android] OpenXR validation ({(issue.error ? "error" : "warning")}): {issue.message}";
                if (issue.error)
                {
                    Debug.LogError(line);
                }
                else
                {
                    Debug.LogWarning(line);
                }
            }
        }
    }
}
