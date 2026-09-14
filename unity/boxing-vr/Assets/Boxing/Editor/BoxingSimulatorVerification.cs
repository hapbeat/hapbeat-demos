using System;
using System.IO;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

namespace Hapbeat.Boxing.Editor
{
    [InitializeOnLoad]
    public static class BoxingSimulatorVerification
    {
        private const string Active = "Boxing.SimulatorVerification.Active";
        private static double started;
        private static bool initialized, finished, tracked, headMoved, leftMoved, rightMoved, confirm, menu, lost, recovered;
        private static int errors;
        private static BoxingGame game;
        private static XRInteractionSimulator simulator;
        private static Keyboard keyboard;
        private static Vector3 initialHead, initialLeft, initialRight;
        static BoxingSimulatorVerification() { if (SessionState.GetBool(Active, false)) Hook(); }
        public static void Run()
        {
            BoxingProject.Validate();
            var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            SessionState.SetBool(Active + ".RestoreXR", xr.InitManagerOnStart);
            xr.InitManagerOnStart = false; EditorUtility.SetDirty(xr); AssetDatabase.SaveAssets();
            SessionState.SetBool(Active, true); started = EditorApplication.timeSinceStartup; Hook();
            EditorApplication.update += Enter;
        }
        private static void Enter()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup - started < 8) return;
            EditorApplication.update -= Enter; EditorApplication.isPlaying = true;
        }
        private static void Hook()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
        }
        private static void OnLog(string text, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++; }
        private static void Tick()
        {
            if (finished || !EditorApplication.isPlaying) return;
            if (!initialized)
            {
                game = UnityEngine.Object.FindAnyObjectByType<BoxingGame>();
                if (game == null || game.Opponent == null) return;
                SessionState.SetInt(Active + ".InputFocus", (int)InputSystem.settings.editorInputBehaviorInPlayMode);
                SessionState.SetInt(Active + ".Background", (int)InputSystem.settings.backgroundBehavior);
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                simulator = BoxingSimulator.CreateInstance(); keyboard = InputSystem.AddDevice<Keyboard>();
                game.menu.enabled = false; game.input.SelectMode(BoxingInputMode.Controllers);
                // Batch has no focused Game view. Model the host's focus/resume
                // notifications without bypassing tracking or gameplay pause logic.
                game.SendMessage("FocusChanged", true, SendMessageOptions.RequireReceiver);
                game.SendMessage("OnApplicationPause", false, SendMessageOptions.RequireReceiver);
                game.StartRound();
                var driver = game.gameObject.AddComponent<BoxingSmokeDriver>(); driver.Sample = Drive;
                initialized = true; started = EditorApplication.timeSinceStartup;
            }
            if (EditorApplication.timeSinceStartup - started < 12) return;
            bool height = initialHead.y > 1.6f && initialHead.y < 1.7f;
            bool fighting = !game.Paused && game.Round.Phase == BoxingPhase.Fighting && game.Round.TimeLeft < game.tuning.roundSeconds - 1;
            string result = $"tracked={tracked} head={headMoved} left={leftMoved} right={rightMoved} height={initialHead.y:0.00} confirm={confirm} menu={menu} lost={lost} recovered={recovered} fighting={fighting} override={game.input.HasOverride} sends={game.feedback.Sends} errors={errors}";
            BoxingVerification.Render(game.input.headCamera, "Logs/boxing-simulator-view.png");
            Finish(tracked && headMoved && leftMoved && rightMoved && height && confirm && menu && lost && recovered && fighting && !game.input.HasOverride && game.feedback.Sends == 0 && errors == 0, result);
        }
        private static void Drive()
        {
            if (finished) return;
            // Batch Editor has no focused Game view to pump native player input.
            // Process queued keyboard events before the standard simulator's Update.
            InputSystem.Update();
            float time = (float)(EditorApplication.timeSinceStartup - started);
            var pose = game.input.Current;
            if (time < 1)
            {
                tracked |= pose.valid;
                initialHead = pose.head; initialLeft = pose.left; initialRight = pose.right;
            }
            else
            {
                if (time > 1.3f && time < 1.8f) headMoved |= Vector3.Distance(pose.head, initialHead) > 0.02f && Vector3.Distance(pose.left, initialLeft) < 0.01f && Vector3.Distance(pose.right, initialRight) < 0.01f;
                if (time > 2.3f && time < 2.8f) leftMoved |= Vector3.Distance(pose.left, initialLeft) > 0.02f && Vector3.Distance(pose.right, initialRight) < 0.01f;
                if (time > 3.3f && time < 3.8f) rightMoved |= Vector3.Distance(pose.right, initialRight) > 0.02f;
            }
            simulator.targetedDeviceInput = time < 2 ? TargetedDevices.HMD : time < 3 ? TargetedDevices.LeftDevice : TargetedDevices.RightDevice;
            bool move = time > 1 && time < 1.2f || time > 2 && time < 2.2f || time > 3 && time < 3.2f;
            var state = move ? new KeyboardState(Key.W) : time > 4 && time < 4.3f ? new KeyboardState(Key.Digit1) : time > 5 && time < 5.3f ? new KeyboardState(Key.Digit2) : new KeyboardState();
            InputSystem.QueueStateEvent(keyboard, state);
            confirm |= game.input.ConfirmPressed; menu |= game.input.MenuPressed;
            simulator.rightControllerIsTracked = !(time > 6 && time < 7);
            if (time > 6.2f && time < 7) lost |= !pose.valid;
            if (time > 8) recovered |= pose.valid;
        }
        private static void Finish(bool pass, string result)
        {
            finished = true; SessionState.SetBool(Active, false); EditorApplication.update -= Tick;
            if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.editorInputBehaviorInPlayMode = (InputSettings.EditorInputBehaviorInPlayMode)SessionState.GetInt(Active + ".InputFocus", 0);
            InputSystem.settings.backgroundBehavior = (InputSettings.BackgroundBehavior)SessionState.GetInt(Active + ".Background", 0);
            var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            xr.InitManagerOnStart = SessionState.GetBool(Active + ".RestoreXR", true); EditorUtility.SetDirty(xr); AssetDatabase.SaveAssets();
            File.WriteAllText("Logs/boxing-simulator-result.txt", (pass ? "PASS " : "FAIL ") + result);
            Debug.Log("BOXING_SIMULATOR " + (pass ? "PASS " : "FAIL ") + result);
            EditorApplication.isPlaying = false; EditorApplication.delayCall += () => EditorApplication.Exit(pass ? 0 : 1);
        }
    }
}
