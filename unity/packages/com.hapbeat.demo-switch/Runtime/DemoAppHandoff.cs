using System;
using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    /// <summary>
    /// Hand-over to another application that was just started (session 次へ / デモを終了, pause Hub に戻る,
    /// Demo Switch SWITCH). Nothing stops when the start call returns. Only after this application has
    /// actually gone to the background (focus lost or paused) does it stop haptics, sound and the 7710
    /// listener, raise <see cref="DemoSwitch.BeforeSwitch"/> and finish its task, exactly once. Finishing
    /// earlier made Quest start its switch to the home environment, which pushed the starting application
    /// to the background. When this application is still in front after <see cref="TimeoutSeconds"/>, the
    /// start counts as failed: the caller shows the error and this application keeps running.
    /// </summary>
    internal static class DemoAppHandoff
    {
        public const float TimeoutSeconds = 5f;
        public const string NotInFrontError = "起動したアプリが前面に出ませんでした";
        private static string _nextDemoId;
        private static Action<string> _onFailed;
        private static Action<string> _onLeft;
        private static float _deadline;
        private static bool _finished;
        private static DemoAppHandoffWatcher _watcher;

        /// <summary>Another application was started; waiting for this one to go to the background.</summary>
        public static bool IsPending => _nextDemoId != null;

        /// <summary>This application has finished its task for a hand-over.</summary>
        public static bool IsFinished => _finished;

        /// <summary>
        /// Called right after another application was started successfully. <paramref name="onFailed"/>
        /// receives the error when this application is still in front after the timeout.
        /// </summary>
        public static void Begin(string nextDemoId, Action<string> onFailed, float now)
        {
            if (IsPending || _finished) throw new InvalidOperationException("A hand-over is already in progress.");
            _nextDemoId = nextDemoId ?? string.Empty;
            _onFailed = onFailed;
            _deadline = now + TimeoutSeconds;
            Debug.Log("[Demo Switch] Started '" + _nextDemoId + "'; waiting for this application to leave the foreground.");
            if (Application.isPlaying && _watcher == null)
            {
                var host = new GameObject("Hapbeat Demo Handoff");
                UnityEngine.Object.DontDestroyOnLoad(host);
                _watcher = host.AddComponent<DemoAppHandoffWatcher>();
            }
        }

        /// <summary>
        /// Runs <paramref name="onLeft"/> (with the started demo ID) once this application has gone to the background,
        /// while 7710 is still bound (PRESET_START sends READY there). False when no hand-over is pending.
        /// </summary>
        internal static bool NotifyWhenLeft(Action<string> onLeft)
        {
            if (!IsPending) return false;
            _onLeft = onLeft;
            return true;
        }

        /// <summary>Focus lost or paused: the started application is in front. Cleans up and finishes once.</summary>
        internal static void OnBackgrounded()
        {
            if (!IsPending) return;
            var next = _nextDemoId;
            var onLeft = _onLeft;
            _nextDemoId = null;
            _onFailed = null;
            _onLeft = null;
            _finished = true;
            try { onLeft?.Invoke(next); }
            catch (Exception exception) { Debug.LogException(exception); }
            if (DemoSwitchRuntime.Instance != null) DemoSwitchRuntime.Instance.StopForHandoff(next);
            else DemoSwitch.NotifyBeforeSwitch(next);
            DemoSession.CurrentHost?.SetHapticsEnabled(false);
            AudioListener.pause = true;
            Debug.Log("[Demo Switch] Left the foreground for '" + next + "'; finishing this task.");
            DemoSession.Platform.FinishTask();
        }

        /// <summary>Reports a failed start when this application is still in front at the deadline.</summary>
        internal static void Tick(float now)
        {
            if (!IsPending || now < _deadline) return;
            var onFailed = _onFailed;
            Debug.LogError("[Demo Switch] '" + _nextDemoId + "' did not come to the front within " + TimeoutSeconds + " s; this application keeps running.");
            _nextDemoId = null;
            _onFailed = null;
            _onLeft = null;
            onFailed?.Invoke(NotInFrontError);
        }

        internal static void ResetForTests()
        {
            _nextDemoId = null;
            _onFailed = null;
            _onLeft = null;
            _finished = false;
        }
    }

    /// <summary>Forwards focus/pause changes and frames to <see cref="DemoAppHandoff"/>.</summary>
    internal sealed class DemoAppHandoffWatcher : MonoBehaviour
    {
        private void Update() => DemoAppHandoff.Tick(Time.realtimeSinceStartup);

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) DemoAppHandoff.OnBackgrounded();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) DemoAppHandoff.OnBackgrounded();
        }
    }
}
