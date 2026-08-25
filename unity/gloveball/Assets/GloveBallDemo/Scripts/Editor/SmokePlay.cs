using System.Collections.Generic;
using GloveBallDemo.Runtime;
using Hapbeat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GloveBallDemo.Editor
{
    /// <summary>
    /// Batch play-mode smoke test: enters play mode on Demo.unity, lets the game loop run, then
    /// reports the wave progress and fails the run if anything logged an error.
    ///
    /// Must be launched WITHOUT -quit (see tools/run-unity.ps1 -NoQuit): the editor loop has to
    /// keep ticking after the -executeMethod call returns. This method exits the editor itself.
    /// </summary>
    [InitializeOnLoad]
    public static class SmokePlay
    {
        private const string ActiveKey = "GloveBallDemo.SmokePlay.Active";
        private const string ErrorsKey = "GloveBallDemo.SmokePlay.Errors";
        private const string DurationKey = "GloveBallDemo.SmokePlay.Duration";
        private const string HandsKey = "GloveBallDemo.SmokePlay.ScriptedHands";

        private const float DefaultDuration = 30f;

        // Minimum play the scripted-hands run has to show before it counts as a pass. Low enough
        // to survive an unlucky wave, high enough that a single fluke cannot satisfy it.
        private const int RequiredCatches = 3;
        private const int RequiredThrows = 3;
        private const int RequiredTargetHits = 1;
        private const int RequiredCombo = 1;

        /// <summary>Wall-clock ceiling so a stalled play mode cannot hang the batch run.</summary>
        private const float RealtimeCap = 300f;

        private static readonly List<string> ErrorSamples = new List<string>();
        private static float _playStartRealtime;
        private static float _playStartGameTime;
        private static bool _timingStarted;
        private static bool _hooked;
        private static ScriptedHandDriver _hands;

        /// <summary>Every gameplay event the relay funnelled, by kind.</summary>
        private static readonly Dictionary<DemoHapticEvent, int> EventTally =
            new Dictionary<DemoHapticEvent, int>();

        /// <summary>Stream sessions and mixed-in sources the SDK reported starting.</summary>
        private static int _sdkStreamStarts;

        // Totals over the whole window, across however many runs the attract loop cycled through.
        private static int _catches;
        private static int _throws;
        private static int _targetHits;
        private static int _launcherHits;
        private static int _bestCombo;

        static SmokePlay()
        {
            // Domain reload on entering play mode re-runs this constructor; re-attach the hooks.
            if (SessionState.GetBool(ActiveKey, false))
            {
                Hook();
            }
        }

        public static void Run()
        {
            BatchOps.MuteEditorAudio();
            var duration = ParseDuration();
            var scriptedHands = HasFlag("-gbScriptedHands");
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetInt(ErrorsKey, 0);
            SessionState.SetFloat(DurationKey, duration);
            SessionState.SetBool(HandsKey, scriptedHands);
            ErrorSamples.Clear();

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DemoAssetPaths.DemoScene) == null)
            {
                Debug.LogError($"[SmokePlay] scene not found: {DemoAssetPaths.DemoScene}");
                Finish(20);
                return;
            }

            EditorSceneManager.OpenScene(DemoAssetPaths.DemoScene, OpenSceneMode.Single);
            // The scene now carries the XR Device Simulator, whose UI needs a keyboard and a
            // mouse to exist before it starts.
            BatchOps.EnsureDesktopInputDevices();
            Hook();

            Debug.Log($"[SmokePlay] entering play mode for {duration:0}s " +
                      $"(scriptedHands={scriptedHands}, endless={HasFlag("-gbEndless")})");
            EditorApplication.isPlaying = true;
        }

        private static float ParseDuration()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-gbSeconds" && float.TryParse(args[i + 1], out var parsed) && parsed > 0f)
                {
                    return parsed;
                }
            }

            return DefaultDuration;
        }

        private static bool HasFlag(string name)
        {
            foreach (var arg in System.Environment.GetCommandLineArgs())
            {
                if (arg == name)
                {
                    return true;
                }
            }

            return false;
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
        }

        private static void Unhook()
        {
            Application.logMessageReceived -= OnLog;
            EditorApplication.update -= OnUpdate;
            _hooked = false;
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            // The SDK announces every stream it opens. Counting those lines is the most direct
            // evidence available in a batch run that packets actually left the SDK - there is no
            // device on the network to answer, so nothing else confirms the send.
            if (type == LogType.Log && condition != null && condition.StartsWith("[Hapbeat]") &&
                (condition.Contains("Stream session begin") || condition.Contains("Stream source added")))
            {
                _sdkStreamStarts++;
            }

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
            if (!SessionState.GetBool(ActiveKey, false))
            {
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                return;
            }

            // In batch mode nothing asks the editor to render, so the player loop is only
            // stepped a handful of times per second unless it is queued explicitly. Without
            // this the game clock barely advances and no wave ever starts.
            EditorApplication.QueuePlayerLoopUpdate();

            if (!_timingStarted)
            {
                _timingStarted = true;
                _playStartRealtime = Time.realtimeSinceStartup;
                _playStartGameTime = Time.time;
                // Belt and braces on top of EditorUtility.audioMasterMute: an unattended batch
                // run must never make noise. Editor/batch only - the player build is untouched.
                AudioListener.volume = 0f;
                Debug.Log($"[SmokePlay] haptics live send: {(DemoHapticsGate.LiveSendEnabled ? "ON" : "OFF")}");
                if (HasFlag("-gbEndless"))
                {
                    var game = DemoGameController.Instance;
                    if (game == null)
                    {
                        Debug.LogError("[SmokePlay] cannot enable endless mode: no DemoGameController");
                    }
                    else
                    {
                        game.SetPlayMode(DemoPlayMode.EndlessRandom);
                        Debug.Log("[SmokePlay] play mode set to EndlessRandom");
                    }
                }
                BeginScriptedPlay();
                return;
            }

            // Sampled rather than read at the end: the run restarts every couple of minutes and
            // takes the score model with it.
            var live = DemoGameController.Instance;
            if (live != null && live.Score.Combo > _bestCombo)
            {
                _bestCombo = live.Score.Combo;
            }

            // Measured in game time: the batch editor runs the loop slower than real time.
            var elapsed = Time.time - _playStartGameTime;
            var wallClock = Time.realtimeSinceStartup - _playStartRealtime;
            if (elapsed < SessionState.GetFloat(DurationKey, DefaultDuration) && wallClock < RealtimeCap)
            {
                return;
            }

            if (wallClock >= RealtimeCap)
            {
                Debug.LogError($"[SmokePlay] hit the {RealtimeCap:0}s wall-clock cap after {elapsed:0.0}s of game time");
            }

            ReportProgress(elapsed);
            EditorApplication.isPlaying = false;
            Finish(SessionState.GetInt(ErrorsKey, 0) == 0 ? 0 : 21);
        }

        /// <summary>
        /// Spawns the scripted hand into the running scene and starts counting what it achieves.
        /// The driver is only ever added here, at runtime, so no scene or prefab refers to it.
        /// </summary>
        private static void BeginScriptedPlay()
        {
            if (!SessionState.GetBool(HandsKey, false))
            {
                return;
            }

            // The scripted driver moves the hand transforms in world space, which only works
            // while nothing else writes them. The XR Device Simulator registers simulated
            // controllers, and the rig's TrackedPoseDriver would then overwrite each hand every
            // frame with the simulated pose. Switching the simulator off removes its devices, so
            // the pose drivers fall silent and the driver has the hand to itself - the same
            // situation this test ran in before the simulator was added to the scene.
            var simulator = GameObject.Find(DemoSceneBuilder.SimulatorObjectName);
            if (simulator != null)
            {
                simulator.SetActive(false);
                Debug.Log("[SmokePlay] XR Device Simulator disabled for the scripted-hands run");
            }

            _catches = 0;
            _throws = 0;
            _targetHits = 0;
            _launcherHits = 0;
            _bestCombo = 0;
            _sdkStreamStarts = 0;
            EventTally.Clear();

            var relay = HapticEventRelay.Instance;
            if (relay == null)
            {
                Debug.LogError("[SmokePlay] no HapticEventRelay in the scene; cannot observe play");
                return;
            }

            // Counted off the haptic funnel rather than the controller's own tallies: those reset
            // with every run, and a 120 s window spans several runs. This also proves the funnel
            // itself fires, which is what phase 3 hangs its haptics on.
            relay.EventReported += OnHapticEvent;

            var go = new GameObject("ScriptedHands");
            _hands = go.AddComponent<ScriptedHandDriver>();
            Debug.Log("[SmokePlay] scripted hand driver attached");
        }

        private static void OnHapticEvent(DemoHapticEvent evt, Vector3 position, float gain)
        {
            EventTally.TryGetValue(evt, out var seen);
            EventTally[evt] = seen + 1;

            switch (evt)
            {
                case DemoHapticEvent.LeftGrab:
                case DemoHapticEvent.RightGrab:
                    _catches++;
                    break;
                case DemoHapticEvent.LeftRelease:
                case DemoHapticEvent.RightRelease:
                    _throws++;
                    break;
                case DemoHapticEvent.TargetHit:
                    _targetHits++;
                    break;
                case DemoHapticEvent.LauncherHit:
                    _launcherHits++;
                    break;
            }
        }

        private static void ReportProgress(float elapsed)
        {
            var controller = DemoGameController.Instance;
            if (controller == null)
            {
                Debug.LogError("[SmokePlay] no DemoGameController was alive in the scene");
                return;
            }

            var pool = controller.Pool;
            Debug.Log(
                $"[SmokePlay] ran {elapsed:0.0}s of game time over {Time.frameCount} frames; " +
                $"phase={controller.Phase.Phase} wave={controller.Phase.WaveIndex + 1} " +
                $"launched={controller.LaunchedCount} caught={controller.CaughtCount} thrown={controller.ThrownCount} " +
                $"playerHits={controller.PlayerHitCount} targetHits={controller.TargetHitCount} launcherHits={controller.LauncherHitCount} " +
                $"score={controller.Score.Score} " +
                $"poolTaken={(pool != null ? pool.TotalTaken : -1)} poolReturned={(pool != null ? pool.TotalReturned : -1)} " +
                $"poolActive={(pool != null ? pool.ActiveCount : -1)}");

            // Read off the pool, not the controller: the controller's tallies are per run and the
            // attract loop may have just started a fresh one.
            if (pool != null && pool.TotalTaken == 0)
            {
                Debug.LogError("[SmokePlay] no ball was ever launched; the wave loop did not run");
            }

            if (pool != null && pool.TotalReturned == 0)
            {
                Debug.LogError("[SmokePlay] no ball was ever returned to the pool");
            }

            ReportScriptedPlay();
        }

        /// <summary>
        /// With the scripted hand attached the run has to show real play, not just balls being
        /// fired into an empty court: catches, throws, at least one target hit, and a combo that
        /// actually incremented. Each shortfall is logged as an error, which fails the run.
        /// </summary>
        private static void ReportScriptedPlay()
        {
            if (!SessionState.GetBool(HandsKey, false))
            {
                return;
            }

            Debug.Log(
                $"[SmokePlay] scripted hands (totals over the window): catches={_catches} throws={_throws} " +
                $"targetHits={_targetHits} launcherHits={_launcherHits} bestCombo={_bestCombo} " +
                $"driverCatches={(_hands != null ? _hands.Catches : -1)} driverThrows={(_hands != null ? _hands.Throws : -1)}");

            Require(_catches, RequiredCatches, "catches");
            Require(_throws, RequiredThrows, "throws");
            Require(_targetHits, RequiredTargetHits, "target hits");
            Require(_bestCombo, RequiredCombo, "combo increments");

            ReportHaptics();
        }

        /// <summary>
        /// Two separate things are checked here, because they fail separately.
        ///
        /// The wiring check is deterministic: every event in the catalogue must have a trigger
        /// that resolves to an EventMap entry, whether or not it happened to fire this run. That
        /// is what catches a rename or a lost reference.
        ///
        /// The traffic check proves the SDK actually opened streams. It is reported per event
        /// kind rather than asserted per kind: a rare event (a launcher taking its third hit)
        /// not occurring inside a 120 s window is not a failure.
        /// </summary>
        private static void ReportHaptics()
        {
            var relay = HapticEventRelay.Instance;
            if (relay == null)
            {
                Debug.LogError("[SmokePlay] no HapticEventRelay alive; haptics were never wired");
                return;
            }

            var tally = new System.Text.StringBuilder();
            foreach (DemoHapticEvent evt in System.Enum.GetValues(typeof(DemoHapticEvent)))
            {
                EventTally.TryGetValue(evt, out var count);
                tally.Append($"{evt}={count} ");

                var wired = (HapbeatTriggerBase)relay.GetLoopTrigger(evt) ?? relay.GetHapbeatTrigger(evt);

                if (wired == null)
                {
                    Debug.LogError($"[SmokePlay] {evt} has no Hapbeat trigger bound");
                }
                else if (wired.ResolveEntry() == null)
                {
                    Debug.LogError($"[SmokePlay] {evt}'s Hapbeat trigger resolves to no EventMap entry");
                }
            }

            Debug.Log($"[SmokePlay] relay events: {tally.ToString().TrimEnd()}");
            Debug.Log(
                $"[SmokePlay] SDK stream starts observed: {_sdkStreamStarts} " +
                $"(live send {(DemoHapticsGate.LiveSendEnabled ? "ON" : "OFF")})");

            // Only meaningful when this run was allowed to send. With the gate closed the SDK is
            // switched off by design, so zero streams is the expected result, not a failure.
            if (DemoHapticsGate.LiveSendEnabled && _sdkStreamStarts <= 0)
            {
                Debug.LogError("[SmokePlay] the SDK never opened a stream; nothing was sent");
            }
        }

        private static void Require(int actual, int required, string label)
        {
            if (actual < required)
            {
                Debug.LogError($"[SmokePlay] only {actual} {label}; at least {required} were required");
            }
        }

        private static void Finish(int exitCode)
        {
            var errors = SessionState.GetInt(ErrorsKey, 0);
            Debug.Log($"[SmokePlay] finished: errorLogs={errors} exitCode={exitCode}");
            foreach (var sample in ErrorSamples)
            {
                Debug.Log($"[SmokePlay] LOGGED {sample}");
            }

            SessionState.SetBool(ActiveKey, false);
            Unhook();
            // EditorApplication.Exit skips EditorApplication.quitting, so the shared Game view
            // mute has to be handed back here or the developer's editor stays silent.
            BatchOps.RestoreEditorAudio();
            EditorApplication.Exit(exitCode);
        }
    }
}
