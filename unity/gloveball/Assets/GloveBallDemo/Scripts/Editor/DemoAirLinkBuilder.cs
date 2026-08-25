// Windows OpenXR configuration for testing the demo through Meta Horizon Link (Air Link).
// Kept separate from DemoAndroidBuilder so switching to fast Editor iteration never weakens
// the Android/Quest distribution settings used by BuildApk.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
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

namespace GloveBallDemo.Editor
{
    /// <summary>
    /// Configures Windows Standalone OpenXR so a Quest connected through Meta Horizon Link can
    /// drive Play Mode with its real HMD and Touch controllers.
    /// </summary>
    public static class DemoAirLinkBuilder
    {
        private const string XrSettingsDir = "Assets/XR";
        private const string OpenXrLoaderTypeName = "UnityEngine.XR.OpenXR.OpenXRLoader";

        [MenuItem("GloveBall Demo/Configure Air Link Play Mode")]
        public static void ConfigureAirLinkPlayMode()
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
            {
                throw new InvalidOperationException(
                    "Could not switch the active build target to Windows Standalone");
            }

            ConfigureStandaloneXr();
            ConfigureStandalonePlayer();
            AssetDatabase.SaveAssets();
            ValidateStandaloneOpenXr();

            Debug.Log(
                "[AirLink] Windows Standalone OpenXR configured. Connect Meta Horizon Link, " +
                "make Meta the active OpenXR runtime, then enter Play Mode.");
        }

        /// <summary>Assigns the OpenXR loader and Meta controller profiles for Windows Play Mode.</summary>
        public static void ConfigureStandaloneXr()
        {
            const BuildTargetGroup targetGroup = BuildTargetGroup.Standalone;
            var perBuildTarget = GetOrCreateGeneralSettings();

            if (!perBuildTarget.HasSettingsForBuildTarget(targetGroup))
            {
                perBuildTarget.CreateDefaultSettingsForBuildTarget(targetGroup);
                Debug.Log("[AirLink] created XRGeneralSettings for Standalone");
            }

            if (!perBuildTarget.HasManagerSettingsForBuildTarget(targetGroup))
            {
                perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(targetGroup);
                Debug.Log("[AirLink] created XRManagerSettings for Standalone");
            }

            var generalSettings = perBuildTarget.SettingsForBuildTarget(targetGroup);
            generalSettings.InitManagerOnStart = true;

            var manager = perBuildTarget.ManagerSettingsForBuildTarget(targetGroup);
            if (!XRPackageMetadataStore.IsLoaderAssigned(OpenXrLoaderTypeName, targetGroup) &&
                !XRPackageMetadataStore.AssignLoader(manager, OpenXrLoaderTypeName, targetGroup))
            {
                throw new InvalidOperationException(
                    "Could not assign the OpenXR loader for Windows Standalone");
            }

            EditorUtility.SetDirty(perBuildTarget);
            EditorUtility.SetDirty(generalSettings);
            EditorUtility.SetDirty(manager);

            FeatureHelpers.RefreshFeatures(targetGroup);
            var openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(targetGroup);
            if (openXr == null)
            {
                throw new InvalidOperationException("No OpenXRSettings found for Windows Standalone");
            }

            // OculusTouch is the portable baseline exposed by PC OpenXR runtimes. The Meta
            // profiles retain the native Touch Plus/Pro layouts when Horizon Link exposes them.
            EnableFeature<OculusTouchControllerProfile>(openXr);
            EnableFeature<MetaQuestTouchPlusControllerProfile>(openXr);
            EnableFeature<MetaQuestTouchProControllerProfile>(openXr);

            EditorUtility.SetDirty(openXr);
            AssetDatabase.SaveAssets();

            Debug.Log("[AirLink] Standalone loaders: " +
                      string.Join(", ", manager.activeLoaders.Select(
                          loader => loader == null ? "<null>" : loader.name)));
            Debug.Log("[AirLink] Standalone OpenXR features: " +
                      string.Join(", ", openXr.GetFeatures().Where(
                          feature => feature.enabled).Select(feature => feature.name)));
        }

        private static void ConfigureStandalonePlayer()
        {
            PlayerSettings.productName = DemoAndroidBuilder.ProductName;

            // Horizon Link's OpenXR runtime supports D3D11. Pinning it keeps Play Mode independent
            // of machine-specific automatic graphics API ordering.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });

            Debug.Log("[AirLink] Standalone graphics APIs: " +
                      string.Join(", ", PlayerSettings.GetGraphicsAPIs(
                          BuildTarget.StandaloneWindows64)));
        }

        /// <summary>Fails configuration when Unity's Standalone OpenXR validation has errors.</summary>
        public static void ValidateStandaloneOpenXr()
        {
            var issues = new List<OpenXRFeature.ValidationRule>();
            OpenXRProjectValidation.GetCurrentValidationIssues(
                issues, BuildTargetGroup.Standalone);

            if (issues.Count == 0)
            {
                Debug.Log("[AirLink] Standalone OpenXR validation: no issues");
                return;
            }

            var errors = new List<string>();
            foreach (var issue in issues)
            {
                var line = $"[AirLink] OpenXR validation " +
                           $"({(issue.error ? "error" : "warning")}): {issue.message}";
                if (issue.error)
                {
                    errors.Add(issue.message);
                    Debug.LogError(line);
                }
                else
                {
                    Debug.LogWarning(line);
                }
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Standalone OpenXR validation failed: " + string.Join(" | ", errors));
            }
        }

        private static void EnableFeature<T>(OpenXRSettings settings) where T : OpenXRFeature
        {
            var feature = settings.GetFeature<T>();
            if (feature == null)
            {
                throw new InvalidOperationException(
                    $"Standalone OpenXR feature not found: {typeof(T).Name}");
            }

            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }

        private static XRGeneralSettingsPerBuildTarget GetOrCreateGeneralSettings()
        {
            EditorBuildSettings.TryGetConfigObject<XRGeneralSettingsPerBuildTarget>(
                XRGeneralSettings.k_SettingsKey, out var settings);

            if (settings == null)
            {
                var guid = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget")
                    .FirstOrDefault();
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
                AssetDatabase.CreateAsset(
                    settings, XrSettingsDir + "/XRGeneralSettingsPerBuildTarget.asset");
                AssetDatabase.SaveAssets();
                Debug.Log("[AirLink] created XRGeneralSettingsPerBuildTarget");
            }

            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, settings, true);
            return settings;
        }
    }
}
