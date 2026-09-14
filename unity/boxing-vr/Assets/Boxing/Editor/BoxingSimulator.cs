using System;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace Hapbeat.Boxing.Editor
{
    [InitializeOnLoad]
    public static class BoxingSimulator
    {
        public const string PrefabPath = "Assets/Boxing/Editor/Simulator/XR Interaction Simulator.prefab";
        private static string PreferenceKey => "Hapbeat.Boxing.Simulator." + Application.dataPath;
        public static bool Enabled => EditorPrefs.GetBool(PreferenceKey, false);

        static BoxingSimulator() => EditorApplication.playModeStateChanged += OnPlayMode;

        [MenuItem("Hapbeat Boxing/Editor Input/Simulator")]
        public static void Enable()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before changing Editor input.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) throw new InvalidOperationException("Simulator prefab missing.");
            EditorPrefs.SetBool(PreferenceKey, true); ApplyMode();
            Debug.Log("BOXING_EDITOR_INPUT: Simulator (native OpenXR startup disabled; Android unchanged)");
        }

        [MenuItem("Hapbeat Boxing/Editor Input/Air Link")]
        public static void Disable()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before changing Editor input.");
            EditorPrefs.SetBool(PreferenceKey, false); ApplyMode();
            Debug.Log("BOXING_EDITOR_INPUT: Air Link (native OpenXR startup enabled)");
        }

        [MenuItem("Hapbeat Boxing/Editor Input/Simulator", true)]
        private static bool CanEnable() { Menu.SetChecked("Hapbeat Boxing/Editor Input/Simulator", Enabled); return !EditorApplication.isPlayingOrWillChangePlaymode; }
        [MenuItem("Hapbeat Boxing/Editor Input/Air Link", true)]
        private static bool CanDisable() { Menu.SetChecked("Hapbeat Boxing/Editor Input/Air Link", !Enabled); return !EditorApplication.isPlayingOrWillChangePlaymode; }

        // Editor selection is local to this project on this PC, never a player setting.
        public static void ApplyMode()
        {
            var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (xr == null) throw new InvalidOperationException("Standalone XR settings missing.");
            xr.InitManagerOnStart = !Enabled;
            EditorUtility.SetDirty(xr); AssetDatabase.SaveAssets();
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (Application.isBatchMode) return; // Automated tasks explicitly own their devices.
            if (state == PlayModeStateChange.ExitingEditMode) ApplyMode();
            if (state == PlayModeStateChange.EnteredPlayMode && Enabled) CreateInstance();
        }

        public static XRInteractionSimulator CreateInstance()
        {
            if (XRInteractionSimulator.instance != null) return XRInteractionSimulator.instance;
            var displaySystems = new System.Collections.Generic.List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displaySystems);
            if (displaySystems.Exists(display => display.running))
                throw new InvalidOperationException("Native XR display is running. Stop Play and select Simulator before starting again.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) throw new InvalidOperationException("Simulator prefab missing.");
            var input = UnityEngine.Object.FindAnyObjectByType<BoxingInput>();
            if (input != null)
            {
                // Simulated poses are device-relative; real OpenXR poses use the floor.
                // This adjustment is Play-only and never saved to the authoritative scene.
                input.origin.RequestedTrackingOriginMode = Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Device;
                input.origin.CameraYOffset = 1.65f;
                input.origin.CameraFloorOffsetObject.transform.localPosition = Vector3.up * 1.65f;
                // A zero simulated pose need not perform a Value action. Reset the
                // driver's initial-pose cache so the scene's preview height is not added twice.
                input.headDriver.enabled = false;
                input.headCamera.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                input.headDriver.enabled = true;
                input.SelectMode(BoxingInputMode.Controllers);
            }
            var instance = UnityEngine.Object.Instantiate(prefab);
            instance.name = "Boxing XR Interaction Simulator (Editor only)";
            var simulator = instance.GetComponent<XRInteractionSimulator>();
            return simulator;
        }

        public static void AssertExcludedFromBuild(UnityEditor.Build.Reporting.BuildReport report)
        {
            if (report.packedAssets.Length == 0) throw new InvalidOperationException("Detailed packed asset report missing.");
            foreach (var packed in report.packedAssets)
            foreach (var item in packed.contents)
                if (item.sourceAssetPath.StartsWith("Assets/Boxing/Editor/Simulator/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Editor simulator asset leaked into player: " + item.sourceAssetPath);
            Debug.Log("BOXING_SIMULATOR_BUILD: no imported simulator assets in player");
        }

        public static void ValidateAssets()
        {
            foreach (string file in System.IO.Directory.GetFiles("Assets/Boxing/Editor/Simulator", "*", System.IO.SearchOption.AllDirectories))
            {
                if (!file.EndsWith(".prefab") && !file.EndsWith(".asset")) continue;
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(System.IO.File.ReadAllText(file), @"guid: ([a-f0-9]{32})"))
                    if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(match.Groups[1].Value)))
                        throw new InvalidOperationException("Unresolved simulator dependency: " + file + " " + match.Groups[1].Value);
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null || prefab.GetComponent<XRInteractionSimulator>() == null) throw new InvalidOperationException("Simulator prefab missing.");
            foreach (var node in prefab.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(node.gameObject) != 0) throw new InvalidOperationException("Missing simulator script.");
        }

        public static void OpenReady()
        {
            BoxingProject.Validate(); ValidateAssets(); ApplyMode(); BoxingSimulatorWindow.Open();
        }
    }

    public sealed class BoxingSimulatorWindow : EditorWindow
    {
        [MenuItem("Hapbeat Boxing/Editor Input/Controls")]
        public static void Open() => GetWindow<BoxingSimulatorWindow>("Boxing XR Controls");
        private void OnGUI()
        {
            GUILayout.Label(BoxingSimulator.Enabled ? "XR Interaction Simulator" : "Air Link / Native OpenXR", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Use Simulator")) BoxingSimulator.Enable();
                if (GUILayout.Button("Use Air Link")) BoxingSimulator.Disable();
            }
            EditorGUILayout.HelpBox("Press Play, then focus the Game view. Use INPUT: Controllers in the boxing menu. Stop Play before changing the Editor input mode.", MessageType.Info);
            GUILayout.Label("Tab: cycle FPS / device control\nH: head; [ / ]: select left / right device\nWASD: move; Q / E: down / up\nRight-mouse drag / arrow keys: rotate selected device\n1: A/X; 2: B/Y\nR: reset; Escape: boxing menu; Enter: confirm", EditorStyles.wordWrappedLabel);
            EditorGUILayout.HelpBox("Simulator input is Unity's standard XRI sample. It is not the game's simplified Desktop mode. The simulator prefab and control assets are Editor-only and are not included in APKs.", MessageType.None);
        }
    }
}
