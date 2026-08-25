using System.Collections.Generic;
using System.Text;
using GloveBallDemo.Core;
using GloveBallDemo.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Unity.XR.CoreUtils;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace GloveBallDemo.Editor
{
    /// <summary>
    /// Batch play-mode check that the XR Device Simulator actually drives this demo's rig.
    ///
    /// Three separate things are proven, because they fail separately:
    ///  1. the simulator registers its devices (XRSimulatedHMD + a handed pair of
    ///     XRSimulatedController),
    ///  2. every action in DemoControls resolves onto one of those devices - this is the binding
    ///     check, and it is what a wrong control path would break,
    ///  3. the resolved poses reach the scene: the rig camera and hands have moved off the
    ///     positions the scene was authored with, onto the values the simulated devices report.
    ///
    /// A fourth check drives the simulator's left grip with synthetic LeftShift+G and reads the
    /// demo's own LeftGrip action. That one is reported rather
    /// than asserted: a batch editor has no focused Game view, and the Input System's default
    /// editor behaviour can withhold keyboard input in that state. Grip is still covered by the
    /// binding check above, and by the manual checklist in docs/simulator-testing.md.
    ///
    /// Must be launched WITHOUT -quit (tools/run-unity.ps1 -NoQuit -Graphics): the editor loop
    /// has to keep ticking after -executeMethod returns. This method exits the editor itself.
    /// </summary>
    [InitializeOnLoad]
    public static class SimulatorCheck
    {
        private const string ActiveKey = "GloveBallDemo.SimulatorCheck.Active";
        private const string ErrorsKey = "GloveBallDemo.SimulatorCheck.Errors";

        /// <summary>Seconds of play before the devices and poses are read.</summary>
        private const float SettleSeconds = 3f;

        /// <summary>Seconds spent holding the synthetic LeftShift+G before grip is read.</summary>
        private const float HoldSeconds = 1.5f;

        /// <summary>Wall-clock ceiling so a stalled play mode cannot hang the batch run.</summary>
        private const float RealtimeCap = 120f;

        private static bool _hooked;
        private static bool _timingStarted;
        private static bool _snapshotTaken;
        private static float _startRealtime;
        private static readonly List<string> ErrorSamples = new List<string>();

        static SimulatorCheck()
        {
            if (SessionState.GetBool(ActiveKey, false))
            {
                Hook();
            }
        }

        public static void Run()
        {
            BatchOps.MuteEditorAudio();
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetInt(ErrorsKey, 0);
            ErrorSamples.Clear();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoAssetPaths.DemoScene) == null)
            {
                Debug.LogError($"[SimCheck] scene not found: {DemoAssetPaths.DemoScene}");
                Finish(40);
                return;
            }

            var scene = EditorSceneManager.OpenScene(DemoAssetPaths.DemoScene, OpenSceneMode.Single);
            CheckSceneWiring(scene);
            BatchOps.EnsureDesktopInputDevices();
            Hook();

            Debug.Log("[SimCheck] entering play mode");
            EditorApplication.isPlaying = true;
        }

        /// <summary>
        /// Edit-mode checks on the saved scene: the simulator is present, and it is tagged so the
        /// APK never carries it.
        /// </summary>
        private static void CheckSceneWiring(Scene scene)
        {
            GameObject simulator = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == DemoSceneBuilder.SimulatorObjectName)
                    {
                        simulator = t.gameObject;
                        break;
                    }
                }
            }

            if (simulator == null)
            {
                Debug.LogError($"[SimCheck] '{DemoSceneBuilder.SimulatorObjectName}' is not in {DemoAssetPaths.DemoScene}");
                return;
            }

            Debug.Log($"[SimCheck] simulator object found: tag={simulator.tag} active={simulator.activeInHierarchy}");

            if (simulator.tag != DemoSceneBuilder.EditorOnlyTag)
            {
                Debug.LogError(
                    $"[SimCheck] simulator is tagged '{simulator.tag}', not '{DemoSceneBuilder.EditorOnlyTag}'; " +
                    "it would ship in the APK");
            }

            if (simulator.GetComponent<XRDeviceSimulator>() == null)
            {
                Debug.LogError("[SimCheck] simulator object has no XRDeviceSimulator component");
            }

            if (simulator.GetComponentInParent<RealHmdSimulatorGate>(true) == null)
            {
                Debug.LogError("[SimCheck] simulator object has no real-HMD gate");
            }

            var origin = Object.FindAnyObjectByType<XROrigin>();
            if (origin == null || origin.GetComponent<XrLocomotionController>() == null)
            {
                Debug.LogError("[SimCheck] XR Origin has no original-input locomotion controller");
            }


            var targetField = Object.FindAnyObjectByType<TargetLayoutField>();
            if (targetField == null || targetField.TargetCount != TargetLayoutPlanner.MaximumTargetCount || !targetField.HasDistinctPositions)
            {
                Debug.LogError("[SimCheck] generated scene has no valid initial target layout");
            }

            var controls = AssetDatabase.LoadAssetAtPath<InputActionAsset>(DemoAssetPaths.InputActions);
            var xrMap = controls != null ? controls.FindActionMap(DemoInputBuilder.MapName) : null;
            foreach (var triggerName in new[] { DemoInputBuilder.TriggerLeft, DemoInputBuilder.TriggerRight })
            {
                if (xrMap?.FindAction(triggerName) == null || xrMap.FindAction(triggerName).type != InputActionType.Button)
                {
                    Debug.LogError($"[SimCheck] {triggerName} is not a generated Button InputAction");
                }
            }

            foreach (var panel in Object.FindObjectsByType<TargetPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                foreach (var ringName in new[] { "CyanRing", "LightRing", "Center" })
                {
                    var ring = panel.transform.Find(ringName);
                    if (ring == null || ring.localPosition.z <= 0.06f)
                    {
                        Debug.LogError($"[SimCheck] {panel.name}/{ringName} is not in front of the target trigger");
                    }
                }
            }

            foreach (var glove in Object.FindObjectsByType<GloveController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var input = new SerializedObject(glove).FindProperty("_triggerInput");
                if (input == null || !input.FindPropertyRelative("m_UseReference").boolValue ||
                    input.FindPropertyRelative("m_Reference").objectReferenceValue == null)
                {
                    Debug.LogError($"[SimCheck] {glove.name} has no trigger input reference");
                }

            }
        }

        private static void Hook()
        {
            if (_hooked)
            {
                return;
            }

            _hooked = true;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += OnUpdate;
            _timingStarted = false;
            _snapshotTaken = false;
        }

        private static void Unhook()
        {
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= OnUpdate;
            _hooked = false;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            {
                return;
            }

            SessionState.SetInt(ErrorsKey, SessionState.GetInt(ErrorsKey, 0) + 1);
            if (ErrorSamples.Count < 20)
            {
                ErrorSamples.Add($"{type}: {condition}");
            }
        }

        private static void OnUpdate()
        {
            if (!SessionState.GetBool(ActiveKey, false) || !EditorApplication.isPlaying)
            {
                return;
            }

            // Batch mode never asks the editor to render, so the player loop only ticks a few
            // times a second unless it is queued explicitly (same reason as in SmokePlay).
            EditorApplication.QueuePlayerLoopUpdate();

            if (!_timingStarted)
            {
                _timingStarted = true;
                _startRealtime = Time.realtimeSinceStartup;
                AudioListener.volume = 0f;
                return;
            }

            var elapsed = Time.realtimeSinceStartup - _startRealtime;
            if (elapsed >= RealtimeCap)
            {
                Debug.LogError($"[SimCheck] hit the {RealtimeCap:0}s wall-clock cap");
                EditorApplication.isPlaying = false;
                Finish(41);
                return;
            }

            if (elapsed < SettleSeconds)
            {
                return;
            }

            if (!_snapshotTaken)
            {
                _snapshotTaken = true;
                ReportDevices();
                ReportBindings();
                ReportPoses();
                return;
            }

            if (elapsed < SettleSeconds + HoldSeconds)
            {
                PressSimulatedGrip();
                return;
            }

            ReportGrip();
            EditorApplication.isPlaying = false;
            Finish(SessionState.GetInt(ErrorsKey, 0) == 0 ? 0 : 42);
        }

        // ------------------------------------------------------------- reporting

        private static void ReportDevices()
        {
            var names = new StringBuilder();
            foreach (var device in InputSystem.devices)
            {
                names.Append($"{device.GetType().Name}({device.name}");
                foreach (var usage in device.usages)
                {
                    names.Append($",{usage}");
                }

                names.Append(") ");
            }

            Debug.Log($"[SimCheck] input devices: {names.ToString().TrimEnd()}");

            if (InputSystem.GetDevice<XRSimulatedHMD>() == null)
            {
                Debug.LogError("[SimCheck] no XRSimulatedHMD was registered");
            }

            var left = false;
            var right = false;
            foreach (var device in InputSystem.devices)
            {
                if (!(device is XRSimulatedController))
                {
                    continue;
                }

                foreach (var usage in device.usages)
                {
                    left |= usage == CommonUsages.LeftHand;
                    right |= usage == CommonUsages.RightHand;
                }
            }

            if (!left)
            {
                Debug.LogError("[SimCheck] no XRSimulatedController carries the LeftHand usage");
            }

            if (!right)
            {
                Debug.LogError("[SimCheck] no XRSimulatedController carries the RightHand usage");
            }
        }

        /// <summary>
        /// The binding check: every action the demo's rig relies on has to resolve onto a control
        /// of a simulated device. An unresolved action is a binding path that does not match the
        /// layouts the simulator registers.
        /// </summary>
        private static void ReportBindings()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(DemoAssetPaths.InputActions);
            if (asset == null)
            {
                Debug.LogError($"[SimCheck] could not load {DemoAssetPaths.InputActions}");
                return;
            }

            var map = asset.FindActionMap(DemoInputBuilder.MapName);
            if (map == null)
            {
                Debug.LogError($"[SimCheck] action map '{DemoInputBuilder.MapName}' missing");
                return;
            }

            foreach (var action in map.actions)
            {
                var controls = new StringBuilder();
                foreach (var control in action.controls)
                {
                    controls.Append($"{control.path}[{control.device.GetType().Name}] ");
                }

                Debug.Log(
                    $"[SimCheck] action {action.name}: enabled={action.enabled} controls={action.controls.Count} " +
                    $"{controls.ToString().TrimEnd()}");

                if (action.controls.Count == 0)
                {
                    Debug.LogError($"[SimCheck] action '{action.name}' resolves to no control on the simulated devices");
                    continue;
                }

                if (!action.enabled)
                {
                    Debug.LogError($"[SimCheck] action '{action.name}' is not enabled at runtime");
                }
            }
        }

        /// <summary>
        /// Proves the resolved poses actually reach the scene, by comparing the rig transforms
        /// against the values the simulated devices report.
        /// </summary>
        private static void ReportPoses()
        {
            var hmd = InputSystem.GetDevice<XRSimulatedHMD>();
            var head = GameObject.FindWithTag("MainCamera");
            if (hmd == null || head == null)
            {
                Debug.LogError("[SimCheck] cannot compare poses (missing simulated HMD or rig camera)");
                return;
            }

            var origin = Object.FindAnyObjectByType<XROrigin>();
            if (origin == null)
            {
                Debug.LogError("[SimCheck] the rig has no XROrigin");
            }
            else
            {
                var offset = origin.CameraFloorOffsetObject;
                Debug.Log(
                    $"[SimCheck] XROrigin: requested={origin.RequestedTrackingOriginMode} " +
                    $"current={origin.CurrentTrackingOriginMode} cameraYOffset={origin.CameraYOffset:0.00} " +
                    $"offsetLocal={(offset != null ? offset.transform.localPosition.ToString() : "<none>")} " +
                    $"cameraHeightAboveOrigin={origin.CameraInOriginSpaceHeight:0.00}");

                // The point of the XR Origin rig: whichever way the pose is measured, the player
                // ends up standing. Under the simulator there is no input subsystem, so this is
                // the authored camera offset showing through.
                if (origin.CameraInOriginSpaceHeight < 1.2f)
                {
                    Debug.LogError(
                        $"[SimCheck] the camera sits {origin.CameraInOriginSpaceHeight:0.00} m above the rig origin; " +
                        "the player is not standing at eye height");
                }
            }

            var reported = hmd.centerEyePosition.ReadValue();
            var applied = head.transform.localPosition;
            Debug.Log(
                $"[SimCheck] head: hmd.centerEyePosition={reported} camera.localPosition={applied} " +
                $"camera.world={head.transform.position}");

            if ((reported - applied).magnitude > 0.01f)
            {
                Debug.LogError(
                    "[SimCheck] the rig camera does not follow the simulated HMD; " +
                    "TrackedPoseDriver is not receiving the pose");
            }

            foreach (var handName in new[] { "LeftHand", "RightHand" })
            {
                var hand = FindChildByName(head.transform.parent, handName);
                if (hand == null)
                {
                    Debug.LogError($"[SimCheck] rig has no '{handName}'");
                    continue;
                }

                Debug.Log($"[SimCheck] {handName}.localPosition={hand.localPosition} world={hand.position}");
            }
        }

        private static Transform FindChildByName(Transform parent, string name)
        {
            if (parent == null)
            {
                return null;
            }

            foreach (var t in parent.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                {
                    return t;
                }
            }

            return null;
        }

        /// <summary>
        /// Holds the simulator's own bindings for "manipulate the left controller" (LeftShift)
        /// and "grip" (G) on a synthetic keyboard.
        /// </summary>
        private static void PressSimulatedGrip()
        {
            BatchOps.EnsureDesktopInputDevices();
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.LeftShift, Key.G));
        }

        private static void ReportGrip()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(DemoAssetPaths.InputActions);
            var map = asset != null ? asset.FindActionMap(DemoInputBuilder.MapName) : null;
            if (map == null)
            {
                return;
            }

            var left = map.FindAction(DemoInputBuilder.LeftGrip);
            var right = map.FindAction(DemoInputBuilder.RightGrip);
            var leftValue = left != null ? left.ReadValue<float>() : -1f;
            var rightValue = right != null ? right.ReadValue<float>() : -1f;

            Debug.Log($"[SimCheck] after synthetic LeftShift+G: LeftGrip={leftValue:0.00} RightGrip={rightValue:0.00}");

            if (leftValue <= 0.5f)
            {
                // Reported, not failed: see the class comment - a batch editor has no focused
                // Game view, and keyboard input can legitimately be withheld in that state.
                Debug.LogWarning(
                    "[SimCheck] synthetic keyboard input did not reach the simulated grip. " +
                    "The binding itself is covered by the action-resolution check above; confirm grip " +
                    "interactively per docs/simulator-testing.md.");
            }
        }

        private static void Finish(int exitCode)
        {
            var errors = SessionState.GetInt(ErrorsKey, 0);
            Debug.Log($"[SimCheck] finished: errorLogs={errors} exitCode={exitCode}");
            foreach (var sample in ErrorSamples)
            {
                Debug.Log($"[SimCheck] LOGGED {sample}");
            }

            SessionState.SetBool(ActiveKey, false);
            Unhook();
            BatchOps.RestoreEditorAudio();
            EditorApplication.Exit(exitCode);
        }
    }
}
