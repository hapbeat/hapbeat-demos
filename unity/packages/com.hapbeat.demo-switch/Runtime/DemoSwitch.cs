using System;
using UnityEngine;

namespace Hapbeat.DemoSwitch
{
    public static class DemoSwitch
    {
        public static event Action<string> BeforeSwitch;
        public static event Action LaunchContextDetected;

        public static bool ReturnToHub() => SwitchTo(DemoSwitchSettings.HubDemoId);

        /// <summary>
        /// Starts an allowlisted application. This application finishes after it has gone to the background;
        /// when it is still in front after 5 s, it keeps running and logs an error.
        /// </summary>
        public static bool SwitchTo(string demoId) => SwitchTo(demoId, null);

        /// <summary><see cref="SwitchTo(string)"/> with the error of a start that did not come to the front.</summary>
        internal static bool SwitchTo(string demoId, Action<string> onFailed)
        {
            if (DemoSwitchRuntime.Instance == null)
            {
                Debug.LogError("[Demo Switch] No Resources/HapbeatDemoSwitchSettings asset was loaded.");
                return false;
            }

            return DemoSwitchRuntime.Instance.SwitchLocal(demoId, onFailed);
        }

        internal static void NotifyBeforeSwitch(string demoId)
        {
            var handlers = BeforeSwitch;
            if (handlers == null) return;
            foreach (Action<string> handler in handlers.GetInvocationList())
            {
                try { handler(demoId); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        internal static void NotifyLaunchContextDetected()
        {
            var handlers = LaunchContextDetected;
            if (handlers == null) return;
            foreach (Action handler in handlers.GetInvocationList())
            {
                try { handler(); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }
    }
}
