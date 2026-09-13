using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;

namespace Hapbeat.Boxing.Editor
{
    [InitializeOnLoad]
    public static class BoxingVerification
    {
        private const string Active = "Boxing.Smoke.Active";
        private static double started;
        private static bool initialized, finished;
        private static int errors;
        private static int progressBucket = -1;
        private static BoxingGame game;
        private static int softImpacts, hardImpacts, zones;
        private static float minGain = float.PositiveInfinity, maxGain;
        static BoxingVerification()
        {
            if (SessionState.GetBool(Active, false)) Hook();
        }
        private static void Hook()
        {
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
        }
        public static void Smoke()
        {
            BoxingProject.Validate();
            var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            SessionState.SetBool("Boxing.Smoke.RestoreXR", xr.InitManagerOnStart);
            xr.InitManagerOnStart = false;
            EditorUtility.SetDirty(xr); AssetDatabase.SaveAssets();
            SessionState.SetBool(Active, true); Hook();
            started = EditorApplication.timeSinceStartup;
            // Finish editor startup (including Search index creation) before a domain reload.
            EditorApplication.update += EnterAfterStartup;
        }
        private static void EnterAfterStartup()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup - started < 8) return;
            EditorApplication.update -= EnterAfterStartup;
            EditorApplication.isPlaying = true;
        }
        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
        }
        private static void Tick()
        {
            if (finished) return;
            if (!EditorApplication.isPlaying) return;
            if (!initialized)
            {
                game = UnityEngine.Object.FindAnyObjectByType<BoxingGame>();
                if (game == null || game.Opponent == null) return;
                game.feedback.forceSilent = true;
                game.menu.enabled = false;
                game.input.mode = BoxingInputMode.Desktop;
                game.input.SetTestPose(Pose(0, false));
                game.StartRound();
                started = EditorApplication.timeSinceStartup; initialized = true;
                game.feedback.Reported += impact => {
                    if (impact.hard) hardImpacts++; else softImpacts++;
                    zones |= 1 << (int)impact.zone;
                    minGain = Mathf.Min(minGain, impact.gain); maxGain = Mathf.Max(maxGain, impact.gain);
                };
                var driver = game.gameObject.AddComponent<BoxingSmokeDriver>();
                if (driver == null) { Finish(false, "cannot attach smoke input driver"); return; }
                driver.Sample = DriveInput;
            }
            float elapsed = (float)(EditorApplication.timeSinceStartup - started);
            if (elapsed > 12 && game.Round.Phase == BoxingPhase.Countdown)
            { Finish(false, $"countdown stalled frame={Time.frameCount} editorPaused={EditorApplication.isPaused} reason={game.PauseReason}"); return; }
            if (elapsed > 125) { Finish(false, $"timeout phase={game.Round.Phase} paused={game.Paused} reason={game.PauseReason} time={game.Round.TimeLeft} valid={game.input.Current.valid}"); return; }
            int bucket = (int)(elapsed / 15);
            if (bucket != progressBucket) { progressBucket = bucket; Debug.Log($"BOXING_SMOKE_PROGRESS t={elapsed:0} phase={game.Round.Phase} left={game.Round.TimeLeft:0.0} paused={game.Paused} reason={game.PauseReason}"); }
            if (game.Round.Phase == BoxingPhase.Results)
            {
                string summary = $"hits={game.Round.Hits} blocks={game.Round.Blocks} headHits={game.Round.Taken} dodges={game.Round.Dodges} soft={softImpacts} hard={hardImpacts} zones={zones} gain={minGain:0.00}..{maxGain:0.00} sends={game.feedback.Sends} errors={errors}";
                Finish(game.Round.Hits >= 3 && game.Round.Blocks >= 2 && game.Round.Taken >= 2 && game.Round.Dodges >= 1 &&
                    softImpacts > 0 && hardImpacts > 0 && zones == 7 && maxGain > minGain + 0.2f && game.feedback.Sends == 0 && errors == 0, summary);
            }
        }
        internal static void DriveInput()
        {
            if (!initialized || finished) return;
            float elapsed = (float)(EditorApplication.timeSinceStartup - started);
            int stage = (int)(elapsed / 8) % 4;
            var pose = Pose(elapsed, stage == 0);
            if (stage == 2)
            {
                var lean = Vector3.right * (0.48f * Mathf.Sin(elapsed * 4));
                pose.head += lean; pose.left += lean; pose.right += lean;
            }
            game.input.SetTestPose(pose);
            if (elapsed > 7 && elapsed < 9) { if (!game.menu.IsOpen) game.menu.Open(); }
            else if (elapsed >= 9 && elapsed < 10 && game.menu.IsOpen) game.menu.Close();
        }
        private static BoxerPose Pose(float elapsed, bool guard)
        {
            float punch = Mathf.Max(0, Mathf.Sin(elapsed * 3.1f)) * 0.85f;
            bool left = (int)(elapsed / 2) % 2 == 0;
            return new BoxerPose {
                valid = true, head = new Vector3(0, 1.65f, 0), headRotation = Quaternion.identity,
                left = new Vector3(guard ? -0.12f : -0.07f, guard ? 1.65f : 1.55f, guard ? 0.3f : 0.3f + (left ? punch : 0)),
                right = new Vector3(guard ? 0.12f : 0.4f, guard ? 1.65f : 1.15f, guard ? 0.3f : 0.3f + (!left ? punch : 0)),
                leftRotation = Quaternion.identity, rightRotation = Quaternion.identity, leftClosed = true, rightClosed = true,
                timestamp = Time.realtimeSinceStartupAsDouble
            };
        }
        private static void Finish(bool pass, string summary)
        {
            finished = true; SessionState.SetBool(Active, false); EditorApplication.update -= Tick;
            var xr = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            xr.InitManagerOnStart = SessionState.GetBool("Boxing.Smoke.RestoreXR", true);
            EditorUtility.SetDirty(xr); AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/boxing-smoke-result.txt", (pass ? "PASS " : "FAIL ") + summary);
            Debug.Log("BOXING_SMOKE " + (pass ? "PASS " : "FAIL ") + summary);
            EditorApplication.isPlaying = false;
            EditorApplication.delayCall += () => EditorApplication.Exit(pass ? 0 : 1);
        }
        public static void Capture()
        {
            BoxingProject.Configure();
            BoxingProject.Validate();
            var game = UnityEngine.Object.FindAnyObjectByType<BoxingGame>(); game.Initialize(); game.feedback.forceSilent = true;
            game.input.mode = BoxingInputMode.Desktop;
            var pose = Pose(0, false);
            pose.left = pose.head + new Vector3(-0.25f, -0.30f, 0.4f);
            pose.right = pose.head + new Vector3(0.25f, -0.30f, 0.4f);
            for (int i = 0; i < 40; i++) game.Simulate(1f / 90, pose);
            var camera = game.input.headCamera;
            camera.transform.SetPositionAndRotation(pose.head, Quaternion.identity);
            game.menu.panel.gameObject.SetActive(false);
            Directory.CreateDirectory("Logs"); Render(camera, "Logs/boxing-player-view.png");
            camera.transform.SetPositionAndRotation(new Vector3(-3, 2.5f, -3), Quaternion.LookRotation(new Vector3(3, -1, 4)));
            Render(camera, "Logs/boxing-arena.png");
            camera.transform.SetPositionAndRotation(pose.head, Quaternion.identity); game.menu.Open();
            game.presentation.Render(game, pose, false);
            Render(camera, "Logs/boxing-menu.png");
        }
        private static void Render(Camera camera, string path)
        {
            var rt = new RenderTexture(1600, 1000, 24); rt.Create();
            var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            RenderPipeline.SubmitRenderRequest(camera, request);
            RenderPipeline.SubmitRenderRequest(camera, request);
            var old = RenderTexture.active; RenderTexture.active = rt;
            var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG()); RenderTexture.active = old;
            UnityEngine.Object.DestroyImmediate(texture); rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
